using D2.Rules.Battle;
using D2.Rules.Rng;

/// <summary>Классы атак первого круга Ф4 (Task 6) и то, что измерил корпус 20260906-b/c/e (Task 15):
/// лечение без бросков, вторая атака юнита, площадная цель с перемешиванием, тик и снятие DoT,
/// паралич на один ход. Двойная атака (ATCK_TWICE) — через BattleSim.Run, с перенацеливанием.</summary>
public class AttacksTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);

    [Fact] public void Damage_SubtractsHpAndNeverBelowZero()
    {
        var (a, t) = Pair(AttackClass.Damage, dam: 25);
        t.Hp = 10;
        var rng = new ScriptedRng([0, 0, 0]);
        var r = Attacks.Apply(a, a.Attack, t, rng, C, round: 1);
        Assert.False(r.Miss);
        Assert.Equal(25, r.Damage);
        Assert.Equal(0, t.Hp);
        Assert.Equal(3, rng.Used);   // 2 броска точности + 1 урона
    }

    /// <summary>Ранг 1 (corpus/20260906-b): у лекаря нет ни miss, ни power, ни rng(100) — лечение
    /// бросков не тратит. Прежняя гипотеза «два rng(100) при power 100» снята.</summary>
    [Fact] public void Heal_RaisesHpUpToMax_WithoutAnyRolls()
    {
        var (a, t) = Pair(AttackClass.Heal, dam: 0, heal: 50);
        t.Hp = 30;
        var rng = new ScriptedRng([]);
        var r = Attacks.Apply(a, a.Attack, t, rng, C, round: 1);
        Assert.False(r.Miss);
        Assert.Equal(50, r.Healed);
        Assert.Equal(80, t.Hp);
        Assert.Equal(0, rng.Used);
    }

    [Fact] public void Heal_DoesNotExceedHpMax()
    {
        var (a, t) = Pair(AttackClass.Heal, dam: 0, heal: 500);
        t.Hp = 90;
        Attacks.Apply(a, a.Attack, t, new ScriptedRng([]), C, round: 1);
        Assert.Equal(t.HpMax, t.Hp);
    }

    [Fact] public void Paralyze_SetsFlagOnHitAndNotOnMiss()
    {
        var (a, t) = Pair(AttackClass.Paralyze);
        var rng = new ScriptedRng([0, 0]);
        Attacks.Apply(a, a.Attack, t, rng, C, round: 1);
        Assert.True(t.Paralyzed);
        Assert.Equal(2, rng.Used);   // статусные классы: только два броска точности
        var (a2, t2) = Pair(AttackClass.Paralyze);
        Attacks.Apply(a2, a2.Attack, t2, new ScriptedRng([99, 99]), C, round: 1);
        Assert.False(t2.Paralyzed);
    }

    [Fact] public void Poison_AddsDotWithSecondaryDamage_NoImmediateDamage()
    {
        var (a, t) = Pair(AttackClass.Poison, dam: 15, infinite: true);
        var rng = new ScriptedRng([0, 0]);
        Attacks.Apply(a, a.Attack, t, rng, C, round: 3);
        Assert.Equal(2, rng.Used);
        Assert.Equal(100, t.Hp);     // немедленного урона нет — ранг 1 (20260906-e: 100 → 65 от удара 35)
        var dot = Assert.Single(t.Dots);
        Assert.Equal(AttackClass.Poison, dot.Class);
        Assert.Equal(15, dot.Damage);
        Assert.Equal(3, dot.AppliedRound);
        Assert.True(dot.Long);
    }

    /// <summary>checkLongEffectDuration: тик, затем rng(100)+1 &gt;= BAT_BREAK − BAT_BMODIF·(round − applied).
    /// Корпус 20260906-e: при 0 раундов 85 держит, 96 снимает; при 1 — 60 держит, 79 снимает.</summary>
    [Fact] public void Dots_TickThenRollAgainstBatBreak()
    {
        var (_, t) = Pair(AttackClass.Damage);
        t.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = true });
        var rng = new ScriptedRng([85, 96]);
        Attacks.TickDots(t, rng, C, round: 1); Assert.Equal(90, t.Hp); Assert.Single(t.Dots);   // 86 < 90
        Attacks.TickDots(t, rng, C, round: 1); Assert.Equal(80, t.Hp); Assert.Empty(t.Dots);    // 97 >= 90
        Assert.Equal(2, rng.Used);

        t.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = true });
        rng = new ScriptedRng([60, 79]);
        Attacks.TickDots(t, rng, C, round: 2); Assert.Single(t.Dots);   // порог 80: 61 держит
        Attacks.TickDots(t, rng, C, round: 2); Assert.Empty(t.Dots);    // 80 снимает
        Assert.Equal(60, t.Hp);
    }

    [Fact] public void Dots_NotLong_RemovedAfterFirstTickWithoutRoll_DeathTicksWithoutRoll()
    {
        var (_, t) = Pair(AttackClass.Damage);
        t.Dots.Add(new DotEffect { Class = AttackClass.Frostbite, Damage = 10, AppliedRound = 1, Long = false });
        var rng = new ScriptedRng([]);
        Attacks.TickDots(t, rng, C, round: 1);
        Assert.Equal(90, t.Hp); Assert.Empty(t.Dots); Assert.Equal(0, rng.Used);

        t.Hp = 5;
        t.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = true });
        Attacks.TickDots(t, rng, C, round: 1);
        Assert.Equal(0, t.Hp); Assert.Empty(t.Dots); Assert.Equal(0, rng.Used);   // умер от тика — броска нет
    }

    /// <summary>Fear заведён задачей 5 Ф6 (подробно — AttackClassTests, на вани́льных данных): два
    /// броска точности и ничего сверх них, попадание ставит намерение бежать. Здесь остаётся
    /// проверка расхода бросков — она и была в Ф4, менять её не за что.</summary>
    [Fact] public void Fear_SpendsExactlyTwoRolls_AndOnHitSetsRetreat()
    {
        var (a, t) = Pair(AttackClass.Fear);
        var rng = new ScriptedRng([99, 99]);
        Assert.True(Attacks.Apply(a, a.Attack, t, rng, C, 1).Miss);
        Assert.False(t.Retreating);
        Assert.Equal(2, rng.Used);

        var hit = new ScriptedRng([0, 0]);
        Assert.True(Attacks.Apply(a, a.Attack, t, hit, C, 1).Applied);
        Assert.True(t.Retreating);
        Assert.Equal(2, hit.Used);
    }

    /// <summary>Все двадцать три класса LattC реализованы (задача 8 закрыла последние два), поэтому
    /// отказ остался только для класса из мода — значения CLASS, которого в LattC нет. Проверяем
    /// именно это: текст называет класс и его номер, а костей до отказа не тратится ни одной.</summary>
    [Fact] public void UnknownModClass_ThrowsWithClassNumber()
    {
        var (a, t) = Pair((AttackClass)99);
        var rng = new ScriptedRng([0, 0]);
        var ex = Assert.Throws<NotSupportedException>(() => Attacks.Apply(a, a.Attack, t, rng, C, 1));
        Assert.Contains("99", ex.Message);
        Assert.Equal(0, rng.Used);
    }

    /// <summary>Ранг 1 (20260906-e, Master Thug 35 + яд 20): вторая атака после первой, по живой
    /// цели попадания, своими двумя бросками; после промаха или убившего удара — нет.</summary>
    [Fact] public void Act_SecondAttackFollowsFirstHitOnLivingTargetOnly()
    {
        var (a, t) = Pair(AttackClass.Damage, dam: 35, second: (AttackClass.Poison, 20));
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a, t] };
        var rng = new ScriptedRng([0, 0, 0, 10, 20]);
        var log = new BattleSim(state, rng, C).Act(1, a, t);
        Assert.Equal([
            new TurnRecord(1, "A", "T", false, 35, 65),
            new TurnRecord(1, "A", "T", false, 0, 65, Attack: 2),
        ], log);
        Assert.Equal(5, rng.Used);
        Assert.Single(t.Dots);

        t.Dots.Clear();
        rng = new ScriptedRng([99, 99]);                                       // промах первой — второй нет
        Assert.Equal([new TurnRecord(1, "A", "T", true, 0, 65)], new BattleSim(state, rng, C).Act(1, a, t));
        Assert.Equal(2, rng.Used);

        t.Hp = 10;
        rng = new ScriptedRng([0, 0, 0]);                                      // убил — второй нет
        Assert.Equal([new TurnRecord(1, "A", "T", false, 35, 0)], new BattleSim(state, rng, C).Act(1, a, t));
        Assert.Equal(3, rng.Used);
        Assert.Empty(t.Dots);
    }

    /// <summary>Площадная атака (REACH 1): n − 1 бросков rng(1)…rng(n − 1) по всей группе,
    /// swap(L[i], L[rng(i)]), L — обратный порядок группы в состоянии боя (задача 7: 76 площадных
    /// ходов корпуса из 76); мёртвые в группе считаются, бьются живые. Ранг 1 по числу бросков и
    /// перестановке трёх целей (corpus/20260906-c).</summary>
    [Fact] public void Act_AreaAttackShufflesWholeEnemyGroupThenHitsLiving()
    {
        var a = Unit("A", true, AttackClass.Damage, reach: AttackType.ReachAll);
        var t1 = Unit("T1", false); var t2 = Unit("T2", false); var t3 = Unit("T3", false); var dead = Unit("T0", false);
        dead.Hp = 0;
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a, t3, t2, t1, dead] };
        // Группа в состоянии [T3, T2, T1, T0], L — обратный порядок [T0, T1, T2, T3];
        // i=1: rng(1)=0 → swap(1,0) → [T1,T0,T2,T3]; i=2: rng(2)=1 → swap(2,1) → [T1,T2,T0,T3];
        // i=3: rng(3)=0 → swap(3,0) → [T3,T2,T0,T1]; живые по порядку: T3, T2, T1.
        var rng = new ScriptedRng([0, 1, 0, /*T3*/ 0, 0, 0, /*T2*/ 0, 0, 1, /*T1*/ 99, 99]);
        var log = new BattleSim(state, rng, C).Act(1, a);
        Assert.Equal(["T3", "T2", "T1"], log.Select(r => r.Target));
        Assert.Equal([25, 26, 0], log.Select(r => r.Damage));
        Assert.Equal(11, rng.Used);
    }

    /// <summary>Штраф мага-лидера (задача 7, ранг 1: GOG 0x627db1 и 0x625244): точность по цели —
    /// база − 10 · место цели в перемешанной группе, мёртвые места считаются. У обычного юнита тот же
    /// удар теми же бросками попадает дважды.</summary>
    [Fact] public void Act_MageLeaderLosesTenPowerPerPlaceInShuffledGroup()
    {
        foreach (var (typeId, missed) in new[] { ("g000uu0021", true), ("g000uu0001", false) })
        {
            var a = Unit("A", true, AttackClass.Damage, reach: AttackType.ReachAll);
            a.Type = a.Type with { Id = typeId };
            var e1 = Unit("E1", false); var e2 = Unit("E2", false); var dead = Unit("E0", false);
            dead.Hp = 0;
            var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a, e1, dead, e2] };
            // L = [E2, E0, E1]; rng(1)=0 → [E0, E2, E1]; rng(2)=1 → [E0, E1, E2]: E1 на месте 1, E2 на месте 2.
            // Среднее 75 против 80 − 10 = 70 и 65 против 80 − 20 = 60 — у мага оба промаха.
            var rng = new ScriptedRng(missed ? [0, 1, 75, 75, 65, 65] : [0, 1, 75, 75, 0, 65, 65, 0]);
            var log = new BattleSim(state, rng, C).Act(1, a);
            Assert.Equal(["E1", "E2"], log.Select(r => r.Target));
            Assert.All(log, r => Assert.Equal(missed, r.Miss));
            Assert.Equal(missed ? 6 : 8, rng.Used);
        }
    }

    /// <summary>Вторая атака-яд по отступающему не кладётся и бросков не тратит (canPerform Poison
    /// 0x65e1ef, бит 29), а паралич — кладётся (у Paralyze такой проверки нет). Корпус:
    /// 20260911-passive/passive-001 [393173] и seq 778765.</summary>
    [Fact] public void Act_SecondAttackPoisonSkipsRetreatingTarget_ParalyzeDoesNot()
    {
        var (a, t) = Pair(AttackClass.Damage, second: (AttackClass.Poison, 10));
        t.Retreating = true;
        var rng = new ScriptedRng([0, 0, 0]);
        var log = new BattleSim(new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a, t] }, rng, C).Act(1, a, t);
        Assert.Single(log);
        Assert.Empty(t.Dots);
        Assert.Equal(3, rng.Used);

        var (a2, t2) = Pair(AttackClass.Damage, second: (AttackClass.Paralyze, 0));
        t2.Retreating = true;
        var rng2 = new ScriptedRng([0, 0, 0, 0, 0]);
        new BattleSim(new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a2, t2] }, rng2, C).Act(1, a2, t2);
        Assert.True(t2.Paralyzed);
        Assert.Equal(5, rng2.Used);
    }

    /// <summary>Лечение всех (Oracle Elf, 20260906-b): броски перемешивания по своей группе есть
    /// (rng(1), rng(2), rng(3) при четырёх), лечение без бросков, записи только с healed &gt; 0 по id.</summary>
    [Fact] public void Act_HealAllRollsShuffleOnly_RecordsHealedByIdOrder()
    {
        var h = Unit("H", true, AttackClass.Heal, reach: AttackType.ReachAll, heal: 30);
        var full = Unit("A1", true); var w2 = Unit("A2", true); var w3 = Unit("A3", true); var enemy = Unit("E", false);
        w2.Hp = 50; w3.Hp = 90;
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [h, full, w2, w3, enemy] };
        var rng = new ScriptedRng([0, 0, 0]);
        var log = new BattleSim(state, rng, C).Act(1, h);
        Assert.Equal([new TurnRecord(1, "H", "A2", false, 0, 80), new TurnRecord(1, "H", "A3", false, 0, 100)], log);
        Assert.Equal(3, rng.Used);
    }

    [Fact] public void Run_ParalyzedUnitSkipsItsTurnOnce_ThenActs()
    {
        var (a, t) = Pair(AttackClass.Damage);
        t.Paralyzed = true;
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 0, Units = [a, t] };
        var rng = new ScriptedRng([
            0, 0, 0, 0,    // раунд 1: две очереди; ничья — A первый
            0, 0, 0,       // A: 25; T парализован — пропускает без бросков
            0, 0,          // раунд 2
            0, 0, 0,       // A: 25
            0, 0, 0,       // T: ходит
        ]);
        var sim = new BattleSim(state, rng, C);
        var log = sim.Run(maxRounds: 2);
        Assert.Equal([
            new TurnRecord(1, "A", "T", false, 25, 75),
            new TurnRecord(2, "A", "T", false, 25, 50),
            new TurnRecord(2, "T", "A", false, 25, 75),
        ], log);
        Assert.False(t.Paralyzed);
        Assert.Equal(15, rng.Used);
    }

    [Fact] public void BeginTurn_SecondCallInSameRound_DoesNotTickAgain()
    {
        var (a, t) = Pair(AttackClass.Damage);
        t.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = true });
        // Обе стороны живы: иначе бой окончен и BeginTurn закрывает его (BattleSim.Finished).
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a, t] };
        var rng = new ScriptedRng([0]);
        var sim = new BattleSim(state, rng, C);
        Assert.True(sim.BeginTurn(t)); Assert.Equal(90, t.Hp);
        Assert.True(sim.BeginTurn(t)); Assert.Equal(90, t.Hp);
        Assert.Equal(1, rng.Used);
        // Журнал тиков для показа (звук POISON на экране боя): один тик, второй вызов в том же раунде — нет.
        Assert.Equal(new DotTick(1, t.Id, AttackClass.Poison, 10, 90), Assert.Single(sim.DotLog));
    }

    [Fact] public void DotLog_StopsAtDeath_ByClassOrder()
    {
        var (a, t) = Pair(AttackClass.Damage);
        t.Hp = 15;
        t.Dots.Add(new DotEffect { Class = AttackClass.Blister, Damage = 10, AppliedRound = 1, Long = false });
        t.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = false });
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [a, t] };
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        Assert.False(sim.BeginTurn(t));   // яд (класс 12) — первым, ожог (23) добивает
        Assert.Equal([(AttackClass.Poison, 5), (AttackClass.Blister, 0)], sim.DotLog.Select(d => (d.Class, d.HpAfter)));
        Assert.Equal(0, t.Hp);
    }

    [Fact] public void Run_AttackTwiceUnitHitsTwicePerTurn()
    {
        var (a, t) = Pair(AttackClass.Damage, attackTwice: true);
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 0, Units = [a, t] };
        var rng = new ScriptedRng([
            0, 0, 0, 0,    // раунд 1: две очереди по два броска; 50 = 50 — ничья, атакующий A первый
            0, 0, 0,       // A, удар 1: попал, 25
            0, 0, 1,       // A, удар 2: попал, 26
            0, 0, 0,       // T: попал, 25
            0, 0,          // раунд 2: очередь
            90, 90,        // A, удар 1: промах
            0, 0, 2,       // A, удар 2: попал, 27
            0, 0, 0,       // T: попал, 25
        ]);
        var log = new BattleSim(state, rng, C).Run(maxRounds: 2);
        Assert.Equal(2, Attacks.AttacksPerTurn(a));
        Assert.Equal(1, Attacks.AttacksPerTurn(t));
        Assert.Equal([
            new TurnRecord(1, "A", "T", false, 25, 75),
            new TurnRecord(1, "A", "T", false, 26, 49),
            new TurnRecord(1, "T", "A", false, 25, 75),
            new TurnRecord(2, "A", "T", true, 0, 49),
            new TurnRecord(2, "A", "T", false, 27, 22),
            new TurnRecord(2, "T", "A", false, 25, 50),
        ], log);
        Assert.Equal(23, rng.Used);
    }

    /// <summary>Защищающаяся цель получает урон по броне 50 (0 → 50 при DFENDBONUS 50: 25 − 12 = 13),
    /// статус снимается в начале её собственного хода (BeginTurn) и ход она бьёт по-обычному.</summary>
    [Fact] public void Run_DefendingTargetTakesReducedDamage_StatusClearsAtItsOwnTurn()
    {
        var (a, t) = Pair(AttackClass.Damage);
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 0, Units = [a, t] };
        new BattleSim(state, new ScriptedRng([]), C).Defend(t);
        Assert.True(t.Defending);
        var rng = new ScriptedRng([
            0, 0, 0, 0,    // раунд 1: две очереди; ничья — A первый
            0, 0, 0,       // A: попал, 25 − 25·50/100 = 13
            0, 0, 0,       // T: BeginTurn снял защиту, бьёт A на 25
        ]);
        var log = new BattleSim(state, rng, C).Run(maxRounds: 1);
        Assert.Equal([
            new TurnRecord(1, "A", "T", false, 13, 87),
            new TurnRecord(1, "T", "A", false, 25, 75),
        ], log);
        Assert.False(t.Defending);
        Assert.Equal(10, rng.Used);
    }

    [Fact] public void Run_AttackTwiceSecondHitRetargetsWhenFirstKills()
    {
        var (a, t1) = Pair(AttackClass.Damage, attackTwice: true);
        var t2 = Unit("T2", false, slot: 1);
        t1.Hp = 10;
        var state = new BattleState { AttackerGroup = "G1", DefenderGroup = "G2", Round = 0, Units = [a, t1, t2] };
        var rng = new ScriptedRng([
            0, 0, 0, 0, 0, 0,   // раунд 1: две очереди по три броска; ничья 50/50/50 — A (атакующий), T, T2
            0, 0, 0,            // A, удар 1: T 10 -> 0
            0, 0, 0,            // A, удар 2: перенацелен на T2, 100 -> 75
            0, 0, 0,            // T2 бьёт A: 100 -> 75
        ]);
        var log = new BattleSim(state, rng, C).Run(maxRounds: 1);
        Assert.Equal([
            new TurnRecord(1, "A", "T", false, 25, 0),
            new TurnRecord(1, "A", "T2", false, 25, 75),
            new TurnRecord(1, "T2", "A", false, 25, 75),
        ], log);
        Assert.Equal(15, rng.Used);
    }

    /// <summary>Задача 15 Ф7: у Damage с <c>CRIT_HIT</c> крит — 5 % от dwb поверх урона после брони,
    /// отдельным выходом (<c>computeDamage</c> <c>0x65b9b0</c>): удар 60 → урон 60, крит 3, HP 100 → 37.
    /// Бросков те же три, лечения без модификатора нет (<c>CAttackImpl::getDrain</c> <c>0x57360d</c> — ноль).</summary>
    [Fact] public void Damage_CritHit_AddsFivePercentOfDwbOnTop()
    {
        var (a, t) = Pair(AttackClass.Damage, dam: 60);
        a.Attack = a.Attack with { CritHit = true };
        var rng = new ScriptedRng([0, 0, 0]);
        var r = Attacks.Apply(a, a.Attack, t, rng, C, round: 1);
        Assert.Equal((60, 3, 37, 0, 3), (r.Damage, r.CritDamage, t.Hp, r.DrainHealed, rng.Used));
    }

    /// <summary>Задача 17 Ф7: <c>Damage::onHit</c> зовёт <c>attack-&gt;getDrain(hpLost)</c> (<c>0x65b67b</c>).
    /// Цель 103 HP, удар 100 + крит 5 → потеряно 103 (добивание), вампиризм 50 % → +51 атакующему:
    /// не 52 (от суммы 105) и не 50 (без крита).</summary>
    [Fact] public void Damage_ModifierDrain_HealsFromHpLost_IncludingCritAndKill()
    {
        var drain = new D2.Rules.Units.Modifier("drain50",
            [new D2.Rules.Units.ModifierElement(D2.Rules.Units.ModifierElementType.AttackDrain, Percent: 50)]);
        var (plain, t) = Pair(AttackClass.Damage, dam: 100);
        var a = new BattleUnit
        {
            Id = "A", Type = plain.Type, Attack = plain.Attack with { CritHit = true }, Attacker = true, Slot = 0,
            HpMax = 100, Hp = 40, Modifiers = [drain],
        };
        t.HpMax = t.Hp = 103;
        var r = Attacks.Apply(a, a.Attack, t, new ScriptedRng([0, 0, 0]), C, round: 1);
        Assert.Equal((100, 5, 0), (r.Damage, r.CritDamage, t.Hp));
        Assert.Equal((51, 91), (r.DrainHealed, a.Hp));
    }

    private static BattleUnit Unit(string id, bool attacker, AttackClass cls = AttackClass.Damage, int dam = 25, int heal = 0,
                                   int reach = 3, bool twice = false, bool infinite = false, int slot = 0,
                                   (AttackClass cls, int dam)? second = null) => new()
    {
        Id = id,
        Type = new UnitType("g000uu0001", 1, 100, 0, "g000aa0001", second is null ? "g000000000" : "g000aa0002", twice, true, 20, 80),
        Attack = new AttackType("g000aa0001", 50, cls, 80, reach, dam, heal, false, 0, infinite),
        Attack2 = second is { } s ? new AttackType("g000aa0002", 50, s.cls, 80, reach, s.dam, 0, false, 0, true) : null,
        Attacker = attacker, Slot = slot, HpMax = 100, Hp = 100,
    };

    private static (BattleUnit attacker, BattleUnit target) Pair(AttackClass cls, int dam = 25, int heal = 0, bool attackTwice = false,
                                                                 bool infinite = false, (AttackClass cls, int dam)? second = null) =>
        (Unit("A", true, cls, dam, heal, twice: attackTwice, infinite: infinite, second: second), Unit("T", false));
}
