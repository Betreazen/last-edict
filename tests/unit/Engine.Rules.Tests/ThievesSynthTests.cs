using LastEdict.Data;
using LastEdict.Data.Map;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Tests;

/// <summary>Разбор синтетических карт 16.09 (группа C, <c>docs/synthetic-maps/analysis/C-thieves.md</c>),
/// план Ф7 §11.3 пп. 11–12: фильтр доступности <c>0x5D32B8</c>, цена отмены и пустого списка, формулы
/// отравления, кражи и подкупа, модель мятежа. Все числа — из журналов карт ai-q11, q12, q22, q42, q43.</summary>
public class ThievesSynthTests
{
    private static readonly ThiefRules Rules = ThiefRules.Vanilla;

    // Цели ai-q43 (сессии Воителя и Гильдмастера) и ai-q12.
    private static readonly ThiefTargetState Merchant = new(ThiefTargetKind.Merchant) { Stocked = true };
    private static readonly ThiefTargetState MageTower = new(ThiefTargetKind.MageTower) { Stocked = true };
    private static readonly ThiefTargetState Ruin = new(ThiefTargetKind.Ruin);

    /// <summary>Нейтральный отряд KC0015: живые юниты, лидер, больше одного юнита, предметов нет, в поле.</summary>
    private static readonly ThiefTargetState NeutralStack = new(ThiefTargetKind.Stack)
    {
        HasUnitAboveOneHp = true, HasLeader = true, UnitCount = 3,
    };

    /// <summary>Деревня эльфов FT0007: играбельный не-союзник, гарнизон жив, у эльфов нечего украсть.</summary>
    private static readonly ThiefTargetState ElfVillage = new(ThiefTargetKind.Fortification)
    {
        OwnerPlayable = true, HasUnitAboveOneHp = true, UnitCount = 2, IsVillage = true, RiotPossible = true,
        BribeCost = 108,
    };

    /// <summary>Нейтральный Skorlus FT0003: цена подкупа 2 × 3 × 635 = 3810.</summary>
    private static readonly ThiefTargetState Skorlus = new(ThiefTargetKind.Fortification)
    {
        HasUnitAboveOneHp = true, UnitCount = 2, IsVillage = true, RiotPossible = true, BribeCost = 3810,
    };

    /// <summary>Neutral N из ai-q12: цена 1 × 3 × (30 + 30) = 180.</summary>
    private static readonly ThiefTargetState NeutralN = Skorlus with { BribeCost = 180 };

    private static int[] Codes(LordCategory lord, ThiefTargetState target, int gold) =>
        [.. Thieves.Available(lord, target, gold).Select(a => (int)a).Order()];

    [Fact]
    public void Available_MatchesTheQ43Table()
    {
        (ThiefTargetState Target, int[] Warrior, int[] Guild)[] table =
        [
            (Merchant, [10], [10]),
            (NeutralStack, [0, 5], [0, 3, 4, 5]),
            (ElfVillage, [1, 6], [1, 3, 4, 6, 9, 13]),
            (Skorlus, [6], [6, 13]),
            (Ruin, [12], [12]),
            (MageTower, [11], [11]),
        ];
        foreach (var (target, warrior, guild) in table)
        {
            Assert.Equal(warrior, Codes(LordCategory.Warrior, target, 550));
            Assert.Equal(guild, Codes(LordCategory.Diplomat, target, 550));
        }
    }

    [Fact]
    public void Available_MatchesQ12_BribeOnlyForNeutralWithinGold()
    {
        Assert.Equal([1, 3, 4, 6, 9, 13], Codes(LordCategory.Diplomat, ElfVillage, 5150));   // 108 ≤ 5150, но раса играбельна
        Assert.Equal([6, 8, 13], Codes(LordCategory.Diplomat, NeutralN, 5150));
        Assert.Equal([6, 13], Codes(LordCategory.Diplomat, Skorlus, 550));                  // 3810 > 550
        Assert.Equal([6, 8, 13], Codes(LordCategory.Diplomat, NeutralN, 180));              // ровно на цене — можно
        Assert.Equal([6, 13], Codes(LordCategory.Diplomat, NeutralN with { BribeCost = 0 }, 5150));
    }

    [Fact]
    public void Available_FollowsTheFilterBranches()
    {
        // Порядок — по возрастанию номера: фильтр 0x5D35CB / 0x5D3856 кладёт действия в Set<LNobleActionCat>, выбор ИИ перемешивает
        // вектор из обхода множества (0x45bc14); noble_choose зонда — всегда по возрастанию.
        Assert.Equal([ThiefAction.Spy, ThiefAction.Assassinate, ThiefAction.Misfit, ThiefAction.PoisonCity, ThiefAction.StealGold,
                      ThiefAction.RiotCity],
                     Thieves.Available(LordCategory.Diplomat, ElfVillage, 0));
        // Бунтующий город — пусто даже у Гильдмастера; повтор мятежа запрещён.
        Assert.Empty(Thieves.Available(LordCategory.Diplomat, ElfVillage with { Rioting = true }, 5150));
        // Визитёр в городе — остаётся только Riot у Гильдмастера.
        Assert.Equal([13], Codes(LordCategory.Diplomat, ElfVillage with { HasVisitor = true }, 5150));
        Assert.Empty(Thieves.Available(LordCategory.Warrior, ElfVillage with { HasVisitor = true }, 5150));
        // Пустой гарнизон: ни Poison City, ни Assassinate, ни Counterfeit (ai-q22: пустая нейтральная деревня у Воителя — пусто).
        var empty = ElfVillage with { UnitCount = 0, HasUnitAboveOneHp = false };
        Assert.Equal([1, 9, 13], Codes(LordCategory.Diplomat, empty, 0));
        Assert.Empty(Thieves.Available(LordCategory.Warrior, Skorlus with { UnitCount = 0, HasUnitAboveOneHp = false }, 0));
        // Assassinate по укреплению — только деревня; Steal Spell — если есть что украсть; уже разведанному Spy нет,
        // союзнику — ничего (0x5F7F5A на входе 0x5D32B8).
        Assert.Equal([1, 4, 6, 7, 9, 13],
                     Codes(LordCategory.Diplomat, ElfVillage with { IsVillage = false, HasSpellToSteal = true }, 0));
        Assert.Equal([6], Codes(LordCategory.Warrior, ElfVillage with { OwnerSpied = true }, 0));
        Assert.Empty(Thieves.Available(LordCategory.Diplomat, ElfVillage with { OwnerAllied = true }, 5150));

        // ai-q11, дни 2–6: эльфийский отряд S143KC0029 из одного юнита в городе A — Riot только когда A не бунтует.
        var elfStack = new ThiefTargetState(ThiefTargetKind.Stack)
        {
            OwnerPlayable = true, HasUnitAboveOneHp = true, HasLeader = true, UnitCount = 1,
        };
        Assert.Equal([0, 1, 3, 5], Codes(LordCategory.Diplomat, elfStack, 0));
        Assert.Equal([0, 1, 3, 5, 13], Codes(LordCategory.Diplomat, elfStack with { RiotPossible = true }, 0));
        Assert.Equal([0, 1, 5], Codes(LordCategory.Warrior, elfStack with { RiotPossible = true }, 0));
        // Poison Party — только при юните с HP > 1; Duel — при лидере; Steal Item — при предметах.
        Assert.Equal([2, 3, 4], Codes(LordCategory.Diplomat,
                                      NeutralStack with { HasUnitAboveOneHp = false, HasLeader = false, ItemCount = 1 }, 0));
        // Руина посещена, лавка пуста — ничего.
        Assert.Empty(Thieves.Available(LordCategory.Diplomat, Ruin with { RuinVisited = true }, 0));
        Assert.Empty(Thieves.Available(LordCategory.Diplomat, Merchant with { Stocked = false }, 0));

        Assert.True(Thieves.Allowed(LordCategory.Diplomat, ElfVillage, 0, ThiefAction.StealGold));
        Assert.False(Thieves.Allowed(LordCategory.Diplomat, Skorlus, 0, ThiefAction.StealGold));
    }

    /// <summary>ai-g2-thief-riot (20260920-gaps, лорд-Гильдмастер, три вора у деревни эльфов FT0007). День 1:
    /// noble_choose seq 194025 по деревне — [1,3,4,6,9,13], мятеж (seq 195053), RIOT_T 5 при ходе 1; воры 2 и 3
    /// (seq 196570, 226653) — пустой список. День 2 «все воры снова могут действовать» — не конец мятежа: RIOT_T 5
    /// держится в снимках ходов 1–3 и в fort_day seq 263275, а на ходу эльфов 1 в деревню вошёл отряд S143KC0028
    /// (fort_day: stack = S143KC0028, stack_move seq 240934). Мишенью стал визитёр — отряд из одного юнита, и его
    /// список [0,1,3,5] (seq 256006, 258297, 260661) — без Riot, потому что город бунтует (0x5D377D).</summary>
    [Fact]
    public void Riot_BlocksTheCityButNotItsVisitor_G2()
    {
        Assert.Equal([1, 3, 4, 6, 9, 13], Codes(LordCategory.Diplomat, ElfVillage, 0));
        Assert.Empty(Thieves.Available(LordCategory.Diplomat, ElfVillage with { Rioting = Fortifications.Rioting(5) }, 0));
        var visitor = new ThiefTargetState(ThiefTargetKind.Stack)
        {
            OwnerPlayable = true, HasUnitAboveOneHp = true, HasLeader = true, UnitCount = 1, RiotPossible = false,
        };
        Assert.Equal([0, 1, 3, 5], Codes(LordCategory.Diplomat, visitor, 0));
        Assert.False(Fortifications.RiotEndsAt(5, 2));      // день 2 и 3 — бунт ещё идёт
        Assert.False(Fortifications.RiotEndsAt(5, 3));
        Assert.True(Fortifications.RiotEndsAt(5, 5));
    }

    /// <summary>ai-q42, q22: отмена — ceil(MOVE/2) = 13; пустой список — 13 и при закрытии ещё 13.</summary>
    [Fact]
    public void CancelAndEmptyList_ChargeHalfTheMovement()
    {
        Assert.Equal(13, Thieves.CancelCost(25));
        Assert.Equal(12, Thieves.Cancel(moveAllowance: 25, moveLeft: 25));
        Assert.Equal(0, Thieves.NoActions(moveAllowance: 25, moveLeft: 25));    // 25 → 12 → 0
        Assert.Equal(0, Thieves.NoActions(moveAllowance: 25, moveLeft: 23));    // q22: 23 → 10 → 0
        Assert.Equal(26, Thieves.NoActionsCharges * Thieves.CancelCost(25));
        // Провал броска — без списания.
        var failed = Thieves.Perform(Rules, new ScriptedRng([92, 92]), ThiefAction.PoisonCity, 80, 25, 25);
        Assert.Equal((0, 25), (failed.Cost, failed.MoveLeft));
    }

    [Fact]
    public void Formulas_ReproduceTheQ12Numbers()
    {
        Assert.Equal(37, Thieves.PoisonDamage(150, 25));      // 150 → 113 (POISON_C 25)
        Assert.Equal(13, Thieves.PoisonDamage(65, 20));       // q42: 65 → 52 (POISON_S 20 от ТЕКУЩЕГО HP)
        Assert.Equal(22, Thieves.PoisonDamage(110, 20));      // 110 → 88
        Assert.Equal(9, Thieves.PoisonDamage(45, 20));        // 45 → 36
        Assert.Equal(1, Thieves.PoisonDamage(2, 99));         // остаток не ниже 1
        Assert.Equal(0, Thieves.PoisonDamage(1, 50));

        Assert.Equal(250, Thieves.StealGoldMax(Rules, 1000, ownerPlayable: true));      // STEAL_RACE 25
        Assert.Equal(2500, Thieves.StealGoldMax(Rules, 1000, ownerPlayable: false));    // STEAL_NEUT 250
        var rng = new ScriptedRng([179]);
        Assert.Equal(180, Thieves.StealGold(rng, 250));       // rand(X) + 1: bank эльфов 1000 → 820
        Assert.Equal(1, rng.Used);

        Assert.Equal(180, Thieves.BribeCost(Rules, 1, 30 + 30));
        Assert.Equal(3810, Thieves.BribeCost(Rules, 2, 635));

        // Подкуп — захват без проверки лидера: гарнизон стёрт, вор — визитёр.
        var fort = new MapSite { Id = "S143FT0005", Kind = SiteKind.Village, RawKind = "", Owner = "S143PL0000", Guards = ["a", "b"] };
        var thief = new MapStack
        {
            Id = "S143KC002c", Owner = "S143PL0003", Subrace = "S143SR0001", LeaderId = "x", Inside = "",
            UnitIds = [], Slots = [],
        };
        var bribed = Thieves.Bribe(fort, thief);
        Assert.Equal(("S143PL0003", 0, "S143KC002c"), (bribed.Owner, bribed.Guards.Count, bribed.StackId));
    }

    [SkippableFact]
    public void Globals_CarryTheThiefVariables()
    {
        ImportedData.RequireRules();
        var rules = EconomyLoader.LoadThieves(ImportedData.Content);
        Assert.Equal((20, 25, 3, 25, 250), (rules.PoisonStack, rules.PoisonCity, rules.Bribe, rules.StealRace, rules.StealNeut));
        Assert.Equal((1, 4, 20), (rules.RiotMin, rules.RiotMax, rules.RiotDamage));
        Assert.Equal(Rules with { ActionChance = rules.ActionChance, Morale = rules.Morale }, rules);
    }

    /// <summary>ai-q11: RIOT_T = ход + RIOT_MIN + 1 + rand(RIOT_MAX − RIOT_MIN); бунт — «RIOT_T &gt; 0»;
    /// сброс в начале хода владельца при ход ≥ RIOT_T и в тот же момент разовый RIOT_DMG.</summary>
    [Fact]
    public void Riot_LastsUntilTheOwnersTurnReset()
    {
        var draws = new ScriptedRng([2, 1, 0]);
        Assert.Equal(5, Fortifications.RiotEnd(Rules, turn: 1, draws));        // A, день 1
        Assert.Equal(4, Fortifications.RiotEnd(Rules, turn: 1, draws));        // B, день 1
        Assert.Equal(7, Fortifications.RiotEnd(Rules, turn: 5, draws));        // минимум 2 хода

        // RIOT_T 5 при ходе 1: бунт на ходах 1–4 и на ходе 5 до хода владельца.
        foreach (int turn in new[] { 1, 2, 3, 4, 5 })
        {
            Assert.True(Fortifications.Rioting(5));
            Assert.Equal(turn == 5, Fortifications.RiotEndsAt(5, turn));
            Assert.Equal(CityGrowCheck.Riot, Fortifications.CheckGrow(3, 5, 0, turn, Bank.Zero, Bank.Zero));
        }
        Assert.False(Fortifications.RiotEndsAt(0, 9));
        Assert.False(Fortifications.Rioting(0));
        Assert.False(Fortifications.Rioting(-1));                              // столица: −1

        var fort = new MapSite { Id = "A", Kind = SiteKind.Village, RawKind = "", RiotTurn = 5 };
        Assert.Same(fort, Fortifications.EndRiot(fort, 4));
        Assert.Equal(0, Fortifications.EndRiot(fort, 5).RiotTurn);

        // В момент сброса: ⌊20 % текущего HP⌋ гарнизону и визитёру.
        Assert.Equal(120, Regeneration.RiotHit(150, Rules.RiotDamage));
        Assert.Equal(72, Regeneration.RiotHit(90, Rules.RiotDamage));
        Assert.Equal(1, Regeneration.RiotHit(1, Rules.RiotDamage));
        Assert.Equal(0, Regeneration.RiotHit(0, Rules.RiotDamage));
    }
}
