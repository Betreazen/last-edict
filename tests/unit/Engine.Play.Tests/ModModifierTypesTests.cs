using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Tests;

/// <summary>Типы элементов модификатора, которых в ванили нет (<c>LmodifE</c> 0 <c>L_QTY_TARGET</c> и 14 <c>L_IMMUNITYC</c>), — на
/// модовых таблицах, как их разбирает движок (<see cref="ModifierLoader"/>, читатели <c>0x5a3d13</c>/<c>0x5a4a90</c>/<c>0x5a31eb</c>).</summary>
public class ModModifierTypesTests
{
    private static ModRules Mod(int type, int source, bool classColumns, int? cls = null, int? level = null)
    {
        var mod = new ModRules();
        try
        {
            Fill(mod, type, source, classColumns, cls, level);
            return mod;
        }
        catch
        {
            mod.Dispose();
            throw;
        }
    }

    private static void Fill(ModRules mod, int type, int source, bool classColumns, int? cls, int? level)
    {
        if (classColumns) mod.Column("GmodifL", "IMMUNITYC", length: 2).Column("GmodifL", "IMMUNECATC", length: 2);
        mod.Row("Gmodif", new Dictionary<string, object?> { ["MODIF_ID"] = "g999um0001", ["SOURCE"] = source, ["COMMENTS"] = "mod" })
           .Row("GmodifL", new Dictionary<string, object?>
           {
               ["BELONGS_TO"] = "g999um0001", ["DESC"] = "x000tgc001", ["TYPE"] = type, ["NUMBER"] = 1,
               ["IMMUNITYC"] = cls, ["IMMUNECATC"] = level,
           });
    }

    /// <summary><c>TYPE 0</c> не читает ни один класс: движок отказывает загрузке «Invalid type '0'» (<c>0x5a918e</c>) — порт так же,
    /// на загрузке, а не падением посреди боя или карты.</summary>
    [SkippableFact]
    public void QtyTarget_RefusedAtLoad()
    {
        using var mod = Mod(type: 0, source: 2, classColumns: false);
        var ex = Assert.Throws<InvalidDataException>(() => ModifierLoader.Load(mod.Dir));
        Assert.Contains("Invalid type '0'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("g999um0001", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>TYPE 14</c>: класс и уровень — из колонок <c>IMMUNITYC</c>/<c>IMMUNECATC</c> (<c>0x5a3e61</c>…<c>0x5a3e97</c>), не из
    /// <c>IMMUNITY</c>/<c>IMMUNECAT</c>; юнит с таким модификатором в бою стоек к классу (Poison, <c>Always</c>), к прочим — нет.</summary>
    [SkippableFact]
    public void Immunityclass_ReadsItsOwnColumns_AndProtectsInBattle()
    {
        using var mod = Mod(type: 14, source: 0, classColumns: true, cls: (int)AttackClass.Poison, level: (int)ImmunityLevel.Always);
        var tables = ModifierLoader.Load(mod.Dir);
        var element = Assert.Single(tables.Modifiers["g999um0001"].Elements);
        Assert.Equal(new ModifierElement(ModifierElementType.Immunityclass, Immunity: (int)AttackClass.Poison,
                                         ImmuneCat: (int)ImmunityLevel.Always), element);

        var rules = RulesLoader.Load(mod.Dir);
        var type = rules.Units["g000uu0001"];
        var unit = new BattleUnit
        {
            Id = "u", Type = type, Attack = rules.AttackOf(type)!, Attacker = false, Slot = 0, HpMax = type.HitPoints, Hp = type.HitPoints,
            Modifiers = [rules.Modifiers!["g999um0001"]],
        };
        Assert.Equal(ImmunityLevel.Always, Immunity.LevelsFor(unit, source: 0, AttackClass.Poison).ByClass);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelsFor(unit, source: 0, AttackClass.Damage).ByClass);
    }

    /// <summary><c>TYPE 14</c> без своих колонок — порча данных мода, отказ на загрузке.</summary>
    [SkippableFact]
    public void Immunityclass_WithoutColumns_RefusedAtLoad()
    {
        using var mod = Mod(type: 14, source: 0, classColumns: false);
        var ex = Assert.Throws<InvalidDataException>(() => ModifierLoader.Load(mod.Dir));
        Assert.Contains("IMMUNITYC", ex.Message, StringComparison.Ordinal);
    }
}
