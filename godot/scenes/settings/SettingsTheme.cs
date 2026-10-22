using System;
using System.IO;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Одна тема окон Last Edict на <c>Control</c> (Э5; вид — макет «B» окна настроек, выбор пользователя 22.10): тёмная кожа, золотая
/// орнаментальная рамка Kenney «Fantasy UI Borders» (CC0, <c>data/ui/own/art/kenney-fantasy-ui-borders</c>; белые линии тонируются в золото
/// здесь), поле окна — вплотную к рамке; вкладки — текстом с золотой чертой у открытой, строки — тонкие полосы, выбор — шевронами,
/// флажок — ползунок, кнопки — в малой рамке, «Применить» — выделена (<see cref="Primary"/>). Шрифт — EB Garamond (<c>data/fonts</c>, OFL).
/// Нет файла рамки — плоские цвета, окно всё равно работает. Линии и значки рисуются здесь же, вдвое крупнее (окно вписывается с
/// масштабом и сглаживанием).
/// </summary>
public static class SettingsTheme
{
    /// <summary>Цвета: текст, приглушённый текст, золото, золото тусклое, поле окна, строки, строка под фокусом, выделенная кнопка.</summary>
    public static readonly Color Light = new("#eadfc6"), Muted = new("#b3a385"), Gold = new("#c9a45a"), GoldSoft = new("#8f7440"),
        Field = new("#17120c"), RowA = new("#211a12"), RowB = new("#1c160f"), RowFocus = new("#2c2216"), PrimaryBack = new("#3a2c17");

    /// <summary>Вариации темы: подпись строки, заголовок окна, полоса строки, внутренняя панель, подпись группы, выделенная кнопка.</summary>
    public const string RowLabel = "RowLabel", Title = "TitleLabel", RowPanel = "RowPanel", RowPanelAlt = "RowPanelAlt", Inner = "InnerPanel",
        GroupLabel = "GroupLabel", Primary = "PrimaryButton", HelpLabel = "HelpLabel";

    private const string Art = "data/ui/own/art/kenney-fantasy-ui-borders";

    private static Theme? _theme;

    public static Theme Theme => _theme ??= Build();

    private static Theme Build()
    {
        var theme = new Theme();
        var regular = Font("EBGaramond-Regular.ttf");
        var semibold = Font("EBGaramond-SemiBold.ttf");
        if (regular is not null) theme.DefaultFont = regular;
        theme.DefaultFontSize = 17;
        theme.SetColor("font_color", "Label", Light);

        // Окно: золотая рамка поверх тёмного поля во всю рамку (зазора между рамкой и полем нет).
        theme.SetStylebox("panel", "PanelContainer", Framed("panel-border-030.png", Field, 16, 22, 16));
        theme.SetTypeVariation(Inner, "PanelContainer");
        theme.SetStylebox("panel", Inner, Empty(4, 2));
        theme.SetStylebox("panel", "TabContainer", Empty(0, 6));
        theme.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());

        // Строки: тонкая полоса с золотой нитью сверху.
        theme.SetTypeVariation(RowPanel, "PanelContainer");
        theme.SetStylebox("panel", RowPanel, Flat(RowA, Gold with { A = 0.14f }, top: 1, margin: 14));
        theme.SetTypeVariation(RowPanelAlt, "PanelContainer");   // строки через одну — чуть темнее
        theme.SetStylebox("panel", RowPanelAlt, Flat(RowB, Gold with { A = 0.14f }, top: 1, margin: 14));
        theme.SetTypeVariation(RowLabel, "Label");
        theme.SetColor("font_color", RowLabel, Light);
        theme.SetFontSize("font_size", RowLabel, 18);
        theme.SetTypeVariation(Title, "Label");
        theme.SetFontSize("font_size", Title, 28);
        theme.SetColor("font_color", Title, Light);
        if (semibold is not null) theme.SetFont("font", Title, semibold);
        theme.SetTypeVariation(GroupLabel, "Label");
        theme.SetFontSize("font_size", GroupLabel, 16);
        theme.SetColor("font_color", GroupLabel, Gold);
        theme.SetTypeVariation(HelpLabel, "Label");
        theme.SetColor("font_color", HelpLabel, Muted);

        // Вкладки — текстом: открытая светлая с золотой чертой снизу, прочие приглушены.
        theme.SetStylebox("tab_selected", "TabContainer", Flat(Colors.Transparent, Gold, bottom: 2, margin: 16));
        theme.SetStylebox("tab_unselected", "TabContainer", Flat(Colors.Transparent, Colors.Transparent, bottom: 2, margin: 16));
        theme.SetStylebox("tab_hovered", "TabContainer", Flat(Colors.Transparent, GoldSoft, bottom: 2, margin: 16));
        theme.SetStylebox("tab_focus", "TabContainer", Focus());
        theme.SetStylebox("tabbar_background", "TabContainer", Flat(Colors.Transparent, Colors.Transparent, bottom: 0, margin: 0));
        theme.SetColor("font_selected_color", "TabContainer", Light);
        theme.SetColor("font_unselected_color", "TabContainer", Muted);
        theme.SetColor("font_hovered_color", "TabContainer", Light);
        theme.SetFontSize("font_size", "TabContainer", 19);

        // Кнопки — в малой золотой рамке; «Применить» — с тёплым полем.
        var normal = Button("panel-border-010.png", new Color("#1b1510"), 16, 12, 5);
        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", Button("panel-border-010.png", RowFocus, 16, 12, 5));
        theme.SetStylebox("pressed", "Button", Button("panel-border-010.png", PrimaryBack, 16, 12, 5));
        theme.SetStylebox("disabled", "Button", Button("panel-border-010.png", new Color("#14100b"), 16, 12, 5, Muted with { A = 0.45f }));
        theme.SetStylebox("focus", "Button", Focus());
        foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            theme.SetColor(color, "Button", Light);
        theme.SetColor("font_disabled_color", "Button", Muted with { A = 0.5f });
        theme.SetTypeVariation(Primary, "Button");
        theme.SetStylebox("normal", Primary, Button("panel-border-010.png", PrimaryBack, 16, 12, 5));
        theme.SetStylebox("hover", Primary, Button("panel-border-010.png", new Color("#4a3a1f"), 16, 12, 5));

        // Флажок — ползунок: включён — золотой, выключен — тёмный.
        theme.SetIcon("checked", "CheckBox", Switch(on: true));
        theme.SetIcon("unchecked", "CheckBox", Switch(on: false));
        theme.SetIcon("checked_disabled", "CheckBox", Switch(on: true, dim: true));
        theme.SetIcon("unchecked_disabled", "CheckBox", Switch(on: false, dim: true));
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("focus", "CheckBox", Focus());

        // Ползунок громкости: тёмная дорожка, золотая заполненная часть, золотая головка.
        theme.SetStylebox("slider", "HSlider", Flat(new Color("#120e09"), GoldSoft, all: 1, margin: 0, height: 6));
        theme.SetStylebox("grabber_area", "HSlider", Flat(GoldSoft, GoldSoft, all: 0, margin: 0, height: 6));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Flat(Gold, Gold, all: 0, margin: 0, height: 6));
        theme.SetIcon("grabber", "HSlider", Dot(Gold));
        theme.SetIcon("grabber_highlight", "HSlider", Dot(Light));
        theme.SetStylebox("focus", "HSlider", Focus());

        // Полоса прокрутки — тонкая, тусклое золото.
        theme.SetStylebox("scroll", "VScrollBar", Flat(new Color(0, 0, 0, 0.25f), Colors.Transparent, all: 0, margin: 2));
        theme.SetStylebox("grabber", "VScrollBar", Flat(GoldSoft, GoldSoft, all: 0, margin: 2));
        theme.SetStylebox("grabber_highlight", "VScrollBar", Flat(Gold, Gold, all: 0, margin: 2));
        theme.SetStylebox("grabber_pressed", "VScrollBar", Flat(Gold, Gold, all: 0, margin: 2));
        return theme;
    }

    /// <summary>Шеврон выбора (‹ или ›), золотой.</summary>
    public static Texture2D Chevron(bool right) => _chevrons[right ? 1 : 0] ??= ChevronTexture(right);

    private static readonly Texture2D?[] _chevrons = new Texture2D?[2];

    /// <summary>Разделитель под заголовком: нить, светлеющая к середине, и ромб посередине.</summary>
    public static Texture2D Divider() => _divider ??= DividerTexture();

    private static Texture2D? _divider;

    /// <summary>Рамка фокуса (клавиатура, геймпад): тонкий золотой контур.</summary>
    private static StyleBoxFlat Focus() => new()
    {
        DrawCenter = false, BorderColor = Gold, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ExpandMarginLeft = 2, ExpandMarginTop = 2, ExpandMarginRight = 2, ExpandMarginBottom = 2,
    };

    private static StyleBoxEmpty Empty(int x, int y) => new() { ContentMarginLeft = x, ContentMarginRight = x, ContentMarginTop = y, ContentMarginBottom = y };

    private static StyleBoxFlat Flat(Color back, Color border, int top = 0, int bottom = 0, int all = -1, int margin = 8, int height = -1)
    {
        var box = new StyleBoxFlat
        {
            BgColor = back, BorderColor = border, ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        box.BorderWidthTop = all >= 0 ? all : top;
        box.BorderWidthBottom = all >= 0 ? all : bottom;
        box.BorderWidthLeft = box.BorderWidthRight = all >= 0 ? all : 0;
        if (height > 0) box.ContentMarginTop = box.ContentMarginBottom = height / 2f;
        return box;
    }

    /// <summary>Рамка кнопки — та же рамка Kenney вдвое мельче (углы 8 точек): у кнопок тонкая кайма, как в макете.</summary>
    private static StyleBox Button(string file, Color back, int corner, int x, int y, Color? line = null) =>
        Framed(file, back, corner, x, y, line, half: true);

    /// <summary>Рамка Kenney (<paramref name="file"/>, 48×48, углы 16; <paramref name="half"/> — вдвое мельче) золотом поверх поля
    /// <paramref name="back"/> во всю рамку; поля содержимого — <paramref name="x"/>, <paramref name="y"/>. Нет файла — плоское поле с
    /// золотой каймой.</summary>
    private static StyleBox Framed(string file, Color back, int corner, int x, int y, Color? line = null, bool half = false)
    {
        string path = GameData.FromRoot(Path.Combine(Art, file));
        var lines = File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (lines is not null && half)
        {
            lines.Resize(lines.GetWidth() / 2, lines.GetHeight() / 2, Image.Interpolation.Lanczos);
            corner /= 2;
        }
        if (lines is null)
            return new StyleBoxFlat
            {
                BgColor = back, BorderColor = line ?? Gold, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                ContentMarginLeft = x, ContentMarginRight = x, ContentMarginTop = y, ContentMarginBottom = y,
            };
        lines.Convert(Image.Format.Rgba8);
        var tone = line ?? Gold;
        var image = Image.CreateEmpty(lines.GetWidth(), lines.GetHeight(), false, Image.Format.Rgba8);
        for (int py = 0; py < image.GetHeight(); py++)
            for (int px = 0; px < image.GetWidth(); px++)
            {
                float a = lines.GetPixel(px, py).A;
                image.SetPixel(px, py, back.Lerp(tone with { A = 1 }, a * tone.A) with { A = back.A });
            }
        var texture = ImageTexture.CreateFromImage(image);
        return new StyleBoxTexture
        {
            Texture = texture, TextureMarginLeft = corner, TextureMarginTop = corner, TextureMarginRight = corner, TextureMarginBottom = corner,
            ContentMarginLeft = x, ContentMarginRight = x, ContentMarginTop = y, ContentMarginBottom = y,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch,
        };
    }

    /// <summary>Ползунок флажка 58×26 (нарисован вдвое крупнее и уменьшен до логического размера).</summary>
    private static Texture2D Switch(bool on, bool dim = false)
    {
        const int s = 2, w = 58 * s, h = 26 * s;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var track = on ? PrimaryBack : new Color("#120e09");
        var edge = on ? Gold : GoldSoft;
        var knob = on ? Gold : new Color("#5d4d33");
        float r = h / 2f, knobR = 9f * s, knobX = on ? w - r : r;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Math.Clamp(x + 0.5f, r, w - r), d = new Vector2(x + 0.5f - cx, y + 0.5f - r).Length();
                var c = Colors.Transparent;
                if (d <= r - s) c = track;
                else if (d <= r) c = edge;
                float k = new Vector2(x + 0.5f - knobX, y + 0.5f - r).Length();
                if (k <= knobR) c = knob;
                float alpha = Math.Clamp(r + 0.5f - d, 0, 1);
                image.SetPixel(x, y, c with { A = c.A * alpha * (dim ? 0.5f : 1f) });
            }
        return Scaled(image, s);
    }

    /// <summary>Головка ползунка — золотой круг 16×16.</summary>
    private static Texture2D Dot(Color color)
    {
        const int s = 2, size = 16 * s;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - r, y + 0.5f - r).Length();
                image.SetPixel(x, y, color with { A = Math.Clamp(r - d, 0, 1) });
            }
        return Scaled(image, s);
    }

    private static Texture2D ChevronTexture(bool right)
    {
        const int s = 2, size = 18 * s;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        // Две линии от середины края к вершине: «›» — вершина справа.
        var a = new Vector2(right ? 6 : 12, 4) * s;
        var tip = new Vector2(right ? 12 : 6, 9) * s;
        var b = new Vector2(right ? 6 : 12, 14) * s;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Math.Min(Segment(p, a, tip), Segment(p, tip, b));
                image.SetPixel(x, y, Gold with { A = Math.Clamp(1.6f * s - d, 0, 1) });
            }
        return Scaled(image, s);
    }

    private static Texture2D DividerTexture()
    {
        const int s = 2, w = 420 * s, h = 14 * s;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float mid = w / 2f, cy = h / 2f, half = 5f * s;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = Math.Abs(x + 0.5f - mid) / mid;
                float lineA = Math.Clamp(s - Math.Abs(y + 0.5f - cy), 0, 1) * Math.Clamp((1 - t) * 1.6f, 0, 1);
                float diamond = Math.Abs(x + 0.5f - mid) + Math.Abs(y + 0.5f - cy);
                var c = Gold with { A = lineA };
                if (diamond <= half) c = diamond >= half - (1.2f * s) ? Gold : Field;
                image.SetPixel(x, y, c);
            }
        return Scaled(image, s);
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return (p - (a + (ab * t))).Length();
    }

    /// <summary>Текстура логического размера (точки — вдвое гуще, <see cref="ImageScales"/>).</summary>
    private static Texture2D Scaled(Image image, int scale) => ImageScales.Texture(ImageScales.With(image, scale));

    /// <summary>Кадр описи интерфейса по имени (импорт); нет — null.</summary>
    public static Texture2D? Frame(string name)
    {
        if (!GameData.Content.AssetsByName.TryGetValue(name, out var asset)) return null;
        string path = UiAssets.PathOf(asset, GameData.UiAssetsRoot);
        if (!File.Exists(path)) return null;
        var image = Image.LoadFromFile(path);
        return image is null ? null : ImageScales.Texture(ImageScales.With(image, asset.Factor));   // HD — логического размера (9-patch)
    }

    private static FontFile? Font(string file)
    {
        string path = GameData.FromRoot(Path.Combine("data", "fonts", file));
        if (!File.Exists(path)) return null;
        var font = new FontFile();
        return font.LoadDynamicFont(path) == Error.Ok ? font : null;
    }
}
