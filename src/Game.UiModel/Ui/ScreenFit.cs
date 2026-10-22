namespace D2.Data.Ui;

/// <summary>Как экран игры 800×600 вписывается в окно (настройка порта <c>Scaling</c> в <c>settings.json</c>, ключ <c>--scale</c>).</summary>
public enum ScreenScaling
{
    /// <summary>«Чёткий билинейный» (sharp bilinear, по умолчанию): экраны рисуются с целым увеличением ближайшим соседом во внеэкранный
    /// буфер (целое n — наименьшее не меньше масштаба окна), буфер сжимается в окно билинейно. Все линии растра одной толщины,
    /// смягчены только границы точек.</summary>
    Sharp,

    /// <summary>Целое увеличение ближайшим соседом (наибольшее целое, не больше масштаба окна), остаток окна — чёрная рамка.</summary>
    Integer,

    /// <summary>Как до 11.10.2026: дробный масштаб узлом с фильтром Nearest — линии растра то в одну, то в две точки.</summary>
    Nearest,
}

/// <summary>
/// Раскладка показа: размер внеэкранного буфера, в котором рисуются экраны (они сами вписывают 800×600 в его размер по меньшей стороне,
/// <c>UiScreenView.Letterbox</c>), и как буфер ложится в окно — масштаб, левый верхний угол и фильтр.
///
/// <para>Буфер — той же формы, что окно (поля шире или выше 800×600 у экранов остаются чёрными, как прежде), а по стороне, которая
/// ограничивает масштаб, ровно <c>800·n</c> или <c>600·n</c> точек: экраны в нём получают масштаб ровно <c>n</c>, целый. Источник
/// приёма — скилл gdscript-knowledge (rendering.md: пиксель-арт, целый масштаб; ui-input.md: несколько разрешений) и шейдер sharp-bilinear
/// libretro (целое предувеличение ближайшим соседом + билинейное приведение к цели); здесь приведение — сжатие из n ≥ k, поэтому каждую
/// точку экрана покрывает не меньше одной точки окна.</para>
/// </summary>
public readonly record struct ScreenFit(int StageWidth, int StageHeight, float Scale, int X, int Y, bool Filtered)
{
    public const int DesignWidth = 800, DesignHeight = 600;

    /// <summary>Допуск при округлении масштаба окна до целого: 1920×1200 даёт ровно 2, а не 2,0000001.</summary>
    private const float Epsilon = 1e-4f;

    /// <summary>Наибольшее увеличение в буфере: дальше буфер рос бы квадратично (окно 8K — 6400×4800 при n = 8), а сжатие по площади
    /// и так держит вес линий; выше — буфер увеличивается в окно (шейдер берёт ближайшую точку).</summary>
    public const int MaxStageScale = 6;

    /// <summary>Раскладка для окна <paramref name="width"/>×<paramref name="height"/> (точки корневого вьюпорта).</summary>
    public static ScreenFit For(int width, int height, ScreenScaling mode)
    {
        if (width <= 0 || height <= 0) return new ScreenFit(Math.Max(1, width), Math.Max(1, height), 1f, 0, 0, false);
        if (mode == ScreenScaling.Nearest) return new ScreenFit(width, height, 1f, 0, 0, false);
        float k = Math.Min(width / (float)DesignWidth, height / (float)DesignHeight);
        // Окно меньше 800×600 целым масштабом не впишется (обрезалось бы) — тогда как чёткий: сжатие по площади.
        if (mode == ScreenScaling.Integer && k < 1 - Epsilon) mode = ScreenScaling.Sharp;
        int n = mode == ScreenScaling.Sharp
            ? Math.Clamp((int)Math.Ceiling(k - Epsilon), 1, MaxStageScale)
            : Math.Max(1, (int)Math.Floor(k + Epsilon));
        bool byWidth = width * DesignHeight <= height * DesignWidth;
        int stageW = byWidth ? DesignWidth * n : (int)Math.Round(width * (double)(DesignHeight * n) / height);
        int stageH = byWidth ? (int)Math.Round(height * (double)(DesignWidth * n) / width) : DesignHeight * n;
        // Чёткий — буфер сжимается в окно ровно по ограничивающей стороне; целый — показывается точка в точку.
        float scale = mode == ScreenScaling.Sharp ? (byWidth ? width / (float)stageW : height / (float)stageH) : 1f;
        int x = (int)Math.Round((width - (stageW * scale)) / 2), y = (int)Math.Round((height - (stageH * scale)) / 2);
        return new ScreenFit(stageW, stageH, scale, x, y, mode == ScreenScaling.Sharp && Math.Abs(scale - 1f) > Epsilon);
    }

    /// <summary>Режим по имени (<c>sharp</c>, <c>integer</c>, <c>nearest</c>, регистр не важен); неизвестное или пустое — null.</summary>
    public static ScreenScaling? Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "sharp" => ScreenScaling.Sharp,
            "integer" => ScreenScaling.Integer,
            "nearest" => ScreenScaling.Nearest,
            _ => null,
        };
}
