using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Randomness;
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

        public static GameSession NewAccount(TableSet tables, ulong seed, GameSessionOptions options = null)
        {
            var (wallet, clock) = FromTables(tables);
            return new GameSession(wallet, clock, new BattleAccumulator(), GameRng.FromSeed(seed), options);
        }

        public static SessionLoadResult Load(TableSet tables, string saveJson, GameSessionOptions options = null)
        {
            var (wallet, clock) = FromTables(tables);
            var errors = new List<string>();
            if (!SaveFormat.TryRead(saveJson, out var d, out var error))
            {
                errors.Add(error);
                return new SessionLoadResult(null, errors);
            }

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
            if (d.LastSavedUtcMs != null && !Clock.IsValidServerUtcMs(d.LastSavedUtcMs.Value)) errors.Add("lastSavedUtcMs: 범위 밖");

            if (errors.Count > 0) return new SessionLoadResult(null, errors);
            var session = new GameSession(wallet, clock, battle, rng, options);
            session.RestoreMeta(d.SaveRevision, (int)d.Chapter);
            return new SessionLoadResult(session, errors);
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

        static (Wallet, Clock) FromTables(TableSet tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var currencies = new List<KeyValuePair<string, CurrencyKind>>();
            foreach (var r in tables[Schemas.Currencies].Rows)
            {
                if (r.Build != CurrentBuild) continue;
                var kind = r.Str("number_type") == "game_number" ? CurrencyKind.GameNumber : CurrencyKind.Integer;
                currencies.Add(new KeyValuePair<string, CurrencyKind>(r.Str("currency_id"), kind));
            }

            TableRow daily = null, weekly = null;
            foreach (var r in tables[Schemas.ResetRules].Rows)
            {
                var period = r.Str("period");
                if (period == "daily" && daily == null) daily = r;
                if (period == "weekly" && weekly == null) weekly = r;
            }
            if (daily == null || weekly == null) throw new ArgumentException("reset_rules에 daily·weekly 규칙이 있어야 함 (검사를 통과한 표만 넘길 것)", nameof(tables));
            var offline = tables[Schemas.OfflineReward].Rows[0];
            var config = new ClockConfig(
                (int)daily.Long("utc_offset_min"),
                (int)daily.Long("reset_hour"),
                (int)weekly.Long("reset_hour"),
                ClockConfig.WeekdayIndex(weekly.Str("weekday")),
                (int)daily.Long("sync_interval_min"),
                (int)daily.Long("anchor_valid_min"),
                (int)daily.Long("max_rtt_sec"),
                (int)offline.Long("max_frame_sec"));
            return (new Wallet(currencies), new Clock(config));
        }
    }
}
