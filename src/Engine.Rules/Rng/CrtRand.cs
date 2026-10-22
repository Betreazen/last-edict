namespace D2.Rules.Rng;

/// <summary>
/// <c>rand()</c> CRT движка (msvcrt, свой у потока; <c>srand</c> зовёт только <c>0x5ae6cf</c>):
/// <c>state = state·214013 + 2531011</c>, результат <c>(state &gt;&gt; 16) &amp; 0x7fff</c>. Им ИИ выбирает юнит найма
/// (<c>0x44c137</c>) и имя лидера (<c>0x44b126</c>). Ранг 1 — формула CRT; разбор O-re65-strategic.md.
/// </summary>
public sealed class CrtRand(uint state)
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;
    private const int Mask = 0x7FFF;

    public uint State { get; private set; } = state;

    /// <summary>Поставить состояние потока (харнесс: поток движка в миг из журнала).</summary>
    public void Reset(uint state) => State = state;

    public int Next()
    {
        State = unchecked(State * Multiplier + Increment);
        return (int)(State >> 16) & Mask;
    }

    /// <summary>
    /// Состояния, из которых два подряд <c>rand()</c> дают <paramref name="first"/> и <paramref name="second"/>;
    /// возвращается состояние ПОСЛЕ второго вызова (<c>new CrtRand(s)</c> продолжает поток). Бит 31 не
    /// влияет ни на один выход (бит k нового состояния зависит только от битов 0..k старого), поэтому он
    /// сброшен и перебор — 2^16 младших битов, а не 2^17. Совпадений может быть больше одного — тогда
    /// различать третьим выходом.
    /// </summary>
    public static IReadOnlyList<uint> FitAfterTwo(int first, int second)
    {
        if ((first & ~Mask) != 0 || (second & ~Mask) != 0)
            throw new ArgumentOutOfRangeException(nameof(first), "выход rand() — 15 бит");
        var found = new List<uint>();
        for (uint low = 0; low <= 0xFFFF; low++)
        {
            uint afterFirst = ((uint)first << 16) | low;
            var rand = new CrtRand(afterFirst);
            if (rand.Next() == second) found.Add(rand.State & 0x7FFF_FFFF);
        }
        return found;
    }
}
