using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Stars
{
    /// <summary>가진 별 하나. 인스턴스 번호는 1부터 오르고 겹치지 않는다. star_id마다 하나뿐이다(B3). 새 별은 등급 1·스킬 레벨 1.</summary>
    public readonly struct OwnedStar : IEquatable<OwnedStar>
    {
        public long InstanceId { get; }
        public string StarId { get; }
        /// <summary>1~5. 보유 효과와 별 모양.</summary>
        public int Grade { get; }
        /// <summary>1~max_skill_level. 스킬 피해 배율.</summary>
        public int SkillLevel { get; }

        public OwnedStar(long instanceId, string starId, int grade = 1, int skillLevel = 1)
        {
            InstanceId = instanceId;
            StarId = starId;
            Grade = grade;
            SkillLevel = skillLevel;
        }

        internal OwnedStar WithGrade(int grade) => new OwnedStar(InstanceId, StarId, grade, SkillLevel);
        internal OwnedStar WithSkillLevel(int level) => new OwnedStar(InstanceId, StarId, Grade, level);

        public bool Equals(OwnedStar other) => InstanceId == other.InstanceId && StarId == other.StarId && Grade == other.Grade && SkillLevel == other.SkillLevel;
        public override bool Equals(object obj) => obj is OwnedStar s && Equals(s);
        public override int GetHashCode() => InstanceId.GetHashCode() ^ (StarId ?? "").GetHashCode() ^ (Grade * 31) ^ (SkillLevel * 1009);
        public override string ToString() => "#" + InstanceId + " " + StarId + " 등급" + Grade + " 스킬" + SkillLevel;
    }

    public sealed class StarObtained : GameEvent
    {
        public long InstanceId { get; }
        public string StarId { get; }
        public StarObtained(long instanceId, string starId) { InstanceId = instanceId; StarId = starId; }
        public override string ToString() => "StarObtained #" + InstanceId + " " + StarId;
    }

    /// <summary>가진 별을 다시 얻어 별조각으로 바뀜. 별조각 재화의 CurrencyChanged(이유 StarDuplicate)는 따로 온다.</summary>
    public sealed class StarDuplicated : GameEvent
    {
        public string StarId { get; }
        public long Shards { get; }
        public StarDuplicated(string starId, long shards) { StarId = starId; Shards = shards; }
        public override string ToString() => "StarDuplicated " + StarId + " +" + Shards;
    }

    public sealed class StarGradeChanged : GameEvent
    {
        public long InstanceId { get; }
        public string StarId { get; }
        public int Grade { get; }
        public StarGradeChanged(long instanceId, string starId, int grade) { InstanceId = instanceId; StarId = starId; Grade = grade; }
        public override string ToString() => "StarGradeChanged #" + InstanceId + " " + StarId + " → " + Grade;
    }

    public sealed class SkillLevelChanged : GameEvent
    {
        public long InstanceId { get; }
        public string StarId { get; }
        public int Level { get; }
        public SkillLevelChanged(long instanceId, string starId, int level) { InstanceId = instanceId; StarId = starId; Level = level; }
        public override string ToString() => "SkillLevelChanged #" + InstanceId + " " + StarId + " → " + Level;
    }

    /// <summary>
    /// 가진 별 목록 (설계서 v0.12 "별과 별 성장"). 지금 빌드의 별만 받고, star_id마다 하나뿐이다.
    /// 별을 얻는 길(뽑기·출석)은 한 번에 별 하나씩 처리한다(표 값의 크기만큼 반복하지 않음).
    /// 별조각 지급·비용은 Wallet이 맡고, 여기서는 별의 상태만 바꾼다.
    /// </summary>
    internal sealed class StarInventory
    {
        public const int MaxGrade = 5;

        readonly StarCatalog _catalog;
        readonly List<OwnedStar> _stars = new List<OwnedStar>();
        readonly Dictionary<string, int> _indexByStar = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>다음에 줄 인스턴스 번호.</summary>
        public long NextInstanceId { get; private set; } = 1;

        public StarInventory(StarCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public StarCatalog Catalog => _catalog;
        public IReadOnlyList<OwnedStar> All => _stars;
        public bool IsKnown(string starId) => _catalog.IsKnown(starId);
        public bool Owns(string starId) => starId != null && _indexByStar.ContainsKey(starId);

        public bool TryFind(long instanceId, out OwnedStar star)
        {
            foreach (var s in _stars)
            {
                if (s.InstanceId == instanceId) { star = s; return true; }
            }
            star = default;
            return false;
        }

        public OwnedStar ByStar(string starId) => _stars[_indexByStar[starId]];

        /// <summary>
        /// 별 하나를 얻을 수 있으면 null. 모르는 별 → InvalidTarget, 새 별인데 인스턴스 번호가 long 끝이면 InvalidInput.
        /// 이미 가진 별이면 duplicate가 참이고 별조각 수(그 희귀도의 dup_shards)를 돌려준다(지급은 호출하는 쪽이 Wallet으로).
        /// pendingNew는 같은 명령에서 이미 새로 얻기로 한 별(10번 뽑기 중 앞에서 나온 별)이다.
        /// </summary>
        public ErrorCode? CheckGain(string starId, ISet<string> pendingNew, out bool duplicate, out long shards, long reservedIds = 0)
        {
            duplicate = false;
            shards = 0;
            if (!IsKnown(starId)) return ErrorCode.InvalidTarget;
            if (Owns(starId) || (pendingNew != null && pendingNew.Contains(starId)))
            {
                duplicate = true;
                shards = _catalog.RarityOf(starId).DupShards;
                return null;
            }
            // 새 번호 = NextInstanceId + reservedIds. reservedIds는 별 종류 수 이하라 빼기로 비교하면 넘치지 않는다
            if (NextInstanceId > long.MaxValue - 1 - reservedIds) return ErrorCode.InvalidInput;
            return null;
        }

        /// <summary>CheckGain을 통과한 별 하나를 얻는다. 새 별이면 StarObtained, 가진 별이면 StarDuplicated(별조각 수).</summary>
        public void Gain(string starId, List<GameEvent> events)
        {
            if (CheckGain(starId, null, out var duplicate, out var shards) != null) throw new InvalidOperationException("얻을 수 없는 별: " + starId);
            if (duplicate)
            {
                events.Add(new StarDuplicated(starId, shards));
                return;
            }
            var star = new OwnedStar(NextInstanceId, starId);
            NextInstanceId++;
            _indexByStar[starId] = _stars.Count;
            _stars.Add(star);
            events.Add(new StarObtained(star.InstanceId, starId));
        }

        /// <summary>등급을 올릴 비용(별조각). 가지지 않은 번호 → StarNotOwned, 등급 5 → InvalidTarget.</summary>
        public ErrorCode? CheckGradeUp(long instanceId, out long cost)
        {
            cost = 0;
            if (!TryFind(instanceId, out var s)) return ErrorCode.StarNotOwned;
            if (s.Grade >= MaxGrade) return ErrorCode.InvalidTarget;
            cost = _catalog.RarityOf(s.StarId).GradeCost(s.Grade + 1);
            return null;
        }

        public void GradeUp(long instanceId, List<GameEvent> events)
        {
            if (CheckGradeUp(instanceId, out _) != null) throw new InvalidOperationException("등급을 올릴 수 없음: " + instanceId);
            var s = _stars.Find(x => x.InstanceId == instanceId);
            var next = s.WithGrade(s.Grade + 1);
            _stars[_indexByStar[s.StarId]] = next;
            events.Add(new StarGradeChanged(next.InstanceId, next.StarId, next.Grade));
        }

        /// <summary>
        /// 스킬 레벨을 올릴 비용 = skill_cost_per_level × 지금 레벨(별조각). 가지지 않은 번호 → StarNotOwned, 최대 레벨 → InvalidTarget.
        /// 곱은 넘침을 확인한 뒤 한다(넘치면 InvalidInput; 표 범위 1e9 × 1000 안에서는 생기지 않음).
        /// </summary>
        public ErrorCode? CheckSkillUp(long instanceId, out long cost)
        {
            cost = 0;
            if (!TryFind(instanceId, out var s)) return ErrorCode.StarNotOwned;
            if (s.SkillLevel >= _catalog.Rules.MaxSkillLevel) return ErrorCode.InvalidTarget;
            var per = _catalog.RarityOf(s.StarId).SkillCostPerLevel;
            if (per > long.MaxValue / s.SkillLevel) return ErrorCode.InvalidInput;
            cost = per * s.SkillLevel;
            return null;
        }

        public void SkillUp(long instanceId, List<GameEvent> events)
        {
            if (CheckSkillUp(instanceId, out _) != null) throw new InvalidOperationException("스킬 레벨을 올릴 수 없음: " + instanceId);
            var s = _stars.Find(x => x.InstanceId == instanceId);
            var next = s.WithSkillLevel(s.SkillLevel + 1);
            _stars[_indexByStar[s.StarId]] = next;
            events.Add(new SkillLevelChanged(next.InstanceId, next.StarId, next.SkillLevel));
        }

        /// <summary>
        /// 저장한 값으로. 번호는 1 이상 nextInstanceId 미만이고 겹치지 않으며, 별은 지금 빌드에 있고 star_id가 겹치지 않으며,
        /// 등급 1~5, 스킬 레벨 1~max_skill_level이어야 한다. 틀리면 errors에 남기고 false(상태는 그대로).
        /// </summary>
        public bool TryRestore(long nextInstanceId, IReadOnlyList<OwnedStar> stars, List<string> errors)
        {
            var ok = true;
            if (nextInstanceId < 1) { errors.Add("stars.nextInstanceId: 1 이상"); ok = false; }
            var seen = new HashSet<long>();
            var seenStar = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in stars)
            {
                if (s.InstanceId < 1 || s.InstanceId >= nextInstanceId) { errors.Add("stars: 번호 " + s.InstanceId + "가 1 이상 nextInstanceId 미만이 아님"); ok = false; }
                if (!seen.Add(s.InstanceId)) { errors.Add("stars: 번호가 겹침 " + s.InstanceId); ok = false; }
                if (!IsKnown(s.StarId)) { errors.Add("stars: 지금 빌드에 없는 별 " + s.StarId); ok = false; }
                else if (!seenStar.Add(s.StarId)) { errors.Add("stars: 같은 별이 둘 " + s.StarId); ok = false; }
                if (s.Grade < 1 || s.Grade > MaxGrade) { errors.Add("stars: " + s.StarId + "의 등급 " + s.Grade + "가 1~" + MaxGrade + " 밖"); ok = false; }
                if (s.SkillLevel < 1 || s.SkillLevel > _catalog.Rules.MaxSkillLevel) { errors.Add("stars: " + s.StarId + "의 스킬 레벨 " + s.SkillLevel + "가 1~" + _catalog.Rules.MaxSkillLevel + " 밖"); ok = false; }
            }
            if (!ok) return false;
            NextInstanceId = nextInstanceId;
            _stars.Clear();
            _indexByStar.Clear();
            foreach (var s in stars)
            {
                _indexByStar[s.StarId] = _stars.Count;
                _stars.Add(s);
            }
            return true;
        }

        /// <summary>테스트 전용: 규칙을 어긴 상태를 만든다(위반 검사 확인용).</summary>
        internal void CorruptForTest(OwnedStar star)
        {
            _stars.Add(star);
        }

        public bool SameState(StarInventory o)
        {
            if (o == null || o.NextInstanceId != NextInstanceId || o._stars.Count != _stars.Count) return false;
            for (var i = 0; i < _stars.Count; i++) if (!o._stars[i].Equals(_stars[i])) return false;
            return true;
        }

        public StarInventory Clone()
        {
            var c = new StarInventory(_catalog) { NextInstanceId = NextInstanceId };
            foreach (var s in _stars)
            {
                c._indexByStar[s.StarId] = c._stars.Count;
                c._stars.Add(s);
            }
            return c;
        }
    }
}
