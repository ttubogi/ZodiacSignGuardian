namespace ZodiacSignGuardian.Core.Economy
{
    /// <summary>
    /// 재화가 바뀐 이유. 모든 증감에 하나가 붙는다 (위반 규칙 1).
    /// 숫자 값은 저장·기록·View 문구와 이어지므로 정한 번호를 바꾸거나 다시 쓰지 않는다. 새 이유는 끝에 새 번호로 더한다.
    /// View는 모르는 값에 일반 문구를 쓴다. (Codex 검토 2026-11-16)
    /// </summary>
    public enum CurrencyReason
    {
        /// <summary>사냥(몬스터 처치) 보상</summary>
        Hunt = 1,
        /// <summary>보스 처치 보상</summary>
        BossReward = 2,
        /// <summary>일일 임무 보상</summary>
        MissionReward = 3,
        /// <summary>출석 보상</summary>
        AttendanceReward = 4,
        /// <summary>오프라인 보상</summary>
        OfflineReward = 5,
        /// <summary>능력치 강화 비용</summary>
        StatUpgrade = 6,
        /// <summary>별 뽑기 비용</summary>
        StarDraw = 7,
        /// <summary>지팡이 강화 비용</summary>
        StaffEnhance = 8,
        /// <summary>가진 별을 다시 얻어 받은 별조각 (뽑기·출석, C4)</summary>
        StarDuplicate = 9,
        /// <summary>별 등급 올리기 비용 (C4)</summary>
        StarGradeUp = 10,
        /// <summary>별 스킬 레벨 올리기 비용 (C4)</summary>
        SkillLevelUp = 11,
    }
}
