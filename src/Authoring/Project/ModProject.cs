using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Project;

/// <summary>
/// Проект Студии — каталог мода (<c>mod.json</c>, <c>content/</c>, …): то, что правит автор. Студия и <c>d2mod</c> пишут <b>только внутри</b>
/// него (<see cref="PathOf"/> отказывает пути наружу), база (<c>data/content/base</c>) только читается — правка базы = запись в слой мода
/// (<c>docs/tools/data-contract.md</c>, П1, П4). Мод с C#-плагином помечен «исполняет код» (<see cref="ExecutesCode"/>, Р21).
/// </summary>
public sealed class ModProject
{
    public const string ManifestFile = "mod.json";

    private ModProject(string dir, ModInfo info)
    {
        Dir = dir;
        Info = info;
    }

    /// <summary>Полный путь каталога мода.</summary>
    public string Dir { get; }

    public ModInfo Info { get; }

    public string Id => Info.Id;

    /// <summary>Каталог содержимого мода (<c>content/</c>; его может ещё не быть).</summary>
    public string ContentDir => Path.Combine(Dir, "content");

    /// <summary>Мод несёт C#-плагин (<c>plugins/*.dll</c>) — исполняет код.</summary>
    public bool ExecutesCode => Info.ExecutesCode;

    /// <summary>Открыть каталог мода (в нём <c>mod.json</c> с полем <c>id</c>).</summary>
    public static ModProject Open(string dir)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);
        string full = Path.GetFullPath(dir);
        string manifest = Path.Combine(full, ManifestFile);
        if (!File.Exists(manifest)) throw new FileNotFoundException($"{full}: нет {ManifestFile} — это не каталог мода", manifest);
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(File.ReadAllText(manifest)) as JsonObject;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{manifest}: {e.Message}", e);
        }
        string id = json?["id"]?.GetValue<string>() is { Length: > 0 } i ? i : throw new InvalidDataException($"{manifest}: нет поля id");
        if (!ValidId(id)) throw new InvalidDataException($"{manifest}: id «{id}» — только строчные латинские буквы, цифры и «-»");
        string name = json?["name"]?.GetValue<string>() is { Length: > 0 } n ? n : id;
        return new ModProject(full, new ModInfo(id, name, full)
        {
            Dependencies = Strings(json?["dependencies"]),
            LoadAfter = Strings(json?["loadAfter"]),
            Plugins = ModInfo.PluginsIn(full),
        });
    }

    /// <summary>Новый мод в пустом (или несуществующем) каталоге: <c>mod.json</c> и пустой <c>content/</c>.</summary>
    public static ModProject Create(string dir, string id, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);
        if (!ValidId(id)) throw new ArgumentException($"id мода «{id}» — только строчные латинские буквы, цифры и «-»", nameof(id));
        string full = Path.GetFullPath(dir);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new IOException($"{full}: каталог не пуст — новый мод создаётся в пустом");
        Directory.CreateDirectory(Path.Combine(full, "content"));
        var manifest = new JsonObject { ["id"] = id, ["name"] = name ?? id };
        File.WriteAllText(Path.Combine(full, ManifestFile), manifest.ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n");
        return Open(full);
    }

    /// <summary>Путь внутри мода по относительному; путь наружу (<c>..</c>, абсолютный в другое место) — отказ.</summary>
    public string PathOf(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        string full = Path.GetFullPath(Path.Combine(Dir, relative));
        string root = Dir.EndsWith(Path.DirectorySeparatorChar) ? Dir : Dir + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !full.Equals(Dir, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"«{relative}» — вне каталога мода {Dir}: Студия пишет только в свой мод");
        return full;
    }

    /// <summary>Записать файл внутри мода целиком и атомарно: во временный рядом, затем замена.</summary>
    public void Write(string relative, string text)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Записать двоичный файл внутри мода (картинку) атомарно.</summary>
    public void Write(string relative, ReadOnlySpan<byte> bytes)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Удалить файл внутри мода (нет — ничего).</summary>
    public void Delete(string relative)
    {
        string path = PathOf(relative);
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool ValidId(string id) => id.Length > 0 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(v => v?.GetValue<string>()).OfType<string>()] : [];
}
