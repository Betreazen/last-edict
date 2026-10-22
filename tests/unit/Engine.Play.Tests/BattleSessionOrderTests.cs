using D2.Data.Battle;
using D2.Rules.Battle;
using D2.Rules.Rng;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Партия 15.10 на <c>port-synth-1-battle</c>: порядок показа (начало хода следующего юнита — после действия прежнего), «Ждать» и
/// прочие действия хода по <c>updateBattleActions</c> (<c>0x623d93</c>), порядок атак по своим в показе.</summary>
public class BattleSessionOrderTests
{
    private static string Rules => ImportedData.PathOf("rules");

    private static readonly AttackType Strike = new("g000aa0019", 90, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Slow = new("g000aa0020", 50, AttackClass.Damage, 100, 2, 10, 0, false, 1);

    private static BattleUnit Unit(string id, bool attacker, int slot, AttackType attack, bool twice = false) => new()
    {
        Id = id, Attacker = attacker, Slot = slot, Hp = 1000, HpMax = 1000, Attack = attack,
        Type = new UnitType("g000uu0019", 1, 1000, 0, attack.Id, "g000000000", twice, true, 10, 100),
    };

    private static BattleSession Session(BattleState state, bool hold, bool playerIsAttacker = true)
    {
        var rules = RulesLoader.Load(Rules);
        return new BattleSession(state, new BattleSim(state, new EngineRng(), rules.Constants), rules.Constants, playerIsAttacker)
        {
            HoldTurnStart = hold,
        };
    }

    /// <summary>Человек (инициатива 90) бьёт отравленного врага (50). С <see cref="BattleSession.HoldTurnStart"/> тик яда врага не проходит
    /// в том же вызове, что удар: ходящего нет, бой не кончен, «Автобой» и «Быстрый бой» доступны; тик — по <see cref="BattleSession.StartTurn"/>.
    /// Без удержания (бой без окна) — сразу, как <see cref="BattleSim.Run"/>.</summary>
    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tick_OfNextUnit_WaitsForStartTurn(bool hold)
    {
        var player = Unit("P", attacker: true, slot: 0, Strike);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        enemy.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 5, AppliedRound = 1, Long = true });
        var state = new BattleState { Units = [player, enemy], AttackerGroup = "A", DefenderGroup = "D" };
        var session = Session(state, hold);
        Assert.Same(player, session.Current);

        int hpAfterStrike = session.Attack(enemy)[^1].TargetHpAfter;

        if (hold)
        {
            Assert.True(session.TurnStartPending);
            Assert.Null(session.Current);
            Assert.False(session.Finished);
            Assert.Empty(session.Sim.DotLog);
            Assert.Equal([BattleCommand.Auto, BattleCommand.Resolve], session.Allowed.Order());
            session.StartTurn();
        }
        Assert.False(session.TurnStartPending);
        Assert.Same(enemy, session.Current);
        var tick = Assert.Single(session.Sim.DotLog);
        Assert.Equal(("E", 5), (tick.Unit, tick.Damage));
        Assert.Equal(hpAfterStrike - 5, enemy.Hp);
    }

    /// <summary>Бой с удержанием доигрывается и без экрана: <see cref="BattleSession.AiStep"/> сам начинает отложенный ход, на ходу человека
    /// останавливается.</summary>
    [SkippableFact]
    public void AiStep_StartsPendingTurn_AndStopsAtPlayer()
    {
        var player = Unit("P", attacker: true, slot: 0, Strike);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        var state = new BattleState { Units = [player, enemy], AttackerGroup = "A", DefenderGroup = "D" };
        var session = Session(state, hold: true);

        session.Defend();
        Assert.True(session.TurnStartPending);
        session.AiStep();   // начало хода врага и его удар
        Assert.True(session.TurnStartPending);
        session.AiStep();   // начало хода человека — и стоп
        Assert.Same(player, session.Current);
        Assert.True(session.WaitsPlayer);
    }

    /// <summary>Пометка 16: «Ждать» тем же юнитом второй раз за раунд движок не даёт — <c>updateBattleActions</c> кладёт действие 3, только если
    /// бит 4 <c>waited</c> не взведён (<c>0x623ef6</c> → <c>0x622eaf</c>); снимает его новый раунд (<c>0x622078</c>). В следующем раунде — снова можно.</summary>
    [SkippableFact]
    public void Wait_OncePerRound_AgainNextRound()
    {
        var player = Unit("P", attacker: true, slot: 0, Strike);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        var state = new BattleState { Units = [player, enemy], AttackerGroup = "A", DefenderGroup = "D" };
        var session = Session(state, hold: false);

        Assert.Contains(BattleCommand.Wait, session.Allowed);
        Assert.True(session.Wait());
        while (!ReferenceEquals(session.Current, player)) session.AiStep();
        Assert.Equal(1, session.Round);
        Assert.DoesNotContain(BattleCommand.Wait, session.Allowed);
        Assert.False(session.Wait());

        session.Defend();
        while (!ReferenceEquals(session.Current, player)) session.AiStep();
        Assert.Equal(2, session.Round);
        Assert.Contains(BattleCommand.Wait, session.Allowed);
    }

    /// <summary>«Ждать» у двойного удара (<c>ATCK_TWICE</c>) — в конце раунда оба удара, как у движка (ранг 1 по листингу). Ячейка очереди
    /// несёт счётчик действий (<c>turnsOrder[i].attackCount</c>, <c>+0xe74</c>; в новом раунде — 2 у двойного, <c>0x626e30</c>).
    /// <c>CBatAttackWait::onHit</c> (<c>0x659e3e</c>) дописывает ячейку со счётчиком 1 (<c>push 1</c> на <c>0x659e45</c> → <c>0x6224dd</c>);
    /// конец <c>battleTurn</c> снимает с ходящего одно действие (<c>0x626519</c> → <c>0x622546</c>: 2 → 1), а для «Ждать» (вид 9,
    /// <c>0x62731a</c>) снимает по одному все оставшиеся у юнита действия, считая (<c>0x62552f</c>–<c>0x625559</c>: 1 + 1 = 2), и
    /// дописывает одну ячейку с этим числом (<c>0x62556f</c>). Итог — счётчик 2, тот же двойной ход.</summary>
    [SkippableFact]
    public void WaitOfTwice_GivesBothHits_AtRoundEnd()
    {
        var player = Unit("P", attacker: true, slot: 0, Strike, twice: true);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        var state = new BattleState { Units = [player, enemy], AttackerGroup = "A", DefenderGroup = "D" };
        var session = Session(state, hold: false);

        Assert.True(session.Wait());
        while (!ReferenceEquals(session.Current, player)) session.AiStep();
        Assert.Equal(1, session.Round);

        Assert.NotEmpty(session.Attack(enemy));
        Assert.Same(player, session.Current);   // второй удар того же хода
        Assert.NotEmpty(session.Attack(enemy));
        Assert.Equal(2, session.Round);   // третьего удара нет: раунд кончился, игрок с инициативой 90 снова первый
    }

    /// <summary>Ждущий с ещё одной ячейкой в очереди (внеочередной ход GiveAttack): движок сливает все его ячейки в одну в конце со
    /// счётчиком «оставшиеся + 1» (<c>0x625525</c>–<c>0x625574</c>) — у модели это его ходы подряд в конце очереди.</summary>
    [Fact]
    public void RequeueWaiter_MergesLaterEntries_ToTheEnd()
    {
        var w = Unit("W", attacker: true, slot: 0, Strike);
        var a = Unit("A", attacker: true, slot: 1, Strike);
        var b = Unit("B", attacker: false, slot: 0, Slow);
        var queue = new List<BattleUnit> { w, a, w, b };   // прошлый ход W (0), сейчас — 2, его же позднее ячеек нет
        BattleSim.RequeueWaiter(queue, 2, w);
        Assert.Equal(["W", "A", "W", "B", "W"], queue.Select(u => u.Id));

        queue = [a, w, b, w];   // ходит W (1) по внеочередной ячейке, его обычная — 3
        BattleSim.RequeueWaiter(queue, 1, w);
        Assert.Equal(["A", "W", "B", "W", "W"], queue.Select(u => u.Id));
    }

    /// <summary><c>updateBattleActions</c> (<c>0x623d93</c>): «Отступить» — только если группа может бежать (<c>0x6240e9</c> → <c>0x5f51bf</c>):
    /// гарнизон, страж руин, обороняющийся отряд в городе — нет.</summary>
    [SkippableFact]
    public void Retreat_NotOffered_WhenGroupCannotRetreat()
    {
        var enemy = Unit("A", attacker: true, slot: 0, Slow);
        var guard = Unit("G", attacker: false, slot: 0, Strike);
        var state = new BattleState { Units = [enemy, guard], AttackerGroup = "A", DefenderGroup = "D", DefendersCanRetreat = false };
        var session = Session(state, hold: false, playerIsAttacker: false);

        Assert.Same(guard, session.Current);
        Assert.Contains(BattleCommand.Defend, session.Allowed);
        Assert.DoesNotContain(BattleCommand.Retreat, session.Allowed);
    }

    /// <summary>Двойной удар (<c>ATCK_TWICE</c>): после первого удара (бит 5 <c>attackedOnceOfTwice</c>) ни «Защищаться» (<c>0x623ebb</c>), ни
    /// «Ждать» (<c>0x623f04</c>), ни «Отступить» (<c>0x6240fe</c>) — только второй удар.</summary>
    [SkippableFact]
    public void SecondHitOfTwice_OffersOnlyTheStrike()
    {
        var player = Unit("P", attacker: true, slot: 0, Strike, twice: true);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        var state = new BattleState { Units = [player, enemy], AttackerGroup = "A", DefenderGroup = "D" };
        var session = Session(state, hold: false);

        session.Attack(enemy);
        Assert.Same(player, session.Current);
        Assert.Equal([BattleCommand.Attack, BattleCommand.Skip, BattleCommand.Auto, BattleCommand.Resolve], session.Allowed.Order());
        Assert.False(session.Wait());
        Assert.False(session.Defend());    // 0x623ebb — команду не примет и модель, не только кнопка
        Assert.False(session.Retreat());   // 0x6240fe
        Assert.Same(player, session.Current);
        Assert.False(player.Defending);
    }

    /// <summary>Пометки 3 и 5: Лесная Дева — обереги (атака 1, без записи хода) и лечение второй атакой (запись). В показе основная атака
    /// раньше второй (у движка шаг показа по номерам атак, <c>0x63513a</c>); прежде обереги уходили в конец, и первым шло лечение.</summary>
    [Fact]
    public void WithAllies_FirstAttackWithoutRecord_GoesBeforeSecond()
    {
        var heal = new TurnRecord(1, "DRYAD", "KNIGHT", false, 0, 120, Attack: 2);
        var wards = new AllyNote(1, "DRYAD", "KNIGHT", AttackClass.BestowWards, Attack: 1);
        var cure = new AllyNote(1, "ABBESS", "KNIGHT", AttackClass.Cure, Attack: 2);
        var abbessHeal = new TurnRecord(1, "ABBESS", "KNIGHT", false, 0, 130);

        var dryad = ShowOrder.WithAllies([heal], [wards], _ => 120);
        Assert.Equal([1, 2], dryad.Select(r => r.Attack));
        Assert.All(dryad, r => Assert.Equal("DRYAD", r.Unit));

        var abbess = ShowOrder.WithAllies([abbessHeal], [cure], _ => 130);   // вторая атака — за первой
        Assert.Equal([1, 2], abbess.Select(r => r.Attack));

        var none = ShowOrder.WithAllies([], [wards], _ => 120);   // записей юнита нет — в конец
        Assert.Equal(1, Assert.Single(none).Attack);
    }

    /// <summary>Ход ИИ без записей хода (призыв, усиление): показ — по одной записи на основную атаку каждой пары «юнит → цель» применений
    /// шага, без урона (у движка такой ход ИИ — <c>bat_show step 0</c>, замах и звуки, как у человека: 20261016e-av 15978146).</summary>
    [Fact]
    public void Unrecorded_OneShowPerMainAttackTarget()
    {
        ActionNote[] acts =
        [
            new(2, "ELEM", "ELEM", 1, AttackClass.Summon),
            new(2, "ELEM", "ELEM", 1, AttackClass.Summon),   // второе место того же призыва — одна запись
            new(2, "HERB", "KNIGHT", 1, AttackClass.BoostDamage),
            new(2, "HERB", "KNIGHT", 2, AttackClass.Heal),   // вторая атака — не основная
        ];
        var shown = ShowOrder.Unrecorded(acts, id => id == "KNIGHT" ? 77 : 50);
        Assert.Equal([("ELEM", "ELEM", 50), ("HERB", "KNIGHT", 77)], shown.Select(r => (r.Unit, r.Target, r.TargetHpAfter)));
        Assert.All(shown, r => Assert.Equal((1, 0), (r.Attack, r.Damage)));
        Assert.Empty(ShowOrder.Unrecorded([], _ => 0));
    }

    /// <summary>Ин38: цель показа призыва — призванный (у движка <c>bat_show</c> призывателя с целью — новым юнитом, <c>TUCH</c> и пауза кода 9
    /// на его клетке, <c>0x63c902</c>; 20261016g-port-synth с seq 519049). Превращение себя (цель — сам, призванных нет) и призыв без призванного
    /// (отряд полон) — как были.</summary>
    [Fact]
    public void SummonTargets_ShowsOnSummonedUnit()
    {
        var spirit = Unit("S143UN004D", attacker: true, slot: 5, Strike);
        spirit.Summoned = true;
        spirit.SummonerId = "ELEM";
        spirit.Hp = 60;
        var other = Unit("OTHER", attacker: true, slot: 4, Strike);   // новый, но не призванный этим шагом

        var shown = ShowOrder.SummonTargets([new TurnRecord(2, "ELEM", "ELEM", false, 0, 95), new TurnRecord(2, "WOLF", "WOLF", false, 0, 80)],
                                            [other, spirit]);
        Assert.Equal([("ELEM", "S143UN004D", 60), ("WOLF", "WOLF", 80)], shown.Select(r => (r.Unit, r.Target, r.TargetHpAfter)));
        Assert.Equal("ELEM", Assert.Single(ShowOrder.SummonTargets([new TurnRecord(2, "ELEM", "ELEM", false, 0, 95)], [])).Target);
    }
}
