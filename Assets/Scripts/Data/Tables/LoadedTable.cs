using System;
using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Data.Tables
{
    /// <summary>
    /// 표 한 행. 값은 열 종류에 따라 long(정수), string(나머지), string[](WeekdayList), 빈 칸은 null.
    /// Line은 CSV 줄 번호. JSON에서 읽은 행은 행 순서(머리줄을 1로 치고 2부터)를 넣는다.
    /// 값을 읽지 못한 칸(이미 문제로 남긴 칸)이 있어도 행은 남긴다. 그래야 다른 검사가
    /// "그 행이 없다"는 엉뚱한 연쇄 문제를 내지 않는다. 검사는 IsValid로 그 칸만 건너뛴다.
    /// </summary>
    public sealed class TableRow
    {
        readonly TableSchema _schema;
        readonly object[] _values;
        readonly bool[] _invalid;

        public int Line { get; }

        public TableRow(TableSchema schema, int line, object[] values, bool[] invalid = null)
        {
            if (values.Length != schema.Columns.Count) throw new ArgumentException("열 수가 맞지 않음");
            _schema = schema;
            Line = line;
            _values = values;
            _invalid = invalid ?? new bool[values.Length];
        }

        /// <summary>그 칸을 제대로 읽었는가. (읽지 못한 칸은 이미 문제로 남아 있다)</summary>
        public bool IsValid(string column) => !_invalid[_schema.IndexOf(column)];
        public bool AllValid(params string[] columns) => columns.All(IsValid);
        public bool HasInvalid => _invalid.Any(x => x);

        public object this[string column] => _values[_schema.IndexOf(column)];
        public object this[int index] => _values[index];

        public string Str(string column) => (string)this[column];
        public long Long(string column) => (long)this[column];
        public long? LongOrNull(string column) => this[column] is long l ? l : (long?)null;
        public string[] Strings(string column) => (string[])this[column];
        public bool IsBlank(string column) => this[column] == null;

        /// <summary>빌드 열이 있으면 그 값, 없으면 표의 빌드.</summary>
        /// <summary>build 칸을 읽지 못했으면 null.</summary>
        public string Build => _schema.HasBuildColumn ? (IsValid("build") ? Str("build") : null) : _schema.TableBuild;

        public bool SameValues(TableRow other)
        {
            if (other._schema != _schema) return false;
            for (int i = 0; i < _values.Length; i++)
            {
                if (!ValueEquals(_values[i], other._values[i])) return false;
            }
            return true;
        }

        static bool ValueEquals(object a, object b)
        {
            if (a is string[] sa && b is string[] sb) return sa.SequenceEqual(sb);
            return Equals(a, b);
        }
    }

    public sealed class LoadedTable
    {
        public TableSchema Schema { get; }
        public IReadOnlyList<TableRow> Rows { get; }
        public TableSource Source { get; }
        /// <summary>
        /// 표의 틀(파일·머리줄·JSON 바깥 모양)을 제대로 읽었는가. false면 행을 하나도 믿을 수 없어
        /// 행 수 검사를 건너뛴다 (이미 형식 문제로 남아 있다).
        /// </summary>
        public bool Complete { get; }

        public LoadedTable(TableSchema schema, IReadOnlyList<TableRow> rows, TableSource source = TableSource.Csv, bool complete = true)
        {
            Schema = schema;
            Rows = rows;
            Source = source;
            Complete = complete;
        }

        public static LoadedTable Broken(TableSchema schema, TableSource source)
        {
            return new LoadedTable(schema, new TableRow[0], source, complete: false);
        }

        public bool SameValues(LoadedTable other)
        {
            return other.Schema == Schema
                && other.Rows.Count == Rows.Count
                && Rows.Zip(other.Rows, (a, b) => a.SameValues(b)).All(x => x);
        }
    }

    /// <summary>표 12개 묶음. 읽지 못한 표는 행이 없는 표로 들어간다.</summary>
    public sealed class TableSet
    {
        readonly Dictionary<string, LoadedTable> _tables = new Dictionary<string, LoadedTable>();

        public LoadedTable this[string name] => _tables[name];
        public LoadedTable this[TableSchema schema] => _tables[schema.Name];
        public IEnumerable<LoadedTable> Tables => Schemas.All.Where(s => _tables.ContainsKey(s.Name)).Select(s => _tables[s.Name]);

        public void Put(LoadedTable table) => _tables[table.Schema.Name] = table;
        public bool Has(string name) => _tables.ContainsKey(name);
    }
}
