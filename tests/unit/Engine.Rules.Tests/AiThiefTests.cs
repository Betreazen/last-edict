using D2.Rules.Map;
using D2.Rules.Rng;

namespace D2.Rules.Tests;

/// <summary>Выбор действия вора ИИ (<c>0x45b98a</c>, <see cref="AiThief"/>): перемешивание доступного на R250 и проверки.</summary>
public class AiThiefTests
{
    private static readonly ThiefAction[] PoisonSpyDuel = [ThiefAction.PoisonStack, ThiefAction.Spy, ThiefAction.Duel];

    private static readonly AiThiefTarget Rodder = new() { Kind = ThiefTargetKind.Stack, HasGroup = true, UnitCount = 1 };

    [Fact]
    public void Shuffle_SwapsFirstPairAlways_ThenPicksFirstPassing()
    {
        // i = 1: rng(1) = 0 — обмен первой пары всегда; i = 2: rng(2) = 0 — [дуэль, отравление, шпионаж].
        var rng = new ScriptedRng([0, 0]);
        var choice = AiThief.Choose(false, false, PoisonSpyDuel, Rodder, rng);
        // У жезлоносца дуэли нет, трёх юнитов для отравления нет — шпионаж (журнал human: 10 из 12 таких свитков).
        Assert.Equal(new AiThiefChoice(ThiefAction.Spy), choice);
        Assert.Equal(2, rng.Used);
    }

    [Fact]
    public void Shuffle_DuelFirst_WhenTargetLeaderHasNoRod()
    {
        var choice = AiThief.Choose(false, false, PoisonSpyDuel, Rodder with { DuelAllowed = true }, new ScriptedRng([0, 0]));
        Assert.Equal(ThiefAction.Duel, choice?.Action);
    }

    [Fact]
    public void Shuffle_SpyFirst_WhenSecondSwapKeepsIt()
    {
        // i = 2: rng(2) = 1 — [шпионаж, дуэль, отравление].
        var choice = AiThief.Choose(false, false, PoisonSpyDuel, Rodder with { DuelAllowed = true }, new ScriptedRng([0, 1]));
        Assert.Equal(ThiefAction.Spy, choice?.Action);
    }

    [Fact]
    public void Poison_NeedsThreeUnits()
    {
        ThiefAction[] poisonDuel = [ThiefAction.PoisonStack, ThiefAction.Duel];
        Assert.Null(AiThief.Choose(false, false, poisonDuel, Rodder with { UnitCount = 2 }, new ScriptedRng([0])));
        Assert.Equal(ThiefAction.PoisonStack,
                     AiThief.Choose(false, false, poisonDuel, Rodder with { UnitCount = AiThief.PoisonUnits }, new ScriptedRng([0]))?.Action);
    }

    [Fact]
    public void Riot_FirstAndWithoutDraws()
    {
        var rng = new ScriptedRng([]);
        var choice = AiThief.Choose(false, true, [ThiefAction.Spy, ThiefAction.RiotCity], new AiThiefTarget { Kind = ThiefTargetKind.Fortification }, rng);
        Assert.Equal(ThiefAction.RiotCity, choice?.Action);
        Assert.Equal(0, rng.Used);
    }

    [Fact]
    public void StealMage_CancelsTheScroll()
    {
        var target = new AiThiefTarget { Kind = ThiefTargetKind.MageTower };
        Assert.Null(AiThief.Choose(false, false, [ThiefAction.StealMage], target, new ScriptedRng([])));
    }

    [Fact]
    public void StealItem_DrawsTheItemIndex()
    {
        var target = new AiThiefTarget { Kind = ThiefTargetKind.Stack, ItemCount = 4 };
        var rng = new ScriptedRng([3]);
        Assert.Equal(new AiThiefChoice(ThiefAction.StealItem, 3), AiThief.Choose(false, true, [ThiefAction.StealItem], target, rng));
        Assert.Equal(1, rng.Used);
    }

    [Fact]
    public void Assist_FixedOrderWithoutDraws()
    {
        var rng = new ScriptedRng([]);
        Assert.Equal(ThiefAction.PoisonStack, AiThief.Choose(true, false, PoisonSpyDuel, Rodder with { DuelAllowed = true }, rng)?.Action);
        Assert.Equal(ThiefAction.PoisonCity,
                     AiThief.Choose(true, false, [ThiefAction.PoisonCity], new AiThiefTarget { Kind = ThiefTargetKind.Fortification }, rng)?.Action);
        Assert.Equal(0, rng.Used);
    }

    [Fact]
    public void Empty_IsCancel() =>
        Assert.Null(AiThief.Choose(false, false, [], Rodder, new ScriptedRng([])));
    /// <summary>Ветка помощника (<c>0x45ba49</c>): Дипломат — подложный приказ, если в группе стрелки (<c>0x45c1e5</c>); дуэль, если
    /// лидер цели слаб (<c>0x45c2b3</c>); крепость не нейтрала — подложный приказ раньше отравления города.</summary>
    [Fact]
    public void Assist_MisfitAndDuel_ByListing()
    {
        var rng = new ScriptedRng([]);
        ThiefAction[] stackAll = [ThiefAction.PoisonStack, ThiefAction.Assassinate, ThiefAction.Misfit, ThiefAction.Duel];
        var stack = new AiThiefTarget { Kind = ThiefTargetKind.Stack, HasGroup = true, UnitCount = 4, DuelAllowed = true };
        Assert.Equal(ThiefAction.Misfit, AiThief.Choose(true, true, stackAll, stack with { MisfitUseful = true }, rng)?.Action);
        Assert.Equal(ThiefAction.Duel, AiThief.Choose(true, true, stackAll, stack with { DuelWeakLeader = true }, rng)?.Action);
        Assert.Equal(ThiefAction.PoisonStack, AiThief.Choose(true, true, stackAll, stack, rng)?.Action);
        Assert.Equal(ThiefAction.Duel, AiThief.Choose(true, false, stackAll, stack with { DuelWeakLeader = true }, rng)?.Action);
        Assert.Equal(ThiefAction.PoisonStack, AiThief.Choose(true, false, stackAll, stack with { MisfitUseful = true }, rng)?.Action);
        ThiefAction[] fortAll = [ThiefAction.PoisonCity, ThiefAction.Misfit];
        var fort = new AiThiefTarget { Kind = ThiefTargetKind.Fortification, HasGroup = true, UnitCount = 3, MisfitUseful = true };
        Assert.Equal(ThiefAction.Misfit, AiThief.Choose(true, false, fortAll, fort, rng)?.Action);
        Assert.Equal(ThiefAction.PoisonCity, AiThief.Choose(true, false, fortAll, fort with { OwnerNeutral = true }, rng)?.Action);
    }
}
