namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 표 검사에서 걸린 문제의 종류. Check1~Check12는 설계서 2절 '검사' 목록의 번호와 같다.
    /// Format은 그 밖의 형식 문제(파일 없음, 머리줄, 값 모양, 행 수 등)다.
    /// </summary>
    public enum TableIssueKind
    {
        Format = 0,
        /// <summary>1. id가 같은 표 안에서 겹치지 않는다.</summary>
        Check1 = 1,
        /// <summary>2. 다른 표를 가리키는 값은 실제로 있다. 일일 임무는 daily 규칙만 가리킨다.</summary>
        Check2 = 2,
        /// <summary>3. 수량·횟수·시간은 양의 정수다.</summary>
        Check3 = 3,
        /// <summary>4. 출석은 1일부터 마지막 날까지 빠짐없이 한 번씩 있고, 마지막 날은 cycle 규칙의 cycle_days와 같다.</summary>
        Check4 = 4,
        /// <summary>5. 요일 던전을 켤 때는 7요일이 모두 있다.</summary>
        Check5 = 5,
        /// <summary>6. 시간대 이벤트는 시작 &lt; 끝, 같은 요일에 겹치지 않음, 하루 경계(05:00)를 넘지 않음.</summary>
        Check6 = 6,
        /// <summary>7. m1 행은 m1에 없는 것을 가리키지 않는다.</summary>
        Check7 = 7,
        /// <summary>8. 별 속성은 다섯 가지 중 하나이고, m1에는 속성마다 3종씩 15종이 있으며, 속성마다 희귀도(일반·희귀·전설)가 하나씩이다.</summary>
        Check8 = 8,
        /// <summary>9. 판마다 칸 1~5가 모두 이어져 있고, 같은 선의 중복·자기 연결이 없다.</summary>
        Check9 = 9,
        /// <summary>10. reset_rules의 utc_offset_min과 시계 설정 열은 모든 행에서 같은 값이다.</summary>
        Check10 = 10,
        /// <summary>11. 스테이지: 번호·장·장 안 번호가 빠짐없이 이어지고, 보스 칸은 장 마지막 행에만, 체력·보상은 줄지 않고, m1 장마다 m1 판이 있다.</summary>
        Check11 = 11,
        /// <summary>12. 뽑기: 희귀도 확률 합이 10000, 확률이 있는 희귀도는 m1 별이 있음, 뽑기·별조각 재화는 서로 다른 정수 재화.</summary>
        Check12 = 12,
    }

    /// <summary>표를 어디서 읽었는가. 문제 위치를 표시할 때 쓴다.</summary>
    public enum TableSource
    {
        /// <summary>Tables/csv의 CSV. 위치는 파일의 실제 줄 번호(1 = 머리줄).</summary>
        Csv = 0,
        /// <summary>Assets/Resources/Tables의 JSON. 위치는 rows 배열의 데이터 행 번호(1부터).</summary>
        Json = 1,
    }

    /// <summary>
    /// 검사에서 걸린 문제 하나.
    /// Line: CSV면 파일의 줄 번호, JSON이면 데이터 행 번호(1부터). 0이면 표 전체에 대한 문제.
    /// </summary>
    public sealed class TableIssue
    {
        public TableIssueKind Kind { get; }
        public string Table { get; }
        public int Line { get; }
        public string Column { get; }
        public string Message { get; }
        public TableSource Source { get; }

        public TableIssue(TableIssueKind kind, string table, int line, string column, string message, TableSource source = TableSource.Csv)
        {
            Kind = kind;
            Table = table;
            Line = line;
            Column = column;
            Message = message;
            Source = source;
        }

        public TableIssue WithSource(TableSource source)
        {
            return source == Source ? this : new TableIssue(Kind, Table, Line, Column, Message, source);
        }

        /// <summary>메시지 안에서 다른 행을 가리킬 때 쓰는 위치 글. CSV는 "3줄", JSON은 "데이터 2행".</summary>
        public static string At(TableSource source, int line)
        {
            return source == TableSource.Json ? "데이터 " + line + "행" : line + "줄";
        }

        public static string KindLabel(TableIssueKind kind)
        {
            return kind == TableIssueKind.Format ? "형식" : "검사" + (int)kind;
        }

        public override string ToString()
        {
            var where = Source == TableSource.Json ? Table + ".json" : Table + ".csv";
            if (Line > 0) where += Source == TableSource.Json ? " 데이터 " + Line + "행" : " " + Line + "줄";
            if (!string.IsNullOrEmpty(Column)) where += " " + Column;
            return "[" + KindLabel(Kind) + "] " + where + ": " + Message;
        }
    }
}
