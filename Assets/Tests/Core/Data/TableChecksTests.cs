using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Tables;
using static ZodiacSignGuardian.Core.Tests.Data.TestTables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    /// <summary>
    /// 검사 1~10 위반 주입. 통과하는 표에서 한 곳만 망가뜨리고, 그 검사만 걸리는지 본다.
    /// 경계(맞닿음, 정확히 경계 시각, 정확히 개수)는 걸리지 않아야 한다.
    /// </summary>
    [TestFixture]
    public class TableChecksTests
    {
        // ---- 1. id 겹침 ----

        [Test]
        public void 검사1_임무_id가_겹치면_잡는다()
        {
            var issues = Run(Good().AppendRow("daily_missions", "kill_monsters,daily,upgrade_stat,5,gold,1,m1"));
            AssertOnly(issues, TableIssueKind.Check1);
            Assert.That(issues[0].Message, Does.Contain("2줄"));
        }

        [TestCase("attendance", "2,currency,gold,1")]
        [TestCase("weekday_dungeons", "mon,dungeon_extra,dungeon_key,1")]
        [TestCase("stars", "fire_9,fire,불,v1\nfire_9,fire,불 둘,v1")]
        [TestCase("currencies", "gold,금화 둘,integer,m1")]
        [TestCase("weekly_challenge", "10,dungeon_key,5")]
        public void 검사1_표마다_키가_겹치면_잡는다(string table, string row)
        {
            AssertOnly(Run(Good().AppendRow(table, row)), TableIssueKind.Check1);
        }

        // ---- 2. 참조 ----

        [TestCase("daily_missions", "kill_monsters,daily,kill_monster,100,gold,", "kill_monsters,daily,kill_monster,100,gem,")]
        [TestCase("daily_missions", "kill_monsters,daily,", "kill_monsters,hourly,")]
        [TestCase("attendance", "3,star,wood_1,1", "3,star,wood_99,1")]
        [TestCase("weekday_dungeons", "mon,dungeon_mon,dungeon_key", "mon,dungeon_mon,dungeon_coin")]
        [TestCase("timed_events", "lunch,mon|wed,12:00,13:00,gold", "lunch,mon|wed,12:00,13:00,golden")]
        [TestCase("weekly_challenge", "10,dungeon_key,3", "10,nothing,3")]
        public void 검사2_없는_것을_가리키면_잡는다(string table, string from, string to)
        {
            AssertOnly(Run(Good().Replace(table, from, to)), TableIssueKind.Check2);
        }

        [TestCase("weekly")]
        [TestCase("attendance_cycle")]
        public void 검사2_일일_임무가_daily가_아닌_규칙을_가리키면_잡는다(string rule)
        {
            var issues = Run(Good().Replace("daily_missions", "kill_monsters,daily,", "kill_monsters," + rule + ","));
            AssertOnly(issues, TableIssueKind.Check2);
            Assert.That(issues.Single().Message, Does.Contain("daily 규칙만"));
        }

        [Test]
        public void 규칙의_period를_못_읽으면_형식_문제만_나오고_연쇄_문제는_없다()
        {
            // daily 행의 period가 틀리면: 검사 2(daily 아님)·검사 4(cycle 개수)·검사 6(daily 없음)이 따라 나오면 안 된다
            AssertOnly(Run(Good().Replace("reset_rules", "daily,daily,5,", "daily,dayly,5,")), TableIssueKind.Format);
            AssertOnly(Run(Good().Replace("reset_rules", "attendance_cycle,cycle,", "attendance_cycle,cycel,")), TableIssueKind.Format);
        }

        [Test]
        public void 검사2_출석은_reward_kind에_맞는_표를_본다()
        {
            // 별 id를 재화로, 재화 id를 별로 주면 둘 다 없는 것
            AssertOnly(Run(Good().Replace("attendance", "3,star,wood_1,1", "3,currency,wood_1,1")), TableIssueKind.Check2);
            AssertOnly(Run(Good().Replace("attendance", "1,currency,gold,50", "1,star,gold,50")), TableIssueKind.Check2);
        }

        // ---- 3. 양의 정수 ----

        [TestCase("daily_missions", "kill_monster,100,", "kill_monster,-5,", "target_count")]
        [TestCase("weekday_dungeons", "dungeon_tue,dungeon_key,2", "dungeon_tue,dungeon_key,1.5", "free_entries")]
        [TestCase("ad_buffs", "hunt_double,2,30", "hunt_double,2,0", "duration_min")]
        [TestCase("offline_reward", "720,1,2,1", "720,1,2,abc", "max_frame_sec")]
        [TestCase("reset_rules", "daily,daily,5,,,540,5,10,5", "daily,daily,5,,,540,0,10,5", "sync_interval_min")]
        public void 검사3_양의_정수가_아니면_잡는다(string table, string from, string to, string column)
        {
            var issues = Run(Good().Replace(table, from, to));
            AssertOnly(issues, TableIssueKind.Check3);
            Assert.That(issues[0].Column, Is.EqualTo(column));
        }

        [Test]
        public void 검사3_대상이_아닌_0_허용_열은_걸리지_않는다()
        {
            // max_level 0 = 제한 없음(테스트 표의 attack이 0), reset_hour 0 = 자정
            Assert.That(Good()["stats.csv"], Does.Contain("attack,공격력,0,m1"));
            AssertNone(Run(Good()));
            Assert.That(Run(Good().Replace("reset_rules", "weekly,weekly,5,", "weekly,weekly,0,")).Any(i => i.Kind == TableIssueKind.Check3), Is.False);
        }

        // ---- 4. 출석 ----

        [Test]
        public void 검사4_가운데_날이_빠지면_잡는다()
        {
            var issues = Run(Good().Replace("attendance", "2,currency,dust,1000\n", ""));
            AssertOnly(issues, TableIssueKind.Check4);
            Assert.That(issues[0].Message, Does.Contain("빠진 날: 2"));
        }

        [Test]
        public void 검사4_이어진_빠진_날은_구간으로_보인다()
        {
            // 1, 3, 6일만 있음 → 빠진 날 2, 4~5
            var issues = Run(Good()
                .Replace("attendance", "2,currency,dust,1000\n3,star,wood_1,1\n", "3,star,wood_1,1\n6,currency,gold,1\n")
                .Replace("reset_rules", "attendance_cycle,cycle,,,3,", "attendance_cycle,cycle,,,6,"));
            AssertOnly(issues, TableIssueKind.Check4);
            Assert.That(issues[0].Message, Does.EndWith("1~6일 중 빠진 날: 2, 4~5"));
        }

        [Test]
        public void 검사4_빠진_구간이_많으면_앞의_10구간만_보인다()
        {
            // 1, 3, 5, …, 25일 → 빠진 날 2, 4, …, 24 (12구간)
            var rows = string.Concat(Enumerable.Range(0, 13).Select(i => (1 + 2 * i) + ",currency,gold,1\n"));
            var texts = Good();
            texts["attendance.csv"] = "day,reward_kind,reward_id,amount\n" + rows;
            var issues = Run(texts.Replace("reset_rules", "attendance_cycle,cycle,,,3,", "attendance_cycle,cycle,,,25,"));
            AssertOnly(issues, TableIssueKind.Check4);
            Assert.That(TableChecks.MaxGapsShown, Is.EqualTo(10));
            Assert.That(issues[0].Message, Does.EndWith("1~25일 중 빠진 날: 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 외 2구간"));
        }

        // ---- 못 읽은 칸 때문에 생기던 연쇄 문제 (2026-11-15) ----
        // 그 칸 자체의 문제만 남고, 개수·빠짐·이어짐 검사는 그 칸 없이 셈하지 않는다.

        [TestCase("attendance", "2,currency,dust,1000", "x,currency,dust,1000", "day")]
        [TestCase("stars", "water_3,water,", "water_3,,", "element")]
        [TestCase("weekday_dungeons", "sun,dungeon_sun,", "sunday,dungeon_sun,", "weekday")]
        [TestCase("boards", "1,cassiopeia,4,5,m1", "1,cassiopeia,4,x,m1", "slot_b")]
        [TestCase("boards", "1,cassiopeia,4,5,m1", "x,cassiopeia,4,5,m1", "chapter")]
        [TestCase("boards", "1,cassiopeia,4,5,m1", "1,,4,5,m1", "constellation_id")]
        public void 못_읽은_칸은_그_칸의_문제만_낸다(string table, string from, string to, string column)
        {
            var texts = Good().Replace(table, from, to);
            var readIssues = new List<TableIssue>();
            TableLoader.LoadTexts(texts, readIssues);
            Assert.That(readIssues.Any(i => i.Table == table && i.Column == column), Is.True, "읽기 단계에서 그 칸이 걸려야 함: " + Describe(readIssues));

            // 검사까지 돈 결과 = 읽기 단계의 문제뿐 (같은 열에 붙는 연쇄 문제도 잡히게 개수와 내용을 모두 비교)
            var issues = Run(texts);
            Assert.That(issues.Select(i => i.ToString()), Is.EquivalentTo(readIssues.Select(i => i.ToString())), Describe(issues));
        }

        // ---- 건너뛰는 범위는 좁게 (Codex 검토 2026-11-16) ----

        [Test]
        public void 대상_표의_키_하나가_깨져도_찾은_다른_참조의_build_위반은_잡는다()
        {
            var issues = Run(Good()
                .Replace("currencies", "dust,\"별가루, 반짝\",game_number,m1", ",\"별가루, 반짝\",game_number,m1")   // dust의 키를 못 읽음
                .Replace("daily_missions", "kill_monsters,daily,kill_monster,100,gold,20,m1", "kill_monsters,daily,kill_monster,100,dungeon_key,20,m1")); // m1 → v1 재화
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check7 && i.Table == "daily_missions" && i.Column == "reward_currency"), Is.True, Describe(issues));
            // dust를 가리키던 출석은 "없음"으로 단정하지 않는다
            Assert.That(issues.Any(i => i.Table == "attendance"), Is.False, Describe(issues));
        }

        [Test]
        public void 한_판의_칸_번호가_깨져도_읽은_선의_중복과_자기_연결은_잡는다()
        {
            var issues = Run(Good().Replace("boards",
                "1,cassiopeia,1,2,m1\n1,cassiopeia,2,3,m1\n1,cassiopeia,3,4,m1\n1,cassiopeia,4,5,m1\n",
                "1,cassiopeia,1,2,m1\n1,cassiopeia,2,1,m1\n1,cassiopeia,3,3,m1\n1,cassiopeia,4,x,m1\n"));
            // 칸 번호 'x'를 못 읽은 문제(boards의 칸 형식 문제는 검사 9로 표시) + 겹친 선 + 자기 연결
            Assert.That(issues.Count(i => i.Kind == TableIssueKind.Check9 && i.Table == "boards"), Is.EqualTo(3), Describe(issues));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check9 && i.Message.Contains("'x'")), Is.True, Describe(issues));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check9 && i.Message.Contains("같은 선 1-2")), Is.True, Describe(issues));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check9 && i.Message.Contains("자기")), Is.True, Describe(issues));
            Assert.That(issues.Any(i => i.Message.StartsWith("1장 판")), Is.False, "칸 번호를 못 읽은 판의 이어짐은 보지 않음: " + Describe(issues));
        }

        [Test]
        public void v1_별의_속성을_못_읽어도_m1_개수는_센다()
        {
            var issues = Run(Good()
                .Replace("stars", "wood_4,wood,wood 4,v1", "wood_4,,wood 4,v1")
                .Replace("stars", "fire_3,fire,fire 3,m1\n", ""));
            // element 칸의 형식 문제도 검사 8로 표시되므로, 개수 문구로 구분한다
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check8 && i.Message.StartsWith("m1 별은 속성마다") && i.Message.Contains("fire 2")), Is.True, Describe(issues));
        }

        [Test]
        public void build를_못_읽은_행이_있어도_별자리_비교는_한다()
        {
            var issues = Run(Good().Replace("boards", "1,cassiopeia,4,5,m1", "1,lyra,4,5,x"));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check9 && i.Column == "constellation_id" && i.Message.Contains("lyra")), Is.True, Describe(issues));
        }

        [Test]
        public void 별자리를_못_읽은_행이_있어도_build_비교는_한다()
        {
            var issues = Run(Good().Replace("boards", "1,cassiopeia,4,5,m1", "1,,4,5,v1"));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check9 && i.Column == "build"), Is.True, Describe(issues));
        }

        [Test]
        public void 못_읽은_칸이_있어도_그_칸과_상관없는_검사는_계속한다()
        {
            // day를 못 읽어도 cycle 규칙 개수 검사는 한다
            var issues = Run(Good()
                .Replace("attendance", "2,currency,dust,1000", "x,currency,dust,1000")
                .Replace("reset_rules", "attendance_cycle,cycle,,,3,540,5,10,5\n", ""));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check4 && i.Message.Contains("cycle 규칙이 정확히 하나")), Is.True, Describe(issues));

            // 한 판의 칸 번호를 못 읽어도 다른 판의 이어짐은 본다
            issues = Run(Good()
                .Replace("boards", "1,cassiopeia,4,5,m1", "1,cassiopeia,4,x,m1")
                .Replace("boards", "2,cygnus,2,5,v1", "2,cygnus,1,3,v1"));
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check9 && i.Message.StartsWith("2장 판이 하나로 이어지지 않음")), Is.True, Describe(issues));
            Assert.That(issues.Any(i => i.Message.StartsWith("1장 판")), Is.False, Describe(issues));
        }

        [Test]
        public void 검사4_1일이_없으면_잡는다()
        {
            AssertOnly(Run(Good().Replace("attendance", "1,currency,gold,50\n", "")), TableIssueKind.Check4);
        }

        [Test]
        public void 검사4_출석_표가_비면_잡는다()
        {
            AssertOnly(Run(Good().HeaderOnly("attendance")), TableIssueKind.Check4);
        }

        [Test]
        public void 검사4_1일_하나만_있어도_빠짐은_없다()
        {
            AssertNone(Run(Good()
                .Replace("attendance", "2,currency,dust,1000\n3,star,wood_1,1\n", "")
                .Replace("reset_rules", "attendance_cycle,cycle,,,3,", "attendance_cycle,cycle,,,1,")));
        }

        [Test]
        public void 검사4_마지막_날이_cycle_days와_다르면_잡는다()
        {
            var issues = Run(Good().Replace("reset_rules", "attendance_cycle,cycle,,,3,", "attendance_cycle,cycle,,,4,"));
            AssertOnly(issues, TableIssueKind.Check4);
            Assert.That(issues[0].Message, Does.Contain("마지막 날 3일").And.Contain("cycle_days 4"));
        }

        [Test]
        public void 검사4_출석일을_늘리고_cycle_days를_안_고치면_잡는다()
        {
            AssertOnly(Run(Good().AppendRow("attendance", "4,currency,gold,10")), TableIssueKind.Check4);
        }

        [Test]
        public void 검사4_cycle_규칙이_없거나_둘이면_잡는다()
        {
            AssertOnly(Run(Good().Replace("reset_rules", "attendance_cycle,cycle,,,3,540,5,10,5\n", "")), TableIssueKind.Check4);
            AssertOnly(Run(Good().AppendRow("reset_rules", "event_cycle,cycle,,,3,540,5,10,5")), TableIssueKind.Check4);
        }

        [Test]
        public void 검사4_순서가_섞여도_빠짐이_없으면_된다()
        {
            var t = Good();
            t["attendance.csv"] = "day,reward_kind,reward_id,amount\n3,star,wood_1,1\n1,currency,gold,50\n2,currency,dust,1000\n";
            AssertNone(Run(t));
        }

        // ---- 5. 요일 던전 ----

        [Test]
        public void 검사5_요일이_하나_빠지면_잡는다()
        {
            var issues = Run(Good().Replace("weekday_dungeons", "sun,dungeon_sun,dungeon_key,2\n", ""));
            AssertOnly(issues, TableIssueKind.Check5);
            Assert.That(issues[0].Message, Does.Contain("sun"));
        }

        [Test]
        public void 검사5_요일이_겹쳐_7행이어도_빠진_요일을_잡는다()
        {
            // 7행이지만 sun 대신 mon이 두 번 → 검사 1(겹침) + 검사 5(빠짐)
            var issues = Run(Good().Replace("weekday_dungeons", "sun,dungeon_sun", "mon,dungeon_sun"));
            AssertHas(issues, TableIssueKind.Check1);
            AssertHas(issues, TableIssueKind.Check5);
        }

        // ---- 6. 시간대 이벤트 ----

        [TestCase("lunch,mon|wed,12:00,13:00", "lunch,mon|wed,12:00,12:00")]
        [TestCase("lunch,mon|wed,12:00,13:00", "lunch,mon|wed,13:00,12:00")]
        public void 검사6_시작이_끝보다_앞이_아니면_잡는다(string from, string to)
        {
            AssertOnly(Run(Good().Replace("timed_events", from, to)), TableIssueKind.Check6);
        }

        [Test]
        public void 검사6_같은_요일에_겹치면_잡는다()
        {
            var issues = Run(Good().Replace("timed_events", "after_lunch,mon,13:00,14:00", "after_lunch,mon,12:59,14:00"));
            AssertOnly(issues, TableIssueKind.Check6);
            Assert.That(issues[0].Message, Does.Contain("mon"));
        }

        [Test]
        public void 검사6_요일_목록의_다른_요일에서_겹쳐도_잡는다()
        {
            var issues = Run(Good().AppendRow("timed_events", "wed_bonus,fri|wed,12:30,12:45,gold"));
            AssertOnly(issues, TableIssueKind.Check6);
            Assert.That(issues[0].Message, Does.Contain("wed"));
        }

        [Test]
        public void 검사6_다른_요일의_같은_시간은_겹침이_아니다()
        {
            AssertNone(Run(Good().AppendRow("timed_events", "tue_lunch,tue|thu,12:00,13:00,gold")));
        }

        [Test]
        public void 검사6_하루_경계를_넘으면_잡는다()
        {
            var issues = Run(Good().AppendRow("timed_events", "night,sun,04:30,05:30,gold"));
            AssertOnly(issues, TableIssueKind.Check6);
            Assert.That(issues[0].Message, Does.Contain("05:00"));
        }

        [Test]
        public void 검사6_하루_경계는_daily_규칙의_reset_hour를_따른다()
        {
            // 경계를 06시로 옮기면 05:00~24:00 이벤트가 06:00을 넘는다
            var issues = Run(Good().Replace("reset_rules", "daily,daily,5,", "daily,daily,6,"));
            AssertOnly(issues, TableIssueKind.Check6);
            Assert.That(issues.Single().Message, Does.Contain("06:00"));
        }

        [Test]
        public void 검사6_daily_규칙이_없으면_경계를_알_수_없다고_알린다()
        {
            // 일일 임무는 daily 규칙만 가리킬 수 있으므로 검사 2도 함께 걸린다
            var t = Good().Replace("reset_rules", "daily,daily,5,,,540,5,10,5\n", "")
                .Replace("daily_missions", ",daily,", ",weekly,");
            var issues = Run(t);
            AssertHas(issues, TableIssueKind.Check6);
            AssertHas(issues, TableIssueKind.Check2);
            Assert.That(issues.All(i => i.Kind == TableIssueKind.Check6 || i.Kind == TableIssueKind.Check2), Is.True, Describe(issues));
        }

        // ---- 7. m1 → v1 참조 ----

        [Test]
        public void 검사7_m1_임무가_v1_재화를_주면_잡는다()
        {
            var issues = Run(Good().Replace("daily_missions", "kill_monsters,daily,kill_monster,100,gold,20,m1", "kill_monsters,daily,kill_monster,100,dungeon_key,20,m1"));
            AssertOnly(issues, TableIssueKind.Check7);
            Assert.That(issues[0].Message, Does.Contain("dungeon_key"));
        }

        [Test]
        public void 검사7_m1_표인_출석이_v1_별을_주면_잡는다()
        {
            AssertOnly(Run(Good().Replace("attendance", "3,star,wood_1,1", "3,star,wood_4,1")), TableIssueKind.Check7);
        }

        [Test]
        public void 검사7_v1_행이_v1을_가리키는_것은_된다()
        {
            // 테스트 표의 dungeon_runs(v1) → dungeon_key(v1), 요일 던전(v1 표) → dungeon_key(v1)
            AssertNone(Run(Good()));
        }

        // ---- 8. 별 ----

        [Test]
        public void 검사8_m1_별이_14종이면_잡는다()
        {
            AssertOnly(Run(Good().Replace("stars", "water_3,water,water 3,m1", "water_3,water,water 3,v1")), TableIssueKind.Check8);
        }

        [Test]
        public void 검사8_m1_별이_16종이면_잡는다()
        {
            AssertOnly(Run(Good().Replace("stars", "wood_4,wood,wood 4,v1", "wood_4,wood,wood 4,m1")), TableIssueKind.Check8);
        }

        [Test]
        public void 검사8_합계_15종이어도_속성마다_3종이_아니면_잡는다()
        {
            var issues = Run(Good().Replace("stars", "water_3,water,", "water_3,fire,"));
            AssertOnly(issues, TableIssueKind.Check8);
            Assert.That(issues[0].Message, Does.Contain("fire 4").And.Contain("water 2"));
        }

        [Test]
        public void 검사8_다섯_속성이_아니면_잡는다()
        {
            var issues = Run(Good().Replace("stars", "wood_4,wood,", "wood_4,light,"));
            AssertOnly(issues, TableIssueKind.Check8);
            Assert.That(issues[0].Column, Is.EqualTo("element"));
        }

        // ---- 9. 별자리 판 ----

        [Test]
        public void 검사9_칸_번호가_5를_넘으면_잡는다()
        {
            AssertOnly(Run(Good().Replace("boards", "1,cassiopeia,4,5,m1", "1,cassiopeia,4,6,m1")), TableIssueKind.Check9);
        }

        [Test]
        public void 검사9_칸_번호가_0이면_잡는다()
        {
            AssertOnly(Run(Good().Replace("boards", "1,cassiopeia,1,2,m1", "1,cassiopeia,0,2,m1")), TableIssueKind.Check9);
        }

        [Test]
        public void 검사9_자기_자신과_연결하면_잡는다()
        {
            AssertOnly(Run(Good().AppendRow("boards", "1,cassiopeia,3,3,m1")), TableIssueKind.Check9);
        }

        [Test]
        public void 검사9_같은_선을_거꾸로_적어도_중복으로_잡는다()
        {
            var issues = Run(Good().AppendRow("boards", "1,cassiopeia,2,1,m1"));
            AssertOnly(issues, TableIssueKind.Check9);
            Assert.That(issues[0].Message, Does.Contain("1-2"));
        }

        [Test]
        public void 검사9_두_묶음으로_떨어지면_잡는다()
        {
            // 1-2 / 3-4-5 로 끊김
            var issues = Run(Good().Replace("boards", "1,cassiopeia,2,3,m1", "1,cassiopeia,3,5,m1"));
            AssertOnly(issues, TableIssueKind.Check9);
            Assert.That(issues[0].Message, Does.Contain("묶음 2개"));
        }

        [Test]
        public void 검사9_선이_없는_칸이_있으면_잡는다()
        {
            var issues = Run(Good().Replace("boards", "1,cassiopeia,4,5,m1\n", ""));
            AssertOnly(issues, TableIssueKind.Check9);
            Assert.That(issues[0].Message, Does.Contain("선이 없는 칸: 5"));
        }

        [Test]
        public void 검사9_한_장_안에서_별자리나_build가_다르면_잡는다()
        {
            AssertOnly(Run(Good().Replace("boards", "1,cassiopeia,4,5,m1", "1,lyra,4,5,m1")), TableIssueKind.Check9);
            AssertOnly(Run(Good().Replace("boards", "1,cassiopeia,4,5,m1", "1,cassiopeia,4,5,v1")), TableIssueKind.Check9);
        }

        [Test]
        public void 검사9_같은_별자리가_두_장에_있으면_잡는다()
        {
            AssertOnly(Run(Good().Replace("boards", "2,cygnus,", "2,cassiopeia,")), TableIssueKind.Check9);
        }

        [Test]
        public void 검사9_고리_모양_연결은_된다()
        {
            AssertNone(Run(Good().AppendRow("boards", "1,cassiopeia,5,1,m1")));
        }

        // ---- 10. 시계 설정 ----

        [TestCase("weekly,weekly,5,mon,,540,", "weekly,weekly,5,mon,,600,", "utc_offset_min")]
        [TestCase("weekly,weekly,5,mon,,540,5,", "weekly,weekly,5,mon,,540,6,", "sync_interval_min")]
        [TestCase("attendance_cycle,cycle,,,3,540,5,10,", "attendance_cycle,cycle,,,3,540,5,11,", "anchor_valid_min")]
        [TestCase("attendance_cycle,cycle,,,3,540,5,10,5", "attendance_cycle,cycle,,,3,540,5,10,4", "max_rtt_sec")]
        public void 검사10_행마다_시계_설정이_다르면_잡는다(string from, string to, string column)
        {
            var issues = Run(Good().Replace("reset_rules", from, to));
            AssertOnly(issues, TableIssueKind.Check10);
            Assert.That(issues[0].Column, Is.EqualTo(column));
        }

        [Test]
        public void 검사10_reset_hour는_행마다_달라도_된다()
        {
            AssertNone(Run(Good().Replace("reset_rules", "weekly,weekly,5,", "weekly,weekly,4,")));
        }

        // ---- 여러 위반을 한꺼번에 ----

        [Test]
        public void 여러_위반이_한꺼번에_있으면_하나에서_멈추지_않고_모두_알린다()
        {
            var t = Good()
                .AppendRow("daily_missions", "kill_monsters,daily,kill_monster,1,gold,1,m1")      // 1
                .Replace("weekly_challenge", "10,dungeon_key,3", "10,nothing,3")                  // 2
                .Replace("ad_buffs", "hunt_double,2,30", "hunt_double,0,30")                       // 3
                .Replace("attendance", "2,currency,dust,1000\n", "")                              // 4
                .Replace("weekday_dungeons", "sun,dungeon_sun,dungeon_key,2\n", "")                // 5
                .AppendRow("timed_events", "night,sun,04:30,05:30,gold")                           // 6
                .Replace("attendance", "3,star,wood_1,1", "3,star,wood_4,1")                      // 7
                .Replace("stars", "water_3,water,water 3,m1", "water_3,water,water 3,v1")         // 8
                .AppendRow("boards", "1,cassiopeia,3,3,m1")                                        // 9
                .Replace("reset_rules", "weekly,weekly,5,mon,,540,", "weekly,weekly,5,mon,,600,"); // 10
            var issues = Run(t);
            for (int k = 1; k <= 10; k++) AssertHas(issues, (TableIssueKind)k);
            var result = TableBuilder.BuildFromTexts(t);
            Assert.That(result.Outputs, Is.Empty, "문제가 있으면 JSON을 내지 않음");
        }
    }
}
