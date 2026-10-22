using LastEdict.Rules.Map;

/// <summary>
/// Правило роста земли (<see cref="LandModel"/>, <see cref="LandWalk"/>) на синтетике — без журнала, без
/// игры и без справочников: скорость, порядок обхода движка поштучно, непроходимость воды и гор,
/// связность, квадрат жилы, квота деревни по дням, отказ своих жезлов и укреплений.
/// </summary>
public class LandTests
{
    private const int OwnTerrain = 1;      // Lterrain L_HU
    private const int OtherTerrain = 5;    // L_NE, нейтральная земля

    [Fact] public void Growth_TakesTenCellsPerDay()
    {
        var land = LandModel.Build(Map(21, capital: 1), OwnTerrain);

        Assert.Equal(1, land.OwnedCells(1));
        Assert.Equal(11, land.OwnedCells(2));
        Assert.Equal(21, land.OwnedCells(3));
        Assert.Equal(1 + 10 * 9, land.OwnedCells(10));
    }

    /// <summary>§3 п. 7: день 2 — кольцо 1 и ровно те шесть клеток кольца 2, что берёт куча движка;
    /// (12,10) и (10,12) остаются на завтра. Множество точное: «уверенно» = «возможно».</summary>
    [Fact] public void Growth_TakesTheEngineCellsInsideTheRing()
    {
        var land = LandModel.Build(Map(21, capital: 1), OwnTerrain);

        (int X, int Y)[] expected = [(10, 10), (10, 11), (11, 10), (9, 10), (10, 9),
                                     (8, 10), (11, 11), (9, 9), (11, 9), (9, 11), (10, 8)];
        Assert.Equal(expected.Order(), land.Certain(2).Order());
        Assert.Equal(land.Certain(2), land.Possible(2));
        Assert.DoesNotContain((12, 10), land.Certain(2));
        Assert.Equal((10, 10), land.Centre);
    }

    /// <summary>
    /// §3 п. 7 закрыт: порядок обхода — куча движка (<see cref="LandWalk"/>), а не функция смещения.
    /// Эталон — лестница изолированного жезла <c>m6-rods</c> (две клетки в сутки, кольца 1…3 сняты
    /// поштучно, <c>task7-land.md</c> §3); внутри суток пара сверяется множеством — сохранение порядка
    /// двух клеток дня не несёт.
    /// </summary>
    [Fact] public void Walk_ReproducesTheRodLadderOfM6()
    {
        (int Dx, int Dy)[] ladder =
        [
            (0, 1), (1, 0), (-1, 0), (0, -1),
            (-2, 0), (1, 1), (-1, -1), (1, -1), (-1, 1), (0, -2), (0, 2), (2, 0),
            (-3, 0), (2, -1), (2, 1), (3, 0), (0, 3), (1, 2), (-2, -1), (-1, -2), (-1, 2), (1, -2), (-2, 1), (0, -3),
        ];
        var owned = new HashSet<(int X, int Y)> { (20, 20) };
        for (int day = 0; day < ladder.Length / 2; day++)
        {
            var got = LandWalk.Walk(40, (20, 20), 2, owned.Contains, _ => false);
            var want = ladder.Skip(2 * day).Take(2).Select(d => (20 + d.Dx, 20 + d.Dy));
            Assert.True(want.ToHashSet().SetEquals(got), $"сутки {day + 1}: {string.Join(" ", got)}");
            owned.UnionWith(got);
        }
    }

    /// <summary>Обход идёт СКВОЗЬ свою землю, не считая её, и не расширяется через отвергнутую клетку
    /// (<c>0x5e0b40</c>, <c>0x5e0b4f</c>).</summary>
    [Fact] public void Walk_ExpandsThroughOwnCells_ButNotThroughRejected()
    {
        var own = new HashSet<(int X, int Y)> { (5, 5), (5, 6), (5, 7) };
        var got = LandWalk.Walk(11, (5, 5), 1, own.Contains, c => c.X != 5 || c.Y < 5);
        Assert.Equal([(5, 8)], got);        // открыт только столбец вниз: (5,6) и (5,7) свои — дальше
        Assert.Equal([(5, 6)], LandWalk.Walk(11, (5, 5), 1, _ => false, c => c.X != 5 || c.Y < 5));
    }

    [Fact] public void Growth_NeverCrossesWater_AndKnowsWhatIsUnreachable()
    {
        // Стена воды по строке 12 отрезает нижнюю половину карты целиком.
        var land = LandModel.Build(Map(21, capital: 1, water: y => y == 12), OwnTerrain);

        int aboveWall = 21 * 12 - 1;                            // строки 0..11, минус сама столица
        Assert.Equal(aboveWall, land.Reachable);
        var all = land.Possible(1000);
        Assert.DoesNotContain((10, 12), all);                   // сама вода
        Assert.DoesNotContain((10, 13), all);                   // и всё, что за ней
        Assert.Contains((10, 11), all);
    }

    [Fact] public void Growth_StopsWhenThereIsNothingLeft()
    {
        var land = LandModel.Build(Map(5, capital: 1), OwnTerrain);

        Assert.Equal(25, land.OwnedCells(100));                 // вся карта 5x5 и ни клеткой больше
        Assert.Equal(24, land.Reachable);
    }

    [Fact] public void Node_ClaimsItsThreeByThree_WhenGrowthReachesHalfOfItsRing()
    {
        // Жила в (10,14) — четыре клетки от центра (10,10); ближняя клетка её квадрата (10,13)
        // стоит на кольце 3 (12 клеток). Кольца 1..2 — это 12 клеток, то есть к концу дня 2
        // занято 10, к концу дня 3 — 20, из них 8 в кольце 3: половина (6) уже пройдена.
        var land = LandModel.Build(Map(21, capital: 1, crystal: (10, 14)), OwnTerrain);

        Assert.Equal(3, land.NodeCaptureDay((10, 14)));
        Assert.DoesNotContain((10, 14), land.Certain(2));
        var certain = land.Certain(3);
        for (int x = 9; x <= 11; x++)
            for (int y = 13; y <= 15; y++)
                Assert.Contains((x, y), certain);
        // Квадрат достаётся сверх дневных десяти — ровно так же, как на ходе 36 прогона.
        Assert.Equal(land.OwnedCells(2) + 10 + 8, land.OwnedCells(3));
    }

    // ------------------------------------------------------------------ задача 7: источники

    /// <summary>Квоты источников складываются, а в день появления источник даёт свои клетки и ОДИН
    /// обход вместо суточного (описание <see cref="LandModel"/>, «Приобретение источника»).</summary>
    [Fact] public void Growth_AddsQuotaOfEverySource_AndOneWalkOnTheDayOfAcquisition()
    {
        // Столица — одна клетка в центре 31×31, жезл ставится на третий день в (25,25).
        var rod = new LandSource("RD", (25, 25), LandQuotas.Vanilla.RodRange, [(25, 25)], Since: 3);
        var land = LandModel.Build(Map(31, capital: 1), OwnTerrain, [rod]);

        Assert.Equal(1, land.OwnedCells(1));            // старт
        Assert.Equal(11, land.OwnedCells(2));           // только столица: +10
        Assert.Equal(11 + 10 + 3, land.OwnedCells(3));  // столица +10, жезл 1 + 2 разовым обходом
        Assert.Equal(24 + 12, land.OwnedCells(4));      // и дальше 10 + 2 в сутки
        Assert.Equal(36 + 12, land.OwnedCells(5));
    }

    /// <summary>Снесённый жезл перестаёт давать квоту, а занятая им земля не возвращается
    /// («земля замерзает», прогон 2 <c>m6-rods</c>).</summary>
    [Fact] public void Growth_StopsAtUntil_ButKeepsTheLand()
    {
        var rod = new LandSource("RD", (25, 25), LandQuotas.Vanilla.RodRange, [(25, 25)], Until: 3);
        var land = LandModel.Build(Map(31, capital: 1), OwnTerrain, [rod]);

        Assert.Equal(1 + 12, land.OwnedCells(2));       // 10 + 2
        Assert.Equal(13 + 12, land.OwnedCells(3));      // последний день жезла
        Assert.Equal(25 + 10, land.OwnedCells(4));      // жезла нет, земля осталась
    }

    /// <summary>
    /// Разовый обход приобретения деревни — её клетки (4×4 = 16) плюс обход на её квоту, и ни
    /// очком расстояния больше. Числа и кольца — ровно наблюдение <c>m7-land</c>: 31 клетка,
    /// <c>d0:1 d1:4 d2:8 d3:12 d4:6</c>. Отвергнутая модель «<c>T_CAPTURE</c> = 100 очков
    /// расстояния» дала бы здесь 36 клеток.
    /// </summary>
    [Fact] public void Acquisition_IsOwnCellsPlusOneWalkOfItsQuota()
    {
        var map = Map(31, capital: 1);
        var own = new List<(int X, int Y)>();
        for (int x = 10; x <= 13; x++)
            for (int y = 10; y <= 13; y++) own.Add((x, y));
        var village = new LandSource("VI", LandModel.FortCentre(10, 10),
                                     LandQuotas.Vanilla.Village(1), own, Since: 2);

        var cells = LandModel.AcquisitionCells(map, new HashSet<(int X, int Y)> { (15, 15) }, village,
                                               LandQuotas.Vanilla);

        Assert.Equal((12, 12), village.Centre);
        Assert.Equal(16 + 15, cells.Count);
        var byRing = cells.GroupBy(c => LandModel.Distance(village.Centre, c))
                          .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<int, int> { [0] = 1, [1] = 4, [2] = 8, [3] = 12, [4] = 6 }, byRing);
    }

    /// <summary>§3 п. 23: квота деревни — по уровню в день обхода. Столица (+10) и деревня 1-го уровня
    /// (+15), с 4-го дня деревня 3-го уровня (+20).</summary>
    [Fact] public void VillageQuota_StepsByDay()
    {
        var own = new List<(int X, int Y)>();
        for (int x = 30; x <= 33; x++)
            for (int y = 30; y <= 33; y++) own.Add((x, y));
        var village = new LandSource("VI", LandModel.FortCentre(30, 30), 15, own, Steps: [(4, 20)]);
        var capital = new LandSource("", (20, 20), LandQuotas.Vanilla.Capital, [(20, 20)]);
        var map = Map(41, capital: 1, extraOwn: own);
        var land = LandModel.BuildSources(map, OwnTerrain, [capital, village], LandQuotas.Vanilla);

        Assert.Equal(15, village.QuotaOn(3));
        Assert.Equal(20, village.QuotaOn(4));
        Assert.Equal(new[] { 17, 42, 67, 97, 127 }, Enumerable.Range(1, 5).Select(land.OwnedCells));
    }

    /// <summary>Приём клетки отвергает СВОЙ жезл и СВОЁ укрепление наравне с чужими (<c>0x5e07b2</c>:
    /// сравнивается объект, а не владелец); нейтральное укрепление не отвергается.</summary>
    [Fact] public void BlockedCells_TakeOwnRodsAndForts_ButNotNeutral()
    {
        var baseMap = Map(9, capital: 1);
        var map = new MapState
        {
            Size = baseMap.Size, Tiles = baseMap.Tiles, Stacks = baseMap.Stacks, Units = baseMap.Units,
            Fog = baseMap.Fog, Turn = 1, Objects = baseMap.Objects,
            Players = new Dictionary<string, MapPlayer>
            {
                ["S001PL0001"] = Player("S001PL0001", "G000RR0000"),
                ["S001PL0009"] = Player("S001PL0009", "G000RR0004"),
            },
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>
            {
                [(1, 1)] = ["S001FT0001"], [(7, 7)] = ["S001FT0002"], [(3, 3)] = ["S001RD0000"],
            },
            Rods = new Dictionary<string, MapRod> { ["S001RD0000"] = new("S001RD0000", "S001PL0001", 3, 3) },
        };
        MapSite[] sites =
        [
            new() { Id = "S001FT0001", Kind = SiteKind.Village, RawKind = "MidVillage", X = 1, Y = 1, Owner = "S001PL0001" },
            new() { Id = "S001FT0002", Kind = SiteKind.Village, RawKind = "MidVillage", X = 7, Y = 7, Owner = "S001PL0009" },
        ];

        var blocked = LandModel.BlockedCells(map, sites);

        Assert.Equal([(1, 1), (3, 3)], blocked.Order());
    }

    /// <summary><c>T_CAPTURE</c>: целое деление первым (<c>0x4341a9…0x4341af</c>).</summary>
    [Fact] public void Acquisition_DividesCapturePercentFirst()
    {
        Assert.Equal(20, (LandQuotas.Vanilla with { CapturePercent = 150 }).Acquisition(20));
        Assert.Equal(40, (LandQuotas.Vanilla with { CapturePercent = 299 }).Acquisition(20));
        Assert.Equal(0, (LandQuotas.Vanilla with { CapturePercent = 50 }).Acquisition(20));
    }

    /// <summary>Уровень деревни двигает квоту: <c>T_CITY</c> 15/15/20/20/25.</summary>
    [Fact] public void VillageQuota_FollowsItsLevel()
    {
        var q = LandQuotas.Vanilla;
        Assert.Equal(new[] { 15, 15, 20, 20, 25 }, Enumerable.Range(1, 5).Select(q.Village));
        Assert.Equal(15, q.Village(0));          // уровня вне 1..5 не бывает — зажимается
        Assert.Equal(25, q.Village(9));
        Assert.Equal(20, q.Acquisition(20));     // T_CAPTURE = 100 % от квоты
    }

    [Fact] public void Build_Throws_WhenPlayerHasNoLandOnTheMap()
    {
        var ex = Assert.Throws<ArgumentException>(() => LandModel.Build(Map(9, capital: 0), OwnTerrain));
        Assert.Contains("нет своей земли", ex.Message);
    }

    [Fact] public void OwnedCells_Throws_OnDayZero()
    {
        var land = LandModel.Build(Map(9, capital: 1), OwnTerrain);
        Assert.Throws<ArgumentOutOfRangeException>(() => land.OwnedCells(0));
    }

    /// <summary>ai-q25 (ранг 1, 4/0): рост земли отвергает клетки ландмарка с <c>GLmark.MOUNTAIN = T</c>
    /// — Кладбище (категория 0) и Плато (3); Водоворот и Лавовая яма (F, категория 3) берутся.
    /// Категория ландмарка ни при чём.</summary>
    [Fact] public void BlockedCells_TakeMountainLandmarks_NotCategory()
    {
        var landmarks = new (string Id, string Type, int X, int Y)[]
        {
            ("S143MM0007", "G000MG0133", 1, 1),   // Кладбище: T, категория 0
            ("S143MM0009", "G000MG0006", 3, 1),   // Плато: T, категория 3
            ("S143MM0008", "G000MG0073", 5, 1),   // Водоворот: F, категория 3
            ("S143MM0057", "G000MG0022", 7, 1),   // Лавовая яма: F, категория 3
        };
        var baseMap = Map(9, capital: 1);
        var map = new MapState
        {
            Size = baseMap.Size, Tiles = baseMap.Tiles, Stacks = baseMap.Stacks, Units = baseMap.Units,
            Players = baseMap.Players, Fog = baseMap.Fog, Turn = 1,
            Objects = landmarks.ToDictionary(l => l.Id,
                l => new MapObject { Id = l.Id, Kind = "MM", TypeId = l.Type, X = l.X, Y = l.Y }),
            Plan = landmarks.ToDictionary(l => (l.X, l.Y), l => (IReadOnlyList<string>)[l.Id]),
        };
        var mountains = new HashSet<string>(["g000mg0133", "g000mg0006"], StringComparer.OrdinalIgnoreCase);

        var blocked = LandModel.BlockedCells(map, [], mountains);

        Assert.Equal([(1, 1), (3, 1)], blocked.Order());
        Assert.Empty(LandModel.BlockedCells(map, []));
    }

    // ------------------------------------------------------------------ синтетическая карта

    /// <summary>Карта <paramref name="size"/>×<paramref name="size"/> из равнины: в центре пятно
    /// своей земли стороной <paramref name="capital"/>, остальное — нейтральная земля.</summary>
    private static MapState Map(int size, int capital, Func<int, bool>? water = null,
                                (int X, int Y)? crystal = null,
                                IReadOnlyCollection<(int X, int Y)>? extraOwn = null)
    {
        var tiles = new Tile[size, size];
        int c = size / 2, half = capital / 2;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                var ground = water is not null && water(y) ? Ground.Water : Ground.Plain;
                bool mine = capital > 0 && Math.Abs(x - c) <= half && Math.Abs(y - c) <= half
                            || extraOwn is not null && extraOwn.Contains((x, y));
                tiles[x, y] = new Tile((byte)(((int)ground << 3) | (mine ? OwnTerrain : OtherTerrain)));
            }

        var objects = new Dictionary<string, MapObject>(StringComparer.OrdinalIgnoreCase);
        if (crystal is { } cr)
            objects["S001CR0000"] = new MapObject { Id = "S001CR0000", Kind = "CR", X = cr.X, Y = cr.Y };

        return new MapState
        {
            Size = size,
            Tiles = tiles,
            Stacks = new Dictionary<string, MapStack>(),
            Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(),
            Objects = objects,
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(),
            Turn = 1,
        };
    }

    private static MapPlayer Player(string id, string race) => new()
    {
        Id = id, RaceId = race, LordId = "", Bank = Bank.Zero, FogId = "", BuildingsId = "", Buildings = [],
    };
}
