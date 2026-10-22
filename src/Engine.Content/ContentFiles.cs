using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

using D2.Data.Content.Schema;

namespace D2.Data.Content;

/// <summary>Содержимое на диске: каталог мода (<c>mod.json</c> + файл на вид) и схемы JSON для редакторов и Студии.
/// Раскладка данных порта: <c>data/content/base</c> — база (выпускает импорт таблиц оригинала), <c>data/content/schema</c> — схемы
/// (выводятся из записей C#, <see cref="Schema"/>); моды кладут свои файлы видов в <c>&lt;мод&gt;/content/</c>.</summary>
public static class ContentFiles
{
    /// <summary>Каталог базы и схем относительно <c>data/</c>.</summary>
    public const string BaseDir = "content/base";
    public const string SchemaDir = "content/schema";

    /// <summary>Записать набор как мод <paramref name="modId"/> в <paramref name="dir"/>: <c>mod.json</c> и файлы видов (пустой вид не
    /// пишется). <paramref name="schemaRef"/> — путь к каталогу схем относительно <paramref name="dir"/> (для <c>$schema</c>).</summary>
    public static void WriteMod(ContentSet set, string dir, string modId, string name, string? schemaRef = "../schema")
    {
        ArgumentNullException.ThrowIfNull(set);
        Directory.CreateDirectory(dir);
        var mod = new JsonObject { ["id"] = modId, ["name"] = name };
        File.WriteAllText(Path.Combine(dir, "mod.json"), mod.ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n");
        foreach (var kind in ContentSet.Kinds)
        {
            string path = Path.Combine(dir, kind.File);
            var items = set.Of(kind.Type);
            if (items.Count == 0) { File.Delete(path); continue; }
            File.WriteAllText(path, ContentJson.Write(kind, items, schemaRef is null ? null : $"{schemaRef}/{SchemaFile(kind)}"));
        }
    }

    /// <summary>Записать схемы всех видов в <paramref name="dir"/>.</summary>
    public static void WriteSchemas(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var kind in ContentSet.Kinds)
            File.WriteAllText(Path.Combine(dir, SchemaFile(kind)), Schema(kind).ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n");
    }

    public static string SchemaFile(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Path.GetFileNameWithoutExtension(kind.File) + ".schema.json";
    }

    /// <summary>Схема файла вида: объект с массивом сущностей. Описания полей — из <see cref="DescriptionAttribute"/> записи.</summary>
    public static JsonNode Schema(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var exporter = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var description = context.PropertyInfo?.AttributeProvider?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                                      .OfType<DescriptionAttribute>().FirstOrDefault()?.Description
                                  ?? (context.PropertyInfo is null ? context.TypeInfo.Type.GetCustomAttribute<DescriptionAttribute>()?.Description : null);
                if (description is not null && node is JsonObject obj) obj.Insert(0, "description", description);
                return node;
            },
        };
        var item = JsonSchemaExporter.GetJsonSchemaAsNode(ContentJson.Options, kind.Type, exporter);
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = $"Disciples: {kind.Name}",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["$schema"] = new JsonObject { ["type"] = "string" },
                [ContentJson.ArrayName(kind)] = new JsonObject { ["type"] = "array", ["items"] = item },
            },
            ["required"] = new JsonArray(ContentJson.ArrayName(kind)),
            ["additionalProperties"] = false,
        };
    }
}
