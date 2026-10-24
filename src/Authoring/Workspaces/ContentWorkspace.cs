using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Чья сущность: базы (мод её не трогает), правленая модом, своя новая сущность мода.</summary>
public enum EntityOrigin { Base, Patched, Mine }

/// <summary>Строка списка сущностей.</summary>
public sealed record EntityRow(string Id, string Name, EntityOrigin Origin);

/// <summary>Поле сущности по схеме: имя в JSON, тип схемы (<c>integer</c>, <c>string</c>, <c>boolean</c>, <c>number</c>, <c>object</c>,
/// <c>array</c>), значения перечня, вид ссылки, описание и значения «база / слой мода / итог» (JSON; нет — null).</summary>
public sealed record FieldRow(string Name, string Type, IReadOnlyList<string> Enum, string? Ref, string Description, JsonNode? Base, JsonNode? Mod,
                              JsonNode? Value)
{
    /// <summary>Значение одной строкой: простое — как есть, объект и список — JSON.</summary>
    public static string Show(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => value.ToJsonString(),
    };
}

/// <summary>
/// Рабочее пространство Студии «Содержимое» (Э6, Р20): сущности любого вида по схеме содержимого (<see cref="ContentFiles.Schema"/>) —
/// список (база, правленые модом, свои), у выбранной — поля с описаниями из схемы и тремя значениями «база / мод / итог», правка поля по
/// его типу (число, строка, флаг, перечень, ссылка — id нужного вида, объект и список — JSON), сброс к базе, новая сущность мода каталогом.
/// Правки — командами сессии (общая отмена), проверка — по всему моду.
/// </summary>
public sealed class ContentWorkspace
{
    public ContentWorkspace(StudioSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        session.Refreshed += () => Revision++;
    }

    public StudioSession Session { get; }

    public int Revision { get; private set; }

    /// <summary>Открытый вид.</summary>
    public string Kind { get; private set; } = "unit";

    /// <summary>Выбранная сущность; нет — null.</summary>
    public string? Selected { get; private set; }

    /// <summary>Фильтр списка по id и имени (без учёта регистра).</summary>
    public string Filter { get; private set; } = "";

    public static IReadOnlyList<string> Kinds => [.. ContentSet.Kinds.Select(k => k.Name)];

    public void Open(string kind)
    {
        _ = ContentSet.KindNamed(kind);
        (Kind, Selected) = (kind, null);
        Revision++;
    }

    public void Find(string filter)
    {
        Filter = filter ?? "";
        Revision++;
    }

    public void Select(string? id)
    {
        Selected = id;
        Revision++;
    }

    /// <summary>Сущности открытого вида: итог (база с модом), свои и правленые помечены.</summary>
    public IReadOnlyList<EntityRow> Entities()
    {
        var content = Session.Report.Content ?? Session.BaseContent;
        var doc = Session.History.Current;
        return [.. content.Of(ContentSet.KindNamed(Kind).Type)
            .Select(d => new EntityRow(d.Id, Session.NameOf(d.Id),
                Session.IsMine(d.Id) ? EntityOrigin.Mine : doc.Find(d.Id) is not null ? EntityOrigin.Patched : EntityOrigin.Base))
            .Where(r => Filter.Length == 0 || r.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Поля выбранной сущности по схеме вида.</summary>
    public IReadOnlyList<FieldRow> Fields()
    {
        if (Selected is not { } id) return [];
        var kind = ContentSet.KindNamed(Kind);
        var items = ContentFiles.Schema(kind)["properties"]![ContentJson.ArrayName(kind)]!["items"]!;
        var properties = items["properties"] as JsonObject ?? [];
        var merged = Node(Session.Report.Content?.Find(id), kind);
        var original = Node(Session.BaseContent.Find(id), kind);
        var patch = Session.History.Current.Find(id)?.Node;
        var refs = kind.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RefAttribute>() is not null)
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), p => p.GetCustomAttribute<RefAttribute>()!.Kind, StringComparer.Ordinal);
        return [.. properties.Where(p => p.Key != "id").Select(p => new FieldRow(p.Key, TypeOf(p.Value), EnumOf(p.Value), refs.GetValueOrDefault(p.Key),
            p.Value?["description"]?.GetValue<string>() ?? "", original?[p.Key]?.DeepClone(), patch?[p.Key]?.DeepClone(), merged?[p.Key]?.DeepClone()))];
    }

    private static JsonObject? Node(ContentDef? def, ContentKind kind) =>
        def is null ? null : JsonSerializer.SerializeToNode(def, kind.Type, ContentJson.Options) as JsonObject;

    private static string TypeOf(JsonNode? schema)
    {
        var type = schema?["type"];
        return type switch
        {
            JsonValue v => v.GetValue<string>(),
            JsonArray a => a.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t is not null and not "null") ?? "string",
            _ => schema?["enum"] is not null ? "string" : "object",
        };
    }

    private static IReadOnlyList<string> EnumOf(JsonNode? schema) =>
        schema?["enum"] is JsonArray values ? [.. values.Select(v => v?.ToString()).OfType<string>()] : [];

    /// <summary>Ссылки, которые может принять поле вида <paramref name="refKind"/>: id сущностей этого вида в итоге.</summary>
    public IReadOnlyList<string> Choices(string refKind) =>
        [.. (Session.Report.Content ?? Session.BaseContent).Of(ContentSet.KindNamed(refKind).Type).Select(d => d.Id)];

    /// <summary>Поле выбранной сущности из текста по типу поля; пусто у необязательного — убрать (null слоя). Неверный текст — отказ.</summary>
    public bool Set(string field, string text)
    {
        if (Selected is not { } id) return false;
        var row = Fields().FirstOrDefault(f => f.Name == field);
        if (row is null) return Session.Refuse($"у {Kind} нет поля {field}");
        JsonNode? value;
        try
        {
            value = Parse(row, text);
        }
        catch (Exception e) when (e is FormatException or JsonException or OverflowException)
        {
            return Session.Refuse($"{field}: «{text}» — не {row.Type}");
        }
        return Session.Do(new SetField(id, field, value));
    }

    private static JsonNode? Parse(FieldRow row, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        return row.Type switch
        {
            "integer" => JsonValue.Create(long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            "number" => JsonValue.Create(double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture)),
            "boolean" => JsonValue.Create(text is "true" or "1" or "да" ? true : text is "false" or "0" or "нет" ? false : throw new FormatException()),
            "object" or "array" => JsonNode.Parse(text),
            _ when row.Enum.Count > 0 && !row.Enum.Contains(text) => throw new FormatException(),
            _ => JsonValue.Create(text),
        };
    }

    /// <summary>Сбросить поле выбранной сущности к базе (убрать из слоя мода).</summary>
    public bool Reset(string field) => Selected is { } id && Session.Do(new SetField(id, field, null, Unset: true));

    /// <summary>Новая сущность мода открытого вида каталогом — копия выбранной (если есть) или пустая; выбирается сразу.</summary>
    public bool Add(string name)
    {
        var kind = ContentSet.KindNamed(Kind);
        var body = Selected is { } from && Node(Session.Report.Content?.Find(from), kind) is { } copy ? copy : [];
        body.Remove("id");
        body.Remove("legacyId");
        if (!Session.Do(new AddEntity(Kind, name, body))) return false;
        Selected = EntityFolders.IdOf(Session.Project.Id, kind, name);
        Revision++;
        return true;
    }

    /// <summary>Убрать выбранную из слоя мода (своя — исчезает, правка чужой — отменяется).</summary>
    public bool Remove() => Selected is { } id && Session.History.Current.Find(id) is not null && Session.Do(new RemoveEntity(id));

    public string Line() =>
        $"content: kind={Kind} entities={Entities().Count} selected={Selected ?? "-"} fields={Fields().Count} " +
        $"patched={Fields().Count(f => f.Mod is not null)}";
}
