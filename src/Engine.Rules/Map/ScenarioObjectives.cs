namespace LastEdict.Rules.Map;

/// <summary>
/// Цели сценария глазами игрока — объекты, которые движок обводит кругом <c>SYM_OBJECTIVE</c> (картинка <c>OBJECTIVE{размер}</c>).
/// Набор не хранится: движок строит его заново при каждой перерисовке объекта для игрока-зрителя (<c>0x5f1e06</c> из
/// <c>0x5ac86b</c>), поэтому он следует за состоянием событий и карты. Разбор 27.09 статически по <c>Discipl2.exe</c>, ранг 1
/// (<c>docs/scenario-events.md</c> §8).
/// </summary>
public static class ScenarioObjectives
{
    /// <summary>
    /// Id объектов-целей игрока <paramref name="viewerId"/> в порядке событий и их условий, без повторов.
    /// <list type="number">
    /// <item>События-цели (<c>0x5f1d36</c>): включено, раса зрителя в <c>HUMAN…</c>-флагах, последний эффект (по <c>NUM</c>) —
    /// <c>L_WIN</c> с <c>WIN_SCEN</c> 1 и победителем той же расы, что зритель. Нет ни одного — те же события с последним <c>L_WIN</c>
    /// и <c>WIN_SCEN</c> 0 (победителя нет).</item>
    /// <item>Id из условий (слот +8 классов условий): одно поле у <c>L_LOCATION</c>, <c>L_ENTER_CITY</c>, <c>L_OWN_CITY</c>,
    /// <c>L_KILL_STACK</c>, <c>L_OWN_ITEM</c>, <c>L_LOOT_RUIN</c>, <c>L_VISIT_SITE</c>, <c>L_STACK_EXISTS</c>; два — у
    /// <c>L_LEADER_OWN_ITEM</c>, <c>L_LEADER_TO_ZONE</c>, <c>L_LEADER_TO_CITY</c>, <c>L_ITEM_TO_LOCATION</c>, <c>L_DIPLOMACY</c>,
    /// <c>L_ALLIANCE</c>; у <c>L_FREQUENCY</c>, <c>L_TRANSFORM_LAND</c>, <c>L_VAR_IN_RANGE</c> — нет.</item>
    /// <item>Разбор id (<c>0x5f2042</c>): отряд — он сам, а стоящий в крепости — крепость; тип предмета — все его держатели
    /// (<c>0x5f21ce</c>: крепость со складом, торговец с ним в продаже, руина с ним в награду, отряд с ним — или его крепость, мешок);
    /// остальное — как есть.</item>
    /// </list>
    /// Движок кладёт в набор и id, которых на карте нет (игроки, шаблоны, погибшие отряды) — рисовать их некому; здесь они
    /// отброшены: в списке только стоящие на карте объекты тех видов, у которых есть рисовальщик круга (<c>0x5bb76e</c>:
    /// столица, деревня, отряд, мешок, руина, четыре вида площадок, кристалл, жезл, область).
    /// </summary>
    public static IReadOnlyList<string> For(MapState map, ScenarioEventSet set, EventProgress progress, EventRules rules,
                                            string viewerId)
    {
        var world = World(map, set, progress, rules);
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in RawIds(world, set, progress, viewerId, stackInFortIsFort: true))
            if (IsDrawn(id, world) && seen.Add(id)) ids.Add(id);
        return ids;
    }

    /// <summary>
    /// Множество целей тактики ИИ (<c>CAiTactic +0x51dc</c>): вход в тактику (<c>0x46586c</c>) зовёт <c>0x4662d7</c>, тот чистит
    /// множество и, если день не меньше порога сложности партии (<see cref="AiTacticScore.WinTargetsDay"/>), заполняет его тем же
    /// <c>0x5f1e06(карта, игрок, 0, множество)</c>, что и круг целей, но с флагом 0: отряд, прямо названный в условии, остаётся
    /// отрядом, даже стоя в крепости (<c>0x5f213f</c>); держатель предмета — отряд в крепости — по-прежнему крепость (<c>0x5f25ab</c>
    /// флаг не смотрит). Фильтра «есть кому рисовать» нет: игроки, шаблоны и погибшие отряды тоже в множестве (ни с одной целью
    /// кэша они не совпадут). Ранг 1 по листингу; сверено журналом legions (<c>ai_tactic_adjust kind=set</c>: +100 у
    /// <c>AttackCity KC0086 → FT0007</c> в дни 52–56 — склад <c>FT0007</c> держит <c>G000IG7113</c> события победы <c>EV0001</c>).
    /// </summary>
    public static IReadOnlySet<string> AiTargets(MapState map, ScenarioEventSet set, EventProgress progress, EventRules rules,
                                                 string playerId) =>
        RawIds(World(map, set, progress, rules), set, progress, playerId, stackInFortIsFort: false)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static EventWorld World(MapState map, ScenarioEventSet set, EventProgress progress, EventRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(rules);
        return new EventWorld(map, rules, set);
    }

    /// <summary><c>0x5f1e06</c> → <c>0x5f2042</c> без фильтра рисования; <paramref name="stackInFortIsFort"/> — флаг разбора id.</summary>
    private static IEnumerable<string> RawIds(EventWorld world, ScenarioEventSet set, EventProgress progress, string viewerId,
                                              bool stackInFortIsFort)
    {
        if (string.IsNullOrEmpty(viewerId) || world.Race(viewerId) is not { } race) return [];
        var events = set.Events.Where(e => IsGoal(e, progress, world, race, win: true)).ToList();
        if (events.Count == 0) events = [.. set.Events.Where(e => IsGoal(e, progress, world, race, win: false))];
        return events.SelectMany(e => e.Conditions).SelectMany(ReferencedIds)
                     .SelectMany(id => Expand(id, world, stackInFortIsFort)).ToList();
    }

    /// <summary><c>0x5f1d36</c>: событие-цель расы зрителя. Раса победителя сверяется по <c>RACE_TYPE</c> игрока <c>ID_PLAYER1</c>.</summary>
    private static bool IsGoal(ScenarioEvent e, EventProgress progress, EventWorld world, RaceInfo race, bool win) =>
        progress.IsEnabled(e.Id) && ScenarioEvents.RaceFlag(e.Races, race)
        && e.Effects.Count > 0 && e.Effects[^1] is WinEffect w && w.Win == win
        && (!win || world.RaceType(w.PlayerId) == race.RaceType);

    /// <summary>Слот +8 условия (<c>0x6153e6</c> — поле +0x10, <c>0x6157be</c> — +0x10 и +0x14, <c>0x46d59e</c> — ничего).</summary>
    private static IEnumerable<string> ReferencedIds(EventCondition c) => c switch
    {
        ZoneCondition z => [z.LocationId],
        EnterCityCondition e => [e.CityId],
        OwnCityCondition o => [o.CityId],
        KillStackCondition k => [k.StackId],
        OwnItemCondition i => [i.ItemType],
        LootRuinCondition r => [r.RuinId],
        VisitSiteCondition v => [v.SiteId],
        StackExistsCondition s => [s.StackId],
        LeaderOwnItemCondition l => [l.ItemType, l.StackId],
        LeaderToZoneCondition l => [l.StackId, l.LocationId],
        LeaderToCityCondition l => [l.StackId, l.CityId],
        ItemToZoneCondition i => [i.ItemType, i.LocationId],
        DiplomacyCondition d => [d.Player1, d.Player2],
        AllianceCondition a => [a.Player1, a.Player2],
        _ => [],
    };

    /// <summary><c>0x5f2042</c>: отряд (<c>KC</c>, тип 0x23) — его крепость, если он в ней и флаг стоит (<c>0x5f213f</c>), иначе он
    /// сам; тип предмета (<c>IG</c>, 0x0c) — держатели; прочее — как есть.</summary>
    private static IEnumerable<string> Expand(string id, EventWorld world, bool stackInFortIsFort)
    {
        if (string.IsNullOrEmpty(id)) return [];
        if (EventWorld.IsStack(id))
            return stackInFortIsFort && world.Map.Stacks.TryGetValue(id, out var stack) ? [FortOf(stack, world) ?? id] : [id];
        return IsItemType(id) ? Holders(id, world) : [id];
    }

    /// <summary><c>0x5f21ce</c>: держатели предмета по типу в порядке движка — крепости (склад <c>+0x70</c>), торговцы (товар),
    /// руины (награда <c>+0x7c</c>), отряды (инвентарь <c>+0x64</c>; в крепости — крепость), мешки (<c>+0x24</c>).</summary>
    private static IEnumerable<string> Holders(string type, EventWorld world)
    {
        var sites = world.Map.Sites.Where(s => !s.Gone).ToList();
        foreach (var fort in sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && world.Holds(s.Items, type)))
            yield return fort.Id;
        foreach (var merchant in sites.Where(s => s.Kind == SiteKind.Merchant
                                                  && s.Stock.Any(i => i.Item.Equals(type, StringComparison.OrdinalIgnoreCase))))
            yield return merchant.Id;
        foreach (var ruin in sites.Where(s => s.Kind == SiteKind.Ruin && s.ItemId.Equals(type, StringComparison.OrdinalIgnoreCase)))
            yield return ruin.Id;
        foreach (var stack in world.Map.Stacks.Values.Where(s => world.Holds(s.Items.Concat(s.Equipment), type)))
            yield return FortOf(stack, world) ?? stack.Id;
        foreach (var bag in sites.Where(s => s.Kind == SiteKind.Bag && world.Holds(s.Items, type)))
            yield return bag.Id;
    }

    /// <summary>Крепость, в которой стоит отряд (<c>CMidStack +0x94</c>); не в крепости — null.</summary>
    private static string? FortOf(MapStack stack, EventWorld world) =>
        stack.Inside.Length > 0 && world.Site(stack.Inside) is { Kind: SiteKind.Capital or SiteKind.Village } fort ? fort.Id : null;

    /// <summary>Есть ли у объекта рисовальщик круга (<c>0x5bb76e</c>) и стоит ли он на карте. Дорог, ландмарков и могил в
    /// условиях не бывает, у них круга нет. Отряд в крепости круга не получает (<c>0x5bbe1d</c> его не рисует) — сюда он и не
    /// доходит: вместо него крепость.</summary>
    private static bool IsDrawn(string id, EventWorld world) =>
        world.Map.Stacks.TryGetValue(id, out var stack) ? stack.Inside.Length == 0
        : world.Site(id) is { } site ? !site.Gone && site.Kind != SiteKind.Other
        : world.Zone(id) is not null;

    /// <summary>Тип предмета — id содержимого вида <c>item</c> (<c>base:item/…</c>, формат «мод:вид/имя»); у объектов карты id иные.</summary>
    private static bool IsItemType(string id) => id.Contains(":item/", StringComparison.OrdinalIgnoreCase);
}
