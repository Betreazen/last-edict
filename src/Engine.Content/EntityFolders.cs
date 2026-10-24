using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LastEdict.Data.Content;

/// <summary>Сущность мода в своём каталоге: вид, имя каталога (оно же имя в id), id и каталог.</summary>
public sealed record EntityFolder(ContentKind Kind, string Name, string Id, string Dir)
{
    public string EntityPath => Path.Combine(Dir, EntityFolders.EntityFile);

    /// <summary>Значок сущности (<c>icon.png</c>), если он есть.</summary>
    public string? Icon => File.Exists(Path.Combine(Dir, EntityFolders.IconFile)) ? Path.Combine(Dir, EntityFolders.IconFile) : null;
}

/// <summary>
/// «Папка = сущность» (Э6, Студия, roadmap §3.4): кроме файла вида (<c>content/buildings.json</c>) сущность мода может жить в своём каталоге
/// <c>content/&lt;вид&gt;/&lt;имя&gt;/</c> — <c>entity.json</c> (та же запись, что элемент массива файла вида; <c>id</c> можно не писать — он
/// <c>&lt;мод&gt;:&lt;вид&gt;/&lt;имя каталога&gt;</c>), тексты <c>strings.&lt;язык&gt;.json</c> короткими ключами (<c>{"name": "Шпиль"}</c>) и
/// значок <c>icon.png</c>. <b>Id строк — автоматические:</b> текстовое поле записи (<c>nameText</c>, <c>descriptionText</c>…), которого нет в
/// <c>entity.json</c>, а короткий ключ (<c>name</c>, <c>description</c>) есть в строках каталога, получает ключ <see cref="TextKey"/>
/// (<c>towers:building/spire#name</c>) — строки мода с этими ключами отдаёт <see cref="Strings"/>. Каталог читается слоем мода наравне с
/// файлом вида (<see cref="ContentLayers"/>), шага сборки нет: Студия и игра видят одно и то же.
/// </summary>
public static class EntityFolders
{
    public const string EntityFile = "entity.json", IconFile = "icon.png";

    /// <summary>Каталог сущностей вида в каталоге содержимого мода: имя файла вида без расширения (<c>buildings</c>).</summary>
    public static string KindDir(string contentDir, ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Path.Combine(contentDir, Path.GetFileNameWithoutExtension(kind.File));
    }

    /// <summary>Каталоги сущностей вида у мода, по имени каталога.</summary>
    public static IReadOnlyList<EntityFolder> Scan(ModInfo mod, ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(mod);
        string dir = KindDir(ContentLayers.ContentDir(mod), kind);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.EnumerateDirectories(dir)
            .Where(d => File.Exists(Path.Combine(d, EntityFile)))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(d => new EntityFolder(kind, Path.GetFileName(d), IdOf(mod.Id, kind, Path.GetFileName(d)), d))];
    }

    /// <summary>Все каталоги сущностей мода.</summary>
    public static IReadOnlyList<EntityFolder> Scan(ModInfo mod) => [.. ContentSet.Kinds.SelectMany(k => Scan(mod, k))];

    public static string IdOf(string modId, ContentKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return $"{modId}:{kind.Name}/{name}";
    }

    /// <summary>Ключ строки поля сущности: <c>&lt;id&gt;#&lt;короткий ключ&gt;</c>.</summary>
    public static string TextKey(string id, string shortKey) => $"{id}#{shortKey}";

    /// <summary>Текстовые поля записи вида: имя в JSON (<c>nameText</c>) → короткий ключ строк каталога (<c>name</c>).</summary>
    public static IReadOnlyList<(string Field, string Short)> TextFields(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return [.. kind.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Text", StringComparison.Ordinal) && p.Name.Length > 4)
            .Select(p => (JsonNamingPolicy.CamelCase.ConvertName(p.Name), JsonNamingPolicy.CamelCase.ConvertName(p.Name[..^4])))];
    }

    /// <summary>Узел слоя из каталога сущности: id — по каталогу (записанный другой — ошибка), текстовые поля без значения — автоматические
    /// ключи строк, если строка есть в каталоге.</summary>
    public static JsonObject Read(EntityFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string path = folder.EntityPath;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
        if (root is not JsonObject node) throw new InvalidDataException($"{path}: ждали объект сущности");
        if (node["id"] is { } written && written.GetValueKind() == JsonValueKind.String && written.GetValue<string>() != folder.Id)
            throw new InvalidDataException($"{path}: id «{written}» не совпадает с каталогом — у сущности в каталоге id «{folder.Id}»");
        var result = new JsonObject { ["id"] = folder.Id };
        foreach (var (key, value) in node)
            if (key != "id") result[key] = value?.DeepClone();
        var shorts = ShortKeys(folder.Dir);
        foreach (var (field, shortKey) in TextFields(folder.Kind))
            if (!result.ContainsKey(field) && shorts.Contains(shortKey)) result[field] = TextKey(folder.Id, shortKey);
        return result;
    }

    /// <summary>Короткие ключи строк каталога на всех языках.</summary>
    private static HashSet<string> ShortKeys(string dir)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(dir, "strings.*.json"))
            keys.UnionWith(ReadStrings(file).Keys);
        return keys;
    }

    private static Dictionary<string, string> ReadStrings(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{file}: {e.Message}", e);
        }
    }

    /// <summary>Строки сущностей мода на языке <paramref name="language"/> (нет файла языка — английский): ключ <see cref="TextKey"/> → текст.</summary>
    public static IReadOnlyDictionary<string, string> Strings(ModInfo mod, string language)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in Scan(mod))
        {
            string file = Path.Combine(folder.Dir, $"strings.{language}.json");
            if (!File.Exists(file)) file = Path.Combine(folder.Dir, "strings.en.json");
            if (!File.Exists(file)) continue;
            foreach (var (key, text) in ReadStrings(file)) strings[TextKey(folder.Id, key)] = text;
        }
        return strings;
    }
}
