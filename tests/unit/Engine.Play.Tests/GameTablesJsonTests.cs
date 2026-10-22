using System.Globalization;
using System.Text;
using System.Text.Json;
using LastEdict.Data.Dbf;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Разбор JSON таблиц порта одним проходом <see cref="Utf8JsonReader"/> даёт те же клетки, что прежний разбор
/// через словари <see cref="JsonElement"/> (эталон ниже — его код как был), — на ручной таблице со всеми видами
/// значений и на всех <c>data/rules/*.json</c>.</summary>
public class GameTablesJsonTests
{
    private static string Rules => ImportedData.PathOf("rules");

    private const string Sample = """
        {"table":"GTest","columns":[{"name":"ID","type":"C","length":10,"decimals":0},{"name":"EMPTY","type":"C","length":5,"decimals":0},
        {"name":"FLAG","type":"L","length":1,"decimals":0},{"name":"OFF","type":"L","length":1,"decimals":0},{"name":"N","type":"N","length":5,"decimals":0},
        {"name":"BIG","type":"N","length":19,"decimals":0},{"name":"F","type":"N","length":6,"decimals":2},{"name":"S","type":"C","length":3,"decimals":0},
        {"name":"T","type":"","length":0,"decimals":0}],
        "rows":[{"ID":"g000uu0001","EMPTY":null,"FLAG":true,"OFF":false,"N":-42,"BIG":9007199254740993,"F":1.25,"S":"","T":"Ж\"A"},
        {"S":"x","ID":"g000uu0002","EMPTY":null,"FLAG":false,"OFF":true,"N":0,"BIG":1e3,"F":0.1,"T":null,"EXTRA":7}]}
        """;

    [Fact]
    public void FromJson_ValuesAsWrittenByHand()
    {
        var t = GameTables.FromJson(Sample);
        Assert.Equal("GTest", t.Name);
        Assert.Equal(["ID", "EMPTY", "FLAG", "OFF", "N", "BIG", "F", "S", "T"], t.Columns.Select(c => c.Name));
        Assert.Equal(new DbfColumn("F", 'N', 6, 2), t.Columns[6]);
        Assert.Equal('C', t.Columns[8].Type);
        var r = t.Rows[0];
        Assert.Equal("g000uu0001", r["ID"]);
        Assert.Null(r["EMPTY"]);
        Assert.Equal(true, r["FLAG"]);
        Assert.Equal(false, r["OFF"]);
        Assert.Equal(-42m, r["N"]);
        Assert.Equal(9007199254740993m, r["BIG"]);
        Assert.Equal(1.25m, r["F"]);
        Assert.Equal("", r["S"]);
        Assert.Equal("Ж\"A", r["T"]);
        Assert.Equal(1000m, t.Rows[1]["BIG"]);
        Assert.Equal(7m, t.Rows[1]["EXTRA"]);
        Assert.Throws<KeyNotFoundException>(() => r["id"]);
    }

    [Fact]
    public void FromJson_SameAsElementParser_OnSample() => AssertSame(Legacy(Sample), GameTables.FromJson(Sample), "образец");

    [SkippableFact]
    public void FromJson_SameAsElementParser_OnRulesData()
    {
        string[] files = Directory.GetFiles(Rules, "*.json");
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            var legacy = Legacy(text);
            AssertSame(legacy, GameTables.FromJson(text), file);
            AssertSame(legacy, GameTables.FromJson(File.ReadAllBytes(file)), file);
        }
    }

    [Fact]
    public void FromJson_AcceptsByteOrderMark()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Sample)];
        AssertSame(GameTables.FromJson(Sample), GameTables.FromJson(bytes), "с BOM");
    }

    [Fact]
    public void FromJson_EmptyOrBroken_IsFormatError()
    {
        Assert.Throws<FormatException>(() => GameTables.FromJson("null"));
        Assert.Throws<FormatException>(() => GameTables.FromJson("""{"table":"X","columns":[],"rows":[{"A":[1]}]}"""));
        Assert.ThrowsAny<JsonException>(() => GameTables.FromJson("""{"table":"X","columns":[],"rows":[{"A":1}"""));
    }

    /// <summary>Клетка в клетку, с типом значения и его записью (масштаб <c>decimal</c> виден в <c>GetString</c>).</summary>
    private static void AssertSame(Expected a, DbfTable b, string where)
    {
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Columns, b.Columns);
        Assert.Equal(a.Rows.Count, b.Rows.Count);
        for (int i = 0; i < a.Rows.Count; i++)
            foreach (var (key, value) in a.Rows[i])
            {
                object? got = b.Rows[i][key];
                Assert.True(Equals(value, got) && value?.GetType() == got?.GetType() && Text(value) == Text(got),
                    $"{where}: строка {i}, {key}: {Text(got)} ({got?.GetType().Name}) вместо {Text(value)} ({value?.GetType().Name})");
            }
    }

    private static void AssertSame(DbfTable a, DbfTable b, string where) =>
        AssertSame(new Expected(a.Name, [.. a.Columns],
            [.. a.Rows.Select(r => a.Columns.ToDictionary(c => c.Name, c => r[c.Name]))]), b, where);

    private static string? Text(object? v) => Convert.ToString(v, CultureInfo.InvariantCulture);

    private sealed record Expected(string Name, List<DbfColumn> Columns, List<Dictionary<string, object?>> Rows);

    /// <summary>Прежний разбор (до однопроходного): документ и словарь элементов на строку, значение — как в старом
    /// <c>GameTables.Value</c>.</summary>
    private static Expected Legacy(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var columns = root.GetProperty("columns").EnumerateArray().Select(c =>
        {
            string type = c.GetProperty("type").GetString()!;
            return new DbfColumn(c.GetProperty("name").GetString()!, type.Length > 0 ? type[0] : 'C',
                c.GetProperty("length").GetInt32(), c.GetProperty("decimals").GetInt32());
        }).ToList();
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Value(p.Value))).ToList();
        return new Expected(root.GetProperty("table").GetString()!, columns, rows);
    }

    private static object? Value(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt64(out long l) ? (decimal)l : (decimal)element.GetDouble(),
        _ => element.GetString(),
    };
}
