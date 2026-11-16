using System;
using System.Globalization;

namespace ZodiacSignGuardian.Core.Numbers
{
    /// <summary>
    /// 별가루·피해처럼 아주 커질 수 있는 수. 가수 × 10^지수로 나타낸다. (설계서 v0.7 1절, C2 결정 Q1 2026-11-16)
    /// <list type="bullet">
    /// <item>가수(Mantissa)는 0이거나 절댓값이 1 이상 10 미만이다. 0은 (0, 0) 하나뿐이다.</item>
    /// <item>지수(Exponent)는 long이라 크기 제한이 사실상 없다. long을 넘으면 OverflowException.</item>
    /// <item>같은 결과를 보장하려고 더하기·빼기·곱하기·나누기·비교만 쓴다. Math.Pow·Log처럼 기기마다 결과가 다를 수 있는 함수는 쓰지 않는다.</item>
    /// <item>가수는 계산마다 유효 숫자 15자리로 반올림한다(SignificantDigits). 15자리 이하의 수는 정확하다. 지수 차이가 MaxDigitGap보다 큰 두 수를 더하면 작은 쪽은 사라진다.</item>
    /// </list>
    /// 표현 방식을 바꿔야 하면 이 타입 안에서만 바꾼다. 화면 표시 형식(1.2만 등)은 View가 정한다.
    /// </summary>
    public readonly struct GameNumber : IEquatable<GameNumber>, IComparable<GameNumber>
    {
        /// <summary>더할 때 이보다 지수 차이가 크면 작은 쪽을 버린다. (유효 숫자 15자리 밖이라 반올림으로 어차피 사라짐)</summary>
        public const int MaxDigitGap = 15;

        /// <summary>
        /// 가수의 유효 숫자. 계산할 때마다 가수를 이 자리에서 반올림한다. 그래서 15자리 이하의 수(정수 포함)는
        /// 표현이 하나뿐이고, 21 + 3 == 24, 21 / 3 == 7처럼 정확히 같아진다. (반올림하지 않으면 24.000000000000004)
        /// </summary>
        public const int SignificantDigits = 15;

        const int TableMax = 308;
        const double RoundScale = 1e14; // 10^(SignificantDigits - 1)

        // 10^0 ~ 10^308. 컴파일러가 소스의 상수를 가장 가까운 double로 바꿔 넣으므로 어느 기기에서도 같고,
        // 각 값이 참값에 가장 가깝다. (실행 중에 10을 곱해 만들면 오차가 쌓여 1e300이 지수 299로 맞춰졌음)
        static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22, 1e23,
            1e24, 1e25, 1e26, 1e27, 1e28, 1e29, 1e30, 1e31, 1e32, 1e33, 1e34, 1e35,
            1e36, 1e37, 1e38, 1e39, 1e40, 1e41, 1e42, 1e43, 1e44, 1e45, 1e46, 1e47,
            1e48, 1e49, 1e50, 1e51, 1e52, 1e53, 1e54, 1e55, 1e56, 1e57, 1e58, 1e59,
            1e60, 1e61, 1e62, 1e63, 1e64, 1e65, 1e66, 1e67, 1e68, 1e69, 1e70, 1e71,
            1e72, 1e73, 1e74, 1e75, 1e76, 1e77, 1e78, 1e79, 1e80, 1e81, 1e82, 1e83,
            1e84, 1e85, 1e86, 1e87, 1e88, 1e89, 1e90, 1e91, 1e92, 1e93, 1e94, 1e95,
            1e96, 1e97, 1e98, 1e99, 1e100, 1e101, 1e102, 1e103, 1e104, 1e105, 1e106, 1e107,
            1e108, 1e109, 1e110, 1e111, 1e112, 1e113, 1e114, 1e115, 1e116, 1e117, 1e118, 1e119,
            1e120, 1e121, 1e122, 1e123, 1e124, 1e125, 1e126, 1e127, 1e128, 1e129, 1e130, 1e131,
            1e132, 1e133, 1e134, 1e135, 1e136, 1e137, 1e138, 1e139, 1e140, 1e141, 1e142, 1e143,
            1e144, 1e145, 1e146, 1e147, 1e148, 1e149, 1e150, 1e151, 1e152, 1e153, 1e154, 1e155,
            1e156, 1e157, 1e158, 1e159, 1e160, 1e161, 1e162, 1e163, 1e164, 1e165, 1e166, 1e167,
            1e168, 1e169, 1e170, 1e171, 1e172, 1e173, 1e174, 1e175, 1e176, 1e177, 1e178, 1e179,
            1e180, 1e181, 1e182, 1e183, 1e184, 1e185, 1e186, 1e187, 1e188, 1e189, 1e190, 1e191,
            1e192, 1e193, 1e194, 1e195, 1e196, 1e197, 1e198, 1e199, 1e200, 1e201, 1e202, 1e203,
            1e204, 1e205, 1e206, 1e207, 1e208, 1e209, 1e210, 1e211, 1e212, 1e213, 1e214, 1e215,
            1e216, 1e217, 1e218, 1e219, 1e220, 1e221, 1e222, 1e223, 1e224, 1e225, 1e226, 1e227,
            1e228, 1e229, 1e230, 1e231, 1e232, 1e233, 1e234, 1e235, 1e236, 1e237, 1e238, 1e239,
            1e240, 1e241, 1e242, 1e243, 1e244, 1e245, 1e246, 1e247, 1e248, 1e249, 1e250, 1e251,
            1e252, 1e253, 1e254, 1e255, 1e256, 1e257, 1e258, 1e259, 1e260, 1e261, 1e262, 1e263,
            1e264, 1e265, 1e266, 1e267, 1e268, 1e269, 1e270, 1e271, 1e272, 1e273, 1e274, 1e275,
            1e276, 1e277, 1e278, 1e279, 1e280, 1e281, 1e282, 1e283, 1e284, 1e285, 1e286, 1e287,
            1e288, 1e289, 1e290, 1e291, 1e292, 1e293, 1e294, 1e295, 1e296, 1e297, 1e298, 1e299,
            1e300, 1e301, 1e302, 1e303, 1e304, 1e305, 1e306, 1e307, 1e308,
        };

        public static readonly GameNumber Zero = default;
        public static readonly GameNumber One = new GameNumber(1.0, 0);

        public double Mantissa { get; }
        public long Exponent { get; }

        GameNumber(double mantissa, long exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        public bool IsZero => Mantissa == 0.0;
        public bool IsNegative => Mantissa < 0.0;
        public int Sign => Mantissa > 0.0 ? 1 : Mantissa < 0.0 ? -1 : 0;

        // ---- 만들기 ----

        public static GameNumber From(long value) => Normalize(value, 0);

        /// <summary>유한한 double만 받는다. NaN·무한은 ArgumentException.</summary>
        public static GameNumber From(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("GameNumber는 유한한 수만 받음: " + value, nameof(value));
            return Normalize(value, 0);
        }

        /// <summary>가수 × 10^지수. 가수가 1~10 밖이어도 맞춰 준다.</summary>
        public static GameNumber From(double mantissa, long exponent)
        {
            if (double.IsNaN(mantissa) || double.IsInfinity(mantissa)) throw new ArgumentException("가수는 유한한 수만 받음: " + mantissa, nameof(mantissa));
            return Normalize(mantissa, exponent);
        }

        /// <summary>가수를 1 이상 10 미만(또는 0)으로 맞춘다. 반복 횟수는 지수 크기가 아니라 double 범위(최대 수십 번)로 정해진다.</summary>
        static GameNumber Normalize(double m, long e)
        {
            if (m == 0.0) return Zero; // -0.0도 여기서 0
            var a = m < 0 ? -m : m;
            checked
            {
                while (a >= 10.0)
                {
                    var k = HighestPowerAtMost(a);
                    if (k == 0) k = 1;
                    a /= Pow10[k];
                    m /= Pow10[k];
                    e += k;
                }
                while (a < 1.0)
                {
                    // a * 10^k >= 1 이 되는 가장 작은 k (한 번에 최대 308)
                    var k = LowestPowerToReachOne(a);
                    a *= Pow10[k];
                    m *= Pow10[k];
                    e -= k;
                }
                // 나누기·곱하기의 반올림으로 10.0이 되거나 1 아래로 내려가는 경우를 다시 맞춘다
                if (a >= 10.0) { m /= 10.0; e += 1; }
                else if (a < 1.0) { m *= 10.0; e -= 1; }

                // 유효 숫자 15자리로 반올림. |m| * 1e14는 1e14 ~ 1e15 사이 정수 범위라 반올림이 정확하다.
                // 반올림해서 10.0이 되면 (1, 지수 + 1)로.
                m = Math.Round(m * RoundScale) / RoundScale;
                if (m >= 10.0 || m <= -10.0) { m = m > 0 ? 1.0 : -1.0; e += 1; }
            }
            return new GameNumber(m, e);
        }

        // 10^k <= a 인 가장 큰 k (0 <= k <= 308). a >= 1.
        static int HighestPowerAtMost(double a)
        {
            int lo = 0, hi = TableMax;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Pow10[mid] <= a) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // a * 10^k >= 1 인 가장 작은 k (1 <= k <= 308). 0 < a < 1. 아주 작은 수(비정규 수)는 308을 돌려주고 바깥 반복이 이어간다.
        static int LowestPowerToReachOne(double a)
        {
            int lo = 1, hi = TableMax;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (a * Pow10[mid] >= 1.0) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        // ---- 계산 ----

        public static GameNumber operator -(GameNumber a) => a.IsZero ? a : new GameNumber(-a.Mantissa, a.Exponent);

        public static GameNumber operator +(GameNumber a, GameNumber b)
        {
            if (a.IsZero) return b;
            if (b.IsZero) return a;
            if (a.Exponent < b.Exponent) { var t = a; a = b; b = t; }
            // a.Exponent >= b.Exponent. 두 지수가 long 양 끝이면 빼기가 넘치므로 먼저 거른다.
            if (b.Exponent < a.Exponent - MaxDigitGap) return a;
            var gap = (int)(a.Exponent - b.Exponent);
            // 큰 쪽의 자리(소수 14자리)에서 먼저 반올림한 뒤 맞춘다. 맞춘 뒤에만 반올림하면 비슷한 두 수를 뺄 때
            // 가수의 이진 오차가 그대로 커져 100000000000001 - 100000000000000이 0.9992가 됐음.
            // |raw| < 20이라 raw * 1e14 < 2e15로 정수가 정확한 범위다.
            var raw = a.Mantissa + b.Mantissa / Pow10[gap];
            raw = Math.Round(raw * RoundScale) / RoundScale;
            return Normalize(raw, a.Exponent);
        }

        public static GameNumber operator -(GameNumber a, GameNumber b) => a + -b;

        public static GameNumber operator *(GameNumber a, GameNumber b)
        {
            if (a.IsZero || b.IsZero) return Zero;
            return Normalize(a.Mantissa * b.Mantissa, checked(a.Exponent + b.Exponent));
        }

        public static GameNumber operator /(GameNumber a, GameNumber b)
        {
            if (b.IsZero) throw new DivideByZeroException("GameNumber를 0으로 나눔");
            if (a.IsZero) return Zero;
            return Normalize(a.Mantissa / b.Mantissa, checked(a.Exponent - b.Exponent));
        }

        public static GameNumber operator *(GameNumber a, long b) => a * From(b);
        public static GameNumber operator /(GameNumber a, long b) => a / From(b);

        // ---- 비교 ----

        public int CompareTo(GameNumber other)
        {
            if (Sign != other.Sign) return Sign.CompareTo(other.Sign);
            if (Sign == 0) return 0;
            // 같은 부호: 절댓값을 비교한 뒤 음수면 뒤집는다
            int abs = Exponent != other.Exponent
                ? Exponent.CompareTo(other.Exponent)
                : Math.Abs(Mantissa).CompareTo(Math.Abs(other.Mantissa));
            return Sign > 0 ? abs : -abs;
        }

        public static bool operator <(GameNumber a, GameNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(GameNumber a, GameNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(GameNumber a, GameNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(GameNumber a, GameNumber b) => a.CompareTo(b) >= 0;
        public static bool operator ==(GameNumber a, GameNumber b) => a.Equals(b);
        public static bool operator !=(GameNumber a, GameNumber b) => !a.Equals(b);

        public static GameNumber Max(GameNumber a, GameNumber b) => a >= b ? a : b;
        public static GameNumber Min(GameNumber a, GameNumber b) => a <= b ? a : b;

        /// <summary>가수·지수가 정확히 같을 때만 같다. (맞춘 표현이 하나뿐이므로 값이 같으면 표현도 같다)</summary>
        public bool Equals(GameNumber other) => Mantissa.Equals(other.Mantissa) && Exponent == other.Exponent;
        public override bool Equals(object obj) => obj is GameNumber g && Equals(g);
        public override int GetHashCode() => Mantissa.GetHashCode() * 397 ^ Exponent.GetHashCode();

        // ---- 바꾸기 ----

        /// <summary>double로 바꾼다. 범위를 넘으면 ±무한 또는 0. 테스트·표시용이며 판정 계산에는 쓰지 않는다.</summary>
        public double ToDouble()
        {
            if (IsZero) return 0.0;
            if (Exponent > TableMax) return Mantissa > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (Exponent >= 0) return Mantissa * Pow10[Exponent];
            if (Exponent >= -TableMax) return Mantissa / Pow10[-Exponent];
            if (Exponent >= -2 * TableMax) return Mantissa / Pow10[TableMax] / Pow10[-Exponent - TableMax];
            return 0.0;
        }

        /// <summary>
        /// 저장용 문자열 "가수e지수" (예: "1.5e3", "-2.25e-7", "0e0").
        /// 가수는 언제나 (15자리 정수 N) / 10^14에 가장 가까운 double이므로, N을 정수로 바꿔 쓰고 읽는다.
        /// double을 십진 문자열로 쓰고 읽지 않는 이유: Unity(Mono)의 double 읽기는 가장 가까운 값을 보장하지 않아
        /// "-4.00469920365359"가 한 비트 다른 값으로 읽혔다 (2026-11-16 Unity Test Runner에서 확인). 정수 읽기와
        /// 나누기 한 번은 어느 기기에서도 같다.
        /// </summary>
        public string ToSaveString()
        {
            if (IsZero) return "0e0";
            var n = (long)Math.Round(Math.Abs(Mantissa) * RoundScale); // 1e14 이상 1e15 미만의 정수
            var digits = n.ToString(CultureInfo.InvariantCulture);
            var frac = digits.Substring(1).TrimEnd('0');
            return (Mantissa < 0 ? "-" : "") + digits[0] + (frac.Length > 0 ? "." + frac : "") + "e" + Exponent.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// ToSaveString의 반대. 형식: [-]숫자[.숫자 1~14개]e[-]정수. 첫 숫자는 1~9 (0은 "0e0"만).
        /// 공백·'+'·지수 표기 가수·15자리를 넘는 가수는 받지 않는다.
        /// </summary>
        public static bool TryParseSave(string text, out GameNumber value)
        {
            value = Zero;
            if (string.IsNullOrEmpty(text)) return false;
            if (text == "0e0") return true;
            var at = text.IndexOf('e');
            if (at <= 0 || at == text.Length - 1) return false;
            if (!TryParseExponent(text.Substring(at + 1), out var e)) return false;

            var i = 0;
            var negative = text[0] == '-';
            if (negative) i = 1;
            // 첫 숫자 1~9
            if (i >= at || text[i] < '1' || text[i] > '9') return false;
            long n = text[i] - '0';
            i++;
            var fracDigits = 0;
            if (i < at)
            {
                if (text[i] != '.') return false;
                i++;
                if (i >= at) return false; // "1.e3"
                for (; i < at; i++)
                {
                    var c = text[i];
                    if (c < '0' || c > '9') return false;
                    if (++fracDigits > SignificantDigits - 1) return false;
                    n = n * 10 + (c - '0');
                }
            }
            for (var k = fracDigits; k < SignificantDigits - 1; k++) n *= 10; // 15자리 정수로
            var m = n / RoundScale; // 정수(2^53 미만) / 1e14: 어느 기기에서도 같은 가장 가까운 double
            value = new GameNumber(negative ? -m : m, e);
            return true;
        }

        static bool TryParseExponent(string s, out long e)
        {
            e = 0;
            var i = 0;
            var negative = false;
            if (s.Length > 0 && s[0] == '-') { negative = true; i = 1; }
            if (i >= s.Length) return false;
            // long.MinValue까지 받으려고 음수 쪽으로 쌓는다
            long acc = 0;
            for (; i < s.Length; i++)
            {
                var c = s[i];
                if (c < '0' || c > '9') return false;
                var d = c - '0';
                if (acc < (long.MinValue + d) / 10) return false; // 넘침
                acc = acc * 10 - d;
            }
            if (!negative)
            {
                if (acc == long.MinValue) return false;
                acc = -acc;
            }
            e = acc;
            return true;
        }

        /// <summary>읽기 쉬운 디버그 표시 (예: "1.5e3"). 저장에는 ToSaveString을 쓴다.</summary>
        public override string ToString()
        {
            return ToSaveString();
        }
    }
}
