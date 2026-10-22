namespace D2.Data.Gfx;

/// <summary>Земля карты кусками: мир делится на квадраты <see cref="ChunkSize"/>×<see cref="ChunkSize"/> мировых
/// точек, каждый собирается <see cref="GroundComposer"/> по требованию и держится в кэше последних
/// <see cref="Capacity"/> кусков. Замена кэша вида движка (поверхности 128×128, <c>docs/ground-render.md</c> §1):
/// содержимое точки зависит только от мира, поэтому сетка кусков привязана к миру, а не к положению вида, и при
/// прокрутке уже собранные куски не пересобираются.
///
/// <para>Сборка идёт в нескольких фоновых потоках (вид карты), а <see cref="Clear"/> зовёт главный (туман игрока изменился),
/// поэтому кэш под замком; сама сборка куска — вне замка (сборщик и его картинки потокобезопасны).</para></summary>
public sealed class GroundChunks
{
    private GroundComposer _composer;
    private readonly Dictionary<(int X, int Y), LinkedListNode<((int X, int Y) Key, RgbaImage Image)>> _cache = new();
    private readonly Dictionary<(int X, int Y), byte[]> _inputs = new();

    /// <summary>Поколение кэша: кусок, начатый до <see cref="Refresh"/> или <see cref="Clear"/>, в кэш не кладётся — он мог
    /// собраться по старому туману.</summary>
    private int _generation;
    private readonly LinkedList<((int X, int Y) Key, RgbaImage Image)> _recent = new();
    private readonly object _lock = new();

    public GroundChunks(GroundComposer composer, int chunkSize = 256, int capacity = 64)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, GroundGeometry.CellWidth);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _composer = composer;
        ChunkSize = chunkSize;
        Capacity = capacity;
    }

    public int ChunkSize { get; }

    public int Capacity { get; }

    /// <summary>Сколько кусков собрано с начала (для проверок: повторный запрос из кэша не собирает).</summary>
    public int Composed { get; private set; }

    public int Cached
    {
        get { lock (_lock) return _cache.Count; }
    }

    /// <summary>Мировой прямоугольник всей карты стороной <paramref name="size"/> клеток: ромб от клетки
    /// <c>(0, N−1)</c> слева до <c>(N−1, 0)</c> справа, от <c>(0, 0)</c> сверху до <c>(N−1, N−1)</c> снизу.</summary>
    public static (int Left, int Top, int Width, int Height) MapBounds(int size) =>
        (-(size - 1) * 32, 0, size * 64, size * 32);

    /// <summary>Куски, задевающие мировой прямоугольник вида, по строкам сверху вниз, слева направо.</summary>
    public IEnumerable<(int X, int Y)> Covering(int left, int top, int width, int height)
    {
        int x0 = FloorDiv(left, ChunkSize), x1 = FloorDiv(left + width - 1, ChunkSize);
        int y0 = FloorDiv(top, ChunkSize), y1 = FloorDiv(top + height - 1, ChunkSize);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return (x, y);
    }

    /// <summary>Мировой левый верхний угол куска.</summary>
    public (int Left, int Top) Origin((int X, int Y) chunk) => (chunk.X * ChunkSize, chunk.Y * ChunkSize);

    public bool IsCached((int X, int Y) chunk)
    {
        lock (_lock) return _cache.ContainsKey(chunk);
    }

    /// <summary>Картинка куска: из кэша или собранная сейчас; самый давно не нужный кусок вытесняется.</summary>
    /// <remarks>Замок здесь не конкурирует, когда сборка в одном потоке (<c>Monitor.LockContentionCount</c> — 0), и стоит десятки
    /// наносекунд. <c>Monitor.Enter_Slowpath</c> под <c>Get</c> в профиле dotnet-trace (14.10: ~7 % снимков) — это сэмплер, а не замок:
    /// отдельная однопоточная сборка без единой конкуренции даёт ту же долю.</remarks>
    public RgbaImage Get((int X, int Y) chunk)
    {
        GroundComposer composer;
        int generation;
        lock (_lock)
        {
            if (_cache.TryGetValue(chunk, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                return node.Value.Image;
            }
            (composer, generation) = (_composer, _generation);
        }
        var (left, top) = Origin(chunk);
        var inputs = composer.Inputs(left, top, ChunkSize, ChunkSize);
        var image = composer.Render(left, top, ChunkSize, ChunkSize);
        lock (_lock)
        {
            Composed++;
            if (_cache.ContainsKey(chunk) || generation != _generation) return image;
            _cache[chunk] = _recent.AddFirst((chunk, image));
            _inputs[chunk] = inputs;
            if (_cache.Count > Capacity)
            {
                var last = _recent.Last!;
                _recent.RemoveLast();
                _cache.Remove(last.Value.Key);
                _inputs.Remove(last.Value.Key);
            }
        }
        return image;
    }

    /// <summary>Забыть собранное: земля зависит от тумана игрока, а он меняется по ходу партии.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _recent.Clear();
            _inputs.Clear();
            _generation++;
        }
    }

    /// <summary>Туман или клетки изменились: оставить только куски, чьи входы (<see cref="GroundComposer.Inputs"/>) те же, — их картинка
    /// та же; прочие забыть. <paramref name="composer"/> — новый сборщик (перекрашены клетки), null — прежний. Возвращает оставленные
    /// куски.</summary>
    public IReadOnlySet<(int X, int Y)> Refresh(GroundComposer? composer = null)
    {
        List<((int X, int Y) Key, byte[]? Old)> entries;
        GroundComposer current;
        lock (_lock)
        {
            if (composer is not null) _composer = composer;
            _generation++;
            current = _composer;
            entries = [.. _recent.Select(n => (n.Key, _inputs.GetValueOrDefault(n.Key)))];
        }
        // Входы считаются вне замка — фоновая сборка кусков не ждёт; поколение уже сменилось, начатое до него в кэш не ляжет.
        var kept = new HashSet<(int X, int Y)>();
        foreach (var (key, old) in entries)
        {
            var (left, top) = Origin(key);
            if (old is not null && old.AsSpan().SequenceEqual(current.Inputs(left, top, ChunkSize, ChunkSize))) kept.Add(key);
        }
        lock (_lock)
        {
            foreach (var (key, _) in entries.Where(e => !kept.Contains(e.Key)))
                if (_cache.Remove(key, out var node))
                {
                    _recent.Remove(node);
                    _inputs.Remove(key);
                }
        }
        return kept;
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
