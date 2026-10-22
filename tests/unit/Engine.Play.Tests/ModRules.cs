using System.Text.Json.Nodes;

namespace LastEdict.Data.Tests;

/// <summary>Правила с модовой правкой для тестов: копия <c>data/rules</c> во временный каталог, в таблицы дописываются колонки и
/// строки (как это делает мод), каталог удаляется вместе с объектом.</summary>
internal sealed class ModRules : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "d2-mod-rules-" + Guid.NewGuid().ToString("N"));

    public ModRules()
    {
        string source = ImportedData.PathOf("rules");
        Directory.CreateDirectory(Dir);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(Dir, Path.GetFileName(file)));
    }

    /// <summary>Дописать в таблицу колонку (если её нет) — у модов бывают колонки, которых нет в ванили.</summary>
    public ModRules Column(string table, string name, string type = "N", int length = 10)
    {
        var doc = Read(table);
        var columns = doc["columns"]!.AsArray();
        if (!columns.Any(c => string.Equals((string?)c!["name"], name, StringComparison.OrdinalIgnoreCase)))
            columns.Add(new JsonObject { ["name"] = name, ["type"] = type, ["length"] = length, ["decimals"] = 0 });
        Write(table, doc);
        return this;
    }

    /// <summary>Дописать строку: пропущенные колонки — пусто (<c>null</c>).</summary>
    public ModRules Row(string table, IReadOnlyDictionary<string, object?> values)
    {
        var doc = Read(table);
        var row = new JsonObject();
        foreach (var column in doc["columns"]!.AsArray())
        {
            string name = (string)column!["name"]!;
            row[name] = values.TryGetValue(name, out var v) ? Value(v) : null;
        }
        doc["rows"]!.AsArray().Add(row);
        Write(table, doc);
        return this;
    }

    private static JsonNode? Value(object? v) => v switch
    {
        null => null,
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        string t => JsonValue.Create(t),
        _ => throw new ArgumentException($"значение {v} типа {v.GetType().Name} в таблицу не пишется"),
    };

    private JsonNode Read(string table) => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, table + ".json")))!;

    private void Write(string table, JsonNode doc) => File.WriteAllText(Path.Combine(Dir, table + ".json"), doc.ToJsonString());

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
