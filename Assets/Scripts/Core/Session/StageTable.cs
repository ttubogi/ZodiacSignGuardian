using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Numbers;

namespace ZodiacSignGuardian.Core.Session
{
    /// <summary>스테이지 하나 (stages.csv의 지금 빌드 행). C4는 오프라인 분당 보상만 쓰고, 처치·넘어가기·보스는 C5.</summary>
    public sealed class StageInfo
    {
        public int StageId { get; }
        public int Chapter { get; }
        public int StageInChapter { get; }
        public GameNumber MonsterHp { get; }
        public GameNumber DustPerKill { get; }
        public GameNumber OfflineDustPerMin { get; }
        public int KillsToClear { get; }
        public int ClearTimeSec { get; }

        public StageInfo(int stageId, int chapter, int stageInChapter, GameNumber monsterHp, GameNumber dustPerKill, GameNumber offlineDustPerMin,
            int killsToClear, int clearTimeSec)
        {
            StageId = stageId;
            Chapter = chapter;
            StageInChapter = stageInChapter;
            MonsterHp = monsterHp;
            DustPerKill = dustPerKill;
            OfflineDustPerMin = offlineDustPerMin;
            KillsToClear = killsToClear;
            ClearTimeSec = clearTimeSec;
        }
    }

    /// <summary>지금 빌드의 스테이지 표. 번호는 1부터 빠짐없이 이어진다(검사 11).</summary>
    internal sealed class StageTable
    {
        readonly Dictionary<int, StageInfo> _byId = new Dictionary<int, StageInfo>();

        public StageTable(IEnumerable<StageInfo> stages)
        {
            foreach (var s in stages)
            {
                if (_byId.ContainsKey(s.StageId)) throw new ArgumentException("스테이지 번호가 겹침: " + s.StageId);
                _byId[s.StageId] = s;
            }
            for (var i = 1; i <= _byId.Count; i++)
            {
                if (!_byId.ContainsKey(i)) throw new ArgumentException("스테이지 " + i + "이 없음 (1부터 빠짐없이)");
            }
            if (_byId.Count == 0) throw new ArgumentException("스테이지가 하나도 없음");
        }

        public int Count => _byId.Count;
        public bool Has(long stageId) => stageId >= 1 && stageId <= _byId.Count;
        public StageInfo this[int stageId] => _byId[stageId];

        /// <summary>그 스테이지의 분당 오프라인 별가루. 표에 없으면 0(테스트용 스테이지 등).</summary>
        public GameNumber OfflineRate(int stageId) => _byId.TryGetValue(stageId, out var s) ? s.OfflineDustPerMin : GameNumber.Zero;
    }
}
