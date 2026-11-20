using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Tables;
using static ZodiacSignGuardian.Core.Tests.Data.TestTables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    /// <summary>
    /// C4 표(rarities·star_rules·stages)와 검사 8(희귀도)·11·12, 큰 수 칸 (설계서 v0.12 2절).
    /// 위반 주입: 그 검사만 걸리는지(AssertOnly), 못 읽은 칸이 연쇄 문제를 내지 않는지, 아주 큰 값에서도 멈추지 않는지.
    /// </summary>
    [TestFixture]
    public class C4TableTests
    {
        const string Stage2 = "2,1,2,2.626e1,2.32e0,2.784e1,15,60,gold,300,,,,,m1";
        const string Stage3 = "3,1,3,3.447938e1,2.32e0,2.784e1,15,60,gold,300,2.0687628e2,30,gold,2000,m1";

        // ---- 큰 수 칸 ----

        [TestCase("1e0", 0L, 100000000000000L)]
        [TestCase("2e1", 1L, 200000000000000L)]
        [TestCase("1.313e1", 1L, 131300000000000L)]
        [TestCase("9.99999999999999e1000000", 1000000L, 999999999999999L)]
        [TestCase("1.00000000000001e0", 0L, 100000000000001L)]
        public void 큰_수_칸은_한_가지_모양만_읽는다(string text, long exponent, long digits)
        {
            Assert.That(GameNumberText.TryParse(text, out var e, out var d, out var why), Is.True, why);
            Assert.That(e, Is.EqualTo(exponent));
            Assert.That(d, Is.EqualTo(digits));
        }

        [TestCase("0e0", Description = "0은 받지 않음 (1 이상)")]
        [TestCase("-1e0")]
        [TestCase("+1e0")]
        [TestCase("1.0e1", Description = "끝 0")]
        [TestCase("1.e1")]
        [TestCase("1e01", Description = "지수 앞 0")]
        [TestCase("1e-1", Description = "1 미만")]
        [TestCase("10e0")]
        [TestCase("1.234567890123456e0", Description = "16자리")]
        [TestCase("1e1000001")]
        [TestCase("1e99999999999999999999999", Description = "long을 넘는 지수")]
        [TestCase("1E1")]
        [TestCase(" 1e1")]
        [TestCase("1e1 ")]
        [TestCase("1e")]
        [TestCase("e1")]
        [TestCase("1")]
        [TestCase("12")]
        [TestCase("1.5")]
        [TestCase("")]
        public void 큰_수_칸의_틀린_모양은_형식_문제(string text)
        {
            Assert.That(GameNumberText.TryParse(text, out _, out _, out _), Is.False);
            var issues = Run(Good().Replace("stages", Stage2, Stage2.Replace(",2.626e1,", "," + text + ",")));
            if (text.Length == 0)
            {
                AssertHas(issues, TableIssueKind.Format); // 빈 칸은 '값이 필요함'
                return;
            }
            AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Single().Column, Is.EqualTo("monster_hp"));
        }

        [Test]
        public void 큰_수_비교는_지수_다음_가수()
        {
            Assert.That(GameNumberText.Compare("9.9e0", "1e1"), Is.LessThan(0));
            Assert.That(GameNumberText.Compare("1.5e3", "1.5e3"), Is.EqualTo(0));
            Assert.That(GameNumberText.Compare("2e3", "1.99999999999999e3"), Is.GreaterThan(0));
            Assert.That(GameNumberText.Compare("1e1000000", "9.99999999999999e999999"), Is.GreaterThan(0));
        }

        // ---- 열 범위 ----

        [TestCase("star_rules", ",1,shard,30,", ",11,shard,30,", "free_draws_per_day")]
        [TestCase("star_rules", ",1,shard,30,", ",1,shard,1001,", "max_skill_level")]
        [TestCase("star_rules", ",1,shard,30,", ",1,shard,0,", "max_skill_level")]
        [TestCase("star_rules", "1000,200,2000,3000,1000", "1000,200,2000,3000,10001", "board_complete_bp")]
        [TestCase("star_rules", "gold,100,", "gold,1000000001,", "draw_cost")]
        [TestCase("rarities", "common,7000,5,3000,", "common,7000,5,100001,", "skill_coef_bp")]
        [TestCase("rarities", "common,7000,5,3000,", "common,7000,5,0,", "skill_coef_bp")]
        [TestCase("rarities", "common,7000,5,", "common,10001,5,", "draw_weight")]
        [TestCase("rarities", "common,7000,5,", "common,7000,0,", "dup_shards")]
        [TestCase("rarities", "10,20,40,80", "10,20,40,1000000001", "grade5_cost")]
        [TestCase("stages", "15,60,gold,300,,,,,m1\n2,", "1001,60,gold,300,,,,,m1\n2,", "kills_to_clear")]
        [TestCase("stages", "15,60,gold,300,,,,,m1\n2,", "15,3601,gold,300,,,,,m1\n2,", "clear_time_sec")]
        public void C4_열은_범위_밖이면_형식_문제(string table, string from, string to, string column)
        {
            var issues = Run(Good().Replace(table, from, to));
            AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Select(i => i.Column).Distinct(), Is.EqualTo(new[] { column }));
        }

        [Test]
        public void 범위의_끝값은_통과한다()
        {
            AssertNone(Run(Good()
                .Replace("star_rules", "gold,100,900,1,shard,30,1000,200,2000,3000,1000", "gold,1000000000,1,10,shard,1000,10000,10000,10000,10000,10000")
                .Replace("rarities", "common,7000,5,3000,2,10,20,40,80", "common,7000,1000000000,100000,1000000000,1,1,1,1000000000")));
        }

        [Test]
        public void 뽑기_설정_표가_두_행이면_형식_문제()
        {
            AssertOnly(Run(Good().AppendRow("star_rules", "gold,100,900,1,shard,30,1000,200,2000,3000,1000")), TableIssueKind.Format);
        }

        // ---- 검사 8: 희귀도 ----

        [Test]
        public void 검사8_m1_속성마다_희귀도가_하나씩이_아니면_잡는다()
        {
            var issues = Run(Good().Replace("stars", "wood_2,wood,rare,", "wood_2,wood,common,"));
            AssertOnly(issues, TableIssueKind.Check8);
            Assert.That(issues.Single().Message, Does.Contain("wood"));
        }

        [Test]
        public void 검사8_v1_별의_희귀도는_세지_않는다()
        {
            AssertNone(Run(Good().Replace("stars", "wood_4,wood,common,", "wood_4,wood,legend,")));
        }

        [Test]
        public void 검사8_m1_별의_희귀도를_못_읽으면_형식_문제만()
        {
            AssertOnly(Run(Good().Replace("stars", "wood_2,wood,rare,", "wood_2,wood,epic,")), TableIssueKind.Check8); // 칸 값 오류도 검사 8 번호
            var issues = Run(Good().Replace("stars", "wood_2,wood,rare,", "wood_2,wood,,"));
            Assert.That(issues.Count, Is.EqualTo(1), Describe(issues));
        }

        // ---- 검사 11: 스테이지 ----

        [Test]
        public void 검사11_좋은_표는_통과()
        {
            AssertNone(Run(Good()));
        }

        [Test]
        public void 검사11_빠진_번호를_잡는다()
        {
            var issues = Run(Good().Replace("stages", Stage2 + "\n", ""));
            AssertOnly(issues, TableIssueKind.Check11);
            Assert.That(issues.Single().Message, Does.Contain("stage_id 2"));
        }

        [Test]
        public void 검사11_번호가_1부터가_아니면_잡는다()
        {
            var t = Good();
            t["stages.csv"] = t["stages.csv"].Replace("\n1,1,1,", "\n9,1,1,");
            var issues = Run(t);
            AssertOnly(issues, TableIssueKind.Check11);
        }

        [TestCase(1000000000L)]
        [TestCase(long.MaxValue)]
        public void 검사11_아주_큰_번호도_행_수만큼만_일한다(long id)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var issues = Run(Good().Replace("stages", Stage2, Stage2.Substring(0, 0) + id + Stage2.Substring(1)));
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000));
            AssertOnly(issues, TableIssueKind.Check11);
        }

        [Test]
        public void 검사11_장_안_번호가_건너뛰면_잡는다()
        {
            AssertOnly(Run(Good().Replace("stages", "2,1,2,", "2,1,3,").Replace("stages", "3,1,3,", "3,1,4,")), TableIssueKind.Check11);
        }

        [Test]
        public void 검사11_장이_건너뛰거나_줄면_잡는다()
        {
            AssertHas(Run(Good().Replace("stages", "4,2,1,", "4,3,1,").Replace("stages", "5,2,2,", "5,3,2,")), TableIssueKind.Check11);
            AssertHas(Run(Good().Replace("stages", "\n1,1,1,", "\n1,2,1,")), TableIssueKind.Check11);
        }

        [Test]
        public void 검사11_보스_칸은_장_마지막에만_모두()
        {
            var middle = Run(Good().Replace("stages", Stage2, Stage2.Replace("300,,,,,m1", "300,,30,,,m1")));
            AssertOnly(middle, TableIssueKind.Check11);
            Assert.That(middle.Single().Column, Is.EqualTo("boss_time_sec"));

            var missing = Run(Good().Replace("stages", Stage3, Stage3.Replace(",2000,m1", ",,m1")));
            AssertOnly(missing, TableIssueKind.Check11);
            Assert.That(missing.Single().Column, Is.EqualTo("boss_reward_amount"));
        }

        [TestCase("monster_hp", "2,1,2,2.626e1,", "2,1,2,1.99e1,")]
        [TestCase("dust_per_kill", "2.626e1,2.32e0,", "2.626e1,1.5e0,")]
        [TestCase("offline_dust_per_min", "2.32e0,2.784e1,15,60,gold,300,,", "2.32e0,2e1,15,60,gold,300,,")]
        public void 검사11_체력과_보상은_줄지_않는다(string column, string from, string to)
        {
            var issues = Run(Good().Replace("stages", from, to));
            AssertOnly(issues, TableIssueKind.Check11);
            Assert.That(issues.Select(i => i.Column).Distinct(), Is.EqualTo(new[] { column }));
        }

        [Test]
        public void 검사11_같은_값은_줄지_않은_것()
        {
            AssertNone(Run(Good().Replace("stages", "2,1,2,2.626e1,", "2,1,2,2e1,")));
        }

        [Test]
        public void 검사11_v1_뒤에_m1이_오면_잡는다()
        {
            var t = Good().Replace("stages", "4,2,1,4.5e1,3e0,3.6e1,15,60,gold,300,,,,,v1", "4,2,1,4.5e1,3e0,3.6e1,15,60,gold,300,,,,,m1")
                .Replace("stages", Stage3, Stage3.Replace(",m1", ",v1"));
            AssertHas(Run(t), TableIssueKind.Check11);
        }

        [Test]
        public void 검사11_m1_장에_m1_판이_없으면_잡는다()
        {
            var t = Good().Replace("stages", "gold,300,,,,,v1", "gold,300,,,,,m1").Replace("stages", "dungeon_key,5,v1", "gold,5,m1");
            var issues = Run(t);
            AssertOnly(issues, TableIssueKind.Check11);
            Assert.That(issues.Single().Message, Does.Contain("2장"));
            // 판도 m1로 바꾸면 통과
            AssertNone(Run(t.Replace("boards", "2,cygnus,1,2,v1\n2,cygnus,2,3,v1\n2,cygnus,2,4,v1\n2,cygnus,2,5,v1", "2,cygnus,1,2,m1\n2,cygnus,2,3,m1\n2,cygnus,2,4,m1\n2,cygnus,2,5,m1")));
        }

        [Test]
        public void 검사11_보상_재화는_정수_재화()
        {
            var issues = Run(Good().Replace("stages", Stage2, Stage2.Replace(",gold,300,", ",dust,300,")));
            AssertOnly(issues, TableIssueKind.Check11);
            Assert.That(issues.Single().Column, Is.EqualTo("first_clear_currency"));
        }

        [Test]
        public void 검사11_m1_스테이지가_없으면_잡는다()
        {
            AssertOnly(Run(Good().HeaderOnly("stages")), TableIssueKind.Check11);
        }

        [Test]
        public void 검사11_번호를_못_읽으면_형식_문제만()
        {
            var issues = Run(Good().Replace("stages", Stage2, "x" + Stage2.Substring(1)));
            Assert.That(issues.Count, Is.EqualTo(1), Describe(issues));
            Assert.That(issues[0].Kind, Is.EqualTo(TableIssueKind.Check3));
        }

        [Test]
        public void 검사11_체력을_못_읽으면_그_칸만_건너뛴다()
        {
            var issues = Run(Good().Replace("stages", "2,1,2,2.626e1,", "2,1,2,abc,"));
            AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Count, Is.EqualTo(1));
        }

        [Test]
        public void 검사11_보스_재화가_없으면_검사2()
        {
            AssertOnly(Run(Good().Replace("stages", Stage3, Stage3.Replace(",30,gold,2000,", ",30,ruby,2000,"))), TableIssueKind.Check2);
        }

        // ---- 검사 12: 뽑기 ----

        [TestCase("legend,500,", "legend,499,")]
        [TestCase("legend,500,", "legend,501,")]
        public void 검사12_확률_합이_10000이_아니면_잡는다(string from, string to)
        {
            AssertOnly(Run(Good().Replace("rarities", from, to)), TableIssueKind.Check12);
        }

        [Test]
        public void 검사12_빠진_희귀도를_잡는다()
        {
            var issues = Run(Good().Replace("rarities", "legend,500,100,8000,10,40,80,160,320\n", "").Replace("rarities", "common,7000,", "common,7500,"));
            AssertOnly(issues, TableIssueKind.Check12);
            Assert.That(issues.Single().Message, Does.Contain("legend"));
        }

        [Test]
        public void 검사12_확률이_0인_희귀도는_별이_없어도_된다()
        {
            // 전설 별을 모두 v1로 → 검사 8은 걸리지만 확률이 0이면 검사 12는 걸리지 않는다
            var t = Good().Replace("rarities", "common,7000,", "common,7500,").Replace("rarities", "legend,500,", "legend,0,");
            foreach (var e in Schemas.Elements) t.Replace("stars", e + "_3," + e + ",legend," + e + " 3,m1", e + "_3," + e + ",legend," + e + " 3,v1");
            var issues = Run(t);
            Assert.That(issues.Any(i => i.Kind == TableIssueKind.Check12), Is.False, Describe(issues));
            // 확률이 있으면 검사 12도 잡는다
            t.Replace("rarities", "common,7500,", "common,7000,").Replace("rarities", "legend,0,", "legend,500,");
            AssertHas(Run(t), TableIssueKind.Check12);
        }

        [TestCase("gold,100,900,1,shard,", "dust,100,900,1,shard,", "draw_currency")]
        [TestCase("gold,100,900,1,shard,", "gold,100,900,1,dust,", "shard_currency")]
        [TestCase("gold,100,900,1,shard,", "gold,100,900,1,gold,", "shard_currency")]
        public void 검사12_뽑기와_별조각은_서로_다른_정수_재화(string from, string to, string column)
        {
            var issues = Run(Good().Replace("star_rules", from, to));
            AssertOnly(issues, TableIssueKind.Check12);
            Assert.That(issues.Single().Column, Is.EqualTo(column));
        }

        [Test]
        public void 검사12_v1_재화를_가리키면_검사7()
        {
            AssertOnly(Run(Good().Replace("star_rules", ",1,shard,", ",1,dungeon_key,")), TableIssueKind.Check7);
        }

        [Test]
        public void 검사12_확률을_못_읽으면_형식_문제만()
        {
            var issues = Run(Good().Replace("rarities", "legend,500,", "legend,x,"));
            Assert.That(issues.Count, Is.EqualTo(1), Describe(issues));
        }

        static void AssertNone(List<TableIssue> issues) => Assert.That(issues, Is.Empty, Describe(issues));
    }
}
