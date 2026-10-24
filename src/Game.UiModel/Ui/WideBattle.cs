namespace LastEdict.Data.Ui;

/// <summary>Как стоят юниты на широком поле боя (<see cref="WideBattle"/>).</summary>
public enum WideUnits
{
    /// <summary>Места врапера DisciplesGL (холст 990, <c>DLG_BATTLE_B</c>) — по середине холста.</summary>
    Wrapper,

    /// <summary>Те же места, разведённые на всю ширину поля между колонками отрядов.</summary>
    Spread,
}

/// <summary>
/// Широкий бой (ответ пользователя 24.10, «как во врапере»): на холсте не уже <see cref="MinWidth"/> (16:9 и шире) разметка
/// <c>DLG_BATTLE_A</c> раскладывается на всю ширину — колонки отрядов у краёв холста (обе видны всегда), кнопки и середина нижней панели —
/// по центру, подписи крупных портретов — до середины, нижняя панель собирается из краёв и центра своей картинки со вставками (кадр
/// <c>Widen:</c>, без растяжения), юниты — на местах врапера (<c>DLG_BATTLE_B</c>, холст 990) по середине или разведённые по полю.
/// Уже 990 (16:10, 4:3) — бой как в оригинале.
/// </summary>
public static class WideBattle
{
    /// <summary>Ширина холста, с которой бой широкий (у врапера — 990: колонки 174 и 173, поле между ними 643).</summary>
    public const int MinWidth = 990;

    /// <summary>Колонки отрядов врапера у краёв холста.</summary>
    public const int LeftColumn = 174, RightColumn = 173;

    private const int ScreenWidth = 800, WrapperWidth = 990;

    /// <summary>Места юнитов врапера (холст 990): левый верхний угол, 80×80.</summary>
    private static readonly IReadOnlyDictionary<string, (int X, int Y)> WrapperUnits = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["IMG_AUNIT1"] = (531, 157), ["IMG_AUNIT2"] = (432, 93), ["IMG_AUNIT3"] = (393, 202),
        ["IMG_AUNIT4"] = (298, 138), ["IMG_AUNIT5"] = (257, 247), ["IMG_AUNIT6"] = (167, 181),
        ["IMG_DUNIT1"] = (622, 216), ["IMG_DUNIT2"] = (731, 287), ["IMG_DUNIT3"] = (484, 264),
        ["IMG_DUNIT4"] = (588, 335), ["IMG_DUNIT5"] = (347, 311), ["IMG_DUNIT6"] = (446, 382),
    };

    /// <summary>Нижняя панель (<c>DLG_BATTLE_A_MAINCOMBATBG</c>, 800×200): левый край до 250, вставка левой полосы 230…250, центр с кнопками
    /// 300…500, вставка правой полосы 520…540, правый край от 550.</summary>
    public const string BottomCuts = "250,230,300,500,520,550";

    /// <summary>Подпись крупного портрета — от его круга до середины нижней панели.</summary>
    private const int TextFromEdge = 192, TextToCenter = 95;

    /// <summary>Холст ширины <paramref name="width"/> (высота 600) — для широкого боя.</summary>
    public static bool Wide(int width) => width >= MinWidth;

    /// <summary>Ширина разметки боя на холсте <paramref name="canvas"/>: уже <see cref="MinWidth"/> — экран оригинала; места врапера — его
    /// холст 990 (остальное — поля прироста); разведённые — весь холст.</summary>
    public static int Width(int canvas, WideUnits units) => !Wide(canvas) ? ScreenWidth : units == WideUnits.Wrapper ? WrapperWidth : canvas;

    /// <summary>Разметка боя на холсте <paramref name="width"/>×600. Уже <see cref="MinWidth"/> — та же разметка.</summary>
    public static UiScreen Place(UiScreen screen, int width, WideUnits units)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (!Wide(width)) return screen;
        int right = width - ScreenWidth, center = (width - ScreenWidth) / 2;
        var elements = screen.Elements.Select(e => e with { Rect = Rect(e, width, right, center, units) }).Select(e => e.Id == "IMG_BIGFACESBG"
            && e.Image is { Length: > 0 } image ? e with { Image = $"Widen:{image}@{width}/{BottomCuts}" } : e).ToList();
        return screen with { Design = screen.Design with { Width = width }, Elements = elements };
    }

    private static UiRect Rect(UiElement e, int width, int right, int center, WideUnits units)
    {
        var r = e.Rect;
        if (WrapperUnits.TryGetValue(e.Id, out var at)) return r with { X = UnitX(at.X, width, units), Y = at.Y };
        return e.Id switch
        {
            "IMG_BIGFACESBG" => r with { X = 0, Width = width },
            "IMG_LBIGTEXT" => r with { X = TextFromEdge, Width = (width / 2) - TextToCenter - TextFromEdge },
            "IMG_RBIGTEXT" => r with { X = (width / 2) + TextToCenter, Width = width - TextFromEdge - ((width / 2) + TextToCenter) },
            // Переключатель групп: обе колонки видны всегда (у врапера он погашен) — за край холста.
            "TOG_RIGHTUNITS" => r with { X = -1000 },
            _ when e.Id.StartsWith("IMG_R", StringComparison.Ordinal) => r with { X = r.X + right },
            _ when e.Id.StartsWith("BTN_", StringComparison.Ordinal) || e.Id == "TOG_AUTOBATTLE" => r with { X = r.X + center },
            _ => r,
        };
    }

    /// <summary>x места юнита: у врапера — его место, сдвинутое на середину холста; разведённое — середина места пропорционально по полю
    /// между колонками (у врапера поле 174…817 из 990).</summary>
    private static int UnitX(int x, int width, WideUnits units)
    {
        if (units == WideUnits.Wrapper) return x + ((width - WrapperWidth) / 2);
        double field = WrapperWidth - LeftColumn - RightColumn, now = width - LeftColumn - RightColumn;
        double centre = LeftColumn + ((x + 40 - LeftColumn) * now / field);
        return (int)Math.Round(centre) - 40;
    }
}
