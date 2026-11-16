using System.Collections.Generic;
using ZodiacSignGuardian.Core.Economy;

namespace ZodiacSignGuardian.Core.Session
{
    /// <summary>위반 규칙 하나를 어긴 것. Rule은 설계서 5절의 번호(1~10).</summary>
    public sealed class InvariantViolation
    {
        public int Rule { get; }
        public string Message { get; }

        public InvariantViolation(int rule, string message)
        {
            Rule = rule;
            Message = message;
        }

        public override string ToString() => "[규칙" + Rule + "] " + Message;
    }

    /// <summary>
    /// 위반 규칙 검사 (설계서 5절). 개발·테스트 빌드에서 명령마다 돈다(GameSessionOptions.CheckInvariants, C2 결정 Q7).
    /// 출시 빌드에서도 하는 입력 검사(InvalidInput, 저장 읽기 검사)와는 다르다: 이것은 코드가 규칙을 어겼는지 보는 것이다.
    /// 검사는 값만 받아 판단하는 함수로 두어, 테스트에서 망가뜨린 값을 바로 넣어 볼 수 있게 한다.
    /// C2에서는 1·7·10번. 나머지는 해당 모듈을 만드는 단계에서 더한다.
    /// </summary>
    public static class Invariants
    {
        /// <summary>규칙 1: 재화는 음수가 되지 않는다. (모든 증감에 이유가 남는 것은 Wallet.Apply가 이유 없이는 바꿀 수 없게 막는다)</summary>
        public static void CheckCurrencies(IEnumerable<KeyValuePair<string, CurrencyAmount>> balances, List<InvariantViolation> found)
        {
            foreach (var b in balances)
            {
                if (b.Value.Sign < 0) found.Add(new InvariantViolation(1, "재화 " + b.Key + "가 음수: " + b.Value));
            }
        }

        /// <summary>규칙 7: 실패한 명령은 상태를 바꾸지 않는다.</summary>
        public static void CheckFailedCommandUnchanged(string command, bool stateUnchanged, List<InvariantViolation> found)
        {
            if (!stateUnchanged) found.Add(new InvariantViolation(7, command + "이(가) 실패했는데 상태가 바뀜"));
        }

        /// <summary>규칙 10: 처리한 일일·주간 구간은 뒤로 가지 않는다. (정산 기준 시점은 C3에서 더함)</summary>
        public static void CheckPeriodsForward(long? dayBefore, long? weekBefore, long? dayAfter, long? weekAfter, List<InvariantViolation> found)
        {
            if (dayBefore != null && (dayAfter == null || dayAfter.Value < dayBefore.Value))
            {
                found.Add(new InvariantViolation(10, "처리한 일일 구간이 뒤로 감: " + dayBefore + " → " + (dayAfter?.ToString() ?? "없음")));
            }
            if (weekBefore != null && (weekAfter == null || weekAfter.Value < weekBefore.Value))
            {
                found.Add(new InvariantViolation(10, "처리한 주간 구간이 뒤로 감: " + weekBefore + " → " + (weekAfter?.ToString() ?? "없음")));
            }
        }
    }
}
