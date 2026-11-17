using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Timing
{
    /// <summary>시간 상태 (설계서 3절 "시간 규칙"). 숫자 값은 바꾸지 않는다.</summary>
    public enum TimeStatus
    {
        /// <summary>시작·복귀 뒤 첫 동기화 전</summary>
        Unverified = 0,
        /// <summary>서버 시각을 받았고 동기화 누적 시간이 anchor_valid_min 이하</summary>
        Valid = 1,
        /// <summary>동기화 누적 시간이 anchor_valid_min 초과</summary>
        NeedsResync = 2,
    }

    public sealed class TimeStatusChanged : GameEvent
    {
        public TimeStatus Status { get; }
        public TimeStatusChanged(TimeStatus status) { Status = status; }
        public override string ToString() => "TimeStatusChanged " + Status;
    }

    /// <summary>일일 구간이 넘어감. Previous가 null이면 처음 정한 날. 빠진 날이 있어도 한 번만 나온다(빠진 날의 출석은 주지 않음).</summary>
    public sealed class DayReset : GameEvent
    {
        public long? Previous { get; }
        public long Day { get; }
        public DayReset(long? previous, long day) { Previous = previous; Day = day; }
        public override string ToString() => "DayReset " + Previous + " → " + Day;
    }

    /// <summary>주간 구간이 넘어감. Previous가 null이면 처음 정한 주.</summary>
    public sealed class WeekReset : GameEvent
    {
        public long? Previous { get; }
        public long Week { get; }
        public WeekReset(long? previous, long week) { Previous = previous; Week = week; }
        public override string ToString() => "WeekReset " + Previous + " → " + Week;
    }

    /// <summary>동기화 응답을 어떻게 처리했는지.</summary>
    public enum SyncOutcome
    {
        Accepted = 0,
        /// <summary>최소 허용 번호보다 작거나 이미 처리한 번호 이하라 버림 (실패가 아니다)</summary>
        IgnoredStale = 1,
        /// <summary>일시정지 중에 와서 버림. 복귀 뒤 요청으로만 받아들인다(V2).</summary>
        IgnoredPaused = 2,
    }

    /// <summary>
    /// 서버 시각 기준점과 날짜 구간. (설계서 3절 "시간 규칙", 결정 기록 2026-11-16)
    /// <list type="bullet">
    /// <item>시각은 1970-01-01T00:00:00Z부터의 밀리초(serverUtcMs)로 받고, 안에서는 마이크로초로 계산한다: 추정 시각 = 기준 시각 × 1000 + 동기화 누적 µs.
    ///       밀리초로 되돌리지 않으므로 남는 µs를 버리는 일이 없다.</item>
    /// <item>서버 시각은 항상 믿는다(V3). 거르는 것은 음수와 9999-12-31 뒤의 값뿐이다(InvalidInput).</item>
    /// <item>응답 번호: 최소 허용 번호보다 작거나 이미 받아들인 번호 이하면 버린다. 받아들이면 기준 시각 = 값, 동기화 누적 = 0, 상태 = 유효.</item>
    /// <item>일일 구간 id = (현지 시각 − reset_hour) 기준 1970-01-01부터의 날 수(내림). 주간 구간은 weekly 규칙의 weekday·reset_hour로 같은 방식.</item>
    /// <item>구간은 유효일 때만, 처리한 구간보다 뒤일 때만 넘긴다. 처리한 구간은 뒤로 가지 않는다(위반 규칙 10).</item>
    /// </list>
    /// 상태를 바꾸는 메서드는 GameSession이 입력을 확인한 뒤에 부른다. 여기서도 잘못된 값이면 예외(코드 잘못).
    /// </summary>
    public sealed class Clock
    {
        /// <summary>serverUtcMs의 상한: 9999-12-31T23:59:59.999Z.</summary>
        public const long MaxServerUtcMs = 253402300799999L;

        /// <summary>
        /// 추정 시각(µs)의 상한: 9999-12-31T23:59:59.999999Z. 받는 서버 시각, 추정 시각, 저장의 lastSavedUtcMs, 불러올 때의 값이
        /// 모두 이 안에 있으므로, 허용한 입력으로 만든 상태는 언제나 다시 저장하고 불러올 수 있다 (Codex 검토 2026-11-16).
        /// </summary>
        public const long MaxEstimatedUtcUs = MaxServerUtcMs * 1000L + 999L;

        public ClockConfig Config { get; }

        // ---- 저장하는 값 ----
        /// <summary>마지막으로 받아들인 서버 시각(ms). 한 번도 받지 않았으면 null.</summary>
        public long? AnchorUtcMs { get; private set; }
        /// <summary>기준 시각 뒤 elapsed 합(µs, 자르기 전).</summary>
        public long SyncElapsedUs { get; private set; }
        public long? ProcessedDay { get; private set; }
        public long? ProcessedWeek { get; private set; }

        // ---- 저장하지 않는 값 ----
        public TimeStatus Status { get; private set; } = TimeStatus.Unverified;
        /// <summary>이보다 작은 번호의 응답은 버린다. 앱 시작 때 1.</summary>
        public long MinRequestId { get; private set; } = 1;
        /// <summary>마지막으로 받아들인 번호. 앱 시작 때 0.</summary>
        public long LastAcceptedRequestId { get; private set; }

        public Clock(ClockConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // ---- 입력 확인 ----

        public static bool IsValidServerUtcMs(long serverUtcMs) => serverUtcMs >= 0 && serverUtcMs <= MaxServerUtcMs;

        /// <summary>
        /// elapsed µs를 더해도 추정 시각이 MaxEstimatedUtcUs를 넘지 않는지. 기준 시각이 없으면 쌓지 않으므로 늘 된다(음수만 안 됨).
        /// </summary>
        public bool CanAdvance(long elapsedUs)
        {
            if (elapsedUs < 0) return false;
            if (AnchorUtcMs == null) return true;
            // 기준 시각 × 1000 + 누적 <= MaxEstimatedUtcUs가 늘 지켜지므로 남은 여유와 비교하면 넘치지 않는다
            return elapsedUs <= MaxEstimatedUtcUs - (AnchorUtcMs.Value * 1000L + SyncElapsedUs);
        }

        /// <summary>처리한 일일·주간 구간 id가 가질 수 있는 범위 (1970-01-01T00:00Z ~ MaxEstimatedUtcUs의 구간).</summary>
        public bool IsPossibleDay(long day) => day >= DayIdAt(0) && day <= DayIdAt(MaxEstimatedUtcUs);
        public bool IsPossibleWeek(long week) => week >= WeekIdAt(0) && week <= WeekIdAt(MaxEstimatedUtcUs);

        // ---- 추정 시각과 구간 ----

        /// <summary>추정 시각(µs). 기준 시각이 없으면 null. 상태가 유효가 아니어도 값은 있다.</summary>
        public long? EstimatedUtcUs => AnchorUtcMs == null ? (long?)null : AnchorUtcMs.Value * 1000L + SyncElapsedUs;

        public long DayIdAt(long utcUs) => FloorDiv(LocalUs(utcUs) - Config.DailyResetHour * ClockConfig.UsPerHour, ClockConfig.UsPerDay);

        public long WeekIdAt(long utcUs)
        {
            var day = FloorDiv(LocalUs(utcUs) - Config.WeeklyResetHour * ClockConfig.UsPerHour, ClockConfig.UsPerDay);
            // 1970-01-01은 목요일(월 0 기준 3). 이 날의 요일 = (day + 3) mod 7. weekday 요일에 새 주가 시작된다.
            return FloorDiv(day + 3 - Config.WeeklyWeekday, 7);
        }

        /// <summary>일일 구간 id가 day인 구간이 시작하는 UTC µs.</summary>
        public long DayStartUtcUs(long day) => day * ClockConfig.UsPerDay + Config.DailyResetHour * ClockConfig.UsPerHour - Config.UtcOffsetMin * ClockConfig.UsPerMin;

        long LocalUs(long utcUs) => utcUs + Config.UtcOffsetMin * ClockConfig.UsPerMin;

        static long FloorDiv(long a, long b)
        {
            var q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>
        /// 날짜 의존 기능(출석·무료 뽑기·일일 임무 진행·수령·오프라인 수령)을 지금 할 수 있으면 null.
        /// 유효가 아니면 TimeNotVerified, 추정 시각의 구간이 처리한 구간보다 앞이면(서버가 뒤로 감) TimeWentBack.
        /// </summary>
        public ErrorCode? DateGate()
        {
            if (Status != TimeStatus.Valid || EstimatedUtcUs == null) return ErrorCode.TimeNotVerified;
            var now = EstimatedUtcUs.Value;
            if (ProcessedDay != null && DayIdAt(now) < ProcessedDay.Value) return ErrorCode.TimeWentBack;
            if (ProcessedWeek != null && WeekIdAt(now) < ProcessedWeek.Value) return ErrorCode.TimeWentBack;
            return null;
        }

        /// <summary>
        /// 전투 구간 [endUs − lengthUs, endUs)를 일일 구간 경계에서 나눈다 (05시를 걸친 프레임의 처치를 전날·새날에 따로 세기 위해).
        /// 앞 구간부터 (일일 구간 id, 길이 µs)를 돌려준다. 한 프레임은 최대 max_frame_sec이라 많아야 두 조각이지만 일반적으로 나눈다.
        /// </summary>
        public IReadOnlyList<KeyValuePair<long, long>> SplitByDay(long endUs, long lengthUs)
        {
            if (lengthUs < 0) throw new ArgumentOutOfRangeException(nameof(lengthUs), lengthUs, "0 이상");
            var parts = new List<KeyValuePair<long, long>>();
            var start = endUs - lengthUs;
            while (start < endUs)
            {
                var day = DayIdAt(start);
                var next = DayStartUtcUs(day + 1);
                var stop = next < endUs ? next : endUs;
                parts.Add(new KeyValuePair<long, long>(day, stop - start));
                start = stop;
            }
            return parts;
        }

        // ---- 바꾸기 ----

        /// <summary>
        /// 동기화 응답. 서버 시각은 IsValidServerUtcMs를 통과해야 한다(아니면 예외). 받아들이면 상태가 유효가 되고 구간을 넘긴다.
        /// </summary>
        public SyncOutcome Sync(long serverUtcMs, long requestId, List<GameEvent> events)
        {
            if (!IsValidServerUtcMs(serverUtcMs)) throw new ArgumentOutOfRangeException(nameof(serverUtcMs), serverUtcMs, "IsValidServerUtcMs를 먼저 확인해야 함");
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (requestId < MinRequestId || requestId <= LastAcceptedRequestId) return SyncOutcome.IgnoredStale;
            AnchorUtcMs = serverUtcMs;
            SyncElapsedUs = 0;
            LastAcceptedRequestId = requestId;
            SetStatus(TimeStatus.Valid, events);
            RollPeriods(events);
            return SyncOutcome.Accepted;
        }

        /// <summary>
        /// 경과 시간(µs, 자르기 전)을 더한다. CanAdvance를 통과해야 한다(아니면 예외).
        /// 서버 시각을 한 번도 받지 않았으면(기준 시각 없음) 쌓지 않는다: 기준이 없는 시간은 쓸 데가 없고 첫 동기화 때 어차피 0이 된다.
        /// 그래서 "기준 시각이 없으면 동기화 누적은 0"이 늘 지켜지고, 저장 검사도 이 조건으로 엄격하게 둔다. (2026-11-17 결정 ①, Codex 검토)
        /// </summary>
        public void Advance(long elapsedUs, List<GameEvent> events)
        {
            if (!CanAdvance(elapsedUs)) throw new ArgumentOutOfRangeException(nameof(elapsedUs), elapsedUs, "CanAdvance를 먼저 확인해야 함");
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (AnchorUtcMs == null) return;
            SyncElapsedUs += elapsedUs;
            if (Status == TimeStatus.Valid && SyncElapsedUs > Config.AnchorValidUs) SetStatus(TimeStatus.NeedsResync, events);
            RollPeriods(events);
        }

        /// <summary>복귀·앱 시작: 미확인으로, 최소 허용 번호를 nextRequestId로. nextRequestId는 1 이상(아니면 예외).</summary>
        public void BeginUnverified(long nextRequestId, List<GameEvent> events)
        {
            if (nextRequestId < 1) throw new ArgumentOutOfRangeException(nameof(nextRequestId), nextRequestId, "1 이상");
            if (events == null) throw new ArgumentNullException(nameof(events));
            MinRequestId = nextRequestId;
            SetStatus(TimeStatus.Unverified, events);
        }

        void SetStatus(TimeStatus status, List<GameEvent> events)
        {
            if (Status == status) return;
            Status = status;
            events.Add(new TimeStatusChanged(status));
        }

        void RollPeriods(List<GameEvent> events)
        {
            if (Status != TimeStatus.Valid || EstimatedUtcUs == null) return;
            var now = EstimatedUtcUs.Value;
            var day = DayIdAt(now);
            if (ProcessedDay == null || day > ProcessedDay.Value)
            {
                events.Add(new DayReset(ProcessedDay, day));
                ProcessedDay = day;
            }
            var week = WeekIdAt(now);
            if (ProcessedWeek == null || week > ProcessedWeek.Value)
            {
                events.Add(new WeekReset(ProcessedWeek, week));
                ProcessedWeek = week;
            }
        }

        // ---- 저장 ----

        /// <summary>
        /// 저장한 값으로 되돌린다(앱 시작). 상태는 미확인, 최소 허용 번호 1, 받아들인 번호 0.
        /// 값이 맞지 않으면(음수 누적, 범위 밖 기준 시각, 처리한 구간이 있는데 기준 시각이 없음 등) false이고 상태는 그대로.
        /// </summary>
        public bool TryRestore(long? anchorUtcMs, long syncElapsedUs, long? processedDay, long? processedWeek)
        {
            if (anchorUtcMs != null && !IsValidServerUtcMs(anchorUtcMs.Value)) return false;
            if (syncElapsedUs < 0) return false;
            if (anchorUtcMs == null && (syncElapsedUs != 0 || processedDay != null || processedWeek != null)) return false;
            if (anchorUtcMs != null && syncElapsedUs > MaxEstimatedUtcUs - anchorUtcMs.Value * 1000L) return false;
            // 처리한 구간은 가능한 범위 안이어야 한다. 서버 시각이 뒤로 보정되는 것은 허용하므로(V3)
            // "처리한 구간 <= 지금 추정 구간"은 강제하지 않는다 (Codex 검토 2026-11-16).
            if (processedDay != null && !IsPossibleDay(processedDay.Value)) return false;
            if (processedWeek != null && !IsPossibleWeek(processedWeek.Value)) return false;
            AnchorUtcMs = anchorUtcMs;
            SyncElapsedUs = syncElapsedUs;
            ProcessedDay = processedDay;
            ProcessedWeek = processedWeek;
            Status = TimeStatus.Unverified;
            MinRequestId = 1;
            LastAcceptedRequestId = 0;
            return true;
        }

        public bool SameState(Clock other)
        {
            return other != null && AnchorUtcMs == other.AnchorUtcMs && SyncElapsedUs == other.SyncElapsedUs
                && ProcessedDay == other.ProcessedDay && ProcessedWeek == other.ProcessedWeek && Status == other.Status
                && MinRequestId == other.MinRequestId && LastAcceptedRequestId == other.LastAcceptedRequestId;
        }

        public Clock Clone()
        {
            return new Clock(Config)
            {
                AnchorUtcMs = AnchorUtcMs, SyncElapsedUs = SyncElapsedUs, ProcessedDay = ProcessedDay, ProcessedWeek = ProcessedWeek,
                Status = Status, MinRequestId = MinRequestId, LastAcceptedRequestId = LastAcceptedRequestId,
            };
        }
    }
}
