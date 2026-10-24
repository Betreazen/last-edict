namespace LastEdict.Data.Gfx;

/// <summary>
/// Кэш с пределом по байтам и вытеснением давно не нужного (LRU): текстуры графики карты и боя (<c>GfxTextures</c>) и картинки кадров
/// интерфейса (<c>UiArt</c>) — с HD (×4, Р19) прежний сброс целиком заставлял следующий кадр разбирать всё заново. Вытесненное и
/// заменённое кэш отдаёт вызывающему: освобождать ли его (живой спрайт может ещё держать текстуру), решает он. Не потокобезопасен —
/// трогает один поток (главный).
/// </summary>
public sealed class ByteLru<TKey, TValue>(long limitBytes, IEqualityComparer<TKey>? comparer = null) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, long Bytes)>> _nodes = new(comparer);

    /// <summary>Порядок давности: в начале — последнее нужное, с конца вытесняется.</summary>
    private readonly LinkedList<(TKey Key, TValue Value, long Bytes)> _order = new();

    /// <summary>Предел по байтам.</summary>
    public long LimitBytes { get; } = limitBytes;

    public int Count => _nodes.Count;

    /// <summary>Байт в кэше.</summary>
    public long Bytes { get; private set; }

    /// <summary>Значения, от последнего нужного к самому давнему.</summary>
    public IEnumerable<TValue> Values => _order.Select(n => n.Value);

    /// <summary>Значение по ключу; найдено — становится последним нужным.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (!_nodes.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }
        _order.Remove(node);
        _order.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>Есть ли ключ (давность не меняется).</summary>
    public bool ContainsKey(TKey key) => _nodes.ContainsKey(key);

    /// <summary>Положить значение размером <paramref name="bytes"/>: прежнее по тому же ключу и самые давние сверх предела уходят из кэша —
    /// они и возвращаются (освободить — вызывающему). Значение больше предела всё равно кладётся: кэш лишь не держит ничего, кроме него.</summary>
    public IReadOnlyList<TValue> Add(TKey key, TValue value, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var gone = new List<TValue>();
        if (_nodes.Remove(key, out var stale))
        {
            _order.Remove(stale);
            Bytes -= stale.Value.Bytes;
            gone.Add(stale.Value.Value);
        }
        while (Bytes + bytes > LimitBytes && _order.Last is { } oldest)
        {
            _order.RemoveLast();
            _nodes.Remove(oldest.Value.Key);
            Bytes -= oldest.Value.Bytes;
            gone.Add(oldest.Value.Value);
        }
        _nodes[key] = _order.AddFirst((key, value, bytes));
        Bytes += bytes;
        return gone;
    }

    /// <summary>Забыть всё.</summary>
    public void Clear()
    {
        _nodes.Clear();
        _order.Clear();
        Bytes = 0;
    }
}
