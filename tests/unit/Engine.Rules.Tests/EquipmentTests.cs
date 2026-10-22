using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Rules.Tests;

/// <summary>Модульные тесты задачи 9: гейт экипировки, карта слотов, три источника способностей и
/// цепочка <c>MOD_EQUIP</c>. Синтетика — чтобы проверить то, чего в корпусе нет: <b>отказов
/// надевания в нём нет ни одного</b> (947 строк <c>item_equip</c>, все <c>ret = true</c>).</summary>
public class EquipmentTests
{
    private static Item Of(ItemCategory category, string modifier = "") =>
        new("G000IG0001", category, Bank.Zero, modifier, "", 0, "", Bank.Zero, "", "");

    private static MapStack Stack(string[]? units = null, string[]? slots = null, string[]? bag = null) => new()
    {
        Id = "S000KC0000", Owner = "S000PL0000", Subrace = "S000SR0000", Inside = "",
        LeaderId = (units ?? ["U1"])[0], LeaderAlive = true,
        UnitIds = units ?? ["U1"], Slots = [0, -1, -1, -1, -1, -1],
        Equipment = slots ?? [], Items = bag ?? [],
    };

    private static readonly IReadOnlySet<int> None = new HashSet<int>();
    private static IReadOnlySet<int> Has(params int[] a) => a.ToHashSet();

    [Fact] public void RequiredAbility_SevenCategoriesGated_RestFree()
    {
        // Таблица levels §1.2: броня и оружие делят 8, остальное по одной; у зелий, свитков,
        // посохов, драгоценностей и квестовых предметов гейта нет.
        Assert.Equal(8, Equipment.RequiredAbility(ItemCategory.Armor));
        Assert.Equal(8, Equipment.RequiredAbility(ItemCategory.Weapon));
        Assert.Equal(10, Equipment.RequiredAbility(ItemCategory.Jewel));
        Assert.Equal(9, Equipment.RequiredAbility(ItemCategory.Banner));
        Assert.Equal(14, Equipment.RequiredAbility(ItemCategory.Orb));
        Assert.Equal(15, Equipment.RequiredAbility(ItemCategory.Talisman));
        Assert.Equal(16, Equipment.RequiredAbility(ItemCategory.TravelItem));
        foreach (var free in new[] { ItemCategory.PotionBoost, ItemCategory.PotionHeal,
                                     ItemCategory.PotionRevive, ItemCategory.PotionPermanent,
                                     ItemCategory.Scroll, ItemCategory.Wand,
                                     ItemCategory.Valuable, ItemCategory.Special })
            Assert.Null(Equipment.RequiredAbility(free));
    }

    [Fact] public void SlotAccepts_MapMatchesTheEngineFunction()
    {
        // 0x005EC309 целиком: 0 знамя, 1 книга, 2-3 зелья/сферы/талисманы, 4-5 броня/оружие, 6 сапоги.
        var expected = new Dictionary<int, ItemCategory[]>
        {
            [0] = [ItemCategory.Banner],
            [1] = [ItemCategory.Jewel],
            [2] = [ItemCategory.PotionBoost, ItemCategory.PotionHeal, ItemCategory.PotionRevive,
                   ItemCategory.PotionPermanent, ItemCategory.Orb, ItemCategory.Talisman],
            [4] = [ItemCategory.Armor, ItemCategory.Weapon],
            [6] = [ItemCategory.TravelItem],
        };
        expected[3] = expected[2];
        expected[5] = expected[4];
        foreach (var category in Enum.GetValues<ItemCategory>())
            for (int slot = -1; slot < 8; slot++)
                Assert.Equal(expected.GetValueOrDefault(slot, []).Contains(category),
                             Equipment.SlotAccepts(category, slot));
    }

    [Fact] public void Check_RefusalCodesAndOrder_FollowTheEngine()
    {
        // Порядок UI: экипируемость -> жив -> способность -> эффект -> слот.
        Assert.Equal(EquipRefusal.NotEquippable,
                     Equipment.Check(Of(ItemCategory.Scroll), 2, Has(7), leaderAlive: true));
        Assert.Equal(EquipRefusal.NotEquippable,
                     Equipment.Check(Of(ItemCategory.Wand), 2, Has(7), leaderAlive: true));
        Assert.Equal(EquipRefusal.NotEquippable,
                     Equipment.Check(Of(ItemCategory.Valuable), 4, Has(8), leaderAlive: true));
        // Мёртвый лидер проверяется РАНЬШЕ способности: у героя её нет, а код всё равно 8.
        Assert.Equal(EquipRefusal.DeadLeader,
                     Equipment.Check(Of(ItemCategory.Orb), 2, None, leaderAlive: false));
        Assert.Equal(EquipRefusal.NeedOrbUse, Equipment.Check(Of(ItemCategory.Orb), 2, None, true));
        Assert.Equal(EquipRefusal.NeedTalismanUse, Equipment.Check(Of(ItemCategory.Talisman), 2, Has(14), true));
        Assert.Equal(EquipRefusal.NeedWeaponArmorUse, Equipment.Check(Of(ItemCategory.Armor), 4, Has(9), true));
        Assert.Equal(EquipRefusal.NeedJewelryUse, Equipment.Check(Of(ItemCategory.Jewel), 1, Has(8), true));
        Assert.Equal(EquipRefusal.NeedBannerUse, Equipment.Check(Of(ItemCategory.Banner), 0, Has(8), true));
        Assert.Equal(EquipRefusal.NeedTravelItemUse, Equipment.Check(Of(ItemCategory.TravelItem), 6, Has(8), true));
        // Способность проверяется РАНЬШЕ слота: сфера в слот сапог даёт «нет навыка», а не «нельзя».
        Assert.Equal(EquipRefusal.NeedOrbUse, Equipment.Check(Of(ItemCategory.Orb), 6, None, true));
        // Эффект — после способности, но до слота.
        Assert.Equal(EquipRefusal.NoEffect,
                     Equipment.Check(Of(ItemCategory.Jewel, "um14"), 3, Has(10, 14), true, Grants));
        Assert.Equal(EquipRefusal.NotEquippable, Equipment.Check(Of(ItemCategory.Orb), 6, Has(14), true));
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(ItemCategory.Orb), 3, Has(14), true));
    }

    // MOD_EQUIP книг ai-q30: g000um2406 даёт способность 14, g000um2407 — 15 (GmodifL TYPE 11).
    private static readonly IReadOnlyDictionary<string, Modifier> Grants = new Dictionary<string, Modifier>
    {
        ["um14"] = new("um14", [new ModifierElement(ModifierElementType.LeaderAbility, Ability: 14)]),
        ["um15"] = new("um15", [new ModifierElement(ModifierElementType.LeaderAbility, Ability: 15)]),
        ["um-armor"] = new("um-armor", [new ModifierElement(ModifierElementType.Armor, Number: 10)]),
    };

    /// <summary>ai-q30 (отчёт B, seq 1026476): Архимаг с врождённой способностью 14 — книга IG4006
    /// (способность 14) даёт 0xB, книга IG4007 (15) надевается. Ранг 1 на одну точку.</summary>
    [Fact] public void Check_ItemGrantingAnAbilityTheLeaderHas_IsNoEffect()
    {
        var archmage = Has(10, 14);
        Assert.Equal(EquipRefusal.NoEffect,
                     Equipment.Check(Of(ItemCategory.Jewel, "um14"), Equipment.SlotTome, archmage, true, Grants));
        Assert.Equal(EquipRefusal.Ok,
                     Equipment.Check(Of(ItemCategory.Jewel, "um15"), Equipment.SlotTome, archmage, true, Grants));
        // Модификатор без способности правилу не подлежит.
        Assert.Equal(EquipRefusal.Ok,
                     Equipment.Check(Of(ItemCategory.Jewel, "um-armor"), Equipment.SlotTome, archmage, true, Grants));
    }

    // MOD_EQUIP ванили: сапоги g000um2111 (TYPE 10, MOVE 3 — вода), книга g000um2401 (TYPE 12, IMMUNITY 7
    // воздух, IMMUNECAT 2 — Once), тот же оберег Always — выдуманный, чтобы проверить ветку 0x800.
    private static readonly IReadOnlyDictionary<string, Modifier> Effects = new Dictionary<string, Modifier>
    {
        ["water"] = new("water", [new ModifierElement(ModifierElementType.MoveAbility, Move: 3)]),
        ["air-once"] = new("air-once", [new ModifierElement(ModifierElementType.Immunity, Immunity: 7, ImmuneCat: 2)]),
        ["air-always"] = new("air-always", [new ModifierElement(ModifierElementType.Immunity, Immunity: 7, ImmuneCat: 3)]),
        ["armor"] = new("armor", [new ModifierElement(ModifierElementType.Armor, Number: 10)]),
    };

    /// <summary>0x5EC48F целиком: земля и иммунитет тоже дают 0xB; только сапоги, книга и знамя.</summary>
    [Fact] public void Check_NoEffect_CoversGroundAndImmunity_OnlyForBootsTomeBanner()
    {
        var gate = Has(9, 10, 16, 8);
        static LeaderEffects With(int[] grounds, params ImmunityGrant[] src) => new(grounds.ToHashSet(), src, []);
        var sailor = With([3]);
        var onceAir = With([], new ImmunityGrant(7, ImmunityLevel.Once));
        var alwaysAir = With([], new ImmunityGrant(7, ImmunityLevel.Always));
        var otherSource = With([], new ImmunityGrant(4, ImmunityLevel.Always));

        Assert.Equal(EquipRefusal.NoEffect, Equipment.Check(Of(ItemCategory.TravelItem, "water"), Equipment.SlotBoots, gate, true, Effects, sailor));
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(ItemCategory.TravelItem, "water"), Equipment.SlotBoots, gate, true, Effects, With([1])));
        // Once-оберег: отказ и при Once, и при Always; Always-элемент — только при Always.
        Assert.Equal(EquipRefusal.NoEffect, Equipment.Check(Of(ItemCategory.Jewel, "air-once"), Equipment.SlotTome, gate, true, Effects, onceAir));
        Assert.Equal(EquipRefusal.NoEffect, Equipment.Check(Of(ItemCategory.Jewel, "air-once"), Equipment.SlotTome, gate, true, Effects, alwaysAir));
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(ItemCategory.Jewel, "air-once"), Equipment.SlotTome, gate, true, Effects, otherSource));
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(ItemCategory.Jewel, "air-always"), Equipment.SlotTome, gate, true, Effects, onceAir));
        Assert.Equal(EquipRefusal.NoEffect, Equipment.Check(Of(ItemCategory.Banner, "air-always"), Equipment.SlotBanner, gate, true, Effects, alwaysAir));
        // Категория вне трёх (броня) не проверяется, статы не проверяются.
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(ItemCategory.Armor, "air-always"), Equipment.SlotArtifact1, gate, true, Effects, alwaysAir));
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(ItemCategory.Banner, "armor"), Equipment.SlotBanner, gate, true, Effects, alwaysAir));
    }

    [Fact] public void Check_PotionsHaveNoAbilityGate_AndOnlyBattleSlots()
    {
        foreach (var potion in new[] { ItemCategory.PotionBoost, ItemCategory.PotionHeal,
                                       ItemCategory.PotionRevive, ItemCategory.PotionPermanent })
        {
            Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(potion), 2, None, leaderAlive: true));
            Assert.Equal(EquipRefusal.Ok, Equipment.Check(Of(potion), 3, None, leaderAlive: true));
            Assert.Equal(EquipRefusal.NotEquippable, Equipment.Check(Of(potion), 0, None, true));
            Assert.Equal(EquipRefusal.NotEquippable, Equipment.Check(Of(potion), 4, None, true));
        }
    }

    [Fact] public void Equip_BannerGoesToEveryUnit_OtherItemsToTheLeaderOnly()
    {
        var stack = Stack(["U1", "U2", "U3"], ["", "", "", "", "", "", ""]);
        var banner = Equipment.Equip(stack, Of(ItemCategory.Banner, "g000um2107"), "S000IM0001",
                                     Equipment.SlotBanner, Has(9), leaderAlive: true);
        Assert.True(banner.Done);
        Assert.Equal(["U1", "U2", "U3"], banner.Bearers);
        Assert.Equal("S000IM0001", banner.Slots[Equipment.SlotBanner]);

        var sword = Equipment.Equip(stack, Of(ItemCategory.Weapon, "g000um2301"), "S000IM0002",
                                    Equipment.SlotArtifact1, Has(8), leaderAlive: true);
        Assert.Equal(["U1"], sword.Bearers);
        Assert.Equal("g000um2301", sword.ModifierId);
    }

    [Fact] public void Equip_RefusedKeepsSlotsAndGivesNobodyAnything()
    {
        var stack = Stack(slots: ["", "", "", "", "", "", ""]);
        var refused = Equipment.Equip(stack, Of(ItemCategory.Talisman, "g000um0001"), "S000IM0003",
                                      Equipment.SlotBattle1, None, leaderAlive: true);
        Assert.False(refused.Done);
        Assert.Equal(EquipRefusal.NeedTalismanUse, refused.Refusal);
        Assert.Empty(refused.Bearers);
        Assert.All(refused.Slots, s => Assert.Equal("", s));
    }

    [Fact] public void Equip_OccupiedSlotThrows_EmptyUnequipThrows()
    {
        var stack = Stack(slots: ["", "", "S000IM0004", "", "", "", ""]);
        Assert.Throws<InvalidOperationException>(() => Equipment.Equip(stack, Of(ItemCategory.Orb),
            "S000IM0005", Equipment.SlotBattle1, Has(14), leaderAlive: true));
        Assert.Throws<InvalidOperationException>(() =>
            Equipment.Unequip(stack, Equipment.SlotBattle2, Of(ItemCategory.Orb)));
    }

    [Fact] public void Unequip_ClearsTheSlotAndNamesTheSameBearers()
    {
        var stack = Stack(["U1", "U2"], ["S000IM0006", "", "", "", "", "", ""]);
        var off = Equipment.Unequip(stack, Equipment.SlotBanner, Of(ItemCategory.Banner, "g000um2107"));
        Assert.True(off.Done);
        Assert.Equal("", off.Slots[Equipment.SlotBanner]);
        Assert.Equal(["U1", "U2"], off.Bearers);
        Assert.Equal("g000um2107", off.ModifierId);
    }

    [Fact] public void Carried_SeesBothTheBagAndTheSlots()
    {
        var stack = Stack(slots: ["S000IM0001", "", "", "", "", "", ""], bag: ["S000IM0002"]);
        Assert.True(Equipment.Carried(stack, "s000im0001"));
        Assert.True(Equipment.Carried(stack, "S000IM0002"));
        Assert.False(Equipment.Carried(stack, "S000IM0003"));
    }

    [Fact] public void Slots_SyntheticStackHasSevenEmpty_WrongCountThrows()
    {
        Assert.Equal(7, Equipment.Slots(Stack()).Count);
        Assert.All(Equipment.Slots(Stack()), s => Assert.Equal("", s));
        Assert.Throws<InvalidDataException>(() => Equipment.Slots(Stack(slots: ["", ""])));
    }

    // ------------------------------------------------------------------ три источника способностей

    private static Modifier Ability(string id, int ability) =>
        new(id, [new ModifierElement(ModifierElementType.LeaderAbility, Ability: ability)]);

    [Fact] public void Split_SeparatesInnateLevelsAndItems()
    {
        var tome = Of(ItemCategory.Jewel, "g000um2407");          // Книга колдовства -> 15
        var catalogue = new Dictionary<string, Modifier>(StringComparer.OrdinalIgnoreCase)
        {
            ["g000um2407"] = Ability("g000um2407", 15),
            ["g000um4002"] = Ability("g000um4002", 10),           // «Тайное знание» -> 10
        };
        var innate = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["G000UU0021"] = [7, 14],                             // Архимаг
        };

        // Цепочка сохранения несёт и уровневый навык, и MOD_EQUIP надетой книги.
        var sources = LeaderAbilities.Split("G000UU0021", innate, ["g000um4002", "g000um2407"],
                                            [tome], [tome], catalogue);
        Assert.Equal([7, 14], sources.Innate.Order());
        Assert.Equal([10], sources.FromLevels.Order());
        Assert.Equal([15], sources.FromItems.Order());
        Assert.Equal([7, 10, 14, 15], sources.All.Order());
        Assert.Equal("GLabi", sources.Where(14));
        Assert.Equal("уровень", sources.Where(10));
        Assert.Equal("предмет", sources.Where(15));
        Assert.Equal("", sources.Where(13));

        // Та же цепочка, но книга — проверяемый предмет: её способности ещё нет ни в одном источнике.
        var gate = LeaderAbilities.Split("G000UU0021", innate, ["g000um4002", "g000um2407"],
                                         [tome], [], catalogue);
        Assert.Empty(gate.FromItems);
        Assert.Equal([10], gate.FromLevels.Order());
        Assert.Equal(EquipRefusal.Ok, Equipment.Check(tome, Equipment.SlotTome, gate.All, true));
        // Без уровневого навыка книгу надеть нельзя — источник несущий.
        var bare = LeaderAbilities.Split("G000UU0021", innate, [], [], [], catalogue);
        Assert.Equal(EquipRefusal.NeedJewelryUse, Equipment.Check(tome, Equipment.SlotTome, bare.All, true));
    }

    [Fact] public void Split_UnknownModifierThrows_AndEmptyInnateIsFine()
    {
        var catalogue = new Dictionary<string, Modifier>(StringComparer.OrdinalIgnoreCase);
        var innate = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() =>
            LeaderAbilities.Split("G000UU0023", innate, ["g000um9999"], [], [], catalogue));
        // Вор: врождённых нет ни одной — пустой набор, а не исключение.
        Assert.Empty(LeaderAbilities.Split("G000UU0023", innate, [], [], [], catalogue).All);
    }
}
