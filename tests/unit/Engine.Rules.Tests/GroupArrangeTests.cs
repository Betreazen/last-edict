using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>Перестановка группы ИИ (<see cref="GroupArrange"/>, <c>0x5d57de</c>/<c>0x5d5d73</c>): примеры — из сохранений и
/// журналов корпуса, сверка целиком — <c>AiStackItemsHarnessTests</c>.</summary>
public class GroupArrangeTests
{
    private static ArrangeUnit Melee(string id, double score) => new(id, score, ArrangeLine.Melee, ArrangeRole.Other);
    private static ArrangeUnit Back(string id, double score, ArrangeRole role = ArrangeRole.Other) => new(id, score, ArrangeLine.Back, role);
    private static ArrangeUnit Big(string id, double score) => new(id, score, ArrangeLine.Big, ArrangeRole.Other);

    [Fact]
    public void Sorted_EqualScore_LaterInsertedFirst()
    {
        var list = GroupArrange.Sorted([Melee("a", 20), Melee("b", 30), Melee("c", 20)]);

        Assert.Equal(["b", "c", "a"], list.Select(u => u.Id));
    }

    [Fact]
    public void ToFront_SkipsCurrentFirst()
    {
        var list = new List<ArrangeUnit> { Melee("m", 24), Big("b", 81) };

        bool moved = GroupArrange.ToFront(list, u => u.Role == ArrangeRole.Other);

        Assert.True(moved);
        Assert.Equal(["b", "m"], list.Select(u => u.Id));
        Assert.False(GroupArrange.ToFront(list, u => u.Id == "b"));
    }

    [Fact]
    public void Queue_LastRoleMoveBeatsMelee()
    {
        // Легионы, день 1, S073KC0003: большой 0198 (81), маг 01d5, поддержка 01ef, боец 019a (24) — движок берёт 0198 первым.
        var queue = GroupArrange.Queue([Big("0198", 81), Back("01d5", 32.4), Back("01ef", 31.2, ArrangeRole.Support), Melee("019a", 24)],
                                       assistOrDefend: false);

        Assert.Equal(["0198", "019a"], queue.Take(2).Select(u => u.Id));
    }

    [Fact]
    public void Queue_LoneMeleeIsNotKeptFirst()
    {
        // human, день 12, жезлоносец S073KC007B: боец 0216 (24) и четыре мага 9,72 — первым идёт последний вставленный маг.
        var queue = GroupArrange.Queue([Back("0205", 9.72), Back("020c", 9.72), Melee("0216", 24), Back("0217", 9.72), Back("0218", 9.72)],
                                       assistOrDefend: false);

        Assert.Equal("0218", queue[0].Id);
    }

    [Fact]
    public void Layout_Centered_MatchesSave()
    {
        // human, сохранение дня 2: S073KC0000 — лидер f9 в 0, e8 в 1, a2 в 4, e9 в 5.
        var cells = GroupArrange.Layout([Melee("f9", 60), Melee("a2", 50), Back("e8", 30), Back("e9", 20)], center: true);

        Assert.Equal(["f9", "e8", "", "", "a2", "e9"], cells);
    }

    [Fact]
    public void Layout_BigCentered_MovesColumnAndItsPartner()
    {
        // human, сохранение дня 2: S073KC0002 — ea в 0, большой 9c в 2–3, fa в 4.
        var cells = GroupArrange.Layout([Melee("fa", 60), Melee("ea", 68), Big("9c", 68)], center: true);

        Assert.Equal(["ea", "", "9c", "9c", "fa", ""], cells);
    }

    [Fact]
    public void Layout_WithoutCenter_KeepsFirstFit()
    {
        var cells = GroupArrange.Layout([Back("lead", 36), Back("mage", 9.72)], center: false);

        Assert.Equal(["", "lead", "", "mage", "", ""], cells);
    }

    [Fact]
    public void Swap_BigTakesWholeColumn()
    {
        var fort = new UnitCells(["g", "big"], ["g", "", "", "", "big", "big"]);
        var stack = new UnitCells(["lead", "a", "b"], ["a", "b", "lead", "", "", ""]);

        var swap = GroupArrange.Swap(fort, 4, stack, 0);

        Assert.NotNull(swap);
        Assert.Equal(["big", "big", "lead", "", "", ""], swap.Value.To.Cells);
        Assert.Equal(["g", "", "", "", "a", "b"], swap.Value.From.Cells);
        Assert.Equal(["lead", "big"], swap.Value.To.Units);
        Assert.Equal(["g", "a", "b"], swap.Value.From.Units);
    }

    /// <summary>Задняя клетка — колонка целиком (<c>0x6116ba</c>: позиции сводятся к передним клеткам колонок): большой на клетку 1 встаёт в
    /// колонку 0, малый с задней клетки на большого уходит со своей колонкой (журнал human t=265422, <c>swap_check</c> 0).</summary>
    [Fact]
    public void Swap_BackCell_TakesItsColumn()
    {
        var fort = new UnitCells(["big"], ["big", "big", "", "", "", ""]);
        var stack = new UnitCells(["lead", "a"], ["", "", "lead", "a", "", ""]);

        var big = GroupArrange.Swap(fort, 0, stack, 1);
        Assert.NotNull(big);
        Assert.Equal(["big", "big", "lead", "a", "", ""], big.Value.To.Cells);

        var small = GroupArrange.Swap(stack, 3, fort, 0);
        Assert.NotNull(small);
        Assert.Equal(["", "", "big", "big", "", ""], small.Value.From.Cells);
        Assert.Equal(["lead", "a", "", "", "", ""], small.Value.To.Cells);
    }

    [Fact]
    public void UnitCells_RoundTripsSlots()
    {
        var cells = UnitCells.Of(["x", "y"], [-1, -1, 1, 1, 0, -1]);

        Assert.Equal(["", "", "y", "y", "x", ""], cells.Cells);
        Assert.Equal([-1, -1, 1, 1, 0, -1], cells.Slots());
        Assert.Equal(2, cells.PositionOf("y"));
        Assert.Equal(3, cells.Occupied);
    }
}
