using System.ComponentModel;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Content.Schema;

/// <summary>Тип юнита (<c>Gunits.dbf</c>): солдат, лидер, вор, призванный, страж. Иммунитеты (<c>Gimmu</c>, <c>GimmuC</c>), врождённые
/// способности лидера (<c>GLabi</c>) и земли без штрафа хода (<c>GMabi</c>) — в нём же, а не отдельными таблицами.</summary>
[LegacyTable("Gunits", "UNIT_ID")]
public sealed record UnitDef : ContentDef
{
    [Legacy("UNIT_CAT")] public UnitRole Role { get; init; }
    [Legacy("LEVEL")] public int Level { get; init; }

    [Legacy("PREV_ID"), Ref("unit"), Description("Тип уровнем ниже: в него превращает цель атака «понижение уровня».")]
    public string? PreviousLevel { get; init; }

    [Legacy("RACE_ID"), Ref("race")] public string? Race { get; init; }
    [Legacy("SUBRACE")] public Subrace Subrace { get; init; }
    [Legacy("BRANCH")] public BranchKind Branch { get; init; }

    [Legacy("SIZE_SMALL"), Description("Занимает одну клетку отряда (ложь — две).")]
    public bool? Small { get; init; }

    [Legacy("SEX_M")] public bool? Male { get; init; }

    [Legacy("ENROLL_C")] public IReadOnlyDictionary<string, long>? HireCost { get; init; }

    [Legacy("ENROLL_B"), Ref("building"), Description("Постройка, без которой юнита не нанять.")]
    public string? HireBuilding { get; init; }

    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("ABIL_TXT")] public string? AbilityText { get; init; }

    [Legacy("ATTACK_ID"), Ref("attack")] public string? Attack { get; init; }
    [Legacy("ATTACK2_ID"), Ref("attack")] public string? SecondAttack { get; init; }
    [Legacy("ATCK_TWICE")] public bool? AttacksTwice { get; init; }

    [Legacy("HIT_POINT")] public int HitPoints { get; init; }

    [Legacy("BASE_UNIT"), Ref("unit")] public string? BaseUnit { get; init; }

    [Legacy("ARMOR")] public int Armor { get; init; }
    [Legacy("REGEN")] public int Regeneration { get; init; }

    [Legacy("REVIVE_C")] public IReadOnlyDictionary<string, long>? ReviveCost { get; init; }
    [Legacy("HEAL_C"), Description("Цена лечения за единицу здоровья.")] public IReadOnlyDictionary<string, long>? HealCost { get; init; }
    [Legacy("TRAINING_C"), Description("Цена обучения за единицу опыта.")] public IReadOnlyDictionary<string, long>? TrainingCost { get; init; }

    [Legacy("XP_KILLED"), Description("Опыт победителю за гибель юнита.")] public int XpForKill { get; init; }

    [Legacy("UPGRADE_B"), Ref("building"), Description("Постройка, открывающая этот тип как улучшение.")]
    public string? UpgradeBuilding { get; init; }

    [Legacy("XP_NEXT"), Description("Опыт до следующего уровня.")] public int XpForNextLevel { get; init; }

    [Legacy("MOVE"), Description("Запас хода лидера за день.")] public int? Move { get; init; }
    [Legacy("SCOUT"), Description("Обзор лидера в клетках.")] public int? Scout { get; init; }
    [Legacy("LIFE_TIME"), Description("Сколько дней живёт призванный (пусто — без срока).")] public int? LifetimeDays { get; init; }
    [Legacy("LEADERSHIP")] public int? Leadership { get; init; }
    [Legacy("NEGOTIATE"), Description("Сопротивление действиям вора.")] public int? Negotiate { get; init; }
    [Legacy("LEADER_CAT")] public LeaderCategory? LeaderCategory { get; init; }

    [LegacyGroup, Description("Рост характеристик с уровнем: шаги «ранний» до уровня смены, дальше «поздний».")]
    public UnitGrowthRef Growth { get; init; } = new();

    [Legacy("WATER_ONLY")] public bool? WaterOnly { get; init; }

    // Признаки, которые движок оригинала держит списками id в коде (LastEdict.Rules.Units.LegacyUnits): импорт выписывает их явно.
    [Description("Маг-лидер: точность площадной атаки падает на 10 с каждой следующей целью. Нет — по id оригинала (список движка).")]
    public bool? MageLeader { get; init; }

    [Description("Прибавка к потолку урона (LeaderBonusMaxDamage). Нет — по id оригинала (список движка).")]
    public bool? DamageBonus { get; init; }

    [Description("Двойник может скопировать юнит. Нет — по id оригинала (список движка; у прочих — да).")]
    public bool? Copyable { get; init; }
    [Legacy("DEATH_ANIM")] public DeathAnimation? DeathAnimation { get; init; }

    [Description("Иммунитеты к источникам атак (Gimmu).")]
    public IReadOnlyList<SourceImmunity> Immunities { get; init; } = [];

    [Description("Иммунитеты к классам атак (GimmuC).")]
    public IReadOnlyList<ClassImmunity> ClassImmunities { get; init; } = [];

    [Description("Врождённые способности лидера (GLabi).")]
    public IReadOnlyList<LeaderAbility> LeaderAbilities { get; init; } = [];

    [Description("Земли, по которым юнит ходит без штрафа (GMabi).")]
    public IReadOnlyList<MoveAbility> MoveAbilities { get; init; } = [];
}

/// <summary>Рост юнита с уровнем (<c>DYN_UPG1</c>, <c>DYN_UPG_LV</c>, <c>DYN_UPG2</c>).</summary>
public sealed record UnitGrowthRef
{
    [Legacy("DYN_UPG1"), Ref("growth")] public string? Early { get; init; }
    [Legacy("DYN_UPG_LV"), Description("Уровень, с которого действуют поздние шаги.")] public int SwitchLevel { get; init; }
    [Legacy("DYN_UPG2"), Ref("growth")] public string? Late { get; init; }
}

/// <summary>Иммунитет к источнику атаки (строка <c>Gimmu</c>).</summary>
public sealed record SourceImmunity(AttackSource Source, ImmunityLevel Level);

/// <summary>Иммунитет к классу атаки (строка <c>GimmuC</c>).</summary>
public sealed record ClassImmunity(AttackClass AttackClass, ImmunityLevel Level);

/// <summary>Шаги роста за уровень (<c>GDynUpgr.dbf</c>): на столько растут характеристики юнита за каждый уровень сверх базового.</summary>
[LegacyTable("GDynUpgr", "UPGRADE_ID")]
public sealed record GrowthDef : ContentDef
{
    [Legacy("ENROLL_C")] public IReadOnlyDictionary<string, long>? HireCost { get; init; }
    [Legacy("HIT_POINT")] public int HitPoints { get; init; }
    [Legacy("ARMOR")] public int? Armor { get; init; }
    [Legacy("REGEN")] public int? Regeneration { get; init; }
    [Legacy("REVIVE_C")] public IReadOnlyDictionary<string, long>? ReviveCost { get; init; }
    [Legacy("HEAL_C")] public IReadOnlyDictionary<string, long>? HealCost { get; init; }
    [Legacy("TRAINING_C")] public IReadOnlyDictionary<string, long>? TrainingCost { get; init; }
    [Legacy("XP_KILLED")] public int XpForKill { get; init; }
    [Legacy("XP_NEXT")] public int XpForNextLevel { get; init; }
    [Legacy("MOVE")] public int? Move { get; init; }
    [Legacy("NEGOTIATE")] public int Negotiate { get; init; }
    [Legacy("DAMAGE")] public int? Damage { get; init; }
    [Legacy("HEAL")] public int? Heal { get; init; }
    [Legacy("INITIATIVE")] public int? Initiative { get; init; }
    [Legacy("POWER")] public int? Power { get; init; }
}
