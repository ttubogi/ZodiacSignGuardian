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
            var stars = new List<OwnedStar>();
            foreach (var kv in d.Stars) stars.Add(new OwnedStar(kv.Key, kv.Value));
            parts.Stars.TryRestore(d.NextStarInstanceId, stars, errors);
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
            return new GameSession(p.Wallet, p.Clock, battle, rng, p.Missions, p.Attendance, p.Stars, p.Offline, sessionNonce, options);
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

            // 별: 지금 빌드의 별 id
            var starIds = new List<string>();
            foreach (var r in tables[Schemas.Stars].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                if (!r.IsValid("star_id")) { errors.Add("stars: " + r.Line + "번 행을 읽지 못함"); continue; }
                starIds.Add(r.Str("star_id"));
            }
            p.Stars = new StarInventory(starIds);

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
