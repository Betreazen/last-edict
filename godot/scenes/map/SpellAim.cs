using System;
using Godot;

namespace LastEdict.Godot;

/// <summary>Ввод прицела заклинанием поверх карты: отдельный узел, чтобы экран карты не держал своих
/// <c>_Process</c>/<c>_UnhandledInput</c> (их правят и другие части экрана). Сообщает клетку щелчка, Esc и смену клетки под
/// мышью; решения — у хозяина (<c>MapScreen.Spells.cs</c>).</summary>
public partial class SpellAim : Node
{
    /// <summary>Клетка под мышью (null — мышь не над картой).</summary>
    public Func<(int X, int Y)?> MouseCell { get; set; } = () => null;

    /// <summary>Прицел открыт и окон поверх нет.</summary>
    public Func<bool> Active { get; set; } = () => false;

    public event Action<(int X, int Y)>? Clicked;

    public event Action? Cancelled;

    public event Action<(int X, int Y)?>? Hovered;

    private (int X, int Y)? _last;

    /// <summary>Нажатие левой кнопки пришло сюда: щелчок — только отпускание своего нажатия. Режим открывается нажатием в окне (двойной
    /// щелчок по заклинанию книги, кнопка панели), и его отпускание прицел не бросает (партия 17.10, карта 11: «Создание Рощи» и «Туман
    /// Смерти» ушли в клетку под курсором через 50–70 мс после открытия прицела) — как у карты (<c>MapView</c>, пометка 34).</summary>
    private bool _leftDown;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active())
        {
            _leftDown = false;
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            bool released = !button.Pressed && _leftDown;
            _leftDown = button.Pressed;
            if (released && MouseCell() is { } cell) Clicked?.Invoke(cell);
        }
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape }) Cancelled?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!Active())
        {
            _leftDown = false;   // нажатие до окна поверх прицела — его отпускание после окна щелчком не считается
            return;
        }
        var cell = MouseCell();
        if (cell == _last) return;
        _last = cell;
        Hovered?.Invoke(cell);
    }
}
