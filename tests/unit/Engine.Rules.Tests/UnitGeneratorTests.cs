using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>Реализации юнитов, выросших уровнем выше своего типа (<see cref="UnitGenerator"/>): ключ — базовый тип, уровень — отдельным
/// полем (<see cref="LastEdict.Rules.Battle.UnitType.GrownLevel"/>). Числа — из журнала большой партии оригинала.</summary>
public class UnitGeneratorTests(ITestOutputHelper output)
{
    private const string KingsGuard = "base:unit/king-s-guard";

    /// <summary>Выросшая реализация — база плюс уровневый рост, и растут в ней и юнит, и атака. Числа сверены с журналом большой партии:
    /// <c>S073UN01fa</c> (King's Guard, база HP 225, урон 60, точность 80) на втором уровне даёт в снимках <c>battle_units</c>
    /// <c>hp_max</c> 240, в событии <c>power</c> 81, а <c>dmg_buffs</c> без баффа — 70 (105 при бусте 50 %). Потолок урона при этом
    /// остаётся 400: прибавка лидера — у базового типа.</summary>
    [SkippableFact] public void Grown_KingsGuardLevel2_MatchesJournal()
    {
        ImportedData.RequireRules();
        var rules = RulesLoader.Load(ImportedData.Content);
        var impl = rules.ImplOf(KingsGuard, 2);

        Assert.NotNull(impl);
        Assert.Equal(KingsGuard, impl!.Type.Id);
        Assert.Equal(2, impl.Type.GrownLevel);
        Assert.Equal(1, impl.Type.Level);
        Assert.Equal(240, impl.Type.HitPoints);
        Assert.Equal(70, impl.Attack!.QtyDamage);
        Assert.Equal(81, impl.Attack.Power);
        Assert.Equal(400, LastEdict.Rules.Battle.Formulas.DamageMax(rules.Constants, impl.Type));
        output.WriteLine($"{impl.Type.Id}@{impl.Type.GrownLevel}: hp {impl.Type.HitPoints}, урон {impl.Attack.QtyDamage}, " +
                         $"точность {impl.Attack.Power}");
    }

    /// <summary>Уровень типа и ниже — тип содержимого как есть, без роста.</summary>
    [SkippableFact] public void OwnLevel_IsTheContentType()
    {
        ImportedData.RequireRules();
        var rules = RulesLoader.Load(ImportedData.Content);
        var impl = rules.ImplOf(KingsGuard, 1)!;
        Assert.Same(rules.Units[KingsGuard], impl.Type);
        Assert.Equal(0, impl.Type.GrownLevel);
        Assert.Null(rules.ImplOf("base:unit/no-such-unit", 3));
    }

    /// <summary>Двойник не копирует юнитов из списка движка, но выросшего уровнем — копирует: движок сравнивал со списком id реализации,
    /// а у выросшей он порождённый (<c>G000UG…</c>).</summary>
    [SkippableFact] public void Grown_NotCopyableUnit_BecomesCopyable()
    {
        ImportedData.RequireRules();
        var rules = RulesLoader.Load(ImportedData.Content);
        var id = LastEdict.Data.Content.ContentSource.Open(ImportedData.Content).Units.First(u => !LegacyUnits.IsCopyable(u.LegacyId)).Id;
        Assert.False(rules.Units[id].Copyable);
        Assert.True(rules.ImplOf(id, rules.Units[id].Level + 1)!.Type.Copyable);
    }
}
