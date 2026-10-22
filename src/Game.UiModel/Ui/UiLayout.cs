using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEdict.Data.Ui;

/// <summary>Правило элемента в <c>ui/layout.json</c>: якорь и растяжение; не задано — как в разметке (или <see cref="UiLayoutRule.Anchor"/>).</summary>
public sealed record UiElementRule(UiAnchor? Anchor = null, UiStretch? Stretch = null);

/// <summary>Политика вписывания экрана (<c>ui/layout.json</c>, Э5, Р13): как экран ложится в окно другой формы. Разметка экранов — импорт
/// `.dlg` (якоря там угаданы по третям экрана, <c>DlgImport.Anchor</c>), политика — свои данные поверх неё, слоями модов.
/// <para><see cref="Elements"/> — по имени элемента или шаблону с <c>*</c> в конце (<c>IMG_L*</c>); точное имя главнее шаблона, длинный
/// шаблон — короткого. <see cref="Anchor"/> — якорь всех прочих элементов экрана (поле боя — <see cref="UiAnchor.Center"/>: юниты
/// держатся середины, а не угаданных краёв).</para></summary>
public sealed record UiLayoutRule
{
    public UiFit? Fit { get; init; }
    public string? Border { get; init; }
    public UiAnchor? Anchor { get; init; }

    /// <summary>Якорь фона экрана (<see cref="UiScreen.BackgroundAnchor"/>).</summary>
    public UiAnchor? Background { get; init; }
    public IReadOnlyDictionary<string, UiElementRule> Elements { get; init; } = new Dictionary<string, UiElementRule>();
}

/// <summary>Чтение <c>ui/layout.json</c>, наложение слоёв модов и применение к экранам.</summary>
public static class UiLayouts
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyDictionary<string, UiLayoutRule> FromJson(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, UiLayoutRule>>(json, Json) is { } rules
            ? new Dictionary<string, UiLayoutRule>(rules, StringComparer.OrdinalIgnoreCase)
            : throw new FormatException("ui/layout.json: пусто");

    /// <summary>Правила из файла; нет файла — null.</summary>
    public static IReadOnlyDictionary<string, UiLayoutRule>? Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;

    /// <summary>Слой мода поверх: у того же экрана заданное модом главнее, элементы — по одному.</summary>
    public static IReadOnlyDictionary<string, UiLayoutRule> Merge(IReadOnlyDictionary<string, UiLayoutRule> baseSet,
                                                                  IReadOnlyDictionary<string, UiLayoutRule> overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var result = new Dictionary<string, UiLayoutRule>(baseSet, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, over) in overlay)
        {
            if (!result.TryGetValue(id, out var under)) { result[id] = over; continue; }
            var elements = new Dictionary<string, UiElementRule>(under.Elements, StringComparer.Ordinal);
            foreach (var (name, rule) in over.Elements) elements[name] = rule;
            result[id] = new UiLayoutRule
            {
                Fit = over.Fit ?? under.Fit, Border = over.Border ?? under.Border, Anchor = over.Anchor ?? under.Anchor,
                Background = over.Background ?? under.Background, Elements = elements,
            };
        }
        return result;
    }

    /// <summary>Правило всех экранов без своего (ключ <c>*</c>): паспарту меню.</summary>
    public const string AnyScreen = "*";

    /// <summary>Экраны с политикой: вписывание, паспарту, якоря и растяжение элементов по правилам. Экран без правила — по правилу
    /// <see cref="AnyScreen"/>, нет и его — как был.</summary>
    public static IReadOnlyList<UiScreen> Apply(IReadOnlyList<UiScreen> screens, IReadOnlyDictionary<string, UiLayoutRule>? rules)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (rules is null || rules.Count == 0) return screens;
        rules.TryGetValue(AnyScreen, out var any);
        return [.. screens.Select(s => rules.TryGetValue(s.Id, out var rule) ? Apply(s, rule) : any is null ? s : Apply(s, any))];
    }

    public static UiScreen Apply(UiScreen screen, UiLayoutRule rule)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(rule);
        return screen with
        {
            Fit = rule.Fit ?? screen.Fit,
            Border = rule.Border ?? screen.Border,
            BackgroundAnchor = rule.Background ?? screen.BackgroundAnchor,
            Elements = [.. screen.Elements.Select(e => RuleOf(rule, e.Id) is { } r
                ? e with { Anchor = r.Anchor ?? rule.Anchor ?? e.Anchor, Stretch = r.Stretch ?? e.Stretch }
                : rule.Anchor is { } all ? e with { Anchor = all } : e)],
        };
    }

    /// <summary>Правило элемента: точное имя, иначе самый длинный подходящий шаблон <c>префикс*</c>.</summary>
    private static UiElementRule? RuleOf(UiLayoutRule rule, string id)
    {
        if (rule.Elements.TryGetValue(id, out var exact)) return exact;
        return rule.Elements.Where(p => p.Key.EndsWith('*') && id.StartsWith(p.Key[..^1], StringComparison.Ordinal))
                            .OrderByDescending(p => p.Key.Length).Select(p => p.Value).FirstOrDefault();
    }
}

/// <summary>Раскладка экрана на холсте формы окна (Э5, Р13: любое соотношение сторон — 16:9, 16:10, 21:9, 4:3). Чистые функции: вид
/// (<c>UiScreenView</c>) и проверки без движка считают одно и то же.</summary>
public static class UiLayout
{
    /// <summary>Холст экрана в точках разметки для окна <paramref name="viewWidth"/>×<paramref name="viewHeight"/>: масштаб по меньшей
    /// стороне (как у показа <c>ScreenFit</c>), по большей — прирост. У <see cref="UiFit.Letterbox"/> холст — сама разметка.</summary>
    public static (int Width, int Height) Canvas(UiScreen screen, double viewWidth, double viewHeight)
    {
        ArgumentNullException.ThrowIfNull(screen);
        int w = screen.Design.Width, h = screen.Design.Height;
        if (screen.Fit == UiFit.Letterbox || viewWidth <= 0 || viewHeight <= 0 || w <= 0 || h <= 0) return (w, h);
        double k = Math.Min(viewWidth / w, viewHeight / h);
        // Округление до точки разметки: холст 2133/2 = 1066,5 → 1067 (поле в полточки по краю не видно).
        return (Math.Max(w, (int)Math.Round(viewWidth / k)), Math.Max(h, (int)Math.Round(viewHeight / k)));
    }

    /// <summary>Экран, разложенный на холсте <paramref name="width"/>×<paramref name="height"/>: прирост по каждой оси элемент получает долей
    /// своего якоря (0 — левый или верхний край, ½ — середина, 1 — правый или нижний), а по оси растяжения — весь, к размеру. Разметка
    /// (<see cref="UiScreen.Design"/>) — холст. Холст не больше разметки или <see cref="UiFit.Letterbox"/> — тот же экран.</summary>
    public static UiScreen Place(UiScreen screen, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var design = screen.Design;
        int extraX = Math.Max(0, width - design.Width), extraY = Math.Max(0, height - design.Height);
        if (screen.Fit == UiFit.Letterbox || (extraX == 0 && extraY == 0)) return screen;
        return screen with
        {
            Design = design with { Width = design.Width + extraX, Height = design.Height + extraY },
            Elements = [.. screen.Elements.Select(e => e with { Rect = Placed(e, extraX, extraY) })],
        };
    }

    /// <summary>Прямоугольник элемента при приросте холста.</summary>
    public static UiRect Placed(UiElement element, int extraX, int extraY)
    {
        ArgumentNullException.ThrowIfNull(element);
        var r = element.Rect;
        var (fx, fy) = Share(element.Anchor);
        bool wide = element.Stretch is UiStretch.Width or UiStretch.Both, tall = element.Stretch is UiStretch.Height or UiStretch.Both;
        return new UiRect(
            wide ? r.X : r.X + (int)Math.Round(extraX * fx, MidpointRounding.AwayFromZero),
            tall ? r.Y : r.Y + (int)Math.Round(extraY * fy, MidpointRounding.AwayFromZero),
            wide ? r.Width + extraX : r.Width,
            tall ? r.Height + extraY : r.Height);
    }

    /// <summary>Доля прироста холста, на которую сдвигается элемент с якорем: по горизонтали и вертикали.</summary>
    public static (double X, double Y) Share(UiAnchor anchor) => anchor switch
    {
        UiAnchor.TopLeft => (0, 0), UiAnchor.Top => (0.5, 0), UiAnchor.TopRight => (1, 0),
        UiAnchor.Left => (0, 0.5), UiAnchor.Center => (0.5, 0.5), UiAnchor.Right => (1, 0.5),
        UiAnchor.BottomLeft => (0, 1), UiAnchor.Bottom => (0.5, 1), _ => (1, 1),
    };
}
