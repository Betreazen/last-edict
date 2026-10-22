using System.ComponentModel;
using D2.Rules.Battle;
using D2.Rules.Units;

namespace D2.Data.Content.Schema;

/// <summary>Атака (<c>Gattacks.dbf</c>). Юниты, в которых превращает или которых призывает атака (<c>Gtransf</c>), — в ней же.</summary>
[LegacyTable("Gattacks", "ATT_ID")]
public sealed record AttackDef : ContentDef
{
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("INITIATIVE")] public int Initiative { get; init; }
    [Legacy("SOURCE")] public AttackSource Source { get; init; }

    [Ref("effect"), Description("Что делает атака — эффект (base:effect/damage, base:effect/heal…). В таблице оригинала — класс CLASS.")]
    public string? Effect { get; init; }

    [Legacy("POWER"), Description("Точность в процентах.")] public int Power { get; init; }
    [Legacy("REACH")] public ReachKind Reach { get; init; }
    [Legacy("QTY_HEAL")] public int? Heal { get; init; }
    [Legacy("QTY_DAM")] public int? Damage { get; init; }

    [Legacy("LEVEL"), Description("Уровень эффекта (усиления, ослабления).")]
    public int? Level { get; init; }

    [Legacy("ALT_ATTACK"), Ref("attack"), Description("Запасная атака: ею бьёт двойник и превращённый в себя.")]
    public string? AltAttack { get; init; }

    [Legacy("INFINITE"), Description("Эффект действует до конца боя.")] public bool? Infinite { get; init; }

    [Legacy("QTY_WARDS")] public int? WardCount { get; init; }

    [Legacy("WARD1", "WARD2", "WARD3", "WARD4"), Ref("modifier"), Description("Обереги, которые атака даёт цели.")]
    public IReadOnlyList<string> Wards { get; init; } = [];

    [Legacy("CRIT_HIT")] public bool? CriticalHit { get; init; }

    [Ref("unit"), Description("В кого превращает или кого призывает атака (Gtransf), по порядку таблицы.")]
    public IReadOnlyList<string> Units { get; init; } = [];
}

/// <summary>Модификатор (<c>Gmodif.dbf</c>) — набор элементов (<c>GmodifL.dbf</c>): обереги, усиления, навыки лидера, свойства предметов.</summary>
[LegacyTable("Gmodif", "MODIF_ID")]
public sealed record ModifierDef : ContentDef
{
    [Legacy("SOURCE")] public ModifierScope Scope { get; init; }
    [Legacy("COMMENTS")] public string? Comment { get; init; }
    public IReadOnlyList<ModifierElementDef> Elements { get; init; } = [];
}

/// <summary>Элемент модификатора (строка <c>GmodifL</c>). Числа — как в таблице: пусто — «не задано», а не ноль. Смысл полей задаёт
/// <see cref="Type"/> (иммунитет к источнику — <see cref="Immunity"/>/<see cref="ImmuneLevel"/>, к классу — <see cref="ImmunityClass"/>/
/// <see cref="ImmunityClassLevel"/>, у модов оригинала — свои столбцы <c>IMMUNITYC</c>/<c>IMMUNECATC</c>).</summary>
public sealed record ModifierElementDef
{
    [Legacy("TYPE")] public ModifierElementType Type { get; init; }
    [Legacy("DESC")] public string? Description { get; init; }
    [Legacy("PERCENT")] public int? Percent { get; init; }
    [Legacy("NUMBER")] public int? Number { get; init; }
    [Legacy("ABILITY")] public int? Ability { get; init; }
    [Legacy("IMMUNITY")] public int? Immunity { get; init; }
    [Legacy("IMMUNECAT")] public int? ImmuneLevel { get; init; }
    [Legacy("MOVE")] public int? Move { get; init; }
    [Legacy("IMMUNITYC", Optional = true)] public int? ImmunityClass { get; init; }
    [Legacy("IMMUNECATC", Optional = true)] public int? ImmunityClassLevel { get; init; }
}
