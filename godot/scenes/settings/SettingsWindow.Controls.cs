using System;
using System.Linq;
using LastEdict.Data.Input;
using Godot;

namespace LastEdict.Godot;

/// <summary>Вкладка «Управление» (Э5, Р12): действия ввода (<see cref="InputBindings"/>, <c>data/ui/input.json</c>) по группам — общие, карта,
/// бой, окна отряда… В строке — подпись действия и две кнопки: клавиатура и геймпад. Нажатие кнопки ждёт следующую клавишу (или кнопку,
/// ось геймпада) и ставит её действию; у действий тех же экранов она снимается (<see cref="InputActions.Bind"/>). Esc (на геймпаде — кнопка
/// «Отмены», B) или повторное нажатие той же кнопки — отмена ожидания, Backspace — снять привязку; поэтому Esc и кнопку «Отмены» другим
/// действиям не дать — только самой «Отмене». Действует сразу, в файл — по «Применить», как прочие пункты.</summary>
public partial class SettingsWindow
{
    /// <summary>Действие и устройство (<c>key</c>/<c>pad</c>), чью привязку ждёт окно; null — не ждёт.</summary>
    private (InputAction Action, string Device)? _capture;

    private void ControlsPage(VBoxContainer rows)
    {
        var actions = InputBindings.Actions;
        foreach (var group in actions.GroupBy(a => a.Group ?? ""))
        {
            string label = group.First().GroupLabel ?? (group.Key.Length == 0 ? "LE_IN_GROUP_GENERAL" : group.Key);
            rows.AddChild(new Label { Text = T(label), ThemeTypeVariation = SettingsTheme.GroupLabel, Name = $"group_{group.Key}" });
            foreach (var action in group) rows.AddChild(BindingRow(action));
        }
        var reset = Command("LE_SET_BIND_RESET", () =>
        {
            _logic.ResetBindings();
            Refresh();
        });
        reset.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        rows.AddChild(reset);
    }

    private Control BindingRow(InputAction action)
    {
        var panel = new PanelContainer { Name = action.Id, ThemeTypeVariation = SettingsTheme.RowPanel, CustomMinimumSize = new Vector2(0, 30) };
        var line = new HBoxContainer();
        panel.AddChild(line);
        line.AddChild(new Label
        {
            Text = T(action.Label), ThemeTypeVariation = SettingsTheme.RowLabel, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center, ClipText = true,
        });
        var key = BindingButton(action, "key", 170);
        var pad = BindingButton(action, "pad", 120);
        line.AddChild(key);
        line.AddChild(pad);
        void Help() => _help.Text = T("LE_SET_BIND_HELP");
        panel.MouseEntered += Help;
        key.FocusEntered += Help;
        pad.FocusEntered += Help;
        _refresh[$"bind:{action.Id}"] = () =>
        {
            key.Text = Shown(action, "key");
            pad.Text = Shown(action, "pad");
        };
        return panel;
    }

    private Button BindingButton(InputAction action, string device, int width)
    {
        var button = new Button
        {
            Name = device, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(width, 26), ClipText = true,
        };
        button.Pressed += () =>
        {
            if (_logic.Confirm is not null) return;
            bool same = _capture is { } c && c.Action.Id == action.Id && c.Device == device;
            _capture = same ? null : (action, device);   // повторное нажатие — отмена ожидания
            Refresh();
        };
        return button;
    }

    /// <summary>Текст кнопки привязки: ждёт — «Нажмите…», иначе подписи привязок устройства через запятую, нет их — прочерк.</summary>
    private string Shown(InputAction action, string device)
    {
        if (_capture is { } c && c.Action.Id == action.Id && c.Device == device) return T("LE_SET_BIND_WAIT");
        var labels = InputActions.BindingsOf(action, _logic.Draft.Current.Bindings?.Map)
                                 .Where(b => InputActions.DeviceOf(b) == device).Select(InputBindings.Label).ToList();
        return labels.Count == 0 ? "—" : string.Join(", ", labels);
    }

    /// <summary>Ожидание привязки берёт событие раньше фокуса и экранов: Esc — отмена, Backspace — снять, клавиша (кнопка, ось) нужного
    /// устройства — новая привязка. Кнопка «Отмены» геймпада при ожидании кнопки для другого действия — отмена.</summary>
    public override void _Input(InputEvent @event)
    {
        if (_capture is not { } c || !Visible) return;
        if (c.Device == "pad" && c.Action.Godot != "ui_cancel" && @event is InputEventJoypadButton && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            EndCapture();
            return;
        }
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.Backspace } control)
        {
            GetViewport().SetInputAsHandled();
            if (control.Keycode == Key.Backspace) Bind(c.Action, c.Device, null);
            EndCapture();
            return;
        }
        if (InputBindings.BindingOf(@event) is not { } binding || InputActions.DeviceOf(binding) != c.Device) return;
        GetViewport().SetInputAsHandled();
        Bind(c.Action, c.Device, binding);
        EndCapture();
    }

    private void EndCapture()
    {
        _capture = null;
        Refresh();
    }

    /// <summary>Окно сменило вкладку, закрывается или применяет — ожидание привязки снимается.</summary>
    private void DropCapture() => _capture = null;

    private void Bind(InputAction action, string device, string? binding)
    {
        _logic.Bind(InputBindings.Actions, action, device, binding);
        GD.Print($"settings привязка {action.Id} {device}={binding ?? "-"} теперь=" +
                 string.Join(",", InputActions.BindingsOf(action, _logic.Draft.Current.Bindings?.Map)));
    }

    /// <summary>Шаги проверки вкладки: <c>capture map.BTN_END_TURN key</c> (дальше — <c>key 69</c>), <c>unbind &lt;id&gt; pad</c>,
    /// <c>resetkeys</c>.</summary>
    private bool ControlsStep(string verb, string arg)
    {
        string[] parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length == 2 ? InputBindings.Actions.FirstOrDefault(a => a.Id == parts[0]) : null;
        switch (verb)
        {
            case "capture" when action is not null && parts[1] is "key" or "pad":
                _capture = (action, parts[1]);
                Refresh();
                return true;
            case "unbind" when action is not null && parts[1] is "key" or "pad":
                Bind(action, parts[1], null);
                Refresh();
                return true;
            case "resetkeys":
                _logic.ResetBindings();
                Refresh();
                return true;
            default:
                return false;
        }
    }
}
