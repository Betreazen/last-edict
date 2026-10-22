using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using D2.Data.Content.Schema;

namespace D2.Data.Content;

/// <summary>JSON содержимого: поля и значения перечней — camelCase (<c>"class": "boostDamage"</c>), пустое не пишется (null, ноль, ложь,
/// пустой список, группа без значений), отступы — для чтения и диффов. Файл слоя — объект с массивом сущностей вида под его именем
/// во множественном числе файла (<c>{"$schema": "…", "units": [ … ]}</c>).</summary>
public static class ContentJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            WriteIndented = true,
            IndentSize = 1,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmpty } },
        };
        // Значение вне перечня (мод оригинала с незнакомым числом) пишется и читается числом — импорт его сохраняет как есть.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Пустой список и группа, равная своему значению по умолчанию, не пишутся.</summary>
    private static void SkipEmpty(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        foreach (var property in info.Properties)
        {
            var type = property.PropertyType;
            if (type.IsEnum) property.ShouldSerialize = static (_, _) => true;   // «soldier», «weapon» — не умалчивать
            else if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
                property.ShouldSerialize = (_, value) => value is IEnumerable e && e.GetEnumerator().MoveNext();
            else if (type.IsClass && type != typeof(string) && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                object empty = Activator.CreateInstance(type)!;
                property.ShouldSerialize = (_, value) => value is not null && !value.Equals(empty);
            }
        }
    }

    /// <summary>Имя массива сущностей в файле вида: имя файла без расширения в camelCase (<c>leader-skills.json</c> → <c>leaderSkills</c>).</summary>
    public static string ArrayName(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var parts = Path.GetFileNameWithoutExtension(kind.File).Split('-');
        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>Файл вида: сущности по порядку, ссылка на схему (<c>$schema</c>) — для подсказок редактора.</summary>
    public static string Write(ContentKind kind, IEnumerable<ContentDef> items, string? schema)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var array = new JsonArray();
        foreach (var item in items) array.Add(JsonSerializer.SerializeToNode(item, kind.Type, Options));
        var doc = new JsonObject();
        if (schema is not null) doc["$schema"] = schema;
        doc[ArrayName(kind)] = array;
        return doc.ToJsonString(Options).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Сущности файла вида как узлы JSON (слой мода может быть частичным — его поля накладываются на базу).</summary>
    public static IReadOnlyList<JsonObject> ReadNodes(ContentKind kind, string json, string where)
    {
        ArgumentNullException.ThrowIfNull(kind);
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new InvalidDataException($"{where}: {e.Message}", e); }
        if (root is not JsonObject obj || obj[ArrayName(kind)] is not JsonArray array)
            throw new InvalidDataException($"{where}: ожидался объект с массивом «{ArrayName(kind)}»");
        return [.. array.Select((n, i) => n as JsonObject ?? throw new InvalidDataException($"{where}: элемент {i} — не объект"))];
    }

    /// <summary>Полная сущность из узла: обязательные поля, известные поля и значения перечней проверяются.</summary>
    public static ContentDef ToDef(ContentKind kind, JsonObject node, string where)
    {
        ArgumentNullException.ThrowIfNull(kind);
        try
        {
            return (ContentDef?)node.Deserialize(kind.Type, Options)
                   ?? throw new InvalidDataException($"{where}: пустая сущность");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{where}: {e.Message}", e);
        }
    }
}
