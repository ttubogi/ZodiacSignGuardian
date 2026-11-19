using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Tests.Data;
using ZodiacSignGuardian.Data.Tables;
using static ZodiacSignGuardian.Core.Tests.Session.C3Harness;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>출석·가진 별·일일 임무 (설계서 v0.10 3절, 2026-11-17 Q1·Q2·Q4, 2026-11-18 별 보상 amount 1).</summary>
    [TestFixture]
    public class AttendanceMissionTests
    {
        /// <summary>수요일 12:00 KST + days일.</summary>
        static long Day(int days, int hour = 12) => KstMs(2026, 11, 18, hour, 0, 0) + days * 86_400_000L;

        static C3Harness Started()
        {
            var h = New();
            h.Sync(Day(0));
            return h;
        }

        // ---- 출석 ----

        [Test]
        public void 출석은_받기를_누른_날만_차례가_오르고_마지막_다음은_1일째()
        {
            var h = Started();
            Assert.That(h.S.AttendanceNextDay, Is.EqualTo(1));
            var e = h.Ok(h.S.ClaimAttendance());
            var c = e.OfType<CurrencyChanged>().Single();
            Assert.That((c.CurrencyId, c.Reason, h.Gold), Is.EqualTo(("gold", CurrencyReason.AttendanceReward, 50L)));
            Assert.That(h.S.AttendanceClaimedToday, Is.True);
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.AlreadyClaimed);

            h.AwayAndBack(Day(1));
            Assert.That(h.S.AttendanceClaimedToday, Is.False);
            h.AwayAndBack(Day(2)); // 접속만 하고 누르지 않은 날이 지나도
            Assert.That(h.S.AttendanceNextDay, Is.EqualTo(2), "같은 차례");
            h.Ok(h.S.ClaimAttendance());
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(1000)), "2일째: GameNumber 재화");

            h.AwayAndBack(Day(3));
            var e3 = h.Ok(h.S.ClaimAttendance());
            var star = e3.OfType<StarObtained>().Single();
            Assert.That((star.InstanceId, star.StarId), Is.EqualTo((1L, "wood_1")));
            Assert.That(h.S.OwnedStars, Is.EqualTo(new[] { new OwnedStar(1, "wood_1") }));
            Assert.That(h.S.AttendanceNextDay, Is.EqualTo(1), "cycle_days(3) 다음은 1일째");
        }

        [Test]
        public void 출석은_날짜_의존_기능이다()
        {
            var h = New();
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.TimeNotVerified);
            h.Sync(Day(1));
            h.Pause();
            h.Resume();
            h.Sync(Day(0)); // 처리한 날보다 이전
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.TimeWentBack);
        }

        // ---- 별 보상 수량 (설계서 v0.10, Codex C3 검토 질문 1 A안: amount 1만) ----

        [TestCase(2L)]
        [TestCase(long.MaxValue)]
        public void 별_보상_amount가_1이_아니면_표_검사와_표_읽기가_막는다(long amount)
        {
            var texts = Texts().Replace("attendance", "1,currency,gold,50", "1,star,fire_2," + amount);
            Assert.That(TableBuilder.BuildFromTexts(texts).Ok, Is.False, "표 검사 4");
            var tables = GameSessionTests.ParsedOnly(texts);
            SessionLoadResult made = null;
            Assert.DoesNotThrow(() => made = GameSessionLoader.NewAccount(tables, 1, DefaultNonce));
            Assert.That(made.Ok, Is.False);
            Assert.That(made.Errors.Any(e => e.Contains("별 보상의 amount는 1")), Is.True, string.Join("\n", made.Errors));
            var save = Started().S.CreateSave().Json;
            SessionLoadResult loaded = null;
            Assert.DoesNotThrow(() => loaded = GameSessionLoader.Load(tables, save, DefaultNonce));
            Assert.That(loaded.Ok, Is.False);
        }

        [TestCase(2L)]
        [TestCase(long.MaxValue)]
        public void 별_보상_amount가_1이_아니면_지급_전_검사도_InvalidInput이고_별_출석_이벤트_그대로(long amount)
        {
            var h = Started();
            h.S.Attendance.ReplaceRewardForTest(new AttendanceReward(1, AttendanceRewardKind.Star, "fire_2", amount));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.InvalidInput);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000), "값의 크기만큼 돌지 않음");
            Assert.That(h.S.OwnedStars, Is.Empty);
            Assert.That(h.S.AttendanceNextDay, Is.EqualTo(1));
            Assert.That(h.S.AttendanceClaimedToday, Is.False);
        }

        [Test]
        public void 별_보상은_하나씩_번호가_겹치지_않게()
        {
            var texts = Texts().Replace("attendance", "1,currency,gold,50", "1,star,fire_2,1");
            var h = New(Tables(texts));
            h.Sync(Day(0));
            var e = h.Ok(h.S.ClaimAttendance());
            Assert.That(e.OfType<StarObtained>().Select(x => (x.InstanceId, x.StarId)), Is.EqualTo(new[] { (1L, "fire_2") }));
            Assert.That(h.S.Stars.NextInstanceId, Is.EqualTo(2));
        }

        [Test]
        public void 별_번호가_끝에_닿으면_출석도_함께_실패하고_끝_바로_앞까지는_쓴다()
        {
            var texts = Texts().Replace("attendance", "1,currency,gold,50", "1,star,fire_2,1");
            var h = New(Tables(texts));
            h.Sync(Day(0));
            Assert.That(h.S.Stars.TryRestore(long.MaxValue, new List<OwnedStar>(), new List<string>()), Is.True);
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.InvalidInput);
            Assert.That(h.S.AttendanceNextDay, Is.EqualTo(1));

            var h2 = New(Tables(texts));
            h2.Sync(Day(0));
            Assert.That(h2.S.Stars.TryRestore(long.MaxValue - 1, new List<OwnedStar>(), new List<string>()), Is.True);
            var e = h2.Ok(h2.S.ClaimAttendance());
            Assert.That(e.OfType<StarObtained>().Single().InstanceId, Is.EqualTo(long.MaxValue - 1));
            Assert.That(h2.S.Stars.NextInstanceId, Is.EqualTo(long.MaxValue));
            var r = GameSessionLoader.Load(Tables(texts), h2.S.CreateSave().Json, DefaultNonce ^ 3);
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
        }

        [Test]
        public void 정수_재화가_넘치면_출석은_InvalidInput이고_그대로()
        {
            var h = Started();
            h.S.Wallet.CorruptBalanceForTest("gold", CurrencyAmount.Of(long.MaxValue - 10));
            h.AssertFailsUnchanged(() => h.S.ClaimAttendance(), ErrorCode.InvalidInput);
        }

        // ---- 일일 임무 ----

        [Test]
        public void 임무는_진행이_바뀔_때마다_MissionProgressed_완료는_한_번_수령_뒤_다음_날_비운다()
        {
            var h = Started();
            h.S.TestKillsPerTick = 1;
            var e = h.Advance(0.3); // 칸 3개
            Assert.That(e.OfType<MissionProgressed>().Where(p => p.MissionId == "kill_ten").Select(p => p.Progress), Is.EqualTo(new[] { 1L, 2L, 3L }));
            Assert.That(e.OfType<MissionCompleted>(), Is.Empty);
            h.AssertFailsUnchanged(() => h.S.ClaimMission("kill_ten"), ErrorCode.NotReady);
            var e2 = h.Advance(1.0); // 칸 10개: 10에서 멈춤
            Assert.That(e2.OfType<MissionCompleted>().Count(m => m.MissionId == "kill_ten"), Is.EqualTo(1));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(10));
            Assert.That(e2.OfType<MissionProgressed>().Count(p => p.MissionId == "kill_ten"), Is.EqualTo(7), "목표에 닿은 뒤로는 나오지 않음");
            var claim = h.Ok(h.S.ClaimMission("kill_ten"));
            var c = claim.OfType<CurrencyChanged>().Single();
            Assert.That((c.CurrencyId, c.Reason), Is.EqualTo(("stardust", CurrencyReason.MissionReward)));
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(100)));
            h.AssertFailsUnchanged(() => h.S.ClaimMission("kill_ten"), ErrorCode.AlreadyClaimed);
            Assert.That(h.Advance(0.5).OfType<MissionProgressed>().Count(p => p.MissionId == "kill_ten"), Is.EqualTo(0), "받은 임무는 더 세지 않음");

            h.AwayAndBack(Day(1));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(0));
            Assert.That(h.MissionClaimed("kill_ten"), Is.False);
        }

        [Test]
        public void 임무_수령_실패_순서는_모르는_id_시간_수령_진행()
        {
            var h = New();
            h.AssertFailsUnchanged(() => h.S.ClaimMission("nope"), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.ClaimMission(null), ErrorCode.InvalidTarget);
            h.AssertFailsUnchanged(() => h.S.ClaimMission("dungeon_runs"), ErrorCode.InvalidTarget); // v1 임무
            h.AssertFailsUnchanged(() => h.S.ClaimMission("kill_ten"), ErrorCode.TimeNotVerified);
        }

        [Test]
        public void 미확인_상태의_처치는_세지_않고_동기화_뒤_소급하지_않는다()
        {
            var h = New();
            h.S.TestKillsPerTick = 1;
            h.Advance(0.5);
            h.Sync(Day(0));
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(0));
            h.Advance(0.1);
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(1));
        }

        [Test]
        public void 앞선_Advance에서_하루_초기화_뒤_ClaimMission이_실패해도_초기화는_남는다()
        {
            var h = New();
            h.Sync(KstMs(2026, 11, 18, 4, 59, 59));
            h.S.TestKillsPerTick = 1;
            h.Advance(1.0); // 05:00:00 정각 칸까지: 전날 9칸 뒤 초기화, 새날 1칸
            h.S.TestKillsPerTick = 0;
            Assert.That(h.MissionProgress("kill_ten"), Is.EqualTo(1));
            // Services: 처리 시점까지의 경과를 한 번 Advance한 뒤 버튼 명령
            h.Advance(0.2);
            h.AssertFailsUnchanged(() => h.S.ClaimMission("kill_ten"), ErrorCode.NotReady);
            Assert.That(h.S.Clock.ProcessedDay, Is.EqualTo(h.S.Clock.DayIdAt(KstMs(2026, 11, 18, 5, 0, 0) * 1000L)));
        }

        [Test]
        public void 다른_재화로_주는_임무_보상()
        {
            var h = Started();
            h.AwayAndBack(Day(0) + 5 * 60_000);
            h.Ok(h.S.ClaimOffline());
            h.Ok(h.S.ClaimMission("claim_offline_once"));
            Assert.That(h.Gold, Is.EqualTo(5));
        }
    }
}
