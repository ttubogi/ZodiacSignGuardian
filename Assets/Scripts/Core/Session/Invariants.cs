using System.Collections.Generic;
using ZodiacSignGuardian.Core.Ads;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;

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
    /// C2에서 1·7·10번, C3에서 2·3·4·9번과 10번의 정산 기준 시점을 더했다. 5·6·8번은 C4·C6.
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

        /// <summary>
        /// 규칙 2: 같은 보상은 한 초기화 구간에서 두 번 받을 수 없다. key는 보상과 구간을 함께 담는다
        /// (출석 "attendance:일일구간", 임무 "mission:id:일일구간", 오프라인 "offline:묶음번호"). 이번 세션에서 같은 key를 두 번 지급하면 위반.
        /// </summary>
        public static void CheckClaimOnce(string key, HashSet<string> paid, List<InvariantViolation> found)
        {
            if (!paid.Add(key)) found.Add(new InvariantViolation(2, "같은 보상을 두 번 지급: " + key));
        }

        /// <summary>
        /// 규칙 3: 유효하지 않은 서버 시각으로는 날짜 의존 보상(출석·임무 진행·수령)과 오프라인 적립·수령을 갱신하지 않는다.
        /// 명령 시작 때의 조건(gateBefore)이 통과하지 않았는데 그런 상태가 바뀌었으면 위반. ConfirmAd의 광고 적용 표시는 금액이 아니므로 예외다(호출하는 쪽이 넣지 않음).
        /// </summary>
        public static void CheckDateDependent(string command, ErrorCode? gateBefore, bool dateStateChanged, List<InvariantViolation> found)
        {
            if (gateBefore != null && dateStateChanged) found.Add(new InvariantViolation(3, command + ": 시간 조건(" + gateBefore + ")이 맞지 않는데 날짜 의존 상태가 바뀜"));
        }

        /// <summary>규칙 3 (동기화): 오프라인 적립은 유효한 동기화로만 생긴다.</summary>
        public static void CheckOfflineAccrualValid(bool accrued, bool validAfter, List<InvariantViolation> found)
        {
            if (accrued && !validAfter) found.Add(new InvariantViolation(3, "유효하지 않은 시각에서 오프라인 적립이 생김"));
        }

        /// <summary>
        /// 규칙 4: 미수령 합계 분은 max_minutes를 넘지 않는다. 묶음이 있으면 분 1 이상, 금액 0보다 큼(E4: 빈 묶음이 남지 않음),
        /// 금액 × ad_multiplier도 표현된다(K7: 광고 적용 뒤 받을 수 없는 묶음이 생기지 않음).
        /// </summary>
        public static void CheckOfflineBundle(bool hasBundle, long minutes, GameNumber amount, int maxMinutes, int adMultiplier, List<InvariantViolation> found)
        {
            if (hasBundle && amount.Sign > 0)
            {
                try { var _ = amount * adMultiplier; }
                catch (System.OverflowException) { found.Add(new InvariantViolation(4, "미수령 금액 " + amount + "의 " + adMultiplier + "배가 표현 범위를 넘음")); }
            }
            if (minutes > maxMinutes) found.Add(new InvariantViolation(4, "미수령 합계 " + minutes + "분이 " + maxMinutes + "분을 넘음"));
            if (hasBundle && (minutes < 1 || amount.Sign <= 0)) found.Add(new InvariantViolation(4, "빈 미수령 묶음 (분 " + minutes + ", 금액 " + amount + ")"));
            if (!hasBundle && (minutes != 0 || !amount.IsZero)) found.Add(new InvariantViolation(4, "묶음이 없는데 미수령 값이 남음"));
        }

        /// <summary>
        /// 규칙 9: 광고 번호는 발급된 대상에 한 번만 쓰인다. 사용됨은 사용됨으로 남고, 한 명령에서 새로 사용되는 번호는 하나뿐이며
        /// 그 번호의 대상은 명령이 받는 대상(usedFor)과 같다.
        /// </summary>
        public static void CheckTickets(IReadOnlyDictionary<long, KeyValuePair<AdTarget, AdTicketState>> before,
            IReadOnlyDictionary<long, KeyValuePair<AdTarget, AdTicketState>> after, AdTarget? usedFor, List<InvariantViolation> found)
        {
            var newlyUsed = 0;
            foreach (var kv in after)
            {
                var wasUsed = before.TryGetValue(kv.Key, out var b) && b.Value == AdTicketState.Used;
                if (wasUsed && kv.Value.Value != AdTicketState.Used) found.Add(new InvariantViolation(9, "사용된 광고 번호 " + kv.Key + "의 상태가 " + kv.Value.Value + "로 바뀜"));
                if (!wasUsed && kv.Value.Value == AdTicketState.Used)
                {
                    newlyUsed++;
                    if (usedFor == null || kv.Value.Key != usedFor.Value) found.Add(new InvariantViolation(9, "광고 번호 " + kv.Key + "(" + kv.Value.Key + ")가 다른 대상에 쓰임"));
                }
            }
            foreach (var kv in before)
            {
                if (kv.Value.Value == AdTicketState.Used && !after.ContainsKey(kv.Key)) found.Add(new InvariantViolation(9, "사용된 광고 번호 " + kv.Key + "의 기록이 사라짐"));
            }
            if (newlyUsed > 1) found.Add(new InvariantViolation(9, "한 명령에서 광고 번호 " + newlyUsed + "개가 쓰임"));
        }

        /// <summary>규칙 10: 정산 기준 시점은 뒤로 가지 않는다(생긴 뒤 없어지지도 않는다).</summary>
        public static void CheckSettlementForward(long? before, long? after, List<InvariantViolation> found)
        {
            if (before != null && (after == null || after.Value < before.Value))
            {
                found.Add(new InvariantViolation(10, "정산 기준 시점이 뒤로 감: " + before + " → " + (after?.ToString() ?? "없음")));
            }
        }

        /// <summary>규칙 10: 처리한 일일·주간 구간은 뒤로 가지 않는다.</summary>
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
