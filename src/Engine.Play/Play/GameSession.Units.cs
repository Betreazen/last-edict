using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Юниты в группах: обмен клеток между отрядом и гарнизоном (<c>0x5d5642</c>, им же воспользуется окно города
/// человека) и расстановка группы (<c>0x5e7991</c>).</summary>
public sealed partial class GameSession
{
    /// <summary>Группа по id — отряд или гарнизон (охрана) объекта; нет — null.</summary>
    public UnitCells? GroupCells(string groupId)
    {
        if (Map.Stacks.TryGetValue(groupId, out var stack)) return UnitCells.Of(stack.UnitIds, stack.Slots);
        var site = Map.Sites.FirstOrDefault(s => Same(s.Id, groupId));
        return site is null ? null : UnitCells.Of(site.Guards, site.GuardSlots);
    }

    /// <summary>Сколько клеток вмещает группа: отряд — <c>min(лидерство, 6)</c> (как <see cref="SiteTrade.FreeCells"/>:
    /// лидер тоже занимает место — у вора лидерство 1), столица — 6, деревня — её уровень
    /// (<see cref="Fortifications.GarrisonCapacity"/>).</summary>
    public int GroupCapacity(string groupId)
    {
        if (Map.Stacks.TryGetValue(groupId, out var stack)) return Math.Min(Leadership(stack), SiteTrade.MaxCells);
        var site = Map.Sites.FirstOrDefault(s => Same(s.Id, groupId));
        return site is { Kind: SiteKind.Village } ? Fortifications.GarrisonCapacity(site.Size ?? 1) : SiteTrade.MaxCells;
    }

    /// <summary>Свободные клетки отряда для найма (<c>0x5f4485</c>, <see cref="SiteTrade.FreeCells"/>): <c>min(лидерство, 6)</c>
    /// минус занятые (большой — две).</summary>
    public int FreeCells(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return SiteTrade.FreeCells(Leadership(stack), UnitCells.Of(stack.UnitIds, stack.Slots).Occupied);
    }

    /// <summary>Лидерство отряда: <c>LEADERSHIP</c> типа лидера (с уровнем не растёт — в <c>GDynUpgr</c> его нет) плюс
    /// навыки и предметы цепочки (<see cref="EffectiveStats.OfStack"/>).</summary>
    public int Leadership(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var book = RequireBook();
        if (!Map.Units.TryGetValue(stack.LeaderId, out var leader)) return 0;
        int basic = book.TryGlobal(leader.TypeId, out var id, out _) && book.Extras.Units.TryGetValue(id, out var e) ? e.Leadership : 0;
        var chain = leader.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        return EffectiveStats.OfStack(chain, leadership: basic).Leadership;
    }

    /// <summary>Скидка найма лидера отряда в процентах — <c>IUsStackLeader::getLowerCost</c>: сумма <c>L_LOWER_COST</c> цепочки
    /// модификаторов лидера (предметы вроде «Lute of Charming», <see cref="EffectiveStats.LowerCostPercent"/>).</summary>
    public int LowerCostPercent(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var book = RequireBook();
        if (!Map.Units.TryGetValue(stack.LeaderId, out var leader) || !book.TryGlobal(leader.TypeId, out var id, out _)
            || !book.Rules.Units.TryGetValue(id, out var type)) return 0;
        var chain = leader.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        return EffectiveStats.Of(type, chain).LowerCostPercent;
    }

    /// <summary>
    /// Можно ли обменять клетки двух разных групп (<c>CVisitorSwapUnitPosition::canApply</c>, <c>0x61111f</c>):
    /// <see cref="GroupArrange.Swap"/> годен; лидер отряда и страж (<c>L_GUARDIAN</c>) свою группу не покидают; из отряда с павшим
    /// лидером воины не уходят (сервер <c>CStackSwapUnitMsg</c>, окно обмена говорит <c>X005TA0212</c>; ИИ ходит той же командой);
    /// заполнение группы после обмена — не больше вместимости (<see cref="GroupCapacity"/>) или не больше, чем было.
    /// </summary>
    /// <returns>Причина отказа; null — можно.</returns>
    public string? SwapRefusal(string fromGroup, int fromPos, string toGroup, int toPos) => TrySwap(fromGroup, fromPos, toGroup, toPos).Refused;

    /// <summary>Обмен клеток двух групп (<c>0x5d5642</c>) после <see cref="SwapRefusal"/>.</summary>
    /// <returns>Причина отказа; null — обменяно.</returns>
    public string? SwapUnits(string fromGroup, int fromPos, string toGroup, int toPos)
    {
        var (refused, from, to) = TrySwap(fromGroup, fromPos, toGroup, toPos);
        if (refused is not null) return refused;
        SetCells(fromGroup, from!);
        SetCells(toGroup, to!);
        return null;
    }

    private (string? Refused, UnitCells? From, UnitCells? To) TrySwap(string fromGroup, int fromPos, string toGroup, int toPos)
    {
        if (Same(fromGroup, toGroup)) return ("перестановка внутри группы не поддержана", null, null);
        if (fromPos is < 0 or >= UnitCells.CellCount || toPos is < 0 or >= UnitCells.CellCount) return ("клетки нет", null, null);
        if (GroupCells(fromGroup) is not { } from || GroupCells(toGroup) is not { } to) return ("группы нет", null, null);
        if (GroupArrange.Swap(from, fromPos, to, toPos) is not { } swap) return ("клетки не годятся", null, null);
        if ((Pinned(fromGroup, swap.Left) ?? Pinned(toGroup, swap.Came)) is { } pinned) return ($"{pinned} не покидает группу", null, null);
        if ((swap.Left.Count > 0 && LeaderDead(fromGroup)) || (swap.Came.Count > 0 && LeaderDead(toGroup)))
            return ("лидер отряда мёртв — воинов не вывести", null, null);
        if (swap.From.Occupied > Math.Max(GroupCapacity(fromGroup), from.Occupied)
            || swap.To.Occupied > Math.Max(GroupCapacity(toGroup), to.Occupied)) return ("нет места", null, null);
        return (null, swap.From, swap.To);
    }

    /// <summary>Кто из уходящих привязан к группе: лидер отряда или страж.</summary>
    private string? Pinned(string groupId, IReadOnlyList<string> leaving)
    {
        var book = RequireBook();
        string leader = Map.Stacks.TryGetValue(groupId, out var stack) ? stack.LeaderId : "";
        return leaving.FirstOrDefault(id => Same(id, leader)
            || (Map.Units.TryGetValue(id, out var u) && book.CategoryOf(u.TypeId) == UnitCategory.Guardian));
    }

    /// <summary>Новые клетки и порядок юнитов группы (<c>0x5e7991</c>).</summary>
    public void SetCells(string groupId, UnitCells cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var map = Map;
        int[] slots = cells.Slots();
        if (map.Stacks.TryGetValue(groupId, out var stack))
        {
            var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            stacks[stack.Id] = stack with { UnitIds = [.. cells.Units], Slots = slots };
            var units = EquipOnMembership(map.Units, stack, cells.Units);
            State = State with { Looks = Retold(Looks, With(map, stacks: stacks, units: units), Looks.Stacks) };
            return;
        }
        var sites = map.Sites.Select(s => Same(s.Id, groupId) ? s with { Guards = [.. cells.Units], GuardSlots = slots } : s).ToList();
        State = State with { Looks = Retold(Looks, With(map, sites: sites), Looks.Stacks) };
    }
}
