using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 설계서 v0.7 2절 검사 1~10 중 표 사이·행 사이 검사.
    /// 검사 3(양의 정수)과 검사 8·9의 값 모양은 칸을 읽을 때(TableLoader) 걸린다.
    /// </summary>
    public static class TableChecks
    {
        public static List<TableIssue> Run(TableSet set)
        {
            var issues = new List<TableIssue>();
            Check1_UniqueKeys(set, issues);
            Check2And7_References(set, issues);
            Check4_Attendance(set, issues);
            Check5_WeekdayDungeons(set, issues);
            Check6_TimedEvents(set, issues);
            Check8_Stars(set, issues);
            Check9_Boards(set, issues);
            Check10_ClockSettings(set, issues);
            return issues;
        }

        static void Add(List<TableIssue> issues, TableIssueKind kind, string table, int line, string column, string message)
        {
            issues.Add(new TableIssue(kind, table, line, column, message));
        }

        // 1. id가 같은 표 안에서 겹치지 않는다.
        static void Check1_UniqueKeys(TableSet set, List<TableIssue> issues)
        {
            foreach (var t in set.Tables.Where(t => t.Schema.KeyColumn != null))
            {
                var key = t.Schema.KeyColumn;
                var seen = new Dictionary<object, int>();
                foreach (var r in t.Rows.Where(r => r.IsValid(key)))
                {
                    var v = r[key];
                    if (seen.TryGetValue(v, out var firstLine))
                    {
                        Add(issues, TableIssueKind.Check1, t.Schema.Name, r.Line, key, "'" + v + "'가 " + firstLine + "줄과 겹침");
                    }
                    else
                    {
                        seen[v] = r.Line;
                    }
                }
            }
        }

        static Dictionary<string, TableRow> Index(LoadedTable t)
        {
            var d = new Dictionary<string, TableRow>();
            foreach (var r in t.Rows.Where(r => r.IsValid(t.Schema.KeyColumn)))
            {
                var k = (string)r[t.Schema.KeyColumn];
                if (!d.ContainsKey(k)) d[k] = r;
            }
            return d;
        }

        // 2. 다른 표를 가리키는 값은 실제로 있다.
        // 7. m1 행은 m1에 없는 것을 가리키지 않는다.
        static void Check2And7_References(TableSet set, List<TableIssue> issues)
        {
            var indexes = new Dictionary<string, Dictionary<string, TableRow>>();
            Dictionary<string, TableRow> Idx(string table)
            {
                if (!indexes.TryGetValue(table, out var d)) indexes[table] = d = Index(set[table]);
                return d;
            }

            void Ref(LoadedTable from, TableRow row, string column, string targetTable)
            {
                if (!row.IsValid(column)) return;
                var id = (string)row[column];
                if (!Idx(targetTable).TryGetValue(id, out var target))
                {
                    Add(issues, TableIssueKind.Check2, from.Schema.Name, row.Line, column, "'" + id + "'가 " + targetTable + "에 없음");
                    return;
                }
                // 일일 임무는 daily 규칙만 가리킨다 (C1 검토, 2026-11-15)
                if (from.Schema == Schemas.DailyMissions && column == "reset_rule"
                    && target.IsValid("period") && target.Str("period") != "daily")
                {
                    Add(issues, TableIssueKind.Check2, from.Schema.Name, row.Line, column,
                        "일일 임무는 daily 규칙만 가리킬 수 있음: '" + id + "'는 period=" + target.Str("period"));
                }
                // build를 읽지 못한 쪽이 있으면 검사 7은 건너뛴다 (이미 형식 문제로 남음)
                if (row.Build == Schemas.M1 && target.Build != null && target.Build != Schemas.M1)
                {
                    Add(issues, TableIssueKind.Check7, from.Schema.Name, row.Line, column,
                        "m1 행이 " + targetTable + "의 " + target.Build + " 항목 '" + id + "'를 가리킴");
                }
            }

            foreach (var t in set.Tables)
            {
                foreach (var col in t.Schema.Columns.Where(c => c.RefTable != null))
                {
                    foreach (var r in t.Rows) Ref(t, r, col.Name, col.RefTable);
                }
            }

            var attendance = set[Schemas.Attendance];
            foreach (var r in attendance.Rows.Where(r => r.IsValid("reward_kind")))
            {
                Ref(attendance, r, "reward_id", r.Str("reward_kind") == "star" ? Schemas.Stars.Name : Schemas.Currencies.Name);
            }
        }

        // 4. 출석은 1일부터 마지막 날까지 빠짐없이 한 번씩 있다. (겹침은 검사 1)
        //    마지막 날은 reset_rules의 cycle 규칙(출석 주기, 하나뿐)의 cycle_days와 같다. (C1 검토, 2026-11-15)
        static void Check4_Attendance(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Attendance];
            var name = t.Schema.Name;
            if (t.Rows.Count == 0)
            {
                Add(issues, TableIssueKind.Check4, name, 0, null, "출석 보상이 하나도 없음");
                return;
            }
            var days = new HashSet<long>(t.Rows.Where(r => r.IsValid("day")).Select(r => r.Long("day")));
            if (days.Count == 0) return; // 날을 하나도 읽지 못함 (이미 문제로 남음)
            var last = days.Max();
            var missing = new List<long>();
            for (long d = 1; d <= last; d++)
            {
                if (!days.Contains(d)) missing.Add(d);
            }
            if (missing.Count > 0)
            {
                Add(issues, TableIssueKind.Check4, name, 0, "day", "1~" + last + "일 중 빠진 날: " + string.Join(", ", missing));
            }

            var rules = set[Schemas.ResetRules].Rows;
            if (rules.Any(r => !r.IsValid("period"))) return; // period를 못 읽은 규칙이 있으면 개수를 알 수 없음 (이미 형식 문제)
            var cycles = rules.Where(r => r.Str("period") == "cycle").ToList();
            if (cycles.Count != 1)
            {
                Add(issues, TableIssueKind.Check4, name, 0, null,
                    "출석 주기를 정하는 reset_rules의 cycle 규칙이 정확히 하나여야 함 (지금 " + cycles.Count + "개)");
            }
            else if (cycles[0].IsValid("cycle_days") && !cycles[0].IsBlank("cycle_days") && cycles[0].Long("cycle_days") != last)
            {
                Add(issues, TableIssueKind.Check4, name, 0, "day",
                    "마지막 날 " + last + "일이 reset_rules '" + cycles[0].Str("rule_id") + "'(" + cycles[0].Line + "줄)의 cycle_days " + cycles[0].Long("cycle_days") + "와 다름");
            }
        }

        // 5. 요일 던전을 켤 때(행이 하나라도 있을 때)는 7요일이 모두 있다. (겹침은 검사 1)
        static void Check5_WeekdayDungeons(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.WeekdayDungeons];
            if (t.Rows.Count == 0) return;
            var have = new HashSet<string>(t.Rows.Where(r => r.IsValid("weekday")).Select(r => r.Str("weekday")));
            var missing = Schemas.Weekdays.Where(w => !have.Contains(w)).ToArray();
            if (missing.Length > 0)
            {
                Add(issues, TableIssueKind.Check5, t.Schema.Name, 0, "weekday", "빠진 요일: " + string.Join(", ", missing));
            }
        }

        // 6. 시간대 이벤트: 시작 < 끝, 같은 요일에 겹치지 않음, 하루 경계(daily 규칙의 reset_hour, 지금 05:00)를 넘지 않음.
        static void Check6_TimedEvents(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.TimedEvents];
            var name = t.Schema.Name;
            if (t.Rows.Count == 0) return;

            var daily = set[Schemas.ResetRules].Rows.FirstOrDefault(r => r.AllValid("period", "reset_hour") && r.Str("period") == "daily" && !r.IsBlank("reset_hour"));
            int? boundary = daily == null ? (int?)null : (int)daily.Long("reset_hour") * 60;
            if (boundary == null)
            {
                // period·reset_hour를 못 읽은 규칙이 있으면 daily 규칙이 없다고 단정하지 않는다 (이미 형식 문제)
                if (set[Schemas.ResetRules].Rows.Any(r => !r.AllValid("period", "reset_hour"))) return;
                Add(issues, TableIssueKind.Check6, name, 0, null, "reset_rules에 daily 규칙이 없어 하루 경계를 알 수 없음");
            }

            var spans = new List<(TableRow row, int start, int end)>();
            foreach (var r in t.Rows.Where(r => r.AllValid("weekdays", "start_time", "end_time")))
            {
                CellParser.TryMinutes(r.Str("start_time"), out var s);
                CellParser.TryMinutes(r.Str("end_time"), out var e);
                if (s >= e)
                {
                    Add(issues, TableIssueKind.Check6, name, r.Line, "end_time", "끝(" + r.Str("end_time") + ")이 시작(" + r.Str("start_time") + ")보다 뒤가 아님");
                    continue;
                }
                if (boundary is int b && s < b && e > b)
                {
                    Add(issues, TableIssueKind.Check6, name, r.Line, "end_time",
                        r.Str("start_time") + "~" + r.Str("end_time") + "이 하루 경계 " + (b / 60).ToString("00") + ":" + (b % 60).ToString("00") + "을 넘음");
                    continue;
                }
                spans.Add((r, s, e));
            }

            foreach (var day in Schemas.Weekdays)
            {
                var onDay = spans.Where(x => x.row.Strings("weekdays").Contains(day)).OrderBy(x => x.start).ThenBy(x => x.row.Line).ToList();
                for (int i = 1; i < onDay.Count; i++)
                {
                    var prev = onDay[i - 1];
                    var cur = onDay[i];
                    if (cur.start < prev.end)
                    {
                        Add(issues, TableIssueKind.Check6, name, cur.row.Line, "start_time",
                            day + "에 '" + cur.row.Str("event_id") + "'가 '" + prev.row.Str("event_id") + "'(" + prev.row.Line + "줄)와 겹침");
                    }
                }
            }
        }

        // 8. m1에는 속성마다 3종씩 15종. (속성 이름은 칸을 읽을 때 검사)
        public const int M1StarsPerElement = 3;

        static void Check8_Stars(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Stars];
            var m1 = t.Rows.Where(r => r.Build == Schemas.M1 && r.IsValid("element")).ToList();
            var wrong = Schemas.Elements
                .Select(e => (element: e, count: m1.Count(r => r.Str("element") == e)))
                .Where(x => x.count != M1StarsPerElement)
                .ToList();
            if (wrong.Count > 0 || m1.Count != Schemas.Elements.Length * M1StarsPerElement)
            {
                Add(issues, TableIssueKind.Check8, t.Schema.Name, 0, "element",
                    "m1 별은 속성마다 " + M1StarsPerElement + "종이어야 함. 지금: " +
                    string.Join(", ", Schemas.Elements.Select(e => e + " " + m1.Count(r => r.Str("element") == e))) + " (합계 " + m1.Count + ")");
            }
        }

        // 9. 판마다 칸 1~5가 모두 이어져 있고, 같은 선의 중복·자기 연결이 없다.
        public const int SlotsPerBoard = 5;

        static void Check9_Boards(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Boards];
            var name = t.Schema.Name;
            var chapterOfConstellation = new Dictionary<string, long>();

            foreach (var chapter in t.Rows.Where(r => r.AllValid("chapter", "constellation_id", "build")).GroupBy(r => r.Long("chapter")).OrderBy(g => g.Key))
            {
                var rows = chapter.ToList();
                var first = rows[0];
                var constellation = first.Str("constellation_id");

                foreach (var r in rows.Where(r => r.Str("constellation_id") != constellation))
                {
                    Add(issues, TableIssueKind.Check9, name, r.Line, "constellation_id",
                        chapter.Key + "장의 별자리가 '" + constellation + "'(" + first.Line + "줄)와 다름: '" + r.Str("constellation_id") + "'");
                }
                foreach (var r in rows.Where(r => r.Build != first.Build))
                {
                    Add(issues, TableIssueKind.Check9, name, r.Line, "build", chapter.Key + "장 안에서 build가 다름");
                }
                if (chapterOfConstellation.TryGetValue(constellation, out var other))
                {
                    Add(issues, TableIssueKind.Check9, name, first.Line, "constellation_id", "'" + constellation + "'가 " + other + "장에도 있음");
                }
                else
                {
                    chapterOfConstellation[constellation] = chapter.Key;
                }

                var parent = Enumerable.Range(0, SlotsPerBoard + 1).ToArray();
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

                var lines = new Dictionary<(long, long), int>();
                var used = new HashSet<long>();
                foreach (var r in rows.Where(r => r.AllValid("slot_a", "slot_b")))
                {
                    long a = r.Long("slot_a"), b = r.Long("slot_b");
                    if (a > SlotsPerBoard || b > SlotsPerBoard)
                    {
                        Add(issues, TableIssueKind.Check9, name, r.Line, a > SlotsPerBoard ? "slot_a" : "slot_b",
                            "칸 번호는 1~" + SlotsPerBoard + ": " + a + "-" + b);
                        continue;
                    }
                    if (a == b)
                    {
                        Add(issues, TableIssueKind.Check9, name, r.Line, "slot_b", "자기 자신과 연결: " + a + "-" + b);
                        continue;
                    }
                    var pair = (System.Math.Min(a, b), System.Math.Max(a, b));
                    if (lines.TryGetValue(pair, out var firstLine))
                    {
                        Add(issues, TableIssueKind.Check9, name, r.Line, "slot_b",
                            "같은 선 " + pair.Item1 + "-" + pair.Item2 + "이 " + firstLine + "줄에도 있음");
                        continue;
                    }
                    lines[pair] = r.Line;
                    used.Add(a);
                    used.Add(b);
                    parent[Find((int)a)] = Find((int)b);
                }

                var unused = Enumerable.Range(1, SlotsPerBoard).Where(s => !used.Contains(s)).ToArray();
                var groups = Enumerable.Range(1, SlotsPerBoard).Where(s => used.Contains(s)).Select(Find).Distinct().Count();
                if (unused.Length > 0 || groups > 1)
                {
                    Add(issues, TableIssueKind.Check9, name, first.Line, null,
                        chapter.Key + "장 판이 하나로 이어지지 않음" +
                        (unused.Length > 0 ? " (선이 없는 칸: " + string.Join(", ", unused) + ")" : "") +
                        (groups > 1 ? " (따로 떨어진 묶음 " + groups + "개)" : ""));
                }
            }
        }

        // 10. reset_rules의 utc_offset_min과 시계 설정 열은 모든 행에서 같은 값이다.
        public static readonly string[] SharedClockColumns = { "utc_offset_min", "sync_interval_min", "anchor_valid_min", "max_rtt_sec" };

        static void Check10_ClockSettings(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.ResetRules];
            if (t.Rows.Count == 0)
            {
                Add(issues, TableIssueKind.Check10, t.Schema.Name, 0, null, "규칙이 하나도 없음");
                return;
            }
            foreach (var col in SharedClockColumns)
            {
                var valid = t.Rows.Where(r => r.IsValid(col)).ToList();
                if (valid.Count == 0) continue;
                var first = valid[0];
                foreach (var r in valid.Skip(1).Where(r => r.Long(col) != first.Long(col)))
                {
                    Add(issues, TableIssueKind.Check10, t.Schema.Name, r.Line, col,
                        r.Long(col) + "이 첫 행(" + first.Line + "줄)의 " + first.Long(col) + "과 다름");
                }
            }
        }
    }
}
