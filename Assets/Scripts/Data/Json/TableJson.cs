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
        /// JSON 글을 표로 읽는다. 모양·열·값 종류가 스키마와 다르면 issues에 남기고 null.
        /// 값은 CSV와 같은 규칙(CellParser)으로 다시 확인한다.
        /// </summary>
        public static LoadedTable Read(TableSchema schema, string json, List<TableIssue> issues)
        {
            void Fail(int line, string message) => issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, line, null, "JSON: " + message));

            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (MiniJson.ParseException e)
            {
                Fail(0, e.Message);
                return null;
            }

            if (!(root is MiniJson.Obj obj)) { Fail(0, "맨 바깥이 객체가 아님"); return null; }
            var keys = obj.Members.Select(m => m.Key).ToArray();
            if (!keys.SequenceEqual(new[] { "table", "columns", "rows" })) { Fail(0, "키는 table, columns, rows 순서여야 함"); return null; }

            obj.TryGet("table", out var tableName);
            if (!(tableName is string tn) || tn != schema.Name) { Fail(0, "table이 '" + schema.Name + "'가 아님"); return null; }

            obj.TryGet("columns", out var columnsValue);
            var expected = schema.Columns.Select(c => c.Name).ToArray();
            if (!(columnsValue is List<object> cols) || !cols.Cast<object>().Select(x => x as string).SequenceEqual(expected))
            {
                Fail(0, "columns가 스키마와 다름");
                return null;
            }

            obj.TryGet("rows", out var rowsValue);
            if (!(rowsValue is List<object> rowList)) { Fail(0, "rows가 배열이 아님"); return null; }

            var rows = new List<TableRow>();
            bool ok = true;
            for (int i = 0; i < rowList.Count; i++)
            {
                int line = i + 2; // CSV처럼 머리줄을 1로 친 행 번호
                if (!(rowList[i] is MiniJson.Obj r) || !r.Members.Select(m => m.Key).SequenceEqual(expected))
                {
                    Fail(line, "행의 키가 columns와 다름");
                    ok = false;
                    continue;
                }
                var values = new object[expected.Length];
                for (int c = 0; c < expected.Length; c++)
                {
                    var col = schema.Columns[c];
                    var v = r.Members[c].Value;
                    if (!TryToCell(col, v, out var raw))
                    {
                        issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, line, col.Name, "JSON: 값 종류가 열과 맞지 않음"));
                        ok = false;
                        continue;
                    }
                    if (!CellParser.TryParse(col, raw, out var parsed, out var error))
                    {
                        issues.Add(new TableIssue(col.FormatIssueKind, schema.Name, line, col.Name, "JSON: " + error));
                        ok = false;
                        continue;
                    }
                    values[c] = parsed;
                }
                rows.Add(new TableRow(schema, line, values));
            }
            return ok ? new LoadedTable(schema, rows) : null;
        }

        /// <summary>JSON 값을 CSV 칸 글자로 되돌린다. 종류가 틀리면 false.</summary>
        static bool TryToCell(ColumnSpec col, object v, out string raw)
        {
            raw = null;
            if (v == null) { raw = ""; return col.Optional; }
            switch (col.Kind)
            {
                case ColumnKind.PositiveInt:
                case ColumnKind.NonNegativeInt:
                case ColumnKind.IntRange:
                    if (v is long l) { raw = l.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }
                    return false;
                case ColumnKind.WeekdayList:
                    if (v is List<object> list && list.Count > 0 && list.All(x => x is string))
                    {
                        raw = string.Join("|", list.Cast<string>());
                        return true;
                    }
                    return false;
                default:
                    if (v is string s && s.Length > 0) { raw = s; return true; }
                    return false;
            }
        }
    }
}
