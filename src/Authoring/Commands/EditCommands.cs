using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Commands;

/// <summary>
/// Команда правки документа мода (<c>docs/tools/studio-architecture.md</c> §3): получает документ и возвращает новый, исходный не меняется —
/// поэтому отмена в ядре (<see cref="EditHistory"/>) — просто прежний документ. У команды имя (журнал, кнопка «Отменить …») и запись JSON:
/// журнал правок — сценарий, который <c>d2mod apply</c> повторяет без окна. Отказ — исключение с текстом; документ тогда прежний.
/// </summary>
public interface IEditCommand
{
    string Name { get; }

    ModDocument Apply(ModDocument doc);

    JsonObject ToJson();
}

/// <summary>Поле сущности по пути через точку (<c>hitPoints</c>, <c>tree.column</c>) — значение JSON; <see cref="Value"/> null и
/// <see cref="Unset"/> — поле убирается из слоя мода (действует базовое). Сущности ещё нет в моде — заводится правка по полям в файле вида
/// (у чужой сущности) — так правка базы ложится в слой мода, а не в базу.</summary>
public sealed record SetField(string Id, string Path, JsonNode? Value, bool Unset = false) : IEditCommand
{
    public string Name => Unset ? $"убрать {Path} у {Id}" : $"{Path} у {Id}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (Path.Length == 0 || Path == "id") throw new InvalidOperationException("id сущности командой не меняется");
        var entity = doc.Find(Id) ?? new ModEntity(ModDocument.KindOf(Id), Id, new JsonObject { ["id"] = Id }, EntitySource.File);
        var node = (JsonObject)entity.Node.DeepClone();
        string[] parts = Path.Split('.');
        var at = node;
        foreach (string part in parts[..^1])
        {
            if (at[part] is not JsonObject inner)
            {
                if (Unset) return doc;
                at[part] = inner = new JsonObject();
            }
            at = inner;
        }
        if (Unset) at.Remove(parts[^1]);
        else at[parts[^1]] = Value?.DeepClone();
        // Правка чужой сущности, у которой не осталось полей, из слоя уходит целиком.
        if (entity.Source == EntitySource.File && !entity.Id.StartsWith(doc.ModId + ":", StringComparison.Ordinal) && node.Count == 1)
            return doc.Without(Id);
        return doc.With(entity with { Node = node });
    }

    public JsonObject ToJson() => Unset
        ? new JsonObject { ["op"] = "unset", ["id"] = Id, ["path"] = Path }
        : new JsonObject { ["op"] = "set", ["id"] = Id, ["path"] = Path, ["value"] = Value?.DeepClone() };
}

/// <summary>Новая сущность мода <c>&lt;мод&gt;:&lt;вид&gt;/&lt;имя&gt;</c>: каталогом («папка = сущность», по умолчанию) или в файле вида.</summary>
public sealed record AddEntity(string Kind, string EntityName, JsonObject Body, bool Folder = true) : IEditCommand
{
    public string Name => $"новая {Kind} {EntityName}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _ = ContentSet.KindNamed(Kind);
        if (EntityName.Length == 0 || !EntityName.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            throw new InvalidOperationException($"имя «{EntityName}» — только строчные латинские буквы, цифры и «-» (оно же каталог и часть id)");
        string id = EntityFolders.IdOf(doc.ModId, ContentSet.KindNamed(Kind), EntityName);
        if (doc.Find(id) is not null) throw new InvalidOperationException($"«{id}» уже есть в моде");
        var node = new JsonObject { ["id"] = id };
        foreach (var (key, value) in Body)
            if (key != "id") node[key] = value?.DeepClone();
        return doc.With(new ModEntity(Kind, id, node, Folder ? EntitySource.Folder : EntitySource.File));
    }

    public JsonObject ToJson() =>
        new() { ["op"] = "add", ["kind"] = Kind, ["name"] = EntityName, ["folder"] = Folder, ["body"] = Body.DeepClone() };
}

/// <summary>Убрать сущность из слоя мода: своя исчезает, правка чужой — отменяется (действует базовая).</summary>
public sealed record RemoveEntity(string Id) : IEditCommand
{
    public string Name => $"убрать {Id}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return doc.Find(Id) is null ? throw new InvalidOperationException($"«{Id}» нет в моде") : doc.Without(Id);
    }

    public JsonObject ToJson() => new() { ["op"] = "remove", ["id"] = Id };
}

/// <summary>Строка сущности-каталога на языке: короткий ключ (<c>name</c>) → текст; null — убрать. Поле записи получает автоматический
/// ключ строки при чтении (<see cref="EntityFolders"/>).</summary>
public sealed record SetText(string Id, string Language, string Key, string? Text) : IEditCommand
{
    public string Name => $"текст {Key} ({Language}) у {Id}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Find(Id) is not { Source: EntitySource.Folder } entity)
            throw new InvalidOperationException($"тексты — у сущности-каталога мода; «{Id}» не она");
        var map = entity.Strings.GetValueOrDefault(Language) ?? ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        map = Text is null ? map.Remove(Key) : map.SetItem(Key, Text);
        return doc.With(entity with { Strings = map.Count == 0 ? entity.Strings.Remove(Language) : entity.Strings.SetItem(Language, map) });
    }

    public JsonObject ToJson() => new() { ["op"] = "text", ["id"] = Id, ["lang"] = Language, ["key"] = Key, ["text"] = Text };
}

/// <summary>Несколько команд одним шагом отмены (перенос значка в дереве — столбец и строка).</summary>
public sealed record Batch(string Label, IReadOnlyList<IEditCommand> Commands) : IEditCommand
{
    public string Name => Label;

    public ModDocument Apply(ModDocument doc) => Commands.Aggregate(doc, (d, c) => c.Apply(d));

    public JsonObject ToJson() => new() { ["op"] = "batch", ["name"] = Label, ["commands"] = new JsonArray([.. Commands.Select(c => (JsonNode)c.ToJson())]) };
}

/// <summary>Команды из записи JSON (журнал правок, сценарий <c>d2mod apply</c>).</summary>
public static class EditCommands
{
    public static IEditCommand Parse(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        string op = Str(json, "op");
        return op switch
        {
            "set" => new SetField(Str(json, "id"), Str(json, "path"), json["value"]?.DeepClone()),
            "unset" => new SetField(Str(json, "id"), Str(json, "path"), null, Unset: true),
            "add" => new AddEntity(Str(json, "kind"), Str(json, "name"), json["body"] as JsonObject ?? [],
                                   json["folder"]?.GetValueKind() != JsonValueKind.False),
            "remove" => new RemoveEntity(Str(json, "id")),
            "text" => new SetText(Str(json, "id"), Str(json, "lang"), Str(json, "key"), json["text"]?.GetValue<string>()),
            "batch" => new Batch(json["name"]?.GetValue<string>() ?? "правки",
                                 [.. (json["commands"] as JsonArray ?? []).OfType<JsonObject>().Select(Parse)]),
            _ => throw new InvalidDataException($"неизвестная команда «{op}»"),
        };
    }

    /// <summary>Список команд сценария: массив или <c>{"commands": [...]}</c>.</summary>
    public static IReadOnlyList<IEditCommand> ParseScript(string json)
    {
        var root = JsonNode.Parse(json);
        var array = root as JsonArray ?? root?["commands"] as JsonArray ?? throw new InvalidDataException("сценарий: ждали массив команд");
        return [.. array.OfType<JsonObject>().Select(Parse)];
    }

    private static string Str(JsonObject json, string key) =>
        json[key]?.GetValue<string>() is { } s ? s : throw new InvalidDataException($"команда {json.ToJsonString()}: нет поля «{key}»");
}
