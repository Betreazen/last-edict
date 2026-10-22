using System.Globalization;
using System.Text;
using NDbfReader;
namespace LastEdict.Data.Dbf;

public sealed record DbfColumn(string Name, char Type, int Length, int Decimals);

public sealed class DbfRow
{
    private readonly Dictionary<string, object?> _v;
    internal DbfRow(Dictionary<string, object?> v) => _v = v;
    public object? this[string col] => _v[col];
    /// <summary>Есть ли колонка в таблице — у модовых таблиц бывают лишние (<c>GmodifL.IMMUNITYC</c>, <c>IMMUNECATC</c>).</summary>
    public bool Has(string col) => _v.ContainsKey(col);
    public string GetString(string col) => (_v[col]?.ToString() ?? "").TrimEnd();
    public int GetInt(string col) => _v[col] switch { null => 0, decimal d => (int)d, int i => i, var o => int.Parse(Convert.ToString(o, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture) };
    public bool GetBool(string col) => _v[col] is bool b && b;
}

public sealed class DbfTable
{
    // ponytail: не разносить на static ctor + field initializer — static field initializers
    // выполняются ДО тела явного статического конструктора независимо от порядка в исходнике,
    // поэтому GetEncoding(866) падал раньше RegisterProvider. Всё — в одном теле.
    public static readonly Encoding GameEncoding;
    static DbfTable()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GameEncoding = Encoding.GetEncoding(866);
    }

    public string Name { get; }
    public IReadOnlyList<DbfColumn> Columns { get; }
    public IReadOnlyList<DbfRow> Rows { get; }

    private DbfTable(string name, List<DbfColumn> cols, List<DbfRow> rows) { Name = name; Columns = cols; Rows = rows; }

    /// <summary>Таблица из готовых строк — так её собирает <see cref="GameTables"/> из JSON порта.</summary>
    internal static DbfTable FromRows(string name, List<DbfColumn> columns, List<DbfRow> rows) =>
        new(name, columns, rows);

    public static DbfTable Read(string path)
    {
        using var table = Table.Open(path);
        var cols = table.Columns.Select(c => new DbfColumn(c.Name, NativeTypeChar(c.Type), c.Size, c.DecimalPrecision)).ToList();
        var rows = new List<DbfRow>();
        var reader = table.OpenReader(GameEncoding);
        while (reader.Read())
        {
            var v = new Dictionary<string, object?>(cols.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var c in table.Columns) v[c.Name] = reader.GetValue(c);
            rows.Add(new DbfRow(v));
        }
        return new DbfTable(Path.GetFileNameWithoutExtension(path), cols, rows);
    }

    // ponytail: NDbfReader.IColumn не отдаёт исходный dBASE-код типа (C/N/L/D/...) —
    // только RawColumn.NativeType, для этого нужен свой HeaderLoader. Тесты не проверяют
    // символ Type, поэтому здесь approx-маппинг из CLR Type; если понадобится точный код —
    // подставить HeaderLoader с CreateColumn(ColumnProperties) → RawColumn.
    private static char NativeTypeChar(Type clrType) => clrType switch
    {
        _ when clrType == typeof(string) => 'C',
        _ when clrType == typeof(bool) => 'L',
        _ when clrType == typeof(int) => 'N',
        _ when clrType == typeof(decimal) => 'N',
        _ when clrType == typeof(DateTime) => 'D',
        _ => '?',
    };
}
