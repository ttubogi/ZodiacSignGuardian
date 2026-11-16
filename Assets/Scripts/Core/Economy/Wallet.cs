using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Numbers;

namespace ZodiacSignGuardian.Core.Economy
{
    /// <summary>재화 하나의 증감 요청. Delta는 부호가 있다(+ 받기, - 쓰기).</summary>
    public readonly struct CurrencyChange
    {
        public string CurrencyId { get; }
        public CurrencyAmount Delta { get; }

        public CurrencyChange(string currencyId, CurrencyAmount delta)
        {
            CurrencyId = currencyId ?? throw new ArgumentNullException(nameof(currencyId));
            Delta = delta;
        }

        public static CurrencyChange Of(string id, long delta) => new CurrencyChange(id, CurrencyAmount.Of(delta));
        public static CurrencyChange Of(string id, GameNumber delta) => new CurrencyChange(id, CurrencyAmount.Of(delta));
    }

    /// <summary>재화가 바뀌었음. Delta는 부호 있는 변화량, Balance는 바뀐 뒤 잔액. 성공한 명령에서만 나온다.</summary>
    public sealed class CurrencyChanged : GameEvent
    {
        public string CurrencyId { get; }
        public CurrencyAmount Delta { get; }
        public CurrencyAmount Balance { get; }
        public CurrencyReason Reason { get; }

        public CurrencyChanged(string currencyId, CurrencyAmount delta, CurrencyAmount balance, CurrencyReason reason)
        {
            CurrencyId = currencyId;
            Delta = delta;
            Balance = balance;
            Reason = reason;
        }

        public override string ToString() => "CurrencyChanged " + CurrencyId + " " + Delta + " → " + Balance + " (" + Reason + ")";
    }

    /// <summary>
    /// 모든 재화의 잔액. (설계서 4절 Wallet, 위반 규칙 1)
    /// <list type="bullet">
    /// <item>잔액은 음수가 되지 않는다. 쓰려는 양이 잔액보다 크면 NotEnoughCurrency.</item>
    /// <item>정수 재화가 long 최대값을 넘으면 InvalidInput (C2 결정 Q5).</item>
    /// <item>바꾸기 전에 Check로 모든 조건을 확인하고, 통과했을 때만 Apply한다 (C2 결정 Q4). Apply가 확인에 실패하면 예외(코드 잘못).</item>
    /// <item>한 번에 여러 재화를 바꾸면 모두 바뀌거나 하나도 안 바뀐다. 같은 재화를 한 요청에 두 번 넣을 수 없다.</item>
    /// <item>재화 id·종류가 틀리거나 이유가 정의되지 않은 값이면 예외(코드 잘못). 플레이어 입력으로 생기는 일이 아니다.</item>
    /// </list>
    /// </summary>
    public sealed class Wallet
    {
        readonly Dictionary<string, CurrencyKind> _kinds = new Dictionary<string, CurrencyKind>(StringComparer.Ordinal);
        readonly Dictionary<string, CurrencyAmount> _balances = new Dictionary<string, CurrencyAmount>(StringComparer.Ordinal);
        readonly List<string> _order = new List<string>();

        /// <summary>재화 정의(id, 종류)로 만든다. 잔액은 모두 0.</summary>
        public Wallet(IEnumerable<KeyValuePair<string, CurrencyKind>> currencies)
        {
            if (currencies == null) throw new ArgumentNullException(nameof(currencies));
            foreach (var c in currencies)
            {
                if (string.IsNullOrEmpty(c.Key)) throw new ArgumentException("재화 id가 비어 있음", nameof(currencies));
                if (c.Value != CurrencyKind.Integer && c.Value != CurrencyKind.GameNumber) throw new ArgumentOutOfRangeException(nameof(currencies), c.Value, "정의되지 않은 재화 종류");
                if (_kinds.ContainsKey(c.Key)) throw new ArgumentException("재화 id가 겹침: " + c.Key, nameof(currencies));
                _kinds[c.Key] = c.Value;
                _balances[c.Key] = CurrencyAmount.ZeroOf(c.Value);
                _order.Add(c.Key);
            }
        }

        /// <summary>정의된 재화 id (정의 순서).</summary>
        public IReadOnlyList<string> CurrencyIds => _order;

        public bool Has(string currencyId) => currencyId != null && _kinds.ContainsKey(currencyId);

        public CurrencyKind KindOf(string currencyId) => _kinds[Known(currencyId)];

        public CurrencyAmount Balance(string currencyId) => _balances[Known(currencyId)];

        string Known(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (!_kinds.ContainsKey(id)) throw new ArgumentException("정의되지 않은 재화: " + id, nameof(id));
            return id;
        }

        // ---- 확인 ----

        /// <summary>바꿀 수 있으면 null, 아니면 실패 코드. 상태는 바꾸지 않는다.</summary>
        public ErrorCode? Check(CurrencyChange change) => Check(new[] { change });

        /// <summary>모두 바꿀 수 있으면 null, 하나라도 안 되면 첫 실패 코드. 상태는 바꾸지 않는다.</summary>
        public ErrorCode? Check(IReadOnlyList<CurrencyChange> changes)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in changes)
            {
                var id = Known(c.CurrencyId);
                if (!seen.Add(id)) throw new ArgumentException("같은 재화를 한 요청에 두 번 넣음: " + id, nameof(changes));
                if (c.Delta.Kind != _kinds[id]) throw new ArgumentException(id + "는 " + _kinds[id] + " 재화인데 " + c.Delta.Kind + " 양을 넣음", nameof(changes));
            }
            foreach (var c in changes)
            {
                if (!TryNewBalance(c, out _, out var error)) return error;
            }
            return null;
        }

        bool TryNewBalance(CurrencyChange c, out CurrencyAmount balance, out ErrorCode error)
        {
            var current = _balances[c.CurrencyId];
            error = default;
            balance = current;
            if (c.Delta.Kind == CurrencyKind.Integer)
            {
                long b = current.Integer, d = c.Delta.Integer;
                // b >= 0이므로 d < 0일 때 b + d는 넘치지 않는다
                if (d > 0 && b > long.MaxValue - d) { error = ErrorCode.InvalidInput; return false; }
                var next = b + d;
                if (next < 0) { error = ErrorCode.NotEnoughCurrency; return false; }
                balance = CurrencyAmount.Of(next);
                return true;
            }
            else
            {
                GameNumber next;
                try
                {
                    next = current.Number + c.Delta.Number;
                }
                catch (OverflowException)
                {
                    // 지수가 long을 넘는 경우 (사실상 일어나지 않음)
                    error = ErrorCode.InvalidInput;
                    return false;
                }
                if (next.IsNegative) { error = ErrorCode.NotEnoughCurrency; return false; }
                balance = CurrencyAmount.Of(next);
                return true;
            }
        }

        // ---- 바꾸기 ----

        /// <summary>Check를 통과한 변경을 적용하고 CurrencyChanged를 events에 더한다. 통과하지 못하면 InvalidOperationException(상태는 그대로).</summary>
        public void Apply(CurrencyChange change, CurrencyReason reason, List<GameEvent> events) => Apply(new[] { change }, reason, events);

        public void Apply(IReadOnlyList<CurrencyChange> changes, CurrencyReason reason, List<GameEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (!Enum.IsDefined(typeof(CurrencyReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason), reason, "정의되지 않은 재화 이유");
            var error = Check(changes);
            if (error != null) throw new InvalidOperationException("Check를 통과하지 못한 변경을 적용하려 함: " + error + " (명령은 바꾸기 전에 확인해야 함)");

            // 모두 계산한 뒤 한꺼번에 바꾼다
            var next = new CurrencyAmount[changes.Count];
            for (var i = 0; i < changes.Count; i++)
            {
                TryNewBalance(changes[i], out next[i], out _);
            }
            for (var i = 0; i < changes.Count; i++)
            {
                var c = changes[i];
                if (c.Delta.IsZero) continue; // 0은 바뀐 것이 없으므로 이벤트도 없다
                _balances[c.CurrencyId] = next[i];
                events.Add(new CurrencyChanged(c.CurrencyId, c.Delta, next[i], reason));
            }
        }

        // ---- 저장 ----

        /// <summary>저장에서 잔액을 되돌린다. 정의된 재화가 모두 있고, 종류가 맞고, 음수가 아니어야 한다. 아니면 false(상태는 그대로).</summary>
        public bool TryRestore(IReadOnlyDictionary<string, CurrencyAmount> balances)
        {
            if (balances == null || balances.Count != _kinds.Count) return false;
            foreach (var id in _order)
            {
                if (!balances.TryGetValue(id, out var b)) return false;
                if (b.Kind != _kinds[id] || b.Sign < 0) return false;
            }
            foreach (var id in _order) _balances[id] = balances[id];
            return true;
        }

        /// <summary>같은 재화 정의와 같은 잔액인지 (테스트에서 실패 전후 비교).</summary>
        public bool SameState(Wallet other)
        {
            if (other == null || other._order.Count != _order.Count) return false;
            for (var i = 0; i < _order.Count; i++)
            {
                var id = _order[i];
                if (other._order[i] != id || other._kinds[id] != _kinds[id] || other._balances[id] != _balances[id]) return false;
            }
            return true;
        }

        public Wallet Clone()
        {
            var list = new List<KeyValuePair<string, CurrencyKind>>();
            foreach (var id in _order) list.Add(new KeyValuePair<string, CurrencyKind>(id, _kinds[id]));
            var w = new Wallet(list);
            foreach (var id in _order) w._balances[id] = _balances[id];
            return w;
        }
    }
}
