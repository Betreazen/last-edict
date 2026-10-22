using D2.Data;
using D2.Data.Battle;
using D2.Rules.Battle;
using D2.Rules.Rng;
using Xunit.Abstractions;

/// <summary>
/// Задача 7 фазы Ф6: третий круг классов атак — <c>GiveAttack</c> (19), <c>Doppelganger</c> (20),
/// <c>TransformSelf</c> (21), <c>TransformOther</c> (22) — и откат превращений и призыва после боя.
/// Юниты, атаки и константы настоящие, из живой ванили; синтетики нет.
///
/// <para><b>Счёт бросков утверждается у каждого класса</b> — величина несущая: С2 воспроизводит бой
/// шаг в шаг по журнальным броскам, и одна лишняя кость сдвигает весь поток. Числа <b>ранга 1</b>,
/// но не из журнала: сплошной проход по всем 74 файлам <c>corpus/**/*.jsonl</c> (502
/// <c>battle_start</c>, 909 <c>battle_units</c>, 20 типов юнитов) не нашёл ни одного из шести
/// вани́льных носителей классов 19..22, ни одной смены типа юнита внутри боя, ни одного повтора
/// юнита в <c>order[]</c> и ни одного бита 22..26 в <c>st</c>. Корпус к этому кругу слеп целиком,
/// поэтому источник — дизассемблер GOG (адреса в <see cref="TransformAttacks"/>).</para>
///
/// <para>Игра не запускалась.</para>
/// </summary>
public class AttackClassTransformTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    // Вани́льные носители: перебор Gunits × Gattacks по классу атаки даёт ровно шесть типов.
    private const string Alchemist = "g000uu0161";     // L3 GiveAttack, hp 120
    private const string GiveAttackTouch = "g000aa0161";  // GiveAttack, POWER 100, REACH 2, SOURCE 2
    private const string Doppel = "g000uu0171";        // L3 Doppelganger, hp 120
    private const string DoppelTouch = "g000aa0171";   // Doppelganger, REACH 2, ALT_ATTACK g000aa1171
    private const string WolfLord = "g000uu0164";      // L4 TransformSelf, hp 225
    private const string WolfChange = "g000aa0164";    // TransformSelf, POWER 100, ALT g000aa1164
    private const string WolfForm = "g000uu0190";      // во что превращается Волчий лорд, hp 275
    private const string Witch = "g000uu0067";         // L2 TransformOther, hp 75
    private const string WitchCurse = "g000aa0067";    // TransformOther, POWER 80, SOURCE 1, INFINITE
    private const string Toad = "g000uu5034";          // малый кандидат Gtransf, hp 50
    private const string BigForm = "g000uu6104";       // большой кандидат Gtransf, hp 100
    private const string Squire = "g000uu0001";        // L1, hp 100, ARMOR 0, иммунитетов нет
    private const string Knight = "g000uu0002";        // L2, hp 150
    private const string Occultist = "g000uu6013";     // L1 Summon — для проверки отката призыва
    private const string SummonOne = "g000aa6013";     // Summon: один кандидат, g000uu0087
    private const string DrainDwarf = "g000uu0178";    // вторая атака — DrainLevel (карта 17, 19.10)
    private const string DrainTouch = "g000aa1178";    // DrainLevel

    private static RulesConstants C => Rules.Value.Constants;

    private static BattleUnit Of(string typeId, string? attackId = null, bool attacker = false,
                                 int slot = 0, string? id = null, int? hp = null)
    {
        var type = Rules.Value.Units[typeId];
        return new BattleUnit
        {
            Id = id ?? typeId, Type = type,
            Attack = attackId is null ? Rules.Value.AttackOf(type)! : Rules.Value.Attacks[attackId],
            Attack2 = Rules.Value.Attack2Of(type),
            Attacker = attacker, Slot = slot, HpMax = type.HitPoints, Hp = hp ?? type.HitPoints,
        };
    }

    private static AttackType Attack(string id) => Rules.Value.Attacks[id];

    private static BattleState State(params BattleUnit[] units) => new()
    {
        Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Round = 0, Rules = Rules.Value,
    };

    private static ScriptedRng Rolls(int n = 64) => new(Enumerable.Repeat(0, n));

    // ---- ванильные числа, на которых стоят утверждения ниже -------------------------------------

    [SkippableFact] public void VanillaData_IsWhatTheTestsAssume()
    {
        ImportedData.RequireRules();
        Assert.Equal(AttackClass.GiveAttack, Attack(GiveAttackTouch).Class);
        Assert.Equal(AttackClass.Doppelganger, Attack(DoppelTouch).Class);
        Assert.Equal(AttackClass.TransformSelf, Attack(WolfChange).Class);
        Assert.Equal(AttackClass.TransformOther, Attack(WitchCurse).Class);
        Assert.Equal(80, Attack(WitchCurse).Power);        // единственный класс круга с точностью
        Assert.True(Attack(WitchCurse).Infinite);          // все пять атак класса 22 — INFINITE
        Assert.Equal(225, Rules.Value.Units[WolfLord].HitPoints);
        Assert.Equal(275, Rules.Value.Units[WolfForm].HitPoints);
        Assert.Equal(50, Rules.Value.Units[Toad].HitPoints);
        Assert.Equal(150, Rules.Value.Units[Knight].HitPoints);
        Assert.Equal(120, Rules.Value.Units[Doppel].HitPoints);
        output.WriteLine("вани́льные числа круга 3 на месте");
    }

    /// <summary>Данные превращений живут в том же <c>Gtransf.dbf</c>, что и призыв, и режутся тем
    /// же фильтром по размеру — <c>fillAttackTransformIdList</c> (GOG <c>0x597b00</c>): на
    /// <c>0x597bc9</c> результат <c>getSizeSmall</c> цели сравнивается с аргументом, несовпадение —
    /// кандидат пропущен. Ранг 1 (задача 6 держала это рангом 2).
    /// <para>Практический вывод, ради которого тест и написан: <b>у всех семи вани́льных атак
    /// классов 21 и 22 после фильтра кандидат ровно один</b>, значит <c>rand()</c> процесса не
    /// зовётся вовсе и превращения воспроизводимы — в отличие от призыва, где две атаки из
    /// двадцати одной многозначны.</para></summary>
    [SkippableFact] public void Gtransf_LeavesExactlyOneCandidateForEveryVanillaTransform()
    {
        ImportedData.RequireRules();
        string[] transforms = ["g000aa0164", "g000aa9022",                       // TransformSelf
                               "g000aa0067", "g000aa0068", "g000aa0173",         // TransformOther
                               "g000aa9021", "g000aa9140"];
        int checkedCount = 0;
        foreach (var id in transforms)
            foreach (bool small in new[] { true, false })
            {
                var found = Rules.Value.SummonCandidates(id, small);
                Assert.True(found.Count <= 1, $"{id} (small={small}): кандидатов {found.Count}");
                checkedCount++;
            }
        Assert.Equal(14, checkedCount);
        // Пара кандидатов у класса 22 одна на все пять атак и различается ровно размером.
        Assert.Equal(Toad, Assert.Single(Rules.Value.SummonCandidates(WitchCurse, smallSlot: true)).Id);
        Assert.Equal(BigForm, Assert.Single(Rules.Value.SummonCandidates(WitchCurse, smallSlot: false)).Id);
        Assert.Equal(WolfForm, Assert.Single(Rules.Value.SummonCandidates(WolfChange, smallSlot: true)).Id);
        Assert.Empty(Rules.Value.SummonCandidates(WolfChange, smallSlot: false));
        // Doppelganger и GiveAttack в Gtransf не значатся вовсе — им таблица не нужна.
        Assert.Empty(Rules.Value.SummonCandidates(DoppelTouch, smallSlot: true));
        Assert.Empty(Rules.Value.SummonCandidates(GiveAttackTouch, smallSlot: true));
        output.WriteLine($"проверено сочетаний «атака × размер»: {checkedCount}, многозначных ноль");
    }

    /// <summary>Слоты 6 и 8 vftable четырёх классов, разложенные по модели. Тест закрепляет два
    /// исправления задачи 7, иначе они тихо уедут обратно:
    /// <list type="bullet">
    /// <item><c>TransformSelf</c> убран из <see cref="Formulas.ClassHasPower"/> — его
    /// <c>canMiss</c> (<c>0x6611a7</c>) при цели, равной самому юниту, отдаёт <c>xor al,al</c>;</item>
    /// <item><c>Doppelganger</c> и <c>TransformSelf</c> добавлены в неспрашивающие иммунитет —
    /// их <c>isImmune</c> (<c>0x66208b</c>, <c>0x661279</c>) в той же ветви тоже ложь.</item>
    /// </list></summary>
    [Fact] public void VftableSlots_OfThirdCircle_AreModelled()
    {
        Assert.False(Formulas.ClassHasPower(AttackClass.GiveAttack));      // заглушка 0x6598f5
        Assert.False(Formulas.ClassHasPower(AttackClass.Doppelganger));    // развилка 0x661fd5
        Assert.False(Formulas.ClassHasPower(AttackClass.TransformSelf));   // развилка 0x6611a7
        Assert.True(Formulas.ClassHasPower(AttackClass.TransformOther));   // 0x6607f3, mov al,1

        Assert.False(Immunity.Checked(AttackClass.GiveAttack));            // заглушка 0x659913
        Assert.False(Immunity.Checked(AttackClass.Doppelganger));          // развилка 0x66208b
        Assert.False(Immunity.Checked(AttackClass.TransformSelf));         // развилка 0x661279
        Assert.True(Immunity.Checked(AttackClass.TransformOther));         // 0x660802 -> 0x65a528

        // Слот 2 (getTargetGroupId): своя группа у всех троих, чужая — только у TransformOther.
        Assert.True(Attacks.TargetsAllies(AttackClass.GiveAttack));
        Assert.True(Attacks.TargetsAllies(AttackClass.Doppelganger));
        Assert.True(Attacks.TargetsAllies(AttackClass.TransformSelf));
        Assert.False(Attacks.TargetsAllies(AttackClass.TransformOther));
    }

    // ---- GiveAttack (19) ------------------------------------------------------------------------

    /// <summary>Передача хода: ноль бросков (обе заглушки vftable), эффект — пометка «ходит
    /// следующим». Это не урон и не статус: движок вдвигает цель в <c>turnsOrder[1]</c>
    /// (<c>BattleMsgData::giveAttack</c>, GOG <c>0x62266e</c>, смещение массива <c>0xe70</c> —
    /// ровно <c>assert_offset(BattleMsgData, turnsOrder, 3696)</c>). Ранг 1.</summary>
    [SkippableFact] public void GiveAttack_SpendsNoRolls_AndMarksExtraTurn()
    {
        ImportedData.RequireRules();
        var (alchemist, ally) = (Of(Alchemist, attacker: true, slot: 1), Of(Squire, attacker: true));
        var rng = Rolls();
        var outcome = Attacks.Apply(alchemist, Attack(GiveAttackTouch), ally, rng, C, round: 1,
                                    State(alchemist, ally));

        Assert.True(outcome.Applied);
        Assert.True(ally.ExtraTurn);
        Assert.Equal(0, rng.Used);
        Assert.Equal(100, ally.Hp);          // HP не трогает вовсе
        Assert.False(alchemist.ExtraTurn);   // ход достаётся цели, не бьющему
        output.WriteLine($"GiveAttack: бросков {rng.Used}, внеочередной ход у {ally.Id}");
    }

    /// <summary>А теперь то же самое в живом прогоне: цель ходит СРАЗУ ЗА бьющим, в том же раунде.
    /// Порядок раунда при нулевых бросках — оруженосец (INI 50, сторона нападающего), оруженосец
    /// врага (INI 50), алхимик (INI 10); алхимик последний, поэтому вставка видна как второй
    /// удар союзника в том же раунде.</summary>
    [SkippableFact] public void GiveAttack_LetsTheAllyActAgainInTheSameRound()
    {
        ImportedData.RequireRules();
        var ally = Of(Squire, attacker: true, slot: 0, id: "ally");
        var alchemist = Of(Alchemist, attacker: true, slot: 1, id: "alchemist");
        var foe = Of(Squire, slot: 0, id: "foe");
        var state = State(ally, alchemist, foe);
        var rng = Rolls();

        var log = new BattleSim(state, rng, C).Run(maxRounds: 1);

        Assert.Equal(2, log.Count(r => r.Unit == "ally"));      // обычный ход плюс подаренный
        Assert.Equal(1, log.Count(r => r.Unit == "foe"));
        Assert.DoesNotContain(log, r => r.Unit == "alchemist"); // у передачи хода нет записи удара
        Assert.False(ally.ExtraTurn);                           // флаг снят вставкой, ход один
        output.WriteLine($"записей за раунд: {log.Count}, из них у союзника " +
                         $"{log.Count(r => r.Unit == "ally")}; бросков {rng.Used}");
    }

    // ---- Doppelganger (20) ----------------------------------------------------------------------

    /// <summary>Предраунд начала боя (<see cref="BattleSim.BeginPreRound"/>, <c>0x6290bc</c>, реестр Б32): двойник превращается в раунде 0
    /// между двумя построениями очереди раунда 1 — бросков инициативы 2 × юнитов, как без двойника, а в раунде 1 он ходит ещё раз уже
    /// копией. Без двойника предраунд пуст. Журнал: <c>20261016-port-synth [545297]</c>, <c>20260908-f6/map-001 [1281528]</c>.</summary>
    [SkippableFact] public void Doppelganger_ActsInPreRound_BetweenTheTwoFirstRoundQueueBuilds()
    {
        ImportedData.RequireRules();
        var doppel = Of(Doppel, attacker: true, slot: 0, id: "doppel");
        var squire = Of(Squire, attacker: true, slot: 1, id: "squire");
        var foe = Of(Knight, slot: 0, id: "foe");
        var state = State(doppel, squire, foe);
        var sim = new BattleSim(state, Rolls(), C);

        sim.BeginRound(1);
        var pre = sim.BeginPreRound();

        Assert.Equal(["doppel"], pre.Select(u => u.Id));
        Assert.Equal(0, state.Round);
        Assert.Empty(new BattleSim(State(Of(Squire, attacker: true, id: "a"), Of(Knight, id: "b")), Rolls(), C).BeginPreRound());

        var full = State(Of(Doppel, attacker: true, slot: 0, id: "doppel"), Of(Squire, attacker: true, slot: 1, id: "squire"), Of(Knight, slot: 0, id: "foe"));
        var rng = Rolls();
        var run = new BattleSim(full, rng, C);
        run.Run(maxRounds: 1);
        var d = full.Units.Single(u => u.Id == "doppel");
        Assert.True(d.TransformedDoppelganger);
        Assert.Contains(run.Choices, c => c.Round == 0 && c.Unit == "doppel");
        Assert.Contains(run.Choices, c => c.Round == 1 && c.Unit == "doppel");
        output.WriteLine($"двойник: {d.Type.Id}, решений {run.Choices.Count}, бросков {rng.Used}");
    }

    /// <summary>Копия цели: превращается САМ бьющий, HP пересчитывается пропорционально, бросков
    /// ноль. Копируется глобальная реализация цели (<c>CUnitGenerator::getGlobalUnitImplId</c>,
    /// слот 4 vftable), то есть строка <c>Gunits</c> — в модели это просто тип цели.</summary>
    [SkippableFact] public void Doppelganger_CopiesTargetType_ScalesHp_AndSpendsNoRolls()
    {
        ImportedData.RequireRules();
        var doppel = Of(Doppel, attacker: true, hp: 60);   // ранен наполовину: 60 из 120
        var model = Of(Knight, attacker: true, slot: 1);   // hp 150
        var rng = Rolls();

        var outcome = Attacks.Apply(doppel, Attack(DoppelTouch), model, rng, C, round: 1,
                                    State(doppel, model));

        Assert.True(outcome.Applied);
        Assert.Equal(0, rng.Used);
        Assert.Equal(Knight, doppel.Type.Id);
        Assert.Equal(150, doppel.HpMax);
        Assert.Equal(75, doppel.Hp);                       // 60 · 150 / 120 = 75
        Assert.Equal(Rules.Value.Units[Knight].AttackId, doppel.Attack.Id);
        Assert.True(doppel.TransformedDoppelganger);
        Assert.False(doppel.Transformed);                  // биты 22..26 взаимно исключающи
        Assert.Equal(Doppel, doppel.OrigType!.Id);
        Assert.False(doppel.TransformKeepHp);
        Assert.Equal(150, model.Hp);                       // цели не делается ничего
        output.WriteLine($"Doppelganger: бросков {rng.Used}, {Doppel} -> {doppel.Type.Id}, hp 60 -> {doppel.Hp}");
    }

    /// <summary>Второй раз превратиться нельзя: <c>cannotUseDoppelgangerAttack</c> (GOG
    /// <c>0x6618e9</c>) — это пять подряд <c>getUnitStatus</c> по битам 0x16..0x1a, любой
    /// взведённый даёт истину, и удар целиком уходит запасной атаке (<c>0x662310</c>:
    /// <c>this+0x18</c>, <c>call [vft+0x30]</c>). Это и есть «раунд 0» survey §4: никакого счётчика
    /// раундов нет, есть однократность.
    /// <para><b>Задача 8 закрыла эту ветвь</b> (долг задачи 7): <c>ALT_ATTACK</c> дочитана в
    /// <see cref="AttackType.AltAttackId"/>, и <c>Attacks.Apply</c> применяет запасную атаку
    /// рекурсией. У <c>g000aa0171</c> запасная — <c>g000aa1171</c>, класс Damage, POWER 80,
    /// QTY_DAM 30. Значит второй удар не падает, а <b>бьёт</b>, и тратит уже <b>три</b> кости
    /// (две на точность запасной атаки плюс добавка урона), а не ноль: развилка меняет счёт
    /// бросков, и это ровно то, что обязано быть закреплено тестом.</para></summary>
    [SkippableFact] public void Doppelganger_SecondTime_GoesToAltAttack_AndSpendsThreeRolls()
    {
        ImportedData.RequireRules();
        var doppel = Of(Doppel, attacker: true);
        var model = Of(Knight, attacker: true, slot: 1);
        var state = State(doppel, model);

        var first = Rolls();
        Assert.True(Attacks.Apply(doppel, Attack(DoppelTouch), model, first, C, round: 1, state).Applied);
        Assert.Equal(0, first.Used);                       // само превращение костей не тратит
        Assert.True(doppel.IsTransformed);

        var rng = new ScriptedRng([0, 0, 0]);              // 2 на точность g000aa1171 + добавка урона
        var r = Attacks.Apply(doppel, Attack(DoppelTouch), model, rng, C, round: 1, state);

        Assert.False(r.Miss);
        Assert.Equal(30, r.Damage);                        // QTY_DAM 30 + rng(6) = 0, броня Knight 0
        Assert.Equal(3, rng.Used);
        Assert.Equal(Knight, doppel.Type.Id);              // копия осталась первой, второй не было
        output.WriteLine($"второе превращение ушло к {Attack(DoppelTouch).AltAttackId}: " +
                         $"урон {r.Damage}, бросков {rng.Used}");
    }

    // ---- TransformSelf (21) ---------------------------------------------------------------------

    /// <summary>Превращение себя: ноль бросков, тип из <c>Gtransf</c>, HP пересчитывается
    /// пропорционально. Волчий лорд (225) → <c>g000uu0190</c> (275).</summary>
    [SkippableTheory]
    [InlineData(225, 275)]   // целый: 225 · 275 / 225
    [InlineData(100, 122)]   // раненый: 100 · 275 / 225 = 122,2 -> 122 (усечение к нулю)
    [InlineData(1, 1)]       // единица остаётся единицей
    public void TransformSelf_TakesGtransfType_AndScalesHp(int hpBefore, int hpAfter)
    {
        ImportedData.RequireRules();
        var wolf = Of(WolfLord, attacker: true, hp: hpBefore);
        var rng = Rolls();

        var outcome = Attacks.Apply(wolf, Attack(WolfChange), wolf, rng, C, round: 1, State(wolf));

        Assert.True(outcome.Applied);
        Assert.Equal(0, rng.Used);
        Assert.Equal(WolfForm, wolf.Type.Id);
        Assert.Equal(275, wolf.HpMax);
        Assert.Equal(hpAfter, wolf.Hp);
        Assert.True(wolf.TransformedSelf);
        Assert.Equal(WolfLord, wolf.OrigType!.Id);
        output.WriteLine($"TransformSelf: бросков {rng.Used}, hp {hpBefore} -> {wolf.Hp} при потолке 225 -> 275");
    }

    /// <summary>Развилка класса прочитана задачей 8 до конца (<c>onHit</c> <c>0x6613e1</c>), и она
    /// НЕ «цель — это я»: сперва <c>0x62ac90(targetId, this+8)</c> (id различаются), затем сравнение
    /// <c>getAllyOrEnemyGroupId(..., 1)</c> цели и себя. То есть по <b>союзнику</b> класс превращает
    /// обычным путём — и превращает именно цель (<c>getUnit(objectMap, targetUnitId)</c> на
    /// <c>0x6614e9</c>), — а к запасной атаке уходит только по врагу. Прежде модель падала на любой
    /// цели, кроме самого атакующего.</summary>
    [SkippableFact] public void TransformSelf_AtAlly_TransformsThatAlly_WithoutRolls()
    {
        ImportedData.RequireRules();
        var (wolf, ally) = (Of(WolfLord, attacker: true), Of(Squire, attacker: true, slot: 1));
        var rng = Rolls();

        var r = Attacks.Apply(wolf, Attack(WolfChange), ally, rng, C, round: 1, State(wolf, ally));

        Assert.True(r.Applied);
        Assert.Equal(0, rng.Used);
        Assert.Equal(WolfForm, ally.Type.Id);              // превратился союзник, а не сам Волчий лорд
        Assert.Equal(WolfLord, wolf.Type.Id);
        Assert.True(ally.TransformedSelf);
        output.WriteLine($"TransformSelf по союзнику: {Squire} -> {ally.Type.Id}, бросков {rng.Used}");
    }

    /// <summary>А по врагу — запасная атака целиком (<c>0x6614bf</c>: <c>this+4</c>,
    /// <c>call [vft+0x30]</c>). У <c>g000aa0164</c> это <c>g000aa1164</c>: класс Damage, POWER 80,
    /// QTY_DAM 40. Долг задачи 7 закрыт задачей 8 — считаем не падение, а урон и три кости.</summary>
    [SkippableFact] public void TransformSelf_AtEnemy_GoesToAltAttack_AndSpendsThreeRolls()
    {
        ImportedData.RequireRules();
        var (wolf, enemy) = (Of(WolfLord, attacker: true), Of(Squire, slot: 1));
        var rng = new ScriptedRng([0, 0, 0]);

        var r = Attacks.Apply(wolf, Attack(WolfChange), enemy, rng, C, round: 1, State(wolf, enemy));

        Assert.False(r.Miss);
        Assert.Equal(40, r.Damage);                        // QTY_DAM 40 + rng(6) = 0, броня 0
        Assert.Equal(60, enemy.Hp);                        // 100 − 40
        Assert.Equal(3, rng.Used);
        Assert.Equal(WolfLord, wolf.Type.Id);              // превращения не было
        Assert.Equal("g000aa1164", Attack(WolfChange).AltAttackId);
        output.WriteLine($"TransformSelf по врагу ушёл к {Attack(WolfChange).AltAttackId}: " +
                         $"урон {r.Damage}, бросков {rng.Used}");
    }

    // ---- TransformOther (22) --------------------------------------------------------------------

    /// <summary>Превращение цели: два броска на точность (свой <c>canMiss</c> <c>0x6607f3</c> —
    /// голое <c>mov al,1</c>), кандидат берётся по размеру ЦЕЛИ, и — единственное место в
    /// движке — <b>HP не пересчитывается</b>: <c>keepHp = 1</c> (<c>push 1</c> на <c>0x66093b</c>).
    /// Оруженосец со ста HP становится жабой (<c>HIT_POINT</c> 50) и остаётся со ста; клампа в
    /// <c>onHit</c> нет. Потолок — исходного (<c>getHpMax</c> <c>0x5e987c</c> при <c>keepHp</c> отдаёт <c>+0x38</c>): 100/100,
    /// как Бес 175/250 на панели оригинала (карта 17). Ранг 1.</summary>
    [SkippableFact] public void TransformOther_KeepsHp_Above_TheNewMaximum()
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Squire, slot: 0));
        var rng = Rolls();

        var outcome = Attacks.Apply(witch, Attack(WitchCurse), victim, rng, C, round: 1,
                                    State(witch, victim));

        Assert.True(outcome.Applied);
        Assert.Equal(2, rng.Used);                 // только точность
        Assert.Equal(Toad, victim.Type.Id);
        Assert.Equal(100, victim.HpMax);           // getHpMax 0x5e987c: потолок исходного, не 50 жабы
        Assert.Equal(100, victim.Hp);              // keepHp = true: HP выше HIT_POINT новой формы
        Assert.True(victim.Transformed);           // бит 22
        Assert.True(victim.TransformedLong);       // бит 23 — атака INFINITE
        Assert.True(victim.TransformKeepHp);
        Assert.Equal(Squire, victim.OrigType!.Id);
        output.WriteLine($"TransformOther: бросков {rng.Used}, {Squire} -> {victim.Type.Id}, " +
                         $"hp {victim.Hp} при потолке {victim.HpMax}");
    }

    /// <summary>Превращение поверх превращения: <c>CMidUnit::transform</c> <c>0x5ea25c</c> у уже превращённого сперва зовёт откат
    /// <c>undoTransform</c> <c>0x5ea3bd</c> (<c>0x5ea264</c>) — тип, HP (обратно по <c>keepHp</c> прошлого) и потолок исходного, и лишь потом
    /// превращает с <c>keepHp</c> этого вызова. Рыцарь 120/150, понижен до Оруженосца — 80/100; Ведьма: откат 80 · 150 / 100 = 120,
    /// жаба 120/150. Журнал: <c>20261018b-port-synth [5062040]</c>, Ветеран 140/200 после понижения → Бес 175/250 (<c>attack_hit</c>).
    /// Откат после боя HP не трогает (<c>keepHp</c> последнего превращения — истина).</summary>
    [SkippableFact] public void TransformOther_OverDrainedLevel_UndoesTheDrainFirst()
    {
        ImportedData.RequireRules();
        var (witch, dwarf, victim) = (Of(Witch, attacker: true), Of(DrainDwarf, attacker: true, slot: 1), Of(Knight, slot: 0, hp: 120));
        var state = State(witch, dwarf, victim);

        Assert.True(Attacks.Apply(dwarf, Attack(DrainTouch), victim, Rolls(), C, round: 1, state).Applied);
        Assert.Equal((Squire, 80, 100), (victim.Type.Id, victim.Hp, victim.HpMax));

        Assert.True(Attacks.Apply(witch, Attack(WitchCurse), victim, Rolls(), C, round: 1, state).Applied);
        Assert.Equal((Toad, 120, 150), (victim.Type.Id, victim.Hp, victim.HpMax));
        Assert.Equal(Knight, victim.OrigType!.Id);
        Assert.True(victim.TransformKeepHp);
        Assert.True(victim.Transformed);
        Assert.False(victim.LevelDrained);

        TransformAttacks.Untransform(victim, state);
        Assert.Equal((Knight, 120, 150), (victim.Type.Id, victim.Hp, victim.HpMax));
    }

    /// <summary>Сила бегства ИИ (<c>0x5dd06c</c>) видит превращённого Ведьмой исходным: при статусе Transform (<c>0x5dd0a7</c>)
    /// солдат — <c>0x5ea4dd</c> (реализация <c>origTypeId</c>). Журнал <c>20261018b-port-synth [5062040]</c>: Бес из Ветерана 124 HP —
    /// s 79,36, по атаке Ветерана.</summary>
    [SkippableFact] public void TransformOther_StrengthSeesTheOriginalUnit()
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Knight, slot: 0));
        double before = AiStrength.Unit(AiStrength.Seen(victim), ignoreCity: false, enemies: 1, C);

        Assert.True(Attacks.Apply(witch, Attack(WitchCurse), victim, Rolls(), C, round: 1, State(witch, victim)).Applied);

        Assert.Equal(Toad, victim.Type.Id);
        Assert.Equal(before, AiStrength.Unit(AiStrength.Seen(victim), ignoreCity: false, enemies: 1, C), 9);
        Assert.NotEqual(before, AiStrength.Unit(victim, ignoreCity: false, enemies: 1, C));   // жабой сила была бы другой
    }

    /// <summary>Промах ничего не делает и стоит те же два броска: 99 и 99 при точности 80.</summary>
    [SkippableFact] public void TransformOther_Miss_ChangesNothing()
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Squire, slot: 0));
        var rng = new ScriptedRng([99, 99]);

        var outcome = Attacks.Apply(witch, Attack(WitchCurse), victim, rng, C, round: 1,
                                    State(witch, victim));

        Assert.True(outcome.Miss);
        Assert.Equal(2, rng.Used);
        Assert.Equal(Squire, victim.Type.Id);
        Assert.Null(victim.OrigType);
        output.WriteLine($"промах TransformOther: бросков {rng.Used}, тип не тронут");
    }

    /// <summary>Срок длинного превращения проверяется и у умершего от яда в начале своего хода: обработка хода (<c>0x628119</c> →
    /// <c>0x6287d6</c> → <c>0x628821</c> → <c>0x626413</c>) идёт за обработчиками DoT без проверки гибели, смерть снимает биты яда, но не
    /// 22/23. Своего броска яд при гибели не делает. Журнал <c>20261019-cov [1531355]</c>: жаба из S092UN010b (10 HP) гибнет от яда в
    /// раунде 2 — rng(100) = 15 (держит: 16 &lt; 90 − 10 · 1), затем раунд 3. Брошено 95 — срок вышел, откат типа, мёртвый остаётся мёртвым.</summary>
    [SkippableTheory]
    [InlineData(15, false)]
    [InlineData(95, true)]
    public void TransformOtherLong_DeathFromPoison_StillRollsTheDuration(int roll, bool breaks)
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Squire, slot: 0));
        var state = State(witch, victim);
        Assert.True(Attacks.Apply(witch, Attack(WitchCurse), victim, Rolls(), C, round: 1, state).Applied);
        Assert.True(victim.TransformedLong);
        victim.TransformAppliedRound = 1;
        victim.Hp = 10;
        victim.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 15, AppliedRound = 1, Long = true });
        state.Round = 2;
        var rng = new ScriptedRng([roll]);

        Assert.False(new BattleSim(state, rng, C).BeginTurn(victim));

        Assert.Equal(1, rng.Used);                       // бросок срока превращения, яд не бросает
        Assert.Equal(0, victim.Hp);
        Assert.False(victim.Alive);
        Assert.Empty(victim.Dots);
        Assert.Equal(breaks ? Squire : Toad, victim.Type.Id);
        Assert.Equal(!breaks, victim.TransformedLong);
    }

    /// <summary>Мёртвый до начала хода хода у движка не получает — и броска срока превращения у модели нет (страж aliveBeforeTick).</summary>
    [SkippableFact] public void TransformOtherLong_DeadBeforeTurn_SpendsNoRoll()
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Squire, slot: 0));
        var state = State(witch, victim);
        Assert.True(Attacks.Apply(witch, Attack(WitchCurse), victim, Rolls(), C, round: 1, state).Applied);
        victim.Hp = 0;
        state.Round = 2;
        var rng = new ScriptedRng([]);

        Assert.False(new BattleSim(state, rng, C).BeginTurn(victim));
        Assert.Equal(0, rng.Used);
        Assert.True(victim.TransformedLong);
    }

    /// <summary>Без бита 23 умерший от яда не бросает ничего — 43 смерти от DoT корпуса из 43 (20260906-e … 20261016b-port-synth).</summary>
    [SkippableFact] public void DeathFromPoison_WithoutTransform_SpendsNoRoll()
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Squire, slot: 0, hp: 10));
        var state = State(witch, victim);
        victim.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 15, AppliedRound = 1, Long = true });
        state.Round = 2;
        var rng = new ScriptedRng([]);

        Assert.False(new BattleSim(state, rng, C).BeginTurn(victim));
        Assert.Equal(0, rng.Used);
        Assert.False(victim.Alive);
    }

    // ---- откат после боя ------------------------------------------------------------------------

    /// <summary>Внеочередной ход встаёт сразу за ходящим, а очередь движка — 13 ячеек от ходящего (<c>0x62266e</c>, REA 13.10): при
    /// полной очереди последний ход хвоста теряется. Прежде очередь порта росла без предела (аудит 13.10).</summary>
    [SkippableFact] public void ExtraTurn_QueueCappedAtThirteenCells()
    {
        ImportedData.RequireRules();
        var units = Enumerable.Range(0, 13).Select(i => Of(Squire, attacker: i % 2 == 0, slot: i / 2, id: $"u{i}")).ToArray();
        var lucky = units[5];
        var state = State(units);
        var sim = new BattleSim(state, Rolls(), C);
        var queue = units.ToList();
        lucky.ExtraTurn = true;

        sim.InsertExtraTurns(queue, after: 0);

        Assert.Equal(BattleSim.TurnsOrderCapacity, queue.Count);
        Assert.Same(lucky, queue[1]);
        Assert.DoesNotContain(units[12], queue);
        Assert.False(lucky.ExtraTurn);
    }

    /// <summary>Откат превращения после боя (survey §7, второй пункт из четырёх):
    /// <c>CMidUnit::undoTransform</c> (GOG <c>0x5ea3bd</c>) возвращает исходную реализацию и, если
    /// при превращении <c>keepHp</c> был ложью, пересчитывает HP обратно тем же пропорциональным
    /// правилом. Волчий лорд: 100 → 122 (в форме) → 99 (обратно), потому что 122 · 225 / 275 = 99,8.
    /// <para>Округление вниз в обе стороны — не ошибка модели, а свойство движка: <c>idiv</c>
    /// усекает, и юнит теряет один HP на паре превращение-откат.</para></summary>
    [SkippableFact] public void AfterBattle_UndoesTransform_AndScalesHpBack()
    {
        ImportedData.RequireRules();
        var wolf = Of(WolfLord, attacker: true, hp: 100);
        var state = State(wolf);
        var sim = new BattleSim(state, Rolls(), C);
        Attacks.Apply(wolf, Attack(WolfChange), wolf, Rolls(), C, round: 1, state);
        Assert.Equal(122, wolf.Hp);

        sim.AfterBattle();

        Assert.Equal(WolfLord, wolf.Type.Id);
        Assert.Equal(225, wolf.HpMax);
        Assert.Equal(99, wolf.Hp);                 // 122 · 225 / 275 = 99,8 -> 99
        Assert.Null(wolf.OrigType);
        Assert.False(wolf.TransformedSelf);
        Assert.Equal(Rules.Value.Units[WolfLord].AttackId, wolf.Attack.Id);
        output.WriteLine($"откат: hp 100 -> 122 -> {wolf.Hp}, тип {wolf.Type.Id}");
    }

    /// <summary>У <c>TransformOther</c> при превращении <c>keepHp</c> был истиной, и откат HP тоже
    /// не трогает (<c>0x5ea48e</c>: пересчёт под тем же флагом, что и при превращении, но взятым
    /// из сохранённого поля юнита).</summary>
    [SkippableFact] public void AfterBattle_KeepsHp_WhenTransformKeptIt()
    {
        ImportedData.RequireRules();
        var (witch, victim) = (Of(Witch, attacker: true), Of(Squire, slot: 0, hp: 40));
        var state = State(witch, victim);
        Attacks.Apply(witch, Attack(WitchCurse), victim, Rolls(), C, round: 1, state);
        Assert.Equal(40, victim.Hp);

        new BattleSim(state, Rolls(), C).AfterBattle();

        Assert.Equal(Squire, victim.Type.Id);
        Assert.Equal(100, victim.HpMax);
        Assert.Equal(40, victim.Hp);
        Assert.Null(victim.OrigType);
        Assert.False(victim.Transformed);
        output.WriteLine($"откат с keepHp: hp остался {victim.Hp}, тип {victim.Type.Id}");
    }

    /// <summary>Мёртвый превращённый откатывается тоже, но нулевое HP нулевым и остаётся: в
    /// <c>undoTransform</c> есть проверка «а был ли HP ненулевым» (<c>0x5ea4b5</c>), которой в
    /// самом превращении нет. Долг задачи 6 закрыт здесь же: призванный после боя из отряда
    /// исчезает (<c>applyCBatAttackUnsummonEffect</c>), и проверяется он раньше превращения.</summary>
    [SkippableFact] public void AfterBattle_RemovesSummons_AndKeepsDeadAtZero()
    {
        ImportedData.RequireRules();
        var occultist = Of(Occultist, attacker: true);
        var wolf = Of(WolfLord, attacker: true, slot: 1);
        var state = State(occultist, wolf);
        Attacks.Apply(occultist, Attack(SummonOne), occultist, Rolls(), C, round: 1, state);
        Assert.Equal(3, state.Units.Count);
        Attacks.Apply(wolf, Attack(WolfChange), wolf, Rolls(), C, round: 1, state);
        wolf.Hp = 0;

        new BattleSim(state, Rolls(), C).AfterBattle();

        Assert.Equal(2, state.Units.Count);
        Assert.DoesNotContain(state.Units, u => u.Summoned);
        Assert.Equal(WolfLord, wolf.Type.Id);
        Assert.Equal(0, wolf.Hp);
        // Сообщения боя для показа и звука: ушёл призванный, откатился превращённый (docs/ai-screen.md §11).
        Assert.Equal([BattleNoteKind.Untransformed, BattleNoteKind.Unsummoned], state.Notes.Select(n => n.Kind).Order());
        Assert.Contains(state.Notes, n => n.Kind == BattleNoteKind.Untransformed && n.UnitId == wolf.Id);
        output.WriteLine($"после боя в отряде {state.Units.Count} юнита, призванный снят, " +
                         $"павший превращённый откачен на {wolf.Type.Id} с hp {wolf.Hp}");
    }

    /// <summary>Юнит, никого не превращавший и никем не призванный, откатом не трогается вовсе —
    /// иначе <see cref="BattleSim.AfterBattle"/> портил бы обычные бои.</summary>
    [SkippableFact] public void AfterBattle_LeavesOrdinaryUnitsAlone()
    {
        ImportedData.RequireRules();
        var squire = Of(Squire, attacker: true, hp: 33);
        var state = State(squire);

        new BattleSim(state, Rolls(), C).AfterBattle();

        Assert.Single(state.Units);
        Assert.Equal(Squire, squire.Type.Id);
        Assert.Equal(33, squire.Hp);
        Assert.Equal(100, squire.HpMax);
    }
}
