using D2.Rules.Map;

namespace D2.Data.Ui.Windows;

/// <summary>Навыки героя строками окна отряда и карточки воина: способность лидера (<c>LLeadA</c>) → значок <c>Icons.ff</c> и строка
/// (<c>0x5b25e0</c>, <c>0x61dd44</c>). Критический удар (17) значка и строки не имеет — не показывается.</summary>
public static class HeroSkills
{
    private static readonly (int Ability, string Icon, string Text)[] Abilities =
    [
        (7, "ABIL0001", "X005TA0181"), (8, "ABIL0002", "X005TA0182"), (9, "ABIL0003", "X005TA0183"), (13, "ABIL0004", "X005TA0359"),
        (10, "ABIL0006", "X005TA0184"), (3, "ABIL0007", "X005TA0175"), (6, "ABIL0008", "X005TA0180"), (14, "ABIL0009", "X005TA0176"), (15, "ABIL0010", "X005TA0177"),
        (16, "ABIL0011", "X005TA0178"),
    ];

    /// <summary>Карточка навыка по строке его имени (<see cref="LeaderAbilityCardLogic"/>).</summary>
    public static UiOpen Card(WindowContext c, string nameTxt) => new(LeaderAbilityCardLogic.Screen, new LeaderAbilityCardLogic(c, nameTxt));

    /// <summary>Описание навыка для карточки <c>DLG_R_C_LEADERABIL</c> (<c>0x61df32</c>) по строке его имени; нет — null.</summary>
    public static string? Describe(string nameTxt) => Descs.GetValueOrDefault(nameTxt);

    private static readonly Dictionary<string, string> Descs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["X005TA0181"] = "X005TA0900", ["X005TA0182"] = "X005TA0901", ["X005TA0183"] = "X005TA0902", ["X005TA0184"] = "X005TA0903",
        ["X005TA0359"] = "X005TA0904", ["X005TA0175"] = "X005TA0905", ["X005TA0180"] = "X005TA0906", ["X005TA0176"] = "X005TA0907",
        ["X005TA0177"] = "X005TA0908", ["X005TA0178"] = "X005TA0909", ["X005TA0256"] = "X005TA0910", ["X160TA0017"] = "X160TA0018",
    };

    /// <summary>Строки навыков (<c>0x497873</c>, <c>0x61e1a0</c>): способности, какие у героя есть, по порядку таблицы; ходит по
    /// равнине, лесу и воде — ещё «Полёт» (<c>ABIL0005</c>, <c>X005TA0256</c>, <c>0x61e2fb</c>). «Благородство» — неподкупность (<c>L_INCORRUPTIBLE</c>,
    /// 3).</summary>
    public static IReadOnlyList<(string Icon, string Text)> Rows(WindowContext c, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(stack);
        var move = c.Session.MoveOptionsOf(stack);
        return Rows(c.Session.LeaderAbilitiesOf(stack), move is { PlainsBonus: true, ForestBonus: true, WaterBonus: true });
    }

    /// <summary>Те же строки по списку способностей и признаку «ходит по равнине, лесу и воде» — для героя из файла (окно
    /// просмотра импорта, <c>0x4cb1ec</c>).</summary>
    public static IReadOnlyList<(string Icon, string Text)> Rows(IEnumerable<int> abilities, bool flies)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        var has = abilities.ToHashSet();
        var rows = Abilities.Where(a => has.Contains(a.Ability)).Select(a => (a.Icon, a.Text)).ToList();
        if (flies) rows.Add(("ABIL0005", "X005TA0256"));
        return rows;
    }
}
