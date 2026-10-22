using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2.Data.Dbf;

/// <summary>Таблица игры в данных порта на запись: имя, колонки и строки как есть. Значение — строка, число или
/// логическое; <c>null</c> — пустая ячейка. Читается без этой записи — <see cref="GameTables.FromJson(string)"/>.</summary>
public sealed record TableOutJson(
    [property: JsonPropertyName("table")] string Table,
    [property: JsonPropertyName("columns")] IReadOnlyList<TableColumnJson> Columns,
    [property: JsonPropertyName("rows")] IReadOnlyList<Dictionary<string, object?>> Rows);

public sealed record TableColumnJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("length")] int Length,
    [property: JsonPropertyName("decimals")] int Decimals);

/// <summary>
/// Откуда правила берут таблицы игры. Порт в рантайме читает свои <c>data/rules/&lt;Имя&gt;.json</c>
/// (принцип проекта: старые форматы — только на входе), а импорт и харнессы — исходные <c>.dbf</c> из копии игры.
/// Каталог решает сам: есть <c>&lt;имя&gt;.json</c> — берётся он, иначе <c>&lt;имя&gt;.dbf</c>.
/// </summary>
public static class GameTables
{
    /// <summary>Таблица <paramref name="name"/> (<c>Gunits.dbf</c>) из каталога: JSON порта или <c>.dbf</c> игры.</summary>
    /// <remarks>Разобранная таблица кешируется на процесс (как <c>SgFile.Open</c>): одни и те же таблицы читают
    /// несколько загрузчиков (<c>Gunits</c> — восемь мест, <c>GVars</c> — восемь, <c>Grace</c> — шесть), и все получают
    /// один экземпляр. Это безопасно: у <see cref="DbfTable"/> и <see cref="DbfRow"/> нет мутаторов, строки и колонки
    /// отдаются только на чтение. Ключ — полный путь найденного файла, время записи и размер: файл, переписанный на
    /// диске (импорт, редактор), разбирается заново.</remarks>
    /// <exception cref="FileNotFoundException">В каталоге нет ни JSON, ни <c>.dbf</c>.</exception>
    public static DbfTable Read(string dir, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);
        ArgumentException.ThrowIfNullOrEmpty(name);
        string stem = Path.GetFileNameWithoutExtension(name);
        if (Find(dir, stem + ".json") is { } json) return Cached(json, static p => FromJson(File.ReadAllBytes(p)));
        if (Find(dir, name) is { } dbf) return Cached(dbf, DbfTable.Read);
        throw new FileNotFoundException($"{name} не найден в {dir} (ни .json порта, ни .dbf игры)");
    }

    private static DbfTable Cached(string path, Func<string, DbfTable> load)
    {
        string full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        string key = $"{full}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        var lazy = Cache.GetOrAdd(key, _ => new Lazy<DbfTable>(() => load(full)));
        try { return lazy.Value; }
        catch
        {
            // Ошибку чтения (файл занят, дописывается) не запоминать: следующий вызов прочтёт заново.
            Cache.TryRemove(new KeyValuePair<string, Lazy<DbfTable>>(key, lazy));
            throw;
        }
    }

    private static readonly ConcurrentDictionary<string, Lazy<DbfTable>> Cache =
        new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    /// <summary>Забыть разобранные таблицы — для замера холодной загрузки (D2.Bench <c>load_rules_book_cold</c>).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>Есть ли таблица в каталоге (в любом из двух видов).</summary>
    public static bool Has(string dir, string name) =>
        Directory.Exists(dir) &&
        (Find(dir, Path.GetFileNameWithoutExtension(name) + ".json") is not null || Find(dir, name) is not null);

    /// <summary>Таблица в JSON порта.</summary>
    public static string ToJson(DbfTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = table.Rows.Select(r => table.Columns.ToDictionary(c => c.Name, c => Cell(r[c.Name]))).ToList();
        var doc = new TableOutJson(Path.GetFileNameWithoutExtension(table.Name),
            [.. table.Columns.Select(c => new TableColumnJson(c.Name, c.Type.ToString(), c.Length, c.Decimals))],
            rows);
        return JsonSerializer.Serialize(doc, Json).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Таблица из JSON порта (текст).</summary>
    public static DbfTable FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return FromJson(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Таблица из JSON порта (байты UTF-8, метка порядка байтов допускается). Один проход
    /// <see cref="Utf8JsonReader"/> прямо в строки таблицы: прежний разбор через словари <see cref="JsonElement"/>
    /// заводил документ JSON с копией на каждую клетку, и загрузка правил (<c>GameBook.Load</c>) большую часть времени
    /// ждала сборщик мусора. Значения те же: пусто — <c>null</c>, логическое — <c>bool</c>, число — <c>decimal</c>
    /// (целое — через <c>long</c>, иначе через <c>double</c>), строка — <c>string</c>; порядок строк и колонок — как в
    /// файле; ключи клеток различают регистр, повтор ключа в строке — последнее значение.</summary>
    internal static DbfTable FromJson(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith(Utf8Bom)) utf8 = utf8[Utf8Bom.Length..];
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType == JsonTokenType.Null) throw new FormatException("пустая таблица");
        Expect(ref reader, JsonTokenType.StartObject);
        string? table = null;
        List<DbfColumn>? columns = null;
        List<DbfRow>? rows = null;
        while (Next(ref reader) == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("table"u8)) { reader.Read(); table = reader.GetString(); }
            else if (reader.ValueTextEquals("columns"u8)) { reader.Read(); columns = ReadColumns(ref reader); }
            else if (reader.ValueTextEquals("rows"u8)) { reader.Read(); rows = ReadRows(ref reader, columns); }
            else { reader.Read(); reader.Skip(); }
        }
        Expect(ref reader, JsonTokenType.EndObject);
        if (table is null || columns is null || rows is null) throw new FormatException("в таблице нет table, columns или rows");
        return DbfTable.FromRows(table, columns, rows);
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static List<DbfColumn> ReadColumns(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray);
        var columns = new List<DbfColumn>();
        while (Next(ref reader) == JsonTokenType.StartObject)
        {
            string name = "", type = "";
            int length = 0, decimals = 0;
            while (Next(ref reader) == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("name"u8)) { reader.Read(); name = reader.GetString()!; }
                else if (reader.ValueTextEquals("type"u8)) { reader.Read(); type = reader.GetString()!; }
                else if (reader.ValueTextEquals("length"u8)) { reader.Read(); length = reader.GetInt32(); }
                else if (reader.ValueTextEquals("decimals"u8)) { reader.Read(); decimals = reader.GetInt32(); }
                else { reader.Read(); reader.Skip(); }
            }
            columns.Add(new DbfColumn(name, type.Length > 0 ? type[0] : 'C', length, decimals));
        }
        Expect(ref reader, JsonTokenType.EndArray);
        return columns;
    }

    /// <summary>Строки таблицы. Ключ клетки обычно совпадает с колонкой на том же месте — тогда берётся её имя
    /// (одна строка на колонку на всю таблицу), иначе ключ читается как есть.</summary>
    private static List<DbfRow> ReadRows(ref Utf8JsonReader reader, List<DbfColumn>? columns)
    {
        Expect(ref reader, JsonTokenType.StartArray);
        byte[][] names = columns is null ? [] : [.. columns.Select(c => Encoding.UTF8.GetBytes(c.Name))];
        var rows = new List<DbfRow>();
        while (Next(ref reader) == JsonTokenType.StartObject)
        {
            var cells = new Dictionary<string, object?>(names.Length);
            for (int i = 0; Next(ref reader) == JsonTokenType.PropertyName; i++)
            {
                string key = i < names.Length && reader.ValueTextEquals(names[i]) ? columns![i].Name : reader.GetString()!;
                reader.Read();
                cells[key] = Value(ref reader);
            }
            rows.Add(new DbfRow(cells));
        }
        Expect(ref reader, JsonTokenType.EndArray);
        return rows;
    }

    private static object? Value(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Number => reader.TryGetInt64(out long l) ? (decimal)l : (decimal)reader.GetDouble(),
        JsonTokenType.String => reader.GetString(),
        var t => throw new FormatException($"клетка таблицы — {t}, а не строка, число или логическое"),
    };

    private static JsonTokenType Next(ref Utf8JsonReader reader) =>
        reader.Read() ? reader.TokenType : throw new FormatException("таблица JSON оборвана");

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType token)
    {
        if (reader.TokenType != token) throw new FormatException($"таблица JSON: ожидался {token}, а не {reader.TokenType}");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static object? Cell(object? value) => value switch
    {
        null => null,
        decimal d => d == Math.Floor(d) ? (long)d : d,
        _ => value,
    };

    /// <summary>Файл <paramref name="name"/> в каталоге без учёта регистра, путь — с именем как на диске (из него
    /// <see cref="DbfTable.Read"/> берёт имя таблицы). Имя отдаётся перечислению шаблоном: отбор идёт до сборки путей,
    /// строки заводятся только на совпавший файл; сравнение после — то же, что было.</summary>
    private static string? Find(string dir, string name) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, name, ByName).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
            : null;

    // Скрытые и системные файлы не пропускаются, ошибки доступа не глушатся — как у перечисления без параметров.
    private static readonly EnumerationOptions ByName = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple, AttributesToSkip = 0, IgnoreInaccessible = false,
    };
}
