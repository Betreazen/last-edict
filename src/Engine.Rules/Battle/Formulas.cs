using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Battle;

/// <summary>Чистые формулы боя. Каждая сверяется с корпусом (С1, ТЗ §7): power, miss, dmg_max,
/// dmg_buffs, armor, dmg — на нынешнем корпусе 0 расхождений. Порядок вычисления — из журнала:
/// armor -> damageMax -> damageWithBuffs(addRandom) -> damage = dwb − dwb·armor/100 -> крит.
/// Ранг доверия помечен у каждой функции: 1 — измерено корпусом или прочитано в дизассемблере
/// движка (GOG Discipl2.exe) до ret, 2 — из разбора движка и не противоречит корпусу,
/// 3 — гипотеза, корпус к ней слеп.</summary>
public static class Formulas
{
    /// <summary>Классы, у которых точность берётся из атаки; остальные не промахиваются —
    /// power = 100 и ни одного броска.
    /// <para><b>Ранг 1, поднят задачей 6 Ф6 со второго.</b> Список прочитан не в тулсете, а в самом
    /// движке: у каждого из 23 классов свой <c>CBatAttack*</c>, и слот 6 его vftable — это
    /// <c>canMiss</c> (<c>batattack.h:76-79</c>). Он либо общая заглушка <c>0x6598f5</c>
    /// (<c>xor al,al</c> — «промахнуться нельзя»), либо своя функция с <c>mov al,1</c>. Vftable
    /// каждого класса добыты из <c>createBatAttack</c> (GOG <c>0x644b3e</c>): ветка по категории →
    /// конструктор → адрес таблицы. Ранг подпёрт корпусом с двух сторон: <c>Heal</c> (заглушка) не
    /// бросает ни разу (20260906-b, 107 ходов лекарей), <c>Paralyze</c> и <c>Fear</c> (свои функции)
    /// бросают ровно две кости (20260906-c).</para>
    /// <para><b>Три исправления против прежнего списка</b> (он шёл из <c>attackHasPower</c> тулсета,
    /// <c>attackutils.cpp:131-142</c>, и в трёх местах врал): <c>Revive</c> (vft <c>0x6f32cc</c>),
    /// <c>BestowWards</c> (<c>0x6f2fac</c>) и <c>Shatter</c> (<c>0x6f2ffc</c>) несут заглушку — они
    /// <b>не промахиваются и не тратят костей</b>. У <c>Shatter</c> при этом <i>своя</i>
    /// <c>isImmune</c> (<c>0x65d899</c>): «не промахивается» и «не проверяет иммунитет» — разные
    /// свойства, см. <see cref="Immunity.Checked"/>. Прежний список съедал по две кости на каждый
    /// такой удар и сдвинул бы поток С2 на первом же бою с вампирским жрецом или дробителем.</para>
    /// <para><b>Четвёртое исправление, задача 7: <c>TransformSelf</c> убран из списка.</b> Правило
    /// «заглушка ⇔ ноль костей» тут не работает: слот 6 у него <i>свой</i> (<c>0x6611a7</c>), но
    /// тело — не <c>mov al,1</c>, а развилка. Прочитано до <c>ret</c>: если цель совпадает с самим
    /// превращающимся (<c>CMidgardID::operator==</c> <c>0x623b60</c> на <c>this-&gt;unitId</c>) —
    /// <c>xor al,al</c>, то есть <b>промахнуться нельзя и костей ноль</b>; иначе ответ отдаёт
    /// запасная атака (<c>ALT_ATTACK</c>, слот 6 её vftable). В ванили TransformSelf применяется
    /// именно к себе (Волчий лорд <c>g000uu0164</c>, атака <c>g000aa0164</c>), а вторая ветвь — это
    /// уже другая атака, и модель её не держит: <see cref="TransformAttacks.TransformSelf"/>.
    /// Оставленный в списке <c>TransformSelf</c> съедал бы по две кости на каждое превращение.</para>
    /// <para><c>TransformOther</c> в списке остаётся и он честный: слот 6 (<c>0x6607f3</c>) — ровно
    /// <c>mov al,1</c>, ранг 1. У <c>Doppelganger</c> слот 6 (<c>0x661fd5</c>) снова развилка
    /// (пока не превратился — <c>xor al,al</c>, дальше запасная атака), и его в списке нет — это
    /// совпадает с моделируемой ветвью. <c>GiveAttack</c> несёт общую заглушку <c>0x6598f5</c>.</para></summary>
    /// <para>С этапа Э2 список — признак эффекта (<see cref="Effects.AttackEffect.HasPower"/>): Paralyze, Petrify, Damage, Drain,
    /// DrainOverflow, Fear, LowerDamage, LowerInitiative, Poison, Frostbite, Blister, DrainLevel, TransformOther.</para>
    public static bool ClassHasPower(AttackClass cls) => Effects.AttackEffects.Of(cls).HasPower;

    /// <summary>Точность атаки. Потолок сигнатуры: getAttackPower движка правит результат тем,
    /// чего здесь нет — сдвиг ИИ по сложности (<see cref="AiPowerShift"/>, его накладывает вызывающий),
    /// надбавка за round &gt; 40, кламп по restrictions.
    /// Штраф магу-лидеру вычитает вызывающий (<see cref="MageLeaderPowerReduction"/>): он зависит
    /// не от атаки, а от места цели в перемешанной группе.</summary>
    public static int AttackPower(AttackType attack) => attack.Effect.HasPower ? attack.Power : 100;

    /// <summary>
    /// Сдвиг точности юнита ИИ по сложности партии — <c>getAttackPower</c> (GOG <c>0x62764a</c>), ранг 1 по листингу
    /// capstone (16.09.2026): группа юнита (<c>0x6226e8</c> → <c>0x6215df</c>/<c>0x621613</c>) проверяется на
    /// игрока-человека (<c>isGroupOwnerPlayerHuman 0x626ca5</c>, <c>0x6278fa</c>); человеку сдвига нет
    /// (<c>jne 0x6279af</c>). Иначе по <c>CScenarioInfo.gameDifficulty</c> (<c>+0x40</c>, <c>0x5e39d5</c>; в
    /// <c>.sg</c> — <c>DIFFGAME</c>): лёгкая <c>0x837988</c> — <c>−15</c> (<c>0x62793d</c>), трудная <c>0x8379a8</c> —
    /// <c>+5</c> (<c>0x627970</c>), очень трудная <c>0x837998</c> — <c>+10</c> (<c>0x6279a3</c>), средняя — ничего.
    /// Имена категорий — ранг 2 (тулсет <c>difficultylevel.cpp</c>, Gog). Сдвиг идёт до штрафа мага-лидера
    /// (<c>0x627a08</c>). Корпус: <c>20260915-passive</c> — юниты ИИ на лёгкой, <c>power</c> 65 при <c>POWER</c> 80.
    /// </summary>
    public static int AiPowerShift(LastEdict.Rules.Map.Difficulty difficulty) => difficulty switch
    {
        LastEdict.Rules.Map.Difficulty.Easy => -15,
        LastEdict.Rules.Map.Difficulty.Hard => 5,
        LastEdict.Rules.Map.Difficulty.VeryHard => 10,
        _ => 0,
    };

    /// <summary>Точность после сдвига: движок кладёт её в <c>Percent</c> сеттером <c>0x5838d0</c>, который
    /// принимает значение только в <c>[−100, 100]</c> (<c>0x583879</c>), иначе оставляет прежнее. Поэтому 100 + 5
    /// у трудной сложности остаётся 100. Ранг 1.</summary>
    public static int ShiftPower(int power, int shift) =>
        power + shift is >= -100 and <= 100 ? power + shift : power;

    /// <summary>Шаг штрафа мага-лидера: <c>restrictions.mageLeaderAttackPowerReduction</c> — байт
    /// <c>add eax, 0xa</c> по GOG <c>0x625264</c> (тулсет: <c>restrictions.cpp</c>, GOG <c>0x625266</c>).</summary>
    public const int MageLeaderPowerStep = 10;

    /// <summary>Глобальные реализации «магов-лидеров» — список ЗАШИТ в код (GOG <c>0x627db1</c>,
    /// прочитан до <c>ret 8</c>): глобальный impl-id юнита (слот 4 vftable, <c>call [edx+0x10]</c>)
    /// сравнивается с шестью строками — <c>0x6f1f30</c> G000UU8010, <c>0x6d95e8</c> G000UU0021,
    /// <c>0x6f1f24</c> G000UU0046, <c>0x6f1f18</c> G000UU0098, <c>0x6f1f0c</c> G000UU0097,
    /// <c>0x6f1f00</c> G000UU0072. Ни UNIT_CAT, ни LEADER_CAT не читаются: нейтральный лидер
    /// G000UU5262 (UNIT_CAT 2, площадной огонь) штрафа не получает — корпус это подтверждает.</summary>
    private static readonly HashSet<string> MageLeaders = new(StringComparer.OrdinalIgnoreCase)
        { "G000UU8010", "G000UU0021", "G000UU0046", "G000UU0098", "G000UU0097", "G000UU0072" };

    /// <summary>Штраф точности площадной атаки мага-лидера по цели, стоящей на месте
    /// <paramref name="place"/> перемешанной группы (<see cref="Attacks.Shuffle"/>, мёртвые включительно).
    /// <para><b>Ранг 1.</b> Движок: в цикле по целям <c>battleTurn</c> после каждой цели
    /// <c>if (isMageLeader(attacker)) setAttackPowerReduction(get + 10)</c> (GOG
    /// <c>0x625244</c>–<c>0x625275</c>; <c>get</c> <c>0x621f86</c>, <c>set</c> <c>0x621f53</c>), а
    /// <c>getAttackPower</c> вычитает накопленное (тулсет <c>hooks.cpp:1767</c>); обнуляет
    /// <c>beforeBattleTurn</c> (тулсет <c>hooks.cpp:1899</c>), поэтому модель считает штраф заново на
    /// каждое действие и не хранит его. Корпус: лестница у 22 ходов магов-лидеров из 22 — точность =
    /// база − 10 · место цели в ВСЕЙ перемешанной группе (80/60/50/40/30 у вампира при мёртвом на
    /// втором месте); счёт среди живых сходится лишь у 15 из 22.</para></summary>
    public static int MageLeaderPowerReduction(string globalUnitId, int place) =>
        MageLeaders.Contains(globalUnitId) ? MageLeaderPowerStep * place : 0;

    /// <summary>Промах = среднее двух бросков rng(100) СТРОГО больше точности. Два броска — факт
    /// корпуса (rng_after - rng_before == 2 у каждого miss). Порог измерен 06.09.2026 на корпусе
    /// 20260906-fast (52 боя): среднее == power (83+77 при 80, дважды) — попадание, 76+86 (81) —
    /// промах; ранг 1 для чётной суммы. Нечётная сумма ровно 2*power+1 в корпусе не встречалась,
    /// поэтому «целочисленное среднее &gt; power» против «сумма &gt; 2*power» — ранг 3 (различаются
    /// только там). Счётчик miss_boundary в C1HarnessTests считает пограничные случаи.</summary>
    public static bool ShouldMiss(int power, int roll1, int roll2) => (roll1 + roll2) / 2 > power;

    /// <summary>typeIndex (младшие 16 бит id) десяти глобальных реализаций лидеров, которым движок
    /// даёт +LeaderBonusMaxDamage. Список зашит в код getUnitImplDamageMax (GOG 0x61dc4a-0x61dc7b),
    /// это не свойство Gunits.dbf: docs/re-damage-max.md. Pegasus Knight, Ranger, King's Guard,
    /// Engineer, Dwarf Champion, Duke, Counselor, Death Knight, Forest Liege, Guardian.</summary>
    private static readonly HashSet<int> LeaderBonusTypeIndices =
        [0x0019, 0x0020, 0x0044, 0x0045, 0x0047, 0x0070, 0x0071, 0x0096, 0x8009, 0x8011];

    /// <summary>getUnitImplDamageMax (GOG 0x61dbe3, вызывается из computeDamageMax 0x61db98):
    /// потолок урона = MaxDamage (300, attackDamage.max из .rdata) + LeaderBonusMaxDamage (100,
    /// fighterExplorerLeaderBonusMaxDamage) для десяти прошитых typeIndex глобального impl-id;
    /// категория, LEADER_CAT, BASE_UNIT, уровень не читаются. Ранг 1: дизассемблирование до ret
    /// плюс два юнита живого боя (g000uu0019 -> 400, g000uu5130 -> 300), docs/re-damage-max.md.
    /// <paramref name="unitTypeId"/> — глобальный id вида g000uu0019. Прокачанный лидер (UG) в движке
    /// сводится к базовому UU через CUnitGenerator::getGlobalUnitImplId; у нас типы приходят только
    /// из Gunits (UU), поэтому UG не поддержан и бросает, а не молчит.</summary>
    public static int DamageMax(RulesConstants c, string unitTypeId)
    {
        if (unitTypeId.Length != 10)
            throw new ArgumentException($"id типа юнита не 10 символов: '{unitTypeId}'", nameof(unitTypeId));
        var kind = unitTypeId.Substring(4, 2).ToLowerInvariant();
        if (kind == "ug")
            throw new NotSupportedException($"{unitTypeId}: UG-id прокачанного лидера нужно свести к базовому UU " +
                                            "(getGlobalUnitImplId), в правилах этого сведения нет");
        if (kind != "uu") return c.MaxDamage;   // движок: не UU/UG -> base
        int typeIndex = Convert.ToInt32(unitTypeId.Substring(6, 4), 16);
        return c.MaxDamage + (LeaderBonusTypeIndices.Contains(typeIndex) ? c.LeaderBonusMaxDamage : 0);
    }

    /// <summary>computeDamageWithBuffs (GOG <c>0x5d2db1</c>, прочитан до <c>ret 0x18</c>):
    /// <code>
    /// base = min(attack.qtyDamage, damageMax)                                       0x5d2dbf-0x5d2dcd
    /// dmg  = base + rng(BAT_DAMAGE)   (только при addRandom)                        0x5d2dd1-0x5d2dfa
    /// dmg += Percent::of(base, BATBOOSTDn) за каждый взведённый BoostDamageLvl1..4   0x5d2e05-0x5d2ec1
    /// dmg -= Percent::of(base, BATLOWERDn) за каждый взведённый LowerDamageLvl1..2   0x5d2ec1-0x5d2f1d
    /// dmg  = clamp(dmg, attackDamage.min, damageMax)                                 0x5d2f1f-0x5d2f34
    /// </code>
    /// <para><b>Ранг 1, и прежняя запись здесь была неверна.</b> Проценты берутся от <b>базы</b> и
    /// усекаются <i>каждый по отдельности</i> (<c>Percent::of</c> — <c>0x5838c0</c>:
    /// <c>imul; cdq; idiv 100</c>), а не сводятся в один множитель <c>(100 + boost − lower)/100</c>,
    /// как считалось (там и стоял ранг 3 — «в корпусе boost == lower == 0»). Разница видна на первом
    /// же живом дебафе: <c>corpus/20260909-arena2/map-001.jsonl</c>, King's Guard под
    /// <c>LowerDamageLvl2</c> (<c>BATLOWERD2</c> = 33 %) — журнал <b>41</b>
    /// (<c>60 − 60·33/100 = 60 − 19</c>), единый множитель дал бы <b>40</b> (<c>60·67/100</c>), два
    /// последовательных усечения — те же 40. Тем же порядком объясняются 35 → 24 и 20 → 14 у
    /// <c>S143UN000b</c> в том же бою.</para>
    /// <para><b>Случайная добавка не умножается</b>: в движке она уходит в <c>esi</c>, а проценты
    /// считаются от <c>edi</c> (базы). Ранг 1 для самой добавки — 30 сверок ещё Ф4.</para>
    /// <para><b>Два уровня одного статуса.</b> Сюда приходит уже сложенный процент, а движок
    /// складывает усечённые слагаемые. Различить это может только юнит, у которого взведены два
    /// уровня буста (или дебафа) сразу; класс <c>BoostDamage</c> кладёт свой уровень поверх
    /// прежнего, так что в ванили этого не бывает — ранг 3, цена вопроса единица урона.</para>
    /// <para><b>Пол <c>attackDamage.min</c> = 1 (<c>.rdata 0x6e9718</c>) здесь не воспроизводится</b>
    /// — он недостижим: <c>BATLOWERD</c> в ванили не больше 50 %, а <c>base − base·50/100 ≥ 1</c>
    /// при любой базе от единицы. Появится дебаф на 100 % — придётся дописать.</para>
    /// <para><b><paramref name="humanOnEasy"/> удваивает ДИАПАЗОН добавки — ранг 1 по коду, задача 16
    /// Ф7.</b> <c>0x5d2dd6</c>–<c>0x5d2df2</c>: <c>eax = BAT_DAMAGE</c> (<c>0x5877e7</c>); шестой
    /// аргумент <c>[ebp+0x1c]</c> ненулевой → <c>add eax, eax</c> (<c>0x5d2def</c>) → <c>rng(eax)</c>.
    /// Признак считает <c>computeDamage</c> (<c>0x65b790</c>–<c>0x65b88b</c>): сторона атакующего
    /// (<c>IsUnitAttacker</c> <c>0x6226e8</c> → игрок атакующих <c>0x621647</c> или защитников
    /// <c>0x62167b</c>) → <c>findPlayer</c> <c>0x62a980</c> → человек (<c>0x62a1a0</c>) → сложность
    /// сценария равна <c>LDifficultyLevel</c> <c>0x837988</c> (лёгкая). В корпусе событий
    /// <c>dmg_buffs</c> с <c>easy = true</c> ноль — живьём не мерилось.</para></summary>
    public static int DamageWithBuffs(AttackType attack, int damageMax, int boostPercent, int lowerPercent,
                                      bool addRandom, IRng rng, RulesConstants c, bool humanOnEasy = false)
    {
        int baseDamage = Math.Min(attack.QtyDamage, damageMax);
        int dmg = baseDamage + (addRandom ? rng.Next(humanOnEasy ? 2 * c.BatDamage : c.BatDamage) : 0);
        dmg += baseDamage * boostPercent / 100 - baseDamage * lowerPercent / 100;
        return Math.Min(dmg, damageMax);
    }

    /// <summary>computeArmor (GOG 0x5d2c8f, дизассемблирован до ret 07.09.2026, перечитан задачей 29 Ф7):
    /// <code>
    /// soldier = unit->getArmor()                   0x5d2cd9: IUsSoldier, слот 10 (call [edx+0x28])
    /// armor   = max(soldier − shattered, fort)     0x5d2d02–0x5d2d18: getUnitShatteredArmor 0x621ebc
    ///                                              (BMD +0x9c), getUnitFortificationArmor 0x621f21 (+0xa0)
    /// Defend: armor = min(armor + _ftol(DFENDBONUS·0.01·(100 − armor)), unitArmor.max)   0x5d2d20–0x5d2d7e
    /// </code>
    /// Сеттер результата <c>0x5838d0</c> ничего не режет: он принимает только −100…100 (<c>0x583879</c>).
    /// <para><b>Потолок 90 без защиты — задача 29 Ф7, ранг 1 по коду.</b> Режет не computeArmor, а
    /// <c>CUmUnit::getArmor</c> (GOG <c>0x5a465a</c>, слот 10 vftable <c>IUsSoldier</c> у CUmUnit
    /// <c>0x6ead9c</c>): <c>clamp(prev-&gt;getArmor + data.armor, unitArmor.min, unitArmor.max)</c>
    /// (<c>0x5a468e</c>–<c>0x5a46b0</c>, <c>.rdata 0x6e88f4 = {0, 90}</c>) на КАЖДОМ слое модификаторов —
    /// см. <see cref="Units.EffectiveStats.ArmorMax"/>. Корпус: страж столицы <c>S143UN0002</c>
    /// (<c>G000UU3002</c>, <c>ARMOR</c> 50) с <c>fort</c> 50 — 96 событий <c>armor</c> = <b>90</b>
    /// (<c>20260911-passive/passive-003</c> 47, <c>-004</c> 49); прежняя сумма давала 100.</para>
    /// <para><b>Почему укрепление складывается с бронёй — ранг 1 в числах, ранг 3 в механизме.</b>
    /// С голой бронёй <c>Gunits</c> формула <c>max(soldier − shattered, fort)</c> не дала бы ни 50 у
    /// <c>S073UN018c</c> (<c>ARMOR</c> 30, <c>fort</c> 20), ни 90 у стража. Числа сходятся, если защита
    /// города (<c>GVars PROT_1..5</c> = 10…30, <c>PROT_CAP</c> = 50) лежит на юните отдельным слоем
    /// CUmUnit, равным <c>fort</c>: 30 + 20 = 50, min(50 + 50, 90) = 90. Тулсет называет такое наложение
    /// <c>ApplyFortificationArmor</c> (Akella <c>0x4212a2</c>, <c>custommodifier.cpp:921</c>); адрес GOG
    /// не искался. Модель кладёт этот слой последним — порядок против цепочки юнита различим только при
    /// отрицательном слое брони (Pain/Rust −50, Mold −80) у защитника города, ранг 3.</para>
    /// <para>Пока <c>soldier + fort ≤ 90</c>, это ровно прежняя сумма:
    /// <c>max(b + f − s, f) = max(b − s, 0) + f</c>, — поэтому ни одна прежняя сверка не меняется.</para>
    /// То есть защита закрывает DFENDBONUS % оставшегося до 100 промежутка: база 0 → 50, 40 → 70,
    /// 60 → 80 — ни «+50», ни «=50». Ранг 1: дизассемблер + корпус 20260906-a (Man at Arms, база 0:
    /// четыре события armor = 50 под статусом и 0 после снятия). Целочисленное bonus·(100−a)/100
    /// против движкового double: при DFENDBONUS 50 совпадает точно (0.5·k усекается одинаково);
    /// ponytail: при ином DFENDBONUS возможна разница в 1 у границы усечения — тогда считать в double.
    ///
    /// <para><b>Укрепление ПРИБАВЛЯЕТСЯ, а не служит полом — исправлено задачей 16 по корпусу.</b>
    /// Прежняя редакция читала это место как <c>max(base − shattered, fort)</c>, и на всём прежнем
    /// корпусе это было неотличимо: там у всех защитников укрепления база брони равна нулю, а
    /// <c>max(0, f) = 0 − 0 + f</c>. Партия на большой карте различила: <c>S073UN018c</c>
    /// (<c>G000UU5356</c>, <c>Gunits.ARMOR</c> = 30) стоит в укреплении с <c>fort</c> = 20 и во всех
    /// десяти событиях <c>armor</c> даёт <b>50</b>, тогда как <c>max(30, 20)</c> дал бы 30.
    /// Сплошной проход по журналу <c>20260910-passive/passive-004.jsonl</c>: событий <c>armor</c> с
    /// <c>fort &gt; 0</c> — 183, из них 173 с нулевой базой (обе редакции совпадают) и <b>10 с
    /// ненулевой — все десять за сумму, ни одного за максимум</b>.</para>
    /// <para>Оговорка задачи 16 («сумма или максимум плюс слой города — неразличимо») снята задачей 29:
    /// в коде стоит максимум (<c>0x5d2d05 cmp; jl</c>), а потолок 90 у стража столицы выдаёт слой — его
    /// режет только <c>CUmUnit::getArmor</c>. Остаётся ранг 3 на том, что слой равен именно
    /// <c>fort</c>, а не <c>PROT</c> другого происхождения.</para>
    /// <para><b>Ветка сбитой брони — ранг 3, и это измерено:</b> событий <c>armor</c> с
    /// <c>shattered &gt; 0</c> во ВСЁМ корпусе <b>ноль</b>. Поэтому <c>max(…, 0)</c> выбран так,
    /// чтобы сохранить всё, что прежнее чтение утверждало и что проверено отдельными тестами:
    /// снятое сверх базовой брони пропадает, а ниже укрепления броня не падает (при
    /// <c>shattered ≥ base</c> обе редакции дают ровно <c>fort</c>). Различает их только случай
    /// <c>0 &lt; shattered &lt; base</c> при <c>fort &gt; 0</c>: прежнее чтение дало бы
    /// <c>base − shattered</c>, это — <c>base − shattered + fort</c>. Снимет удар «крошителя» по
    /// защитнику города с ненулевой собственной бронёй.</para>
    /// <para>База берётся через <c>EffectiveStats</c>, то есть с цепочкой модификаторов юнита
    /// (<c>GmodifL TYPE 5</c> — броня): раньше цепочки в бою просто не было.</para></summary>
    public static int Armor(BattleUnit unit, RulesConstants c)
    {
        int fort = unit.FortificationArmor;
        int armor = Math.Max(SoldierArmor(unit, c.MaxArmor) - unit.ShatteredArmor, fort);
        if (!unit.Defending) return armor;
        return Math.Min(armor + c.DefendBonus * (100 - armor) / 100, c.MaxArmor);
    }

    /// <summary><c>IUsSoldier::getArmor</c> юнита (слот 10, <c>call [edx+0x28]</c>) — броня цепочки модификаторов
    /// со слоем защиты города; её читают <see cref="Armor"/> (<c>0x5d2cd9</c>) и <c>canPerform</c> Shatter
    /// (<c>0x65d82d</c>, <see cref="Attacks.CanPerform"/>). Потолок слоя у движка — константа <c>unitArmor</c> {0, 90}
    /// (<c>0x6e88f4</c>, <see cref="Units.EffectiveStats.ArmorMax"/>); <c>canPerform</c> констант правил не получает и берёт её.</summary>
    public static int SoldierArmor(BattleUnit unit, int maxArmor = Units.EffectiveStats.ArmorMax)
    {
        int soldier = Units.EffectiveStats.Of(unit.Type, unit.Chain).Armor;
        // ponytail: слой защиты города заводится только при fort > 0 — голую реализацию Gunits движок
        // не режет, а в ванили ARMOR ≤ 65, так что без слоя отличить нечем.
        int fort = unit.FortificationArmor;
        return fort > 0 ? Math.Clamp(soldier + fort, Units.EffectiveStats.ArmorMin, maxArmor) : soldier;
    }

    /// <summary>computeDamage (GOG 0x65b747, 0x65b8c9–0x65b8d1): damage = dwb − dwb·armor/100,
    /// где dwb·armor/100 — Percent::of (0x5838c0: imul; cdq; idiv 100), целочисленно. Ранг 1:
    /// дизассемблер + корпус 20260906-a: dwb 27 при броне 50 → 14; формула тулсета
    /// dwb·(100 − armor)/100 (hooks.cpp:1638) даёт 13 — тулсет здесь движок не воспроизводит.
    /// Округление половины вверх (round(13.5) = 14) на этом случае неотличимо, различает dwb 27 при
    /// броне 40 (17 против 16) — Gargoyle в сценарии _d; C1 ведёт наблюдение dmg_round_alt.
    /// Следствие: при armor &lt; 100 и dwb ≥ 1 урон не меньше 1. Отрицательный урон при armor &gt; 100
    /// не отсекается — как в движке (потолок 90 делает это недостижимым).</summary>
    public static int ComputeDamage(int damageWithBuffs, int armor) =>
        damageWithBuffs - damageWithBuffs * armor / 100;

    /// <summary>Процент критического удара — литерал <c>push 5</c> на <c>0x65b97d</c> в ветке крита
    /// <c>computeDamage</c> (<c>0x65b8f3</c>–<c>0x65b9bf</c>). В данных его нет: ни в 83 колонках
    /// <c>GVars</c>, ни в <c>Gattacks</c> (там <c>CRIT_HIT</c> — только флаг). Ранг 1, задача 15 Ф7,
    /// разбор <c>.superpowers/sdd/2026-09-11-f7-port/crit.md</c>.</summary>
    public const int CriticalHitPercent = 5;

    /// <summary><c>LleadA.dbf L_CRITICAL_HIT</c> — способность лидера, которую проверяет
    /// <c>leader-&gt;hasAbility</c> (слот 7, <c>0x65b92a</c>–<c>0x65b962</c>, категория
    /// <c>0x8375d8</c>). В ванили её выдаёт только улучшение <c>g000um4103</c> (<c>GmodifL TYPE 11</c>).
    /// Ранг 1 для проверки, ранг 2 для имени категории.</summary>
    public const int LeaderAbilityCriticalHit = 17;

    /// <summary>Крит — <c>Percent(5).of(dwb)</c>: <c>push dwb; push 5; ctor 0x586b7a; call 0x5838c0</c>
    /// (<c>imul; cdq; idiv 100</c>), <c>0x65b979</c>–<c>0x65b9a1</c>. Ранг 1.
    /// <para>База — <paramref name="damageWithBuffs"/>, то есть результат <c>0x5d2db1</c>: со случайной
    /// добавкой, боевыми усилениями и ослаблениями и зажимом в dmax. Крит прибавляется к урону уже
    /// ПОСЛЕ брони (<c>0x65b99f add edx, eax</c>) и сам бронёй и защитой не режется; броска нет — в
    /// ветке нет ни <c>0x54851f</c>, ни <c>attackShouldMiss</c>. При dwb &lt; 20 крит 0, при dmax 300
    /// не больше 15.</para></summary>
    public static int CriticalDamage(int damageWithBuffs) => damageWithBuffs * CriticalHitPercent / 100;

    /// <summary>Критует ли удар. Ветку крита зовёт только <c>Damage::onHit</c> (<c>0x65b577 push 1</c>);
    /// <c>Drain</c> и <c>DrainOverflow</c> передают 0 — отсюда проверка класса. Внутри ветки:
    /// <c>attack-&gt;getCritHit()</c> (<c>0x65b910</c>, слот 15) либо атакующий — лидер с
    /// <see cref="LeaderAbilityCriticalHit"/>. Источник атаки не читается. Ранг 1.
    /// <para><b>Потолок сигнатуры.</b> Движок перед <c>hasAbility</c> требует
    /// <c>castUnitImplToStackLeader ≠ NULL</c> (<c>0x40a41f</c>), а у <see cref="UnitType"/> категории
    /// юнита нет. Здесь способность берётся из цепочки модификаторов
    /// (<see cref="Units.EffectiveStats.LeaderAbilities"/>): в ванили 17 приходит только лидерским
    /// улучшением, так что разницы нет; мод, выдавший <c>TYPE 11</c> не-лидеру, потребует
    /// <c>LEADER_CAT</c>/<c>UNIT_CAT</c>.</para></summary>
    public static bool HasCriticalHit(AttackType attack, BattleUnit attacker) =>
        attack.Class == AttackClass.Damage &&
        (attack.CritHit ||
         Units.EffectiveStats.Of(attacker.Type, attacker.Chain).LeaderAbilities.Contains(LeaderAbilityCriticalHit));
}
