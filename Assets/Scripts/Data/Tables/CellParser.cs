using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>칸 글자 하나를 열 종류에 맞는 값으로 바꾼다. 앞뒤 공백·부호(+)·소수점은 받지 않는다.</summary>
    public static class CellParser
    {
        /// <summary>성공하면 true와 값(long/string/string[]/null). 실패하면 false와 이유.</summary>
        public static bool TryParse(ColumnSpec col, string raw, out object value, out string error)
        {
            value = null;
            error = null;

            if (raw.Length == 0)
            {
                if (col.Optional) return true;
                error = "빈 칸 (값이 필요함)";
                return false;
            }

            switch (col.Kind)
            {
                case ColumnKind.Id:
                    if (!IsId(raw)) { error = "id는 영문 소문자로 시작하고 소문자·숫자·밑줄만 쓴다: '" + raw + "'"; return false; }
                    value = raw;
                    return true;

                case ColumnKind.Text:
                    if (raw.Trim().Length != raw.Length) { error = "앞뒤 공백이 있음: '" + raw + "'"; return false; }
                    if (raw.Any(char.IsControl)) { error = "제어 문자(줄바꿈 등)가 있음"; return false; }
                    value = raw;
                    return true;

                case ColumnKind.PositiveInt:
                    return Integer(raw, 1, long.MaxValue, "1 이상의 정수", out value, out error);

                case ColumnKind.NonNegativeInt:
                    return Integer(raw, 0, long.MaxValue, "0 이상의 정수", out value, out error);

                case ColumnKind.IntRange:
                    return Integer(raw, col.Min, col.Max, col.Min + "~" + col.Max + " 사이 정수", out value, out error);

                case ColumnKind.Enum:
                    if (!col.EnumValues.Contains(raw)) { error = "'" + raw + "'는 " + string.Join("/", col.EnumValues) + " 중 하나가 아님"; return false; }
                    value = raw;
                    return true;

                case ColumnKind.Weekday:
                    if (!Schemas.Weekdays.Contains(raw)) { error = "'" + raw + "'는 요일(mon~sun)이 아님"; return false; }
                    value = raw;
                    return true;

                case ColumnKind.WeekdayList:
                {
                    var parts = raw.Split('|');
                    var bad = parts.FirstOrDefault(p => !Schemas.Weekdays.Contains(p));
                    if (bad != null) { error = "'" + bad + "'는 요일(mon~sun)이 아님 (여러 요일은 mon|wed처럼 씀)"; return false; }
                    if (parts.Distinct().Count() != parts.Length) { error = "같은 요일이 두 번 있음: '" + raw + "'"; return false; }
                    value = parts;
                    return true;
                }

                case ColumnKind.TimeOfDay:
                    return Time(raw, false, out value, out error);

                case ColumnKind.TimeOfDayEnd:
                    return Time(raw, true, out value, out error);

                case ColumnKind.Build:
                    if (raw != Schemas.M1 && raw != Schemas.V1) { error = "build는 m1 또는 v1: '" + raw + "'"; return false; }
                    value = raw;
                    return true;
            }

            error = "알 수 없는 열 종류";
            return false;
        }

        public static bool IsId(string s)
        {
            if (s.Length == 0 || s[0] < 'a' || s[0] > 'z') return false;
            return s.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_');
        }

        static bool Integer(string raw, long min, long max, string what, out object value, out string error)
        {
            value = null;
            error = null;
            int start = raw[0] == '-' ? 1 : 0;
            if (start == raw.Length || !raw.Skip(start).All(c => c >= '0' && c <= '9'))
            {
                error = "'" + raw + "'는 " + what + "가 아님";
                return false;
            }
            if (!long.TryParse(raw, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                error = "'" + raw + "'는 너무 큰 수 (long 범위 밖)";
                return false;
            }
            if (n < min || n > max)
            {
                error = "'" + raw + "'는 " + what + "가 아님";
                return false;
            }
            value = n;
            return true;
        }

        /// <summary>HH:MM을 하루 시작(00:00)부터 분으로. 값은 "HH:MM" 글자 그대로 둔다.</summary>
        static bool Time(string raw, bool allow24, out object value, out string error)
        {
            value = null;
            error = null;
            if (TryMinutes(raw, out var m) && (allow24 ? m >= 1 && m <= 1440 : m <= 1439))
            {
                value = raw;
                return true;
            }
            error = "'" + raw + "'는 시각(" + (allow24 ? "00:01~24:00" : "00:00~23:59") + ", HH:MM)이 아님";
            return false;
        }

        public static bool TryMinutes(string hhmm, out int minutes)
        {
            minutes = 0;
            if (hhmm == null || hhmm.Length != 5 || hhmm[2] != ':') return false;
            if (!Digit(hhmm[0]) || !Digit(hhmm[1]) || !Digit(hhmm[3]) || !Digit(hhmm[4])) return false;
            int h = (hhmm[0] - '0') * 10 + (hhmm[1] - '0');
            int mm = (hhmm[3] - '0') * 10 + (hhmm[4] - '0');
            if (mm > 59 || h > 24 || (h == 24 && mm != 0)) return false;
            minutes = h * 60 + mm;
            return true;
        }

        // char.IsDigit은 다른 문자권의 숫자도 받으므로 쓰지 않는다.
        static bool Digit(char c) => c >= '0' && c <= '9';
    }
}
