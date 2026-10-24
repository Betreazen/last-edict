using System.Collections.Frozen;

using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Content;

/// <summary>Вид сущности содержимого: имя в id (<c>base:unit/…</c>), файл слоя (<c>units.json</c>) и запись.</summary>
public sealed record ContentKind(string Name, string File, Type Type);

/// <summary>Содержимое правил: все сущности базы и модов после наложения слоёв (<see cref="ContentLayers"/>), в порядке слоёв (порядок
/// таблиц оригинала у базы, дальше — новые сущности модов). Только чтение.</summary>
public sealed class ContentSet
{
    /// <summary>Все виды, порядок — порядок файлов в слое.</summary>
    public static readonly IReadOnlyList<ContentKind> Kinds =
    [
        new("effect", "effects.json", typeof(EffectDef)),
        new("unit", "units.json", typeof(UnitDef)),
        new("growth", "growth.json", typeof(GrowthDef)),
        new("attack", "attacks.json", typeof(AttackDef)),
        new("modifier", "modifiers.json", typeof(ModifierDef)),
        new("spell", "spells.json", typeof(SpellDef)),
        new("item", "items.json", typeof(ItemDef)),
        new("building", "buildings.json", typeof(BuildingDef)),
        new("building-branch", "building-branches.json", typeof(BuildingBranchDef)),
        new("race", "races.json", typeof(RaceDef)),
        new("city", "cities.json", typeof(CityLevelDef)),
        new("lord", "lords.json", typeof(LordDef)),
        new("leader-skill", "leader-skills.json", typeof(LeaderSkillDef)),
        new("landmark", "landmarks.json", typeof(LandmarkDef)),
        new("thief-action", "thief-actions.json", typeof(ThiefActionDef)),
        new("ai", "ai.json", typeof(AiAttitudeDef)),
        new("globals", "globals.json", typeof(GlobalsDef)),
    ];

    public static ContentKind KindOf(Type type) => Kinds.First(k => k.Type == type);

    public static ContentKind KindNamed(string name) =>
        Kinds.FirstOrDefault(k => k.Name == name) ?? throw new ArgumentException($"нет вида содержимого «{name}»", nameof(name));

    private readonly IReadOnlyDictionary<Type, IReadOnlyList<ContentDef>> _byKind;
    private readonly IReadOnlyDictionary<Type, Array> _typed;
    private readonly FrozenDictionary<string, ContentDef> _byId;

    /// <param name="origin">Откуда сущность (мод и файл) — для сообщений проверки целостности (<see cref="ContentChecks"/>).</param>
    public ContentSet(IReadOnlyDictionary<Type, IReadOnlyList<ContentDef>> byKind, Func<string, string?>? origin = null)
    {
        ArgumentNullException.ThrowIfNull(byKind);
        _byKind = Kinds.ToDictionary(k => k.Type, k => (IReadOnlyList<ContentDef>)(byKind.TryGetValue(k.Type, out var list) ? [.. list] : []));
        _typed = _byKind.ToDictionary(p => p.Key, p =>
        {
            var array = Array.CreateInstance(p.Key, p.Value.Count);
            for (int i = 0; i < p.Value.Count; i++) array.SetValue(p.Value[i], i);
            return array;
        });
        var byId = new Dictionary<string, ContentDef>(StringComparer.Ordinal);
        foreach (var def in _byKind.Values.SelectMany(l => l))
            if (!byId.TryAdd(def.Id, def))
                throw new InvalidDataException($"id «{def.Id}» у двух сущностей");
        _byId = byId.ToFrozenDictionary(StringComparer.Ordinal);
        if (Of<GlobalsDef>().Count != 1)
            throw new InvalidDataException($"глобальных величин должно быть ровно одна запись, а не {Of<GlobalsDef>().Count}");
        ContentChecks.Validate(this, origin);
    }

    public IReadOnlyList<T> Of<T>() where T : ContentDef => (T[])_typed[typeof(T)];

    public IReadOnlyList<ContentDef> Of(Type type) => _byKind[type];

    public IReadOnlyList<EffectDef> Effects => Of<EffectDef>();
    public IReadOnlyList<UnitDef> Units => Of<UnitDef>();
    public IReadOnlyList<GrowthDef> Growth => Of<GrowthDef>();
    public IReadOnlyList<AttackDef> Attacks => Of<AttackDef>();
    public IReadOnlyList<ModifierDef> Modifiers => Of<ModifierDef>();
    public IReadOnlyList<SpellDef> Spells => Of<SpellDef>();
    public IReadOnlyList<ItemDef> Items => Of<ItemDef>();
    public IReadOnlyList<BuildingDef> Buildings => Of<BuildingDef>();
    public IReadOnlyList<BuildingBranchDef> BuildingBranches => Of<BuildingBranchDef>();
    public IReadOnlyList<RaceDef> Races => Of<RaceDef>();
    public IReadOnlyList<CityLevelDef> Cities => Of<CityLevelDef>();
    public IReadOnlyList<LordDef> Lords => Of<LordDef>();
    public IReadOnlyList<LeaderSkillDef> LeaderSkills => Of<LeaderSkillDef>();
    public IReadOnlyList<LandmarkDef> Landmarks => Of<LandmarkDef>();
    public IReadOnlyList<ThiefActionDef> ThiefActions => Of<ThiefActionDef>();
    public IReadOnlyList<AiAttitudeDef> AiAttitudes => Of<AiAttitudeDef>();
    public GlobalsDef Globals => Of<GlobalsDef>()[0];

    /// <summary>Сущность по id; нет — null.</summary>
    public ContentDef? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Ключ правил по ссылке содержимого: id сущности → её <see cref="ContentDef.Key"/>; ссылка на объект оригинала вне
    /// содержимого (<c>legacy:g000rr0006</c>) → id оригинала; пусто — пустая строка. Висячая ссылка — ошибка данных.</summary>
    public string KeyOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return "";
        if (reference.StartsWith(LegacyPrefix, StringComparison.Ordinal)) return reference[LegacyPrefix.Length..];
        return _byId.TryGetValue(reference, out var def) ? def.Key
            : throw new InvalidDataException($"ссылка «{reference}» не ведёт ни к одной сущности");
    }

    /// <summary>Префикс ссылки на объект оригинала, которого нет в содержимом (подрасы нейтралов у городов).</summary>
    public const string LegacyPrefix = "legacy:";
}
