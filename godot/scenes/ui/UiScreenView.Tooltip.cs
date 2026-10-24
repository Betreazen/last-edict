using System;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Время экрана: автоповтор зажатой кнопки (<see cref="UiScreenState.Hold"/>) и всплывающая подсказка (<see cref="UiTooltip"/>) —
/// чёрный текст на белом в рамке, снизу слева от мыши, прижат к краям окна игры.</summary>
public partial class UiScreenView
{
    /// <summary>Проверка сцены ведёт время сама (шаг <c>wait N</c>): счёт автоповтора и подсказки не зависит от скорости машины.</summary>
    public static bool ManualTime { get; set; }

    private readonly UiTooltip _tip = new();
    private Node2D? _tipNode;
    private ColorRect? _tipBorder, _tipBack;
    private Label? _tipLabel;
    private Vector2I _mouse;

    /// <summary>Показанная подсказка (текст) — для строки проверки; не показана — null.</summary>
    public string? TooltipShown => _tip.Shown ? TipText() : null;

    /// <summary>Время кадра в целых мс с переносом остатка: округление каждого кадра (было <c>Math.Round(delta·1000)</c>) на 300 к/с теряло
    /// десятую часть времени, автоповтор и подсказки шли медленнее.</summary>
    private readonly FrameClock _clock = new();

    public override void _Process(double delta)
    {
        if (ManualTime) return;
        int ms = _clock.Advance(delta);
        if (ms > 0) Advance(ms);
    }

    /// <summary>Прошло <paramref name="ms"/>: щелчки автоповтора и отсчёт подсказки. Экран не принимает ввод (под окном) — подсказки нет.</summary>
    public void Advance(int ms)
    {
        if (_root is null) return;
        Animate(ms);   // анимации идут и у экрана под окном
        if (!IsProcessingUnhandledInput() || !IsVisibleInTree())
        {
            if (_tip.Element is not null) HideTip();
            return;
        }
        var events = _state.Hold(ms);
        if (events.Count > 0) Dispatch(events);
        if (Logic is IUiTicking ticking)
        {
            int before = _state.Revision;
            Guarded(() => ticking.Tick(ms, _state), "tick");
            if (_state.Revision != before) Refresh();
        }
        if (_tip.Tick(ms)) ShowTip();
    }

    /// <summary>Мышь в точке разметки: подсказка того, что под ней.</summary>
    private void TipMove(int x, int y)
    {
        _mouse = new Vector2I(x, y);
        var hit = _state.TooltipAt(x, y);
        string? before = _tip.Element;
        bool shown = _tip.Shown;
        _tip.Move(hit?.Element, hit?.Text);
        if (_tip.Element != before || _tip.Shown != shown) ShowTip();
    }

    private void HideTip()
    {
        _tip.Hide();
        ShowTip();
    }

    /// <summary>Строка подсказки: id — текст из таблицы языка (разметка снимается), не найден — сам id, как у движка (<c>0x5c8694</c>).</summary>
    private string TipText() =>
        _tip.Text is { } id && _strings.TryGetValue(id, out var raw) ? UiTextStyle.Parse(raw).Text : _tip.Text ?? "";

    private void ShowTip()
    {
        if (!_tip.Shown || _root is null)
        {
            if (_tipNode is not null) _tipNode.Visible = false;
            return;
        }
        if (_tipNode is null)
        {
            _tipNode = new Node2D { Name = "Tooltip", ZIndex = 100, ZAsRelative = false };
            // Подсказка только рисуется: по умолчанию ColorRect ловит мышь (Stop), и щелчок по месту под ней до экрана не доходил.
            _tipBorder = new ColorRect { Name = "Border", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
            _tipBack = new ColorRect { Name = "Back", Color = Colors.White, Position = Vector2.One, MouseFilter = Control.MouseFilterEnum.Ignore };
            UiFont face = _fonts.Resolve(null);
            _tipLabel = new Label
            {
                Name = "Text", AutowrapMode = TextServer.AutowrapMode.Word, ClipText = true,
                LabelSettings = UiArt.Settings(face, Colors.Black),
            };
            _tipNode.AddChild(_tipBorder);
            _tipNode.AddChild(_tipBack);
            _tipNode.AddChild(_tipLabel);
            _root.AddChild(_tipNode);
        }
        string text = TipText();
        var settings = _tipLabel!.LabelSettings;
        Vector2 size = settings.Font.GetMultilineStringSize(text, HorizontalAlignment.Left, UiTooltip.MaxWidth, settings.FontSize);
        // Строки подписи идут с шагом оригинала (UiArt.LineSpacing), а размер шрифта считает их через свою высоту — поправка на интервал.
        int lines = Math.Max(1, Mathf.RoundToInt(size.Y / settings.Font.GetHeight(settings.FontSize)));
        float height = size.Y + ((lines - 1) * settings.LineSpacing);
        var box = UiTooltip.Place(_mouse.X, _mouse.Y, Mathf.CeilToInt(size.X), Mathf.CeilToInt(height), TipBounds());
        var origin = Origin(_state.Screen);
        _tipNode.Position = new Vector2(box.X - origin.X, box.Y - origin.Y);
        _tipBorder!.Size = new Vector2(box.Width, box.Height);
        _tipBack!.Size = new Vector2(box.Width - 2, box.Height - 2);
        _tipLabel.Position = new Vector2(UiTooltip.PadX, UiTooltip.PadY);
        _tipLabel.Size = new Vector2(box.Width - (2 * UiTooltip.PadX), box.Height - (2 * UiTooltip.PadY));
        _tipLabel.Text = text;
        _tipNode.Visible = true;
    }

    /// <summary>Края окна игры в координатах разметки этого экрана: у встроенного — площадка хозяина, иначе сам экран.</summary>
    private UiRect TipBounds()
    {
        var design = _state.Screen.Design;
        if (SlotAt is not { } at) return design;
        at += SlotInset();   // экран по середине места больше разметки (UiScreenView.Layout.cs)
        // Окно в доле масштаба хозяина (SlotScale) с той же серединой: экран хозяина в точках окна — шире в 1/доля раз.
        float f = SlotScale;
        var origin = (at + (new Vector2(design.Width, design.Height) * (1f - f) / 2f)) / f;
        return new UiRect(design.X - (int)origin.X, design.Y - (int)origin.Y, (int)(SlotHost.X / f), (int)(SlotHost.Y / f));
    }
}
