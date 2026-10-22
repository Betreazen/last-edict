using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;

/// <summary>Очередь раунда и прогон боя на записанных бросках. Тесты BattleSim.Run лежат здесь же:
/// они проверяют именно расход бросков очереди по раундам (замер шага 1 Task 5), а не формулы.</summary>
public class TurnOrderTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);

    [Fact] public void Build_SortsByInitiativePlusRoll_DrawsInUnitsInfoOrder()
    {
        var state = State(("a", 50, 0, true), ("b", 65, 1, false));
        var rng = new ScriptedRng([9, 0]);   // a: 50+9=59, b: 65+0=65
        var order = TurnOrder.Build(state, rng, C);
        Assert.Equal(["b", "a"], order.Select(u => u.Id));
        Assert.Equal(2, rng.Used);           // ровно один бросок на живого юнита, в порядке unitsInfo
    }

    /// <summary>Ничья решается порядком массива unitsInfo, а не стороной и не слотом: первым в
    /// массиве здесь стоит обороняющийся из слота 1, и при равных значениях он и ходит первым.
    /// Прежнее правило («сторона нападающего, затем меньший слот») дало бы обратный порядок —
    /// харнесс очереди на корпусе развёл эти два ответа 534/0 против 494/40.</summary>
    [Fact] public void Build_TieKeepsUnitsInfoOrder()
    {
        var state = State(("b", 50, 1, false), ("a", 50, 0, true));
        var order = TurnOrder.Build(state, new ScriptedRng([5, 5]), C);
        Assert.Equal(["b", "a"], order.Select(u => u.Id));
    }

    [Fact] public void Build_SkipsDead()
    {
        var state = State(("a", 50, 0, true), ("b", 65, 1, false));
        state.Unit("a")!.Hp = 0;
        var rng = new ScriptedRng([0]);
        var order = TurnOrder.Build(state, rng, C);
        Assert.Equal(["b"], order.Select(u => u.Id));
        Assert.Equal(1, rng.Used);           // мёртвый броска не тратит
    }

    [Fact] public void Run_BuildsRoundOneQueueTwice_ThenOncePerRound()
    {
        var state = State(("a", 50, 0, true), ("b", 65, 0, false));
        var rng = new ScriptedRng([
            0, 0,          // раунд 1, первый вызов beforeBattleRound — очередь выбрасывается
            9, 0,          // раунд 1, второй вызов: a 59, b 65 -> b, a
            10, 20, 3,     // b бьёт a: (10+20)/2 < 80 — попал, 25+3
            90, 90,        // a бьёт b: промах
            0, 0,          // раунд 2, один вызов: b 65, a 50
            0, 0, 0,       // b бьёт a: 25
            0, 0, 5,       // a бьёт b: 30
        ]);
        var sim = new BattleSim(state, rng, C);
        var log = sim.Run(maxRounds: 2);

        Assert.Equal([
            new TurnRecord(1, "b", "a", false, 28, 72),
            new TurnRecord(1, "a", "b", true, 0, 100),
            new TurnRecord(2, "b", "a", false, 25, 47),
            new TurnRecord(2, "a", "b", false, 30, 70),
        ], log);
        Assert.Equal(17, rng.Used);
        Assert.Null(sim.Winner);
    }

    [Fact] public void Run_BuildsNextRoundQueueBeforeDecidingBattleIsOver()
    {
        var state = State(("a", 65, 0, true), ("b", 50, 0, false));
        state.Unit("b")!.Hp = 20;
        var rng = new ScriptedRng([0, 0, 0, 0, 0, 0, 0]);   // раунд 1 и раунд 2 вперёд (2+2, Р4; второе построение раунда 1 — без бросков), удар (3)
        var sim = new BattleSim(state, rng, C);
        var log = sim.Run();

        Assert.Equal([new TurnRecord(1, "a", "b", false, 25, 0)], log);
        Assert.Equal(7, rng.Used);   // очередь раунда 2 — по брошенным заранее, бой кончен — раунд 3 не бросается
        Assert.Equal("G1", sim.Winner);
    }

    private static BattleState State(params (string id, int ini, int slot, bool attacker)[] units) => new()
    {
        AttackerGroup = "G1", DefenderGroup = "G2", Round = 0,
        Units = units.Select(u => new BattleUnit
        {
            Id = u.id,
            Type = new UnitType("base:unit/squire-empire", 1, 100, 0, "base:attack/squire-empire", "g000000000", false, true, 20, 80),
            Attack = new AttackType("base:attack/squire-empire", u.ini, AttackClass.Damage, 80, 3, 25, 0, false, 0),
            Attacker = u.attacker, Slot = u.slot, HpMax = 100, Hp = 100,
        }).ToList(),
    };
}
