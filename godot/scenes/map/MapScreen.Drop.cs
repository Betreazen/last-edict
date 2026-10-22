using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>«Бросить предмет» (<c>CTaskPickupDrop</c> <c>0x4cefa3</c>): окно отряда закрылось, панель команд сменилась на
/// <c>DLG_DROP_BAG</c> (<see cref="DropBagPanelLogic"/>), карта ждёт щелчка по клетке рядом с отрядом; над годной клеткой
/// (<see cref="LastEdict.Data.Play.GameSession.DropCheck"/>) — курсор <c>TAKEBAG</c> (центр — горячая точка, <c>0x5b27ee</c>); годная клетка —
/// мешок (новый или лежащий) и его окно <c>DLG_PICKUP_DROP</c>; Esc или «Закрыть» — отмена.</summary>
public partial class MapScreen
{
    private string? _dropStack;
    private SpellAim? _dropInput;
    private UiScreenView? _dropPanel;

    private void StartDrop(string stackId)
    {
        if (_dropStack is not null || Aiming || Windows is not { } context || _panelView is null) return;
        _dropStack = stackId;
        _dropPanel = ReplacePanel(DropBagPanelLogic.Screen, new DropBagPanelLogic(context, stackId), StopDrop);
        _dropInput = new SpellAim { Name = "DropAim", MouseCell = MouseCell, Active = () => _dropStack is not null && _dialogViews.Count == 0 };
        _dropInput.Clicked += DropAt;
        _dropInput.Cancelled += () => Callable.From(StopDrop).CallDeferred();
        _dropInput.Hovered += DropHover;
        AddChild(_dropInput);
        _map.PointerEnabled = false;   // щелчки — прицелу; колесо и стрелки двигают вид (MapView.PointerEnabled)
        var at = _map.Map.Stacks[stackId];
        GD.Print($"pickup start {stackId} at={at.X},{at.Y} {Text("X005TA0156").Replace(' ', '_')}");
    }

    private void StopDrop()
    {
        if (_dropStack is null) return;
        _dropStack = null;
        _dropInput?.QueueFree();
        _dropInput = null;
        if (_dropPanel is { } panel) RestorePanel(panel);
        _dropPanel = null;
        Cursor.Show(null);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    /// <summary>Курсор над клеткой: годная для мешка — <c>TAKEBAG</c>, иначе обычный.</summary>
    private void DropHover((int X, int Y)? cell)
    {
        bool ok = cell is { } c && _dropStack is { } stack && _map.Session.DropCheck(stack, c.X, c.Y, out _) is null;
        Cursor.Show(ok ? "TAKEBAG" : null);
    }

    /// <summary>Клетка для мешка: рядом и пустая — мешок и окно; иначе ждём дальше.</summary>
    private void DropAt((int X, int Y) cell)
    {
        if (_dropStack is not { } stack || Windows is not { } context) return;
        var (refused, bag) = _map.Session.Run<(string?, string)>(new LastEdict.Data.Play.Commands.DropBagCommand(stack, cell.X, cell.Y));
        GD.Print($"pickup at {cell.X},{cell.Y} {(refused ?? bag).Replace(' ', '_')}");
        if (refused is not null) return;
        _map.Session.Run(new LastEdict.Data.Play.Commands.ActInPlaceCommand(stack, Attack: false));   // бросок мешка на соседнюю клетку — команда хода с места (0x4415c8)
        StopDrop();
        _map.Reload();
        OpenWindow(PickupDropLogic.Screen, new PickupDropLogic(context, stack, bag, _stackTexts ??= StackTexts.Load(GameData.Rules), GameData.Content.Screens));
    }
}
