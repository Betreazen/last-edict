namespace LastEdict.Data.Tests;

/// <summary>Группы каталогов корпуса для категорий тестов (<c>[Trait("Category", …)]</c>, <c>tools/affected_tests.py</c>).</summary>
internal static class CorpusSets
{
    /// <summary>Партии синтетических карт порта (<c>corpus/*-port-synth</c>, <c>mods/port-synth</c>): их сверки — группа <c>Synth</c>.</summary>
    public static bool IsSynth(string run) => run.EndsWith("-port-synth", StringComparison.OrdinalIgnoreCase);
}
