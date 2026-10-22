namespace LastEdict.Rules.Rng;

/// <summary>Генератор движка Disciples 2 (GOG 3.01): R250 с лагом 147 над таблицей из 250 слов,
/// константа в образе на старте — <see cref="RngTable"/>. Восстановлен по коду 0x54851f /
/// 0x6781dd / 0x67819c, подтверждён на 125 бросках пяти боёв — docs/re-rng.md в disciples-modding.
/// Состояние — ДВА числа: индекс после reseed (0x5484bd, 0..249) и число шагов от reseed до
/// нужной точки (<see cref="Steps"/>). Таблица при каждом <see cref="Step"/> мутирует на месте
/// (XOR), поэтому одного индекса недостаточно: генератор с тем же <see cref="Index"/>, но со
/// свежей таблицей, не воспроизводит дальнейшие броски (перепроверено на корпусе: 1/24, 0/24,
/// 0/24 совпадений). 250 стартовых индексов — «250 траекторий» в docs/re-rng.md; <see cref="Fit"/>
/// перебирает их так же, как tools/re/rng_search.py (там — numpy, здесь — прямой перебор шагов).
/// </summary>
public sealed class EngineRng : IRng
{
    public const int Size = 250;
    public const int Lag = 147;
    public const int DefaultIndex = 47;   // .data:0x838c24 в образе

    private readonly ushort[] _table;

    public EngineRng(int index = DefaultIndex) : this((ushort[])RngTable.Values.Clone(), Normalize(index)) { }

    private EngineRng(ushort[] table, int index, int steps = 0)
    {
        _table = table;
        Index = index;
        Steps = steps;
    }

    private static int Normalize(int index) => ((index % Size) + Size) % Size;

    /// <summary>Состояние из строки <c>rng_seed</c> журнала (схема 6.2): 250 слов таблицы после записи
    /// (<c>table</c>) и индекс (<c>index_after</c>). Следующий вызов движка — <c>n + 1</c>; <see cref="Steps"/>
    /// считаются от снимка. Таблица копируется.</summary>
    public static EngineRng FromSeed(IReadOnlyList<ushort> table, int index)
    {
        if (table.Count != Size)
            throw new ArgumentException($"таблица генератора — {Size} слов, получено {table.Count}", nameof(table));
        return new EngineRng(table.ToArray(), Normalize(index));
    }

    /// <summary>Копия таблицы (250 слов) в текущем состоянии — тот же вид, что <c>rng_seed.table</c>.</summary>
    public ushort[] TableSnapshot() => (ushort[])_table.Clone();

    public int Index { get; private set; }

    /// <summary>Число вызовов <see cref="Step"/> с момента конструктора (для генератора,
    /// возвращённого <see cref="Fit"/>, — с момента виртуального старта найденной траектории).</summary>
    public int Steps { get; private set; }

    /// <summary>Один шаг 0x67819c: table[i] ^= table[i-147]; i = (i+1) % 250.</summary>
    public ushort Step()
    {
        int i = Index;
        int j = i >= Lag ? i - Lag : i + (Size - Lag);
        ushort v = (ushort)(_table[i] ^ _table[j]);
        _table[i] = v;
        Index = i >= Size - 1 ? 0 : i + 1;
        Steps++;
        return v;
    }

    /// <summary>Перемотать состояние (<paramref name="table"/>, <paramref name="index"/>) назад до исходной таблицы образа
    /// (<see cref="RngTable"/>): шаг назад — <c>i -= 1; t[i] ^= t[i−147]</c> (обратный к <see cref="Step"/>). Возвращает число шагов и
    /// индекс, с которым таблица была исходной, — индекс после reseed новой игры; не нашлось за <paramref name="limit"/> шагов — null
    /// (состояние не из первой партии запуска игры). Сравнение таблиц — счётчиком расхождений, по слову на шаг.</summary>
    public static (int Steps, int Index)? RewindToTable(IReadOnlyList<ushort> table, int index, int limit = 3_000_000)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Count != Size) throw new ArgumentException($"таблица генератора — {Size} слов", nameof(table));
        var t = table.ToArray();
        var pristine = RngTable.Values;
        int differ = 0;
        for (int k = 0; k < Size; k++) if (t[k] != pristine[k]) differ++;
        int i = Normalize(index);
        for (int steps = 0; ; steps++)
        {
            if (differ == 0) return (steps, i);
            if (steps == limit) return null;
            i = i == 0 ? Size - 1 : i - 1;
            int j = i >= Lag ? i - Lag : i + (Size - Lag);
            bool was = t[i] == pristine[i];
            t[i] = (ushort)(t[i] ^ t[j]);
            bool now = t[i] == pristine[i];
            differ += (was ? 1 : 0) - (now ? 1 : 0);
        }
    }

    /// <summary>Точка reseed новой игры не в первой партии запуска: таблица R250 между партиями не сеется, reseed (<c>0x429dcd</c>,
    /// <c>new_game_time</c>) меняет только индекс, поэтому <see cref="RewindToTable"/> ломается на нём. Встречный поиск: состояние
    /// (<paramref name="table"/>, <paramref name="index"/>) перематывается назад до <paramref name="back"/> шагов (хэш таблицы на каждом
    /// шаге — сумма хэшей слов, обновляется за шаг), навстречу идут 250 траекторий от исходной таблицы до <paramref name="ahead"/>
    /// шагов; первая общая таблица — таблица в момент reseed. Возвращает шаги назад от состояния, индекс после reseed и таблицу; не
    /// нашлось — null. Проверено по строкам <c>rng_seed</c> журнала (<c>20260928-shots</c>: вторая партия запуска — индекс 93).</summary>
    public static (int Steps, int Index, ushort[] Table)? RewindToReseed(IReadOnlyList<ushort> table, int index,
                                                                         int back = 2_000_000, int ahead = 2_000_000)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Count != Size) throw new ArgumentException($"таблица генератора — {Size} слов", nameof(table));
        var seen = new Dictionary<ulong, int>(back + 1);
        var t = table.ToArray();
        int i = Normalize(index);
        ulong hash = TableHash(t);
        for (int k = 0; k <= back; k++)
        {
            seen.TryAdd(hash, k);
            i = i == 0 ? Size - 1 : i - 1;
            int j = i >= Lag ? i - Lag : i + (Size - Lag);
            ushort was = t[i];
            t[i] = (ushort)(was ^ t[j]);
            hash += WordHash(i, t[i]) - WordHash(i, was);
        }

        int found = -1;
        Parallel.For(0, Size, new ParallelOptions { MaxDegreeOfParallelism = FitThreads }, (start, loop) =>
        {
            var walker = new EngineRng(start);
            ulong h = TableHash(walker._table);
            for (int m = 0; m < ahead && !loop.IsStopped; m++)
            {
                if (seen.TryGetValue(h, out int k) && Matches(table, index, k, walker._table))
                {
                    Interlocked.CompareExchange(ref found, k, -1);
                    loop.Stop();
                    return;
                }
                int w = walker.Index;
                ushort old = walker._table[w];
                ushort v = walker.Step();
                h += WordHash(w, v) - WordHash(w, old);
            }
        });
        if (found < 0) return null;
        var at = Rewound(table, index, found);
        return (found, at.Index, at._table);
    }

    private static bool Matches(IReadOnlyList<ushort> table, int index, int back, ushort[] other) =>
        Rewound(table, index, back)._table.AsSpan().SequenceEqual(other);

    private static EngineRng Rewound(IReadOnlyList<ushort> table, int index, int back)
    {
        var t = table.ToArray();
        int i = Normalize(index);
        for (int k = 0; k < back; k++)
        {
            i = i == 0 ? Size - 1 : i - 1;
            t[i] ^= t[i >= Lag ? i - Lag : i + (Size - Lag)];
        }
        return new EngineRng(t, i);
    }

    private static ulong TableHash(ushort[] t)
    {
        ulong h = 0;
        for (int k = 0; k < Size; k++) h += WordHash(k, t[k]);
        return h;
    }

    /// <summary>splitmix64 от (место, слово): хэш таблицы — сумма по местам, шаг меняет одно слово.</summary>
    private static ulong WordHash(int position, ushort value)
    {
        ulong z = ((ulong)position << 16 | value) + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary><c>setState</c> движка (<c>0x67816c</c>): таблица копируется, индекс по модулю 250; <see cref="Steps"/> не меняется.
    /// Харнессам — поставить генератор партии в состояние движка из журнала (<c>rng_seed</c> и счётчик вызовов).</summary>
    public void SetState(IReadOnlyList<ushort> table, int index)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Count != Size) throw new ArgumentException($"таблица генератора — {Size} слов, получено {table.Count}", nameof(table));
        for (int k = 0; k < Size; k++) _table[k] = table[k];
        Index = Normalize(index);
    }

    /// <summary>Прогнать <paramref name="steps"/> шагов, отбросив значения.</summary>
    public void Advance(int steps)
    {
        for (int k = 0; k < steps; k++) Step();
    }

    /// <summary>0x6781dd: отбраковка хвоста диапазона, затем остаток. ponytail: один шаг на
    /// попытку — так сходится корпус; в статике видны два шага, но двухшаговая модель не сходится
    /// ни от одного из 250 индексов (docs/re-rng.md, «что не удалось»).</summary>
    public int Next(int maxValue)
    {
        if (maxValue < 0 || maxValue > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(maxValue), maxValue,
                "0..0xFFFF: таблица — 16-битные слова, при maxValue > 0xFFFF limit вырождается в 0 и Next не завершается");
        if (maxValue == 0) return 0;
        int limit = 0xFFFF / maxValue * maxValue;
        while (true)
        {
            int v = Step();
            if (v < limit) return v % maxValue;
        }
    }

    /// <summary>Перебор состояния по записанным броскам (порт tools/re/rng_search.py). Неизвестных
    /// две — стартовый индекс после reseed (0..249) и число шагов от reseed до окна бросков; сама
    /// таблица не сеется, поэтому пространство перебора — <see cref="Size"/> × <paramref
    /// name="maxSteps"/>. Лимит 1 200 000: загрузка сценария стоит ~50,7 тыс. шагов, и в серии
    /// «одна игра — много боёв» (corpus_run --keep-game) бой k начинается с ~50,7k·k — хватает на ~20
    /// боёв за процесс (замер 07.09.2026 по 52 боям). Дешёвая проверка на каждом шаге — совпадает ли сырое значение с первым
    /// броском (<c>draws[0]</c>); полная проверка всей последовательности через <see cref="Next"/>
    /// идёт только по кандидатам, прошедшим дешёвую. Возвращает генератор в состоянии ПЕРЕД первым
    /// броском последовательности, либо null — броски не воспроизведены ни от одного индекса, либо
    /// решение неединственно (тогда это ошибка вызывающего теста, не движка).</summary>
    public static EngineRng? Fit(IReadOnlyList<(int max, int r)> draws, int maxSteps = 1_200_000)
    {
        if (draws.Count == 0) throw new ArgumentException("нужен хотя бы один бросок", nameof(draws));
        var (m0, r0) = draws[0];
        int limit0 = 0xFFFF / m0 * m0;
        var walkers = new EngineRng[Size];
        for (int i = 0; i < Size; i++) walkers[i] = new EngineRng(i);
        var found = new List<EngineRng>(2);

        // Ступени: все 250 траекторий доводятся до общего предела, и только если единственного
        // решения нет — предел удваивается. Первый бой процесса лежит на ~50,7k шагов и находится на
        // первой ступени за прежнюю цену; бой k серии — на ступени, где предел ≥ 50,7k·k.
        for (int limit = FirstStage; ; limit = Math.Min(limit * 2, maxSteps))
        {
            // Траектории независимы (у каждой своя таблица) — обходятся параллельно по ядрам. Ответ от порядка не
            // зависит: решает только число найденных кандидатов. Длинный сеанс без перезапуска игры (20260916-synth)
            // гоняет каждую до предела: 250 × 1,2 млн шагов на бой, строгий С2 шёл 1 ч 38 мин на одном ядре.
            int stage = limit;
            Parallel.ForEach(walkers, new ParallelOptions { MaxDegreeOfParallelism = FitThreads }, walker =>
            {
                while (walker.Steps < stage)
                {
                    int i = walker.Index;
                    ushort v = walker.Step();
                    if (v >= limit0 || v % m0 != r0) continue;

                    if (walker.MatchesFrom(i, v, draws))
                    {
                        var candidate = walker.SnapshotBefore(i, v);
                        lock (found) found.Add(candidate);
                    }
                }
            });
            if (found.Count == 1) return found[0];
            if (found.Count > 1) return null; // решение не единственно — ошибка теста
            if (limit >= maxSteps) return null;
        }
    }

    private const int FirstStage = 200_000;

    /// <summary>Потоков перебора в <see cref="Fit"/>: переменная <c>D2_RNG_THREADS</c>, по умолчанию все ядра, кроме
    /// четырёх (машине остаётся запас на работу рядом с долгим прогоном).</summary>
    public static int FitThreads { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("D2_RNG_THREADS"), out int n) && n > 0
            ? n : Math.Max(1, Environment.ProcessorCount - 4);

    /// <summary>Стартовый индекс траектории: вместе с <see cref="Steps"/> однозначно задаёт состояние
    /// (<see cref="At"/>) — таблица от старта детерминирована.</summary>
    public int Start => Normalize(Index - Steps % Size);

    /// <summary>Состояние траектории <paramref name="start"/> после <paramref name="steps"/> шагов — то же, что вернул бы
    /// <see cref="Fit"/> (кеш подборов хранит только эти два числа).</summary>
    public static EngineRng At(int start, int steps)
    {
        var rng = new EngineRng(start);
        rng.Advance(steps);
        return rng;
    }

    /// <summary>Продолжение по серии «одна игра — много боёв»: этот генератор стоит ПОСЛЕ последнего
    /// броска предыдущего боя, между боями движок сделал <paramref name="minSteps"/> известных вызовов
    /// (разница счётчика n — строки rng вне боя не пишутся, но n растёт всегда) плюс неизвестное
    /// число шагов сверх того (отбраковка хвоста диапазона в Next и всё, что шагает таблицу мимо
    /// 0x54851f: в корпусе 20260906-c между боями шагов на ~240 больше, чем вызовов). Идёт вперёд
    /// от minSteps до <paramref name="maxSteps"/> шагов, пока не найдёт состояние, воспроизводящее
    /// <paramref name="draws"/> целиком; возвращает его (ПЕРЕД первым броском) либо null. Дешевле
    /// <see cref="Fit"/> на порядки: одна траектория вместо 250, и лимит шагов Fit (1,2 млн) серии
    /// больше не ограничивает.</summary>
    public EngineRng? FindAhead(IReadOnlyList<(int max, int r)> draws, long minSteps, long maxSteps)
    {
        if (draws.Count == 0) throw new ArgumentException("нужен хотя бы один бросок", nameof(draws));
        var (m0, r0) = draws[0];
        int limit0 = 0xFFFF / m0 * m0;
        for (long k = 0; k < minSteps; k++) Step();
        for (long k = minSteps; k < maxSteps; k++)
        {
            int i = Index;
            ushort v = Step();
            if (v >= limit0 || v % m0 != r0) continue;
            if (MatchesFrom(i, v, draws)) return SnapshotBefore(i, v);
        }
        return null;
    }

    /// <summary>Прогнать записанные броски через генератор, сверяя каждый; false — расхождение.
    /// После true генератор стоит сразу после последнего броска — точка для <see cref="FindAhead"/>.</summary>
    public bool Replay(IReadOnlyList<(int max, int r)> draws) => MatchesAll(draws);

    /// <summary>Состояние ПЕРЕД шагом, который только что дал <paramref name="producedValue"/> в
    /// ячейке <paramref name="i"/>: table[j] шагом не тронут, поэтому старое table[i] = value XOR
    /// table[j] (XOR обратим).</summary>
    private EngineRng SnapshotBefore(int i, ushort producedValue)
    {
        var snap = (ushort[])_table.Clone();
        int j = i >= Lag ? i - Lag : i + (Size - Lag);
        snap[i] = (ushort)(producedValue ^ _table[j]);
        return new EngineRng(snap, i, Steps - 1);
    }

    /// <summary>Буфер проверки кандидата — свой у каждого потока перебора.</summary>
    [ThreadStatic] private static EngineRng? _probe;

    /// <summary>То же, что <c>SnapshotBefore(i, v).Clone().MatchesAll(draws)</c>, но без выделения памяти: кандидат
    /// собирается в буфере потока. Дешёвый фильтр по первому броску пропускает до половины шагов (при диапазоне 2), и
    /// две копии таблицы на кандидата упирали параллельный <see cref="Fit"/> в сборщик мусора: из 28 потоков половина
    /// стояла в Clone/SnapshotBefore.</summary>
    private bool MatchesFrom(int i, ushort producedValue, IReadOnlyList<(int max, int r)> draws)
    {
        var probe = _probe ??= new EngineRng(new ushort[Size], 0);
        Array.Copy(_table, probe._table, Size);
        int j = i >= Lag ? i - Lag : i + (Size - Lag);
        probe._table[i] = (ushort)(producedValue ^ _table[j]);
        probe.Index = i;
        probe.Steps = 0;
        return probe.MatchesAll(draws);
    }

    public EngineRng Clone() => new((ushort[])_table.Clone(), Index, Steps);

    private bool MatchesAll(IReadOnlyList<(int max, int r)> draws)
    {
        foreach (var (m, r) in draws)
            if (Next(m) != r) return false;
        return true;
    }
}

public static class RngTableProbe
{
    public static int Length => RngTable.Values.Length;
    public static int First => RngTable.Values[0];
    public static int Last => RngTable.Values[^1];
    public static long Sum => RngTable.Values.Sum(v => (long)v);
}
