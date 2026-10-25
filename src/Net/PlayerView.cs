using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Net;

/// <summary>
/// Что видит игрок сетевой партии (<c>docs/network.md</c> §2, <c>docs/rust-web/03-contracts-and-multiplayer.md</c> §7): партия хоста, из
/// которой убрано всё, чего игрок не знает. Хост раздаёт каждому только это — полной партии у клиента нет. Проекция — тоже
/// <see cref="GameState"/>: клиент держит её в своей сессии, и окна, карта и бой читают её как обычную партию.
/// <para>Убирается разрешительным списком: чужие отряды под туманом и невидимые (с их юнитами, предметами, приказами), содержимое
/// крепостей, руин и лавок под туманом, мешки, жезлы и могилы под туманом; чужие казна, второй кошелёк, заклинания, постройки, разведка и
/// туман; генераторы случайных чисел, память ИИ, приказы ИИ, чужие предложения, попапы, итоги хода и записи квест-лога; чужие наложенные
/// заклинания и применения предметов, чужие убийства отрядов.</para>
/// <para>Остаётся то, что известно и так: карта и её постоянные объекты (сценарий есть у каждого, §7 контрактов), события сценария,
/// дипломатия, игроки и их расы, свои отряды и всё видимое — целиком.</para>
/// </summary>
public static class PlayerView
{
    /// <summary>Проекция партии <paramref name="state"/> для игрока <paramref name="viewer"/>.</summary>
    /// <exception cref="ArgumentException">Нет такого игрока.</exception>
    public static GameState Of(GameState state, string viewer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrEmpty(viewer);
        var map = state.Looks.Map;
        if (!map.Players.TryGetValue(viewer, out var me)) throw new ArgumentException($"в партии нет игрока «{viewer}»", nameof(viewer));
        var hidden = map.Fog.GetValueOrDefault(me.FogId);
        bool Sees(int x, int y) => hidden is null || !map.Contains(x, y) || !hidden[x, y];
        bool Mine(string owner) => owner.Equals(viewer, StringComparison.OrdinalIgnoreCase);

        var invisible = state.Looks.Stacks.Where(s => s.Invisible).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stacks = new Dictionary<string, MapStack>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, stack) in map.Stacks)
        {
            if (Mine(stack.Owner)) stacks[id] = stack;
            else if (Sees(stack.X, stack.Y) && !invisible.Contains(id))
                // Видимый чужой отряд: юниты и снаряжение (оно в бою) — да; сумка, приказы — нет.
                stacks[id] = stack with { Items = [], Order = 0, OrderTarget = "", AiOrder = 0, AiOrderTarget = "", AiIgnore = false };
        }

        var sites = map.Sites.Where(s => s.Kind != SiteKind.Bag || Sees(s.X, s.Y)).Select(s => Sees(s.X, s.Y) || Mine(s.Owner) ? s : Veiled(s)).ToList();

        var units = new Dictionary<string, MapUnit>(StringComparer.OrdinalIgnoreCase);
        void Keep(IEnumerable<string> ids)
        {
            foreach (string id in ids)
                if (id.Length > 0 && map.Units.TryGetValue(id, out var unit)) units[id] = unit;
        }
        foreach (var stack in stacks.Values) Keep([stack.LeaderId, .. stack.UnitIds]);
        foreach (var site in sites) Keep(site.Guards);

        var players = map.Players.ToDictionary(p => p.Key, p => Mine(p.Key) ? p.Value : Stranger(p.Value), StringComparer.OrdinalIgnoreCase);
        var fog = map.Fog.ToDictionary(p => p.Key, p => p.Key.Equals(me.FogId, StringComparison.OrdinalIgnoreCase) ? p.Value : Shroud(map.Size),
                                       StringComparer.OrdinalIgnoreCase);
        var rods = map.Rods.Where(r => Mine(r.Value.Owner) || Sees(r.Value.X, r.Value.Y))
                           .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
        var tombs = map.Tombs.Where(t => Sees(t.X, t.Y)).ToList();

        // Занятость клеток — только то, что осталось в проекции (отряды, мешки, жезлы, могилы — по id объекта клетки).
        var gone = map.Stacks.Keys.Where(k => !stacks.ContainsKey(k))
                      .Concat(map.Sites.Where(s => s.Kind == SiteKind.Bag && !Sees(s.X, s.Y)).Select(s => s.Id))
                      .Concat(map.Rods.Keys.Where(k => !rods.ContainsKey(k)))
                      .Concat(map.Tombs.Where(t => !Sees(t.X, t.Y)).Select(t => t.Id))
                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>();
        foreach (var (cell, ids) in map.Plan)
        {
            var left = gone.Count == 0 ? ids : ids.Where(id => !gone.Contains(id)).ToList();
            if (left.Count > 0) plan[cell] = left;
        }

        var keptUnits = units.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projected = new MapState
        {
            Size = map.Size, Tiles = map.Tiles, Stacks = stacks, Units = units, Players = players,
            Objects = map.Objects.Where(o => !gone.Contains(o.Key)).ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase),
            Plan = plan, Fog = fog, Turn = map.Turn, Rods = rods, Sites = sites, Relations = map.Relations,
            TalismanCharges = map.TalismanCharges, SpellCasts = [.. map.SpellCasts.Where(c => Mine(c.PlayerId))],
            ItemCasts = [.. map.ItemCasts.Where(c => Mine(c.PlayerId))],
            SpellEffects = [.. map.SpellEffects.Where(e => keptUnits.Contains(e.UnitId))],
            ItemTypes = map.ItemTypes, Limits = map.Limits, Subraces = map.Subraces,
            Destroyed = [.. map.Destroyed.Where(d => Mine(d.KillerId))], Tombs = tombs, IdNext = map.IdNext,
            LandOwners = map.LandOwners, LandOwnerIds = map.LandOwnerIds, MapSeed = map.MapSeed,
        };

        var looks = state.Looks;
        var shownLooks = new MapLooks
        {
            Map = projected, TreeImage = looks.TreeImage, TreeGroup = looks.TreeGroup,
            Forts = [.. looks.Forts.Select(f => Sees(f.X, f.Y) || Mine(f.Owner) ? f : f with { Owner = "", Riot = false, Garrison = "" })],
            Ruins = [.. looks.Ruins.Select(r => Sees(r.X, r.Y) ? r : r with { Looted = false })], Sites = looks.Sites, Crystals = looks.Crystals,
            Bags = [.. looks.Bags.Where(b => Sees(b.X, b.Y))], Tombs = [.. looks.Tombs.Where(t => Sees(t.X, t.Y))],
            Rods = [.. looks.Rods.Where(r => rods.ContainsKey(r.Id))], Landmarks = looks.Landmarks, Mountains = looks.Mountains,
            Roads = looks.Roads, Stacks = [.. looks.Stacks.Where(s => stacks.ContainsKey(s.Id))], Banners = looks.Banners,
        };

        return new GameState(state.Info, shownLooks)
        {
            Events = state.Events,
            EventProgress = state.EventProgress with { Log = [.. state.EventProgress.Log.Where(l => Mine(l.PlayerId))] },
            Offers = [.. state.Offers.Where(o => Mine(o.From) || Mine(o.To))],
            Messages = [.. state.Messages.Where(m => Mine(m.PlayerId))],
            ActiveHuman = state.ActiveHuman,
            TurnIncome = state.TurnIncome.Where(p => Mine(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            Summaries = state.Summaries.Where(p => Mine(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Чужой игрок: имя, раса, лорд, сторона, команда и столица остаются; казна, второй кошелёк, заклинания, постройки, разведка и
    /// обмены картами — нет.</summary>
    private static MapPlayer Stranger(MapPlayer p) => p with
    {
        Bank = Bank.Zero, SpellBank = Bank.Zero, KnownSpells = [], Buildings = [], Spied = [], MapExchanges = null, Attitude = 0,
    };

    /// <summary>Объект под туманом: место, вид и имя (они из сценария) остаются, всё, что меняет партия, — нет.</summary>
    private static MapSite Veiled(MapSite s) => s with
    {
        Owner = "", Cash = Bank.Zero, ItemId = "", LooterId = "", Items = [], Guards = [], GuardSlots = [], VisitorId = "", StackId = "",
        Visitors = [], RiotTurn = null, Morale = null, PreviousOwners = null, Stock = [], Buys = null, GrowthTurn = null, RegenBonus = null,
        ProtectBonus = "", Gone = false,
    };

    /// <summary>Чужой туман — вся карта скрыта: что разведал другой, не видно.</summary>
    private static bool[,] Shroud(int size)
    {
        var all = new bool[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                all[x, y] = true;
        return all;
    }
}
