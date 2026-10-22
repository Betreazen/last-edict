using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Тексты окон из содержимого правил — id строк по ключу сущности: описание и раса воина, имя расы, имя постройки, имя атаки, здание
/// найма воина (только у тех, у кого оно есть), пол воина (T/F). Площадки, карточка воина, окно построек, имя героя.
/// </summary>
public sealed record RulesTexts(IReadOnlyDictionary<string, string> UnitDescs, IReadOnlyDictionary<string, string> RaceNames,
                                IReadOnlyDictionary<string, string> UnitRaces, IReadOnlyDictionary<string, string> BuildingNames,
                                IReadOnlyDictionary<string, string> AttackNames, IReadOnlyDictionary<string, string> EnrollBuildings,
                                IReadOnlyDictionary<string, string> UnitSexes)
{
    /// <summary>Тип героя строкой (<c>Gunits.ABIL_TXT</c>: «Герой», «Вор»…) — последняя строка героя в окне найма (<c>getAbilityName</c>).</summary>
    public IReadOnlyDictionary<string, string> UnitAbilities { get; init; } = new Dictionary<string, string>();

    /// <summary>Сколько ходов живёт призванный (<c>Gunits.LIFE_TIME</c>) — длительность в карточке заклинания призыва.</summary>
    public IReadOnlyDictionary<string, int> UnitLifeTimes { get; init; } = new Dictionary<string, int>();

    public static RulesTexts Load(string rulesDir) => Load(ContentSource.Open(rulesDir));

    public static RulesTexts Load(ContentSet c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(
            Map(c.Units, u => u.DescriptionText),
            Map(c.Races, r => r.NameText),
            Map(c.Units, u => c.KeyOf(u.Race)),
            Map(c.Buildings, b => b.NameText),
            Map(c.Attacks, a => a.NameText),
            Map(c.Units.Where(u => u.HireBuilding is not null), u => c.KeyOf(u.HireBuilding)),
            Map(c.Units, u => u.Male == true ? "T" : "F"))
        {
            UnitAbilities = Map(c.Units, u => u.AbilityText),
            UnitLifeTimes = c.Units.GroupBy(u => u.Key, StringComparer.OrdinalIgnoreCase)
                                   .ToDictionary(g => g.Key, g => g.First().LifetimeDays ?? 0, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static Dictionary<string, string> Map<T>(IEnumerable<T> items, Func<T, string?> value) where T : ContentDef
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) map.TryAdd(item.Key, value(item) ?? "");
        return map;
    }
}
