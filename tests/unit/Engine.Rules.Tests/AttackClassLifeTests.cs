using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Задача 6 фазы Ф6: второй круг классов атак — <c>Drain</c> (2), <c>DrainOverflow</c> (15),
/// <c>Revive</c> (14), <c>Summon</c> (17), <c>DrainLevel</c> (18) и площадное <c>Heal</c>.
/// Юниты, атаки и константы — настоящие, из живой ванили; синтетики нет.
///
/// <para><b>Счёт бросков утверждается у каждого класса</b> — это несущая величина: С2 воспроизводит
/// бой шаг в шаг по журнальным броскам, и один лишний <c>rng</c> сдвигает весь поток. Числа здесь
/// <b>ранга 1</b>, но не из журнала (корпус к этому кругу слеп целиком — сплошной проход по всем 74
/// файлам <c>corpus/**/*.jsonl</c> не нашёл ни одного из 24 вани́льных типов с этими классами), а из
/// дизассемблера GOG: слот 6 vftable каждого <c>CBatAttack*</c> — <c>canMiss</c>, и он либо общая
/// заглушка <c>0x6598f5</c> (<c>xor al,al</c> — костей нет), либо своя функция с <c>mov al,1</c>
/// (две кости). Тела <c>onHit</c> прочитаны до <c>ret</c>: генератор в них не зовётся ни разу.</para>
///
/// <para>Игра не запускалась. Всё, чего дизассемблер не показывает — порядок обхода отряда при
/// раздаче излишка, HP после превращения, выбор слота призыва, — помечено рангом 3 в
/// <see cref="LifeAttacks"/> и в отчёте задачи.</para>
/// </summary>
public class AttackClassLifeTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    // Ванильные участники, названные поимённо (перебор Gunits × Gattacks по классу атаки).
    private const string Nosferat = "g000uu0097";      // L1 Drain, hp 90
    private const string NosferatBite = "g000aa0097";  // Drain, POWER 80, REACH 1, SOURCE 3, QTY_DAM 10
    private const string Vampire = "g000uu5283";       // L1, hp 185 — мишень покрупнее
    private const string VampireBite = "g000aa0083";   // Drain, QTY_DAM 50
    private const string ElderVampire = "g000uu0175";  // L5 DrainOverflow, hp 210, PREV_ID g000uu0083
    private const string ElderBite = "g000aa0175";     // DrainOverflow, POWER 80, REACH 1, QTY_DAM 60
    private const string Hierophant = "g000uu0150";    // L4, ATTACK2 — Revive
    private const string ReviveTouch = "g000aa1150";   // Revive, POWER 100, REACH 2, SOURCE 2, QTY_HEAL 50
    private const string Wight = "g000uu0178";         // L4, ATTACK2 — DrainLevel
    private const string DrainLevelTouch = "g000aa1178"; // DrainLevel, POWER 80, REACH 2, SOURCE 3
    private const string Occultist = "g000uu6013";     // L1 Summon
    private const string SummonOne = "g000aa6013";     // Summon: один кандидат, g000uu0087
    private const string SummonMany = "g000aa6014";    // Summon: пять малых кандидатов — rand()
    private const string OracleElf = "g000uu5006";     // L1 площадной лекарь, hp 125
    private const string OracleHeal = "g000aa5006";    // Heal, REACH 1, QTY_HEAL 60
    private const string Squire = "g000uu0001";        // L1, hp 100, ARMOR 0, PREV_ID пуст, иммунитетов нет
    private const string Knight = "g000uu0002";        // L2, hp 150, PREV_ID g000uu0001, иммунитетов нет

    private static RulesConstants C => Rules.Value.Constants;

    private static BattleUnit Of(string typeId, string? attackId = null, bool attacker = false,
                                 int slot = 0, string? id = null, int groupIndex = -1)
    {
        var type = Rules.Value.Units[typeId];
        return new BattleUnit
        {
            Id = id ?? typeId, Type = type,
            Attack = attackId is null ? Rules.Value.AttackOf(type)! : Rules.Value.Attacks[attackId],
            Attack2 = Rules.Value.Attack2Of(type),
            Attacker = attacker, Slot = slot, GroupIndex = groupIndex, HpMax = type.HitPoints, Hp = type.HitPoints,
        };
    }

    private static AttackType Attack(string id) => Rules.Value.Attacks[id];

    private static BattleState State(params BattleUnit[] units) => new()
    {
        Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Round = 1, Rules = Rules.Value,
    };

    /// <summary>Броски: попадание (среднее 0 не больше любой точности) и нулевая добавка урона.</summary>
    private static readonly int[] HitNoBonus = [0, 0, 0];

    // ---- ванильные числа, на которых стоят утверждения ниже -------------------------------------

    [SkippableFact] public void VanillaData_IsWhatTheTestsAssume()
    {
        ImportedData.RequireRules();
        Assert.Equal(AttackClass.Drain, Attack(NosferatBite).Class);
        Assert.Equal(10, Attack(NosferatBite).QtyDamage);
        Assert.Equal(50, Attack(VampireBite).QtyDamage);
        Assert.Equal(AttackClass.DrainOverflow, Attack(ElderBite).Class);
        Assert.Equal(60, Attack(ElderBite).QtyDamage);
        Assert.Equal(AttackClass.Revive, Attack(ReviveTouch).Class);
        Assert.Equal(AttackClass.DrainLevel, Attack(DrainLevelTouch).Class);
        Assert.Equal(AttackClass.Summon, Attack(SummonOne).Class);
        Assert.Equal(AttackType.ReachAll, Attack(OracleHeal).Reach);
        Assert.Equal(60, Attack(OracleHeal).QtyHeal);
        // Броня мишеней нулевая — иначе арифметика урона ниже была бы другой.
        Assert.Equal(0, Rules.Value.Units[Squire].Armor);
        Assert.Equal(0, Rules.Value.Units[Vampire].Armor);
        // PREV_ID: у Старшего вампира предыдущий — Вампир, у Оруженосца предыдущего нет вовсе.
        Assert.Equal("g000uu0083", Rules.Value.Units[ElderVampire].PrevId);
        Assert.Equal("", Rules.Value.Units[Squire].PrevId);
        // Gtransf: у одной атаки призыва кандидат один, у другой — пять малых.
        Assert.Single(Rules.Value.SummonCandidates(SummonOne, smallSlot: true));
        Assert.Equal(5, Rules.Value.SummonCandidates(SummonMany, smallSlot: true).Count);
    }

    /// <summary>Список «класс промахивается» прочитан в движке (слот 6 vftable, см.
    /// <see cref="Formulas.ClassHasPower"/>) и в трёх местах расходится с <c>attackHasPower</c>
    /// тулсета. Тест закрепляет именно эти три исправления — иначе они тихо уедут обратно.</summary>
    [Fact] public void ClassHasPower_FollowsEngineVftable_NotToolset()
    {
        Assert.False(Formulas.ClassHasPower(AttackClass.Revive));       // vft 0x6f32cc, canMiss заглушка
        Assert.False(Formulas.ClassHasPower(AttackClass.BestowWards));  // vft 0x6f2fac
        Assert.False(Formulas.ClassHasPower(AttackClass.Shatter));      // vft 0x6f2ffc
        Assert.False(Formulas.ClassHasPower(AttackClass.Summon));       // vft 0x6f3584
        Assert.True(Formulas.ClassHasPower(AttackClass.Drain));         // canMiss 0x65bd13, mov al,1
        Assert.True(Formulas.ClassHasPower(AttackClass.DrainOverflow)); // 0x65f7c9
        Assert.True(Formulas.ClassHasPower(AttackClass.DrainLevel));    // 0x66282e
    }

    /// <summary>Семь классов не спрашивают иммунитет вовсе (слот 8 vftable — заглушка 0x659913).
    /// Shatter в этот список НЕ входит, хотя и не промахивается: у него своя isImmune 0x65d899.</summary>
    [Fact] public void Immunity_IsNotAskedBySevenClasses()
    {
        foreach (var cls in new[] { AttackClass.Heal, AttackClass.BoostDamage, AttackClass.Revive,
                                    AttackClass.Cure, AttackClass.Summon, AttackClass.GiveAttack,
                                    AttackClass.BestowWards })
            Assert.False(Immunity.Checked(cls), $"{cls} иммунитета не спрашивает");
        foreach (var cls in new[] { AttackClass.Damage, AttackClass.Drain, AttackClass.DrainOverflow,
                                    AttackClass.DrainLevel, AttackClass.Shatter })
            Assert.True(Immunity.Checked(cls), $"{cls} иммунитет спрашивает");
    }

    // ---- Drain (2) ------------------------------------------------------------------------------

    /// <summary>Вампиризм — ровно половина снятых HP, и бросков столько же, сколько у Damage:
    /// два rng(100) на точность плюс один rng(BAT_DAMAGE) внутри computeDamageWithBuffs. Само
    /// лечение не бросает ни одной кости (ранг 1: onHit 0x65bd65 читается до ret).</summary>
    [SkippableFact] public void Drain_HealsHalfOfHpTaken_AndSpendsThreeRolls()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Nosferat), Of(Squire, attacker: true));
        a.Hp = 1;                                   // изранен — потолок лечению не мешает
        var rng = new ScriptedRng(HitNoBonus);
        var r = Attacks.Apply(a, Attack(NosferatBite), t, rng, C, round: 1);

        Assert.Equal(10, r.Damage);                 // QTY_DAM 10 + rng(6) = 0, броня 0
        Assert.Equal(90, t.Hp);                     // 100 − 10
        Assert.Equal(5, r.DrainHealed);             // (100 − 90) / 2
        Assert.Equal(6, a.Hp);
        Assert.Equal(3, rng.Used);
        output.WriteLine($"Drain: урон {r.Damage}, вернулось {r.DrainHealed}, бросков {rng.Used}");
    }

    /// <summary>Половина усекается, а не округляется: движок делит sar-ом (0x65be8d-0x65be91).
    /// Нечётная разница 51 даёт 25, а не 26.</summary>
    [SkippableFact] public void Drain_TruncatesOddHalf()
    {
        ImportedData.RequireRules();
        // Мишень — Оруженосец, а не вампир: у всей нежити в Gimmu стоит Always к источнику 3
        // (Death), то есть вампир вампира не кусает вовсе. Это ровно тот механизм, который завела
        // задача 3, и он тут работает поперёк замысла теста.
        var (a, t) = (Of(Vampire), Of(Squire, attacker: true, id: "target"));
        a.Hp = 1;
        var rng = new ScriptedRng([0, 0, 1]);       // попадание, добавка урона 1
        var r = Attacks.Apply(a, Attack(VampireBite), t, rng, C, round: 1);

        Assert.Equal(51, r.Damage);                 // QTY_DAM 50 + rng(6) = 1
        Assert.Equal(25, r.DrainHealed);            // 51 / 2 = 25, не 26
        Assert.Equal(26, a.Hp);
        Assert.Equal(3, rng.Used);
    }

    /// <summary>Лечение усекается потолком HP атакующего (changeUnitHp движка) — у здорового
    /// вампира вампиризм пропадает целиком, и это ровно та величина, которая у DrainOverflow
    /// уходит союзникам.</summary>
    [SkippableFact] public void Drain_OnHealthyAttacker_HealsNothing()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Nosferat), Of(Squire, attacker: true));
        var r = Attacks.Apply(a, Attack(NosferatBite), t, new ScriptedRng(HitNoBonus), C, round: 1);

        Assert.Equal(0, r.DrainHealed);
        Assert.Equal(a.HpMax, a.Hp);
    }

    /// <summary>Модификатор AttackDrain (GmodifL TYPE 15) — прибавка в процентных ПУНКТАХ поверх
    /// вани́льной половины: 50 % + 25 % = 75 % от разницы HP. Ранг 2 (template.lua getAttackDrain
    /// возвращает HP, а не множитель к половине), снимает задача 13.</summary>
    [SkippableFact] public void Drain_ChainModifierAddsPercentagePoints()
    {
        ImportedData.RequireRules();
        var a = new BattleUnit
        {
            Id = "a", Type = Rules.Value.Units[Nosferat], Attack = Attack(NosferatBite),
            Attacker = false, Slot = 0, HpMax = 90, Hp = 1,
            Modifiers = [new Modifier("g000um2317", [new ModifierElement(ModifierElementType.AttackDrain, Percent: 25)])],
        };
        var t = Of(Squire, attacker: true);
        var r = Attacks.Apply(a, Attack(NosferatBite), t, new ScriptedRng(HitNoBonus), C, round: 1);

        Assert.Equal(10, r.Damage);
        Assert.Equal(7, r.DrainHealed);             // 10·50/100 + 10·25/100 = 5 + 2
    }

    // ---- DrainOverflow (15) ---------------------------------------------------------------------

    /// <summary>Излишек делится поровну между теми союзниками, кому лечение нужно: single =
    /// излишек / их число, остаток раздаётся тем же порядком заново (0x65fbb8, рекурсия на
    /// 0x65fcca). Атакующий из раздачи исключён. Бросков сверх трёх — ноль.</summary>
    [SkippableFact] public void DrainOverflow_SplitsSurplusAmongWoundedAllies()
    {
        ImportedData.RequireRules();
        var a = Of(ElderVampire, id: "elder");            // полон HP — весь вампиризм в излишек
        var (w1, w2, w3) = (Of(Squire, slot: 1, id: "w1"), Of(Squire, slot: 2, id: "w2"),
                            Of(Squire, slot: 3, id: "w3"));
        w1.Hp = w2.Hp = w3.Hp = 40;                       // каждому не хватает 60
        var t = Of(Squire, attacker: true, id: "target"); // мишень не из нежити: Death-иммунитета нет
        var state = State(a, w1, w2, w3, t);

        var rng = new ScriptedRng(HitNoBonus);
        var r = Attacks.Apply(a, Attack(ElderBite), t, rng, C, round: 1, state);

        Assert.Equal(60, r.Damage);
        Assert.Equal(0, r.DrainHealed);                   // атакующему некуда
        Assert.Equal(3, rng.Used);                        // раздача костей не тратит
        Assert.Equal(50, w1.Hp);                          // 30 / 3 = по 10 каждому
        Assert.Equal(50, w2.Hp);
        Assert.Equal(50, w3.Hp);
        Assert.Equal(a.HpMax, a.Hp);
        output.WriteLine($"DrainOverflow: снято {r.Damage}, излишек 30 → +10/+10/+10");
    }

    /// <summary>Журнал показа вампиризма (<see cref="BattleState.DrainHeals"/>): как записи <c>BattleAttackInfo</c> у <c>DrainOverflow::onHit</c>
    /// (<c>0x65f81b</c>) — бьющий всегда, и с нулём (<c>0x65f9f5</c>), затем каждый полеченный излишком (<c>0x65fb4a</c>). По ним экран играет
    /// лечение на группе бьющего (партия 15.10, пометки 1 и 2).</summary>
    [SkippableFact] public void DrainOverflow_LogsAttackerAndSpilledHeals()
    {
        ImportedData.RequireRules();
        var a = Of(ElderVampire, id: "elder");            // полон HP: себе 0, всё — излишком
        var w1 = Of(Squire, slot: 1, id: "w1");
        w1.Hp = 40;
        var full = Of(Squire, slot: 2, id: "full");      // цел — ему ничего
        var t = Of(Squire, attacker: true, id: "target");
        var state = State(a, w1, full, t);

        Attacks.Apply(a, Attack(ElderBite), t, new ScriptedRng(HitNoBonus), C, round: 1, state);

        Assert.Equal([new DrainHeal(1, "elder", "elder", 0), new DrainHeal(1, "elder", "w1", 30)], state.DrainHeals);
    }

    /// <summary>Неделимый остаток излишка достаётся первым в <b>порядке группы</b> (<c>0x65fbb8</c>: цикл по индексу <c>CMidUnitGroup</c>,
    /// <c>0x5f44b9(i)</c>, REA 13.10) — <see cref="BattleUnit.GroupIndex"/>, а не по клетке и не по порядку состояния: 30 на четверых — по 7,
    /// затем по 1 первым двоим в группе.</summary>
    [SkippableFact] public void DrainOverflow_RemainderGoesInGroupOrder_NotSlot()
    {
        ImportedData.RequireRules();
        var a = Of(ElderVampire, id: "elder", groupIndex: 4);
        var w3 = Of(Squire, slot: 3, id: "w3", groupIndex: 1);
        var w1 = Of(Squire, slot: 1, id: "w1", groupIndex: 3);
        var w2 = Of(Squire, slot: 2, id: "w2", groupIndex: 0);
        var w4 = Of(Squire, slot: 4, id: "w4", groupIndex: 2);
        w1.Hp = w2.Hp = w3.Hp = w4.Hp = 40;
        var t = Of(Squire, attacker: true, id: "target");
        var state = State(a, w3, w1, w2, w4, t);

        Attacks.Apply(a, Attack(ElderBite), t, new ScriptedRng(HitNoBonus), C, round: 1, state);

        Assert.Equal([48, 48, 47, 47], new[] { w2.Hp, w3.Hp, w4.Hp, w1.Hp });   // группа: w2, w3, w4, w1
    }

    /// <summary>Из раздачи выпадают трое: сам атакующий, здоровый союзник и мёртвый (isUnitNotAtFullHp
    /// отсекает hp == 0). Излишку, которому некуда деться, деваться и не надо — он пропадает.</summary>
    [SkippableFact] public void DrainOverflow_SkipsAttacker_Healthy_Dead_AndRetreated()
    {
        ImportedData.RequireRules();
        var a = Of(ElderVampire, id: "elder");
        var healthy = Of(Squire, slot: 1, id: "healthy");
        var dead = Of(Squire, slot: 2, id: "dead");
        var fled = Of(Squire, slot: 3, id: "fled");
        var wounded = Of(Squire, slot: 4, id: "wounded");
        dead.Hp = 0;
        fled.Hp = 40; fled.Retreated = true;
        wounded.Hp = 40;
        var t = Of(Squire, attacker: true, id: "target");
        var state = State(a, healthy, dead, fled, wounded, t);

        Attacks.Apply(a, Attack(ElderBite), t, new ScriptedRng(HitNoBonus), C, round: 1, state);

        Assert.Equal(0, dead.Hp);
        Assert.Equal(40, fled.Hp);
        Assert.Equal(healthy.HpMax, healthy.Hp);
        Assert.Equal(70, wounded.Hp);                     // весь излишек 30 достался ему одному
    }

    /// <summary>Никому не достаётся больше, чем не хватает: план лечения учитывает уже выданное
    /// (updateUnitHealFromDrainOverflow, 0x65fcd5), поэтому потолок HP не перескакивается даже на
    /// втором круге раздачи.</summary>
    [SkippableFact] public void DrainOverflow_NeverExceedsHpMax()
    {
        ImportedData.RequireRules();
        var a = Of(ElderVampire, id: "elder");
        var almost = Of(Squire, slot: 1, id: "almost");
        almost.Hp = almost.HpMax - 2;                     // не хватает всего двух
        var wounded = Of(Squire, slot: 2, id: "wounded");
        wounded.Hp = 40;                                  // не хватает 60
        var t = Of(Squire, attacker: true, id: "target");
        var state = State(a, almost, wounded, t);

        Attacks.Apply(a, Attack(ElderBite), t, new ScriptedRng(HitNoBonus), C, round: 1, state);

        // Первый круг: нуждающихся двое, single = 30 / 2 = 15; «почти здоровому» достаётся только
        // 2 (больше некуда), раненому 15, остаток 13. Второй круг: нуждающийся один, single = 13.
        Assert.Equal(almost.HpMax, almost.Hp);            // ровно до потолка, ни на HP выше
        Assert.Equal(68, wounded.Hp);                     // 40 + 15 + 13
    }

    // ---- Revive (14) ----------------------------------------------------------------------------

    /// <summary>Воскрешение возвращает половину потолка HP и не тратит ни одного броска
    /// (canMiss — заглушка). Ранг 1: onHit 0x65f4de, imul eax,eax,0x32; idiv 100 над
    /// IUsSoldier::getHitPoints.</summary>
    [SkippableFact] public void Revive_RestoresHalfOfHpMax_WithoutAnyRoll()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Hierophant), Of(Squire, attacker: true));
        t.Hp = 0;
        var rng = new ScriptedRng([]);                    // ни одной кости в запасе — и не понадобится
        var r = Attacks.Apply(a, Attack(ReviveTouch), t, rng, C, round: 1);

        Assert.True(r.Applied);
        Assert.Equal(50, t.Hp);                           // потолок 100, половина от него
        Assert.True(t.Alive);
        Assert.Equal(0, rng.Used);
        output.WriteLine($"Revive: hp 0 → {t.Hp} при потолке {t.HpMax}, бросков {rng.Used}");
    }

    /// <summary>Иммунитет Revive не спрашивает вовсе: цель с Always к источнику 2 (Life) — а именно
    /// такой источник у g000aa1150 — всё равно поднимается, и заряд Once не тратится.</summary>
    [SkippableFact] public void Revive_IgnoresImmunityEntirely()
    {
        ImportedData.RequireRules();
        var attack = Attack(ReviveTouch);
        Assert.Equal(2, attack.Source);                   // Life — иначе тест ничего не проверяет

        var immuneType = Rules.Value.Units[Squire] with
        {
            ImmunitiesBySource = [new ImmunityGrant(2, ImmunityLevel.Always)],
        };
        var t = new BattleUnit
        {
            Id = "t", Type = immuneType, Attack = Attack("g000aa0001"), Attacker = true, Slot = 0,
            HpMax = 50, Hp = 0,
        };
        var r = Attacks.Apply(Of(Hierophant), attack, t, new ScriptedRng([]), C, round: 1);

        Assert.False(r.Immune);
        Assert.Equal(25, t.Hp);
    }

    /// <summary>По живому эффекта нет — воскрешать нечего.</summary>
    [SkippableFact] public void Revive_OnLivingTarget_DoesNothing()
    {
        ImportedData.RequireRules();
        var t = Of(Squire, attacker: true);
        t.Hp = 7;
        var r = Attacks.Apply(Of(Hierophant), Attack(ReviveTouch), t, new ScriptedRng([]), C, round: 1);

        Assert.False(r.Applied);
        Assert.Equal(7, t.Hp);
    }

    /// <summary>Выбор цели: Revive — единственный класс, целящийся в МЁРТВОГО союзника
    /// (слот 14 vftable, 0x65f606). BattleSim.Act должен находить именно павшего, а не живого.</summary>
    [SkippableFact] public void Revive_InAct_TargetsFallenAlly()
    {
        ImportedData.RequireRules();
        var healer = new BattleUnit
        {
            Id = "healer", Type = Rules.Value.Units[Hierophant], Attack = Attack(ReviveTouch),
            Attacker = false, Slot = 0, HpMax = 125, Hp = 125,
        };
        var living = Of(Squire, slot: 1, id: "living");
        var fallen = Of(Squire, slot: 2, id: "fallen");
        fallen.Hp = 0;
        var enemy = Of(Squire, attacker: true, slot: 0, id: "enemy");
        var state = State(healer, living, fallen, enemy);

        var rng = new ScriptedRng([]);
        var target = BattleAi.Choose(state, healer, rng, C).Target;   // ИИ выбирает павшего (AiRareTargets)
        Assert.Same(fallen, target);
        new BattleSim(state, rng, C).Act(round: 1, healer, target);

        Assert.Equal(50, fallen.Hp);                      // потолок Оруженосца 100
        Assert.Equal(living.HpMax, living.Hp);
        Assert.Equal(0, rng.Used);
    }

    // ---- Summon (17) ----------------------------------------------------------------------------

    /// <summary>Призыв не тратит ни одного броска боевого потока: выбор делает
    /// generateRandomNumberStd (0x548576, обёртка над rand процесса), а не R250 боя (0x54851f).
    /// Ранг 1: единственный вызов в getSummonUnitImplIdByAttack (0x660a60) стоит на 0x660b3e и
    /// ведёт именно в std-версию.</summary>
    [SkippableFact] public void Summon_PlacesTheOnlyCandidate_WithoutAnyBattleRoll()
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonOne, id: "occultist");
        var mate = Of(Squire, slot: 1, id: "mate");
        var enemy = Of(Squire, attacker: true, id: "enemy");
        var state = State(a, mate, enemy);

        var rng = new ScriptedRng([]);
        var r = Attacks.Apply(a, Attack(SummonOne), a, rng, C, round: 1, state);

        Assert.True(r.Applied);
        Assert.Equal(0, rng.Used);
        var summoned = Assert.Single(state.Units, u => u.Summoned);
        Assert.Equal("g000uu0087", summoned.Type.Id);     // единственный кандидат Gtransf
        Assert.Equal(2, summoned.Slot);                   // первый свободный: 0 и 1 заняты
        Assert.False(summoned.Attacker);                  // сторона — та же, что у призывателя
        Assert.Equal(summoned.HpMax, summoned.Hp);
        output.WriteLine($"Summon: {summoned.Type.Id} в слот {summoned.Slot}, бросков {rng.Used}");
    }

    /// <summary>Снятие призыва очередью (<c>CBatAttackUnsummonEffectQueued</c>): в начале каждого хода (<c>0x627ffa</c> → <c>0x628a84</c>)
    /// живой призванный изгоняется, если его призыватель пал, ушёл с поля или его нет в бою; при живом призывателе — остаётся. Изгнанный —
    /// вне поля и очереди, в <see cref="BattleState.Gone"/>, с сообщением <see cref="BattleNoteKind.UnsummonedInBattle"/>.</summary>
    [SkippableTheory]
    [InlineData("alive", false)]
    [InlineData("dead", true)]
    [InlineData("retreated", true)]
    [InlineData("gone", true)]
    public void Summon_LeavesBattle_WhenSummonerIsLost(string summonerState, bool unsummoned)
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonOne, id: "occultist");
        var mate = Of(Squire, slot: 1, id: "mate");
        var enemy = Of(Squire, attacker: true, id: "enemy");
        var state = State(a, mate, enemy);
        Assert.True(Attacks.Apply(a, Attack(SummonOne), a, new ScriptedRng([]), C, round: 1, state).Applied);
        var summoned = Assert.Single(state.Units, u => u.Summoned);
        Assert.Equal("occultist", summoned.SummonerId);

        switch (summonerState)
        {
            case "dead": a.Hp = 0; break;
            case "retreated": a.Retreated = true; break;
            case "gone": state.Units.Remove(a); break;
        }
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        sim.ClearFallenSummons();

        Assert.Equal(unsummoned, !state.Units.Contains(summoned));
        Assert.Equal(unsummoned, summoned.Unsummoned);
        Assert.Equal(!unsummoned, summoned.OnField);
        Assert.True(summoned.Alive);   // изгнан, а не убит: опыта за него нет
        Assert.Equal(unsummoned, state.Notes.Any(n => n is { UnitId: var id, Kind: BattleNoteKind.UnsummonedInBattle } && id == summoned.Id));
        if (unsummoned) Assert.Same(summoned, state.Unit(summoned.Id));
    }

    /// <summary>Призванный беглец в свой ход не уходит, а изгоняется (бит 27 → <c>0x628f96</c> → <c>0x628fb4</c> → <c>0x628c00</c>): хода
    /// нет, бита «ушёл» нет, с поля снят.</summary>
    [SkippableFact] public void Summon_Retreating_IsUnsummonedInsteadOfLeaving()
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonOne, id: "occultist");
        var enemy = Of(Squire, attacker: true, id: "enemy");
        var state = State(a, enemy);
        Assert.True(Attacks.Apply(a, Attack(SummonOne), a, new ScriptedRng([]), C, round: 1, state).Applied);
        var summoned = Assert.Single(state.Units, u => u.Summoned);
        summoned.Retreating = true;

        var sim = new BattleSim(state, new ScriptedRng([]), C);
        Assert.False(sim.BeginTurn(summoned));
        Assert.False(summoned.Retreated);
        Assert.True(summoned.Unsummoned);
        Assert.DoesNotContain(summoned, state.Units);
        Assert.Null(sim.Winner);   // призыватель на поле
    }

    /// <summary>Бой с карты: id призванного — из счётчика карты (<see cref="BattleState.NewUnitId"/>; движок —
    /// <c>CBatAttackSummon::onHit</c> <c>0x6631eb</c> → addUnitToGroup <c>0x5e78f7</c> → слот 9 карты <c>0x5ee8bd</c>, ai-q09 день 5:
    /// <c>UN0274</c>…<c>UN0278</c>), по одному на призыв; без выдатчика — прежний <c>{призыватель}-summon{N}</c>.</summary>
    [SkippableFact] public void Summon_TakesUnitIdFromMapCounter()
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonOne, id: "occultist");
        var state = State(a, Of(Squire, attacker: true, id: "enemy"));
        int next = 0x274;
        state.NewUnitId = () => $"S143UN{next++:x4}";

        Attacks.Apply(a, Attack(SummonOne), a, new ScriptedRng([]), C, round: 1, state);
        Attacks.Apply(a, Attack(SummonOne), a, new ScriptedRng([]), C, round: 2, state);

        Assert.Equal(["S143UN0274", "S143UN0275"], state.Units.Where(u => u.Summoned).Select(u => u.Id));
        Assert.Equal(0x276, next);

        var bare = State(Of(Occultist, SummonOne, id: "occultist"), Of(Squire, attacker: true, id: "enemy"));
        Attacks.Apply(bare.Units[0], Attack(SummonOne), bare.Units[0], new ScriptedRng([]), C, round: 1, bare);
        Assert.Equal("occultist-summon2", Assert.Single(bare.Units, u => u.Summoned).Id);
    }

    /// <summary>Когда кандидатов несколько, модель НЕ выбирает наугад, а падает с внятным текстом:
    /// выбор в движке идёт через rand() процесса, которого у модели нет вовсе, и молчаливая догадка
    /// испортила бы сверку С2 незаметно. В ванили так у двух атак из двадцати одной.</summary>
    [SkippableFact] public void Summon_WithSeveralCandidates_ThrowsNamingRand()
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonMany, id: "summoner");
        var state = State(a, Of(Squire, attacker: true, id: "enemy"));

        var rng = new ScriptedRng([]);
        var ex = Assert.Throws<NotSupportedException>(
            () => Attacks.Apply(a, Attack(SummonMany), a, rng, C, round: 1, state));

        Assert.Contains("generateRandomNumberStd", ex.Message);
        Assert.Equal(0, rng.Used);
        output.WriteLine(ex.Message);
    }

    /// <summary>Бой с карты (<see cref="BattleState.ProcessRand"/>): кандидат — <c>candidates[rand() % n]</c> по потоку CRT партии; при одном
    /// кандидате <c>rand()</c> не зовётся (<c>0x660aed</c>: размер 1 — выход до <c>0x660b3e</c>; журнал <c>20261016e-av</c> seq 15948372…15948377 —
    /// призыв одним кандидатом без строки <c>crt_rand</c>; аудит 13.10 считал, что бросок тратится всегда). Прежде несколько кандидатов
    /// роняли живой бой исключением (аудит 13.10).</summary>
    [SkippableFact] public void Summon_WithProcessRand_PicksByCrtStream()
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonMany, id: "summoner");
        var state = State(a, Of(Squire, attacker: true, id: "enemy"));
        state.ProcessRand = new LastEdict.Rules.Rng.CrtRand(12345);
        var candidates = state.Rules!.SummonCandidates(SummonMany, smallSlot: true);
        Assert.True(candidates.Count > 1);
        var mirror = new LastEdict.Rules.Rng.CrtRand(12345);

        Attacks.Apply(a, Attack(SummonMany), a, new ScriptedRng([]), C, round: 1, state);

        Assert.Equal(candidates[mirror.Next() % candidates.Count].Id, Assert.Single(state.Units, u => u.Summoned).Type.Id);
        Assert.Equal(mirror.State, state.ProcessRand.State);

        var one = State(Of(Occultist, SummonOne, id: "occultist"), Of(Squire, attacker: true, id: "enemy"));
        one.ProcessRand = new LastEdict.Rules.Rng.CrtRand(7);
        Attacks.Apply(one.Units[0], Attack(SummonOne), one.Units[0], new ScriptedRng([]), C, round: 1, one);
        Assert.Equal(new LastEdict.Rules.Rng.CrtRand(7).State, one.ProcessRand.State);
    }

    /// <summary>Отряд полон — призывать некуда, эффекта нет.</summary>
    [SkippableFact] public void Summon_IntoFullGroup_DoesNothing()
    {
        ImportedData.RequireRules();
        var a = Of(Occultist, SummonOne, id: "occultist");
        var mates = Enumerable.Range(1, 5).Select(i => Of(Squire, slot: i, id: $"mate{i}")).ToArray();
        var state = State([a, .. mates, Of(Squire, attacker: true, id: "enemy")]);

        var r = Attacks.Apply(a, Attack(SummonOne), a, new ScriptedRng([]), C, round: 1, state);

        Assert.False(r.Applied);
        Assert.Equal(7, state.Units.Count);
    }

    // ---- DrainLevel (18) ------------------------------------------------------------------------

    /// <summary>Понижение уровня подменяет реализацию юнита на PREV_ID: тип, обе атаки и потолок HP.
    /// Бросков — ровно два на точность (свой canMiss 0x66282e), в теле onHit генератор не зовётся.</summary>
    [SkippableFact] public void DrainLevel_TurnsTargetIntoPreviousType_AndSpendsTwoRolls()
    {
        ImportedData.RequireRules();
        // Жертва — Рыцарь, а не нежить: у всей нежити в Gimmu стоит Always к источнику 3 (Death),
        // то есть Умертвие своего в уровне не понизит вовсе.
        var (a, t) = (Of(Wight), Of(Knight, attacker: true, id: "victim"));
        Assert.Equal(150, t.HpMax);

        var rng = new ScriptedRng([0, 0]);
        var r = Attacks.Apply(a, Attack(DrainLevelTouch), t, rng, C, round: 1, State(a, t));

        Assert.True(r.Applied);
        Assert.Equal(Squire, t.Type.Id);                  // Рыцарь → Оруженосец
        Assert.Equal(100, t.HpMax);
        Assert.Equal(100, t.Hp);                          // усечено новым потолком
        Assert.Equal(Rules.Value.Units[Squire].AttackId, t.Attack.Id);
        Assert.True(t.LevelDrained);
        Assert.Equal(2, rng.Used);
        output.WriteLine($"DrainLevel: {Knight} → {t.Type.Id}, потолок 150 → {t.HpMax}, бросков {rng.Used}");
    }

    /// <summary>PREV_ID пуст — движок выходит из onHit сразу, эффекта нет; броски на точность
    /// при этом всё равно потрачены (удар состоялся).</summary>
    [SkippableFact] public void DrainLevel_OnFirstLevelTarget_DoesNothing()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Wight), Of(Squire, attacker: true));
        var rng = new ScriptedRng([0, 0]);
        var r = Attacks.Apply(a, Attack(DrainLevelTouch), t, rng, C, round: 1, State(a, t));

        Assert.False(r.Applied);
        Assert.Equal(Squire, t.Type.Id);
        Assert.False(t.LevelDrained);
        Assert.Equal(2, rng.Used);
    }

    /// <summary>Без справочников класс падает с внятным текстом, а не тихо ничего не делает:
    /// PREV_ID сам по себе — только id, тип за ним живёт в Gunits.</summary>
    [SkippableFact] public void DrainLevel_WithoutRules_ThrowsInsteadOfSilence()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Wight), Of(Knight, attacker: true, id: "victim"));
        Assert.Throws<InvalidOperationException>(
            () => Attacks.Apply(a, Attack(DrainLevelTouch), t, new ScriptedRng([0, 0]), C, round: 1));
    }

    // ---- площадное Heal -------------------------------------------------------------------------

    /// <summary>Площадное лечение (REACH 1) — единственная механика этого круга, которую корпус
    /// видит: Oracle Elf g000uu5006 / g000aa5006, 49 ходов в corpus/20260906-b (seq 53177, 53293,
    /// 53397, …), до трёх целей за ход, прирост ровно QTY_HEAL 60 с потолком. Ранг 1 и для
    /// величины, и для расхода бросков: лечение не тратит ни одной кости, а n−1 бросков
    /// перемешивания площадной цели (Attacks.Shuffle) тратятся ВСЕГДА, попал кто-то или нет.</summary>
    [SkippableFact] public void AreaHeal_HealsEveryWoundedAlly_AndSpendsOnlyShuffleRolls()
    {
        ImportedData.RequireRules();
        var healer = Of(OracleElf, id: "healer");
        healer.Hp = 99;                                   // лечит и себя — как в журнале (99 → 125)
        var hurt = Of(Squire, slot: 1, id: "hurt");
        hurt.Hp = 10;                                     // не хватает 90 — ляжет весь QTY_HEAL
        var scratched = Of(Squire, slot: 2, id: "scratched");
        scratched.Hp = scratched.HpMax - 5;               // не хватает 5 — ляжет только пять
        var full = Of(Squire, slot: 3, id: "full");
        var state = State(healer, hurt, scratched, full, Of(Squire, attacker: true, id: "enemy"));

        var rng = new ScriptedRng([0, 0, 0]);             // перемешивание группы из четырёх: n−1 = 3
        var records = new BattleSim(state, rng, C).Act(round: 1, healer);

        Assert.Equal(125, healer.Hp);                     // 99 + 60 = 159, усечено потолком 125
        Assert.Equal(70, hurt.Hp);                        // 10 + 60, потолок 100 не мешает
        Assert.Equal(scratched.HpMax, scratched.Hp);      // 5 до потолка
        Assert.Equal(full.HpMax, full.Hp);
        Assert.Equal(3, rng.Used);                        // только перемешивание
        Assert.Equal(3, records.Count);                   // запись только там, где лечение легло
        Assert.DoesNotContain(records, r => r.Target == "full");
        output.WriteLine($"площадное лечение: {records.Count} записей, бросков {rng.Used}");
    }

    // ---- то, что раньше падало ------------------------------------------------------------------

    /// <summary>Последние два класса (Shatter и BestowWards) закрыты задачей 8, поэтому таблица
    /// нереализованных опустела. Здесь остаётся только то, ради чего она стояла: у обоих
    /// <b>ноль бросков</b> (слот 6 vftable — общая заглушка <c>0x6598f5</c>; прежний список ранга 2
    /// по <c>attackHasPower</c> тулсета съедал у них по две кости). Само поведение классов —
    /// <c>AttackClassWardTests</c>.
    /// <para>Три строки третьего круга (GiveAttack, Doppelganger, TransformOther) убраны задачей 7,
    /// счёт их бросков — в <c>AttackClassTransformTests</c>.</para></summary>
    [SkippableTheory]
    [InlineData("g000aa8150")]    // Shatter — canMiss заглушка
    [InlineData("g000aa8133")]    // BestowWards — тоже заглушка
    public void FourthCircleClasses_AreImplemented_AndSpendNoRolls(string attackId)
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Squire, attacker: true), Of(Squire, id: "target"));
        var rng = new ScriptedRng([]);          // ни одной кости в запасе — и не понадобится

        var r = Attacks.Apply(a, Attack(attackId), t, rng, C, round: 1, State(a, t));

        Assert.True(r.Applied);
        Assert.Equal(0, rng.Used);
        output.WriteLine($"{attackId} ({Attack(attackId).Class}): {rng.Used} бросков");
    }
}
