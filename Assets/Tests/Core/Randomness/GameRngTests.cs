using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Randomness;

namespace ZodiacSignGuardian.Core.Tests.Randomness
{
    /// <summary>
    /// 기대값은 C 기준 구현(prng.di.unimi.it)을 그대로 옮긴 별도의 파이썬 코드로 만들었다 (2026-11-16).
    /// 같은 값이 dotnet(.NET 8)과 Unity(Mono)에서 나와야 한다.
    /// </summary>
    [TestFixture]
    public class GameRngTests
    {
        static string Hex(ulong v) => v.ToString("x16");

        static IEnumerable<string> Take(GameRng rng, RngStream s, int n)
        {
            for (var i = 0; i < n; i++) yield return Hex(rng.NextUInt64(s));
        }

        // ---- 기준 출력 ----

        [Test]
        public void 시드_0의_상태는_SplitMix64의_첫_12개()
        {
            // SplitMix64(0)의 첫 값 e220a8397b1dcdaf는 기준 구현의 널리 알려진 값
            var rng = GameRng.FromSeed(0);
            Assert.That(rng.ToSaveStrings(), Is.EqualTo(new[]
            {
                "e220a8397b1dcdaf", "6e789e6aa1b965f4", "06c45d188009454f", "f88bb8a8724c81ec",
                "1b39896a51a8749b", "53cb9f0c747ea2ea", "2c829abe1f4532e1", "c584133ac916ab3c",
                "3ee5789041c98ac3", "f3b8488c368cb0a6", "657eecdd3cb13d09", "c2d326e0055bdef6",
            }));
        }

        [Test]
        public void xoshiro256별별_기준_상태_1234의_출력()
        {
            // 상태 {1,2,3,4}: 첫 출력 = rotl(2*5, 7) * 9 = 11520
            Assert.That(GameRng.TryFromSaveStrings(new[]
            {
                "0000000000000001", "0000000000000002", "0000000000000003", "0000000000000004",
                "0000000000000001", "0000000000000001", "0000000000000001", "0000000000000001",
                "0000000000000001", "0000000000000001", "0000000000000001", "0000000000000001",
            }, out var rng), Is.True);
            var outs = Enumerable.Range(0, 6).Select(_ => rng.NextUInt64(RngStream.Gacha)).ToArray();
            Assert.That(outs, Is.EqualTo(new ulong[] { 11520, 0, 1509978240, 1215971899390074240, 1216172134540287360, 607988272756665600 }));
        }

        [TestCase(0UL,
            "99ec5f36cb75f2b4,bf6e1f784956452a,1a5f849d4933e6e0,6aa594f1262d2d2c,bba5ad4a1f842e59",
            "657a983d215193d9,e4610125ff96ac53,8a9447f5e4a82f39,b44cb7ab0604b426,86c8e1135cf71683",
            "b26052cb5d869a69,26e6fd2cf2a6ff44,8f84bdd40a66a910,f2e283ff80317e0d,4c730ffefbcc4f7f")]
        [TestCase(20261116UL,
            "afc6e0782753d47d,dce3567ca1afbaff,759a1b2184283dd8,6eeecbe19d18072c,e806870eb18c5f3a",
            "469b439070b5235f,f111f18c37bf796f,fe404cccc152a592,a9a29e3b9eacdffc,f4fd849ed9cf8bc9",
            "a877b49931c59c0a,06b89246758e9a35,3346907049df61e5,04ffdbb9371f86c9,cd65499dd530fe49")]
        [TestCase(ulong.MaxValue,
            "8f5520d52a7ead08,c476a018caa1802d,81de31c0d260469e,bf658d7e065f3c2f,913593fda1bca32a",
            "1bc52aeefc73fc07,56707cbe0cd97041,561098f7a08c42e6,34e7c9408c4624fe,d07b5b582d89c416",
            "2a1e5de0262d763f,462bf9e64c40e83a,f64d52506921950c,81c5b9f3e1f48436,fc55c0ca610dc715")]
        public void 시드별_세_흐름의_기준_출력(ulong seed, string gacha, string drop, string combat)
        {
            var rng = GameRng.FromSeed(seed);
            Assert.That(Take(rng, RngStream.Gacha, 5), Is.EqualTo(gacha.Split(',')));
            Assert.That(Take(rng, RngStream.Drop, 5), Is.EqualTo(drop.Split(',')));
            Assert.That(Take(rng, RngStream.Combat, 5), Is.EqualTo(combat.Split(',')));
        }

        [Test]
        public void NextInt_기준_출력()
        {
            var rng = GameRng.FromSeed(20261116);
            Assert.That(Enumerable.Range(0, 10).Select(_ => rng.NextInt(RngStream.Gacha, 10000)), Is.EqualTo(new[] { 1341, 6719, 40, 6828, 3642, 9513, 2907, 8710, 9099, 5760 }));
            Assert.That(Enumerable.Range(0, 10).Select(_ => rng.NextInt(RngStream.Drop, 7)), Is.EqualTo(new[] { 6, 3, 5, 5, 6, 3, 4, 6, 2, 2 }));
            Assert.That(Enumerable.Range(0, 5).Select(_ => rng.NextInt(RngStream.Combat, int.MaxValue)), Is.EqualTo(new[] { 45417791, 50314946, 812417734, 1092566587, 1878757767 }));
        }

        // ---- 흐름은 서로 독립 ----

        [Test]
        public void 한_흐름을_많이_써도_다른_흐름의_결과는_같다()
        {
            var a = GameRng.FromSeed(42);
            var b = GameRng.FromSeed(42);
            for (var i = 0; i < 10000; i++) b.NextUInt64(RngStream.Combat);
            for (var i = 0; i < 37; i++) b.NextInt(RngStream.Drop, 100);
            Assert.That(Take(b, RngStream.Gacha, 20), Is.EqualTo(Take(a, RngStream.Gacha, 20)));
        }

        [Test]
        public void 흐름마다_상태가_다르다()
        {
            var rng = GameRng.FromSeed(7);
            var s = rng.ToSaveStrings();
            Assert.That(s.Distinct().Count(), Is.EqualTo(12), "같은 시드로 SplitMix를 세 번 새로 만들어 상태를 복제하지 않는다");
        }

        // ---- 범위와 확률 ----

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NextInt의_n이_0_이하면_예외(int n)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GameRng.FromSeed(1).NextInt(RngStream.Gacha, n));
        }

        [Test]
        public void NextInt_1은_항상_0이고_난수를_하나_쓴다()
        {
            var a = GameRng.FromSeed(3);
            var b = GameRng.FromSeed(3);
            Assert.That(a.NextInt(RngStream.Drop, 1), Is.EqualTo(0));
            b.NextUInt64(RngStream.Drop);
            Assert.That(a.SameState(b), Is.True);
        }

        [Test]
        public void NextInt는_범위_안이고_고르게_나온다()
        {
            var rng = GameRng.FromSeed(99);
            const int n = 10, draws = 200000;
            var counts = new int[n];
            for (var i = 0; i < draws; i++)
            {
                var v = rng.NextInt(RngStream.Combat, n);
                Assert.That(v, Is.InRange(0, n - 1));
                counts[v]++;
            }
            // 카이제곱(자유도 9) 99.9% 임계값 27.88
            double expected = draws / (double)n;
            var chi = counts.Sum(c => (c - expected) * (c - expected) / expected);
            Assert.That(chi, Is.LessThan(27.88), string.Join(",", counts));
        }

        [TestCase(-1)]
        [TestCase(10001)]
        public void 확률이_0에서_10000_밖이면_예외(int p)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GameRng.FromSeed(1).Roll(RngStream.Gacha, p));
        }

        [TestCase(0, false)]
        [TestCase(10000, true)]
        public void 확률_0과_10000도_난수를_하나_쓴다(int p, bool expected)
        {
            var a = GameRng.FromSeed(5);
            var b = GameRng.FromSeed(5);
            for (var i = 0; i < 100; i++)
            {
                Assert.That(a.Roll(RngStream.Gacha, p), Is.EqualTo(expected));
                b.NextInt(RngStream.Gacha, GameRng.ChanceScale);
            }
            Assert.That(a.SameState(b), Is.True, "p와 상관없이 NextInt(10000)을 한 번씩");
        }

        [Test]
        public void 확률은_1만분의_1_단위로_맞다()
        {
            var rng = GameRng.FromSeed(2026);
            const int draws = 200000;
            var hits = 0;
            for (var i = 0; i < draws; i++) if (rng.Roll(RngStream.Drop, 2500)) hits++;
            // 25% ± 4 표준편차
            var sd = Math.Sqrt(draws * 0.25 * 0.75);
            Assert.That(Math.Abs(hits - draws * 0.25), Is.LessThan(4 * sd), hits.ToString());
            // 경계: Roll(p)는 NextInt(10000) < p
            var a = GameRng.FromSeed(20261116);
            var first = GameRng.FromSeed(20261116).NextInt(RngStream.Gacha, 10000); // 1341
            Assert.That(a.Clone().Roll(RngStream.Gacha, first), Is.False);
            Assert.That(a.Clone().Roll(RngStream.Gacha, first + 1), Is.True);
        }

        // ---- 저장 ----

        [Test]
        public void 저장하고_불러오면_이어지는_출력이_같다()
        {
            var a = GameRng.FromSeed(123456789);
            for (var i = 0; i < 500; i++) { a.NextUInt64(RngStream.Gacha); a.NextInt(RngStream.Drop, 13); a.Roll(RngStream.Combat, 3000); }
            var saved = a.ToSaveStrings();
            Assert.That(saved.All(x => x.Length == 16 && x.All(c => "0123456789abcdef".IndexOf(c) >= 0)), Is.True);
            Assert.That(GameRng.TryFromSaveStrings(saved, out var b), Is.True);
            Assert.That(b.SameState(a), Is.True);
            foreach (RngStream s in Enum.GetValues(typeof(RngStream)))
            {
                Assert.That(Take(b, s, 50), Is.EqualTo(Take(a, s, 50)), s.ToString());
            }
        }

        [Test]
        public void 복사본은_따로_움직인다()
        {
            var a = GameRng.FromSeed(8);
            var c = a.Clone();
            Assert.That(c.SameState(a), Is.True);
            c.NextUInt64(RngStream.Gacha);
            Assert.That(c.SameState(a), Is.False);
            Assert.That(a.SameState(null), Is.False);
        }

        static string[] Good() => GameRng.FromSeed(1).ToSaveStrings().ToArray();

        [Test]
        public void 틀린_저장은_불러오지_않는다()
        {
            var cases = new List<(string why, string[] v)>
            {
                ("null", null),
                ("11개", Good().Take(11).ToArray()),
                ("13개", Good().Concat(new[] { "0000000000000001" }).ToArray()),
                ("대문자", Good().Select((x, i) => i == 0 ? x.ToUpperInvariant().Replace("0", "A") : x).ToArray()),
                ("15자리", Good().Select((x, i) => i == 5 ? x.Substring(1) : x).ToArray()),
                ("17자리", Good().Select((x, i) => i == 5 ? "0" + x : x).ToArray()),
                ("0x 앞붙이", Good().Select((x, i) => i == 2 ? "0x" + x.Substring(2) : x).ToArray()),
                ("공백", Good().Select((x, i) => i == 3 ? " " + x.Substring(1) : x).ToArray()),
                ("null 값", Good().Select((x, i) => i == 4 ? null : x).ToArray()),
                ("g", Good().Select((x, i) => i == 7 ? "g" + x.Substring(1) : x).ToArray()),
                ("드롭 흐름이 모두 0", Good().Select((x, i) => i >= 4 && i < 8 ? "0000000000000000" : x).ToArray()),
                ("전투 흐름이 모두 0", Good().Select((x, i) => i >= 8 ? "0000000000000000" : x).ToArray()),
            };
            foreach (var (why, v) in cases)
            {
                Assert.That(GameRng.TryFromSaveStrings(v, out var rng), Is.False, why);
                Assert.That(rng, Is.Null, why);
            }
            // 네 값 중 일부만 0이면 괜찮다
            var partial = Good().Select((x, i) => i >= 4 && i < 7 ? "0000000000000000" : x).ToArray();
            Assert.That(GameRng.TryFromSaveStrings(partial, out _), Is.True);
        }

        [Test]
        public void 정의되지_않은_흐름은_예외()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GameRng.FromSeed(1).NextUInt64((RngStream)3));
            Assert.Throws<ArgumentOutOfRangeException>(() => GameRng.FromSeed(1).NextUInt64((RngStream)(-1)));
        }

        [Test]
        public void 흐름_번호는_저장_순서와_같다()
        {
            Assert.That((int)RngStream.Gacha, Is.EqualTo(0));
            Assert.That((int)RngStream.Drop, Is.EqualTo(1));
            Assert.That((int)RngStream.Combat, Is.EqualTo(2));
            Assert.That(Enum.GetValues(typeof(RngStream)).Length, Is.EqualTo(GameRng.StreamCount));
        }
    }
}
