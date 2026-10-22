using D2.Data.Menu;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Торговец (<c>DLG_MERCHANT</c>, <c>CSiteMerchantInterf</c> <c>0x4990e9</c>): справа склад торговца (<c>MLBOX_LINVENTORY</c>, в ячейке —
/// число штук <c>X005TA0323</c>), слева сумка отряда (<c>MLBOX_RINVENTORY</c>, одинаковые — одна ячейка, <see cref="ItemCells"/>). Картинка лавки — <c>SITE0003</c> у лавки задания,
/// иначе <c>SITE0002</c> (<c>0x49931f</c>), имя лавки — в <c>TXT_LABEL1</c> как есть (<c>0x4993e6</c>). Приветствие
/// <c>X005TA0493</c>/<c>0494</c> (по «задание»), пустой склад — <c>X005TA0515</c>, окно остаётся. Взятый предмет (<c>0x499781</c>) —
/// описание <c>X005TA0491</c> и ответ торговца: купить — <c>X005TA0100</c>/<c>0109</c>; продать — <c>X005TA0346</c> (не берёт),
/// ценность (<c>L_VALUABLE</c>) — <c>X005TA0589</c>, прочее — <c>X005TA0101</c>. Товар в сумку — вопрос <c>X005TA0102</c> (не хватает
/// золота — бросок не принимается), предмет из сумки или с куклы на склад — вопрос <c>X005TA0103</c> (особые не берутся).
/// Кукла (<c>TOG_PAPERDOLL</c>, <c>0x49a28e</c>): на месте героя и его отряда — <see cref="PaperdollView"/>; портрет, имя и подпись
/// сумки убираются, надеть из сумки и снять в сумку можно, надетое продаётся перетаскиванием на склад (<c>0x4fe84a</c>).
/// Рамки предметов при тащении — как в окне отряда (<see cref="ItemFrames"/>, <see cref="ItemFramesRender"/>).
/// Зелье из сумки — на воина отряда (сумка — <c>CDDStackInventoryForMerc</c> <c>0x49a0d1</c>, наследник сумки окна отряда с тем же
/// <c>IMidItemExt</c>; группа — <c>CDDStackGroup</c>; <see cref="PotionDrops"/>), воина можно переставить (<see cref="SiteLogic"/>).
/// </summary>
public sealed class MerchantLogic(WindowContext context, string stackId, string siteId, StackTexts texts,
                                  IReadOnlyList<UiScreen>? screens = null)
    : SiteLogic(context, stackId, siteId, "IMG_SLOT1", "TXT_LEADER_NAME")
{
    public const string Screen = "DLG_MERCHANT";
    private const string Store = "MLBOX_LINVENTORY", Bag = "MLBOX_RINVENTORY", Doll = "IMG_PAPERDOLL_BG";

    /// <summary>Фон места отряда, пока кукла выключена (<c>0x49a449</c>); без него сквозь левую часть окна видна карта.</summary>
    private const string GroupBackground = "DLG_MERCHANT_GROUP_BG";
    private readonly PaperdollView _doll = new();

    /// <summary>Что тянут: склад (<c>s</c>), сумка (<c>b</c>) — номер среди видимых; слот куклы (<c>w</c>).</summary>
    private (char Kind, int Index)? _drag;

    public bool Paperdoll { get; private set; }

    public IReadOnlyList<MerchantStock> Stock => Site.Stock.Where(s => s.Count > 0).ToList();

    public IReadOnlyList<string> BagItems =>
        [.. Stack.Items.Where(i => !Equipment.Slots(Stack).Contains(i, StringComparer.OrdinalIgnoreCase))];

    /// <summary>Сумка героя по ячейкам со счётчиком (<see cref="ItemCells"/>).</summary>
    public IReadOnlyList<ItemCell> BagCells => ItemCells.Fold(BagItems, C.Map);

    public override string Summary => base.Summary + $" товаров={Stock.Count} сумка={BagItems.Count} кукла={(Paperdoll ? 1 : 0)}";

    protected override bool GroupShown => !Paperdoll;

    public override UiScreen Shape(UiScreen screen)
    {
        var shaped = base.Shape(screen);
        var elements = shaped.Elements.ToList();
        PaperdollView.Underlay(elements, Doll);
        _doll.Insert(elements, screens, Doll);
        ItemFrames.Insert(elements, [.. ManageStackLogic.SlotImages, Bag, Store]);   // рамки предметов (CDDEquipment, сумка, склад)
        return shaped with { Elements = elements };
    }

    /// <summary>Переходы графа на склад и сумку (вопросы, записанные по перетаскиванию) не берутся: открыли бы шаблон без подстановки
    /// <c>%NAME%</c>/<c>%GOLD%</c>. Двойной щелчок по ним — <see cref="DoubleClick"/>.</summary>
    public override bool Allows(string element, UiScreenState ui) => element is not (Store or Bag);

    /// <summary>Двойной щелчок по товару — то же, что бросок его в сумку (<see cref="Buy"/>), по предмету сумки — что бросок на склад
    /// (<see cref="Sell"/>): тот же вопрос с именем и ценой, те же отказы. Отступление от движка по решению пользователя (15.10): у этих
    /// списков движок двойного щелчка не ставит (<c>0x53616c</c>: функтора <c>+0x84</c> и кнопки <c>+0x50</c> нет), покупка у него —
    /// только перетаскиванием (<c>docs/ui-windows.md</c>, торговец).</summary>
    public override void DoubleClick(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ListAt(ui, Store, Stock.Count, x, y) is { } s) Buy(s);
        else if (ListAt(ui, Bag, BagCells.Count, x, y) is { } b) Sell(BagCells[b].Top);
        else return;
        _drag = null;   // щелчок исполнен — отпускание ничего не бросает
        Frames(ui);
        ItemFramesRender(ui);
    }

    public override void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetImage("IMG_MERCHANT", Site.Mission ? "SITE0003" : "SITE0002", UiImageAlign.Clip);   // 260×248 в месте 260×219
        ui.SetEnabled("TOG_PAPERDOLL", _doll.Background is not null);
        Refresh(ui);
        if (Stock.Count == 0) Requests.Add(new UiTell(C.Text("X005TA0515")));
    }

    public override void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiToggled { Element: "TOG_PAPERDOLL" } t) Paperdoll = t.Checked && _doll.Background is not null;
        Refresh(ui);
    }

    protected override void Refresh(UiScreenState ui)
    {
        base.Refresh(ui);
        ui.SetChecked("TOG_PAPERDOLL", Paperdoll);
        ui.SetImage(Doll, Paperdoll ? _doll.Background : GroupBackground);
        PaperdollView.Render(ui, Paperdoll, Stack, C.Map);
        ui.SetText("TXT_LEADER", Paperdoll ? "" : UiTemplate.Fill(C.Text("X005TA0106"), ("NAME", LeaderName)));
        ui.SetText("TXT_DESCRIPTION", C.Text(Site.Mission ? "X005TA0493" : "X005TA0494"));
        ui.SetText("TXT_DDFEEDBACK", "");
        ui.SetCards(Store, [.. Stock.Select(s => new UiCard(ItemCells.IconFrame(s.Item, C), new UiPoint(7, 0),
            UiTemplate.Fill(C.Text("X005TA0323"), ("NUM", s.Count)), new UiRect(0, 0, 85, 85)))]);
        ui.SetCards(Bag, [.. BagCells.Select(c => ItemCells.Card(c, C, ItemCells.Spec(ui, Bag)))]);
        ItemFramesRender(ui);
    }

    /// <summary>
    /// Рамки предметов (<see cref="ItemFrames"/>), пока тащат предмет; бросили — рамок нет.
    /// <list type="bullet">
    /// <item>Слоты куклы — те же <c>CDDEquipment</c>, что в окне отряда (кукла <c>0x4cbcea</c> → <c>0x504c16</c> → <c>0x5048f6</c> →
    /// <c>0x50401c</c>, база <c>CDDBaseEquip</c> <c>0x571bbf</c>): товар склада — нет (<c>0x504782</c>: <c>0x49a722</c> → 2), предмет
    /// сумки и надетое — <see cref="ItemFrames.Slot"/> (<c>0x571c80</c>).</item>
    /// <item>Сумка (<c>CDDStackInventoryForMerc</c>, vt <c>0x6dec24</c>[1] = <c>0x5056f7</c>, как у сумки отряда): товар — если хватает
    /// золота (<c>0x5df9ea</c>), надетое — да, своё — нет.</item>
    /// <item>Склад (<c>CDDMerchantInventory</c> → <c>CMidDropInventory</c> <c>0x572147</c>, кадр вида 0 — <c>DRAGDROP_INVENT_VALID</c>
    /// по середине списка, <c>0x572585</c>): предмет героя, который торговец купит (<c>0x4fe84a</c> → <c>0x5dfe1a</c>, <see cref="Sells"/>).</item>
    /// </list>
    /// </summary>
    private void ItemFramesRender(UiScreenState ui)
    {
        var drag = _drag;
        string? item = drag switch
        {
            ('b', var i) when i < BagCells.Count => BagCells[i].Top,
            ('w', var w) => Worn(w),
            _ => null,
        };
        int? from = drag is ('w', var s) ? s : null;
        ItemFrames.Slots(ui, ManageStackLogic.SlotImages,
                         Paperdoll && item is { } it ? slot => ItemFrames.Slot(C.Session, StackId, it, slot, from) : null);
        ItemFrames.List(ui, Bag, drag is ('s', var g) ? Affordable(g) : from is not null && item is not null);
        ItemFrames.List(ui, Store, item is not null && Sells(item));
    }

    public override void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _drag = null;
        if (ListAt(ui, Store, Stock.Count, x, y) is { } s)
        {
            _drag = ('s', s);
            var item = C.Session.RequireBook().Items[Stock[s].Item];
            long price = C.Session.ItemPrice(Stack, item).Gold;   // 0x5dfd5f: со скидкой лидера
            Describe(ui, Stock[s].Item);
            ui.SetText("TXT_DDFEEDBACK", UiTemplate.Fill(C.Text(Gold >= price ? "X005TA0100" : "X005TA0109"), ("PRICE", price)));
        }
        else if (ListAt(ui, Bag, BagCells.Count, x, y) is { } b)
        {
            _drag = ('b', b);
            Offer(ui, BagCells[b].Top);
        }
        else if (Paperdoll && _doll.SlotAt(x, y) is { } w && Worn(w) is { } worn)
        {
            _drag = ('w', w);
            Offer(ui, worn);
        }
        else
        {
            base.Press(x, y, ui);   // воин отряда
            return;
        }
        Frames(ui);   // зелье из сумки — рамки у воинов (0x56c58a)
        ItemFramesRender(ui);   // и у слотов куклы и списков (0x571b20, 0x56cf89, 0x572511)
    }

    /// <summary>Примет ли место тащимый предмет (как <see cref="Release"/>): товар — сумка, если хватает золота (рамка списка по цене,
    /// <c>0x5df9ea</c>); предмет сумки — склад, слот куклы с рамкой (не «нет», <c>0x56eace</c>) или воин с рамкой (зелье, свиток и посох —
    /// <see cref="DropFrames.Potion"/>); надетое — склад, сумка или другой слот с рамкой. Предмет не тащат — как у площадки (воин).</summary>
    public override bool? DropAccepts(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (_drag is not { } d) return base.DropAccepts(x, y, ui);
        return d.Kind switch
        {
            's' => Inside(ui, Bag, x, y) && Affordable(d.Index),
            'b' => d.Index < BagCells.Count && (Inside(ui, Store, x, y)
                   || SlotTarget(x, y, BagCells[d.Index].Top, null) is not null
                   || CellAt(x, y) is { } cell && C.Session.GroupCells(StackId) is { } cells
                      && DropFrames.Potion(C, StackId, BagCells[d.Index].Top, cells, cell) != DropFrame.None),
            _ => Inside(ui, Store, x, y) || Inside(ui, Bag, x, y)
                 || Worn(d.Index) is { } worn && SlotTarget(x, y, worn, d.Index) is { } to && to != d.Index,
        };
    }

    /// <summary>Кадр тащимого (<see cref="IWindowLogic.DragImage"/>): значок товара, предмета сумки или надетого (<c>CDDFoldedInv</c>
    /// <c>0x56d84d</c>: картинка тащимого по середине курсора); воин — его портрет.</summary>
    public override string? DragImage => _drag switch
    {
        ('s', var i) when i < Stock.Count => ItemCells.IconFrame(Stock[i].Item, C),
        ('b', var i) when i < BagCells.Count => ItemCells.Icon(BagCells[i].Top, C),
        ('w', var i) when Worn(i) is { } worn => ItemCells.Icon(worn, C),
        null => base.DragImage,
        _ => null,
    };

    /// <summary>Тащат предмет сумки — рамки зелья (<see cref="DropFrames.Potion"/>), иначе — как у площадки (воин).</summary>
    protected override Func<int, DropFrame>? FrameState(UnitCells cells) =>
        _drag is ('b', var i) && i < BagCells.Count && BagCells[i].Top is var item
            ? p => DropFrames.Potion(C, StackId, item, cells, p)
            : base.FrameState(cells);

    public override void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var drag = _drag;
        _drag = null;
        if (drag is not { } d)
        {
            base.Release(x, y, ui);
            return;
        }
        string stack = StackId;
        string? item = d.Kind switch
        {
            'b' when d.Index < BagCells.Count => BagCells[d.Index].Top,
            'w' => Worn(d.Index),
            _ => null,
        };
        int? slot = item is null ? null : SlotTarget(x, y, item, d.Kind == 'w' ? d.Index : null);
        switch (d.Kind)
        {
            case 's' when Inside(ui, Bag, x, y):
                if (!Buy(d.Index)) return;
                break;
            case 'b' when Inside(ui, Store, x, y):
                Sell(BagCells[d.Index].Top);
                break;
            case 'b' when CellAt(x, y) is { } cell && C.Session.GroupCells(StackId) is { } cells
                          && DropFrames.Potion(C, StackId, BagCells[d.Index].Top, cells, cell) != DropFrame.None:
                LastRefusal = PotionDrops.Drink(C, Requests, StackId, BagCells[d.Index].Top, cells.Cells[cell]);
                break;
            case 'w' when Inside(ui, Store, x, y) && Worn(d.Index) is { } worn:
                Sell(worn);
                break;
            case 'b' when slot is { } to:
                Equip(BagCells[d.Index].Top, to);
                break;
            case 'w' when Inside(ui, Bag, x, y):
                C.Session.Run(new UnequipItemCommand(stack, d.Index));
                LastRefusal = null;
                break;
            case 'w' when slot is { } to && to != d.Index && item is { } moved:
                C.Session.Run(new UnequipItemCommand(stack, d.Index));   // со слота на слот — как в окне отряда
                Equip(moved, to);
                break;
        }
        Refresh(ui);
    }

    /// <summary>Правая кнопка: товар — карточка с ценой (<c>0x4fe330</c>), предмет героя — без цены, воин — его карточка.</summary>
    public override void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ListAt(ui, Store, Stock.Count, x, y) is { } s)
        {
            if (ItemCards.For(C, Stock[s].Item, texts, withPrice: true, C.Session.LowerCostPercent(Stack)) is { } card) Requests.Add(card);
            return;
        }
        string? item = ListAt(ui, Bag, BagCells.Count, x, y) is { } b ? BagCells[b].Top
            : Paperdoll && _doll.SlotAt(x, y) is { } w ? Worn(w) : null;
        if (item is not null)
        {
            if (ItemCards.For(C, item, texts) is { } card) Requests.Add(card);
            return;
        }
        base.RightPress(x, y, ui);
    }

    /// <summary>Слот куклы под точкой, который примет предмет <paramref name="item"/> (рамка не «нет», <see cref="ItemFrames.Slot"/>,
    /// <c>0x56eace</c>; не того рода — не принимается, без сообщения); куклы нет — null.</summary>
    private int? SlotTarget(int x, int y, string item, int? fromSlot) =>
        Paperdoll && _doll.SlotAt(x, y) is { } slot && ItemFrames.Slot(C.Session, StackId, item, slot, fromSlot) != DropFrame.None ? slot : null;

    private void Equip(string item, int slot)
    {
        var refusal = C.Session.Run<EquipRefusal>(new EquipItemCommand(StackId, item, slot));
        LastRefusal = refusal == EquipRefusal.Ok ? null : refusal.ToString();
        if (ManageStackLogic.RefusalText(refusal) is { } text) Requests.Add(new UiTell(C.Text(text)));
    }

    /// <summary>Хватит ли золота на товар склада <paramref name="index"/> (<c>0x5df9ea</c>: цена со скидкой лидера).</summary>
    private bool Affordable(int index) =>
        index < Stock.Count && Gold >= C.Session.ItemPrice(Stack, C.Session.RequireBook().Items[Stock[index].Item]).Gold;

    /// <summary>Купит ли торговец предмет героя (<c>0x5dfe1a</c>): особые (<c>L_SPECIAL</c>) — нет. Тем же решаются рамка склада и продажа.</summary>
    private bool Sells(string item) => C.Session.ItemOf(item) is { Category: not ItemCategory.Special };

    private string? Worn(int slot) => Equipment.Slots(Stack) is var worn && slot < worn.Count && worn[slot].Length > 0 ? worn[slot] : null;

    /// <summary>Описание предмета героя и ответ торговца: не берёт, ценность или обычная цена.</summary>
    private void Offer(UiScreenState ui, string item)
    {
        if (C.Session.ItemOf(item) is not { } it) return;
        Describe(ui, C.Map.ItemTypes[item]);
        string line = it.Category switch
        {
            ItemCategory.Special => C.Text("X005TA0346"),
            ItemCategory.Valuable => UiTemplate.Fill(C.Text("X005TA0589"), ("PRICE", SellPrice(item))),
            _ => UiTemplate.Fill(C.Text("X005TA0101"), ("PRICE", SellPrice(item))),
        };
        ui.SetText("TXT_DDFEEDBACK", line);
    }

    /// <summary>Товар склада в сумку: вопрос <c>X005TA0102</c> с именем и ценой (<c>0x505904…0x50597b</c>), «Да» — покупка. Не хватает
    /// золота — false, бросок не принимается (<c>0x5df9ea</c>, код <c>0xA</c>).</summary>
    private bool Buy(int index)
    {
        string stack = StackId, site = SiteId, type = Stock[index].Item;
        long price = C.Session.ItemPrice(Stack, C.Session.RequireBook().Items[type]).Gold;   // 0x50595a → 0x5dfd5f
        if (Gold < price) { LastRefusal = "не хватает золота"; return false; }
        Requests.Add(new UiAsk(UiTemplate.Fill(C.Text("X005TA0102"), ("NAME", ItemName(type)), ("GOLD", price)), () =>
            Report(C.Session.Run<string?>(new BuyItemCommand(stack, site, type)))));
        return true;
    }

    private void Sell(string item)
    {
        if (!Sells(item)) { LastRefusal = "особый предмет"; return; }
        string stack = StackId, site = SiteId;
        Requests.Add(new UiAsk(UiTemplate.Fill(C.Text("X005TA0103"), ("NAME", ItemName(C.Map.ItemTypes[item])), ("GOLD", SellPrice(item))),
            () => Report(C.Session.Run<string?>(new SellItemCommand(stack, site, item)))));
    }

    private void Report(string? refusal)
    {
        LastRefusal = refusal;
        if (refusal is not null) Requests.Add(new UiTell(refusal));
    }

    private long SellPrice(string item) =>
        C.Session.ItemOf(item) is { } it
            ? Treasury.ItemSellPrice(it, C.Session.RequireBook().Economy.SellRatio,
                                     C.Map.TalismanCharges.TryGetValue(item, out int n) ? n : null).Gold
            : 0;

    private void Describe(UiScreenState ui, string type) =>
        ui.SetText("TXT_DESCRIPTION", UiTemplate.Fill(C.Text("X005TA0491"), ("NAME", ItemName(type)),
            ("DESC", texts.ItemDescs is { } d && d.TryGetValue(type, out var desc) ? C.Text(desc) : "")));

    private string ItemName(string type) => texts.Items.TryGetValue(type, out var n) ? C.Text(n) : type;
}
