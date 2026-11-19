using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 설계서 2절의 표 15개. 날짜별 규칙 표 7개 + 정의 표 8개(C4에서 rarities·star_rules·stages 추가, v0.12).
    /// 열 이름·순서는 CSV 머리줄과 정확히 같아야 한다.
    /// 정의 표의 열 구성은 C1 검토 때 확정한다.
    /// </summary>
    public static class Schemas
    {
        public const string M1 = "m1";
        public const string V1 = "v1";

        public static readonly string[] Weekdays = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        public static readonly string[] Elements = { "wood", "fire", "earth", "metal", "water" };
        /// <summary>별 희귀도. 이 순서가 뽑기에서 확률 구간을 쌓는 순서다(설계서 v0.12 3절 "한 번 뽑기").</summary>
        public static readonly string[] RarityIds = { "common", "rare", "legend" };

        /// <summary>비용·별조각·보상 수량 칸의 상한 (설계서 v0.12 2절 열 범위). Core가 곱셈에서 넘치지 않게 한다.</summary>
        public const long MaxAmount = 1000000000;

        static ColumnSpec Id(string name, string refTable = null) => new ColumnSpec(name, ColumnKind.Id, refTable: refTable);
        static ColumnSpec Text(string name) => new ColumnSpec(name, ColumnKind.Text);
        static ColumnSpec Pos(string name, bool optional = false) => new ColumnSpec(name, ColumnKind.PositiveInt, optional);
        static ColumnSpec Build() => new ColumnSpec("build", ColumnKind.Build);

        /// <summary>
        /// 시계 설정 열 (검사 10). 범위는 2026-11-17 결정: utc_offset_min −720~840, sync_interval_min 1~60, anchor_valid_min 1~1440,
        /// max_rtt_sec 1~60, max_frame_sec 1~60. Core가 int로 받으므로 범위 밖 값이 표 검사를 지나가지 않게 한다
        /// (예전에는 sync_interval_min 4294967301이 통과해 int로 5가 됐음, Codex 검토 2026-11-16).
        /// </summary>
        static ColumnSpec Clock(string name, long min, long max) => new ColumnSpec(name, ColumnKind.IntRange, min: min, max: max, issueKind: TableIssueKind.Check10);
        static ColumnSpec Enum(string name, params string[] values) => new ColumnSpec(name, ColumnKind.Enum, enumValues: values);
        static ColumnSpec Range(string name, long min, long max, bool optional = false) => new ColumnSpec(name, ColumnKind.IntRange, optional, min: min, max: max);
        static ColumnSpec Amount(string name, bool optional = false) => Range(name, 1, MaxAmount, optional);
        static ColumnSpec Bp(string name) => Range(name, 0, 10000);
        static ColumnSpec Big(string name, bool optional = false) => new ColumnSpec(name, ColumnKind.GameNumber, optional);

        // ---- 날짜별 규칙 표 ----

        public static readonly TableSchema ResetRules = new TableSchema("reset_rules", "rule_id", M1, false,
            Id("rule_id"),
            Enum("period", "daily", "weekly", "cycle"),
            new ColumnSpec("reset_hour", ColumnKind.IntRange, optional: true, min: 0, max: 23),
            new ColumnSpec("weekday", ColumnKind.Weekday, optional: true),
            Pos("cycle_days", optional: true),
            Clock("utc_offset_min", -720, 840),
            Clock("sync_interval_min", 1, 60),
            Clock("anchor_valid_min", 1, 1440),
            Clock("max_rtt_sec", 1, 60));

        public static readonly TableSchema DailyMissions = new TableSchema("daily_missions", "mission_id", null, false,
            Id("mission_id"),
            Id("reset_rule", refTable: "reset_rules"),
            Enum("condition", "kill_monster", "upgrade_stat", "draw_star", "claim_offline", "place_star", "clear_boss"),
            Pos("target_count"),
            Id("reward_currency", refTable: "currencies"),
            Pos("reward_amount"),
            Build());

        /// <summary>reward_id는 reward_kind가 currency면 currencies, star면 stars를 가리킨다 (검사 2에서 따로 본다).</summary>
        public static readonly TableSchema Attendance = new TableSchema("attendance", "day", M1, false,
            Pos("day"),
            Enum("reward_kind", "currency", "star"),
            Id("reward_id"),
            Pos("amount"));

        public static readonly TableSchema OfflineReward = new TableSchema("offline_reward", null, M1, true,
            Pos("max_minutes"),
            Pos("min_minutes"),
            Pos("ad_multiplier"),
            Clock("max_frame_sec", 1, 60));

        public static readonly TableSchema WeekdayDungeons = new TableSchema("weekday_dungeons", "weekday", V1, false,
            new ColumnSpec("weekday", ColumnKind.Weekday),
            Id("dungeon_id"),
            Id("reward_currency", refTable: "currencies"),
            Pos("free_entries"));

        public static readonly TableSchema TimedEvents = new TableSchema("timed_events", "event_id", V1, false,
            Id("event_id"),
            new ColumnSpec("weekdays", ColumnKind.WeekdayList),
            new ColumnSpec("start_time", ColumnKind.TimeOfDay),
            new ColumnSpec("end_time", ColumnKind.TimeOfDayEnd),
            Id("reward_currency", refTable: "currencies"));

        public static readonly TableSchema WeeklyChallenge = new TableSchema("weekly_challenge", "min_stage", V1, false,
            Pos("min_stage"),
            Id("reward_currency", refTable: "currencies"),
            Pos("reward_amount"));

        // ---- 정의 표 (값은 임시) ----

        public static readonly TableSchema Currencies = new TableSchema("currencies", "currency_id", null, false,
            Id("currency_id"),
            Text("name_ko"),
            Enum("number_type", "integer", "game_number"),
            Build());

        public static readonly TableSchema Stars = new TableSchema("stars", "star_id", null, false,
            Id("star_id"),
            new ColumnSpec("element", ColumnKind.Enum, enumValues: Elements, issueKind: TableIssueKind.Check8),
            new ColumnSpec("rarity", ColumnKind.Enum, enumValues: RarityIds, issueKind: TableIssueKind.Check8),
            Text("name_ko"),
            Build());

        /// <summary>연결선 하나가 한 행. 칸 번호는 검사 9에서 1~5인지 본다.</summary>
        public static readonly TableSchema Boards = new TableSchema("boards", null, null, false,
            Pos("chapter"),
            Id("constellation_id"),
            new ColumnSpec("slot_a", ColumnKind.PositiveInt, issueKind: TableIssueKind.Check9),
            new ColumnSpec("slot_b", ColumnKind.PositiveInt, issueKind: TableIssueKind.Check9),
            Build());

        public static readonly TableSchema Stats = new TableSchema("stats", "stat_id", null, false,
            Id("stat_id"),
            Text("name_ko"),
            new ColumnSpec("max_level", ColumnKind.NonNegativeInt),
            Build());

        public static readonly TableSchema AdBuffs = new TableSchema("ad_buffs", "buff_id", null, false,
            Id("buff_id"),
            Pos("multiplier"),
            Pos("duration_min"),
            Build());

        // ---- C4 정의 표 (설계서 v0.12 2절) ----

        /// <summary>희귀도마다 뽑기 확률(1만분의 1), 중복 별조각, 스킬 계수, 스킬 레벨 비용 계수, 등급 2~5로 올리는 별조각.</summary>
        public static readonly TableSchema Rarities = new TableSchema("rarities", "rarity", M1, false,
            Enum("rarity", RarityIds),
            Range("draw_weight", 0, 10000),
            Amount("dup_shards"),
            Range("skill_coef_bp", 1, 100000),
            Amount("skill_cost_per_level"),
            Amount("grade2_cost"),
            Amount("grade3_cost"),
            Amount("grade4_cost"),
            Amount("grade5_cost"));

        /// <summary>뽑기·별 성장·판 보너스 설정 (한 행).</summary>
        public static readonly TableSchema StarRules = new TableSchema("star_rules", null, M1, true,
            Id("draw_currency", refTable: "currencies"),
            Amount("draw_cost"),
            Amount("draw10_cost"),
            Range("free_draws_per_day", 0, 10),
            Id("shard_currency", refTable: "currencies"),
            Range("max_skill_level", 1, 1000),
            Bp("skill_step_bp"),
            Bp("own_bonus_bp"),
            Bp("link_same_bp"),
            Bp("link_generate_bp"),
            Bp("board_complete_bp"));

        /// <summary>스테이지 (DataGen 출력, 손으로 고치지 않음). 보스 칸 4개는 장 마지막 행에만 (검사 11).</summary>
        public static readonly TableSchema Stages = new TableSchema("stages", "stage_id", null, false,
            Pos("stage_id"),
            Pos("chapter"),
            Pos("stage_in_chapter"),
            Big("monster_hp"),
            Big("dust_per_kill"),
            Big("offline_dust_per_min"),
            Range("kills_to_clear", 1, 1000),
            Range("clear_time_sec", 1, 3600),
            Id("first_clear_currency", refTable: "currencies"),
            Amount("first_clear_amount"),
            Big("boss_hp", optional: true),
            Range("boss_time_sec", 1, 3600, optional: true),
            new ColumnSpec("boss_reward_currency", ColumnKind.Id, optional: true, refTable: "currencies"),
            Amount("boss_reward_amount", optional: true),
            Build());

        /// <summary>표 15개. 순서는 설계서 2절 표 순서.</summary>
        public static readonly IReadOnlyList<TableSchema> All = new[]
        {
            ResetRules, DailyMissions, Attendance, OfflineReward, WeekdayDungeons, TimedEvents, WeeklyChallenge,
            Currencies, Stars, Boards, Stats, AdBuffs, Rarities, StarRules, Stages,
        };

        public static TableSchema Find(string name) => All.FirstOrDefault(s => s.Name == name);
    }
}
