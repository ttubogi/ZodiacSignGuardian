using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Stars
{
    /// <summary>가진 별 하나. 인스턴스 번호는 1부터 오르고 겹치지 않는다.</summary>
    public readonly struct OwnedStar : IEquatable<OwnedStar>
    {
        public long InstanceId { get; }
        public string StarId { get; }

        public OwnedStar(long instanceId, string starId)
        {
            InstanceId = instanceId;
            StarId = starId;
        }

        public bool Equals(OwnedStar other) => InstanceId == other.InstanceId && StarId == other.StarId;
        public override bool Equals(object obj) => obj is OwnedStar s && Equals(s);
        public override int GetHashCode() => InstanceId.GetHashCode() ^ (StarId ?? "").GetHashCode();
        public override string ToString() => "#" + InstanceId + " " + StarId;
    }

    public sealed class StarObtained : GameEvent
    {
        public long InstanceId { get; }
        public string StarId { get; }
        public StarObtained(long instanceId, string starId) { InstanceId = instanceId; StarId = starId; }
        public override string ToString() => "StarObtained #" + InstanceId + " " + StarId;
    }

    /// <summary>
    /// 가진 별 목록 (2026-11-17 Q2: C3에서 최소로 만들고 C4가 이어서 쓴다). 지금 빌드의 별만 받는다.
    /// </summary>
    internal sealed class StarInventory
    {
        readonly HashSet<string> _known;
        readonly List<OwnedStar> _stars = new List<OwnedStar>();

        /// <summary>다음에 줄 인스턴스 번호.</summary>
        public long NextInstanceId { get; private set; } = 1;

        public StarInventory(IEnumerable<string> knownStarIds)
        {
            if (knownStarIds == null) throw new ArgumentNullException(nameof(knownStarIds));
            _known = new HashSet<string>(knownStarIds, StringComparer.Ordinal);
        }

        public IReadOnlyList<OwnedStar> All => _stars;
        public bool IsKnown(string starId) => starId != null && _known.Contains(starId);

        /// <summary>
        /// 별 하나를 더할 수 있으면 null. 모르는 별 → InvalidTarget, 인스턴스 번호가 long 끝이면 InvalidInput.
        /// 한 번에 하나만 더한다(표 값의 크기만큼 반복하지 않음, 설계서 v0.10 · Codex C3 검토 K5).
        /// </summary>
        public ErrorCode? CheckAdd(string starId)
        {
            if (!IsKnown(starId)) return ErrorCode.InvalidTarget;
            if (NextInstanceId == long.MaxValue) return ErrorCode.InvalidInput;
            return null;
        }

        public void Add(string starId, List<GameEvent> events)
        {
            if (CheckAdd(starId) != null) throw new InvalidOperationException("더할 수 없는 별: " + starId);
            var star = new OwnedStar(NextInstanceId, starId);
            NextInstanceId++;
            _stars.Add(star);
            events.Add(new StarObtained(star.InstanceId, starId));
        }

        /// <summary>저장한 값으로. 번호는 1 이상 nextInstanceId 미만이고 겹치지 않으며, 별은 지금 빌드에 있어야 한다.</summary>
        public bool TryRestore(long nextInstanceId, IReadOnlyList<OwnedStar> stars, List<string> errors)
        {
            var ok = true;
            if (nextInstanceId < 1) { errors.Add("stars.nextInstanceId: 1 이상"); ok = false; }
            var seen = new HashSet<long>();
            foreach (var s in stars)
            {
                if (s.InstanceId < 1 || s.InstanceId >= nextInstanceId) { errors.Add("stars: 번호 " + s.InstanceId + "가 1 이상 nextInstanceId 미만이 아님"); ok = false; }
                if (!seen.Add(s.InstanceId)) { errors.Add("stars: 번호가 겹침 " + s.InstanceId); ok = false; }
                if (!IsKnown(s.StarId)) { errors.Add("stars: 지금 빌드에 없는 별 " + s.StarId); ok = false; }
            }
            if (!ok) return false;
            NextInstanceId = nextInstanceId;
            _stars.Clear();
            _stars.AddRange(stars);
            return true;
        }

        public bool SameState(StarInventory o)
        {
            if (o == null || o.NextInstanceId != NextInstanceId || o._stars.Count != _stars.Count) return false;
            for (var i = 0; i < _stars.Count; i++) if (!o._stars[i].Equals(_stars[i])) return false;
            return true;
        }

        public StarInventory Clone()
        {
            var c = new StarInventory(_known) { NextInstanceId = NextInstanceId };
            c._stars.AddRange(_stars);
            return c;
        }
    }
}
