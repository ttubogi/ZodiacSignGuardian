using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Ads
{
    /// <summary>광고 대상. 숫자 값은 바꾸지 않는다.</summary>
    public enum AdTarget
    {
        Offline = 1,
        HuntBuff = 2,
    }

    /// <summary>
    /// 1회용 광고 번호 = { sessionNonce, 세션 안 순번(1부터), 대상 }. 저장하지 않는다.
    /// 광고 시청 증명이 아니다: Services는 광고 어댑터의 보상 획득 콜백이 온 경우에만 쓴다.
    /// </summary>
    public sealed class AdTicket : IEquatable<AdTicket>
    {
        public ulong SessionNonce { get; }
        public long Serial { get; }
        public AdTarget Target { get; }

        public AdTicket(ulong sessionNonce, long serial, AdTarget target)
        {
            SessionNonce = sessionNonce;
            Serial = serial;
            Target = target;
        }

        public bool Equals(AdTicket other) => other != null && other.SessionNonce == SessionNonce && other.Serial == Serial && other.Target == Target;
        public override bool Equals(object obj) => Equals(obj as AdTicket);
        public override int GetHashCode() => SessionNonce.GetHashCode() ^ Serial.GetHashCode() ^ ((int)Target * 31);
        public override string ToString() => "AdTicket(" + SessionNonce.ToString("x16") + ", " + Serial + ", " + Target + ")";
    }

    public sealed class AdTicketIssued : GameEvent
    {
        public AdTarget Target { get; }
        public AdTicket Ticket { get; }
        public AdTicketIssued(AdTarget target, AdTicket ticket) { Target = target; Ticket = ticket; }
        public override string ToString() => "AdTicketIssued " + Ticket;
    }

    /// <summary>세션에서 낸 번호의 상태 (D3). 사용됨은 무효로 바뀌지 않는다.</summary>
    public enum AdTicketState
    {
        Open = 0,
        Used = 1,
        Revoked = 2,
    }

    /// <summary>
    /// 이번 세션에서 낸 광고 번호와 그 상태 (설계서 v0.9 3절 "광고", E2·D3). 저장하지 않는다.
    /// <list type="bullet">
    /// <item>대상마다 열린 번호는 하나. 새로 내면 그 대상의 열린 이전 번호는 무효가 된다.</item>
    /// <item>Offline 번호는 발급 때의 미수령 묶음 번호에 연결된다. 묶음이 끝나면 그 묶음의 열린 번호는 무효가 된다.</item>
    /// <item>판정 순서: 정확한 번호 → 명령이 받는 대상 → 사용됨(AlreadyClaimed) → 무효·묶음 종료(InvalidTarget).</item>
    /// </list>
    /// </summary>
    internal sealed class AdTickets
    {
        sealed class Record
        {
            public AdTarget Target;
            public AdTicketState State;
            public long BundleId;
        }

        readonly Dictionary<long, Record> _records = new Dictionary<long, Record>();
        readonly Dictionary<AdTarget, long> _open = new Dictionary<AdTarget, long>();

        public ulong SessionNonce { get; }
        public long LastSerial { get; private set; }

        public AdTickets(ulong sessionNonce)
        {
            SessionNonce = sessionNonce;
        }

        public static bool IsTarget(AdTarget target) => target == AdTarget.Offline || target == AdTarget.HuntBuff;

        public bool CanIssue => LastSerial < long.MaxValue;

        /// <summary>새 번호를 낸다. 같은 대상의 열린 번호는 무효가 된다. bundleId는 Offline일 때 연결할 묶음(그 밖에는 0).</summary>
        public AdTicket Issue(AdTarget target, long bundleId, List<GameEvent> events)
        {
            if (!IsTarget(target)) throw new ArgumentOutOfRangeException(nameof(target), target, "정의되지 않은 대상");
            if (!CanIssue) throw new InvalidOperationException("번호가 끝에 닿음");
            if (_open.TryGetValue(target, out var prev)) _records[prev].State = AdTicketState.Revoked;
            LastSerial++;
            _records.Add(LastSerial, new Record { Target = target, State = AdTicketState.Open, BundleId = bundleId });
            _open[target] = LastSerial;
            var ticket = new AdTicket(SessionNonce, LastSerial, target);
            events.Add(new AdTicketIssued(target, ticket));
            return ticket;
        }

        /// <summary>
        /// commandTarget을 받는 명령에 ticket을 쓸 수 있으면 null (E2 순서). currentBundleId는 지금 미수령 묶음 번호(없으면 0).
        /// </summary>
        public ErrorCode? Check(AdTicket ticket, AdTarget commandTarget, long currentBundleId)
        {
            if (ticket == null) return ErrorCode.InvalidInput;
            // 1. 이번 세션에서 낸 정확한 번호인지
            if (ticket.SessionNonce != SessionNonce || !_records.TryGetValue(ticket.Serial, out var r) || r.Target != ticket.Target) return ErrorCode.InvalidTarget;
            // 2. 이 명령이 받는 대상인지
            if (r.Target != commandTarget) return ErrorCode.InvalidTarget;
            // 3. 사용됨
            if (r.State == AdTicketState.Used) return ErrorCode.AlreadyClaimed;
            // 4. 무효이거나 연결된 묶음이 끝남
            if (r.State == AdTicketState.Revoked) return ErrorCode.InvalidTarget;
            if (r.Target == AdTarget.Offline && r.BundleId != currentBundleId) return ErrorCode.InvalidTarget;
            return null;
        }

        public void MarkUsed(AdTicket ticket)
        {
            var r = _records[ticket.Serial];
            if (r.State != AdTicketState.Open) throw new InvalidOperationException("열린 번호가 아닌데 사용함");
            r.State = AdTicketState.Used;
            if (_open.TryGetValue(r.Target, out var s) && s == ticket.Serial) _open.Remove(r.Target);
        }

        /// <summary>묶음이 끝날 때: 그 묶음에 연결된 열린 번호를 무효로. 사용된 번호는 그대로.</summary>
        public void RevokeBundle(long bundleId)
        {
            foreach (var kv in _records)
            {
                if (kv.Value.Target != AdTarget.Offline || kv.Value.BundleId != bundleId || kv.Value.State != AdTicketState.Open) continue;
                kv.Value.State = AdTicketState.Revoked;
                if (_open.TryGetValue(AdTarget.Offline, out var s) && s == kv.Key) _open.Remove(AdTarget.Offline);
            }
        }

        /// <summary>번호별 (대상, 상태). 위반 규칙 9 검사용.</summary>
        public IReadOnlyDictionary<long, KeyValuePair<AdTarget, AdTicketState>> States()
        {
            var d = new Dictionary<long, KeyValuePair<AdTarget, AdTicketState>>();
            foreach (var kv in _records) d[kv.Key] = new KeyValuePair<AdTarget, AdTicketState>(kv.Value.Target, kv.Value.State);
            return d;
        }

        public AdTicketState? StateOf(long serial) => _records.TryGetValue(serial, out var r) ? r.State : (AdTicketState?)null;

        public bool SameState(AdTickets o)
        {
            if (o == null || o.SessionNonce != SessionNonce || o.LastSerial != LastSerial || o._records.Count != _records.Count || o._open.Count != _open.Count) return false;
            foreach (var kv in _records)
            {
                if (!o._records.TryGetValue(kv.Key, out var r) || r.Target != kv.Value.Target || r.State != kv.Value.State || r.BundleId != kv.Value.BundleId) return false;
            }
            foreach (var kv in _open)
            {
                if (!o._open.TryGetValue(kv.Key, out var s) || s != kv.Value) return false;
            }
            return true;
        }

        public AdTickets Clone()
        {
            var c = new AdTickets(SessionNonce) { LastSerial = LastSerial };
            foreach (var kv in _records) c._records.Add(kv.Key, new Record { Target = kv.Value.Target, State = kv.Value.State, BundleId = kv.Value.BundleId });
            foreach (var kv in _open) c._open.Add(kv.Key, kv.Value);
            return c;
        }

        /// <summary>테스트용: 번호의 상태를 억지로 바꾼다(위반 주입).</summary>
        internal void CorruptStateForTest(long serial, AdTicketState state) => _records[serial].State = state;
    }
}
