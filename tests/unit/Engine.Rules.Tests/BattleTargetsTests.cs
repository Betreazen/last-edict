using LastEdict.Rules.Battle;

/// <summary>Возможные цели действия (<c>updateBattleActions</c> <c>0x623d93</c>) и дистанция
/// досягаемости (<c>0x65aad3</c>/<c>0x65ab4b</c>) — часть A переноса политики ИИ
/// (docs/synthetic-maps/analysis/G-ai-battle.md §5A).</summary>
public class BattleTargetsTests
{
    /// <summary>Первый ряд своей группы — 0, 2, 4; второй — 1, 3, 5. Для врага d умножается на |dx|.</summary>
    [Theory]
    [InlineData(0, 0, 1)] [InlineData(0, 2, 1)] [InlineData(0, 4, 2)]
    [InlineData(0, 1, 4)] [InlineData(0, 5, 6)]
    [InlineData(1, 0, 4)] [InlineData(1, 2, 4)] [InlineData(1, 4, 6)]
    [InlineData(1, 1, 9)] [InlineData(1, 5, 12)]
    public void Distance_ToEnemy(int from, int to, int expected) =>
        Assert.Equal(expected, BattleTargets.Distance(from, to, ally: false));

    [Fact] public void Distance_ToAlly_IsNotMultiplied() =>
        Assert.Equal(1, BattleTargets.Distance(0, 1, ally: true));   // (0 + 2·1) >> 1

    [Fact] public void Possible_Adjacent_TakesNearestEnemies_SortedBySlot()
    {
        var a = Unit("A", true, slot: 2);
        var e4 = Unit("E4", false, slot: 4); var e0 = Unit("E0", false, slot: 0);
        var e2 = Unit("E2", false, slot: 2); var e1 = Unit("E1", false, slot: 1);
        var state = State(a, e4, e0, e2, e1);
        // из слота 2 (y = 1): до 0, 2, 4 — по 1; до второго ряда — 4.
        Assert.Equal(["E0", "E2", "E4"], BattleTargets.Possible(state, a, a.Attack).Select(u => u.Id));
    }

    [Fact] public void Possible_Adjacent_SecondRowOnlyWhenFirstRowIsGone()
    {
        var a = Unit("A", true, slot: 0);
        var e0 = Unit("E0", false, slot: 0); var e3 = Unit("E3", false, slot: 3);
        var state = State(a, e0, e3);
        Assert.Equal(["E0"], BattleTargets.Possible(state, a, a.Attack).Select(u => u.Id));
        e0.Hp = 0;
        Assert.Equal(["E3"], BattleTargets.Possible(state, a, a.Attack).Select(u => u.Id));
    }

    [Fact] public void Possible_Adjacent_FromSecondRowBlockedByLivingFrontAlly()
    {
        var a = Unit("A", true, slot: 1);
        var front = Unit("F", true, slot: 2);
        var e = Unit("E", false, slot: 0);
        var state = State(a, front, e);
        Assert.Empty(BattleTargets.Possible(state, a, a.Attack));
        front.Retreated = true;
        Assert.Equal(["E"], BattleTargets.Possible(state, a, a.Attack).Select(u => u.Id));
    }

    [Fact] public void Possible_Any_TakesAllLivingOnField()
    {
        var a = Unit("A", true, slot: 1, reach: 2);
        var front = Unit("F", true, slot: 0);
        var e5 = Unit("E5", false, slot: 5); var e0 = Unit("E0", false, slot: 0);
        var gone = Unit("G", false, slot: 2); gone.Retreated = true;
        var dead = Unit("D", false, slot: 4); dead.Hp = 0;
        var state = State(a, front, e5, e0, gone, dead);
        Assert.Equal(["E0", "E5"], BattleTargets.Possible(state, a, a.Attack).Select(u => u.Id));
    }

    [Fact] public void Possible_Heal_SkipsUnhurtAllies()
    {
        var h = Unit("H", true, slot: 1, cls: AttackClass.Heal, reach: 2);
        var full = Unit("F", true, slot: 0);
        var hurt = Unit("W", true, slot: 2); hurt.Hp = 40;
        var state = State(h, full, hurt, Unit("E", false, slot: 0));
        Assert.Equal(["W"], BattleTargets.Possible(state, h, h.Attack).Select(u => u.Id));
    }

    [Fact] public void Possible_Revive_TakesDeadNotRetreatedAllies()
    {
        var r = Unit("R", true, slot: 1, cls: AttackClass.Revive, reach: 2);
        var dead = Unit("D", true, slot: 0); dead.Hp = 0;
        var alive = Unit("L", true, slot: 2);
        var state = State(r, dead, alive, Unit("E", false, slot: 0));
        Assert.Equal(["D"], BattleTargets.Possible(state, r, r.Attack).Select(u => u.Id));
    }

    [Fact] public void Possible_PoisonSkipsRetreating()
    {
        var a = Unit("A", true, slot: 0, cls: AttackClass.Poison, reach: 2);
        var e = Unit("E", false, slot: 0); e.Retreating = true;
        Assert.Empty(BattleTargets.Possible(State(a, e), a, a.Attack));
    }

    internal static BattleState State(params BattleUnit[] units) =>
        new() { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [.. units] };

    internal static BattleUnit Unit(string id, bool attacker, int slot, AttackClass cls = AttackClass.Damage,
                                    int reach = 3, int dam = 25, int hp = 100) => new()
    {
        Id = id,
        Type = new UnitType("g000uu0001", 1, hp, 0, "g000aa0001", "g000000000", false, true, 20, 80),
        Attack = new AttackType("g000aa0001", 50, cls, 80, reach, dam, 30, false, 1, Source: 0),
        Attacker = attacker, Slot = slot, HpMax = hp, Hp = hp,
    };
}
