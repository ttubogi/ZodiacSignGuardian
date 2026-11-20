using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ZodiacSignGuardian.DataGen
{
    /// <summary>
    /// stage_params.csv(한 행) → stages.csv 글자. 밸런스 기초 문서 4절의 공식:
    /// <list type="bullet">
    /// <item>전체 스테이지 번호 g(1부터), 장 c = (g − 1) / stages_per_chapter + 1</item>
    /// <item>monster_hp = hp_base × (hp_ratio_num / hp_ratio_den)^(g − 1)</item>
    /// <item>dust_per_kill = dust_base × (dust_ratio_num / dust_ratio_den)^(g − 1)</item>
    /// <item>offline_dust_per_min = offline_kills_per_min × dust_per_kill</item>
    /// <item>boss_hp = boss_hp_mul × 그 장 마지막 스테이지의 monster_hp (장 마지막 행에만)</item>
    /// </list>
    /// 비율은 소수 글자가 아니라 정수 분수로 받는다(AGENTS.md 배운 점 2026-11-16). 값은 정수 분수(BigInteger)로 정확히 계산한 뒤
    /// 유효 숫자 15자리로 한 번만 반올림(절반은 올림)해 "가수e지수"로 쓴다. 같은 입력이면 어느 기기에서도 같은 글자다.
    /// 이 도구는 Core·Data를 참조하지 않는다(설계서 4절). 검사(검사 11 등)는 TableBuild가 한다.
    /// </summary>
    public static class StageGen
    {
        public const string Header =
            "stage_id,chapter,stage_in_chapter,monster_hp,dust_per_kill,offline_dust_per_min,kills_to_clear,clear_time_sec," +
            "first_clear_currency,first_clear_amount,boss_hp,boss_time_sec,boss_reward_currency,boss_reward_amount,build";

        public static readonly string[] ParamColumns =
        {
            "chapters", "stages_per_chapter", "hp_base", "hp_ratio_num", "hp_ratio_den", "dust_base", "dust_ratio_num", "dust_ratio_den",
            "offline_kills_per_min", "kills_to_clear", "clear_time_sec", "first_clear_currency", "first_clear_amount",
            "boss_hp_mul", "boss_time_sec", "boss_reward_currency", "boss_reward_amount",
        };

        /// <summary>정수 입력 칸의 범위. 표가 터무니없이 커지거나 거듭제곱이 매우 커지지 않게 막는다.</summary>
        static readonly Dictionary<string, (long min, long max)> Ranges = new Dictionary<string, (long, long)>
        {
            ["chapters"] = (1, 100),
            ["stages_per_chapter"] = (1, 1000),
            ["hp_base"] = (1, 1000000000),
            ["hp_ratio_num"] = (1, 1000000),
            ["hp_ratio_den"] = (1, 1000000),
            ["dust_base"] = (1, 1000000000),
            ["dust_ratio_num"] = (1, 1000000),
            ["dust_ratio_den"] = (1, 1000000),
            ["offline_kills_per_min"] = (1, 10000),
            ["kills_to_clear"] = (1, 1000),
            ["clear_time_sec"] = (1, 3600),
            ["first_clear_amount"] = (1, 1000000000),
            ["boss_hp_mul"] = (1, 1000000),
            ["boss_time_sec"] = (1, 3600),
            ["boss_reward_amount"] = (1, 1000000000),
        };

        public const int SignificantDigits = 15;

        /// <summary>입력 글자를 읽어 stages.csv 글자를 만든다. 입력이 틀리면 false와 이유.</summary>
        public static bool TryGenerate(string paramsCsv, out string output, out string error)
        {
            output = null!;
            if (!TryReadParams(paramsCsv, out var p, out error)) return false;

            long chapters = p.Int["chapters"], perChapter = p.Int["stages_per_chapter"];
            var total = chapters * perChapter; // 100 × 1000 이하
            var hpNum = new BigInteger(p.Int["hp_ratio_num"]);
            var hpDen = new BigInteger(p.Int["hp_ratio_den"]);
            var dustNum = new BigInteger(p.Int["dust_ratio_num"]);
            var dustDen = new BigInteger(p.Int["dust_ratio_den"]);

            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            // 거듭제곱은 앞 스테이지 값에 곱해 이어 간다(분자·분모를 따로 쌓아 정확함 유지, 일의 양은 스테이지 수에 비례)
            var hpN = new BigInteger(p.Int["hp_base"]);
            var hpD = BigInteger.One;
            var dustN = new BigInteger(p.Int["dust_base"]);
            var dustD = BigInteger.One;
            for (long g = 1; g <= total; g++)
            {
                if (g > 1)
                {
                    hpN *= hpNum; hpD *= hpDen;
                    dustN *= dustNum; dustD *= dustDen;
                }
                var chapter = (g - 1) / perChapter + 1;
                var inChapter = (g - 1) % perChapter + 1;
                var last = inChapter == perChapter;
                var cells = new List<string>
                {
                    Num(g), Num(chapter), Num(inChapter),
                    Format(hpN, hpD), Format(dustN, dustD), Format(dustN * p.Int["offline_kills_per_min"], dustD),
                    Num(p.Int["kills_to_clear"]), Num(p.Int["clear_time_sec"]),
                    p.Text["first_clear_currency"], Num(p.Int["first_clear_amount"]),
                    last ? Format(hpN * p.Int["boss_hp_mul"], hpD) : "",
                    last ? Num(p.Int["boss_time_sec"]) : "",
                    last ? p.Text["boss_reward_currency"] : "",
                    last ? Num(p.Int["boss_reward_amount"]) : "",
                    "m1",
                };
                sb.Append(string.Join(",", cells)).Append('\n');
            }
            output = sb.ToString();
            return true;
        }

        static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 양의 분수 n / d를 유효 숫자 15자리로 한 번 반올림(절반은 올림)해 "d.ddde지수"로 쓴다. 소수부 끝의 0은 지운다.
        /// 1 미만이면 지수가 음수다(표 검사가 1 이상만 받으므로 그런 입력은 TableBuild에서 걸린다).
        /// </summary>
        public static string Format(BigInteger n, BigInteger d)
        {
            if (n.Sign <= 0 || d.Sign <= 0) throw new ArgumentException("양수만");
            // 지수 E: 10^E <= n/d < 10^(E+1). 자릿수 차이로 어림한 뒤 고친다.
            var e = (long)n.ToString(CultureInfo.InvariantCulture).Length - d.ToString(CultureInfo.InvariantCulture).Length;
            while (Compare(n, d, e) < 0) e--;
            while (Compare(n, d, e + 1) >= 0) e++;
            // q = round(n / d × 10^(14 − E))
            var shift = SignificantDigits - 1 - e;
            BigInteger num = n, den = d;
            if (shift >= 0) num *= BigInteger.Pow(10, (int)shift);
            else den *= BigInteger.Pow(10, (int)-shift);
            var q = BigInteger.DivRem(num, den, out var rem);
            if (rem * 2 >= den) q += 1;
            if (q == BigInteger.Pow(10, SignificantDigits)) { q /= 10; e++; } // 9.999…를 올려 10이 된 경우
            var digits = q.ToString(CultureInfo.InvariantCulture);
            var frac = digits.Substring(1).TrimEnd('0');
            return digits[0] + (frac.Length > 0 ? "." + frac : "") + "e" + e.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>n/d와 10^e 비교.</summary>
        static int Compare(BigInteger n, BigInteger d, long e)
        {
            return e >= 0 ? n.CompareTo(d * BigInteger.Pow(10, (int)e)) : (n * BigInteger.Pow(10, (int)-e)).CompareTo(d);
        }

        sealed class Params
        {
            public readonly Dictionary<string, long> Int = new Dictionary<string, long>();
            public readonly Dictionary<string, string> Text = new Dictionary<string, string>();
        }

        static bool TryReadParams(string text, out Params p, out string error)
        {
            p = new Params();
            error = null!;
            var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
            if (lines.Count != 2) { error = "머리줄과 값 한 줄이 있어야 함 (지금 " + lines.Count + "줄)"; return false; }
            var header = lines[0].Split(',');
            var values = lines[1].Split(',');
            if (!header.SequenceEqual(ParamColumns)) { error = "머리줄이 다름. 기대: " + string.Join(",", ParamColumns); return false; }
            if (values.Length != header.Length) { error = "칸 수가 " + values.Length + "개 (머리줄은 " + header.Length + "개)"; return false; }
            for (var i = 0; i < header.Length; i++)
            {
                var col = header[i];
                var raw = values[i];
                if (Ranges.TryGetValue(col, out var range))
                {
                    // 숫자만 (부호·소수점·공백 없음)
                    if (raw.Length == 0 || raw.Length > 18 || !raw.All(c => c >= '0' && c <= '9'))
                    {
                        error = col + ": '" + raw + "'는 정수가 아님 (소수는 _num/_den 분수로)";
                        return false;
                    }
                    var v = long.Parse(raw, NumberStyles.None, CultureInfo.InvariantCulture);
                    if (v < range.min || v > range.max) { error = col + ": " + v + "는 " + range.min + "~" + range.max + " 밖"; return false; }
                    p.Int[col] = v;
                }
                else
                {
                    if (raw.Length == 0 || raw[0] < 'a' || raw[0] > 'z' || !raw.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                    {
                        error = col + ": '" + raw + "'는 id가 아님";
                        return false;
                    }
                    p.Text[col] = raw;
                }
            }
            return true;
        }
    }
}
