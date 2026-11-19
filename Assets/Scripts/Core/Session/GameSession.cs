using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Ads;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Randomness;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Timing;

namespace ZodiacSignGuardian.Core.Session
{
    /// <summary>실행 상태 (설계서 3절 "실행 상태"). 저장하지 않는다.</summary>
    public enum RunState
    {
        Active = 0,
        Paused = 1,
    }

    public sealed class GameSessionOptions
    {
        /// <summary>명령마다 위반 규칙을 검사한다. 테스트는 항상 켜고, Services는 개발 빌드에서 켠다 (C2 결정 Q7).</summary>
        public bool CheckInvariants { get; set; }
    }

    /// <summary>두 기기 충돌 때 화면에 보여줄 요약 (설계서 3절 "저장").</summary>
    public sealed class SaveSummary
    {
        public long SaveRevision { get; }
        public int Chapter { get; }
        /// <summary>별가루 잔액. 별가루 재화가 표에 없으면 0.</summary>
        public GameNumber Stardust { get; }
        /// <summary>저장할 때의 추정 서버 시각(ms). 서버 시각을 한 번도 받지 않았으면 null.</summary>
        public long? LastSavedUtcMs { get; }

        public SaveSummary(long saveRevision, int chapter, GameNumber stardust, long? lastSavedUtcMs)
        {
            SaveRevision = saveRevision;
            Chapter = chapter;
            Stardust = stardust;
            LastSavedUtcMs = lastSavedUtcMs;
        }
    }

    /// <summary>저장할 때마다 만드는 고정된 사본. Json은 Core가 정한 형식이며 Services는 그대로 보관한다.</summary>
    public sealed class SaveSnapshot
    {
        public long SaveRevision { get; }
        public string Json { get; }
        public SaveSummary Summary { get; }

        internal SaveSnapshot(long saveRevision, string json, SaveSummary summary)
        {
            SaveRevision = saveRevision;
            Json = json;
            Summary = summary;
        }
    }

    /// <summary>
    /// 판정의 입구. 명령을 받아 ok·error·events를 돌려준다. (설계서 v0.9 3절, 4절 GameSession)
    /// <list type="bullet">
    /// <item>명령은 조건을 모두 확인한 뒤에만 상태를 바꾼다. 실패하면 상태는 그대로이고 이벤트도 없다 (C2 결정 Q4).</item>
    /// <item>위반 검사를 켜면 명령마다 규칙을 보고 Violations에 쌓는다. 실패한 명령은 전후 상태를 비교한다(규칙 7).</item>
    /// <item>C3 범위: 시간·실행·저장(C2) + 오프라인 정산·수령, 광고 번호(StartAd·ConfirmAd), 출석, 일일 임무, 가진 별 목록(최소).
    ///       전투·성장·별자리 판·뽑기·사냥 버프는 C4·C5.</item>
    /// <item>Services는 모든 명령을 하나의 직렬 경로로, 처리 시점까지의 미반영 경과를 한 번만 Advance한 뒤 넣는다(설계서 4절 Services 약속).</item>
    /// </list>
    /// 만들기와 불러오기는 GameSessionLoader가 한다(표 묶음을 받는 쪽은 Services).
    /// </summary>
    public sealed class GameSession
    {
        public const int SaveVersion = 1;
        /// <summary>별가루 재화 id. 요약과 오프라인 보상에 쓴다.</summary>
        public const string StardustId = "stardust";

        internal Wallet Wallet { get; private set; }
        internal Clock Clock { get; private set; }
        internal BattleAccumulator Battle { get; private set; }
        internal GameRng Rng { get; private set; }
        internal ElapsedConverter Converter { get; private set; }
        internal Missions Missions { get; private set; }
        internal Attendance Attendance { get; private set; }
        internal StarInventory Stars { get; private set; }
        internal OfflineRewards Offline { get; private set; }
        internal AdTickets Ads { get; private set; }

        public GameSessionOptions Options { get; }
        public RunState RunState { get; private set; } = RunState.Active;
        /// <summary>정산 대기. 앱 시작·Resume 때 켜지고 오프라인 정산이 끈다. 저장하지 않는다(불러오면 항상 켬).</summary>
        public bool SettlementPending { get; private set; } = true;
        /// <summary>마지막으로 낸 저장 번호. 새 계정은 0이고 첫 저장이 1.</summary>
        public long SaveRevision { get; private set; }
        /// <summary>진행 중인 장. 전투·진행(C5) 전까지는 1.</summary>
        public int Chapter { get; private set; } = 1;
        /// <summary>진행 중인 스테이지. 전투·진행(C5) 전까지는 1 (2026-11-17 Q3).</summary>
        public int Stage { get; private set; } = 1;

        /// <summary>마지막 Advance에서 지난 0.1초 칸 수 (C5 전투 전까지 확인용).</summary>
        public long LastAdvanceTicks { get; private set; }
        /// <summary>마지막 SyncServerTime을 어떻게 처리했는지.</summary>
        public SyncOutcome? LastSyncOutcome { get; private set; }

        readonly List<InvariantViolation> _violations = new List<InvariantViolation>();
        /// <summary>위반 검사를 켰을 때 찾은 것 (쌓임).</summary>
        public IReadOnlyList<InvariantViolation> Violations => _violations;

        /// <summary>이번 세션에서 지급한 보상 열쇠 (위반 규칙 2). 저장하지 않는다.</summary>
        readonly HashSet<string> _paid = new HashSet<string>(StringComparer.Ordinal);

        // ---- 테스트·다음 단계를 위한 자리 (internal) ----

        /// <summary>
        /// 스테이지 → 분당 별가루 (2026-11-17 Q3 A안). 스테이지 표는 C4(DataGen)에서 생기므로 그 전 실제 게임은 null이고 분당 보상은 0이다.
        /// 테스트는 손으로 만든 표를 넣는다.
        /// </summary>
        internal Func<int, GameNumber> OfflineRateForStage { get; set; }

        /// <summary>테스트 전용 가짜 전투: 0.1초 칸마다 kill_monster 진행을 이만큼 센다(그 칸의 시각 조건으로). 0이면 없음. 실제 전투는 C5.</summary>
        internal long TestKillsPerTick { get; set; }

        internal GameSession(Wallet wallet, Clock clock, BattleAccumulator battle, GameRng rng, Missions missions, Attendance attendance,
            StarInventory stars, OfflineRewards offline, ulong sessionNonce, GameSessionOptions options)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Missions = missions ?? throw new ArgumentNullException(nameof(missions));
            Attendance = attendance ?? throw new ArgumentNullException(nameof(attendance));
            Stars = stars ?? throw new ArgumentNullException(nameof(stars));
            Offline = offline ?? throw new ArgumentNullException(nameof(offline));
            Ads = new AdTickets(sessionNonce);
            Converter = new ElapsedConverter();
            Options = options ?? new GameSessionOptions();
        }

        internal void RestoreMeta(long saveRevision, int chapter, int stage)
        {
            SaveRevision = saveRevision;
            Chapter = chapter;
            Stage = stage;
        }

        /// <summary>테스트용: 스테이지를 바꾼다 (진행은 C5).</summary>
        internal void SetStageForTest(int stage)
        {
            if (stage < 1) throw new ArgumentOutOfRangeException(nameof(stage), stage, "1 이상");
            Stage = stage;
        }

        // ---- View·Services가 읽는 값 (Data 형식을 드러내지 않음) ----

        public TimeStatus TimeStatus => Clock.Status;
        /// <summary>
        /// 시계 설정(읽기 전용). Services는 동기화 간격(SyncIntervalMin)·응답 왕복 허용(MaxRttSec)을 여기서 읽는다 (Codex 검토 2026-11-16).
        /// </summary>
        public ClockConfig ClockSettings => Clock.Config;
        public CurrencyAmount Balance(string currencyId) => Wallet.Balance(currencyId);
        public IReadOnlyList<string> CurrencyIds => Wallet.CurrencyIds;
        /// <summary>추정 서버 시각(ms, 내림). 서버 시각을 한 번도 받지 않았으면 null.</summary>
        public long? EstimatedUtcMs => Clock.EstimatedUtcUs == null ? (long?)null : FloorDiv(Clock.EstimatedUtcUs.Value, 1000);
        /// <summary>이 세션의 광고 번호 무작위 값 (만들 때 Services가 넘김, 저장하지 않음).</summary>
        public ulong SessionNonce => Ads.SessionNonce;
        /// <summary>미수령 오프라인 묶음. 없으면 null. 금액은 배율 적용 전.</summary>
        public OfflineBundle OfflineBundle => Offline.Bundle;
        public OfflineConfig OfflineSettings => Offline.Config;
        public IReadOnlyList<MissionStatus> MissionStatuses => Missions.Statuses();
        /// <summary>다음에 받을 출석 차례(1~cycle_days).</summary>
        public int AttendanceNextDay => Attendance.NextDay;
        public int AttendanceCycleDays => Attendance.CycleDays;
        /// <summary>지금 처리한 일일 구간에 출석을 받았는지. 날짜를 모르면 false.</summary>
        public bool AttendanceClaimedToday => Clock.ProcessedDay != null && Attendance.LastClaimedDay == Clock.ProcessedDay;
        public IReadOnlyList<OwnedStar> OwnedStars => Stars.All;

        static long FloorDiv(long a, long b) => a / b - ((a % b != 0) && ((a < 0) != (b < 0)) ? 1 : 0);

        GameNumber OfflineRate(int stage)
        {
            if (!Wallet.Has(StardustId) || Wallet.KindOf(StardustId) != CurrencyKind.GameNumber || OfflineRateForStage == null) return GameNumber.Zero;
            var r = OfflineRateForStage(stage);
            return r.Sign > 0 ? r : GameNumber.Zero;
        }

        // ---- 시간·실행 명령 ----

        /// <summary>
        /// 매 프레임. elapsedSec가 음수·NaN·무한·상한 초과이거나, 추정 시각·활성 경과가 표현 범위를 넘으면 InvalidInput.
        /// 시간 상태(미확인·재확인 필요·TimeWentBack) 때문에는 거부하지 않는다. 일시정지 중에는 무시한다(성공, 이벤트 없음).
        /// 프레임 안의 날짜 경계·유효 시간 끝·전투 칸은 시간순으로 처리한다(Clock.AdvanceTimeline).
        /// </summary>
        public CommandResult Advance(double elapsedSec)
        {
            return Execute("Advance", events =>
            {
                if (!ElapsedConverter.IsValid(elapsedSec)) return ErrorCode.InvalidInput;
                if (RunState == RunState.Paused)
                {
                    LastAdvanceTicks = 0;
                    return null;
                }
                var us = Converter.Peek(elapsedSec, out var residual);
                if (!Clock.CanAdvance(us) || !Offline.CanAddActive(us)) return ErrorCode.InvalidInput;
                // 여기부터 바꾼다
                Converter.Accept(residual);
                var ticks = Battle.Plan(us, Clock.Config.MaxFrameUs, out var pending, out var discarded);
                Battle.Commit(pending, discarded);
                Clock.AdvanceTimeline(us, ticks, (offset, gate) => OnTick(gate, events), Missions.ClearDay, events);
                Offline.AddActive(us);
                LastAdvanceTicks = ticks.Count;
                return null;
            }, CommandKind.Time);
        }

        /// <summary>0.1초 칸 하나. gate는 그 칸 시각의 날짜 의존 조건(null이면 셀 수 있음). 실제 전투는 C5.</summary>
        void OnTick(ErrorCode? gate, List<GameEvent> events)
        {
            if (TestKillsPerTick > 0 && gate == null) Missions.Record("kill_monster", TestKillsPerTick, events);
        }

        /// <summary>
        /// 서버 시각 응답. serverUtcMs는 1970-01-01T00:00:00Z부터의 밀리초(왕복 절반 보정은 Services가 한 번만).
        /// 음수·9999년 뒤면 InvalidInput. 오래된 번호나 일시정지 중의 응답은 버린다(성공, LastSyncOutcome으로 알 수 있음).
        /// 받아들였고 정산 대기면 오프라인을 정산한다(도착이 아니라 받아들일 때).
        /// </summary>
        public CommandResult SyncServerTime(long serverUtcMs, long requestId)
        {
            return Execute("SyncServerTime", events =>
            {
                if (!Clock.IsValidServerUtcMs(serverUtcMs)) return ErrorCode.InvalidInput;
                if (RunState == RunState.Paused)
                {
                    LastSyncOutcome = SyncOutcome.IgnoredPaused;
                    return null;
                }
                LastSyncOutcome = Clock.Sync(serverUtcMs, requestId, events, Missions.ClearDay);
                if (LastSyncOutcome == SyncOutcome.Accepted && SettlementPending)
                {
                    Offline.Settle(Clock.AnchorUtcMs.Value, OfflineRate(Offline.LeaveStage), events);
                    SettlementPending = false;
                }
                return null;
            }, CommandKind.Time);
        }

        /// <summary>백그라운드 진입·종료 직전(광고로 생긴 Unity 일시정지 포함, E1). 활성일 때만 효과가 있다. 정산 대기가 아니면 떠날 때 스테이지를 기록한다.</summary>
        public CommandResult Suspend()
        {
            return Execute("Suspend", events =>
            {
                if (RunState != RunState.Active) return null;
                if (!SettlementPending) Offline.SetLeaveStage(Stage);
                RunState = RunState.Paused;
                return null;
            });
        }

        /// <summary>
        /// 복귀 직후. nextRequestId는 복귀 뒤 첫 요청 번호(마지막으로 보낸 번호 + 1, 1 이상, 아니면 InvalidInput). 일시정지일 때만 효과가 있다:
        /// 활성으로, 시간은 미확인으로, 이보다 작은 번호의 응답은 버림, 정산 대기를 켬.
        /// </summary>
        public CommandResult Resume(long nextRequestId)
        {
            return Execute("Resume", events =>
            {
                if (nextRequestId < 1) return ErrorCode.InvalidInput;
                if (RunState != RunState.Paused) return null;
                RunState = RunState.Active;
                Clock.BeginUnverified(nextRequestId, events);
                SettlementPending = true;
                Converter.Reset();
                return null;
            });
        }

        // ---- 광고 ----

        /// <summary>
        /// 광고 시작 직전. 대상마다 열린 번호는 하나이고 새로 내면 이전 열린 번호는 무효가 된다. AdTicketIssued로 번호를 알린다.
        /// Offline: 미수령 묶음이 없거나 금액이 0이면 NotReady, 이미 광고 적용이면 AlreadyClaimed(X3). 정의되지 않은 대상은 InvalidInput.
        /// 실패하면 번호를 내지 않고 기존 번호 상태도 그대로다.
        /// </summary>
        public CommandResult StartAd(AdTarget target)
        {
            return Execute("StartAd", events =>
            {
                if (!AdTickets.IsTarget(target) || !Ads.CanIssue) return ErrorCode.InvalidInput;
                long bundle = 0;
                if (target == AdTarget.Offline)
                {
                    if (!Offline.HasClaimableBundle) return ErrorCode.NotReady;
                    if (Offline.BundleAdApplied) return ErrorCode.AlreadyClaimed;
                    bundle = Offline.BundleId;
                }
                Ads.Issue(target, bundle, events);
                return null;
            });
        }

        /// <summary>
        /// 오프라인 2배 광고의 보상 획득 콜백 뒤. 연결된 묶음의 광고 적용을 참으로 하고 번호를 사용됨으로 한다(배율을 곱하지 않음).
        /// 시간 상태·실행 상태와 상관없이 된다. 판정 순서(E2): 정확한 번호 → 대상(Offline) → 사용됨(AlreadyClaimed) → 무효·묶음 종료(InvalidTarget).
        /// null이면 InvalidInput.
        /// </summary>
        public CommandResult ConfirmAd(AdTicket ticket)
        {
            return Execute("ConfirmAd", events =>
            {
                var error = Ads.Check(ticket, AdTarget.Offline, Offline.BundleId);
                if (error != null) return error;
                if (!Offline.HasBundle) return ErrorCode.InvalidTarget;
                Offline.ApplyAd();
                Ads.MarkUsed(ticket);
                return null;
            }, usedFor: AdTarget.Offline);
        }

        // ---- 보상 ----

        /// <summary>
        /// 오프라인 수령. 날짜 의존 기능(TimeNotVerified·TimeWentBack이 먼저), 미수령이 없거나 금액이 0이면 NotReady.
        /// 광고 적용이면 ad_multiplier배(곱한 값이 범위를 넘으면 InvalidInput), 아니면 1배를 별가루로 지급하고, 묶음을 끝내고 그 묶음의 열린 광고 번호를 무효로 한다(한 상태 변경).
        /// claim_offline 임무를 1 센다(명령 시각).
        /// </summary>
        public CommandResult ClaimOffline()
        {
            return Execute("ClaimOffline", events =>
            {
                var gate = Clock.DateGate();
                if (gate != null) return gate;
                if (!Wallet.Has(StardustId)) return ErrorCode.NotReady;
                var payoutError = Offline.CheckPayout(out var payout);
                if (payoutError != null) return payoutError;
                var change = CurrencyChange.Of(StardustId, payout);
                var error = Wallet.Check(change);
                if (error != null) return error;
                // 여기부터 바꾼다
                var bundleId = Offline.BundleId;
                Wallet.Apply(change, CurrencyReason.OfflineReward, events);
                Paid("offline:" + bundleId);
                Offline.ClearBundle();
                Ads.RevokeBundle(bundleId);
                Missions.Record("claim_offline", 1, events);
                return null;
            }, CommandKind.DateDependent);
        }

        /// <summary>
        /// 출석 받기. 날짜 의존 기능, 같은 일일 구간에 받았으면 AlreadyClaimed. 받기를 누른 날만 차례가 오른다.
        /// 재화면 CurrencyChanged, 별이면 가진 별 목록에 하나를 더하고 StarObtained(별 생성과 출석 처리는 함께, 별 amount는 1만).
        /// </summary>
        public CommandResult ClaimAttendance()
        {
            return Execute("ClaimAttendance", events =>
            {
                var gate = Clock.DateGate();
                if (gate != null) return gate;
                var today = Clock.ProcessedDay.Value; // 유효하면 처리한 구간이 있다
                var error = Attendance.CheckClaim(today);
                if (error != null) return error;
                var reward = Attendance.NextReward;
                CurrencyChange? change = null;
                if (reward.Kind == AttendanceRewardKind.Currency)
                {
                    if (!TryRewardChange(reward.RewardId, reward.Amount, out var c)) return ErrorCode.InvalidTarget;
                    error = Wallet.Check(c);
                    if (error != null) return error;
                    change = c;
                }
                else
                {
                    // 별 보상은 한 번에 하나(amount 1). 표 검사 4와 표 읽기가 막지만 지급 전에도 본다 (K5)
                    if (reward.Amount != 1) return ErrorCode.InvalidInput;
                    error = Stars.CheckAdd(reward.RewardId);
                    if (error != null) return error;
                }
                // 여기부터 바꾼다
                if (change != null) Wallet.Apply(change.Value, CurrencyReason.AttendanceReward, events);
                else Stars.Add(reward.RewardId, events);
                Paid("attendance:" + today);
                Attendance.MarkClaimed(today);
                return null;
            }, CommandKind.DateDependent);
        }

        /// <summary>임무 보상 받기. 표에 없는 id → InvalidTarget, 날짜 의존 기능, 받았으면 AlreadyClaimed, 덜 했으면 NotReady.</summary>
        public CommandResult ClaimMission(string missionId)
        {
            return Execute("ClaimMission", events =>
            {
                var def = Missions.Find(missionId);
                if (def == null) return ErrorCode.InvalidTarget;
                var gate = Clock.DateGate();
                if (gate != null) return gate;
                var error = Missions.CheckClaim(missionId);
                if (error != null) return error;
                if (!TryRewardChange(def.RewardCurrency, def.RewardAmount, out var change)) return ErrorCode.InvalidTarget;
                error = Wallet.Check(change);
                if (error != null) return error;
                // 여기부터 바꾼다
                Wallet.Apply(change, CurrencyReason.MissionReward, events);
                Paid("mission:" + missionId + ":" + Clock.ProcessedDay.Value);
                Missions.MarkClaimed(missionId);
                return null;
            }, CommandKind.DateDependent);
        }

        bool TryRewardChange(string currencyId, long amount, out CurrencyChange change)
        {
            change = default;
            if (!Wallet.Has(currencyId)) return false;
            change = Wallet.KindOf(currencyId) == CurrencyKind.Integer ? CurrencyChange.Of(currencyId, amount) : CurrencyChange.Of(currencyId, GameNumber.From(amount));
            return true;
        }

        void Paid(string key)
        {
            if (Options.CheckInvariants) Invariants.CheckClaimOnce(key, _paid, _violations);
        }

        // ---- 저장 ----

        /// <summary>
        /// 저장 번호를 1 올리고 그 상태의 고정된 사본을 만든다. 정산 대기가 아니면 떠날 때 스테이지를 지금 스테이지로 기록한 뒤 쓴다.
        /// Services는 이 전에 처리 시점까지의 미반영 경과를 Advance해 둔다.
        /// </summary>
        public SaveSnapshot CreateSave()
        {
            if (SaveRevision == long.MaxValue) throw new InvalidOperationException("저장 번호가 끝에 닿음");
            if (!SettlementPending) Offline.SetLeaveStage(Stage);
            var revision = SaveRevision + 1;
            var json = SaveFormat.Write(this, revision);
            SaveRevision = revision;
            return new SaveSnapshot(revision, json, Summary(revision));
        }

        /// <summary>
        /// 두 기기 충돌에서 이 기기를 골랐을 때: 다음 저장 번호를 클라우드 번호 + 1로 한다 (설계서 3절 "두 기기 충돌"). 세션은 그대로다.
        /// 음수면 InvalidInput.
        /// </summary>
        public CommandResult AdoptCloudRevision(long cloudRevision)
        {
            return Execute("AdoptCloudRevision", events =>
            {
                if (cloudRevision < 0 || cloudRevision == long.MaxValue) return ErrorCode.InvalidInput;
                SaveRevision = cloudRevision;
                return null;
            });
        }

        internal SaveSummary Summary(long revision)
        {
            var dust = Wallet.Has(StardustId) && Wallet.KindOf(StardustId) == CurrencyKind.GameNumber ? Wallet.Balance(StardustId).Number : GameNumber.Zero;
            return new SaveSummary(revision, Chapter, dust, EstimatedUtcMs);
        }

        // ---- 실행 틀 ----

        internal enum CommandKind
        {
            /// <summary>Advance·SyncServerTime: 날짜 의존 상태를 시각마다 스스로 판단한다.</summary>
            Time,
            /// <summary>출석·임무·오프라인 수령: 명령 시작 때 날짜 조건이 맞아야 날짜 의존 상태를 바꿀 수 있다.</summary>
            DateDependent,
            /// <summary>그 밖: 날짜 의존 상태를 바꾸면 안 된다(광고 적용 표시는 예외).</summary>
            Other,
        }

        /// <summary>
        /// 명령 하나를 실행한다. body는 조건을 모두 확인한 뒤에만 상태를 바꾸고, 실패면 실패 코드를 돌려준다.
        /// 위반 검사를 켜면 실패 전후 상태를 비교하고(규칙 7) 끝난 뒤 규칙을 본다.
        /// </summary>
        internal CommandResult Execute(string name, Func<List<GameEvent>, ErrorCode?> body, CommandKind kind = CommandKind.Other, AdTarget? usedFor = null)
        {
            var check = Options.CheckInvariants;
            var before = check ? Capture() : null;
            var gateBefore = check ? Clock.DateGate() : null;
            var events = new List<GameEvent>();
            var error = body(events);
            if (check)
            {
                if (error != null) Invariants.CheckFailedCommandUnchanged(name, Capture().Same(before), _violations);
                CheckRules(name, before, gateBefore, kind, usedFor);
            }
            return error != null ? CommandResult.Fail(error.Value) : CommandResult.Success(events);
        }

        void CheckRules(string name, StateCopy before, ErrorCode? gateBefore, CommandKind kind, AdTarget? usedFor)
        {
            var balances = new List<KeyValuePair<string, CurrencyAmount>>();
            foreach (var id in Wallet.CurrencyIds) balances.Add(new KeyValuePair<string, CurrencyAmount>(id, Wallet.Balance(id)));
            Invariants.CheckCurrencies(balances, _violations);
            Invariants.CheckPeriodsForward(before.Clock.ProcessedDay, before.Clock.ProcessedWeek, Clock.ProcessedDay, Clock.ProcessedWeek, _violations);
            Invariants.CheckSettlementForward(before.Offline.SettlementPointUtcMs, Offline.SettlementPointUtcMs, _violations);
            Invariants.CheckOfflineBundle(Offline.HasBundle, Offline.BundleMinutes, Offline.BundleAmount, Offline.Config.MaxMinutes, Offline.Config.AdMultiplier, _violations);
            Invariants.CheckTickets(before.Ads.States(), Ads.States(), usedFor, _violations);

            var accrued = Offline.BundleMinutes > before.Offline.BundleMinutes;
            Invariants.CheckOfflineAccrualValid(accrued, Clock.Status == TimeStatus.Valid, _violations);
            if (kind != CommandKind.Time)
            {
                // 광고 적용 표시(ConfirmAd)와 떠날 때 스테이지(Suspend·저장)는 날짜 의존 상태가 아니다
                var offlineChanged = before.Offline.BundleId != Offline.BundleId || before.Offline.BundleMinutes != Offline.BundleMinutes
                    || !before.Offline.BundleAmount.Equals(Offline.BundleAmount);
                var changed = !before.Missions.SameState(Missions) || !before.Attendance.SameState(Attendance) || offlineChanged;
                Invariants.CheckDateDependent(name, gateBefore, changed, _violations);
            }
        }

        /// <summary>판정 상태 전체의 사본 (실패 전후 비교용).</summary>
        internal sealed class StateCopy
        {
            public Wallet Wallet;
            public Clock Clock;
            public BattleAccumulator Battle;
            public GameRng Rng;
            public Missions Missions;
            public Attendance Attendance;
            public StarInventory Stars;
            public OfflineRewards Offline;
            public AdTickets Ads;
            public double Residual;
            public RunState Run;
            public bool Pending;
            public long Revision;
            public int Chapter;
            public int Stage;

            public bool Same(StateCopy o)
            {
                return Wallet.SameState(o.Wallet) && Clock.SameState(o.Clock) && Battle.SameState(o.Battle) && Rng.SameState(o.Rng)
                    && Missions.SameState(o.Missions) && Attendance.SameState(o.Attendance) && Stars.SameState(o.Stars)
                    && Offline.SameState(o.Offline) && Ads.SameState(o.Ads)
                    && Residual.Equals(o.Residual) && Run == o.Run && Pending == o.Pending && Revision == o.Revision && Chapter == o.Chapter && Stage == o.Stage;
            }
        }

        internal StateCopy Capture()
        {
            return new StateCopy
            {
                Wallet = Wallet.Clone(), Clock = Clock.Clone(), Battle = Battle.Clone(), Rng = Rng.Clone(),
                Missions = Missions.Clone(), Attendance = Attendance.Clone(), Stars = Stars.Clone(), Offline = Offline.Clone(), Ads = Ads.Clone(),
                Residual = Converter.ResidualUs, Run = RunState, Pending = SettlementPending, Revision = SaveRevision, Chapter = Chapter, Stage = Stage,
            };
        }
    }
}
