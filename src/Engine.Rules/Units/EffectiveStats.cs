using System.Runtime.CompilerServices;
using LastEdict.Rules.Battle;

namespace LastEdict.Rules.Units;

/// <summary>Эффективная атака и накопленный вампиризм (<see cref="ModifierElementType.AttackDrain"/>).
/// У <see cref="AttackType"/> поля вампиризма нет — базу (<c>drainHealPercent</c> = 50) складывает
/// с <see cref="DrainPercent"/> задача 6.</summary>
public sealed record EffectiveAttack(AttackType Attack, int DrainPercent);

/// <summary>Эффективные статы юнита: база из <c>Gunits</c>, поверх неё цепочка модификаторов.
/// <para><b>Форма свёртки.</b> Взята не из головы:
/// <c>reference/D2ModdingToolset/Scripts/Modifiers/template.lua</c> — 68 функций вида
/// <c>getX(unit, prev)</c>. Слои накладываются по порядку, и каждый видит результат предыдущего,
/// а не базу. <b>Ранг 3</b> для арифметической стороны этого: «процент от предыдущего слоя»
/// против «все проценты от базы» различимо только на двух и более процентных слоях одного стата,
/// и в ванили такие пары редки. Снимает задача 13: два бафа инициативы на одном юните
/// (<c>g000um2307</c> +50 % поверх <c>g000um0002</c> +10 %) дают 165 против 160 при базе 100 —
/// разница видна в очереди ходов журнала.</para>
/// <para><b>Порядок наложения</b> берётся из порядка списка; для сохранённой игры это порядок
/// хранения <c>MidUnit.Modifiers</c> (<c>Sg.g.cs:3804</c>). <b>Ранг 3</b>: что движок накладывает
/// именно в этом порядке, а не сортируя по <c>Gmodif.SOURCE</c>, журналом не проверено — снимает
/// та же задача 13 на юните с двумя процентными модификаторами.</para>
/// <para><b>Потолки: свои у атаки, чужих не заводим.</b> Точность и инициативу режет сама
/// цепочка — <see cref="OfAttack"/>, потолки <see cref="PowerMax"/> и <see cref="InitiativeMax"/>
/// (других мест у них нет). Броню тоже режет цепочка, на каждом слое — <see cref="ArmorMax"/>
/// (<c>CUmUnit::getArmor</c>, ранг 1, задача 29 Ф7); слой защиты города и ветку защиты режет
/// <c>Formulas.Armor</c> той же ячейкой. Урон — <c>Formulas.DamageMax</c> плюс
/// <c>Formulas.DamageWithBuffs</c>. Второго потолка урона здесь нет и не будет:
/// <c>RulesConstants.MaxDamage</c> = 300 — это и есть <c>attackDamage.max</c> той же таблицы
/// ограничений (<c>.rdata</c> 0x6e9718 = {1, 300}), одно число, а не два похожих.</para>
/// <para><b>Пустая цепочка возвращает базу.</b> Ф4 и Ф5 модификаторов не знают и продолжают
/// работать бит в бит.</para>
/// </summary>
public sealed record EffectiveStats
{
    public required int HitPoints { get; init; }
    public required int Armor { get; init; }
    public int Regeneration { get; init; }
    public int MoveAllowance { get; init; }
    public int ScoutingRange { get; init; }
    public int Leadership { get; init; }
    /// <summary><c>CUmStackData.fastRetreat</c>: в ванили один модификатор, «Escape».</summary>
    public bool FastRetreat { get; init; }
    /// <summary>Сумма процентов <see cref="ModifierElementType.LowerCost"/> («Lute of Charming»).</summary>
    public int LowerCostPercent { get; init; }
    /// <summary>Id из <c>Lground.dbf</c>, добавленные цепочкой (3 — вода).</summary>
    public IReadOnlyList<int> MoveAbilities { get; init; } = [];
    /// <summary>Id из <c>LleadA.dbf</c>, добавленные цепочкой.</summary>
    public IReadOnlyList<int> LeaderAbilities { get; init; } = [];
    public IReadOnlyList<ImmunityGrant> Immunities { get; init; } = [];
    /// <summary>Иммунитеты к <b>классу</b> атаки (<c>LattC</c>), выданные цепочкой — <see cref="ModifierElementType.Immunityclass"/>
    /// (у <c>CUmUnit</c> поля <c>+0x2c</c>/<c>+0x34</c>, колонки <c>IMMUNITYC</c>/<c>IMMUNECATC</c>; в ванили 0 строк, только моды).</summary>
    public IReadOnlyList<ImmunityGrant> ClassImmunities { get; init; } = [];

    /// <summary>Статы юнита и отряда (<c>CUmUnitData</c> + <c>CUmStackData</c>). Элементы атаки
    /// (<c>CUmAttackData</c>) пропускаются — их берёт <see cref="OfAttack"/>, ровно как в движке,
    /// где <c>CUmUnit</c> реализует только свои геттеры.
    /// <para><c>REGEN</c>, <c>MOVE</c>, <c>SCOUT</c>, <c>LEADERSHIP</c> из <c>Gunits</c> в
    /// <see cref="UnitType"/> ещё не переехали (их читает <c>LastEdict.Data.RulesLoader.LoadExtras</c>,
    /// переселение — задача 11), поэтому приходят параметрами. База 0 означает «чистая прибавка
    /// цепочки».</para></summary>
    public static EffectiveStats Of(UnitType type, IReadOnlyList<Modifier> chain,
                                    int regeneration = 0, int moveAllowance = 0,
                                    int scoutingRange = 0, int leadership = 0)
    {
        int hp = type.HitPoints, armor = type.Armor, regen = regeneration;
        var (move, scout, lead) = OfStack(chain, moveAllowance, scoutingRange, leadership);
        int lowerCost = 0;
        bool fastRetreat = false;
        List<int> moveAbilities = [], leaderAbilities = [];
        List<ImmunityGrant> immunities = [], classImmunities = [];

        foreach (var e in Elements(chain))
            switch (e.Type)
            {
                case ModifierElementType.Hp: hp = ApplyHp(hp, e); break;
                // Ранг 1: CUmUnit::getArmor сводит к unitArmor на каждом слое (ArmorMax).
                case ModifierElementType.Armor:
                    armor = Math.Clamp(armor + Need(e.Number, e), ArmorMin, ArmorMax); break;
                // Ранг 3. Слагаемое в процентных ПУНКТАХ, а не процент от текущей регенерации:
                // сам стат уже измеряется в процентах, и при чтении «процент от процента» подпись
                // x000tg6035 «Add 10% regeneration» означала бы +1 к регенерации 10 — так подпись
                // не пишут. Снимает: дифф регенерации в журнале задачи 13 после «Leader Upgrade:
                // Natural Healing» на юните с ненулевым REGEN (10 + 10 = 20 против 11).
                case ModifierElementType.Regeneration: regen += Need(e.Percent, e); break;
                // MOVE, SCOUT и LEADERSHIP считает OfStack: их спрашивают и без юнита — запас хода
                // отряда и радиус обзора на карте (задача 11 Ф7). Дом арифметики один, проходов по
                // цепочке два: ни с одним другим элементом эти три стата не взаимодействуют.
                case ModifierElementType.MoveAllowance or ModifierElementType.ScoutingRange
                    or ModifierElementType.Leadership: break;
                case ModifierElementType.LowerCost: lowerCost += Need(e.Percent, e); break;
                case ModifierElementType.FastRetreat: fastRetreat = true; break;
                case ModifierElementType.MoveAbility: moveAbilities.Add(Need(e.Move, e)); break;
                case ModifierElementType.LeaderAbility: leaderAbilities.Add(Need(e.Ability, e)); break;
                case ModifierElementType.Immunity:
                    // IMMUNITY — источник (LattS), IMMUNECAT — уровень (LImmune). Ранг 1, см.
                    // ImmunityGrant: у всех шести вани́льных вардов IMMUNECAT = 2 (Once).
                    immunities.Add(new ImmunityGrant(Need(e.Immunity, e),
                                                     (ImmunityLevel)Need(e.ImmuneCat, e))); break;
                // Класс атаки (LattC) и уровень (LImmune) — колонки IMMUNITYC/IMMUNECATC (ModifierLoader), разбор 0x5a3e61.
                case ModifierElementType.Immunityclass:
                    classImmunities.Add(new ImmunityGrant(Need(e.Immunity, e), (ImmunityLevel)Need(e.ImmuneCat, e))); break;
                case ModifierElementType.Power or ModifierElementType.QtyDamage
                    or ModifierElementType.Initiative or ModifierElementType.AttackDrain: break;
                default: throw Unsupported(e);
            }

        return new EffectiveStats
        {
            HitPoints = hp, Armor = armor, Regeneration = regen, MoveAllowance = move,
            ScoutingRange = scout, Leadership = lead, FastRetreat = fastRetreat,
            LowerCostPercent = lowerCost, MoveAbilities = moveAbilities,
            LeaderAbilities = leaderAbilities, Immunities = immunities, ClassImmunities = classImmunities,
        };
    }

    /// <summary>
    /// Статы <b>отряда</b> (<c>CUmStackData</c>), которые цепочка меняет без участия юнита: запас
    /// хода, радиус обзора и лидерство. <c>MOVE</c>, <c>SCOUT</c> и <c>LEADERSHIP</c> в
    /// <see cref="UnitType"/> не живут (их читает <c>LastEdict.Data.RulesLoader.LoadExtras</c>), поэтому
    /// база приходит параметрами, а не из типа — и потому же эту часть можно спросить там, где
    /// типа под рукой нет: <see cref="LastEdict.Rules.Map.Movement.StartMovement"/> и
    /// <see cref="LastEdict.Rules.Map.Fog.Radius"/> держат базу словарём по типу.
    ///
    /// <para><b>Ранг 1 — вид арифметики (таблица <c>GmodifL</c>):</b> запас хода задан в
    /// <c>PERCENT</c> (навыки Pathfinding +20 % и Advanced Pathfinding +25 %, П§20), обзор и
    /// лидерство — в <c>NUMBER</c> (Keen-Sight +3, Leadership +1). Процент — от того, что дал
    /// предыдущий слой (<see cref="Boost"/>), как и у остальных процентных статов;
    /// <b>ранг 3</b> — что два процента хода складываются именно так: лидер с обоими навыками
    /// (5-й уровень доступен) даёт 20 · 1,2 · 1,25 = 30 против 20 + 20 % + 25 % = 29 от базы.
    /// Живьём не мерилось (план Ф7 §1.5: ход и обзор — только по таблицам).</para>
    /// </summary>
    public static (int MoveAllowance, int ScoutingRange, int Leadership) OfStack(
        IReadOnlyList<Modifier> chain, int moveAllowance = 0, int scoutingRange = 0, int leadership = 0)
    {
        foreach (var e in Elements(chain))
            switch (e.Type)
            {
                case ModifierElementType.MoveAllowance:
                    moveAllowance = Boost(moveAllowance, Need(e.Percent, e)); break;
                case ModifierElementType.ScoutingRange: scoutingRange += Need(e.Number, e); break;
                case ModifierElementType.Leadership: leadership += Need(e.Number, e); break;
            }
        return (moveAllowance, scoutingRange, leadership);
    }

    /// <summary>Потолки и полы атаки — таблица ограничений движка <c>GameRestrictions</c>.
    /// Значения прочитаны прямо из <c>.rdata</c> <c>game-pristine/Discipl2.exe</c> (GOG, image base
    /// 0x400000), порядок полей структуры сверен по <c>reference/D2ModdingToolset/mss32/include/
    /// restrictions.h:38-49</c> — соседняя строка <c>0x6e88fc</c> это <c>stackScoutRange</c> {1, 8},
    /// её легко перепутать с точностью:
    /// <list type="bullet">
    /// <item><c>attackPower</c> <b>0x6e9710</b> = {0, <b>100</b>}</item>
    /// <item><c>attackInitiative</c> <b>0x6e9708</b> = {1, <b>150</b>}</item>
    /// <item><c>attackDamage</c> <b>0x6e9718</b> = {1, 300} — это и есть <c>RulesConstants.MaxDamage</c>,
    /// второй раз здесь не заводится (см. потолки в описании класса)</item>
    /// <item><c>unitArmor</c> <b>0x6e88f4</b> = {0, 90} — <see cref="ArmorMin"/>/<see cref="ArmorMax"/>
    /// и <c>RulesConstants.MaxArmor</c></item>
    /// </list>
    /// <para><b>Ранг 1</b> для точности: живой прогон 08.09.2026, журнал
    /// <c>game-dev/probe/20260908-211749-36276.jsonl</c>. На <c>S143UN0005</c> (King's Guard)
    /// надето «Зелье меткости» (<c>G000IG0009</c> → <c>g000um2009</c>, TYPE 3 Power PERCENT 30):
    /// <c>power.ret</c> база <b>80 → 100</b>, тогда как 80 + 30 % = <b>104</b>. Движок обрезал.
    /// Тот же прогон и то же зелье силы (<c>g000um2015</c>, TYPE 4 QtyDamage PERCENT 30) урон не
    /// режет: <c>dmg_buffs.ret</c> 60 → 78, ровно +30 % — потолок урона там ещё далеко.</para>
    /// <para><b>Ранг 2</b> для инициативы: живого случая, где она перевалила бы за 150, в журнале
    /// нет (в ванили максимум <c>INITIATIVE</c> = 90, и одного модификатора мало: 90 + 60 % = 144).
    /// Источник — <c>custommodifier.cpp:1248</c>, где тулсет повторяет <c>CUmAttack::getInitiative</c>
    /// тем же <c>std::clamp</c> по той же строке таблицы. Снимает бой с двумя бафами инициативы на
    /// одном юните (<c>g000um2019</c> +60 % и <c>g000um4022</c> +50 % на базе 90 дают 216 → 150).</para>
    /// <para><b>Где именно режется — ранг 3.</b> Здесь потолок стоит на каждом слое цепочки, как в
    /// <c>custommodifier.cpp</c> (<c>attackGetPower</c> :1272, <c>attackGetInitiative</c> :1248 —
    /// каждый слой отдаёт наружу уже сведённое значение). Альтернатива «свести один раз в конце»
    /// различима только на паре слоёв разного знака: точность 100, «Hypnosis» −33 % поверх зелья
    /// +30 % даёт 67 по слоям (100 → 100 → 67) против 87 в конце (100 → 130 → 87). Снимает бой на
    /// юните, у которого одновременно висят баф и дебаф одного стата.</para></summary>
    private const int PowerMin = 0, PowerMax = 100;

    /// <inheritdoc cref="PowerMin"/>
    private const int InitiativeMin = 1, InitiativeMax = 150;

    /// <summary><c>unitArmor</c> <b>0x6e88f4</b> = {0, <b>90</b>} — границы, в которые
    /// <c>CUmUnit::getArmor</c> (GOG <c>0x5a465a</c>, слот 10 vftable <c>IUsSoldier</c> у CUmUnit
    /// <c>0x6ead9c</c>) сводит броню на КАЖДОМ слое: <c>clamp(prev-&gt;getArmor + data.armor, min, max)</c>,
    /// <c>0x5a468e</c>–<c>0x5a46b0</c>. <b>Ранг 1</b>, задача 29 Ф7: страж столицы (броня 50 и слой защиты
    /// города 50) в корпусе даёт 90, а не 100. Та же ячейка читается веткой защиты <c>computeArmor</c>
    /// (<c>0x5d2d6f</c>), поэтому <c>RulesConstants.MaxArmor</c> берёт значение отсюда.
    /// <para>Сводится каждый элемент, а не каждый модификатор: движок прибавляет <c>data.armor</c>
    /// модификатора разом, но двух элементов брони в одном модификаторе ванили нет. Отрицательные
    /// слои есть (Pain/Rust −50, Mold −80, «−10 Armor»): 30 → 0 под Mold и дальше вверх от нуля.</para></summary>
    public const int ArmorMin = 0, ArmorMax = 90;

    /// <summary>Статы атаки (<c>CUmAttackData</c>: qtyDamage, power, initiative, attackDrain).
    /// Элементы юнита и отряда пропускаются — их берёт <see cref="Of"/>.
    /// <para>Точность и инициатива сводятся к границам движка (<see cref="PowerMin"/>,
    /// <see cref="InitiativeMin"/>) на каждом слое; урон не сводится — его потолок зависит от
    /// юнита (<c>Formulas.DamageMax</c>: 300 либо 400 у десяти прошитых лидеров), юнита здесь нет,
    /// и режет он там же, где считается сам урон.</para>
    /// <para>При пустой цепочке возвращается та же самая ссылка на <paramref name="attack"/>.
    /// Сводить там нечего: по всем 354 строкам <c>Gattacks.dbf</c> ванили <c>POWER</c> лежит в
    /// 30..100, <c>INITIATIVE</c> в 10..90 — база в границы уложена самими данными игры.</para></summary>
    public static EffectiveAttack OfAttack(AttackType attack, IReadOnlyList<Modifier> chain)
    {
        if (chain.Count == 0) return new EffectiveAttack(attack, 0);

        int power = attack.Power, damage = attack.QtyDamage, initiative = attack.Initiative, drain = 0;
        foreach (var e in Elements(chain))
            switch (e.Type)
            {
                case ModifierElementType.Power:
                    power = Math.Clamp(Boost(power, Need(e.Percent, e)), PowerMin, PowerMax); break;
                case ModifierElementType.Initiative:
                    initiative = Math.Clamp(Boost(initiative, Need(e.Percent, e)),
                                            InitiativeMin, InitiativeMax); break;
                // Ранг 3. Накопление процентных ПУНКТОВ поверх базы drainHealPercent (50,
                // L/settings.lua:63-64), а не процент от неё: сигнатура тулсета
                // getAttackDrain(unit, damage, prev) величину не раскрывает, а «Unholy Dagger»
                // (g000um2317, PERCENT 25) при чтении «процент от 50» дал бы +12, что от прибавки
                // 25 неотличимо только на глаз. Снимает: бой задачи 13 с надетым кинжалом —
                // вылеченное 75 % от разницы HP против 62 %.
                case ModifierElementType.AttackDrain: drain += Need(e.Percent, e); break;
                case ModifierElementType.QtyDamage:
                    if (!DamageIsFixed(attack.Class)) damage = Boost(damage, Need(e.Percent, e));
                    break;
                case ModifierElementType.Hp or ModifierElementType.Armor
                    or ModifierElementType.Regeneration or ModifierElementType.MoveAllowance
                    or ModifierElementType.ScoutingRange or ModifierElementType.Leadership
                    or ModifierElementType.LowerCost or ModifierElementType.FastRetreat
                    or ModifierElementType.MoveAbility or ModifierElementType.LeaderAbility
                    or ModifierElementType.Immunity or ModifierElementType.Immunityclass: break;
                default: throw Unsupported(e);
            }

        return new EffectiveAttack(
            attack with { Power = power, QtyDamage = damage, Initiative = initiative }, drain);
    }

    /// <summary>Урон Poison/Frostbite/Blister модификаторами не меняется — это решение движка,
    /// а не наше упрощение. Ранг 2 (A, <c>Scripts/Modifiers/template.lua</c>, <c>getAttackDamage</c>:
    /// «Poison/Frostbite/Blister attack damage cannot be altered by modifiers. This is by the game
    /// design»); снимает бой из задачи 13 с бафом урона на носителе яда.</summary>
    private static bool DamageIsFixed(AttackClass cls) =>
        cls is AttackClass.Poison or AttackClass.Frostbite or AttackClass.Blister;

    /// <summary>Единственная строка <c>GmodifL</c>, где заполнены обе числовые колонки — и это не
    /// два эффекта, а величина плюс её вид. <c>CUmUnitData</c> (<c>umunit.h:45-57</c>) держит одно
    /// <c>ModifierValue hp</c> и один <c>bool isPercentHp</c>, то есть места под два числа там нет.
    /// Величина лежит в <c>PERCENT</c>, вид — в <c>NUMBER</c>: 1 процент, 2 число.
    /// <para>Ранг 2, но подпёрт данными самой игры — текстами описаний из <c>Tglobal.dbf</c>, теми
    /// самыми, что игрок видит в подсказке. Все пять строк <c>TYPE 6</c> ванили:
    /// <c>g000um9001</c> 20/1 → «Add 20% Hit Points», <c>g000um9002</c> −20/1 → «Subtract 20% Hit
    /// Points», <c>g000um7545</c> 50/<b>2</b> → «+50 to Hit Point Max» (<c>Gmodif.COMMENTS</c>:
    /// «Gallean's Boon (+50 hp max)»), <c>g000um2007</c> 15/1 («Potion boost Hit points permanent»),
    /// <c>g000um4017</c> 20/1 («Leader Upgrade: Toughness»). Единственная строка с <c>NUMBER = 2</c>
    /// — единственная, чей текст обещает не процент. Версия плана «процент или число, не оба»
    /// данными опровергнута: <c>PERCENT</c> заполнен у всех пяти.</para>
    /// <para>Снимает: хук на <c>CUmUnitApi::Api.readData</c> либо дифф <c>units[]</c> в журнале при
    /// надевании «Gallean's Boon» на юнита с hp max, не кратным 100 (задача 13) — процент и число
    /// там расходятся.</para></summary>
    private const int HpValueIsNumber = 2;

    private static int ApplyHp(int hp, ModifierElement e)
    {
        int value = Need(e.Percent, e);
        return Need(e.Number, e) == HpValueIsNumber ? hp + value : Boost(hp, value);
    }

    private static IEnumerable<ModifierElement> Elements(IReadOnlyList<Modifier> chain) =>
        chain.SelectMany(m => m.Elements);

    /// <summary>Процент от того, что дал предыдущий слой. Целочисленно с усечением к нулю — как
    /// <c>Percent::of</c> движка (GOG <c>0x5838c0</c>: <c>imul; cdq; idiv 100</c>), тот же приём уже
    /// использует <c>Formulas.ComputeDamage</c>.</summary>
    private static int Boost(int value, int percent) => value + value * percent / 100;

    /// <summary>«Не задано» — не ноль (<c>ModifierValue.initialized</c>). Молча подставить 0
    /// нельзя: у двух строк ванили ноль задан по-настоящему.</summary>
    private static int Need(int? value, ModifierElement e,
                            [CallerArgumentExpression(nameof(value))] string field = "")
        => value ?? throw new InvalidDataException(
               $"модификатор {e.Type}: поле {field.Replace("e.", "")} не задано, а тип его требует");

    // Остаётся только QtyTarget (TYPE 0): его не читает ни один класс модификатора движка, и загрузчик таблиц отказывает раньше
    // (ModifierLoader, 0x5a918e) — сюда он доходит лишь собранным в коде.
    private static NotSupportedException Unsupported(ModifierElement e) => new(
        $"элемент модификатора {e.Type} ({(int)e.Type}) движок не читает ни в одном классе модификатора " +
        "(отказ загрузки «Invalid type», 0x5a918e)");
}
