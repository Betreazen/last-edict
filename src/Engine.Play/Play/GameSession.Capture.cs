using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Смена хозяина деревни и отряда — то, что движок делает визиторами рядом со сменой (аудит 13.10, «Осталось» п. 6):
/// прежние хозяева деревни (<c>CVisitorChangePreviousOwner</c>) и сброс приказов «охранять город» / «защищать отряд», направленных на
/// сменивший хозяина объект, у отрядов и шаблонов отрядов чужой подрасы (<c>CVisitorChangeStackOrder</c>,
/// <c>CVisitorChangeStackTemplateOrder</c>).</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Деревня перешла к <paramref name="captured"/>.Owner (<c>CEffectCapture</c> <c>0x433e2e</c>: вход в деревню и подкуп — флаг <c>+0x14</c>).
    /// <list type="number">
    /// <item>Прежний хозяин не нейтрал (<c>0x5e5358</c>) и его расы нет среди прежних (<c>0x5fe50d</c>) — раса записывается
    /// (<c>CVisitorChangePreviousOwner</c> <c>0x5e8992</c>; единственный вызов — здесь, <c>0x434017</c>).</item>
    /// <item>Хозяин сменился (<c>CVisitorChangeVillageOwner</c> <c>0x60fe36</c>), и если у деревни была подраса — приказ
    /// <c>L_SECURE_CITY</c> с целью-деревней у отрядов и шаблонов отрядов другой подрасы сбрасывается: <c>L_NORMAL</c>, у нейтрала —
    /// <c>L_STAND</c>, цель пуста (<c>0x60fb7b</c>; шаблоны — <c>CVisitorChangeStackTemplateOrder</c> <c>0x5e8abf</c>, отряды —
    /// <c>CVisitorChangeStackOrder</c> <c>0x5e811a</c>).</item>
    /// <item>Расы нового хозяина среди прежних нет — <c>CCmdOccupyCityMsg</c> с флагом 1 всем (<c>0x434080</c>): звук <c>OCCUPY</c>.</item>
    /// </list>
    /// Возвращает карту с деревней <paramref name="captured"/> (уже нового хозяина) и признак <c>OCCUPY</c>; шаблоны — в
    /// <see cref="GameState.Events"/> сразу.
    /// </summary>
    private (MapState Map, bool Occupy) VillageCaptured(MapState map, MapSite before, MapSite captured)
    {
        var races = (captured.PreviousOwners ?? []).ToList();
        if (before.Owner.Length > 0 && !IsNeutralIn(map, before.Owner) && RaceTypeOf(map, before.Owner) is { } old && !races.Contains(old))
            races.Add(old);
        var site = captured with { PreviousOwners = races.Count > 0 ? races : null };
        map = With(map, sites: [.. map.Sites.Select(s => Same(s.Id, site.Id) ? site : s)]);
        if (before.Subrace.Length > 0) map = DropOrders(map, StackOrder.SecureCity, site.Id, site.Subrace);
        bool occupy = RaceTypeOf(map, site.Owner) is not { } race || !races.Contains(race);
        return (CaptureLand(map, site), occupy);
    }

    /// <summary>
    /// Земля взятой деревни сразу (<c>CEffectCapture</c>: <c>0x434173</c> — пятно плана укрепления <c>0x5da3cf</c>, затем
    /// <c>0x4341bb</c> — обход <c>0x5da21f</c> на <c>CapturePercent / 100 · T_CITY[уровень]</c>, <see cref="LandQuotas.Acquisition"/>) —
    /// тот же разовый обход приобретения, что у жезла (<see cref="LandModel.AcquisitionCells"/>). Ранг 1 — листинг и журнал карты 23
    /// (<c>20261020-port-synth</c>): <c>fort_capture</c> и сразу <c>land_grow</c> с центром в укреплении (день 54: <c>S143KC0007</c>
    /// берёт <c>S143FT000a</c> (120,130) — 25 клеток Легионов вокруг (122,132), seq 8960179/8960180). Прежде порт красил землю взятой
    /// деревни только дневным ростом следующего хода, и обход Нежити того же дня от <c>S143FT000b</c> шёл не к отобранным клеткам, а
    /// на север — к кристаллу <c>S143CR0013</c>, который у движка в тот день стал землёй Кланов (реестр И152). Хозяин не играбельной
    /// расы — ничего.
    /// </summary>
    private MapState CaptureLand(MapState map, MapSite site)
    {
        var book = RequireBook();
        if (!map.Players.TryGetValue(site.Owner, out var owner) || !book.Economy.Races.TryGetValue(owner.RaceId, out var race) || !race.Playable)
            return map;
        int terrain = race.Terrain;
        var owned = LandOwnership.OwnCells(map, owner.Id, terrain);   // при общей расе — только земля хозяина деревни
        var centre = LandModel.FortCentre(site.X, site.Y);
        var plan = LandModel.PlanCells(map, site.Id);
        var source = new LandSource(site.Id, centre, book.Land.Village(site.Size ?? 1), plan.Count > 0 ? plan : [centre]);
        var cells = LandModel.AcquisitionLand(map, owned, source, book.Land, LandModel.BlockedCells(map, map.Sites, book.MountainLandmarks, book.Economy.NeutralRaces));
        return cells.Count == 0 ? map : Recolor(map, cells.ToHashSet(), terrain, owner.Id);
    }

    /// <summary>Отряд <paramref name="stackId"/> сменил хозяина (<c>CVisitorChangeStackOwner</c> <c>0x60d6a5</c> → <c>0x60d3ea</c>;
    /// шлют <c>CEffectChangeOwner</c> и <c>CEffectCreateStack</c>): приказ <c>L_DEFEND_STACK</c> с целью-отрядом у отрядов другой подрасы —
    /// <c>L_NORMAL</c> (у нейтрала <c>L_STAND</c>), цель пуста. У шаблонов движок на этом пути сравнивает с <c>L_SECURE_CITY</c>
    /// (<c>0x60d57e</c>) — цель-город никогда не равна id отряда, порт эту ветку не повторяет (ранг 2).</summary>
    private MapState StackOwnerChanged(MapState map, string stackId) =>
        map.Stacks.TryGetValue(stackId, out var stack) ? DropOrders(map, StackOrder.DefendStack, stack.Id, stack.Subrace, templates: false) : map;

    /// <summary>Сбросить приказ <paramref name="order"/> с целью <paramref name="targetId"/> у отрядов (и шаблонов) не подрасы
    /// <paramref name="subrace"/>: <c>L_NORMAL</c>, у нейтрала <c>L_STAND</c> (<c>0x5e5358</c> → <c>0x837908</c>), цель пуста.</summary>
    private MapState DropOrders(MapState map, StackOrder order, string targetId, string subrace, bool templates = true)
    {
        bool Hit(int o, string target, string sub) => o == (int)order && Same(target, targetId) && !Same(sub, subrace);
        int Reset(string owner) => (int)(IsNeutralIn(map, owner) ? StackOrder.Stand : StackOrder.Normal);
        var dropped = map.Stacks.Values.Where(s => Hit(s.Order, s.OrderTarget, s.Subrace)).ToList();
        if (dropped.Count > 0)
        {
            var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var s in dropped) stacks[s.Id] = s with { Order = Reset(s.Owner), OrderTarget = "" };
            map = With(map, stacks: stacks);
        }
        if (!templates) return map;
        var events = State.Events;
        var hit = events.Templates.Values.Where(t => Hit(t.Order, t.OrderTarget, t.Subrace)).ToList();
        if (hit.Count == 0) return map;
        var all = events.Templates.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var t in hit) all[t.Id] = t with { Order = Reset(t.Owner), OrderTarget = "" };
        State = State with { Events = events with { Templates = all } };
        return map;
    }

    private int? RaceTypeOf(MapState map, string playerId) =>
        Book is { } book && map.Players.TryGetValue(playerId, out var p) && book.Economy.Races.TryGetValue(p.RaceId, out var r) ? r.RaceType : null;

    private bool IsNeutralIn(MapState map, string playerId) =>
        Book is { } book && map.Players.TryGetValue(playerId, out var p) && book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Neutral;
}
