using System.Globalization;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Ячейки группы юнитов в окнах (<c>CDDUnitGroup</c>, отрисовка <c>0x56e02d</c>): малый портрет в ячейке, большой — в колонке
/// (<see cref="UnitGroupLayout"/>). Раненый — нижняя доля портрета по потерянному здоровью смешана с красным наполовину
/// (<c>0x5b1af4</c>), павший — маска <c>MASKDEADS</c>/<c>MASKDEADL</c> поверх всего портрета (<c>0x5b1698</c>). Под ячейкой —
/// <c>X005TA0757</c> «здоровье/максимум», у большого — на подложке <c>SPLITLRG</c>. Значки (<c>0x56e2e7</c>, кадры
/// <c>Battle.ff</c>): готов к повышению, но постройка наследника не построена (<c>FIUPGRADE</c>, <c>0x61e6d9</c>); высокий
/// уровень (<c>FIHIGHLEVEL1..3</c>: уровень выше базового на 5, 10, 15, <c>0x61ea4c</c>); усиление, зелье, ослабление
/// (<c>FIISOSPELLBOOST</c>, <c>FIPOTIONBOOST</c>, <c>FIISOSPELLLOWER</c>, <c>0x5ea500</c>): в цепочке юнита есть модификатор
/// заклинания категории «усиление»/«ослабление» или зелья-усиления (<c>L_POTION_BOOST</c>; постоянные зелья — нет), откуда бы он
/// ни взялся. У павшего значков нет. Раскладка зеркальная — <see cref="UnitGroupLayout.Expand"/>.
/// Отражение портретов — флаг адаптера группы (<c>CMidUnitGroupAdapter</c> <c>0x56f577</c>, 4-й аргумент; 0 — отражены, <c>0x5b13fb</c>):
/// отражены только гарнизон окна армии и правый отряд окна обмена, у площадок, окна отряда и мешка — нет (21 вызов, ранг 1). Маска
/// павшего и красная доля раненого отражаются вместе с лицом (общий блиттер <c>0x5b1af4</c>); надпись, подложка и значки — нет.
/// </summary>
public static class UnitCellsView
{
    private const string Archive = "Battle";
    private static readonly UiColor Wounded = new(255, 0, 0);

    /// <summary>Клетки группы строкой (для проверок): <c>x</c> — воин, <c>b</c> — большой, <c>0</c> — павший, <c>.</c> — пусто;
    /// группы нет — <c>------</c>.</summary>
    public static string Line(WindowContext c, string groupId)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (groupId.Length == 0 || c.Session.GroupCells(groupId) is not { } cells) return "------";
        return new string([.. cells.Cells.Select((id, p) => id.Length == 0 ? '.'
            : c.Map.Units.TryGetValue(id, out var u) && u.Hp <= 0 ? '0'
            : string.Equals(cells.Cells[Formation.Pair(p)], id, StringComparison.OrdinalIgnoreCase) ? 'b' : 'x')]);
    }

    public static void Render(UiScreenState ui, string group, UnitCells? cells, WindowContext c, string owner, bool mirrored = false)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(c);
        for (int p = 0; p < UnitGroupLayout.Positions; p++) Clear(ui, group, p.ToString(CultureInfo.InvariantCulture),
                                                                    UnitGroupLayout.CellImage(group, p), UnitGroupLayout.CellText(group, p));
        for (int r = 0; r < UnitGroupLayout.Positions / 2; r++)
        {
            Clear(ui, group, $"B{r}", UnitGroupLayout.BigImage(group, r), UnitGroupLayout.BigText(group, r));
            ui.SetImage(UnitGroupLayout.BigSplit(group, r), null);
        }
        if (cells is null) return;
        foreach (string id in cells.Units)
        {
            int p = cells.PositionOf(id);
            if (p < 0 || !c.Map.Units.TryGetValue(id, out var unit)) continue;
            bool big = string.Equals(cells.Cells[Formation.Pair(p)], id, StringComparison.OrdinalIgnoreCase);
            string key = big ? $"B{p / 2}" : p.ToString(CultureInfo.InvariantCulture);
            int max = c.Session.HpMax(unit);
            string portrait = Portrait(c, unit, max, big);
            ui.SetImage(big ? UnitGroupLayout.BigImage(group, p / 2) : UnitGroupLayout.CellImage(group, p),
                        mirrored ? UiFrameRef.Mirrored(portrait) : portrait);
            ui.SetText(big ? UnitGroupLayout.BigText(group, p / 2) : UnitGroupLayout.CellText(group, p),
                       UiTemplate.Fill(c.Text("X005TA0757"), ("HP", Math.Max(0, unit.Hp)), ("HPMAX", max)));
            if (big) ui.SetImage(UnitGroupLayout.BigSplit(group, p / 2), "SPLITLRG");
            if (unit.Hp <= 0) continue;
            ui.SetImage(UnitGroupLayout.Icon(group, key, UnitGroupLayout.UpgradeIcon), ReadyToUpgrade(c, unit, owner) ? $"{Archive}:FIUPGRADE" : null);
            int high = HighLevel(c, unit);
            ui.SetImage(UnitGroupLayout.Icon(group, key, UnitGroupLayout.HighLevelIcon), high > 0 ? $"{Archive}:FIHIGHLEVEL{high}" : null);
            var marks = D2.Data.Gfx.BattleMarkSets.Of(c.Session.RequireBook());
            ui.SetImage(UnitGroupLayout.Icon(group, key, UnitGroupLayout.PotionIcon), unit.Modifiers.Any(marks.Potion.Contains) ? $"{Archive}:FIPOTIONBOOST" : null);
            ui.SetImage(UnitGroupLayout.Icon(group, key, UnitGroupLayout.BoostIcon), unit.Modifiers.Any(marks.SpellBoost.Contains) ? $"{Archive}:FIISOSPELLBOOST" : null);
            ui.SetImage(UnitGroupLayout.Icon(group, key, UnitGroupLayout.LowerIcon), unit.Modifiers.Any(marks.SpellLower.Contains) ? $"{Archive}:FIISOSPELLLOWER" : null);
        }
    }

    /// <summary>
    /// Рамки допустимости группы при перетаскивании (<see cref="DropFrames"/>, <c>0x56e02d</c> → <c>0x56e487</c>): у колонки большого юнита —
    /// одна рамка по состоянию его передней позиции, у прочих позиций (и пустых) — своя. <paramref name="state"/> null — ничего не тащат,
    /// группы нет (<paramref name="cells"/> null) — рамок нет.
    /// </summary>
    public static void Frames(UiScreenState ui, string group, UnitCells? cells, Func<int, DropFrame>? state)
    {
        ArgumentNullException.ThrowIfNull(ui);
        for (int row = 0; row < UnitGroupLayout.Positions / 2; row++)
        {
            int front = row * 2;
            bool big = cells is not null && cells.Cells[front].Length > 0
                       && string.Equals(cells.Cells[front + 1], cells.Cells[front], StringComparison.OrdinalIgnoreCase);
            Frame(ui, UnitGroupLayout.BigDropImage(group, row), big && state is not null ? DropFrames.Image(state(front), large: true) : null);
            for (int p = front; p < front + 2; p++)
                Frame(ui, UnitGroupLayout.DropImage(group, p), !big && cells is not null && state is not null ? DropFrames.Image(state(p), large: false) : null);
        }
    }

    private static void Frame(UiScreenState ui, string id, string? frame)
    {
        if (frame is null) ui.SetImage(id, null);
        else ui.SetImage(id, frame, UiImageAlign.Center);
    }

    /// <summary>Кадр портрета: павший — с маской, раненый — с красной нижней долей.</summary>
    public static string Portrait(WindowContext c, MapUnit unit, int max, bool big)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(unit);
        string face = c.Faces.Small(unit.TypeId);
        if (unit.Hp <= 0) return UiFrameRef.Masked(face, big ? "MASKDEADL" : "MASKDEADS");
        if (max <= 0 || unit.Hp >= max) return face;
        return UiFrameRef.Tinted(face, Wounded, unit.Hp * 100 / max);
    }

    /// <summary>Номер значка высокого уровня: разница с уровнем базового типа (+1, если базовый — первый) — 5, 10, 15
    /// (таблица <c>0x6e8948</c>); меньше 5 — 0.</summary>
    public static int HighLevel(WindowContext c, MapUnit unit)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(unit);
        if (!c.Session.RequireBook().Rules.Units.TryGetValue(unit.TypeId, out var type)) return 0;
        int d = unit.Level - type.Level + (type.Level == 1 ? 1 : 0);
        return d >= 15 ? 3 : d >= 10 ? 2 : d >= 5 ? 1 : 0;
    }

    /// <summary>Значки повышения и высокого уровня у портрета в панели боя (<c>CBatUnitGroup2</c> <c>0x64c000</c>: <c>FIUPGRADE</c> — <c>0x61e6d9</c>
    /// у живого (флаг <c>+0x15</c> — статус Dead, <c>0x64bb4c</c>), <c>FIHIGHLEVEL1..3</c> — <c>0x61ea4c</c>, пороги 5/10/15 <c>0x6e8948</c>) — по
    /// юниту карты, как в окнах. Юнита на карте нет (призванный) — значков нет.</summary>
    public static (bool Upgrade, int HighLevel) BattleLevelMarks(WindowContext c, string unitId)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!c.Map.Units.TryGetValue(unitId, out var unit)) return (false, 0);
        string owner = c.Map.Stacks.Values.FirstOrDefault(s => s.UnitIds.Contains(unitId, StringComparer.OrdinalIgnoreCase))?.Owner
                       ?? c.Map.Sites.FirstOrDefault(f => f.Guards.Contains(unitId, StringComparer.OrdinalIgnoreCase))?.Owner ?? "";
        return (owner.Length > 0 && ReadyToUpgrade(c, unit, owner), HighLevel(c, unit));
    }

    /// <summary>Готов к повышению, а постройки наследника нет: опыт ≥ предел − 1, наследники по <c>PREV_ID</c> есть, ни у одного
    /// постройка <c>UPGRADE_B</c> не построена владельцем. ponytail: смысл флага <c>0x61e6d9</c> — по разбору, ранг 2.</summary>
    public static bool ReadyToUpgrade(WindowContext c, MapUnit unit, string owner)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(unit);
        var book = c.Session.RequireBook();
        int next = book.XpNextOf(unit.TypeId, unit.Level);
        if (next <= 0 || unit.Xp < next - 1) return false;
        var successors = UnitUpgrade.Successors(book.Rules.Units, unit.TypeId, unit.Level);
        if (successors.Count == 0 || !c.Map.Players.TryGetValue(owner, out var player)) return false;
        var built = player.Buildings.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return !successors.Any(s => book.UpgradeBuilding.GetValueOrDefault(s.Id, "") is var b && (b.Length == 0 || built.Contains(b)));
    }

    private static void Clear(UiScreenState ui, string group, string key, string image, string text)
    {
        ui.SetImage(image, null);
        ui.SetText(text, "");
        foreach (string kind in UnitGroupLayout.IconKinds) ui.SetImage(UnitGroupLayout.Icon(group, key, kind), null);
    }
}
