using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LastEdict.Data.Content;

/// <summary>Правила мода для сети и упаковки: id, имя, хеш файлов, которые меняют правила (<see cref="RulesetHash.ModFiles"/>), и «исполняет
/// код».</summary>
public sealed record ModRules(string Id, string Name, string Hash, bool ExecutesCode);

/// <summary>Набор правил партии (<c>ruleset_hash</c>, Э7, <c>docs/network.md</c> §5): хеш базы содержимого, моды по порядку наложения со
/// своими хешами и общий хеш. Одинаковый <see cref="Hash"/> у хоста и клиента — одни правила; разный — отказ входа с текстом
/// (<see cref="RulesetHash.Differences"/>).</summary>
public sealed record Ruleset(string Hash, string Base, IReadOnlyList<ModRules> Mods);

/// <summary>
/// Хеш правил с модами — один на игру, сеть и <c>d2mod pack</c> (договор Э7 и «Студии 2»). В хеш входит только то, что меняет правила:
/// <list type="bullet">
/// <item>база — файлы видов содержимого (<c>units.json</c>, …) каталога базы;</item>
/// <item>мод — <c>mod.json</c> (только id, <c>dependencies</c>, <c>loadAfter</c>: порядок наложения), файлы видов его содержимого,
/// <c>entity.json</c> «папок = сущностей» и сборки плагина <c>plugins/*.dll</c>.</item>
/// </list>
/// Не входят строки (<c>strings.*.json</c>), значки, графика, звук, разметка экранов и сценарии: партия по сети идёт состоянием хоста, а
/// тексты и вид у каждого свои. JSON хешируется каноничным (ключи по порядку, без пробелов) — переводы строк и отступы не важны.
/// Хеши — полный SHA-256 (64 знака).
/// </summary>
public static class RulesetHash
{
    /// <summary>Набор правил: база <paramref name="baseDir"/> и моды в порядке наложения (<see cref="ContentLayers.Order"/>).</summary>
    public static Ruleset Of(string baseDir, IReadOnlyList<ModInfo> mods)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDir);
        ArgumentNullException.ThrowIfNull(mods);
        string baseHash = Combine(ContentSet.Kinds.Select(k => Path.Combine(baseDir, k.File)).Where(File.Exists)
                                                  .Select(p => (Path.GetFileName(p), FileHash(p))));
        // Мод без файлов правил (только графика, звук, строки — HD-слой) в набор не входит: у друзей он может быть разным.
        var ordered = ContentLayers.Order(mods).Where(HasRules).Select(Of).ToList();
        var all = new StringBuilder("le-ruleset/1\nbase=").Append(baseHash).Append('\n');
        foreach (var mod in ordered) all.Append("mod=").Append(mod.Id).Append(':').Append(mod.Hash).Append('\n');
        return new Ruleset(Sha(all.ToString()), baseHash, ordered);
    }

    /// <summary>У мода есть файлы правил (кроме <c>mod.json</c>): виды содержимого, «папки = сущности», плагины.</summary>
    public static bool HasRules(ModInfo mod) => ModFiles(mod).Count > 1;

    /// <summary>Правила одного мода.</summary>
    public static ModRules Of(ModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        return new ModRules(mod.Id, mod.Name, Combine(ModFiles(mod).Select(f => (f.Name, f.Hash))), mod.ExecutesCode);
    }

    /// <summary>Файлы мода, которые входят в хеш, — относительный путь (через «/») и хеш содержимого, по пути. <c>mod.json</c> — своей
    /// записью порядка наложения.</summary>
    public static IReadOnlyList<(string Name, string Hash)> ModFiles(ModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        var files = new List<(string, string)>
        {
            ("mod.json", Sha(Canonical(new JsonObject
            {
                ["id"] = mod.Id,
                ["dependencies"] = new JsonArray([.. mod.Dependencies.Select(d => (JsonNode)d)]),
                ["loadAfter"] = new JsonArray([.. mod.LoadAfter.Select(d => (JsonNode)d)]),
            }))),
        };
        string content = ContentLayers.ContentDir(mod);
        foreach (var kind in ContentSet.Kinds)
        {
            string path = Path.Combine(content, kind.File);
            if (File.Exists(path)) files.Add((Relative(mod.Dir, path), FileHash(path)));
            foreach (var folder in EntityFolders.Scan(mod, kind)) files.Add((Relative(mod.Dir, folder.EntityPath), FileHash(folder.EntityPath)));
        }
        foreach (string plugin in mod.Plugins)
            files.Add((Relative(mod.Dir, plugin), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(plugin))).ToLowerInvariant()));
        return [.. files.OrderBy(f => f.Item1, StringComparer.Ordinal)];
    }

    /// <summary>Чем набор <paramref name="theirs"/> (входящего) отличается от <paramref name="ours"/> (хоста) — строки для отказа входа;
    /// пусто — правила одни.</summary>
    public static IReadOnlyList<string> Differences(Ruleset ours, Ruleset theirs)
    {
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(theirs);
        if (ours.Hash == theirs.Hash) return [];
        var lines = new List<string>();
        if (ours.Base != theirs.Base) lines.Add("другая база содержимого: импорт с другой копии или версии игры (переимпортируйте игру)");
        var mine = ours.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var other = theirs.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var m in ours.Mods.Where(m => !other.ContainsKey(m.Id))) lines.Add($"нет мода «{m.Name}» ({m.Id}) — включите его");
        foreach (var m in theirs.Mods.Where(m => !mine.ContainsKey(m.Id))) lines.Add($"лишний мод «{m.Name}» ({m.Id}) — выключите его");
        foreach (var m in ours.Mods.Where(m => other.TryGetValue(m.Id, out var o) && o.Hash != m.Hash))
            lines.Add($"мод «{m.Name}» ({m.Id}) другой версии");
        if (lines.Count == 0 && !ours.Mods.Select(m => m.Id).SequenceEqual(theirs.Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase))
            lines.Add("моды в другом порядке: " + string.Join(", ", ours.Mods.Select(m => m.Id)));
        if (lines.Count == 0) lines.Add("другие правила");
        return lines;
    }

    private static string FileHash(string path)
    {
        try
        {
            return Sha(Canonical(JsonNode.Parse(File.ReadAllText(path))));
        }
        catch (JsonException)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();   // битый JSON — байтами
        }
    }

    private static string Combine(IEnumerable<(string Name, string Hash)> files) =>
        Sha(string.Concat(files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name}={f.Hash}\n")));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Canonical(JsonNode? node) => Sorted(node)?.ToJsonString(Compact) ?? "null";

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray arr => new JsonArray([.. arr.Select(Sorted)]),
        _ => node?.DeepClone(),
    };
}
