using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Окно армии: предметы, кукла гостя, подписи групп и карточка воина.
/// <list type="bullet">
/// <item>Сумка гостя <c>MLBOX_LINVENTORY</c> (<c>CDDStackInventoryDisplay</c> <c>0x501b1e</c>, без надетого) и склад укрепления
/// <c>MLBOX_RINVENTORY</c> (<c>CDDFortInventoryDisplay</c> <c>0x506477</c>); перенос — перетаскиванием
/// (<see cref="Play.GameSession.ExchangeItem"/>, ответ пользователя 25.09).</item>
/// <item>Кукла (<c>TOG_PAPERDOLL</c> → <c>DLG_PAPERDOLL</c> на месте группы гостя, <c>0x4b268c</c>): группа гостя, портрет,
/// имя и подпись отряда убираются; предмет из сумки или со склада — на слот (со склада — сперва в сумку), со слота — в
/// сумку или на склад. Ответ пользователя 25.09: «кукла вместо отряда».</item>
/// <item>Подписи: <c>TXT_LSTACK_STATS</c> (<c>0x4b312d</c>: «Отряд», скорость <c>X005TA0169</c>, лидерство <c>X005TA0166</c>
/// из <c>X005TA0575</c> «занято − 1 / лидерство − 1»; гостя нет — <c>X005TA0158</c>), <c>TXT_RSTACK_STATS</c>
/// (<c>0x4b1f82</c>: «Гарнизон», у деревни — уровень <c>X005TA0208</c> и <c>X005TA0576</c>).</item>
/// <item>Правая кнопка на воине — карточка (<see cref="UnitCardLogic"/>), пока кнопка держится.</item>
/// </list>
/// Одинаковые предметы в сумке и на складе — одна ячейка со счётчиком (<see cref="ItemCells"/>), тянется последний.
/// <para>Зелье из сумки гостя, со склада или со слота куклы — на воина любой группы окна (обе группы — <c>CDDStackGroup</c>/<c>CDDReinfGroup</c>,
/// рамки <c>0x500d03</c>, бросок <c>0x4ff396</c>; держатель — сумка гостя или склад, <see cref="PotionDrops"/>). Журнал gaps: пять зелий со
/// склада воинам гостя (держатель — укрепление, воин — отряда), human: зелье из сумки лидеру гостя.</para>
/// </summary>
public sealed partial class CityStackLogic
{
    private const string Bag = "MLBOX_LINVENTORY", Store = "MLBOX_RINVENTORY", Doll = "IMG_PAPERDOLL_BG", SkillIcons = "MLBOX_ITEM_ICON";

    private readonly PaperdollView _doll = new();
    private UiRect _bag, _store;
    private UiList _listSpec = new();

    /// <summary>Что тянут из предметов: сумка (<c>l</c>), склад (<c>r</c>) — номер среди видимых; слот куклы (<c>s</c>).</summary>
    private (char Kind, int Index)? _itemDrag;

    /// <summary>Открыта кукла гостя.</summary>
    public bool Paperdoll { get; private set; }

    /// <summary>Сумка гостя без надетого; гостя нет — пусто.</summary>
    public IReadOnlyList<string> BagItems =>
        Guest is { } stack ? [.. stack.Items.Where(i => !Equipment.Slots(stack).Contains(i, StringComparer.OrdinalIgnoreCase))] : [];

    /// <summary>Склад укрепления.</summary>
    public IReadOnlyList<string> StoreItems => Fort.Items;

    /// <summary>Сумка и склад по ячейкам: одинаковые предметы — одна ячейка со счётчиком (<see cref="ItemCells"/>).</summary>
    public IReadOnlyList<ItemCell> BagCells => ItemCells.Fold(BagItems, _c.Map);

    public IReadOnlyList<ItemCell> StoreCells => ItemCells.Fold(StoreItems, _c.Map);

    private MapStack? Guest => VisitorId.Length > 0 && _c.Map.Stacks.TryGetValue(VisitorId, out var s) ? s : null;

    /// <summary>Кукла встраивается на месте фона групп (оба — в точке <c>IMG_PAPERDOLL_BG</c>), видна — когда включена.</summary>
    private void ShapeItems(List<UiElement> elements, IReadOnlyList<UiScreen> screens)
    {
        var bag = elements.First(e => e.Id == Bag);
        (_bag, _store, _listSpec) = (bag.Rect, elements.First(e => e.Id == Store).Rect, bag.List ?? new UiList());
        _doll.Insert(elements, screens, Doll);
        ItemFrames.Insert(elements, [.. ManageStackLogic.SlotImages, Bag, Store]);   // рамки предметов (CDDBaseEquip, CDDFoldedInv)
    }

    /// <summary>Рамки предметов (<see cref="ItemFrames"/>): слоты куклы гостя (видны, пока кукла открыта) и списки — сумка гостя и склад, кроме
    /// списка, из которого тащат; у сумки без гостя (<c>CDDNoInventoryDisplay</c>) рамки нет.</summary>
    private void ItemFramesRender(UiScreenState ui)
    {
        var d = _itemDrag;
        var item = Dragged;
        ItemFrames.Slots(ui, ManageStackLogic.SlotImages, Paperdoll && item is { } it && Guest is { } g
            ? s => ItemFrames.Slot(_c.Session, g.Id, it.Item, s, d is ('s', var from) ? from : null)
            : null);
        ItemFrames.List(ui, Bag, item is not null && Guest is not null && d is not ('l', _));
        ItemFrames.List(ui, Store, item is not null && d is not ('r', _));
    }

    private void RefreshItems(UiScreenState ui, bool guest)
    {
        var map = _c.Map;
        ui.SetEnabled("TOG_PAPERDOLL", guest);
        if (!guest) Paperdoll = false;
        ui.SetChecked("TOG_PAPERDOLL", Paperdoll);
        ui.SetImage(Doll, Paperdoll ? _doll.Background ?? "DLG_PAPERDOLL_PAPERDOLL_BG" : "DLG_CITY_STACK_GROUP_BG");
        PaperdollView.Render(ui, Paperdoll, Guest, map);
        ui.SetCards(Bag, [.. BagCells.Select(c => ItemCells.Card(c, _c, ItemCells.Spec(ui, Bag)))]);
        ui.SetCards(Store, [.. StoreCells.Select(c => ItemCells.Card(c, _c, ItemCells.Spec(ui, Store)))]);
        Stats(ui, guest);
    }

    private void Stats(UiScreenState ui, bool guest)
    {
        // Навыки гостя-героя — значки ABIL в MLBOX_ITEM_ICON под сводкой (ячейка 31×36 — размер значка), как в окне отряда; при
        // кукле сводки, навыков и золота не видно (снимки армии столицы 28.09: 1413573, 2042537 — значки; 984972 — кукла).
        ui.SetCards(SkillIcons, Paperdoll || Guest is not { } visitor ? []
            : [.. HeroSkills.Rows(_c, visitor).Select(r => new UiCard($"Icons:{r.Icon}", new UiPoint(0, 0), null, default))]);
        ui.SetHidden("IMG_MANA_GOLD", Paperdoll);
        ui.SetHidden("TXT_GOLD", Paperdoll);
        if (Paperdoll || !guest)
        {
            ui.SetText("TXT_LSTACK_STATS", Paperdoll ? "" : _c.Text("X005TA0158"));
        }
        else if (Guest is { } stack)
        {
            var cells = _c.Session.GroupCells(stack.Id);
            string lead = UiTemplate.Fill(_c.Text("X005TA0575"), ("USED", (cells?.Occupied ?? 1) - 1),
                                          ("AVAIL", _c.Session.Leadership(stack) - 1));
            ui.SetText("TXT_LSTACK_STATS", _c.Text("X005TA0080")
                + UiTemplate.Fill(_c.Text("X005TA0169"), ("NOW", stack.Move), ("MAX", _c.Session.DayAllowance(stack)))
                + UiTemplate.Fill(_c.Text("X005TA0166"), ("QTY", lead)));
        }
        var fort = Fort;
        string right = _c.Text("X005TA0081") + "\\n";
        if (fort.Kind == SiteKind.Village)
            right += $"{_c.Text("X005TA0208")} {fort.Size ?? 1}\\n"
                     + UiTemplate.Fill(_c.Text("X005TA0576"), ("IN", fort.Guards.Count), ("TOTAL", fort.Size ?? 1));
        ui.SetText("TXT_RSTACK_STATS", right);
    }

    /// <summary>Кадр тащимого (<see cref="IWindowLogic.DragImage"/>): значок предмета (<c>IconItem</c>) или портрет воина.</summary>
    public string? DragImage => _itemDrag switch
    {
        ('l', var i) when i < BagCells.Count => ItemCells.Icon(BagCells[i].Top, _c),
        ('r', var i) when i < StoreCells.Count => ItemCells.Icon(StoreCells[i].Top, _c),
        ('s', var i) when Guest is { } g && Equipment.Slots(g)[i] is { Length: > 0 } worn => ItemCells.Icon(worn, _c),
        null => _drag is { } d ? ItemCells.Face(UnitAt(d), _c) : null,
        _ => null,
    };

    /// <summary>Держатель и экземпляр тащимого предмета: сумка и слот куклы — гость, склад — укрепление; не тащат — null.</summary>
    private (string Holder, string Item)? Dragged => _itemDrag switch
    {
        ('l', var i) when Guest is { } g && i < BagCells.Count => (g.Id, BagCells[i].Top),
        ('r', var i) when i < StoreCells.Count => (FortId, StoreCells[i].Top),
        ('s', var i) when Guest is { } g && Equipment.Slots(g)[i] is { Length: > 0 } worn => (g.Id, worn),
        _ => null,
    };

    /// <summary>Слот куклы под точкой, который примет тащимый предмет (рамка не «нет», <see cref="ItemFrames.Slot"/>); иначе null.</summary>
    private int? SlotTarget(int x, int y)
    {
        if (!Paperdoll || _doll.SlotAt(x, y) is not { } slot || Dragged is not { } d || Guest is not { } g) return null;
        int? from = _itemDrag is ('s', var s) ? s : null;
        return ItemFrames.Slot(_c.Session, g.Id, d.Item, slot, from) != DropFrame.None ? slot : null;
    }

    /// <summary>Воин под точкой, который примет тащимое зелье (рамка не «нет», <c>0x56eace</c>); иначе null.</summary>
    private string? PotionTarget(int x, int y)
    {
        if (Dragged is not { } d || CellAt(x, y) is not { } cell || GroupId(cell.Group) is not { } group
            || _c.Session.GroupCells(group) is not { } cells) return null;
        return DropFrames.Potion(_c, d.Holder, d.Item, cells, cell.Pos) != DropFrame.None ? cells.Cells[cell.Pos] : null;
    }

    /// <summary>Примет ли место тащимый предмет (как <see cref="ReleaseItems"/>); предмет не тащат — null.</summary>
    private bool? ItemDropAccepts(int x, int y)
    {
        if (_itemDrag is not { } d) return null;
        if (PotionTarget(x, y) is not null) return true;
        bool slot = SlotTarget(x, y) is not null;
        return d.Kind switch
        {
            'l' => Inside(_store, x, y) || slot,
            'r' => Inside(_bag, x, y) || slot,
            _ => Inside(_bag, x, y) || Inside(_store, x, y),
        };
    }

    /// <summary>Нажатие на предмет сумки, склада или слот куклы — начало перетаскивания; true — нажатие было по ним.</summary>
    private bool PressItems(int x, int y, UiScreenState ui)
    {
        _itemDrag = null;
        if (ListAt(_bag, Bag, BagCells.Count, x, y, ui) is { } b) _itemDrag = ('l', b);
        else if (ListAt(_store, Store, StoreCells.Count, x, y, ui) is { } r) _itemDrag = ('r', r);
        else if (Paperdoll && _doll.SlotAt(x, y) is { } s && Guest is { } g && Equipment.Slots(g)[s].Length > 0) _itemDrag = ('s', s);
        return _itemDrag is not null;
    }

    /// <summary>Бросок предмета: в другой список — перенос, на слот куклы — надеть, со слота в список — снять (на склад —
    /// ещё и перенести). true — тянули предмет.</summary>
    private bool ReleaseItems(int x, int y)
    {
        if (_itemDrag is not { } d) return false;
        if (PotionTarget(x, y) is { } unit && Dragged is { } potion)
        {
            _itemDrag = null;
            LastRefusal = PotionDrops.Drink(_c, _requests, potion.Holder, potion.Item, unit);
            return true;
        }
        int? toSlot = SlotTarget(x, y);
        _itemDrag = null;
        if (Guest is not { } guest) return true;
        string fort = FortId, stack = guest.Id;
        bool toBag = Inside(_bag, x, y), toStore = Inside(_store, x, y);
        switch (d.Kind)
        {
            case 'l' when toStore:
                LastRefusal = _c.Session.Run<string?>(new ExchangeItemCommand(stack, fort, BagCells[d.Index].Top));
                break;
            case 'r' when toBag:
                LastRefusal = _c.Session.Run<string?>(new ExchangeItemCommand(fort, stack, StoreCells[d.Index].Top));
                break;
            case 'l' when toSlot is { } slot:
                Equip(stack, BagCells[d.Index].Top, slot);
                break;
            case 'r' when toSlot is { } slot:
                // Со склада — сперва в сумку; не наделось — остаётся в сумке (откат движка не разобран, ранг 2).
                string item = StoreCells[d.Index].Top;
                LastRefusal = _c.Session.Run<string?>(new ExchangeItemCommand(fort, stack, item));
                if (LastRefusal is null) Equip(stack, item, slot);
                break;
            case 's' when toBag:
                _c.Session.Run(new UnequipItemCommand(stack, d.Index));
                LastRefusal = null;
                break;
            case 's' when toStore:
                LastRefusal = _c.Session.Run<string?>(new ExchangeItemCommand(stack, fort, Equipment.Slots(guest)[d.Index]));
                break;
        }
        return true;
    }

    private void Equip(string stack, string item, int slot)
    {
        var refusal = _c.Session.Run<EquipRefusal>(new EquipItemCommand(stack, item, slot));
        LastRefusal = refusal == EquipRefusal.Ok ? null : refusal.ToString();
        if (ManageStackLogic.RefusalText(refusal) is { } text) _requests.Add(new UiTell(_c.Text(text)));
    }

    /// <summary>Правая кнопка: на воине — его карточка, на предмете сумки, склада или куклы — карточка предмета.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        string? item = ListAt(_bag, Bag, BagCells.Count, x, y, ui) is { } b ? BagCells[b].Top
            : ListAt(_store, Store, StoreCells.Count, x, y, ui) is { } r ? StoreCells[r].Top
            : Paperdoll && _doll.SlotAt(x, y) is { } s && Guest is { } g && Equipment.Slots(g)[s] is { Length: > 0 } worn ? worn : null;
        if (item is not null)
        {
            if (ItemCards.For(_c, item) is { } card) _requests.Add(card);
            return;
        }
        if (CellAt(x, y) is not { } cell) return;   // при кукле группа гостя в CellAt не попадает
        if (UnitCards.For(_c, GroupId(cell.Group), cell.Pos) is { } open) _requests.Add(open);
    }

    /// <summary>Предмет списка под точкой (номер среди его предметов): ячейки по разметке и прокрутка.</summary>
    private int? ListAt(UiRect list, string id, int count, int x, int y, UiScreenState ui)
    {
        if (!Inside(list, x, y) || _listSpec.CellWidth <= 0) return null;
        int step = _listSpec.CellWidth + _listSpec.GapX;
        int col = (x - list.X) / step;
        if (x - list.X - (col * step) >= _listSpec.CellWidth) return null;
        int index = ui.ListTop(id) + col;
        return index < count ? index : null;
    }
}
