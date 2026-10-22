using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>Порождённые прокачкой реализации юнитов (<c>G000UG****</c>). Числа — из
/// дизассемблера GOG <c>Discipl2.exe</c> и из журнала большой партии; см.
/// <see cref="UnitGenerator"/>.</summary>
public class UnitGeneratorTests(ITestOutputHelper output)
{
    /// <summary>Ванильные константы генератора: 356 глобальных реализаций (359 строк
    /// <c>Gunits.dbf</c>, три помечены удалёнными и дублируют живые), отсюда
    /// <c>levelMax = 0xFFFF / 356 = 184</c> и шаг 183. Если хоть одно из трёх поедет, поедут и все
    /// порождённые id — поэтому они проверяются числом, а не подразумеваются.</summary>
    [SkippableFact] public void VanillaLevelMax_IsFullTypeIndexRangeDividedByUnitCount()
    {
        ImportedData.RequireRules();
        var rules = RulesLoader.Load(ImportedData.Content);
        Assert.Equal(356, rules.Units.Count);
        Assert.NotNull(rules.Generator);
        Assert.Equal(184, rules.Generator!.LevelMax);
        output.WriteLine($"глобальных реализаций {rules.Units.Count}, levelMax {rules.Generator.LevelMax}, " +
                         $"шаг {rules.Generator.LevelMax - 1}");
    }

    /// <summary>Прямое и обратное преобразование сходятся на живом наблюдении: King's Guard
    /// <c>g000uu0044</c> на втором уровне — это <c>G000UG1e06</c> (журнал
    /// <c>corpus/20260910-passive/passive-004.jsonl</c>, юнит <c>S073UN01fa</c>), и разбор этого id
    /// возвращает ту же пару. Круговой прогон по всем 356 типам и всем уровням до потолка ловит
    /// сдвиг индекса или смещения на единицу.</summary>
    [SkippableFact] public void GeneratedId_RoundTripsForEveryTypeAndLevel()
    {
        ImportedData.RequireRules();
        var g = RulesLoader.Load(ImportedData.Content).Generator!;

        Assert.Equal("G000UG1e06", g.GeneratedId("g000uu0044", 2));
        Assert.True(g.TryDecode("G000UG1e06", out var baseId, out int level));
        Assert.Equal("g000uu0044", baseId, ignoreCase: true);
        Assert.Equal(2, level);

        int pairs = 0;
        foreach (var id in RulesLoader.Load(ImportedData.Content).Units.Keys)
            for (int lvl = 2; lvl <= g.LevelMax; lvl++)
            {
                var generated = g.GeneratedId(id, lvl);
                Assert.NotEqual("", generated);
                Assert.True(g.TryDecode(generated, out var back, out int backLevel), generated);
                Assert.Equal(id, back, ignoreCase: true);
                Assert.Equal(lvl, backLevel);
                pairs++;
            }
        output.WriteLine($"круговых сверок id {pairs}");

        // Границы движка (0x598672-0x598692): ниже второго уровня и выше потолка id не бывает.
        Assert.Equal("", g.GeneratedId("g000uu0044", 1));
        Assert.Equal("", g.GeneratedId("g000uu0044", g.LevelMax + 1));
        Assert.False(g.TryDecode("g000uu0044", out _, out _));
    }

    /// <summary>Порождённая реализация — это база плюс уровневый рост, и растут в ней и юнит, и
    /// атака. Числа сверены с журналом большой партии: <c>S073UN01fa</c> (King's Guard, база HP 225,
    /// урон 60, точность 80) на втором уровне даёт в снимках <c>battle_units</c> <c>hp_max</c> 240,
    /// в событии <c>power</c> 81, а <c>dmg_buffs</c> без баффа — 70 (105 при бусте 50 %). Потолок
    /// урона при этом остаётся 400: движок сводит <c>UG</c> к базовому <c>UU</c>.</summary>
    [SkippableFact] public void Grown_KingsGuardLevel2_MatchesJournal()
    {
        ImportedData.RequireRules();
        var rules = RulesLoader.Load(ImportedData.Content);
        var impl = rules.ImplOf("G000UG1e06");

        Assert.NotNull(impl);
        Assert.Equal("G000UG1e06", impl!.Type.Id);
        Assert.Equal("g000uu0044", impl.Type.GlobalId, ignoreCase: true);
        Assert.Equal(240, impl.Type.HitPoints);
        Assert.Equal(70, impl.Attack!.QtyDamage);
        Assert.Equal(81, impl.Attack.Power);
        Assert.Equal(400, LastEdict.Rules.Battle.Formulas.DamageMax(rules.Constants, impl.Type.GlobalId));
        output.WriteLine($"{impl.Type.Id}: hp {impl.Type.HitPoints}, урон {impl.Attack.QtyDamage}, " +
                         $"точность {impl.Attack.Power}");
    }
}
