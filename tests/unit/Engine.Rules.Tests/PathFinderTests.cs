using LastEdict.Rules.Map;
using Xunit;

/// <summary>
/// Докуда отряд дойдёт сегодня рядом с врагом (<c>0x5cd5b2</c> + <c>0x5cda67</c>, <c>docs/map-path.md</c> §2):
/// отойти от соседнего врага можно, шагнуть в клетку, которая всё ещё рядом с ним, — нельзя; новый враг рядом с
/// клеткой останавливает на ней; чужая крепость путь не обрывает.
/// </summary>
public class PathFinderTests
{
    private const string Me = "S001PL0001";
    private const string Enemy = "S001PL0002";
    private static readonly MoveOptions Walk = new(ForbidWaterOnLand: false);

    /// <summary>Ответ пользователя 23.09: если идти не в сторону соседнего врага, идти можно.</summary>
    [Fact]
    public void StackNextToEnemy_CanWalkAway()
    {
        var map = Map(Stack("S001KC0001", Me, 5, 5), Stack("S001KC0002", Enemy, 6, 5));

        var path = Find(map, "S001KC0001", 1, 5);

        Assert.True(path.Reachable > 0, "отход от врага должен быть разрешён");
        Assert.Equal((1, 5), (path.Points[^1].X, path.Points[^1].Y));
        Assert.Equal(path.Points.Count - 1, path.Reachable);
    }

    /// <summary>Первый шаг остаётся рядом с тем же врагом — движок не шагает (<c>0x5cdba2</c>: «идти» = 0).</summary>
    [Fact]
    public void StackNextToEnemy_CannotStepAlongIt()
    {
        var map = Map(Stack("S001KC0001", Me, 5, 5), Stack("S001KC0002", Enemy, 6, 5));

        var path = Find(map, "S001KC0001", 5, 9);

        Assert.True(path.Points.Count > 2);
        Assert.True(Adjacent(path.Points[1], (6, 5)), "путь должен начинаться вдоль врага");
        Assert.Equal(0, path.Reachable);
    }

    /// <summary>Враг, которого на старте рядом не было, останавливает на первой клетке рядом с ним.</summary>
    [Fact]
    public void NewEnemyNearPath_StopsOnFirstCellNextToIt()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 1), Stack("S001KC0002", Enemy, 5, 2));

        var path = Find(map, "S001KC0001", 9, 1);

        int stop = path.Reachable;
        Assert.InRange(stop, 1, path.Points.Count - 2);
        Assert.True(Adjacent(path.Points[stop], (5, 2)));
        Assert.All(path.Points.Take(stop), p => Assert.False(Adjacent(p, (5, 2))));
    }

    /// <summary>Чужая крепость рядом путь не обрывает: <c>0x5cd1c7</c> с флагом 1 её не проверяет.</summary>
    [Fact]
    public void EnemyFortNearPath_DoesNotStop()
    {
        var map = Map([Stack("S001KC0001", Me, 1, 1)], fort: (5, 2, Enemy));

        var path = Find(map, "S001KC0001", 9, 1);

        Assert.Contains(path.Points, p => Adjacent(p, (5, 2)));
        Assert.Equal(path.Points.Count - 1, path.Reachable);
    }

    /// <summary>Отряд внутри крепости (визитёр) соседом-помехой не считается (<c>0x5deb24</c>).</summary>
    [Fact]
    public void EnemyInsideFort_DoesNotStop()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 1), Stack("S001KC0002", Enemy, 5, 2) with { Inside = "S001FT0009" });

        var path = Find(map, "S001KC0001", 9, 1);

        Assert.Equal(path.Points.Count - 1, path.Reachable);
    }

    /// <summary>Выход из ворот бесплатен (<c>0x5cc7cf</c>/<c>0x5cca05</c>): у отряда в крепости первый шаг за входом
    /// стоит 0, дальше — цена клеток; у отряда в поле первый шаг оплачивается.</summary>
    [Fact]
    public void StackInsideFort_FirstStepFree()
    {
        var inside = Stack("S001KC0001", Me, 5, 5) with { Inside = "S001FT0001" };
        var map = Map([inside], fort: (5, 5, Me));

        var fromFort = Find(map, "S001KC0001", 9, 5);
        var fromField = Find(Map(Stack("S001KC0001", Me, 5, 5)), "S001KC0001", 9, 5);

        Assert.Equal(0, fromFort.Points[1].Cost);
        Assert.Equal(fromField.Points[2].Cost - fromField.Points[1].Cost, fromFort.Points[2].Cost);
        Assert.True(fromField.Points[1].Cost > 0);
    }

    // ------------------------------------------------------------------ маски ИИ (0x5ccac4)

    /// <summary>Путь ИИ обходит клетку чужого отряда и восемь её соседей (маска «нельзя»), игрок идёт вплотную.</summary>
    [Fact]
    public void AiPath_AvoidsZoneAroundEnemy()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 5), Stack("S001KC0002", Enemy, 5, 5));

        var player = Find(map, "S001KC0001", 9, 5);
        var ai = FindAi(map, "S001KC0001", 9, 5, mode: 1);

        Assert.Contains(player.Points, p => Adjacent(p, (5, 5)));
        Assert.True(ai.Points.Count > 0);
        Assert.DoesNotContain(ai.Points, p => Adjacent(p, (5, 5)));
    }

    /// <summary>Цель — сам враг: соседи ближе 2 к цели открыты (<c>0x5ccf0c</c>), путь доходит до соседней клетки.</summary>
    [Fact]
    public void AiPath_ToEnemy_EndsNextToIt()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 5), Stack("S001KC0002", Enemy, 6, 5));

        var path = FindAi(map, "S001KC0001", 6, 5, mode: 2);

        Assert.True(Adjacent(path.Points[^1], (6, 5)));
        Assert.NotEqual((6, 5), (path.Points[^1].X, path.Points[^1].Y));
    }

    /// <summary>
    /// Мешок-цель у ИИ (<c>a8</c> = 1, мешки в маске «можно»). Листинг 30.09:
    /// <code>
    /// 0x5cc958  cmp byte [ebp-0x10], 0     ; цель проходима при нулевых масках (0x5cca55)?
    /// 0x5cc97a  test [a7 + бит цели], dl    ; нет — бит a7 у цели?
    /// 0x5cc97f  mov byte [ebp-0x10], 1      ; есть — цель «проходима», pop_back в сборке списка (0x680b05 → 0x45af6f) не делается
    /// 0x5cd8b3  call 0x5cda67               ; «идти» по точке p; мешок в p — «идти» = 0
    /// 0x5cd984  +0x20 := последняя; +0x10 := p; 0x5cd4f5(список, последняя) — хвост после последней стёрт
    /// </code>
    /// Мешок в досягаемости: у движка список кончается перед мешком, цель <c>+0x10</c> — мешок; порт точки не режет — список движка
    /// <c>Points[0..Reachable]</c>, клетка обрыва — <see cref="StackPath.CutAt"/>. Мешок дальше, чем хватает очков: обрыва нет, мешок —
    /// последняя точка списка (ai-q09, 59 поисков <c>path_expand</c> 7.2, ret <c>0x469070</c>).
    /// </summary>
    [Fact]
    public void AiPath_ToBag_StopsBeforeIt()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 5));
        map = WithBag(map, "S001BG0001", 6, 5);

        var path = FindAi(map, "S001KC0001", 6, 5, mode: 2);

        Assert.Equal((6, 5), (path.Points[^1].X, path.Points[^1].Y));
        Assert.Equal((5, 5), (path.Points[path.Reachable].X, path.Points[path.Reachable].Y));
        Assert.Equal((6, 5), path.CutAt);

        var slow = WithBag(Map(Stack("S001KC0001", Me, 1, 5) with { Move = 2 }), "S001BG0001", 6, 5);
        var far = FindAi(slow, "S001KC0001", 6, 5, mode: 2);
        Assert.Equal((6, 5), (far.Points[^1].X, far.Points[^1].Y));
        Assert.Equal((2, 5), (far.Points[far.Reachable].X, far.Points[far.Reachable].Y));   // равнина — 2: шаг в долг, дальше стоп
        Assert.Null(far.CutAt);
    }

    /// <summary>Мешок на пути ИИ (маска «можно», <c>a8</c>): в любой клетке шага, и в первой, отряд встаёт перед ним (<c>0x5cda67</c>, ранг 1
    /// по листингу 28.09); мешок в первой клетке — шага нет, мешок берут эффекты хода (журналы legions день 17, human дни 1 и 6: предмет
    /// мешка в отряде к следующей оценке). Прежнее правило «первую клетку насквозь» (ранг 2) подменяло этот подбор.</summary>
    [Fact]
    public void AiPath_BagStopsTheWalk_EvenOnFirstStep()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 5));
        var ahead = FindAi(WithBag(map, "S001BG0001", 3, 5), "S001KC0001", 6, 5, mode: 1);
        Assert.Equal((2, 5), (ahead.Points[ahead.Reachable].X, ahead.Points[ahead.Reachable].Y));

        var first = FindAi(WithBag(map, "S001BG0001", 2, 5), "S001KC0001", 6, 5, mode: 1);
        Assert.Equal(0, first.Reachable);
        Assert.Equal((1, 5), (first.Points[first.Reachable].X, first.Points[first.Reachable].Y));
    }

    /// <summary>Невидимый чужой отряд в масках <c>0x5ccac4</c>: при <c>a5</c> = 0 (интерфейс человека, <c>0x4cca54</c>) — «можно»
    /// (<c>0x5ccc68</c>), путь идёт сквозь него, а <see cref="PathFinder.Recount"/> с видящей враждебностью (команда хода,
    /// <c>0x5cc120(…, 1, 1)</c>) обрывает путь перед ним, и <see cref="PathFinder.ActionObject"/> находит его по таблице <c>0x7b0718</c>;
    /// при <c>a5</c> = 1 (ход сервера) — без зоны контроля (<c>0x5ccd1c</c>) и непроходим.</summary>
    [Fact]
    public void InvisibleStack_PassableForTheInterface_StopsTheCommand()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 5), Stack("S001KC0002", "S001PL0009", 4, 5));
        var stack = map.Stacks["S001KC0001"];
        bool Hidden(MapStack s) => s.Id == "S001KC0002";
        var shown = PathFinder.AiMasks(map, stack, 7, 5, 1, Walk, MovementRules.Vanilla, (_, _) => false, bags: false, invisible: Hidden);
        Assert.Contains((4, 5), shown.Allow);
        Assert.Empty(shown.Forbid);
        var path = PathFinder.Find(map, stack, 7, 5, Walk, MovementRules.Vanilla, dayAllowance: 100,
                                   hostile: s => s.Owner != Me && !Hidden(s), masks: shown);
        Assert.Equal(Enumerable.Range(1, 7).Select(x => (x, 5)), path.Points.Select(p => (p.X, p.Y)));
        Assert.Equal(6, path.Reachable);
        var command = PathFinder.Recount(map, stack, path, Walk, 100, s => s.Owner != Me);
        Assert.Equal((3, 5), (command.Points[command.Reachable].X, command.Points[command.Reachable].Y));
        Assert.Equal("S001KC0002", PathFinder.ActionObject(map, stack, command, (7, 5), (s, _) => s.Owner != Me));

        var server = PathFinder.AiMasks(map, stack, 7, 5, 1, Walk, MovementRules.Vanilla, (_, _) => false, bags: false, invisible: Hidden,
                                        seeInvisible: true);
        Assert.Empty(server.Allow);
        Assert.Empty(server.Forbid);   // невидимый при a5 = 1 зоны контроля не даёт
        var visible = PathFinder.AiMasks(map, stack, 7, 5, 1, Walk, MovementRules.Vanilla, (_, _) => false, bags: false);
        Assert.Contains((3, 5), visible.Forbid);   // видимый в режиме 1 — зона контроля
    }

    /// <summary>Объект действия команды (<c>0x5cd38a</c> → <c>0x5cd11f</c>): путь проходит вплотную мимо видимого врага — последняя достижимая
    /// встаёт рядом с ним (<c>0x5cd5b2</c>), и объект — этот враг, хотя щёлкнули дальше; цель-отряд, до которой путь дошёл, важнее соседа
    /// (<c>0x5cd1c7</c> по <c>+0x10</c> раньше <c>0x5cd11f</c>); из двух соседей — первый по таблице <c>0x7b0718</c>.</summary>
    [Fact]
    public void ActionObject_AnyHostileBesideTheLastReachable()
    {
        static bool Hostile(MapStack s, bool atTarget) => s.Owner != Me;
        var map = Map(Stack("S001KC0001", Me, 1, 5), Stack("S001KC0002", Enemy, 4, 6));
        var stack = map.Stacks["S001KC0001"];
        var path = Find(map, "S001KC0001", 8, 5);
        Assert.Equal((3, 5), (path.Points[path.Reachable].X, path.Points[path.Reachable].Y));   // встал вплотную к (4,6)
        Assert.Equal("S001KC0002", PathFinder.ActionObject(map, stack, path, (8, 5), Hostile));
        Assert.Null(PathFinder.ActionObject(map, stack, path, (8, 5), (_, _) => false));        // не враг — объекта нет

        var two = Map(Stack("S001KC0001", Me, 1, 5), Stack("S001KC0002", Enemy, 3, 4), Stack("S001KC0003", Enemy, 3, 6));
        var me = two.Stacks["S001KC0001"];
        // Цель — враг, и путь до неё дошёл: объект — цель, хотя (3,4) раньше по таблице соседей.
        var near = Find(two, "S001KC0001", 3, 6);
        Assert.Equal(near.Points.Count - 1, near.Reachable);
        Assert.Equal("S001KC0003", PathFinder.ActionObject(two, me, near, (3, 6), Hostile));
        // Щелчок дальше — объект по таблице 0x7b0718: от (2,5) сосед (1,−1) — (3,4) — раньше (1,1) — (3,6).
        var far = Find(two, "S001KC0001", 8, 5);
        Assert.Equal((2, 5), (far.Points[far.Reachable].X, far.Points[far.Reachable].Y));
        Assert.Equal("S001KC0002", PathFinder.ActionObject(two, me, far, (8, 5), Hostile));
    }

    /// <summary>Крепость в клетке цели, до которой путь дошёл, — объект-крепость: соседа-отряда <c>0x5cd38a</c> не ищет (<c>0x5cd4a7</c>);
    /// мешок в последней достижимой — объект-мешок (<c>0x5cd482</c>), нападения нет.</summary>
    [Fact]
    public void ActionObject_ReachedFortOrBagHasNoStackObject()
    {
        static bool Hostile(MapStack s, bool atTarget) => s.Owner != Me;
        var map = Map([Stack("S001KC0001", Me, 1, 5), Stack("S001KC0002", Enemy, 4, 6)], fort: (4, 5, Me));
        var stack = map.Stacks["S001KC0001"];
        var path = new StackPath([new PathPoint(1, 5, 0, 0), new PathPoint(2, 5, 1, 0), new PathPoint(3, 5, 2, 0)], 2);
        Assert.Null(PathFinder.ActionObject(map, stack, path, (4, 5), Hostile));                 // дошёл, в цели крепость
        Assert.Equal("S001KC0002", PathFinder.ActionObject(map, stack, path, (5, 5), Hostile));   // цель не крепость — сосед (4,6)
        Assert.Null(PathFinder.ActionObject(WithBag(map, "S001BG0001", 3, 5), stack, path, (5, 5), Hostile));   // мешок в последней
    }

    /// <summary>Жезл (<c>N</c> = 2, <c>0x45abd8</c>): последняя точка снимается до расчёта дня — отряд встаёт рядом.</summary>
    [Fact]
    public void StopShort_DropsLastPoints()
    {
        var map = Map(Stack("S001KC0001", Me, 1, 5));

        var path = PathFinder.Find(map, map.Stacks["S001KC0001"], 6, 5, Walk, MovementRules.Vanilla, dayAllowance: 100,
                                   stopShort: 1);

        Assert.Equal((5, 5), (path.Points[^1].X, path.Points[^1].Y));
        Assert.Equal(path.Points.Count - 1, path.Reachable);
    }

    private static StackPath FindAi(MapState map, string stackId, int x, int y, int mode)
    {
        var stack = map.Stacks[stackId];
        var masks = PathFinder.AiMasks(map, stack, x, y, mode, Walk, MovementRules.Vanilla, (_, _) => false);
        return PathFinder.Find(map, stack, x, y, Walk, MovementRules.Vanilla, dayAllowance: 100, masks: masks);
    }

    private static MapState WithBag(MapState map, string id, int x, int y)
    {
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[(x, y)] = [id];
        return new MapState
        {
            Size = map.Size, Tiles = map.Tiles, Stacks = map.Stacks, Units = map.Units, Players = map.Players,
            Objects = map.Objects, Plan = plan, Fog = map.Fog, Turn = map.Turn, Sites = map.Sites,
        };
    }

    // ------------------------------------------------------------------ синтетическая карта

    private static StackPath Find(MapState map, string stackId, int x, int y) =>
        PathFinder.Find(map, map.Stacks[stackId], x, y, Walk, MovementRules.Vanilla, dayAllowance: 100);

    private static bool Adjacent(PathPoint p, (int X, int Y) c) =>
        Math.Abs(p.X - c.X) <= 1 && Math.Abs(p.Y - c.Y) <= 1;

    private static MapStack Stack(string id, string owner, int x, int y) => new()
    {
        Id = id, Owner = owner, Subrace = "", LeaderId = "", LeaderAlive = true, X = x, Y = y, Move = 100,
        Inside = "", UnitIds = [], Slots = [],
    };

    private static MapState Map(params MapStack[] stacks) => Map(stacks, fort: null);

    /// <summary>Равнина 12×12 с отрядами; крепость — одна клетка с владельцем.</summary>
    private static MapState Map(MapStack[] stacks, (int X, int Y, string Owner)? fort)
    {
        const int size = 12;
        var tiles = new Tile[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                tiles[x, y] = new Tile((byte)(((int)Ground.Plain << 3) | 1));

        var plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>();
        foreach (var s in stacks.Where(s => s.Inside.Length == 0)) plan[(s.X, s.Y)] = [s.Id];
        var sites = new List<MapSite>();
        var objects = new Dictionary<string, MapObject>(StringComparer.OrdinalIgnoreCase);
        if (fort is { } f)
        {
            plan[(f.X, f.Y)] = ["S001FT0001"];
            objects["S001FT0001"] = new MapObject { Id = "S001FT0001", Kind = "FT", X = f.X, Y = f.Y };
            sites.Add(new MapSite { Id = "S001FT0001", Kind = SiteKind.Village, RawKind = "MidVillage", X = f.X, Y = f.Y, Owner = f.Owner });
        }

        return new MapState
        {
            Size = size,
            Tiles = tiles,
            Stacks = stacks.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase),
            Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(),
            Objects = objects,
            Plan = plan,
            Fog = new Dictionary<string, bool[,]>(),
            Turn = 1,
            Sites = sites,
        };
    }
}
