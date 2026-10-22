using D2.Data.Content;
using D2.Data.Content.Schema;
using D2.Rules.Units;

namespace D2.Data.Battle;

/// <summary>Лорд — класс игрока (<see cref="LordDef"/>): раса, категория (0 маг, 1 воин, 2 гильдмастер), тексты.</summary>
public sealed record Lord(string Id, string RaceId, int Category, string NameTxt, string DescTxt);

/// <summary>Модификаторы, навыки лидера по уровню и лорды одним куском.</summary>
public sealed record ModifierTables(IReadOnlyDictionary<string, Modifier> Modifiers,
                                    IReadOnlyList<LeaderUpgrade> LeaderUpgrades,
                                    IReadOnlyDictionary<string, Lord> Lords);

/// <summary>Содержимое (модификаторы с элементами, навыки лидера, лорды) → <see cref="ModifierTables"/>. Ключи регистронезависимы.
/// <para>Записи <see cref="Modifier"/> и <see cref="ModifierElement"/> живут в <c>D2.Rules.Units</c>. Строки элементов без хозяина в
/// таблицах оригинала (7 строк <c>GmodifL</c>) содержимое не несёт — правила их и так не читали; факт о таблицах проверяет
/// <c>F6DataTests</c> по самим таблицам.</para></summary>
public static class ModifierLoader
{
    public static ModifierTables Load(string globalsDir) => Load(ContentSource.Open(globalsDir));

    public static ModifierTables Load(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var modifiers = new Dictionary<string, Modifier>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in content.Modifiers)
            modifiers[m.Key] = new Modifier(m.Key, [.. m.Elements.Select(e => Element(m, e))]);
        return new ModifierTables(modifiers, LoadLeaderUpgrades(content), LoadLords(content));
    }

    public static IReadOnlyList<LeaderUpgrade> LoadLeaderUpgrades(string globalsDir) => LoadLeaderUpgrades(ContentSource.Open(globalsDir));

    /// <summary>Навыки лидера по уровню в порядке таблицы — он же порядок равенства при выборе ИИ.</summary>
    public static IReadOnlyList<LeaderUpgrade> LoadLeaderUpgrades(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return [.. content.LeaderSkills.Select(s => new LeaderUpgrade(content.KeyOf(s.Lord), content.KeyOf(s.Modifier), s.MinLevel, s.Priority))];
    }

    public static IReadOnlyDictionary<string, Lord> LoadLords(string globalsDir) => LoadLords(ContentSource.Open(globalsDir));

    public static IReadOnlyDictionary<string, Lord> LoadLords(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Lords.ToDictionary(l => l.Key, l => new Lord(l.Key, content.KeyOf(l.Race), (int)l.Category, l.NameText ?? "",
                                                                    l.DescriptionText ?? ""), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Элемент модификатора. <b>Как у движка (16.10, ранг 1 по листингу).</b> Элементы читают три читателя по классу
    /// модификатора: юнита (<c>0x5a3d13</c>: Hp, Regeneration, Armor, Immunity, Immunityclass), атаки (<c>0x5a4a90</c>: Power, QtyDamage,
    /// Initiative, AttackDrain) и отряда (<c>0x5a31eb</c>…: ход, обзор, лидерство, способности, бегство, скидка). <c>L_QTY_TARGET</c> не
    /// читает ни один (константа <c>[0x838468]</c> в коде не встречается): движок отвечает «DB: Error loading modifier. Invalid type '%d'
    /// in table '%s'» (<c>0x5a918e</c>), то есть мод с ним не загружается — так же здесь. Иммунитет к классу (<c>Immunityclass</c>)
    /// движок читает из своих столбцов <c>IMMUNITYC</c> (класс атаки) и <c>IMMUNECATC</c> (уровень) — <c>0x5a3e61</c>…<c>0x5a3e97</c>, а не
    /// из столбцов источника; в ванильной таблице их нет, их добавляют моды. В записи правил класс ложится в те же поля
    /// <see cref="ModifierElement.Immunity"/> и <see cref="ModifierElement.ImmuneCat"/> — смысл задаёт тип.
    /// <para>Пустое число — «не задано», а не ноль (<c>ModifierValue.initialized</c>).</para></summary>
    private static ModifierElement Element(ModifierDef owner, ModifierElementDef e)
    {
        if (e.Type == ModifierElementType.QtyTarget)
            throw new InvalidDataException(
                $"модификатор «{owner.Id}»: элемент типа qtyTarget (L_QTY_TARGET) — его не читает ни один класс модификатора, движок " +
                "отказывает «DB: Error loading modifier. Invalid type '0' in table 'GmodifL'» (0x5a918e)");
        if (e.Type == ModifierElementType.Immunityclass)
        {
            if (e.ImmunityClass is not { } cls || e.ImmunityClassLevel is not { } level)
                throw new InvalidDataException(
                    $"модификатор «{owner.Id}»: элемент immunityclass без immunityClass/immunityClassLevel (IMMUNITYC/IMMUNECATC) — класс и " +
                    "уровень движок читает только из них (0x5a3e61)");
            return new ModifierElement(ModifierElementType.Immunityclass, Immunity: cls, ImmuneCat: level);
        }
        return new ModifierElement(e.Type, e.Percent, e.Number, e.Ability, e.Immunity, e.ImmuneLevel, e.Move);
    }
}
