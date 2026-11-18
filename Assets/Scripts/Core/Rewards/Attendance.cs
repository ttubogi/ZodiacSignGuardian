using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Rewards
{
    public enum AttendanceRewardKind
    {
        Currency = 0,
        Star = 1,
    }

    /// <summary>출석 표의 한 행.</summary>
    public sealed class AttendanceReward
    {
        public int Day { get; }
        public AttendanceRewardKind Kind { get; }
        public string RewardId { get; }
        public long Amount { get; }

        public AttendanceReward(int day, AttendanceRewardKind kind, string rewardId, long amount)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day), day, "1 이상");
            if (string.IsNullOrEmpty(rewardId)) throw new ArgumentException("보상 id가 없음", nameof(rewardId));
            if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount), amount, "1 이상");
            Day = day;
            Kind = kind;
            RewardId = rewardId;
            Amount = amount;
        }
    }

    /// <summary>
    /// 출석 (설계서 v0.9 3절 "출석", 2026-11-17 Q1).
    /// <list type="bullet">
    /// <item>받기를 누른 날만 차례가 오른다. 누르지 않으면 다음에 접속한 날 같은 차례를 받는다.</item>
    /// <item>마지막 차례(cycle_days) 다음은 1일째.</item>
    /// <item>같은 일일 구간에 이미 받았으면 AlreadyClaimed. 마지막으로 받은 일일 구간 id를 저장한다.</item>
    /// </list>
    /// </summary>
    internal sealed class Attendance
    {
        readonly Dictionary<int, AttendanceReward> _rows;

        public int CycleDays { get; }
        /// <summary>다음에 받을 차례(1~CycleDays).</summary>
        public int NextDay { get; private set; } = 1;
        /// <summary>마지막으로 받은 일일 구간 id. 받은 적이 없으면 null.</summary>
        public long? LastClaimedDay { get; private set; }

        public Attendance(int cycleDays, IEnumerable<AttendanceReward> rows)
        {
            if (cycleDays < 1) throw new ArgumentOutOfRangeException(nameof(cycleDays), cycleDays, "1 이상");
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            CycleDays = cycleDays;
            _rows = new Dictionary<int, AttendanceReward>();
            foreach (var r in rows)
            {
                if (r == null) throw new ArgumentException("출석 행에 null", nameof(rows));
                if (_rows.ContainsKey(r.Day)) throw new ArgumentException("출석 날이 겹침: " + r.Day, nameof(rows));
                _rows.Add(r.Day, r);
            }
            for (var d = 1; d <= cycleDays; d++)
            {
                if (!_rows.ContainsKey(d)) throw new ArgumentException("출석 " + d + "일째가 없음", nameof(rows));
            }
            if (_rows.Count != cycleDays) throw new ArgumentException("출석 행 수가 cycle_days와 다름", nameof(rows));
        }

        public AttendanceReward NextReward => _rows[NextDay];

        /// <summary>today 구간에 받을 수 있으면 null, 이미 받았으면 AlreadyClaimed.</summary>
        public ErrorCode? CheckClaim(long today) => LastClaimedDay == today ? ErrorCode.AlreadyClaimed : (ErrorCode?)null;

        public void MarkClaimed(long today)
        {
            if (CheckClaim(today) != null) throw new InvalidOperationException("같은 날 출석을 두 번 받음");
            LastClaimedDay = today;
            NextDay = NextDay == CycleDays ? 1 : NextDay + 1;
        }

        public bool TryRestore(long nextDay, long? lastClaimedDay, List<string> errors)
        {
            if (nextDay < 1 || nextDay > CycleDays) { errors.Add("attendance.nextDay: 1~" + CycleDays); return false; }
            NextDay = (int)nextDay;
            LastClaimedDay = lastClaimedDay;
            return true;
        }

        /// <summary>테스트용: 받은 기록을 잊는다(같은 날 두 번 지급하는 잘못을 일부러 만든다, 위반 규칙 2 주입).</summary>
        internal void ForgetClaimForTest() => LastClaimedDay = null;

        public bool SameState(Attendance o) => o != null && o.NextDay == NextDay && o.LastClaimedDay == LastClaimedDay;

        public Attendance Clone() => new Attendance(CycleDays, _rows.Values) { NextDay = NextDay, LastClaimedDay = LastClaimedDay };
    }
}
