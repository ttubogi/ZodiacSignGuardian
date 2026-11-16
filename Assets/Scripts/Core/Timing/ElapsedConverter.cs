using System;

namespace ZodiacSignGuardian.Core.Timing
{
    /// <summary>
    /// elapsedSec(double 초)를 정수 마이크로초로 바꾼다. (설계서 결정 기록 2026-11-16, Codex 검토)
    /// <list type="bullet">
    /// <item>가장 가까운 정수로 반올림하고(같은 거리면 짝수 쪽), 1µs 미만의 남는 값은 다음 호출로 넘긴다.
    ///       그래서 1/60초×60번, 0.05초×20번, 0.1초×10번, 1초×1번이 모두 정확히 1,000,000µs다.
    ///       (호출마다 따로 반올림하면 1/60초×60번이 1,000,020µs가 되어 "나눠 넣어도 같다"는 약속이 깨짐)</item>
    /// <item>그 밖에 나눈 경우는 합계가 1µs 이내로 같다.</item>
    /// <item>남는 값은 저장하지 않는다. 불러오거나 복귀하면 0에서 다시 시작하므로 그때마다 1µs 미만이 사라질 수 있다.</item>
    /// </list>
    /// </summary>
    public sealed class ElapsedConverter
    {
        /// <summary>한 번에 받는 elapsedSec의 상한(약 31.7년). 넘으면 잘못된 입력이다.</summary>
        public const double MaxElapsedSec = 1e9;

        double _residualUs;

        /// <summary>남은 값 (|값| &lt;= 0.5µs). 테스트용.</summary>
        public double ResidualUs => _residualUs;

        /// <summary>음수·NaN·무한·상한 초과가 아니면 true. (InvalidInput 판단)</summary>
        public static bool IsValid(double elapsedSec)
        {
            return !double.IsNaN(elapsedSec) && !double.IsInfinity(elapsedSec) && elapsedSec >= 0.0 && elapsedSec <= MaxElapsedSec;
        }

        /// <summary>IsValid를 통과한 값만 받는다(아니면 ArgumentOutOfRangeException). 0 이상의 마이크로초를 돌려준다.</summary>
        public long Convert(double elapsedSec)
        {
            if (!IsValid(elapsedSec)) throw new ArgumentOutOfRangeException(nameof(elapsedSec), elapsedSec, "IsValid를 먼저 확인해야 함");
            var x = elapsedSec * 1e6 + _residualUs;
            var n = Math.Round(x, MidpointRounding.ToEven);
            if (n < 0) n = 0; // x >= -0.5라 반올림하면 0 이상이지만, -0.0 등을 막는다
            _residualUs = x - n;
            return (long)n;
        }

        /// <summary>남는 값을 버린다 (불러오기·복귀 때).</summary>
        public void Reset() => _residualUs = 0.0;
    }
}
