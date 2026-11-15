namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 표 검사에서 걸린 문제의 종류. Check1~Check10은 설계서 v0.7 2절 '검사' 목록의 번호와 같다.
    /// Format은 그 열 개 밖의 형식 문제(파일 없음, 머리줄, 값 모양, 행 수 등)다.
    /// </summary>
    public enum TableIssueKind
    {
        Format = 0,
        /// <summary>1. id가 같은 표 안에서 겹치지 않는다.</summary>
        Check1 = 1,
        /// <summary>2. 다른 표를 가리키는 값은 실제로 있다.</summary>
        Check2 = 2,
        /// <summary>3. 수량·횟수·시간은 양의 정수다.</summary>
        Check3 = 3,
        /// <summary>4. 출석은 1일부터 마지막 날까지 빠짐없이 한 번씩 있다.</summary>
        Check4 = 4,
        /// <summary>5. 요일 던전을 켤 때는 7요일이 모두 있다.</summary>
        Check5 = 5,
        /// <summary>6. 시간대 이벤트는 시작 &lt; 끝, 같은 요일에 겹치지 않음, 하루 경계(05:00)를 넘지 않음.</summary>
        Check6 = 6,
        /// <summary>7. m1 행은 m1에 없는 것을 가리키지 않는다.</summary>
        Check7 = 7,
        /// <summary>8. 별 속성은 다섯 가지 중 하나이고, m1에는 속성마다 3종씩 15종이 있다.</summary>
        Check8 = 8,
        /// <summary>9. 판마다 칸 1~5가 모두 이어져 있고, 같은 선의 중복·자기 연결이 없다.</summary>
        Check9 = 9,
        /// <summary>10. reset_rules의 utc_offset_min과 시계 설정 열은 모든 행에서 같은 값이다.</summary>
        Check10 = 10,
    }

    /// <summary>검사에서 걸린 문제 하나. Line은 CSV의 줄 번호(1 = 머리줄), 0이면 표 전체에 대한 문제.</summary>
    public sealed class TableIssue
    {
        public TableIssueKind Kind { get; }
        public string Table { get; }
        public int Line { get; }
        public string Column { get; }
        public string Message { get; }

        public TableIssue(TableIssueKind kind, string table, int line, string column, string message)
        {
            Kind = kind;
            Table = table;
            Line = line;
            Column = column;
            Message = message;
        }

        public static string KindLabel(TableIssueKind kind)
        {
            return kind == TableIssueKind.Format ? "형식" : "검사" + (int)kind;
        }

        public override string ToString()
        {
            var where = Table + ".csv";
            if (Line > 0) where += " " + Line + "줄";
            if (!string.IsNullOrEmpty(Column)) where += " " + Column;
            return "[" + KindLabel(Kind) + "] " + where + ": " + Message;
        }
    }
}
