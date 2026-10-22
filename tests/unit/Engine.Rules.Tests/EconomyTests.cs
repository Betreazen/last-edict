using LastEdict.Data;
using LastEdict.Data.Map;
using LastEdict.Rules.Map;
using Xunit.Abstractions;

namespace LastEdict.Rules.Tests;

/// <summary>
/// Экономика: справочники, дневной доход, найм, постройки. Формула дохода — ранг 1, прочитана
/// в дизассемблере (<c>computePlayerDailyIncome</c>, Gog <c>0x5da55f</c>); подробности и адреса —
/// в комментариях <see cref="Economy"/>. Здесь она проверяется на синтетике (каждое слагаемое
/// отдельно) и на настоящих <c>.sg</c>; сверка с журналом зонда — <see cref="EconomyHarnessTests"/>.
/// </summary>
public class EconomyTests
{
    private readonly ITestOutputHelper _out;

    public EconomyTests(ITestOutputHelper output) => _out = output;

    private const string Empire = "g000rr0000";      // Grace.RACE_TYPE 0 -> земля L_HU (1)
    private const string Neutral = "g000rr0004";     // PLAYABLE = F

    // ---------------------------------------------------------------- справочники

    [SkippableFact]
    public void Load_ReadsRacesCitiesBuildingsAndVars()
    {
        ImportedData.RequireRules();
        var rules = EconomyLoader.Load(ImportedData.Content);

        var empire = rules.Races[Empire];
        Assert.Equal(100, empire.Income.Gold);
        Assert.Equal(25, empire.Income.Life);
        Assert.Equal(0, empire.RaceType);
        Assert.True(empire.Playable);
        Assert.False(rules.Races[Neutral].Playable);

        Assert.Equal(65, rules.Cities.Count);                 // тринадцать рас по пять размеров
        var size3 = rules.Cities.Single(c => c.RaceId == Empire && c.Size == 3);
        Assert.Equal(50, size3.ResourceMoney);
        Assert.Equal(5, size3.Scout);
        Assert.Equal(500, size3.Growth.Gold);

        var knight2 = rules.Buildings["g000bb0002"];
        Assert.Equal(500, knight2.Cost.Gold);
        Assert.Equal("g000bb0001", knight2.RequiredId);
        Assert.Equal(Economy.UnitBuildingCategory, knight2.Category);
        Assert.Equal("", rules.Buildings["g000bb0001"].RequiredId);   // корневая: g000000000 -> ""

        // GVars, ранг 1 (прочитано): надбавка к доходу по сложности и выработка жилы.
        Assert.Equal(new[] { 30, 0, 0, 0 }, rules.IncomeBonusPercent);
        Assert.Equal(50, rules.CrystalProfit);
        Assert.Equal(20, rules.SellRatio);

        // Найм: цена рекрута Империи из Gunits.ENROLL_C.
        Assert.Equal(50, Economy.HireCost("g000uu0001", rules).Gold);
        Assert.Equal("", rules.Hire["g000uu0001"].BuildingId);
        Assert.Equal("g000bb0022", rules.Hire["g000uu0018"].BuildingId);   // Титан — из постройки
    }

    // ---------------------------------------------------------------- дневной доход

    [Fact]
    public void DailyIncome_CapitalOnly_IsRaceIncome()
    {
        var rules = SyntheticRules();
        var map = MapWith(Ground.Plain, terrain: 1);

        Assert.Equal(new Bank(100, 0, 25, 0, 0, 0), Economy.DailyIncome(map, "P1", rules));
    }

    [Fact]
    public void DailyIncome_EasyDifficulty_AddsIncomePercentToEveryCurrency()
    {
        var rules = SyntheticRules();
        var map = MapWith(Ground.Plain, terrain: 1);

        // 100 + 100*30/100 = 130; 25 + 25*30/100 = 25 + 7 (усечение к нулю) = 32.
        Assert.Equal(new Bank(130, 0, 32, 0, 0, 0),
            Economy.DailyIncome(map, "P1", rules, Difficulty.Easy));
        // Средняя и выше: INCOME_A/H/V = 0, доход не меняется.
        Assert.Equal(new Bank(100, 0, 25, 0, 0, 0),
            Economy.DailyIncome(map, "P1", rules, Difficulty.Hard));
    }

    [Theory]
    // жила на своей равнине и в своём лесу — платит; вода, горы и чужая земля — нет
    [InlineData(Ground.Plain, 1, 50)]
    [InlineData(Ground.Forest, 1, 50)]
    [InlineData(Ground.Water, 1, 0)]
    [InlineData(Ground.Mountain, 1, 0)]
    [InlineData(Ground.Plain, 4, 0)]      // земля Орд
    [InlineData(Ground.Plain, 5, 0)]      // ничья земля
    public void DailyIncome_ManaNodePaysOnlyOnOwnLandAndOnlyOnPlainOrForest(
        Ground ground, int terrain, int expectedLife)
    {
        var rules = SyntheticRules();
        var map = MapWith(ground, terrain, node: (2, 2, Resource: 2));   // 2 = L_YELLOW, мана жизни

        var income = Economy.DailyIncome(map, "P1", rules);
        Assert.Equal(25 + expectedLife, income.Life);
        Assert.Equal(100, income.Gold);
    }

    [Fact]
    public void DailyIncome_GoldMineAddsGold_NotMana()
    {
        var rules = SyntheticRules();
        var map = MapWith(Ground.Plain, terrain: 1, node: (2, 2, Resource: 0));   // L_GOLD

        Assert.Equal(new Bank(150, 0, 25, 0, 0, 0), Economy.DailyIncome(map, "P1", rules));
    }

    [Fact]
    public void DailyIncome_UnplayableRaceGetsNothing()
    {
        var rules = SyntheticRules();
        var map = MapWith(Ground.Plain, terrain: 5, playerRace: Neutral, node: (2, 2, Resource: 2));

        Assert.Equal(Bank.Zero, Economy.DailyIncome(map, "P2", rules, Difficulty.Easy));
    }

    [Fact]
    public void DailyIncome_ClampsEveryCurrencyAt9999()
    {
        var rules = SyntheticRules();
        // 300 жил золота по 50 — больше потолка казны движка (int16 с зажимом в [0, 9999]).
        var map = MapWith(Ground.Plain, terrain: 1, nodes: Enumerable.Range(0, 300)
            .Select(i => (X: i % 30, Y: i / 30, Resource: 0)).ToList());

        Assert.Equal(Economy.CurrencyMax, Economy.DailyIncome(map, "P1", rules).Gold);
    }

    [Fact]
    public void DailyIncome_UnknownPlayerFailsLoudly()
    {
        var rules = SyntheticRules();
        var map = MapWith(Ground.Plain, terrain: 1);

        var e = Assert.Throws<ArgumentException>(() => Economy.DailyIncome(map, "P404", rules));
        Assert.Contains("P404", e.Message);
    }

    // ---------------------------------------------------------------- постройки

    [Fact]
    public void CanBuild_ReturnsEngineStatusInEngineOrder()
    {
        var rules = SyntheticRules();

        // Пусто в казне и нет требуемой постройки: движок сообщает про КАЗНУ (проверка 3 раньше 9).
        var poor = MapWith(Ground.Plain, 1, bank: new Bank(0, 0, 0, 0, 0, 0));
        Assert.Equal(BuildStatus.InsufficientBank, Economy.CanBuild(poor, "P1", "b2", rules));
        Assert.Equal(BuildStatus.NoRequiredBuilding,
            Economy.CanBuild(poor, "P1", "b2", rules, ignoreCost: true));

        var rich = MapWith(Ground.Plain, 1, bank: new Bank(9999, 0, 0, 0, 0, 0));
        Assert.Equal(BuildStatus.NoRequiredBuilding, Economy.CanBuild(rich, "P1", "b2", rules));
        Assert.Equal(BuildStatus.CanBeBuilt, Economy.CanBuild(rich, "P1", "b1", rules));
        Assert.Equal(BuildStatus.AlreadyBuiltThisDay,
            Economy.CanBuild(rich, "P1", "b1", rules, builtThisDay: true));
        Assert.Equal(BuildStatus.CanBeBuilt,
            Economy.CanBuild(rich, "P1", "b1", rules, ignoreCost: true, builtThisDay: true));
        Assert.Equal(BuildStatus.ExceedsMaxLevel,
            Economy.CanBuild(rich, "P1", "b1", rules, maxUnitLevel: 1));

        var built = MapWith(Ground.Plain, 1, bank: new Bank(9999, 0, 0, 0, 0, 0),
            buildings: new[] { "b1" });
        Assert.Equal(BuildStatus.AlreadyBuilt, Economy.CanBuild(built, "P1", "b1", rules));
        Assert.Equal(BuildStatus.CanBeBuilt, Economy.CanBuild(built, "P1", "b2", rules));
        // b3 — та же ветка и тот же уровень, что у построенной b1: движок не даёт строить обе.
        Assert.Equal(BuildStatus.SiblingUnitBuilding, Economy.CanBuild(built, "P1", "b3", rules));
    }

    [Fact]
    public void HireList_IsRaceUnitsUnlockedByBuildings()
    {
        var rules = SyntheticRules();
        var map = MapWith(Ground.Plain, 1);
        Assert.Equal(new[] { "u1" }, Economy.HireList(map, "P1", rules));

        var built = MapWith(Ground.Plain, 1, buildings: new[] { "b1" });
        Assert.Equal(new[] { "u1", "u2" }, Economy.HireList(built, "P1", rules));
    }

    // ---------------------------------------------------------------- настоящие карты

    /// <summary>
    /// Каждая карта <c>Exports</c> и сохранение игры: доход считается для каждого игрока, тип
    /// ресурса каждой жилы лежит в диапазоне <c>Lres.dbf</c>. Числа печатаются — их читает
    /// приёмочный прогон; молчаливого «похоже, работает» здесь нет.
    /// </summary>
    [SkippableFact]
    public void DailyIncome_RunsOnEveryRealMap()
    {
        ImportedData.RequireRules();
        Skip.IfNot(Directory.Exists(GamePaths.Exports), $"нет Exports: {GamePaths.Exports}");
        var rules = EconomyLoader.Load(ImportedData.Content);

        var files = Directory.GetFiles(GamePaths.Exports, "*.sg")
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        Skip.If(files.Count == 0, "в Exports нет ни одной карты");

        int maps = 0, players = 0, nodes = 0, paying = 0;
        foreach (var path in files)
        {
            var map = MapLoader.Load(path);
            maps++;
            foreach (var node in map.Objects.Values.Where(o => o.Kind == "CR"))
            {
                nodes++;
                Assert.InRange(node.Resource, 0, 5);
            }
            foreach (var player in map.Players.Values)
            {
                players++;
                var income = Economy.DailyIncome(map, player.Id, rules);
                if (income != Bank.Zero) paying++;
                Assert.InRange(income.Gold, 0, Economy.CurrencyMax);
            }
        }

        _out.WriteLine($"карт {maps}, игроков {players} (с доходом {paying}), жил {nodes}");
        Assert.True(nodes > 0, "ни одной жилы ни на одной карте — проверять нечего");
        Assert.True(paying > 0, "ни у одного игрока нет дохода — формула ничего не считает");
    }

    /// <summary>Сохранение игры: доход человека печатается по слагаемым. Проверяемое утверждение
    /// одно — доход не меньше расового (столица даёт его всегда), и он приходит в тех валютах,
    /// которые у расы есть.
    /// <para><b>Захваченный игрок из проверки исключён</b> (задача 14 Ф7): у него доход ноль, а не
    /// расовый — движок на дневном тике видит <c>isCaptured</c> и обнуляет казну дохода
    /// (<c>0x43AB89</c>). В сохранении, на котором тест смотрит, так и есть: <c>S143PL0002</c>
    /// захвачен, и до задачи 14 модель давала ему 100 золота, которых он в игре не получал.</para></summary>
    [SkippableFact]
    public void DailyIncome_OnSaveGame_IsAtLeastRaceIncome()
    {
        ImportedData.RequireRules();
        var save = SaveGames().FirstOrDefault();
        Skip.If(save is null, "нет сохранений игры (SaveGame/*.sg)");
        var rules = EconomyLoader.Load(ImportedData.Content);

        var map = MapLoader.Load(save!);
        foreach (var player in map.Players.Values.Where(p => rules.Races[p.RaceId].Playable))
        {
            if (player.CapturedBy.Length > 0)
            {
                _out.WriteLine($"{player.Id} захвачен игроком {player.CapturedBy} — доход ноль, " +
                               "расовое утверждение к нему не применяется");
                Assert.Equal(Bank.Zero, Economy.DailyIncome(map, player.Id, rules));
                continue;
            }
            var race = rules.Races[player.RaceId];
            var own = map.Objects.Values.Count(o => o.Kind == "CR"
                && map.Contains(o.X, o.Y)
                && map.At(o.X, o.Y).Terrain == race.Terrain
                && map.At(o.X, o.Y).Ground is Ground.Plain or Ground.Forest);
            var income = Economy.DailyIncome(map, player.Id, rules);
            _out.WriteLine($"{player.Id} раса {player.RaceId} свои жилы {own} " +
                           $"доход {income.Gold}/{income.Infernal},{income.Life},{income.Death}," +
                           $"{income.Runic},{income.Grove} казна {player.Bank.Gold}");
            Assert.True(income.Gold >= race.Income.Gold,
                $"{player.Id}: доход {income.Gold} меньше расового {race.Income.Gold}");
        }
    }

    private static IEnumerable<string> SaveGames()
    {
        // Сохранения лежат не в той копии игры, на которую смотрит GamePaths (game-pristine
        // чистая) — та же развилка, что в MapLoadTests.
        var roots = new[]
        {
            Path.Combine(GamePaths.GameDir, "SaveGame"),
            Path.Combine(Path.GetDirectoryName(GamePaths.GameDir.TrimEnd(Path.DirectorySeparatorChar)) ?? ".",
                "game-dev", "SaveGame"),
        };
        return roots.Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.sg"))
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------- синтетика

    /// <summary>Крошечные справочники: Империя (100 золота + 25 маны жизни), нейтралы,
    /// три постройки одной ветки и два юнита. Числа взяты из ванильных <c>.dbf</c>, но таблицы
    /// здесь свои — тест на формулу не должен зависеть от полноты игры на диске.</summary>
    private static EconomyRules SyntheticRules()
    {
        var races = new Dictionary<string, RaceInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [Empire] = new(Empire, new Bank(100, 0, 25, 0, 0, 0), 4, 35, "", 0, true) { Soldiers = ["u1", "u2"] },
            [Neutral] = new(Neutral, new Bank(100, 0, 0, 0, 0, 0), 0, 0, "", 4, false),
        };
        var buildings = new Dictionary<string, BuildingType>(StringComparer.OrdinalIgnoreCase)
        {
            ["b1"] = new("b1", "", new Bank(200, 0, 0, 0, 0, 0), 2, Economy.UnitBuildingCategory, 0),
            ["b2"] = new("b2", "b1", new Bank(500, 0, 0, 0, 0, 0), 3, Economy.UnitBuildingCategory, 0),
            ["b3"] = new("b3", "", new Bank(200, 0, 0, 0, 0, 0), 2, Economy.UnitBuildingCategory, 0),
        };
        var hire = new Dictionary<string, UnitHire>(StringComparer.OrdinalIgnoreCase)
        {
            ["u1"] = new("u1", Empire, new Bank(50, 0, 0, 0, 0, 0), "", 1),
            ["u2"] = new("u2", Empire, new Bank(300, 0, 0, 0, 0, 0), "b1", 2),
            ["u3"] = new("u3", Neutral, new Bank(50, 0, 0, 0, 0, 0), "", 1),
        };
        return new EconomyRules(races, Array.Empty<CityInfo>(), buildings, hire,
            IncomeBonusPercent: new[] { 30, 0, 0, 0 }, CrystalProfit: 50, SellRatio: 20,
            ConstructionUrgency: 0);
    }

    private static MapState MapWith(
        Ground ground, int terrain, (int X, int Y, int Resource)? node = null,
        IReadOnlyList<(int X, int Y, int Resource)>? nodes = null,
        string playerRace = Empire, Bank? bank = null, IReadOnlyList<string>? buildings = null)
    {
        const int size = 30;
        var tiles = new Tile[size, size];
        // Байт клетки: биты 0-2 — террейн (Lterrain 1..6), биты 3-5 — грунт (Lground).
        var raw = (byte)((terrain & 0b111) | ((int)ground << 3));
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                tiles[x, y] = new Tile(raw);

        var all = nodes?.ToList() ?? new List<(int X, int Y, int Resource)>();
        if (node is not null) all.Add(node.Value);
        var objects = all
            .Select((n, i) => new MapObject
            {
                Id = $"S001CR{i:0000}", Kind = "CR", X = n.X, Y = n.Y, Resource = n.Resource,
            })
            .ToDictionary(o => o.Id, o => o, StringComparer.OrdinalIgnoreCase);

        var player = new MapPlayer
        {
            Id = playerRace == Empire ? "P1" : "P2",
            RaceId = playerRace,
            LordId = "",
            IsHuman = true,
            Bank = bank ?? Bank.Zero,
            FogId = "",
            BuildingsId = "",
            Buildings = buildings ?? Array.Empty<string>(),
        };

        return new MapState
        {
            Size = size,
            Tiles = tiles,
            Stacks = new Dictionary<string, MapStack>(),
            Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(StringComparer.OrdinalIgnoreCase)
                { [player.Id] = player },
            Objects = objects,
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(),
            Turn = 1,
        };
    }
}
