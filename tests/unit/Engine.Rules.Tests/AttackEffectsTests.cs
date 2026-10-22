using LastEdict.Rules.Battle;
using LastEdict.Rules.Battle.Effects;

namespace LastEdict.Rules.Tests;

/// <summary>Реестр эффектов (этап Э2, задача D): признаки встроенных эффектов — ровно прежние списки по классам движка (ранг 1, слоты
/// vftable <c>CBatAttack*</c>), числа ИИ — прежние таблицы <c>AiStrength</c>/<c>AiTargets</c>. Списки ниже — те, что стояли в коде
/// до реестра: правка признака эффекта, которая разойдётся с ними, видна здесь, а не только сдвигом С1/С2.</summary>
public class AttackEffectsTests
{
    private static readonly AttackClass[] All = Enum.GetValues<AttackClass>();

    private static void Same(Func<AttackEffect, bool> trait, params AttackClass[] expected) =>
        Assert.Equal(expected.Order(), All.Where(c => trait(AttackEffects.Of(c))).Order());

    [Fact]
    public void EveryLattCClass_HasItsOwnBuiltInEffect()
    {
        Assert.Equal(23, AttackEffects.Vanilla.Count);
        foreach (var cls in All)
        {
            var effect = AttackEffects.Of(cls);
            Assert.Equal(cls, effect.LegacyClass);
            Assert.Same(effect, AttackEffects.Find(effect.Behavior));
        }
        Assert.Equal(23, All.Select(c => AttackEffects.Of(c).Behavior).Distinct().Count());
    }

    [Fact]
    public void Traits_MatchTheEngineLists()
    {
        Same(e => e.HasPower, AttackClass.Paralyze, AttackClass.Petrify, AttackClass.Damage, AttackClass.Drain, AttackClass.DrainOverflow,
             AttackClass.Fear, AttackClass.LowerDamage, AttackClass.LowerInitiative, AttackClass.Poison, AttackClass.Frostbite,
             AttackClass.Blister, AttackClass.DrainLevel, AttackClass.TransformOther);
        Same(e => !e.ChecksImmunity, AttackClass.Heal, AttackClass.BoostDamage, AttackClass.Revive, AttackClass.Cure, AttackClass.Summon,
             AttackClass.GiveAttack, AttackClass.BestowWards, AttackClass.Doppelganger, AttackClass.TransformSelf);
        Same(e => e.TargetsAllies, AttackClass.Heal, AttackClass.Cure, AttackClass.BoostDamage, AttackClass.Revive, AttackClass.Summon,
             AttackClass.GiveAttack, AttackClass.Doppelganger, AttackClass.TransformSelf, AttackClass.BestowWards);
        Same(e => e.RejectsRetreating, AttackClass.Fear, AttackClass.BoostDamage, AttackClass.BestowWards, AttackClass.Shatter,
             AttackClass.LowerDamage, AttackClass.LowerInitiative, AttackClass.Poison, AttackClass.Frostbite, AttackClass.Blister,
             AttackClass.GiveAttack, AttackClass.DrainLevel);
        Same(e => e.HasDamage, AttackClass.Damage, AttackClass.Drain, AttackClass.DrainOverflow, AttackClass.Poison, AttackClass.Frostbite,
             AttackClass.Blister, AttackClass.Shatter);
        Same(e => e.IsDamaging, AttackClass.Damage, AttackClass.Drain, AttackClass.DrainOverflow, AttackClass.Poison, AttackClass.Blister,
             AttackClass.Frostbite);
        Same(e => e.IsDirectDamage, AttackClass.Damage, AttackClass.Drain, AttackClass.DrainOverflow);
        Same(e => e.IsDot, AttackClass.Poison, AttackClass.Frostbite, AttackClass.Blister);
        Same(e => e.IsOffence, AttackClass.Damage, AttackClass.Drain, AttackClass.DrainOverflow, AttackClass.Summon, AttackClass.Poison,
             AttackClass.Blister, AttackClass.Frostbite);
    }

    [Fact]
    public void AiNumbers_MatchTheEngineTables()
    {
        var plain = new AttackType("a", 0, AttackClass.Damage, 100, 2, 10, 0, false, 0);
        double Factor(AttackClass c, bool ignoreCity = false, int reach = 2) =>
            AttackEffects.Of(c).AiClassFactor(plain with { Class = c, Reach = reach }, ignoreCity);
        Assert.Equal((30, 1.5, 40, 2, 200, 100, 50), (Factor(AttackClass.Paralyze), Factor(AttackClass.Drain), Factor(AttackClass.BoostDamage),
                     Factor(AttackClass.DrainOverflow), Factor(AttackClass.Summon), Factor(AttackClass.DrainLevel), Factor(AttackClass.GiveAttack)));
        Assert.Equal((200, 100), (Factor(AttackClass.Doppelganger), Factor(AttackClass.Doppelganger, ignoreCity: true)));
        Assert.Equal((100, 60), (Factor(AttackClass.TransformOther, reach: AttackReach.All), Factor(AttackClass.TransformOther)));
        Assert.Equal(1, Factor(AttackClass.Damage));
        Assert.Equal((1.1, 1.15, 3, 1), (AttackEffects.Of(AttackClass.Cure).Tuning.SecondFactor, AttackEffects.Of(AttackClass.Blister).Tuning.SecondFactor,
                                         AttackEffects.Of(AttackClass.Revive).Tuning.SecondFactor, AttackEffects.Of(AttackClass.Damage).Tuning.SecondFactor));
        Same(e => e.Tuning.FullPower, AttackClass.Heal, AttackClass.BoostDamage, AttackClass.Cure, AttackClass.Summon, AttackClass.GiveAttack,
             AttackClass.Doppelganger);
        Same(e => e.Tuning.InfiniteCounts, AttackClass.Paralyze, AttackClass.Petrify, AttackClass.BoostDamage, AttackClass.LowerDamage,
             AttackClass.LowerInitiative, AttackClass.Poison, AttackClass.Frostbite, AttackClass.Blister, AttackClass.TransformOther);
        // Множители опыта цели — в порядке списка движка (0x5ceff1).
        Assert.Equal([(AttackClass.Heal, 2), (AttackClass.Paralyze, 8), (AttackClass.Petrify, 8), (AttackClass.Summon, 10),
                      (AttackClass.TransformOther, 9), (AttackClass.GiveAttack, 3)],
                     All.Select(AttackEffects.Of).Where(e => e.Tuning.XpRank is not null).OrderBy(e => e.Tuning.XpRank)
                        .Select(e => (e.LegacyClass, e.Tuning.XpFactor)));
    }

    [Fact]
    public void ModEffect_OnBuiltInBehavior_KeepsBehaviorAndTakesItsData()
    {
        var poison = AttackEffects.Of(AttackClass.Poison);
        var acid = poison with { Id = "mymod:effect/acid", Tuning = poison.Tuning with { SecondFactor = 2 }, Looks = poison.Looks with { Sound = "ACID" } };
        var attack = new AttackType("acid", 50, AttackClass.Poison, 80, 2, 15, 0, false, 0) { Effect = acid };

        Assert.Same(acid, attack.Effect);
        Assert.True(attack.Effect.IsDot && attack.Effect.HasPower);
        Assert.Equal(("ACID", 2.0), (attack.Effect.Looks.Sound, attack.Effect.Tuning.SecondFactor));
        Assert.Same(AttackEffects.Of(AttackClass.Heal), (attack with { Class = AttackClass.Heal }).Effect);   // чужой класс — свой эффект
    }

    [Fact]
    public void UnknownClass_LoadsButRefusesTheHit()
    {
        var effect = AttackEffects.Of((AttackClass)99);
        Assert.IsType<UnknownEffect>(effect);
        Assert.False(effect.HasPower);
        var unit = new BattleUnit { Id = "U", Attacker = true, HpMax = 10, Slot = 0, Type = new UnitType("t", 1, 10, 0, "a", "", false, true, 0, 0), Attack = new AttackType("a", 0, (AttackClass)99, 100, 2, 1, 0, false, 0) };
        Assert.Contains("не реализован", Assert.Throws<NotSupportedException>(() => effect.Hit(new EffectHit(unit, unit.Attack, unit, 0, new LastEdict.Rules.Rng.ScriptedRng([]), new RulesConstants(0, 0), 1, null))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_RefusesToReplaceAnotherBehavior()
    {
        var clash = AttackEffects.Of(AttackClass.Heal) with { Tuning = new(ClassFactor: 7) };
        Assert.Throws<InvalidOperationException>(() => AttackEffects.Register(clash));
        AttackEffects.Register(AttackEffects.Of(AttackClass.Heal));   // тот же — не ошибка
    }
}
