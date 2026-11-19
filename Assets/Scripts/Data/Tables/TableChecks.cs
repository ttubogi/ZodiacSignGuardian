using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 설계서 2절 검사 1~12 중 표 사이·행 사이 검사.
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
            Check11_Stages(set, issues);
            Check12_Gacha(set, issues);
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
                        Add(issues, TableIssueKind.Check1, t.Schema.Name, r.Line, key, "'" + v + "'가 " + TableIssue.At(t.Source, firstLine) + "과 겹침");
                    }
                    else
                    {
                        seen[v] = r.Line;
                    }
                }
            }
        }

        /// <summary>
        /// 이 열들 중 하나라도 값을 못 읽은 행이 있는지. 개수·빠짐·이어짐처럼 "전체를 다 알아야" 하는 검사는
        /// 이때 건너뛴다. 못 읽은 칸은 이미 형식 문제로 남았고, 그 칸 때문에 생기는 연쇄 문제를 내지 않으려는 것.
        /// </summary>
        static bool HasUnreadable(LoadedTable t, params string[] columns)
        {
            return t.Rows.Any(r => !r.AllValid(columns));
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
            // 키 칸을 못 읽은 행이 있는 표는 "그 값이 없다"고 단정할 수 없다 (그 행의 키일 수 있음, 이미 형식 문제로 남음)
            bool KeysKnown(string table) => !HasUnreadable(set[table], set[table].Schema.KeyColumn);

            void Ref(LoadedTable from, TableRow row, string column, string targetTable)
            {
                if (!row.IsValid(column) || row.IsBlank(column)) return; // 빈 선택 칸은 가리키는 것이 없다
                var id = (string)row[column];
                if (!Idx(targetTable).TryGetValue(id, out var target))
                {
                    if (!KeysKnown(targetTable)) return;
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

        /// <summary>검사 4 문구에 보여 줄 빠진 구간 수. 넘으면 "외 N구간"으로 줄인다.</summary>
        public const int MaxGapsShown = 10;

        // 4. 출석은 1일부터 마지막 날까지 빠짐없이 한 번씩 있다. (겹침은 검사 1)
        //    마지막 날은 reset_rules의 cycle 규칙(출석 주기, 하나뿐)의 cycle_days와 같다. (C1 검토, 2026-11-15)
        //    별 보상(reward_kind star) 행의 amount는 1이다. (설계서 v0.10, Codex C3 검토 질문 1 A안, 2026-11-18)
        static void Check4_Attendance(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Attendance];
            var name = t.Schema.Name;
            if (t.Rows.Count == 0)
            {
                Add(issues, TableIssueKind.Check4, name, 0, null, "출석 보상이 하나도 없음");
                return;
            }
            // 별 보상은 한 번에 하나만 준다. 칸 하나만 보는 검사라 다른 칸을 못 읽어도 이 행 안에서 끝난다.
            foreach (var r in t.Rows)
            {
                if (!r.IsValid("reward_kind") || !r.IsValid("amount") || r.Str("reward_kind") != "star") continue;
                if (r.Long("amount") != 1)
                {
                    Add(issues, TableIssueKind.Check4, name, r.Line, "amount", "별 보상의 amount는 1이어야 함 (지금 " + r.Long("amount") + ")");
                }
            }
            // 읽은 날만 정렬해 이웃한 날 사이의 빈 구간을 찾는다. 일의 양은 날 숫자의 크기가 아니라 행 수에 비례한다.
            // (Codex 검토 2026-11-15: 1부터 마지막 날까지 하나씩 세면 day 하나가 10억이면 10억 번 돌고 메모리가 바닥남)
            // day를 못 읽은 행이 있으면 빠진 날·마지막 날을 알 수 없다 (그 행이 빠진 날일 수 있음, 이미 형식 문제로 남음).
            // 아래 cycle 규칙 개수 검사는 day와 상관없으므로 계속한다.
            long? last = null;
            if (!HasUnreadable(t, "day"))
            {
                var days = t.Rows.Select(r => r.Long("day")).Distinct().OrderBy(d => d).ToList();
                last = days[days.Count - 1];
                var gaps = new List<(long from, long to)>();
                long prev = 0;
                foreach (var d in days)
                {
                    // day는 양수라 0 <= prev < d. 빼기로 비교해 long 최대값에서도 넘치지 않는다.
                    if (d - prev > 1) gaps.Add((prev + 1, d - 1));
                    prev = d;
                }
                if (gaps.Count > 0)
                {
                    var shown = gaps.Take(MaxGapsShown).Select(g => g.from == g.to ? g.from.ToString() : g.from + "~" + g.to);
                    var more = gaps.Count > MaxGapsShown ? " 외 " + (gaps.Count - MaxGapsShown) + "구간" : "";
                    Add(issues, TableIssueKind.Check4, name, 0, "day", "1~" + last + "일 중 빠진 날: " + string.Join(", ", shown) + more);
                }
            }

            var rules = set[Schemas.ResetRules].Rows;
            if (rules.Any(r => !r.IsValid("period"))) return; // period를 못 읽은 규칙이 있으면 개수를 알 수 없음 (이미 형식 문제)
            var cycles = rules.Where(r => r.Str("period") == "cycle").ToList();
            if (cycles.Count != 1)
            {
                Add(issues, TableIssueKind.Check4, name, 0, null,
                    "출석 주기를 정하는 reset_rules의 cycle 규칙이 정확히 하나여야 함 (지금 " + cycles.Count + "개)");
            }
            else if (last != null && cycles[0].IsValid("cycle_days") && !cycles[0].IsBlank("cycle_days") && cycles[0].Long("cycle_days") != last)
            {
                Add(issues, TableIssueKind.Check4, name, 0, "day",
                    "마지막 날 " + last + "일이 reset_rules '" + cycles[0].Str("rule_id") + "'(" + TableIssue.At(set[Schemas.ResetRules].Source, cycles[0].Line) + ")의 cycle_days " + cycles[0].Long("cycle_days") + "와 다름");
            }
        }

        // 5. 요일 던전을 켤 때(행이 하나라도 있을 때)는 7요일이 모두 있다. (겹침은 검사 1)
        static void Check5_WeekdayDungeons(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.WeekdayDungeons];
            if (t.Rows.Count == 0) return;
            if (HasUnreadable(t, "weekday")) return; // 못 읽은 요일이 빠진 요일일 수 있음 (이미 형식 문제로 남음)
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
                // daily 규칙이 없으면 하루 경계를 알 수 없어 05시 검사를 건너뛴다. daily 규칙 개수는 검사 10이 본다 (같은 문제를 두 번 내지 않게).
                return;
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
                            day + "에 '" + cur.row.Str("event_id") + "'가 '" + prev.row.Str("event_id") + "'(" + TableIssue.At(t.Source, prev.row.Line) + ")와 겹침");
                    }
                }
            }
        }

        // 8. m1에는 속성마다 3종씩 15종, 속성마다 희귀도가 하나씩. (속성·희귀도 이름은 칸을 읽을 때 검사)
        public const int M1StarsPerElement = 3;

        static void Check8_Stars(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Stars];
            // m1일 수 있는 별(build를 못 읽었거나 m1인 별)의 element를 못 읽었으면 속성별 개수를 셀 수 없다 (이미 형식 문제로 남음).
            // build가 m1이 아닌 것이 확실한 별의 칸은 m1 개수와 상관없으므로 건너뛰지 않는다 (Codex 검토 2026-11-16).
            if (t.Rows.Any(r => r.Build == null || (r.Build == Schemas.M1 && !r.IsValid("element")))) return;
            var m1 = t.Rows.Where(r => r.Build == Schemas.M1).ToList();
            var wrong = Schemas.Elements
                .Select(e => (element: e, count: m1.Count(r => r.Str("element") == e)))
                .Where(x => x.count != M1StarsPerElement)
                .ToList();
            if (wrong.Count > 0 || m1.Count != Schemas.Elements.Length * M1StarsPerElement)
            {
                Add(issues, TableIssueKind.Check8, t.Schema.Name, 0, "element",
                    "m1 별은 속성마다 " + M1StarsPerElement + "종이어야 함. 지금: " +
                    string.Join(", ", Schemas.Elements.Select(e => e + " " + m1.Count(r => r.Str("element") == e))) + " (합계 " + m1.Count + ")");
                return; // 개수가 틀리면 희귀도 짝은 같은 원인의 연쇄 문제가 된다
            }
            // m1에서 속성마다 희귀도(일반·희귀·전설)가 하나씩 (설계서 v0.12, 밸런스 기초 문서 6절). 희귀도를 못 읽은 m1 별이 있으면 셀 수 없다.
            if (m1.Any(r => !r.IsValid("rarity"))) return;
            foreach (var e in Schemas.Elements)
            {
                var have = m1.Where(r => r.Str("element") == e).Select(r => r.Str("rarity")).OrderBy(x => System.Array.IndexOf(Schemas.RarityIds, x)).ToList();
                if (!have.SequenceEqual(Schemas.RarityIds))
                {
                    Add(issues, TableIssueKind.Check8, t.Schema.Name, 0, "rarity",
                        "m1 " + e + " 별은 희귀도 " + string.Join("·", Schemas.RarityIds) + "가 하나씩이어야 함. 지금: " + string.Join("·", have));
                }
            }
        }

        // 9. 판마다 칸 1~5가 모두 이어져 있고, 같은 선의 중복·자기 연결이 없다.
        public const int SlotsPerBoard = 5;

        static void Check9_Boards(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Boards];
            var name = t.Schema.Name;
            var chapterOfConstellation = new Dictionary<string, long>();
            // chapter를 못 읽은 행은 어느 판의 선인지 모르므로, 그런 행이 있으면 판의 이어짐은 보지 않는다 (이미 형식 문제로 남음)
            var chaptersKnown = !HasUnreadable(t, "chapter");

            foreach (var chapter in t.Rows.Where(r => r.IsValid("chapter")).GroupBy(r => r.Long("chapter")).OrderBy(g => g.Key))
            {
                var rows = chapter.ToList();
                var first = rows[0];

                // 별자리 비교는 별자리를 읽은 행끼리, build 비교는 build를 읽은 행끼리 따로 한다
                // (한쪽 칸을 못 읽어도 다른 쪽 비교는 계속, Codex 검토 2026-11-16)
                var builds = rows.Where(r => r.Build != null).ToList();
                foreach (var r in builds.Where(r => r.Build != builds[0].Build))
                {
                    Add(issues, TableIssueKind.Check9, name, r.Line, "build", chapter.Key + "장 안에서 build가 다름");
                }
                var named = rows.Where(r => r.IsValid("constellation_id")).ToList();
                if (named.Count > 0)
                {
                    var head = named[0];
                    var constellation = head.Str("constellation_id");
                    foreach (var r in named.Where(r => r.Str("constellation_id") != constellation))
                    {
                        Add(issues, TableIssueKind.Check9, name, r.Line, "constellation_id",
                            chapter.Key + "장의 별자리가 '" + constellation + "'(" + TableIssue.At(t.Source, head.Line) + ")와 다름: '" + r.Str("constellation_id") + "'");
                    }
                    if (chapterOfConstellation.TryGetValue(constellation, out var other))
                    {
                        Add(issues, TableIssueKind.Check9, name, head.Line, "constellation_id", "'" + constellation + "'가 " + other + "장에도 있음");
                    }
                    else
                    {
                        chapterOfConstellation[constellation] = chapter.Key;
                    }
                }

                // 칸 번호를 못 읽은 선이 있으면 그 선이 빠진 칸을 이을 수 있으므로 이어짐은 보지 않는다
                var linksKnown = chaptersKnown && !rows.Any(r => !r.AllValid("slot_a", "slot_b"));

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
                            "같은 선 " + pair.Item1 + "-" + pair.Item2 + "이 " + TableIssue.At(t.Source, firstLine) + "에도 있음");
                        continue;
                    }
                    lines[pair] = r.Line;
                    used.Add(a);
                    used.Add(b);
                    parent[Find((int)a)] = Find((int)b);
                }

                var unused = Enumerable.Range(1, SlotsPerBoard).Where(s => !used.Contains(s)).ToArray();
                var groups = Enumerable.Range(1, SlotsPerBoard).Where(s => used.Contains(s)).Select(Find).Distinct().Count();
                if (linksKnown && (unused.Length > 0 || groups > 1))
                {
                    Add(issues, TableIssueKind.Check9, name, first.Line, null,
                        chapter.Key + "장 판이 하나로 이어지지 않음" +
                        (unused.Length > 0 ? " (선이 없는 칸: " + string.Join(", ", unused) + ")" : "") +
                        (groups > 1 ? " (따로 떨어진 묶음 " + groups + "개)" : ""));
                }
            }
        }

        // 10. 시계 설정: reset_rules의 utc_offset_min과 시계 설정 열은 모든 행에서 같은 값이고,
        //     daily·weekly 규칙은 각각 정확히 하나, anchor_valid_min은 sync_interval_min보다 크다. (열의 범위는 Schemas.Clock)
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
                        r.Long(col) + "이 첫 행(" + TableIssue.At(t.Source, first.Line) + ")의 " + first.Long(col) + "과 다름");
                }
            }

            // 하루·주 경계 규칙은 각각 정확히 하나 (Core가 하나씩 쓴다, 2026-11-17 결정).
            // period를 못 읽은 행이 있으면 개수를 단정하지 않는다 (이미 형식 문제)
            if (!HasUnreadable(t, "period"))
            {
                foreach (var period in new[] { "daily", "weekly" })
                {
                    var count = t.Rows.Count(r => r.Str("period") == period);
                    if (count != 1)
                    {
                        Add(issues, TableIssueKind.Check10, t.Schema.Name, 0, "period",
                            period + " 규칙이 정확히 하나여야 함 (지금 " + count + "개)");
                    }
                }
            }

            foreach (var r in t.Rows.Where(r => r.AllValid("sync_interval_min", "anchor_valid_min")))
            {
                if (r.Long("anchor_valid_min") <= r.Long("sync_interval_min"))
                {
                    Add(issues, TableIssueKind.Check10, t.Schema.Name, r.Line, "anchor_valid_min",
                        "anchor_valid_min(" + r.Long("anchor_valid_min") + ")은 sync_interval_min(" + r.Long("sync_interval_min") + ")보다 커야 함");
                }
            }
        }

        // 11. 스테이지 (설계서 v0.12 2절). 일의 양은 행 수에 비례한다(값 크기만큼 돌지 않음, AGENTS.md 배운 점 2026-11-15).
        //   - stage_id는 1부터 빠짐없이(겹침은 검사 1). m1 행이 하나 이상이고 v1 행 뒤에 m1 행이 없다(m1 스테이지가 1부터 이어짐).
        //   - stage_id 순서로 장은 1부터 빠짐없이 오르고(줄지 않고 한 번에 하나씩), 장 안의 stage_in_chapter는 1부터 하나씩 오른다.
        //   - 보스 칸 4개(boss_hp, boss_time_sec, boss_reward_currency, boss_reward_amount)는 장 마지막 행에만 모두 있고 다른 행은 비어 있다.
        //   - monster_hp·dust_per_kill·offline_dust_per_min은 다음 스테이지에서 줄지 않는다.
        //   - 첫 돌파·보스 보상 재화는 정수 재화다(수량이 정수 칸이므로).
        //   - m1 스테이지의 장마다 boards에 m1 판이 있다.
        public static readonly string[] BossColumns = { "boss_hp", "boss_time_sec", "boss_reward_currency", "boss_reward_amount" };
        static readonly string[] NonDecreasingColumns = { "monster_hp", "dust_per_kill", "offline_dust_per_min" };

        static void Check11_Stages(TableSet set, List<TableIssue> issues)
        {
            var t = set[Schemas.Stages];
            var name = t.Schema.Name;
            if (!t.Complete) return; // 표의 틀을 못 읽었으면 행을 믿을 수 없음 (이미 형식 문제)
            if (HasUnreadable(t, "stage_id", "chapter", "stage_in_chapter", "build")) return; // 순서·이어짐을 알 수 없음 (이미 형식 문제)
            var rows = t.Rows.OrderBy(r => r.Long("stage_id")).ThenBy(r => r.Line).ToList();
            if (rows.Count == 0 || rows.All(r => r.Build != Schemas.M1))
            {
                Add(issues, TableIssueKind.Check11, name, 0, null, "m1 스테이지가 하나도 없음");
                return;
            }

            // stage_id: 정렬한 i번째가 i+1 (겹침은 검사 1이 알리므로 여기서는 같은 번호를 빈 번호로 세지 않는다)
            long expect = 1;
            var orderOk = true;
            for (var i = 0; i < rows.Count; i++)
            {
                var id = rows[i].Long("stage_id");
                if (i > 0 && id == rows[i - 1].Long("stage_id")) { orderOk = false; continue; }
                if (id != expect)
                {
                    Add(issues, TableIssueKind.Check11, name, rows[i].Line, "stage_id", "stage_id " + expect + "이 빠짐 (다음 번호 " + id + ")");
                    orderOk = false;
                    break; // 첫 빈 곳만 알린다 (그 뒤는 모두 같은 원인)
                }
                expect = id + 1; // id < long 최대값: 행 수보다 클 수 없음
            }
            if (!orderOk) return; // 순서를 믿을 수 없으면 장·보스·크기 비교는 연쇄 문제가 된다

            for (var i = 1; i < rows.Count; i++)
            {
                if (rows[i - 1].Build != Schemas.M1 && rows[i].Build == Schemas.M1)
                {
                    Add(issues, TableIssueKind.Check11, name, rows[i].Line, "build", "v1 스테이지 뒤에 m1 스테이지가 있음 (m1 스테이지는 1부터 이어져야 함)");
                }
            }

            // 장과 장 안 번호
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                long ch = r.Long("chapter"), inCh = r.Long("stage_in_chapter");
                if (i == 0)
                {
                    if (ch != 1) Add(issues, TableIssueKind.Check11, name, r.Line, "chapter", "첫 스테이지의 장은 1이어야 함 (지금 " + ch + ")");
                    if (inCh != 1) Add(issues, TableIssueKind.Check11, name, r.Line, "stage_in_chapter", "장의 첫 스테이지는 1이어야 함 (지금 " + inCh + ")");
                    continue;
                }
                var p = rows[i - 1];
                long pch = p.Long("chapter"), pin = p.Long("stage_in_chapter");
                // 빼기로 비교한다: 양수끼리라 넘치지 않음
                if (ch == pch)
                {
                    if (inCh - pin != 1) Add(issues, TableIssueKind.Check11, name, r.Line, "stage_in_chapter", "장 안 번호가 " + pin + " 다음에 " + inCh + " (하나씩 올라야 함)");
                }
                else if (ch - pch == 1)
                {
                    if (inCh != 1) Add(issues, TableIssueKind.Check11, name, r.Line, "stage_in_chapter", ch + "장의 첫 스테이지는 1이어야 함 (지금 " + inCh + ")");
                }
                else
                {
                    Add(issues, TableIssueKind.Check11, name, r.Line, "chapter", "장이 " + pch + " 다음에 " + ch + " (줄지 않고 하나씩 올라야 함)");
                }
            }

            // 보스 칸: 장 마지막 행(다음 행의 장이 다르거나 마지막 행)에만 모두
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var last = i == rows.Count - 1 || rows[i + 1].Long("chapter") != r.Long("chapter");
                foreach (var col in BossColumns)
                {
                    if (!r.IsValid(col)) continue; // 못 읽은 칸은 이미 형식 문제
                    if (last && r.IsBlank(col)) Add(issues, TableIssueKind.Check11, name, r.Line, col, r.Long("chapter") + "장 마지막 스테이지에 보스 값이 없음");
                    if (!last && !r.IsBlank(col)) Add(issues, TableIssueKind.Check11, name, r.Line, col, "보스 값은 장 마지막 스테이지에만 씀");
                }
            }

            // 줄지 않음: 이웃한 두 행의 값을 모두 읽었을 때만 비교
            foreach (var col in NonDecreasingColumns)
            {
                for (var i = 1; i < rows.Count; i++)
                {
                    if (!rows[i - 1].IsValid(col) || !rows[i].IsValid(col)) continue;
                    if (GameNumberText.Compare(rows[i].Str(col), rows[i - 1].Str(col)) < 0)
                    {
                        Add(issues, TableIssueKind.Check11, name, rows[i].Line, col,
                            rows[i].Str(col) + "이 앞 스테이지(" + TableIssue.At(t.Source, rows[i - 1].Line) + ")의 " + rows[i - 1].Str(col) + "보다 작음");
                    }
                }
            }

            // 보상 재화는 정수 재화
            var currencies = set[Schemas.Currencies];
            var numberType = new Dictionary<string, string>();
            foreach (var c in currencies.Rows.Where(c => c.AllValid("currency_id", "number_type")))
            {
                if (!numberType.ContainsKey(c.Str("currency_id"))) numberType[c.Str("currency_id")] = c.Str("number_type");
            }
            foreach (var r in rows)
            {
                foreach (var col in new[] { "first_clear_currency", "boss_reward_currency" })
                {
                    if (!r.IsValid(col) || r.IsBlank(col)) continue;
                    if (numberType.TryGetValue(r.Str(col), out var type) && type != "integer")
                    {
                        Add(issues, TableIssueKind.Check11, name, r.Line, col, "'" + r.Str(col) + "'는 정수 재화가 아님 (수량 칸이 정수)");
                    }
                }
            }

            // m1 스테이지의 장마다 m1 판
            var boards = set[Schemas.Boards];
            if (HasUnreadable(boards, "chapter", "build")) return; // 판이 어느 장·빌드인지 모름 (이미 형식 문제)
            var m1Boards = new HashSet<long>(boards.Rows.Where(b => b.Build == Schemas.M1).Select(b => b.Long("chapter")));
            foreach (var ch in rows.Where(r => r.Build == Schemas.M1).GroupBy(r => r.Long("chapter")))
            {
                if (!m1Boards.Contains(ch.Key))
                {
                    Add(issues, TableIssueKind.Check11, name, ch.First().Line, "chapter", ch.Key + "장(m1)에 boards의 m1 판이 없음");
                }
            }
        }

        // 12. 뽑기 (설계서 v0.12 2절).
        //   - rarities의 draw_weight 합은 10000(확률 1만분의 1). 희귀도 셋이 모두 있다(별의 비용을 찾을 수 있게).
        //   - 확률이 0보다 큰 희귀도는 m1 별이 하나 이상 있다(뽑았는데 별이 없는 일이 없게).
        //   - star_rules의 draw_currency·shard_currency는 정수 재화이고 서로 다르다.
        public const long WeightTotal = 10000;

        static void Check12_Gacha(TableSet set, List<TableIssue> issues)
        {
            var rarities = set[Schemas.Rarities];
            var name = rarities.Schema.Name;
            if (rarities.Complete && !HasUnreadable(rarities, "rarity"))
            {
                var have = new HashSet<string>(rarities.Rows.Select(r => r.Str("rarity")));
                var missing = Schemas.RarityIds.Where(x => !have.Contains(x)).ToArray();
                if (missing.Length > 0) Add(issues, TableIssueKind.Check12, name, 0, "rarity", "빠진 희귀도: " + string.Join(", ", missing));

                if (!HasUnreadable(rarities, "draw_weight"))
                {
                    long sum = 0; // 칸마다 0~10000이고 행은 희귀도 수 이하(겹침은 검사 1)라 넘치지 않음
                    foreach (var r in rarities.Rows) sum += r.Long("draw_weight");
                    if (sum != WeightTotal) Add(issues, TableIssueKind.Check12, name, 0, "draw_weight", "확률 합이 " + sum + " (" + WeightTotal + "이어야 함)");

                    var stars = set[Schemas.Stars];
                    // m1일 수 있는 별의 희귀도를 못 읽었으면 '별이 없다'고 단정할 수 없다 (이미 형식 문제)
                    if (!stars.Rows.Any(r => r.Build == null || (r.Build == Schemas.M1 && !r.IsValid("rarity"))))
                    {
                        var m1Rarities = new HashSet<string>(stars.Rows.Where(r => r.Build == Schemas.M1).Select(r => r.Str("rarity")));
                        foreach (var r in rarities.Rows.Where(r => r.Long("draw_weight") > 0 && !m1Rarities.Contains(r.Str("rarity"))))
                        {
                            Add(issues, TableIssueKind.Check12, name, r.Line, "draw_weight", "확률이 있는 '" + r.Str("rarity") + "'에 m1 별이 없음");
                        }
                    }
                }
            }

            var rules = set[Schemas.StarRules];
            if (rules.Rows.Count != 1) return; // 행 수는 TableValidation이 알린다
            var rule = rules.Rows[0];
            var types = new Dictionary<string, string>();
            foreach (var c in set[Schemas.Currencies].Rows.Where(c => c.AllValid("currency_id", "number_type")))
            {
                if (!types.ContainsKey(c.Str("currency_id"))) types[c.Str("currency_id")] = c.Str("number_type");
            }
            foreach (var col in new[] { "draw_currency", "shard_currency" })
            {
                if (!rule.IsValid(col)) continue;
                if (types.TryGetValue(rule.Str(col), out var type) && type != "integer")
                {
                    Add(issues, TableIssueKind.Check12, rules.Schema.Name, rule.Line, col, "'" + rule.Str(col) + "'는 정수 재화가 아님");
                }
            }
            if (rule.AllValid("draw_currency", "shard_currency") && rule.Str("draw_currency") == rule.Str("shard_currency"))
            {
                Add(issues, TableIssueKind.Check12, rules.Schema.Name, rule.Line, "shard_currency", "뽑기 재화와 별조각 재화가 같음 (비용과 별조각을 따로 확인함)");
            }
        }
    }
}
