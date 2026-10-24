namespace LastEdict.Rules.Map;

/// <summary>Карта глазами событий: игроки с расами, области, отряды по шаблону.</summary>
internal sealed class EventWorld(MapState map, EventRules rules, ScenarioEventSet set)
{
    public MapState Map { get; } = map;

    public EventRules Rules { get; } = rules;

    public ScenarioEventSet Set { get; } = set;

    public int? RaceType(string playerId) => Race(playerId)?.RaceType;

    /// <summary>Сторона дипломатии игрока (<see cref="Diplomacy.PartyOf(MapState, IReadOnlyDictionary{string, RaceInfo}, string)"/>).</summary>
    public int? Party(string playerId) => Diplomacy.PartyOf(Map, Rules.Races, playerId);

    /// <summary>Раса игрока из справочника; игрока или расы нет — null.</summary>
    public RaceInfo? Race(string playerId) =>
        Map.Players.TryGetValue(playerId, out var p) && Rules.Races.TryGetValue(p.RaceId, out var r) ? r : null;

    /// <summary><c>AffectsPlayer</c> (<c>0x444b33</c>): раса игрока есть в <c>HUMAN…</c>-флагах события (или в id рас, и игрок — среди игроков
    /// события, <see cref="ScenarioEvents.ForPlayer"/>). Игрока нет — ложь.</summary>
    public bool Affects(ScenarioEvent e, string playerId) =>
        Map.Players.TryGetValue(playerId, out var player) && Race(playerId) is { } race && ScenarioEvents.ForPlayer(e, player, race);

    /// <summary>Нейтральный игрок (раса с признаком <see cref="RaceInfo.Neutral"/>) — убийца «никто» у <c>L_KILL_STACK</c>.</summary>
    public string NeutralPlayer =>
        Map.Players.Values.FirstOrDefault(p => Rules.Races.TryGetValue(p.RaceId, out var r) && r.Neutral)?.Id ?? "";

    public EventZone? Zone(string id) => Set.Zones.GetValueOrDefault(id);

    /// <summary>Отряд стоит в квадрате области <c>[x−R, x+R]×[y−R, y+R]</c> и не в городе (<c>0x444a98</c>, <c>0x5ffc15</c>).</summary>
    public bool InZone(MapStack stack, string zoneId) =>
        stack.Inside.Length == 0 && Zone(zoneId) is { } z
        && Math.Abs(stack.X - z.X) <= z.Radius && Math.Abs(stack.Y - z.Y) <= z.Radius;

    /// <summary>Отряд или шаблон — по типу id (<c>0x57f3b6</c>: <c>KC</c> — отряд, иначе — шаблон), ранг 1: отряд — он сам,
    /// если он есть; шаблон — все отряды с этим <c>SRCTMPL_ID</c>.</summary>
    public IEnumerable<MapStack> StacksOf(string id) =>
        IsStack(id) ? (Map.Stacks.TryGetValue(id, out var s) ? [s] : [])
        : Map.Stacks.Values.Where(x => x.TemplateId.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Id отряда: класс <c>KC</c> в позиции 4–5 (<c>S001KC0001</c>).</summary>
    public static bool IsStack(string id) => id.Length >= 6 && id.AsSpan(4, 2).Equals("KC", StringComparison.OrdinalIgnoreCase);

    public MapSite? Site(string id) => Map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Тип экземпляра предмета (<c>IM</c> → <c>G000IG…</c>).</summary>
    public string ItemType(string instance) => Map.ItemTypes.GetValueOrDefault(instance) ?? instance;

    public bool Holds(IEnumerable<string> instances, string type) =>
        instances.Any(i => i.Length > 0 && ItemType(i).Equals(type, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Условия событий по тестам движка (<c>docs/scenario-events.md</c> §2, фабрика <c>0x441f1c</c>). Ранг 1, где не
/// сказано иначе.</summary>
public static class EventConditions
{
    internal static bool Holds(EventCondition condition, ScenarioEvent e, EventWorld w, EventProgress progress,
                               string candidate, bool samePlayer, MapStack? triggerer) => condition switch
    {
        // 0x444993: только в начале хода самого кандидата, дни с 1. Период 0 движок делит на ноль — здесь ложь.
        FrequencyCondition f => samePlayer && f.Frequency > 0 && w.Map.Turn % f.Frequency == 0,
        // 0x444a26: отряд-триггерер стоит в области (а не «вошёл»), не в городе, и событие затрагивает его владельца.
        ZoneCondition z => triggerer is not null && w.Affects(e, triggerer.Owner) && w.InZone(triggerer, z.LocationId),
        // 0x444e23: в городе стоит отряд, событие затрагивает владельца города; триггерер и кандидат не смотрятся.
        EnterCityCondition c => w.Site(c.CityId) is { } city && city.StackId.Length > 0 && w.Affects(e, city.Owner),
        OwnCityCondition c => OwnsCity(c, e, w),
        // 0x4450d1: запись CMidStackDestroyed с этим отрядом или шаблоном; пустой убийца — нейтрал.
        KillStackCondition k => w.Map.Destroyed.Any(d => (d.StackId.Equals(k.StackId, StringComparison.OrdinalIgnoreCase)
                                                        || d.TemplateId.Equals(k.StackId, StringComparison.OrdinalIgnoreCase))
                                                       && w.Affects(e, d.KillerId.Length > 0 ? d.KillerId : w.NeutralPlayer)),
        OwnItemCondition i => OwnsItem(i.ItemType, e, w),
        // 0x445359: у отряда или экземпляра шаблона — предмет типа (инвентарь или снаряжение).
        LeaderOwnItemCondition l => w.StacksOf(l.StackId).Any(s => w.Holds(s.Items.Concat(s.Equipment), l.ItemType)),
        DiplomacyCondition d => SameBand(d, w),
        // 0x44567b → 0x5f7ebd (ранг 1): пара игроков самого условия (кандидат и триггерер не смотрятся), оба не нейтральной расы, разные,
        // бит союза (бит 0) у пары рас; PERMALLI не смотрится.
        AllianceCondition a => w.Party(a.Player1) is { } r1 && w.Party(a.Player2) is { } r2
                               && Diplomacy.AreAllies(w.Map.Relations, r1, r2),
        // 0x44570e: у руины есть разграбивший, и событие его затрагивает.
        LootRuinCondition r => w.Site(r.RuinId) is { LooterId.Length: > 0 } ruin && w.Affects(e, ruin.LooterId),
        TransformLandCondition t => LandShare(t, e, w),
        // 0x445978: среди посетивших объект — игрок расы из HUMAN…-флагов.
        VisitSiteCondition v => w.Site(v.SiteId) is { } site && site.Visitors.Any(p => w.Affects(e, p)),
        LeaderToZoneCondition l => triggerer is not null && LeaderInZone(l, w, candidate),
        // 0x444ed9: отряд в городе — этот отряд или экземпляр шаблона; расы не смотрятся.
        LeaderToCityCondition l => w.Site(l.CityId) is { StackId.Length: > 0 } city
                                   && (city.StackId.Equals(l.StackId, StringComparison.OrdinalIgnoreCase)
                                       || (!EventWorld.IsStack(l.StackId) && w.Map.Stacks.TryGetValue(city.StackId, out var inside)
                                           && inside.TemplateId.Equals(l.StackId, StringComparison.OrdinalIgnoreCase))),
        // 0x445a9f: у триггерера в области (не в городе) предмет типа в инвентаре или снаряжении.
        ItemToZoneCondition i => triggerer is not null && w.Affects(e, triggerer.Owner) && w.InZone(triggerer, i.LocationId)
                                 && w.Holds(triggerer.Items.Concat(triggerer.Equipment), i.ItemType),
        StackExistsCondition s => w.StacksOf(s.StackId).Any() == (s.Mode == 0),
        VariableInRangeCondition v => VariablesInRange(v, progress),
        _ => false,
    };

    /// <summary>0x444fd5: деревня — событие затрагивает её владельца; столица — только взятая (владелец захвачен), и событие
    /// затрагивает захватчика.</summary>
    private static bool OwnsCity(OwnCityCondition c, ScenarioEvent e, EventWorld w)
    {
        if (w.Site(c.CityId) is not { } city) return false;
        if (city.Kind != SiteKind.Capital) return w.Affects(e, city.Owner);
        return w.Map.Players.TryGetValue(city.Owner, out var owner) && owner.CapturedBy.Length > 0 && w.Affects(e, owner.CapturedBy);
    }

    /// <summary>0x44524e: предмет типа в инвентаре или снаряжении любого отряда или на складе укрепления, и событие затрагивает
    /// хоть одного владельца. Сумки на земле не считаются.</summary>
    private static bool OwnsItem(string type, ScenarioEvent e, EventWorld w) =>
        w.Map.Stacks.Values.Any(s => w.Holds(s.Items.Concat(s.Equipment), type) && w.Affects(e, s.Owner))
        || w.Map.Sites.Any(s => s.Kind is SiteKind.Capital or SiteKind.Village && w.Holds(s.Items, type) && w.Affects(e, s.Owner));

    /// <summary>0x4454fe: отношение игроков и значение условия в одной полосе — война (≤ <c>D_WAR</c>), нейтралитет
    /// (≤ <c>D_NEUTRAL</c>), мир.</summary>
    private static bool SameBand(DiplomacyCondition d, EventWorld w)
    {
        if (w.Party(d.Player1) is not { } r1 || w.Party(d.Player2) is not { } r2) return false;
        int Band(int v) => v <= w.Rules.Diplomacy.War ? 0 : v <= w.Rules.Diplomacy.Neutral ? 1 : 2;
        return Band(Diplomacy.Current(w.Map.Relations, r1, r2)) == Band(d.Relation);
    }

    /// <summary>0x4457c3: земля какой-нибудь играбельной расы из <c>HUMAN…</c>-флагов — не меньше <c>равнина·pct/100</c>
    /// (равнина — клетки с почвой «равнина», <c>0x5f33c9</c>, <c>0x5838c0</c>).</summary>
    private static bool LandShare(TransformLandCondition t, ScenarioEvent e, EventWorld w)
    {
        var counts = new int[8];
        int plain = 0;
        for (int x = 0; x < w.Map.Size; x++)
            for (int y = 0; y < w.Map.Size; y++)
            {
                var tile = w.Map.Tiles[x, y];
                if (tile.Ground == Ground.Plain) plain++;
                counts[tile.Terrain]++;
            }
        long need = (long)plain * t.Percent / 100;
        return w.Rules.Races.Values.Where(r => r.Playable && ScenarioEvents.ForRace(e, r))
                                   .Any(r => counts[r.Terrain] >= need);
    }

    /// <summary>0x444c48: id отряда — этот отряд в области и не в городе; id шаблона — отряд кандидата из шаблона в области.</summary>
    private static bool LeaderInZone(LeaderToZoneCondition l, EventWorld w, string candidate)
    {
        if (EventWorld.IsStack(l.StackId)) return w.Map.Stacks.TryGetValue(l.StackId, out var stack) && w.InZone(stack, l.LocationId);
        return w.Map.Stacks.Values.Any(s => s.TemplateId.Equals(l.StackId, StringComparison.OrdinalIgnoreCase)
                                            && s.Owner.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                                            && w.InZone(s, l.LocationId));
    }

    /// <summary>0x445e3d: переменные в пределах, границы включительно. Раскладка <c>MISC_INT…MISC_INT7</c>: переменная 1, её
    /// нижняя и верхняя граница, переменная 2, её границы, режим (1 — обе, 2 — любая, иначе — только первая). Ранг 1 (поток
    /// <c>0x614f8d</c>, тест <c>0x445e3d</c>). Нет переменной — значение 0 (у движка второй поиск без переменной падает).</summary>
    private static bool VariablesInRange(VariableInRangeCondition v, EventProgress progress)
    {
        if (v.Values.Count < 7) return false;
        bool In(int id, int lo, int hi) => progress.Variables.GetValueOrDefault(id) is var x && x >= lo && x <= hi;
        bool first = In(v.Values[0], v.Values[1], v.Values[2]), second = In(v.Values[3], v.Values[4], v.Values[5]);
        return v.Values[6] switch { 1 => first && second, 2 => first || second, _ => first };
    }
}
