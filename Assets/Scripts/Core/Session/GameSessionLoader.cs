using System;
using System.Collections.Generic;
using System.Linq;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Randomness;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Timing;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Session
{
    /// <summary>불러오기 결과. Ok일 때만 Session이 있다. 실패해도 저장 문자열은 지우지 않는다(Services, 설계서 4절).</summary>
    public sealed class SessionLoadResult
    {
        public GameSession Session { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Ok => Session != null;

        internal SessionLoadResult(GameSession session, IReadOnlyList<string> errors)
        {
            Session = session;
            Errors = errors;
        }
    }

    /// <summary>
    /// 게임 세션을 만들고 불러온다. Services가 TableBuilder.LoadJson이 성공했을 때 받은 표 묶음을 넘긴다 (설계서 4절 "표 데이터의 흐름").
    /// 표 형식(Data)은 여기서만 다루고, View가 쓰는 GameSession의 기능에는 드러내지 않는다.
    /// <list type="bullet">
    /// <item>새 계정: 재화 0, 서버 시각 없음, 시드는 Services가 보안 난수로 만든 64비트 하나.</item>
    /// <item>불러오기: 앱 시작 순서(설계서 3절) — 활성, 정산 대기, 시간 미확인, 최소 허용 번호 1. 저장 검사는 출시 빌드에서도 한다.</item>
    /// <item>재화는 지금 빌드(m1) 행만 쓴다.</item>
    /// </list>
    /// </summary>
    public static class GameSessionLoader
    {
        /// <summary>지금 만드는 빌드. 표의 build 열이 이 값인 행만 판정에 쓴다.</summary>
        public const string CurrentBuild = Schemas.M1;

        /// <summary>
        /// 새 계정. 표에서 Core가 쓰는 값을 읽지 못하면(규칙이 없거나 범위 밖) 예외 대신 Errors로 돌려준다.
        /// sessionNonce는 Services가 새 세션마다 보안 난수로 만든 64비트 값(광고 번호에만 씀, 저장하지 않음).
        /// tables가 null이면 ArgumentNullException(코드 잘못).
        /// </summary>
        public static SessionLoadResult NewAccount(TableSet tables, ulong seed, ulong sessionNonce, GameSessionOptions options = null)
        {
            var errors = new List<string>();
            if (!TryFromTables(tables, errors, out var parts)) return new SessionLoadResult(null, errors);
            return new SessionLoadResult(Make(parts, new BattleAccumulator(), GameRng.FromSeed(seed), sessionNonce, options), errors);
        }

        static readonly Dictionary<string, Element> ElementIds = new Dictionary<string, Element>(StringComparer.Ordinal)
        {
            ["wood"] = Element.Wood, ["fire"] = Element.Fire, ["earth"] = Element.Earth, ["metal"] = Element.Metal, ["water"] = Element.Water,
        };

        static readonly Dictionary<string, Rarity> RarityIds = new Dictionary<string, Rarity>(StringComparer.Ordinal)
        {
            ["common"] = Rarity.Common, ["rare"] = Rarity.Rare, ["legend"] = Rarity.Legend,
        };

        /// <summary>
        /// 저장 불러오기(앱 시작·클라우드 상태로 교체). 불러온 상태는 활성·정산 대기·시간 미확인이고, 저장된 정산 기준 시점·활성 경과·
        /// 떠날 때 스테이지·미수령 묶음은 그대로 둔다(정산 대기만 켬, 설계서 v0.9 3절 "실행 상태").
        /// </summary>
        public static SessionLoadResult Load(TableSet tables, string saveJson, ulong sessionNonce, GameSessionOptions options = null)
        {
            var errors = new List<string>();
            if (!TryFromTables(tables, errors, out var parts)) return new SessionLoadResult(null, errors);
            if (!SaveFormat.TryRead(saveJson, out var d, out var error))
            {
                errors.Add(error);
                return new SessionLoadResult(null, errors);
            }
            var wallet = parts.Wallet;
            var clock = parts.Clock;

            // 재화: 표와 같은 id 집합, 종류에 맞는 값
            var balances = new Dictionary<string, CurrencyAmount>(StringComparer.Ordinal);
            foreach (var m in d.Wallet)
            {
                if (!wallet.Has(m.Key)) { errors.Add("wallet: 표에 없는 재화 " + m.Key); continue; }
                if (!SaveFormat.TryAmount(m.Value, wallet.KindOf(m.Key), out var amount)) { errors.Add("wallet." + m.Key + ": " + wallet.KindOf(m.Key) + " 값이 아님"); continue; }
                balances[m.Key] = amount;
            }
            foreach (var id in wallet.CurrencyIds)
            {
                if (!balances.ContainsKey(id) && !d.Wallet.Exists(m => m.Key == id)) errors.Add("wallet: 빠진 재화 " + id);
            }
            if (errors.Count == 0 && !wallet.TryRestore(balances)) errors.Add("wallet: 음수 잔액");

            if (!clock.TryRestore(d.AnchorUtcMs, d.SyncElapsedUs, d.ProcessedDay, d.ProcessedWeek)) errors.Add("clock: 값이 맞지 않음 (기준 시각 범위, 음수 누적, 기준 시각 없는 구간)");
            var battle = new BattleAccumulator();
            if (!battle.TryRestore(d.BattlePendingUs)) errors.Add("clock.battlePendingUs: 0 이상 " + BattleAccumulator.TickUs + " 미만이어야 함");
            if (!GameRng.TryFromSaveStrings(d.Rng, out var rng)) errors.Add("rng: 16진수 소문자 16자리 12개, 흐름마다 모두 0이 아니어야 함");
            if (d.SaveRevision < 1) errors.Add("saveRevision: 저장된 번호는 1 이상");
            if (d.Chapter < 1 || d.Chapter > int.MaxValue) errors.Add("progress.chapter: 1 이상");
            if (d.Stage < 1 || d.Stage > int.MaxValue) errors.Add("progress.stage: 1 이상");
            if (d.LastSavedUtcMs != null && !Clock.IsValidServerUtcMs(d.LastSavedUtcMs.Value)) errors.Add("lastSavedUtcMs: 범위 밖");

            parts.Offline.TryRestore(d.SettlementPointUtcMs, d.ActiveElapsedUs, d.LeaveStage, d.NextBundleId,
                d.BundleId, d.BundleMinutes, d.BundleAmount, d.BundleAdApplied == 1, errors);
            if (d.AttendanceLastClaimedDay != null && !clock.IsPossibleDay(d.AttendanceLastClaimedDay.Value)) errors.Add("attendance.lastClaimedDay: 범위 밖");
            else parts.Attendance.TryRestore(d.AttendanceNextDay, d.AttendanceLastClaimedDay, errors);
            // 등급·스킬 레벨은 long으로 읽었으므로 int로 바꾸기 전에 범위를 본다 (AGENTS.md 배운 점 2026-11-17)
            var stars = new List<OwnedStar>();
            var starsReadable = true;
            foreach (var st in d.Stars)
            {
                if (st.Grade < 1 || st.Grade > StarInventory.MaxGrade) { errors.Add("stars: " + st.StarId + "의 등급 " + st.Grade + "가 1~" + StarInventory.MaxGrade + " 밖"); starsReadable = false; continue; }
                if (st.SkillLevel < 1 || st.SkillLevel > parts.Catalog.Rules.MaxSkillLevel) { errors.Add("stars: " + st.StarId + "의 스킬 레벨 " + st.SkillLevel + "가 1~" + parts.Catalog.Rules.MaxSkillLevel + " 밖"); starsReadable = false; continue; }
                stars.Add(new OwnedStar(st.Id, st.StarId, (int)st.Grade, (int)st.SkillLevel));
            }
            var starsOk = starsReadable && parts.Stars.TryRestore(d.NextStarInstanceId, stars, errors);
            // 판 배치는 가진 별을 알아야 검사할 수 있다. 별을 읽지 못했으면 판 검사는 연쇄 문제가 되므로 건너뛴다 (이미 오류로 남음)
            if (starsOk && d.Chapter >= 1 && d.Chapter <= int.MaxValue)
            {
                var boards = new Dictionary<int, long[]>();
                foreach (var kv in d.Boards) boards[(int)kv.Key] = kv.Value; // 키는 SaveFormat이 1~int 범위로 읽음
                parts.Boards.TryRestore(boards, parts.Stars, (int)d.Chapter, errors);
            }
            if (d.FreeDrawDay != null && !clock.IsPossibleDay(d.FreeDrawDay.Value)) errors.Add("freeDraw.day: 범위 밖");
            else parts.FreeDraws.TryRestore(d.FreeDrawDay, d.FreeDrawUsed, parts.Catalog.Rules.FreeDrawsPerDay, errors);
            // 스테이지·떠날 때 스테이지는 지금 빌드의 스테이지 표에 있어야 한다 (설계서 v0.12 "불러올 때 C4 검사")
            if (!parts.Stages.Has(d.Stage)) errors.Add("progress.stage: 스테이지 표에 없는 " + d.Stage);
            if (!parts.Stages.Has(d.LeaveStage)) errors.Add("offline.leaveStage: 스테이지 표에 없는 " + d.LeaveStage);
            var missions = new Dictionary<string, KeyValuePair<long, bool>>(StringComparer.Ordinal);
            foreach (var kv in d.Missions) missions[kv.Key] = new KeyValuePair<long, bool>(kv.Value.Key, kv.Value.Value == 1);
            parts.Missions.TryRestore(missions, errors);

            if (errors.Count > 0) return new SessionLoadResult(null, errors);
            var session = Make(parts, battle, rng, sessionNonce, options);
            session.RestoreMeta(d.SaveRevision, (int)d.Chapter, (int)d.Stage);
            return new SessionLoadResult(session, errors);
        }

        static GameSession Make(Parts p, BattleAccumulator battle, GameRng rng, ulong sessionNonce, GameSessionOptions options)
        {
            var session = new GameSession(p.Wallet, p.Clock, battle, rng, p.Missions, p.Attendance, p.Stars, p.Offline, sessionNonce, options,
                p.Boards, p.FreeDraws, p.Stages);
            return session;
        }

        /// <summary>표 없이 저장의 요약만 읽는다 (두 기기 충돌 때 클라우드 쪽 요약을 보여줄 때). 형식이 틀리면 false.</summary>
        public static bool TryReadSummary(string saveJson, out SaveSummary summary)
        {
            summary = null;
            if (!SaveFormat.TryRead(saveJson, out var d, out _)) return false;
            if (d.Chapter < 1 || d.Chapter > int.MaxValue || d.SaveRevision < 1) return false;
            var dust = GameNumber.Zero;
            foreach (var m in d.Wallet)
            {
                if (m.Key != GameSession.StardustId) continue;
                if (!(m.Value is string s) || !GameNumber.TryParseSave(s, out dust)) return false;
            }
            summary = new SaveSummary(d.SaveRevision, (int)d.Chapter, dust, d.LastSavedUtcMs);
            return true;
        }

        sealed class Parts
        {
            public Wallet Wallet;
            public Clock Clock;
            public Missions Missions;
            public Attendance Attendance;
            public StarInventory Stars;
            public OfflineRewards Offline;
            public StarCatalog Catalog;
            public StarBoards Boards;
            public FreeDraws FreeDraws;
            public StageTable Stages;
        }

        /// <summary>
        /// 표에서 Core가 쓰는 값을 읽는다. 표 검사(검사 10: daily·weekly 각 하나, 시계 설정 범위)를 통과한 표라면 늘 되지만,
        /// 검사가 보장하지 못한 경우에도 예외 대신 오류로 돌려준다 (Codex 검토 2026-11-16). long을 int로 그냥 바꾸지 않는다.
        /// 임무·별·재화는 지금 빌드(m1) 행만 쓴다. 출석은 cycle 규칙의 cycle_days만큼 1일째부터 빠짐없이 있어야 한다.
        /// </summary>
        static bool TryFromTables(TableSet tables, List<string> errors, out Parts parts)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            parts = null;
            var before = errors.Count;

            var currencies = new List<KeyValuePair<string, CurrencyKind>>();
            var currencyUnreadable = false;
            foreach (var r in tables[Schemas.Currencies].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                if (!r.AllValid("currency_id", "number_type")) { errors.Add("currencies: " + r.Line + "번 행을 읽지 못함"); currencyUnreadable = true; continue; }
                var kind = r.Str("number_type") == "game_number" ? CurrencyKind.GameNumber : CurrencyKind.Integer;
                currencies.Add(new KeyValuePair<string, CurrencyKind>(r.Str("currency_id"), kind));
            }
            // 오프라인 보상을 받는 별가루는 반드시 있어야 하고 GameNumber여야 한다. 없을 때 분당 보상 0으로 숨기지 않는다 (K11, Codex C3 검토).
            // 읽지 못한 재화 행이 있으면 그 행이 stardust일 수 있으므로 연쇄 문제를 내지 않는다(이미 오류로 남음).
            if (!currencyUnreadable)
            {
                var dust = currencies.Where(c => c.Key == GameSession.StardustId).ToList();
                if (dust.Count == 0) errors.Add("currencies: 지금 빌드에 " + GameSession.StardustId + "가 없음 (오프라인 보상 재화)");
                else if (dust.Any(c => c.Value != CurrencyKind.GameNumber)) errors.Add("currencies." + GameSession.StardustId + ": number_type이 game_number가 아님");
            }

            var rules = tables[Schemas.ResetRules].Rows;
            var daily = One(rules, "daily", errors);
            var weekly = One(rules, "weekly", errors);
            var cycle = One(rules, "cycle", errors);
            var offlineRows = tables[Schemas.OfflineReward].Rows;
            if (offlineRows.Count != 1) errors.Add("offline_reward: 행이 정확히 1개여야 함 (지금 " + offlineRows.Count + "개)");
            if (errors.Count > before) return false;

            var offline = offlineRows[0];
            var ok = Int(daily, "utc_offset_min", errors, out var offset)
                & Int(daily, "reset_hour", errors, out var dailyHour)
                & Int(weekly, "reset_hour", errors, out var weeklyHour)
                & Int(daily, "sync_interval_min", errors, out var syncInterval)
                & Int(daily, "anchor_valid_min", errors, out var anchorValid)
                & Int(daily, "max_rtt_sec", errors, out var maxRtt)
                & Int(offline, "max_frame_sec", errors, out var maxFrame)
                & Int(offline, "max_minutes", errors, out var maxMinutes)
                & Int(offline, "min_minutes", errors, out var minMinutes)
                & Int(offline, "ad_multiplier", errors, out var adMultiplier)
                & Int(cycle, "cycle_days", errors, out var cycleDays);
            var weekday = -1;
            if (!weekly.IsValid("weekday") || weekly.IsBlank("weekday")) { errors.Add("reset_rules.weekly.weekday: 값이 없음"); ok = false; }
            else
            {
                try { weekday = ClockConfig.WeekdayIndex(weekly.Str("weekday")); }
                catch (ArgumentException e) { errors.Add("reset_rules.weekly.weekday: " + e.Message); ok = false; }
            }
            if (!ok) return false;

            var p = new Parts();
            try
            {
                var config = new ClockConfig(offset, dailyHour, weeklyHour, weekday, syncInterval, anchorValid, maxRtt, maxFrame);
                p.Clock = new Clock(config);
                p.Wallet = new Wallet(currencies);
                p.Offline = new OfflineRewards(new OfflineConfig(maxMinutes, minMinutes, adMultiplier));
            }
            catch (ArgumentException e)
            {
                errors.Add("시계·재화·오프라인 설정이 맞지 않음: " + e.Message);
                return false;
            }

            // 별·희귀도·뽑기 설정·판·스테이지 (C4, 설계서 v0.12)
            if (!TryStars(tables, p.Wallet, errors, out p.Catalog)) return false;
            p.Stars = new StarInventory(p.Catalog);
            if (!TryBoards(tables, p.Catalog, errors, out p.Boards)) return false;
            p.FreeDraws = new FreeDraws();
            if (!TryStages(tables, errors, out p.Stages)) return false;

            // 임무: 지금 빌드 행
            var missions = new List<MissionDefinition>();
            foreach (var r in tables[Schemas.DailyMissions].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                if (!r.AllValid("mission_id", "condition", "target_count", "reward_currency", "reward_amount")) { errors.Add("daily_missions: " + r.Line + "번 행을 읽지 못함"); continue; }
                var currency = r.Str("reward_currency");
                if (!p.Wallet.Has(currency)) { errors.Add("daily_missions." + r.Str("mission_id") + ": 지금 빌드에 없는 재화 " + currency); continue; }
                missions.Add(new MissionDefinition(r.Str("mission_id"), r.Str("condition"), r.Long("target_count"), currency, r.Long("reward_amount")));
            }

            // 출석
            var attendance = new List<AttendanceReward>();
            foreach (var r in tables[Schemas.Attendance].Rows)
            {
                if (!r.AllValid("day", "reward_kind", "reward_id", "amount")) { errors.Add("attendance: " + r.Line + "번 행을 읽지 못함"); continue; }
                var day = r.Long("day");
                if (day < 1 || day > cycleDays) { errors.Add("attendance: " + day + "일째가 1~cycle_days 밖"); continue; }
                var kind = r.Str("reward_kind") == "star" ? AttendanceRewardKind.Star : AttendanceRewardKind.Currency;
                var rewardId = r.Str("reward_id");
                if (kind == AttendanceRewardKind.Currency && !p.Wallet.Has(rewardId)) { errors.Add("attendance " + day + "일째: 지금 빌드에 없는 재화 " + rewardId); continue; }
                if (kind == AttendanceRewardKind.Star && !p.Stars.IsKnown(rewardId)) { errors.Add("attendance " + day + "일째: 지금 빌드에 없는 별 " + rewardId); continue; }
                if (kind == AttendanceRewardKind.Star && r.Long("amount") != 1) { errors.Add("attendance " + day + "일째: 별 보상의 amount는 1이어야 함 (지금 " + r.Long("amount") + ")"); continue; }
                attendance.Add(new AttendanceReward((int)day, kind, rewardId, r.Long("amount")));
            }
            if (errors.Count > before) return false;

            try
            {
                p.Missions = new Missions(missions);
                p.Attendance = new Attendance(cycleDays, attendance);
            }
            catch (ArgumentException e)
            {
                errors.Add("임무·출석 표가 맞지 않음: " + e.Message);
                return false;
            }
            parts = p;
            return true;
        }

        /// <summary>별(지금 빌드)·희귀도·뽑기 설정을 읽는다. 표 검사(8·12)를 통과한 표면 늘 되지만, 아닐 때도 예외 대신 오류로.</summary>
        static bool TryStars(TableSet tables, Wallet wallet, List<string> errors, out StarCatalog catalog)
        {
            catalog = null;
            var before = errors.Count;
            var stars = new List<StarInfo>();
            foreach (var r in tables[Schemas.Stars].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                if (!r.AllValid("star_id", "element", "rarity")) { errors.Add("stars: " + r.Line + "번 행을 읽지 못함"); continue; }
                if (!ElementIds.TryGetValue(r.Str("element"), out var element) || !RarityIds.TryGetValue(r.Str("rarity"), out var rarity))
                {
                    errors.Add("stars." + r.Str("star_id") + ": 모르는 속성·희귀도");
                    continue;
                }
                stars.Add(new StarInfo(r.Str("star_id"), element, rarity));
            }
            var rarities = new List<RarityInfo>();
            foreach (var r in tables[Schemas.Rarities].Rows)
            {
                var cols = new[] { "rarity", "draw_weight", "dup_shards", "skill_coef_bp", "skill_cost_per_level", "grade2_cost", "grade3_cost", "grade4_cost", "grade5_cost" };
                if (!r.AllValid(cols)) { errors.Add("rarities: " + r.Line + "번 행을 읽지 못함"); continue; }
                if (!RarityIds.TryGetValue(r.Str("rarity"), out var rarity)) { errors.Add("rarities: 모르는 희귀도 " + r.Str("rarity")); continue; }
                if (!IntIn(r, "draw_weight", 0, 10000, errors, out var weight) | !IntIn(r, "skill_coef_bp", 1, 100000, errors, out var coef)) continue;
                rarities.Add(new RarityInfo(rarity, weight, r.Long("dup_shards"), coef, r.Long("skill_cost_per_level"),
                    new[] { r.Long("grade2_cost"), r.Long("grade3_cost"), r.Long("grade4_cost"), r.Long("grade5_cost") }));
            }
            var ruleRows = tables[Schemas.StarRules].Rows;
            if (ruleRows.Count != 1) { errors.Add("star_rules: 행이 정확히 1개여야 함 (지금 " + ruleRows.Count + "개)"); return false; }
            var rule = ruleRows[0];
            var ok = IntIn(rule, "free_draws_per_day", 0, 10, errors, out var free)
                & IntIn(rule, "max_skill_level", 1, 1000, errors, out var maxSkill)
                & IntIn(rule, "skill_step_bp", 0, 10000, errors, out var step)
                & IntIn(rule, "own_bonus_bp", 0, 10000, errors, out var own)
                & IntIn(rule, "link_same_bp", 0, 10000, errors, out var same)
                & IntIn(rule, "link_generate_bp", 0, 10000, errors, out var generate)
                & IntIn(rule, "board_complete_bp", 0, 10000, errors, out var complete);
            if (!rule.AllValid("draw_currency", "shard_currency", "draw_cost", "draw10_cost")) { errors.Add("star_rules: 행을 읽지 못함"); return false; }
            foreach (var col in new[] { "draw_currency", "shard_currency" })
            {
                var id = rule.Str(col);
                if (!wallet.Has(id)) { errors.Add("star_rules." + col + ": 지금 빌드에 없는 재화 " + id); ok = false; }
                else if (wallet.KindOf(id) != CurrencyKind.Integer) { errors.Add("star_rules." + col + ": 정수 재화가 아님 " + id); ok = false; }
            }
            if (!ok || errors.Count > before) return false;
            try
            {
                var rules = new StarRules(rule.Str("draw_currency"), rule.Long("draw_cost"), rule.Long("draw10_cost"), free, rule.Str("shard_currency"),
                    maxSkill, step, own, same, generate, complete);
                catalog = new StarCatalog(stars, rarities, rules);
            }
            catch (ArgumentException e)
            {
                errors.Add("별·희귀도·뽑기 설정이 맞지 않음: " + e.Message);
                return false;
            }
            return true;
        }

        /// <summary>지금 빌드의 판(장마다 선 목록). 검사 9를 통과한 표면 늘 된다.</summary>
        static bool TryBoards(TableSet tables, StarCatalog catalog, List<string> errors, out StarBoards boards)
        {
            boards = null;
            var links = new SortedDictionary<long, List<BoardLink>>();
            var ok = true;
            foreach (var r in tables[Schemas.Boards].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                if (!r.AllValid("chapter", "slot_a", "slot_b")) { errors.Add("boards: " + r.Line + "번 행을 읽지 못함"); ok = false; continue; }
                long ch = r.Long("chapter"), a = r.Long("slot_a"), b = r.Long("slot_b");
                if (ch > int.MaxValue || a > StarBoards.SlotCount || b > StarBoards.SlotCount) { errors.Add("boards: " + r.Line + "번 행의 장·칸이 범위 밖"); ok = false; continue; }
                if (!links.TryGetValue(ch, out var list)) links[ch] = list = new List<BoardLink>();
                list.Add(new BoardLink((int)a, (int)b));
            }
            if (!ok) return false;
            try
            {
                var defs = new List<BoardDefinition>();
                foreach (var kv in links) defs.Add(new BoardDefinition((int)kv.Key, kv.Value));
                boards = new StarBoards(catalog, defs);
            }
            catch (ArgumentException e)
            {
                errors.Add("판 표가 맞지 않음: " + e.Message);
                return false;
            }
            return true;
        }

        /// <summary>지금 빌드의 스테이지. 검사 11을 통과한 표면 1부터 빠짐없이 이어진다.</summary>
        static bool TryStages(TableSet tables, List<string> errors, out StageTable stages)
        {
            stages = null;
            var list = new List<StageInfo>();
            var ok = true;
            foreach (var r in tables[Schemas.Stages].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                if (!r.AllValid("stage_id", "chapter", "stage_in_chapter", "monster_hp", "dust_per_kill", "offline_dust_per_min", "kills_to_clear", "clear_time_sec"))
                {
                    errors.Add("stages: " + r.Line + "번 행을 읽지 못함");
                    ok = false;
                    continue;
                }
                if (!IntIn(r, "stage_id", 1, int.MaxValue, errors, out var id) | !IntIn(r, "chapter", 1, int.MaxValue, errors, out var ch)
                    | !IntIn(r, "stage_in_chapter", 1, int.MaxValue, errors, out var inCh) | !IntIn(r, "kills_to_clear", 1, 1000, errors, out var kills)
                    | !IntIn(r, "clear_time_sec", 1, 3600, errors, out var time))
                {
                    ok = false;
                    continue;
                }
                if (!GameNumber.TryParseSave(r.Str("monster_hp"), out var hp) | !GameNumber.TryParseSave(r.Str("dust_per_kill"), out var dust)
                    | !GameNumber.TryParseSave(r.Str("offline_dust_per_min"), out var offline) || hp.Sign <= 0 || dust.Sign <= 0 || offline.Sign <= 0)
                {
                    errors.Add("stages." + id + ": 큰 수 칸을 읽지 못함");
                    ok = false;
                    continue;
                }
                list.Add(new StageInfo(id, ch, inCh, hp, dust, offline, kills, time));
            }
            if (!ok) return false;
            try
            {
                stages = new StageTable(list);
            }
            catch (ArgumentException e)
            {
                errors.Add("스테이지 표가 맞지 않음: " + e.Message);
                return false;
            }
            return true;
        }

        static bool IntIn(TableRow row, string column, long min, long max, List<string> errors, out int value)
        {
            value = 0;
            if (!row.IsValid(column) || row.IsBlank(column)) { errors.Add(column + ": 값이 없음"); return false; }
            var v = row.Long(column);
            if (v < min || v > max) { errors.Add(column + ": " + min + "~" + max + " 밖 (" + v + ")"); return false; }
            value = (int)v;
            return true;
        }

        static TableRow One(IReadOnlyList<TableRow> rules, string period, List<string> errors)
        {
            TableRow found = null;
            var count = 0;
            foreach (var r in rules)
            {
                if (!r.IsValid("period") || r.Str("period") != period) continue;
                count++;
                found = r;
            }
            if (count != 1) { errors.Add("reset_rules: " + period + " 규칙이 정확히 하나여야 함 (지금 " + count + "개)"); return null; }
            return found;
        }

        static bool Int(TableRow row, string column, List<string> errors, out int value)
        {
            value = 0;
            if (!row.IsValid(column) || row.IsBlank(column)) { errors.Add(column + ": 값이 없음"); return false; }
            var v = row.Long(column);
            if (v < int.MinValue || v > int.MaxValue) { errors.Add(column + ": int 범위 밖 (" + v + ")"); return false; }
            value = (int)v;
            return true;
        }
    }
}
