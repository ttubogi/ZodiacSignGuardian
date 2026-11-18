using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Timing;
using static ZodiacSignGuardian.Core.Tests.Session.C3Harness;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>
    /// 프레임 안의 경계와 전투 칸 (설계서 v0.9 3절 "Advance 처리 순서", D5, Codex v0.8·v0.9 검토).
    /// 가짜 전투(TestKillsPerTick)로 칸마다 kill_monster를 세어 어느 날로 셌는지 본다.
    /// </summary>
    [TestFixture]
    public class TimelineTests
    {
        static C3Harness Killing(long syncMs)
        {
            var h = New();
            h.Sync(syncMs);
            h.S.TestKillsPerTick = 1;
            return h;
        }

        /// <summary>이벤트를 "P" (kill_ten 진행), "D" (DayReset), "W" (WeekReset), "S" (상태 변화)로 줄인다.</summary>
        static string Trace(IEnumerable<GameEvent> events)
        {
            return string.Concat(events.Select(e =>
                e is MissionProgressed p && p.MissionId == "kill_ten" ? "P" :
                e is DayReset ? "D" : e is WeekReset ? "W" : e is TimeStatusChanged ? "S" : ""));
        }

        [Test]
        public void 다섯시를_걸친_프레임은_앞_전투_초기화_뒤_전투이고_정각_칸은_새날()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 59, 59, 500));
            var e = h.Advance(1.0); // 칸: 59.6 .7 .8 .9 | 05:00:00.0 .1 .2 .3 .4 .5
            Assert.That(Trace(e), Is.EqualTo("PPPPDPPPPPP"));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(6));
        }

        [Test]
        public void 남은_0점09초와_다음_프레임_0점01초_사이의_다섯시는_채워지는_시각의_날()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 59, 59, 905));
            Assert.That(Trace(h.Advance(0.09)), Is.EqualTo(""), "칸이 채워지지 않음, 04:59:59.995");
            Assert.That(Trace(h.Advance(0.01)), Is.EqualTo("DP"), "05:00:00.005에 채워짐");
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(1));
        }

        [Test]
        public void 남은_0점09초를_저장하고_불러온_뒤_0점01초면_한_번만_처리()
        {
            var h = Killing(KstMs(2026, 11, 18, 12, 0, 0));
            h.Advance(0.09);
            h.Restart();
            h.Sync(KstMs(2026, 11, 18, 12, 0, 1));
            h.Advance(0.01);
            Assert.That(h.S.LastAdvanceTicks, Is.EqualTo(1));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(1));
        }

        [Test]
        public void 전투에_인정하지_않은_앞부분에서_다섯시를_지나도_초기화하고_전투는_마지막_1초만()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 59, 58));
            var e = h.Advance(5.0); // 05:00은 2초 뒤, 전투는 마지막 1초
            Assert.That(Trace(e), Is.EqualTo("D" + new string('P', 10)));
            Assert.That(h.S.Battle.LastDiscardedUs, Is.EqualTo(4_000_000));
        }

        [Test]
        public void 긴_프레임에서_다섯시_통과_유효_끝_마지막_1초_전투면_초기화는_하고_임무는_세지_않는다()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 55, 0));
            var e = h.Advance(20 * 60);
            Assert.That(Trace(e), Is.EqualTo("DS"));
            Assert.That(h.S.TimeStatus, Is.EqualTo(TimeStatus.NeedsResync));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(0));
        }

        [Test]
        public void 다섯시와_유효_끝이_같은_시각이면_그_칸은_유효한_새날_칸이고_뒤는_무효()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 50, 0));
            h.S.TestKillsPerTick = 0;
            h.Advance(599.5); // 04:59:59.5, 동기화 누적 599.5초
            h.S.TestKillsPerTick = 1;
            var e = h.Advance(1.0); // 칸 .6~.9 전날, 05:00:00.0 = 유효 끝 = 경계, 그 뒤 무효
            Assert.That(Trace(e), Is.EqualTo("PPPPDPS"));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(1));
        }

        [Test]
        public void 다섯시와_유효_끝이_같고_그_순간_칸이_없어도_초기화하고_뒤는_재확인_필요()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 50, 0));
            h.S.TestKillsPerTick = 0;
            var e = h.Advance(600.05);
            Assert.That(Trace(e), Is.EqualTo("DS"));
            Assert.That(h.S.TimeStatus, Is.EqualTo(TimeStatus.NeedsResync));
        }

        [Test]
        public void 칸이_생기지_않아도_다섯시를_지나면_하루_초기화()
        {
            var h = Killing(KstMs(2026, 11, 18, 4, 59, 59, 995));
            Assert.That(Trace(h.Advance(0.01)), Is.EqualTo("D"));
        }

        [Test]
        public void 월요일_다섯시를_걸친_프레임은_앞_전투_하루_주_초기화_뒤_전투()
        {
            var h = Killing(KstMs(2026, 11, 23, 4, 59, 59, 500)); // 월요일
            Assert.That(Trace(h.Advance(1.0)), Is.EqualTo("PPPPDWPPPPPP"));
        }

        [Test]
        public void 프레임_중간에_유효_시간이_끝나면_앞부분만_센다()
        {
            var h = Killing(KstMs(2026, 11, 18, 12, 0, 0));
            h.S.TestKillsPerTick = 0;
            h.Advance(599.5);
            h.S.TestKillsPerTick = 1;
            var e = h.Advance(1.0); // 유효 끝은 0.5초 뒤 (정확히 그 칸까지 유효)
            Assert.That(Trace(e), Is.EqualTo("PPPPPS"));
        }

        [Test]
        public void 프레임_중간에_TimeWentBack이_풀리면_풀린_시각의_칸부터_센다_이전_구간을_다시_초기화하지_않음()
        {
            var h = New();
            h.Sync(KstMs(2026, 11, 18, 5, 0, 10)); // 처리한 날 = 18일
            h.Pause();
            h.Resume();
            h.Sync(KstMs(2026, 11, 18, 4, 59, 59, 500)); // 17일로 뒤로 감
            Assert.That(h.S.Clock.DateGate(), Is.EqualTo(ErrorCode.TimeWentBack));
            h.S.TestKillsPerTick = 1;
            var e = h.Advance(1.0); // 05:00:00.0 정각 칸부터 풀림
            Assert.That(Trace(e), Is.EqualTo("PPPPPP"), "DayReset 없음");
            Assert.That(h.S.Clock.DateGate(), Is.Null);
        }

        [Test]
        public void 서버가_뒤로_가_처리한_날을_따라잡지_못하면_세지_않는다()
        {
            var h = New();
            h.Sync(KstMs(2026, 11, 18, 6, 0, 0));
            h.Pause();
            h.Resume();
            h.Sync(KstMs(2026, 11, 18, 4, 0, 0));
            h.S.TestKillsPerTick = 1;
            Assert.That(Trace(h.Advance(1.0)), Is.EqualTo(""));
        }

        [Test]
        public void 시간을_나눠_넣어도_같다_단_1초를_넘겨_버린_시간이_없을_때()
        {
            var a = Killing(KstMs(2026, 11, 18, 12, 0, 0));
            var b = Killing(KstMs(2026, 11, 18, 12, 0, 0));
            for (var i = 0; i < 60; i++) a.Advance(1.0 / 60);
            b.Advance(1.0);
            Assert.That(a.MissionProgress("kill_ten"), Is.EqualTo(b.MissionProgress("kill_ten")));
            var c = Killing(KstMs(2026, 11, 18, 12, 0, 0));
            c.Advance(3.0);
            var d = Killing(KstMs(2026, 11, 18, 12, 0, 0));
            for (var i = 0; i < 3; i++) d.Advance(1.0);
            Assert.That((c.MissionProgress("kill_ten"), d.MissionProgress("kill_ten")), Is.EqualTo((10L, 10L)), "3초 한 번은 전투 1초: 10칸 / 1초 세 번은 30칸이지만 목표 10에서 멈춤");
            Assert.That(c.S.Battle.LastDiscardedUs, Is.EqualTo(2_000_000));
        }

        [Test]
        public void 동기화_전에는_경계도_없고_칸은_모두_미확인()
        {
            var h = New();
            h.S.TestKillsPerTick = 1;
            Assert.That(Trace(h.Advance(100)), Is.EqualTo(""));
            Assert.That(h.S.Clock.SyncElapsedUs, Is.EqualTo(0));
        }
    }
}
