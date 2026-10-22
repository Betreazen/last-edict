using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using static BattleAiTests;

/// <summary>Редкие классы политики ИИ (<see cref="AiRareTargets"/>) — по Lua тулсета (ранг 2) и журналу
/// map-001 [1281528] для самопревращения.</summary>
public class BattleAiRareTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);

    [Fact] public void Revive_TakesFallenWithMostXp_LastOnTie()
    {
        var r = U("R", true, 1, cls: AttackClass.Revive, reach: AttackReach.Any);
        var d1 = U("D1", true, 0, xpk: 30); d1.Hp = 0;
        var d2 = U("D2", true, 2, xpk: 30); d2.Hp = 0;
        var d3 = U("D3", true, 4, xpk: 10); d3.Hp = 0;
        Assert.Equal("D2", BattleAi.Choose(S(r, d1, d2, d3, U("E", false, 0)), r, new ScriptedRng([]), C).Target?.Id);
    }

    [Fact] public void Cure_PrefersParalyzedOverPoisoned()
    {
        var c = U("C", true, 1, cls: AttackClass.Cure, reach: AttackReach.Any);
        var poisoned = U("P", true, 0, xpk: 90);
        poisoned.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 5, AppliedRound = 1, Long = true });
        var stunned = U("S", true, 2, xpk: 10); stunned.Paralyzed = true;
        Assert.Equal("S", BattleAi.Choose(S(c, poisoned, stunned, U("E", false, 0)), c, new ScriptedRng([]), C).Target?.Id);
    }

    [Fact] public void GiveAttack_PrefersDamageDealerWhoCanStrike()
    {
        var g = U("G", true, 1, cls: AttackClass.GiveAttack, reach: AttackReach.Any);
        var healer = U("H", true, 3, cls: AttackClass.Heal, reach: AttackReach.Any, xpk: 90);
        var fighter = U("F", true, 0, xpk: 10);
        var d = BattleAi.Choose(S(g, healer, fighter, U("E", false, 0)), g, new ScriptedRng([]), C);
        Assert.Equal((AiAction.Attack, "F"), (d.Action, d.Target?.Id));
    }

    /// <summary>Самопревращение: при трёх живых врагах — нельзя, удар уходит запасной площадной атаке по
    /// первому врагу; при двух — превращается сам.</summary>
    [Fact] public void TransformSelf_UsesAltAttackOnEnemiesUnlessSelfTransformPossible()
    {
        var alt = new AttackType("g000aa1164", 50, AttackClass.Damage, 80, AttackReach.All, 40, 0, false, 1, Source: 0);
        var wolf = U("W", true, 0, cls: AttackClass.TransformSelf, reach: AttackReach.Any);
        wolf.Attack = wolf.Attack with { AltAttackId = alt.Id };
        var rules = new GameRules(new Dictionary<string, UnitType>(), new Dictionary<string, AttackType> { [alt.Id] = alt }, C);
        var e0 = U("E0", false, 0); var e2 = U("E2", false, 2); var e4 = U("E4", false, 4);
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [wolf, e0, e2, e4], Rules = rules };
        var d = BattleAi.Choose(state, wolf, new ScriptedRng([]), C);
        Assert.Equal((AiAction.Attack, "E0"), (d.Action, d.Target?.Id));
        e4.Hp = 0;
        Assert.Same(wolf, BattleAi.Choose(state, wolf, new ScriptedRng([]), C).Target);
    }

    [Fact] public void Doppelganger_NeverCopiesItself()
    {
        var dop = U("D", true, 0, cls: AttackClass.Doppelganger, reach: AttackReach.Any, xpk: 99);
        var d = BattleAi.Choose(S(dop, U("E", false, 0, xpk: 5)), dop, new ScriptedRng([]), C);
        Assert.Equal((AiAction.Attack, "E"), (d.Action, d.Target?.Id));
    }

    [Fact] public void Shatter_TakesHighestArmor()
    {
        var s = U("S", true, 1, cls: AttackClass.Shatter, reach: AttackReach.Any);
        var soft = U("A", false, 0, armor: 10); var hard = U("B", false, 2, armor: 40);
        Assert.Equal("B", BattleAi.Choose(S(s, soft, hard), s, new ScriptedRng([]), C).Target?.Id);
    }
}
