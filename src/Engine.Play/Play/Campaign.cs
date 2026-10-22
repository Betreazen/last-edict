using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Партия — глава кампании (<c>docs/day-cycle.md</c> §15): сага из каталога или своя кампания, номер главы с 0 и число
/// глав. У движка сага узнаётся по <c>campaignId</c> заголовка сохранения, своя кампания — по <c>CMidgard</c> <c>+0xDC</c> (путь),
/// <c>+0xEC</c> (индекс), <c>+0xF0</c> (число глав).</summary>
public sealed record CampaignRun(string CampaignId, int Chapter, int Chapters, bool Custom = false)
{
    /// <summary>Глава последняя: после победы — вывоз героя и конец кампании, а не перенос в следующую (<c>0x42128f</c>).</summary>
    public bool LastChapter => Chapter >= Chapters - 1;

    public CampaignRun Next() => this with { Chapter = Chapter + 1 };
}

/// <summary>Перенос в следующую главу (<c>CNextScenarioMsg</c> → сборка <c>0x42eecd</c>): герой — снимок юнита без надетых предметов
/// (<c>CVisitorUnEquipItem</c>), его имя, отложенные навыки (<c>UPGCOUNT</c>) и победы отряда (<c>NBBATTLE</c>); выбранные вещи — только
/// типы (до пяти). Казна, другие отряды и города не переносятся.</summary>
public sealed record CarryOver
{
    public MapUnit? Hero { get; init; }
    public string HeroName { get; init; } = "";
    public int UpgradeCount { get; init; }
    public int BattlesWon { get; init; }
    public IReadOnlyList<string> ItemTypes { get; init; } = [];

    /// <summary>Сколько вещей можно взять (<c>LBOX_ITEMS_KEEP</c>).</summary>
    public const int MaxItems = 5;
}

public sealed partial class GameSession
{
    /// <summary>Перенос героя отряда <paramref name="heroStackId"/> (пусто — без героя) и вещей-экземпляров <paramref name="itemIds"/>
    /// игрока в следующую главу: снимок героя без модификаторов надетых предметов, типы вещей.</summary>
    public CarryOver CarryOverOf(string heroStackId, IReadOnlyList<string> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var book = RequireBook();
        var types = itemIds.Take(CarryOver.MaxItems).Select(i => Map.ItemTypes.GetValueOrDefault(i, "")).Where(t => t.Length > 0).ToList();
        if (heroStackId.Length == 0 || !Map.Stacks.TryGetValue(heroStackId, out var stack) || !Map.Units.TryGetValue(stack.LeaderId, out var leader))
            return new CarryOver { ItemTypes = types };
        var worn = stack.Equipment.Where(e => e.Length > 0 && Map.ItemTypes.ContainsKey(e))
                                  .Select(e => book.Items.GetValueOrDefault(Map.ItemTypes[e])?.EquipModifierId ?? "")
                                  .Where(m => m.Length > 0).ToList();
        var modifiers = leader.Modifiers.ToList();
        foreach (string m in worn)
        {
            int at = modifiers.FindIndex(x => x.Equals(m, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) modifiers.RemoveAt(at);
        }
        return new CarryOver
        {
            Hero = leader with { Modifiers = modifiers },
            HeroName = Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "",
            UpgradeCount = stack.UpgradeCount,
            BattlesWon = stack.BattlesWon,
            ItemTypes = types,
        };
    }

    /// <summary>Вещи игрока для окна переноса (<c>0x4fa273</c>, <c>0x4fa502</c>): склады всех его городов и сумки и снаряжение всех его
    /// отрядов, по типу (порядок — первое появление).</summary>
    public IReadOnlyList<string> CarryItems(string playerId)
    {
        var sites = Map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                             .SelectMany(s => s.Items);
        var stacks = Map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                                      .SelectMany(s => s.Items.Concat(s.Equipment.Where(e => e.Length > 0)));
        var all = sites.Concat(stacks).Where(Map.ItemTypes.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return [.. all.GroupBy(i => Map.ItemTypes[i], StringComparer.OrdinalIgnoreCase).SelectMany(g => g)];
    }
}
