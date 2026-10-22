namespace D2.Rules.Rng;

/// <summary>Источник бросков. В бою — EngineRng; в тестах С2 подставляется записанная
/// последовательность (ScriptedRng), и тогда бой обязан повторить игровой шаг в шаг (ТЗ §6 Ф4).</summary>
public interface IRng
{
    /// <summary>Целое из [0, maxValue). maxValue == 0 даёт 0 и не тратит бросок — так ведёт себя
    /// обёртка движка 0x54851f.</summary>
    int Next(int maxValue);
}

/// <summary>Броски из журнала. Кончились — исключение, не тихий ноль.</summary>
public sealed class ScriptedRng(IEnumerable<int> draws) : IRng
{
    private readonly IEnumerator<int> _draws = draws.GetEnumerator();
    public int Used { get; private set; }

    public int Next(int maxValue)
    {
        if (maxValue == 0) return 0;
        if (!_draws.MoveNext()) throw new InvalidOperationException($"броски кончились после {Used}");
        Used++;
        return _draws.Current;
    }
}
