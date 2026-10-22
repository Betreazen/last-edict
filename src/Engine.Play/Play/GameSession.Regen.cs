using LastEdict.Data.Map;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Лечение юнита в начале хода: слагаемое укрепления <see cref="FortPercent"/> и слагаемое юнита <see cref="UnitPercent"/>
/// (своя регенерация, Воитель, своя земля), каждое — <c>⌊HPmax·p/100⌋</c> (<c>0x5d4099</c>). <see cref="Group"/> — отряд или
/// укрепление (гарнизон), где стоит юнит; <see cref="HpMax"/> — максимум здоровья (null — тип юнита неизвестен).</summary>
public sealed record RegenTerm(string UnitId, string Group, int FortPercent, int UnitPercent, int? HpMax);

public sealed partial class GameSession
{
    /// <summary>Дневная регенерация игрока (<see cref="Regeneration"/>, <see cref="RegenTerms"/>).</summary>
    private MapState Regenerate(MapState map, string playerId)
    {
        var book = Book!;
        Dictionary<string, MapUnit>? units = null;
        foreach (var term in RegenTerms(map, playerId, book))
        {
            if (!map.Units.TryGetValue(term.UnitId, out var unit) || unit.Hp <= 0) continue;
            if (term.HpMax is not { } max) continue;
            int hp = Regeneration.Tick(unit.Hp, max, term.UnitPercent, term.FortPercent);
            if (hp == unit.Hp) continue;
            units ??= map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            units[term.UnitId] = unit with { Hp = hp };
        }
        return units is null ? map : With(map, units: units);
    }

    /// <summary>Удар конца мятежа, отложенный до конца лечения: юнит и урон (<see cref="Regeneration.RiotDamage"/> от HP до лечения),
    /// гости, чей запас хода обнуляется.</summary>
    private sealed record RiotEnding(IReadOnlyList<(string Unit, int Damage)> Hits, IReadOnlyList<string> Guests);

    /// <summary>Конец мятежей игрока в начале его хода (<c>CEffectRmvRiot</c>, <see cref="Fortifications.EndRiot"/>), первая часть — до
    /// лечения: у деревни с <c>ход ≥ RIOT_T</c> бунт снимается сразу (в этот день она уже лечит), урон <c>RIOT_DMG</c> % считается от
    /// HP до лечения. Вычитает его <see cref="FinishRiotEnds"/> — после лечения и роста земли, перед доходом. Ранг 1:
    /// <c>20261016b-port-synth</c>, карта 2, R1 — <c>regen</c> 15 % и 20 % (80 → 132, 40 → 62), <c>land_grow</c>, затем <c>hp_change</c> 16 и
    /// 8 (20 % от 80 и 40), <c>income</c>; ai-q11, прогон 13.10 — <c>hp_change</c> 1764913/1764915 после <c>regen</c> и <c>land_grow</c>,
    /// удар 7/7, запас 30 → 0. <i>Было иначе</i> (до 16.10): бунт снимался после лечения, и в день сброса деревня не лечила, а удар брался
    /// от HP после лечения.</summary>
    private (MapState Map, RiotEnding? Ending) BeginRiotEnds(MapState map, string playerId)
    {
        var ending = map.Sites.Where(s => s.Kind == SiteKind.Village && Same(s.Owner, playerId)
                                          && s.RiotTurn is { } riot && Fortifications.RiotEndsAt(riot, map.Turn)).ToList();
        if (ending.Count == 0) return (map, null);
        int damage = Book!.Thieves.RiotDamage;
        var hits = new List<(string, int)>();
        var guests = new List<string>();
        foreach (var fort in ending)
        {
            var guest = fort.StackId.Length > 0 ? map.Stacks.GetValueOrDefault(fort.StackId) : null;
            if (guest is not null) guests.Add(guest.Id);
            foreach (string id in fort.Guards.Concat(guest?.UnitIds ?? []))
                if (map.Units.TryGetValue(id, out var unit) && unit.Hp > 0) hits.Add((id, Regeneration.RiotDamage(unit.Hp, damage)));
        }
        var ids = ending.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sites = map.Sites.Select(s => ids.Contains(s.Id) ? Fortifications.EndRiot(s, map.Turn) : s).ToList();
        return (With(map, sites: sites), new RiotEnding(hits, guests));
    }

    /// <summary>Вторая часть конца мятежей (<see cref="BeginRiotEnds"/>): удар по HP после лечения и запас хода гостя — 0.</summary>
    private MapState FinishRiotEnds(MapState map, RiotEnding? ending)
    {
        if (ending is null) return map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, damage) in ending.Hits)
            if (units.TryGetValue(id, out var unit) && unit.Hp > 0) units[id] = unit with { Hp = Math.Max(unit.Hp - damage, 0) };
        foreach (string id in ending.Guests)
            if (stacks.TryGetValue(id, out var guest)) stacks[id] = guest with { Move = 0 };
        return With(map, units: units, stacks: stacks);
    }

    /// <summary>Слагаемые дневного лечения юнитов игрока (<c>CEffectRegenUnits</c>, начало хода; ранг 1 — строки <c>regen</c>
    /// зонда 7.2, probe72, <c>RegenHarnessTests</c>). Два прохода, как у движка:
    /// <list type="bullet">
    /// <item>укрепления игрока (<c>0x5d40d9</c> → <c>0x5d4181</c>): гарнизон (<c>ret</c> <c>0x5d4164</c>) и гость (<c>0x5d4156</c>)
    /// получают процент укрепления — столица <c>Grace.REGEN_H</c>, деревня <c>REGEN_F + REGEN_B</c>;</item>
    /// <item>группы игрока (<c>0x5d41d8</c> → <c>0x5d4588</c>): отряды (<c>0x5d43f6</c>) и <b>гарнизоны</b> (<c>0x5d4519</c>) — процент
    /// юнита: своя регенерация, <c>REGEN_LWAR</c> у Воителя, +10 на своей земле — только отряду в поле.</item>
    /// </list>
    /// Гарнизон прежде не лечился вовсе (probe72: <c>S143UN0019</c> в деревне <c>FT000c</c> 95 → 137 → 170 — 25 % укрепления и 20 %
    /// юнита, <c>S143UN0272</c> в столице 55 → 97 → 120 — 35 % и 20 %).</summary>
    public static IReadOnlyList<RegenTerm> RegenTerms(MapState map, string playerId, GameBook book)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(book);
        var player = map.Players[playerId];
        bool playable = book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable;
        int lordCategory = book.LordCategory(player.LordId);
        RegenTerm Term(string id, string group, int fortPercent, bool ownLand)
        {
            var stats = map.Units.TryGetValue(id, out var unit) ? Stats(unit, book) : null;
            int unitPercent = stats is null ? 0
                : Regeneration.UnitPercent(stats.Regeneration, lordCategory, playable, player.CapturedBy.Length > 0,
                                           book.Vars.WarlordRegen, ownLand);
            return new RegenTerm(id, group, fortPercent, unitPercent, stats?.HitPoints);
        }
        var terms = new List<RegenTerm>();
        foreach (var fort in map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && Same(s.Owner, playerId)))
        {
            if (RiotingVillage(fort)) continue;   // в бунтующей деревне не лечится никто (ai-q11 13.10: гость 72/90 без строк regen, ход 3)
            int percent = FortPercent(map, fort, book);
            terms.AddRange(fort.Guards.Select(id => Term(id, fort.Id, percent, ownLand: false)));
        }
        foreach (var stack in map.Stacks.Values.Where(s => Same(s.Owner, playerId)))
        {
            var fort = stack.Inside.Length == 0 ? null : map.Sites.FirstOrDefault(s => Same(s.Id, stack.Inside));
            if (fort is not null && RiotingVillage(fort)) continue;   // гость бунтующей деревни — ни слагаемого укрепления, ни своего
            int percent = fort is null ? 0 : FortPercent(map, fort, book);
            bool ownLand = stack.Inside.Length == 0 && race is not null && map.Contains(stack.X, stack.Y)
                           && map.At(stack.X, stack.Y).Terrain == race.Terrain;
            terms.AddRange(stack.UnitIds.Select(id => Term(id, stack.Id, percent, ownLand)));
        }
        return terms;
    }

    /// <summary>Лечение охраны руин в начале дня (<c>0x5d463e</c>, <c>ret</c> <c>0x5d4706</c>): <c>⌊HPmax·REGEN_RUIN/100⌋</c> каждому
    /// живому юниту охраны. Ранг 1 на место и процент (probe72: строки <c>regen term = ruin</c>, <c>pct</c> 20 = <c>GVars.REGEN_RUIN</c>, между
    /// концом хода последнего игрока и <c>spell_expire</c> хозяина нового дня); ранг 2 на лечение живых — в партии все строки о
    /// павших (<c>hp</c> 0 → 0).</summary>
    private MapState RegenerateRuins(MapState map)
    {
        var book = Book!;
        int percent = book.Vars.RuinRegen;
        if (percent <= 0) return map;
        Dictionary<string, MapUnit>? units = null;
        foreach (string id in map.Sites.Where(s => s.Kind == SiteKind.Ruin).SelectMany(s => s.Guards))
        {
            if (!map.Units.TryGetValue(id, out var unit) || unit.Hp <= 0 || HpMax(unit, book) is not { } max) continue;
            int hp = Regeneration.Tick(unit.Hp, max, percent, 0);
            if (hp == unit.Hp) continue;
            units ??= map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            units[id] = unit with { Hp = hp };
        }
        return units is null ? map : With(map, units: units);
    }

    private static EffectiveStats? Stats(MapUnit unit, GameBook book)
    {
        if (book.Grown(unit.TypeId, unit.Level) is not { } grown) return null;
        var chain = unit.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        return EffectiveStats.Of(grown.Type, chain, regeneration: grown.Regeneration);
    }

    private static int? HpMax(MapUnit unit, GameBook book) => Stats(unit, book)?.HitPoints;

    /// <summary>Деревня в бунте: <c>RIOT_T &gt; 0</c> до сброса в ход владельца (<see cref="Fortifications.Rioting"/>).</summary>
    private static bool RiotingVillage(MapSite site) => site.Kind == SiteKind.Village && Fortifications.Rioting(site.RiotTurn);

    /// <summary>Процент укрепления: столица — <c>Grace.REGEN_H</c> расы, деревня — по уровню (<c>0x5d4181</c>).</summary>
    private static int FortPercent(MapState map, MapSite site, GameBook book)
    {
        string raceId = map.Players.GetValueOrDefault(site.Owner)?.RaceId ?? "";
        try
        {
            return site.Kind switch
            {
                SiteKind.Capital when book.Economy.Races.TryGetValue(raceId, out var race) => Regeneration.CapitalPercent(race),
                SiteKind.Village => Regeneration.VillagePercent(book.Economy, raceId, site.Size ?? 1, site.RegenBonus ?? 0,
                                                                RiotingVillage(site)),
                _ => 0,
            };
        }
        catch (ArgumentException)
        {
            return 0;   // нет строки GcityInf расы владельца — укрепление не лечит
        }
    }
}
