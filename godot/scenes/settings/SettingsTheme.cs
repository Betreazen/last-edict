using System.IO;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Одна тема окон Last Edict на <c>Control</c> (Э5, roadmap §3.3): рамки и плашки из окна настроек оригинала — 9-patch из
/// <c>DLG_OPTIONS_OPTIONS_MAIN</c> (рамка окна, мраморная полоса строки, плашка заголовка), переключатель — <c>DLG_OPTIONS_TOGGLE_*</c>, стрелки
/// выбора — <c>DLG_OPTIONS_ARROW*2_*</c>; шрифт — EB Garamond (<c>data/fonts</c>, OFL). Картинки — из импорта (опись интерфейса), в репозитории
/// их нет; нет кадра — тема без него (плоские цвета), окно всё равно работает.
/// </summary>
public static class SettingsTheme
{
    /// <summary>Цвет текста на тёмном (заголовок, вкладки) и на мраморе (строки, кнопки).</summary>
    public static readonly Color Light = new(0.91f, 0.86f, 0.75f), Ink = new(0.17f, 0.13f, 0.08f);

    /// <summary>Вариация темы для подписей строк на мраморе.</summary>
    public const string RowLabel = "RowLabel", Title = "TitleLabel", RowPanel = "RowPanel", Inner = "InnerPanel";

    private const string Background = "DLG_OPTIONS_OPTIONS_MAIN";

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

        var back = Frame(Background);
        // Окно: рамка оригинала (решётка сверху, край с ромбами по бокам), середина — тёмная заливка поверх растянутой середины.
        theme.SetStylebox("panel", "PanelContainer", back is null ? Flat(new Color(0.1f, 0.09f, 0.07f), 2)
            : Patch(back, new Rect2(0, 0, 800, 600), 14, 40, 14, 14));
        var inner = Flat(new Color(0.08f, 0.07f, 0.05f, 0.96f), 1);
        theme.SetStylebox("panel", "TabContainer", inner);
        // Внутри рамки — тёмная панель: растянутая середина картинки окна оригинала под ней не видна.
        theme.SetTypeVariation(Inner, "PanelContainer");
        theme.SetStylebox("panel", Inner, Flat(new Color(0.1f, 0.085f, 0.06f), 1));
        // Строка пункта — мраморная полоса строки окна оригинала (16…376 × 82…106).
        StyleBox row = back is null ? Flat(new Color(0.78f, 0.74f, 0.66f), 1) : Patch(back, new Rect2(14, 82, 362, 24), 8, 4, 8, 4);
        theme.SetTypeVariation(RowPanel, "PanelContainer");
        theme.SetStylebox("panel", RowPanel, row);
        theme.SetTypeVariation(RowLabel, "Label");
        theme.SetColor("font_color", RowLabel, Ink);
        theme.SetTypeVariation(Title, "Label");
        theme.SetFontSize("font_size", Title, 26);
        if (semibold is not null) theme.SetFont("font", Title, semibold);

        // Вкладки и кнопки — плашка заголовка окна оригинала (120…258 × 46…76).
        StyleBox Plate(float shade) => back is null ? Flat(new Color(0.75f, 0.7f, 0.6f) * shade, 1)
            : Tinted(Patch(back, new Rect2(120, 46, 138, 30), 10, 6, 10, 6), shade);
        theme.SetStylebox("tab_selected", "TabContainer", Plate(1f));
        theme.SetStylebox("tab_unselected", "TabContainer", Plate(0.62f));
        theme.SetStylebox("tab_hovered", "TabContainer", Plate(0.85f));
        theme.SetStylebox("tab_focus", "TabContainer", Focus());
        theme.SetColor("font_selected_color", "TabContainer", Ink);
        theme.SetColor("font_unselected_color", "TabContainer", Ink);
        theme.SetColor("font_hovered_color", "TabContainer", Ink);
        theme.SetStylebox("normal", "Button", Plate(0.9f));
        theme.SetStylebox("hover", "Button", Plate(1.05f));
        theme.SetStylebox("pressed", "Button", Plate(0.7f));
        theme.SetStylebox("disabled", "Button", Plate(0.45f));
        theme.SetStylebox("focus", "Button", Focus());
        foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            theme.SetColor(color, "Button", Ink);
        theme.SetColor("font_disabled_color", "Button", Ink with { A = 0.5f });

        // Переключатель — камень окна оригинала: включён — горит (_C), наведён — _H, выключен — тусклый (_D).
        if (Frame("DLG_OPTIONS_TOGGLE_C") is { } on && Frame("DLG_OPTIONS_TOGGLE_D") is { } off)
        {
            theme.SetIcon("checked", "CheckBox", on);
            theme.SetIcon("unchecked", "CheckBox", off);
            theme.SetIcon("checked_disabled", "CheckBox", on);
            theme.SetIcon("unchecked_disabled", "CheckBox", off);
        }
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("focus", "CheckBox", Focus());
        theme.SetStylebox("focus", "HSlider", Focus());
        return theme;
    }

    /// <summary>Рамка фокуса (клавиатура, геймпад): тонкий золотой контур.</summary>
    private static StyleBoxFlat Focus() => new()
    {
        DrawCenter = false, BorderColor = new Color(0.95f, 0.8f, 0.35f), BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2,
        BorderWidthBottom = 2, ExpandMarginLeft = 2, ExpandMarginTop = 2, ExpandMarginRight = 2, ExpandMarginBottom = 2,
    };

    private static StyleBoxFlat Flat(Color color, int border) => new()
    {
        BgColor = color, BorderColor = new Color(0.36f, 0.31f, 0.23f), BorderWidthLeft = border, BorderWidthTop = border,
        BorderWidthRight = border, BorderWidthBottom = border, ContentMarginLeft = 8, ContentMarginTop = 4, ContentMarginRight = 8,
        ContentMarginBottom = 4,
    };

    private static StyleBoxTexture Patch(Texture2D texture, Rect2 region, int left, int top, int right, int bottom) => new()
    {
        Texture = texture, RegionRect = region, TextureMarginLeft = left, TextureMarginTop = top, TextureMarginRight = right,
        TextureMarginBottom = bottom, ContentMarginLeft = left + 4, ContentMarginTop = top + 2, ContentMarginRight = right + 4,
        ContentMarginBottom = bottom + 2, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch,
        AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch,
    };

    private static StyleBox Tinted(StyleBoxTexture box, float shade)
    {
        box.ModulateColor = new Color(shade, shade, shade);
        return box;
    }

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
