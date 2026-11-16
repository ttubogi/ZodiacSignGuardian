using System;
using System.Collections.Generic;

namespace ZodiacSignGuardian.Core.Commands
{
    /// <summary>
    /// 명령이 실패한 이유. 설계서 v0.7 3절의 아홉 가지 그대로이며, 늘리거나 바꾸려면 3절을 먼저 고치고 Codex와 확인한다.
    /// 숫자 값은 저장·기록에 남을 수 있으므로 바꾸지 않는다.
    /// </summary>
    public enum ErrorCode
    {
        NotEnoughCurrency = 1,
        NotUnlocked = 2,
        AlreadyClaimed = 3,
        NotReady = 4,
        InvalidTarget = 5,
        StarNotOwned = 6,
        TimeNotVerified = 7,
        TimeWentBack = 8,
        /// <summary>잘못된 입력(음수·NaN elapsedSec 등). 정수 재화가 long 최대값을 넘을 때도 이 코드 (C2 결정 Q5).</summary>
        InvalidInput = 9,
    }

    /// <summary>판정이 화면에 알리는 일. 종류별 이벤트는 각 모듈에서 이 클래스를 이어 만든다.</summary>
    public abstract class GameEvent
    {
    }

    /// <summary>
    /// 모든 명령의 결과: 성공 여부, 실패 이유, 이벤트. (설계서 3절 "결과·이벤트·상태")
    /// 실패한 명령은 상태를 바꾸지 않으므로 이벤트도 없다.
    /// </summary>
    public sealed class CommandResult
    {
        static readonly IReadOnlyList<GameEvent> NoEvents = Array.Empty<GameEvent>();
        static readonly CommandResult OkEmpty = new CommandResult(true, null, NoEvents);

        public bool Ok { get; }
        /// <summary>실패했을 때만 값이 있다.</summary>
        public ErrorCode? Error { get; }
        public IReadOnlyList<GameEvent> Events { get; }

        CommandResult(bool ok, ErrorCode? error, IReadOnlyList<GameEvent> events)
        {
            Ok = ok;
            Error = error;
            Events = events;
        }

        public static CommandResult Success() => OkEmpty;

        public static CommandResult Success(IReadOnlyList<GameEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (events.Count == 0) return OkEmpty;
            // 바깥에서 목록을 고쳐도 결과가 바뀌지 않게 복사한다
            var copy = new GameEvent[events.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = events[i] ?? throw new ArgumentException("이벤트 목록에 null이 있음", nameof(events));
            }
            return new CommandResult(true, null, copy);
        }

        public static CommandResult Fail(ErrorCode error)
        {
            if (!Enum.IsDefined(typeof(ErrorCode), error)) throw new ArgumentOutOfRangeException(nameof(error), error, "정의되지 않은 실패 코드");
            return new CommandResult(false, error, NoEvents);
        }

        public override string ToString() => Ok ? "Ok (이벤트 " + Events.Count + "개)" : "Fail " + Error;
    }
}
