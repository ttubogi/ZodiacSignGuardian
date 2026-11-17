using System;

namespace ZodiacSignGuardian.Core.Timing
{
    /// <summary>
    /// 전투 누적기 (설계서 3절 "시간 규칙", 4절 Combat). C2에서는 0.1초 칸이 몇 번 지났는지만 센다. 실제 전투는 C5.
    /// <list type="bullet">
    /// <item>프레임마다 min(경과 µs, max_frame_sec)를 더하고 0.1초(100,000µs)씩 꺼낸다.</item>
    /// <item>0.1초 미만은 다음 프레임으로 남는다(저장한다). 버리는 것은 max_frame_sec를 넘는 부분뿐이다.</item>
    /// <item>보스 제한 시간·스킬 쿨타임·필살기 충전·사냥 버프도 여기서 꺼낸 시간으로만 흐른다(C5).</item>
    /// </list>
    /// </summary>
    public sealed class BattleAccumulator
    {
        public const long TickUs = 100_000L;

        /// <summary>아직 꺼내지 않은 시간(µs). 항상 0 이상 TickUs 미만.</summary>
        public long PendingUs { get; private set; }

        /// <summary>마지막 Add에서 버린 시간(µs, max_frame_sec 초과분). 테스트·기록용.</summary>
        public long LastDiscardedUs { get; private set; }

        /// <summary>경과 µs를 더하고 지난 0.1초 칸 수를 돌려준다. elapsedUs·maxFrameUs는 0 이상(아니면 예외).</summary>
        public long Add(long elapsedUs, long maxFrameUs)
        {
            if (elapsedUs < 0) throw new ArgumentOutOfRangeException(nameof(elapsedUs), elapsedUs, "0 이상");
            if (maxFrameUs <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrameUs), maxFrameUs, "양수");
            var used = elapsedUs < maxFrameUs ? elapsedUs : maxFrameUs;
            LastDiscardedUs = elapsedUs - used;
            var total = PendingUs + used; // PendingUs < 0.1초, used <= maxFrameUs라 넘치지 않음
            PendingUs = total % TickUs;
            return total / TickUs;
        }

        /// <summary>저장한 값으로. 0 이상 TickUs 미만이 아니면 false(상태는 그대로).</summary>
        public bool TryRestore(long pendingUs)
        {
            if (pendingUs < 0 || pendingUs >= TickUs) return false;
            PendingUs = pendingUs;
            LastDiscardedUs = 0;
            return true;
        }

        public bool SameState(BattleAccumulator other) => other != null && other.PendingUs == PendingUs;

        public BattleAccumulator Clone() => new BattleAccumulator { PendingUs = PendingUs, LastDiscardedUs = LastDiscardedUs };
    }
}
