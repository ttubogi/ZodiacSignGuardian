using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Rewards
{
    /// <summary>일일 임무 하나의 정의 (daily_missions 표의 지금 빌드 행).</summary>
    public sealed class MissionDefinition
    {
        public string Id { get; }
        /// <summary>kill_monster, upgrade_stat, draw_star, claim_offline, place_star, clear_boss</summary>
        public string Condition { get; }
        public long Target { get; }
        public string RewardCurrency { get; }
        public long RewardAmount { get; }

        public MissionDefinition(string id, string condition, long target, string rewardCurrency, long rewardAmount)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("임무 id가 없음", nameof(id));
            if (string.IsNullOrEmpty(condition)) throw new ArgumentException("조건이 없음", nameof(condition));
            if (target < 1) throw new ArgumentOutOfRangeException(nameof(target), target, "1 이상");
            if (string.IsNullOrEmpty(rewardCurrency)) throw new ArgumentException("보상 재화가 없음", nameof(rewardCurrency));
            if (rewardAmount < 1) throw new ArgumentOutOfRangeException(nameof(rewardAmount), rewardAmount, "1 이상");
            Id = id;
            Condition = condition;
            Target = target;
            RewardCurrency = rewardCurrency;
            RewardAmount = rewardAmount;
        }
    }

    /// <summary>임무 진행 값이 실제로 바뀔 때마다 (설계서 v0.9 3절 "일일 임무").</summary>
    public sealed class MissionProgressed : GameEvent
    {
        public string MissionId { get; }
        public long Progress { get; }
        public long Target { get; }
        public MissionProgressed(string missionId, long progress, long target) { MissionId = missionId; Progress = progress; Target = target; }
        public override string ToString() => "MissionProgressed " + MissionId + " " + Progress + "/" + Target;
    }

    /// <summary>미완료 → 완료로 바뀔 때 한 번만.</summary>
    public sealed class MissionCompleted : GameEvent
    {
        public string MissionId { get; }
        public MissionCompleted(string missionId) { MissionId = missionId; }
        public override string ToString() => "MissionCompleted " + MissionId;
    }

    /// <summary>화면이 읽는 임무 상태.</summary>
    public sealed class MissionStatus
    {
        public string Id { get; }
        public string Condition { get; }
        public long Progress { get; }
        public long Target { get; }
        public bool Claimed { get; }
        public bool Completed => Progress >= Target;

        public MissionStatus(string id, string condition, long progress, long target, bool claimed)
        {
            Id = id;
            Condition = condition;
            Progress = progress;
            Target = target;
            Claimed = claimed;
        }
    }

    /// <summary>
    /// 일일 임무의 진행·수령 (설계서 v0.9 3절 "일일 임무", 2026-11-17 Q4 A안 "임무 진행 기록 창구").
    /// <list type="bullet">
    /// <item>진행은 Record(condition, n)으로만 센다. 진행을 셀지(날짜·유효·TimeWentBack)는 부르는 쪽이 그 시각으로 판단한다.</item>
    /// <item>진행은 목표에서 멈춘다(넘겨 세지 않음). 받은 임무는 더 세지 않는다.</item>
    /// <item>DayReset에서 진행과 수령 표시를 모두 비운다.</item>
    /// </list>
    /// </summary>
    internal sealed class Missions
    {
        readonly List<MissionDefinition> _defs;
        readonly Dictionary<string, MissionDefinition> _byId;
        readonly Dictionary<string, long> _progress = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.Ordinal);

        public Missions(IEnumerable<MissionDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            _defs = new List<MissionDefinition>();
            _byId = new Dictionary<string, MissionDefinition>(StringComparer.Ordinal);
            foreach (var d in definitions)
            {
                if (d == null) throw new ArgumentException("임무 정의에 null", nameof(definitions));
                if (_byId.ContainsKey(d.Id)) throw new ArgumentException("임무 id가 겹침: " + d.Id, nameof(definitions));
                _byId.Add(d.Id, d);
                _defs.Add(d);
                _progress.Add(d.Id, 0);
            }
        }

        public IReadOnlyList<MissionDefinition> Definitions => _defs;
        public MissionDefinition Find(string id) => id != null && _byId.TryGetValue(id, out var d) ? d : null;
        public long ProgressOf(string id) => _progress[id];
        public bool IsClaimed(string id) => _claimed.Contains(id);

        public IReadOnlyList<MissionStatus> Statuses()
        {
            var list = new List<MissionStatus>(_defs.Count);
            foreach (var d in _defs) list.Add(new MissionStatus(d.Id, d.Condition, _progress[d.Id], d.Target, _claimed.Contains(d.Id)));
            return list;
        }

        /// <summary>condition이 같은 임무의 진행을 count만큼 올린다(목표까지). count는 1 이상.</summary>
        public void Record(string condition, long count, List<GameEvent> events)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "1 이상");
            foreach (var d in _defs)
            {
                if (d.Condition != condition || _claimed.Contains(d.Id)) continue;
                var p = _progress[d.Id];
                if (p >= d.Target) continue;
                var room = d.Target - p;
                var next = p + (count < room ? count : room);
                _progress[d.Id] = next;
                events.Add(new MissionProgressed(d.Id, next, d.Target));
                if (next == d.Target) events.Add(new MissionCompleted(d.Id));
            }
        }

        /// <summary>받을 수 있으면 null. 표에 없는 id → InvalidTarget, 받았으면 AlreadyClaimed, 덜 했으면 NotReady.</summary>
        public ErrorCode? CheckClaim(string id)
        {
            var d = Find(id);
            if (d == null) return ErrorCode.InvalidTarget;
            if (_claimed.Contains(id)) return ErrorCode.AlreadyClaimed;
            if (_progress[id] < d.Target) return ErrorCode.NotReady;
            return null;
        }

        public void MarkClaimed(string id)
        {
            if (CheckClaim(id) != null) throw new InvalidOperationException("받을 수 없는 임무를 받음: " + id);
            _claimed.Add(id);
        }

        public void ClearDay()
        {
            foreach (var d in _defs) _progress[d.Id] = 0;
            _claimed.Clear();
        }

        /// <summary>저장한 값으로. 진행은 0~목표, 받은 임무는 진행이 목표와 같아야 한다. 아니면 오류를 더하고 false(상태는 그대로).</summary>
        public bool TryRestore(IReadOnlyDictionary<string, KeyValuePair<long, bool>> saved, List<string> errors)
        {
            var ok = true;
            foreach (var d in _defs)
            {
                if (!saved.TryGetValue(d.Id, out var v)) { errors.Add("missions: 빠진 임무 " + d.Id); ok = false; continue; }
                if (v.Key < 0 || v.Key > d.Target) { errors.Add("missions." + d.Id + ".progress: 0~" + d.Target); ok = false; }
                if (v.Value && v.Key != d.Target) { errors.Add("missions." + d.Id + ": 받았는데 목표에 닿지 않음"); ok = false; }
            }
            foreach (var k in saved.Keys)
            {
                if (!_byId.ContainsKey(k)) { errors.Add("missions: 표에 없는 임무 " + k); ok = false; }
            }
            if (!ok) return false;
            _claimed.Clear();
            foreach (var d in _defs)
            {
                _progress[d.Id] = saved[d.Id].Key;
                if (saved[d.Id].Value) _claimed.Add(d.Id);
            }
            return true;
        }

        public bool SameState(Missions o)
        {
            if (o == null || o._defs.Count != _defs.Count) return false;
            foreach (var d in _defs)
            {
                if (!o._progress.TryGetValue(d.Id, out var p) || p != _progress[d.Id]) return false;
                if (o._claimed.Contains(d.Id) != _claimed.Contains(d.Id)) return false;
            }
            return true;
        }

        public Missions Clone()
        {
            var c = new Missions(_defs);
            foreach (var d in _defs) c._progress[d.Id] = _progress[d.Id];
            foreach (var id in _claimed) c._claimed.Add(id);
            return c;
        }
    }
}
