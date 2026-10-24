using System.Text.Json;
using System.Text.Json.Nodes;

using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Content;

/// <summary>Что слой мода сделал с сущностью: добавил, поправил поля, убрал.</summary>
public enum LayerAction { Added, Patched, Removed }

/// <summary>Строка отчёта наложения: мод <see cref="Mod"/> сделал <see cref="Action"/> с сущностью <see cref="Id"/>; у правки — какие
/// поля верхнего уровня он задал, и чей слой правил эту сущность перед ним (<see cref="Over"/>).</summary>
public sealed record LayerChange(string Mod, string Kind, string Id, LayerAction Action, IReadOnlyList<string> Fields, string? Over);

/// <summary>
/// Слои содержимого: база (каталог мода <c>base</c>, его выпускает импорт таблиц оригинала) и моды поверх, каждый — каталог с
/// <c>mod.json</c> и файлами видов (<c>content/units.json</c> у мода, <c>units.json</c> у базы). Сущность опознаётся по id:
/// <list type="bullet">
/// <item>нового id ещё нет — сущность добавляется в конец своего вида (полная, проверяется схемой); id новой сущности обязан быть в
/// пространстве имён самого мода (<c>mymod:…</c>) — правка чужой сущности, которой нет (опечатка, её убрал другой мод), — ошибка, а не
/// пустышка;</item>
/// <item>id уже есть — правка по полям: объекты сливаются вглубь, списки и значения заменяются целиком, <c>null</c> убирает поле
/// (как JSON Merge Patch, RFC 7396);</item>
/// <item><c>"$remove": true</c> — сущность убирается (только логическое значение); <c>legacyId</c> правкой не меняется — по нему сущность
/// находят сохранения и журналы.</item>
/// </list>
/// Порядок модов — по <c>dependencies</c> и <c>loadAfter</c> из <c>mod.json</c> (<see cref="Order"/>), при равенстве — как перечислены.
/// Итог — <see cref="ContentSet"/> и отчёт <see cref="LayerChange"/>: кто что добавил, поправил и чью правку перекрыл.
/// </summary>
public static class ContentLayers
{
    /// <summary>Каталог содержимого слоя: у базы — сам каталог, у мода — его <c>content/</c>.</summary>
    public static string ContentDir(ModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        string inner = Path.Combine(mod.Dir, "content");
        return Directory.Exists(inner) ? inner : mod.Dir;
    }

    public static ContentSet Load(string baseDir, IReadOnlyList<ModInfo> mods) => Load(baseDir, mods, out _);

    public static ContentSet Load(string baseDir, IReadOnlyList<ModInfo> mods, out IReadOnlyList<LayerChange> report)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDir);
        ArgumentNullException.ThrowIfNull(mods);
        var changes = new List<LayerChange>();
        var from = new Dictionary<string, string>(StringComparer.Ordinal);
        var byKind = new Dictionary<Type, IReadOnlyList<ContentDef>>();
        var ordered = Order(mods);
        foreach (var kind in ContentSet.Kinds)
        {
            var nodes = new List<JsonObject?>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            string basePath = Path.Combine(baseDir, kind.File);
            if (File.Exists(basePath))
                foreach (var node in ContentJson.ReadNodes(kind, File.ReadAllText(basePath), basePath))
                {
                    string id = IdOf(node, basePath);
                    if (!index.TryAdd(id, nodes.Count)) throw new InvalidDataException($"{basePath}: id «{id}» дважды");
                    nodes.Add(node);
                    owner[id] = "base";
                    from[id] = $"база, {kind.File}";
                }
            foreach (var mod in ordered)
            {
                string path = Path.Combine(ContentDir(mod), kind.File);
                if (File.Exists(path))
                    foreach (var node in ContentJson.ReadNodes(kind, File.ReadAllText(path), path))
                    {
                        Apply(kind, mod, node, path, nodes, index, owner, changes);
                        string id = IdOf(node, path);
                        from[id] = nodes[index[id]] is null ? $"её убрал мод {mod.Id}, content/{kind.File}" : $"мод {mod.Id}, content/{kind.File}";
                    }
                // «Папка = сущность» (EntityFolders): каталоги content/<вид>/<имя>/entity.json — после файла вида, по имени каталога.
                foreach (var folder in EntityFolders.Scan(mod, kind))
                {
                    var node = EntityFolders.Read(folder);
                    Apply(kind, mod, node, folder.EntityPath, nodes, index, owner, changes);
                    string where = $"{Path.GetFileNameWithoutExtension(kind.File)}/{folder.Name}/{EntityFolders.EntityFile}";
                    from[folder.Id] = nodes[index[folder.Id]] is null ? $"её убрал мод {mod.Id}, content/{where}" : $"мод {mod.Id}, content/{where}";
                }
            }
            byKind[kind.Type] = [.. nodes.OfType<JsonObject>().Select(n => ContentJson.ToDef(kind, n, $"«{n["id"]}» ({from[n["id"]!.GetValue<string>()]})"))];
        }
        report = changes;
        return new ContentSet(byKind, id => from.GetValueOrDefault(id));
    }

    private static void Apply(ContentKind kind, ModInfo mod, JsonObject node, string path, List<JsonObject?> nodes,
                              Dictionary<string, int> index, Dictionary<string, string> owner, List<LayerChange> changes)
    {
        string id = IdOf(node, path);
        bool remove = false;
        if (node.TryGetPropertyValue("$remove", out var flag))
            remove = flag?.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException($"{path}: «{id}»: $remove — только true или false"),
            };
        if (!index.TryGetValue(id, out int at) || nodes[at] is null)
        {
            if (remove) throw new InvalidDataException($"{path}: убрать «{id}» нельзя — такой сущности нет");
            if (!id.StartsWith(mod.Id + ":", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{path}: «{id}» — такой сущности нет (опечатка или её убрал другой мод), а новая сущность мода «{mod.Id}» " +
                    $"должна быть в его пространстве имён: «{mod.Id}:{kind.Name}/…»");
            index[id] = nodes.Count;
            var added = (JsonObject)node.DeepClone();
            added.Remove("$remove");
            nodes.Add(added);
            owner[id] = mod.Id;
            changes.Add(new LayerChange(mod.Id, kind.Name, id, LayerAction.Added, [], null));
            return;
        }
        string over = owner[id];
        owner[id] = mod.Id;
        if (remove)
        {
            nodes[at] = null;
            changes.Add(new LayerChange(mod.Id, kind.Name, id, LayerAction.Removed, [], over));
            return;
        }
        var fields = node.Select(p => p.Key).Where(k => k is not ("id" or "$remove")).ToList();
        if (node.ContainsKey("legacyId"))
            throw new InvalidDataException($"{path}: «{id}»: legacyId правкой не меняется — по нему сущность находят сохранения и журналы");
        Merge(nodes[at]!, node);
        changes.Add(new LayerChange(mod.Id, kind.Name, id, LayerAction.Patched, fields, over));
    }

    /// <summary>JSON Merge Patch (RFC 7396): объект сливается вглубь, остальное заменяется, null убирает поле.</summary>
    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (key is "id" or "$remove") continue;
            if (value is null) { target.Remove(key); continue; }
            if (value is JsonObject inner && target[key] is JsonObject existing) { Merge(existing, inner); continue; }
            target[key] = WithoutNulls(value.DeepClone());
        }
    }

    /// <summary>Новое значение поля: у объектов — без полей <c>null</c> (по RFC 7396 null в правке значит «убрать», а не «записать null»).</summary>
    private static JsonNode WithoutNulls(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var key in obj.Where(p => p.Value is null).Select(p => p.Key).ToList()) obj.Remove(key);
        if (node is JsonObject o)
            foreach (var (_, value) in o)
                if (value is JsonObject) WithoutNulls(value);
        return node;
    }

    private static string IdOf(JsonObject node, string path) =>
        node["id"]?.GetValueKind() == JsonValueKind.String ? node["id"]!.GetValue<string>()
            : throw new InvalidDataException($"{path}: у сущности нет id");

    /// <summary>Порядок слоёв: мод идёт после своих <c>dependencies</c> и <c>loadAfter</c>; при прочих равных — как перечислены.
    /// Нет обязательной зависимости — ошибка с её именем; цикл — ошибка с участниками.</summary>
    public static IReadOnlyList<ModInfo> Order(IReadOnlyList<ModInfo> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        var byId = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
            if (!byId.TryAdd(mod.Id, mod)) throw new InvalidDataException($"мод «{mod.Id}» подключён дважды ({mod.Dir})");
        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (!byId.ContainsKey(dep) && !dep.Equals("base", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"мод «{mod.Id}» требует мод «{dep}», а его нет");
        var done = new List<ModInfo>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = mods.ToList();
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(m => m.Dependencies.Concat(m.LoadAfter)
                .All(d => placed.Contains(d) || !byId.ContainsKey(d)))
                ?? throw new InvalidDataException("моды ждут друг друга по кругу: " + string.Join(", ", pending.Select(m => m.Id)));
            done.Add(next);
            placed.Add(next.Id);
            pending.Remove(next);
        }
        return done;
    }
}
