using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Play;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пути своих отрядов через конец хода и «Продолжить движение» (<c>docs/map-path.md</c> §4, «Путь через конец хода»): интерфейс помнит путь за
/// каждым отрядом (<see cref="StackRoutes"/>), выбор отряда и начало дня показывают его снова. <c>BTN_CONT_MOVE</c> (клавиша M) — щелчок по
/// цели показанного пути (<c>0x48f451</c> → <c>CTaskSelectUnit</c> <c>0x4d2213</c> → <c>0x4d0e2c</c>).
/// </summary>
public partial class MapView
{
    private readonly StackRoutes _routes = new();

    /// <summary>Путь выбранного отряда — снова на карту (<see cref="StackRoutes.Show"/>). true — показан.</summary>
    private bool ShowRoute()
    {
        if (_selected is not { } id || !Map.Stacks.TryGetValue(id, out var stack) || !Owned(stack)
            || _routes.Show(Session, stack) is not { } shown) return false;
        (_preview, _target) = shown;
        return true;
    }

    /// <summary>
    /// После хода (и боя, которым он кончился) интерфейс снова показывает путь к той же цели с новой клетки, если его не сбросило
    /// (<see cref="ShowRoute"/>, <c>0x4cfead</c>): тогда один щелчок по цели ведёт дальше. Хозяин зовёт и после закрытия боя.
    /// </summary>
    public void RestoreShownPath()
    {
        if (Walking || !ShowRoute()) return;
        Marks();
        Changed?.Invoke();
    }

    /// <summary>Начало своего хода (после хода ИИ): выбор остаётся, путь выбранного отряда пересоздаётся с новым запасом очков — белые флажки
    /// вчерашнего дня становятся синими (<c>0x4d2089</c> → <c>0x4cc3b3</c>; журнал <c>20261019c-cov</c>: <c>path</c> seq 2161840 <c>last (8,58)</c> до
    /// конца хода, 2191363 <c>last (7,63)</c> после окна начала хода, щелчок 2192030 по концу (7,63) — сразу ход).</summary>
    private void RouteAtTurnStart()
    {
        _routes.Prune(Map.Stacks.ContainsKey);
        _preview = null;
        _target = null;
        ShowRoute();
    }

    /// <summary>
    /// Путь выбранного отряда стёрт (реестр Ин82, <c>0x4cc63b(1)</c> → <c>0x41a808</c>): «ОК» окна «расторгните альянс» (<c>0x4d201e</c>), «Нет» сносу
    /// жезла (<c>0x4d1f06</c>), итог своего вора «Разведать руины» (<c>0x4d23a2</c>, <see cref="LastEdict.Data.Ui.Windows.ThiefActionsLogic.DropsRoute"/>).
    /// Модель показанного пути сброшена (клетки <c>+0x14…+0x28</c> = −1: следующий щелчок — новый путь, не подтверждение), а слой флажков
    /// <c>0x4cd329</c> здесь не снимается (его снимают команда хода <c>0x4d22e0</c> и показ другого пути) — флажки до следующего показа меток.
    /// </summary>
    public void DropRoute()
    {
        if (_selected is not { } id) return;
        _routes.Forget(id);
        _preview = null;
        _target = null;
        GameLog.Verbose($"routedrop {id}");
        Changed?.Invoke();
    }

    /// <summary>Кнопка «Продолжить движение» доступна (<see cref="StackRoutes.CanContinue"/>, <c>0x4cfdff</c>). Показан ли путь, кнопка у движка не
    /// смотрит (<c>0x4d2346</c>) — это проверяет нажатие (<see cref="ContinueMove"/>, <c>0x4cc7b0</c>).</summary>
    public bool CanContinueMove =>
        !Walking && !AiRunning && _selected is { } id && Map.Stacks.TryGetValue(id, out var stack) && Owned(stack) && _routes.CanContinue(stack);

    /// <summary>«Продолжить движение» (<c>BTN_CONT_MOVE</c>, клавиша M; <c>0x48f451</c> → <c>0x4d2213</c>): путь показан (<c>0x4cc7b0</c>) и кнопка
    /// доступна — щелчок по цели показанного пути (<c>0x4cc7c2</c> → <c>0x4d0e2c</c>), то есть подтверждение: отряд идёт.</summary>
    public void ContinueMove()
    {
        if (!CanContinueMove || _preview is not { Points.Count: > 0 } || _target is not { } aim
            || !Map.Stacks.TryGetValue(_selected!, out var stack)) return;
        Command(stack, aim.X, aim.Y);
    }

    /// <summary>Имена меток последнего показа (<see cref="Marks"/>): эллипс и флажки пути — для строки проверки.</summary>
    private IReadOnlyList<string> _markNames = [];

    /// <summary>Строка проверки «route»: выбранный отряд, его клетка и очки хода, показанный путь (точек, дойдёт), флажки по видам (синие
    /// <c>MOVENORMAL</c>, белые <c>MOVEACTION</c>, красные <c>MOVEOUT</c>, меч/свиток), сколько отрядов помнят путь, помнит ли выбранный.</summary>
    public string RouteLine()
    {
        var stack = SelectedStack;
        int Count(string name) => _markNames.Count(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return $"выбран={_selected ?? "-"} в={(stack is null ? "-" : $"{stack.X},{stack.Y}")} ходы={stack?.Move ?? 0} " +
               $"путь={_preview?.Points.Count ?? 0} дойдёт={_preview?.Reachable ?? 0} цель={(_target is { } t ? $"{t.X},{t.Y}" : "-")} " +
               $"синих={Count("MOVENORMAL")} белых={Count("MOVEACTION")} красных={Count("MOVEOUT")} " +
               $"у_цели={Count("MOVEBATTLE") + Count("MOVENEGO")} помнят={_routes.Count} " +
               $"свой={(_selected is { } id && _routes.Has(id) ? 1 : 0)} продолжить={(CanContinueMove ? 1 : 0)}";
    }
}
