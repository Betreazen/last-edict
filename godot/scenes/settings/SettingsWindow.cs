using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LastEdict.Data.Map;
using LastEdict.Data.Settings;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Окно настроек Last Edict на <c>Control</c> (Э5, roadmap §3.3): вкладки «Экран», «Звук», «Игра», «Бой», «Управление», «Язык», строки — по
/// пунктам <see cref="SettingsPages"/> (флажок, ползунок, выбор стрелками), подсказка пункта под фокусом или мышью, внизу — «По умолчанию»,
/// «Отмена», «Применить» и в партии кнопки игры. Логика и черновик — <see cref="SettingsLogic"/> (без движка); окно стоит поверх экрана
/// <see cref="SettingsLogic.Screen"/> в его разметке 800×600 (хозяин — <see cref="UiScreenView"/>, он вписывает окно в холст и передаёт
/// события элементов графу экранов). Клавиатура и геймпад — фокусом Godot (<c>ui_*</c>): стрелки и крестовина между строками, влево/вправо —
/// значение, «Отмена» (Esc, B) — закрыть. Вкладка «Управление» — переназначение клавиш и кнопок (<c>SettingsWindow.Controls.cs</c>).
/// </summary>
public partial class SettingsWindow : Control
{
    /// <summary>Место окна в разметке 800×600 экрана-хозяина.</summary>
    private static readonly Rect2 Frame = new(30, 22, 740, 556);

    private readonly SettingsLogic _logic;
    private readonly Func<string, string> _text;
    private readonly Action<string> _emit;
    private readonly Dictionary<string, Action> _refresh = new(StringComparer.Ordinal);
    private TabContainer _tabs = null!;
    private Label _help = null!;
    private Control _confirm = null!;
    private Label _confirmText = null!;
    private Button _apply = null!;

    /// <param name="emit">Событие элемента хозяину (<see cref="SettingsLogic.Close"/>, <see cref="SettingsLogic.Save"/>…).</param>
    public SettingsWindow(SettingsLogic logic, Func<string, string> text, Action<string> emit)
    {
        _logic = logic;
        _text = text;
        _emit = emit;
        Name = "SettingsWindow";
        Size = new Vector2(800, 600);
        MouseFilter = MouseFilterEnum.Stop;   // экран под окном щелчков не получает
        Theme = SettingsTheme.Theme;
        TextureFilter = TextureFilterEnum.Linear;   // рамка и значки темы вписываются с масштабом — сглаживанием, не «ближайшим»
    }

    public override void _Ready()
    {
        // Затемнение — на весь холст, а не только на разметку 800×600: окно игры шире (Э5, любое соотношение сторон).
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.55f), Position = new Vector2(-2000, -2000), Size = Size + new Vector2(4000, 4000),
                                 MouseFilter = MouseFilterEnum.Stop });
        var frame = new PanelContainer { Position = Frame.Position, Size = Frame.Size };
        AddChild(frame);
        var inner = new PanelContainer { ThemeTypeVariation = SettingsTheme.Inner };
        frame.AddChild(inner);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        inner.AddChild(column);
        column.AddChild(new Label { Text = T("LE_SET_TITLE"), ThemeTypeVariation = SettingsTheme.Title, HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new TextureRect { Texture = SettingsTheme.Divider(), StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                                          CustomMinimumSize = new Vector2(0, 14) });
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, TabAlignment = TabBar.AlignmentMode.Center };
        column.AddChild(_tabs);
        foreach (var tab in Enum.GetValues<SettingsTab>()) _tabs.AddChild(Page(tab));
        _tabs.CurrentTab = (int)_logic.Tab;
        _tabs.TabChanged += index =>
        {
            _logic.Tab = (SettingsTab)(int)index;
            DropCapture();
            Refresh();
        };
        column.AddChild(new ColorRect { Color = SettingsTheme.Gold with { A = 0.35f }, CustomMinimumSize = new Vector2(0, 1) });
        _help = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 46), VerticalAlignment = VerticalAlignment.Top,
            ThemeTypeVariation = SettingsTheme.HelpLabel,
        };
        column.AddChild(_help);
        column.AddChild(Footer());
        _confirm = Confirm();
        AddChild(_confirm);
        _confirm.Visible = false;
        Refresh();
        // Фокус — на первую строку открытой вкладки: окно сразу управляется клавиатурой и геймпадом.
        Callable.From(FocusFirst).CallDeferred();
    }

    private string T(string key) => _text(key);

    // --- вкладки и строки ------------------------------------------------------------------------------------------

    private Control Page(SettingsTab tab)
    {
        var scroll = new ScrollContainer { Name = T(SettingsPages.TabLabel(tab)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(rows);
        int index = 0;
        foreach (var item in SettingsPages.Of(tab)) rows.AddChild(Row(item, index++ % 2 == 1));
        if (tab == SettingsTab.Display)
        {
            // Быстрые настройки окна оригинала: «Низкое качество» / «Высокое качество» (MapSettings.Preset).
            var presets = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            presets.AddChild(Command(SettingsLogic.LowEndLabel, () => { _logic.Preset(lowEnd: true); Refresh(); }));
            presets.AddChild(Command(SettingsLogic.HighEndLabel, () => { _logic.Preset(lowEnd: false); Refresh(); }));
            rows.AddChild(presets);
        }
        if (tab == SettingsTab.Controls) ControlsPage(rows);   // SettingsWindow.Controls.cs
        return scroll;
    }

    private Control Row(SettingItem item, bool alternate = false)
    {
        var panel = new PanelContainer
        {
            Name = item.Id, ThemeTypeVariation = alternate ? SettingsTheme.RowPanelAlt : SettingsTheme.RowPanel, CustomMinimumSize = new Vector2(0, 34),
        };
        var line = new HBoxContainer();
        panel.AddChild(line);
        line.AddChild(new Label
        {
            Text = T(item.Label), ThemeTypeVariation = SettingsTheme.RowLabel, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center, ClipText = true,
        });
        Control input = item.Kind switch
        {
            SettingKind.Toggle => Toggle(item),
            SettingKind.Slider => Slider(item),
            _ => Choice(item),
        };
        line.AddChild(input);
        void Help() => _help.Text = item.Help is { } help ? T(help) : "";
        panel.MouseEntered += Help;
        foreach (var focusable in Focusables(input)) focusable.FocusEntered += Help;
        return panel;
    }

    private static IEnumerable<Control> Focusables(Control root)
    {
        if (root.FocusMode != FocusModeEnum.None) yield return root;
        foreach (var child in root.GetChildren().OfType<Control>())
            foreach (var inner in Focusables(child)) yield return inner;
    }

    private Control Toggle(SettingItem item)
    {
        var box = new CheckBox { Name = "Value", FocusMode = FocusModeEnum.All, Disabled = !_logic.Enabled(item) };
        box.Toggled += on => Set(item, on ? 1 : 0);
        _refresh[item.Id] = () => box.SetPressedNoSignal(_logic.Draft.Value(item) != 0);
        return box;
    }

    private Control Slider(SettingItem item)
    {
        var line = new HBoxContainer();
        var slider = new HSlider
        {
            Name = "Value", MinValue = item.Min, MaxValue = item.Max, Step = item.Step, CustomMinimumSize = new Vector2(200, 24),
            SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.All,
        };
        var value = new Label { ThemeTypeVariation = SettingsTheme.RowLabel, CustomMinimumSize = new Vector2(44, 0), HorizontalAlignment = HorizontalAlignment.Right };
        slider.ValueChanged += v => Set(item, (int)Math.Round(v));
        line.AddChild(slider);
        line.AddChild(value);
        _refresh[item.Id] = () =>
        {
            int v = _logic.Draft.Value(item);
            slider.SetValueNoSignal(v);
            value.Text = Percent(item, v);
        };
        return line;
    }

    /// <summary>Громкость — процентами от наибольшей (у музыки и звуков наибольшая 127, как у <c>Disciple.ini</c>).</summary>
    private static string Percent(SettingItem item, int value) =>
        item.Max == 0 ? "0" : $"{Math.Round(100.0 * (value - item.Min) / (item.Max - item.Min)).ToString(CultureInfo.InvariantCulture)}%";

    /// <summary>Выбор: стрелки по бокам и значение посередине; влево/вправо (клавиши, крестовина) на значении — соседний вариант, нажатие — следующий.</summary>
    private Control Choice(SettingItem item)
    {
        var line = new HBoxContainer();
        var left = Arrow(right: false);
        var right = Arrow(right: true);
        var value = new Button
        {
            Name = "Value", CustomMinimumSize = new Vector2(190, 26), FocusMode = FocusModeEnum.All, Disabled = !_logic.Enabled(item), Flat = true,
        };
        left.Pressed += () => Step(item, -1);
        right.Pressed += () => Step(item, +1);
        value.Pressed += () => Step(item, +1, wrap: true);
        value.GuiInput += e =>
        {
            if (e.IsActionPressed("ui_left")) { Step(item, -1); value.AcceptEvent(); }
            else if (e.IsActionPressed("ui_right")) { Step(item, +1); value.AcceptEvent(); }
        };
        line.AddChild(left);
        line.AddChild(value);
        line.AddChild(right);
        _refresh[item.Id] = () =>
        {
            int v = _logic.Draft.Value(item);
            value.Text = item.Choices.FirstOrDefault(c => c.Value == v) is { } choice ? T(choice.Label) : v.ToString(CultureInfo.InvariantCulture);
            left.Disabled = right.Disabled = !_logic.Enabled(item);
        };
        return line;
    }

    /// <summary>Шеврон выбора: соседний вариант влево или вправо.</summary>
    private static Button Arrow(bool right) => new()
    {
        FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 26), Flat = true, Icon = SettingsTheme.Chevron(right),
        IconAlignment = HorizontalAlignment.Center,
    };

    private void Step(SettingItem item, int by, bool wrap = false)
    {
        var values = item.Choices.Select(c => c.Value).ToList();
        int at = values.IndexOf(_logic.Draft.Value(item));
        int next = at + by;
        next = wrap ? ((next % values.Count) + values.Count) % values.Count : Math.Clamp(next, 0, values.Count - 1);
        Set(item, values[next]);
    }

    private void Set(SettingItem item, int value)
    {
        _logic.Set(item, value);
        Refresh();
    }

    // --- низ окна и подтверждение -----------------------------------------------------------------------------------

    private Control Footer()
    {
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        if (_logic.InGame)
        {
            footer.AddChild(Command("LE_SET_SAVE_GAME", () => _emit(SettingsLogic.Save)));
            footer.AddChild(Command("LE_SET_LOAD_GAME", () => _emit(SettingsLogic.Load)));
            footer.AddChild(Command("LE_SET_QUIT", () => _emit(SettingsLogic.Quit)));
        }
        footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        footer.AddChild(Command("LE_SET_DEFAULTS", () => { _logic.Defaults(); Refresh(); }));
        footer.AddChild(Command("LE_SET_CANCEL", Close));
        _apply = Command("LE_SET_APPLY", Apply);
        _apply.ThemeTypeVariation = SettingsTheme.Primary;
        footer.AddChild(_apply);
        return footer;
    }

    private Button Command(string label, Action action)
    {
        var button = new Button { Text = T(label), Name = label, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 30) };
        button.Pressed += action;
        return button;
    }

    private Control Confirm()
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.5f), Size = Size, MouseFilter = MouseFilterEnum.Stop };
        var box = new PanelContainer { Position = new Vector2(220, 220), Size = new Vector2(360, 160) };
        shade.AddChild(box);
        var column = new VBoxContainer();
        box.AddChild(column);
        column.AddChild(new Label { Text = T("LE_SET_CONFIRM_TITLE"), ThemeTypeVariation = SettingsTheme.Title, HorizontalAlignment = HorizontalAlignment.Center });
        _confirmText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        column.AddChild(_confirmText);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddChild(Command("LE_SET_CONFIRM_KEEP", () => Answer(true)));
        buttons.AddChild(Command("LE_SET_CONFIRM_REVERT", () => Answer(false)));
        column.AddChild(buttons);
        return shade;
    }

    private void Apply()
    {
        DropCapture();
        if (!_logic.Apply())
        {
            if (DisplaySettings.Allowed(OS.GetCmdlineUserArgs())) DisplaySettings.ApplyLook(_logic.Draft.Current);
            Refresh();
            return;
        }
        ApplyDisplay(_logic.Draft.Current);   // новый режим — сразу, на пробу
        _confirm.Visible = true;
        Refresh();
        _confirm.FindChildren("LE_SET_CONFIRM_KEEP", "Button", true, false).OfType<Button>().FirstOrDefault()?.GrabFocus();
    }

    private void Answer(bool keep)
    {
        var settings = _logic.Answer(keep);
        if (!keep) ApplyDisplay(settings);
        _confirm.Visible = false;
        Refresh();
        _apply.GrabFocus();
    }

    /// <summary>Режим экрана — окну игры: режим окна, синхронизация, предел кадров (<see cref="DisplaySettings"/>), вписывание (<see cref="ScreenStage"/>).
    /// Без окна и в проверках окно ОС не трогается — только строка проверки.</summary>
    private static void ApplyDisplay(MapSettings settings)
    {
        GD.Print($"settings экран окно={settings.WindowMode} vsync={(settings.VSync ? 1 : 0)} fps={settings.MaxFps} показ={settings.Scaling}");
        if (!DisplaySettings.Allowed(OS.GetCmdlineUserArgs())) return;
        DisplaySettings.Apply(settings);
        ScreenStage.Current?.SetMode(settings.ScreenScaling);
    }

    /// <summary>«Отмена» и Esc: невыбранное — назад, окно закрывается (событие «Закрыть» хозяину).</summary>
    private void Close()
    {
        DropCapture();
        if (_logic.Confirm is not null)
        {
            Answer(false);
            return;
        }
        _logic.Cancel();
        _emit(SettingsLogic.Close);
    }

    public override void _Process(double delta)
    {
        // Окно поверх (запись, загрузка) — у хозяина ввод только у верхнего экрана: окно на Control иначе ловило бы его щелчки раньше
        // экранов (они берут ввод в _UnhandledInput), поэтому прячется, пока экран под ним не верхний.
        Visible = GetParent() is not UiScreenView host || host.IsProcessingUnhandledInput();
        if (_logic.Confirm is null) return;
        if (_logic.Tick(delta)) Answer(false);   // не ответили за 15 с — прежний режим
        else _confirmText.Text = T("LE_SET_CONFIRM_TEXT").Replace("%SECONDS%", _logic.Confirm?.Left.ToString(CultureInfo.InvariantCulture) ?? "0",
                                                                 StringComparison.Ordinal);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || !@event.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        Close();
    }

    private void Refresh()
    {
        foreach (var refresh in _refresh.Values) refresh();
        _apply.Disabled = !_logic.Draft.Changed && _logic.Confirm is null;
        if (_logic.Confirm is { } c) _confirmText.Text = T("LE_SET_CONFIRM_TEXT").Replace("%SECONDS%", c.Left.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private void FocusFirst()
    {
        if (_tabs.GetCurrentTabControl() is { } page && Focusables(page).FirstOrDefault() is { } first) first.GrabFocus();
    }

    // --- сценарий проверки ------------------------------------------------------------------------------------------

    /// <summary>Шаг сценария проверки (<c>UiScreenView.Step</c> отдаёт его окну): <c>tab Audio</c>, <c>set music 64</c>, <c>apply</c>, <c>keep</c>,
    /// <c>revert</c>, <c>wait 15000</c> (время подтверждения), <c>defaults</c>, <c>preset low|high</c>, <c>cancel</c>, <c>click BTN_SAVE</c> (кнопки
    /// игры и «Закрыть»); вкладки «Управление» — <c>capture</c>, <c>unbind</c>, <c>resetkeys</c> (SettingsWindow.Controls.cs).
    /// false — шаг не окна.</summary>
    public bool Step(string verb, string arg)
    {
        if (ControlsStep(verb, arg)) return true;
        switch (verb)
        {
            case "tab" when Enum.TryParse<SettingsTab>(arg, true, out var tab):
                _tabs.CurrentTab = (int)tab;
                return true;
            case "set" when arg.Split(' ') is [var id, var raw] && int.TryParse(raw, CultureInfo.InvariantCulture, out int v):
                Set(SettingsPages.Get(id), v);
                return true;
            case "apply": Apply(); return true;
            case "keep": Answer(true); return true;
            case "revert": Answer(false); return true;
            case "defaults": _logic.Defaults(); Refresh(); return true;
            case "preset" when arg is "low" or "high":
                _logic.Preset(arg == "low");
                Refresh();
                return true;
            case "cancel": Close(); return true;
            case "wait" when int.TryParse(arg, out int ms):
                _Process(ms / 1000.0);
                return true;
            case "click" when arg is SettingsLogic.Save or SettingsLogic.Load or SettingsLogic.Quit && _logic.InGame:
                _emit(arg);
                return true;
            case "click" when arg == SettingsLogic.Close:
                Close();
                return true;
            default: return false;
        }
    }

    /// <summary>Строка проверки окна (<see cref="SettingsLogic.Line"/>) и видимое: вкладка, подтверждение.</summary>
    public string Line() => $"{_logic.Line()} показ_подтверждения={(_confirm.Visible ? 1 : 0)} применить={(_apply.Disabled ? 0 : 1)}";
}
