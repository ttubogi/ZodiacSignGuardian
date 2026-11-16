using System;
using System.Collections.Generic;
using System.Text;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Data.Json;

namespace ZodiacSignGuardian.Core.Session
{
    /// <summary>
    /// 저장 JSON 형식 (saveVersion 1). Core가 형식과 읽기·쓰기를 모두 맡고, Services는 받은 문자열을 그대로 보관한다 (결정 기록 2026-11-16).
    /// <code>
    /// {"saveVersion": 1, "saveRevision": 3, "lastSavedUtcMs": 1763251200000 | null,
    ///  "progress": {"chapter": 1},
    ///  "wallet": {"stardust": "1.5e3", "galaxy_crystal": 120, ...},
    ///  "clock": {"anchorUtcMs": … | null, "syncElapsedUs": …, "processedDay": … | null, "processedWeek": … | null, "battlePendingUs": …},
    ///  "rng": ["16진수 16자리" × 12]}
    /// </code>
    /// <list type="bullet">
    /// <item>숫자는 정수(long)만 쓴다. 소수를 글자로 쓰고 읽지 않는다(AGENTS.md 배운 점 2026-11-16). GameNumber 재화는 "가수e지수" 문자열.</item>
    /// <item>읽을 때는 키 집합이 정확히 맞아야 한다(빠진 키·남는 키는 오류). 키 순서는 따지지 않는다.</item>
    /// <item>시간 상태·실행 상태·요청 번호·광고 번호·µs 미만 남는 값은 저장하지 않는다(설계서 3절).</item>
    /// </list>
    /// </summary>
    internal static class SaveFormat
    {
        internal sealed class Data
        {
            public long SaveVersion;
            public long SaveRevision;
            public long? LastSavedUtcMs;
            public long Chapter;
            public List<KeyValuePair<string, object>> Wallet = new List<KeyValuePair<string, object>>(); // long 또는 string
            public long? AnchorUtcMs;
            public long SyncElapsedUs;
            public long? ProcessedDay;
            public long? ProcessedWeek;
            public long BattlePendingUs;
            public List<string> Rng = new List<string>();
        }

        public static string Write(GameSession s, long revision)
        {
            var wallet = new MiniJson.Obj();
            foreach (var id in s.Wallet.CurrencyIds)
            {
                var b = s.Wallet.Balance(id);
                wallet.Add(id, b.Kind == CurrencyKind.Integer ? (object)b.Integer : b.Number.ToSaveString());
            }
            var clock = new MiniJson.Obj()
                .Add("anchorUtcMs", s.Clock.AnchorUtcMs)
                .Add("syncElapsedUs", s.Clock.SyncElapsedUs)
                .Add("processedDay", s.Clock.ProcessedDay)
                .Add("processedWeek", s.Clock.ProcessedWeek)
                .Add("battlePendingUs", s.Battle.PendingUs);
            var root = new MiniJson.Obj()
                .Add("saveVersion", (long)GameSession.SaveVersion)
                .Add("saveRevision", revision)
                .Add("lastSavedUtcMs", s.EstimatedUtcMs)
                .Add("progress", new MiniJson.Obj().Add("chapter", (long)s.Chapter))
                .Add("wallet", wallet)
                .Add("clock", clock)
                .Add("rng", new List<object>(s.Rng.ToSaveStrings()));
            var sb = new StringBuilder();
            MiniJson.WriteValue(sb, Box(root));
            return sb.ToString();
        }

        // MiniJson은 long?를 모르므로 null 또는 long으로 바꿔 넣는다
        static object Box(object v)
        {
            switch (v)
            {
                case MiniJson.Obj o:
                    var copy = new MiniJson.Obj();
                    foreach (var m in o.Members) copy.Add(m.Key, Box(m.Value));
                    return copy;
                case List<object> list:
                    var l = new List<object>();
                    foreach (var x in list) l.Add(Box(x));
                    return l;
                default:
                    return v;
            }
        }

        static readonly string[] RootKeys = { "saveVersion", "saveRevision", "lastSavedUtcMs", "progress", "wallet", "clock", "rng" };
        static readonly string[] ProgressKeys = { "chapter" };
        static readonly string[] ClockKeys = { "anchorUtcMs", "syncElapsedUs", "processedDay", "processedWeek", "battlePendingUs" };

        /// <summary>구조와 값 종류만 읽는다. 값이 맞는지(음수, 범위, 표와 같은지)는 불러오는 쪽이 각 모듈의 TryRestore로 본다.</summary>
        public static bool TryRead(string json, out Data data, out string error)
        {
            data = null;
            error = null;
            if (json == null) { error = "저장 문자열이 없음"; return false; }
            object parsed;
            try
            {
                parsed = MiniJson.Parse(json);
            }
            catch (MiniJson.ParseException e)
            {
                error = "JSON 형식 오류: " + e.Message;
                return false;
            }
            var d = new Data();
            if (!(parsed is MiniJson.Obj root)) { error = "맨 위가 객체가 아님"; return false; }
            if (!Keys(root, RootKeys, "맨 위", out error)) return false;

            if (!Long(root, "saveVersion", out d.SaveVersion, out error)) return false;
            if (d.SaveVersion != GameSession.SaveVersion) { error = "모르는 saveVersion: " + d.SaveVersion; return false; }
            if (!Long(root, "saveRevision", out d.SaveRevision, out error)) return false;
            if (!NullableLong(root, "lastSavedUtcMs", out d.LastSavedUtcMs, out error)) return false;

            if (!Child(root, "progress", out var progress, out error) || !Keys(progress, ProgressKeys, "progress", out error)) return false;
            if (!Long(progress, "chapter", out d.Chapter, out error)) return false;

            if (!Child(root, "wallet", out var wallet, out error)) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in wallet.Members)
            {
                if (!seen.Add(m.Key)) { error = "wallet 키가 겹침: " + m.Key; return false; }
                if (!(m.Value is long) && !(m.Value is string)) { error = "wallet." + m.Key + "는 정수 또는 문자열이어야 함"; return false; }
                d.Wallet.Add(m);
            }

            if (!Child(root, "clock", out var clock, out error) || !Keys(clock, ClockKeys, "clock", out error)) return false;
            if (!NullableLong(clock, "anchorUtcMs", out d.AnchorUtcMs, out error)) return false;
            if (!Long(clock, "syncElapsedUs", out d.SyncElapsedUs, out error)) return false;
            if (!NullableLong(clock, "processedDay", out d.ProcessedDay, out error)) return false;
            if (!NullableLong(clock, "processedWeek", out d.ProcessedWeek, out error)) return false;
            if (!Long(clock, "battlePendingUs", out d.BattlePendingUs, out error)) return false;

            root.TryGet("rng", out var rng);
            if (!(rng is List<object> rngList)) { error = "rng는 배열이어야 함"; return false; }
            foreach (var x in rngList)
            {
                if (!(x is string str)) { error = "rng 값은 문자열이어야 함"; return false; }
                d.Rng.Add(str);
            }
            data = d;
            return true;
        }

        static bool Keys(MiniJson.Obj o, string[] expected, string where, out string error)
        {
            error = null;
            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in o.Members)
            {
                if (!have.Add(m.Key)) { error = where + " 키가 겹침: " + m.Key; return false; }
            }
            var want = new HashSet<string>(expected, StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (var k in expected) if (!have.Contains(k)) missing.Add(k);
            var extra = new List<string>();
            foreach (var k in have) if (!want.Contains(k)) extra.Add(k);
            extra.Sort(StringComparer.Ordinal);
            if (missing.Count == 0 && extra.Count == 0) return true;
            error = where + ": " + (missing.Count > 0 ? "빠진 키 " + string.Join(", ", missing) : "") + (missing.Count > 0 && extra.Count > 0 ? " / " : "") + (extra.Count > 0 ? "남는 키 " + string.Join(", ", extra) : "");
            return false;
        }

        static bool Child(MiniJson.Obj o, string key, out MiniJson.Obj child, out string error)
        {
            error = null;
            o.TryGet(key, out var v);
            child = v as MiniJson.Obj;
            if (child == null) error = key + "는 객체여야 함";
            return child != null;
        }

        static bool Long(MiniJson.Obj o, string key, out long value, out string error)
        {
            error = null;
            o.TryGet(key, out var v);
            if (v is long l) { value = l; return true; }
            value = 0;
            error = key + "는 정수여야 함";
            return false;
        }

        static bool NullableLong(MiniJson.Obj o, string key, out long? value, out string error)
        {
            error = null;
            o.TryGet(key, out var v);
            if (v == null) { value = null; return true; }
            if (v is long l) { value = l; return true; }
            value = null;
            error = key + "는 정수 또는 null이어야 함";
            return false;
        }

        /// <summary>wallet 값 하나를 재화 종류에 맞게 읽는다.</summary>
        public static bool TryAmount(object raw, CurrencyKind kind, out CurrencyAmount amount)
        {
            amount = default;
            if (kind == CurrencyKind.Integer && raw is long l) { amount = CurrencyAmount.Of(l); return true; }
            if (kind == CurrencyKind.GameNumber && raw is string s && GameNumber.TryParseSave(s, out var g)) { amount = CurrencyAmount.Of(g); return true; }
            return false;
        }
    }
}
