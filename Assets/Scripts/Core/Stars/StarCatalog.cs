using System;
using System.Collections.Generic;

namespace ZodiacSignGuardian.Core.Stars
{
    /// <summary>별 속성(오행). 숫자는 상생 순서다: 나무 → 불 → 흙 → 쇠 → 물 → 나무.</summary>
    public enum Element
    {
        Wood = 0,
        Fire = 1,
        Earth = 2,
        Metal = 3,
        Water = 4,
    }

    /// <summary>별 희귀도. 숫자는 뽑기에서 확률 구간을 쌓는 순서다(설계서 v0.12 "한 번 뽑기").</summary>
    public enum Rarity
    {
        Common = 0,
        Rare = 1,
        /// <summary>영웅 (2026-11-20 추가: 일반·희귀·영웅·전설).</summary>
        Hero = 2,
        Legend = 3,
    }

    /// <summary>별 정의 하나 (stars.csv의 지금 빌드 행).</summary>
    public sealed class StarInfo
    {
        public string StarId { get; }
        public Element Element { get; }
        public Rarity Rarity { get; }

        public StarInfo(string starId, Element element, Rarity rarity)
        {
            StarId = starId ?? throw new ArgumentNullException(nameof(starId));
            Element = element;
            Rarity = rarity;
        }
    }

    /// <summary>희귀도 정의 (rarities.csv). 등급 비용은 다음 등급(2~5)으로 올리는 별조각.</summary>
    public sealed class RarityInfo
    {
        public Rarity Rarity { get; }
        /// <summary>뽑기 확률(1만분의 1).</summary>
        public int DrawWeight { get; }
        public long DupShards { get; }
        public int SkillCoefBp { get; }
        public long SkillCostPerLevel { get; }
        readonly long[] _gradeCosts;

        public RarityInfo(Rarity rarity, int drawWeight, long dupShards, int skillCoefBp, long skillCostPerLevel, long[] gradeCosts)
        {
            if (gradeCosts == null || gradeCosts.Length != StarInventory.MaxGrade - 1) throw new ArgumentException("등급 비용은 " + (StarInventory.MaxGrade - 1) + "개");
            if (drawWeight < 0 || drawWeight > 10000) throw new ArgumentOutOfRangeException(nameof(drawWeight));
            if (dupShards < 1 || skillCostPerLevel < 1 || skillCoefBp < 1) throw new ArgumentOutOfRangeException(nameof(dupShards), "양수");
            foreach (var c in gradeCosts) if (c < 1) throw new ArgumentOutOfRangeException(nameof(gradeCosts), "양수");
            Rarity = rarity;
            DrawWeight = drawWeight;
            DupShards = dupShards;
            SkillCoefBp = skillCoefBp;
            SkillCostPerLevel = skillCostPerLevel;
            _gradeCosts = (long[])gradeCosts.Clone();
        }

        /// <summary>grade(2~5)로 올리는 별조각.</summary>
        public long GradeCost(int nextGrade)
        {
            if (nextGrade < 2 || nextGrade > StarInventory.MaxGrade) throw new ArgumentOutOfRangeException(nameof(nextGrade));
            return _gradeCosts[nextGrade - 2];
        }
    }

    /// <summary>뽑기·별 성장·판 보너스 설정 (star_rules.csv 한 행).</summary>
    public sealed class StarRules
    {
        public string DrawCurrency { get; }
        public long DrawCost { get; }
        public long Draw10Cost { get; }
        public int FreeDrawsPerDay { get; }
        public string ShardCurrency { get; }
        public int MaxSkillLevel { get; }
        public int SkillStepBp { get; }
        public int OwnBonusBp { get; }
        public int LinkSameBp { get; }
        public int LinkGenerateBp { get; }
        public int BoardCompleteBp { get; }

        public StarRules(string drawCurrency, long drawCost, long draw10Cost, int freeDrawsPerDay, string shardCurrency, int maxSkillLevel,
            int skillStepBp, int ownBonusBp, int linkSameBp, int linkGenerateBp, int boardCompleteBp)
        {
            if (drawCurrency == null || shardCurrency == null) throw new ArgumentNullException(nameof(drawCurrency));
            if (drawCurrency == shardCurrency) throw new ArgumentException("뽑기 재화와 별조각 재화가 같음");
            if (drawCost < 1 || draw10Cost < 1) throw new ArgumentOutOfRangeException(nameof(drawCost));
            if (freeDrawsPerDay < 0) throw new ArgumentOutOfRangeException(nameof(freeDrawsPerDay));
            if (maxSkillLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxSkillLevel));
            foreach (var bp in new[] { skillStepBp, ownBonusBp, linkSameBp, linkGenerateBp, boardCompleteBp })
            {
                if (bp < 0 || bp > 10000) throw new ArgumentOutOfRangeException(nameof(skillStepBp), "0~10000");
            }
            DrawCurrency = drawCurrency;
            DrawCost = drawCost;
            Draw10Cost = draw10Cost;
            FreeDrawsPerDay = freeDrawsPerDay;
            ShardCurrency = shardCurrency;
            MaxSkillLevel = maxSkillLevel;
            SkillStepBp = skillStepBp;
            OwnBonusBp = ownBonusBp;
            LinkSameBp = linkSameBp;
            LinkGenerateBp = linkGenerateBp;
            BoardCompleteBp = boardCompleteBp;
        }
    }

    /// <summary>지금 빌드의 별·희귀도 정의 묶음. 뽑기 목록은 희귀도마다 star_id 서수 순서다.</summary>
    internal sealed class StarCatalog
    {
        readonly Dictionary<string, StarInfo> _byId = new Dictionary<string, StarInfo>(StringComparer.Ordinal);
        readonly List<StarInfo> _all = new List<StarInfo>();
        readonly Dictionary<Rarity, RarityInfo> _rarities = new Dictionary<Rarity, RarityInfo>();
        readonly Dictionary<Rarity, List<string>> _pool = new Dictionary<Rarity, List<string>>();

        public StarRules Rules { get; }

        public StarCatalog(IEnumerable<StarInfo> stars, IEnumerable<RarityInfo> rarities, StarRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            foreach (var r in rarities)
            {
                if (_rarities.ContainsKey(r.Rarity)) throw new ArgumentException("희귀도가 겹침: " + r.Rarity);
                _rarities[r.Rarity] = r;
                _pool[r.Rarity] = new List<string>();
            }
            var weight = 0;
            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
            {
                if (!_rarities.ContainsKey(r)) throw new ArgumentException("빠진 희귀도: " + r);
                weight += _rarities[r].DrawWeight;
            }
            if (weight != 10000) throw new ArgumentException("뽑기 확률 합이 10000이 아님: " + weight);
            foreach (var s in stars)
            {
                if (_byId.ContainsKey(s.StarId)) throw new ArgumentException("별이 겹침: " + s.StarId);
                _byId[s.StarId] = s;
                _all.Add(s);
                _pool[s.Rarity].Add(s.StarId);
            }
            foreach (var list in _pool.Values) list.Sort(StringComparer.Ordinal);
            foreach (var r in _rarities.Values)
            {
                if (r.DrawWeight > 0 && _pool[r.Rarity].Count == 0) throw new ArgumentException("확률이 있는 희귀도에 별이 없음: " + r.Rarity);
            }
        }

        public IReadOnlyList<StarInfo> All => _all;
        public bool IsKnown(string starId) => starId != null && _byId.ContainsKey(starId);
        public StarInfo Find(string starId) => starId != null && _byId.TryGetValue(starId, out var s) ? s : null;
        public RarityInfo RarityOf(string starId) => _rarities[_byId[starId].Rarity];
        public RarityInfo Rarity(Rarity r) => _rarities[r];
        /// <summary>그 희귀도의 별을 star_id 서수 순서로.</summary>
        public IReadOnlyList<string> Pool(Rarity r) => _pool[r];

        /// <summary>두 속성이 같거나 상생 쌍(나무-불, 불-흙, 흙-쇠, 쇠-물, 물-나무)인지. 같으면 Same, 상생이면 Generate, 아니면 null.</summary>
        public static LinkKind? LinkOf(Element a, Element b)
        {
            if (a == b) return LinkKind.Same;
            var d = ((int)b - (int)a + 5) % 5;
            return d == 1 || d == 4 ? LinkKind.Generate : (LinkKind?)null;
        }
    }

    /// <summary>켜진 연결선의 종류.</summary>
    public enum LinkKind
    {
        Same = 1,
        Generate = 2,
    }
}
