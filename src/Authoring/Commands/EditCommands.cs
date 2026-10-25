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
        CheckName(EntityName);
        string id = EntityFolders.IdOf(doc.ModId, ContentSet.KindNamed(Kind), EntityName);
        if (doc.Find(id) is not null) throw new InvalidOperationException($"«{id}» уже есть в моде");
        var node = new JsonObject { ["id"] = id };
        foreach (var (key, value) in Body)
            if (key != "id") node[key] = value?.DeepClone();
        return doc.With(new ModEntity(Kind, id, node, Folder ? EntitySource.Folder : EntitySource.File));
    }

    public JsonObject ToJson() =>
        new() { ["op"] = "add", ["kind"] = Kind, ["name"] = EntityName, ["folder"] = Folder, ["body"] = Body.DeepClone() };

    /// <summary>Имя сущности мода (каталог и часть id): строчные латинские буквы, цифры и «-»; иначе — отказ с текстом.</summary>
    public static void CheckName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            throw new InvalidOperationException($"имя «{name}» — только строчные латинские буквы, цифры и «-» (оно же каталог и часть id)");
        if (LegacyIds.LooksLegacy(name))
            throw new InvalidOperationException($"имя «{name}» похоже на id оригинала — графика такой сущности назвалась бы как графика оригинала");
    }
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

/// <summary>Картинка сущности-каталога мода (<see cref="EntityArt"/>: <c>icon.png</c> постройки…): байты PNG; null — убрать. Картинка лежит в
/// документе — выбор и замена отменяются, как любая правка, а на диск идут при записи мода.</summary>
public sealed record SetFile(string Id, string FileName, ImmutableArray<byte>? Bytes) : IEditCommand
{
    public string Name => Bytes is null ? $"убрать {FileName} у {Id}" : $"{FileName} у {Id}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Find(Id) is not { Source: EntitySource.Folder } entity)
            throw new InvalidOperationException($"картинки — у сущности-каталога мода; «{Id}» не она");
        if (EntityArt.Find(entity.Kind, FileName) is not { } slot)
            throw new InvalidOperationException($"у {entity.Kind} нет картинки «{FileName}» (есть: {string.Join(", ", EntityArt.Of(entity.Kind).Select(s => s.File))})");
        if (Bytes is not { } bytes) return entity.Files.ContainsKey(slot.File) ? doc.With(entity with { Files = entity.Files.Remove(slot.File) }) : doc;
        var (w, h) = EntityArt.PngSize(bytes.AsSpan());
        if (w <= 0 || h <= 0 || bytes.Length < 45)   // подпись, IHDR, IDAT и IEND — не меньше 45 байт
            throw new InvalidOperationException($"{FileName}: это не PNG (или файл оборван)");
        return doc.With(entity with { Files = entity.Files.SetItem(slot.File, bytes) });
    }

    public JsonObject ToJson() => new()
    {
        ["op"] = "file", ["id"] = Id, ["name"] = FileName, ["png"] = Bytes is { } b ? Convert.ToBase64String(b.AsSpan()) : null,
    };
}

/// <summary>Поле описания мода (<c>mod.json</c>): имя, описание, версия, зависимости, порядок наложения (<c>loadAfter</c> — моды, которые
/// этот мод перекрывает: он «главнее» их); null — убрать поле. <c>id</c> не меняется.</summary>
public sealed record SetManifest(string Field, JsonNode? Value) : IEditCommand
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "name", "description", "version", "authors", "dependencies", "loadAfter", "languages",
    };

    public string Name => $"mod.json: {Field}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (!Fields.Contains(Field)) throw new InvalidOperationException($"поле mod.json «{Field}» Студия не правит (можно: {string.Join(", ", Fields)})");
        if (doc.Manifest is not { } manifest) throw new InvalidOperationException("у документа нет mod.json");
        if (Field is "dependencies" or "loadAfter" && Value is JsonArray ids && ids.Any(i => i?.GetValue<string>() == doc.ModId))
            throw new InvalidOperationException("мод не может зависеть от себя или ставиться после себя");
        var next = (JsonObject)manifest.DeepClone();
        if (Value is null) next.Remove(Field);
        else next[Field] = Value.DeepClone();
        return JsonNode.DeepEquals(next, manifest) ? doc : doc.WithManifest(next);
    }

    public JsonObject ToJson() => new() { ["op"] = "manifest", ["field"] = Field, ["value"] = Value?.DeepClone() };
}

/// <summary>Переименовать свою сущность-каталог: новое имя каталога и id (<c>мод:вид/новое</c>), ссылки на неё в документе мода (требования,
/// списки лорда, постройки найма…) переходят на новый id; строки и картинки — с ней, место в документе — то же.</summary>
public sealed record RenameEntity(string Id, string NewName) : IEditCommand
{
    public string Name => $"переименовать {Id} в {NewName}";

    public ModDocument Apply(ModDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Find(Id) is not { Source: EntitySource.Folder } || !Id.StartsWith(doc.ModId + ":", StringComparison.Ordinal))
            throw new InvalidOperationException($"переименовать можно только свою сущность-каталог; «{Id}» не она");
        AddEntity.CheckName(NewName);
        string id = EntityFolders.IdOf(doc.ModId, ContentSet.KindNamed(ModDocument.KindOf(Id)), NewName);
        if (id == Id) return doc;
        if (doc.Find(id) is not null) throw new InvalidOperationException($"«{id}» уже есть в моде");
        return doc.WithEntities(doc.Entities.ConvertAll(e =>
        {
            var node = (JsonObject)Replace(e.Node, Id, id);   // и само поле id переименованной
            if (e.Id == Id) return e with { Id = id, Node = node };
            return JsonNode.DeepEquals(node, e.Node) ? e : e with { Node = node };
        }));
    }

    /// <summary>Копия узла, где строки, равные <paramref name="from"/>, — <paramref name="to"/>.</summary>
    private static JsonNode Replace(JsonNode node, string from, string to) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Replace(p.Value, from, to)))),
        JsonArray a => new JsonArray([.. a.Select(v => v is null ? null : Replace(v, from, to))]),
        JsonValue v when v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == from => JsonValue.Create(to),
        _ => node.DeepClone(),
    };

    public JsonObject ToJson() => new() { ["op"] = "rename", ["id"] = Id, ["name"] = NewName };
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
            "file" => new SetFile(Str(json, "id"), Str(json, "name"), FileBytes(json)),
            "rename" => new RenameEntity(Str(json, "id"), Str(json, "name")),
            "manifest" => new SetManifest(Str(json, "field"), json["value"]?.DeepClone()),
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

    /// <summary>Байты картинки команды <c>file</c>: <c>png</c> — base64 (так пишет журнал), <c>from</c> — путь к PNG (сценарий
    /// <c>d2mod apply</c>); ни того, ни другого — убрать картинку.</summary>
    private static ImmutableArray<byte>? FileBytes(JsonObject json) =>
        json["png"]?.GetValue<string>() is { } b64 ? ImmutableArray.Create(Convert.FromBase64String(b64))
        : json["from"]?.GetValue<string>() is { } path ? ImmutableArray.Create(File.ReadAllBytes(path))
        : null;

    private static string Str(JsonObject json, string key) =>
        json[key]?.GetValue<string>() is { } s ? s : throw new InvalidDataException($"команда {json.ToJsonString()}: нет поля «{key}»");
}
