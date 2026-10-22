namespace LastEdict.Rules.Rng;

/// <summary>Поток случайных чисел партии (Э4, <c>docs/own-version-roadmap.md</c> §3.2). У оригинала один генератор на всё: лишний бросок ИИ
/// сдвигает все бои дальше, и честное A/B баланса невозможно. В Last Edict у каждой системы свой поток от зерна партии — правка ИИ не
/// трогает броски боя, правка добычи — решения ИИ.</summary>
public enum RngStream
{
    /// <summary>Бои: инициатива, промахи, урон, эффекты (<c>BattleSim</c>).</summary>
    Battle,

    /// <summary>Решения ИИ: тактика, стратегия, найм лидеров, дипломатия, подарки и ответы, заклинания ИИ, вылазки воров ИИ.</summary>
    Ai,

    /// <summary>Добыча: заклинания побеждённого.</summary>
    Loot,

    /// <summary>Мир карты: события сценария, шпионы, действия воров человека, облик перекладываемых гор.</summary>
    Map,
}

/// <summary>Потоки R250 (<see cref="EngineRng"/>) от зерна партии: у каждого потока своя таблица из 250 слов. Таблица — слова SplitMix64 от
/// пары (зерно, поток) с диагональю старших битов по Киркпатрику—Штоллю (слова <c>11·k + 3</c>, бит <c>15 − k</c> и ноль выше него): так
/// шестнадцать битовых столбцов линейно независимы, и у R250 полный период.</summary>
public static class RngStreams
{
    public static IReadOnlyList<RngStream> All { get; } = Enum.GetValues<RngStream>();

    /// <summary>Имя потока в сохранении (<c>battle</c>, <c>ai</c>…).</summary>
    public static string Name(RngStream stream) => stream.ToString().ToLowerInvariant();

    /// <summary>Генератор потока <paramref name="stream"/> партии с зерном <paramref name="seed"/>.</summary>
    public static EngineRng Derive(ulong seed, RngStream stream)
    {
        ulong state = seed ^ (0xD1B54A32D192ED03UL * ((ulong)stream + 1));
        var table = new ushort[EngineRng.Size];
        for (int i = 0; i < table.Length; i++) table[i] = (ushort)(SplitMix64(ref state) >> 48);
        ushort mask = 0xFFFF, msb = 0x8000;
        for (int k = 0; k < 16; k++)
        {
            int at = 11 * k + 3;
            table[at] = (ushort)((table[at] & mask) | msb);
            mask >>= 1;
            msb >>= 1;
        }
        return EngineRng.FromSeed(table, 0);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
