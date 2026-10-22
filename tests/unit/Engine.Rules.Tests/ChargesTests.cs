using D2.Data;
using D2.Data.Battle;
using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Rng;
using Xunit.Abstractions;

namespace D2.Rules.Tests;

/// <summary>
/// Задача 10 Ф7 на живой ванили: расход сферы, зарядов талисмана и суточный посох. Предметы взяты
/// поимённо из <c>Globals/GItem.DBF</c>, синтетики нет; корпусные сверки — <see cref="ChargesHarnessTests"/>.
/// </summary>
public class ChargesTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    private static readonly Lazy<IReadOnlyDictionary<string, Item>> Items =
        new(() => D2.Data.Magic.ItemLoader.Load(ImportedData.Content));

    private const string StaffLife = "G000IG6001";   // «Staff of Healing», CASTING 100 маны жизни
    private const string StaffTwo = "G000IG6011";    // CASTING 150 смерти + 150 рун
    private const string Orb = "G000IG9001";         // сфера, атака g000aa9101
    private const string Talisman = "G000IG9101";    // талисман, та же атака
    private const string Armour = "G000IG2001";      // надетая броня — расход её не касается
    private const string Squire = "g000uu0001";

    private static Item Item(string id) => Items.Value[id];

    private static Bank Purse => new(1000, 1000, 1000, 1000, 1000, 1000);

    // ---- 1. талисман: заряды ---------------------------------------------------------------------

    /// <summary>Ряд П§18 целиком: пять зарядов, по одному за бой с применением, ноль — уничтожен.
    /// Бой без применения не тратит ничего, сколько бы боёв ни прошло.</summary>
    [Fact] public void Talisman_SpendsOneChargePerBattleWithApplication()
    {
        int charges = 5;
        var series = new List<int> { charges };
        foreach (bool applied in new[] { true, false, true, true, true, true })
        {
            charges = Charges.TalismanAfterBattle(charges, applied);
            series.Add(charges);
        }
        Assert.Equal([5, 4, 4, 3, 2, 1, 0], series);
        Assert.True(Charges.Destroyed(charges));
        Assert.False(Charges.Destroyed(1));
        output.WriteLine($"ряд зарядов: {string.Join(" → ", series)} (0 — снят и уничтожен)");
    }

    /// <summary>Талисмана без зарядов в сохранении нет вовсе, поэтому спрашивать его расход —
    /// ошибка вызывающего, а не «ноль минус один».</summary>
    [Fact] public void Talisman_WithoutCharges_IsACallerError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Charges.TalismanAfterBattle(0, applied: true));

    // ---- 2. сфера: исчезает только применённая ---------------------------------------------------

    /// <summary>Конец боя: применённая сфера пропадает, неприменённая остаётся, применённый талисман
    /// остаётся (его судьба — заряды), броня не при чём.</summary>
    [SkippableFact] public void AfterBattle_DestroysOnlyAppliedOrb()
    {
        ImportedData.RequireRules();
        var bearer = Unit("A1", attacker: true, Orb, Talisman);
        var other = Unit("D1", attacker: false, Orb, Armour);
        var state = State(bearer, other);
        bearer.AppliedItems.Add(0);   // сферу применили
        bearer.AppliedItems.Add(1);   // и талисман тоже
        // у второго носителя не применяли ничего

        new BattleSim(state, new ScriptedRng(Enumerable.Repeat(0, 8)), Rules.Value.Constants).AfterBattle();

        Assert.Equal([Talisman], bearer.BattleItems.Select(i => i.Id).ToArray());
        Assert.Equal([Orb, Armour], other.BattleItems.Select(i => i.Id).ToArray());
        Assert.Empty(bearer.AppliedItems);   // множество живёт один бой
    }

    /// <summary>Применение — это удар по цели: <see cref="BattleSim.UseBattleItem"/> отмечает номер
    /// предмета и бьёт атакой предмета (без броска на промах, задача 32), а конец боя эту сферу
    /// съедает. Построение атаки в модели вообще ничего не отмечает — отмечать нечему.</summary>
    [SkippableFact] public void UseBattleItem_MarksApplied_AndSpendsTheOrb()
    {
        ImportedData.RequireRules();
        var bearer = Unit("A1", attacker: true, Orb);
        var target = Unit("D1", attacker: false);
        var state = State(bearer, target);
        var sim = new BattleSim(state, new ScriptedRng(Enumerable.Repeat(0, 8)), Rules.Value.Constants);

        var outcome = sim.UseBattleItem(bearer, 0, bearer.Attack, target);

        Assert.False(outcome.Miss);                    // у атаки предмета броска точности нет вовсе
        Assert.Equal([0], bearer.AppliedItems.Order().ToArray());
        sim.AfterBattle();
        Assert.Empty(bearer.BattleItems);
    }

    /// <summary>Номер предмета вне боевых слотов — ошибка вызывающего.</summary>
    [SkippableFact] public void UseBattleItem_RejectsUnknownIndex()
    {
        ImportedData.RequireRules();
        var bearer = Unit("A1", attacker: true, Orb);
        var target = Unit("D1", attacker: false);
        var sim = new BattleSim(State(bearer, target), new ScriptedRng([0]), Rules.Value.Constants);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.UseBattleItem(bearer, 1, bearer.Attack, target));
    }

    // ---- 3. посох: сутки и мана ------------------------------------------------------------------

    /// <summary>Цена применения — своя колонка <c>CASTING</c>: 100 маны жизни у <c>IG6001</c>,
    /// по 150 смерти и рун у <c>IG6011</c> (замер П§18). Остальная казна не движется.</summary>
    [SkippableFact] public void PayCasting_TakesTheItemsOwnColumn()
    {
        ImportedData.RequireRules();
        Assert.Equal(new Bank(0, 0, 100, 0, 0, 0), Item(StaffLife).CastingCost);
        Assert.Equal(new Bank(0, 0, 0, 150, 150, 0), Item(StaffTwo).CastingCost);
        Assert.Equal(new Bank(1000, 1000, 900, 1000, 1000, 1000), SpellCast.PayCasting(Purse, Item(StaffLife)));
        Assert.Throws<InvalidOperationException>(
            () => SpellCast.PayCasting(new Bank(9999, 9999, 50, 9999, 9999, 9999), Item(StaffLife)));
    }

    /// <summary>«Раз в сутки на экземпляр»: тот же <c>IM</c> второй раз за день — отказ, второй
    /// ЭКЗЕМПЛЯР того же типа — проходит (П§19: два одинаковых <c>IG6001</c> у двух героев одного
    /// игрока сработали в один день), передача применённого другому герою того же игрока не помогает
    /// (ключ — игрок плюс предмет), а начало дня список очищает.</summary>
    [SkippableFact] public void StaffUses_AreOncePerInstancePerDay()
    {
        ImportedData.RequireRules();
        var staff = new StaffUses();
        var first = staff.Use(Item(StaffLife), "P1", "IM1", Purse);
        Assert.True(first.Done);
        Assert.Equal(new Bank(1000, 1000, 900, 1000, 1000, 1000), first.Treasury);

        Assert.Equal(StaffRefusal.UsedToday, staff.Use(Item(StaffLife), "P1", "IM1", Purse).Refusal);
        Assert.True(staff.Use(Item(StaffLife), "P1", "IM2", Purse).Done);        // второй экземпляр
        Assert.True(staff.Use(Item(StaffLife), "P2", "IM1", Purse).Done);        // другой игрок
        Assert.Equal(3, staff.Count);

        staff.NewDay();
        Assert.Equal(0, staff.Count);
        Assert.True(staff.Use(Item(StaffLife), "P1", "IM1", Purse).Done);
    }

    /// <summary>§3 пп. 36, 54: запись снимается в начале хода СВОЕГО игрока при <c>ход ≥ TURN_STOP</c>;
    /// чужие записи его начало хода не трогает.</summary>
    [SkippableFact] public void StaffUses_ExpireAtOwnersTurnStart_PerPlayer()
    {
        ImportedData.RequireRules();
        var staff = new StaffUses();
        Assert.True(staff.Use(Item(StaffLife), "P1", "IM1", Purse, turn: 7).Done);
        Assert.True(staff.Use(Item(StaffLife), "P2", "IM2", Purse, turn: 7).Done);

        staff.StartTurn("P1", 7);                    // тот же день — рано
        Assert.Equal(2, staff.Count);
        staff.StartTurn("P2", 8);                    // день 8, ход P2: снимается только его запись
        Assert.Equal(1, staff.Count);
        Assert.True(staff.UsedToday("P1", "IM1"));   // P1 ещё не ходил — его запись лежит
        staff.StartTurn("p1", 8);
        Assert.Equal(0, staff.Count);
        Assert.True(staff.Use(Item(StaffLife), "P1", "IM1", Purse, turn: 8).Done);
    }

    /// <summary>Отказы: не посох и нехватка маны. Отказ не меняет ни казну, ни список.</summary>
    [SkippableFact] public void StaffUses_RefuseWithoutChangingAnything()
    {
        ImportedData.RequireRules();
        var staff = new StaffUses();
        var thin = new Bank(9999, 9999, 50, 9999, 9999, 9999);

        var notStaff = staff.Use(Item(Orb), "P1", "IM1", Purse);
        Assert.Equal(StaffRefusal.NotAStaff, notStaff.Refusal);
        Assert.Equal(Purse, notStaff.Treasury);

        var poor = staff.Use(Item(StaffLife), "P1", "IM1", thin);
        Assert.Equal(StaffRefusal.NoMana, poor.Refusal);
        Assert.Equal(thin, poor.Treasury);
        Assert.Equal(0, staff.Count);
        Assert.False(staff.UsedToday("P1", "IM1"));
    }

    /// <summary>Список читается из сохранения (второй список <c>CMidSpellCast</c>) и после этого
    /// запрещает те же экземпляры: регистр идентификатора роли не играет.</summary>
    [SkippableFact] public void StaffUses_LoadFromSave_BlocksTheSameInstances()
    {
        ImportedData.RequireRules();
        var staff = new StaffUses();
        staff.Load([new CastRecord("S143PL0001", "S143IM0008", 2)]);
        Assert.Equal(1, staff.Count);
        Assert.True(staff.UsedToday("s143pl0001", "s143im0008"));
        Assert.Equal(StaffRefusal.UsedToday, staff.Use(Item(StaffLife), "S143PL0001", "S143IM0008", Purse).Refusal);
        Assert.True(staff.Use(Item(StaffLife), "S143PL0001", "S143IM0009", Purse).Done);
    }

    /// <summary>Калитки <c>0x5dbbe8</c> (отчёт B, ai-q34): два разных экземпляра в один день — оба;
    /// тот же экземпляр второй раз — 0x11 у Воителя, у Мага можно, третий раз у Мага — 0x12.</summary>
    [SkippableFact] public void StaffUses_MageHasTwoUsesPerInstance_OthersOne()
    {
        ImportedData.RequireRules();
        var warrior = new StaffUses();
        Assert.True(warrior.Use(Item(StaffLife), "P1", "IM1", Purse).Done);
        Assert.True(warrior.Use(Item(StaffLife), "P1", "IM2", Purse).Done);
        Assert.Equal(StaffRefusal.UsedToday, warrior.Use(Item(StaffLife), "P1", "IM1", Purse).Refusal);
        Assert.Equal(0x11, (int)StaffRefusal.UsedToday);

        var mage = new StaffUses();
        Assert.True(mage.Use(Item(StaffLife), "P1", "IM1", Purse, lord: LordCategory.Mage).Done);
        Assert.True(mage.Use(Item(StaffLife), "P1", "IM1", Purse, lord: LordCategory.Mage).Done);
        var third = mage.Use(Item(StaffLife), "P1", "IM1", Purse, lord: LordCategory.Mage);
        Assert.Equal(StaffRefusal.MageLimit, third.Refusal);
        Assert.Equal(0x12, (int)StaffRefusal.MageLimit);
        Assert.Equal(Purse, third.Treasury);
        Assert.Equal(2, mage.Count);   // каждая запись — строка второго списка CMidSpellCast
        Assert.Equal(2, mage.UsesToday("P1", "IM1"));
    }

    /// <summary>Мана проверяется раньше «сегодня», а мёртвый лидер или нет <c>L_WAND_SCROLL_USE</c>
    /// (0xE) — раньше маны.</summary>
    [SkippableFact] public void StaffUses_GateOrder_SkillThenManaThenToday()
    {
        ImportedData.RequireRules();
        var staff = new StaffUses();
        var thin = new Bank(9999, 9999, 50, 9999, 9999, 9999);
        Assert.True(staff.Use(Item(StaffLife), "P1", "IM1", Purse).Done);

        Assert.Equal(StaffRefusal.NoMana, staff.Use(Item(StaffLife), "P1", "IM1", thin).Refusal);
        Assert.Equal(9, (int)StaffRefusal.NoMana);

        var dead = staff.Use(Item(StaffLife), "P1", "IM1", thin, leaderAlive: false);
        Assert.Equal(StaffRefusal.CannotUse, dead.Refusal);
        Assert.Equal(0xE, (int)StaffRefusal.CannotUse);
        Assert.Equal(StaffRefusal.CannotUse,
                     staff.Use(Item(StaffLife), "P1", "IM2", Purse, hasWandScrollUse: false).Refusal);
        Assert.Equal(1, staff.Count);
    }

    // ---- 4. зелье в бою --------------------------------------------------------------------------

    private const string HealPotion = "G000IG0005";   // HP_POTION 50
    private const string ArmorPotion = "G000IG0002";  // MOD_POTION g000um2002: броня +15

    /// <summary>Лечебное зелье в бою: 83 → 100 при потолке 100 и +50 (ai-q33, seq 122535), предмет
    /// применён и после боя исчезает.</summary>
    [SkippableFact] public void DrinkPotion_Heal_ClampsToMaxAndSpendsThePotion()
    {
        ImportedData.RequireRules();
        var bearer = Unit("A1", attacker: true, HealPotion);
        var enemy = Unit("D1", attacker: false);
        bearer.HpMax = 100;
        bearer.Hp = 83;
        var sim = new BattleSim(State(bearer, enemy), new ScriptedRng([0]), Rules.Value.Constants);

        Assert.Equal(PotionResult.Ok, sim.DrinkPotion(bearer, 0, bearer));

        Assert.Equal(100, bearer.Hp);
        Assert.Equal([0], bearer.AppliedItems.Order().ToArray());
        sim.AfterBattle();
        Assert.Empty(bearer.BattleItems);
    }

    /// <summary>Зелье защиты: <c>MOD_POTION</c> ложится сразу и держится до конца боя — броня цели
    /// 0 → 15 в следующих ударах (ai-q33: Squire S135UN0141, раунды 2 и 3).</summary>
    [SkippableFact] public void DrinkPotion_Boost_AddsArmorForTheRestOfTheBattle()
    {
        ImportedData.RequireRules();
        var bearer = Unit("A1", attacker: true, ArmorPotion);
        var enemy = Unit("D1", attacker: false);
        var sim = new BattleSim(State(bearer, enemy), new ScriptedRng(Enumerable.Repeat(0, 8)), Rules.Value.Constants);
        Assert.Equal(0, Formulas.Armor(bearer, Rules.Value.Constants));

        Assert.Equal(PotionResult.Ok, sim.DrinkPotion(bearer, 0, bearer));

        Assert.Equal(15, Formulas.Armor(bearer, Rules.Value.Constants));
        sim.BeginTurn(bearer);   // ход сам по себе зелье не снимает
        sim.EndTurn(bearer);
        Assert.Equal(15, Formulas.Armor(bearer, Rules.Value.Constants));
        // Второй раз то же зелье не пьётся — модификатор уже висит (0x5de603).
        bearer.BattleItems.Add(Item(ArmorPotion));
        Assert.Equal(PotionResult.AlreadyApplied, sim.DrinkPotion(bearer, 1, bearer));
        Assert.Equal([0], bearer.AppliedItems.Order().ToArray());
    }

    // ---- прочее ----------------------------------------------------------------------------------

    private static BattleUnit Unit(string id, bool attacker, params string[] itemIds)
    {
        var type = Rules.Value.Units[Squire];
        var unit = new BattleUnit
        {
            Id = id, Type = type, Attack = Rules.Value.AttackOf(type)!, Attacker = attacker, Slot = 0,
            HpMax = type.HitPoints, Hp = type.HitPoints,
        };
        foreach (var itemId in itemIds) unit.BattleItems.Add(Item(itemId));
        return unit;
    }

    private static BattleState State(params BattleUnit[] units) => new()
    {
        Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Round = 1, Rules = Rules.Value,
    };
}
