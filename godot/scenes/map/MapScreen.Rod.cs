using System;
using System.Collections.Generic;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>«Поставить жезл» (<c>BTN_DROP_ROD</c>, <see cref="DropRodLogic"/>): отказ кнопки — окно сообщения; иначе панель команд сменяется на
/// <c>DLG_DROP_BAG</c> с <c>X005TA0284</c>, курсор <c>DROPRODVAL</c>/<c>DROPRODINV</c> по клетке под мышью, отметки <c>SELECT_YES/NO</c> на
/// клетках блока 2×2 (годность — свой отряд, как у всех режимов <c>CTaskStrat</c>). Щелчок по годной клетке — путь до неё, второй по ней
/// же — вопрос с ценой, если сегодня доходим хотя бы до соседней с ней клетки (иначе — обычный ход); «Да» — отряд идёт, если клетка дальше соседней, и ставит жезл, когда
/// дошёл. Esc или «Закрыть» — отмена.</summary>
public partial class MapScreen
{
    private string? _rodStack;
    private SpellAim? _rodInput;
    private UiScreenView? _rodPanel;
    private Node2D? _rodMarkNode;
    private IReadOnlyList<AimMark> _rodMarks = [];
    private string _rodCursor = "-";

    /// <summary>Клетка, до которой показан путь (первый щелчок).</summary>
    private (int X, int Y)? _rodTarget;

    /// <summary>Жезл после хода: отряд и клетка — ставится, когда отряд встал на неё.</summary>
    private (string Stack, int X, int Y)? _rodAfterWalk;

    private void StartDropRod()
    {
        if (_rodStack is not null || _exchangeStack is not null || _dropStack is not null || Aiming || CoversMap || Windows is not { } context
            || _panelView is null || _map.SelectedStack is not { } stack) return;
        if (DropRodLogic.StartRefusal(context, stack.Id) is { } refusal)
        {
            GD.Print($"droprod refused {stack.Id} {refusal.Replace(' ', '_')}");
            OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(refusal, null));
            return;
        }
        _rodStack = stack.Id;
        _rodTarget = null;
        _rodPanel = ReplacePanel(DropBagPanelLogic.Screen, new DropBagPanelLogic(context, stack.Id, DropRodLogic.PanelText), StopDropRod);
        _rodInput = new SpellAim { Name = "RodAim", MouseCell = MouseCell, Active = () => _rodStack is not null && _dialogViews.Count == 0 };
        _rodInput.Clicked += RodAt;
        _rodInput.Cancelled += () => Callable.From(StopDropRod).CallDeferred();
        _rodInput.Hovered += RodHover;
        AddChild(_rodInput);
        _rodMarkNode = new Node2D { Name = "RodMarks", ZIndex = 100 };
        _map.AddChild(_rodMarkNode);
        _map.PointerEnabled = false;   // щелчки — прицелу; колесо и стрелки двигают вид (MapView.PointerEnabled)
        GD.Print($"droprod start {stack.Id} at={stack.X},{stack.Y}");
    }

    private void StopDropRod()
    {
        if (_rodStack is null) return;
        _rodStack = null;
        _rodTarget = null;
        _rodInput?.QueueFree();
        _rodInput = null;
        _rodMarkNode?.QueueFree();
        _rodMarkNode = null;
        _rodMarks = [];
        if (_rodPanel is { } panel) RestorePanel(panel);
        _rodPanel = null;
        Cursor.Show(null);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    /// <summary>Мышь над клеткой: курсор по годности клетки для жезла, отметки своих отрядов в блоке 2×2.</summary>
    private void RodHover((int X, int Y)? cell)
    {
        if (_rodStack is not { } stack || Windows is not { } context) return;
        _rodCursor = cell is { } c ? DropRodLogic.Cursor(context, stack, c.X, c.Y) : "-";
        Cursor.Show(cell is null ? null : _rodCursor);
        _rodMarks = cell is { } at && _map.Viewer is { } viewer
            ? SpellAimView.TargetMarks(_map.Session, viewer, at.X, at.Y, (x, y) => SpellAimView.OwnStackAt(_map.Session, viewer, x, y)) : [];
        ShowMarks(_rodMarkNode, _rodMarks);
    }

    /// <summary>Щелчок по клетке в режиме жезла (<c>0x4cdbeb</c>).</summary>
    private void RodAt((int X, int Y) cell)
    {
        if (_rodStack is not { } stackId || Windows is not { } context || !_map.Map.Stacks.TryGetValue(stackId, out var stack)) return;
        if (DropRodLogic.Refusal(context, stackId, cell.X, cell.Y) is { } refused)
        {
            GD.Print($"droprod at {cell.X},{cell.Y} отказ={refused.Replace(' ', '_')}");
            if (DropRodLogic.ClickMessage(context, stackId, cell.X, cell.Y) is { } message)
                OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(Text(message), null));
            else if (SpellAimView.OwnStackAt(_map.Session, _map.Viewer ?? "", cell.X, cell.Y))
            {
                // Свой отряд под щелчком — базовый щелчок режима его выбирает, и режим кончается (0x4d3f71 → CTaskSelectUnit).
                StopDropRod();
                _map.Click(cell.X, cell.Y);
            }
            return;
        }
        bool near = !DropRodLogic.NeedsWalk(stack, cell.X, cell.Y);
        if (_rodTarget != cell)
        {
            // Первый щелчок — путь до клетки, как у обычного хода (путь не строится — BEEP у движка, 0x4ccb8f).
            _rodTarget = cell;
            _map.Click(cell.X, cell.Y);
            GD.Print($"droprod path {cell.X},{cell.Y} точек={_map.Preview?.Points.Count ?? 0}");
            return;
        }
        // Жезл ставится с соседней клетки (MapView.WalkBeside): вопрос с ценой — уже когда сегодня доходим до клетки перед выбранной.
        bool today = DropRodLogic.AsksToday(_map.Preview, cell.X, cell.Y, near);
        if (!today)
        {
            GD.Print($"droprod walk {cell.X},{cell.Y} — сегодня не дойти, обычный ход");
            StopDropRod();
            _map.Click(cell.X, cell.Y);
            return;
        }
        OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(DropRodLogic.Question(context), () => PlaceRodAt(stackId, cell, near)));
        StopDropRod();
    }

    /// <summary>«Да» на вопрос: рядом — жезл сразу; дальше — ход по показанному пути и жезл, когда отряд встал на клетку.</summary>
    private void PlaceRodAt(string stackId, (int X, int Y) cell, bool near)
    {
        if (near)
        {
            _map.Session.Run(new LastEdict.Data.Play.Commands.ActInPlaceCommand(stackId, Attack: false));   // постановка с соседней клетки — команда хода с места (0x440cfc)
            RodPlaced(stackId, cell.X, cell.Y);
            return;
        }
        // Ожидание жезла ставится только после того, как отряд пошёл: выбор сам зовёт SyncPanels (Changed), и прежде RodAfterWalk снимал
        // ожидание раньше хода («не дошёл, жезла нет»), — отряд доходил, а жезла не было (замечание 11.10, п. 31). Отряд встаёт рядом с
        // клеткой и ставит жезл в неё (MapView.WalkBeside, 0x440cfc): прежде он вставал на саму клетку, и постановка отказывала — клетка занята.
        _map.Select(stackId);
        if (!_map.WalkBeside(stackId, cell.X, cell.Y))
        {
            GD.Print($"droprod walk {cell.X},{cell.Y} — ход не начался, жезла нет");
            return;
        }
        _rodAfterWalk = (stackId, cell.X, cell.Y);
        GD.Print($"droprod walk {cell.X},{cell.Y} — идёт, жезл по приходу");
    }

    /// <summary>Отряд дошёл (зовётся на каждое изменение карты): стоит на клетке жезла — поставить; ход кончился не там — жезла нет.</summary>
    private void RodAfterWalk()
    {
        if (_rodAfterWalk is not { } pending || _map.Walking) return;
        _rodAfterWalk = null;
        if (_map.Map.Stacks.TryGetValue(pending.Stack, out var stack) && !DropRodLogic.NeedsWalk(stack, pending.X, pending.Y))
            RodPlaced(pending.Stack, pending.X, pending.Y);
        else GD.Print($"droprod walk {pending.X},{pending.Y} — не дошёл, жезла нет");
    }

    private void RodPlaced(string stackId, int x, int y)
    {
        string? refused = _map.Session.Run<string?>(new LastEdict.Data.Play.Commands.PlaceRodCommand(stackId, x, y));
        GD.Print($"droprod placed {stackId} at={x},{y} {(refused is null ? "жезл" : "отказ=" + refused.Replace(' ', '_'))} " +
                 $"золото={_map.Map.Players[_map.Map.Stacks[stackId].Owner].Bank.Gold}");
        _map.RefreshGround();
        _map.Reload();
        SyncPanels();
    }

    /// <summary>Строка проверки режима жезла.</summary>
    private string RodLine() => $"жезл={(_rodStack is null ? "-" : _rodStack)} курсор={_rodCursor} отметка={MarksLine(_rodMarks)} " +
                                $"панель={(_rodPanel is not null ? DropBagPanelLogic.Screen : "-")}";
}
