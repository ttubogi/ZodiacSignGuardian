using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZodiacSignGuardian.Data.Json
{
    /// <summary>
    /// 표 JSON에 필요한 만큼만 다루는 작은 JSON 쓰기·읽기 (외부 라이브러리 없음, Unity·dotnet 공통).
    /// 값: null, true/false, 정수(long), 문자열, 배열(List&lt;object&gt;), 객체(키 순서를 지키는 목록).
    /// 소수·지수 표기는 표에 없으므로 읽지 않는다(오류).
    /// </summary>
    public static class MiniJson
    {
        /// <summary>키 순서를 지키는 JSON 객체.</summary>
        public sealed class Obj
        {
            public List<KeyValuePair<string, object>> Members { get; } = new List<KeyValuePair<string, object>>();

            public Obj Add(string key, object value)
            {
                Members.Add(new KeyValuePair<string, object>(key, value));
                return this;
            }

            public bool TryGet(string key, out object value)
            {
                foreach (var m in Members)
                {
                    if (m.Key == key) { value = m.Value; return true; }
                }
                value = null;
                return false;
            }
        }

        // ---------------- 쓰기 ----------------

        public static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case string s: WriteString(sb, s); break;
                case Obj o:
                    sb.Append('{');
                    for (int k = 0; k < o.Members.Count; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        WriteString(sb, o.Members[k].Key);
                        sb.Append(": ");
                        WriteValue(sb, o.Members[k].Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    throw new ArgumentException("JSON으로 쓸 수 없는 값: " + value.GetType().Name);
            }
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c); // 한글 등은 그대로 (파일은 UTF-8)
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- 읽기 ----------------

        public sealed class ParseException : Exception
        {
            public int Position { get; }
            public ParseException(string message, int position) : base(message + " (위치 " + position + ")") { Position = position; }
        }

        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue(0);
            p.SkipWs();
            if (!p.AtEnd) throw new ParseException("값 뒤에 남은 글자가 있음", p.Pos);
            return v;
        }

        sealed class Parser
        {
            const int MaxDepth = 32;
            readonly string _s;
            public int Pos;

            public Parser(string s)
            {
                _s = s;
                Pos = s.Length > 0 && s[0] == (char)0xFEFF ? 1 : 0;
            }

            public bool AtEnd => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length && (_s[Pos] == ' ' || _s[Pos] == '\t' || _s[Pos] == '\n' || _s[Pos] == '\r')) Pos++;
            }

            char Peek() => Pos < _s.Length ? _s[Pos] : '\0';

            void Expect(char c)
            {
                if (Peek() != c) throw new ParseException("'" + c + "'가 와야 함", Pos);
                Pos++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new ParseException("너무 깊게 중첩됨", Pos);
                if (AtEnd) throw new ParseException("값이 없음", Pos);
                char c = Peek();
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadInteger();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw new ParseException("알 수 없는 값", Pos);
            }

            bool Word(string w)
            {
                if (string.CompareOrdinal(_s, Pos, w, 0, w.Length) == 0)
                {
                    Pos += w.Length;
                    return true;
                }
                return false;
            }

            Obj ReadObject(int depth)
            {
                Expect('{');
                var o = new Obj();
                var keys = new HashSet<string>();
                SkipWs();
                if (Peek() == '}') { Pos++; return o; }
                while (true)
                {
                    SkipWs();
                    int keyPos = Pos;
                    if (Peek() != '"') throw new ParseException("객체 키는 문자열이어야 함", Pos);
                    var key = ReadString();
                    if (!keys.Add(key)) throw new ParseException("키 '" + key + "'가 두 번 있음", keyPos);
                    SkipWs();
                    Expect(':');
                    SkipWs();
                    o.Add(key, ReadValue(depth + 1));
                    SkipWs();
                    if (Peek() == ',') { Pos++; continue; }
                    Expect('}');
                    return o;
                }
            }

            List<object> ReadArray(int depth)
            {
                Expect('[');
                var list = new List<object>();
                SkipWs();
                if (Peek() == ']') { Pos++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue(depth + 1));
                    SkipWs();
                    if (Peek() == ',') { Pos++; continue; }
                    Expect(']');
                    return list;
                }
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new ParseException("닫히지 않은 문자열", Pos);
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw new ParseException("문자열 안에 제어 문자", Pos - 1);
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw new ParseException("닫히지 않은 문자열", Pos);
                    char e = _s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > _s.Length || !int.TryParse(_s.Substring(Pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            {
                                throw new ParseException("잘못된 \\u 이스케이프", Pos);
                            }
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default:
                            throw new ParseException("잘못된 이스케이프 \\" + e, Pos - 1);
                    }
                }
            }

            long ReadInteger()
            {
                int start = Pos;
                if (Peek() == '-') Pos++;
                if (!(Peek() >= '0' && Peek() <= '9')) throw new ParseException("숫자가 와야 함", Pos);
                if (Peek() == '0' && Pos + 1 < _s.Length && _s[Pos + 1] >= '0' && _s[Pos + 1] <= '9')
                {
                    throw new ParseException("0으로 시작하는 숫자", Pos);
                }
                while (Peek() >= '0' && Peek() <= '9') Pos++;
                if (Peek() == '.' || Peek() == 'e' || Peek() == 'E') throw new ParseException("표 JSON에는 소수·지수가 없음", Pos);
                if (!long.TryParse(_s.Substring(start, Pos - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                {
                    throw new ParseException("long 범위 밖의 수", start);
                }
                return n;
            }
        }
    }
}
