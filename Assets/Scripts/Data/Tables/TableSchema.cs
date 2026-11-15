using System;
using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    public enum ColumnKind
    {
        /// <summary>영문 소문자로 시작, 소문자·숫자·밑줄. 예: kill_monsters</summary>
        Id,
        /// <summary>비어 있지 않은 글. 앞뒤 공백 없음. 예: 별가루</summary>
        Text,
        /// <summary>1 이상의 정수 (검사 3)</summary>
        PositiveInt,
        /// <summary>0 이상의 정수</summary>
        NonNegativeInt,
        /// <summary>Min~Max 사이 정수</summary>
        IntRange,
        /// <summary>정해진 낱말 중 하나</summary>
        Enum,
        /// <summary>mon tue wed thu fri sat sun 중 하나</summary>
        Weekday,
        /// <summary>요일 여러 개를 | 로 이음. 예: mon|wed. 겹침 없음</summary>
        WeekdayList,
        /// <summary>HH:MM, 00:00~23:59</summary>
        TimeOfDay,
        /// <summary>HH:MM, 00:01~24:00 (끝 시각용)</summary>
        TimeOfDayEnd,
        /// <summary>m1 또는 v1</summary>
        Build,
    }

    public sealed class ColumnSpec
    {
        public string Name { get; }
        public ColumnKind Kind { get; }
        /// <summary>빈 칸 허용. 빈 칸의 값은 null.</summary>
        public bool Optional { get; }
        public IReadOnlyList<string> EnumValues { get; }
        public long Min { get; }
        public long Max { get; }
        /// <summary>다른 표의 키를 가리키면 그 표 이름 (검사 2·7). 없으면 null.</summary>
        public string RefTable { get; }
        /// <summary>값이 틀렸을 때 붙일 검사 번호. 기본은 열 종류로 정한다.</summary>
        public TableIssueKind FormatIssueKind { get; }

        public ColumnSpec(string name, ColumnKind kind, bool optional = false, string[] enumValues = null,
            long min = 0, long max = 0, string refTable = null, TableIssueKind? issueKind = null)
        {
            Name = name;
            Kind = kind;
            Optional = optional;
            EnumValues = enumValues ?? new string[0];
            Min = min;
            Max = max;
            RefTable = refTable;
            FormatIssueKind = issueKind ?? (kind == ColumnKind.PositiveInt ? TableIssueKind.Check3 : TableIssueKind.Format);
        }
    }

    public sealed class TableSchema
    {
        public string Name { get; }
        public string FileName => Name + ".csv";
        public string JsonFileName => Name + ".json";
        public IReadOnlyList<ColumnSpec> Columns { get; }
        /// <summary>검사 1에서 겹치면 안 되는 열. 없으면 null.</summary>
        public string KeyColumn { get; }
        /// <summary>build 열이 없는 표의 빌드(m1/v1). build 열이 있으면 null이고 행마다 정한다.</summary>
        public string TableBuild { get; }
        /// <summary>행 수가 정확히 1이어야 하는 설정 표.</summary>
        public bool SingleRow { get; }

        public bool HasBuildColumn => Columns.Any(c => c.Kind == ColumnKind.Build);

        public TableSchema(string name, string keyColumn, string tableBuild, bool singleRow, params ColumnSpec[] columns)
        {
            Name = name;
            KeyColumn = keyColumn;
            TableBuild = tableBuild;
            SingleRow = singleRow;
            Columns = columns;

            if (HasBuildColumn == (tableBuild != null))
            {
                throw new ArgumentException(name + ": build 열과 표 빌드 중 하나만 정해야 함");
            }
            if (keyColumn != null && columns.All(c => c.Name != keyColumn))
            {
                throw new ArgumentException(name + ": 키 열 " + keyColumn + "이 없음");
            }
        }

        public int IndexOf(string column)
        {
            for (int i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Name == column) return i;
            }
            throw new ArgumentException(Name + "에 열 " + column + "이 없음");
        }
    }
}
