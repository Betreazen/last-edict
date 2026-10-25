using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Project;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Documents;

/// <summary>Где сущность лежит в моде: в файле вида (<c>content/buildings.json</c>) или своим каталогом (<c>content/buildings/spire/</c>).</summary>
public enum EntitySource { File, Folder }

/// <summary>
/// Сущность слоя мода: узел JSON (полная сущность мода или правка по полям чужой — как в файле слоя), место и, у каталога, строки по языкам
/// (короткий ключ → текст). Узел после создания не меняется: команды строят новый (<see cref="With"/>).
/// </summary>
public sealed record ModEntity(string Kind, string Id, JsonObject Node, EntitySource Source)
{
    /// <summary>Строки каталога: язык → короткий ключ → текст (у сущности из файла вида — пусто).</summary>
    public ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, string>> Strings { get; init; } =
        ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, string>>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>Картинки каталога (<c>icon.png</c>… — <see cref="EntityArt"/>): имя файла → байты. У сущности из файла вида — пусто.
    /// Байты не меняются: команда кладёт новый набор (<see cref="Commands.SetFile"/>).</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<byte>> Files { get; init; } =
        ImmutableSortedDictionary<string, ImmutableArray<byte>>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>Имя в id (после «/»): у каталога — имя каталога.</summary>
    public string Name => Id[(Id.LastIndexOf('/') + 1)..];

    public ContentKind ContentKind => ContentSet.KindNamed(Kind);

    /// <summary>Копия с другим узлом (узел копируется — исходный не меняется).</summary>
    public ModEntity With(JsonObject node) => this with { Node = (JsonObject)node.DeepClone() };

    /// <summary>Значение поля по пути через точку (<c>tree.column</c>); нет — null.</summary>
    public JsonNode? Get(string path)
    {
        JsonNode? at = Node;
        foreach (string part in path.Split('.'))
        {
            if (at is not JsonObject obj || !obj.TryGetPropertyValue(part, out at)) return null;
        }
        return at;
    }
}

/// <summary>
/// Документ мода — его слой содержимого целиком: сущности файлов видов и каталогов «папка = сущность» по порядку. Неизменяемый: команда
/// (<see cref="Commands.IEditCommand"/>) возвращает новый документ, общие сущности делятся, — отмена в ядре дешёвая и проверяется тестами.
/// Читается и пишется только внутри каталога мода; запись трогает лишь изменившиеся файлы.
/// </summary>
public sealed class ModDocument
{
    private ModDocument(string modId, ImmutableList<ModEntity> entities, ImmutableDictionary<string, string>? schemas = null)
    {
        ModId = modId;
        Entities = entities;
        Schemas = schemas ?? ImmutableDictionary<string, string>.Empty;
    }

    /// <summary>Ссылки <c>$schema</c> файлов видов мода (вид → ссылка): запись их сохраняет — подсказки редактора у автора не пропадают.</summary>
    public ImmutableDictionary<string, string> Schemas { get; }

    public string ModId { get; }

    public ImmutableList<ModEntity> Entities { get; }

    public static ModDocument Empty(string modId) => new(modId, []);

    public ModEntity? Find(string id) => Entities.FirstOrDefault(e => e.Id == id);

    public IEnumerable<ModEntity> Of(string kind) => Entities.Where(e => e.Kind == kind);

    /// <summary>Документ с сущностью: та же по id — заменена на месте, новая — в конец.</summary>
    public ModDocument With(ModEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        int at = Entities.FindIndex(e => e.Id == entity.Id);
        return new(ModId, at >= 0 ? Entities.SetItem(at, entity) : Entities.Add(entity), Schemas);
    }

    /// <summary>Документ с другим списком сущностей (переименование правит ссылки во всех).</summary>
    public ModDocument WithEntities(ImmutableList<ModEntity> entities) => new(ModId, entities ?? throw new ArgumentNullException(nameof(entities)), Schemas);

    public ModDocument Without(string id)
    {
        int at = Entities.FindIndex(e => e.Id == id);
        return at < 0 ? this : new(ModId, Entities.RemoveAt(at), Schemas);
    }

    /// <summary>Вид по id сущности (<c>мод:вид/имя</c>).</summary>
    public static string KindOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int colon = id.IndexOf(':'), slash = id.IndexOf('/');
        if (colon <= 0 || slash <= colon + 1) throw new ArgumentException($"«{id}» — не id сущности (мод:вид/имя)", nameof(id));
        string kind = id[(colon + 1)..slash];
        _ = ContentSet.KindNamed(kind);
        return kind;
    }

    /// <summary>Слепок документа для сравнения: все файлы, какими их запишет <see cref="Save"/>.</summary>
    public string Fingerprint() => string.Join("\n", Files()
        .Concat(Binaries().Select(p => KeyValuePair.Create(p.Key, "sha256:" + Convert.ToHexString(SHA256.HashData(p.Value.AsSpan())))))
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"--- {p.Key}\n{p.Value}"));

    // ---- чтение и запись

    public static ModDocument Load(ModProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var entities = ImmutableList.CreateBuilder<ModEntity>();
        var schemas = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var kind in ContentSet.Kinds)
        {
            string file = Path.Combine(project.ContentDir, kind.File);
            if (File.Exists(file) && SchemaOf(file) is { } schema) schemas[kind.Name] = schema;
            if (File.Exists(file))
                foreach (var node in ContentJson.ReadNodes(kind, File.ReadAllText(file), file))
                    entities.Add(new ModEntity(kind.Name, node["id"]?.GetValue<string>() ?? throw new InvalidDataException($"{file}: сущность без id"),
                                               (JsonObject)node.DeepClone(), EntitySource.File));
            foreach (var folder in EntityFolders.Scan(project.Info, kind))
            {
                JsonObject node;
                try
                {
                    node = JsonNode.Parse(File.ReadAllText(folder.EntityPath)) as JsonObject ?? throw new InvalidDataException($"{folder.EntityPath}: ждали объект");
                }
                catch (JsonException e)
                {
                    throw new InvalidDataException($"{folder.EntityPath}: {e.Message}", e);
                }
                node.Remove("id");
                var withId = new JsonObject { ["id"] = folder.Id };
                foreach (var (key, value) in node) withId[key] = value?.DeepClone();
                entities.Add(new ModEntity(kind.Name, folder.Id, withId, EntitySource.Folder)
                {
                    Strings = ReadStrings(folder.Dir), Files = ReadFiles(folder.Dir),
                });
            }
        }
        return new ModDocument(project.Id, entities.ToImmutable(), schemas.ToImmutable());
    }

    /// <summary><c>$schema</c> файла вида; нет или файл не читается как объект — null (ошибку разбора скажет чтение сущностей).</summary>
    private static string? SchemaOf(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            })?["$schema"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ImmutableSortedDictionary<string, ImmutableSortedDictionary<string, string>> ReadStrings(string dir)
    {
        var result = ImmutableSortedDictionary.CreateBuilder<string, ImmutableSortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(dir, "strings.*.json"))
        {
            string lang = Path.GetFileNameWithoutExtension(file)["strings.".Length..];
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
            result[lang] = map.ToImmutableSortedDictionary(StringComparer.Ordinal);
        }
        return result.ToImmutable();
    }

    /// <summary>Картинки каталога сущности (<c>*.png</c> рядом с <c>entity.json</c>).</summary>
    private static ImmutableSortedDictionary<string, ImmutableArray<byte>> ReadFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.png").ToImmutableSortedDictionary(f => Path.GetFileName(f), f => ImmutableArray.Create(File.ReadAllBytes(f)),
                                                                             StringComparer.Ordinal);

    /// <summary>Записать документ в мод: файлы видов, каталоги сущностей, строки, картинки. Меняются только файлы с другим содержимым; файлы и
    /// каталоги сущностей, которых в документе больше нет, удаляются (только внутри <c>content/</c> мода). Сколько файлов записано и удалено.</summary>
    public (int Written, int Deleted) Save(ModProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Id != ModId) throw new ArgumentException($"документ мода {ModId} в каталог мода {project.Id}", nameof(project));
        var files = Files();
        var binaries = Binaries();
        int written = 0, deleted = 0;
        foreach (var (relative, text) in files)
        {
            string path = project.PathOf(relative);
            if (File.Exists(path) && File.ReadAllText(path) == text) continue;
            project.Write(relative, text);
            written++;
        }
        foreach (var (relative, bytes) in binaries)
        {
            string path = project.PathOf(relative);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes.AsSpan())) continue;
            project.Write(relative, bytes.AsSpan());
            written++;
        }
        // Лишнее: файлы видов без сущностей и каталоги сущностей, которых больше нет.
        foreach (var kind in ContentSet.Kinds)
        {
            string relative = Path.Combine("content", kind.File);
            if (!files.ContainsKey(Norm(relative)) && File.Exists(project.PathOf(relative)))
            {
                project.Delete(relative);
                deleted++;
            }
            foreach (var folder in EntityFolders.Scan(project.Info, kind).Where(f => Find(f.Id) is not { Source: EntitySource.Folder }))
            {
                string dir = project.PathOf(Path.GetRelativePath(project.Dir, folder.Dir));
                foreach (string file in Directory.EnumerateFiles(dir, "*.json").Concat(Directory.EnumerateFiles(dir, "*.png"))) File.Delete(file);
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                deleted++;
            }
            foreach (var folder in EntityFolders.Scan(project.Info, kind).Where(f => Find(f.Id) is { Source: EntitySource.Folder }))
            {
                foreach (string file in Directory.EnumerateFiles(folder.Dir, "strings.*.json"))
                    if (!files.ContainsKey(Norm(Path.GetRelativePath(project.Dir, file))))
                    {
                        File.Delete(file);
                        deleted++;
                    }
                foreach (string file in Directory.EnumerateFiles(folder.Dir, "*.png"))
                    if (!binaries.ContainsKey(Norm(Path.GetRelativePath(project.Dir, file))))
                    {
                        File.Delete(file);
                        deleted++;
                    }
            }
        }
        return (written, deleted);
    }

    /// <summary>Файлы мода по документу: относительный путь (через «/») → текст.</summary>
    private Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in ContentSet.Kinds)
        {
            var inFile = Entities.Where(e => e.Kind == kind.Name && e.Source == EntitySource.File).ToList();
            if (inFile.Count > 0)
            {
                var array = new JsonArray([.. inFile.Select(e => (JsonNode)e.Node.DeepClone())]);
                var root = new JsonObject();
                if (Schemas.TryGetValue(kind.Name, out var schema)) root["$schema"] = schema;
                root[ContentJson.ArrayName(kind)] = array;
                files[$"content/{kind.File}"] = Text(root);
            }
            foreach (var e in Entities.Where(e => e.Kind == kind.Name && e.Source == EntitySource.Folder))
            {
                string dir = $"content/{Path.GetFileNameWithoutExtension(kind.File)}/{e.Name}";
                var body = new JsonObject();
                foreach (var (key, value) in e.Node)
                    if (key != "id") body[key] = value?.DeepClone();
                files[$"{dir}/{EntityFolders.EntityFile}"] = Text(body);
                foreach (var (lang, map) in e.Strings.Where(s => s.Value.Count > 0))
                    files[$"{dir}/strings.{lang}.json"] = Text(new JsonObject(map.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))));
            }
        }
        return files;
    }

    /// <summary>Картинки сущностей-каталогов: относительный путь (через «/») → байты.</summary>
    private Dictionary<string, ImmutableArray<byte>> Binaries()
    {
        var files = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var e in Entities.Where(e => e.Source == EntitySource.Folder))
            foreach (var (name, bytes) in e.Files)
                files[$"{FolderOf(e)}/{name}"] = bytes;
        return files;
    }

    /// <summary>Каталог сущности в моде (через «/»): <c>content/buildings/spire</c>.</summary>
    public static string FolderOf(ModEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return $"content/{Path.GetFileNameWithoutExtension(entity.ContentKind.File)}/{entity.Name}";
    }

    private static string Norm(string relative) => relative.Replace('\\', '/');

    private static string Text(JsonNode node) => node.ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n";

    public override string ToString() => new StringBuilder().Append(ModId).Append(": ").Append(Entities.Count).Append(" сущностей").ToString();
}
