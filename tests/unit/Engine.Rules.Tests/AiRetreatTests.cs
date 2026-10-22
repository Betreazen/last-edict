using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;
using static BattleAiTests;

/// <summary>Сила отряда (<c>0x5ceb4b</c> → <c>0x5dd06c</c>) и бегство ИИ (<c>0x5d2a4d</c>, <c>0x5d2779</c>,
/// <c>0x5d2b85</c>) — разбор docs/synthetic-maps/analysis/G-ai-battle.md §3.</summary>
public class AiRetreatTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);

    /// <summary>A: 100 HP, броня 0, POWER 80, урон 25, «соседняя» — 20. B: 50 HP, броня 50, POWER 100, урон 40,
    /// «любой», Once к оружию (57): (50/30)/(50/60) · 1.0 · 40 · 1.5 · (1.57 · 50) · 0.01 = 94.2.</summary>
    [Fact] public void UnitStrength_ByFormula()
    {
        var a = U("A", true, 0);
        var b = U("B", false, 0, hp: 50, armor: 50, dam: 40, reach: AttackReach.Any,
                  bySource: [new(0, ImmunityLevel.Once)]);
        b.Attack = b.Attack with { Power = 100 };
        Assert.Equal(20.0, AiStrength.Unit(a, ignoreCity: true, enemies: 1, C), 9);
        Assert.Equal(94.2, AiStrength.Unit(b, ignoreCity: false, enemies: 1, C), 9);
        Assert.Equal(20.0 / 94.2 * 0.5, AiStrength.Relative(S(a, b), a, C), 9);
    }

    [Fact] public void UnitStrength_InfiniteSecondAttackAndTwice()
    {
        var a = U("A", true, 0, second: AttackClass.Poison);
        Assert.Equal(20.0 * 1.15, AiStrength.Unit(a, true, 1, C), 9);
        // INFINITE у урона движок не читает (0x5dd8b6: только паралич, окаменение, усиление, ослабление, инициатива, яд,
        // обморожение, ожог, превращение другого) — остаётся множитель второй атаки.
        a.Attack = a.Attack with { Infinite = true };
        Assert.Equal(20.0 * 1.15, AiStrength.Unit(a, true, 1, C), 9);
        a.Attack = a.Attack with { Class = AttackClass.Poison, Infinite = false };
        double plain = AiStrength.Unit(a, true, 1, C);
        a.Attack = a.Attack with { Infinite = true };
        Assert.Equal(plain / 1.15 * 1.25, AiStrength.Unit(a, true, 1, C), 9);
        a.Type = a.Type with { AttackTwice = true };
        Assert.Equal(plain / 1.15 * 1.25 * 2, AiStrength.Unit(a, true, 1, C), 9);
        a.Hp = 0;
        Assert.Equal(0.0, AiStrength.Unit(a, true, 1, C));
    }

    [Fact] public void Relative_IsZeroWhenOwnGroupCannotHurt_AndCappedAtOne()
    {
        var a = U("A", true, 0);
        var e = U("E", false, 0, bySource: [new(0, ImmunityLevel.Always)]);
        Assert.Equal(0.0, AiStrength.Relative(S(a, e), a, C));
        var weak = U("W", false, 0, dam: 1);
        Assert.Equal(1.0, AiStrength.Relative(S(a, weak), a, C));
    }

    // --- бегство ---

    private static BattleSide Ai => new() { Known = true, IsStack = true, PlayableRace = true, Order = StackOrder.Normal };

    private static BattleState Battle(BattleSide attackers, BattleSide defenders, int round, params BattleUnit[] units) =>
        new() { AttackerGroup = "S1KC0001", DefenderGroup = "S1KC0002", Round = round, Units = [.. units],
                AttackerSide = attackers, DefenderSide = defenders };

    private static BattleUnit Leader(string id, bool attacker, int slot)
    {
        var u = U(id, attacker, slot);
        u.Type = u.Type with { IsLeader = true, Category = UnitCategory.Leader };
        return u;
    }

    private static BattleUnit Brute(string id, int slot) => U(id, false, slot, dam: 250);

    [Fact] public void Leader_WeakSide_Retreats()
    {
        var l = Leader("L", true, 0);
        var state = Battle(Ai, Ai, 1, l, U("S", true, 2), Brute("E", 0));
        Assert.Equal(AiAction.Retreat, BattleAi.Choose(state, l, new ScriptedRng([]), C).Action);
    }

    [Fact] public void Leader_Alone_DoesNotRetreat()
    {
        var l = Leader("L", true, 0);
        var state = Battle(Ai, Ai, 1, l, Brute("E", 0));
        Assert.Equal(AiAction.Attack, BattleAi.Choose(state, l, new ScriptedRng([]), C).Action);
    }

    [Fact] public void Leader_HumanOrUnknownOrRoundZero_DoesNotRetreat()
    {
        foreach (var (side, round) in new[] { (Ai with { IsHuman = true }, 1), (new BattleSide(), 1), (Ai, 0),
                                              (Ai with { PlayableRace = false }, 1),
                                              (Ai with { Order = StackOrder.Stand }, 1),
                                              (Ai with { AiOrder = StackOrder.Assist }, 1),
                                              (Ai with { Order = StackOrder.AttackStack, OrderTarget = "S1KC0002" }, 1) })
        {
            var l = Leader("L", true, 0);
            var state = Battle(side, Ai, round, l, U("S", true, 2), Brute("E", 0));
            Assert.Equal(AiAction.Attack, BattleAi.Choose(state, l, new ScriptedRng([]), C).Action);
        }
    }

    /// <summary>Защитник-лидер не бежит, пока у нападающего есть ход.</summary>
    [Fact] public void DefendingLeader_StaysWhileAttackerCanMove()
    {
        var l = Leader("L", false, 0);
        var e = U("E", true, 0, dam: 250);
        var mover = Ai with { Movement = 5 };
        var state = Battle(mover, Ai, 1, l, U("S", false, 2), e);
        var d = BattleAi.Choose(state, l, new ScriptedRng([]), C);
        Assert.Equal(AiAction.Attack, d.Action);
        Assert.True(state.RetreatDecided);
        Assert.Equal(RetreatStatus.NoRetreat, state.DefendersRetreat);
    }

    /// <summary>Полное бегство: группа не может бить (все враги Always) — бежит и человек, и нейтрал.</summary>
    [Fact] public void GroupWithoutOffence_FullRetreat()
    {
        var s = U("S", true, 0);
        var e = U("E", false, 0, bySource: [new(0, ImmunityLevel.Always)]);
        var state = Battle(Ai with { IsHuman = true, PlayableRace = false }, Ai, 1,
                           Leader("L", true, 2), s, e);
        Assert.Equal(AiAction.Retreat, BattleAi.Choose(state, s, new ScriptedRng([]), C).Action);
        Assert.Equal(RetreatStatus.Full, state.AttackersRetreat);
    }

    /// <summary>Прикрытие (<c>0x5d2b85</c>): первый ряд без защищающихся — защита, пока живых двое и больше;
    /// второй ряд и лидер — бегство; кто-то другой в первом ряду защищается — бегство.</summary>
    [Fact] public void Cover_FrontDefends_BackAndLeaderRetreat()
    {
        var front = U("F", false, 0);
        var other = U("O", false, 2);
        var back = U("B", false, 1);
        var leader = Leader("L", false, 3);
        var state = Battle(Ai, Ai, 1, front, other, back, leader, Attacker(Brute("E", 0)));
        Assert.Equal(AiAction.Defend, BattleAi.Choose(state, front, new ScriptedRng([]), C).Action);
        Assert.Equal(RetreatStatus.Cover, state.DefendersRetreat);
        Assert.Equal(AiAction.Retreat, BattleAi.Choose(state, back, new ScriptedRng([]), C).Action);
        Assert.Equal(AiAction.Retreat, BattleAi.Choose(state, leader, new ScriptedRng([]), C).Action);
        other.Defending = true;
        Assert.Equal(AiAction.Retreat, BattleAi.Choose(state, front, new ScriptedRng([]), C).Action);
    }

    [Fact] public void Cover_LastOneStanding_Retreats()
    {
        var front = U("F", false, 0);
        var dead = U("D", false, 2); dead.Hp = 0;
        var state = Battle(Ai, Ai, 1, front, dead, Leader("L", false, 1), Attacker(Brute("E", 0)));
        state.DefendersRetreat = RetreatStatus.Cover;
        state.RetreatDecided = true;
        state.Allies(front).Single(u => u.Id == "L").Retreated = true;
        Assert.Equal(AiAction.Retreat, BattleAi.Choose(state, front, new ScriptedRng([]), C).Action);
    }

    private static BattleUnit Attacker(BattleUnit u) => new()
    {
        Id = u.Id, Type = u.Type, Attack = u.Attack, Attacker = true, Slot = u.Slot, HpMax = u.HpMax, Hp = u.Hp,
    };
}
