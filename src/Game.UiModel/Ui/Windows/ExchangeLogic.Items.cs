using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Окно обмена: сумки и куклы обеих сторон (<see cref="ExchangeLogic"/>).</summary>
public sealed partial class ExchangeLogic
{
    private const string ItemArchive = "IconItem";

    /// <summary>Слоты куклы стороны: <c>IMG_{L|R}BANNER</c> и т. д. — по порядку <see cref="ManageStackLogic.SlotImages"/>.</summary>
    private static string SlotImage(string side, int slot) => $"IMG_{side}{ManageStackLogic.SlotImages[slot]["IMG_".Length..]}";

    private readonly Dictionary<(string Side, int Slot), UiRect> _slots = [];
    private string? _dollBackground;

    /// <summary>Что тянут из предметов: сумка стороны (номер ячейки) или слот куклы стороны.</summary>
    private (string Side, bool Slot, int Index)? _itemDrag;

    /// <summary>Открыты куклы героев.</summary>
    public bool Paperdoll { get; private set; }

    /// <summary>Сумка стороны без надетого.</summary>
    public IReadOnlyList<string> BagItems(string side) =>
        Stack(side) is { } stack ? [.. stack.Items.Where(i => !Equipment.Slots(stack).Contains(i, StringComparer.OrdinalIgnoreCase))] : [];

    public IReadOnlyList<ItemCell> BagCells(string side) => ItemCells.Fold(BagItems(side), _c.Map);

    private static string BagList(string side) => $"MLBOX_{side}INVENTORY";

    private void ShapeItems(List<UiElement> elements)
    {
        _dollBackground = _screens.FirstOrDefault(s => s.Id == PaperdollView.Screen)?.Background;
        foreach (string side in (string[])[Left, Right])
            for (int s = 0; s < ManageStackLogic.SlotImages.Length; s++)
                if (elements.FirstOrDefault(e => e.Id == SlotImage(side, s)) is { } slot) _slots[(side, s)] = slot.Rect;
        foreach (string side in (string[])[Left, Right])   // рамки предметов (CDDBaseEquip, CDDFoldedInv)
            ItemFrames.Insert(elements, [.. SlotImages(side), BagList(side)]);
    }

    private static string[] SlotImages(string side) => [.. Enumerable.Range(0, ManageStackLogic.SlotImages.Length).Select(s => SlotImage(side, s))];

    /// <summary>Слот куклы под точкой, который примет тащимый предмет (рамка не «нет», <see cref="ItemFrames.Slot"/>); иначе null.</summary>
    private (string Side, int Slot)? SlotTarget(int x, int y, (string Side, bool Slot, int Index)? drag = null)
    {
        var d = drag ?? _itemDrag;
        if (SlotAt(x, y) is not { } slot || d is not { } from || Stack(slot.Side) is not { } stack || Stack(from.Side) is not { } source) return null;
        string item = from.Slot ? Equipment.Slots(source)[from.Index] : from.Index < BagCells(from.Side).Count ? BagCells(from.Side)[from.Index].Top : "";
        int? fromSlot = from.Slot ? (from.Side == slot.Side ? from.Index : -1) : null;
        return item.Length > 0 && ItemFrames.Slot(_c.Session, stack.Id, item, slot.Slot, fromSlot) != DropFrame.None ? slot : null;
    }

    /// <summary>Рамки предметов (<see cref="ItemFrames"/>): слоты обеих кукол (видны, пока куклы открыты) и обе сумки, кроме той, из которой
    /// тащат.</summary>
    private void ItemFramesRender(UiScreenState ui)
    {
        var d = _itemDrag;
        var item = Dragged;
        foreach (string side in (string[])[Left, Right])
        {
            var stack = Stack(side);
            ItemFrames.Slots(ui, SlotImages(side), Paperdoll && item is { } it && stack is not null
                ? s => ItemFrames.Slot(_c.Session, stack.Id, it.Item, s, d is { Slot: true } from ? (from.Side == side ? from.Index : -1) : null)
                : null);
            ItemFrames.List(ui, BagList(side), item is not null && stack is not null && !(d is { Slot: false } b && b.Side == side));
        }
    }

    private void RefreshItems(UiScreenState ui, string side, MapStack? stack)
    {
        var worn = stack is null ? [] : Equipment.Slots(stack);
        for (int s = 0; s < ManageStackLogic.SlotImages.Length; s++)
        {
            string id = SlotImage(side, s);
            ui.SetHidden(id, !Paperdoll);
            ui.SetImage(id, Paperdoll && s < worn.Count && worn[s].Length > 0 && _c.Map.ItemTypes.TryGetValue(worn[s], out var type)
                ? $"{ItemArchive}:{ArtNames.Of(type)}" : null);
        }
        ui.SetCards(BagList(side), [.. BagCells(side).Select(c => ItemCells.Card(c, _c, ItemCells.Spec(ui, BagList(side))))]);
    }

    private (string Side, int Slot)? SlotAt(int x, int y)
    {
        if (!Paperdoll) return null;
        foreach (var (key, r) in _slots)
            if (x >= r.X && y >= r.Y && x < r.Right && y < r.Bottom) return key;
        return null;
    }

    private (string Side, int Index)? BagAt(int x, int y, UiScreenState ui)
    {
        foreach (string side in (string[])[Left, Right])
            if (SiteLogicLists.At(ui, BagList(side), BagCells(side).Count, x, y) is { } i) return (side, i);
        return null;
    }

    private string? BagSide(int x, int y, UiScreenState ui) =>
        SiteLogicLists.Inside(ui, BagList(Left), x, y) ? Left : SiteLogicLists.Inside(ui, BagList(Right), x, y) ? Right : null;

    /// <summary>Предмет под точкой — для карточки: ячейка сумки или надетое в слоте куклы.</summary>
    private string? ItemAt(int x, int y, UiScreenState ui)
    {
        if (BagAt(x, y, ui) is { } b) return BagCells(b.Side)[b.Index].Top;
        return SlotAt(x, y) is { } s && Stack(s.Side) is { } stack && Equipment.Slots(stack)[s.Slot] is { Length: > 0 } worn ? worn : null;
    }

    /// <summary>Держатель и экземпляр тащимого предмета (отряд стороны); не тащат — null.</summary>
    private (string Holder, string Item)? Dragged => _itemDrag is { } d && Stack(d.Side) is { } from
        ? d.Slot ? Equipment.Slots(from)[d.Index] is { Length: > 0 } worn ? (from.Id, worn) : null
          : d.Index < BagCells(d.Side).Count ? (from.Id, BagCells(d.Side)[d.Index].Top) : null
        : null;

    /// <summary>Воин под точкой, который примет тащимое зелье (рамка не «нет», <c>0x56eace</c>; обе группы — <c>CDDStackGroup</c>, зелье из
    /// сумки любой стороны — воину любой стороны, <see cref="PotionDrops"/>); иначе null.</summary>
    private string? PotionTarget(int x, int y)
    {
        if (Dragged is not { } d || CellAt(x, y) is not { } cell || _c.Session.GroupCells(StackOf(cell.Side)) is not { } cells) return null;
        return DropFrames.Potion(_c, d.Holder, d.Item, cells, cell.Pos) != DropFrame.None ? cells.Cells[cell.Pos] : null;
    }

    private bool? ItemDropAccepts(int x, int y, UiScreenState ui)
    {
        if (_itemDrag is not { } d) return null;
        if (PotionTarget(x, y) is not null) return true;
        if (BagSide(x, y, ui) is { } bag) return bag != d.Side || d.Slot;
        return SlotTarget(x, y) is { } slot && (slot.Side, slot.Slot) != (d.Side, d.Slot ? d.Index : -1);
    }

    private bool PressItems(int x, int y, UiScreenState ui)
    {
        _itemDrag = BagAt(x, y, ui) is { } b ? (b.Side, false, b.Index)
            : SlotAt(x, y) is { } s && Stack(s.Side) is { } stack && Equipment.Slots(stack)[s.Slot].Length > 0 ? (s.Side, true, s.Slot)
            : null;
        return _itemDrag is not null;
    }

    /// <summary>Кадр тащимого (<see cref="IWindowLogic.DragImage"/>): значок предмета или портрет воина.</summary>
    public string? DragImage => _itemDrag is { } d
        ? Stack(d.Side) is not { } from ? null
          : d.Slot ? Equipment.Slots(from)[d.Index] is { Length: > 0 } worn ? ItemCells.Icon(worn, _c) : null
          : d.Index < BagCells(d.Side).Count ? ItemCells.Icon(BagCells(d.Side)[d.Index].Top, _c) : null
        : _drag is { } u ? ItemCells.Face(UnitAt(u), _c) : null;

    /// <summary>Бросок предмета: в сумку другой стороны — перенос (надетый снимается), в свою сумку со слота — снять, на слот —
    /// надеть (с чужой стороны — сперва перенос, со своего слота — снять). true — тянули предмет.</summary>
    private bool ReleaseItems(int x, int y, UiScreenState ui)
    {
        if (_itemDrag is not { } d) return false;
        if (PotionTarget(x, y) is { } unit && Dragged is { } potion)
        {
            _itemDrag = null;
            LastRefusal = PotionDrops.Drink(_c, _requests, potion.Holder, potion.Item, unit);
            return true;
        }
        _itemDrag = null;
        if (Stack(d.Side) is not { } from) return true;
        string item = d.Slot ? Equipment.Slots(from)[d.Index] : BagCells(d.Side)[d.Index].Top;
        if (BagSide(x, y, ui) is { } bag)
        {
            if (bag != d.Side) LastRefusal = _c.Session.Run<string?>(new ExchangeItemCommand(StackOf(d.Side), StackOf(bag), item));
            else if (d.Slot)
            {
                _c.Session.Run(new UnequipItemCommand(from.Id, d.Index));
                LastRefusal = null;
            }
        }
        else if (SlotTarget(x, y, d) is { } slot && (slot.Side, slot.Slot) != (d.Side, d.Slot ? d.Index : -1))
        {
            // С чужой стороны — сперва в свою сумку (надетое снимается); со своего слота — снять (как в окне отряда); не наделось —
            // остаётся в сумке (как в окне армии).
            if (slot.Side != d.Side) LastRefusal = _c.Session.Run<string?>(new ExchangeItemCommand(StackOf(d.Side), StackOf(slot.Side), item));
            else
            {
                if (d.Slot) _c.Session.Run(new UnequipItemCommand(from.Id, d.Index));
                LastRefusal = null;
            }
            if (LastRefusal is null) Equip(StackOf(slot.Side), item, slot.Slot);
        }
        return true;
    }

    private void Equip(string stack, string item, int slot)
    {
        var refusal = _c.Session.Run<EquipRefusal>(new EquipItemCommand(stack, item, slot));
        LastRefusal = refusal == EquipRefusal.Ok ? null : refusal.ToString();
        if (ManageStackLogic.RefusalText(refusal) is { } text) _requests.Add(new UiTell(_c.Text(text)));
    }
}
