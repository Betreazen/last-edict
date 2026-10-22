using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Rng;
using Xunit.Abstractions;

/// <summary>
/// Долг задачи 10 фазы Ф6: уничтожение боевых предметов в конце боя — первый пункт survey §7
/// (предметы → снятие призыва и превращений → groupUpgrade → battleCount). Проверяется врезка
/// <see cref="BattleSim.AfterBattle"/>; само правило «какой вид исчезает» проверяет
/// <c>ItemTests.DestroyedAfterBattle_CoversOrbAndPotions_ButNotTalisman</c>.
///
/// <para>Предметы настоящие, из <c>Globals/GItem.DBF</c>, взяты поимённо; синтетики нет. Игра не
/// запускалась, и подтвердить врезку журналом нечем: применений предмета в корпусе Ф6 ноль (ранг
/// врезки назван в <c>BattleSim.DestroyBattleItems</c>).</para>
/// </summary>
public class BattleItemsAfterBattleTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    private static readonly Lazy<IReadOnlyDictionary<string, Item>> Items =
        new(() => LastEdict.Data.Magic.ItemLoader.Load(ImportedData.Content));

    private const string Orb = "base:item/squire-orb";        // «Squire Orb», категория 11
    private const string Talisman = "base:item/squire-talisman";   // «Squire Talisman», категория 12
    private const string HealPotion = "base:item/potion-of-healing"; // «Potion of Healing», 50 HP, категория 5
    private const string LifePotion = "base:item/life-potion"; // «Life Potion», воскрешение, категория 6
    private const string Squire = "base:unit/squire-empire";     // носитель: L1, hp 100

    private static Item Item(string id) => Items.Value[id];

    private static BattleUnit Leader(bool attacker, params string[] itemIds)
    {
        var type = Rules.Value.Units[Squire];
        var unit = new BattleUnit
        {
            Id = attacker ? "A1" : "D1", Type = type, Attack = Rules.Value.AttackOf(type)!,
            Attacker = attacker, Slot = 0, HpMax = type.HitPoints, Hp = type.HitPoints,
        };
        foreach (var id in itemIds) unit.BattleItems.Add(Item(id));
        return unit;
    }

    private static BattleState State(params BattleUnit[] units) => new()
    {
        Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Round = 0, Rules = Rules.Value,
    };

    private static BattleSim Sim(BattleState state) =>
        new(state, new ScriptedRng(Enumerable.Repeat(0, 8)), Rules.Value.Constants);

    /// <summary>Категории четырёх предметов, на которых стоят утверждения ниже, — из живой ванили.</summary>
    [SkippableFact] public void VanillaItems_AreWhatTheTestsAssume()
    {
        ImportedData.RequireRules();
        Assert.Equal(ItemCategory.Orb, Item(Orb).Category);
        Assert.Equal(ItemCategory.Talisman, Item(Talisman).Category);
        Assert.Equal(ItemCategory.PotionHeal, Item(HealPotion).Category);
        Assert.Equal(ItemCategory.PotionRevive, Item(LifePotion).Category);
    }

    /// <summary>Конец боя обходит боевые слоты обеих сторон: <b>применённые</b> сфера и оба зелья
    /// исчезают, применённый талисман остаётся. По два предмета на сторону — ровно столько слотов
    /// снаряжения обходит движок (<c>checkAndDestroyEquippedBattleItems</c>, GOG <c>0x629755</c>).
    /// <para>Признак применения ставится вручную: кто и когда применяет — не дело
    /// <see cref="BattleSim.AfterBattle"/>; в бою его ставит <see cref="BattleSim.UseBattleItem"/>
    /// (задача 10, <c>ChargesTests</c>).</para></summary>
    [SkippableFact] public void AfterBattle_DestroysAppliedOrbAndPotions_OnBothSides()
    {
        ImportedData.RequireRules();
        var attacker = Leader(attacker: true, Orb, Talisman);
        var defender = Leader(attacker: false, HealPotion, LifePotion);
        var state = State(attacker, defender);
        foreach (var u in state.Units)
            for (int i = 0; i < u.BattleItems.Count; i++) u.AppliedItems.Add(i);

        Sim(state).AfterBattle();

        Assert.Equal([Talisman], attacker.BattleItems.Select(i => i.Id).ToArray());
        Assert.Empty(defender.BattleItems);
        output.WriteLine($"после боя у атакующего {attacker.BattleItems.Count} предмет " +
                         $"({string.Join(", ", attacker.BattleItems.Select(i => i.Id))}), " +
                         $"у обороняющегося {defender.BattleItems.Count}");
    }

    /// <summary>Талисман остаётся и когда носитель несёт второй такой же: его судьбу решает остаток
    /// зарядов (<c>0x5eb9b3</c>, <c>jge</c> на <c>0x6299f4</c>) — состояние карты, а не боя
    /// (<see cref="Charges.TalismanAfterBattle"/>, задача 10). Оба применены, и оба остаются.</summary>
    [SkippableFact] public void AfterBattle_KeepsTalisman_WithAndWithoutDuplicate()
    {
        ImportedData.RequireRules();
        var single = Leader(attacker: true, Talisman);
        var doubled = Leader(attacker: false, Talisman, Talisman);
        var state = State(single, doubled);
        foreach (var u in state.Units)
            for (int i = 0; i < u.BattleItems.Count; i++) u.AppliedItems.Add(i);

        Sim(state).AfterBattle();

        Assert.Single(single.BattleItems);
        Assert.Equal(2, doubled.BattleItems.Count);
        Assert.All(doubled.BattleItems, i => Assert.Equal(ItemCategory.Talisman, i.Category));
    }

    /// <summary>Бой без предметов конец боя не трогает вовсе — иначе врезка сдвинула бы С2.</summary>
    [SkippableFact] public void AfterBattle_LeavesItemlessBattleAlone()
    {
        ImportedData.RequireRules();
        var (attacker, defender) = (Leader(attacker: true), Leader(attacker: false));
        var state = State(attacker, defender);

        Sim(state).AfterBattle();

        Assert.Equal(2, state.Units.Count);
        Assert.All(state.Units, u => Assert.Empty(u.BattleItems));
        Assert.All(state.Units, u => Assert.Equal(100, u.Hp));
    }
}
