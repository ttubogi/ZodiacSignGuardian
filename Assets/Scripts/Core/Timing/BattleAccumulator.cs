using System;
using System.Collections.Generic;

namespace ZodiacSignGuardian.Core.Timing
{
    /// <summary>
    /// 전투 누적기 (설계서 3절 "시간 규칙", 4절 Combat). C2에서는 0.1초 칸이 몇 번 지났는지만 센다. 실제 전투는 C5.
    /// <list type="bullet">
    /// <item>프레임마다 min(경과 µs, max_frame_sec × 1,000,000)를 더하고 0.1초(100,000µs)씩 꺼낸다.</item>
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

        /// <summary>
        /// 이번 프레임에서 0.1초 칸이 채워지는 시각들(프레임 시작부터의 µs, 오름차순)을 계산한다. 상태는 바꾸지 않는다 (설계서 v0.9 3절).
        /// <list type="bullet">
        /// <item>전투에 인정하는 시간은 프레임 경과의 마지막 부분 min(경과, maxFrameUs)이다. 그 앞부분은 전투가 없고 버린다.</item>
        /// <item>칸 하나는 채워지는 시각에 일어난 것으로 본다: j번째 칸 = (경과 − 인정) + j × 0.1초 − 남은 시간.</item>
        /// <item>칸 수는 maxFrameUs / 0.1초 + 1 이하라(최대 601) 값의 크기만큼 돌지 않는다.</item>
        /// </list>
        /// Commit(newPendingUs, discardedUs)로 확정한다.
        /// </summary>
        public List<long> Plan(long elapsedUs, long maxFrameUs, out long newPendingUs, out long discardedUs)
        {
            if (elapsedUs < 0) throw new ArgumentOutOfRangeException(nameof(elapsedUs), elapsedUs, "0 이상");
            if (maxFrameUs <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrameUs), maxFrameUs, "양수");
            var used = elapsedUs < maxFrameUs ? elapsedUs : maxFrameUs;
            discardedUs = elapsedUs - used;
            var total = PendingUs + used;
            var count = total / TickUs;
            newPendingUs = total % TickUs;
            var ticks = new List<long>((int)count);
            for (long j = 1; j <= count; j++) ticks.Add(discardedUs + j * TickUs - PendingUs);
            return ticks;
        }

        /// <summary>Plan의 결과를 확정한다.</summary>
        public void Commit(long newPendingUs, long discardedUs)
        {
            if (newPendingUs < 0 || newPendingUs >= TickUs) throw new ArgumentOutOfRangeException(nameof(newPendingUs), newPendingUs, "0 이상 0.1초 미만");
            PendingUs = newPendingUs;
            LastDiscardedUs = discardedUs;
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
