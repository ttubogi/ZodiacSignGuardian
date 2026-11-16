using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Numbers;

namespace ZodiacSignGuardian.Core.Tests.Numbers
{
    [TestFixture]
    public class GameNumberTests
    {
        static void AssertNormalized(GameNumber g, string what = null)
        {
            if (g.IsZero)
            {
                Assert.That(g.Exponent, Is.EqualTo(0), (what ?? g.ToString()) + ": 0의 지수는 0");
                Assert.That(BitConverter.DoubleToInt64Bits(g.Mantissa), Is.EqualTo(0L), (what ?? g.ToString()) + ": 0은 +0.0");
                return;
            }
            var a = Math.Abs(g.Mantissa);
            Assert.That(a, Is.GreaterThanOrEqualTo(1.0).And.LessThan(10.0), (what ?? g.ToString()) + ": 가수는 1 이상 10 미만");
        }

        static void AssertClose(GameNumber g, double expected, double relative = 1e-13)
        {
            var actual = g.ToDouble();
            if (expected == 0) { Assert.That(actual, Is.EqualTo(0.0)); return; }
            Assert.That(Math.Abs(actual - expected) / Math.Abs(expected), Is.LessThan(relative), g + " ≈ " + expected.ToString("R"));
        }

        // ---- 만들기와 맞추기 ----

        [TestCase(0L, 0.0, 0L)]
        [TestCase(1L, 1.0, 0L)]
        [TestCase(9L, 9.0, 0L)]
        [TestCase(10L, 1.0, 1L)]
        [TestCase(99L, 9.9, 1L)]
        [TestCase(100L, 1.0, 2L)]
        [TestCase(123456789L, 1.23456789, 8L)]
        [TestCase(-50L, -5.0, 1L)]
        [TestCase(long.MaxValue, 9.22337203685478, 18L)] // 15자리로 반올림
        [TestCase(long.MinValue, -9.22337203685478, 18L)]
        public void 정수를_가수와_지수로_맞춘다(long value, double mantissa, long exponent)
        {
            var g = GameNumber.From(value);
            Assert.That(g.Mantissa, Is.EqualTo(mantissa));
            Assert.That(g.Exponent, Is.EqualTo(exponent));
            AssertNormalized(g);
        }

        [TestCase(0.5, 5.0, -1L)]
        [TestCase(0.25, 2.5, -1L)]
        [TestCase(1e300, 1.0, 300L)]
        [TestCase(-1.5e-10, -1.5, -10L)]
        [TestCase(double.MaxValue, 1.79769313486232, 308L)]
        public void 소수를_가수와_지수로_맞춘다(double value, double mantissa, long exponent)
        {
            var g = GameNumber.From(value);
            Assert.That(g.Exponent, Is.EqualTo(exponent));
            Assert.That(g.Mantissa, Is.EqualTo(mantissa).Within(1e-15).Percent);
            AssertNormalized(g);
        }

        [Test]
        public void 십의_거듭제곱은_모두_가수_1이고_double로_정확히_돌아온다()
        {
            // 10^k를 실행 중에 곱해 만들면 오차가 쌓여 큰 k에서 가수가 1이 아니거나 ToDouble이 어긋난다
            for (var k = 0; k <= 308; k++)
            {
                var literal = double.Parse("1e" + k, System.Globalization.CultureInfo.InvariantCulture);
                var g = GameNumber.From(literal);
                Assert.That(g, Is.EqualTo(GameNumber.From(1.0, k)), "1e" + k);
                Assert.That(GameNumber.From(1.0, k).ToDouble(), Is.EqualTo(literal), "1e" + k + " ToDouble");
                Assert.That(GameNumber.From(1.0, -k).ToDouble(), Is.EqualTo(1.0 / literal).Within(1).Ulps, "1e-" + k + " ToDouble");
            }
        }

        [Test]
        public void 아주_작은_수도_맞춘다()
        {
            var tiny = GameNumber.From(double.Epsilon); // 4.94e-324 (비정규 수)
            AssertNormalized(tiny);
            Assert.That(tiny.Exponent, Is.EqualTo(-324));
            var min = GameNumber.From(double.MinValue);
            AssertNormalized(min);
            Assert.That(min.Exponent, Is.EqualTo(308));
        }

        [Test]
        public void 음의_0은_0이다()
        {
            var g = GameNumber.From(-0.0);
            Assert.That(g, Is.EqualTo(GameNumber.Zero));
            Assert.That(g.IsZero && !g.IsNegative && g.Sign == 0, Is.True);
            AssertNormalized(g);
            AssertNormalized(-GameNumber.Zero, "-0");
            Assert.That(GameNumber.From(0.0, 12345), Is.EqualTo(GameNumber.Zero));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void 유한하지_않은_수는_받지_않는다(double bad)
        {
            Assert.Throws<ArgumentException>(() => GameNumber.From(bad));
            Assert.Throws<ArgumentException>(() => GameNumber.From(bad, 3));
        }

        [Test]
        public void 범위_밖_가수도_맞춰_준다()
        {
            var g = GameNumber.From(12345.0, 10);
            Assert.That(g.Mantissa, Is.EqualTo(1.2345));
            Assert.That(g.Exponent, Is.EqualTo(14));
            var h = GameNumber.From(0.003, -5);
            AssertNormalized(h);
            Assert.That(h.Exponent, Is.EqualTo(-8));
        }

        // ---- 더하기·빼기 ----

        [Test]
        public void 자리올림()
        {
            Assert.That(GameNumber.From(9) + GameNumber.From(1), Is.EqualTo(GameNumber.From(10)));
            Assert.That(GameNumber.From(5000) + GameNumber.From(5000), Is.EqualTo(GameNumber.From(10000)));
            Assert.That(GameNumber.From(9.5, 100) + GameNumber.From(5, 99), Is.EqualTo(GameNumber.From(1.0, 101)));
        }

        [Test]
        public void 지수_차이가_크면_작은_쪽은_사라진다()
        {
            var big = GameNumber.From(1.0, 100);
            var small = GameNumber.From(9.0, 100 - GameNumber.MaxDigitGap - 1);
            Assert.That(big + small, Is.EqualTo(big));
            Assert.That(small + big, Is.EqualTo(big));
            Assert.That(big - small, Is.EqualTo(big));
            // 차이가 MaxDigitGap 이하면 더해진다 (double 정밀도 안에서)
            var near = GameNumber.From(5.0, 100 - 10);
            Assert.That(big + near, Is.GreaterThan(big));
        }

        [Test]
        public void 지수가_long_양_끝이어도_더하기가_넘치지_않는다()
        {
            var huge = GameNumber.From(1.0, long.MaxValue);
            var tiny = GameNumber.From(1.0, long.MinValue);
            Assert.That(huge + tiny, Is.EqualTo(huge));
            Assert.That(tiny + huge, Is.EqualTo(huge));
            Assert.That(tiny - huge, Is.EqualTo(-huge));
        }

        [Test]
        public void 같은_수를_빼면_정확히_0()
        {
            var x = GameNumber.From(3.14159, 42);
            Assert.That(x - x, Is.EqualTo(GameNumber.Zero));
            Assert.That(x + -x, Is.EqualTo(GameNumber.Zero));
        }

        [Test]
        public void 거의_같은_수를_빼도_정확하다()
        {
            Assert.That(GameNumber.From(100000000000001L) - GameNumber.From(100000000000000L), Is.EqualTo(GameNumber.One));
            Assert.That(GameNumber.From(1.00000000000001) - GameNumber.One, Is.EqualTo(GameNumber.From(1.0, -14)));
            Assert.That(GameNumber.From(999999999999999L) - GameNumber.From(999999999999998L), Is.EqualTo(GameNumber.One));
            // 15자리 밖의 차이는 반올림으로 사라진다
            Assert.That(GameNumber.From(1.0000000000000002), Is.EqualTo(GameNumber.One));
        }

        // ---- 15자리 이하의 수는 정확하다 (C2, 2026-11-16) ----

        [Test]
        public void 정수_더하기_빼기_곱하기_나누기가_정확하다()
        {
            Assert.That(GameNumber.From(21) + GameNumber.From(3), Is.EqualTo(GameNumber.From(24)), "반올림 전에는 24.000000000000004");
            Assert.That(GameNumber.From(21) / GameNumber.From(3), Is.EqualTo(GameNumber.From(7)), "반올림 전에는 7.000000000000001");
            Assert.That(GameNumber.From(0.1) + GameNumber.From(0.2), Is.EqualTo(GameNumber.From(0.3)));
            var rng = new Random(15);
            for (var i = 0; i < 20000; i++)
            {
                long a = rng.Next(0, 1 << 30) * (long)rng.Next(0, 1 << 15); // < 2^45 (약 3.5e13)
                long b = rng.Next(0, 1 << 30);
                if (rng.Next(3) == 0) b = -b;
                GameNumber ga = GameNumber.From(a), gb = GameNumber.From(b);
                Assert.That(ga + gb, Is.EqualTo(GameNumber.From(a + b)), a + " + " + b);
                Assert.That(ga - gb, Is.EqualTo(GameNumber.From(a - b)), a + " - " + b);
                long c = rng.Next(1, 30000);
                long small = a % 30000000000L; // 곱이 15자리를 넘지 않게
                Assert.That(GameNumber.From(small) * GameNumber.From(c), Is.EqualTo(GameNumber.From(small * c)), small + " * " + c);
                Assert.That(GameNumber.From(small * c) / GameNumber.From(c), Is.EqualTo(GameNumber.From(small)), small * c + " / " + c);
            }
        }

        [Test]
        public void 같은_수를_여러_번_더해도_오차가_쌓이지_않는다()
        {
            // 별가루를 1씩 100만 번 받은 것과 한 번에 100만 받은 것이 같다
            var sum = GameNumber.Zero;
            var one = GameNumber.One;
            for (var i = 0; i < 1000000; i++) sum += one;
            Assert.That(sum, Is.EqualTo(GameNumber.From(1000000)));
            var tenth = GameNumber.From(0.1);
            var t = GameNumber.Zero;
            for (var i = 0; i < 1000; i++) t += tenth;
            Assert.That(t, Is.EqualTo(GameNumber.From(100)));
        }

        [Test]
        public void 반올림해_10이_되면_지수가_오른다()
        {
            var g = GameNumber.From(9.999999999999999);
            Assert.That(g, Is.EqualTo(GameNumber.From(10)));
            AssertNormalized(g);
            Assert.That(GameNumber.From(9.99999999999999), Is.Not.EqualTo(GameNumber.From(10)), "15자리 안쪽은 그대로");
        }

        [Test]
        public void 작은_수에서_큰_수를_빼면_음수()
        {
            var d = GameNumber.From(3) - GameNumber.From(5);
            Assert.That(d, Is.EqualTo(GameNumber.From(-2)));
            Assert.That(d.IsNegative, Is.True);
        }

        // ---- 곱하기·나누기 ----

        [Test]
        public void 곱하기와_나누기()
        {
            Assert.That(GameNumber.From(4) * GameNumber.From(5), Is.EqualTo(GameNumber.From(20)));
            Assert.That(GameNumber.From(1.0, 300) * GameNumber.From(1.0, 300), Is.EqualTo(GameNumber.From(1.0, 600)));
            Assert.That(GameNumber.From(1.0, 600) / GameNumber.From(1.0, 300), Is.EqualTo(GameNumber.From(1.0, 300)));
            Assert.That(GameNumber.From(1) / GameNumber.From(4), Is.EqualTo(GameNumber.From(0.25)));
            Assert.That(GameNumber.From(-6) * GameNumber.From(3), Is.EqualTo(GameNumber.From(-18)));
            Assert.That(GameNumber.From(7) * 3L, Is.EqualTo(GameNumber.From(21)));
            Assert.That(GameNumber.From(21) / 3L, Is.EqualTo(GameNumber.From(7)));
        }

        [Test]
        public void 영과의_곱하기_나누기()
        {
            var x = GameNumber.From(5.5, 77);
            Assert.That(x * GameNumber.Zero, Is.EqualTo(GameNumber.Zero));
            Assert.That(GameNumber.Zero * x, Is.EqualTo(GameNumber.Zero));
            Assert.That(GameNumber.Zero / x, Is.EqualTo(GameNumber.Zero));
            Assert.Throws<DivideByZeroException>(() => { var _ = x / GameNumber.Zero; });
            Assert.Throws<DivideByZeroException>(() => { var _ = GameNumber.Zero / GameNumber.Zero; });
        }

        [Test]
        public void 지수가_long을_넘으면_예외()
        {
            var top = GameNumber.From(5.0, long.MaxValue);
            Assert.Throws<OverflowException>(() => { var _ = top + top; }, "자리올림으로 지수 +1");
            Assert.Throws<OverflowException>(() => { var _ = top * GameNumber.From(10); });
            var bottom = GameNumber.From(1.0, long.MinValue);
            Assert.Throws<OverflowException>(() => { var _ = bottom / GameNumber.From(10); });
            Assert.Throws<OverflowException>(() => { var _ = bottom * GameNumber.From(0.5); }, "가수를 맞추며 지수 -1");
        }

        // ---- 비교 ----

        [Test]
        public void 크기_순서()
        {
            var ordered = new[]
            {
                GameNumber.From(-1.0, 100), GameNumber.From(-99999), GameNumber.From(-9), GameNumber.From(-1), GameNumber.From(-1.0, -50),
                GameNumber.Zero, GameNumber.From(1.0, -50), GameNumber.From(0.001), GameNumber.One, GameNumber.From(9.99),
                GameNumber.From(10), GameNumber.From(1.0, 100), GameNumber.From(1.0, long.MaxValue),
            };
            var shuffled = ordered.OrderBy(x => x.GetHashCode()).ThenBy(x => x.Exponent).ToList();
            shuffled.Sort((a, b) => a.CompareTo(b));
            Assert.That(shuffled, Is.EqualTo(ordered));
            for (var i = 0; i + 1 < ordered.Length; i++)
            {
                Assert.That(ordered[i] < ordered[i + 1] && ordered[i + 1] > ordered[i] && ordered[i] <= ordered[i + 1] && ordered[i] != ordered[i + 1], Is.True, ordered[i] + " < " + ordered[i + 1]);
            }
            Assert.That(GameNumber.Max(GameNumber.From(3), GameNumber.From(5)), Is.EqualTo(GameNumber.From(5)));
            Assert.That(GameNumber.Min(GameNumber.From(-3), GameNumber.From(5)), Is.EqualTo(GameNumber.From(-3)));
        }

        // ---- 무작위로 넣어 보기: 늘 맞춘 표현이고 double 계산과 같다 ----

        [Test]
        public void 무작위_계산은_늘_맞춘_표현이고_double과_같다()
        {
            var rng = new Random(20261116); // 테스트 값을 만들 때만 쓴다
            for (var i = 0; i < 5000; i++)
            {
                double x = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-150, 150));
                double y = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-150, 150));
                if (x == 0 || y == 0) continue;
                GameNumber gx = GameNumber.From(x), gy = GameNumber.From(y);
                AssertNormalized(gx);
                var sum = gx + gy; var dif = gx - gy; var mul = gx * gy; var div = gx / gy;
                AssertNormalized(sum, "합"); AssertNormalized(dif, "차"); AssertNormalized(mul, "곱"); AssertNormalized(div, "몫");
                // 크게 상쇄되지 않는 경우만 상대 오차로 비교
                if (Math.Abs(x + y) > 1e-10 * Math.Max(Math.Abs(x), Math.Abs(y))) AssertClose(sum, x + y, 1e-9);
                AssertClose(mul, x * y, 1e-12);
                AssertClose(div, x / y, 1e-12);
                if (x.CompareTo(y) != 0) Assert.That(gx.CompareTo(gy), Is.EqualTo(x.CompareTo(y)), x + " vs " + y);
            }
        }

        // ---- 저장 문자열 ----

        [TestCase(0L, "0e0")]
        [TestCase(1500L, "1.5e3")]
        [TestCase(-25L, "-2.5e1")]
        [TestCase(123456789012345L, "1.23456789012345e14")]
        [TestCase(-999999999999999L, "-9.99999999999999e14")]
        public void 저장_문자열_모양(long value, string text)
        {
            Assert.That(GameNumber.From(value).ToSaveString(), Is.EqualTo(text));
            Assert.That(GameNumber.TryParseSave(text, out var back), Is.True);
            Assert.That(back, Is.EqualTo(GameNumber.From(value)));
        }

        [TestCase("-4.00469920365359e6762986232160741", -4.00469920365359, 6762986232160741L)]
        [TestCase("9.99999999999999e9223372036854775807", 9.99999999999999, long.MaxValue)]
        [TestCase("1e-9223372036854775808", 1.0, long.MinValue)]
        [TestCase("3.33333333333333e-1", 3.33333333333333, -1L)]
        public void 저장_문자열을_읽으면_같은_계산으로_만든_값과_비트까지_같다(string text, double mantissa, long exponent)
        {
            // Unity(Mono)에서 double 읽기가 한 비트 어긋났던 값 (2026-11-16)
            Assert.That(GameNumber.TryParseSave(text, out var v), Is.True, text);
            var made = GameNumber.From(mantissa, exponent);
            Assert.That(BitConverter.DoubleToInt64Bits(v.Mantissa), Is.EqualTo(BitConverter.DoubleToInt64Bits(made.Mantissa)));
            Assert.That(v.Exponent, Is.EqualTo(exponent));
            Assert.That(v.ToSaveString(), Is.EqualTo(text));
        }

        [Test]
        public void 저장_문자열은_정확히_되돌아온다()
        {
            var rng = new Random(7);
            var values = new List<GameNumber>
            {
                GameNumber.Zero, GameNumber.One, -GameNumber.One, GameNumber.From(double.Epsilon), GameNumber.From(double.MaxValue),
                GameNumber.From(9.99999999999999, long.MaxValue), GameNumber.From(1.00000000000001, long.MinValue),
                GameNumber.From(0.1), GameNumber.From(1.0 / 3.0),
            };
            for (var i = 0; i < 3000; i++)
            {
                var m = 1.0 + rng.NextDouble() * 9.0;
                if (m >= 10.0) continue;
                long e = ((long)rng.Next() << 32 | (uint)rng.Next()) * (rng.Next(2) == 0 ? 1 : -1);
                values.Add(GameNumber.From(rng.Next(2) == 0 ? m : -m, e));
            }
            foreach (var v in values)
            {
                var text = v.ToSaveString();
                Assert.That(GameNumber.TryParseSave(text, out var back), Is.True, text);
                Assert.That(back, Is.EqualTo(v), text);
                Assert.That(BitConverter.DoubleToInt64Bits(back.Mantissa), Is.EqualTo(BitConverter.DoubleToInt64Bits(v.Mantissa)), text + " 가수 비트");
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("e5")]
        [TestCase("1.5e")]
        [TestCase("1.5")]
        [TestCase("1.5E3")]
        [TestCase("1e2e3")]
        [TestCase("10e0")]
        [TestCase("0.5e1")]
        [TestCase("0e5")]
        [TestCase(" 1.5e3")]
        [TestCase("1.5e3 ")]
        [TestCase("NaNe0")]
        [TestCase("Infinitye0")]
        [TestCase("-Infinitye0")]
        [TestCase("1.5e99999999999999999999")]
        [TestCase("1,5e3")]
        [TestCase("1.5e3.0")]
        [TestCase("1.0000000000000002e0")]
        [TestCase("9.999999999999998e0")]
        [TestCase("1.000000000000001e0")]
        [TestCase("+1.5e3")]
        [TestCase("1.e3")]
        [TestCase("01.5e3")]
        [TestCase("0.0e0")]
        [TestCase("-0e0")]
        [TestCase("1.5e+3")]
        [TestCase("1.5e-")]
        [TestCase("1.5e9223372036854775808")]
        [TestCase("1.5e-9223372036854775809")]
        [TestCase("-")]
        [TestCase("1.5ee3")]
        public void 틀린_저장_문자열은_읽지_않는다(string text)
        {
            Assert.That(GameNumber.TryParseSave(text, out var value), Is.False, text);
            Assert.That(value, Is.EqualTo(GameNumber.Zero));
        }

        // ---- 기기마다 같은 결과 ----

        /// <summary>
        /// 같은 계산을 어느 기기에서 해도 비트까지 같은지. 기대값은 dotnet(.NET 8)에서 이 코드로 얻은 값이며,
        /// Unity(Mono)·Android(IL2CPP)에서 같은 값이 나와야 한다. 값이 달라지면 결정성이 깨진 것이다.
        /// </summary>
        [Test]
        public void 정해진_계산의_결과는_기기와_상관없이_같다()
        {
            var x = GameNumber.From(7);
            var growth = GameNumber.From(1.0377);
            var log = new List<string>();
            for (var i = 1; i <= 2000; i++)
            {
                x = x * growth + GameNumber.From(i);
                if (i % 7 == 0) x = x - GameNumber.From(i * 3);
                if (i % 13 == 0) x = x / GameNumber.From(1.5);
                if (i % 500 == 0) log.Add(x.ToSaveString());
            }
            // 지수가 아주 커지는 계산과 큰 수끼리의 뺄셈
            var z = GameNumber.From(3);
            var w = GameNumber.From(2);
            for (var i = 1; i <= 3000; i++)
            {
                z = z * GameNumber.From(123.456) + z / GameNumber.From(7);
                w = w * GameNumber.From(1.234e-3) + GameNumber.From(9.87654321, i);
                if (i % 1000 == 0) log.Add(z.ToSaveString());
                if (i % 1000 == 0) log.Add((z - z / GameNumber.From(3) * GameNumber.From(2.999)).ToSaveString());
                if (i % 1000 == 0) log.Add(w.ToSaveString());
            }
            var y = GameNumber.From(1.0001);
            for (var i = 0; i < 60; i++) y *= y;
            log.Add(y.ToSaveString());
            log.Add((GameNumber.One / y).ToSaveString());
            Assert.That(log, Is.EqualTo(Golden), string.Join(", ", log.Select(s => "\"" + s + "\"")));
        }

        static readonly string[] Golden =
        {
            "2.37781487730175e5",
            "6.54501657844694e6",
            "9.82563494164982e7",
            "2.17491237559175e9",
            "3.10153239075325e2092",
            "1.03384413026e2089",
            "9.87776212584633e1000",
            "3.20650105696381e4184",
            "1.06883368566e4181",
            "9.87776212584633e2000",
            "3.31502229638595e6276",
            "1.10500743213e6273",
            "9.87776212584633e3000",
            "3.60156493578709e50068241381684",
            "2.77657079027914e-50068241381685",
        };

        [Test]
        public void 같음과_해시()
        {
            var a = GameNumber.From(12345);
            var b = GameNumber.From(1.2345, 4);
            Assert.That(a.Equals(b) && a == b && a.GetHashCode() == b.GetHashCode(), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals("12345"), Is.False);
        }
    }
}
