using System.Collections.Generic;
using System.Linq;
using ZodiacSignGuardian.Data.Json;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 표를 읽고 검사하는 입구 두 개.
    /// - Build: CSV 15개 → 검사 → JSON 15개 (도구 TableBuild가 씀)
    /// - LoadJson: JSON 15개 → 같은 검사 → 표 묶음 (게임 시작 때 Services가 씀)
    /// 두 입구 모두 읽은 뒤 TableValidation.Validate 하나만 거친다.
    /// 문제가 하나라도 있으면 출력(JSON·표 묶음)을 하나도 주지 않는다 (설계서 2절 "하나라도 걸리면 멈춘다").
    /// 파일 읽기·쓰기는 하지 않는다.
    /// </summary>
    public static class TableBuilder
    {
        public sealed class Result
        {
            public List<TableIssue> Issues { get; } = new List<TableIssue>();
            /// <summary>JSON 파일 이름(예: reset_rules.json) → 글. 문제가 있으면 비어 있다.</summary>
            public Dictionary<string, string> Outputs { get; } = new Dictionary<string, string>();
            /// <summary>검사를 통과한 표 묶음. 문제가 있으면 null.</summary>
            public TableSet Tables { get; internal set; }
            public bool Ok => Issues.Count == 0;
        }

        /// <summary>JSON을 읽은 결과. Ok일 때만 Tables가 있다.</summary>
        public sealed class JsonLoadResult
        {
            public IReadOnlyList<TableIssue> Issues { get; }
            /// <summary>검사를 통과한 표 묶음. 문제가 하나라도 있으면 null이라 잘못 쓸 수 없다.</summary>
            public TableSet Tables { get; }
            public bool Ok => Issues.Count == 0;

            internal JsonLoadResult(IReadOnlyList<TableIssue> issues, TableSet tables)
            {
                Issues = issues;
                Tables = issues.Count == 0 ? tables : null;
            }
        }

        public static Result Build(IReadOnlyDictionary<string, byte[]> csvFiles)
        {
            var result = new Result();
            var set = TableLoader.LoadFiles(csvFiles, result.Issues);
            return Finish(set, result);
        }

        public static Result BuildFromTexts(IReadOnlyDictionary<string, string> csvTexts)
        {
            var result = new Result();
            var set = TableLoader.LoadTexts(csvTexts, result.Issues);
            return Finish(set, result);
        }

        static Result Finish(TableSet set, Result result)
        {
            result.Issues.AddRange(TableValidation.Validate(set));
            if (!result.Ok) return result;

            result.Tables = set;
            foreach (var table in set.Tables)
            {
                result.Outputs[table.Schema.JsonFileName] = TableJson.Write(table);
            }
            return result;
        }

        /// <summary>
        /// JSON 15개(파일 이름 예: reset_rules.json → 글)를 읽고 CSV와 같은 검사를 돌린다.
        /// 값이 null인 항목은 파일이 없는 것으로 본다.
        /// </summary>
        public static JsonLoadResult LoadJson(IReadOnlyDictionary<string, string> jsonTexts)
        {
            var issues = new List<TableIssue>();
            foreach (var name in jsonTexts.Keys.Where(k => Schemas.All.All(s => s.JsonFileName != k)).OrderBy(k => k))
            {
                issues.Add(new TableIssue(TableIssueKind.Format, TableName(name), 0, null, "알 수 없는 표 JSON (표 15개에 없음)", TableSource.Json));
            }

            var set = new TableSet();
            foreach (var schema in Schemas.All)
            {
                if (jsonTexts.TryGetValue(schema.JsonFileName, out var text) && text != null)
                {
                    set.Put(TableJson.Read(schema, text, issues));
                }
                else
                {
                    issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "JSON 파일이 없음", TableSource.Json));
                    set.Put(LoadedTable.Broken(schema, TableSource.Json));
                }
            }
            issues.AddRange(TableValidation.Validate(set));
            return new JsonLoadResult(issues, set);
        }

        static string TableName(string fileName)
        {
            return fileName.EndsWith(".json") ? fileName.Substring(0, fileName.Length - 5) : fileName;
        }
    }
}
