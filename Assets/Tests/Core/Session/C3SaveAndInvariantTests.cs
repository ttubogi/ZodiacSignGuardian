using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Ads;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Session;
using static ZodiacSignGuardian.Core.Tests.Session.C3Harness;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>C3 저장 칸, 위반 규칙 2·3·4·9·10 주입, 무작위 명령 (설계서 v0.9 5절).</summary>
    [TestFixture]
    public class C3SaveAndInvariantTests
    {
        static readonly long T0 = KstMs(2026, 11, 18, 12, 0, 0);
        const long Min = 60_000L;

        /// <summary>저장 → 불러오기 → 같은 번호로 다시 쓰면 같은 JSON (저장하는 상태가 같다).</summary>
        static void AssertRoundTrip(C3Harness h, string where)
        {
            var save = h.S.CreateSave();
            var r = GameSessionLoader.Load(h.T, save.Json, DefaultNonce ^ 1);
            Assert.That(r.Ok, Is.True, where + ": " + string.Join("\n", r.Errors));
            Assert.That(SaveFormat.Write(r.Session, save.SaveRevision), Is.EqualTo(save.Json), where);
            Assert.That(r.Session.SettlementPending, Is.True, "불러오면 정산 대기");
            Assert.That(r.Session.TimeStatus, Is.EqualTo(ZodiacSignGuardian.Core.Timing.TimeStatus.Unverified));
        }

        [Test]
        public void 허용하는_모든_상태에서_저장하고_불러오면_저장하는_상태가_같다()
        {
            var h = New();
            AssertRoundTrip(h, "새 계정");
            h.Advance(12.345678);
            AssertRoundTrip(h, "동기화 전 진행");
            h.Sync(T0);
            AssertRoundTrip(h, "첫 동기화 뒤");
            h.AwayAndBack(T0 + 30 * Min);
            AssertRoundTrip(h, "묶음");
            h.Ok(h.S.ConfirmAd(h.StartAd(AdTarget.Offline)));
            AssertRoundTrip(h, "광고 적용 묶음");
            h.Ok(h.S.ClaimAttendance());
            h.S.TestKillsPerTick = 1;
            h.Advance(0.75);
            AssertRoundTrip(h, "출석·임무 진행");
            h.Ok(h.S.ClaimOffline());
            h.Ok(h.S.ClaimMission("claim_offline_once"));
            AssertRoundTrip(h, "임무 수령");
            h.Pause();
            h.Resume();
            AssertRoundTrip(h, "정산 대기");
            h.S.SetStageForTest(int.MaxValue);
            AssertRoundTrip(h, "스테이지 끝값");
        }

        static string R(string json, string from, string to)
        {
            Assert.That(json.Contains(from), Is.True, "변형할 글이 없음: " + from);
            return json.Replace(from, to);
        }

        static string BaseSave()
        {
            var h = New();
            h.Sync(T0);
            h.AwayAndBack(T0 + 30 * Min);
            h.S.TestKillsPerTick = 1;
            h.Advance(0.3);
            h.Ok(h.S.ClaimAttendance()); // 별이 아닌 1일째
            return h.S.CreateSave().Json;
        }

        static IEnumerable<TestCaseData> BadSaves()
        {
            var cases = new (string, Func<string, string>)[]
            {
                ("stage 0", j => R(j, "\"stage\": 1", "\"stage\": 0")),
                ("stage int 밖", j => R(j, "\"stage\": 1", "\"stage\": 2147483648")),
                ("leaveStage 0", j => R(j, "\"leaveStage\": 1", "\"leaveStage\": 0")),
                ("활성 경과 음수", j => System.Text.RegularExpressions.Regex.Replace(j, "\"activeElapsedUs\": \\d+", "\"activeElapsedUs\": -1")),
                ("정산 기준 음수", j => R(j, "\"settlementPointUtcMs\": " + (T0 + 30 * Min), "\"settlementPointUtcMs\": -1")),
                ("정산 기준 9999년 뒤", j => R(j, "\"settlementPointUtcMs\": " + (T0 + 30 * Min), "\"settlementPointUtcMs\": 253402300800000")),
                ("묶음 분 0", j => R(j, "\"minutes\": 30", "\"minutes\": 0")),
                ("묶음 분 721", j => R(j, "\"minutes\": 30", "\"minutes\": 721")),
                ("묶음 금액 0", j => R(j, "\"amount\": \"3e2\"", "\"amount\": \"0\"")),
                ("묶음 금액 음수", j => R(j, "\"amount\": \"3e2\"", "\"amount\": \"-3e2\"")),
                ("묶음 번호 0", j => R(j, "\"bundle\": {\"id\": 1", "\"bundle\": {\"id\": 0")),
                ("묶음 번호가 다음 번호 이상", j => R(j, "\"bundle\": {\"id\": 1", "\"bundle\": {\"id\": 2")),
                ("광고 적용 2", j => R(j, "\"adApplied\": 0", "\"adApplied\": 2")),
                ("묶음 키 빠짐", j => R(j, ", \"adApplied\": 0", "")),
                ("다음 묶음 번호 0", j => R(j, "\"nextBundleId\": 2", "\"nextBundleId\": 0")),
                ("출석 차례 0", j => R(j, "\"nextDay\": 2", "\"nextDay\": 0")),
                ("출석 차례 4", j => R(j, "\"nextDay\": 2", "\"nextDay\": 4")),
                ("출석 날 범위 밖", j => System.Text.RegularExpressions.Regex.Replace(j, "\"lastClaimedDay\": \\d+", "\"lastClaimedDay\": 9223372036854775807")),
                ("별 번호 겹침", j => R(j, "\"owned\": []", "\"owned\": [{\"id\": 1, \"starId\": \"wood_1\"}, {\"id\": 1, \"starId\": \"wood_2\"}]")),
                ("별 번호가 다음 번호 이상", j => R(j, "\"owned\": []", "\"owned\": [{\"id\": 1, \"starId\": \"wood_1\"}]")),
                ("v1 별", j => R(R(j, "\"nextInstanceId\": 1", "\"nextInstanceId\": 2"), "\"owned\": []", "\"owned\": [{\"id\": 1, \"starId\": \"wood_4\"}]")),
                ("별 키 남음", j => R(R(j, "\"nextInstanceId\": 1", "\"nextInstanceId\": 2"), "\"owned\": []", "\"owned\": [{\"id\": 1, \"starId\": \"wood_1\", \"x\": 1}]")),
                ("임무 진행 목표 넘음", j => R(j, "\"kill_ten\": {\"progress\": 3", "\"kill_ten\": {\"progress\": 11")),
                ("임무 진행 음수", j => R(j, "\"kill_ten\": {\"progress\": 3", "\"kill_ten\": {\"progress\": -1")),
                ("받았는데 목표 미달", j => R(j, "\"kill_ten\": {\"progress\": 3, \"claimed\": 0", "\"kill_ten\": {\"progress\": 3, \"claimed\": 1")),
                ("임무 빠짐", j => R(j, "\"claim_offline_once\": {\"progress\": 0, \"claimed\": 0}, ", "")),
                ("표에 없는 임무", j => R(j, "\"missions\": {", "\"missions\": {\"ghost\": {\"progress\": 0, \"claimed\": 0}, ")),
                ("offline 키 빠짐", j => R(j, "\"leaveStage\": 1, ", "")),
                ("attendance 없음", j => System.Text.RegularExpressions.Regex.Replace(j, ", \"attendance\": \\{[^}]*\\}", "")),
            };
            foreach (var (why, make) in cases) yield return new TestCaseData(why, make).SetName("틀린_C3_저장_" + why.Replace(' ', '_'));
        }

        [TestCaseSource(nameof(BadSaves))]
        public void 틀린_C3_저장은_불러오지_않고_예외도_없다(string why, Func<string, string> make)
        {
            var good = BaseSave();
            var tables = Tables();
            Assert.That(GameSessionLoader.Load(tables, good, DefaultNonce).Ok, Is.True, "고치기 전 저장은 읽힘");
            var bad = make(good);
            SessionLoadResult r = null;
            Assert.DoesNotThrow(() => r = GameSessionLoader.Load(tables, bad, DefaultNonce), why);
            Assert.That(r.Ok, Is.False, why);
            Assert.That(r.Errors, Is.Not.Empty);
        }

        [Test]
        public void C2_때_만든_저장은_읽지_않는다()
        {
            var c2 = "{\"saveVersion\": 1, \"saveRevision\": 1, \"lastSavedUtcMs\": null, \"progress\": {\"chapter\": 1}, " +
                     "\"wallet\": {\"gold\": 0, \"stardust\": \"0\"}, \"clock\": {\"anchorUtcMs\": null, \"syncElapsedUs\": 0, \"processedDay\": null, \"processedWeek\": null, \"battlePendingUs\": 0}, " +
                     "\"rng\": []}";
            var r = GameSessionLoader.Load(Tables(), c2, DefaultNonce);
            Assert.That(r.Ok, Is.False);
        }

        // ---- 위반 주입 ----

        [Test]
        public void 같은_날_출석을_두_번_지급하면_잡는다_규칙2()
        {
            var h = New();
            h.Sync(T0);
            h.Ok(h.S.ClaimAttendance());
            h.S.Attendance.ForgetClaimForTest(); // 받은 기록을 잃은 잘못을 만든다
            var r = h.S.ClaimAttendance();
            Assert.That(r.Ok, Is.True);
            Assert.That(h.S.Violations.Select(v => v.Rule), Does.Contain(2));
        }

        [Test]
        public void 위반_검사_함수는_C3_규칙의_망가뜨린_값을_모두_잡는다()
        {
            var found = new List<InvariantViolation>();
            var paid = new HashSet<string>();
            Invariants.CheckClaimOnce("offline:1", paid, found);
            Invariants.CheckClaimOnce("offline:1", paid, found);
            Assert.That(found.Select(v => v.Rule), Is.EqualTo(new[] { 2 }));

            found.Clear();
            Invariants.CheckDateDependent("ClaimAttendance", ErrorCode.TimeNotVerified, true, found);
            Invariants.CheckDateDependent("ClaimAttendance", ErrorCode.TimeWentBack, true, found);
            Invariants.CheckDateDependent("ConfirmAd", ErrorCode.TimeNotVerified, false, found); // 광고 적용 표시는 예외
            Invariants.CheckOfflineAccrualValid(true, false, found);
            Assert.That(found.Select(v => v.Rule), Is.EqualTo(new[] { 3, 3, 3 }));

            found.Clear();
            Invariants.CheckOfflineBundle(true, 721, GameNumber.From(1), 720, found);
            Invariants.CheckOfflineBundle(true, 0, GameNumber.From(1), 720, found);
            Invariants.CheckOfflineBundle(true, 10, GameNumber.Zero, 720, found);
            Invariants.CheckOfflineBundle(false, 3, GameNumber.Zero, 720, found);
            Invariants.CheckOfflineBundle(true, 720, GameNumber.From(1), 720, found); // 정확히 720은 됨
            Assert.That(found.Select(v => v.Rule), Is.EqualTo(new[] { 4, 4, 4, 4 }));

            found.Clear();
            var before = new Dictionary<long, KeyValuePair<AdTarget, AdTicketState>>
            {
                [1] = new KeyValuePair<AdTarget, AdTicketState>(AdTarget.Offline, AdTicketState.Used),
                [2] = new KeyValuePair<AdTarget, AdTicketState>(AdTarget.Offline, AdTicketState.Open),
                [3] = new KeyValuePair<AdTarget, AdTicketState>(AdTarget.HuntBuff, AdTicketState.Open),
            };
            var after = new Dictionary<long, KeyValuePair<AdTarget, AdTicketState>>(before)
            {
                [1] = new KeyValuePair<AdTarget, AdTicketState>(AdTarget.Offline, AdTicketState.Revoked), // 사용됨이 바뀜
                [2] = new KeyValuePair<AdTarget, AdTicketState>(AdTarget.Offline, AdTicketState.Used),
                [3] = new KeyValuePair<AdTarget, AdTicketState>(AdTarget.HuntBuff, AdTicketState.Used), // 다른 대상에 쓰임 + 두 개
            };
            Invariants.CheckTickets(before, after, AdTarget.Offline, found);
            Assert.That(found.Count, Is.EqualTo(3));
            Assert.That(found.All(v => v.Rule == 9), Is.True);
            found.Clear();
            Invariants.CheckTickets(before, new Dictionary<long, KeyValuePair<AdTarget, AdTicketState>>(), null, found);
            Assert.That(found.Select(v => v.Rule), Is.EqualTo(new[] { 9 }), "사용된 번호 기록이 사라짐");

            found.Clear();
            Invariants.CheckSettlementForward(100, 99, found);
            Invariants.CheckSettlementForward(100, null, found);
            Invariants.CheckSettlementForward(100, 100, found);
            Invariants.CheckSettlementForward(null, 5, found);
            Assert.That(found.Select(v => v.Rule), Is.EqualTo(new[] { 10, 10 }));
        }

        [Test]
        public void 날짜_조건이_틀린데_출석을_바꾸는_잘못을_잡는다_규칙3()
        {
            // 명령 틀을 직접 써서 조건 없이 출석을 바꾸는 잘못된 명령을 만든다
            var h = New(); // 미확인
            h.S.Execute("BadClaim", events =>
            {
                h.S.Attendance.MarkClaimed(0);
                return null;
            }, GameSession.CommandKind.DateDependent);
            Assert.That(h.S.Violations.Select(v => v.Rule), Does.Contain(3));
        }

        // ---- 무작위 명령 ----

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(20261118)]
        public void 무작위_명령_수천_개에도_위반이_없고_언제_저장해도_다시_불러온다(int seed)
        {
            var rng = new Random(seed);
            var h = New();
            var now = T0;
            var lastRequest = 0L;
            var tickets = new List<AdTicket>();
            h.S.TestKillsPerTick = 1;
            var anyStateChanged = 0;
            for (var i = 0; i < 3000; i++)
            {
                var before = h.S.Capture();
                CommandResult r;
                switch (rng.Next(14))
                {
                    case 0:
                    case 1:
                        var sec = rng.Next(4) == 0 ? rng.NextDouble() * 900 : rng.NextDouble() * 0.2;
                        r = h.S.Advance(sec);
                        if (r.Ok && h.S.RunState == RunState.Active) now += (long)(sec * 1000);
                        break;
                    case 2:
                        now += rng.Next(3) == 0 ? -rng.Next(0, 3_600_000) : rng.Next(0, 4 * 3_600_000);
                        if (now < 0) now = 0;
                        r = h.S.SyncServerTime(now, rng.Next(5) == 0 ? lastRequest : ++lastRequest);
                        break;
                    case 3:
                        r = h.S.Suspend();
                        break;
                    case 4:
                        r = h.S.Resume(lastRequest + 1);
                        break;
                    case 5:
                        r = h.S.StartAd(rng.Next(3) == 0 ? AdTarget.HuntBuff : AdTarget.Offline);
                        if (r.Ok) tickets.Add(r.Events.OfType<AdTicketIssued>().Single().Ticket);
                        break;
                    case 6:
                        r = h.S.ConfirmAd(tickets.Count == 0 ? null : tickets[rng.Next(tickets.Count)]);
                        break;
                    case 7:
                        r = h.S.ClaimOffline();
                        break;
                    case 8:
                        r = h.S.ClaimAttendance();
                        break;
                    case 9:
                        r = h.S.ClaimMission(rng.Next(2) == 0 ? "kill_ten" : rng.Next(2) == 0 ? "claim_offline_once" : "kill_monsters");
                        break;
                    case 10:
                        h.S.SetStageForTest(1 + rng.Next(3));
                        r = CommandResult.Success();
                        break;
                    case 11:
                        r = h.S.AdoptCloudRevision(h.S.SaveRevision + rng.Next(3));
                        break;
                    case 12:
                        AssertRoundTrip(h, "무작위 " + i);
                        r = CommandResult.Success();
                        break;
                    default:
                        if (rng.Next(4) == 0)
                        {
                            var nonce = (ulong)rng.Next() << 20;
                            h.Restart(nonce);
                            lastRequest = 0;
                            tickets.Clear();
                        }
                        r = CommandResult.Success();
                        break;
                }
                if (!r.Ok) Assert.That(h.S.Capture().Same(before), Is.True, "실패한 명령 " + i);
                else if (!h.S.Capture().Same(before)) anyStateChanged++;
                Assert.That(h.S.Violations, Is.Empty, "명령 " + i + ": " + string.Join("\n", h.S.Violations));
                Assert.That(h.S.OfflineBundle == null || h.S.OfflineBundle.Minutes <= 720, Is.True);
            }
            Assert.That(anyStateChanged, Is.GreaterThan(500), "무작위 명령이 실제로 상태를 바꿈");
        }
    }
}
