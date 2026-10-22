using System;
using System.Collections.Generic;
using D2.Data.Ui;

namespace D2.Godot;

/// <summary>Строки интерфейса для зонда порта (схема 7.6, <c>docs/probe-av.md</c> §8): нажатие кнопки или переключателя (<c>ui_click</c>, до
/// обработчика — как у зонда оригинала) и смена доступности (<c>ui_enable</c>). Доступность у порта считается на лету
/// (<see cref="UiScreenState.IsEnabled"/>), поэтому смену видно только там, где вид её пересчитывает, — в <see cref="Refresh"/> (кадр
/// «погашена» берётся там же). Пишется только смена против прошлой перерисовки; новая кнопка — включённая, как у движка (у созданной
/// <c>CButtonInterf</c> флаг поднят: в партии <c>20261020-faces</c> окно отряда пишет одни «погасла»). Без зонда — ничего.</summary>
public partial class UiScreenView
{
    /// <summary>Доступность кнопок и переключателей экрана при прошлой перерисовке (только с зондом) и чьё это состояние: экран, загруженный
    /// заново, — новые кнопки.</summary>
    private Dictionary<string, bool>? _enabledShown;
    private UiScreenState? _enabledOf;

    /// <summary>Панель создана заново (как у движка: <c>CStratInterf</c> к ходу человека строит <c>DLG_STRATEGIC</c> новой, Зс57): её кнопки для
    /// зонда — новые, включённые; следующая перерисовка пишет только погашенные. Состояние экрана не трогает.</summary>
    public void RenewButtons() => _enabledShown = null;

    /// <summary><c>ui_click</c> для щелчков и переключений из <paramref name="events"/>; <paramref name="hotkey"/> — от клавиши.</summary>
    private void TraceClicks(IReadOnlyList<UiEvent> events, bool hotkey)
    {
        if (PortProbe.Av is not { } av) return;
        foreach (var e in events)
        {
            switch (e)
            {
                case UiClicked c:
                    av.UiClick("button", _state.Screen.Id, c.Element, hotkey, null, PortProbe.Top, PortProbe.TopClass);
                    break;
                case UiToggled t:
                    av.UiClick("toggle", _state.Screen.Id, t.Element, hotkey, t.Checked, PortProbe.Top, PortProbe.TopClass);
                    break;
            }
        }
    }

    /// <summary><c>ui_enable</c> на каждую кнопку и переключатель, чья доступность сменилась с прошлой перерисовки видимого экрана.</summary>
    private void TraceEnabled()
    {
        // Скрытая или снятая панель (панель команд в ход ИИ и под панелью режима: у движка её в это время нет вовсе) не пишет: смены лягут
        // строкой, когда её снова видно.
        if (PortProbe.Av is not { } av || !Visible || !IsInsideTree()) return;
        if (_enabledShown is null || !ReferenceEquals(_enabledOf, _state))
        {
            _enabledShown = new Dictionary<string, bool>(StringComparer.Ordinal);
            _enabledOf = _state;
        }
        foreach (var element in _state.Screen.Elements)
        {
            if (element.Kind is not (UiKind.Button or UiKind.Toggle)) continue;
            bool on = _state.IsEnabled(element.Id);
            bool was = _enabledShown.GetValueOrDefault(element.Id, true);
            _enabledShown[element.Id] = on;
            if (was != on) av.UiEnable(_state.Screen.Id, element.Id, on, PortProbe.Top, PortProbe.TopClass);
        }
    }
}
