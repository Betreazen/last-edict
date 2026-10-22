using D2.Rules.Battle;
using D2.Rules.Rng;
using D2.Rules.Units;

namespace D2.Rules.Tests;

/// <summary>Журнал показа атак по своим (<see cref="BattleSim.AllyLog"/>): записи хода (<see cref="BattleSim.Log"/>, его сверяют харнессы)
/// такие атаки не дают, надпись экрану боя — по этому журналу (<c>0x62c462</c> → <c>0x63b391</c>).</summary>
public class AllyLogTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);

    private static BattleUnit Unit(string id, bool attacker, AttackClass cls, int hp = 100) => new()
    {
        Id = id,
        Type = new UnitType("g000uu0001", 1, 100, 0, "g000aa0001", "g000000000", false, true, 20, 80),
        Attack = new AttackType("g000aa0001", 50, cls, 80, 3, 25, 0, false, 1, false),
        Attacker = attacker, Slot = 0, HpMax = 100, Hp = hp,
    };

    [Fact]
    public void Boost_NotedForShow_NotInTurnLog_ReviveStaysInTurnLog()
    {
        var booster = Unit("B", true, AttackClass.BoostDamage);
        var fighter = Unit("F", true, AttackClass.Damage);
        var enemy = Unit("E", false, AttackClass.Damage);
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [booster, fighter, enemy] };
        var sim = new BattleSim(state, new ScriptedRng([]), C);

        Assert.Empty(sim.Act(1, booster, fighter));
        Assert.Empty(sim.Log);
        Assert.Equal(new AllyNote(1, "B", "F", AttackClass.BoostDamage), Assert.Single(sim.AllyLog));

        var reviver = Unit("R", true, AttackClass.Revive);
        var fallen = Unit("D", true, AttackClass.Damage, hp: 0);
        var second = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [reviver, fallen, enemy] };
        var sim2 = new BattleSim(second, new ScriptedRng([]), C);
        sim2.Act(1, reviver, fallen);
        Assert.Single(sim2.Log);          // воскрешение с полученным здоровьем — запись хода, надпись по ней (строка класса)
        Assert.Empty(sim2.AllyLog);
    }

    [Fact]
    public void Heal_StaysInTurnLog_NotInAllyLog()
    {
        var healer = new BattleUnit
        {
            Id = "H", Type = new UnitType("g000uu0001", 1, 100, 0, "g000aa0001", "g000000000", false, true, 20, 80),
            Attack = new AttackType("g000aa0001", 50, AttackClass.Heal, 80, 3, 0, 30, false, 0, false), Attacker = true, Slot = 0, HpMax = 100, Hp = 100,
        };
        var hurt = Unit("W", true, AttackClass.Damage, hp: 50);
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [healer, hurt, Unit("E", false, AttackClass.Damage)] };
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        sim.Act(1, healer, hurt);
        Assert.Single(sim.Log);
        Assert.Empty(sim.AllyLog);
    }
}
