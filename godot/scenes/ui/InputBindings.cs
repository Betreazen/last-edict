using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Input;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Действия ввода (<see cref="InputActions"/>, <c>data/ui/input.json</c>) в <c>InputMap</c> Godot: у общих — встроенные имена
/// (<c>ui_accept</c>, <c>ui_left</c>…: их же ведут фокус окон на <c>Control</c> и прокрутка карты), у прочих — id действия. Привязки — по
/// умолчанию или переназначенные (<see cref="KeyBindings"/> из настроек, <see cref="Apply"/>). Экраны разметки ждут коды клавиш Windows
/// (<see cref="UiElement.Hotkeys"/>): <see cref="VirtualKeys"/> переводит событие клавиатуры или геймпада в коды действий экрана; клавиша,
/// которую не держит ни одно действие экрана (стирание в поле ввода и т. п.), идёт своим кодом, как прежде.
/// </summary>
public static class InputBindings
{
    /// <summary>Порог стика и курков: дальше — нажато (как у <c>ui_*</c> Godot по умолчанию).</summary>
    public const float Deadzone = 0.5f;

    /// <summary>Порог стика указателя (<c>pointer.*</c>): он водится плавно, половина хода стика не должна пропадать.</summary>
    public const float PointerDeadzone = 0.15f;

    private static IReadOnlyList<InputAction>? _applied;

    /// <summary>Имена действий в <c>InputMap</c> — для <see cref="Has"/> без вызова движка (его зовут каждый кадр).</summary>
    private static HashSet<string> _names = new(StringComparer.Ordinal);
    private static KeyBindings? _overrides;
    private static readonly Dictionary<string, InputAction[]> ByScreen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Действия, у которых есть привязка к оси: движение стика проверяется только по ним.</summary>
    private static InputAction[] _axes = [];

    /// <summary>Оси за порогом — по устройству и действию: стик шлёт движение без конца, нажатие — только переход через порог.</summary>
    private static readonly HashSet<(int Device, string Name)> Held = [];

    /// <summary>Последнее разобранное движение оси и сработавшие на нём действия: одно событие получают все экраны вьюпорта, переход через
    /// порог считается один раз.</summary>
    private static InputEvent? _lastMotion;
    private static HashSet<string> _motionPressed = new(StringComparer.Ordinal);

    public static IReadOnlyList<InputAction> Actions => GameData.Content.Input;

    /// <summary>Переназначенные привязки, что сейчас стоят в <c>InputMap</c>.</summary>
    public static KeyBindings? Current => _overrides;

    /// <summary>Имя действия в <c>InputMap</c>.</summary>
    public static string NameOf(InputAction action) => action.Godot ?? action.Id;

    /// <summary>Привязки — в <c>InputMap</c>: по умолчанию и переназначенные пользователем. Те же, что уже стоят, — ничего не делает (окно
    /// настроек зовёт на каждый выбор).</summary>
    public static void Apply(KeyBindings? overrides)
    {
        if (_applied is not null && ReferenceEquals(_applied, Actions) && Equals(_overrides, overrides)) return;
        _applied = Actions;
        _overrides = overrides;
        ByScreen.Clear();
        Held.Clear();
        _lastMotion = null;
        foreach (var action in _applied)
        {
            string name = NameOf(action);
            if (InputMap.HasAction(name)) InputMap.ActionEraseEvents(name);
            else InputMap.AddAction(name, Deadzone);
            InputMap.ActionSetDeadzone(name, name.StartsWith("pointer.", StringComparison.Ordinal) ? PointerDeadzone : Deadzone);
            foreach (string binding in InputActions.BindingsOf(action, overrides?.Map))
                if (EventOf(binding) is { } e) InputMap.ActionAddEvent(name, e);
        }
        _names = new HashSet<string>(_applied.Select(NameOf), StringComparer.Ordinal);
        _axes = [.. _applied.Where(a => InputActions.BindingsOf(a, overrides?.Map).Any(b => b.StartsWith("axis:", StringComparison.Ordinal)))];
    }

    /// <summary>Действия ещё не в <c>InputMap</c> (хозяин настроек их не ставил): привязки из файла настроек (<c>--settings</c> или
    /// <c>user://settings.json</c>), в проверках без <c>--settings</c> — по умолчанию.</summary>
    private static void Ensure()
    {
        if (_applied is not null) return;
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--settings");
        string? path = at >= 0 && at + 1 < args.Length ? args[at + 1]
            : args.Any(a => a is "--check" or "--input") ? null : ProjectSettings.GlobalizePath("user://settings.json");
        Apply(path is null ? null : LastEdict.Data.Map.MapSettings.Load(path).Bindings);
    }

    /// <summary>Коды клавиш, которые событие жмёт на экране <paramref name="screen"/>: по коду на сработавшее действие экрана (у действия
    /// группы — код, который держат элементы этого экрана), а клавиша без действия на экране — своим кодом (<paramref name="raw"/>). Пусто —
    /// событие ничего не нажимает; null в списке — клавиша без кода (логика окна получает 0).</summary>
    public static IReadOnlyList<int?> VirtualKeys(InputEvent @event, UiScreen screen, Func<Key, int?> raw)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(raw);
        Ensure();
        if (@event is InputEventKey { Echo: true }) return [];
        var on = On(screen.Id);
        var result = new List<int?>();
        bool motion = @event is InputEventJoypadMotion;
        if (motion) Motion(@event);
        foreach (var action in on)
        {
            if (action.Vk.Count == 0 || !(motion ? _motionPressed.Contains(action.Id) : Pressed(@event, NameOf(action)))) continue;
            int code = action.Group is null ? action.Vk[0]
                : action.Vk.FirstOrDefault(v => screen.Elements.Any(e => e.Hotkeys.Contains(v)), action.Vk[0]);
            if (action.Group is null && @event is InputEventJoypadButton or InputEventJoypadMotion && OwnPadButton(screen, code, on)) continue;
            if (!result.Contains(code)) result.Add(code);
        }
        if (result.Count > 0 || @event is not InputEventKey { Pressed: true } key) return result;
        int? vk = raw(key.Keycode);
        return vk is int own && on.Any(a => a.Vk.Contains(own)) ? [] : [vk];
    }

    /// <summary>Кнопка геймпада общего действия не жмёт элемент со своей кнопкой геймпада: на панели карты Esc — вторая горячая клавиша
    /// «Опций» (F4, Esc), и B («отмена») открывал бы опции, а у них на геймпаде — Start. Клавиатура — как в оригинале.</summary>
    private static bool OwnPadButton(UiScreen screen, int code, IReadOnlyList<InputAction> on)
    {
        var targets = screen.Elements.Where(e => e.Hotkeys.Contains(code)).ToList();
        return targets.Count > 0 && targets.All(e => on.Any(a => a.Group is not null && a.Id == $"{a.Group}.{e.Id}"
            && InputMap.HasAction(NameOf(a))
            && InputMap.ActionGetEvents(NameOf(a)).Any(ev => ev is InputEventJoypadButton or InputEventJoypadMotion)));
    }

    /// <summary>Клавиша и кнопка — нажатием.</summary>
    private static bool Pressed(InputEvent @event, string name) => @event.IsAction(name) && @event.IsActionPressed(name);

    /// <summary>Движение оси — по всем действиям с осями сразу (так удержание не залипает на экране, где действия нет): сработали те, что
    /// перешли порог.</summary>
    private static void Motion(InputEvent @event)
    {
        if (ReferenceEquals(@event, _lastMotion)) return;
        _lastMotion = @event;
        _motionPressed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in _axes)
        {
            string name = NameOf(action);
            if (!@event.IsAction(name)) continue;
            var key = (@event.Device, name);
            if (!@event.IsActionPressed(name, allowEcho: true)) Held.Remove(key);
            else if (Held.Add(key)) _motionPressed.Add(action.Id);
        }
    }

    private static InputAction[] On(string screen)
    {
        if (!ByScreen.TryGetValue(screen, out var on)) ByScreen[screen] = on = [.. InputActions.On(_applied!, screen)];
        return on;
    }

    /// <summary>Действие есть в <c>InputMap</c> (данные ввода загружены); нет — хозяин берёт прежнюю клавишу.</summary>
    public static bool Has(string name)
    {
        Ensure();
        return _names.Contains(name);
    }

    /// <summary>Событие привязки: <c>key:Space</c>, <c>pad:A</c>, <c>axis:LeftX-</c>; имя не разобралось — null.</summary>
    public static InputEvent? EventOf(string binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        int colon = binding.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return null;
        string kind = binding[..colon], name = binding[(colon + 1)..];
        switch (kind)
        {
            case "key" when Enum.TryParse(name, ignoreCase: true, out Key key):
                return new InputEventKey { Keycode = key };
            case "pad" when Enum.TryParse(name, ignoreCase: true, out JoyButton button):
                return new InputEventJoypadButton { ButtonIndex = button, Device = -1 };
            case "axis" when name.Length > 1 && name[^1] is '+' or '-' && Enum.TryParse(name[..^1], ignoreCase: true, out JoyAxis axis):
                return new InputEventJoypadMotion { Axis = axis, AxisValue = name[^1] == '+' ? 1 : -1, Device = -1 };
            default:
                return null;
        }
    }

    /// <summary>Привязка по пойманному событию (переназначение во вкладке «Управление»); не годится (отпускание, ось у нуля, мышь) — null.</summary>
    public static string? BindingOf(InputEvent @event) => @event switch
    {
        InputEventKey { Pressed: true, Echo: false } key when key.Keycode != Key.None => $"key:{key.Keycode}",
        InputEventJoypadButton { Pressed: true } button => $"pad:{button.ButtonIndex}",
        InputEventJoypadMotion motion when Math.Abs(motion.AxisValue) >= Deadzone =>
            $"axis:{motion.Axis}{(motion.AxisValue > 0 ? '+' : '-')}",
        _ => null,
    };

    /// <summary>Подпись привязки на кнопке вкладки: имя клавиши Godot, кнопка и ось геймпада — коротко (<see cref="InputNames.PadLabel"/>).</summary>
    public static string Label(string binding) =>
        binding.StartsWith("key:", StringComparison.Ordinal) && EventOf(binding) is InputEventKey key
            ? OS.GetKeycodeString(key.Keycode)
            : InputNames.PadLabel(binding);
}
