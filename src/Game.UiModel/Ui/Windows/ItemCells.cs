using LastEdict.Data.Menu;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Ячейка списка предметов: тип (<c>GItem</c>) и все предметы этого типа по порядку появления.</summary>
public sealed record ItemCell(string Type, IReadOnlyList<string> Ids)
{
    /// <summary>Какой предмет тянется из ячейки — последний (<c>0x61fd61</c>).</summary>
    public string Top => Ids[^1];
}

/// <summary>
/// Одинаковые предметы — одна ячейка со счётчиком (<c>CInventoryCounter</c> <c>0x61faca</c>, <c>CDDFoldedInv</c>): ключ — тип предмета,
/// ячейки идут по первому появлению типа, новый предмет — в конец своей ячейки. Число — <c>X005TA0323</c> и два пробела, белым
/// мелким в правом нижнем углу ячейки с отступом в точку (<c>0x56d06f</c>); рисуется и «1» (код движка, ответ пользователя 26.09).
/// Так списки сумки и склада в окне армии, сумки в окне отряда, мешка и сумки героя у торговца.
/// </summary>
public static class ItemCells
{
    private const string ItemArchive = "IconItem";

    public static IReadOnlyList<ItemCell> Fold(IEnumerable<string> items, MapState map)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(map);
        var cells = new List<(string Type, List<string> Ids)>();
        foreach (string item in items)
        {
            string type = map.ItemTypes.TryGetValue(item, out var t) ? t : item;
            int at = cells.FindIndex(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
            if (at < 0) cells.Add((type, [item]));
            else cells[at].Ids.Add(item);
        }
        return [.. cells.Select(c => new ItemCell(c.Type, c.Ids))];
    }

    /// <summary>Карточка ячейки: значок и счётчик в размер ячейки списка <paramref name="list"/>.</summary>
    public static UiCard Card(ItemCell cell, WindowContext c, UiList list)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(list);
        return new UiCard(IconFrame(cell.Type, c), new UiPoint(7, 0),
                          UiTemplate.Fill(c.Text("X005TA0323"), ("NUM", cell.Ids.Count)) + "  ",
                          new UiRect(1, 1, Math.Max(0, list.CellWidth - 2), Math.Max(0, list.CellHeight - 2)));
    }

    /// <summary>Кадр значка предмета (по типу экземпляра) — для курсора тащения (<see cref="IWindowLogic.DragImage"/>).</summary>
    public static string Icon(string itemId, WindowContext c)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(c);
        return IconFrame(c.Map.ItemTypes.TryGetValue(itemId, out var t) ? t : itemId, c);
    }

    /// <summary>Кадр значка типа предмета (<see cref="Play.GameBook.ItemIcon"/>: у свитка — значок расы его заклинания, своих значков у свитков
    /// нет — без этого ячейка свитка показывала одно число, «призрак» прогона 3 пользователя 11.10, снимок 39).</summary>
    public static string IconFrame(string itemType, WindowContext c)
    {
        ArgumentNullException.ThrowIfNull(itemType);
        ArgumentNullException.ThrowIfNull(c);
        return $"{ItemArchive}:{c.Session.Book?.ItemIcon(itemType) ?? itemType.ToUpperInvariant()}";
    }

    /// <summary>Портрет воина-экземпляра — для курсора тащения; воина нет — null.</summary>
    public static string? Face(string? unitId, WindowContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return unitId is { Length: > 0 } && c.Map.Units.TryGetValue(unitId, out var unit) ? c.Faces.Small(unit.TypeId) : null;
    }

    /// <summary>Разметка списка по id элемента (ячейка и отступы); нет — пустая.</summary>
    public static UiList Spec(UiScreenState ui, string id) =>
        ui?.Screen.Elements.FirstOrDefault(e => e.Id == id)?.List ?? new UiList();
}
