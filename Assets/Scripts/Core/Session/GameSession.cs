using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Randomness;
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
    /// 판정의 입구. 명령을 받아 ok·error·events를 돌려준다. (설계서 3절, 4절 GameSession)
    /// <list type="bullet">
    /// <item>명령은 조건을 모두 확인한 뒤에만 상태를 바꾼다. 실패하면 상태는 그대로이고 이벤트도 없다 (C2 결정 Q4).</item>
    /// <item>위반 검사를 켜면 명령마다 규칙을 보고 Violations에 쌓는다. 실패한 명령은 전후 상태를 비교한다(규칙 7).</item>
    /// <item>C2 범위: 시간(Advance·SyncServerTime), 실행(Suspend·Resume), 저장 사본. 전투·보상·오프라인 정산은 C3·C5.</item>
    /// </list>
    /// 만들기와 불러오기는 GameSessionLoader가 한다(표 묶음을 받는 쪽은 Services).
    /// </summary>
    public sealed class GameSession
    {
        public const int SaveVersion = 1;
        /// <summary>요약에 쓰는 별가루 재화 id.</summary>
        public const string StardustId = "stardust";

        internal Wallet Wallet { get; private set; }
        internal Clock Clock { get; private set; }
        internal BattleAccumulator Battle { get; private set; }
        internal GameRng Rng { get; private set; }
        internal ElapsedConverter Converter { get; private set; }

        public GameSessionOptions Options { get; }
        public RunState RunState { get; private set; } = RunState.Active;
        /// <summary>정산 대기. 앱 시작·Resume 때 켜진다. 오프라인 정산(C3)이 끈다.</summary>
        public bool SettlementPending { get; private set; } = true;
        /// <summary>마지막으로 낸 저장 번호. 새 계정은 0이고 첫 저장이 1.</summary>
        public long SaveRevision { get; private set; }
        /// <summary>진행 중인 장. 전투·진행(C5) 전까지는 1.</summary>
        public int Chapter { get; private set; } = 1;

        /// <summary>마지막 Advance에서 지난 0.1초 칸 수 (C5 전투 전까지 확인용).</summary>
        public long LastAdvanceTicks { get; private set; }
        /// <summary>마지막 SyncServerTime을 어떻게 처리했는지.</summary>
        public SyncOutcome? LastSyncOutcome { get; private set; }

        readonly List<InvariantViolation> _violations = new List<InvariantViolation>();
        /// <summary>위반 검사를 켰을 때 찾은 것 (쌓임).</summary>
        public IReadOnlyList<InvariantViolation> Violations => _violations;

        internal GameSession(Wallet wallet, Clock clock, BattleAccumulator battle, GameRng rng, GameSessionOptions options)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Converter = new ElapsedConverter();
            Options = options ?? new GameSessionOptions();
        }

        internal void RestoreMeta(long saveRevision, int chapter)
        {
            SaveRevision = saveRevision;
            Chapter = chapter;
        }

        // ---- View·Services가 읽는 값 (Data 형식을 드러내지 않음) ----

        public TimeStatus TimeStatus => Clock.Status;
        public CurrencyAmount Balance(string currencyId) => Wallet.Balance(currencyId);
        public IReadOnlyList<string> CurrencyIds => Wallet.CurrencyIds;
        /// <summary>추정 서버 시각(ms, 내림). 서버 시각을 한 번도 받지 않았으면 null.</summary>
        public long? EstimatedUtcMs => Clock.EstimatedUtcUs == null ? (long?)null : FloorDiv(Clock.EstimatedUtcUs.Value, 1000);

        static long FloorDiv(long a, long b) => a / b - ((a % b != 0) && ((a < 0) != (b < 0)) ? 1 : 0);

        // ---- 명령 ----

        /// <summary>매 프레임. elapsedSec가 음수·NaN·무한·상한 초과면 InvalidInput. 일시정지 중에는 무시한다(성공, 이벤트 없음).</summary>
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
                if (!Clock.CanAdvance(us)) return ErrorCode.InvalidInput;
                // 여기부터 바꾼다
                Converter.Accept(residual);
                Clock.Advance(us, events);
                LastAdvanceTicks = Battle.Add(us, Clock.Config.MaxFrameUs);
                return null;
            });
        }

        /// <summary>
        /// 서버 시각 응답. serverUtcMs는 1970-01-01T00:00:00Z부터의 밀리초(왕복 절반 보정은 Services가 한 번만).
        /// 음수·9999년 뒤면 InvalidInput. 오래된 번호나 일시정지 중의 응답은 버린다(성공, LastSyncOutcome으로 알 수 있음).
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
                LastSyncOutcome = Clock.Sync(serverUtcMs, requestId, events);
                return null;
            });
        }

        /// <summary>백그라운드 진입·종료 직전. 활성일 때만 효과가 있다.</summary>
        public CommandResult Suspend()
        {
            return Execute("Suspend", events =>
            {
                if (RunState == RunState.Active) RunState = RunState.Paused;
                return null;
            });
        }

        /// <summary>
        /// 복귀 직후. nextRequestId는 복귀 뒤 첫 요청 번호(1 이상, 아니면 InvalidInput). 일시정지일 때만 효과가 있다:
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

        // ---- 저장 ----

        /// <summary>저장 번호를 1 올리고 그 상태의 고정된 사본을 만든다.</summary>
        public SaveSnapshot CreateSave()
        {
            if (SaveRevision == long.MaxValue) throw new InvalidOperationException("저장 번호가 끝에 닿음");
            var revision = SaveRevision + 1;
            var json = SaveFormat.Write(this, revision);
            SaveRevision = revision;
            return new SaveSnapshot(revision, json, Summary(revision));
        }

        /// <summary>
        /// 두 기기 충돌에서 이 기기를 골랐을 때: 다음 저장 번호를 클라우드 번호 + 1로 한다 (설계서 3절 "두 기기 충돌").
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

        /// <summary>
        /// 명령 하나를 실행한다. body는 조건을 모두 확인한 뒤에만 상태를 바꾸고, 실패면 실패 코드를 돌려준다.
        /// 위반 검사를 켜면 실패 전후 상태를 비교하고(규칙 7) 끝난 뒤 규칙을 본다.
        /// </summary>
        internal CommandResult Execute(string name, Func<List<GameEvent>, ErrorCode?> body)
        {
            var check = Options.CheckInvariants;
            var before = check ? Capture() : null;
            var events = new List<GameEvent>();
            var error = body(events);
            if (check)
            {
                if (error != null) Invariants.CheckFailedCommandUnchanged(name, Capture().Same(before), _violations);
                CheckRules(before);
            }
            return error != null ? CommandResult.Fail(error.Value) : CommandResult.Success(events);
        }

        void CheckRules(StateCopy before)
        {
            var balances = new List<KeyValuePair<string, CurrencyAmount>>();
            foreach (var id in Wallet.CurrencyIds) balances.Add(new KeyValuePair<string, CurrencyAmount>(id, Wallet.Balance(id)));
            Invariants.CheckCurrencies(balances, _violations);
            Invariants.CheckPeriodsForward(before.Clock.ProcessedDay, before.Clock.ProcessedWeek, Clock.ProcessedDay, Clock.ProcessedWeek, _violations);
        }

        /// <summary>판정 상태 전체의 사본 (실패 전후 비교용).</summary>
        internal sealed class StateCopy
        {
            public Wallet Wallet;
            public Clock Clock;
            public BattleAccumulator Battle;
            public GameRng Rng;
            public double Residual;
            public RunState Run;
            public bool Pending;
            public long Revision;
            public int Chapter;

            public bool Same(StateCopy o)
            {
                return Wallet.SameState(o.Wallet) && Clock.SameState(o.Clock) && Battle.SameState(o.Battle) && Rng.SameState(o.Rng)
                    && Residual.Equals(o.Residual) && Run == o.Run && Pending == o.Pending && Revision == o.Revision && Chapter == o.Chapter;
            }
        }

        internal StateCopy Capture()
        {
            return new StateCopy
            {
                Wallet = Wallet.Clone(), Clock = Clock.Clone(), Battle = Battle.Clone(), Rng = Rng.Clone(),
                Residual = Converter.ResidualUs, Run = RunState, Pending = SettlementPending, Revision = SaveRevision, Chapter = Chapter,
            };
        }
    }
}
