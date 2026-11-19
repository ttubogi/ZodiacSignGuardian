namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 표 칸의 큰 수 글자 "가수e지수" (설계서 v0.12 2절). Core의 GameNumber 저장 형식과 같은 글자이며, 표에서는 더 좁게 받는다:
    /// <list type="bullet">
    /// <item>1 이상만: 부호 없음, 지수 0 이상 MaxExponent 이하.</item>
    /// <item>같은 값은 한 가지로만 쓴다: 첫 숫자 1~9, 소수부는 1~14자리이고 끝이 0이 아님, 지수에 앞 0 없음. (GameNumber.ToSaveString과 같은 모양)</item>
    /// </list>
    /// Data는 Core를 참조하지 않으므로 검사 11의 크기 비교를 위해 여기서 정수(지수, 15자리 가수)로 읽는다. 소수(double)로 읽지 않는다
    /// (AGENTS.md 배운 점 2026-11-16).
    /// </summary>
    public static class GameNumberText
    {
        /// <summary>가수의 유효 숫자 수. Core GameNumber와 같다.</summary>
        public const int SignificantDigits = 15;
        /// <summary>표에 쓸 수 있는 가장 큰 지수. 표 값이 Core 계산에서 넘치지 않게 넉넉히 작게 둔다.</summary>
        public const long MaxExponent = 1000000;
        public const string FormatHint = "예: 2e0, 1.313e1, 지수 0~" + "1000000";

        /// <summary>
        /// 성공하면 지수와 15자리 정수 가수(1e14 이상 1e15 미만, 값 = digits / 1e14 × 10^exponent). 실패하면 이유.
        /// </summary>
        public static bool TryParse(string text, out long exponent, out long digits, out string why)
        {
            exponent = 0;
            digits = 0;
            why = null;
            if (string.IsNullOrEmpty(text)) { why = "빈 글자"; return false; }
            var at = text.IndexOf('e');
            if (at <= 0 || at == text.Length - 1) { why = "'e'와 지수가 필요함"; return false; }

            if (text[0] < '1' || text[0] > '9') { why = "첫 글자는 1~9 (부호·0으로 시작하지 않음)"; return false; }
            long n = text[0] - '0';
            var frac = 0;
            var i = 1;
            if (i < at)
            {
                if (text[i] != '.') { why = "가수는 한 자리 정수부와 소수부(d.ddd)"; return false; }
                i++;
                if (i == at) { why = "소수점 뒤 숫자가 없음"; return false; }
                for (; i < at; i++)
                {
                    var c = text[i];
                    if (c < '0' || c > '9') { why = "가수에 숫자가 아닌 글자"; return false; }
                    if (++frac > SignificantDigits - 1) { why = "가수가 " + SignificantDigits + "자리를 넘음"; return false; }
                    n = n * 10 + (c - '0');
                }
                if (text[at - 1] == '0') { why = "소수부 끝의 0은 쓰지 않음"; return false; }
            }
            for (var k = frac; k < SignificantDigits - 1; k++) n *= 10;

            var exp = text.Substring(at + 1);
            if (exp.Length > 1 && exp[0] == '0') { why = "지수에 앞 0을 쓰지 않음"; return false; }
            long e = 0;
            foreach (var c in exp)
            {
                if (c < '0' || c > '9') { why = "지수는 0 이상의 정수"; return false; }
                // 자릿수를 더하기 전에 상한을 넘을지 본다 (넘침 없이)
                if (e > (MaxExponent - (c - '0')) / 10) { why = "지수가 " + MaxExponent + "를 넘음"; return false; }
                e = e * 10 + (c - '0');
            }
            exponent = e;
            digits = n;
            return true;
        }

        /// <summary>두 글자의 크기 비교(둘 다 TryParse가 되는 글자). 음수면 a가 작다.</summary>
        public static int Compare(string a, string b)
        {
            if (!TryParse(a, out var ea, out var da, out _) || !TryParse(b, out var eb, out var db, out _))
            {
                throw new System.ArgumentException("큰 수 글자가 아님: " + a + " / " + b);
            }
            if (ea != eb) return ea < eb ? -1 : 1;
            return da.CompareTo(db);
        }
    }
}
