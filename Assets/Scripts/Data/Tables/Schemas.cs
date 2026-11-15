using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 설계서 v0.7 2절의 표 12개. 날짜별 규칙 표 7개 + 정의 표 5개.
    /// 열 이름·순서는 CSV 머리줄과 정확히 같아야 한다.
    /// 정의 표의 열 구성은 C1 검토 때 확정한다.
    /// </summary>
    public static class Schemas
    {
        public const string M1 = "m1";
        public const string V1 = "v1";

        public static readonly string[] Weekdays = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        public static readonly string[] Elements = { "wood", "fire", "earth", "metal", "water" };

        static ColumnSpec Id(string name, string refTable = null) => new ColumnSpec(name, ColumnKind.Id, refTable: refTable);
        static ColumnSpec Text(string name) => new ColumnSpec(name, ColumnKind.Text);
        static ColumnSpec Pos(string name, bool optional = false) => new ColumnSpec(name, ColumnKind.PositiveInt, optional);
        static ColumnSpec Build() => new ColumnSpec("build", ColumnKind.Build);
        static ColumnSpec Enum(string name, params string[] values) => new ColumnSpec(name, ColumnKind.Enum, enumValues: values);

        // ---- 날짜별 규칙 표 ----

        public static readonly TableSchema ResetRules = new TableSchema("reset_rules", "rule_id", M1, false,
            Id("rule_id"),
            Enum("period", "daily", "weekly", "cycle"),
            new ColumnSpec("reset_hour", ColumnKind.IntRange, optional: true, min: 0, max: 23),
            new ColumnSpec("weekday", ColumnKind.Weekday, optional: true),
            Pos("cycle_days", optional: true),
            new ColumnSpec("utc_offset_min", ColumnKind.IntRange, min: -720, max: 840),
            Pos("sync_interval_min"),
            Pos("anchor_valid_min"),
            Pos("max_rtt_sec"));

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
            Pos("max_frame_sec"));

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

        /// <summary>표 12개. 순서는 설계서 2절 표 순서.</summary>
        public static readonly IReadOnlyList<TableSchema> All = new[]
        {
            ResetRules, DailyMissions, Attendance, OfflineReward, WeekdayDungeons, TimedEvents, WeeklyChallenge,
            Currencies, Stars, Boards, Stats, AdBuffs,
        };

        public static TableSchema Find(string name) => All.FirstOrDefault(s => s.Name == name);
    }
}
