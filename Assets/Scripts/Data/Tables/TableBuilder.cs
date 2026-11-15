using System.Collections.Generic;
using System.Linq;
using ZodiacSignGuardian.Data.Json;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// CSV 12개 → 검사 → JSON 12개. 문제가 하나라도 있으면 JSON을 하나도 내지 않는다 (설계서 2절 "하나라도 걸리면 빌드를 멈춘다").
    /// 파일 읽기·쓰기는 하지 않는다 (도구 TableBuild가 맡는다).
    /// </summary>
    public static class TableBuilder
    {
        public sealed class Result
        {
            public List<TableIssue> Issues { get; } = new List<TableIssue>();
            /// <summary>JSON 파일 이름(예: reset_rules.json) → 글. 문제가 있으면 비어 있다.</summary>
            public Dictionary<string, string> Outputs { get; } = new Dictionary<string, string>();
            public TableSet Tables { get; internal set; }
            public bool Ok => Issues.Count == 0;
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
            result.Tables = set;
            result.Issues.AddRange(TableChecks.Run(set));
            if (!result.Ok) return result;

            foreach (var table in set.Tables)
            {
                result.Outputs[table.Schema.JsonFileName] = TableJson.Write(table);
            }
            return result;
        }

        /// <summary>JSON 12개를 읽어 같은 검사를 다시 돌린다. (Services가 읽는 쪽과 같은 경로)</summary>
        public static TableSet LoadJson(IReadOnlyDictionary<string, string> jsonTexts, List<TableIssue> issues)
        {
            int issuesBefore = issues.Count;
            var set = new TableSet();
            foreach (var schema in Schemas.All)
            {
                LoadedTable table = null;
                if (jsonTexts.TryGetValue(schema.JsonFileName, out var text))
                {
                    table = TableJson.Read(schema, text, issues);
                }
                else
                {
                    issues.Add(new TableIssue(TableIssueKind.Format, schema.Name, 0, null, "JSON 파일이 없음"));
                }
                set.Put(table ?? new LoadedTable(schema, new TableRow[0]));
            }
            if (issues.Count == issuesBefore) issues.AddRange(TableChecks.Run(set));
            return set;
        }
    }
}
