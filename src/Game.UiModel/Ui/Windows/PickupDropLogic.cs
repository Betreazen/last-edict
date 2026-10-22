using D2.Data.Menu;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>Выбрать клетку рядом с отрядом, чтобы бросить мешок (<c>CTaskPickupDrop</c>, строка <c>X005TA0156</c>).</summary>
public sealed record UiDrop(string StackId) : UiRequest;

/// <summary>
/// Окно мешка (<c>DLG_PICKUP_DROP</c>, <c>CPickUpDropInterf</c> <c>0x49d216</c>; сервер открывает его командой <c>OpenBag</c> после
/// <c>CDropBagMsg</c>): <c>X005TA0634</c> «Выбросить вещи», <c>X005TA0635</c>; слева сумка отряда (<c>MLBOX_RINVENTORY</c>), справа
/// мешок на земле (<c>MLBOX_LINVENTORY</c>), перенос перетаскиванием в обе стороны (<see cref="Play.GameSession.ExchangeItem"/>).
/// «Назад» (<c>BTN_BACK</c> → <c>CCloseBagMsg</c>) закрывает; пустой мешок убирается (<see cref="Play.GameSession.CloseBag"/>).
/// Кукла героя (<c>TOG_PAPERDOLL</c>) — как у торговца (<see cref="PaperdollView"/> в точке <c>IMG_PAPERDOLL_BG</c>, портрет и имя
/// убираются): из сумки — на слот, со слота — в сумку, с земли на слот — сперва в сумку, со слота на землю — снять и положить.
/// ponytail: картинка земли <c>IMG_GROUND</c> — нет (откуда движок её берёт, не разобрано).
/// </summary>
public sealed class PickupDropLogic(WindowContext context, string stackId, string bagId, StackTexts texts,
                                    IReadOnlyList<UiScreen>? screens = null) : IWindowLogic
{
    public const string Screen = "DLG_PICKUP_DROP";
    private const string Bag = "MLBOX_RINVENTORY", Ground = "MLBOX_LINVENTORY", Doll = "IMG_PAPERDOLL_BG";

    /// <summary>Фон места отряда, пока кукла выключена (<c>0x49db06</c>, как у торговца); без него сквозь окно видна карта.</summary>
    private const string GroupBackground = "DLG_PICKUP_DROP_GROUP_BG";
    private readonly UiRequests _requests = new();
    private readonly PaperdollView _doll = new();
    private (char Kind, int Index)? _drag;

    /// <summary>Открыта кукла героя.</summary>
    public bool Paperdoll { get; private set; }

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var elements = screen.Elements.ToList();
        PaperdollView.Underlay(elements, Doll);
        _doll.Insert(elements, screens, Doll);
        return screen with { Elements = elements };
    }

    private string? Worn(int slot) => Equipment.Slots(Stack) is var worn && slot < worn.Count && worn[slot].Length > 0 ? worn[slot] : null;

    public string BagId => bagId;

    public string? LastRefusal { get; private set; }

    private MapStack Stack => context.Map.Stacks[stackId];

    public IReadOnlyList<string> StackItems =>
        [.. Stack.Items.Where(i => !Equipment.Slots(Stack).Contains(i, StringComparer.OrdinalIgnoreCase))];

    public IReadOnlyList<string> GroundItems => context.Session.ItemsOf(bagId) ?? [];

    /// <summary>Сумка отряда и мешок по ячейкам со счётчиком (<see cref="ItemCells"/>).</summary>
    public IReadOnlyList<ItemCell> StackCells => ItemCells.Fold(StackItems, context.Map);

    public IReadOnlyList<ItemCell> GroundCells => ItemCells.Fold(GroundItems, context.Map);

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"мешок={bagId} сумка={StackItems.Count} на_земле={GroundItems.Count} кукла={(Paperdoll ? 1 : 0)} " +
                             $"отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_TITLE", context.Text("X005TA0634"));
        ui.SetText("TXT_DESCRIPTION", context.Text("X005TA0635"));
        ui.SetEnabled("TOG_PAPERDOLL", _doll.Background is not null);
        Refresh(ui);
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_BACK";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_BACK" })
        {
            context.Session.Run(new CloseBagCommand(bagId));
            _requests.Add(new UiClose());
            return;
        }
        if (e is UiToggled { Element: "TOG_PAPERDOLL" } t) Paperdoll = t.Checked && _doll.Background is not null;
        Refresh(ui);
    }

    public void Press(int x, int y, UiScreenState ui)
    {
        _drag = null;
        if (SiteLogicLists.At(ui, Bag, StackCells.Count, x, y) is { } b) _drag = ('s', b);
        else if (SiteLogicLists.At(ui, Ground, GroundCells.Count, x, y) is { } g) _drag = ('g', g);
        else if (Paperdoll && _doll.SlotAt(x, y) is { } w && Worn(w) is not null) _drag = ('w', w);
        if (_drag is { } d)
        {
            string item = d.Kind == 's' ? StackCells[d.Index].Top : d.Kind == 'g' ? GroundCells[d.Index].Top : Worn(d.Index)!;
            string type = context.Map.ItemTypes.TryGetValue(item, out var t) ? t : item;
            ui.SetText("TXT_DESCRIPTION", @"\fMedBold;" + (texts.Items.TryGetValue(type, out var n) ? context.Text(n) : type) + @"\n\fNormal;"
                + (texts.ItemDescs is { } descs && descs.TryGetValue(type, out var desc) ? context.Text(desc) : ""));
        }
    }

    /// <summary>Примет ли место тащимый предмет: из сумки — земля или слот, с земли — сумка или слот, со слота — сумка или земля.</summary>
    public bool? DropAccepts(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (_drag is not { } d) return null;
        bool slot = Paperdoll && _doll.SlotAt(x, y) is not null;
        bool ground = SiteLogicLists.Inside(ui, Ground, x, y), bag = SiteLogicLists.Inside(ui, Bag, x, y);
        return d.Kind switch { 's' => ground || slot, 'g' => bag || slot, _ => bag || ground };
    }

    /// <summary>Кадр тащимого (<see cref="IWindowLogic.DragImage"/>): значок предмета сумки, земли или куклы (<c>CDDFoldedInv</c> <c>0x56d84d</c>).</summary>
    public string? DragImage => _drag switch
    {
        ('s', var i) when i < StackCells.Count => ItemCells.Icon(StackCells[i].Top, context),
        ('g', var i) when i < GroundCells.Count => ItemCells.Icon(GroundCells[i].Top, context),
        ('w', var i) when Worn(i) is { } worn => ItemCells.Icon(worn, context),
        _ => null,
    };

    /// <summary>Правая кнопка по предмету — его карточка.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        string? item = SiteLogicLists.At(ui, Bag, StackCells.Count, x, y) is { } b ? StackCells[b].Top
            : SiteLogicLists.At(ui, Ground, GroundCells.Count, x, y) is { } g ? GroundCells[g].Top
            : Paperdoll && _doll.SlotAt(x, y) is { } w ? Worn(w) : null;
        if (item is not null && ItemCards.For(context, item, texts) is { } card) _requests.Add(card);
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var drag = _drag;
        _drag = null;
        if (drag is not { } d) return;
        int? slot = Paperdoll ? _doll.SlotAt(x, y) : null;
        bool toGround = SiteLogicLists.Inside(ui, Ground, x, y), toBag = SiteLogicLists.Inside(ui, Bag, x, y);
        switch (d.Kind)
        {
            case 's' when toGround:
                LastRefusal = context.Session.Run<string?>(new ExchangeItemCommand(stackId, bagId, StackCells[d.Index].Top));
                break;
            case 'g' when toBag:
                LastRefusal = context.Session.Run<string?>(new ExchangeItemCommand(bagId, stackId, GroundCells[d.Index].Top));
                break;
            case 's' when slot is { } to:
                Equip(StackCells[d.Index].Top, to);
                break;
            case 'g' when slot is { } to:
                string lifted = GroundCells[d.Index].Top;
                LastRefusal = context.Session.Run<string?>(new ExchangeItemCommand(bagId, stackId, lifted));
                if (LastRefusal is null) Equip(lifted, to);
                break;
            case 'w' when toBag:
                context.Session.Run(new UnequipItemCommand(stackId, d.Index));
                LastRefusal = null;
                break;
            case 'w' when toGround && Worn(d.Index) is { } worn:
                LastRefusal = context.Session.Run<string?>(new ExchangeItemCommand(stackId, bagId, worn));
                break;
        }
        ui.SetText("TXT_DESCRIPTION", context.Text("X005TA0635"));
        Refresh(ui);
    }

    private void Equip(string item, int slot)
    {
        var refusal = context.Session.Run<EquipRefusal>(new EquipItemCommand(stackId, item, slot));
        LastRefusal = refusal == EquipRefusal.Ok ? null : refusal.ToString();
        if (ManageStackLogic.RefusalText(refusal) is { } text) _requests.Add(new UiTell(context.Text(text)));
    }

    private void Refresh(UiScreenState ui)
    {
        string type = context.Map.Units.TryGetValue(Stack.LeaderId, out var leader) ? leader.TypeId : "";
        ui.SetImage("IMG_LEADER", !Paperdoll && type.Length > 0 ? context.Faces.Large(type) : null);
        ui.SetText("TXT_LEADER_NAME", Paperdoll ? "" : context.UnitName(type));
        ui.SetChecked("TOG_PAPERDOLL", Paperdoll);
        ui.SetImage(Doll, Paperdoll ? _doll.Background : GroupBackground);
        PaperdollView.Render(ui, Paperdoll, Stack, context.Map);
        ui.SetText("TXT_GOLD", $"{context.Map.Players[Stack.Owner].Bank.Gold}");
        ui.SetCards(Bag, [.. StackCells.Select(c => ItemCells.Card(c, context, ItemCells.Spec(ui, Bag)))]);
        ui.SetCards(Ground, [.. GroundCells.Select(c => ItemCells.Card(c, context, ItemCells.Spec(ui, Ground)))]);
    }
}

/// <summary>Панель «Бросить предмет» (<c>DLG_DROP_BAG</c>, <c>0x48d688</c> режим 2) на месте панели команд, пока выбирается клетка:
/// <c>X005TA0156</c>, портрет героя отряда, «Закрыть» (Esc) — отмена. Та же панель — у выбора отряда для обмена (<c>X005TA0157</c>).</summary>
public sealed class DropBagPanelLogic(WindowContext context, string stackId, string textId = "X005TA0156") : IScreenLogic
{
    public const string Screen = "DLG_DROP_BAG";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_SPELL_INFO", context.Text(textId));
        string type = context.Map.Stacks.TryGetValue(stackId, out var stack) && context.Map.Units.TryGetValue(stack.LeaderId, out var leader)
            ? leader.TypeId : "";
        ui.SetImage("IMG_SPELL_ICON", type.Length > 0 ? context.Faces.Small(type) : null);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    /// <summary>«Закрыть» снимает режим хозяин.</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_CLOSE";
}

/// <summary>Пункты списков картинок под точкой — общее окон с предметами.</summary>
public static class SiteLogicLists
{
    public static int? At(UiScreenState ui, string id, int count, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var e = ui.Screen.Elements.First(el => el.Id == id);
        var spec = e.List ?? new UiList();
        var r = e.Rect;
        if (x < r.X || x >= r.Right || y < r.Y || y >= r.Bottom || spec.CellWidth <= 0 || spec.CellHeight <= 0) return null;
        int stepX = spec.CellWidth + spec.GapX, stepY = spec.CellHeight + spec.GapY;
        int col = (x - r.X) / stepX, row = (y - r.Y) / stepY;
        if (x - r.X - (col * stepX) >= spec.CellWidth || y - r.Y - (row * stepY) >= spec.CellHeight) return null;
        int columns = Math.Max(1, (r.Width + spec.GapX) / stepX);
        int index = ui.ListTop(id) + (row * columns) + col;
        return index < count ? index : null;
    }

    public static bool Inside(UiScreenState ui, string id, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var r = ui.Screen.Elements.First(e => e.Id == id).Rect;
        return x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
    }
}
