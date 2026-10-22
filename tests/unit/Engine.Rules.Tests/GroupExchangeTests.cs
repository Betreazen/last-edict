using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>Перенос юнитов между отрядом и гарнизоном/другим отрядом — прогон 20260917-synth
/// (<c>disciples-modding/docs/synthetic-maps/analysis/M-run17-ui-diplomacy.md</c>, q20b), журнал <c>swap_check</c>.</summary>
public class GroupExchangeTests
{
    /// <summary>seq 3817263: в пустую клетку полного героя — 0xB; при юнитах &lt; лидерства — 0.</summary>
    [Fact]
    public void IntoEmptyCell_OnlyBelowLeadership()
    {
        Assert.Equal(GroupExchange.Refused, GroupExchange.Check(false, null, leadership: 3, units: 3));
        Assert.Equal(GroupExchange.Allowed, GroupExchange.Check(false, null, leadership: 3, units: 2));
    }

    /// <summary>Гарнизон лидерства не имеет — пустая клетка принимает.</summary>
    [Fact]
    public void IntoGarrisonEmptyCell_Allowed() =>
        Assert.Equal(GroupExchange.Allowed, GroupExchange.Check(false, null, leadership: null, units: 5));

    /// <summary>Обмен с занятой клеткой разрешён всегда, даже у полного героя (0).</summary>
    [Fact]
    public void SwapWithOccupiedCell_AlwaysAllowed() =>
        Assert.Equal(GroupExchange.Allowed, GroupExchange.Check(false, occupantIsLeader: false, leadership: 3, units: 3));

    /// <summary>seq 3210163: Myzrael (страж) в клетку 2 крепости — 0xB; обмен, уводящий лидера в гарнизон, — 0xB.</summary>
    [Fact]
    public void LeaderOrGuardian_NeverLeavesItsGroup()
    {
        Assert.Equal(GroupExchange.Refused, GroupExchange.Check(true, null, leadership: null, units: 0));
        Assert.Equal(GroupExchange.Refused, GroupExchange.Check(true, occupantIsLeader: false, leadership: null, units: 3));
        Assert.Equal(GroupExchange.Refused, GroupExchange.Check(false, occupantIsLeader: true, leadership: null, units: 3));
    }
}
