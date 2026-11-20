using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Tests.Data;
using static ZodiacSignGuardian.Core.Tests.Session.C4;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>
    /// 저장소의 실제 표(별 60종, 일반·희귀·영웅·전설 × 3종, 확률 70 / 20 / 9 / 1)로 뽑기와 추천 배치를 본다 (2026-11-20 결정).
    /// 표 값은 임시라 바뀌면 기대값도 바뀐다. 기대값은 Core를 쓰지 않는 따로 계산(파이썬 기준 구현, 테스트 안의 모든 경우 비교)으로 만든다.
    /// </summary>
    public class C4StarCountTests
    {
        static C3Harness Repo()
        {
            var h = New(RepoFiles.CsvTexts());
            h.S.SetChapterForTest(3);
            return h;
        }

        // ---- 뽑기 ----

        // 시드 7, 뽑기 흐름의 처음 20번. 실제 표의 희귀도 4개·별 60종으로 파이썬 기준 구현(SplitMix64·xoshiro256**·뽑는 순서, 저장소 밖)이 만든 값.
        // fire_08은 영웅, earth_05·metal_05는 희귀.
        static readonly string[] RepoSeed7Twenty =
        {
            "wood_03", "fire_08", "water_03", "earth_05", "fire_02", "earth_02", "water_03", "metal_03", "water_01", "metal_02",
            "metal_02", "wood_01", "water_01", "water_03", "water_02", "fire_03", "water_02", "wood_02", "wood_01", "metal_05",
        };

        [Test]
        public void 실제_표로_시드_7의_처음_20번은_정해진_star_id_순서()
        {
            var h = Repo();
            Give(h, "galaxy_crystal", 1800);
            var drawn = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                var e = h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
                drawn.AddRange(e.Where(x => x is StarObtained || x is StarDuplicated).Select(x => x is StarObtained o ? o.StarId : ((StarDuplicated)x).StarId));
            }
            Assert.That(drawn, Is.EqualTo(RepoSeed7Twenty));
        }

        [Test]
        public void 실제_표의_확률은_70_20_9_1에_가깝고_희귀도_안에서는_고르게_나온다()
        {
            // 같은 시드라 결과는 늘 같다. 3만 번에서 표준편차는 일반 약 0.26%p, 희귀 0.23%p, 영웅 0.17%p, 전설 0.06%p
            var h = Repo();
            Give(h, "galaxy_crystal", 3_000 * 900);
            var rarity = h.S.StarDefinitions.ToDictionary(s => s.StarId, s => s.Rarity);
            var byRarity = new Dictionary<Rarity, int>();
            var byStar = new Dictionary<string, int>();
            for (var i = 0; i < 3_000; i++)
            {
                var r = h.S.DrawStar(10, DrawPayment.Crystal);
                Assert.That(r.Ok, Is.True);
                foreach (var e in r.Events)
                {
                    var id = e is StarObtained o ? o.StarId : e is StarDuplicated d ? d.StarId : null;
                    if (id == null) continue;
                    byRarity.TryGetValue(rarity[id], out var c);
                    byRarity[rarity[id]] = c + 1;
                    byStar.TryGetValue(id, out var k);
                    byStar[id] = k + 1;
                }
            }
            const double n = 30_000;
            Assert.That(byRarity[Rarity.Common] / n, Is.EqualTo(0.70).Within(0.01));
            Assert.That(byRarity[Rarity.Rare] / n, Is.EqualTo(0.20).Within(0.01));
            Assert.That(byRarity[Rarity.Hero] / n, Is.EqualTo(0.09).Within(0.007));
            Assert.That(byRarity[Rarity.Legend] / n, Is.EqualTo(0.01).Within(0.003));
            // 일반 별 하나는 0.70 / 15 ≈ 1400번 (표준편차 약 37) → 1200~1600
            foreach (var id in rarity.Where(kv => kv.Value == Rarity.Common).Select(kv => kv.Key))
            {
                Assert.That(byStar.TryGetValue(id, out var c) ? c : 0, Is.InRange(1200, 1600), id);
            }
            Assert.That(h.S.OwnedStars.Count(), Is.EqualTo(60), "3만 번이면 60종이 모두 나온다(전설 하나가 한 번도 안 나올 확률은 약 e^-20)");
        }

        // ---- 추천 배치 ----

        static void SetLevels(C3Harness h, Func<OwnedStar, int> level)
        {
            var json = h.S.CreateSave().Json;
            foreach (var s in h.S.OwnedStars)
            {
                var lv = level(s);
                if (lv == 1) continue;
                json = R(json, "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": 1", "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": " + lv);
            }
            h.LoadJson(json);
            h.Sync(T0);
        }

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(3, 4)]
        [TestCase(2, 5)]
        [TestCase(1, 6)]
        [TestCase(3, 7)]
        [TestCase(3, 8)]
        public void 한_속성의_후보가_다섯보다_많아도_따로_계산한_가장_좋은_배치와_같다(int chapter, int seed)
        {
            // 한 속성에서 6~8개(추천 배치가 속성마다 앞 5개만 남기는 부분을 지나게) + 다른 속성 3~4개, 레벨은 몇 가지 값만 써서 같은 힘이 자주 생기게
            var rnd = new Random(seed);
            var h = Repo();
            var elements = new[] { "wood", "fire", "earth", "metal", "water" };
            var main = elements[rnd.Next(5)];
            var all = h.S.StarDefinitions.Select(s => s.StarId).ToList();
            var pick = all.Where(id => id.StartsWith(main + "_")).OrderBy(_ => rnd.Next()).Take(6 + rnd.Next(3))
                .Concat(all.Where(id => !id.StartsWith(main + "_")).OrderBy(_ => rnd.Next()).Take(3 + rnd.Next(2))).ToList();
            foreach (var id in pick) Own(h, id);
            var levels = new[] { 1, 1, 5, 9, 20 };
            SetLevels(h, s => levels[rnd.Next(levels.Length)]);
            h.Ok(h.S.AutoArrange(chapter));
            Assert.That(Slots(h, chapter), Is.EqualTo(C4BoardTests.Reference(h, chapter, h.S.OwnedStars.ToList())));
        }

        [Test]
        public void 같은_속성_같은_힘이_다섯보다_많으면_star_id가_작은_다섯을_순서대로_쓴다()
        {
            // 실제 표 skill_step_bp 1000: 희귀 레벨 7은 5000 × (10000 + 1000 × 6) = 8천만 = 전설 레벨 1: 8000 × 10000. 나무 희귀 3 + 전설 3 = 같은 힘 여섯.
            // 어느 다섯을 어떻게 놓아도 점수가 같으므로 칸 1부터 star_id 순서: wood_04, wood_05, wood_06, wood_10, wood_11 (wood_12는 빠짐)
            var h = Repo();
            var ids = new[] { "wood_12", "wood_04", "wood_11", "wood_05", "wood_10", "wood_06" }.ToDictionary(id => id, id => Own(h, id));
            SetLevels(h, s => s.StarId == "wood_04" || s.StarId == "wood_05" || s.StarId == "wood_06" ? 7 : 1);
            h.Ok(h.S.AutoArrange(1));
            Assert.That(Slots(h, 1), Is.EqualTo(new[] { ids["wood_04"], ids["wood_05"], ids["wood_06"], ids["wood_10"], ids["wood_11"] }));
            Assert.That(Slots(h, 1), Is.EqualTo(C4BoardTests.Reference(h, 1, h.S.OwnedStars.ToList())));
        }

        [Test]
        public void 별_60종을_모두_가져도_빠르고_칸_하나를_바꾸거나_두_칸을_맞바꿔도_더_좋은_배치가_없다()
        {
            var rnd = new Random(60);
            var h = Repo();
            foreach (var s in h.S.StarDefinitions) Own(h, s.StarId);
            SetLevels(h, s => 1 + rnd.Next(30)); // 실제 표 max_skill_level 30
            foreach (var chapter in new[] { 1, 2, 3 })
            {
                // 다른 판에 놓인 별은 후보가 아니므로 판마다 새로 비교한다
                var candidates = h.S.OwnedStars.Where(s => !h.S.BoardStatuses.Where(b => b.Chapter != chapter).Any(b => b.Slots.Contains(s.InstanceId))).ToList();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                h.Ok(h.S.AutoArrange(chapter));
                sw.Stop();
                TestContext.WriteLine(chapter + "장 추천 배치(후보 " + candidates.Count + "개, 위반 검사 포함): " + sw.ElapsedMilliseconds + "ms");
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000));

                var score = C4BoardTests.Scorer(h, chapter);
                var byId = h.S.OwnedStars.ToDictionary(s => s.InstanceId);
                var result = Slots(h, chapter).Select(id => (OwnedStar?)byId[id]).ToArray();
                Assert.That(result.All(s => s != null), Is.True, "후보가 다섯 이상이면 다섯 칸을 모두 채운다");
                var best = score(result);
                var key = Key(result);
                var unused = candidates.Where(c => !result.Any(r => r.Value.InstanceId == c.InstanceId)).ToList();
                for (var i = 0; i < 5; i++)
                {
                    foreach (var c in unused)
                    {
                        var other = (OwnedStar?[])result.Clone();
                        other[i] = c;
                        AssertNotBetter(score(other), Key(other), best, key);
                    }
                    for (var j = i + 1; j < 5; j++)
                    {
                        var other = (OwnedStar?[])result.Clone();
                        (other[i], other[j]) = (other[j], other[i]);
                        AssertNotBetter(score(other), Key(other), best, key);
                    }
                }
            }
            // 세 판에 놓인 별이 겹치지 않는다
            var placed = h.S.BoardStatuses.SelectMany(b => b.Slots).Where(id => id != 0).ToList();
            Assert.That(placed.Distinct().Count(), Is.EqualTo(15));
        }

        static string[] Key(OwnedStar?[] pick) => pick.Select(p => p == null ? C4BoardTests.Empty : p.Value.StarId).ToArray();

        static void AssertNotBetter(System.Numerics.BigInteger score, string[] key, System.Numerics.BigInteger best, string[] bestKey)
        {
            Assert.That(score > best, Is.False, "더 큰 점수의 배치가 있음: " + string.Join(",", key));
            if (score == best) Assert.That(C4BoardTests.KeyBefore(key, bestKey), Is.False, "같은 점수에서 앞서는 배치가 있음: " + string.Join(",", key));
        }
    }
}
