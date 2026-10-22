using D2.Rules.Map;
using D2.Data.Play.Commands;
using D2.Rules.Units;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Обмен между отрядами (<c>DLG_EXCHANGE</c>, <c>CExchangeInterf</c> <c>0x492cee</c>): слева выделенный отряд, справа тот, по которому
/// щёлкнули в режиме выбора (<see cref="Play.GameSession.ExchangeCheck"/>). Стороны устроены одинаково:
/// <list type="bullet">
/// <item><c>TXT_LEFT_NAME</c>/<c>TXT_RIGHT_NAME</c> — имя лидера; <c>IMG_LLEADER</c>/<c>IMG_RLEADER</c> — большой портрет, правый зеркально;
/// <c>TXT_LSTACK_STATS</c>/<c>TXT_RSTACK_STATS</c> — ход <c>X005TA0169</c> и лидерство <c>X005TA0166</c> (<c>X005TA0575</c>
/// «занято − 1 / лидерство − 1»), без «Отряд»; <c>LBOX_L_ITEM_ICON</c>/<c>LBOX_R_ITEM_ICON</c> — значки навыков героя
/// (<see cref="HeroSkills"/>); группа в <c>IMG_LSLOT1</c>/<c>IMG_RSLOT1</c> на фоне <c>DLG_EXCHANGE_GROUP_BGL</c>/<c>BGR</c>;
/// сумки <c>MLBOX_LINVENTORY</c>/<c>MLBOX_RINVENTORY</c> без надетого (<see cref="ItemCells"/>).</item>
/// <item>Воины — перетаскиванием (<c>CStackSwapUnitMsg</c>): на свою сторону — перестановка, на чужую — обмен
/// (<see cref="Play.GameSession.ExchangeUnits"/>); предметы — перетаскиванием между сумками (<c>CStackExchangeItemMsg</c>,
/// <see cref="Play.GameSession.ExchangeItem"/>).</item>
/// <item>Лидер любого отряда мёртв — при показе <c>X005TA0212</c>, воинов не обменять (отказ сессии).</item>
/// <item><c>TOG_PAPERDOLL</c> — куклы обоих героев на месте групп (портрет, имя, подпись и навыки убираются; правая кукла
/// зеркальна — слоты разметки стоят по зеркалу); предмет из сумки — на слот, со слота — в сумку.</item>
/// <item>Правая кнопка — карточка воина или предмета, пока держится; <c>BTN_CLOSE</c> — на карту.</item>
/// </list>
/// Разбор движка — <c>docs/ui-windows.md</c> §8.
/// </summary>
public sealed partial class ExchangeLogic : IWindowLogic
{
    public const string Screen = "DLG_EXCHANGE";
    public const string Left = "L", Right = "R";
    private const int BgW = 350, BgH = 386;   // фон групп и куклы — в точке IMG_?PAPERDOLL_BG

    private readonly WindowContext _c;
    private readonly IReadOnlyList<UiScreen> _screens;
    private readonly UiRequests _requests = new();
    private UiRect _leftFirst, _rightFirst;
    private (string Side, int Pos)? _drag;

    /// <param name="screens">Экраны набора — для фона куклы (<c>DLG_PAPERDOLL</c>); нет — кукла без фона.</param>
    public ExchangeLogic(WindowContext context, string leftStack, string rightStack, IReadOnlyList<UiScreen>? screens = null)
    {
        _c = context ?? throw new ArgumentNullException(nameof(context));
        LeftId = leftStack;
        RightId = rightStack;
        _screens = screens ?? [];
    }

    public string LeftId { get; }

    public string RightId { get; }

    /// <summary>Последний отказ действия (словами сессии) — для проверок; null — отказа не было.</summary>
    public string? LastRefusal { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary =>
        $"обмен L={LeftId} R={RightId} Lк={UnitCellsView.Line(_c, LeftId)} Rк={UnitCellsView.Line(_c, RightId)} " +
        $"сумкаL={BagItems(Left).Count} сумкаR={BagItems(Right).Count} кукла={(Paperdoll ? 1 : 0)} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    private string StackOf(string side) => side == Left ? LeftId : RightId;

    private MapStack? Stack(string side) => _c.Map.Stacks.TryGetValue(StackOf(side), out var s) ? s : null;

    /// <summary>Мёртв ли лидер отряда стороны (отряда нет — тоже).</summary>
    private bool LeaderDead(string side) =>
        Stack(side) is not { } s || !_c.Map.Units.TryGetValue(s.LeaderId, out var leader) || leader.Hp <= 0;

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _leftFirst = screen.Elements.First(e => e.Id == "IMG_LSLOT1").Rect;
        _rightFirst = screen.Elements.First(e => e.Id == "IMG_RSLOT1").Rect;
        var shaped = UnitGroupLayout.Expand(screen, "IMG_LSLOT1", Left, leftSide: true);
        shaped = UnitGroupLayout.Expand(shaped, "IMG_RSLOT1", Right, leftSide: false);
        var elements = shaped.Elements.ToList();
        // Фоны групп (и кукол) — под всем остальным, в размер картинки.
        foreach (string bg in (string[])["IMG_RPAPERDOLL_BG", "IMG_LPAPERDOLL_BG"])
        {
            int at = elements.FindIndex(e => e.Id == bg);
            var sized = elements[at] with { Rect = elements[at].Rect with { Width = BgW, Height = BgH } };
            elements.RemoveAt(at);
            elements.Insert(0, sized);
        }
        ShapeItems(elements);
        return shaped with { Elements = elements };
    }

    public void Open(UiScreenState ui)
    {
        if (LeaderDead(Left) || LeaderDead(Right)) _requests.Add(new UiTell(_c.Text("X005TA0212")));
        Refresh(ui);
    }

    public void Resume(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiToggled { Element: "TOG_PAPERDOLL" } t:
                Paperdoll = t.Checked;
                break;
            case UiClicked { Element: "BTN_CLOSE" }:
                _requests.Add(new UiClose());
                return;
        }
        Refresh(ui);
    }

    /// <summary>«Закрыть» закрывает сама логика (хозяин после закрытия перечитывает карту).</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_CLOSE";

    public void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _drag = null;
        if (!PressItems(x, y, ui)) _drag = CellAt(x, y) is { } cell && UnitAt(cell) is not null ? cell : null;
        Frames(ui);   // начало перетаскивания — рамки у обеих групп (0x56c58a)
    }

    /// <summary>Рамки допустимости обеих групп: тащат воина (<see cref="DropFrames.Unit"/>) или предмет (<see cref="DropFrames.Potion"/>); куклы
    /// на месте групп — рамок нет.</summary>
    private void Frames(UiScreenState ui)
    {
        foreach (string side in (string[])[Left, Right])
        {
            var cells = Paperdoll || Stack(side) is null ? null : _c.Session.GroupCells(StackOf(side));
            string to = StackOf(side);
            Func<int, DropFrame>? state = cells is null ? null
                : _drag is { } from ? p => DropFrames.Unit(_c.Session, StackOf(from.Side), from.Pos, to, p)
                : Dragged is { } item ? p => DropFrames.Potion(_c, item.Holder, item.Item, cells, p)
                : null;
            UnitCellsView.Frames(ui, side, cells, state);
        }
        ItemFramesRender(ui);
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!ReleaseItems(x, y, ui) && _drag is { } from && CellAt(x, y) is { } to && to != from) Drop(from, to);
        _drag = null;
        Refresh(ui);
    }

    /// <summary>Правая кнопка: на значке навыка — карточка навыка, на воине — его карточка, на предмете сумки или куклы — карточка предмета.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        foreach (string side in (string[])[Left, Right])
            if (!Paperdoll && Stack(side) is { } s && HeroSkills.Rows(_c, s) is var rows
                && SiteLogicLists.At(ui, $"LBOX_{side}_ITEM_ICON", rows.Count, x, y) is { } k)
            {
                _requests.Add(HeroSkills.Card(_c, rows[k].Text));
                return;
            }
        if (ItemAt(x, y, ui) is { } item)
        {
            if (ItemCards.For(_c, item) is { } card) _requests.Add(card);
            return;
        }
        if (CellAt(x, y) is { } cell && UnitCards.For(_c, StackOf(cell.Side), cell.Pos) is { } open) _requests.Add(open);
    }

    /// <summary>Примет ли место тащимое: предмет — чужая сумка, своя со слота, слот куклы; воин — ячейка своей стороны или обмен, который
    /// пройдёт (<see cref="Play.GameSession.ExchangeRefusal"/>).</summary>
    public bool? DropAccepts(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ItemDropAccepts(x, y, ui) is { } item) return item;
        if (_drag is not { } from) return null;
        if (CellAt(x, y) is not { } to) return false;
        if (to.Side == from.Side) return true;
        if (UnitAt(from) is not { } unit || _c.Session.GroupCells(StackOf(from.Side)) is not { } cells) return false;
        bool big = string.Equals(cells.Cells[Formation.Pair(cells.PositionOf(unit))], unit, StringComparison.OrdinalIgnoreCase);
        return _c.Session.ExchangeRefusal(StackOf(from.Side), from.Pos, StackOf(to.Side), big ? Formation.ColumnFront(to.Pos) : to.Pos) is null;
    }

    /// <summary>Бросок воина на ячейку: своя сторона — перестановка, чужая — обмен. Большой встаёт колонкой — цель его передняя клетка.</summary>
    private void Drop((string Side, int Pos) from, (string Side, int Pos) to)
    {
        if (UnitAt(from) is not { } unit || _c.Session.GroupCells(StackOf(from.Side)) is not { } cells) return;
        int target = to.Pos;
        if (string.Equals(cells.Cells[Formation.Pair(cells.PositionOf(unit))], unit, StringComparison.OrdinalIgnoreCase)) target = Formation.ColumnFront(target);
        LastRefusal = from.Side == to.Side
            ? _c.Session.Run<string?>(new MoveInGroupCommand(StackOf(from.Side), from.Pos, target))
            : _c.Session.Run<string?>(new SwapUnitsCommand(StackOf(from.Side), from.Pos, StackOf(to.Side), target));
    }

    private void Refresh(UiScreenState ui)
    {
        ui.SetChecked("TOG_PAPERDOLL", Paperdoll);
        foreach (string side in (string[])[Left, Right]) RefreshSide(ui, side);
        Frames(ui);
    }

    private void RefreshSide(UiScreenState ui, string side)
    {
        bool left = side == Left;
        var stack = Stack(side);
        string type = stack is not null && _c.Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        bool group = !Paperdoll && stack is not null;
        ui.SetImage($"IMG_{side}PAPERDOLL_BG", Paperdoll
            ? Mirror(_dollBackground ?? "DLG_PAPERDOLL_PAPERDOLL_BG", left)
            : left ? "DLG_EXCHANGE_GROUP_BGL" : "DLG_EXCHANGE_GROUP_BGR");
        ui.SetImage($"IMG_{side}LEADER", group && type.Length > 0 ? Mirror(_c.Faces.Large(type), left) : null);
        ui.SetText(left ? "TXT_LEFT_NAME" : "TXT_RIGHT_NAME", group ? LeaderName(stack!.Id, type) : "");
        ui.SetText($"TXT_{side}STACK_STATS", group ? Stats(stack!) : "");
        ui.SetCards($"LBOX_{side}_ITEM_ICON", group
            ? [.. HeroSkills.Rows(_c, stack!).Select(r => new UiCard($"Icons:{r.Icon}", new UiPoint(0, 0), null, new UiRect(0, 0, 0, 0)))]
            : []);
        // Правый отряд — отражён (адаптер с флагом 0, 0x493764), левый — нет.
        UnitCellsView.Render(ui, side, group ? _c.Session.GroupCells(stack!.Id) : null, _c, stack?.Owner ?? "", mirrored: !left);
        RefreshItems(ui, side, stack);
    }

    /// <summary>Правая сторона — зеркально (<c>0x492cee</c>: портрет и кукла отражены).</summary>
    private static string Mirror(string frame, bool left) => left ? frame : UiFrameRef.Mirrored(frame);

    /// <summary>Ход и лидерство (<c>X005TA0169</c> + <c>X005TA0166</c>), как у отряда в окне армии, но без строки «Отряд».</summary>
    private string Stats(MapStack stack)
    {
        var cells = _c.Session.GroupCells(stack.Id);
        string lead = UiTemplate.Fill(_c.Text("X005TA0575"), ("USED", (cells?.Occupied ?? 1) - 1), ("AVAIL", _c.Session.Leadership(stack) - 1));
        return UiTemplate.Fill(_c.Text("X005TA0169"), ("NOW", stack.Move), ("MAX", _c.Session.DayAllowance(stack)))
               + UiTemplate.Fill(_c.Text("X005TA0166"), ("QTY", lead));
    }

    private string LeaderName(string stackId, string leaderType)
    {
        var look = _c.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stackId, StringComparison.OrdinalIgnoreCase));
        return look is { LeaderName.Length: > 0 } ? look.LeaderName : _c.UnitName(leaderType);
    }

    /// <summary>Ячейка под точкой (клетка большого воина — его позиция); при кукле групп нет.</summary>
    private (string Side, int Pos)? CellAt(int x, int y)
    {
        if (Paperdoll) return null;
        foreach (var (side, first, left) in (ReadOnlySpan<(string, UiRect, bool)>)[(Left, _leftFirst, true), (Right, _rightFirst, false)])
        {
            if (UnitGroupLayout.PositionAt(first, left, x, y) is not { } p) continue;
            if (_c.Session.GroupCells(StackOf(side)) is { } cells && cells.Cells[p].Length > 0) p = cells.PositionOf(cells.Cells[p]);
            return (side, p);
        }
        return null;
    }

    private string? UnitAt((string Side, int Pos) cell) =>
        _c.Session.GroupCells(StackOf(cell.Side)) is { } cells && cells.Cells[cell.Pos].Length > 0 ? cells.Cells[cell.Pos] : null;
}
