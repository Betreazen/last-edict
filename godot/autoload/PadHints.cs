using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Input;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Подсказки кнопок геймпада (Э5, Р12): пока играют геймпадом (<see cref="GamepadPointer.PadActive"/>), внизу окна — полоса «кнопка —
/// действие» для верхнего экрана (<see cref="InputActions.Hints"/>: щелчок, отмена, кнопки групп экрана, крестовина или прокрутка); надписи
/// кнопок — по устройству (<see cref="InputNames.StyleOf"/>: Xbox, PlayStation, Steam Deck). Клавиатура или мышь — полоса прячется.
/// </summary>
public partial class PadHints : CanvasLayer
{
    private const double Period = 0.2;

    /// <summary>Кегль подсказок: обычный и крупного интерфейса (<see cref="UiScale"/>).</summary>
    private const int Font = 17, LargeFont = 24;

    private PanelContainer _panel = null!;
    private Label _text = null!;
    private double _clock;
    private string _key = "";

    /// <summary>Текст полосы сейчас (строка проверки); спрятана — «-».</summary>
    public string Shown { get; private set; } = "-";

    public static PadHints? Current { get; private set; }

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
    }

    /// <summary>Играют геймпадом — полоса следит за экранами; нет — спрятана и кадр не тратит.</summary>
    public void Activate(bool active)
    {
        if (!IsNodeReady()) return;   // только что добавлен: _Ready включит сам
        SetProcess(active);
        _clock = Period;
        if (!active) Refresh();
    }

    public override void _Ready()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        _clock = Period;
        _panel = new PanelContainer { Theme = SettingsTheme.Theme, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _text = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddChild(_text);
        AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        if ((_clock += delta) < Period) return;
        _clock = 0;
        Refresh();
    }

    /// <summary>Полоса по верхнему экрану и устройству — заново, если что-то сменилось.</summary>
    public void Refresh()
    {
        var active = GamepadPointer.PadActive ? UiScreenView.Active : [];
        var style = InputNames.StyleOf(Input.GetConnectedJoypads().Count > 0 ? Input.GetJoyName(Input.GetConnectedJoypads()[0]) : null);
        var screens = active.Select(v => v.ScreenId).ToList();
        bool navigates = active.Any(v => v.PadNavigates);
        string key = active.Count == 0 ? ""
            : $"{string.Join(",", screens)}|{GetViewport().GetVisibleRect().Size}|{style}|{InputBindings.Current?.GetHashCode()}|{navigates}|{GameData.Language}|{UiScale.Large}";
        if (key == _key) return;
        _key = key;
        if (active.Count == 0)
        {
            _panel.Visible = false;
            Shown = "-";
            return;
        }
        var strings = GameData.Strings(GameData.Language);
        string T(string id) => strings.GetValueOrDefault(id, id);
        var parts = InputActions.Hints(InputBindings.Actions, InputBindings.Current?.Map, screens)
            .Select(h => $"{InputNames.PadLabel(h.Binding, style)} {T(h.Action.Label)}").ToList();
        parts.Add(navigates ? $"✥ {T("LE_IN_HINT_BUTTONS")}" : $"{(style == PadStyle.Xbox ? "RS" : "R")} {T("LE_IN_HINT_SCROLL")}");
        Shown = string.Join("   ", parts);
        _text.Text = Shown;
        _text.AddThemeFontSizeOverride("font_size", UiScale.Large ? LargeFont : Font);
        _panel.Visible = true;
        Place();
    }

    /// <summary>Полоса — по середине у нижнего края окна.</summary>
    private void Place()
    {
        var view = GetViewport().GetVisibleRect().Size;
        _panel.ResetSize();
        var size = _panel.GetCombinedMinimumSize();
        _panel.Position = new Vector2((view.X - size.X) / 2, view.Y - size.Y - 6);
    }
}
