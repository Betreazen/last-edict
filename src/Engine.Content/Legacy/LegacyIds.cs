using System.Collections.Frozen;
using System.Text.Json;

namespace LastEdict.Data.Content;

/// <summary>
/// Id оригинала (<c>g000uu0002</c>) ↔ id содержимого (<c>base:unit/knight</c>). Правила, сохранения и сценарии Last Edict живут на id
/// содержимого; id оригинала остаётся там, где говорит игра оригинала: импорт таблиц и <c>.sg</c>, имена графики и звука в импорте
/// (<c>G000UU0002FACE</c>), сценарии и сохранения прежних версий (их переводит <c>LegacyRefs</c> при чтении).
/// <para><see cref="Base"/> — таблица ресурса <c>legacy-ids.json</c> (<c>tools/content_ids.py</c>): по ней импорт называет сущности мода
/// <c>base</c>, поэтому она верна для любой копии игры без чтения содержимого. <see cref="Of(ContentSet)"/> — по набору содержимого: туда
/// попадают и сущности модов оригинала, которых нет в таблице.</para>
/// </summary>
public sealed partial class LegacyIds : IComparer<string>
{
    private readonly FrozenDictionary<string, string> _ids;
    private readonly FrozenDictionary<string, string> _legacy;
    private readonly FrozenDictionary<string, int> _index;

    private LegacyIds(IEnumerable<KeyValuePair<string, string>> pairs, IEnumerable<string>? order = null)
    {
        _index = (order ?? []).Select((id, i) => (id, i)).DistinctBy(p => p.id, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(p => p.id, p => p.i, StringComparer.OrdinalIgnoreCase);
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (old, id) in pairs)
        {
            if (!ids.TryAdd(old, id)) throw new InvalidDataException($"id оригинала «{old}» у двух сущностей: «{ids[old]}» и «{id}»");
            legacy.TryAdd(id, old);
        }
        _ids = ids.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _legacy = legacy.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Сущности оригинала мода <c>base</c> — по таблице ресурса.</summary>
    public static LegacyIds Base { get; } = new(LoadTable());

    /// <summary>Сущности набора содержимого, у которых есть <see cref="Schema.ContentDef.LegacyId"/>.</summary>
    public static LegacyIds Of(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var all = ContentSet.Kinds.SelectMany(k => content.Of(k.Type)).ToList();
        return new(all.Where(d => d.LegacyId is { Length: > 0 }).Select(d => KeyValuePair.Create(d.LegacyId!, d.Id)), all.Select(d => d.Id));
    }

    /// <summary>Id содержимого по id оригинала (регистр не важен); не сущность — null.</summary>
    public string? IdOf(string legacyId) => _ids.GetValueOrDefault(legacyId);

    /// <summary>Id оригинала по id содержимого; у сущности без него (новая сущность мода) — null.</summary>
    public string? LegacyOf(string id) => _legacy.GetValueOrDefault(id);

    /// <summary>Имя графики и звука импорта: у сущности оригинала — её id оригинала, иначе сам ключ. Сущность мода оригинала, которой нет в
    /// таблице, импорт называет <c>base:вид/&lt;id оригинала&gt;</c> (<see cref="LegacyContent"/>) — её имя после «/».</summary>
    public string ArtOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (LegacyOf(key) is { } legacy) return legacy;
        int slash = key.LastIndexOf('/');
        return slash >= 0 && key.Contains(':', StringComparison.Ordinal) && LegacyShape().IsMatch(key.AsSpan(slash + 1)) ? key[(slash + 1)..] : key;
    }

    /// <summary>Вид id оригинала: <c>g000uu0002</c> — категория, номер, тип из двух букв, номер из четырёх.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][0-9a-f]{3}[a-z]{2}[0-9a-f]{4}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LegacyShape();

    /// <summary>Порядок, в котором движок оригинала перебирал сущности по id: сущности оригинала — по id оригинала (без учёта регистра:
    /// он же порядок id заглавными), за ними новые сущности модов — в порядке содержимого (у набора без содержимого — по id).</summary>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null || y is null) return x is null ? -1 : 1;
        string? lx = LegacyOf(x), ly = LegacyOf(y);
        if (lx is not null || ly is not null)
            return lx is null ? 1 : ly is null ? -1 : StringComparer.OrdinalIgnoreCase.Compare(lx, ly);
        int ix = _index.GetValueOrDefault(x, int.MaxValue), iy = _index.GetValueOrDefault(y, int.MaxValue);
        return ix != iy ? ix.CompareTo(iy) : StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }

    /// <summary>Таблица <c>legacy-ids.json</c> — она же у импорта таблиц (<see cref="LegacyContent"/>).</summary>
    internal static Dictionary<string, string> LoadTable()
    {
        using var stream = typeof(LegacyIds).Assembly.GetManifestResourceStream("LastEdict.Data.Content.Legacy.legacy-ids.json")
                           ?? throw new InvalidOperationException("нет ресурса legacy-ids.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
