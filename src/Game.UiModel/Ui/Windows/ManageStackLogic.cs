using System.Globalization;
using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Имена предметов (<c>GItem.NAME_TXT</c>) и навыков героя (<c>GleaUpg.NAME_TXT</c> по <c>MODIF_ID</c>).</summary>
public sealed record StackTexts(IReadOnlyDictionary<string, string> Items, IReadOnlyDictionary<string, string> Skills,
                                IReadOnlyDictionary<string, string>? ItemDescs = null)
{
    /// <summary>Без текстов (имя предмета — его тип).</summary>
    public static readonly StackTexts Empty = new(new Dictionary<string, string>(), new Dictionary<string, string>());

    public static StackTexts Load(string rulesDir) => Load(ContentSource.Open(rulesDir));

    public static StackTexts Load(ContentSet c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(
            c.Items.ToDictionary(i => i.Key, i => i.NameText ?? "", StringComparer.OrdinalIgnoreCase),
            c.LeaderSkills.GroupBy(s => c.KeyOf(s.Modifier), StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.First().NameText ?? "", StringComparer.OrdinalIgnoreCase),
            c.Items.ToDictionary(i => i.Key, i => i.DescriptionText ?? "", StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Окно отряда на карте (<c>DLG_MANAGE_STACK</c>, <c>CManageStkInterf</c> <c>0x4965a7</c>; открывается отпусканием мыши на
/// портрете героя панели команд, <c>0x49072c</c>). Портрет, имя, <c>X005TA0400</c> (скорость, лидерство
/// <c>X005TA0575</c>, обзор), навыки героя (<c>LBOX_ITEM_ICON</c>, заголовок <c>X005TA0185</c>, строка <c>X005TA0940</c>),
/// воины в ячейках от <c>IMG_SLOT1</c> (<see cref="UnitGroupLayout"/>, перетаскивание — перестановка), кукла героя
/// <c>DLG_PAPERDOLL</c> на месте <c>IMG_PAPERDOLL_BG</c> (семь слотов) и сумка <c>MLBOX_INVENTORY</c>. Предметы
/// перетаскиваются: из сумки в слот — надеть (<see cref="Play.GameSession.EquipItem"/>, отказы <c>X005TA0601</c>… по
/// <see cref="EquipRefusal"/>), из слота в сумку — снять, зелье из сумки на воина — выпить
/// (<see cref="Play.GameSession.DrinkPotion"/>).
/// Отпуск воина и «Остановить продвижение» — инструменты, как в окне армии (<see cref="UnitOrders"/>).
/// </summary>
public sealed class ManageStackLogic : IWindowLogic
{
    public const string Screen = "DLG_MANAGE_STACK";
    public const string Group = "S";
    private const string Anchor = "IMG_SLOT1", Bag = "MLBOX_INVENTORY", Skills = "LBOX_ITEM_ICON", ItemArchive = "IconItem";

    /// <summary>Слоты куклы по номеру слота снаряжения (<see cref="Equipment.SlotBanner"/>…).</summary>
    public static readonly string[] SlotImages =
        ["IMG_BANNER", "IMG_TOME", "IMG_BATTLE1", "IMG_BATTLE2", "IMG_ARTIFACT1", "IMG_ARTIFACT2", "IMG_BOOTS"];

    private readonly WindowContext _c;
    private readonly IReadOnlyList<UiScreen> _screens;
    private readonly StackTexts _texts;
    private readonly UiRequests _requests = new();
    private UiRect _first, _bag;
    private UiList _bagSpec = new();
    private Dictionary<int, UiRect> _slots = [];

    /// <summary>Что тянут мышью: воина (позиция), предмет из сумки (номер среди видимых в сумке) или из слота.</summary>
    private (char Kind, int Index)? _drag;

    /// <summary>Воин под нажатием при включённом инструменте.</summary>
    private int? _toolPress;

    /// <summary>Инструмент: отпуск (<c>TOG_DISMISS</c>) или «Остановить продвижение» (<c>TOG_DYNLEVEL</c>, <c>0x49686f</c>).</summary>
    public CityTool Tool { get; private set; }

    public ManageStackLogic(WindowContext context, string stackId, IReadOnlyList<UiScreen> screens, StackTexts texts)
    {
        _c = context ?? throw new ArgumentNullException(nameof(context));
        StackId = stackId;
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

    public string StackId { get; }

    public string? LastRefusal { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    private MapStack Stack => _c.Map.Stacks[StackId];

    /// <summary>Предметы в сумке — всё, что несёт отряд, кроме надетого.</summary>
    public IReadOnlyList<string> BagItems
    {
        get
        {
            var worn = Equipment.Slots(Stack).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return [.. Stack.Items.Where(i => !worn.Contains(i))];
        }
    }

    /// <summary>Сумка по ячейкам: одинаковые предметы — одна ячейка со счётчиком (<see cref="ItemCells"/>).</summary>
    public IReadOnlyList<ItemCell> BagCells => ItemCells.Fold(BagItems, _c.Map);

    public string Summary
    {
        get
        {
            var slots = Equipment.Slots(Stack);
            string doll = string.Concat(slots.Select(s => s.Length > 0 ? 'x' : '.'));
            var cells = _c.Session.GroupCells(StackId);
            string group = cells is null ? "------" : new string([.. cells.Cells.Select(id => id.Length == 0 ? '.' : 'x')]);
            return $"отряд={StackId} S={group} кукла={doll} сумка={BagItems.Count} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";
        }
    }

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _first = screen.Elements.First(e => e.Id == Anchor).Rect;
        var bag = screen.Elements.First(e => e.Id == Bag);
        (_bag, _bagSpec) = (bag.Rect, bag.List ?? new UiList());
        var shaped = UnitGroupLayout.Expand(screen, Anchor, Group, leftSide: true);
        // Кукла — дочернее окно DLG_PAPERDOLL в точке IMG_PAPERDOLL_BG (сам элемент движок прячет).
        var doll = _screens.FirstOrDefault(s => s.Id == "DLG_PAPERDOLL");
        if (doll is null) return shaped;
        var at = screen.Elements.First(e => e.Id == "IMG_PAPERDOLL_BG").Rect;
        var elements = shaped.Elements.Where(e => e.Id != "IMG_PAPERDOLL_BG").ToList();
        var added = new List<UiElement>
        {
            new() { Id = "IMG_PAPERDOLL", Kind = UiKind.Image, Rect = new UiRect(at.X, at.Y, doll.Design.Width, doll.Design.Height), Image = doll.Background },
        };
        added.AddRange(doll.Elements.Select(e => e with { Rect = e.Rect with { X = e.Rect.X + at.X, Y = e.Rect.Y + at.Y } }));
        _slots = SlotImages.Select((id, slot) => (id, slot)).Where(p => added.Any(e => e.Id == p.id))
                           .ToDictionary(p => p.slot, p => added.First(e => e.Id == p.id).Rect);
        elements.InsertRange(0, added);
        ItemFrames.Insert(elements, [.. SlotImages, Bag]);   // рамки предметов (CDDBaseEquip, CDDFoldedInv)
        return shaped with { Elements = elements };
    }

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Resume(UiScreenState ui) => Refresh(ui);

    /// <summary>«Бросить предмет», отпуск воина и «Остановить продвижение» делает сама логика.</summary>
    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_PICK_UP_DROP" or "TOG_DISMISS" or "TOG_DYNLEVEL");

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiToggled { Element: "TOG_DISMISS" } dismiss) Tool = dismiss.Checked ? CityTool.Dismiss : CityTool.None;
        if (e is UiToggled { Element: "TOG_DYNLEVEL" } grow) Tool = grow.Checked ? CityTool.StopGrowth : CityTool.None;
        // «Бросить предмет» (0x497b95): окно закрывается, на карте — выбор клетки рядом с отрядом.
        if (e is UiClicked { Element: "BTN_PICK_UP_DROP" } && CanDrop)
        {
            _requests.Add(new UiClose());
            _requests.Add(new UiDrop(StackId));
            return;
        }
        Refresh(ui);
    }

    /// <summary>«Бросить предмет» доступна, если сумка не пуста и отряд не в объекте (<c>0x496a4f</c>).</summary>
    private bool CanDrop => BagItems.Count > 0 && Stack.Inside.Length == 0;

    public void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _drag = null;
        _toolPress = null;
        if (Tool != CityTool.None)
        {
            _toolPress = CellAt(x, y);
            return;
        }
        if (CellAt(x, y) is { } p && _c.Session.GroupCells(StackId) is { } cells && cells.Cells[p].Length > 0)
            _drag = ('u', cells.PositionOf(cells.Cells[p]));
        else if (BagAt(x, y, ui) is { } i) _drag = ('b', i);
        else if (SlotAt(x, y) is { } s && Equipment.Slots(Stack)[s].Length > 0) _drag = ('s', s);
        Frames(ui);   // начало перетаскивания — рамки у мест броска (0x56c58a)
    }

    /// <summary>Рамки допустимости группы (<see cref="DropFrames"/>): инструмент, тащимый воин или зелье из сумки.</summary>
    private void Frames(UiScreenState ui)
    {
        var cells = _c.Session.GroupCells(StackId);
        Func<int, DropFrame>? state = cells is null ? null : (Tool, _drag) switch
        {
            (CityTool.Dismiss, _) => p => DropFrames.Dismiss(_c, StackId, cells, p, garrison: false),
            (CityTool.StopGrowth, _) => p => DropFrames.StopGrowth(_c, cells, p),
            (_, ('u', var from)) => p => DropFrames.Unit(_c.Session, StackId, from, StackId, p),
            (_, ('b', var i)) when i < BagCells.Count && BagCells[i].Top is var item => p => DropFrames.Potion(_c, StackId, item, cells, p),
            (_, ('s', var s)) when Equipment.Slots(Stack)[s] is { Length: > 0 } worn => p => DropFrames.Potion(_c, StackId, worn, cells, p),
            _ => null,
        };
        UnitCellsView.Frames(ui, Group, cells, state);
        // Предмет из сумки или слота: рамки слотов куклы и сумки (ItemFrames); у инструмента и воина их нет.
        (string Item, int? From)? dragged = Tool != CityTool.None ? null : _drag switch
        {
            ('b', var i) when i < BagCells.Count => (BagCells[i].Top, null),
            ('s', var s) when Equipment.Slots(Stack)[s] is { Length: > 0 } worn => (worn, s),
            _ => null,
        };
        ItemFrames.Slots(ui, SlotImages, dragged is { } d ? s => ItemFrames.Slot(_c.Session, StackId, d.Item, s, d.From) : null);
        ItemFrames.List(ui, Bag, dragged is { From: not null });
    }

    /// <summary>Примет ли место тащимое: воин — ячейка своей группы; предмет сумки — слот куклы или воин с рамкой (зелье, <c>0x56eace</c>);
    /// слот — сумка, другой слот или воин с рамкой; ничего не тащат — null.</summary>
    public bool? DropAccepts(int x, int y, UiScreenState ui) => _drag switch
    {
        null => null,
        { Kind: 'u' } => CellAt(x, y) is not null,
        { Kind: 'b' } d => d.Index < BagCells.Count && (SlotAt(x, y) is { } s && SlotTakes(BagCells[d.Index].Top, s, null)
                                                         || CellAt(x, y) is { } p && Takes(BagCells[d.Index].Top, p)),
        { Kind: 's' } d => Inside(_bag, x, y) || Equipment.Slots(Stack)[d.Index] is { Length: > 0 } worn
                           && (SlotAt(x, y) is { } s && s != d.Index && SlotTakes(worn, s, d.Index) || CellAt(x, y) is { } p && Takes(worn, p)),
        _ => false,
    };

    /// <summary>Примет ли слот куклы предмет: рамка не «нет» (<see cref="ItemFrames.Slot"/>, <c>0x56eace</c>) — слот того рода.</summary>
    private bool SlotTakes(string item, int slot, int? from) => ItemFrames.Slot(_c.Session, StackId, item, slot, from) != DropFrame.None;

    /// <summary>Примет ли воин позиции <paramref name="position"/> зелье: рамка не «нет» (<see cref="DropFrames.Potion"/>).</summary>
    private bool Takes(string item, int position) =>
        _c.Session.GroupCells(StackId) is { } cells && DropFrames.Potion(_c, StackId, item, cells, position) != DropFrame.None;

    /// <summary>Кадр тащимого (<see cref="IWindowLogic.DragImage"/>): портрет воина или значок предмета сумки и куклы.</summary>
    public string? DragImage => _drag switch
    {
        ('u', var p) when _c.Session.GroupCells(StackId) is { } cells => ItemCells.Face(cells.Cells[p], _c),
        ('b', var i) when i < BagCells.Count => ItemCells.Icon(BagCells[i].Top, _c),
        ('s', var i) when Equipment.Slots(Stack)[i] is { Length: > 0 } worn => ItemCells.Icon(worn, _c),
        _ => null,
    };

    /// <summary>Правая кнопка на воине — карточка, пока кнопка держится.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        if (UnitCards.For(_c, StackId, CellAt(x, y)) is { } open) _requests.Add(open);
        else if (SiteLogicLists.At(ui, Skills, SkillRows(Stack).Count, x, y) is { } k) _requests.Add(HeroSkills.Card(_c, SkillRows(Stack)[k].Text));
        else if (BagAt(x, y, ui) is { } b && ItemCards.For(_c, BagCells[b].Top, _texts) is { } bag) _requests.Add(bag);
        else if (SlotAt(x, y) is { } s && Equipment.Slots(Stack)[s] is { Length: > 0 } worn && ItemCards.For(_c, worn, _texts) is { } slot)
            _requests.Add(slot);
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var drag = _drag;
        _drag = null;
        if (Tool != CityTool.None)
        {
            // Инструмент отжимается после одного воина, как в окне армии.
            if (CellAt(x, y) is { } at && at == _toolPress && _c.Session.GroupCells(StackId) is { } cells && cells.Cells[at] is { Length: > 0 } unit)
            {
                LastRefusal = Tool == CityTool.Dismiss
                    ? UnitOrders.Dismiss(_c, _requests, StackId, unit, r => LastRefusal = r)
                    : UnitOrders.StopGrowth(_c, _requests, unit, r => LastRefusal = r);
                Tool = CityTool.None;
            }
            _toolPress = null;
            Refresh(ui);
            return;
        }
        if (drag is not { } d) return;
        switch (d.Kind)
        {
            case 'u' when CellAt(x, y) is { } to && to != d.Index:
                LastRefusal = _c.Session.Run<string?>(new MoveInGroupCommand(StackId, d.Index, BigAt(d.Index) ? to - (to % 2) : to));
                break;
            case 'b' when SlotAt(x, y) is { } slot && SlotTakes(BagCells[d.Index].Top, slot, null):
                Equip(BagCells[d.Index].Top, slot);
                break;
            case 'b' when CellAt(x, y) is { } cell:
                Drink(BagCells[d.Index].Top, cell);
                break;
            case 's' when Inside(_bag, x, y):
                _c.Session.Run(new UnequipItemCommand(StackId, d.Index));
                LastRefusal = null;
                break;
            case 's' when SlotAt(x, y) is { } slot && slot != d.Index && SlotTakes(Equipment.Slots(Stack)[d.Index], slot, d.Index):
                string item = Equipment.Slots(Stack)[d.Index];
                _c.Session.Run(new UnequipItemCommand(StackId, d.Index));
                Equip(item, slot);
                break;
            case 's' when CellAt(x, y) is { } cell && Equipment.Slots(Stack)[d.Index] is { Length: > 0 } worn:
                Drink(worn, cell);   // надетое зелье боевого слота — тоже IMidItemExt (CDDEquipment)
                break;
        }
        Refresh(ui);
    }

    private void Equip(string item, int slot)
    {
        var refusal = _c.Session.Run<EquipRefusal>(new EquipItemCommand(StackId, item, slot));
        LastRefusal = refusal == EquipRefusal.Ok ? null : refusal.ToString();
        if (RefusalText(refusal) is { } text) _requests.Add(new UiTell(_c.Text(text)));
    }

    /// <summary>Строка отказа надевания (<see cref="EquipRefusal"/> → <c>X005TA…</c>); надето — null.</summary>
    public static string? RefusalText(EquipRefusal refusal) => refusal switch
    {
        EquipRefusal.Ok => null,
        EquipRefusal.NeedWeaponArmorUse => "X005TA0601",
        EquipRefusal.NeedJewelryUse => "X005TA0602",
        EquipRefusal.NeedBannerUse => "X005TA0603",
        EquipRefusal.NeedOrbUse => "X005TA0696",
        EquipRefusal.NeedTalismanUse => "X005TA0697",
        EquipRefusal.NeedTravelItemUse => "X005TA0698",
        EquipRefusal.DeadLeader => "X005TA0651",
        EquipRefusal.NoEffect => "X005TA0810",
        _ => "X005TA0608",
    };

    /// <summary>Зелье на воина (<see cref="PotionDrops.Drink"/>), если позиция его примет.</summary>
    private void Drink(string item, int position)
    {
        if (!Takes(item, position)) return;
        LastRefusal = PotionDrops.Drink(_c, _requests, StackId, item, _c.Session.GroupCells(StackId)!.Cells[position]);
    }

    private void Refresh(UiScreenState ui)
    {
        var stack = Stack;
        var map = _c.Map;
        string leaderType = map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        ui.SetImage("IMG_LEADER_FACE", leaderType.Length > 0 ? _c.Faces.Large(leaderType) : null);
        var look = _c.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase));
        ui.SetText("TXT_NAME", look is { LeaderName.Length: > 0 } ? look.LeaderName : _c.UnitName(leaderType));
        var cells = _c.Session.GroupCells(StackId);
        int leadership = _c.Session.Leadership(stack);
        string lead = UiTemplate.Fill(_c.Text("X005TA0575"), ("USED", cells?.Occupied ?? 0), ("AVAIL", leadership));
        int scout = LastEdict.Rules.Map.Fog.Radius(stack, map, _c.Session.RequireBook().ScoutByType, fallback: 1,
                                            modifiers: _c.Session.RequireBook().Rules.Modifiers);
        ui.SetText("TXT_STACK_STATS", UiTemplate.Fill(_c.Text("X005TA0400"), ("MOVE1", stack.Move),
            ("MOVE2", _c.Session.DayAllowance(stack)), ("LEADERSHIP", lead), ("SCOUTRNG", scout)));
        ui.SetText("TXT_LEADER_ABILITY", _c.Text("X005TA0185"));
        ui.SetCards(Skills, [.. SkillRows(stack).Select(r => new UiCard($"Icons:{r.Icon}", new UiPoint(0, 0),
            UiTemplate.Fill(_c.Text("X005TA0940"), ("ABIL", _c.Text(r.Text))), new UiRect(36, 0, 158 - 36, 36)))]);
        ui.SetEnabled("BTN_PICK_UP_DROP", CanDrop);
        ui.SetChecked("TOG_DISMISS", Tool == CityTool.Dismiss);
        ui.SetChecked("TOG_DYNLEVEL", Tool == CityTool.StopGrowth);

        var slots = Equipment.Slots(stack);
        for (int s = 0; s < SlotImages.Length; s++)
            if (ui.Screen.Elements.Any(e => e.Id == SlotImages[s]))
                ui.SetImage(SlotImages[s], slots[s].Length > 0 && map.ItemTypes.TryGetValue(slots[s], out var type) ? $"{ItemArchive}:{ArtNames.Of(type)}" : null);
        int top = ui.ListTop(Bag);
        var bag = BagCells;
        ui.SetCards(Bag, [.. bag.Select(c => ItemCells.Card(c, _c, _bagSpec))]);
        if (top > 0 && bag.Count > 0) ui.Select(Bag, Math.Min(top, bag.Count - 1));   // прокрутка сумки остаётся
        UnitCellsView.Render(ui, Group, cells, _c, stack.Owner);
        Frames(ui);
    }

    /// <summary>Строки навыков героя — <see cref="HeroSkills.Rows"/>.</summary>
    public IReadOnlyList<(string Icon, string Text)> SkillRows(MapStack stack) => HeroSkills.Rows(_c, stack);

    private bool BigAt(int position) =>
        _c.Session.GroupCells(StackId) is { } cells && cells.Cells[position].Length > 0
        && string.Equals(cells.Cells[Formation.Pair(position)], cells.Cells[position], StringComparison.OrdinalIgnoreCase);

    private int? CellAt(int x, int y)
    {
        if (UnitGroupLayout.PositionAt(_first, leftSide: true, x, y) is not { } p) return null;
        return _c.Session.GroupCells(StackId) is { } cells && cells.Cells[p].Length > 0 ? cells.PositionOf(cells.Cells[p]) : p;
    }

    private int? SlotAt(int x, int y)
    {
        foreach (var (slot, r) in _slots)
            if (Inside(r, x, y)) return slot;
        return null;
    }

    /// <summary>Ячейка сумки под точкой (номер в <see cref="BagCells"/>): ячейки списка по разметке и прокрутка.</summary>
    private int? BagAt(int x, int y, UiScreenState ui)
    {
        if (!Inside(_bag, x, y) || _bagSpec.CellWidth <= 0) return null;
        int step = _bagSpec.CellWidth + _bagSpec.GapX;
        int col = (x - _bag.X) / step;
        if (x - _bag.X - (col * step) >= _bagSpec.CellWidth) return null;
        int index = ui.ListTop(Bag) + col;
        return index < BagCells.Count ? index : null;
    }

    private static bool Inside(UiRect r, int x, int y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
}
