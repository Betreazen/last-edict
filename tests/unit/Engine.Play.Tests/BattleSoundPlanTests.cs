using D2.Data.Sound;
using D2.Rules.Battle;

namespace D2.Data.Tests;

/// <summary>Звуки действия боя (<see cref="BattleSoundPlan"/>) по настоящей описи юнитов: мечник бьёт замахом на кадре 6, ударом на
/// кадре 7; промах не звучит; «больно» — только при броске ≥ 70 и тише; гибель — <c>DEATH</c>; вторая атака — звуком класса.</summary>
public class BattleSoundPlanTests
{
    private static readonly Lazy<UnitSounds> LoadedSounds = new(() => UnitSounds.Load(ImportedData.PathOf("sound/UnitSounds.json")));
    private static UnitSounds Sounds => LoadedSounds.Value;

    private static BattleUnit Unit(string id, string type, AttackType? second = null) => new()
    {
        Id = id,
        Type = new UnitType(type, 1, 100, 0, "a", "", false, true, 10, 100),
        Attack = new AttackType("a", 50, AttackClass.Damage, 80, 3, 25, 0, false, 1),
        Attack2 = second,
        Attacker = id.StartsWith('a'),
        Slot = 0,
        HpMax = 100,
    };

    [SkippableFact]
    public void Hit_PlaysSwingThenImpact_HurtOnlyOnHighRoll()
    {
        var units = new[] { Unit("a1", "G000UU0001"), Unit("d1", "G000UU0001") };
        BattleUnit? Of(string id) => units.FirstOrDefault(u => u.Id == id);
        var hit = new List<TurnRecord> { new(1, "a1", "d1", false, 25, 75) };

        var quiet = BattleSoundPlan.Of(hit, Of, Sounds, n => n == 100 ? 69 : 0);
        Assert.Equal(2, quiet.Count);
        Assert.Equal((0.0, "SWORD4"), (quiet[0].At, quiet[0].Name));   // первый вариант списка SWORDSWOOSH
        Assert.Equal(0.066, quiet[1].At, 3);
        Assert.Equal("MDBLD1A", quiet[1].Name);   // список SWORDIMPACT

        var loud = BattleSoundPlan.Of(hit, Of, Sounds, n => n == 100 ? 70 : 0);
        Assert.Equal(3, loud.Count);
        Assert.Equal(BattleSoundPlan.HurtVolume, loud[2].Volume);
    }

    [SkippableFact]
    public void MissKillAndSecondAttack()
    {
        var poison = new AttackType("p", 50, AttackClass.Poison, 80, 3, 10, 0, false, 1);
        var units = new[] { Unit("a1", "G000UU0001", poison), Unit("d1", "G000UU0001"), Unit("d2", "G000UU0001") };
        BattleUnit? Of(string id) => units.FirstOrDefault(u => u.Id == id);

        var miss = BattleSoundPlan.Of([new(1, "a1", "d1", true, 0, 100)], Of, Sounds, _ => 0);
        Assert.Single(miss);   // только замах

        var cues = BattleSoundPlan.Of([new(1, "a1", "d1", false, 100, 0), new(1, "a1", "d2", false, 25, 75),
                                       new(1, "a1", "d2", false, 10, 65, Attack: 2)], Of, Sounds, _ => 0);
        Assert.Contains(cues, c => c.Name == "DEATH");
        var classCue = Assert.Single(cues, c => c.Name == "POISON");
        Assert.Equal(0.066 + BattleSoundPlan.SecondDelay, classCue.At, 3);
        Assert.All(cues, c => Assert.Equal("Battle", c.Archive));

        // Боевой предмет звучит юнитом предмета (сфера — Elementalist g000uu0153), а не лидером.
        var orb = BattleSoundPlan.Of([new(1, "a1", "d1", false, 25, 75)], Of, Sounds, _ => 0, actorType: "g000uu0153");
        Assert.NotEqual("SWORD4", orb[0].Name);
        // Тики: яд, мороз, ожог — по звуку через SecondDelay.
        var ticks = BattleSoundPlan.OfTicks([new DotTick(1, "d1", AttackClass.Poison, 5, 70), new DotTick(1, "d1", AttackClass.Blister, 5, 65)]);
        Assert.Equal([("POISON", 0.0), ("BLISTER", BattleSoundPlan.SecondDelay)], ticks.Select(c => (c.Name, c.At)));
        // Добила вторая атака — гибель звучит вместе с её звуком, не раньше.
        var late = BattleSoundPlan.Of([new(1, "a1", "d1", false, 25, 5), new(1, "a1", "d1", false, 10, 0, Attack: 2)], Of, Sounds, _ => 0);
        Assert.Equal(Assert.Single(late, c => c.Name == "POISON").At, Assert.Single(late, c => c.Name == "DEATH").At);
    }

    /// <summary>Звук эффекта ходящего движок ставит в стадии <c>HEFF</c> (<c>0x63a30f</c>): у Повелителя стихий картинки HEFF нет — его призыв
    /// звучит замахом и одним ударом <c>SUMMON</c> (журнал <c>20261016g-port-synth</c>: 8 призывов — 8 <c>G000SS0008</c>), а не двумя.</summary>
    [SkippableFact]
    public void NoCasterEffect_NoEffectSound()
    {
        var units = new[] { Unit("a1", "G000UU0153"), Unit("a2", "G000UU0001") };
        BattleUnit? Of(string id) => units.FirstOrDefault(u => u.Id == id);
        List<TurnRecord> summon = [new(1, "a1", "a2", false, 0, 100)];

        var with = BattleSoundPlan.Of(summon, Of, Sounds, _ => 0);
        var without = BattleSoundPlan.Of(summon, Of, Sounds, _ => 0, casterEffect: false);

        Assert.Equal(2, with.Count(c => c.Name == "G000SS0008"));   // эффект и удар — оба из списка SUMMON
        Assert.Equal(["0153_ATTACK_A", "G000SS0008"], without.Select(c => c.Name));
    }
}
