using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZodiacSignGuardian.Data.Csv;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// CSV 파일 15개를 읽어 TableSet으로 만든다. 여기서는 읽기만 한다:
    /// 파일·인코딩·CSV 문법·머리줄·칸 수·칸 값 모양(검사 3과 검사 8·9의 값 모양 포함).
    /// 행 수·한 행 안의 규칙·표 사이 검사는 JSON과 같은 경로인 TableValidation이 한다.
    /// </summary>
    public static class TableLoader
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>파일 이름(예: reset_rules.csv) → 바이트. 표 15개가 모두 있어야 한다.</summary>
        public static TableSet LoadFiles(IReadOnlyDictionary<string, byte[]> files, List<TableIssue> issues)
        {
            var texts = new Dictionary<string, string>();
            var undecodable = new HashSet<string>();
            foreach (var pair in files)
            {
                var text = DecodeUtf8(pair.Value, TableName(pair.Key), issues);
                if (text != null) texts[pair.Key] = text;
                else undecodable.Add(pair.Key);
            }
            return LoadTexts(texts, issues, undecodable);
        }

        /// <summary>파일 이름 → 글. 테스트에서 글을 바로 넣을 때 쓴다.</summary>
        public static TableSet LoadTexts(IReadOnlyDictionary<string, string> texts, List<TableIssue> issues)
        {
            return LoadTexts(texts, issues, new HashSet<string>());
        }

        static TableSet LoadTexts(IReadOnlyDictionary<string, string> texts, List<TableIssue> issues, HashSet<string> undecodable)
        {
            var set = new TableSet();
            foreach (var name in texts.Keys.Where(k => Schemas.All.All(s => s.FileName != k)).OrderBy(k => k))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, TableName(name), 0, null, "알 수 없는 표 파일 (표 15개에 없음)"));
            }

            foreach (var schema in Schemas.All)
            {
                if (texts.TryGetValue(schema.FileName, out var text))
                {
                    set.Put(Parse(schema, text, issues));
                }
                else
                {
                    // 해독에 실패한 파일은 '파일 없음'을 또 알리지 않는다
                    if (!undecodable.Contains(schema.FileName))
                    {
                        issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "파일이 없음"));
                    }
                    set.Put(LoadedTable.Broken(schema, TableSource.Csv));
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

        /// <summary>CSV 글 하나를 표로 읽는다. 검사는 하지 않는다 (TableValidation).</summary>
        public static LoadedTable Parse(TableSchema schema, string text, List<TableIssue> issues)
        {
            var rows = new List<TableRow>();
            var csv = CsvParser.Parse(text);
            if (csv.Error != null)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, csv.ErrorLine, null, csv.Error));
                return LoadedTable.Broken(schema, TableSource.Csv);
            }

            if (csv.Records.Count == 0)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "머리줄이 없음 (빈 표도 머리줄은 있어야 함)"));
                return LoadedTable.Broken(schema, TableSource.Csv);
            }

            var header = csv.Records[0];
            var expected = schema.Columns.Select(c => c.Name).ToArray();
            if (!header.Fields.SequenceEqual(expected))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, header.Line, null,
                    "머리줄이 다름. 기대: " + string.Join(",", expected) + " / 실제: " + string.Join(",", header.Fields)));
                return LoadedTable.Broken(schema, TableSource.Csv);
            }

            foreach (var record in csv.Records.Skip(1))
            {
                if (record.Fields.Count != expected.Length)
                {
                    issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, record.Line, null,
                        "칸 수가 " + record.Fields.Count + "개 (머리줄은 " + expected.Length + "개)"));
                    continue;
                }
                rows.Add(ParseRow(schema, record.Line, record.Fields, issues, TableSource.Csv));
            }

            return new LoadedTable(schema, rows, TableSource.Csv);
        }

        /// <summary>
        /// 칸 글자들을 값으로 바꾼 행. 값을 읽지 못한 칸은 문제로 남기고 표시만 한다 (행은 남긴다).
        /// rawCells의 null은 '이미 문제로 남긴 칸'(JSON에서 종류가 틀린 값)이다. CSV와 JSON이 같이 쓴다.
        /// </summary>
        internal static TableRow ParseRow(TableSchema schema, int line, IReadOnlyList<string> rawCells, List<TableIssue> issues, TableSource source)
        {
            var values = new object[schema.Columns.Count];
            var invalid = new bool[schema.Columns.Count];
            for (int i = 0; i < schema.Columns.Count; i++)
            {
                var col = schema.Columns[i];
                if (rawCells[i] == null)
                {
                    invalid[i] = true;
                    continue;
                }
                if (!CellParser.TryParse(col, rawCells[i], out var v, out var error))
                {
                    issues.Add(new TableIssue(col.FormatIssueKind, schema.Name, line, col.Name, error, source));
                    invalid[i] = true;
                }
                values[i] = v;
            }
            return new TableRow(schema, line, values, invalid);
        }

        static string TableName(string fileName)
        {
            return fileName.EndsWith(".csv") ? fileName.Substring(0, fileName.Length - 4) : fileName;
        }
    }
}
