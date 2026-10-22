namespace D2.Rules.Map;

/// <summary>
/// Квоты роста земли — поля одной строки <c>GVars.dbf</c>. Всё ранг 1: значения таблицы,
/// сверенные с корпусом (см. <see cref="LandModel"/>, раздел «Источники»).
/// </summary>
/// <param name="Capital"><c>T_CAPITAL</c> = 10 — дневная квота столицы в КЛЕТКАХ.</param>
/// <param name="City"><c>T_CITY1…T_CITY5</c> = 15/15/20/20/25 — дневная квота деревни по уровню.</param>
/// <param name="RodRange"><c>ROD_RANGE</c> = 2 — дневная квота своего жезла в клетках.
/// <i>Было иначе:</i> тулсет читал это как РАДИУС (план Ф7 §1.6).</param>
/// <param name="CapturePercent"><c>T_CAPTURE</c> = 100 — множитель разового обхода приобретения в сотых,
/// с отбрасыванием остатка (<see cref="LandQuotas.Acquisition"/>, ранг 1 по листингу).
/// <i>Было иначе:</i> план Ф7 §1.6 читал это как 100 очков манхэттенского расстояния — на
/// <c>m12-capture</c> совпало (36 клеток, сумма ровно 100), на <c>m7-land</c> опровергнуто
/// (31 клетка, сумма 80); разбор — отчёт задачи 7 <c>task7-land.md</c> §2.</param>
public sealed record LandQuotas(int Capital, IReadOnlyList<int> City, int RodRange, int CapturePercent)
{
    /// <summary>Ванильные значения — на случай отсутствия <c>Globals</c> (синтетика тестов).</summary>
    public static readonly LandQuotas Vanilla = new(10, [15, 15, 20, 20, 25], 2, 100);

    /// <summary>Квота деревни уровня <paramref name="size"/> (1…5).</summary>
    public int Village(int size) => City[Math.Clamp(size, 1, City.Count) - 1];

    /// <summary>Квота РАЗОВОГО обхода в день, когда источник стал нашим: <c>(T_CAPTURE / 100) × квота</c>,
    /// деление ЦЕЛОЕ и первым — ранг 1, <c>0x4341a1…0x4341af</c> (<c>cdq; idiv 100; imul</c>), та же
    /// формула у деревни при захвате. При ванильных 100 — ровно квота: деревня 3-го уровня дала 20
    /// клеток обхода (<c>m12-capture</c>), 1-го — 15 (<c>m7-land</c>), жезл — 2 (<c>m6-rods</c>).
    /// <i>Было иначе:</i> <c>квота × T_CAPTURE / 100</c> — расходится у мода с <c>T_CAPTURE</c>, не кратным 100.</summary>
    public int Acquisition(int quota) => CapturePercent / 100 * quota;
}


/// <summary>
/// Источник роста земли: своя столица, своя деревня или свой жезл. У каждого свой центр обхода и
/// своя квота; квоты складываются, перекрытие ничего не отнимает (ранг 1, план Ф7 §1.6).
/// </summary>
/// <param name="Id">Id объекта <c>.sg</c>. Им источники различаются, и в этом же порядке (ordinal)
/// движок делает дневные обходы: ранг 1, 210 дневных пачек из 210 (<c>land_grow</c> корпусов 6.x).</param>
/// <param name="Centre">Центр обхода: у укрепления <c>POS + (2, 2)</c>
/// (<see cref="LandModel.FortCentre"/>), у жезла — его клетка.</param>
/// <param name="Quota">Дневная квота в клетках (<see cref="LandQuotas"/>) — с первого дня и до первой
/// ступени <paramref name="Steps"/>.</param>
/// <param name="Own">Клетки самого объекта (<c>CMidgardPlan</c>): 25 у столицы, 16 у деревни,
/// 1 у жезла. В день приобретения они перекрашиваются ДАРОМ, сверх обхода (ранг 1). Обход их не
/// отвергает, хотя чужие укрепления и жезлы отвергает (<c>0x5e07b2</c>: объект в клетке сравнивается
/// с объектом-источником <c>[walker+0x1c]</c>).</param>
/// <param name="Since">С какого дня источник наш. <b>0 — был наш на старте</b>: его клетки уже
/// лежат в стартовом пятне и разового обхода не будет. Больше нуля — приобретён в этот день:
/// сначала <paramref name="Own"/>, потом разовый обход <see cref="LandQuotas.Acquisition"/>,
/// а дневная квота начинает работать со СЛЕДУЮЩЕГО дня.</param>
/// <param name="Until">Последний день, в который источник ещё даёт квоту (жезл снесён). Ранг 1:
/// прогон 2 <c>m6-rods</c> — жезл, стоявший в сохранении хода N, дал свои +2 и на ходу N+1,
/// а занятая земля не вернулась («земля замерзает», план Ф7 §1.6).</param>
/// <param name="Steps">Смена квоты по дням — рост деревни (план Ф7 §3 п. 23): пары «с какого дня,
/// какая квота» по возрастанию дня. Ранг 1: <c>CEffectTerrainCity::apply</c> <c>0x43eea8</c> берёт
/// <c>T_CITY</c> по уровню <c>[fort+0x90]</c> в момент обхода; в корпусе квота каждого обхода деревни —
/// <c>T_CITY[SIZE]</c> из последнего сохранения перед обходом (155/0 дневных, 14/0 наград).
/// <c>null</c> — квота постоянна.</param>
public sealed record LandSource(string Id, (int X, int Y) Centre, int Quota,
                                IReadOnlyList<(int X, int Y)> Own, int Since = 0,
                                int Until = int.MaxValue,
                                IReadOnlyList<(int From, int Quota)>? Steps = null)
{
    /// <summary>Квота источника на день <paramref name="day"/>: последняя ступень, начавшаяся не
    /// позже этого дня, иначе <see cref="Quota"/>.</summary>
    public int QuotaOn(int day)
    {
        int quota = Quota;
        foreach (var (from, value) in Steps ?? [])
            if (from <= day) quota = value;
        return quota;
    }

    /// <summary>Жезл — объект класса <c>RD</c> (<c>S143<b>RD</b>0000</c>); синтетика тестов зовёт жезл
    /// просто <c>RD</c>.</summary>
    public bool IsRod => Id.Contains("RD", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Обход роста земли движка — <c>0x5e0ab0</c>, дословно. Ранг 1 по листингу и по корпусу: все 1254
/// строки <c>land_grow</c> журналов 6.x (<c>20260914-smoke</c>, <c>20260915-passive</c>,
/// <c>20260916-synth</c>) — 7277 клеток — совпали с моделью ПОШТУЧНО И ПО ПОРЯДКУ, 0 расхождений
/// (план Ф7 §3 п. 7 закрыт).
/// <list type="number">
/// <item>Куча — <c>std::vector&lt;{dist, x, y}&gt;</c> с <c>push_heap</c>/<c>pop_heap</c> Dinkumware
/// (MSVC6): вставка <c>0x5e0bea</c> → <c>0x5e14db</c>, извлечение <c>0x5e0a85</c> → <c>0x5e1546</c> →
/// <c>0x5e1591</c>. Предикат сравнивает ТОЛЬКО <c>dist</c> — координаты в порядок не входят.</item>
/// <item>Извлечение — «дыра до дна»: из двух детей идёт правый, если он не больше левого
/// (<c>0x5e15b1</c>), затем последний элемент всплывает от дна (<c>0x5e1627</c>).</item>
/// <item>Соседи <c>0x5e0a1f</c> — таблица <c>0x6edfe8</c>: (0,+1) (0,−1) (+1,0) (−1,0); клетка вне
/// карты не кладётся, посещённая — кладётся (проверка посещения — при извлечении,
/// <c>0x5e0b1f</c>). <c>dist</c> соседа — <c>dist</c> родителя + 1, у центра 1: это число шагов, а не
/// манхэттенское расстояние.</item>
/// <item>Цикл: извлечь → посещённую пропустить → отметить → отвергнутую (<c>0x5e07b2</c>) не
/// расширять → свою (<c>0x5e0764</c>, террейн клетки = террейн источника) расширить, не считая →
/// чужую добавить в список и при длине = квоте остановиться, иначе расширить. Центр отмечен сразу
/// и приёмом не проверяется.</item>
/// <item>Квоты 0 движок не отсекает (<c>0x5e0551</c>, <c>0x5e0b5e</c> сравнивает после добавления):
/// такой обход берёт всё достижимое.</item>
/// </list>
/// </summary>
public static class LandWalk
{
    private readonly record struct Entry(int Dist, (int X, int Y) Cell);

    /// <summary><c>0x6edfe8</c>.</summary>
    private static readonly (int Dx, int Dy)[] Four = [(0, 1), (0, -1), (1, 0), (-1, 0)];

    /// <summary><c>0x6ee008</c> — восемь соседей в порядке, в котором их смотрит <c>0x5e0959</c>.</summary>
    private static readonly (int Dx, int Dy)[] Around =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary><c>0x6f0660</c> — восемь соседей и сама клетка: где <c>0x60ba1c</c> ищет жилу.</summary>
    private static readonly (int Dx, int Dy)[] CrystalProbe =
        [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1), (0, 0)];

    /// <summary>Клетки одного обхода в порядке взятия — ровно выходной список <c>0x5e0551</c>
    /// (<c>land_grow.cells</c>).</summary>
    /// <param name="own">Клетка уже своего террейна (<c>0x5e0764</c>).</param>
    /// <param name="rejected">Клетку отвергает приём <c>0x5e07b2</c> (см. <see cref="LandModel"/>).</param>
    public static IReadOnlyList<(int X, int Y)> Walk(int size, (int X, int Y) centre, int quota,
                                                     Func<(int X, int Y), bool> own,
                                                     Func<(int X, int Y), bool> rejected)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(rejected);
        var heap = new List<Entry>();
        var seen = new HashSet<(int X, int Y)> { centre };
        var taken = new List<(int X, int Y)>();
        Expand(heap, size, centre, 1);
        while (heap.Count > 0)
        {
            var (dist, cell) = Pop(heap);
            if (!seen.Add(cell) || rejected(cell)) continue;
            if (!own(cell))
            {
                taken.Add(cell);
                if (taken.Count == quota) break;
            }
            Expand(heap, size, cell, dist);
        }
        return taken;
    }

    /// <summary>Список перекраски вместе с квадратами жил: <c>CVisitorMapTerrain::apply</c>
    /// <c>0x60b966</c> для каждой клетки ищет первую жилу среди восьми соседей и самой клетки
    /// (<c>0x60ba1c</c>) и добавляет в список весь её квадрат 3×3 в пределах карты (<c>0x60ba78</c>).
    /// Ранг 1 по листингу; на корпусе без этого правила и без перекраски пятна до обхода расходились
    /// 102 обхода из 1254, с ними — 0; им же объясняется клетка 42:4 (план Ф7 §8, T05).
    /// <para><b>Квадраты цепляются друг за друга</b>: список — дерево <c>CMqPoint</c> в порядке «строка, затем столбец» (сравнение
    /// <c>0x419c1d</c>: сперва <c>y</c>, при равных — <c>x</c>), цикл <c>0x60b966</c> идёт по нему итератором (<c>0x5606b4</c>), пока в него
    /// вставляют, и клетка квадрата, вставленная дальше текущей, тоже ищет свою жилу. Ранг 1 — листинг и журнал карты 23
    /// (<c>20261020-port-synth</c>, день 55): у гнезда жил <c>S143CR000f</c> (128,124), <c>S143CR0010</c> (125,124), <c>S143CR0011</c> (127,125)
    /// обход Нежити от (120,120) берёт (126,124) — квадрат <c>CR0011</c> — и через него клетки квадратов соседних жил (<c>terrain_day</c>
    /// Нежити: (127,124), (128,124), (128,125)); прежде (ponytail «квадраты не цепляются», 1254/0 на корпусе без гнёзд жил) порт их
    /// не красил, и у Кланов на кристалле <c>CR0011</c> вставал жезл, которого движок не ставит (день 59, реестр И152).</para></summary>
    public static IReadOnlyList<(int X, int Y)> WithCrystalSquares(IReadOnlyList<(int X, int Y)> cells,
                                                                   IReadOnlySet<(int X, int Y)> crystals,
                                                                   int size)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(crystals);
        var result = new List<(int X, int Y)>(cells);
        var seen = new HashSet<(int X, int Y)>(cells);
        if (crystals.Count == 0 || cells.Count == 0) return result;
        var tree = new SortedSet<(int Y, int X)>(cells.Select(c => (c.Y, c.X)));
        var cur = tree.Min;
        while (true)
        {
            foreach (var (dx, dy) in CrystalProbe)
            {
                var c = (X: cur.X + dx, Y: cur.Y + dy);
                if (!crystals.Contains(c)) continue;
                foreach (var (ex, ey) in CrystalProbe)
                {
                    var q = (X: c.X + ex, Y: c.Y + ey);
                    if (q.X < 0 || q.Y < 0 || q.X >= size || q.Y >= size) continue;
                    tree.Add((q.Y, q.X));
                    if (seen.Add(q)) result.Add(q);
                }
                break;
            }
            var max = tree.Max;
            if (cur.Y > max.Y || cur.Y == max.Y && cur.X >= max.X) break;
            cur = tree.GetViewBetween((cur.Y, cur.X + 1), max).Min;   // Min у вида — O(log n), Count обходил бы весь вид
        }
        return result;
    }

    /// <summary><c>0x5e0959</c>: клетку отвергает первая жила среди её восьми соседей, если первый
    /// жезл среди восьми соседей этой жилы — не источник обхода. Ранг 1 по листингу; в корпусе 499
    /// таких отказов объяснены этим правилом.</summary>
    public static bool NearForeignRodCrystal((int X, int Y) cell, IReadOnlySet<(int X, int Y)> crystals,
                                             IReadOnlyDictionary<(int X, int Y), string> rods,
                                             string sourceId)
    {
        foreach (var (dx, dy) in Around)
        {
            var c = (X: cell.X + dx, Y: cell.Y + dy);
            if (!crystals.Contains(c)) continue;
            foreach (var (ex, ey) in Around)
                if (rods.TryGetValue((c.X + ex, c.Y + ey), out var rod))
                    return !rod.Equals(sourceId, StringComparison.OrdinalIgnoreCase);
            return false;
        }
        return false;
    }

    private static void Expand(List<Entry> heap, int size, (int X, int Y) cell, int dist)
    {
        foreach (var (dx, dy) in Four)
        {
            int x = cell.X + dx, y = cell.Y + dy;
            if (x >= 0 && y >= 0 && x < size && y < size) Push(heap, new Entry(dist + 1, (x, y)));
        }
    }

    /// <summary><c>push_back</c> + <c>0x5e14db</c>: подъём, пока значение строго меньше родителя.</summary>
    private static void Push(List<Entry> heap, Entry value)
    {
        heap.Add(value);
        int hole = heap.Count - 1;
        while (hole > 0)
        {
            int parent = (hole - 1) / 2;
            if (value.Dist >= heap[parent].Dist) break;
            heap[hole] = heap[parent];
            hole = parent;
        }
        heap[hole] = value;
    }

    /// <summary><c>0x5e1546</c>/<c>0x5e1591</c> + <c>pop_back</c>.</summary>
    private static Entry Pop(List<Entry> heap)
    {
        var top = heap[0];
        var last = heap[^1];
        int n = heap.Count - 1;
        int hole = 0, child = 2;
        while (child < n)
        {
            if (heap[child - 1].Dist < heap[child].Dist) child--;
            heap[hole] = heap[child];
            hole = child;
            child = 2 * child + 2;
        }
        if (child == n)
        {
            heap[hole] = heap[child - 1];
            hole = child - 1;
        }
        while (hole > 0)
        {
            int parent = (hole - 1) / 2;
            if (last.Dist >= heap[parent].Dist) break;
            heap[hole] = heap[parent];
            hole = parent;
        }
        heap[hole] = last;
        heap.RemoveAt(n);
        return top;
    }
}

/// <summary>
/// Земля игрока по дням: каждый день каждый свой источник делает обход движка
/// (<see cref="LandWalk"/>), и модель знает множество клеток ТОЧНО.
///
/// <para><b>Источники и квоты (ранг 1, задача 7 Ф7).</b> Раз в сутки КАЖДЫЙ свой источник делает
/// свой обход со своей квотой в клетках: столица <c>T_CAPITAL</c> = 10, деревня
/// <c>T_CITY[SIZE]</c> = 15/15/20/20/25 по уровню В ДЕНЬ ОБХОДА (<see cref="LandSource.Steps"/>), свой
/// жезл <c>ROD_RANGE</c> = 2. Квоты складываются, своя клетка квоту не тратит, предела расстояния
/// нет. Обходы идут по возрастанию id объекта (<c>CEffectTerrainCity::apply</c> <c>0x43ed42</c>, один
/// проход по коллекции объектов игрока; 210/0 пачек), и каждый видит перекраску предыдущего.</para>
///
/// <para><b>Приобретение источника.</b> В день, когда источник стал нашим, он даёт свои клетки
/// (<see cref="LandSource.Own"/>) плюс ОДИН обход на <see cref="LandQuotas.Acquisition"/>, а дневной
/// обход того же дня не делает. Своё пятно перекрашивается ДО обхода: у жезла — через визитор с
/// квадратом жилы (<c>0x4377b2</c> → <c>0x5da43f</c> → <c>0x5e6ed7</c>), у укрепления — без него (на
/// корпусе иначе расходится захват ai-q22). Приобретения дня идут после дневных обходов: захват и
/// постановка — действия внутри хода, дневной рост — его начало.</para>
///
/// <para><b>Приём клетки <c>0x5e07b2</c> (ранг 1).</b> Отвергают: вне карты; вода и горы; ЛЮБОЙ жезл,
/// кроме самого источника (201 отказ корпуса — свои жезлы); жила; <c>0x5e0959</c>
/// (<see cref="LandWalk.NearForeignRodCrystal"/>); укрепление, чей владелец не пуст и не нейтрал,
/// кроме самого источника (2628 отказов — свои укрепления); ландмарк с <c>GLmark.MOUNTAIN</c>.
/// Чужой террейн не отвергается — спорную клетку забирает выросший позже (п. 8).
/// <i>Было иначе:</i> «чужой жезл», «чужое укрепление».</para>
///
/// <para><b>Жила (ранг 1).</b> Перекраска любой клетки рядом с жилой перекрашивает весь её квадрат
/// 3×3 (<see cref="LandWalk.WithCrystalSquares"/>), сверх квоты. Прогон Ф6 (<c>20260908-f6</c>,
/// <c>terrain_day</c>): все 59 дней совпали множеством клеток, включая день захвата жилы (12,20).
/// <i>Было иначе:</i> «день, когда занята половина кольца» (ранг 3).</para>
///
/// <para>Жезлы-источники стоят на карте с дня после <see cref="LandSource.Since"/> по
/// <see cref="LandSource.Until"/> включительно (в день постановки дневные обходы идут раньше неё).</para>
///
/// <para>ponytail: в квадрат жилы берутся только проходимые клетки — прежнее правило порта;
/// непроходимой клетки в захваченном квадрате корпус не видел (ранг 3). Жезлы стартовой карты
/// (<see cref="BlockedCells"/>) отвергаются и после сноса — потолок: снесённый жезл, через клетку
/// которого рост пошёл бы дальше; доработка — снимать его клетку с <see cref="LandSource.Until"/>.</para>
/// </summary>
public sealed class LandModel
{
    /// <summary>Дневная квота СТОЛИЦЫ, <c>GVars.T_CAPITAL</c>. Ранг 1: 57 дней из 58 прогона
    /// 20260908-f6 дали ровно столько, и все 19 первых ходов <c>m6-rods</c> до первого жезла.</summary>
    public const int CellsPerDay = 10;

    /// <summary>Полурадиус квадрата, который переводит на свою землю жила. Ранг 1: <c>0x60ba78</c>.</summary>
    public const int NodeBlockRadius = 1;

    /// <summary>Смещение центра земли укрепления от его <c>POS</c>: <c>POS + size/2</c>
    /// (<c>0x5da24b…0x5da26c</c>), у деревни 4/2 и у столицы 5/2 — оба 2. Ранг 1.</summary>
    public const int FortCentreOffset = 2;

    /// <summary>Класс id жилы в идентификаторе объекта (<c>S001<b>CR</b>0003</c>).</summary>
    private const string CrystalKind = "CR";

    /// <summary>Раса нейтралов (<c>Grace.dbf</c>). Ранг 2: <c>0x5e5358</c> пропускает укрепление без
    /// владельца или с владельцем вне списка игроков, дальше решает <c>0x5e53a6</c> по категории расы
    /// (не дочитана); на корпусе «отвергнуть, если раса не нейтральная» объяснило 2654 отказа из 2654
    /// опознанных.</summary>
    private const string NeutralRace = "G000RR0004";

    private readonly MapState _map;
    private readonly HashSet<(int X, int Y)> _start;
    private readonly IReadOnlyList<LandSource> _sources;
    private readonly IReadOnlyList<LandSource> _byId;
    private readonly Dictionary<string, HashSet<(int X, int Y)>> _ownOf;
    private readonly IReadOnlySet<(int X, int Y)> _blocked;
    private readonly HashSet<(int X, int Y)> _crystals;
    private readonly List<(LandSource? Source, (int X, int Y) Cell, string Id)> _rods;
    private readonly LandQuotas _quotas;
    private readonly List<HashSet<(int X, int Y)>> _days = [];      // индекс = день−1
    private readonly int _lastEvent;                                 // последний день приобретения, сноса или ступени
    private bool _frozen;                                            // последний посчитанный день ничего не добавил

    /// <summary>Центр роста первого источника — для прогона Ф6 это центр столицы.</summary>
    public (int X, int Y) Centre => _sources[0].Centre;

    /// <summary>Клеток своей земли на старте (пятно столицы в файле карты).</summary>
    public int StartCells => _start.Count;

    /// <summary>Сколько клеток вообще может занять рост: проходимые, не отвергнутые и связные со
    /// стартовым пятном.</summary>
    public int Reachable { get; }

    private LandModel(MapState map, HashSet<(int X, int Y)> start, IReadOnlyList<LandSource> sources,
                      LandQuotas quotas, IReadOnlySet<(int X, int Y)> blocked)
    {
        _map = map;
        _start = start;
        _sources = sources;
        _byId = [.. sources.OrderBy(s => s.Id, StringComparer.Ordinal)];
        _ownOf = sources.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(s => s.Own).ToHashSet(), StringComparer.OrdinalIgnoreCase);
        _quotas = quotas;
        _blocked = blocked;
        _crystals = CrystalsOf(map);
        _rods = RodsOf(map, sources);
        _lastEvent = sources.Select(s => Math.Max(Math.Max(s.Since, s.Until == int.MaxValue ? 1 : s.Until),
                                                   (s.Steps ?? []).Select(t => t.From).DefaultIfEmpty(1).Max()))
                            .Append(1).Max();
        Reachable = CountReachable();
    }

    /// <summary>Центр земли укрепления: <c>POS + (2, 2)</c> (<see cref="FortCentreOffset"/>).</summary>
    public static (int X, int Y) FortCentre(int x, int y) => (x + FortCentreOffset, y + FortCentreOffset);

    /// <summary>
    /// Собирает модель по стартовой карте: своя земля игрока и все его источники — свои укрепления
    /// из <see cref="MapState.Sites"/> (§3 п. 26) и свои жезлы.
    /// </summary>
    /// <param name="quotas"><c>GVars</c> (<c>T_CAPITAL</c>, <c>T_CITY1…5</c>, <c>ROD_RANGE</c>); null — ванильные.</param>
    /// <param name="mountainLandmarks">Типы ландмарков с <c>MOUNTAIN = T</c> — их клетки обход отвергает
    /// (<see cref="BlockedCells"/>); null — ландмарки не проверяются.</param>
    /// <exception cref="ArgumentException">Нет такого игрока, его расы в <c>Grace.dbf</c>, или у
    /// игрока на карте нет ни одной клетки своей земли (расти неоткуда).</exception>
    public static LandModel Build(MapState map, string playerId, EconomyRules rules, LandQuotas? quotas = null,
                                  IReadOnlySet<string>? mountainLandmarks = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        if (!map.Players.TryGetValue(playerId, out var player))
            throw new ArgumentException($"игрока {playerId} нет на карте", nameof(playerId));
        if (!rules.Races.TryGetValue(player.RaceId, out var race))
            throw new ArgumentException($"расы {player.RaceId} нет в Grace.dbf", nameof(playerId));

        int terrain = race.Terrain;
        // Без quotas (MapSim) — ванильные квоты; партия подаёт свои из GVars (GameBook.Land).
        var q = quotas ?? LandQuotas.Vanilla;
        bool Mine(string owner) => owner.Equals(playerId, StringComparison.OrdinalIgnoreCase);
        var forts = map.Sites
            .Where(s => (s.Kind is SiteKind.Capital or SiteKind.Village) && Mine(s.Owner))
            .OrderBy(s => s.Kind == SiteKind.Capital ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s =>
            {
                var centre = FortCentre(s.X, s.Y);
                var own = PlanCells(map, s.Id);
                return (Capital: s.Kind == SiteKind.Capital,
                        Source: new LandSource(s.Id, centre,
                                               s.Kind == SiteKind.Capital ? q.Capital : q.Village(s.Size ?? 1),
                                               own.Count > 0 ? own : [centre]));
            })
            .ToList();
        // ponytail: уровень деревни в прогоне модели не растёт — рост города (трата) MapSim не считает,
        // поэтому ступеней квоты здесь нет. Потолок: купленный рост деревни на маршруте.
        var rods = map.Rods.Values
            .Where(r => Mine(r.Owner))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new LandSource(r.Id, (r.X, r.Y), q.RodRange, [(r.X, r.Y)]));
        List<LandSource> others = [.. forts.Where(f => !f.Capital).Select(f => f.Source), .. rods];
        var capital = forts.Where(f => f.Capital).Select(f => f.Source).FirstOrDefault();
        var blocked = BlockedCells(map, map.Sites, mountainLandmarks);
        return capital is not null
            ? BuildSources(map, terrain, [capital, .. others], q, playerId, blocked)
            : Build(map, terrain, others, q, playerId, blocked);
    }

    /// <summary>
    /// То же по номеру своего террейна (<c>Lterrain.ID</c>) — без справочников: журнал зонда
    /// печатает его прямо в <c>terrain_day.terrain</c>, и харнессу роста <c>Grace.dbf</c> не нужен.
    /// </summary>
    /// <exception cref="ArgumentException">На карте нет ни одной клетки этого террейна.</exception>
    public static LandModel Build(MapState map, int ownTerrain, string who = "игрока") =>
        Build(map, ownTerrain, [], null, who);

    /// <summary>
    /// Сборка со синтетической столицей: стартовое пятно своей земли плюс <paramref name="extra"/>.
    /// Первым источником берётся столица с центром в центре тяжести стартового пятна и квотой
    /// <see cref="LandQuotas.Capital"/> — для одной столицы это ровно её центр (прогон Ф6: пятно 5×5 в
    /// 5..9 → (7,7)), для пятна из нескольких укреплений центр тяжести смысла не имеет, и тогда
    /// источники подаются целиком (<see cref="BuildSources"/>). Id столицы пустой — она идёт первой
    /// и в дневном порядке.
    /// </summary>
    /// <param name="extra">Деревни и жезлы; столицу подавать не нужно.</param>
    /// <param name="quotas"><c>GVars</c>; null — ванильные значения.</param>
    /// <param name="blocked">Клетки, которые обход отвергает (<see cref="BlockedCells"/>);
    /// null — <see cref="BlockedCells"/> по самой карте без ландмарков.</param>
    /// <exception cref="ArgumentException">На карте нет ни одной клетки этого террейна.</exception>
    public static LandModel Build(MapState map, int ownTerrain, IReadOnlyList<LandSource> extra,
                                  LandQuotas? quotas = null, string who = "игрока",
                                  IReadOnlySet<(int X, int Y)>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(extra);
        var start = StartSpot(map, ownTerrain, who);
        var quota = quotas ?? LandQuotas.Vanilla;
        var centre = (X: start.Sum(c => c.X) / start.Count, Y: start.Sum(c => c.Y) / start.Count);
        List<LandSource> sources = [new("", centre, quota.Capital, [.. start]), .. extra];
        return BuildSources(map, ownTerrain, sources, quota, who, blocked);
    }

    /// <summary>
    /// Полная сборка: стартовое пятно своей земли и ВЕСЬ список источников, столицу включая.
    /// Первый источник — тот, к которому привязан <see cref="Centre"/>; порядок обходов — по id.
    /// </summary>
    /// <exception cref="ArgumentException">На карте нет ни одной клетки этого террейна, или список
    /// источников пуст.</exception>
    public static LandModel BuildSources(MapState map, int ownTerrain, IReadOnlyList<LandSource> sources,
                                         LandQuotas quotas, string who = "игрока",
                                         IReadOnlySet<(int X, int Y)>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(quotas);
        if (sources.Count == 0) throw new ArgumentException("список источников пуст", nameof(sources));
        var start = StartSpot(map, ownTerrain, who);
        return new LandModel(map, start, sources, quotas, blocked ?? BlockedCells(map, map.Sites));
    }

    /// <summary>Стартовое пятно — все клетки своего террейна на карте.</summary>
    /// <exception cref="ArgumentException">Ни одной такой клетки нет: расти неоткуда.</exception>
    private static HashSet<(int X, int Y)> StartSpot(MapState map, int ownTerrain, string who)
    {
        ArgumentNullException.ThrowIfNull(map);
        var start = new HashSet<(int X, int Y)>();
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
                if (map.Tiles[x, y].Terrain == ownTerrain) start.Add((x, y));
        if (start.Count == 0)
            throw new ArgumentException($"у {who} (террейн {ownTerrain}) на карте нет своей земли",
                                        nameof(ownTerrain));
        return start;
    }

    /// <summary>
    /// Клетки РАЗОВОГО обхода приобретения источника в порядке движка: сначала его собственные
    /// клетки (<see cref="LandSource.Own"/>, у жезла — с квадратом жилы), которых у нас ещё нет, потом
    /// обход на <see cref="LandQuotas.Acquisition"/> клеток (<c>land_grow.cells</c>). Квадраты жил,
    /// которые перекрасит сам обход, сюда не входят. Это и «награда за захват» деревни, и «+3 сразу» у
    /// жезла — одно правило.
    /// </summary>
    /// <param name="blocked">null — <see cref="BlockedCells"/> по самой карте без ландмарков.</param>
    public static IReadOnlyList<(int X, int Y)> AcquisitionCells(
        MapState map, IReadOnlySet<(int X, int Y)> owned, LandSource source, LandQuotas quotas,
        IReadOnlySet<(int X, int Y)>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(quotas);
        var model = new LandModel(map, [.. owned], [source], quotas, blocked ?? BlockedCells(map, map.Sites));
        var mine = new HashSet<(int X, int Y)>(owned);
        var own = model.Recolor(source).ToList();
        var cells = own.Where(c => !owned.Contains(c)).ToList();
        mine.UnionWith(own);
        var rods = model.RodsOn(source.Since);
        if (source.IsRod) rods[source.Own[0]] = source.Id;     // поставленный жезл уже стоит
        cells.AddRange(model.Walk(mine, source, quotas.Acquisition(source.QuotaOn(source.Since)), rods));
        return cells;
    }

    /// <summary>
    /// Вся перекраска разового обхода приобретения (<see cref="AcquisitionCells"/>) — с квадратами жил, которые перекрашивает сам обход
    /// (<see cref="LandWalk.WithCrystalSquares"/>, проходимые клетки), как у дневного обхода (<c>Grow</c>): визитор перекраски
    /// <c>CVisitorMapTerrain::apply</c> <c>0x60b966</c> один и для дневного роста, и для захвата деревни, и для жезла. Ранг 1 — журнал карты 23
    /// (<c>20261020-port-synth</c>, день 55): захват <c>S143FT000b</c> Легионами — клетки у жил рядом с обходом (<c>terrain_day</c> Нежити
    /// того же дня их уже не числит); прежде порт красил только список обхода (реестр И152). Клетки — без повторов и без уже своих.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> AcquisitionLand(
        MapState map, IReadOnlySet<(int X, int Y)> owned, LandSource source, LandQuotas quotas,
        IReadOnlySet<(int X, int Y)>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(quotas);
        var model = new LandModel(map, [.. owned], [source], quotas, blocked ?? BlockedCells(map, map.Sites));
        var mine = new HashSet<(int X, int Y)>(owned);
        var land = model.Recolor(source).ToList();
        mine.UnionWith(land);
        var rods = model.RodsOn(source.Since);
        if (source.IsRod) rods[source.Own[0]] = source.Id;
        var walk = model.Walk(mine, source, quotas.Acquisition(source.QuotaOn(source.Since)), rods);
        land.AddRange(model.Passable(LandWalk.WithCrystalSquares(walk, model._crystals, map.Size)));
        return [.. land.Distinct().Where(c => !owned.Contains(c))];
    }

    /// <summary>
    /// Клетки, которые обход отвергает сверх воды и гор: жезлы, жилы, укрепления не нейтральных
    /// владельцев и блокирующие ландмарки. Свой жезл и своё укрепление сюда входят — движок отвергает и
    /// их; исключение только для клеток самого источника, и его делает модель (ранг 1, описание класса).
    /// <para><b>Блокирующий ландмарк — <c>GLmark.MOUNTAIN = T</c></b>, категория (<c>LLMCat</c>) ни при чём.
    /// Ранг 1 по синтетической карте ai-q25 (4/0): Кладбище (T, категория 0) — 45 отказов, Плато (T, 3) —
    /// 32, ни одной клетки; Водоворот и Лавовая яма (F, 3) — клетки взяты. Отвергаются и свои клетки под
    /// такими ландмарками. <i>Было иначе:</i> «признак блокировки из <c>LMarkCat</c>».</para>
    /// </summary>
    /// <param name="mountainLandmarks">Типы ландмарков с <c>MOUNTAIN = T</c>
    /// (<c>D2.Data.Map.EconomyLoader.LoadMountainLandmarks</c>, регистр не важен — набор с
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>). <c>null</c> — ландмарки не проверяются.</param>
    public static IReadOnlySet<(int X, int Y)> BlockedCells(MapState map, IReadOnlyList<MapSite> sites,
                                                            IReadOnlySet<string>? mountainLandmarks = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        var blocked = new HashSet<(int X, int Y)>();
        foreach (var r in map.Rods.Values) blocked.Add((r.X, r.Y));
        foreach (var o in map.Objects.Values)
            if (o.Kind.Equals(CrystalKind, StringComparison.OrdinalIgnoreCase)) blocked.Add((o.X, o.Y));
            else if (mountainLandmarks is not null && mountainLandmarks.Contains(o.TypeId))
                blocked.UnionWith(PlanCells(map, o.Id));
        foreach (var s in sites)
        {
            if (s.Kind is not (SiteKind.Capital or SiteKind.Village)) continue;
            if (Neutral(map, s.Owner)) continue;
            blocked.UnionWith(PlanCells(map, s.Id));
        }
        return blocked;
    }

    /// <summary><c>0x5e5358</c>: владелец пуст, не найден среди игроков или нейтрал.</summary>
    private static bool Neutral(MapState map, string owner)
    {
        if (owner.Length == 0) return true;
        var player = map.Players.TryGetValue(owner, out var p) ? p
            : map.Players.Values.FirstOrDefault(x => x.Id.Equals(owner, StringComparison.OrdinalIgnoreCase));
        return player is null || player.RaceId.Equals(NeutralRace, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Клетки объекта по занятости карты (<c>CMidgardPlan</c>): 25 у столицы, 16 у
    /// деревни, 1 у жезла. Ранг 1 — поле файла.</summary>
    public static IReadOnlyList<(int X, int Y)> PlanCells(MapState map, string objectId)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. PlanIndex.CellsOf(map, objectId)];
    }

    /// <summary>Манхэттенское расстояние от центра — сверка колец в харнессах.</summary>
    public static int Distance((int X, int Y) a, (int X, int Y) b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>Сколько клеток своей земли у игрока на день <paramref name="day"/> (день 1 — старт).</summary>
    public int OwnedCells(int day) => State(day).Count;

    /// <summary>Своя земля на конец дня <paramref name="day"/> — точно (§3 п. 7 закрыт).</summary>
    public IReadOnlySet<(int X, int Y)> Certain(int day) => State(day);

    /// <summary>То же, что <see cref="Certain"/>: неизвестного порядка внутри кольца больше нет. Оставлено
    /// ради харнессов, сверяющих верхнюю границу.</summary>
    public IReadOnlySet<(int X, int Y)> Possible(int day) => State(day);

    /// <summary>День, с которого клетка жилы <paramref name="at"/> — своя земля (её перекрашивает
    /// собственный квадрат); <c>null</c> — жилы здесь нет или рост до неё не дошёл.</summary>
    public int? NodeCaptureDay((int X, int Y) at)
    {
        if (!_crystals.Contains(at)) return null;
        for (int day = 1; day <= MaxDay; day++)
            if (State(day).Contains(at)) return day;
        return null;
    }

    /// <summary>Дальше этого дня земля не меняется: рост упёрся, новых источников и ступеней квоты нет.</summary>
    public int MaxDay
    {
        get
        {
            State(_lastEvent + _map.Size * _map.Size + 1);
            return _frozen ? _days.Count - 1 : _days.Count;
        }
    }

    /// <summary>Все клетки квадратов жил карты — то, что достаётся игроку не обходом, а правилом
    /// жилы. Нужно проверке порядка колец: эти клетки кольцевой порядок и нарушают.</summary>
    public IReadOnlySet<(int X, int Y)> NodeCells
    {
        get
        {
            var cells = new HashSet<(int X, int Y)>();
            foreach (var (x, y) in _crystals)
                for (int dx = -NodeBlockRadius; dx <= NodeBlockRadius; dx++)
                    for (int dy = -NodeBlockRadius; dy <= NodeBlockRadius; dy++)
                        if (_map.Contains(x + dx, y + dy) && _map.Tiles[x + dx, y + dy].Passable)
                            cells.Add((x + dx, y + dy));
            return cells;
        }
    }

    /// <summary>Состояние на день; дни считаются по порядку и кешируются. Счёт останавливается на первом
    /// дне, который ничего не добавил, когда событий впереди нет: дальше земля та же.</summary>
    private HashSet<(int X, int Y)> State(int day)
    {
        if (day < 1) throw new ArgumentOutOfRangeException(nameof(day), day, "день начинается с 1");
        while (_days.Count < day && !_frozen)
        {
            _days.Add(Advance(_days.Count + 1));
            int n = _days.Count;
            _frozen = n > _lastEvent && n >= 2 && _days[n - 1].Count == _days[n - 2].Count;
        }
        return _days[Math.Min(day, _days.Count) - 1];
    }

    /// <summary>Один день поверх предыдущего: дневные обходы по id, потом приобретения дня.</summary>
    private HashSet<(int X, int Y)> Advance(int day)
    {
        if (day == 1) return new HashSet<(int X, int Y)>(_start);
        var owned = new HashSet<(int X, int Y)>(_days[day - 2]);
        var rods = RodsOn(day);
        foreach (var s in _byId)
            if (s.Since < day && s.Until >= day) Grow(owned, s, s.QuotaOn(day), rods, day);
        foreach (var s in _byId)
            if (s.Since == day)
            {
                if (s.IsRod) rods[s.Own[0]] = s.Id;
                owned.UnionWith(Recolor(s));
                Grow(owned, s, _quotas.Acquisition(s.QuotaOn(day)), rods, day);
            }
        return owned;
    }

    private readonly Dictionary<int, List<(LandSource Source, IReadOnlyList<(int X, int Y)> Cells)>> _walks = [];

    /// <summary>Обходы дня <paramref name="day"/> по источникам — клетки в порядке взятия (<c>0x5e0551</c>, без квадратов жил): у движка
    /// каждый обход — группа сообщения 27 (<c>CEffectTerrainCity::apply</c> <c>0x43ed42</c>), анимация <c>CMidAnim2Terrain</c> снимает
    /// клетки группы с начала. Только для показа — счёт земли от этого не зависит.</summary>
    public IReadOnlyList<(LandSource Source, IReadOnlyList<(int X, int Y)> Cells)> Walks(int day)
    {
        State(day);
        return _walks.TryGetValue(day, out var walks) ? walks : [];
    }

    /// <summary>Своё пятно источника в день приобретения: у жезла — с квадратом жилы, у укрепления — без.</summary>
    private IEnumerable<(int X, int Y)> Recolor(LandSource s) =>
        s.IsRod ? Passable(LandWalk.WithCrystalSquares(s.Own, _crystals, _map.Size)) : s.Own;

    private void Grow(HashSet<(int X, int Y)> owned, LandSource s, int quota,
                      IReadOnlyDictionary<(int X, int Y), string> rods, int day)
    {
        var walk = Walk(owned, s, quota, rods);
        if (!_walks.TryGetValue(day, out var walks)) _walks[day] = walks = [];
        walks.Add((s, walk));
        owned.UnionWith(Passable(LandWalk.WithCrystalSquares(walk, _crystals, _map.Size)));
    }

    private IReadOnlyList<(int X, int Y)> Walk(HashSet<(int X, int Y)> owned, LandSource s, int quota,
                                               IReadOnlyDictionary<(int X, int Y), string> rods) =>
        LandWalk.Walk(_map.Size, s.Centre, quota, owned.Contains, c => Rejected(c, s, rods));

    /// <summary><c>0x5e07b2</c> (описание класса).</summary>
    private bool Rejected((int X, int Y) c, LandSource s, IReadOnlyDictionary<(int X, int Y), string> rods) =>
        !_map.Tiles[c.X, c.Y].Passable
        || (_blocked.Contains(c) && !_ownOf[s.Id].Contains(c))
        || (rods.TryGetValue(c, out var rod) && !rod.Equals(s.Id, StringComparison.OrdinalIgnoreCase))
        || LandWalk.NearForeignRodCrystal(c, _crystals, rods, s.Id);

    /// <summary>Жезлы, стоящие на карте во время дневных обходов дня <paramref name="day"/>.</summary>
    private Dictionary<(int X, int Y), string> RodsOn(int day)
    {
        var rods = new Dictionary<(int X, int Y), string>();
        foreach (var (source, cell, id) in _rods)
            if (source is null || (source.Since < day && source.Until >= day)) rods[cell] = id;
        return rods;
    }

    private IEnumerable<(int X, int Y)> Passable(IEnumerable<(int X, int Y)> cells) =>
        cells.Where(c => _map.Tiles[c.X, c.Y].Passable);

    /// <summary>Обход в ширину от стартового пятна; «видели» — плотный массив по номеру клетки y·Size + x, а не множество пар
    /// (perf-log Г3; gdscript-knowledge performance.md «Когда переносить код в C#…»).</summary>
    private int CountReachable()
    {
        int size = _map.Size;
        var seen = new bool[size * size];
        foreach (var (x, y) in _start)
            if (_map.Contains(x, y)) seen[y * size + x] = true;
        var queue = new Queue<(int X, int Y)>(_start);
        int count = 0;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            count += Visit(x, y - 1) + Visit(x, y + 1) + Visit(x - 1, y) + Visit(x + 1, y);
        }
        return count;

        int Visit(int nx, int ny)
        {
            if (!_map.Contains(nx, ny) || seen[ny * size + nx]) return 0;
            seen[ny * size + nx] = true;
            if (!_map.Tiles[nx, ny].Passable || _blocked.Contains((nx, ny))) return 0;
            queue.Enqueue((nx, ny));
            return 1;
        }
    }

    private static HashSet<(int X, int Y)> CrystalsOf(MapState map) =>
        map.Objects.Values.Where(o => o.Kind.Equals(CrystalKind, StringComparison.OrdinalIgnoreCase))
            .Select(o => (o.X, o.Y)).ToHashSet();

    /// <summary>Жезлы стартовой карты (стоят всегда) и жезлы-источники (стоят по своим дням); жезл,
    /// который есть и там и там, берётся как источник.</summary>
    private static List<(LandSource? Source, (int X, int Y) Cell, string Id)> RodsOf(
        MapState map, IReadOnlyList<LandSource> sources)
    {
        var rodSources = sources.Where(s => s.IsRod).ToList();
        var ids = rodSources.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. map.Rods.Values.Where(r => !ids.Contains(r.Id)).Select(r => ((LandSource?)null, (r.X, r.Y), r.Id)),
            .. rodSources.Select(s => ((LandSource?)s, s.Own[0], s.Id)),
        ];
    }
}
