using System.Globalization;
using LastEdict.Data.Menu;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Инструмент окна: лечение, воскрешение, отпуск или «Остановить продвижение» по щелчку на воине (<c>TOG_HEAL</c>,
/// <c>TOG_REVIVE</c>, <c>TOG_DISMISS</c>, <c>TOG_DYNLEVEL</c>).</summary>
public enum CityTool { None, Heal, Revive, Dismiss, StopGrowth }

/// <summary>
/// Окно армии укрепления (<c>DLG_CITY_STACK</c>, <c>CCityStackInterf</c> <c>0x4b0bcf</c>): слева отряд-гость
/// (<c>CDDStackGroup</c>), справа гарнизон (<c>CDDReinfGroup</c>), ячейки — <see cref="UnitGroupLayout"/>.
/// <list type="bullet">
/// <item>Перетаскивание левой кнопкой (<c>0x56e59a</c>): бросок на ячейку своей группы — перестановка
/// (<see cref="Play.GameSession.MoveInGroup"/>), чужой — обмен (<see cref="Play.GameSession.ExchangeUnits"/>).</item>
/// <item>Щелчок по пустой ячейке гарнизона — окно найма воина в эту ячейку (<c>0x5028b1</c>); по пустой левой половине,
/// когда гостя нет, — найм героя (<c>0x505146</c>). Оба — <c>DLG_HIRE_LEADER_2</c> (<see cref="HireLogic"/>).</item>
/// <item><c>TOG_HEAL</c>/<c>TOG_REVIVE</c> — инструмент, щелчок по воину спрашивает цену (<c>X005TA0227</c>, частичное
/// <c>X005TA0509</c>, воскрешение <c>X005TA0226</c>) и лечит одного воина (<c>0x50001f</c>, <c>0x5002fd</c>); после
/// щелчка по воину инструмент отжимается.</item>
/// <item><c>TOG_DISMISS</c> — инструмент отпуска (<c>CDismissUnit</c> <c>0x4f8662</c>): щелчок по воину — вопрос <c>X002TA0033</c>,
/// стража прогнать нельзя (<c>X005TA0450</c>), героя задания — тоже (<c>X005TA0883</c>); отпуск лидера распускает отряд
/// (<see cref="Play.GameSession.DismissUnit"/>). <c>TOG_DYNLEVEL</c> — «Остановить продвижение» (<see cref="UnitOrders.StopGrowth"/>) — и в
/// отряде, и в гарнизоне.</item>
/// <item><c>TXT_CITYNAME</c> — имя укрепления из сценария как есть, у столицы тоже (<c>0x4b1ec0</c>).</item>
/// <item><c>BTN_GROW_CITY</c> — рост деревни с вопросом <c>X005TA0216</c>.</item>
/// </list>
/// Маршрут и поведение — ответы пользователя 25.09 и разбор движка (<c>docs/ui-windows.md</c> §2).
/// </summary>
public sealed partial class CityStackLogic : IWindowLogic
{
    public const string Screen = "DLG_CITY_STACK";
    public const string Visitor = "L", Garrison = "R";
    private const string LeftAnchor = "IMG_LSLOT1", RightAnchor = "IMG_RSLOT1";
    private const string EmptyGroup = "IMG_LEMPTY", EmptyAdd = "IMG_LEMPTYADD", EmptyText = "TXT_LEMPTY", Add = "IMG_RADD";

    /// <summary>Подсказка пустой левой половины: «Щелкните мышкой, чтобы нанять героя и создать отряд» (<c>CDDUnitNoGroup</c>
    /// <c>0x573131</c>: две надписи <c>X005TA0159</c> размером с место группы, рисуются по его середине — <c>0x573610</c>).</summary>
    private const string EmptyHint = "X005TA0159";

    // Картинки пустых мест (0x5736aa, 0x502e6b): фон пустой группы 160×323 со сдвигом +7 по y, значок найма 28×43.
    private const int EmptyW = 160, EmptyH = 323, EmptyDy = 7, AddW = 28, AddH = 43, AddDx = 24, AddDy = 21;
    private const int GroupBgW = 350, GroupBgH = 386;   // DLG_CITY_STACK_GROUP_BG в точке IMG_PAPERDOLL_BG (0x4b1a29)

    private readonly WindowContext _c;
    private readonly IReadOnlyList<UiScreen> _screens;
    private readonly UiRequests _requests = new();
    private UiRect _left, _right;
    private (string Group, int Pos)? _pressed;
    private (string Group, int Pos)? _drag;
    private bool _pressedEmptyLeft;

    /// <param name="screens">Экраны набора — для куклы гостя (<c>DLG_PAPERDOLL</c>); нет — окно без куклы.</param>
    public CityStackLogic(WindowContext context, string fortId, IReadOnlyList<UiScreen>? screens = null)
    {
        _c = context ?? throw new ArgumentNullException(nameof(context));
        FortId = fortId;
        _screens = screens ?? [];
    }

    public string FortId { get; private set; }

    public CityTool Tool { get; private set; }

    /// <summary>Последний отказ действия (словами партии) — для проверок; null — отказа не было.</summary>
    public string? LastRefusal { get; private set; }

    private MapSite Fort => _c.Map.Sites.First(s => s.Id.Equals(FortId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Отряд-гость; нет — пусто.</summary>
    public string VisitorId => Fort.StackId;

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary
    {
        get
        {
            int guest = VisitorId.Length > 0 && _c.Session.GroupCells(VisitorId) is { } g ? g.Units.Count : 0;
            int garrison = _c.Session.GroupCells(FortId)?.Units.Count ?? 0;
            return $"город={FortId} имя={(FortName.Length > 0 ? FortName.Replace(' ', '_') : "-")} гость={guest} гарнизон={garrison} L={Cells(VisitorId)} R={Cells(FortId)} " +
                   $"сумка={BagItems.Count} склад={StoreItems.Count} кукла={(Paperdoll ? 1 : 0)} " +
                   $"золото={_c.Map.Players[Fort.Owner].Bank.Gold} инструмент={Tool} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";
        }
    }

    private string Cells(string groupId) => UnitCellsView.Line(_c, groupId);

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _left = screen.Elements.First(e => e.Id == LeftAnchor).Rect;
        _right = screen.Elements.First(e => e.Id == RightAnchor).Rect;
        var shaped = UnitGroupLayout.Expand(screen, LeftAnchor, Visitor, leftSide: true);
        shaped = UnitGroupLayout.Expand(shaped, RightAnchor, Garrison, leftSide: false);
        var elements = shaped.Elements.ToList();

        // Фон групп — под всем остальным (в разметке он стоит после портрета героя и ячеек).
        int bg = elements.FindIndex(e => e.Id == "IMG_PAPERDOLL_BG");
        var groupBg = elements[bg] with { Rect = elements[bg].Rect with { Width = GroupBgW, Height = GroupBgH } };
        elements.RemoveAt(bg);
        elements.Insert(0, groupBg);

        var area = UnitGroupLayout.Area(_left, leftSide: true);
        int cx = area.X + (area.Width / 2), cy = area.Y + (area.Height / 2);
        int after = elements.FindIndex(e => e.Id == LeftAnchor) + 1;
        elements.InsertRange(after,
        [
            new UiElement { Id = EmptyGroup, Kind = UiKind.Image, Rect = new(cx - (EmptyW / 2), cy - (EmptyH / 2) + EmptyDy, EmptyW, EmptyH) },
            new UiElement { Id = EmptyAdd, Kind = UiKind.Image, Rect = new(cx - (AddW / 2), cy - (AddH / 2), AddW, AddH) },
            new UiElement { Id = EmptyText, Kind = UiKind.Text, Rect = area },
        ]);
        after = elements.FindIndex(e => e.Id == RightAnchor) + 1;
        elements.InsertRange(after, Enumerable.Range(0, UnitGroupLayout.Positions).Select(p =>
        {
            var cell = UnitGroupLayout.Cell(_right, leftSide: false, p);
            return new UiElement { Id = Add + p, Kind = UiKind.Image, Rect = new(cell.X + AddDx, cell.Y + AddDy, AddW, AddH) };
        }));
        ShapeItems(elements, _screens);
        return shaped with { Elements = elements };
    }

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Resume(UiScreenState ui) => Refresh(ui);

    /// <summary>Рост спрашивает сама логика (вопрос с ценой), переход графа к окну сообщения не нужен; вывод героя закрывает
    /// окно сам (в графе у него переход на панель карты).</summary>
    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_EXIT_CITY" or "BTN_GROW_CITY");

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiToggled { Element: "TOG_HEAL" } t:
                Tool = t.Checked ? CityTool.Heal : CityTool.None;
                break;
            case UiToggled { Element: "TOG_REVIVE" } t:
                Tool = t.Checked ? CityTool.Revive : CityTool.None;
                break;
            case UiToggled { Element: "TOG_DISMISS" } t:
                Tool = t.Checked ? CityTool.Dismiss : CityTool.None;
                break;
            case UiToggled { Element: "TOG_DYNLEVEL" } t:
                Tool = t.Checked ? CityTool.StopGrowth : CityTool.None;
                break;
            case UiToggled { Element: "TOG_PAPERDOLL" } t:
                Paperdoll = t.Checked && VisitorId.Length > 0;
                break;
            case UiClicked { Element: "BTN_GROW_CITY" }:
                Grow();
                break;
            case UiClicked { Element: "BTN_EXIT_CITY" }:
                Leave();
                break;
            case UiClicked { Element: "BTN_NEXT_CITY" }:
                Step(+1);
                break;
            case UiClicked { Element: "BTN_PREV_CITY" }:
                Step(-1);
                break;
        }
        Refresh(ui);
    }

    public void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (Tool == CityTool.None && PressItems(x, y, ui))
        {
            _pressed = _drag = null;
            _pressedEmptyLeft = false;
            Frames(ui);   // зелье — рамки у воинов обеих групп (0x56c58a)
            return;
        }
        _pressed = CellAt(x, y);
        _drag = Tool == CityTool.None && _pressed is { } p && UnitAt(p) is not null ? p : null;
        _pressedEmptyLeft = VisitorId.Length == 0 && Inside(UnitGroupLayout.Area(_left, leftSide: true), x, y);
        Frames(ui);   // начало перетаскивания — рамки у всех мест броска (0x56c58a)
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ReleaseItems(x, y))
        {
            Refresh(ui);
            return;
        }
        var at = CellAt(x, y);
        var pressed = _pressed;
        var drag = _drag;
        bool emptyLeft = _pressedEmptyLeft;
        _pressed = _drag = null;
        _pressedEmptyLeft = false;

        if (Tool != CityTool.None)
        {
            // Инструмент отжимается после одного воина (ответ пользователя 25.09).
            if (at is { } t && t == pressed && UnitAt(t) is { } unit)
            {
                if (Tool == CityTool.Dismiss) LastRefusal = UnitOrders.Dismiss(_c, _requests, GroupId(t.Group)!, unit, Report);
                else if (Tool == CityTool.StopGrowth) LastRefusal = UnitOrders.StopGrowth(_c, _requests, unit, Report);
                else Care(unit);
                Tool = CityTool.None;
            }
        }
        else if (drag is { } from)
        {
            if (at is { } to && to != from) Drop(from, to);
        }
        else if (emptyLeft && VisitorId.Length == 0 && Inside(UnitGroupLayout.Area(_left, leftSide: true), x, y))
        {
            _requests.Add(new UiOpen(HireLogic.Screen, new HireLogic(_c, FortId, HireKind.Leader)));
        }
        else if (at is { Group: Garrison } g && g == pressed && UnitAt(g) is null)
        {
            _requests.Add(new UiOpen(HireLogic.Screen, new HireLogic(_c, FortId, HireKind.Unit, g.Pos)));
        }
        Refresh(ui);
    }

    /// <summary>Примет ли место под мышью тащимое: предмет — список или слот куклы, куда его можно бросить; воин — ячейка своей группы
    /// или обмен, который пройдёт (<see cref="Play.GameSession.ExchangeRefusal"/>).</summary>
    public bool? DropAccepts(int x, int y, UiScreenState ui)
    {
        if (ItemDropAccepts(x, y) is { } item) return item;
        if (_drag is not { } from) return null;
        if (CellAt(x, y) is not { } to) return false;
        if (to.Group == from.Group) return true;
        if (GroupId(from.Group) is not { Length: > 0 } fromId || GroupId(to.Group) is not { Length: > 0 } toId || UnitAt(from) is not { } unit) return false;
        int target = IsBig(from.Group, unit) ? Formation.ColumnFront(to.Pos) : to.Pos;
        return _c.Session.ExchangeRefusal(fromId, from.Pos, toId, target) is null;
    }

    // --- действия ---

    /// <summary>Бросок перетаскиваемого воина на ячейку. Большой воин встаёт в колонку целиком, поэтому целью берётся её
    /// передняя клетка; клетка большого воина-цели — его позиция.</summary>
    private void Drop((string Group, int Pos) from, (string Group, int Pos) to)
    {
        if (GroupId(from.Group) is not { Length: > 0 } fromId || GroupId(to.Group) is not { Length: > 0 } toId) return;
        if (UnitAt(from) is not { } unit) return;
        int target = to.Pos;
        if (IsBig(from.Group, unit)) target = Formation.ColumnFront(target);
        LastRefusal = from.Group == to.Group
            ? _c.Session.Run<string?>(new MoveInGroupCommand(fromId, from.Pos, target))
            : _c.Session.Run<string?>(new SwapUnitsCommand(fromId, from.Pos, toId, target));
    }

    private void Care(string unitId)
    {
        var book = _c.Session.RequireBook();
        var unit = _c.Map.Units[unitId];
        var player = _c.Map.Players[Fort.Owner];
        if (!book.Treasury.Units.TryGetValue(unit.TypeId, out var costs)) { LastRefusal = $"нет цен типа {unit.TypeId}"; return; }
        bool temple = Healing.HasTemple(player.Buildings, book.Economy.Buildings);
        long gold = player.Bank.Gold;
        string fort = FortId;
        if (Tool == CityTool.Revive)
        {
            if (unit.Hp > 0) return;
            if (!temple) { Tell("X005TA0231"); return; }
            long price = Treasury.RevivePrice(costs).Gold;
            if (gold < price) { Tell("X005TA0510"); return; }
            Ask(UiTemplate.Fill(_c.Text("X005TA0226"), ("PRICE", price)), () => Report(_c.Session.Run<string?>(new ReviveUnitCommand(fort, unitId))));
            return;
        }
        if (unit.Hp <= 0) return;
        int max = _c.Session.HpMax(unit);
        if (unit.Hp >= max) { Tell("X005TA0594"); return; }
        if (!temple) { Tell("X005TA0229"); return; }
        long full = costs.Heal.Gold * (max - unit.Hp);
        if (gold <= 0) { Tell("X005TA0510"); return; }
        string text = gold >= full
            ? UiTemplate.Fill(_c.Text("X005TA0227"), ("PRICE", full))
            : UiTemplate.Fill(_c.Text("X005TA0509"), ("PRICE", full), ("AMOUNT", gold));
        Ask(text, () => Report(_c.Session.Run<string?>(new HealUnitCommand(fort, unitId))));
    }

    private void Grow()
    {
        var fort = Fort;
        if (fort.Kind != SiteKind.Village) return;
        var (check, level, price) = GrowCheck(fort);
        string id = FortId;
        if (check == CityGrowCheck.Allowed)
            Ask(UiTemplate.Fill(_c.Text("X005TA0216"), ("LEVEL", level), ("COST", price)), () =>
            {
                var done = _c.Session.Run<CityGrowCheck>(new GrowCityCommand(id));
                Report(done == CityGrowCheck.Allowed ? null : done.ToString());
            });
        else if (check == CityGrowCheck.NotEnoughBank)
            Tell(UiTemplate.Fill(_c.Text("X005TA0217"), ("LEVEL", level), ("COST", price)), raw: true);
        else LastRefusal = check.ToString();
    }

    /// <summary>Можно ли растить деревню, до какого уровня и почём (<see cref="Play.GameSession.GrowCity"/> считает так же).</summary>
    private (CityGrowCheck Check, int Level, long Price) GrowCheck(MapSite village)
    {
        var book = _c.Session.RequireBook();
        var player = _c.Map.Players[village.Owner];
        int size = village.Size ?? 1;
        var price = Treasury.CityGrowthPrice(book.Economy, player.RaceId, size, book.LordCategory(player.LordId));
        var check = Fortifications.CheckGrow(size, village.RiotTurn ?? 0, village.GrowthTurn ?? 0, _c.Map.Turn, player.Bank, price);
        return (check, size + 1, price.Gold);
    }

    /// <summary>Соседнее своё укрепление (<c>BTN_PREV_CITY</c>/<c>BTN_NEXT_CITY</c>): столица, затем деревни по id.</summary>
    private void Step(int direction)
    {
        var forts = OwnForts();
        int i = forts.FindIndex(f => f.Equals(FortId, StringComparison.OrdinalIgnoreCase));
        if (forts.Count < 2 || i < 0) return;
        FortId = forts[(i + direction + forts.Count) % forts.Count];
        Tool = CityTool.None;
    }

    private List<string> OwnForts() =>
        [.. _c.Map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.Owner.Equals(Fort.Owner, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(s => s.Kind == SiteKind.Capital ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id)];

    /// <summary>«Вывести героя из города»: гость выходит к входу (<see cref="Play.GameSession.LeaveFort"/>), окно закрывается,
    /// отряд выделяется на карте (<c>0x406e8e</c>).</summary>
    private void Leave()
    {
        string stack = VisitorId;
        if (stack.Length == 0) return;
        Report(_c.Session.Run<string?>(new LeaveFortCommand(stack)));
        if (LastRefusal is not null) return;
        _requests.Add(new UiClose());
        _requests.Add(new UiFocus(stack));
    }

    /// <summary>Итог действия после «Да»: отказ (партия передумала после вопроса) — сообщением, не молча.</summary>
    private void Report(string? refusal)
    {
        LastRefusal = refusal;
        if (refusal is not null) _requests.Add(new UiTell(refusal));
    }

    private void Ask(string text, Action yes) => _requests.Add(new UiAsk(text, yes));

    /// <summary>Имя укрепления из сценария; нет — пусто.</summary>
    public string FortName =>
        _c.Session.Map.Sites.FirstOrDefault(f => f.Id.Equals(FortId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";

    private void Tell(string textOrId, bool raw = false) => _requests.Add(new UiTell(raw ? textOrId : _c.Text(textOrId)));

    // --- содержимое ---

    private void Refresh(UiScreenState ui)
    {
        var fort = Fort;
        var player = _c.Map.Players[fort.Owner];
        ui.SetImage("IMG_PAPERDOLL_BG", "DLG_CITY_STACK_GROUP_BG");
        ui.SetHidden("TOG_ENROLL", true);   // движок прячет его в конструкторе (0x4b0ff7)
        ui.SetChecked("TOG_DYNLEVEL", Tool == CityTool.StopGrowth);
        ui.SetText("TXT_CITYNAME", FortName);
        ui.SetChecked("TOG_HEAL", Tool == CityTool.Heal);
        ui.SetChecked("TOG_REVIVE", Tool == CityTool.Revive);
        ui.SetChecked("TOG_DISMISS", Tool == CityTool.Dismiss);
        ui.SetText("TXT_GOLD", player.Bank.Gold.ToString(CultureInfo.InvariantCulture));
        bool many = OwnForts().Count > 1;
        ui.SetEnabled("BTN_PREV_CITY", many);
        ui.SetEnabled("BTN_NEXT_CITY", many);
        // Роста у столицы нет — кнопки не видно (снимки армии столицы 28.09, четыре), у деревни — выключена, пока расти нельзя.
        ui.SetHidden("BTN_GROW_CITY", fort.Kind != SiteKind.Village);
        ui.SetEnabled("BTN_GROW_CITY", fort.Kind == SiteKind.Village && GrowCheck(fort).Check is CityGrowCheck.Allowed or CityGrowCheck.NotEnoughBank);
        ui.SetImage("IMG_CITY", CityPicture(fort, player));

        bool guest = Guest is not null;
        ui.SetEnabled("BTN_EXIT_CITY", guest);
        ui.SetImage(EmptyGroup, guest ? null : "DLG_CITY_STACK_NO_GROUPBNG");
        ui.SetImage(EmptyAdd, guest ? null : "DLG_CITY_STACK_ADDSTACK");
        ui.SetText(EmptyText, guest ? "" : _c.Text(EmptyHint));
        RefreshItems(ui, guest);
        if (guest && Paperdoll)
        {
            // Кукла вместо отряда (0x4b268c): портрет, имя и группа гостя убраны.
            ui.SetImage("IMG_LEADER", null);
            ui.SetText("TXT_LEFT_NAME", "");
        }
        else if (guest)
        {
            var stack = _c.Map.Stacks[VisitorId];
            string leaderType = _c.Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
            ui.SetImage("IMG_LEADER", leaderType.Length > 0 ? _c.Faces.Large(leaderType) : null);
            ui.SetText("TXT_LEFT_NAME", LeaderName(stack.Id, leaderType));
        }
        else
        {
            ui.SetImage("IMG_LEADER", null);
            ui.SetText("TXT_LEFT_NAME", "");
        }
        UnitCellsView.Render(ui, Visitor, guest && !Paperdoll ? _c.Session.GroupCells(VisitorId) : null, _c, fort.Owner);
        var garrison = _c.Session.GroupCells(FortId);
        UnitCellsView.Render(ui, Garrison, garrison, _c, fort.Owner, mirrored: true);   // адаптер гарнизона — флаг 0 (0x4b1d75)
        int free = _c.Session.GroupCapacity(FortId) - (garrison?.Occupied ?? 0);
        for (int p = 0; p < UnitGroupLayout.Positions; p++)
            ui.SetImage(Add + p, free > 0 && garrison is not null && garrison.Cells[p].Length == 0 ? "DLG_CITY_STACK_ADDSTACK" : null);
        Frames(ui);
    }

    /// <summary>Рамки допустимости обеих групп (<see cref="DropFrames"/>): тащат воина или нажат инструмент. Пустая половина гостя и кукла
    /// на её месте рамок не рисуют (<c>CDDEmptyGroup</c>).</summary>
    private void Frames(UiScreenState ui)
    {
        foreach (var (group, garrison) in (ReadOnlySpan<(string, bool)>)[(Visitor, false), (Garrison, true)])
        {
            string? id = group == Visitor && Paperdoll ? null : GroupId(group);
            var cells = id is null ? null : _c.Session.GroupCells(id);
            UnitCellsView.Frames(ui, group, cells, id is null || cells is null ? null : FrameState(id, cells, garrison));
        }
        ItemFramesRender(ui);
    }

    /// <summary>Состояние позиций группы <paramref name="groupId"/>: инструмент окна, тащимый воин или предмет (зелье); ничего — null.</summary>
    private Func<int, DropFrame>? FrameState(string groupId, UnitCells cells, bool garrison)
    {
        string owner = Fort.Owner;
        return Tool switch
        {
            CityTool.Heal => p => DropFrames.Care(_c, cells, p, owner, revive: false),
            CityTool.Revive => p => DropFrames.Care(_c, cells, p, owner, revive: true),
            CityTool.Dismiss => p => DropFrames.Dismiss(_c, groupId, cells, p, garrison),
            CityTool.StopGrowth => p => DropFrames.StopGrowth(_c, cells, p),
            _ when _drag is { } from && GroupId(from.Group) is { Length: > 0 } fromId => p => DropFrames.Unit(_c.Session, fromId, from.Pos, groupId, p),
            _ when Dragged is { } item => p => DropFrames.Potion(_c, item.Holder, item.Item, cells, p),
            _ => null,
        };
    }

    /// <summary>Картинка укрепления (<c>City.ff</c>): столица — по расе владельца (<c>AL{раса}0</c>), деревня — по уровню
    /// (<c>ALN{уровень}</c>). Буквы рас — по <c>Grace.RACE_TYPE</c>: 0 Империя H, 1 Орды U, 2 Легионы F, 3 Кланы D, 5 эльфы E.
    /// ponytail: ранг 2 — выбор картинки движком не разобран; буквы сопоставлены по составу архива.</summary>
    /// <summary>Картинка укрепления (<c>0x4b3d81</c>, имя <c>AL%c%d</c> — <c>0x5c56a1</c>): деревня уровня 1…5 — <c>ALN{уровень}</c>, столица
    /// (уровень 0) — буква расы владельца (<c>Lrace</c>: Империя H, Нежить U, Легионы E, Кланы D, эльфы F, иначе N); уровень вне
    /// 0…5 — картинки нет.</summary>
    private string? CityPicture(MapSite fort, MapPlayer player)
    {
        if (fort.Kind == SiteKind.Village) return fort.Size is >= 1 and <= 5 ? $"City:ALN{fort.Size}" : null;
        char race = _c.Session.RequireBook().Economy.Races.TryGetValue(player.RaceId, out var info) ? info.RaceType switch
        {
            0 => 'H', 1 => 'U', 2 => 'E', 3 => 'D', 5 => 'F', _ => 'N',
        } : 'N';
        return $"City:AL{race}0";
    }

    private string LeaderName(string stackId, string leaderType)
    {
        var look = _c.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stackId, StringComparison.OrdinalIgnoreCase));
        return look is { LeaderName.Length: > 0 } ? look.LeaderName : _c.UnitName(leaderType);
    }

    // --- ячейки ---

    private string? GroupId(string group) => group == Visitor ? (VisitorId.Length > 0 ? VisitorId : null) : FortId;

    /// <summary>Ячейка под точкой; клетка большого воина — его позиция (передняя клетка колонки).</summary>
    /// <summary>Курсор инструмента (<c>IsoCursr</c>): над воином, к которому инструмент применим, — <c>…VAL</c>, иначе <c>…INV</c>. Лечение — живой
    /// раненый, воскрешение — павший, отпуск — <see cref="Play.GameSession.DismissCheck"/> без отказа, «Остановить продвижение» — любой
    /// воин. Ранг 2 на условия: курсор движка по воину не разобран; что курсор есть и меняется — слова пользователя 14.10 (в порте не
    /// менялся вовсе).</summary>
    public string? ToolCursor(int x, int y, UiScreenState ui)
    {
        string? name = Tool switch
        {
            CityTool.Heal => "HEAL", CityTool.Revive => "REVIVE", CityTool.Dismiss => "DISMISS", CityTool.StopGrowth => "DYNLEVEL", _ => null,
        };
        if (name is null) return null;
        bool valid = CellAt(x, y) is { } cell && UnitAt(cell) is { } id && _c.Map.Units.TryGetValue(id, out var unit) && Tool switch
        {
            CityTool.Heal => unit.Hp > 0 && unit.Hp < _c.Session.HpMax(unit),
            CityTool.Revive => unit.Hp <= 0,
            CityTool.Dismiss => GroupId(cell.Group) is { } group && _c.Session.DismissCheck(group, id) == 0,
            _ => true,
        };
        return name + (valid ? "VAL" : "INV");
    }

    private (string Group, int Pos)? CellAt(int x, int y)
    {
        foreach (var (group, first, left) in (ReadOnlySpan<(string, UiRect, bool)>)[(Visitor, _left, true), (Garrison, _right, false)])
        {
            if (group == Visitor && Paperdoll) continue;   // на месте группы гостя — кукла
            if (UnitGroupLayout.PositionAt(first, left, x, y) is not { } p) continue;
            if (GroupId(group) is { } id && _c.Session.GroupCells(id) is { } cells && cells.Cells[p].Length > 0)
                p = cells.PositionOf(cells.Cells[p]);
            return (group, p);
        }
        return null;
    }

    private string? UnitAt((string Group, int Pos) cell) =>
        GroupId(cell.Group) is { } id && _c.Session.GroupCells(id) is { } cells && cells.Cells[cell.Pos].Length > 0
            ? cells.Cells[cell.Pos]
            : null;

    private bool IsBig(string group, string unitId) =>
        GroupId(group) is { } id && _c.Session.GroupCells(id) is { } cells && cells.PositionOf(unitId) is var p and >= 0
        && string.Equals(cells.Cells[Formation.Pair(p)], unitId, StringComparison.OrdinalIgnoreCase);

    private static bool Inside(UiRect r, int x, int y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
}
