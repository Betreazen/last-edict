using System;
using System.Linq;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Дерево окна построек (Э6, <see cref="BuildLogic"/>): вкладки веток над деревом и само дерево в области с обрезкой (<c>ClipContents</c>),
/// внутри — <c>Node2D</c> с масштабом и сдвигом вида (<see cref="ZoomView"/>). Рамки значков и плашки подписей — 9-patch темы окон Last Edict
/// (<see cref="SettingsTheme.Plate"/>), связи «требуемая → постройка» — линиями, подписи — векторным шрифтом (PT Serif, MSDF): при зуме
/// значки берутся из HD-слоя (текстура логического размера, сглаживание с mip-уровнями — <see cref="ImageScales.Filter"/>), а текст и
/// рамки рисуются в точках экрана и не мылятся. Ввода у вида нет (<c>MouseFilter.Ignore</c>): мышь и геймпад идут через экран
/// (<c>UiScreenView</c>) в логику окна — попадания и зум решает она.
/// </summary>
public partial class BuildTreeView : Control
{
    /// <summary>Цвета: подпись, приглушённая подпись, золото, тусклое золото, поле плашки, выбор (жёлтый контур движка), связь не построенной.</summary>
    private static readonly Color Light = SettingsTheme.Light, Muted = SettingsTheme.Muted, Gold = SettingsTheme.Gold, GoldSoft = SettingsTheme.GoldSoft,
        PlateBack = new("#1b1510"), Pick = new("#e1e100"), Faint = new(0.56f, 0.45f, 0.25f, 0.55f), Mine = new("#9fd27a");

    private const int CaptionSize = 13, TabSize = 14;

    private readonly IBuildTreeSource _logic;
    private readonly Func<string, Texture2D?> _texture;
    private readonly Control _clip = new() { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Canvas _canvas;
    private int _revision = -1;

    /// <summary>Левый верхний угол вида в точках разметки (над вкладками).</summary>
    private Vector2 _corner;

    private static Font? _font;

    /// <summary>Подписи — векторным шрифтом всегда: при зуме растровый шрифт игры мылился бы.</summary>
    private static Font Caption => _font ??= UiArt.Face(VectorFonts.Apply(GameData.Content.Fonts).Faces["Normal"]);

    private static StyleBox? _plate, _tab, _tabOpen, _frame, _frameHover;

    public BuildTreeView(IBuildTreeSource logic, Func<string, Texture2D?> texture)
    {
        _logic = logic ?? throw new ArgumentNullException(nameof(logic));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        Name = "BuildTree";
        MouseFilter = MouseFilterEnum.Ignore;
        _canvas = new Canvas(this);
        AddChild(_clip);
        _clip.AddChild(_canvas);
    }

    /// <summary>Узлов-значков нарисовано в последний раз (строка проверки).</summary>
    public int Drawn { get; private set; }

    /// <summary>Перенести вид под состояние логики: место (точки разметки от начала экрана <paramref name="origin"/>), вид, перерисовка.</summary>
    public void Sync(Vector2I origin)
    {
        if (_logic.Revision == _revision || _logic.TreeArea is not { } area) return;
        _revision = _logic.Revision;
        var tabs = _logic.Tabs;
        int top = tabs.Count > 0 ? tabs.Min(t => t.Rect.Y) - BuildLogic.TabPad : area.Y;
        _corner = new Vector2(area.X, top);
        Position = _corner - origin;
        Size = new Vector2(area.Width, area.Bottom - top);
        _clip.Position = new Vector2(0, area.Y - top);
        _clip.Size = new Vector2(area.Width, area.Height);
        _canvas.Position = new Vector2((float)_logic.View.X, (float)_logic.View.Y);
        _canvas.Scale = new Vector2((float)_logic.View.Zoom, (float)_logic.View.Zoom);
        QueueRedraw();
        _canvas.QueueRedraw();
    }

    /// <summary>Вкладки веток.</summary>
    public override void _Draw()
    {
        if (_logic.TreeArea is null) return;
        _tab ??= SettingsTheme.Plate(PlateBack, GoldSoft);
        _tabOpen ??= SettingsTheme.Plate(SettingsTheme.PrimaryBack, Gold);
        foreach (var tab in _logic.Tabs)
        {
            var rect = new Rect2(tab.Rect.X - _corner.X, tab.Rect.Y - _corner.Y, tab.Rect.Width, tab.Rect.Height);
            DrawStyleBox(tab.Open ? _tabOpen : _tab, rect);
            var color = tab.Open || tab.Hovered ? Light : Muted;
            DrawCentered(this, tab.Name, rect, TabSize, color);
        }
        if (_logic is not BuildLogic build) return;
        if (build.Card is { } card) DrawCard(card);
        var gold = build.GoldRect;
        if (build.Gold.Length > 0 && gold.Width > 0)
            DrawString(Caption, new Vector2(gold.X - _corner.X, gold.Y - _corner.Y + ((gold.Height + Caption.GetAscent(TabSize) - Caption.GetDescent(TabSize)) / 2f)),
                       build.Gold, HorizontalAlignment.Right, gold.Width - 6, TabSize, Gold);
        foreach (var button in build.Buttons) DrawButton(button);
    }

    private static StyleBox? _button, _buttonHover, _buttonOff;

    /// <summary>Кнопка в теме Last Edict: плашка с золотой нитью, подпись по середине; погашенная — приглушена.</summary>
    private void DrawButton(BuildLogic.BuildButton button)
    {
        _button ??= SettingsTheme.Plate(SettingsTheme.PrimaryBack, Gold);
        _buttonHover ??= SettingsTheme.Plate(new Color("#4a3a1f"), Light);
        _buttonOff ??= SettingsTheme.Plate(PlateBack, GoldSoft with { A = 0.5f });
        var rect = new Rect2(button.Rect.X - _corner.X, button.Rect.Y - _corner.Y, button.Rect.Width, button.Rect.Height);
        DrawStyleBox(!button.Enabled ? _buttonOff : button.Hovered ? _buttonHover : _button, rect);
        DrawCentered(this, button.Label, rect, TabSize + 1, button.Enabled ? Light : Muted with { A = 0.6f });
    }

    private static StyleBox? _card;

    /// <summary>Карточка выбранной постройки у правого края (окно построек без панели «Управление городом»): имя, цена, описание, портрет
    /// воина постройки и строки о нём, статус; «Построить» — кнопка разметки поверх неё (<c>BTN_BUILD</c>).</summary>
    private void DrawCard(BuildLogic.BuildCard card)
    {
        _card ??= SettingsTheme.Plate(SettingsTheme.Field with { A = 0.94f }, Gold, small: false);
        var r = new Rect2(card.Rect.X - _corner.X, card.Rect.Y - _corner.Y, card.Rect.Width, card.Rect.Height);
        DrawStyleBox(_card, r);
        var font = Caption;
        float x = r.Position.X + 16, width = r.Size.X - 32, y = r.Position.Y + 18;
        DrawString(font, new Vector2(x, y + font.GetAscent(20)), Fit(font, card.Title, width, 20), HorizontalAlignment.Left, width, 20, Light);
        y += 30;
        DrawString(font, new Vector2(x, y + font.GetAscent(15)), Fit(font, card.Cost, width, 15), HorizontalAlignment.Left, width, 15, Gold);
        y += 24;
        DrawMultilineString(font, new Vector2(x, y + font.GetAscent(14)), card.Description, HorizontalAlignment.Left, width, 14, 6, Light);
        var face = BuildLogic.FaceRect(card);
        var faceRect = new Rect2(face.X - _corner.X, face.Y - _corner.Y, face.Width, face.Height);
        if (card.Face is { } frame && _texture(frame) is { } portrait)
        {
            // Портрет — целиком в месте, с его пропорцией, по середине; рамка — темы Last Edict.
            var size = portrait.GetSize();
            float k = Mathf.Min(faceRect.Size.X / size.X, faceRect.Size.Y / size.Y);
            var drawn = new Rect2(faceRect.Position + ((faceRect.Size - (size * k)) / 2f), size * k);
            DrawTextureRect(portrait, drawn, false);
            _frame ??= SettingsTheme.Plate(new Color(0, 0, 0, 0), GoldSoft);
            DrawStyleBox(_frame, drawn.Grow(4));
        }
        // Строки о воине: у малого портрета — справа от него, у большого — под ним.
        var at = card.FaceBig || card.Face is null
            ? new Vector2(x, faceRect.End.Y + (card.Face is null ? -faceRect.Size.Y : 10))
            : new Vector2(faceRect.End.X + 12, faceRect.Position.Y);
        float lines = r.End.X - 16 - at.X;
        DrawMultilineString(font, at + new Vector2(0, font.GetAscent(13)), string.Join("\n", card.UnitLines), HorizontalAlignment.Left, lines, 13, 9,
                            Muted);
        var statusColor = card.Kind == BuildIcon.Can ? new Color("#cfe8b0") : card.Kind == BuildIcon.Built ? Light : new Color("#e0a07a");
        float statusY = r.End.Y - 16 - 32 - 40;
        DrawMultilineString(font, new Vector2(x, statusY + font.GetAscent(14)), card.Status, HorizontalAlignment.Center, width, 14, 2, statusColor);
    }

    /// <summary>Строка по середине прямоугольника (обрезается многоточием, если не входит).</summary>
    private static void DrawCentered(CanvasItem item, string text, Rect2 rect, int size, Color color)
    {
        var font = Caption;
        float width = rect.Size.X - 8;
        float baseline = rect.Position.Y + ((rect.Size.Y + font.GetAscent(size) - font.GetDescent(size)) / 2f);
        item.DrawString(font, new Vector2(rect.Position.X + 4, baseline), Fit(font, text, width, size), HorizontalAlignment.Center, width, size, color);
    }

    /// <summary>Строка, укороченная многоточием до ширины.</summary>
    private static string Fit(Font font, string text, float width, int size)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            string cut = text[..n].TrimEnd() + "…";
            if (font.GetStringSize(cut, HorizontalAlignment.Left, -1, size).X <= width) return cut;
        }
        return "…";
    }

    /// <summary>Дерево в точках содержимого; масштаб и сдвиг — у узла (вид), обрезка — у области.</summary>
    private sealed partial class Canvas(BuildTreeView owner) : Node2D
    {
        public override void _Draw()
        {
            var logic = owner._logic;
            _plate ??= SettingsTheme.Plate(PlateBack, GoldSoft);
            _frame ??= SettingsTheme.Plate(new Color(0, 0, 0, 0), GoldSoft);
            _frameHover ??= SettingsTheme.Plate(new Color(0, 0, 0, 0), Gold);
            foreach (var link in logic.Links) Link(link);
            int drawn = 0;
            // Фильтр — один на узел: по первому значку (у слоя HD все значки дерева из одного архива — HD, иначе все ×1).
            if (logic.Items.Count > 0 && owner._texture(logic.Items[0].Frame) is { } first)
                TextureFilter = ImageScales.Filter(first, TextureFilterEnum.Nearest);
            foreach (var item in logic.Items)
            {
                var icon = Rect(item.Icon);
                if (owner._texture(item.Frame) is { } texture)
                {
                    DrawTextureRect(texture, icon, false);
                    drawn++;
                }
                DrawStyleBox(item.Hovered ? _frameHover : _frame, icon.Grow(5));
                if (item.Marked) DrawRect(icon.Grow(8), Mine, false, 2f);   // постройка мода (живая страница Студии)
                if (item.Selected) DrawRect(icon.Grow(1), Pick, false, 1.5f);
                var plate = Rect(item.Plate);
                DrawStyleBox(_plate, plate);
                DrawCentered(this, item.Name, plate, CaptionSize, item.Kind == BuildIcon.OtherPath ? Muted : Light);
            }
            owner.Drawn = drawn;
        }

        /// <summary>Связь: от низа плашки требуемой вниз, вбок над постройкой, вниз к верху её рамки.</summary>
        private void Link(BuildTreeLink link)
        {
            var from = new Vector2(link.From.X + (link.From.Width / 2f), link.From.Bottom);
            var to = new Vector2(link.To.X + (link.To.Width / 2f), link.To.Y - 5);
            float mid = (from.Y + to.Y) / 2f;
            var color = link.Built ? Gold : Faint;
            DrawPolyline([from, new Vector2(from.X, mid), new Vector2(to.X, mid), to], color, 2f, true);
        }

        private static Rect2 Rect(UiRect r) => new(r.X, r.Y, r.Width, r.Height);
    }
}
