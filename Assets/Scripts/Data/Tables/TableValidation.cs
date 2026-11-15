using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 읽은 표 묶음에 대한 검사의 단 하나의 입구. CSV에서 읽었든 JSON에서 읽었든 같은 검사를 거친다.
    /// 1) 표 안 규칙: 행 수(설정 표는 1행), 한 행 안의 조건(period별 칸, min ≤ max)
    /// 2) 검사 1~10 중 표 사이·행 사이 검사 (TableChecks)
    /// 칸 값 모양(검사 3 등)은 읽을 때 걸린다 (TableLoader.ParseRow, CSV·JSON 공통).
    /// </summary>
    public static class TableValidation
    {
        public static List<TableIssue> Validate(TableSet set)
        {
            var issues = new List<TableIssue>();
            foreach (var table in set.Tables)
            {
                RowCount(table, issues);
                RowRules(table, issues);
            }
            issues.AddRange(TableChecks.Run(set));

            // 문제 위치는 그 표를 읽은 곳(CSV/JSON)으로 표시한다
            return issues.Select(i => set.Has(i.Table) ? i.WithSource(set[i.Table].Source) : i).ToList();
        }

        static void RowCount(LoadedTable table, List<TableIssue> issues)
        {
            if (table.Schema.SingleRow && table.Complete && table.Rows.Count != 1)
            {
                issues.Add(new TableIssue(TableIssueKind.Format, table.Schema.Name, 0, null,
                    "설정 표는 행이 정확히 1개여야 함 (지금 " + table.Rows.Count + "개)"));
            }
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
    }
}
