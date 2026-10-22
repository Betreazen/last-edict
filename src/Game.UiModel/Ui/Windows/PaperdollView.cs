using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Кукла героя <c>DLG_PAPERDOLL</c> (<c>CPaperdollChildInterf</c> <c>0x4cbcea</c>), встроенная в чужое окно в точке элемента-рамки
/// (<c>IMG_PAPERDOLL_BG</c>): окно армии (кукла гостя, <c>0x4b268c</c>) и торговец (кукла героя, <c>0x49a18a</c>). Слоты
/// <see cref="ManageStackLogic.SlotImages"/> вставляются в форму окна сразу за рамкой и видны, пока кукла включена.
/// </summary>
public sealed class PaperdollView
{
    public const string Screen = "DLG_PAPERDOLL";
    private const string ItemArchive = "IconItem";

    private readonly Dictionary<int, UiRect> _slots = [];

    /// <summary>Размер фона куклы и фона места группы (<c>DLG_PAPERDOLL_PAPERDOLL_BG</c>, <c>DLG_*_GROUP_BG</c>).</summary>
    public const int Width = 350, Height = 386;

    /// <summary>Фон куклы (кадр <c>DLG_PAPERDOLL</c>); куклы в экранах нет — null.</summary>
    public string? Background { get; private set; }

    /// <summary>Рамка <paramref name="frame"/> — в размер фона (в разметке место 32×32 в углу, картинка ужалась бы по ширине места) и
    /// первой среди элементов: фон места группы (<c>DLG_*_GROUP_BG</c>, пока кукла выключена) лежит под портретом героя и ячейками,
    /// фон куклы — под её слотами (их <see cref="Insert"/> ставит сразу за рамкой). Рамки нет — ничего.</summary>
    public static void Underlay(List<UiElement> elements, string frame)
    {
        ArgumentNullException.ThrowIfNull(elements);
        int at = elements.FindIndex(e => e.Id == frame);
        if (at < 0) return;
        var bg = elements[at] with { Rect = elements[at].Rect with { Width = Width, Height = Height } };
        elements.RemoveAt(at);
        elements.Insert(0, bg);
    }

    /// <summary>Вставить слоты куклы за элементом <paramref name="frame"/>; нет экрана куклы — ничего.</summary>
    public void Insert(List<UiElement> elements, IReadOnlyList<UiScreen>? screens, string frame)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var doll = screens?.FirstOrDefault(s => s.Id == Screen);
        int at = elements.FindIndex(e => e.Id == frame);
        if (doll is null || at < 0) return;
        var origin = elements[at].Rect;
        Background = doll.Background;
        var slots = doll.Elements.Select(e => e with { Rect = e.Rect with { X = e.Rect.X + origin.X, Y = e.Rect.Y + origin.Y } }).ToList();
        for (int s = 0; s < ManageStackLogic.SlotImages.Length; s++)
            if (slots.FirstOrDefault(e => e.Id == ManageStackLogic.SlotImages[s]) is { } slot) _slots[s] = slot.Rect;
        elements.InsertRange(at + 1, slots);
    }

    /// <summary>Слоты: видны при <paramref name="shown"/>, в них — значки надетого на <paramref name="stack"/>.</summary>
    public static void Render(UiScreenState ui, bool shown, MapStack? stack, MapState map)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(map);
        var worn = stack is null ? [] : Equipment.Slots(stack);
        for (int s = 0; s < ManageStackLogic.SlotImages.Length; s++)
        {
            string id = ManageStackLogic.SlotImages[s];
            if (!ui.Screen.Elements.Any(e => e.Id == id)) continue;
            ui.SetHidden(id, !shown);
            ui.SetImage(id, shown && s < worn.Count && worn[s].Length > 0 && map.ItemTypes.TryGetValue(worn[s], out var type)
                ? $"{ItemArchive}:{ArtNames.Of(type)}" : null);
        }
    }

    /// <summary>Слот под точкой; вне слотов — null.</summary>
    public int? SlotAt(int x, int y)
    {
        foreach (var (slot, r) in _slots)
            if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return slot;
        return null;
    }
}
