using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Ads;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Tests.Data;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>
    /// C3 테스트용 표와 세션 조작. 표는 TestTables.Good()에서 별가루를 stardust로 바꾸고 임무를 더한 것이다.
    /// 분당 보상은 손으로 만든 표(스테이지 1 → 10, 2 → 100, 그 밖 0)를 넣는다(2026-11-17 Q3).
    /// Services처럼 요청 번호를 0부터 올리고, 복귀 때 마지막으로 보낸 번호 + 1을 넘긴다.
    /// </summary>
    internal sealed class C3Harness
    {
        internal static readonly TimeSpan Kst = TimeSpan.FromHours(9);
        internal static long KstMs(int y, int mo, int d, int h, int mi, int s, int ms = 0) => new DateTimeOffset(y, mo, d, h, mi, s, ms, Kst).ToUnixTimeMilliseconds();

        internal const ulong DefaultNonce = 0x1234_5678_9abc_def0UL;

        internal static GameNumber Rate(int stage) => stage == 1 ? GameNumber.From(10) : stage == 2 ? GameNumber.From(100) : GameNumber.Zero;

        internal static Dictionary<string, string> Texts()
        {
            return TestTables.Good()
                .Replace("currencies", "dust,\"별가루, 반짝\",game_number,m1", "stardust,별가루,game_number,m1")
                .Replace("attendance", "2,currency,dust,1000", "2,currency,stardust,1000")
                .AppendRow("daily_missions", "claim_offline_once,daily,claim_offline,1,gold,5,m1")
                .AppendRow("daily_missions", "kill_ten,daily,kill_monster,10,stardust,100,m1");
        }

        internal static TableSet Tables(Dictionary<string, string> texts = null)
        {
            var r = TableBuilder.BuildFromTexts(texts ?? Texts());
            Assert.That(r.Ok, Is.True, TestTables.Describe(r.Issues));
            return r.Tables;
        }

        public GameSession S { get; private set; }
        public TableSet T { get; }
        long _lastRequest;

        C3Harness(TableSet tables, GameSession s)
        {
            T = tables;
            S = s;
        }

        internal static C3Harness New(TableSet tables = null, ulong nonce = DefaultNonce, bool check = true)
        {
            tables = tables ?? Tables();
            var r = GameSessionLoader.NewAccount(tables, 7, nonce, new GameSessionOptions { CheckInvariants = check });
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
            r.Session.OfflineRateForStage = Rate;
            return new C3Harness(tables, r.Session);
        }

        /// <summary>저장한 뒤 새 세션으로 불러온다(앱 재시작). 요청 번호는 0부터.</summary>
        internal void Restart(ulong nonce = DefaultNonce)
        {
            var json = S.CreateSave().Json;
            var r = GameSessionLoader.Load(T, json, nonce, new GameSessionOptions { CheckInvariants = S.Options.CheckInvariants });
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
            r.Session.OfflineRateForStage = Rate;
            r.Session.TestKillsPerTick = S.TestKillsPerTick;
            AssertNoViolations();
            S = r.Session;
            _lastRequest = 0;
        }

        /// <summary>주어진 저장 글로 새 세션을 불러온다(저장 글을 손으로 바꾼 시험용). 실패하면 테스트 실패.</summary>
        internal void LoadJson(string json, ulong nonce = DefaultNonce)
        {
            var r = GameSessionLoader.Load(T, json, nonce, new GameSessionOptions { CheckInvariants = S.Options.CheckInvariants });
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
            r.Session.OfflineRateForStage = Rate;
            S = r.Session;
            _lastRequest = 0;
        }

        internal List<GameEvent> Ok(CommandResult r)
        {
            Assert.That(r.Ok, Is.True, "실패: " + r);
            AssertNoViolations();
            return r.Events.ToList();
        }

        internal void AssertNoViolations() => Assert.That(S.Violations, Is.Empty, string.Join("\n", S.Violations));

        internal List<GameEvent> Sync(long serverUtcMs)
        {
            _lastRequest++;
            var e = Ok(S.SyncServerTime(serverUtcMs, _lastRequest));
            Assert.That(S.LastSyncOutcome, Is.EqualTo(ZodiacSignGuardian.Core.Timing.SyncOutcome.Accepted));
            return e;
        }

        internal List<GameEvent> Advance(double sec) => Ok(S.Advance(sec));
        internal void Pause() => Ok(S.Suspend());
        internal void Resume() => Ok(S.Resume(_lastRequest + 1));

        /// <summary>나갔다가 돌아오고 serverUtcMs로 동기화한다.</summary>
        internal List<GameEvent> AwayAndBack(long serverUtcMs)
        {
            Pause();
            Resume();
            return Sync(serverUtcMs);
        }

        internal AdTicket StartAd(AdTarget target)
        {
            var e = Ok(S.StartAd(target));
            return e.OfType<AdTicketIssued>().Single().Ticket;
        }

        internal void AssertFailsUnchanged(Func<CommandResult> command, ErrorCode expected)
        {
            var before = S.Capture();
            var r = command();
            Assert.That(r.Ok, Is.False, "성공하면 안 됨");
            Assert.That(r.Error, Is.EqualTo(expected));
            Assert.That(r.Events, Is.Empty);
            Assert.That(S.Capture().Same(before), Is.True, "실패한 명령은 상태를 바꾸지 않음");
            AssertNoViolations();
        }

        internal GameNumber Dust => S.Balance(GameSession.StardustId).Number;
        internal long Gold => S.Balance("gold").Integer;
        internal long MissionProgress(string id) => S.MissionStatuses.Single(m => m.Id == id).Progress;
        internal bool MissionClaimed(string id) => S.MissionStatuses.Single(m => m.Id == id).Claimed;
    }
}
