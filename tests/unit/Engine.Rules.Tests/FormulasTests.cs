using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;

public class FormulasTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);
    private static AttackType Attack(AttackClass cls = AttackClass.Damage, int power = 80, int dam = 25) =>
        new("g000aa5030", 65, cls, power, 3, dam, 0, false, 0);

    [Fact] public void AttackPower_ClassWithoutPower_Is100()
    {
        Assert.Equal(80, Formulas.AttackPower(Attack()));
        Assert.Equal(100, Formulas.AttackPower(Attack(AttackClass.Heal)));
        Assert.Equal(100, Formulas.AttackPower(Attack(AttackClass.Summon)));
        Assert.Equal(80, Formulas.AttackPower(Attack(AttackClass.Paralyze)));
    }

    [Fact] public void ShouldMiss_AverageOfTwoRollsAgainstPower()
    {
        Assert.False(Formulas.ShouldMiss(80, 96, 7));    // (96+7)/2 = 51 < 80 - попадание
        Assert.True(Formulas.ShouldMiss(80, 90, 90));    // 90 >= 80 - промах
        Assert.False(Formulas.ShouldMiss(80, 83, 77));   // 80 == 80 - граница: попадание (корпус 20260906-fast, battle-023/026)
        Assert.True(Formulas.ShouldMiss(80, 76, 86));    // 81 > 80 - промах (battle-012)
    }

    /// <summary>docs/re-damage-max.md эталона: 300 + 100 только для десяти прошитых typeIndex (<see cref="LegacyUnits"/>); BASE_UNIT
    /// (g000uu5130 -> g000uu5030) движок не читает.</summary>
    [Fact] public void DamageMax_Is300Plus100ForTenHardcodedLeaders()
    {
        Assert.True(LegacyUnits.HasDamageBonus("g000uu0019"));    // Pegasus Knight, живой бой
        Assert.True(LegacyUnits.HasDamageBonus("G000UU8009"));    // Forest Liege, регистр корпуса
        Assert.False(LegacyUnits.HasDamageBonus("g000uu5130"));   // Thug, живой бой
        Assert.False(LegacyUnits.HasDamageBonus("g000uu5030"));   // его BASE_UNIT
        Assert.False(LegacyUnits.HasDamageBonus("g000uu0018"));   // соседний индекс, не в списке
        Assert.False(LegacyUnits.HasDamageBonus("S143UG0019"));   // не тип UU
        Assert.False(LegacyUnits.HasDamageBonus(null));           // новый юнит мода
        var unit = new UnitType("mod:unit/hero", 1, 100, 0, "", "", false, true, 0, 0);
        Assert.Equal(300, Formulas.DamageMax(C, unit));
        Assert.Equal(400, Formulas.DamageMax(C, unit with { DamageBonus = true }));
    }

    [Fact] public void DamageWithBuffs_NoRandom_IsAttackDamageCappedByMax()
    {
        var rng = new ScriptedRng([]);
        Assert.Equal(25, Formulas.DamageWithBuffs(Attack(), 300, 0, 0, false, rng, C));
        Assert.Equal(300, Formulas.DamageWithBuffs(Attack(dam: 500), 300, 0, 0, false, rng, C));
        Assert.Equal(0, rng.Used);
    }

    [Fact] public void DamageWithBuffs_Random_AddsOneRollOfBatDamage()
    {
        var rng = new ScriptedRng([1]);
        Assert.Equal(26, Formulas.DamageWithBuffs(Attack(), 300, 0, 0, true, rng, C));
        Assert.Equal(1, rng.Used);
    }

    /// <summary>Ранг 1 (дизассемблер <c>0x5d2e05</c>-<c>0x5d2f1d</c> плюс живой дебаф в
    /// <c>corpus/20260909-arena2</c>): буст и дебаф — не один множитель и не два последовательных
    /// усечения, а два отдельных слагаемых, каждое усечено само по себе и оба взяты от базы.
    /// Различают все три чтения оба последних случая: 25 − 25·50/100 = 13 (единый множитель дал бы
    /// 25·50/100 = 12), 25 + 6 − 12 = 19 (единый — 25·75/100 = 18, последовательные усечения — 15).</summary>
    [Fact] public void DamageWithBuffs_BoostAndLowerAreSeparateTruncatedTerms()
    {
        var rng = new ScriptedRng([]);
        Assert.Equal(31, Formulas.DamageWithBuffs(Attack(), 300, 25, 0, false, rng, C));   // 25 + 25*25/100
        Assert.Equal(13, Formulas.DamageWithBuffs(Attack(), 300, 0, 50, false, rng, C));   // 25 − 25*50/100
        Assert.Equal(19, Formulas.DamageWithBuffs(Attack(), 300, 25, 50, false, rng, C));  // 25 + 6 − 12
    }

    /// <summary>computeArmor 0x5d2c8f: max(base − shattered, 0) + fort; без защиты потолка нет.
    /// <para><b>Укрепление ПРИБАВЛЯЕТСЯ — правка задачи 16, и не по дизассемблеру, а по корпусу.</b>
    /// <c>S073UN018c</c> (<c>Gunits.ARMOR</c> 30) стоит в укреплении с <c>fort</c> 20 и во всех
    /// десяти событиях <c>armor</c> даёт ровно 50; прежнее чтение «укрепление — нижняя граница»
    /// дало бы 30. По всему журналу партии событий с <c>fort &gt; 0</c> — 183, из них 10 с ненулевой
    /// базой, и все десять за сумму.</para>
    /// <para>Событий со сбитой бронёй во всём корпусе <b>ноль</b>, поэтому её ветка выбрана так,
    /// чтобы сохранить всё проверенное прежде: снятое сверх базы пропадает, а ниже укрепления броня
    /// не падает. Единственный случай, где две редакции расходятся, — <c>0 &lt; shattered &lt; base</c>
    /// при <c>fort &gt; 0</c>: он помечен рангом 3 прямо в теле теста.</para></summary>
    [Fact] public void Armor_FortificationAddsToArmor_NoCapWithoutDefend()
    {
        var unit = Unit(armor: 30);
        unit.FortificationArmor = 20;
        Assert.Equal(50, Formulas.Armor(unit, C));    // 30 + 20 — ранг 1, так пишет журнал партии
        unit.ShatteredArmor = 10;
        unit.FortificationArmor = 5;
        Assert.Equal(25, Formulas.Armor(unit, C));    // (30 − 10) + 5 — ранг 3, корпусом не проверено
        unit.ShatteredArmor = 100;
        Assert.Equal(5, Formulas.Armor(unit, C));     // снятое сверх базы пропадает -> остаётся укрепление
        unit.FortificationArmor = 0;
        Assert.Equal(0, Formulas.Armor(unit, C));
        // Голая реализация без единого слоя CUmUnit не режется (потолок — в CUmUnit::getArmor 0x5a465a
        // и в ветке защиты); в ванили ARMOR ≤ 65, так что это случай мода.
        Assert.Equal(95, Formulas.Armor(Unit(armor: 95), C));
    }

    /// <summary>Защита закрывает DFENDBONUS % промежутка до 100: 0 → 50 (корпус 20260906-a, Man at
    /// Arms), 40 → 70 и 60 → 80 (Gargoyle/Marble Gargoyle в сценарии _d — ещё не сняты), потолок 90.</summary>
    [Fact] public void Armor_DefendClosesBonusPercentOfGapTo100_CappedAt90()
    {
        int Defended(int armor) { var u = Unit(armor); u.Defending = true; return Formulas.Armor(u, C); }
        Assert.Equal(50, Defended(0));
        Assert.Equal(70, Defended(40));
        Assert.Equal(80, Defended(60));
        Assert.Equal(82, Defended(65));   // 65 + 17 (35·50/100 усечено)
        Assert.Equal(90, Defended(95));   // 95 + 2 -> потолок unitArmor.max
        var shattered = Unit(armor: 40); shattered.Defending = true; shattered.ShatteredArmor = 40;
        Assert.Equal(50, Formulas.Armor(shattered, C));   // бонус от брони после shatter, не от базы
        Assert.Equal(0, Formulas.Armor(Unit(armor: 0), C));   // без статуса бонуса нет
        var u75 = Unit(armor: 20); u75.Defending = true;
        Assert.Equal(80, Formulas.Armor(u75, C with { DefendBonus = 75 }));   // DFENDBONUS из GVars, не константа: 20 + 60
    }

    /// <summary>computeDamage 0x65b747: dwb − dwb·armor/100 (0x5838c0: imul/cdq/idiv), не
    /// dwb·(100−armor)/100 тулсета. Пары, где формулы расходятся: 26/30 (19 против 18), 27/50 (14
    /// против 13 — корпус 20260906-a seq 53186), 1/70 (1 против 0). 27/40 = 17 отличает и от
    /// round-half-up (16) — это проверит сценарий _d.</summary>
    [Fact] public void ComputeDamage_SubtractsTruncatedArmorShare()
    {
        Assert.Equal(26, Formulas.ComputeDamage(26, 0));
        Assert.Equal(19, Formulas.ComputeDamage(26, 30));   // 26 − 7
        Assert.Equal(14, Formulas.ComputeDamage(27, 50));   // 27 − 13, корпус
        Assert.Equal(17, Formulas.ComputeDamage(27, 40));   // 27 − 10; round(16.2) дал бы 16
        Assert.Equal(1, Formulas.ComputeDamage(1, 70));     // 1 − 0: урон не ниже 1 при armor < 100
    }

    /// <summary>Ранг 1 (<c>0x65b97d push 5</c>, <c>0x5838c0</c> усечение к нулю): крит — 5 % от dwb.
    /// 125 — удар 100 под BoostDamageLvl1: усиления входят в базу; 300 — потолок dmax.</summary>
    [Fact] public void CriticalDamage_IsFivePercentOfDamageWithBuffs()
    {
        Assert.Equal(0, Formulas.CriticalDamage(19));
        Assert.Equal(1, Formulas.CriticalDamage(26));
        Assert.Equal(5, Formulas.CriticalDamage(100));
        Assert.Equal(6, Formulas.CriticalDamage(125));
        Assert.Equal(15, Formulas.CriticalDamage(300));
    }

    /// <summary>Критует класс Damage при <c>CRIT_HIT</c> (в ванили — <c>g000aa8017</c> Centaur Savage) или
    /// у атакующего со способностью 17 в цепочке (улучшение <c>g000um4103</c>); Drain — никогда
    /// (<c>computeCriticalHit = 0</c>, <c>0x65bdf6</c>).</summary>
    [Fact] public void HasCriticalHit_CritHitFlagOrLeaderAbility17_OnlyForDamageClass()
    {
        var plain = Unit(armor: 0);
        var club = new AttackType("g000aa8017", 50, AttackClass.Damage, 80, 3, 60, 0, CritHit: true, 0);
        Assert.True(Formulas.HasCriticalHit(club, plain));
        Assert.False(Formulas.HasCriticalHit(Attack(), plain));
        Assert.False(Formulas.HasCriticalHit(club with { Class = AttackClass.Drain }, plain));

        var upgrade = new LastEdict.Rules.Units.Modifier("g000um4103",
            [new LastEdict.Rules.Units.ModifierElement(LastEdict.Rules.Units.ModifierElementType.LeaderAbility, Ability: 17)]);
        var leader = new BattleUnit
        {
            Id = "S143UN0002", Type = plain.Type, Attack = Attack(), Attacker = true, Slot = 0,
            HpMax = 65, Hp = 65, Modifiers = [upgrade],
        };
        Assert.True(Formulas.HasCriticalHit(Attack(), leader));
        Assert.False(Formulas.HasCriticalHit(Attack(AttackClass.Drain), leader));
    }

    /// <summary><c>0x5d2def add eax, eax</c>: человеку на лёгкой сложности бросок добавки идёт по
    /// <c>2 × BAT_DAMAGE</c>, остальным — по <c>BAT_DAMAGE</c>.</summary>
    [Fact] public void DamageWithBuffs_HumanOnEasy_DoublesRandomRange()
    {
        var rng = new MaxRecordingRng(7);
        Assert.Equal(32, Formulas.DamageWithBuffs(Attack(), 300, 0, 0, true, rng, C, humanOnEasy: true));
        Assert.Equal(32, Formulas.DamageWithBuffs(Attack(), 300, 0, 0, true, rng, C));
        Assert.Equal([12, 6], rng.Maxes);
    }

    /// <summary>Задача 29 Ф7: слой защиты города сводится к 90 в <c>CUmUnit::getArmor</c>
    /// (<c>0x5a465a</c>), а <c>computeArmor</c> берёт <c>max(soldier − shattered, fort)</c>. Страж
    /// столицы <c>G000UU3002</c> — 50 + 50 → 90 (96 событий корпуса). Под сбитой бронёй потолок
    /// виден: 80 + 50 → 90 − 30 = 60, прежняя сумма дала бы 100.</summary>
    [Fact] public void Armor_FortificationLayerIsCappedAt90_BeforeShatter()
    {
        var guardian = Unit(armor: 50);
        guardian.FortificationArmor = 50;
        Assert.Equal(90, Formulas.Armor(guardian, C));
        var heavy = Unit(armor: 80);
        heavy.FortificationArmor = 50;
        heavy.ShatteredArmor = 30;
        Assert.Equal(60, Formulas.Armor(heavy, C));
        heavy.ShatteredArmor = 100;
        Assert.Equal(50, Formulas.Armor(heavy, C));   // ниже укрепления не падает
    }

    /// <summary>Бросок 0…max−1 с фиксированным ответом; запоминает запрошенные границы.</summary>
    private sealed class MaxRecordingRng(int value) : IRng
    {
        public List<int> Maxes { get; } = [];
        public int Next(int maxValue) { Maxes.Add(maxValue); return value; }
    }

    private static BattleUnit Unit(int armor) => new()
    {
        Id = "S143UN0001",
        Type = new UnitType("g000uu5130", 1, 65, armor, "g000aa5030", "g000000000", false, true, 15, 50),
        Attack = Attack(), Attacker = false, Slot = 0, HpMax = 65, Hp = 65,
    };
}
