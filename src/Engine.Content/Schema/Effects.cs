using System.ComponentModel;
using LastEdict.Rules.Battle.Effects;

namespace LastEdict.Data.Content.Schema;

/// <summary>Эффект атаки — что делает удар (урон, лечение, паралич, призыв…): поведение из реестра эффектов (код, <see cref="AttackEffects"/>)
/// и его данные — числа ИИ, строки и звуки показа. Двадцать три эффекта оригинала (<c>LattC.dbf</c>) выпускает импорт с числами движка;
/// мод заводит свой эффект на встроенном поведении со своими данными (<c>"behavior": "poison"</c>, другие звуки и числа) или на своём
/// поведении (мод с кодом).</summary>
public sealed record EffectDef : ContentDef
{
    [Description("Поведение — код эффекта в реестре: damage, drain, drainOverflow, paralyze, petrify, fear, heal, boostDamage, lowerDamage, " +
                 "lowerInitiative, poison, frostbite, blister, revive, cure, summon, drainLevel, giveAttack, doppelganger, transformSelf, " +
                 "transformOther, bestowWards, shatter или поведение мода с кодом.")]
    public required string Behavior { get; init; }

    [Description("Подсказки ИИ: множители силы отряда и приоритета цели.")]
    public EffectTuning Ai { get; init; } = new();

    [Description("Строки и звуки показа: имя класса, подпись действия, звук второй атаки, звук тика.")]
    public EffectLooks Looks { get; init; } = new();
}
