using D2.Data;
using D2.Rules.Battle;
using D2.Rules.Rng;
using D2.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Задача 2 фазы Ф6: цепочка модификаторов и эффективные статы. Числа берутся из живой ванили —
/// <c>Gmodif.dbf</c> / <c>GmodifL.dbf</c> через <c>D2.Data.Battle.ModifierLoader</c>, синтетических
/// модификаторов здесь нет. Игра не запускается.
/// </summary>
public class EffectiveStatsTests(ITestOutputHelper output)
{
    // ---- вход: настоящие модификаторы ванили -------------------------------------------------

    private static readonly Lazy<D2.Data.Battle.ModifierTables> Tables =
        new(() => D2.Data.Battle.ModifierLoader.Load(ImportedData.Content));

    /// <summary>Цепочка из вани́льных модификаторов по их id, в порядке наложения. Пересборки
    /// записей здесь больше нет: задача 3 свела <c>Modifier</c>/<c>ModifierElement</c> в одно
    /// определение (<c>D2.Rules.Units</c>), и загрузчик отдаёт сразу его, с разобранным
    /// <c>Type</c>.</summary>
    private static IReadOnlyList<Modifier> Chain(params string[] ids) =>
        ids.Select(id => Tables.Value.Modifiers[id]).ToList();

    private static UnitType Unit(int hp, int armor) =>
        new("g000uu5130", 1, hp, armor, "g000aa0001", "g000000000", false, true, 10, 100);

    private static AttackType Attack(int power = 80, int damage = 25, int initiative = 50,
                                     AttackClass cls = AttackClass.Damage) =>
        new("g000aa0001", initiative, cls, power, 3, damage, 0, false, 0);

    // ---- свойство, на котором держатся Ф4 и Ф5 -----------------------------------------------

    /// <summary>Пустая цепочка возвращает базу побитово — иначе Ф4/Ф5 поехали бы молча.
    /// Юнит и атака берутся настоящие, из <c>Gunits</c>/<c>Gattacks</c>.</summary>
    [SkippableFact]
    public void EmptyChain_ReturnsBaseVerbatim()
    {
        ImportedData.RequireRules();
        var rules = D2.Data.Battle.RulesLoader.Load(ImportedData.Content);
        int checked_ = 0;
        foreach (var type in rules.Units.Values)
        {
            var s = EffectiveStats.Of(type, []);
            Assert.Equal(type.HitPoints, s.HitPoints);
            Assert.Equal(type.Armor, s.Armor);
            Assert.Equal((0, 0, 0, 0, 0, false), (s.Regeneration, s.MoveAllowance, s.ScoutingRange,
                                                  s.Leadership, s.LowerCostPercent, s.FastRetreat));
            Assert.Empty(s.MoveAbilities);
            Assert.Empty(s.LeaderAbilities);
            Assert.Empty(s.Immunities);
            checked_++;
        }
        foreach (var a in rules.Attacks.Values)
        {
            var e = EffectiveStats.OfAttack(a, []);
            Assert.Same(a, e.Attack);           // та же ссылка, а не равная копия
            Assert.Equal(0, e.DrainPercent);
            checked_++;
        }
        output.WriteLine($"пустая цепочка: проверено {checked_} базовых типов");
        Assert.True(checked_ > 600, "проверено слишком мало типов — данные не прочитались");
    }

    /// <summary>База, отличная от <see cref="UnitType"/> (<c>REGEN</c>/<c>MOVE</c>/<c>SCOUT</c>/
    /// <c>LEADERSHIP</c> из <c>Gunits</c>), проходит насквозь при пустой цепочке.</summary>
    [Fact]
    public void EmptyChain_PassesThroughStatsOutsideUnitType()
    {
        var s = EffectiveStats.Of(Unit(65, 0), [], regeneration: 15, moveAllowance: 30,
                                  scoutingRange: 4, leadership: 2);
        Assert.Equal((15, 30, 4, 2), (s.Regeneration, s.MoveAllowance, s.ScoutingRange, s.Leadership));
    }

    // ---- по одному вани́льному модификатору на тип элемента ------------------------------------

    /// <summary>Пять модификаторов ванили, каждый со своим конкретным числом. Ожидания сверены с
    /// описаниями самой игры из <c>Tglobal.dbf</c>: «+ 10% Initiative» (x000tgc002), «+ 20 Armor»
    /// (x000tgc012), «+ 33% Power» (x000tgc036), «Enhances Sailing ability» (x000tgc027,
    /// <c>Lground</c> 3 = вода), «Wards against Air attacks» (x000tgc001, <c>LattS</c> 7 = Air,
    /// <c>LImmune</c> 2 = Once).</summary>
    [SkippableFact]
    public void VanillaModifiers_GiveExactNumbers()
    {
        ImportedData.RequireRules();

        Assert.Equal(55, EffectiveStats.OfAttack(Attack(initiative: 50), Chain("g000um0002")).Attack.Initiative);
        // база 60, а не обычная 80: 80 + 33 % = 106 упёрлось бы в потолок точности 100 и
        // перестало бы показывать само число модификатора — потолок проверяет AttackCaps_*
        Assert.Equal(79, EffectiveStats.OfAttack(Attack(power: 60), Chain("g000um0036")).Attack.Power);
        Assert.Equal(40, EffectiveStats.Of(Unit(65, 20), Chain("g000um0012")).Armor);
        Assert.Equal([3], EffectiveStats.Of(Unit(65, 0), Chain("g000um0027")).MoveAbilities);
        Assert.Equal([new ImmunityGrant(7, ImmunityLevel.Once)],
                     EffectiveStats.Of(Unit(65, 0), Chain("g000um0001")).Immunities);
    }

    /// <summary>Процент считается целочисленно с усечением к нулю — как <c>Percent::of</c> движка
    /// (GOG 0x5838c0: imul; cdq; idiv 100). 50 · 33 % = 16,5 → 16, то есть 66, а не 67.
    /// <para>Прежняя база 99 (131 против 132) больше не годится: 131 упирается в потолок точности
    /// 100 и усечения не показывает. Дробная часть здесь ровно 0,5 — то есть случай, на котором
    /// усечение и округление расходятся сильнее всего.</para></summary>
    [SkippableFact]
    public void Percent_TruncatesTowardsZero()
    {
        ImportedData.RequireRules();
        Assert.Equal(66, EffectiveStats.OfAttack(Attack(power: 50), Chain("g000um0036")).Attack.Power);
        Assert.NotEqual(67, EffectiveStats.OfAttack(Attack(power: 50), Chain("g000um0036")).Attack.Power);
        Assert.Equal(52, EffectiveStats.Of(Unit(65, 0), Chain("g000um9002")).HitPoints);   // −20 %
    }

    /// <summary>Долг ревью задачи 2: отрицательный процент проверялся только там, где деление
    /// ровное (65 · (−20) / 100 = −13), и потому усечение к нулю было неотличимо от усечения
    /// к −∞. Дробный случай на вани́льном модификаторе: <c>g000um0055</c> «Hypnosis (D55)»,
    /// <c>TYPE 3</c> Power <c>PERCENT</c> −33, база 80 — самая частая сила атаки ванили.
    /// <para>80 · (−33) / 100 = −26,4. Семантика C# <c>/</c> (усечение к нулю, как <c>idiv</c>
    /// движка — <c>Percent::of</c>, GOG <c>0x5838c0</c>) даёт −26, то есть <b>54</b>; floor дал бы
    /// −27, то есть 53. Утверждается и то и другое, чтобы разница не была словом в отчёте.</para>
    /// <para>Ранг 1 по арифметике (дизассемблер), ранг 3 по тому, что <c>Percent::of</c> зовётся
    /// и для модификаторов: снимает журнал задачи 13 на юните с Hypnosis.</para></summary>
    [SkippableFact]
    public void NegativePercent_TruncatesTowardsZero_NotFloor()
    {
        ImportedData.RequireRules();
        var hypnosis = Chain("g000um0055");
        Assert.Equal(54, EffectiveStats.OfAttack(Attack(power: 80), hypnosis).Attack.Power);
        Assert.NotEqual(53, EffectiveStats.OfAttack(Attack(power: 80), hypnosis).Attack.Power);
        // тот же −33 % на другой некруглой базе: 65 · (−33) / 100 = −21,45 → −21, floor дал бы −22
        Assert.Equal(44, EffectiveStats.OfAttack(Attack(power: 65), hypnosis).Attack.Power);
        output.WriteLine("g000um0055 Power −33 %: 80 → 54 (floor дал бы 53), 65 → 44 (floor 43)");
    }

    // ---- открытый вопрос задачи 1: TYPE 6 с двумя заполненными колонками ----------------------

    /// <summary>
    /// <c>NUMBER</c> при <c>TYPE 6</c> — не второе слагаемое, а вид величины: 1 процент, 2 число.
    /// Все пять строк ванили и их тексты из <c>Tglobal.dbf</c>:
    /// <c>g000um9001</c> 20/1 «Add 20% Hit Points», <c>g000um9002</c> −20/1 «Subtract 20% Hit
    /// Points», <c>g000um2007</c> 15/1, <c>g000um4017</c> 20/1, <c>g000um7545</c> 50/<b>2</b>
    /// «+50 to Hit Point Max». Единственная строка с двойкой — единственная, чей текст обещает не
    /// процент. Ранг 2 (структура <c>CUmUnitData</c>: одно <c>hp</c> + <c>isPercentHp</c>), подпёрт
    /// данными игры; снимает журнал задачи 13.
    /// </summary>
    [SkippableFact]
    public void Hp_NumberColumnSelectsPercentOrAbsolute()
    {
        ImportedData.RequireRules();

        // 1 — процент: 100 → 120, и на некруглой базе 65 → 78
        Assert.Equal(120, EffectiveStats.Of(Unit(100, 0), Chain("g000um9001")).HitPoints);
        Assert.Equal(78, EffectiveStats.Of(Unit(65, 0), Chain("g000um9001")).HitPoints);
        // 2 — число: +50 к максимуму независимо от базы
        Assert.Equal(150, EffectiveStats.Of(Unit(100, 0), Chain("g000um7545")).HitPoints);
        Assert.Equal(115, EffectiveStats.Of(Unit(65, 0), Chain("g000um7545")).HitPoints);
        // если бы применялись оба поля («процент, затем число»), вышло бы 122 и 152
        Assert.NotEqual(122, EffectiveStats.Of(Unit(100, 0), Chain("g000um9001")).HitPoints);

        var six = Tables.Value.Modifiers.Values.SelectMany(m => m.Elements)
            .Where(e => e.Type == ModifierElementType.Hp).ToList();
        Assert.Equal(5, six.Count);
        Assert.All(six, e => Assert.NotNull(e.Percent));
        Assert.All(six, e => Assert.Contains(e.Number, new int?[] { 1, 2 }));
        output.WriteLine("TYPE 6: " + string.Join(", ", six.Select(e => $"{e.Percent}/{e.Number}")));
    }

    // ---- цепочка -----------------------------------------------------------------------------

    /// <summary>Свёртка в порядке наложения: каждый слой видит результат предыдущего.
    /// <c>g000um0002</c> (+10 % инициативы) и <c>g000um2307</c> (+40 % урона и +50 % инициативы,
    /// один из двух двухэлементных модификаторов ванили): 50 → 55 → 82.
    /// Альтернатива «все проценты от базы» дала бы 80 — это и есть различающий случай, ранг 3.</summary>
    [SkippableFact]
    public void Chain_EachLayerSeesPrevious()
    {
        ImportedData.RequireRules();
        var e = EffectiveStats.OfAttack(Attack(damage: 25, initiative: 50), Chain("g000um0002", "g000um2307"));
        Assert.Equal(82, e.Attack.Initiative);       // 55 + 55·50/100 = 55 + 27
        Assert.NotEqual(80, e.Attack.Initiative);    // 50 + 5 + 25 — «проценты от базы»
        Assert.Equal(35, e.Attack.QtyDamage);        // 25 + 25·40/100
    }

    /// <summary>Элементы атаки и элементы юнита не путаются: <see cref="EffectiveStats.Of"/>
    /// пропускает первые, <see cref="EffectiveStats.OfAttack"/> — вторые, как <c>CUmUnit</c> и
    /// <c>CUmAttack</c> в движке.</summary>
    [SkippableFact]
    public void UnitAndAttackElements_DoNotLeakIntoEachOther()
    {
        ImportedData.RequireRules();
        var chain = Chain("g000um0012", "g000um2307");           // Armor +20, QtyDamage/Initiative %
        var stats = EffectiveStats.Of(Unit(65, 20), chain);
        var attack = EffectiveStats.OfAttack(Attack(damage: 25, initiative: 50), chain);
        Assert.Equal(40, stats.Armor);
        Assert.Equal(65, stats.HitPoints);
        Assert.Equal(35, attack.Attack.QtyDamage);
        Assert.Equal(75, attack.Attack.Initiative);
    }

    /// <summary>Урон Poison/Frostbite/Blister модификаторами не меняется (ранг 2, template.lua
    /// getAttackDamage), а инициатива той же атаки — меняется.</summary>
    [SkippableFact]
    public void DotDamage_IsNotChangedByModifiers()
    {
        ImportedData.RequireRules();
        var e = EffectiveStats.OfAttack(Attack(damage: 25, initiative: 50, cls: AttackClass.Poison),
                                        Chain("g000um2307"));
        Assert.Equal(25, e.Attack.QtyDamage);
        Assert.Equal(75, e.Attack.Initiative);
    }

    /// <summary>Вампиризм копится процентными пунктами: <c>g000um2317</c> «Unholy Dagger»
    /// («Drain Life», PERCENT 25). База <c>drainHealPercent</c> = 50 сюда не входит — её прибавляет
    /// задача 6. Ранг 3: что 25 — пункты, а не процент от базы, снимает бой с надетым кинжалом.</summary>
    [SkippableFact]
    public void AttackDrain_AccumulatesPercentPoints()
    {
        ImportedData.RequireRules();
        Assert.Equal(25, EffectiveStats.OfAttack(Attack(), Chain("g000um2317")).DrainPercent);
    }

    /// <summary>Отряд и карта: <c>g000um4015</c> «Keen-Sight» (NUMBER 3), <c>g000um9031</c>
    /// (NUMBER 1, лидерство), <c>g000um2503</c> «Boots 3» (PERCENT 20), <c>g000um7546</c> «Escape»,
    /// <c>g000um7548</c> «Lute of Charming» (PERCENT 10), <c>g000um9035</c> (PERCENT 10, регенерация).
    /// <para><b>COMMENTS в <c>Gmodif</c> врут, и ожидания взяты из <c>GmodifL</c>.</b> У сапог
    /// подписи «+25% / +50% / +100% Move», а в данных 20 / 40 / 60 (<c>g000um2503</c>, <c>2504</c>,
    /// <c>2505</c>). Тест утверждает 24 (20 + 20 %), а не 25.</para></summary>
    [SkippableFact]
    public void StackAndUnitStats_ComeFromChain()
    {
        ImportedData.RequireRules();
        var s = EffectiveStats.Of(Unit(65, 0),
            Chain("g000um4015", "g000um9031", "g000um2503", "g000um7546", "g000um7548", "g000um9035"),
            regeneration: 10, moveAllowance: 20, scoutingRange: 4, leadership: 1);
        Assert.Equal(7, s.ScoutingRange);        // 4 + 3
        Assert.Equal(2, s.Leadership);           // 1 + 1
        Assert.Equal(24, s.MoveAllowance);       // 20 + 20 %, а не подписанные COMMENTS 25
        Assert.True(s.FastRetreat);
        Assert.Equal(10, s.LowerCostPercent);
        Assert.Equal(20, s.Regeneration);        // 10 + 10 пунктов, ранг 3
    }

    // ---- потолки движка: точность и инициатива ------------------------------------------------

    /// <summary>Наблюдение живого прогона 08.09.2026 (журнал
    /// <c>game-dev/probe/20260908-211749-36276.jsonl</c>, прогон предметов). На King's Guard
    /// <c>S143UN0005</c> применены два зелья, и журнал развёл потолок и его отсутствие:
    /// <list type="bullet">
    /// <item>«Зелье меткости» <c>G000IG0009</c> → <c>g000um2009</c> (TYPE 3 Power PERCENT 30):
    /// <c>power.ret</c> <b>80 → 100</b>, а не 104. Потолок <c>attackPower.max</c>
    /// (<c>.rdata</c> 0x6e9710 = {0, 100});</item>
    /// <item>«Зелье силы» <c>G000IG0015</c> → <c>g000um2015</c> (TYPE 4 QtyDamage PERCENT 30):
    /// <c>dmg_buffs.ret</c> <b>60 → 78</b>, ровно +30 %. До потолка урона (300) отсюда далеко, и
    /// второго потолка урона в цепочке нет — он живёт в <c>Formulas.DamageMax</c>.</item>
    /// </list>
    /// Ранг 1: числа из журнала, границы прочитаны из <c>.rdata</c> самой игры.</summary>
    [SkippableFact]
    public void PowerCeiling_MatchesProbe_80Plus30Percent_Gives100()
    {
        ImportedData.RequireRules();
        var accuracy = Chain("g000um2009");     // «Зелье меткости», Power +30 %
        var strength = Chain("g000um2015");     // «Зелье силы», QtyDamage +30 %

        Assert.Equal(100, EffectiveStats.OfAttack(Attack(power: 80), accuracy).Attack.Power);
        Assert.NotEqual(104, EffectiveStats.OfAttack(Attack(power: 80), accuracy).Attack.Power);
        Assert.Equal(78, EffectiveStats.OfAttack(Attack(damage: 60), strength).Attack.QtyDamage);

        // тот же модификатор ниже потолка считается без обрезания: 60 + 30 % = 78
        Assert.Equal(78, EffectiveStats.OfAttack(Attack(power: 60), accuracy).Attack.Power);
        output.WriteLine("journal 20260908-211749: power 80 -> 100 (не 104), dmg_buffs 60 -> 78");
    }

    /// <summary>Инициатива сводится к <c>attackInitiative</c> (<c>.rdata</c> 0x6e9708 = {1, 150}).
    /// Одного вани́льного модификатора мало (максимум <c>INITIATIVE</c> 90, максимум прибавки 60 %
    /// — <c>g000um2019</c> — даёт 144), поэтому берутся два: <c>g000um2019</c> +60 % и
    /// <c>g000um4022</c> +50 %. 90 → 144 → 216, потолок отдаёт 150.</summary>
    [SkippableFact]
    public void InitiativeCeiling_ClampsAt150()
    {
        ImportedData.RequireRules();
        var two = Chain("g000um2019", "g000um4022");
        Assert.Equal(144, EffectiveStats.OfAttack(Attack(initiative: 90), Chain("g000um2019")).Attack.Initiative);
        Assert.Equal(150, EffectiveStats.OfAttack(Attack(initiative: 90), two).Attack.Initiative);
        Assert.NotEqual(216, EffectiveStats.OfAttack(Attack(initiative: 90), two).Attack.Initiative);
    }

    /// <summary>Нижние границы: <c>attackPower.min</c> = 0 и <c>attackInitiative.min</c> = 1.
    /// <para>Вани́льными данными они недостижимы, и это не догадка, а свойство арифметики: процент
    /// усекается к нулю, поэтому <c>Boost(v, −33)</c> при v = 3 уже ничего не меняет, и сколько
    /// вани́льных дебафов ни складывай, ниже 3 стат не опустится (сильнейшие в ванили — Power
    /// −33 % <c>g000um0055</c> и Initiative −33 % <c>g000um0075</c>). Поэтому пол проверяется
    /// синтетическим элементом — так же, как это уже делают тесты на порчу данных ниже. Граница
    /// настоящая: она стоит в таблице движка, и мод её достанет.</para></summary>
    [Fact]
    public void Floors_MatchEngineMinimums()
    {
        // -100 %: инициатива уходит в 0, а движок держит её не ниже 1
        Assert.Equal(1, EffectiveStats.OfAttack(Attack(initiative: 90),
            Synthetic(ModifierElementType.Initiative, -100)).Attack.Initiative);
        // -200 %: точность ушла бы в минус, а движок держит её не ниже 0
        Assert.Equal(0, EffectiveStats.OfAttack(Attack(power: 80),
            Synthetic(ModifierElementType.Power, -200)).Attack.Power);
    }

    /// <summary>Модификатор, которого в ванили нет, — только для проверки границ таблицы движка.</summary>
    private static IReadOnlyList<Modifier> Synthetic(ModifierElementType type, int percent) =>
        [new Modifier("synthetic", [new ModifierElement(type, Percent: percent)])];

    // ---- потолки живут в Formulas и здесь не дублируются --------------------------------------

    /// <summary>Броню режет каждый слой цепочки — <c>CUmUnit::getArmor</c> (GOG <c>0x5a465a</c>:
    /// <c>clamp(prev + data.armor, 0, 90)</c>), ранг 1, задача 29 Ф7. База 80 + 20 → 90, а не 100.
    /// Слой за слоем, а не один раз в конце: 30 под Mold (−80) → 0, затем +20 → 20; свод в конце дал бы
    /// max(30 − 80 + 20, 0) = 0.</summary>
    [SkippableFact]
    public void ArmorCeiling_CutOnEachModifierLayer()
    {
        ImportedData.RequireRules();
        var c = new RulesConstants(10, 6);
        Assert.Equal(20, EffectiveStats.Of(Unit(65, 30), Chain("g000um7543", "g000um0012")).Armor);
        var type = Unit(65, 80);
        int effective = EffectiveStats.Of(type, Chain("g000um0012")).Armor;
        Assert.Equal(EffectiveStats.ArmorMax, effective);

        var unit = new BattleUnit
        {
            Id = "S000UN0001", Type = type with { Armor = effective }, Attack = Attack(),
            Attacker = true, Slot = 0, HpMax = 65, Hp = 65, Defending = true,
        };
        Assert.Equal(c.MaxArmor, Formulas.Armor(unit, c));
    }

    /// <summary>Урон из цепочки уходит в <c>Formulas.DamageWithBuffs</c>, и потолок
    /// <c>DamageMax</c> режет там же. 250 + 40 % = 350 → 300.</summary>
    [SkippableFact]
    public void DamageCeiling_StaysInFormulas()
    {
        ImportedData.RequireRules();
        var c = new RulesConstants(10, 6);
        var boosted = EffectiveStats.OfAttack(Attack(damage: 250), Chain("g000um2307")).Attack;
        Assert.Equal(350, boosted.QtyDamage);
        int max = Formulas.DamageMax(c, "g000uu5130");
        Assert.Equal(c.MaxDamage, Formulas.DamageWithBuffs(boosted, max, 0, 0, false, new ScriptedRng([]), c));
    }

    // ---- ничего не проглатывается молча --------------------------------------------------------

    /// <summary>«Не задано» ≠ «ноль»: элемент брони без <c>NUMBER</c> — порча данных, а не ноль.</summary>
    [Fact]
    public void UnsetRequiredField_Throws()
    {
        var chain = new[] { new Modifier("test", [new ModifierElement(ModifierElementType.Armor)]) };
        var ex = Assert.Throws<InvalidDataException>(() => EffectiveStats.Of(Unit(65, 10), chain));
        Assert.Contains("Armor", ex.Message);
    }

    /// <summary><c>QtyTarget</c> (0) движок не читает ни в одном классе модификатора (загрузка отказывает «Invalid type», <c>0x5a918e</c>)
    /// — собранный в коде, он бросает, а не тихо пропускается.</summary>
    [Fact]
    public void QtyTarget_Throws()
    {
        var chain = new[] { new Modifier("test", [new ModifierElement(ModifierElementType.QtyTarget, Number: 1)]) };
        Assert.Throws<NotSupportedException>(() => EffectiveStats.Of(Unit(65, 10), chain));
        Assert.Throws<NotSupportedException>(() => EffectiveStats.OfAttack(Attack(), chain));
    }

    /// <summary><c>Immunityclass</c> (14) — иммунитет юнита к классу атаки (<c>CUmUnit</c>, <c>IMMUNITYC</c>/<c>IMMUNECATC</c>,
    /// <c>0x5a3e61</c>): в <see cref="EffectiveStats.ClassImmunities"/>, не в источниках; атаку не меняет. В бою — тот же уровень, что
    /// у врождённого из <c>GimmuC</c>: сильнейший (<see cref="Immunity.LevelsFor"/>).</summary>
    [Fact]
    public void Immunityclass_GrantsClassImmunity()
    {
        var chain = new[] { new Modifier("test", [new ModifierElement(ModifierElementType.Immunityclass, Immunity: (int)AttackClass.Poison,
                                                                      ImmuneCat: (int)ImmunityLevel.Once)]) };
        var stats = EffectiveStats.Of(Unit(65, 10), chain);
        Assert.Equal([new ImmunityGrant((int)AttackClass.Poison, ImmunityLevel.Once)], stats.ClassImmunities);
        Assert.Empty(stats.Immunities);
        var attack = Attack();
        Assert.Equal(attack, EffectiveStats.OfAttack(attack, chain).Attack);

        var target = new BattleUnit
        {
            Id = "t", Type = Unit(65, 10), Attack = attack, Attacker = false, Slot = 0, HpMax = 65, Hp = 65, Modifiers = chain,
        };
        Assert.Equal(ImmunityLevel.Once, Immunity.LevelsFor(target, source: 0, AttackClass.Poison).ByClass);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelsFor(target, source: 0, AttackClass.Damage).ByClass);
    }

    /// <summary>Каждый <c>TYPE</c>, встречающийся в ванили, свёртка знает: ни одна из 187 строк не
    /// доходит до ветки «не реализовано». Пустая проверка ничего не доказывает — счётчик печатается.</summary>
    [SkippableFact]
    public void EveryVanillaModifier_FoldsWithoutThrowing()
    {
        ImportedData.RequireRules();
        int elements = 0;
        foreach (var id in Tables.Value.Modifiers.Keys)
        {
            var chain = Chain(id);
            EffectiveStats.Of(Unit(100, 10), chain, 10, 20, 3, 1);
            EffectiveStats.OfAttack(Attack(), chain);
            elements += chain[0].Elements.Count;
        }
        output.WriteLine($"свёрнуто модификаторов {Tables.Value.Modifiers.Count}, элементов {elements}");
        Assert.Equal(178, Tables.Value.Modifiers.Count);
        Assert.Equal(180, elements);   // 187 строк GmodifL минус 7 без хозяина
    }
}
