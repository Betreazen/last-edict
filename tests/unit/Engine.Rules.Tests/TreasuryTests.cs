using LastEdict.Data;
using LastEdict.Data.Magic;
using LastEdict.Data.Map;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;

namespace LastEdict.Rules.Tests;

/// <summary>
/// Казна: одна операция изменения и цены механик (<see cref="Treasury"/>). Числа — из живых прогонов
/// Ф7 (план <c>disciples-modding/docs/superpowers/plans/2026-09-09-f7-map-economy-leaders.md</c>,
/// разделы 10–19); сверка со всем корпусом — <see cref="TreasuryHarnessTests"/>.
/// </summary>
public class TreasuryTests
{
    private static Bank Gold(long g) => new(g, 0, 0, 0, 0, 0);

    // ---------------------------------------------------------------- операция казны

    [Fact]
    public void Apply_ClampsAfterAddition_EveryCurrency()
    {
        // Империя 5523 + 6662 = 12185 -> 9999 (big2, захват столицы Империи).
        Assert.Equal(Gold(9999), Treasury.Apply(Gold(5523), Gold(6662)));
        // Трата не уходит в минус.
        Assert.Equal(Bank.Zero, Treasury.Pay(Gold(20), Gold(50)));
        // Продажа при полной казне теряется (big2: IG5025 за 40 при 9999).
        Assert.Equal(Gold(9999), Treasury.Apply(Gold(9999), Gold(40)));
    }

    [Fact]
    public void Apply_DailyIncomeAtMapLoad_AddsRaceIncome()
    {
        // Загрузка карты — тоже дневной тик: 2000 -> 2100 золота, 0 -> 25 маны жизни (прогон 2).
        var income = new Bank(100, 0, 25, 0, 0, 0);
        Assert.Equal(new Bank(2100, 0, 25, 0, 0, 0), Treasury.Apply(Gold(2000), income));
    }

    // ---------------------------------------------------------------- захват столицы

    [Fact]
    public void CapitalCapture_Undead_WinnerGetsWholeTreasury()
    {
        var winner = new Bank(4527, 9999, 9999, 9999, 9999, 9999);
        var loser = new Bank(379, 400, 325, 125, 525, 0);

        var (w, l) = Treasury.CapitalCapture(winner, loser, victory: false);

        Assert.Equal(new Bank(4906, 9999, 9999, 9999, 9999, 9999), w);   // +379 золота ровно
        Assert.Equal(Bank.Zero, l);
    }

    [Fact]
    public void CapitalCapture_Empire_ClampsSumAt9999()
    {
        var (w, l) = Treasury.CapitalCapture(new Bank(5523, 9999, 9999, 9999, 9999, 9999),
                                             new Bank(6662, 375, 215, 100, 150, 0), victory: false);
        Assert.Equal(new Bank(9999, 9999, 9999, 9999, 9999, 9999), w);
        Assert.Equal(Bank.Zero, l);
    }

    [Fact]
    public void CapitalCapture_M22_AllSixCurrenciesMove()
    {
        var (w, l) = Treasury.CapitalCapture(new Bank(150, 0, 75, 0, 0, 0),
                                             new Bank(500, 333, 444, 555, 666, 777), victory: false);
        Assert.Equal(new Bank(650, 333, 519, 555, 666, 777), w);          // +500 +333 +444 +555 +666 +777
        Assert.Equal(Bank.Zero, l);
    }

    [Fact]
    public void CapitalCapture_VictoryBranch_MovesNothing()
    {
        // f7-m20: у захватчика один живой соперник, события победы нет — CEffectWin, перехода нет.
        bool victory = Treasury.CaptureEndsInVictory(liveOpponents: 1, scenarioHasWinEvent: false,
                                                     capitalOwnerIsHuman: false, multiplayer: false);
        var loser = new Bank(500, 333, 444, 555, 666, 777);

        var (w, l) = Treasury.CapitalCapture(Gold(150), loser, victory);

        Assert.True(victory);
        Assert.Equal(Gold(150), w);
        Assert.Equal(loser, l);
    }

    [Theory]
    [InlineData(2, false, false, false, false)]   // big2: Империя взята при двух живых соперниках
    [InlineData(1, false, false, false, true)]    // m20: последний соперник
    [InlineData(1, true, false, false, false)]    // событие победы в сценарии есть — обычный захват
    [InlineData(3, false, true, false, true)]     // ИИ взял столицу человека в одиночной игре
    [InlineData(3, false, true, true, false)]     // то же в сетевой игре — захват
    public void CaptureEndsInVictory_FollowsGenerator0x440EE0(
        int opponents, bool winEvent, bool ownerHuman, bool multiplayer, bool expected) =>
        Assert.Equal(expected, Treasury.CaptureEndsInVictory(opponents, winEvent, ownerHuman, multiplayer));

    // ---------------------------------------------------------------- лечение

    [Theory]
    [InlineData(1, 40, 1900, 40, 40)]      // Squire, нехватка 40
    [InlineData(2, 179, 1260, 179, 358)]   // Imperial Knight
    [InlineData(3, 124, 400, 124, 372)]    // Ангел после зелья
    [InlineData(3, 224, 255, 85, 255)]     // частичное: в казне 255 -> 85 HP
    [InlineData(3, 31, 28, 9, 27)]         // частичное: в казне 28 -> списано 27, 9 HP, остаётся 1
    public void Heal_PaysHealCostPerMissingHp_UpToTreasury(int rate, int missing, int purse, int hp, int cost)
    {
        var bought = Treasury.Heal(Gold(rate), missing, Gold(purse));

        Assert.Equal(hp, bought.Points);
        Assert.Equal(Gold(cost), bought.Cost);
        Assert.Equal(Gold(purse - cost), Treasury.Pay(Gold(purse), bought.Cost));
    }

    [Fact]
    public void Heal_ZeroRate_HealsFullyForFree() =>
        Assert.Equal(new Purchase(50, Bank.Zero), Treasury.Heal(Bank.Zero, 50, Bank.Zero));

    [Fact]
    public void Revive_IsFlatReviveCost()
    {
        var angel = new UnitCosts("g000uu0014", Gold(600), Gold(3), Gold(0), 1000);
        Assert.Equal(Gold(1300), Treasury.Pay(Gold(1900), Treasury.RevivePrice(angel)));   // прогон 2
    }

    // ---------------------------------------------------------------- обучение

    [Theory]
    [InlineData(4, 70, 0, 405, 69, 276)]       // Archer до XP_NEXT − 1
    [InlineData(4, 80, 0, 329, 79, 316)]       // Squire
    [InlineData(4, 80, 0, 105, 26, 104)]       // частичное: казна 105 -> 26 очков за 104
    [InlineData(4, 80, 26, 1000, 53, 212)]     // второе обучение того же Squire: потолок на итоговом опыте
    [InlineData(5, 475, 0, 110, 22, 110)]      // Knight, ставка 5 из Gunits
    [InlineData(5, 550, 0, 9889, 549, 2745)]   // big2: UU0053 ур. 2
    [InlineData(4, 70, 69, 5000, 0, 0)]        // уже на потолке — «прошёл курс», ничего не списано
    public void Training_PaysRatePerXpPoint_UpToXpNextMinusOne(
        int rate, int xpNext, int xp, int purse, int points, int cost)
    {
        var bought = Treasury.Training(Gold(rate), xpNext, xp, Gold(purse));

        Assert.Equal(points, bought.Points);
        Assert.Equal(Gold(cost), bought.Cost);
    }

    // ---------------------------------------------------------------- рост города, продажа, дань

    [Theory]
    [InlineData(1, 2, 75)]
    [InlineData(2, 2, 125)]
    [InlineData(3, 2, 250)]
    [InlineData(4, 2, 375)]     // Повелитель Воров: 75/125/250/375 (прогон 1)
    [InlineData(2, 1, 250)]
    [InlineData(3, 1, 500)]     // Воитель: табличные 250 и 500 (city-hotkeys)
    [InlineData(1, 0, 150)]
    public void CityGrowthPrice_IsGrowthOfCurrentSize_HalvedForGuildmaster(int size, int lordCategory, int gold)
    {
        var growth = new[] { 150, 250, 500, 750, 750 };
        var cities = Enumerable.Range(1, 5)
            .Select(s => new CityInfo("g000rr0000", s, 0, 0, "", Gold(growth[s - 1]), 0)).ToList();
        var rules = new EconomyRules(new Dictionary<string, RaceInfo>(), cities,
            new Dictionary<string, BuildingType>(), new Dictionary<string, UnitHire>(),
            IncomeBonusPercent: [0, 0, 0, 0], CrystalProfit: 50, SellRatio: 20, ConstructionUrgency: 0);

        Assert.Equal(Gold(gold), Treasury.CityGrowthPrice(rules, "G000RR0000", size, lordCategory));
    }

    [Theory]
    [InlineData(150, 30)]
    [InlineData(400, 80)]
    [InlineData(1000, 200)]
    [InlineData(1200, 240)]
    public void ItemSellPrice_IsValueTimesSellRatio(int value, int gold)
    {
        var item = new Item("G000IG0005", ItemCategory.PotionHeal, Gold(value), "", "", 0, "", Bank.Zero, "", "");
        Assert.Equal(Gold(gold), Treasury.ItemSellPrice(item, sellRatio: 20));
    }

    /// <summary>ai-q03 (ранг 1): Lightning <c>y0200</c> — Маг 100, Воитель 200; Wrath of God
    /// <c>y0450 w0450</c> — Маг 225 + 225, Воитель 450 + 450.</summary>
    [Theory]
    [InlineData("g0000:r0000:y0200:e0000:w0000", Treasury.MageCategory, "g0000:r0000:y0100:e0000:w0000")]
    [InlineData("g0000:r0000:y0200:e0000:w0000", 1, "g0000:r0000:y0200:e0000:w0000")]
    [InlineData("g0000:r0000:y0450:e0000:w0450", Treasury.MageCategory, "g0000:r0000:y0225:e0000:w0225")]
    [InlineData("g0000:r0000:y0450:e0000:w0450", 1, "g0000:r0000:y0450:e0000:w0450")]
    public void SpellResearchPrice_IsHalvedForMage(string research, int lordCategory, string price) =>
        Assert.Equal(Bank.Parse(price), Treasury.SpellResearchPrice(Bank.Parse(research), lordCategory));

    /// <summary>ai-q03 (ранг 1, 1/0): у Мага Империи Башня магии <c>G000BB0024</c> построена при старте
    /// бесплатно; у Воителя её нет (он строил её за 150 на 2-м ходу).</summary>
    [Fact]
    public void StartBuildings_MageGetsItsMagicTower()
    {
        BuildingType B(string id, int category) => new(id, "", Gold(150), 2, category, 0);
        var empire = new[] { B("G000BB0023", 1), B("G000BB0024", 2), B("G000BB0025", 0) };

        Assert.Equal(["G000BB0024"], Treasury.StartBuildings(Treasury.MageCategory, empire));
        Assert.Empty(Treasury.StartBuildings(1, empire));
        Assert.Empty(Treasury.StartBuildings(Treasury.GuildmasterCategory, empire));
    }

    [Fact]
    public void Tribute_MovesGoldFromPayerToRecipient()
    {
        // Прогон 4: дань 300, получатель 60 -> 360, плательщик 3000 -> 2700.
        var (payer, recipient) = Treasury.Tribute(new Bank(3000, 1000, 1075, 1000, 1000, 1000),
                                                  new Bank(60, 0, 0, 0, 37, 0), 300);
        Assert.Equal(new Bank(2700, 1000, 1075, 1000, 1000, 1000), payer);
        Assert.Equal(new Bank(360, 0, 0, 0, 37, 0), recipient);
    }

    // ---------------------------------------------------------------- справочники игры

    [SkippableFact]
    public void Prices_FromGameTables_MatchRunNumbers()
    {
        ImportedData.RequireRules();
        var treasury = EconomyLoader.LoadTreasury(ImportedData.Content);
        var economy = EconomyLoader.Load(ImportedData.Content);
        var items = ItemLoader.Load(ImportedData.Content);
        var spells = SpellLoader.Load(ImportedData.Content);

        Assert.Equal(Gold(150), Treasury.RodPrice(treasury));
        Assert.Equal(Gold(600), Treasury.RevivePrice(treasury.Units["g000uu0014"]));
        Assert.Equal(Gold(5), treasury.Units["g000uu0002"].Training);
        Assert.Equal(475, treasury.Units["g000uu0002"].XpNext);
        Assert.Equal(Treasury.GuildmasterCategory, treasury.LordCategory["G000LR0002"]);
        Assert.Equal(Treasury.MageCategory, treasury.LordCategory["G000LR0003"]);
        Assert.Equal(Gold(150), Treasury.ItemBuyPrice(items["G000IG0005"]));
        Assert.Equal(Gold(30), Treasury.ItemSellPrice(items["G000IG0005"], economy.SellRatio));
        Assert.Equal(Gold(200), Treasury.SpellBuyPrice(spells["g000ss0001"]));
        Assert.Equal(Gold(300), Treasury.BuildingPrice(economy.Buildings["g000bb0023"]));
        Assert.Equal(Gold(75), Treasury.CityGrowthPrice(economy, "g000rr0000", 1, Treasury.GuildmasterCategory));
        Assert.Equal(Gold(250), Treasury.CityGrowthPrice(economy, "g000rr0000", 2, lordCategory: 1));
    }
}
