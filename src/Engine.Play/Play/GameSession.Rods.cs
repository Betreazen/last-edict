using LastEdict.Data.Map;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Жезлы в партии: постановка и снос по правилам <see cref="Rods"/>. Общие для человека и ИИ.</summary>
public sealed partial class GameSession
{
    /// <summary>Отряд ставит жезл в клетку (<see cref="Rods.Place"/>): 150 из казны и весь остаток хода; земля красится сразу —
    /// приобретение жезла (<see cref="PaintRodLand"/>); невидимый отряд показывается (<see cref="Unhide"/>). Вернёт причину отказа или
    /// <c>null</c>.</summary>
    public string? PlaceRod(string stackId, int x, int y)
    {
        var book = RequireBook();
        var map = Map;
        var stack = map.Stacks[stackId];
        var player = map.Players[stack.Owner];
        var ids = map.IdNext;
        string id = NewId(ref ids, map.Rods.Keys, stackId, "RD");
        var cmd = Rods.Place(map, stack, id, x, y, player.Bank, book.Treasury, LeaderAbilities(stack), AllyTerrain(stack.Owner));
        if (cmd.Refused is { } why) return why;
        Unhide(stackId);   // невидимый показывается до CEffectDropRod (0x440cfc → CEffectShow с 1, 0x440e3b)
        var rod = cmd.Placed!;
        var rods = map.Rods.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        rods[rod.Id] = rod;
        ApplyRod(stack with { Move = cmd.MoveLeft }, player with { Bank = cmd.Purse }, rods, add: rod, remove: null, idNext: ids);
        PaintRodLand(rod);
        RunActionEvents(stackId);   // LeaderDropRod 0x41f30d
        return null;
    }

    /// <summary>
    /// Земля жезла в момент постановки (<c>CEffectDropRod::apply</c> <c>0x437606</c>: <c>land_grow</c> с центром в клетке жезла
    /// сразу за <c>rod_drop</c>, квота <c>ROD_RANGE</c>): клетка жезла с квадратом жилы и разовый обход приобретения
    /// (<see cref="LandModel.AcquisitionCells"/>). До 28.09 порт этого не делал вовсе: дневная модель считает жезлы карты
    /// источниками без дня приобретения. Журнал human, день 30: <c>KC0090</c> ставит жезл у <c>CR0018</c>, и кристалл уходит из
    /// кэша <c>KC0091</c> того же хода (его клетка — своя земля), а в кэш входит <c>CR0013</c> с чужим жезлом.
    /// </summary>
    private void PaintRodLand(MapRod rod)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Players.TryGetValue(rod.Owner, out var owner) || !book.Economy.Races.TryGetValue(owner.RaceId, out var race)
            || !race.Playable) return;
        int terrain = race.Terrain;
        var owned = new HashSet<(int X, int Y)>();
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
                if (map.Tiles[x, y].Terrain == terrain) owned.Add((x, y));
        var source = new LandSource(rod.Id, (rod.X, rod.Y), book.Land.RodRange, [(rod.X, rod.Y)]);
        var cells = LandModel.AcquisitionLand(map, owned, source, book.Land, LandModel.BlockedCells(map, map.Sites, book.MountainLandmarks));
        if (cells.Count == 0) return;
        State = State with { Looks = Retold(Looks, Recolor(map, cells.ToHashSet(), terrain), Looks.Stacks) };
    }

    /// <summary>Отряд сносит жезл (<see cref="Rods.Break"/>): весь остаток хода; чужой — штраф отношений <c>D_ROD</c> с хозяином жезла
    /// (<see cref="Diplomacy.RodDestroyed"/>, <c>0x5d9fab</c> → <c>0x5da006</c>); невидимый отряд показывается (<see cref="Unhide"/>).
    /// Вернёт причину отказа или <c>null</c>.</summary>
    public string? BreakRod(string stackId, string rodId)
    {
        var map = Map;
        var stack = map.Stacks[stackId];
        if (!map.Rods.TryGetValue(rodId, out var rod)) return $"жезла {rodId} нет";
        var player = map.Players[stack.Owner];
        var cmd = Rods.Break(stack, rod, player.Bank, LeaderAbilities(stack), RodOwnerAlly(stack.Owner, rod.Owner));
        if (cmd.Refused is { } why) return why;
        Unhide(stackId);   // невидимый показывается до CEffectBreakRod (0x4408cb → CEffectShow с 1, 0x440c9f)
        var rods = map.Rods.Where(p => !p.Key.Equals(rodId, StringComparison.OrdinalIgnoreCase))
                           .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        ApplyRod(stack with { Move = cmd.MoveLeft }, player, rods, add: null, remove: rod);
        if (cmd.Blamed && PartyOf(stack.Owner) is { } breaker && PartyOf(rod.Owner) is { } owner)
        {
            var book = RequireBook();
            var change = Diplomacy.RodDestroyed(Map.Relations, Diplomacy.PlayerParties(Map, book.Economy), book.Diplomacy, breaker, owner, blamed: true);
            State = State with { Looks = Retold(Looks, With(Map, relations: change.Relations), Looks.Stacks) };
        }
        return null;
    }

    /// <summary>Жезл в клетке, который отряд снесёт по щелчку человека (курсор <c>BREAKROD</c>): тот, что <see cref="Rods.Break"/> не
    /// отвергнет, — отряд не в укреплении, хозяин жезла не союзник, у лидера <c>L_ROD</c>. Нет такого — null.</summary>
    public MapRod? BreakableRodAt(string stackId, int x, int y)
    {
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack) || !map.Players.TryGetValue(stack.Owner, out var player)
            || !map.Plan.TryGetValue((x, y), out var ids)) return null;
        var rod = ids.Select(id => map.Rods.GetValueOrDefault(id)).OfType<MapRod>().FirstOrDefault();
        return rod is not null && Rods.Break(stack, rod, player.Bank, LeaderAbilities(stack), RodOwnerAlly(stack.Owner, rod.Owner)).Refused is null
            ? rod : null;
    }

    /// <summary>
    /// Щелчок человека по сносимому жезлу (<c>CTaskSelectUnit</c> <c>0x4d0e2c</c> → <c>0x4d144d</c>): когда путь к жезлу готов (второй щелчок
    /// по той же клетке, <c>0x4cc8c2</c>; рядом с жезлом путь — одна клетка отряда, и вопрос идёт с первого щелчка) и сегодня доходит до
    /// конца (<c>0x4cc7c8</c>: последняя достижимая точка — конец пути), движок спрашивает «Хотите уничтожить этот жезл?» (<c>X005TA0272</c>).
    /// «Да» (<c>0x4d1ec7</c>) повторяет щелчок с флагом — отряд идёт к жезлу, и шаг у цели сносит его (<c>0x4408cb</c> →
    /// <c>CEffectBreakRod</c>). Путь дальше сегодняшнего — обычный ход без вопроса. Журнал human: три сноса человека — вопрос
    /// <c>DLG_MESSAGE_BOX</c> (<c>0x4d156a</c>), <c>BTN_YES</c>, затем <c>ap_restore</c> <c>cause = rod_break</c>.
    /// </summary>
    public static bool AsksRodBreak(StackPath? path) => path is { Points.Count: > 0 } p && p.Reachable >= p.Points.Count - 1;

    /// <summary>Вопрос сноса жезла (<see cref="AsksRodBreak"/>).</summary>
    public const string RodBreakQuestion = "X005TA0272";

    private void ApplyRod(MapStack stack, MapPlayer player, Dictionary<string, MapRod> rods, MapRod? add, MapRod? remove,
                          IReadOnlyDictionary<string, int>? idNext = null)
    {
        var map = Map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player;
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value.ToList());
        if (remove is not null && plan.TryGetValue((remove.X, remove.Y), out var cell))
            cell.RemoveAll(id => id.Equals(remove.Id, StringComparison.OrdinalIgnoreCase));
        if (add is not null)
        {
            if (!plan.TryGetValue((add.X, add.Y), out var at)) plan[(add.X, add.Y)] = at = [];
            at.Add(add.Id);
        }
        var next = WithRods(With(map, stacks: stacks, players: players,
                                 plan: plan.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value), idNext: idNext), rods);
        var looks = Retold(Looks, next, Looks.Stacks) with
        {
            Rods = [.. Looks.Rods.Where(r => remove is null || !r.Id.Equals(remove.Id, StringComparison.OrdinalIgnoreCase)),
                    .. add is null ? [] : new[] { new RodLook(add.Id, add.X, add.Y, add.Owner) }],
        };
        State = State with { Looks = looks };
    }

    /// <summary>Способности лидера для жезла: <c>L_ROD</c> у жезлоносца (<c>LEADER_CAT</c> 4 — ранг 2, как у тактики).</summary>
    private IReadOnlyList<int> LeaderAbilities(MapStack stack) =>
        Map.Units.TryGetValue(stack.LeaderId, out var leader) && RequireBook().LeaderCategoryOf(leader.TypeId) == 4
            ? [Rods.LeaderAbilityRod] : [];

    private Func<int, bool>? AllyTerrain(string playerId)
    {
        var book = RequireBook();
        if (!Map.Players.TryGetValue(playerId, out var p) || !book.Economy.Races.TryGetValue(p.RaceId, out var race)) return null;
        return Diplomacy.AllyTerrain(Map.Relations, Diplomacy.PlayerParties(Map, book.Economy), p.Party ?? race.RaceType, book.Economy, Map);
    }

    private bool RodOwnerAlly(string a, string b)
    {
        var races = RequireBook().Economy.Races;
        return Map.Players.TryGetValue(a, out var pa) && Map.Players.TryGetValue(b, out var pb)
            && races.TryGetValue(pa.RaceId, out var ra) && races.TryGetValue(pb.RaceId, out var rb)
            && (pa.Party ?? ra.RaceType) is var partyA && (pb.Party ?? rb.RaceType) is var partyB && partyA != partyB && Diplomacy.AreAllies(Map.Relations, partyA, partyB);
    }
}
