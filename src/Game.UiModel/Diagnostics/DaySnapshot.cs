using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Diagnostics;

/// <summary>
/// Снимок мира на начало хода человека — поля строки <c>day</c> журнала зонда (<see cref="PortJournalWatcher"/>): по ним отчёт строит
/// таблицу по дням, а расхождение двух запусков видно с первого дня. Компактно: числа и короткие списки, без юнитов поимённо.
/// <list type="bullet">
/// <item><c>players</c> — по игроку: <c>bank</c> — казна [золото, ад, жизнь, смерть, руны, роща] (<see cref="Bank"/>), <c>spell_bank</c> —
/// второй кошелёк (<see cref="MapPlayer.SpellBank"/>, только непустой), <c>capital</c> — столица (её нет — null), <c>villages</c> —
/// деревни «id → уровень», <c>riot</c> — бунтующие (<see cref="Fortifications.Rioting"/>), <c>stacks</c>, <c>units</c> (в отрядах и
/// гарнизонах, живые), <c>leaders</c> (живые лидеры отрядов), <c>levels</c> (сумма уровней живых), <c>spells</c> (известных),
/// <c>land</c> (клеток своей земли, <see cref="RaceInfo.Terrain"/>).</item>
/// <item><c>relations</c> — «раса-раса → отношение» (<see cref="Relation.Current"/>), <c>allies</c> — пары в союзе.</item>
/// <item><c>rng</c> — генераторы партии: индекс R250 (<see cref="GameSession.Rng"/>) и состояние CRT (<see cref="GameSession.Crt"/>).</item>
/// <item><c>events_off</c> — выключенных событий, <c>quest_log</c> — записей квест-лога (<see cref="EventProgress"/>).</item>
/// </list>
/// </summary>
public static class DaySnapshot
{
    /// <summary>Поля строки <c>day</c> на текущем состоянии <paramref name="session"/>; <paramref name="playerId"/> — чей ход.</summary>
    public static Dictionary<string, object?> Of(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        var players = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in map.Players.Values) players[p.Id] = Player(map, p, session.Book);
        var relations = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var allies = new List<string>();
        foreach (var r in map.Relations)
        {
            string pair = $"{Math.Min(r.Race1, r.Race2)}-{Math.Max(r.Race1, r.Race2)}";
            relations[pair] = r.Current;
            if (r.Ally) allies.Add(pair);
        }
        var progress = session.State.EventProgress;
        return new Dictionary<string, object?>
        {
            ["day"] = map.Turn, ["player"] = playerId, ["players"] = players, ["relations"] = relations, ["allies"] = allies,
            ["rng"] = new Dictionary<string, object?> { ["r250"] = session.BattleRng.Index, ["crt"] = session.Crt.State },
            ["events_off"] = progress.Disabled.Count, ["quest_log"] = progress.Log.Count,
        };
    }

    private static Dictionary<string, object?> Player(MapState map, MapPlayer p, GameBook? book)
    {
        bool Mine(string owner) => owner.Equals(p.Id, StringComparison.OrdinalIgnoreCase);
        var forts = map.Sites.Where(s => !s.Gone && Mine(s.Owner)).ToList();
        var stacks = map.Stacks.Values.Where(s => Mine(s.Owner)).ToList();
        var unitIds = stacks.SelectMany(s => s.UnitIds).Concat(forts.SelectMany(f => f.Guards));
        var alive = unitIds.Select(id => map.Units.GetValueOrDefault(id)).OfType<MapUnit>().Where(u => u.Hp > 0).ToList();
        var fields = new Dictionary<string, object?>
        {
            ["bank"] = Values(p.Bank),
            ["capital"] = forts.FirstOrDefault(f => f.Kind == SiteKind.Capital)?.Id,
            ["villages"] = forts.Where(f => f.Kind == SiteKind.Village).OrderBy(f => f.Id, StringComparer.Ordinal)
                                .ToDictionary(f => f.Id, f => f.Size ?? 0),
            ["riot"] = forts.Where(f => f.Kind == SiteKind.Village && Fortifications.Rioting(f.RiotTurn)).Select(f => f.Id).ToList(),
            ["stacks"] = stacks.Count,
            ["units"] = alive.Count,
            ["leaders"] = stacks.Count(s => s.LeaderAlive),
            ["levels"] = alive.Sum(u => u.Level),
            ["spells"] = p.KnownSpells.Count,
            ["land"] = Land(map, p, book),
        };
        if (p.SpellBank != Bank.Zero) fields["spell_bank"] = Values(p.SpellBank);
        return fields;
    }

    private static long[] Values(Bank b) => [b.Gold, b.Infernal, b.Life, b.Death, b.Runic, b.Grove];

    /// <summary>Клеток земли расы игрока (<see cref="Tile.Terrain"/>); без правил или у расы без своей земли — null.</summary>
    private static int? Land(MapState map, MapPlayer p, GameBook? book)
    {
        // В сохранениях id рас бывают строчными (g000rr0000) — ключ без учёта регистра.
        var race = book?.Economy.Races.FirstOrDefault(r => r.Key.Equals(p.RaceId, StringComparison.OrdinalIgnoreCase)).Value;
        if (race is null) return null;
        int own = race.Terrain;
        int n = 0;
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
                if (map.Tiles[x, y].Terrain == own) n++;
        return n;
    }
}
