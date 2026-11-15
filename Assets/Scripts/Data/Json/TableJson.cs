using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Data.Json
{
    /// <summary>
    /// 표 하나 ↔ JSON 글. 모양:
    /// <code>
    /// {
    ///   "table": "daily_missions",
    ///   "columns": ["mission_id", ...],
    ///   "rows": [
    ///     {"mission_id": "kill_monsters", ...},
    ///     ...
    ///   ]
    /// }
    /// </code>
    /// 한 행이 한 줄이라 git 차이가 행 단위로 보인다. 줄바꿈은 LF, 끝에 줄바꿈 하나. 같은 표는 항상 같은 글.
    /// </summary>
    public static class TableJson
    {
        public static string Write(LoadedTable table)
        {
            var sb = new StringBuilder();
            var schema = table.Schema;
            sb.Append("{\n  \"table\": ");
            MiniJson.WriteString(sb, schema.Name);
            sb.Append(",\n  \"columns\": ");
            MiniJson.WriteValue(sb, schema.Columns.Select(c => c.Name).ToList());
            sb.Append(",\n  \"rows\": [");
            for (int i = 0; i < table.Rows.Count; i++)
            {
                sb.Append(i == 0 ? "\n    " : ",\n    ");
                var obj = new MiniJson.Obj();
                for (int c = 0; c < schema.Columns.Count; c++)
                {
                    obj.Add(schema.Columns[c].Name, table.Rows[i][c]);
                }
                MiniJson.WriteValue(sb, obj);
            }
            sb.Append(table.Rows.Count == 0 ? "]\n}\n" : "\n  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>
        /// JSON 글을 표로 읽는다. 읽기만 하고 검사는 하지 않는다 (TableValidation, CSV와 같은 경로).
        /// - 바깥 모양: 객체, 키는 table·columns·rows 세 개(순서는 따지지 않음), table 이름과 columns 목록이 스키마와 같음.
        ///   어긋나면 표 전체를 믿을 수 없어 빈 '깨진 표'(Complete=false)를 돌려준다.
        /// - 행: 키는 열 이름과 같은 묶음(순서는 따지지 않음). 빠지거나 남는 키가 있는 행은 문제로 남기고 건너뛴다(CSV의 칸 수 오류와 같음).
        /// - 값: CSV 칸과 같은 규칙(CellParser)으로 읽는다. 값을 못 읽은 칸은 표시만 하고 행은 남긴다.
        /// 위치(Line)는 rows 배열의 데이터 행 번호(1부터)다.
        /// </summary>
        public static LoadedTable Read(TableSchema schema, string json, List<TableIssue> issues)
        {
            void Fail(int line, string message) => issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, line, null, message, TableSource.Json));
            LoadedTable Broken() => LoadedTable.Broken(schema, TableSource.Json);

            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (MiniJson.ParseException e)
            {
                Fail(0, "JSON 문법 오류: " + e.Message);
                return Broken();
            }

            if (!(root is MiniJson.Obj obj)) { Fail(0, "맨 바깥이 객체가 아님"); return Broken(); }
            var topKeys = new[] { "table", "columns", "rows" };
            var keyProblem = KeySetProblem(obj, topKeys);
            if (keyProblem != null) { Fail(0, "맨 바깥 키가 table, columns, rows가 아님 (" + keyProblem + ")"); return Broken(); }

            obj.TryGet("table", out var tableName);
            if (!(tableName is string tn) || tn != schema.Name) { Fail(0, "table이 '" + schema.Name + "'가 아님"); return Broken(); }

            obj.TryGet("columns", out var columnsValue);
            var expected = schema.Columns.Select(c => c.Name).ToArray();
            if (!(columnsValue is List<object> cols) || !cols.Select(x => x as string).SequenceEqual(expected))
            {
                Fail(0, "columns가 스키마와 다름. 기대: " + string.Join(",", expected));
                return Broken();
            }

            obj.TryGet("rows", out var rowsValue);
            if (!(rowsValue is List<object> rowList)) { Fail(0, "rows가 배열이 아님"); return Broken(); }

            var rows = new List<TableRow>();
            for (int i = 0; i < rowList.Count; i++)
            {
                int line = i + 1;
                if (!(rowList[i] is MiniJson.Obj r))
                {
                    Fail(line, "행이 객체가 아님");
                    continue;
                }
                var rowProblem = KeySetProblem(r, expected);
                if (rowProblem != null)
                {
                    Fail(line, "행의 키가 columns와 다름 (" + rowProblem + ")");
                    continue;
                }

                var raw = new string[expected.Length];
                for (int c = 0; c < expected.Length; c++)
                {
                    var col = schema.Columns[c];
                    r.TryGet(col.Name, out var v);
                    if (!TryToCell(col, v, out raw[c]))
                    {
                        issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, line, col.Name,
                            "값 종류가 열과 맞지 않음 (기대: " + ExpectedType(col) + ")", TableSource.Json));
                        raw[c] = null;
                    }
                }
                rows.Add(TableLoader.ParseRow(schema, line, raw, issues, TableSource.Json));
            }
            return new LoadedTable(schema, rows, TableSource.Json);
        }

        /// <summary>객체의 키가 기대한 묶음과 같으면 null, 다르면 무엇이 빠지고 남는지.</summary>
        static string KeySetProblem(MiniJson.Obj o, IReadOnlyCollection<string> expected)
        {
            var keys = o.Members.Select(m => m.Key).ToList();
            var missing = expected.Where(k => !keys.Contains(k)).ToList();
            var extra = keys.Where(k => !expected.Contains(k)).ToList();
            if (missing.Count == 0 && extra.Count == 0) return null;
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add("빠진 키: " + string.Join(", ", missing));
            if (extra.Count > 0) parts.Add("남는 키: " + string.Join(", ", extra));
            return string.Join(" / ", parts);
        }

        static string ExpectedType(ColumnSpec col)
        {
            switch (col.Kind)
            {
                case ColumnKind.PositiveInt:
                case ColumnKind.NonNegativeInt:
                case ColumnKind.IntRange:
                    return "정수";
                case ColumnKind.WeekdayList:
                    return "요일 문자열 배열";
                default:
                    return "문자열";
            }
        }

        /// <summary>
        /// JSON 값을 CSV 칸 글자로 되돌린다. null은 빈 칸("")으로 보고 필수 여부는 CellParser가 판단한다(CSV 빈 칸과 같은 결과).
        /// 종류가 틀리면 false.
        /// </summary>
        static bool TryToCell(ColumnSpec col, object v, out string raw)
        {
            raw = null;
            if (v == null) { raw = ""; return true; }
            switch (col.Kind)
            {
                case ColumnKind.PositiveInt:
                case ColumnKind.NonNegativeInt:
                case ColumnKind.IntRange:
                    if (v is long l) { raw = l.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }
                    return false;
                case ColumnKind.WeekdayList:
                    // 요소 안의 '|'는 여러 요일을 한 칸에 숨기는 것이라 받지 않는다
                    if (v is List<object> list && list.All(x => x is string e && e.IndexOf('|') < 0))
                    {
                        raw = string.Join("|", list.Cast<string>());
                        return true;
                    }
                    return false;
                default:
                    if (v is string s) { raw = s; return true; }
                    return false;
            }
        }
    }
}
