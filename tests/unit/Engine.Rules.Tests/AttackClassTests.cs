using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using Xunit.Abstractions;

/// <summary>
/// Задача 5 фазы Ф6: первый круг классов атак без урона — <c>Fear</c> (7), <c>Petrify</c> (9),
/// <c>Cure</c> (16), <c>BoostDamage</c> (8), <c>LowerDamage</c> (10), <c>LowerInitiative</c> (11).
/// Все юниты, атаки и константы — настоящие, из живой ванили (<c>Gunits</c>, <c>Gattacks</c>,
/// <c>GVars</c>); синтетики здесь нет вовсе.
///
/// <para><b>Счёт бросков утверждается у каждого класса</b> — это несущая величина: С2 воспроизводит
/// бой шаг в шаг по журнальным броскам, и один лишний или недостающий <c>rng</c> сдвигает весь
/// поток. Измерено (ранг 1, corpus/20260906-c, seq 53135-53147): <c>Fear</c> тратит ровно два
/// <c>rng(100)</c> на цель и НИ ОДНОГО сверх них; уход с поля не тратит ни одного
/// (seq 53197 → 53198). Классы без точности (<c>Cure</c>, <c>BoostDamage</c>) не тратят ничего —
/// ранг 2 по <c>attackHasPower</c>, подпёртый рангом 1 у <c>Heal</c>.</para>
///
/// <para>Игра не запускалась: журнала с окаменением, лечением статусов, бустом и снижениями пока
/// нет вовсе, поэтому всё, кроме <c>Fear</c>, — ранг 2/3 до прогона задачи 13. Здесь проверяется
/// ровно то, что модель делает то, что о ней написано.</para>
/// </summary>
public class AttackClassTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    // Ванильные участники, названные поимённо (перебор Gunits × Gattacks по классу атаки):
    private const string Shamaness = "base:unit/shamaness-neutral";     // ATTACK2 — Fear
    private const string FearBite = "base:attack/shamaness-neutral-2nd";      // Fear, POWER 30, REACH 1, SOURCE 1, INFINITE = T
    private const string Medusa = "base:unit/medusa";        // ATTACK — Petrify
    private const string PetrifyGaze = "base:attack/medusa";   // Petrify, POWER 60, REACH 1, SOURCE 6
    private const string Matriarch = "base:unit/matriarch";     // ATTACK2 — Cure
    private const string CureTouch = "base:attack/matriarch-2nd";     // Cure, POWER 100, REACH 1
    private const string Tenderfoot = "base:unit/tenderfoot";    // ATTACK — BoostDamage LEVEL 1
    private const string Boost1 = "base:attack/tenderfoot";        // BoostDamage, LEVEL 1 (+25 %)
    private const string Boost2 = "base:attack/novice";        // BoostDamage, LEVEL 2 (+50 %)
    private const string Boost3 = "base:attack/druidess";        // BoostDamage, LEVEL 3 (+75 %), INFINITE = T
    private const string Boost4 = "base:attack/archdruidess";        // BoostDamage, LEVEL 4 (+100 %), INFINITE = T
    private const string Tiamath = "base:unit/tiamath-legions";       // ATTACK2 — LowerDamage
    private const string LowerDam = "base:attack/tiamath-legions-2nd";      // LowerDamage, LEVEL 2 (−33 %), INFINITE = T
    private const string Hermit = "base:unit/hermit-clans";        // ATTACK2 — LowerInitiative
    private const string LowerIni = "base:attack/hermit-clans-2nd";      // LowerInitiative, LEVEL 1 (−50 %), INFINITE = F
    private const string Squire = "base:unit/squire-empire";        // Damage, QTY_DAM 25, ARMOR 0 — мишень и боец

    private static RulesConstants C => Rules.Value.Constants;

    private static BattleUnit Of(string typeId, string? attackId = null, bool attacker = false, int slot = 0)
    {
        var type = Rules.Value.Units[typeId];
        return new BattleUnit
        {
            Id = typeId, Type = type,
            Attack = attackId is null ? Rules.Value.AttackOf(type)! : Rules.Value.Attacks[attackId],
            Attacker = attacker, Slot = slot, HpMax = type.HitPoints, Hp = type.HitPoints,
        };
    }

    private static AttackType Attack(string id) => Rules.Value.Attacks[id];

    /// <summary>Броски, гарантирующие попадание (среднее 0 не больше любой точности) и промах.</summary>
    private static readonly int[] Hit = [0, 0];
    private static readonly int[] Miss = [99, 99];

    // ---- ванильные числа, на которых стоят все остальные утверждения ---------------------------

    /// <summary>GVars.dbf — ранг 1: проценты берутся из таблицы игры, а не зашиты в правилах.</summary>
    [SkippableFact] public void Percents_ComeFromGVars()
    {
        ImportedData.RequireRules();
        Assert.Equal(new[] { 25, 50, 75, 100 }, Enumerable.Range(1, 4).Select(C.BoostDamagePercent).ToArray());
        Assert.Equal(new[] { 50, 33 }, Enumerable.Range(1, 2).Select(C.LowerDamagePercent).ToArray());
        Assert.Equal(50, C.LowerInitiativePercent(1));
        // Уровень вне диапазона — ноль, а не крайний элемент (getBoostDamage/getLowerInitiative).
        Assert.Equal(0, C.BoostDamagePercent(0));
        Assert.Equal(0, C.BoostDamagePercent(5));
        Assert.Equal(0, C.LowerDamagePercent(3));
        Assert.Equal(0, C.LowerInitiativePercent(2));
        output.WriteLine($"BATBOOSTD1..4 = {string.Join('/', Enumerable.Range(1, 4).Select(C.BoostDamagePercent))}, " +
                         $"BATLOWERD1..2 = {string.Join('/', Enumerable.Range(1, 2).Select(C.LowerDamagePercent))}, " +
                         $"BATLOWERI1 = {C.LowerInitiativePercent(1)}");
    }

    /// <summary>Уровни атак ванили: у буста есть все четыре, у снижения урона — только второй,
    /// у снижения инициативы — только первый. Если данные скажут иначе, тесты ниже лгут.</summary>
    [SkippableFact] public void VanillaAttackLevels_AreWhatTheTestsAssume()
    {
        ImportedData.RequireRules();
        Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { Boost1, Boost2, Boost3, Boost4 }.Select(a => Attack(a).Level).ToArray());
        Assert.Equal(2, Attack(LowerDam).Level);
        Assert.Equal(1, Attack(LowerIni).Level);
        Assert.Equal(AttackType.ReachAll, Attack(FearBite).Reach);
        Assert.Equal(30, Attack(FearBite).Power);
        // Squire — мишень и боец всех числовых утверждений ниже: урон 25, броня 0, инициатива 50.
        Assert.Equal(25, Attack("base:attack/squire-empire").QtyDamage);
        Assert.Equal(50, Attack("base:attack/squire-empire").Initiative);
        Assert.Equal(0, Rules.Value.Units[Squire].Armor);
    }

    // ---- Fear (7) ------------------------------------------------------------------------------

    /// <summary>Ранг 1 (corpus/20260906-c, seq 53135-53147): три цели площадного Fear — три пары
    /// rng(100), два промаха и одно попадание; попадание ставит цели ровно бит 29 (st = 0x20000000)
    /// и не тратит ни одного броска сверх пары на точность. Бит 4 DisableLong не взводится, хотя у
    /// самой атаки INFINITE = T.</summary>
    [SkippableFact] public void Fear_OnHit_SetsRetreat_AndSpendsExactlyTwoRolls()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Shamaness), Of(Squire, attacker: true));
        var rng = new ScriptedRng(Hit);
        var r = Attacks.Apply(a, Attack(FearBite), t, rng, C, round: 1);

        Assert.True(r.Applied);
        Assert.True(t.Retreating);
        Assert.False(t.Retreated);      // с поля уходит в свой ход, не сразу
        Assert.True(t.OnField);
        Assert.Equal(2, rng.Used);      // ровно точность, ничего сверх
    }

    [SkippableFact] public void Fear_OnMiss_ChangesNothing_AndStillSpendsTwoRolls()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Shamaness), Of(Squire, attacker: true));
        var rng = new ScriptedRng(Miss);
        var r = Attacks.Apply(a, Attack(FearBite), t, rng, C, round: 1);

        Assert.True(r.Miss);
        Assert.False(t.Retreating);
        Assert.Equal(2, rng.Used);
    }

    /// <summary>Ранг 1 (corpus/20260906-c, seq 53197 turn_before → 53198 turn_after той же цели):
    /// 0x20000000 → 0x10000000, ни battle_turn, ни единого броска между ними, из order жертва
    /// исчезает. Ход при этом пропускается — BeginTurn возвращает false.</summary>
    [SkippableFact] public void Fear_VictimLeavesFieldOnItsOwnTurn_WithoutAnyRoll()
    {
        ImportedData.RequireRules();
        var victim = Of(Squire, attacker: true);
        victim.Retreating = true;
        var state = new BattleState
        {
            Units = [victim, Of(Shamaness, slot: 1)],
            AttackerGroup = "A", DefenderGroup = "D", Round = 1,
        };
        var rng = new ScriptedRng([]);
        var sim = new BattleSim(state, rng, C);

        Assert.False(sim.BeginTurn(victim));
        Assert.False(victim.Retreating);
        Assert.True(victim.Retreated);
        Assert.True(victim.Alive);       // жив
        Assert.False(victim.OnField);    // но для боя его нет
        Assert.Equal(0, rng.Used);
    }

    /// <summary>Отступление ИНДИВИДУАЛЬНОЕ, а не групповое — ранг 1, corpus/20260906-c, бой 1:
    /// площадной Fear попал по одной цели из трёх, ушла с поля она одна, а двое её живых союзников
    /// (hp 11 и 15) остались в очереди и продолжили бой до самой смерти. Групповое решение
    /// (<c>RetreatStatus::FullRetreat</c>, A battlemsgdata.h:90-95) пометило бы всех сразу; здесь
    /// оно не наблюдается ни разу, и survey §7 в этой строке говорит о другом механизме —
    /// о добровольном отступлении отряда по команде игрока, а не о статусе от страха.</summary>
    [SkippableFact] public void Fear_RetreatIsIndividual_AlliesKeepFighting()
    {
        ImportedData.RequireRules();
        var feared = Of(Squire, attacker: true, slot: 0);
        var ally = Of(Squire, attacker: true, slot: 1);
        var enemy = Of(Shamaness, slot: 2);
        var state = new BattleState
        {
            Units = [feared, ally, enemy], AttackerGroup = "A", DefenderGroup = "D", Round = 1,
        };
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        feared.Retreating = true;
        sim.BeginTurn(feared);

        Assert.False(feared.OnField);
        Assert.True(ally.OnField);
        Assert.Null(sim.Winner);   // сторона держится союзником, хотя один ушёл

        // Очередь следующего раунда: бросок только на оставшихся (ранг 1, seq 53208-53210).
        var order = new ScriptedRng([0, 0]);
        var built = TurnOrder.Build(state, order, C);
        Assert.Equal(2, order.Used);
        Assert.DoesNotContain(feared, built);
    }

    /// <summary>Сторона, где все либо мертвы, либо отступили, бой проиграла — ранг 1
    /// (corpus/20260906-c: жертва ушла с hp 118, союзники погибли, движок дал battle_end).</summary>
    [SkippableFact] public void Fear_SideWithOnlyRetreatedUnitsLosesTheBattle()
    {
        ImportedData.RequireRules();
        var runaway = Of(Squire, attacker: true);
        var winner = Of(Shamaness, slot: 1);
        var state = new BattleState
        {
            Units = [runaway, winner], AttackerGroup = "A", DefenderGroup = "D", Round = 1,
        };
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        Assert.Null(sim.Winner);
        runaway.Retreated = true;
        Assert.Equal("D", sim.Winner);
    }

    // ---- Petrify (9) ---------------------------------------------------------------------------

    /// <summary>Окаменение — отдельный статус (A), а не паралич (B): ранг 2, выбор сделан по данным
    /// движка, потому что корпус слеп — бит 3 не встретился ни разу ни в одном из восьми каталогов
    /// corpus/20260906-*. Бросков — два (класс с точностью), как у паралича, ранг 2.</summary>
    [SkippableFact] public void Petrify_SetsOwnStatus_NotParalyze_AndSpendsTwoRolls()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Medusa), Of(Squire, attacker: true));
        var rng = new ScriptedRng(Hit);
        Attacks.Apply(a, Attack(PetrifyGaze), t, rng, C, round: 1);

        Assert.True(t.Petrified);
        Assert.False(t.Paralyzed);   // именно отдельный бит, а не сведение к параличу
        Assert.Equal(2, rng.Used);
    }

    /// <summary>Ранг 3: длительность взята у паралича (пропуск ближайшего хода со снятием в нём же),
    /// потому что измерить её нечем.</summary>
    [SkippableFact] public void Petrify_SkipsNearestTurnAndClearsThere()
    {
        ImportedData.RequireRules();
        var stone = Of(Squire, attacker: true);
        stone.Petrified = true;
        var state = new BattleState
        {
            Units = [stone, Of(Shamaness, slot: 1)], AttackerGroup = "A", DefenderGroup = "D", Round = 1,
        };
        var rng = new ScriptedRng([]);
        var sim = new BattleSim(state, rng, C);

        Assert.False(sim.BeginTurn(stone));
        Assert.False(stone.Petrified);
        Assert.Equal(0, rng.Used);
        state.Round = 2;
        Assert.True(sim.BeginTurn(stone));   // следующий ход уже свой
    }

    // ---- Cure (16) -----------------------------------------------------------------------------

    /// <summary>Cure бросков не тратит вовсе — класс без точности. Ранг 1 (задача 22 Ф7):
    /// <c>onHit</c> <c>0x6600c8</c> сразу снимает только яды и ставит Cured; паралич и окаменение
    /// снимаются в начале следующего хода по биту Cured (<c>0x628874</c>); страх и оба снижения
    /// «Исцеление» не трогает вовсе.</summary>
    [SkippableFact] public void Cure_ClearsDotsNow_DisableAtNextTurn_KeepsFearAndLowerings()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Matriarch), Of(Squire, attacker: true));
        t.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 20, AppliedRound = 1, Long = true });
        t.Paralyzed = t.Petrified = t.Retreating = true;
        t.LowerDamageLevel = 2; t.LowerDamageLong = true;
        t.LowerInitiativeLevel = 1;

        var rng = new ScriptedRng([]);
        var r = Attacks.Apply(a, Attack(CureTouch), t, rng, C, round: 1);

        Assert.True(r.Applied);
        Assert.Empty(t.Dots);
        Assert.True(t.Paralyzed);            // снимется в начале следующего хода
        Assert.True(t.Petrified);
        Assert.True(t.Retreating);           // страх не лечится
        Assert.Equal(2, t.LowerDamageLevel); // снижение урона не лечится
        Assert.Equal(1, t.LowerInitiativeLevel);
        Assert.True(t.Cured);
        Assert.Equal(0, rng.Used);

        var state = new BattleState { Units = [a, t], AttackerGroup = "A", DefenderGroup = "D", Round = 1 };
        new BattleSim(state, new ScriptedRng([]), C).BeginTurn(a);
        Assert.False(t.Paralyzed);
        Assert.False(t.Petrified);
        Assert.False(t.Cured);
        Assert.True(t.Retreating);
    }

    /// <summary>Ушедшего с поля Cure не возвращает: бит 28 — уже свершившийся факт, а не статус,
    /// который лечат. Ранг 3, как и всё вокруг Cured.</summary>
    [SkippableFact] public void Cure_DoesNotBringBackAlreadyRetreatedUnit()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Matriarch), Of(Squire, attacker: true));
        t.Retreated = true;
        var r = Attacks.Apply(a, Attack(CureTouch), t, new ScriptedRng([]), C, round: 1);

        Assert.False(r.Applied);     // снимать было нечего
        Assert.True(t.Retreated);
        Assert.False(t.OnField);
    }

    // ---- BoostDamage (8) -----------------------------------------------------------------------

    /// <summary>Буст бросков не тратит, кладёт уровень из Gattacks.LEVEL и Long из INFINITE.</summary>
    [SkippableFact] public void BoostDamage_SetsLevelFromAttack_WithoutAnyRoll()
    {
        ImportedData.RequireRules();
        foreach (var (id, level, @long) in new[] { (Boost1, 1, false), (Boost2, 2, false), (Boost3, 3, true), (Boost4, 4, true) })
        {
            var (a, t) = (Of(Tenderfoot), Of(Squire, attacker: true));
            var rng = new ScriptedRng([]);
            Attacks.Apply(a, Attack(id), t, rng, C, round: 1);
            Assert.Equal(level, t.BoostDamageLevel);
            Assert.Equal(@long, t.BoostDamageLong);
            Assert.Equal(0, rng.Used);
        }
    }

    /// <summary>Буст входит в урон ровно через параметр computeDamageWithBuffs — отдельным
    /// усечённым слагаемым от базы (Formulas.DamageWithBuffs), а не слоем цепочки модификаторов:
    /// Squire бьёт на 25, при уровне 1 (+25 %) это 31 (25 + 25·25/100), при уровне 4 (+100 %) — 50.
    /// Броня цели 0, случайная добавка rng(BAT_DAMAGE) = 0.</summary>
    [SkippableFact] public void BoostDamage_MultipliesDamageThroughDamageWithBuffs()
    {
        ImportedData.RequireRules();
        Assert.Equal(25, DamageOfSquire(boostLevel: 0));
        Assert.Equal(31, DamageOfSquire(boostLevel: 1));
        Assert.Equal(50, DamageOfSquire(boostLevel: 4));
    }

    // ---- LowerDamage (10) ----------------------------------------------------------------------

    /// <summary>Снижение урона: два броска точности, уровень из атаки (в ванили только 2, то есть
    /// −33 %), и оно гасит буст, включая длинный — A battlemsgdata.h:52, ранг 2.</summary>
    [SkippableFact] public void LowerDamage_SetsLevelTwo_AndCancelsAnyBoost()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Tiamath), Of(Squire, attacker: true));
        t.BoostDamageLevel = 4; t.BoostDamageLong = true;
        var rng = new ScriptedRng(Hit);
        Attacks.Apply(a, Attack(LowerDam), t, rng, C, round: 1);

        Assert.Equal(2, t.LowerDamageLevel);
        Assert.True(t.LowerDamageLong);      // INFINITE = T у g000aa1168
        Assert.Equal(0, t.BoostDamageLevel);
        Assert.False(t.BoostDamageLong);
        Assert.Equal(2, rng.Used);
    }

    /// <summary>25 − 25·33/100 = 25 − 8 = 17: слагаемое усекается отдельно, а не сводится в
    /// множитель (тот дал бы 25·67/100 = 16). Ранг 1 — Formulas.DamageWithBuffs.</summary>
    [SkippableFact] public void LowerDamage_CutsDamageThroughDamageWithBuffs()
    {
        ImportedData.RequireRules();
        Assert.Equal(17, DamageOfSquire(lowerLevel: 2));
        Assert.Equal(13, DamageOfSquire(lowerLevel: 1));   // −50 %: 25 − 25·50/100 = 13
    }

    // ---- LowerInitiative (11) ------------------------------------------------------------------

    /// <summary>Снижение инициативы: два броска точности, уровень 1, и в очереди инициатива режется
    /// вдвое. Второго уровня в ванили нет, и getLowerInitiative для него всё равно даёт 0.</summary>
    [SkippableFact] public void LowerInitiative_HalvesInitiativeInTurnOrder()
    {
        ImportedData.RequireRules();
        var slow = Of(Squire, attacker: true);
        var hermit = Of(Hermit, slot: 1);
        Assert.Equal(50, slow.Attack.Initiative);   // база Squire

        var rng = new ScriptedRng(Hit);
        Attacks.Apply(hermit, Attack(LowerIni), slow, rng, C, round: 1);
        Assert.Equal(1, slow.LowerInitiativeLevel);
        Assert.False(slow.LowerInitiativeLong);   // INFINITE = F у g000aa1163
        Assert.Equal(2, rng.Used);

        // Очередь: инициатива носителя статуса — 25 вместо 50. Проверяется через саму очередь:
        // Hermit (инициатива 40 у g000aa0163) обгоняет замедленного Squire (25), хотя базового
        // Squire (50) он не обгонял. Броски BAT_INIT нулевые у обоих — решает только инициатива.
        var state = new BattleState
        {
            Units = [slow, hermit], AttackerGroup = "A", DefenderGroup = "D", Round = 1,
        };
        Assert.Equal(25, Initiative(slow, C));
        Assert.True(Initiative(hermit, C) > Initiative(slow, C),
                    $"Hermit {Initiative(hermit, C)} должен обгонять замедленного Squire {Initiative(slow, C)}");
        var order = new ScriptedRng([0, 0]);
        var built = TurnOrder.Build(state, order, C);
        Assert.Equal(2, order.Used);
        Assert.Same(hermit, built[0]);

        // Конечное снижение гасит само построение (0x624752): следующая очередь — без статуса, и порядок
        // обратный — значит очередь действительно решает статус, а не слот.
        Assert.Equal(0, slow.LowerInitiativeLevel);
        Assert.Same(slow, TurnOrder.Build(state, new ScriptedRng([0, 0]), C)[0]);
    }

    /// <summary>Построение очереди 0x62457c (ранг 1): процент снимается с СУММЫ инициативы и броска
    /// (0x6246e7 → 0x5838c0), конечный статус гасится тут же (0x624752), длинный (бит 14) остаётся.
    /// Замедленный Squire: 50 + 9 = 59 → 59 − 29 = 30; «процент до броска» дал бы 25 + 9 = 34. Соперник
    /// с атакой инициативы 30 и броском 2 (32) стоит ровно между — порядок различает правила.</summary>
    [SkippableFact] public void LowerInitiative_CutsRolledInitiative_AndFiniteClearsOnBuild()
    {
        ImportedData.RequireRules();
        var finite = Of(Squire, attacker: true);
        finite.LowerInitiativeLevel = 1;
        var eternal = Of(Squire, attacker: true, slot: 1);
        eternal.LowerInitiativeLevel = 1; eternal.LowerInitiativeLong = true;
        var rival = Of(Squire, attackId: "base:attack/hill-giant-clans", slot: 1);   // INITIATIVE 30
        var state = new BattleState { Units = [finite, eternal, rival], AttackerGroup = "A", DefenderGroup = "D", Round = 1 };

        var built = TurnOrder.Build(state, new ScriptedRng([9, 0, 2]), C);   // 30, 25, 32
        Assert.Equal(new[] { rival, finite, eternal }, built);
        Assert.Equal(0, finite.LowerInitiativeLevel);
        Assert.Equal(1, eternal.LowerInitiativeLevel);

        var next = TurnOrder.Build(state, new ScriptedRng([0, 9, 2]), C);    // 50, 30, 32
        Assert.Equal(new[] { finite, rival, eternal }, next);
    }

    // ---- границы хода --------------------------------------------------------------------------

    /// <summary>afterBattleTurn снимает КОНЕЧНЫЕ буст и снижение урона и не трогает длинные —
    /// survey §7, A hooks.cpp:1786-1791 (removeFiniteBoostLowerDamage), ранг 2. Бросков не тратит.
    /// Снижение инициативы конец хода не трогает — его гасит построение очереди (0x624752, ранг 1).</summary>
    [SkippableFact] public void EndTurn_RemovesFiniteBoostAndLower_KeepsLong()
    {
        ImportedData.RequireRules();
        var finite = Of(Squire, attacker: true);
        finite.BoostDamageLevel = 1; finite.LowerDamageLevel = 2; finite.LowerInitiativeLevel = 1;
        var eternal = Of(Squire, attacker: true, slot: 1);
        eternal.BoostDamageLevel = 3; eternal.BoostDamageLong = true;
        eternal.LowerDamageLevel = 2; eternal.LowerDamageLong = true;
        eternal.LowerInitiativeLevel = 1; eternal.LowerInitiativeLong = true;

        var state = new BattleState
        {
            Units = [finite, eternal, Of(Shamaness, slot: 2)], AttackerGroup = "A", DefenderGroup = "D", Round = 1,
        };
        var rng = new ScriptedRng([]);
        var sim = new BattleSim(state, rng, C);
        sim.EndTurn(finite);
        sim.EndTurn(eternal);

        Assert.Equal(0, finite.BoostDamageLevel);
        Assert.Equal(0, finite.LowerDamageLevel);
        Assert.Equal(1, finite.LowerInitiativeLevel);
        Assert.Equal(3, eternal.BoostDamageLevel);
        Assert.Equal(2, eternal.LowerDamageLevel);
        Assert.Equal(1, eternal.LowerInitiativeLevel);
        Assert.Equal(0, rng.Used);
    }

    // ---- то, что обязано продолжать падать -----------------------------------------------------
    //
    // Таблица опустела и метод убран. У задачи 5 в ней было шесть строк; задача 6 забрала Drain,
    // Revive, Summon и увезла Shatter в AttackClassLifeTests с исправленным счётом бросков (ноль
    // вместо двух), задача 7 — GiveAttack и Doppelganger. Нереализованными остались Shatter и
    // BestowWards (задача 8), их и проверяет AttackClassLifeTests.

    // ---- вспомогательное -----------------------------------------------------------------------

    /// <summary>Урон Squire (QTY_DAM 25) по цели без брони при данном уровне буста и снижения;
    /// случайная добавка rng(BAT_DAMAGE) = 0, точность — попадание.</summary>
    private static int DamageOfSquire(int boostLevel = 0, int lowerLevel = 0)
    {
        var a = Of(Squire);
        var t = Of(Squire, attacker: true);
        a.BoostDamageLevel = boostLevel;
        a.LowerDamageLevel = lowerLevel;
        return Attacks.Apply(a, a.Attack, t, new ScriptedRng([0, 0, 0]), C, round: 1).Damage;
    }

    /// <summary>Инициатива юнита так, как её считает TurnOrder (без броска BAT_INIT).</summary>
    private static int Initiative(BattleUnit unit, RulesConstants c)
    {
        int ini = LastEdict.Rules.Units.EffectiveStats.OfAttack(unit.Attack, unit.Chain).Attack.Initiative;
        return ini - ini * c.LowerInitiativePercent(unit.LowerInitiativeLevel) / 100;
    }
}
