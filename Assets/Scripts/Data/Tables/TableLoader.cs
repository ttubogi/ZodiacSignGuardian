using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZodiacSignGuardian.Data.Csv;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// CSV 파일 12개를 읽어 TableSet으로 만든다. 형식 문제와 칸 단위 검사(3·8·9의 값 모양)는 여기서 걸린다.
    /// 표 사이·행 사이 검사는 TableChecks가 한다.
    /// </summary>
    public static class TableLoader
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>파일 이름(예: reset_rules.csv) → 바이트. 표 12개가 모두 있어야 한다.</summary>
        public static TableSet LoadFiles(IReadOnlyDictionary<string, byte[]> files, List<TableIssue> issues)
        {
            var texts = new Dictionary<string, string>();
            foreach (var pair in files)
            {
                var text = DecodeUtf8(pair.Value, TableName(pair.Key), issues);
                if (text != null) texts[pair.Key] = text;
            }
            // 해독에 실패한 파일은 '파일 없음'이 아니라 해독 오류 하나로만 남는다 (LoadTexts에서 거름)
            return LoadTexts(texts, issues);
        }

        /// <summary>파일 이름 → 글. 테스트에서 글을 바로 넣을 때 쓴다.</summary>
        public static TableSet LoadTexts(IReadOnlyDictionary<string, string> texts, List<TableIssue> issues)
        {
            var set = new TableSet();
            foreach (var name in texts.Keys.Where(k => Schemas.All.All(s => s.FileName != k)).OrderBy(k => k))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, TableName(name), 0, null, "알 수 없는 표 파일 (표 12개에 없음)"));
            }

            foreach (var schema in Schemas.All)
            {
                if (texts.TryGetValue(schema.FileName, out var text))
                {
                    set.Put(Parse(schema, text, issues));
                }
                else
                {
                    if (!issues.Any(i => i.Table == schema.Name && i.Message.StartsWith("UTF-8")))
                    {
                        issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "파일이 없음"));
                    }
                    set.Put(new LoadedTable(schema, new TableRow[0]));
                }
            }
            return set;
        }

        /// <summary>BOM이 있든 없든 UTF-8만 받는다. Excel의 기본 'CSV'(CP949)는 여기서 걸린다.</summary>
        public static string DecodeUtf8(byte[] bytes, string table, List<TableIssue> issues)
        {
            try
            {
                var text = StrictUtf8.GetString(bytes);
                if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
                return text;
            }
            catch (DecoderFallbackException)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, table, 0, null,
                    "UTF-8이 아님. Excel에서는 'CSV UTF-8(쉼표로 분리)'로 저장해야 함"));
                return null;
            }
        }

        public static LoadedTable Parse(TableSchema schema, string text, List<TableIssue> issues)
        {
            int issuesBefore = issues.Count;
            var rows = new List<TableRow>();
            var csv = CsvParser.Parse(text);
            if (csv.Error != null)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, csv.ErrorLine, null, csv.Error));
                return new LoadedTable(schema, rows);
            }

            if (csv.Records.Count == 0)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "머리줄이 없음 (빈 표도 머리줄은 있어야 함)"));
                return new LoadedTable(schema, rows);
            }

            var header = csv.Records[0];
            var expected = schema.Columns.Select(c => c.Name).ToArray();
            if (!header.Fields.SequenceEqual(expected))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, header.Line, null,
                    "머리줄이 다름. 기대: " + string.Join(",", expected) + " / 실제: " + string.Join(",", header.Fields)));
                return new LoadedTable(schema, rows);
            }

            foreach (var record in csv.Records.Skip(1))
            {
                if (record.Fields.Count != expected.Length)
                {
                    issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, record.Line, null,
                        "칸 수가 " + record.Fields.Count + "개 (머리줄은 " + expected.Length + "개)"));
                    continue;
                }

                var values = new object[expected.Length];
                var invalid = new bool[expected.Length];
                for (int i = 0; i < expected.Length; i++)
                {
                    var col = schema.Columns[i];
                    if (!CellParser.TryParse(col, record.Fields[i], out var v, out var error))
                    {
                        issues.Add(new TableIssue(col.FormatIssueKind, schema.Name, record.Line, col.Name, error));
                        invalid[i] = true;
                    }
                    values[i] = v;
                }
                rows.Add(new TableRow(schema, record.Line, values, invalid));
            }

            if (schema.SingleRow && rows.Count != 1 && issues.Count == issuesBefore)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "설정 표는 행이 정확히 1개여야 함 (지금 " + rows.Count + "개)"));
            }

            var table = new LoadedTable(schema, rows);
            RowRules(table, issues);
            return table;
        }

        /// <summary>한 행 안에서만 정해지는 규칙.</summary>
        static void RowRules(LoadedTable table, List<TableIssue> issues)
        {
            if (table.Schema == Schemas.ResetRules)
            {
                foreach (var r in table.Rows.Where(r => r.IsValid("period")))
                {
                    var period = r.Str("period");
                    Need(r, "reset_hour", period == "daily" || period == "weekly", period, issues);
                    Need(r, "weekday", period == "weekly", period, issues);
                    Need(r, "cycle_days", period == "cycle", period, issues);
                }
            }
            else if (table.Schema == Schemas.OfflineReward)
            {
                foreach (var r in table.Rows.Where(r => r.AllValid("min_minutes", "max_minutes") && r.Long("min_minutes") > r.Long("max_minutes")))
                {
                    issues.Add(new TableIssue(TableIssueKind.Format, table.Schema.Name, r.Line, "min_minutes",
                        "min_minutes(" + r.Long("min_minutes") + ")가 max_minutes(" + r.Long("max_minutes") + ")보다 큼"));
                }
            }
        }

        static void Need(TableRow r, string column, bool required, string period, List<TableIssue> issues)
        {
            if (!r.IsValid(column)) return;
            if (required && r.IsBlank(column))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, "reset_rules", r.Line, column, "period=" + period + "이면 값이 필요함"));
            }
            else if (!required && !r.IsBlank(column))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, "reset_rules", r.Line, column, "period=" + period + "이면 비워야 함"));
            }
        }

        static string TableName(string fileName)
        {
            return fileName.EndsWith(".csv") ? fileName.Substring(0, fileName.Length - 4) : fileName;
        }
    }
}
