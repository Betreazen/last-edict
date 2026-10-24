using System;
using System.Linq;
using LastEdict.Data.Input;
using Godot;

namespace LastEdict.Godot;

/// <summary>Крестовина геймпада на окне разметки (Э5, Р12): у окон оригинала фокуса нет, поэтому шаг крестовины переводит указатель геймпада
/// (<see cref="GamepadPointer"/>) на соседнюю кнопку или переключатель в ту сторону (<see cref="UiNavigation"/>), и A щёлкает её. На экранах
/// стрелок (<c>ArrowScreens</c> в <c>data/ui/input.json</c>: панели карты) крестовина остаётся стрелками — прокрутка карты.</summary>
public partial class UiScreenView
{
    private static readonly (string Action, NavDirection Direction)[] Directions =
    [
        ("ui_up", NavDirection.Up), ("ui_down", NavDirection.Down), ("ui_left", NavDirection.Left), ("ui_right", NavDirection.Right),
    ];

    /// <summary>Элемент, на котором стоит указатель после шага крестовиной.</summary>
    private string? _padFocus;

    /// <summary>Экраны в дереве в порядке появления: верхний принимающий ввод — последний (подсказки кнопок, <see cref="PadHints"/>).</summary>
    private static readonly System.Collections.Generic.List<UiScreenView> Live = [];

    /// <summary>Экраны, которые сейчас принимают ввод (на карте их несколько: панель команд, палитра, поле; окно поверх их глушит).</summary>
    public static System.Collections.Generic.IReadOnlyList<UiScreenView> Active =>
        [.. Live.Where(v => v.IsVisibleInTree() && v.IsProcessingUnhandledInput() && v._root is not null)];

    /// <summary>Id экрана (для подсказок).</summary>
    public string ScreenId => _state.Screen.Id;

    /// <summary>Крестовина на этом экране шагает по кнопкам (не стрелки); у окна на <c>Control</c> поверх экрана — по его фокусу.</summary>
    public bool PadNavigates => _overlay is not null ||
        !GameData.Content.InputSetup.ArrowScreens.Contains(_state.Screen.Id, StringComparer.OrdinalIgnoreCase)
        && _state.Screen.Elements.Any(e => UiNavigation.Targetable(e) && Usable(e.Id));

    /// <summary>Шаг крестовиной; true — событие съедено (указатель переведён или стоит на последней кнопке в эту сторону).</summary>
    private bool Navigate(InputEvent @event)
    {
        // Без щелчка указателем (нет данных ввода) шаг по кнопкам бесполезен — крестовина остаётся стрелками.
        if (@event is not InputEventJoypadButton { Pressed: true } || GamepadPointer.Current is not { } pointer
            || !InputBindings.Has(GamepadPointer.Click)) return false;
        if (GameData.Content.InputSetup.ArrowScreens.Contains(_state.Screen.Id, StringComparer.OrdinalIgnoreCase)) return false;
        foreach (var (action, direction) in Directions)
        {
            if (!InputMap.HasAction(action) || !@event.IsActionPressed(action)) continue;
            // Цели вне разметки (значки дерева построек) — рядом с кнопками экрана.
            var pad = Logic as IPadTargets;
            var extra = pad?.PadTargets() ?? [];
            var screen = extra.Count == 0 ? _state.Screen : _state.Screen with { Elements = [.. _state.Screen.Elements, .. extra] };
            bool Can(string id) => extra.Any(e => e.Id == id) || Usable(id);
            string? from = _padFocus is { } focus && screen.Elements.Any(e => e.Id == focus) && Can(focus) ? focus
                : _state.Hovered is { } hovered && Usable(hovered) ? hovered : null;
            string? next = UiNavigation.Next(screen, Can, from, direction) ?? from;
            if (next is null) return false;   // нажимать нечего — крестовина остаётся стрелками
            _padFocus = next;
            if (pad is not null && extra.Any(e => e.Id == next))
            {
                pad.PadFocused(next);   // вид сдвинулся к цели — место цели заново
                Refresh();
                screen = _state.Screen with { Elements = [.. _state.Screen.Elements, .. pad.PadTargets()] };
            }
            var rect = screen.Elements.First(e => e.Id == next).Rect;
            var (x, y) = UiNavigation.Center(rect);
            pointer.MoveTo(RootPoint(new Vector2((float)x, (float)y)));
            GD.Print($"pad focus {_state.Screen.Id} {direction.ToString().ToLowerInvariant()} → {next} {pointer.Line()}");
            return true;
        }
        return false;
    }

    /// <summary>Элемент можно нажать: доступен, не спрятан, виден и логика экрана его пускает.</summary>
    private bool Usable(string id) =>
        _state.IsEnabled(id) && !_state.IsHidden(id) && (!_sprites.TryGetValue(id, out var sprite) || sprite.Visible)
        && (Logic?.Allows(id, _state) ?? true);

    /// <summary>Точка разметки → точка корневого вьюпорта (тем же преобразованием, каким рисуется экран, и обратным переводом показа).</summary>
    private Vector2 RootPoint(Vector2 design)
    {
        Vector2 view = _root!.GetGlobalTransformWithCanvas() * (design - (Vector2)Origin(_state.Screen));
        return ScreenStage.Current is { } stage && stage.Stage == GetViewport() ? stage.ToRoot * view : view;
    }
}
