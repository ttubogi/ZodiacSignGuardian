using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Tests.Data;
using static ZodiacSignGuardian.Core.Tests.Session.C4;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>
    /// C4 별자리 판 (설계서 v0.12 3절 "별자리 판"). 1장 카시오페이아: 1-2, 2-3, 3-4, 4-5 (한 줄).
    /// 2장 시그너스: 1-2, 2-3, 2-4, 2-5 (칸 2가 가운데).
    /// </summary>
    [TestFixture]
    public class C4BoardTests
    {
        // ---- 놓기 ----

        [Test]
        public void 놓으면_BoardChanged_선이_켜지면_LinkBonusChanged_임무_1()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            var w2 = Own(h, "wood_2");
            var e = h.Ok(h.S.PlaceStar(1, 1, w1));
            Assert.That(EventText.Names(e), Is.EqualTo(new[] { "BoardChanged", "MissionProgressed" }), "선이 없으면 LinkBonusChanged 없음");
            e = h.Ok(h.S.PlaceStar(1, 2, w2));
            Assert.That(EventText.Names(e), Is.EqualTo(new[] { "BoardChanged", "LinkBonusChanged", "MissionProgressed" }));
            var link = ((LinkBonusChanged)e[1]).Links.Single();
            Assert.That(link.Link, Is.EqualTo(new BoardLink(1, 2)));
            Assert.That(link.Kind, Is.EqualTo(LinkKind.Same));
            Assert.That(h.MissionProgress("place_any"), Is.EqualTo(2));
            Assert.That(Slots(h, 1), Is.EqualTo(new[] { w1, w2, 0, 0, 0 }));
        }

        [Test]
        public void 이미_그_칸에_있으면_성공하고_이벤트와_임무가_없다()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            h.Ok(h.S.PlaceStar(1, 3, w1));
            var before = h.S.Capture();
            var e = h.Ok(h.S.PlaceStar(1, 3, w1));
            Assert.That(e, Is.Empty);
            Assert.That(h.S.Capture().Same(before), Is.True);
            Assert.That(h.MissionProgress("place_any"), Is.EqualTo(1));
        }

        [Test]
        public void 같은_판에서_옮기면_원래_칸이_빈다()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            h.Ok(h.S.PlaceStar(1, 1, w1));
            var e = h.Ok(h.S.PlaceStar(1, 5, w1));
            Assert.That(e.OfType<BoardChanged>().Count(), Is.EqualTo(1));
            Assert.That(Slots(h, 1), Is.EqualTo(new[] { 0L, 0, 0, 0, w1 }));
        }

        [Test]
        public void 다른_판의_별을_놓으면_원래_판에서_빠지고_두_판_모두_BoardChanged()
        {
            var h = New();
            h.S.SetChapterForTest(2);
            var w1 = Own(h, "wood_1");
            var w2 = Own(h, "wood_2");
            h.Ok(h.S.PlaceStar(1, 1, w1));
            h.Ok(h.S.PlaceStar(1, 2, w2));
            var e = h.Ok(h.S.PlaceStar(2, 2, w2));
            // 장 순서: 1장(빠짐, 선 꺼짐) → 2장(놓임)
            Assert.That(EventText.Names(e), Is.EqualTo(new[] { "BoardChanged", "LinkBonusChanged", "BoardChanged", "MissionProgressed" }));
            Assert.That(((BoardChanged)e[0]).Chapter, Is.EqualTo(1));
            Assert.That(((LinkBonusChanged)e[1]).Links, Is.Empty);
            Assert.That(((BoardChanged)e[2]).Chapter, Is.EqualTo(2));
            Assert.That(Slots(h, 1), Is.EqualTo(new[] { w1, 0, 0, 0, 0 }));
            Assert.That(Slots(h, 2), Is.EqualTo(new[] { 0L, w2, 0, 0, 0 }));
        }

        [Test]
        public void 차_있는_칸에_놓으면_있던_별은_판에서_빠진다()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            var f1 = Own(h, "fire_1");
            h.Ok(h.S.PlaceStar(1, 1, w1));
            h.Ok(h.S.PlaceStar(1, 1, f1));
            Assert.That(Slots(h, 1), Is.EqualTo(new[] { f1, 0, 0, 0, 0 }));
            Assert.That(h.S.OwnedStars.Any(s => s.InstanceId == w1), Is.True, "빠진 별은 가진 별로 남음");
        }

        [Test]
        public void 놓기_실패_순서는_InvalidTarget_NotUnlocked_StarNotOwned()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            h.AssertFailsUnchanged(() => h.S.PlaceStar(99, 1, 12345), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.PlaceStar(0, 1, w1), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.PlaceStar(-1, 1, w1), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.PlaceStar(int.MaxValue, 1, w1), ErrorCode.InvalidTarget);
            foreach (var slot in new[] { 0, 6, -1, int.MaxValue, int.MinValue })
            {
                h.AssertFailsUnchanged(() => h.S.PlaceStar(1, slot, w1), ErrorCode.InvalidTarget);
                h.AssertFailsUnchanged(() => h.S.PlaceStar(2, slot, 12345), ErrorCode.InvalidTarget); // 닫힌 판보다 칸이 먼저
            }
            h.AssertFailsUnchanged(() => h.S.PlaceStar(2, 1, 12345), ErrorCode.NotUnlocked);       // 닫힌 판이 가진 별보다 먼저
            h.AssertFailsUnchanged(() => h.S.PlaceStar(2, 1, w1), ErrorCode.NotUnlocked);
            foreach (var id in new[] { 0L, -1L, w1 + 1, long.MaxValue })
            {
                h.AssertFailsUnchanged(() => h.S.PlaceStar(1, 1, id), ErrorCode.StarNotOwned);
            }
        }

        [Test]
        public void 판은_그_장에_들어가면_열린다()
        {
            var h = New();
            Assert.That(h.S.BoardStatuses.Select(b => b.IsOpen), Is.EqualTo(new[] { true, false }));
            h.S.SetChapterForTest(2);
            Assert.That(h.S.BoardStatuses.Select(b => b.IsOpen), Is.EqualTo(new[] { true, true }));
            var w1 = Own(h, "wood_1");
            h.Ok(h.S.PlaceStar(2, 1, w1));
        }

        [Test]
        public void 시간_미확인이면_놓기는_되지만_임무는_세지_않는다()
        {
            var h = New(sync: false);
            var w1 = Own(h, "wood_1");
            h.Ok(h.S.PlaceStar(1, 1, w1));
            Assert.That(h.MissionProgress("place_any"), Is.EqualTo(0));
        }

        // ---- 빼기 ----

        [Test]
        public void 빼기는_빈_칸이면_NotReady_빼면_임무는_세지_않는다()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            h.AssertFailsUnchanged(() => h.S.RemoveStar(1, 1), ErrorCode.NotReady);
            h.AssertFailsUnchanged(() => h.S.RemoveStar(1, 0), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.RemoveStar(7, 1), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.RemoveStar(2, 1), ErrorCode.NotUnlocked);
            h.Ok(h.S.PlaceStar(1, 1, w1));
            var progress = h.MissionProgress("place_any");
            var e = h.Ok(h.S.RemoveStar(1, 1));
            Assert.That(EventText.Names(e), Is.EqualTo(new[] { "BoardChanged" }));
            Assert.That(Slots(h, 1), Is.EqualTo(new long[5]));
            Assert.That(h.MissionProgress("place_any"), Is.EqualTo(progress));
        }

        // ---- 완성 ----

        [Test]
        public void 다섯_칸이_차면_BoardCompleted_한_번_다시_채우면_다시()
        {
            var h = New();
            var ids = new[] { "wood_1", "fire_1", "earth_1", "metal_1", "water_1", "wood_2" }.Select(id => Own(h, id)).ToArray();
            for (var i = 0; i < 4; i++)
            {
                Assert.That(h.Ok(h.S.PlaceStar(1, i + 1, ids[i])).OfType<BoardCompleted>(), Is.Empty);
            }
            var e = h.Ok(h.S.PlaceStar(1, 5, ids[4]));
            Assert.That(e.OfType<BoardCompleted>().Single().Chapter, Is.EqualTo(1));
            Assert.That(Board(h, 1).IsComplete, Is.True);
            // 완성된 판에서 별을 바꿔도(차 있는 칸) 다시 나오지 않음
            Assert.That(h.Ok(h.S.PlaceStar(1, 5, ids[5])).OfType<BoardCompleted>(), Is.Empty);
            h.Ok(h.S.RemoveStar(1, 5));
            Assert.That(Board(h, 1).IsComplete, Is.False);
            Assert.That(h.Ok(h.S.PlaceStar(1, 5, ids[4])).OfType<BoardCompleted>().Count(), Is.EqualTo(1));
        }

        // ---- 연결 ----

        [TestCase("wood_1", "wood_2", LinkKind.Same)]
        [TestCase("wood_1", "fire_1", LinkKind.Generate)]
        [TestCase("fire_1", "earth_1", LinkKind.Generate)]
        [TestCase("earth_1", "metal_1", LinkKind.Generate)]
        [TestCase("metal_1", "water_1", LinkKind.Generate)]
        [TestCase("water_1", "wood_1", LinkKind.Generate)]
        [TestCase("fire_1", "wood_1", LinkKind.Generate)]
        [TestCase("wood_1", "earth_1", null)]
        [TestCase("wood_1", "metal_1", null)]
        [TestCase("fire_1", "metal_1", null)]
        [TestCase("fire_1", "water_1", null)]
        [TestCase("earth_1", "water_1", null)]
        public void 연결선은_같은_속성과_상생_쌍에만_켜진다(string a, string b, LinkKind? kind)
        {
            var h = New();
            h.Ok(h.S.PlaceStar(1, 3, Own(h, a)));
            h.Ok(h.S.PlaceStar(1, 4, Own(h, b)));
            var active = Board(h, 1).ActiveLinks;
            if (kind == null) Assert.That(active, Is.Empty);
            else Assert.That(active, Is.EqualTo(new[] { new ActiveLink(new BoardLink(3, 4), kind.Value) }));
        }

        [Test]
        public void 상생_순서로_한_줄이면_선_4개가_모두_켜진다()
        {
            var h = New();
            var order = new[] { "wood_1", "fire_1", "earth_1", "metal_1", "water_1" };
            for (var i = 0; i < 5; i++) h.Ok(h.S.PlaceStar(1, i + 1, Own(h, order[i])));
            Assert.That(Board(h, 1).ActiveLinks.Select(l => l.Kind), Is.EqualTo(Enumerable.Repeat(LinkKind.Generate, 4)));
        }

        // ---- 추천 배치 ----

        [Test]
        public void 추천_배치는_놓을_별이_없으면_NotReady()
        {
            var h = New();
            h.AssertFailsUnchanged(() => h.S.AutoArrange(1), ErrorCode.NotReady);
            h.AssertFailsUnchanged(() => h.S.AutoArrange(9), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.AutoArrange(2), ErrorCode.NotUnlocked);
            // 모든 별이 다른 판에 있으면 후보가 없음
            h.S.SetChapterForTest(2);
            h.Ok(h.S.PlaceStar(2, 1, Own(h, "wood_1")));
            h.AssertFailsUnchanged(() => h.S.AutoArrange(1), ErrorCode.NotReady);
        }

        [Test]
        public void 후보가_다섯보다_적으면_모두_놓는다()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            var f1 = Own(h, "fire_1");
            var e = h.Ok(h.S.AutoArrange(1));
            Assert.That(e.OfType<BoardChanged>().Count(), Is.EqualTo(1));
            var slots = Slots(h, 1);
            Assert.That(slots.Count(x => x != 0), Is.EqualTo(2));
            // 두 별을 이웃 칸에 두어 상생 선을 켠다. 같은 점수 중 칸 1부터 star_id가 작은 쪽: fire_1(칸 1), wood_1(칸 2)
            Assert.That(slots, Is.EqualTo(new[] { f1, w1, 0, 0, 0 }));
            Assert.That(h.MissionProgress("place_any"), Is.EqualTo(1));
        }

        [Test]
        public void 추천_배치는_다른_판의_별을_쓰지_않는다()
        {
            var h = New();
            h.S.SetChapterForTest(2);
            var ids = new[] { "wood_3", "fire_3", "earth_3", "water_3", "metal_3", "wood_1" }.Select(id => Own(h, id)).ToArray();
            h.Ok(h.S.PlaceStar(2, 2, ids[0]));
            h.Ok(h.S.AutoArrange(1));
            Assert.That(Slots(h, 1), Has.No.Member(ids[0]));
            Assert.That(Slots(h, 2)[1], Is.EqualTo(ids[0]));
        }

        [Test]
        public void 결과가_지금과_같으면_성공하고_이벤트가_없다()
        {
            var h = New();
            foreach (var id in new[] { "wood_1", "fire_2", "earth_3", "metal_1", "water_2", "wood_3", "fire_1" }) Own(h, id);
            h.Ok(h.S.AutoArrange(1));
            var before = h.S.Capture();
            var progress = h.MissionProgress("place_any");
            Assert.That(h.Ok(h.S.AutoArrange(1)), Is.Empty);
            Assert.That(h.S.Capture().Same(before), Is.True);
            Assert.That(h.MissionProgress("place_any"), Is.EqualTo(progress));
        }

        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(2, 4)]
        [TestCase(1, 5)]
        public void 추천_배치는_따로_계산한_가장_좋은_배치와_같다(int chapter, int seed)
        {
            // 무작위로 별과 레벨을 정하고, 테스트 안에서 따로 쓴 모든 경우 비교와 결과를 대조한다
            var rnd = new System.Random(seed);
            var h = New();
            h.S.SetChapterForTest(2);
            var all = h.S.StarDefinitions.Select(s => s.StarId).OrderBy(_ => rnd.Next()).Take(5 + rnd.Next(11)).ToList();
            foreach (var id in all) Own(h, id);
            var json = h.S.CreateSave().Json;
            foreach (var s in h.S.OwnedStars)
            {
                json = R(json, "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": 1", "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": " + (1 + rnd.Next(30)));
            }
            h.LoadJson(json);
            h.Sync(T0);
            h.Ok(h.S.AutoArrange(chapter));
            var expected = Reference(h, chapter, h.S.OwnedStars.ToList());
            Assert.That(Slots(h, chapter), Is.EqualTo(expected));
        }

        [Test]
        public void 같은_점수면_칸_1부터_star_id가_작은_배치()
        {
            // 같은 희귀도·같은 레벨·같은 속성 다섯 개는 어느 배치든 점수가 같다 → star_id 순서대로
            var t = Texts();
            var h = New(t);
            var ids = new[] { "wood_1", "fire_1", "earth_1", "metal_1", "water_1" }.Select(id => Own(h, id)).ToArray();
            // 일반 다섯 개: 속성이 다르므로 점수는 선에 따라 다르다 → 따로 계산과 같은지만 본다
            h.Ok(h.S.AutoArrange(1));
            Assert.That(Slots(h, 1), Is.EqualTo(Reference(h, 1, h.S.OwnedStars.ToList())));
            // 연결 보너스가 모두 0이면 점수가 모두 같다 → 칸 1부터 earth_1, fire_1, metal_1, water_1, wood_1
            var h0 = New(Texts().Replace("star_rules", "200,2000,3000,1000", "200,0,0,1000"));
            var byStar = new[] { "wood_1", "fire_1", "earth_1", "metal_1", "water_1" }.ToDictionary(id => id, id => Own(h0, id));
            h0.Ok(h0.S.AutoArrange(1));
            Assert.That(Slots(h0, 1), Is.EqualTo(new[] { byStar["earth_1"], byStar["fire_1"], byStar["metal_1"], byStar["water_1"], byStar["wood_1"] }));
        }

        [Test]
        public void 후보_15개와_표의_끝값에서도_넘치지_않고_따로_계산과_같다()
        {
            // skill_coef_bp 100000, skill_step_bp 10000, max_skill_level 1000, 연결 보너스 10000 (설계서 v0.12 열 범위의 끝)
            var t = Texts()
                .Replace("rarities", "legend,500,100,8000,", "legend,500,100,100000,")
                .Replace("star_rules", "gold,100,900,1,shard,30,1000,200,2000,3000,1000", "gold,100,900,1,shard,1000,10000,200,10000,10000,1000");
            var h = New(t);
            h.S.SetChapterForTest(2);
            foreach (var s in h.S.StarDefinitions) Own(h, s.StarId);
            var json = h.S.CreateSave().Json;
            foreach (var s in h.S.OwnedStars.Where(s => s.StarId.EndsWith("_3")))
            {
                json = R(json, "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": 1", "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": 1000");
            }
            h.LoadJson(json);
            h.Sync(T0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            h.Ok(h.S.AutoArrange(2)); // 가운데 칸에 선 4개
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000));
            Assert.That(Slots(h, 2), Is.EqualTo(Reference(h, 2, h.S.OwnedStars.ToList())));
        }

        /// <summary>
        /// 따로 쓴 기준 계산: 후보로 칸 5개를 채우는 모든 경우(빈 칸 포함)를 만들고 (점수 내림차순, 칸마다 star_id 오름차순·빈 칸 마지막)으로 정렬해 첫째.
        /// 점수는 BigInteger로 계산해 넘침과 상관없이 맞다. 연결은 상생 쌍 목록을 따로 둔다.
        /// </summary>
        /// <summary>빈 칸의 정렬 키: 어떤 star_id보다 뒤 (문자 0xFFFF, 숫자로 써서 파일에 보이지 않는 문자를 넣지 않음).</summary>
        static readonly string Empty = ((char)0xFFFF).ToString();

        static long[] Reference(C3Harness h, int chapter, List<OwnedStar> candidates)
        {
            var defs = h.S.StarDefinitions.ToDictionary(s => s.StarId);
            var rules = h.S.StarSettings;
            var coef = new Dictionary<Rarity, long>();
            foreach (var line in h.T[ZodiacSignGuardian.Data.Tables.Schemas.Rarities].Rows)
            {
                coef[(Rarity)Array.IndexOf(ZodiacSignGuardian.Data.Tables.Schemas.RarityIds, line.Str("rarity"))] = line.Long("skill_coef_bp");
            }
            var links = Board(h, chapter).Links;
            var pairs = new HashSet<string> { "wood>fire", "fire>earth", "earth>metal", "metal>water", "water>wood" };
            string El(OwnedStar s) => defs[s.StarId].Element.ToString().ToLowerInvariant();
            long LinkBp(OwnedStar a, OwnedStar b)
            {
                if (El(a) == El(b)) return rules.LinkSameBp;
                return pairs.Contains(El(a) + ">" + El(b)) || pairs.Contains(El(b) + ">" + El(a)) ? rules.LinkGenerateBp : 0;
            }
            var place = Math.Min(5, candidates.Count);
            var best = new List<(System.Numerics.BigInteger score, string[] key, long[] slots)>();
            void Go(int slot, OwnedStar?[] pick, HashSet<long> used)
            {
                if (slot == 5)
                {
                    if (pick.Count(p => p != null) != place) return;
                    System.Numerics.BigInteger score = 0;
                    for (var i = 0; i < 5; i++)
                    {
                        if (pick[i] == null) continue;
                        var s = pick[i].Value;
                        long bonus = 0;
                        foreach (var l in links)
                        {
                            var other = l.SlotA == i + 1 ? pick[l.SlotB - 1] : l.SlotB == i + 1 ? pick[l.SlotA - 1] : null;
                            if (other != null) bonus += LinkBp(s, other.Value);
                        }
                        var power = new System.Numerics.BigInteger(coef[defs[s.StarId].Rarity]) * (10000 + (long)rules.SkillStepBp * (s.SkillLevel - 1));
                        score += power * (10000 + bonus);
                    }
                    best.Add((score, pick.Select(p => p == null ? Empty : p.Value.StarId).ToArray(), pick.Select(p => p?.InstanceId ?? 0).ToArray()));
                    return;
                }
                foreach (var c in candidates)
                {
                    if (used.Contains(c.InstanceId)) continue;
                    used.Add(c.InstanceId);
                    pick[slot] = c;
                    Go(slot + 1, pick, used);
                    used.Remove(c.InstanceId);
                }
                pick[slot] = null;
                Go(slot + 1, pick, used);
            }
            Go(0, new OwnedStar?[5], new HashSet<long>());
            var max = best.Max(b => b.score);
            return best.Where(b => b.score == max)
                .OrderBy(b => b.key, Comparer<string[]>.Create((x, y) =>
                {
                    for (var i = 0; i < 5; i++)
                    {
                        var c = string.CompareOrdinal(x[i], y[i]);
                        if (c != 0) return c;
                    }
                    return 0;
                }))
                .First().slots;
        }
    }
}
