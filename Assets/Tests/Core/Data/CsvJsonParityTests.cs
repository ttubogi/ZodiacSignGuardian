using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Json;
using ZodiacSignGuardian.Data.Tables;
using static ZodiacSignGuardian.Core.Tests.Data.TestTables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    /// <summary>
    /// CSV 경로와 JSON 경로가 같은 위반에 같은 문제를 내는지 (Codex 검토 2026-11-15).
    /// 한쪽 경로에만 검사를 더하면 이 테스트가 실패한다.
    /// 칸 값 모양 오류(검사 3 등)는 JSON으로 옮겨 쓸 수 없어 여기서 빼고, JsonTests에서 따로 본다.
    /// </summary>
    [TestFixture]
    public class CsvJsonParityTests
    {
        static readonly Dictionary<string, Func<Dictionary<string, string>, Dictionary<string, string>>> Cases =
            new Dictionary<string, Func<Dictionary<string, string>, Dictionary<string, string>>>
        {
            ["문제 없음"] = t => t,
            ["형식: 설정 표 2행"] = t => t.AppendRow("offline_reward", "600,1,2,1"),
            ["형식: 설정 표 0행"] = t => t.HeaderOnly("offline_reward"),
            ["형식: min > max"] = t => t.Replace("offline_reward", "720,1,2,1", "720,721,2,1"),
            ["형식: weekly에 weekday 없음"] = t => t.Replace("reset_rules", "weekly,weekly,5,mon,", "weekly,weekly,5,,"),
            ["형식: daily에 reset_hour 없음"] = t => t.Replace("reset_rules", "daily,daily,5,", "daily,daily,,"),
            ["형식: daily에 cycle_days"] = t => t.Replace("reset_rules", "daily,daily,5,,,", "daily,daily,5,,7,"),
            ["검사1: 임무 id 겹침"] = t => t.AppendRow("daily_missions", "kill_monsters,daily,upgrade_stat,5,gold,1,m1"),
            ["검사1: 출석일 겹침"] = t => t.AppendRow("attendance", "2,currency,gold,1"),
            ["검사2: 없는 재화"] = t => t.Replace("weekly_challenge", "10,dungeon_key,3", "10,nothing,3"),
            ["검사2: 출석 종류 어긋남"] = t => t.Replace("attendance", "3,star,wood_1,1", "3,currency,wood_1,1"),
            ["검사2: 일일 임무가 weekly"] = t => t.Replace("daily_missions", "kill_monsters,daily,", "kill_monsters,weekly,"),
            ["검사4: 빠진 날"] = t => t.Replace("attendance", "2,currency,dust,1000\n", ""),
            ["검사4: cycle_days 어긋남"] = t => t.Replace("reset_rules", "attendance_cycle,cycle,,,3,", "attendance_cycle,cycle,,,4,"),
            ["검사4: cycle 규칙 없음"] = t => t.Replace("reset_rules", "attendance_cycle,cycle,,,3,540,5,10,5\n", ""),
            ["검사5: 요일 빠짐"] = t => t.Replace("weekday_dungeons", "sun,dungeon_sun,dungeon_key,2\n", ""),
            ["검사6: 시작=끝"] = t => t.Replace("timed_events", "lunch,mon|wed,12:00,13:00", "lunch,mon|wed,12:00,12:00"),
            ["검사6: 겹침"] = t => t.AppendRow("timed_events", "wed_bonus,fri|wed,12:30,12:45,gold"),
            ["검사6: 경계 넘음"] = t => t.AppendRow("timed_events", "night,sun,04:30,05:30,gold"),
            ["검사6: 경계가 reset_hour를 따름"] = t => t.Replace("reset_rules", "daily,daily,5,", "daily,daily,6,"),
            ["검사7: m1이 v1 재화"] = t => t.Replace("daily_missions", "kill_monsters,daily,kill_monster,100,gold,20,m1", "kill_monsters,daily,kill_monster,100,dungeon_key,20,m1"),
            ["검사7: 출석이 v1 별"] = t => t.Replace("attendance", "3,star,wood_1,1", "3,star,wood_4,1"),
            ["검사8: m1 별 14종"] = t => t.Replace("stars", "water_3,water,water 3,m1", "water_3,water,water 3,v1"),
            ["검사8: 속성 쏠림"] = t => t.Replace("stars", "water_3,water,", "water_3,fire,"),
            ["검사9: 칸 6"] = t => t.Replace("boards", "1,cassiopeia,4,5,m1", "1,cassiopeia,4,6,m1"),
            ["검사9: 자기 연결"] = t => t.AppendRow("boards", "1,cassiopeia,3,3,m1"),
            ["검사9: 거꾸로 중복"] = t => t.AppendRow("boards", "1,cassiopeia,2,1,m1"),
            ["검사9: 끊김"] = t => t.Replace("boards", "1,cassiopeia,2,3,m1", "1,cassiopeia,3,5,m1"),
            ["검사9: 별자리 다름"] = t => t.Replace("boards", "1,cassiopeia,4,5,m1", "1,lyra,4,5,m1"),
            ["검사9: 두 장에 같은 별자리"] = t => t.Replace("boards", "2,cygnus,", "2,cassiopeia,"),
            ["검사10: 시차 다름"] = t => t.Replace("reset_rules", "weekly,weekly,5,mon,,540,", "weekly,weekly,5,mon,,600,"),
            ["여러 위반 한꺼번에"] = t => t
                .AppendRow("daily_missions", "kill_monsters,daily,kill_monster,1,gold,1,m1")
                .Replace("weekly_challenge", "10,dungeon_key,3", "10,nothing,3")
                .Replace("attendance", "2,currency,dust,1000\n", "")
                .Replace("weekday_dungeons", "sun,dungeon_sun,dungeon_key,2\n", "")
                .AppendRow("timed_events", "night,sun,04:30,05:30,gold")
                .Replace("attendance", "3,star,wood_1,1", "3,star,wood_4,1")
                .Replace("stars", "water_3,water,water 3,m1", "water_3,water,water 3,v1")
                .AppendRow("boards", "1,cassiopeia,3,3,m1")
                .Replace("reset_rules", "weekly,weekly,5,mon,,540,", "weekly,weekly,5,mon,,600,")
                .AppendRow("offline_reward", "600,1,2,1"),
        };

        public static IEnumerable<string> CaseNames => Cases.Keys;

        static readonly Regex CsvLineRef = new Regex(@"(\d+)줄");
        static readonly Regex JsonRowRef = new Regex(@"데이터 (\d+)행");

        /// <summary>위치를 '데이터 행 번호'로 맞춘 비교용 글. 테스트 표는 빈 줄이 없어 CSV 줄 = 데이터 행 + 1.</summary>
        static string Normalize(TableIssue i)
        {
            int row = i.Line == 0 ? 0 : (i.Source == TableSource.Csv ? i.Line - 1 : i.Line);
            var msg = i.Source == TableSource.Csv
                ? CsvLineRef.Replace(i.Message, m => "#" + (int.Parse(m.Groups[1].Value) - 1))
                : JsonRowRef.Replace(i.Message, m => "#" + m.Groups[1].Value);
            return TableIssue.KindLabel(i.Kind) + " | " + i.Table + " | " + row + " | " + i.Column + " | " + msg;
        }

        [TestCaseSource(nameof(CaseNames))]
        public void CSV와_JSON이_같은_문제를_낸다(string caseName)
        {
            var texts = Cases[caseName](Good());

            // CSV 경로
            var csvIssues = TableBuilder.BuildFromTexts(texts).Issues;

            // 같은 표를 JSON으로 옮긴다 (읽기 단계에 문제가 없는 경우만 비교할 수 있다)
            var parseIssues = new List<TableIssue>();
            var parsed = TableLoader.LoadTexts(texts, parseIssues);
            Assert.That(parseIssues, Is.Empty, "이 사례는 칸 값 오류가 없어야 함: " + Describe(parseIssues));
            foreach (var t in parsed.Tables)
            {
                for (int i = 0; i < t.Rows.Count; i++)
                {
                    Assert.That(t.Rows[i].Line, Is.EqualTo(i + 2), t.Schema.Name + ": 빈 줄 없는 CSV여야 위치 비교가 맞음");
                }
            }
            var json = parsed.Tables.ToDictionary(t => t.Schema.JsonFileName, t => TableJson.Write(t));

            // JSON 경로
            var jsonResult = TableBuilder.LoadJson(json);

            Assert.That(csvIssues.All(i => i.Source == TableSource.Csv), Is.True, "CSV 문제는 CSV 위치로");
            Assert.That(jsonResult.Issues.All(i => i.Source == TableSource.Json), Is.True, "JSON 문제는 JSON 위치로");

            var a = csvIssues.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var b = jsonResult.Issues.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.That(b, Is.EqualTo(a), "CSV:\n" + string.Join("\n", a) + "\nJSON:\n" + string.Join("\n", b));

            Assert.That(jsonResult.Ok, Is.EqualTo(csvIssues.Count == 0));
            Assert.That(jsonResult.Tables == null, Is.EqualTo(csvIssues.Count > 0));
        }

        [Test]
        public void 비교_사례는_검사_1_2_4_5_6_7_8_9_10과_형식을_모두_덮는다()
        {
            // 비교 사례가 줄어 한 검사가 빠지면 알 수 있게
            var kinds = new HashSet<TableIssueKind>();
            foreach (var name in CaseNames) kinds.UnionWith(TableBuilder.BuildFromTexts(Cases[name](Good())).Issues.Select(i => i.Kind));
            var need = new[]
            {
                TableIssueKind.Format, TableIssueKind.Check1, TableIssueKind.Check2, TableIssueKind.Check4, TableIssueKind.Check5,
                TableIssueKind.Check6, TableIssueKind.Check7, TableIssueKind.Check8, TableIssueKind.Check9, TableIssueKind.Check10,
            };
            Assert.That(need.Where(k => !kinds.Contains(k)).ToArray(), Is.Empty);
        }
    }
}
