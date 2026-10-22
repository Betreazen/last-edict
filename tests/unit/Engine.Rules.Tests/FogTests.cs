using LastEdict.Data;
using LastEdict.Data.Dbf;
using LastEdict.Data.Map;
using LastEdict.Data.Sg;
using LastEdict.Rules.Map;
using Xunit.Abstractions;
using KSg = Kaitai.Sg;

namespace LastEdict.Rules.Tests;

public class FogTests
{
    private readonly ITestOutputHelper _out;

    public FogTests(ITestOutputHelper output) => _out = output;

    // ---------------------------------------------------------------- пятно обзора

    [Theory]
    [InlineData(FogShape.Square, 1, 9)]
    [InlineData(FogShape.Diamond, 1, 5)]
    [InlineData(FogShape.Circle, 1, 5)]
    [InlineData(FogShape.Square, 2, 25)]
    [InlineData(FogShape.Diamond, 2, 13)]
    [InlineData(FogShape.Circle, 2, 13)]   // при r=2 круг и ромб — одно и то же множество
    [InlineData(FogShape.Square, 3, 49)]
    [InlineData(FogShape.Diamond, 3, 25)]
    [InlineData(FogShape.Circle, 3, 29)]   // а при r=3 уже расходятся
    public void Reveal_OpensExpectedCellCount(FogShape shape, int radius, int expected)
    {
        var fog = Fog.Empty(21);
        Fog.Reveal(fog, 10, 10, radius, shape);

        Assert.Equal(expected, Opened(fog));
    }

    [Fact]
    public void Reveal_ClampsToMapEdges()
    {
        var fog = Fog.Empty(5);
        Fog.Reveal(fog, 0, 0, 2, FogShape.Square);   // из девяти клеток пятна на карте девять

        Assert.Equal(9, Opened(fog));
        Assert.False(fog[0, 0]);
        Assert.True(fog[3, 0]);
    }

    /// <summary><c>0x5d4dfc</c>: угол квадрата обзора, который сам угол карты, остаётся (сохранение оригинала карты 12 t01-028: (0,0)
    /// открыта от столицы в (6,6) с обзором 6), прочие три угла — нет; посреди карты — 49 − 4 клетки.</summary>
    [Fact]
    public void SquareNoCorners_KeepsOnlyCornerOnMapCorner()
    {
        var fog = Fog.Empty(144);
        Fog.Reveal(fog, 6, 6, 6, FogShape.SquareNoCorners);
        Assert.False(fog[0, 0]);
        Assert.True(fog[12, 0]);
        Assert.True(fog[0, 12]);
        Assert.True(fog[12, 12]);
        Assert.Equal(169 - 3, Opened(fog));

        var middle = Fog.Empty(21);
        Fog.Reveal(middle, 10, 10, 3, FogShape.SquareNoCorners);
        Assert.Equal(49 - 4, Opened(middle));

        var edge = Fog.Empty(21);   // у левого края, но не в углу карты: угол (0,2) снят
        Fog.Reveal(edge, 3, 5, 3, FogShape.SquareNoCorners);
        Assert.True(edge[0, 2]);
        Assert.False(edge[0, 3]);
        Assert.Equal(49 - 4, Opened(edge));

        var far = Fog.Empty(144);   // правый нижний угол карты
        Fog.Reveal(far, 137, 137, 6, FogShape.SquareNoCorners);
        Assert.False(far[143, 143]);
        Assert.True(far[131, 143]);
    }

    /// <summary><c>0x5d4e2a</c>: при радиусе ≥ size/2 квадрат зажимается в карту, и углы снимаются у зажатого прямоугольника.</summary>
    [Fact]
    public void SquareNoCorners_BigRadius_ClampsThenCutsCorners()
    {
        var fog = Fog.Empty(8);
        Fog.Reveal(fog, 1, 1, 4, FogShape.SquareNoCorners);   // [0, 6) × [0, 6): (0,0) — угол карты, остаётся; (5,0), (0,5), (5,5) сняты
        Assert.False(fog[0, 0]);
        Assert.True(fog[5, 0]);
        Assert.True(fog[0, 5]);
        Assert.True(fog[5, 5]);
        Assert.Equal(36 - 3, Opened(fog));
    }

    [Fact]
    public void Empty_HidesEverything()
    {
        var fog = Fog.Empty(4);
        Assert.Equal(0, Opened(fog));
    }

    // ---------------------------------------------------------------- сверка масок

    [Fact]
    public void Compare_CountsBothKindsOfDisagreement()
    {
        var ours = Fog.Empty(3);
        var theirs = Fog.Empty(3);
        Fog.Reveal(ours, 0, 0, 0, FogShape.Square);      // лишняя у нас
        Fog.Reveal(theirs, 2, 2, 0, FogShape.Square);    // недостающая у нас
        Fog.Reveal(ours, 1, 1, 0, FogShape.Square);      // общая
        Fog.Reveal(theirs, 1, 1, 0, FogShape.Square);

        var (total, same, onlyOurs, onlyTheirs) = Fog.Compare(ours, theirs);

        Assert.Equal(9, total);
        Assert.Equal(7, same);
        Assert.Equal(1, onlyOurs);
        Assert.Equal(1, onlyTheirs);
        Assert.Equal(total, same + onlyOurs + onlyTheirs);
    }

    [Fact]
    public void Compare_RejectsDifferentSizes() =>
        Assert.Throws<ArgumentException>(() => Fog.Compare(Fog.Empty(3), Fog.Empty(4)));

    [Fact]
    public void Components_CountsSeparateBlobs()
    {
        var fog = Fog.Empty(9);
        Assert.Equal(0, Components(fog));

        Fog.Reveal(fog, 1, 1, 1, FogShape.Square);
        Assert.Equal(1, Components(fog));

        Fog.Reveal(fog, 7, 7, 1, FogShape.Square);   // второе пятно, не касается первого
        Assert.Equal(2, Components(fog));
        // Оба пятна прижаты к краю карты, поэтому смена только с одной стороны: три строки и
        // три столбца на пятно, всего 12.
        Assert.Equal(12, Transitions(fog));
    }

    // ---------------------------------------------------------------- пересчёт по карте

    [SkippableFact]
    public void Recompute_UnitesStackAndCityRadii()
    {
        var scenario = FindScenario();
        Skip.If(scenario is null, "нет ни одного сценария в Exports");
        var map = MapLoader.Load(scenario!);
        var stack = map.Stacks.Values.First();
        var scout = ScoutByType();

        var alone = Fog.Recompute(map, stack.Owner, scout, FogShape.Square, radiusOverride: 1);
        var withCity = Fog.Recompute(map, stack.Owner, scout, FogShape.Square, radiusOverride: 1,
                                     extra: [(X: 0, Y: 0, Radius: 1)]);

        _out.WriteLine($"открыто отрядами {Opened(alone)}, с добавленным глазом {Opened(withCity)}");
        Assert.True(Opened(alone) > 0, "ни один отряд игрока не открыл ни клетки");
        Assert.True(Opened(withCity) > Opened(alone), "лишний глаз не добавил ни клетки");
        // Объединение, а не перезапись: всё, что открыл отряд, осталось открытым.
        for (int y = 0; y < map.Size; y++)
            for (int x = 0; x < map.Size; x++)
                if (!alone[x, y])
                    Assert.False(withCity[x, y]);
    }

    [SkippableFact]
    public void Radius_OfHeroStackIsScoutFromGunits()
    {
        var scenario = FindScenario();
        Skip.If(scenario is null, "нет ни одного сценария в Exports");
        var map = MapLoader.Load(scenario!);
        var scout = ScoutByType();

        var hero = map.Stacks.Values.FirstOrDefault(
            s => map.Units.TryGetValue(s.LeaderId, out var u) && scout.ContainsKey(u.TypeId));
        Skip.If(hero is null, "в сценарии нет отряда с известным типом лидера");

        int leaderScout = Fog.Radius(hero!, map, scout);
        _out.WriteLine($"отряд {hero!.Id}: лидер {map.Units[hero.LeaderId].TypeId}, " +
                       $"SCOUT лидера {leaderScout}, максимум по отряду {Fog.MaxRadius(hero, map, scout)}");
        Assert.Equal(scout[map.Units[hero.LeaderId].TypeId], leaderScout);
        Assert.InRange(leaderScout, 1, 12);
    }

    // ---------------------------------------------------------------- измерение по сохранению

    /// <summary>
    /// Выбор формы и радиуса числами, а не на глаз: все три формы против каждого правдоподобного
    /// радиуса, эталон — туман человека из сохранения игры. Тест печатает всю таблицу и не выбирает
    /// молча; порог здесь не ставится — его берёт харнесс задачи 9 (сверка по маршруту).
    /// </summary>
    [SkippableFact]
    public void FogShape_And_Radius_MeasuredAgainstTheSave()
    {
        var savePath = FindSaveGame();
        Skip.If(savePath is null, "нет сохранения игры");
        var save = MapLoader.Load(savePath!);
        var scout = ScoutByType();
        Skip.If(scout.Count == 0, "нет Globals/Gunits.dbf — не из чего взять SCOUT");

        var human = save.Players.Values.First(p => p.IsHuman);
        var theirs = save.Fog[human.FogId];
        int total = save.Size * save.Size, openedInSave = Opened(theirs);
        _out.WriteLine($"{Path.GetFileName(savePath)}: день {save.Turn}, размер {save.Size}, " +
                       $"человек {human.Id} ({human.RaceId}); в сохранении открыто {openedInSave} " +
                       $"из {total} ({100.0 * openedInSave / total:F2} %)");

        // Чей SCOUT берётся — лидера или максимум по отряду: печатаем оба по каждому отряду.
        var stacks = save.Stacks.Values
            .Where(s => s.Owner.Equals(human.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var s in stacks)
            _out.WriteLine($"  отряд {s.Id} в ({s.X},{s.Y}) внутри \"{s.Inside}\": лидер " +
                           $"{(save.Units.TryGetValue(s.LeaderId, out var u) ? u.TypeId : "?")}, " +
                           $"SCOUT лидера {Fog.Radius(s, save, scout)}, максимум по отряду " +
                           $"{Fog.MaxRadius(s, save, scout)}");
        int leaderSum = stacks.Sum(s => Fog.Radius(s, save, scout));
        int maxSum = stacks.Sum(s => Fog.MaxRadius(s, save, scout));
        _out.WriteLine(leaderSum == maxSum
            ? "SCOUT лидера и максимум по отряду совпали на всех отрядах — данные их не различают"
            : $"SCOUT лидера и максимум по отряду расходятся (суммы {leaderSum} и {maxSum})");

        var cities = CitiesOf(savePath!, human.Id);
        _out.WriteLine($"городов человека {cities.Count}: " +
                       string.Join(", ", cities.Select(c => $"({c.X},{c.Y})")));

        // Своя строка на каждую пару (форма, радиус); лучшая — по числу совпавших клеток.
        (FogShape Shape, int Radius, int Same, int OnlyOurs, int OnlyTheirs) best = default;
        var tightest = new Dictionary<FogShape, int>();   // наибольший радиус без единой лишней клетки
        _out.WriteLine("форма    радиус  совпало       %   лишних у нас  недостающих");
        foreach (var shape in Enum.GetValues<FogShape>())
            for (int radius = 1; radius <= 10; radius++)
            {
                var ours = Fog.Recompute(save, human.Id, scout, shape, radius,
                                         cities.Select(c => (c.X, c.Y, radius)));
                var (cells, same, onlyOurs, onlyTheirs) = Fog.Compare(ours, theirs);
                _out.WriteLine($"{shape,-8} {radius,6} {same,7} {100.0 * same / cells,7:F2}   " +
                               $"{onlyOurs,12} {onlyTheirs,12}");
                if (same > best.Same)
                    best = (shape, radius, same, onlyOurs, onlyTheirs);
                if (onlyOurs == 0)
                    tightest[shape] = radius;
            }

        // Туман только накапливается: клетка, раз открытая, закрытой не станет. Значит любая
        // клетка, открытая нами по ТЕКУЩИМ позициям, но скрытая в сохранении, — перебор формы или
        // радиуса (глаз стоял здесь в момент сохранения), а недостающие — просто след маршрута.
        // Поэтому «лишних у нас = 0» и есть верхняя граница настоящего обзора, а процент совпадения
        // ею не является.
        int scoutRadius = stacks.Count > 0 ? Fog.Radius(stacks[0], save, scout) : 0;
        _out.WriteLine("наибольший радиус без единой лишней клетки (верхняя граница обзора): " +
                       string.Join(", ", tightest.Select(t => $"{t.Key} {t.Value}")) +
                       $"; SCOUT лидера {scoutRadius}");
        foreach (var shape in Enum.GetValues<FogShape>())
        {
            var ours = Fog.Recompute(save, human.Id, scout, shape, scoutRadius,
                                     cities.Select(c => (c.X, c.Y, scoutRadius)));
            var extras = new List<string>();
            for (int y = 0; y < save.Size; y++)
                for (int x = 0; x < save.Size; x++)
                    if (!ours[x, y] && theirs[x, y])
                    {
                        var eyes = stacks.Select(s => (s.X, s.Y)).Concat(cities);
                        var (ex, ey) = eyes.OrderBy(e => Math.Max(Math.Abs(e.X - x), Math.Abs(e.Y - y))).First();
                        extras.Add($"({x},{y}) от глаза ({ex},{ey}) dx={x - ex} dy={y - ey}");
                    }
            _out.WriteLine($"{shape} радиус {scoutRadius} (SCOUT лидера): лишних у нас {extras.Count}" +
                           (extras.Count == 0 ? "" : " — " + string.Join("; ", extras.Take(12))));
        }

        // Связность открытой области — признак, к которому счётчик «скрыто N из M» слеп.
        // Верно прочитанная маска даёт одно-два связных пятна вокруг своих глаз; зеркальное чтение
        // внутри байта рассыпает их на оторванные столбцы по границам групп из восьми колонок,
        // поэтому и компонент, и переходов открыто/скрыто становится заметно больше.
        _out.WriteLine($"эталон: связных областей открытого {Components(theirs)}, " +
                       $"переходов открыто/скрыто по строкам и столбцам {Transitions(theirs)}");

        // Картинка маски (только строки, где что-то открыто): # открыто, . скрыто, @ глаз.
        var eyeSet = stacks.Select(s => (s.X, s.Y)).Concat(cities).ToHashSet();
        for (int y = 0; y < save.Size; y++)
        {
            var row = new System.Text.StringBuilder();
            bool any = false;
            for (int x = 0; x < save.Size; x++)
            {
                if (!theirs[x, y]) any = true;
                row.Append(theirs[x, y] ? '.' : eyeSet.Contains((x, y)) ? '@' : '#');
            }
            if (any)
                _out.WriteLine($"маска {y,2} {row}");
        }

        _out.WriteLine($"лучшая пара: {best.Shape} радиус {best.Radius}, совпало {best.Same} " +
                       $"из {total} ({100.0 * best.Same / total:F2} %), лишних у нас {best.OnlyOurs}, " +
                       $"недостающих {best.OnlyTheirs}");
        // Опорные точки, без которых процент выше ничего не значит.
        var allHidden = Fog.Compare(Fog.Empty(save.Size), theirs);
        _out.WriteLine($"для сравнения: маска «всё скрыто» даёт совпадений {allHidden.Same} " +
                       $"({100.0 * allHidden.Same / total:F2} %)");
        _out.WriteLine(best.Same > allHidden.Same
            ? "пересчёт по позициям лучше пустой маски"
            : "пересчёт по позициям НЕ ЛУЧШЕ пустой маски — форма по этому сохранению не выбирается");

        Assert.Equal(total, best.Same + best.OnlyOurs + best.OnlyTheirs);
        Assert.True(best.Radius > 0, "не напечатано ни одной пары");
    }

    // ---------------------------------------------------------------- вспомогательное

    private static int Opened(bool[,] fog)
    {
        int opened = 0;
        foreach (bool hidden in fog) if (!hidden) opened++;
        return opened;
    }

    /// <summary>Число связных областей открытого (соседство по четырём сторонам).</summary>
    private static int Components(bool[,] fog)
    {
        int w = fog.GetLength(0), h = fog.GetLength(1), components = 0;
        var seen = new bool[w, h];
        for (int y0 = 0; y0 < h; y0++)
            for (int x0 = 0; x0 < w; x0++)
            {
                if (fog[x0, y0] || seen[x0, y0])
                    continue;
                components++;
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x0, y0));
                seen[x0, y0] = true;
                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && !fog[nx, ny] && !seen[nx, ny])
                        {
                            seen[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                }
            }
        return components;
    }

    /// <summary>Число смен «открыто/скрыто» вдоль всех строк и всех столбцов — мера рваности края.</summary>
    private static int Transitions(bool[,] fog)
    {
        int w = fog.GetLength(0), h = fog.GetLength(1), transitions = 0;
        for (int y = 0; y < h; y++)
            for (int x = 1; x < w; x++)
                if (fog[x, y] != fog[x - 1, y]) transitions++;
        for (int x = 0; x < w; x++)
            for (int y = 1; y < h; y++)
                if (fog[x, y] != fog[x, y - 1]) transitions++;
        return transitions;
    }

    /// <summary>Колонка <c>SCOUT</c> таблицы <c>Gunits</c> из импорта (<c>data/rules</c>); пусто, если её нет.</summary>
    private static IReadOnlyDictionary<string, int> ScoutByType()
    {
        var scout = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string tables = Path.Combine(ImportedData.Dir, "rules");
        if (!GameTables.Has(tables, "Gunits.dbf"))
            return scout;
        foreach (var row in GameTables.Read(tables, "Gunits.dbf").Rows)
            scout[row.GetString("UNIT_ID")] = row.GetInt("SCOUT");
        return scout;
    }

    /// <summary>
    /// Клетки городов игрока. Владелец города в <see cref="MapObject"/> не хранится, поэтому
    /// берётся из сырой записи <c>City</c> — единственное место, где он есть.
    /// </summary>
    private static List<(int X, int Y)> CitiesOf(string sgPath, string ownerId)
    {
        var cities = new List<(int X, int Y)>();
        foreach (var o in SgFile.Open(sgPath).Raw.Objects)
        {
            var city = o.Data switch
            {
                KSg.Capital c => c.City,
                KSg.MidVillage v => v.City,
                _ => null,
            };
            if (city is not null && city.Owner.String.Equals(ownerId, StringComparison.OrdinalIgnoreCase))
                cities.Add(((int)city.Position.X.Value, (int)city.Position.Y.Value));
        }
        return cities;
    }

    // ponytail: те же два корня, что у MapLoadTests.FindSaveGame/FindScenario — метод там приватный,
    // а задача разрешает трогать только эти два файла, так что поиск повторён, а не вынесен.
    // Потолок: при третьей копии игры это переезжает в D2_GAME_DIR или в общий helper.
    private static string? FindScenario()
    {
        if (!Directory.Exists(GamePaths.Exports))
            return null;
        string fast = Path.Combine(GamePaths.Exports, "test_fast_battle.sg");
        return File.Exists(fast) ? fast : Directory.EnumerateFiles(GamePaths.Exports, "*.sg").FirstOrDefault();
    }

    private static string? FindSaveGame()
    {
        var roots = new[]
        {
            Path.Combine(GamePaths.GameDir, "SaveGame"),
            Path.Combine(Path.GetDirectoryName(GamePaths.GameDir.TrimEnd(Path.DirectorySeparatorChar)) ?? ".",
                         "game-dev", "SaveGame"),
        };
        // Имя автосохранения кириллическое — берём любой .sg маской, а не по имени.
        return roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.sg"))
            .FirstOrDefault();
    }
}
