using System.Collections.Generic;
using D2.Data.Ui.Windows;
using Godot;

namespace D2.Godot;

/// <summary>Обмен между отрядами (<c>BTN_EXCHANGE</c> панели команд, <c>CExchangeInterf</c> <c>0x492cee</c>): панель команд сменяется на
/// <c>DLG_DROP_BAG</c> с <c>X005TA0157</c>, карта ждёт щелчка по своему отряду рядом (<see cref="D2.Data.Play.GameSession.ExchangeCheck"/>);
/// над годной клеткой — курсор <c>EXCHANGE</c>. Годная клетка — окно <c>DLG_EXCHANGE</c> (<see cref="ExchangeLogic"/>: слева выделенный,
/// справа кликнутый); Esc или «Закрыть» — отмена. Под курсором — отметки <c>SELECT_YES/NO</c> на клетках блока 2×2
/// (<see cref="SpellAimView.TargetMarks"/>; годность — базовая <c>CTaskStrat</c>: свой отряд, <see cref="SpellAimView.OwnStackAt"/>).</summary>
public partial class MapScreen
{
    private const string ExchangeCursor = "EXCHANGE";
    private string? _exchangeStack;
    private SpellAim? _exchangeInput;
    private UiScreenView? _exchangePanel;
    private Node2D? _exchangeMarkNode;

    /// <summary>Отметки целей обмена последней клетки — для проверок.</summary>
    private IReadOnlyList<AimMark> _exchangeMarks = [];

    private void StartExchange()
    {
        if (_exchangeStack is not null || _dropStack is not null || Aiming || CoversMap || Windows is not { } context || _panelView is null) return;
        if (_map.SelectedStack is not { } stack || !_map.Session.CanExchange(stack.Id)) return;
        _exchangeStack = stack.Id;
        _exchangePanel = ReplacePanel(DropBagPanelLogic.Screen, new DropBagPanelLogic(context, stack.Id, "X005TA0157"), StopExchange);
        _exchangeInput = new SpellAim { Name = "ExchangeAim", MouseCell = MouseCell, Active = () => _exchangeStack is not null && _dialogViews.Count == 0 };
        _exchangeInput.Clicked += ExchangeAt;
        _exchangeInput.Cancelled += () => Callable.From(StopExchange).CallDeferred();
        _exchangeInput.Hovered += ExchangeHover;
        AddChild(_exchangeInput);
        _exchangeMarkNode = new Node2D { Name = "ExchangeMarks", ZIndex = 100 };
        _map.AddChild(_exchangeMarkNode);
        _map.PointerEnabled = false;   // щелчки — прицелу; колесо и стрелки двигают вид (MapView.PointerEnabled)
        GD.Print($"exchange start {stack.Id} at={stack.X},{stack.Y}");
    }

    private void StopExchange()
    {
        if (_exchangeStack is null) return;
        _exchangeStack = null;
        _exchangeInput?.QueueFree();
        _exchangeInput = null;
        _exchangeMarkNode?.QueueFree();
        _exchangeMarkNode = null;
        _exchangeMarks = [];
        if (_exchangePanel is { } panel) RestorePanel(panel);
        _exchangePanel = null;
        Cursor.Show(null);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    /// <summary>Мышь над клеткой: курсор и отметка цели.</summary>
    private void ExchangeHover((int X, int Y)? cell)
    {
        Cursor.Show(ExchangeCursorAt(cell));
        _exchangeMarks = cell is { } c && _exchangeStack is not null && _map.Viewer is { } viewer
            ? SpellAimView.TargetMarks(_map.Session, viewer, c.X, c.Y, (x, y) => SpellAimView.OwnStackAt(_map.Session, viewer, x, y)) : [];
        ShowMarks(_exchangeMarkNode, _exchangeMarks);
    }

    /// <summary>Курсор над клеткой: свой отряд рядом — <c>EXCHANGE</c>, иначе обычный.</summary>
    private string? ExchangeCursorAt((int X, int Y)? cell) =>
        cell is { } c && _exchangeStack is { } stack && _map.Session.ExchangeCheck(stack, c.X, c.Y, out _) is null ? ExchangeCursor : null;

    /// <summary>Клетка со своим отрядом рядом — окно обмена; иначе ждём дальше.</summary>
    private void ExchangeAt((int X, int Y) cell)
    {
        if (_exchangeStack is not { } stack || Windows is not { } context) return;
        string? refused = _map.Session.ExchangeCheck(stack, cell.X, cell.Y, out string other);
        GD.Print($"exchange at {cell.X},{cell.Y} {(refused ?? other).Replace(' ', '_')}");
        if (refused is not null) return;
        StopExchange();
        OpenWindow(ExchangeLogic.Screen, new ExchangeLogic(context, stack, other, GameData.Content.Screens));
    }
}
