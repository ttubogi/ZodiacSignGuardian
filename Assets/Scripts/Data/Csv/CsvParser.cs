using System.Collections.Generic;
using System.Text;

namespace ZodiacSignGuardian.Data.Csv
{
    /// <summary>CSV 한 행. Line은 그 행이 시작하는 줄 번호(1부터).</summary>
    public sealed class CsvRecord
    {
        public int Line { get; }
        public IReadOnlyList<string> Fields { get; }

        public CsvRecord(int line, IReadOnlyList<string> fields)
        {
            Line = line;
            Fields = fields;
        }
    }

    /// <summary>
    /// RFC 4180 CSV 읽기. 쉼표 구분, 큰따옴표로 감싼 칸 안의 쉼표·줄바꿈·"" 허용.
    /// 줄바꿈은 CRLF·LF·CR 모두 받는다. 맨 앞의 BOM은 버린다. 완전히 빈 줄은 행으로 치지 않는다.
    /// 엄격하게 읽는다: 따옴표 없는 칸 안의 따옴표, 닫는 따옴표 뒤의 다른 글자, 닫히지 않은 따옴표는 오류다.
    /// 칸 앞뒤 공백은 지우지 않는다(값 검사에서 걸러낸다).
    /// </summary>
    public static class CsvParser
    {
        public sealed class Result
        {
            public List<CsvRecord> Records { get; } = new List<CsvRecord>();
            /// <summary>null이면 성공.</summary>
            public string Error { get; internal set; }
            public int ErrorLine { get; internal set; }
        }

        public static Result Parse(string text)
        {
            var result = new Result();
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);

            var fields = new List<string>();
            var field = new StringBuilder();
            int line = 1;
            int recordLine = 1;
            int i = 0;
            bool fieldStarted = false;   // 이 행에 글자나 쉼표가 하나라도 있었는지

            while (i < text.Length)
            {
                char c = text[i];

                if (c == '"' && field.Length == 0)
                {
                    // 따옴표가 칸의 첫 글자면 따옴표 칸
                    int openLine = line;
                    i++;
                    while (true)
                    {
                        if (i >= text.Length)
                        {
                            result.Error = "닫히지 않은 큰따옴표";
                            result.ErrorLine = openLine;
                            return result;
                        }
                        char q = text[i];
                        if (q == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                field.Append('"');
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        if (q == '\r' || q == '\n')
                        {
                            // 칸 안의 줄바꿈은 LF 하나로 맞춘다
                            if (q == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                            field.Append('\n');
                            line++;
                            i++;
                            continue;
                        }
                        field.Append(q);
                        i++;
                    }
                    fieldStarted = true;
                    // 닫는 따옴표 뒤에는 쉼표·줄바꿈·끝만 온다
                    if (i < text.Length && text[i] != ',' && text[i] != '\r' && text[i] != '\n')
                    {
                        result.Error = "닫는 큰따옴표 뒤에 다른 글자가 있음";
                        result.ErrorLine = line;
                        return result;
                    }
                    continue;
                }

                if (c == '"')
                {
                    result.Error = "따옴표로 감싸지 않은 칸 안에 큰따옴표가 있음";
                    result.ErrorLine = line;
                    return result;
                }

                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = true;
                    i++;
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    i++;
                    EndRecord(result, fields, field, fieldStarted, recordLine);
                    fields = new List<string>();
                    fieldStarted = false;
                    line++;
                    recordLine = line;
                    continue;
                }

                field.Append(c);
                fieldStarted = true;
                i++;
            }

            EndRecord(result, fields, field, fieldStarted, recordLine);
            return result;
        }

        static void EndRecord(Result result, List<string> fields, StringBuilder field, bool fieldStarted, int recordLine)
        {
            if (!fieldStarted && fields.Count == 0 && field.Length == 0)
            {
                return; // 빈 줄
            }
            fields.Add(field.ToString());
            field.Clear();
            result.Records.Add(new CsvRecord(recordLine, fields));
        }
    }
}
