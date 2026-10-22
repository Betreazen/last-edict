using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Tests;

/// <summary>
/// Экономический шаг ИИ (<see cref="AiStrategic"/>, <see cref="AiStrategicRules"/>) на правилах разбора
/// <c>docs/synthetic-maps/analysis/O-re65-strategic.md</c> (disciples-modding). Сверка с живым прогоном —
/// <see cref="AiStrategicCorpusTests"/>.
/// </summary>
public class AiStrategicTests
{
    private static readonly Bank Gold100 = new(100, 0, 0, 0, 0, 0);

    private static AiBudget Budget(long gold) =>
        new(AiStrategic.ListOrder.ToDictionary(t => t, _ => new Bank(gold, 0, 0, 0, 0, 0)), new Bank(gold, 0, 0, 0, 0, 0));

    private static AiEvaluation Eval(string id, AiStepType type, string actionId, Func<bool>? when = null) =>
        new(id, () => when is null || when() ? [new AiStepAction(type, actionId)] : []);

    // ── Выбор шага ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ListOrder_IsHealUpgradeHireLeaderCapitalGrowSpellDiplomacy()
    {
        Assert.Equal([5, 1, 2, 0, 4, 3, 6, 8], AiStrategic.ListOrder.Select(t => (int)t));
    }

    [Fact]
    public void Pass_TakesFirstActionOfFirstNonEmptyList_AndEvaluatesAllLists()
    {
        var evaluated = new List<AiStepType>();
        IEnumerable<AiEvaluation> Lists(AiStepType t, AiBudget _)
        {
            evaluated.Add(t);
            if (t is not (AiStepType.Capital or AiStepType.HireUnit)) return [];
            return [Eval("", t, t + "-a"), Eval("", t, t + "-b")];
        }

        var actions = AiStrategic.Pass(1, Lists, Budget(0), new Dictionary<(string, AiStepType), int>());

        Assert.Equal(AiStrategic.ListOrder, evaluated);
        Assert.Equal(["HireUnit-a", "HireUnit-b", "Capital-a", "Capital-b"], actions.Select(a => a.Id));
    }

    [Fact]
    public void Pass_SkipsEvaluationRememberedToday_ButNotEmptyIdOrYesterday()
    {
        var memo = new Dictionary<(string, AiStepType), int>
        {
            [("S1", AiStepType.UpgradeLeader)] = 5,
            [("", AiStepType.HireUnit)] = 5,
            [("V1", AiStepType.GrowCities)] = 4,
        };
        IEnumerable<AiEvaluation> Lists(AiStepType t, AiBudget _) => t switch
        {
            AiStepType.UpgradeLeader => [Eval("S1", t, "S1"), Eval("S2", t, "S2")],
            AiStepType.HireUnit => [Eval("", t, "C1")],
            AiStepType.GrowCities => [Eval("V1", t, "V1")],
            _ => [],
        };

        var actions = AiStrategic.Pass(5, Lists, Budget(0), memo);

        Assert.Equal(["S2", "C1", "V1"], actions.Select(a => a.Id));
    }

    [Fact]
    public void RunTurn_RebuildsAfterEachStep_RemembersSuccess_SpendsBudget_StopsWhenEmpty()
    {
        long gold = 1000;
        var grown = new HashSet<string>();
        IEnumerable<AiEvaluation> Lists(AiStepType t, AiBudget b) => t switch
        {
            AiStepType.GrowCities => new[] { "V1", "V2" }
                .Select(v => Eval(v, t, v, () => b.Allows(t, new Bank(300, 0, 0, 0, 0, 0)))),
            _ => [],
        };
        bool Execute(AiStepAction a)
        {
            grown.Add(a.Id);
            gold -= 300;
            return true;
        }

        var budget = Budget(1000).With(AiStepType.GrowCities, new Bank(500, 0, 0, 0, 0, 0));
        var turn = AiStrategic.RunTurn(3, Lists, Execute, () => new Bank(gold, 0, 0, 0, 0, 0), budget,
                                       new Dictionary<(string, AiStepType), int>());

        // V1 исполнен и запомнен; V2 уже не влезает в бюджет роста (500 − 300 = 200 < 300).
        Assert.Equal(["V1"], turn.Steps.Select(s => s.Action.Id));
        Assert.Equal(1, turn.Steps[0].Pass);
        Assert.Equal(200, turn.Budget.ByType[AiStepType.GrowCities].Gold);
        Assert.Equal(1000, turn.Budget.ByType[AiStepType.HireUnit].Gold);
        Assert.Equal(700, turn.Budget.TurnStart.Gold);
        Assert.Equal(3, turn.Memo[("V1", AiStepType.GrowCities)]);
    }

    [Fact]
    public void RunTurn_FailedExecution_LeavesNoMemoAndNoSpend_AndEndlessFailureThrows()
    {
        IEnumerable<AiEvaluation> Lists(AiStepType t, AiBudget _) =>
            t == AiStepType.HireLeader ? [Eval("C1", t, "C1")] : [];

        var ex = Assert.Throws<InvalidOperationException>(() => AiStrategic.RunTurn(
            1, Lists, _ => false, () => Gold100, Budget(100), new Dictionary<(string, AiStepType), int>(), maxPasses: 5));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void RunTurn_BudgetCheckRefusesWhenAnyCurrencyIsShort()
    {
        var budget = new AiBudget(AiStrategic.ListOrder.ToDictionary(t => t, _ => new Bank(500, 10, 0, 0, 0, 0)),
                                  new Bank(500, 10, 0, 0, 0, 0));
        Assert.True(budget.Allows(AiStepType.SpellResearch, new Bank(500, 10, 0, 0, 0, 0)));
        Assert.False(budget.Allows(AiStepType.SpellResearch, new Bank(0, 11, 0, 0, 0, 0)));
        // Трата — разница казны с начала хода (или с прошлой траты), и только своему типу.
        var spent = budget.Spend(AiStepType.SpellResearch, new Bank(450, 4, 0, 0, 0, 0));
        Assert.Equal(new Bank(450, 4, 0, 0, 0, 0), spent.ByType[AiStepType.SpellResearch]);
        Assert.Equal(new Bank(500, 10, 0, 0, 0, 0), spent.ByType[AiStepType.Capital]);
        Assert.Equal(new Bank(450, 4, 0, 0, 0, 0), spent.TurnStart);
    }

    /// <summary>Трата <c>0x4464ef</c> вычитает <c>Bank::subtract 0x586578</c> с обрезкой в [0, 9999]: казна, выросшая с прошлой
    /// траты (сделка принята), бюджет не прибавляет, и бюджет не уходит в минус. Карта 15, seq 3656201: начало 90, казна 390 → 418.</summary>
    [Fact]
    public void Spend_ClampsDifferenceAndBudgetAtZero()
    {
        var budget = new AiBudget(AiStrategic.ListOrder.ToDictionary(t => t, _ => new Bank(418, 117, 0, 0, 0, 0)),
                                  new Bank(90, 124, 0, 0, 0, 0));
        var grown = budget.Spend(AiStepType.HireLeader, new Bank(390, 124, 0, 0, 0, 0));
        Assert.Equal(new Bank(418, 117, 0, 0, 0, 0), grown.ByType[AiStepType.HireLeader]);
        Assert.Equal(new Bank(390, 124, 0, 0, 0, 0), grown.TurnStart);
        var over = grown.Spend(AiStepType.HireLeader, new Bank(0, 0, 0, 0, 0, 0));
        Assert.Equal(new Bank(28, 0, 0, 0, 0, 0), over.ByType[AiStepType.HireLeader]);
        // Резерв: начало + казна магии (0x586642) тоже обрезается — 9000 + 5000 → 9999, трата 9999 − 9000 = 999.
        var reserved = new AiBudget(AiStrategic.ListOrder.ToDictionary(t => t, _ => new Bank(5000, 0, 0, 0, 0, 0)), new Bank(9000, 0, 0, 0, 0, 0))
        {
            SpellStart = new Bank(5000, 0, 0, 0, 0, 0),
            Reserved = new HashSet<AiStepType> { AiStepType.SpellResearch },
        };
        Assert.Equal(new Bank(4001, 0, 0, 0, 0, 0), reserved.Spend(AiStepType.SpellResearch, new Bank(9000, 0, 0, 0, 0, 0)).ByType[AiStepType.SpellResearch]);
    }

    /// <summary>Дипломатия — список 8: общий цикл повторяет <see cref="AiDiplomacy.RunPasses"/> — оценка на каждом
    /// проходе, исполнение только без экономики, цель выпадает до конца дня (run17 q09, PL2→PL4: семь проходов).</summary>
    [Fact]
    public void Diplomacy_AsListEight_MatchesRunPasses()
    {
        bool[] economic = [true, true, true, true, true, true];
        var decisions = new Queue<AiDiploAction>([
            AiDiploAction.Offer, AiDiploAction.Offer, AiDiploAction.Offer,
            AiDiploAction.Offer, AiDiploAction.Offer, AiDiploAction.Offer, AiDiploAction.Offer,
        ]);
        AiDiploDecision Decide(string _) => new(decisions.Dequeue(), null);

        var expected = AiDiplomacy.RunPasses(["S143PL0004"], economic, Decide);

        decisions = new Queue<AiDiploAction>(Enumerable.Repeat(AiDiploAction.Offer, 7));
        int pass = 0;
        IEnumerable<AiEvaluation> Lists(AiStepType t, AiBudget _)
        {
            if (t == AiStepType.HealRevive) pass++;
            return t switch
            {
                AiStepType.HireUnit when pass <= economic.Length => [Eval("", t, "C1")],
                AiStepType.Diplomacy => AiStrategic.DiplomacyEvaluations(["S143PL0004"], Decide),
                _ => [],
            };
        }

        var turn = AiStrategic.RunTurn(1, Lists, _ => true, () => Gold100, Budget(100),
                                       new Dictionary<(string, AiStepType), int>());

        var dipl = Assert.Single(turn.Steps, s => s.Action.Type == AiStepType.Diplomacy);
        Assert.Equal(expected.Single().Pass, dipl.Pass);
        Assert.Equal(7, dipl.Pass);
        Assert.Equal("S143PL0004", dipl.Action.Id);
        Assert.Empty(decisions);
    }

    // ── По типам ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HealRevive_CostPerUnit_HealAlwaysPlusReviveIfRevivable()
    {
        var revive = new AiCareUnit(CanRevive: true, CanHeal: false, ReviveCost: new Bank(100, 0, 0, 0, 0, 0), HealCost: new Bank(3, 0, 0, 0, 0, 0));
        var heal = new AiCareUnit(CanRevive: false, CanHeal: true, ReviveCost: new Bank(999, 0, 0, 0, 0, 0), HealCost: new Bank(7, 0, 0, 0, 0, 0));
        var none = new AiCareUnit(CanRevive: false, CanHeal: false, ReviveCost: new Bank(50, 0, 0, 0, 0, 0), HealCost: new Bank(50, 0, 0, 0, 0, 0));
        Assert.Equal(new Bank(103, 0, 0, 0, 0, 0), AiStrategicRules.CareCost(revive));
        Assert.Equal(new Bank(7, 0, 0, 0, 0, 0), AiStrategicRules.CareCost(heal));
        Assert.Null(AiStrategicRules.CareCost(none));
    }

    [Fact]
    public void UpgradeLeader_TakesStrictMaxPriority_FirstOnTie()
    {
        Assert.Equal("U2", AiStrategicRules.PickUpgrade([("U1", 3), ("U2", 7), ("U3", 7), ("U4", 1)]));
        Assert.Equal("U1", AiStrategicRules.PickUpgrade([("U1", 0), ("U2", 0)]));
        Assert.Null(AiStrategicRules.PickUpgrade([]));
    }

    [Fact]
    public void HireUnit_ThreeStages_SmallMeleeThenBigThenAny()
    {
        var options = new[]
        {
            new AiHireOption("MAGE", SmallMelee: false, Big: false),
            new AiHireOption("FIGHTER", SmallMelee: true, Big: false),
            new AiHireOption("GIANT", SmallMelee: false, Big: true),
            new AiHireOption("ARCHER", SmallMelee: false, Big: false),
        };
        var pools = new List<string[]>();
        string Pick(IReadOnlyList<AiHireOption> pool)
        {
            pools.Add([.. pool.Select(o => o.UnitId)]);
            return pool[^1].UnitId;
        }

        // Пустой гарнизон, 5 мест: боец (1), большой (тоже 1, 0x44bf80), дальше любые на оставшиеся 3.
        var picked = AiStrategicRules.PickUnits(5, hasSmallMelee: false, hasBig: false, options,
                                                p => p.First(o => o.UnitId == Pick(p)));
        Assert.Equal(["FIGHTER", "GIANT", "ARCHER", "ARCHER", "ARCHER"], picked.Select(o => o.UnitId));
        Assert.Equal(["FIGHTER"], pools[0]);
        Assert.Equal(["GIANT"], pools[1]);
        Assert.Equal(4, pools[2].Length);

        // Боец и большой уже есть — только «любые».
        pools.Clear();
        picked = AiStrategicRules.PickUnits(1, hasSmallMelee: true, hasBig: true, options, p => p[0]);
        Assert.Equal(["MAGE"], picked.Select(o => o.UnitId));

        // Нет мест — ни одного броска.
        Assert.Empty(AiStrategicRules.PickUnits(0, false, false, options, _ => throw new InvalidOperationException()));
        // Пустой пул не бросает.
        Assert.Empty(AiStrategicRules.PickUnits(2, false, false, [], _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void HireUnit_Placeable_SmallNeedsCell_BigNeedsColumn()
    {
        Assert.True(AiStrategicRules.Placeable([0, 0, 1, -1, 2, 3], freeSlots: 1, big: false));
        Assert.False(AiStrategicRules.Placeable([0, 0, 1, -1, 2, 3], freeSlots: 1, big: true));
        Assert.True(AiStrategicRules.Placeable([0, 1, -1, -1, 2, 3], freeSlots: 2, big: true));
        // Колонка пуста, но деревня уровня 5 с четырьмя занятыми клетками — места одно.
        Assert.False(AiStrategicRules.Placeable([0, 1, -1, -1, 2, 3], freeSlots: 1, big: true));
        Assert.False(AiStrategicRules.Placeable([-1, 1, 2, 3, 4, 5], freeSlots: 0, big: false));
        Assert.False(AiStrategicRules.Placeable([0, 1, 2, 3, 4, 5], freeSlots: 1, big: false));
    }

    [Theory]
    // Вызовы корпуса 20260918-probe65 (b, i, f, j → ret): роль, боец, вор.
    [InlineData(true, true, true, 1, 1, 0, 0.33f, 0.33f, 0.33f, 0, 1, 2, 2)]
    [InlineData(true, true, true, 1, 1, 1, 0.33f, 0.33f, 0.33f, 0, 1, 2, 0)]
    [InlineData(true, true, true, 4, 3, 3, 0.33f, 0.33f, 0.33f, 0, 1, 2, 1)]
    [InlineData(true, true, false, 1, 0, 0, 0.33f, 0.33f, 0.33f, 0, 1, 2, 1)]
    [InlineData(true, true, true, 0, 0, 0, 0.5f, 0.3f, 0.2f, 1, 0, 2, 1)]
    [InlineData(true, true, true, 1, 1, 0, 0.5f, 0.3f, 0.2f, 1, 0, 2, 2)]
    [InlineData(true, true, false, 1, 0, 0, 0.4f, 0.4f, 0.1f, 0, 2, 1, 2)]
    [InlineData(true, true, false, 1, 1, 1, 0.4f, 0.4f, 0.1f, 0, 2, 1, 0)]
    [InlineData(true, true, true, 2, 2, 0, 0.4f, 0.4f, 0.1f, 0, 2, 1, 1)]
    // Два запрета — без счёта.
    [InlineData(false, false, true, 9, 9, 9, 0.1f, 0.1f, 0.1f, 7, 8, 9, 9)]
    [InlineData(true, false, false, 9, 9, 9, 0.1f, 0.1f, 0.1f, 7, 8, 9, 7)]
    [InlineData(false, true, false, 9, 9, 9, 0.1f, 0.1f, 0.1f, 7, 8, 9, 8)]
    public void HireLeader_PickByShare_MatchesListing(bool b0, bool b1, bool b2, int i0, int i1, int i2,
        float f0, float f1, float f2, int j0, int j1, int j2, int expected)
    {
        Assert.Equal(expected, AiStrategicRules.PickByShare([b0, b1, b2], [i0, i1, i2], [f0, f1, f2], [j0, j1, j2]));
    }

    [Fact]
    public void HireUnit_CrtPicker_UsesRandModCount()
    {
        var rand = new CrtRand(1);
        var options = Enumerable.Range(0, 7).Select(i => new AiHireOption($"U{i}", false, false)).ToList();
        int expected = new CrtRand(1).Next() % 7;
        Assert.Equal($"U{expected}", AiStrategicRules.CrtPicker(rand)(options).UnitId);
    }

    [Fact]
    public void CrtRand_MatchesMsvcrt_AndStateIsRecoveredFromTwoOutputs()
    {
        // srand(1): 41, 18467, 6334, 26500 — известная последовательность msvcrt.
        var r = new CrtRand(1);
        Assert.Equal([41, 18467, 6334, 26500], [r.Next(), r.Next(), r.Next(), r.Next()]);

        var truth = new CrtRand(0xDEADBEEF);
        int a = truth.Next(), b = truth.Next();
        var states = CrtRand.FitAfterTwo(a, b);
        Assert.Contains(truth.State & 0x7FFF_FFFF, states);
        int next = truth.Next();
        Assert.Contains(states, s => new CrtRand(s).Next() == next);
    }

    [Fact]
    public void HireLeader_CitiesWeakestFirst_StableOnTruncatedStrength()
    {
        var ranked = AiStrategicRules.RankCities([("A", 10.9), ("B", 3.2), ("C", 10.1), ("D", 3.9)]);
        Assert.Equal([("B", 7), ("D", 7), ("A", 0), ("C", 0)], ranked);
    }

    [Fact]
    public void HireLeader_TypePick_FiltersRoleAndPurse_AssistTakesMaxMove_ElseR250()
    {
        var options = new[]
        {
            new AiLeaderOption("F1", AiLeaderRole.Fighter, new Bank(100, 0, 0, 0, 0, 0), Move: 20),
            new AiLeaderOption("F2", AiLeaderRole.Fighter, new Bank(100, 0, 0, 0, 0, 0), Move: 25),
            new AiLeaderOption("F3", AiLeaderRole.Fighter, new Bank(100, 0, 0, 0, 0, 0), Move: 25),
            new AiLeaderOption("R1", AiLeaderRole.Rod, new Bank(50, 0, 0, 0, 0, 0), Move: 30),
            new AiLeaderOption("F4", AiLeaderRole.Fighter, new Bank(900, 0, 0, 0, 0, 0), Move: 40),
        };
        var purse = new Bank(500, 0, 0, 0, 0, 0);

        // assist — максимум движения, при равенстве последний (back() после сортировки); без броска.
        var noRoll = new ScriptedRng([]);
        Assert.Equal("F3", AiStrategicRules.PickLeader(options, AiLeaderRole.Fighter, purse, assist: true, noRoll)?.UnitId);
        Assert.Equal(0, noRoll.Used);

        // иначе — list[rng(count)] по отфильтрованным: F1, F2, F3.
        var rng = new ScriptedRng([2]);
        Assert.Equal("F3", AiStrategicRules.PickLeader(options, AiLeaderRole.Fighter, purse, assist: false, rng)?.UnitId);
        Assert.Equal(1, rng.Used);

        // пусто — rng(0) броска не тратит.
        var empty = new ScriptedRng([]);
        Assert.Null(AiStrategicRules.PickLeader(options, AiLeaderRole.Thief, purse, assist: false, empty));
        Assert.Equal(0, empty.Used);
    }

    private static BuildingType B(string id, int category, int level = 2, int branch = 0, string required = "") =>
        new(id, required, new Bank(100, 0, 0, 0, 0, 0), level, category, branch);

    [Fact]
    public void Capital_NoTemple_S1_HealThenMagicThenGuild()
    {
        var available = new[] { B("GUILD", 0), B("MAGIC", 2), B("TEMPLE", 1), B("UNIT", 3) };
        var pick = AiStrategicRules.PickBuilding(available, hasTemple: false, units: [], sideshowBuilding: "UNIT");
        Assert.Equal("TEMPLE", pick?.Id);
        pick = AiStrategicRules.PickBuilding(available[..2], hasTemple: false, units: [], sideshowBuilding: "UNIT");
        Assert.Equal("MAGIC", pick?.Id);
    }

    [Fact]
    public void Capital_WithTemple_S2_MostUnitsNearLevelUp_StrictMax()
    {
        var available = new[] { B("MAGIC", 2), B("UB1", 3), B("UB2", 3) };
        // xp > xpNext − 5: 96 из 100 — да, 95 — нет; юнит с двумя ветками засчитывается обеим.
        AiCapitalUnit[] units =
        [
            new(["UB2"], 96, 100), new(["UB1", "UB2"], 99, 100), new(["UB2"], 200, 200), new(["UB1"], 95, 100),
        ];
        Assert.Equal("UB2", AiStrategicRules.PickBuilding(available, hasTemple: true, units, "UB1")?.Id);

        // Равенство — первая по списку доступных.
        Assert.Equal("UB1", AiStrategicRules.PickBuilding(available, true, [new(["UB2"], 99, 100), new(["UB1"], 99, 100)], "")?.Id);

        // Никого у порога (строгий максимум > 0) — запасная S1: магия, хотя храм уже есть.
        Assert.Equal("MAGIC", AiStrategicRules.PickBuilding(available, true, [new(["UB1"], 1, 100)], "UB2")?.Id);

        // S1 без магии/лечения/гильдии — здание найма базового юнита L_SIDESHOW, если оно доступно.
        Assert.Equal("UB2", AiStrategicRules.PickBuilding(available[1..], true, [], "ub2")?.Id);
        Assert.Null(AiStrategicRules.PickBuilding(available[1..], true, [], "ELSEWHERE"));
    }

    [Fact]
    public void Capital_Candidate_StatusZero_BudgetOnlyForUnitBuildings()
    {
        var temple = B("TEMPLE", 1);
        var unit = B("UB", 3);
        var poor = Budget(0);
        Assert.True(AiStrategicRules.CapitalAllowed(temple, BuildStatus.CanBeBuilt, poor));
        Assert.False(AiStrategicRules.CapitalAllowed(temple, BuildStatus.AlreadyBuilt, Budget(1000)));
        Assert.False(AiStrategicRules.CapitalAllowed(unit, BuildStatus.CanBeBuilt, poor));
        Assert.True(AiStrategicRules.CapitalAllowed(unit, BuildStatus.CanBeBuilt, Budget(100)));
        Assert.True(AiStrategicRules.IgnoresCost(temple));
        Assert.False(AiStrategicRules.IgnoresCost(unit));
    }

    [Fact]
    public void GrowCities_KeepsOrder_NeedsCheckZeroAndBudget()
    {
        var grow = AiStrategicRules.GrowCandidates(
            [("V1", true, new Bank(300, 0, 0, 0, 0, 0)), ("V2", false, Bank.Zero), ("V3", true, new Bank(100, 0, 0, 0, 0, 0))],
            Budget(200));
        Assert.Equal(["V3"], grow);
    }

    [Fact]
    public void SpellResearch_EveryFifthDay_LimitAndLevelThenCategoryOrder()
    {
        AiSpellOption[] spells =
        [
            new("BOOST1", 1, SpellCategory.Boost, true),
            new("ATTACK2", 2, SpellCategory.Attack, true),
            new("SUMMON1", 1, SpellCategory.Summon, false),
            new("LOWER1", 1, SpellCategory.Lower, true),
            new("HEAL1", 1, SpellCategory.Heal, true),
            new("ATTACK1", 1, SpellCategory.Attack, true),
        ];
        Assert.Equal("ATTACK1", AiStrategicRules.PickResearch(10, 3, LordCategory.Warrior, spells)?.Id);
        Assert.Null(AiStrategicRules.PickResearch(11, 3, LordCategory.Warrior, spells));
        Assert.Null(AiStrategicRules.PickResearch(0 + 5, 12, LordCategory.Warrior, spells));
        Assert.Equal("ATTACK1", AiStrategicRules.PickResearch(5, 14, LordCategory.Mage, spells)?.Id);
        Assert.Null(AiStrategicRules.PickResearch(5, 15, LordCategory.Mage, spells));
        // Уровень старше категории: весь уровень 1 раньше атаки уровня 2.
        Assert.Equal("HEAL1", AiStrategicRules.PickResearch(5, 0, LordCategory.Diplomat, spells[..5])?.Id);
        Assert.Equal("BOOST1", AiStrategicRules.PickResearch(5, 0, LordCategory.Diplomat, spells[..3])?.Id);
        Assert.Equal("ATTACK2", AiStrategicRules.PickResearch(5, 0, LordCategory.Diplomat, spells[1..3])?.Id);
        // Пятый уровень не изучается.
        Assert.Null(AiStrategicRules.PickResearch(5, 0, LordCategory.Mage, [new("X5", 5, SpellCategory.Attack, true)]));
    }
    /// <summary>Базы бюджета, которые считают сами списки (<c>0x44a053</c>, <c>0x446a48</c>, <c>0x44bc09</c>).</summary>
    [Fact]
    public void BudgetBases_ByListing()
    {
        var shares = new AiStrategicRules.AiLeaderShares(2, 60, 20, 20, FighterMin: 3, FighterMax: 5, ThiefMin: 2, ThiefMax: 3,
                                                         RodMin: 2, RodMax: 3);
        Assert.Equal(100, AiStrategicRules.LeaderBudgetBase(0, 5, 5, shares, true, true, false, 15));
        Assert.Equal(90, AiStrategicRules.LeaderBudgetBase(2, 5, 5, shares, true, true, false, 15));
        Assert.Equal(90, AiStrategicRules.LeaderBudgetBase(3, 1, 5, shares, rodOk: true, thiefOk: true, false, 15));
        Assert.Equal(15, AiStrategicRules.LeaderBudgetBase(3, 1, 5, shares, rodOk: false, thiefOk: true, false, 15));   // жезла не бывает
        Assert.Equal(0, AiStrategicRules.LeaderBudgetBase(5, 2, 2, shares, true, true, false, 15));
        Assert.Equal(15, AiStrategicRules.LeaderBudgetBase(5, 2, 2, shares, true, true, highLevel: true, 15));  // воров меньше T_MAX
        Assert.Equal(90, AiStrategicRules.CapitalBudgetBase(hasTemple: false, fieldLeaders: 3, bCapital: 50));
        Assert.Equal(90, AiStrategicRules.CapitalBudgetBase(hasTemple: true, fieldLeaders: 0, bCapital: 50));
        Assert.Equal(50, AiStrategicRules.CapitalBudgetBase(hasTemple: true, fieldLeaders: 1, bCapital: 50));
        Assert.Equal(100, AiStrategicRules.HireUnitBudgetBase(1, 75));
        Assert.Equal(75, AiStrategicRules.HireUnitBudgetBase(2, 75));
    }

    /// <summary>Пределы лидеров (<c>0x44a71e</c>): ниже уровня 10 — своя строка; с 10 — строка <c>L_AI_SMALL</c>, дипломату
    /// <c>T_MAX</c> + 1; доли <c>PCT_*</c> свои.</summary>
    [Fact]
    public void LeaderLimits_HighLevelSmallRowAndDiplomat()
    {
        var own = new AiStrategicRules.AiLeaderShares(3, 60, 20, 20, FighterMin: 4, FighterMax: 6, ThiefMin: 2, ThiefMax: 4, RodMin: 2, RodMax: 4);
        var small = new AiStrategicRules.AiLeaderShares(0, 33, 33, 33, FighterMin: 1, FighterMax: 2, ThiefMin: 1, ThiefMax: 1, RodMin: 1, RodMax: 1);
        Assert.Same(own, AiStrategicRules.LeaderLimits(own, small, highLevel: false, diplomat: true));
        Assert.Equal(own with { FighterMin = 1, FighterMax = 2, ThiefMin = 1, ThiefMax = 1, RodMin = 1, RodMax = 1 },
                     AiStrategicRules.LeaderLimits(own, small, highLevel: true, diplomat: false));
        Assert.Equal(own with { FighterMin = 1, FighterMax = 2, ThiefMin = 1, ThiefMax = 2, RodMin = 1, RodMax = 1 },
                     AiStrategicRules.LeaderLimits(own, small, highLevel: true, diplomat: true));
    }
}
