namespace D2.Rules.Map;

/// <summary>Точка пути: клетка, накопленная цена от начала (сколько очков хода нужно, чтобы стоять здесь) и номер
/// дня (0 — этот ход).</summary>
public sealed record PathPoint(int X, int Y, int Cost, int Day);

/// <summary>Путь отряда: точки в порядке движения (первая — клетка отряда, цена 0) и номер последней точки,
/// достижимой сегодня.</summary>
public sealed record StackPath(IReadOnlyList<PathPoint> Points, int Reachable)
{
    public static readonly StackPath Empty = new([], 0);

    /// <summary>Часть пути, которую отряд проходит сегодня (без клетки, где он стоит).</summary>
    public IReadOnlyList<PathPoint> Today => [.. Points.Skip(1).Take(Math.Max(0, Reachable))];

    /// <summary>Сколько очков хода списывается за сегодняшний путь (цена последней достижимой точки).</summary>
    public int Spend => Points.Count == 0 ? 0 : Points[Math.Clamp(Reachable, 0, Points.Count - 1)].Cost;

    /// <summary>Клетка, на которой путь оборвало «идти» = 0 (<c>0x5cda67</c>: мешок в клетке или клетка рядом с врагом старта). Движок
    /// кладёт её в цель искателя <c>+0x10</c> и режет список <c>+0x30</c> после последней достижимой (<c>0x5cd984</c> → <c>0x5cd4f5</c>);
    /// по <c>+0x10</c> проверка пути ИИ считает готовность (<c>0x46903e</c>), а эффекты хода берут мешок (<c>0x4408cb</c>). Порт точки не
    /// режет (показ и событие <c>L_MOVE_STACK</c> держатся за весь список) — список движка здесь <c>Points[0..Reachable]</c>. null —
    /// не обрывало.</summary>
    public (int X, int Y)? CutAt { get; init; }

    /// <summary>Списание команды, когда команда (видящая невидимых, <see cref="PathFinder.Recount"/>) встала раньше последней достижимой
    /// присланного пути: <c>CEffectMove</c> списывает цену <b>конца списка</b> команды (<c>0x5cd0e9</c>: накопленная цена последней точки
    /// <c>+0x30</c>), а список — точки пути до прежней последней достижимой с ценами, пересчитанными сеткой без масок (<c>0x5cc3d2</c> →
    /// <c>0x602692(…, 0, 0)</c>): клетка с отрядом стоит 0. Отряд проходит только до новой последней достижимой (<c>0x5ccf8b</c>). Журнал
    /// 20261017-port-synth, карта 10: охотник ИИ сквозь невидимку (13,45) встал на (12,45), списано 10 (до (16,45) без клетки невидимки); мимо
    /// невидимки (13,49) — встал на (12,50), списано 12. null — команда встала там же, где путь.</summary>
    public int? Charge { get; init; }

    /// <summary>Сколько списать за ход, пройдя <paramref name="walked"/> шагов сегодняшней части: <see cref="Charge"/>, если отряд дошёл до
    /// последней достижимой команды и путь не оборван событием (<paramref name="cut"/>, и на последней клетке), иначе — цена точки, где встал.
    /// Обрыв событием и перед невидимым списывает цену клетки обрыва, а не конца списка: журнал 20261018-port-synth, карта 13 — CH1 4, CH2 6
    /// (человек, невидимка за клеткой обрыва), CA1 2, CA2 4 (ИИ; <c>ap_restore</c> seq 612853, 647011, 852882, 855631).</summary>
    public int ChargeFor(int walked, bool cut = false) =>
        Points.Count == 0 ? 0
        : Charge is { } charge && !cut && walked == Reachable ? charge : Points[Math.Clamp(walked, 0, Points.Count - 1)].Cost;
}

/// <summary>
/// Поиск пути отряда по карте — как в движке (<c>docs/map-path.md</c>, разбор <c>Discipl2.exe</c> GOG 3.01).
/// A* идёт <b>от цели к отряду</b> (<c>0x680517</c>): соседи в порядке таблицы <c>0x6f41d8</c>, шаг стоит
/// «цена клетки + 0.4142 за диагональ + 0.4142 за поворот», оценка — расстояние до клетки отряда; открытый узел
/// повторно не улучшается, закрытый улучшается. Цена клетки — <see cref="Movement.Cost"/> плюс занятость
/// (чужой отряд или мешок — нельзя).
///
/// <para>Маски <c>a6</c>/<c>a7</c> движка (зона контроля вокруг чужих отрядов) в обычном режиме игрока почти
/// пусты и здесь не делаются — это режимы ИИ, вора и Ctrl (<c>docs/map-path.md</c> §1).</para>
/// </summary>
/// <summary>Маски поиска пути (<c>a6</c>/<c>a7</c> движка): клетки «нельзя» и «можно, даже если занята мешком или
/// чужим отрядом». Строит <see cref="PathFinder.AiMasks"/>.</summary>
public sealed record PathMasks(IReadOnlySet<(int X, int Y)> Forbid, IReadOnlySet<(int X, int Y)> Allow);

/// <summary>Узел A*, снятый из открытого списка и раскрытый: клетка, <c>g</c> и <c>f</c> во float движка
/// (<see cref="PathFinder.Find"/>, параметр <c>trace</c>; сверка — <c>path_expand</c> зонда 7.2).</summary>
public sealed record ExpandedNode(int X, int Y, float G, float F);

public static class PathFinder
{
    /// <summary>Соседи в порядке движка (<c>0x6f41d8</c>) — он решает равные пути.</summary>
    private static readonly (int Dx, int Dy)[] Neighbours =
        [(0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1)];

    /// <summary>Путь отряда к клетке. <paramref name="dayAllowance"/> — полный дневной запас лидера (для номеров
    /// дней), <paramref name="hostile"/> — враждебен ли отряд-сосед (по умолчанию — любой чужой). <paramref name="masks"/>
    /// — маски режимов ИИ (<see cref="AiMasks"/>); <paramref name="stopShort"/> — сколько последних точек снять до
    /// расчёта дневного запаса (действие ИИ «встать за клетку до цели»). <paramref name="trace"/> получает раскрытые узлы A* в
    /// порядке снятия из открытого списка (как <c>path_expand</c> зонда: клетка отряда, снятая последней, не пишется).</summary>
    public static StackPath Find(MapState map, MapStack stack, int targetX, int targetY,
                                 MoveOptions opt, MovementRules rules, int dayAllowance,
                                 Func<MapStack, bool>? hostile = null, PathMasks? masks = null, int stopShort = 0,
                                 List<ExpandedNode>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        hostile ??= other => !other.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase);

        var start = Start(map, stack);
        var (tx, ty) = Target(map, stack, targetX, targetY, opt, rules);
        if ((tx, ty) == start || !map.Contains(tx, ty)) return StackPath.Empty;

        int Cost(int x, int y) => masks is null ? CellCost(map, stack, x, y, opt, rules)
            : masks.Forbid.Contains((x, y)) ? Movement.Forbidden
            : masks.Allow.Contains((x, y)) ? Movement.Cost(map, x, y, opt, rules)
            : CellCost(map, stack, x, y, opt, rules);
        // Цель остаётся в списке, если она проходима при нулевых масках (0x5cca55 → [ebp-0x10]) или у неё бит a7 (0x5cc958/0x5cc97a);
        // иначе pop_back (0x680b05 → 0x45af6f). Мешок-цель ИИ (a8: мешки в a7) поэтому в списке остаётся — ai-q09, 59 поисков path_expand
        // 7.2 (ret 0x469070, режим 2): последняя точка — мешок; ход он не меняет — клетку мешка обрывает «идти» = 0 (Reachable, CutAt).
        bool targetPassable = CellCost(map, stack, tx, ty, opt, rules) != Movement.Forbidden
                              || (masks is not null && masks.Allow.Contains((tx, ty)));
        // Цель, в которую можно шагнуть при нулевых масках (0x5cca55 → 0x5cc342) или с битом a7 (0x5cc97a), «обязана быть
        // проходимой»: бит a6 у неё — поиска нет (0x5cc9a2), и A* сверяет её цену с масками (0x6805b0). Так ИИ не находит
        // пути в клетку зоны контроля чужого отряда (помощник к точке рядом с врагом, human день 48: KC00a4 → 8,25).
        if (masks is not null && Cost(tx, ty) == Movement.Forbidden
            && (masks.Allow.Contains((tx, ty)) || CellCost(map, stack, tx, ty, opt, rules) != Movement.Forbidden))
            return StackPath.Empty;

        var cells = Search(map, start, (tx, ty), Cost, trace);
        if (cells.Count == 0) return StackPath.Empty;
        if (!targetPassable) cells.RemoveAt(cells.Count - 1);
        // Действие ИИ «встать за N − 1 клетку до цели» (0x45abd8: N − 1 раз pop_back до команды движения).
        if (stopShort > 0) cells.RemoveRange(Math.Max(1, cells.Count - stopShort), Math.Min(stopShort, cells.Count - 1));
        if (cells.Count == 0) return StackPath.Empty;
        // Остаётся одна клетка отряда: цель занята и стоит вплотную — движок так и отдаёт путь из одной точки.
        if (cells.Count == 1) return new StackPath([new PathPoint(cells[0].X, cells[0].Y, 0, 0)], 0);

        // Накопленные цены: первая точка — 0 (0x680b78), дальше сумма цен клеток. У отряда в крепости или руине вход
        // снимается (pop_front, 0x5cc7cf), цена первой оставшейся точки обнуляется и вход возвращается с ценой 0
        // (0x5cca05): первый шаг из ворот бесплатен (журнал human, день 2: S073KC0072 — шесть шагов по 2 за 10 очков).
        var costs = new int[cells.Count];
        int paidFrom = stack.Inside.Length > 0 ? 2 : 1;
        for (int i = 1; i < cells.Count; i++)
            costs[i] = costs[i - 1] + (i < paidFrom ? 0 : Math.Max(0, Cost(cells[i].X, cells[i].Y)));

        int reachable = Reachable(map, stack, cells, costs, opt, hostile, out int cut);
        return Mark(cells, costs, reachable, cut, stack, dayAllowance);
    }

    /// <summary>Точки пути с номерами дней (<c>docs/map-path.md</c> §2): до последней достижимой — день 0, дальше — по дневному запасу.</summary>
    private static StackPath Mark(List<(int X, int Y)> cells, int[] costs, int reachable, int cut, MapStack stack, int dayAllowance)
    {
        var points = new List<PathPoint>(cells.Count);
        int day = 0, baseCost = 0;
        bool passed = stack.Move <= 0;
        for (int i = 0; i < cells.Count; i++)
        {
            day = passed ? (dayAllowance <= 0 ? 1 : ((costs[i] - baseCost) / dayAllowance) + 1) : 0;
            if (i == reachable) { passed = true; baseCost = costs[i]; }
            points.Add(new PathPoint(cells[i].X, cells[i].Y, costs[i], day));
        }
        return new StackPath(points, reachable) { CutAt = cut < 0 ? null : cells[cut] };
    }

    /// <summary>
    /// Докуда дойдёт отряд по уже готовому списку точек (команда хода человека: <c>0x43fe12</c> → <c>0x5cc120</c> строит искатель из
    /// присланных интерфейсом точек, <c>0x5cd38a</c> → <c>0x5cd5b2</c> пересчитывает последнюю достижимую) — с другой враждебностью
    /// <paramref name="hostile"/>: интерфейс не видит чужих невидимых (<c>a5</c> = 0), команда видит (<c>0x5cc120(…, 1, 1)</c>). Цены точек и
    /// список не меняются, меняются только последняя достижимая, номера дней и клетка обрыва.
    /// </summary>
    public static StackPath Recount(MapState map, MapStack stack, StackPath path, MoveOptions opt, int dayAllowance,
                                    Func<MapStack, bool> hostile)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(hostile);
        if (path.Points.Count < 2) return path;
        var cells = path.Points.Select(p => (p.X, p.Y)).ToList();
        var costs = path.Points.Select(p => p.Cost).ToArray();
        int reachable = Reachable(map, stack, cells, costs, opt, hostile, out int cut);
        // Сумма — по точкам до прежней последней достижимой (не дальше конца списка); клетка с любым другим отрядом стоит 0, как в сетке
        // без масок. Повторный пересчёт уже пересчитанного пути прежнее списание не теряет.
        int? charge = path.Charge;
        if (reachable < path.Reachable)
        {
            int sum = 0;
            for (int i = 1; i <= path.Reachable && i < cells.Count; i++)
                if (!OtherStackAt(map, stack, cells[i].X, cells[i].Y)) sum += costs[i] - costs[i - 1];
            charge = sum;
        }
        return Mark(cells, costs, reachable, cut, stack, dayAllowance) with { Charge = charge };
    }

    /// <summary>В клетке стоит другой отряд (план карты) — сетка цен без масок даёт ей 0 (<c>computeMovementCost</c>).</summary>
    private static bool OtherStackAt(MapState map, MapStack stack, int x, int y) =>
        map.Plan.TryGetValue((x, y), out var ids)
        && ids.Any(id => KindIs(id, "KC") && !id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase) && map.Stacks.ContainsKey(id));

    /// <summary>
    /// Отряд — объект действия пути (<c>+0</c> искателя, <c>0x5cd38a</c> после последней достижимой <c>0x5cd5b2</c>), на который нападёт команда
    /// хода (генератор <c>0x43fc3c</c>: <c>0x43ff9d</c> берёт объект типа «отряд» или «крепость», если путь не оборван событием и очки хода
    /// были). Порядок движка:
    /// <list type="number">
    /// <item>дошёл до конца списка (последняя достижимая = конец <c>+0x20</c>) — враждебный отряд в клетке цели <paramref name="goal"/>
    /// (<c>+0x10</c>; <c>0x5cd1c7</c> с флагом 0 и «на цели» = 1), затем крепость там же;</item>
    /// <item>мешок в последней достижимой (<c>0x5f54e2</c>, тип 0x27) — объект-мешок, нападения нет;</item>
    /// <item>крепость в клетке цели и дошёл до конца — объекта-отряда нет;</item>
    /// <item>иначе — первый враждебный отряд вокруг последней достижимой (<c>0x5cd11f</c>: восемь соседей по таблице <c>0x7b0718</c> —
    /// (−1,−1) (0,−1) (1,−1) (−1,0) (1,0) (−1,1) (0,1) (1,1), затем сама клетка; «на цели» — сосед совпал с концом пути). <b>Любой</b>, не
    /// только невидимый: команда, кончившаяся вплотную к врагу, нападает на него.</item>
    /// </list>
    /// Крепость-объект здесь не отдаётся (null): нападение на крепость решает вызывающий по своей цели. <paramref name="hostile"/>(отряд, на
    /// цели) — враждебность <c>0x5cd301</c>: <c>0x5deb24</c> с «видеть невидимых» = <c>a5</c> и не союзник (<c>0x5cd2ed</c>).
    /// </summary>
    public static string? ActionObject(MapState map, MapStack stack, StackPath path, (int X, int Y) goal, Func<MapStack, bool, bool> hostile)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(hostile);
        if (path.Points.Count == 0) return null;
        var reach = path.Points[Math.Clamp(path.Reachable, 0, path.Points.Count - 1)];
        var end = path.Points[^1];
        bool atEnd = reach.X == end.X && reach.Y == end.Y;
        if (atEnd && HostileStackAt(map, stack, goal.X, goal.Y, other => hostile(other, true)) is { } atGoal) return atGoal;
        if (BagAt(map, reach.X, reach.Y)) return null;
        if (atEnd && map.Plan.TryGetValue(goal, out var there) && there.Any(id => KindIs(id, "FT"))) return null;
        return HostileBeside(map, stack, reach.X, reach.Y, (end.X, end.Y), hostile);
    }

    /// <summary>Враждебный отряд вокруг клетки (<c>0x5cd11f</c> — <c>getBlockingPathNearbyStackId</c>; им же интерфейс решает, ведёт ли путь
    /// к действию, <c>0x4ccc2c</c>): первый в восьми соседях по таблице <c>0x7b0718</c>, затем в самой клетке; «на цели» — клетка совпала с
    /// <paramref name="end"/>. null — нет.</summary>
    public static string? HostileBeside(MapState map, MapStack stack, int x, int y, (int X, int Y) end, Func<MapStack, bool, bool> hostile)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(hostile);
        foreach (var (dx, dy) in ObjectNeighbours)
        {
            int nx = x + dx, ny = y + dy;
            bool onEnd = nx == end.X && ny == end.Y;
            if (HostileStackAt(map, stack, nx, ny, other => hostile(other, onEnd)) is { } id) return id;
        }
        return HostileStackAt(map, stack, x, y, other => hostile(other, x == end.X && y == end.Y));
    }

    /// <summary>Соседи клетки в порядке <c>0x5cd11f</c> (таблица <c>0x7b0718</c>, 8 пар).</summary>
    private static readonly (int Dx, int Dy)[] ObjectNeighbours =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary>Направление шага (<c>FACING</c>) — номер соседа в порядке движка; шаг «на месте» — 4.</summary>
    public static int Facing(PathPoint from, PathPoint to)
    {
        int i = Array.IndexOf(Neighbours, (to.X - from.X, to.Y - from.Y));
        return i < 0 ? 4 : i;
    }

    /// <summary>Клетка, с которой отряд выходит: своя или вход объекта, внутри которого он стоит
    /// (<c>0x5cc7cf</c>).</summary>
    public static (int X, int Y) Start(MapState map, MapStack stack) =>
        stack.Inside.Length == 0 ? (stack.X, stack.Y) : EntranceOf(map, stack.Inside) ?? (stack.X, stack.Y);

    /// <summary>Клетка цели (<c>0x5cc23d</c>): сама клетка, если в неё можно шагнуть; иначе вход объекта в ней или
    /// клетка стоящего там отряда.</summary>
    public static (int X, int Y) Target(MapState map, MapStack stack, int x, int y, MoveOptions opt, MovementRules rules)
    {
        if (Movement.Cost(map, x, y, opt, rules) != Movement.Forbidden) return (x, y);
        if (!map.Plan.TryGetValue((x, y), out var ids)) return (x, y);
        foreach (string id in ids)
        {
            if (KindIs(id, "KC") && map.Stacks.TryGetValue(id, out var other)) return (other.X, other.Y);
            if ((KindIs(id, "FT") || KindIs(id, "RU") || KindIs(id, "SI")) && EntranceOf(map, id) is { } entrance) return entrance;
        }
        return (x, y);
    }

    /// <summary>Цена клетки для поиска: грунт и объекты (<see cref="Movement.Cost"/>) плюс занятость — чужой отряд
    /// или мешок делают клетку непроходимой (<c>docs/map-path.md</c> §1).</summary>
    public static int CellCost(MapState map, MapStack stack, int x, int y, MoveOptions opt, MovementRules rules)
    {
        int cost = Movement.Cost(map, x, y, opt, rules);
        if (cost == Movement.Forbidden) return Movement.Forbidden;
        if (!map.Plan.TryGetValue((x, y), out var ids)) return cost;
        foreach (string id in ids)
        {
            if (KindIs(id, "BG")) return Movement.Forbidden;
            if (KindIs(id, "KC") && !id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)) return Movement.Forbidden;
        }
        return cost;
    }

    /// <summary>
    /// A* от цели к клетке отряда (<c>0x680543</c>, ранг 1); возвращает клетки в порядке движения (от отряда к цели). Узел хранит
    /// <c>f</c> и <c>g</c> во float (<c>fstp dword</c>); <c>g' = g + цена + поворот + диагональ</c> (<c>0x680865</c>, константы — float
    /// <c>0,4142</c>); эвристика — табличный корень <c>0x51f35e</c> во float (<see cref="AiTacticScore.FastSqrtF"/>), а не точный.
    /// Открытый список — двоичная куча вектора (<c>push_heap</c>/<c>pop_heap</c> Dinkumware: <c>0x68161e</c>, <c>0x68169e</c>) с
    /// «меньше по <c>f</c>»; порядок равных задаёт сама куча (<see cref="HeapPush"/>, <see cref="HeapPop"/>), а не очередь.
    /// </summary>
    private static List<(int X, int Y)> Search(MapState map, (int X, int Y) start, (int X, int Y) target,
                                               Func<int, int, int> cost, List<ExpandedNode>? trace)
    {
        // Открытый и закрытый списки и цена клетки — плотные массивы по номеру клетки y·Size + x с поколением вместо очистки
        // (perf-log Г3; gdscript-knowledge performance.md «Когда переносить код в C#…»: коллекции в горячем цикле). Порядок
        // раскрытия задаёт только куча — массивы заменяют словари один к одному. Цена клетки за один поиск не меняется (карта
        // и маски неизменны), поэтому считается один раз на клетку, а не на каждого соседа.
        var s = Scratch.For(map.Size);
        int size = map.Size;
        var heap = s.Heap;
        heap.Clear();
        var seed = new FNode(target.X, target.Y, 0f, HeuristicF(target.X, target.Y, start), null);
        HeapPush(heap, seed);
        s.Open(target.Y * size + target.X, seed);

        while (heap.Count > 0)
        {
            var u = HeapPop(heap);
            s.Close(u.Y * size + u.X, u);

            if ((u.X, u.Y) == start) { var path = Collect(u); s.Release(); return path; }
            trace?.Add(new ExpandedNode(u.X, u.Y, u.G, u.F));

            for (int i = 0; i < Neighbours.Length; i++)
            {
                var (dx, dy) = Neighbours[i];
                int vx = u.X + dx, vy = u.Y + dy;
                if (!map.Contains(vx, vy)) continue;
                if (u.Parent is { } parent && (parent.X, parent.Y) == (vx, vy)) continue;
                int v = vy * size + vx;
                int c = s.Cost(v, vx, vy, cost);
                if (c == Movement.Forbidden && (vx, vy) != start) continue;
                double turn = u.Parent is { } from && (u.X - from.X, u.Y - from.Y) == (dx, dy) ? 0 : DiagonalStep;
                // Сумма на стеке x87 (0x680865: fld g; fiadd цена; fadd поворот; fadd диаг; fstp) — округление до float один раз, в
                // конце. В C# «float + int» — сложение во float: g′ округлялось дважды и уходило на 1 ulp (10,070999 вместо 10,071 —
                // path_expand 7.2), а с ним и f равных путей.
                float g = (float)((double)u.G + c + turn + (dx != 0 && dy != 0 ? DiagonalStep : 0));
                var state = s.State(v);
                if (state == Scratch.Closed && s.Node[v]!.G <= g) continue;
                if (state == Scratch.InOpen) continue;   // открытый узел движок не улучшает
                var node = new FNode(vx, vy, g, (float)(HeuristicF(vx, vy, start) + (double)g), u);
                HeapPush(heap, node);
                s.Open(v, node);   // закрытый узел с худшим g уходит из закрытых
            }
        }
        s.Release();
        return [];
    }

    /// <summary>Рабочие массивы поиска на поток: состояние клетки (нет/открыта/закрыта), её узел и цена — по номеру клетки;
    /// актуальны только записи текущего поколения, поэтому состояние и цену новый поиск не очищает (узлы отпускает <see cref="Release"/>).
    /// Поиск не вложенный: цена клетки сама пути не ищет.</summary>
    private sealed class Scratch
    {
        public const byte None = 0, InOpen = 1, Closed = 2;

        [ThreadStatic] private static Scratch? _current;

        public readonly List<FNode> Heap = [];
        public FNode?[] Node = [];
        private int[] _stamp = [], _costStamp = [], _cost = [];
        private byte[] _state = [];
        private int _epoch;

        public static Scratch For(int size)
        {
            var s = _current ??= new Scratch();
            int n = size * size;
            if (s._stamp.Length < n)
            {
                s.Node = new FNode?[n];
                s._stamp = new int[n];
                s._costStamp = new int[n];
                s._cost = new int[n];
                s._state = new byte[n];
                s._epoch = 0;
            }
            if (++s._epoch == int.MaxValue)
            {
                Array.Clear(s._stamp);
                Array.Clear(s._costStamp);
                s._epoch = 1;
            }
            return s;
        }

        /// <summary>Отпускает узлы прошедшего поиска, чтобы массив не держал их графы живыми до следующего поиска.</summary>
        public void Release()
        {
            Heap.Clear();
            Array.Clear(Node);
        }

        public byte State(int v) => _stamp[v] == _epoch ? _state[v] : None;

        public void Open(int v, FNode node) => Set(v, InOpen, node);

        public void Close(int v, FNode node) => Set(v, Closed, node);

        private void Set(int v, byte state, FNode node)
        {
            _stamp[v] = _epoch;
            _state[v] = state;
            Node[v] = node;
        }

        public int Cost(int v, int x, int y, Func<int, int, int> cost)
        {
            if (_costStamp[v] == _epoch) return _cost[v];
            _costStamp[v] = _epoch;
            return _cost[v] = cost(x, y);
        }
    }

    /// <summary>Штраф за диагональ и за поворот (<c>0x680517</c>: <c>0.4142</c> ≈ √2 − 1) — float <c>0x6f4240</c> и таблица
    /// <c>0x6f4218</c>.</summary>
    private const double DiagonalStep = (double)0.4142f;   // именно float-значение движка, расширенное до double

    private static float HeuristicF(int x, int y, (int X, int Y) start) =>
        AiTacticScore.FastSqrtF(((x - start.X) * (x - start.X)) + ((y - start.Y) * (y - start.Y)));

    /// <summary><c>push_heap</c> (<c>0x68161e</c>): дырка поднимается, пока <c>f</c> нового строго меньше родителя.</summary>
    private static void HeapPush(List<FNode> heap, FNode v)
    {
        heap.Add(v);
        HeapUp(heap, heap.Count - 1, 0, v);
    }

    private static void HeapUp(List<FNode> heap, int hole, int top, FNode v)
    {
        for (int parent = (hole - 1) / 2; hole > top && v.F < heap[parent].F; parent = (hole - 1) / 2)
        {
            heap[hole] = heap[parent];
            hole = parent;
        }
        heap[hole] = v;
    }

    /// <summary><c>pop_heap</c> + <c>pop_back</c> (<c>0x6815f6</c> → <c>0x68169e</c>): вершина уходит, последний спускается от корня —
    /// дырка идёт в меньшего ребёнка, левого только при строго меньшем <c>f</c>, потом поднимается как при вставке.</summary>
    private static FNode HeapPop(List<FNode> heap)
    {
        var top = heap[0];
        var val = heap[^1];
        heap.RemoveAt(heap.Count - 1);
        int len = heap.Count;
        if (len == 0) return top;
        int hole = 0, idx = 2;
        for (; idx < len; idx = 2 * idx + 2)
        {
            if (heap[idx - 1].F < heap[idx].F) idx--;
            heap[hole] = heap[idx];
            hole = idx;
        }
        if (idx == len)
        {
            heap[hole] = heap[len - 1];
            hole = len - 1;
        }
        HeapUp(heap, hole, 0, val);
        return top;
    }

    private static List<(int X, int Y)> Collect(FNode node)
    {
        var cells = new List<(int X, int Y)>();
        for (FNode? n = node; n is not null; n = n.Parent) cells.Add((n.X, n.Y));
        return cells;   // узел поиска — клетка отряда, родители ведут к цели: уже порядок движения
    }

    private sealed record FNode(int X, int Y, float G, float F, FNode? Parent);

    /// <summary>Номер последней точки, достижимой сегодня (<c>0x5cd5b2</c>). Клетка
    /// принимается, и <b>после</b> неё путь обрывается, если её накопленная цена не меньше остатка очков — последний
    /// шаг идёт в долг. Обрывает путь и враждебный отряд рядом с клеткой (<c>0x5cda67</c>, «продолжать»).
    /// Враги, стоявшие рядом с отрядом на старте, путь не обрывают, но в клетку рядом с ними шагнуть нельзя — путь
    /// кончается перед ней (<c>0x5cda67</c> → <c>0x5cdba2</c>, «идти» = 0): отойти от врага можно, пройти мимо — нет.
    /// Движок при этом кладёт клетку обрыва в цель <c>+0x10</c> и режет хвост списка (<c>0x5cd984</c> → <c>0x5cd4f5</c>); здесь точки не
    /// режутся, клетка обрыва — <paramref name="cut"/> (<see cref="StackPath.CutAt"/>), −1 — не обрывало. Враг в первой клетке шага,
    /// нет очков хода и водный на суше хвост не режут (<c>0x5cd861</c>–<c>0x5cd86d</c>, <c>0x5cd8c2</c>).
    /// Водный лидер на суше останавливается на клетке <b>перед</b> ней (<c>0x5cd8c2</c>).</summary>
    private static int Reachable(MapState map, MapStack stack, IReadOnlyList<(int X, int Y)> cells,
                                 IReadOnlyList<int> costs, MoveOptions opt, Func<MapStack, bool> hostile, out int cut)
    {
        cut = -1;
        if (stack.Move <= 0) return 0;
        // Враги вокруг клетки отряда (0x5cd9e0); у отряда в крепости первая точка — вход, соседей не собирает.
        var nearStart = stack.Inside.Length == 0 ? HostilesAround(map, stack, cells[0].X, cells[0].Y, hostile) : [];
        // Первая клетка шага проверяется сама по себе, без соседей (0x5cd7fe → 0x5cd1c7).
        if (HostileAt(map, stack, cells[1].X, cells[1].Y, hostile)) return 0;
        int last = 0;
        for (int i = 1; i < cells.Count; i++)
        {
            var (x, y) = cells[i];
            // Мешок в клетке — «идти» = 0 (0x5cda67): путь кончается перед ним, и в первой клетке шага тоже (ранг 1 по листингу 28.09;
            // 0x5cd7fe смотрит только врага). Мешок берут эффекты хода с соседней клетки (0x43fbac → 0x4408cb) —
            // GameSession.TakeBagAtFirstStep и AiMove (docs/map-path.md §8, поправка). Без масок в мешок путь не ведёт.
            if (BagAt(map, x, y))
            {
                cut = i;
                return last;
            }
            bool blocked = HostileAt(map, stack, x, y, hostile) || HostilesAround(map, stack, x, y, hostile).Count > 0;
            if (blocked && nearStart.Count > 0 && HostilesAround(map, stack, x, y, hostile).Overlaps(nearStart))
            {
                cut = i;
                return last;
            }
            if (opt.WaterOnly && !Movement.IsDeepWater(map, x, y)) return i - 1;
            last = i;
            if (blocked || costs[i] >= stack.Move) break;
        }
        return last;
    }

    /// <summary>Враждебные отряды в восьми соседях клетки (<c>0x5cd9e0</c>).</summary>
    private static HashSet<string> HostilesAround(MapState map, MapStack stack, int x, int y, Func<MapStack, bool> hostile)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dx, dy) in Neighbours)
            if (HostileStackAt(map, stack, x + dx, y + dy, hostile) is { } id) found.Add(id);
        return found;
    }

    private static bool HostileAt(MapState map, MapStack stack, int x, int y, Func<MapStack, bool> hostile) =>
        HostileStackAt(map, stack, x, y, hostile) is not null;

    /// <summary>Враждебный отряд в клетке (<c>0x5cd1c7</c> с флагом 1 → <c>0x5cd301</c>). Крепости путь здесь не
    /// обрывают: флаг 1 пропускает их проверку. Отряд внутри крепости или руины не мешает (<c>0x5deb24</c>).</summary>
    private static string? HostileStackAt(MapState map, MapStack stack, int x, int y, Func<MapStack, bool> hostile)
    {
        if (!map.Contains(x, y) || !map.Plan.TryGetValue((x, y), out var ids)) return null;
        foreach (string id in ids)
            if (KindIs(id, "KC") && map.Stacks.TryGetValue(id, out var other)
                && !other.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)
                && other.Inside.Length == 0 && hostile(other)) return other.Id;
        return null;
    }

    private static bool BagAt(MapState map, int x, int y) =>
        map.Plan.TryGetValue((x, y), out var ids) && ids.Any(id => KindIs(id, "BG"));

    /// <summary>
    /// Маски поиска пути ИИ (<c>0x5ccac4</c>, вызов из <c>0x5cc7cf</c>; разбор 23.09.2026,
    /// <c>docs/map-path.md</c> §1). ИИ ищет путь в режиме 1 или 2 (<c>0x46903e</c>: аргументы
    /// <c>a5 = 0</c>, <c>a8 = 1</c>, <c>a10 = 0</c>):
    /// <list type="bullet">
    /// <item>«можно» (<c>a7</c>): все мешки карты (<c>a8</c>); при <c>a8</c> и не в режиме 2 — клетка враждебного отряда, стоящего на
    /// цели (<c>0x5ccc7d</c>; прежнее прочтение «в режиме 1» листингом 30.09 не подтвердилось);</item>
    /// <item>«нельзя» (<c>a6</c>): клетка враждебного не союзного отряда в поле и восемь её соседей (<c>0x5cceb1</c>) —
    /// у отряда на цели только в режиме 2; если на цели стоит враждебный отряд, соседи ближе 2 к цели по обеим осям
    /// (<c>0x5ccf0c</c>) остаются открыты; в режиме 2 закрыта и цель-крепость или руина, и вход крепости с чужим
    /// отрядом внутри;</item>
    /// <item>клетка цели и там и там — «нельзя» снимается.</item>
    /// </list>
    /// Флаги поиска помехи (<c>0x460711</c>, <c>0x460fa0</c>): <paramref name="bags"/> — <c>a8</c> (мешки в «можно»; ложь — нет),
    /// <paramref name="allStacks"/> — <c>a10</c> (каждый враждебный отряд — в «можно», дальше не смотрится).
    /// Невидимые (<paramref name="invisible"/>; флаг отряда <c>+0x9b</c>, в модели карты его нет — его даёт партия): при
    /// <paramref name="seeInvisible"/> = ложь (<c>a5</c> = 0: интерфейс человека и ИИ, <c>0x4cca54</c>, <c>0x46903e</c>) отряд — в «можно» и дальше
    /// не смотрится (<c>0x5ccc68</c>–<c>0x5ccca5</c>), при <c>a5</c> = 1 (ход сервера <c>CEffectMove</c>, <c>0x439f73</c>) — без зоны контроля
    /// (<c>0x5ccd1c</c>). Без <paramref name="invisible"/> невидимых нет (ИИ — прежнее поведение: в корпусе путь ИИ мимо невидимки не встречался).
    /// <para>Враждебность отряда (<c>0x5cc6b4</c> → <c>0x5deb24(карта, мы, он, видеть невидимых = 1, считать внутри = 0, без рас = на
    /// цели)</c>) — по подрасе и расе хозяев, а не по хозяину: <paramref name="hostile"/>(отряд, на цели); без него — «хозяин другой».
    /// Нейтралы двух подрас «нейтральной» категории друг другу враги: 20261014-replay, NM KC009b (53,1) → LO0006 (59,4) — проход мимо
    /// нейтрала KC0074 (52,4) движку закрыт его зоной контроля (seq 246393, путь пуст), а порт шёл мимо как мимо своего.</para>
    /// </summary>
    public static PathMasks AiMasks(MapState map, MapStack stack, int targetX, int targetY, int mode,
                                    MoveOptions opt, MovementRules rules, Func<string, string, bool> allied,
                                    bool bags = true, bool allStacks = false, Func<MapStack, bool, bool>? hostile = null,
                                    Func<MapStack, bool>? invisible = null, bool seeInvisible = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(allied);
        var target = Target(map, stack, targetX, targetY, opt, rules);
        bool mode2 = mode == 2, mode12 = mode is 1 or 2;
        var bagCells = bags ? BagCells(map) : [];
        var allow = new HashSet<(int X, int Y)>(bagCells.Length + map.Stacks.Count);
        var forbid = new HashSet<(int X, int Y)>(map.Stacks.Count * 9);
        foreach (var bag in bagCells) allow.Add(bag);
        bool Hostile(MapStack s, bool atTarget) => (hostile?.Invoke(s, atTarget) ?? !s.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase))
                                                   && (s.Inside.Length == 0 || atTarget);
        bool targetHostile = map.Stacks.Values.Any(s => (s.X, s.Y) == target && Hostile(s, true));
        foreach (var s in map.Stacks.Values)
        {
            bool atTarget = (s.X, s.Y) == target;
            if (!Hostile(s, atTarget)) continue;
            if (allStacks)
            {
                allow.Add((s.X, s.Y));
                continue;
            }
            bool hidden = invisible?.Invoke(s) == true;
            // 0x5ccc68: невидимый и не a5 — в «можно» (0x5ccd9b), дальше не смотрится.
            if (hidden && !seeInvisible)
            {
                allow.Add((s.X, s.Y));
                continue;
            }
            // 0x5ccc7d: cl = a8 && !m2 — враждебный отряд на цели уходит в «можно» при a8 и не в режиме 2 (не «в режиме 1»): ai-q09
            // seq 228446 (режим 1, a8 = 0) — KC008a на цели не в a7, цель снята; seq 228468 (тот же ход, a8 = 1) — осталась.
            if (bags && !mode2 && atTarget)
            {
                allow.Add(target);
                continue;
            }
            if (s.Inside.Length == 0)
            {
                // 0x5ccd1c: невидимый при a5 — без зоны контроля.
                if (allied(stack.Owner, s.Owner) || (atTarget && !mode2) || !mode12 || hidden) continue;
                forbid.Add((s.X, s.Y));
                foreach (var (dx, dy) in Neighbours)
                {
                    var n = (X: s.X + dx, Y: s.Y + dy);
                    if (targetHostile && Math.Abs(n.X - target.X) < 2 && Math.Abs(n.Y - target.Y) < 2) continue;
                    forbid.Add(n);
                }
            }
            else if (mode2 && EntranceOf(map, s.Inside) == target) forbid.Add(target);
        }
        if (mode2 && map.Plan.TryGetValue(target, out var ids) && ids.Any(id => (KindIs(id, "RU") || KindIs(id, "FT"))))
            forbid.Add(target);
        if (allow.Contains(target)) forbid.Remove(target);
        return new PathMasks(forbid, allow);
    }

    /// <summary>Вход объекта — правый нижний угол пятна (<c>0x4cd253</c>); пятно берётся из плана карты.</summary>
    public static (int X, int Y)? EntranceOf(MapState map, string objectId)
    {
        var cells = PlanIndex.CellsOf(map, objectId);
        return cells.Count == 0 ? null : (cells.Max(c => c.X), cells.Max(c => c.Y));
    }

    /// <summary>Тип объекта по id — символы 4–5 без регистра (<c>S143<b>KC</b>0001</c>): то же, что <c>id.Substring(4, 2).ToUpperInvariant()
    /// == kind</c>, без двух строк на каждый id (маски и цены клеток пути ИИ зовут это на каждый поиск).</summary>
    private static bool KindIs(string id, string kind) =>
        id.Length >= 6 && char.ToUpperInvariant(id[4]) == kind[0] && char.ToUpperInvariant(id[5]) == kind[1];

    /// <summary>Клетки плана с мешком — один раз на экземпляр плана (план не меняется на месте, как у <see cref="PlanIndex"/>).</summary>
    private static (int X, int Y)[] BagCells(MapState map) =>
        Bags.GetValue(map.Plan, static plan => [.. plan.Where(p => p.Value.Any(id => KindIs(id, "BG"))).Select(p => p.Key)]);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>>,
                                                                                (int X, int Y)[]> Bags = new();

}
