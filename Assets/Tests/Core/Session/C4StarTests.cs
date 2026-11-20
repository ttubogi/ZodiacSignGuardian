using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Tests.Data;
using static ZodiacSignGuardian.Core.Tests.Session.C4;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>C4 뽑기·별 성장·출석 중복 (설계서 v0.12 3절 "별 뽑기"·"별과 별 성장", 5절 ★ 시나리오).</summary>
    [TestFixture]
    public class C4StarTests
    {
        // ---- 뽑기 ----

        // 시드 7(C3Harness.New), 뽑기 흐름의 처음 10번. 기대값은 SplitMix64·xoshiro256**·뽑는 순서를 따로 옮긴 파이썬 코드로 만들었다
        // (C 기준 구현을 옮긴 것, 저장소 밖). 같은 테스트가 Unity Test Runner에서도 돌아 dotnet과 Unity의 결과가 같음을 본다.
        static readonly string[] Seed7Ten = { "wood_1", "wood_3", "fire_1", "fire_2", "wood_1", "fire_1", "fire_1", "water_1", "wood_1", "metal_1" };

        [Test]
        public void 같은_시드로_10번_뽑으면_정해진_star_id_순서()
        {
            var h = New();
            Give(h, "gold", 900);
            var e = h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
            var drawn = e.Where(x => x is StarObtained || x is StarDuplicated)
                .Select(x => x is StarObtained o ? o.StarId : ((StarDuplicated)x).StarId).ToArray();
            Assert.That(drawn, Is.EqualTo(Seed7Ten));
        }

        [Test]
        public void 한_번씩_10번_뽑아도_10번_뽑기와_같은_별()
        {
            var h = New();
            Give(h, "gold", 1000);
            var drawn = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                var e = h.Ok(h.S.DrawStar(1, DrawPayment.Crystal));
                drawn.Add(e.OfType<StarObtained>().Select(o => o.StarId).Concat(e.OfType<StarDuplicated>().Select(d => d.StarId)).Single());
            }
            Assert.That(drawn, Is.EqualTo(Seed7Ten));
            Assert.That(Bal(h, "gold"), Is.EqualTo(0));
        }

        [Test]
        public void 열_번_뽑기의_이벤트_순서와_별조각_합계()
        {
            var h = New();
            Give(h, "gold", 1000);
            var e = h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
            // 비용 → 뽑은 순서대로 별 → 별조각 합계 한 번 → 임무
            Assert.That(e[0], Is.TypeOf<CurrencyChanged>());
            var cost = (CurrencyChanged)e[0];
            Assert.That(cost.Reason, Is.EqualTo(CurrencyReason.StarDraw));
            Assert.That(cost.Delta.Integer, Is.EqualTo(-900));
            var starEvents = e.Skip(1).Take(10).ToList();
            Assert.That(starEvents.All(x => x is StarObtained || x is StarDuplicated), Is.True, string.Join(", ", e));
            // wood_1 ×4(새 1 + 중복 3: 일반 5 × 3), fire_1 ×3(중복 2) → 일반 중복 5개 × 5 = 25
            var dups = starEvents.OfType<StarDuplicated>().ToList();
            Assert.That(dups.Select(d => d.StarId), Is.EqualTo(new[] { "wood_1", "fire_1", "fire_1", "wood_1" }));
            Assert.That(dups.Sum(d => d.Shards), Is.EqualTo(20));
            var shard = (CurrencyChanged)e[11];
            Assert.That(shard.Reason, Is.EqualTo(CurrencyReason.StarDuplicate));
            Assert.That(shard.CurrencyId, Is.EqualTo("shard"));
            Assert.That(shard.Delta.Integer, Is.EqualTo(20));
            Assert.That(e.OfType<CurrencyChanged>().Count(), Is.EqualTo(2), "별조각은 한 번에");
            Assert.That(e.Skip(12).All(x => x is MissionProgressed), Is.True);
            Assert.That(h.MissionProgress("draw_any"), Is.EqualTo(10));
            Assert.That(h.S.OwnedStars.Select(s => s.StarId), Is.EqualTo(new[] { "wood_1", "wood_3", "fire_1", "fire_2", "water_1", "metal_1" }));
            Assert.That(Bal(h, "shard"), Is.EqualTo(20));
            Assert.That(Bal(h, "gold"), Is.EqualTo(100));
        }

        [Test]
        public void 가진_별이_뽑히면_새_인스턴스_없이_별조각()
        {
            var h = New();
            Own(h, "wood_1"); // 시드 7의 첫 뽑기
            var before = h.S.OwnedStars.Count;
            Give(h, "gold", 100);
            var e = h.Ok(h.S.DrawStar(1, DrawPayment.Crystal));
            Assert.That(e.OfType<StarObtained>(), Is.Empty);
            Assert.That(e.OfType<StarDuplicated>().Single().Shards, Is.EqualTo(5));
            Assert.That(h.S.OwnedStars.Count, Is.EqualTo(before));
            Assert.That(Bal(h, "shard"), Is.EqualTo(5));
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(9)]
        [TestCase(11)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void 뽑기_수는_1과_10만(int count)
        {
            var h = New();
            Give(h, "gold", 100000);
            h.AssertFailsUnchanged(() => h.S.DrawStar(count, DrawPayment.Crystal), ErrorCode.InvalidInput);
        }

        [TestCase(0)]
        [TestCase(3)]
        [TestCase(-1)]
        public void 모르는_결제_방식은_InvalidInput(int pay)
        {
            var h = New();
            Give(h, "gold", 1000);
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, (DrawPayment)pay), ErrorCode.InvalidInput);
        }

        [Test]
        public void 무료_10번은_InvalidInput()
        {
            var h = New();
            h.AssertFailsUnchanged(() => h.S.DrawStar(10, DrawPayment.Free), ErrorCode.InvalidInput);
        }

        [TestCase(1, 99)]
        [TestCase(10, 899)]
        [TestCase(1, 0)]
        public void 비용이_모자라면_NotEnoughCurrency_난수도_그대로(int count, long gold)
        {
            var h = New();
            Give(h, "gold", gold);
            h.AssertFailsUnchanged(() => h.S.DrawStar(count, DrawPayment.Crystal), ErrorCode.NotEnoughCurrency);
            // 실패 뒤에 뽑아도 처음 순서 그대로 (난수를 쓰지 않았음)
            Give(h, "gold", 900);
            var e = h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
            Assert.That(e.OfType<StarObtained>().First().StarId, Is.EqualTo("wood_1"));
        }

        [Test]
        public void 비용이_딱_맞으면_뽑힌다()
        {
            var h = New();
            Give(h, "gold", 900);
            h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
            Assert.That(Bal(h, "gold"), Is.EqualTo(0));
        }

        [Test]
        public void 무료_뽑기는_하루_한_번_05시_뒤에_다시()
        {
            var h = New();
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(1));
            var e = h.Ok(h.S.DrawStar(1, DrawPayment.Free));
            Assert.That(e.OfType<CurrencyChanged>().Where(c => c.Reason == CurrencyReason.StarDraw), Is.Empty, "무료는 비용 없음");
            Assert.That(h.MissionProgress("draw_any"), Is.EqualTo(1), "무료 뽑기도 draw_star");
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(0));
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
            // 다음 날 05시 (T0는 12시) 뒤
            h.Sync(T0 + 17 * 60 * Min - 1000);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(0), "05시 전");
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
            h.Sync(T0 + 17 * 60 * Min);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(1), "05:00:00");
            h.Ok(h.S.DrawStar(1, DrawPayment.Free));
        }

        [Test]
        public void 시간_미확인에서_무료는_TimeNotVerified_유료는_뽑히지만_임무는_세지_않음()
        {
            var h = New(sync: false);
            Assert.That(h.S.FreeDrawsLeftToday, Is.Null);
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.TimeNotVerified);
            Give(h, "gold", 100);
            h.Ok(h.S.DrawStar(1, DrawPayment.Crystal));
            Assert.That(h.S.OwnedStars.Count, Is.EqualTo(1));
            Assert.That(h.MissionProgress("draw_any"), Is.EqualTo(0));
        }

        [Test]
        public void 서버가_뒤로_가면_무료는_TimeWentBack_유료는_임무를_세지_않음()
        {
            var h = New();
            h.Sync(T0 + Day);           // 다음 날 처리
            h.Sync(T0);                 // 서버가 하루 뒤로 감 → 날짜 기능 TimeWentBack
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.TimeWentBack);
            Give(h, "gold", 100);
            var before = h.MissionProgress("draw_any");
            h.Ok(h.S.DrawStar(1, DrawPayment.Crystal));
            Assert.That(h.MissionProgress("draw_any"), Is.EqualTo(before));
        }

        [Test]
        public void 하루_무료가_0번이면_AlreadyClaimed()
        {
            var h = New(Texts().Replace("star_rules", "gold,100,900,1,shard,", "gold,100,900,0,shard,"));
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(0));
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
        }

        [Test]
        public void 하루_무료가_여러_번이면_그만큼()
        {
            var h = New(Texts().Replace("star_rules", "gold,100,900,1,shard,", "gold,100,900,3,shard,"));
            for (var i = 0; i < 3; i++) h.Ok(h.S.DrawStar(1, DrawPayment.Free));
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
        }

        [Test]
        public void 별조각_합계가_넘치면_실패하고_재화_별_난수가_그대로()
        {
            var h = New();
            Own(h, "wood_1");
            Give(h, "gold", 900);
            Give(h, "shard", long.MaxValue - 4); // 일반 중복 하나(5)만 더해도 넘침
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Crystal), ErrorCode.InvalidInput);
            h.AssertFailsUnchanged(() => h.S.DrawStar(10, DrawPayment.Crystal), ErrorCode.InvalidInput);
            Give(h, "shard", long.MaxValue - 5); // 딱 맞으면 됨
            h.Ok(h.S.DrawStar(1, DrawPayment.Crystal));
            Assert.That(Bal(h, "shard"), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void 새_인스턴스_번호가_long_끝이면_새_별은_InvalidInput_중복은_된다()
        {
            var h = New();
            var json = R(h.S.CreateSave().Json, "\"nextInstanceId\": 1", "\"nextInstanceId\": " + long.MaxValue);
            h.LoadJson(json);
            h.Sync(T0);
            Give(h, "gold", 1000);
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Crystal), ErrorCode.InvalidInput);
            // 번호 하나만 남으면 새 별 하나는 되고 10번(새 별 여럿)은 실패
            json = R(h.S.CreateSave().Json, "\"nextInstanceId\": " + long.MaxValue, "\"nextInstanceId\": " + (long.MaxValue - 1));
            h.LoadJson(json);
            h.Sync(T0);
            h.AssertFailsUnchanged(() => h.S.DrawStar(10, DrawPayment.Crystal), ErrorCode.InvalidInput);
            h.Ok(h.S.DrawStar(1, DrawPayment.Crystal));
            Assert.That(h.S.OwnedStars.Single().InstanceId, Is.EqualTo(long.MaxValue - 1));
        }

        [Test]
        public void 확률은_70_25_5에_가깝다()
        {
            // 같은 시드라 결과는 늘 같다. 2만 번에서 표준편차는 일반 약 0.32%p, 전설 약 0.15%p → 1%p 안
            var h = New();
            Give(h, "gold", 2_000 * 900);
            var counts = new Dictionary<Rarity, int>();
            var rarity = h.S.StarDefinitions.ToDictionary(s => s.StarId, s => s.Rarity);
            for (var i = 0; i < 2_000; i++)
            {
                var r = h.S.DrawStar(10, DrawPayment.Crystal);
                Assert.That(r.Ok, Is.True);
                foreach (var e in r.Events)
                {
                    var id = e is StarObtained o ? o.StarId : e is StarDuplicated d ? d.StarId : null;
                    if (id == null) continue;
                    counts.TryGetValue(rarity[id], out var c);
                    counts[rarity[id]] = c + 1;
                }
            }
            Assert.That(counts[Rarity.Common] / 20000.0, Is.EqualTo(0.70).Within(0.01));
            Assert.That(counts[Rarity.Rare] / 20000.0, Is.EqualTo(0.25).Within(0.01));
            Assert.That(counts[Rarity.Legend] / 20000.0, Is.EqualTo(0.05).Within(0.01));
            Assert.That(counts.ContainsKey(Rarity.Hero), Is.False, "테스트 표의 영웅 확률은 0");
            h.AssertNoViolations();
        }

        [Test]
        public void 확률이_0인_희귀도는_나오지_않는다()
        {
            var h = New(Texts().Replace("rarities", "common,7000,", "common,7500,").Replace("rarities", "legend,500,", "legend,0,"));
            Give(h, "gold", 300 * 900);
            for (var i = 0; i < 300; i++)
            {
                var r = h.S.DrawStar(10, DrawPayment.Crystal);
                Assert.That(r.Events.OfType<StarObtained>().Any(o => o.StarId.EndsWith("_3")), Is.False);
                Assert.That(r.Events.OfType<StarDuplicated>().Any(o => o.StarId.EndsWith("_3")), Is.False);
            }
        }

        // ---- 등급·스킬 레벨 ----

        [Test]
        public void 등급은_비용을_내고_5까지()
        {
            var h = New();
            var id = Own(h, "wood_2"); // 희귀: 20·40·80·160
            Give(h, "shard", 300);
            foreach (var (grade, cost) in new[] { (2, 20L), (3, 40L), (4, 80L), (5, 160L) })
            {
                var e = h.Ok(h.S.UpgradeStar(id));
                Assert.That(EventText.Names(e), Is.EqualTo(new[] { "CurrencyChanged", "StarGradeChanged" }));
                var c = (CurrencyChanged)e[0];
                Assert.That(c.Reason, Is.EqualTo(CurrencyReason.StarGradeUp));
                Assert.That(c.Delta.Integer, Is.EqualTo(-cost));
                Assert.That(((StarGradeChanged)e[1]).Grade, Is.EqualTo(grade));
                Assert.That(Star(h, id).Grade, Is.EqualTo(grade));
            }
            Assert.That(Bal(h, "shard"), Is.EqualTo(0));
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(id), ErrorCode.InvalidTarget);
        }

        [Test]
        public void 등급_실패_순서는_StarNotOwned_다음_InvalidTarget_다음_NotEnoughCurrency()
        {
            var h = New();
            var id = Own(h, "wood_1");
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(id + 1), ErrorCode.StarNotOwned);
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(0), ErrorCode.StarNotOwned);
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(long.MaxValue), ErrorCode.StarNotOwned);
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(id), ErrorCode.NotEnoughCurrency); // 0개
            Give(h, "shard", 9);
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(id), ErrorCode.NotEnoughCurrency); // 일반 10 필요
            Give(h, "shard", 10);
            h.Ok(h.S.UpgradeStar(id));
            // 최대 등급에서는 별조각이 없어도 InvalidTarget
            h.LoadJson(R(h.S.CreateSave().Json, "\"grade\": 2", "\"grade\": 5"));
            Give(h, "shard", 0);
            h.AssertFailsUnchanged(() => h.S.UpgradeStar(id), ErrorCode.InvalidTarget);
        }

        [Test]
        public void 스킬_레벨_비용은_계수_곱하기_지금_레벨()
        {
            var h = New();
            var id = Own(h, "fire_3"); // 전설: 10 × L
            Give(h, "shard", 10 + 20 + 30);
            for (var level = 2; level <= 4; level++)
            {
                var e = h.Ok(h.S.UpgradeSkill(id));
                Assert.That(EventText.Names(e), Is.EqualTo(new[] { "CurrencyChanged", "SkillLevelChanged" }));
                Assert.That(((CurrencyChanged)e[0]).Reason, Is.EqualTo(CurrencyReason.SkillLevelUp));
                Assert.That(((SkillLevelChanged)e[1]).Level, Is.EqualTo(level));
            }
            Assert.That(Bal(h, "shard"), Is.EqualTo(0));
            h.AssertFailsUnchanged(() => h.S.UpgradeSkill(id), ErrorCode.NotEnoughCurrency);
            h.AssertFailsUnchanged(() => h.S.UpgradeSkill(id + 5), ErrorCode.StarNotOwned);
        }

        [Test]
        public void 스킬_레벨은_최대에서_InvalidTarget()
        {
            var h = New();
            var id = Own(h, "earth_1");
            h.LoadJson(R(h.S.CreateSave().Json, "\"skillLevel\": 1", "\"skillLevel\": 29"));
            h.Sync(T0);
            Give(h, "shard", 2 * 29);
            h.Ok(h.S.UpgradeSkill(id));
            Assert.That(Star(h, id).SkillLevel, Is.EqualTo(30));
            Give(h, "shard", long.MaxValue);
            h.AssertFailsUnchanged(() => h.S.UpgradeSkill(id), ErrorCode.InvalidTarget);
        }

        [Test]
        public void 스킬_비용_곱은_표의_끝값에서도_넘치지_않는다()
        {
            // skill_cost_per_level 1e9 × 레벨 999 = 9.99e11
            var t = Texts().Replace("rarities", "common,7000,5,3000,2,", "common,7000,5,3000,1000000000,")
                .Replace("star_rules", ",1,shard,30,", ",1,shard,1000,");
            var h = New(t);
            var id = Own(h, "earth_1");
            h.LoadJson(R(h.S.CreateSave().Json, "\"skillLevel\": 1", "\"skillLevel\": 999"));
            Give(h, "shard", 999_000_000_000L - 1);
            h.AssertFailsUnchanged(() => h.S.UpgradeSkill(id), ErrorCode.NotEnoughCurrency);
            Give(h, "shard", 999_000_000_000L);
            h.Ok(h.S.UpgradeSkill(id));
            Assert.That(Bal(h, "shard"), Is.EqualTo(0));
        }

        // ---- 출석 중복 ----

        [Test]
        public void 출석_별이_이미_있으면_별조각()
        {
            var h = New();
            Own(h, "wood_1");
            h.S.Attendance.ReplaceRewardForTest(new AttendanceReward(1, AttendanceRewardKind.Star, "wood_1", 1));
            var count = h.S.OwnedStars.Count;
            var e = h.Ok(h.S.ClaimAttendance());
            Assert.That(e.OfType<StarObtained>(), Is.Empty);
            Assert.That(e.OfType<StarDuplicated>().Single().Shards, Is.EqualTo(5));
            var c = e.OfType<CurrencyChanged>().Single();
            Assert.That(c.Reason, Is.EqualTo(CurrencyReason.StarDuplicate));
            Assert.That(c.CurrencyId, Is.EqualTo("shard"));
            Assert.That(h.S.OwnedStars.Count, Is.EqualTo(count));
        }

        [Test]
        public void 출석_별조각이_넘치면_InvalidInput_출석도_그대로()
        {
            var h = New();
            Own(h, "wood_1");
            Give(h, "shard", long.MaxValue);
            h.S.Attendance.ReplaceRewardForTest(new AttendanceReward(1, AttendanceRewardKind.Star, "wood_1", 1));
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.InvalidInput);
        }
    }
}
