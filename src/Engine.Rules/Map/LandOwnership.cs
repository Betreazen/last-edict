namespace LastEdict.Rules.Map;

/// <summary>
/// Земля по владельцу клетки (Last Edict, Э4 «одинаковые расы», <c>docs/own-rules.md</c> §4). У оригинала «своя земля» игрока — клетки рода
/// земли его расы (<see cref="Tile.Terrain"/>, 3 бита клетки): раса на карте одна у одного игрока. В Last Edict раса бывает у двоих —
/// тогда у клеток их рода есть хозяин (<see cref="MapState.LandOwners"/>), и рост, доход жил, лечение, жезлы и ИИ считают землю каждого
/// отдельно; рост одного занимает землю другого так же, как землю чужой расы. Пока у каждой расы не больше одного игрока, слоя нет
/// (<c>null</c>) и всё решает род земли — партии и снимки оригинала те же.
/// <para>Хозяин клетки ставится там, где меняется род земли: рост, захват деревни, жезл (своему игроку), событие и заклинание (игроку
/// этой расы с ближайшим источником роста — столицей, деревней или жезлом). На старте земля рода, общего у нескольких игроков, делится
/// обходом в ширину от их укреплений и жезлов
/// (<see cref="Assign"/>).</para>
/// </summary>
public static class LandOwnership
{
    /// <summary>Есть ли у карты хозяева земли (раса у двоих и больше).</summary>
    public static bool Tracked(MapState map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.LandOwners is not null && map.LandOwnerIds is not null;
    }

    /// <summary>Хозяин клетки; null — слоя нет или у клетки своего хозяина нет.</summary>
    public static string? OwnerAt(MapState map, int x, int y)
    {
        if (!Tracked(map)) return null;
        int slot = map.LandOwners![x, y];
        return slot == 0 || slot > map.LandOwnerIds!.Count ? null : map.LandOwnerIds[slot - 1];
    }

    /// <summary>Своя ли клетка игрока <paramref name="playerId"/> с землёй рода <paramref name="terrain"/>: род тот же и, если у карты есть
    /// хозяева, хозяин — он. Без слоя — ровно правило оригинала (род земли).</summary>
    public static bool IsOwn(MapState map, int x, int y, string playerId, int terrain)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Tiles[x, y].Terrain != terrain) return false;
        if (!Tracked(map)) return true;
        int slot = map.LandOwners![x, y];
        // Клетка без хозяина своя, только если её род — не общий (у общего рода хозяин есть у каждой клетки).
        return slot == 0 ? Index(map.LandOwnerIds!, playerId) < 0 : Same(map.LandOwnerIds![slot - 1], playerId);
    }

    /// <summary>Сторона дипломатии хозяина по номеру слота (индекс — значение <see cref="MapState.LandOwners"/>, 0 — без хозяина): для
    /// проходов по клеткам без поиска игрока на каждой клетке. Слоя нет — пусто.</summary>
    public static int?[] PartyBySlot(MapState map, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!Tracked(map)) return [];
        var parties = new int?[map.LandOwnerIds!.Count + 1];
        for (int i = 0; i < map.LandOwnerIds.Count; i++) parties[i + 1] = Diplomacy.PartyOf(map, rules.Races, map.LandOwnerIds[i]);
        return parties;
    }

    /// <summary>Все свои клетки игрока (<see cref="IsOwn"/>).</summary>
    public static HashSet<(int X, int Y)> OwnCells(MapState map, string playerId, int terrain)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cells = new HashSet<(int X, int Y)>();
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
                if (IsOwn(map, x, y, playerId, terrain)) cells.Add((x, y));
        return cells;
    }

    /// <summary>Игроки, у которых род земли общий: играбельные расы, род — у двоих и больше; по роду, игроки по id.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> SharedTerrains(MapState map, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        return map.Players.Values
            .Where(p => rules.Races.TryGetValue(p.RaceId, out var r) && r.Playable)
            .GroupBy(p => rules.Races[p.RaceId].Terrain)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(p => p.Id).Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Хозяева земли на старте партии (или для сохранения без них): если род земли общий у нескольких игроков, его клетки делятся между
    /// ними обходом в ширину по клеткам этого рода от их источников роста (столица, деревни, жезлы — <see cref="Sources"/>; первыми встают
    /// источники игрока с меньшим id), недостижимые обходом клетки — ближайшему источнику по манхэттенскому расстоянию. Слой уже есть или
    /// общих родов нет — null (карта не меняется).
    /// </summary>
    public static (byte[,] Owners, IReadOnlyList<string> Ids)? Assign(MapState map, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (Tracked(map)) return null;
        var shared = SharedTerrains(map, rules);
        if (shared.Count == 0) return null;
        IReadOnlyList<string> ids = [.. shared.Values.SelectMany(p => p).Order(StringComparer.OrdinalIgnoreCase)];
        if (ids.Count > byte.MaxValue) throw new InvalidOperationException($"игроков общей земли {ids.Count} — больше {byte.MaxValue}");
        var owners = new byte[map.Size, map.Size];
        foreach (var (terrain, players) in shared.OrderBy(p => p.Key))
            Split(map, terrain, [.. players.Select(p => (Slot: (byte)(Index(ids, p) + 1), Sources: Sources(map, p)))], owners);
        return (owners, ids);
    }

    /// <summary>Хозяева клеток, у которых сменился род земли (<paramref name="changed"/>; <paramref name="tiles"/> — новые клетки): клетка
    /// общего рода — игроку <paramref name="claimant"/>, если его род — этот, иначе игроку этого рода с ближайшим источником роста (столица,
    /// деревня, жезл); клетка рода
    /// одного игрока или ничьего — без хозяина. Слоя нет — null.</summary>
    public static byte[,]? Retag(MapState map, Tile[,] tiles, IEnumerable<(int X, int Y)> changed, string? claimant, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(changed);
        if (!Tracked(map)) return null;
        var shared = SharedTerrains(map, rules);
        var owners = (byte[,])map.LandOwners!.Clone();
        var sources = new Dictionary<string, IReadOnlyList<(int X, int Y)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (x, y) in changed)
        {
            int terrain = tiles[x, y].Terrain;
            if (!shared.TryGetValue(terrain, out var players))
            {
                owners[x, y] = 0;
                continue;
            }
            string owner = claimant is not null && players.Any(p => Same(p, claimant))
                ? claimant
                : players.OrderBy(p => Nearest(sources.TryGetValue(p, out var s) ? s : sources[p] = Sources(map, p), x, y)).First();
            int index = Index(map.LandOwnerIds!, owner);
            owners[x, y] = index < 0 ? (byte)0 : (byte)(index + 1);
        }
        return owners;
    }

    /// <summary>Источники роста игрока для дележа: центры столицы и деревень (<see cref="LandModel.FortCentre"/>), клетки жезлов.</summary>
    private static IReadOnlyList<(int X, int Y)> Sources(MapState map, string playerId) =>
    [
        .. map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && Same(s.Owner, playerId))
                    .OrderBy(s => s.Kind == SiteKind.Capital ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal)
                    .Select(s => LandModel.FortCentre(s.X, s.Y)),
        .. map.Rods.Values.Where(r => Same(r.Owner, playerId)).OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => (r.X, r.Y)),
    ];

    /// <summary>Делёж клеток рода <paramref name="terrain"/>: обход в ширину от источников всех игроков сразу (4 соседа), клетка — тому, чей
    /// обход пришёл первым; остальные — ближайшему источнику.</summary>
    private static void Split(MapState map, int terrain, IReadOnlyList<(byte Slot, IReadOnlyList<(int X, int Y)> Sources)> players,
                              byte[,] owners)
    {
        var queue = new Queue<(int X, int Y)>();
        foreach (var (slot, sources) in players)
            foreach (var (x, y) in sources)
            {
                if (!map.Contains(x, y) || map.Tiles[x, y].Terrain != terrain || owners[x, y] != 0) continue;
                owners[x, y] = slot;
                queue.Enqueue((x, y));
            }
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in Four)
            {
                int nx = x + dx, ny = y + dy;
                if (!map.Contains(nx, ny) || owners[nx, ny] != 0 || map.Tiles[nx, ny].Terrain != terrain) continue;
                owners[nx, ny] = owners[x, y];
                queue.Enqueue((nx, ny));
            }
        }
        var withSources = players.Where(p => p.Sources.Count > 0).ToList();
        if (withSources.Count == 0) return;
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
                if (owners[x, y] == 0 && map.Tiles[x, y].Terrain == terrain)
                    owners[x, y] = withSources.OrderBy(p => Nearest(p.Sources, x, y)).First().Slot;
    }

    private static readonly (int Dx, int Dy)[] Four = [(0, 1), (0, -1), (1, 0), (-1, 0)];

    private static int Nearest(IReadOnlyList<(int X, int Y)> sources, int x, int y) =>
        sources.Count == 0 ? int.MaxValue : sources.Min(s => Math.Abs(s.X - x) + Math.Abs(s.Y - y));

    private static int Index(IReadOnlyList<string> ids, string id)
    {
        for (int i = 0; i < ids.Count; i++)
            if (Same(ids[i], id)) return i;
        return -1;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
