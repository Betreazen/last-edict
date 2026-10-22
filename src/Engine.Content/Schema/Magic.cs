using System.ComponentModel;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Content.Schema;

/// <summary>Заклинание (<c>Gspells.dbf</c>).</summary>
[LegacyTable("Gspells", "SPELL_ID")]
public sealed record SpellDef : ContentDef
{
    [Legacy("CATEGORY")] public SpellCategory Category { get; init; }
    [Legacy("LEVEL")] public int Level { get; init; }
    [Legacy("CASTING_C")] public IReadOnlyDictionary<string, long>? CastCost { get; init; }
    [Legacy("BUY_C")] public IReadOnlyDictionary<string, long>? BuyCost { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("UNIT_ID"), Ref("unit"), Description("Призываемый юнит.")] public string? Unit { get; init; }
    [Legacy("RESTORE_M"), Description("Сколько процентов запаса хода возвращает.")] public int? RestoreMovePercent { get; init; }
    [Legacy("AREA"), Description("Сторона квадрата действия на карте.")] public int Area { get; init; }
    [Legacy("MODIF_ID"), Ref("modifier")] public string? Modifier { get; init; }
    [Legacy("MODIF_TXT")] public string? ModifierText { get; init; }
    [Legacy("DAMAGE_SRC")] public AttackSource? DamageSource { get; init; }
    [Legacy("DAMAGE_QTY")] public int? Damage { get; init; }
    [Legacy("HEAL_QTY")] public int? Heal { get; init; }
    [Legacy("AI_CAT"), Description("Кому полезно заклинание — подсказка выбора ИИ (пусто — без подсказки).")]
    public AiSpellUse? AiUse { get; init; }
    [Legacy("GROUND_CAT"), Description("Во что превращает землю.")] public TerrainKind? Terrain { get; init; }
    [Legacy("CHNGTERCAT")] public bool? ChangesTerrain { get; init; }
    [Legacy("QTY_WARDS")] public int? WardCount { get; init; }
    [Legacy("WARD1", "WARD2", "WARD3", "WARD4"), Ref("modifier")] public IReadOnlyList<string> Wards { get; init; } = [];
}

/// <summary>Предмет (<c>GItem.dbf</c>).</summary>
[LegacyTable("GItem", "ITEM_ID")]
public sealed record ItemDef : ContentDef
{
    [Legacy("ITEM_CAT")] public ItemCategory Category { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("IMAGE_ID")] public string? Image { get; init; }
    [Legacy("VALUE")] public IReadOnlyDictionary<string, long>? Value { get; init; }
    [Legacy("MOD_EQUIP"), Ref("modifier"), Description("Действует, пока предмет надет.")] public string? EquipModifier { get; init; }
    [Legacy("MOD_POTION"), Ref("modifier"), Description("Действие зелья.")] public string? PotionModifier { get; init; }
    [Legacy("HP_POTION")] public int? PotionHeal { get; init; }
    [Legacy("SPELL_ID"), Ref("spell"), Description("Заклинание свитка или посоха.")] public string? Spell { get; init; }
    [Legacy("CASTING")] public IReadOnlyDictionary<string, long>? UseCost { get; init; }
    [Legacy("ATTACK_ID"), Ref("attack"), Description("Атака сферы или талисмана.")] public string? Attack { get; init; }
    [Legacy("UNIT_ID"), Ref("unit")] public string? Unit { get; init; }
}
