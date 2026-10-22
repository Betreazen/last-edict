namespace LastEdict.Data.Ui;

/// <summary>Размеры окна и разрешения полного экрана (настройки <c>display.windowSize</c>, <c>display.resolution</c>; ответ пользователя 22.10).
/// В файле и в списке настроек размер — одно число <c>ширина × 10000 + высота</c> (<see cref="Encode"/>); 0 — по умолчанию: окно — каким
/// его открыл движок, полный экран — в разрешении экрана. Полный экран у Godot не меняет видеорежим монитора: выбранное разрешение — размер,
/// в котором игра рисует кадр, растянутый на экран.</summary>
public static class DisplaySizes
{
    /// <summary>Размеры списка: 16:9, 16:10, 4:3 — по возрастанию внутри формы.</summary>
    public static readonly IReadOnlyList<(int W, int H)> Common =
    [
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160),
        (1280, 800), (1440, 900), (1680, 1050), (1920, 1200), (2560, 1600),
        (1024, 768), (1280, 960), (1600, 1200),
    ];

    public static int Encode(int width, int height) => (width * 10000) + height;

    /// <summary>Размер по числу настройки; 0 и меньше — null.</summary>
    public static (int W, int H)? Decode(int value) => value > 0 ? (value / 10000, value % 10000) : null;

    /// <summary>Число из списка (<see cref="Common"/>) или 0; прочее (правка файла руками) — 0.</summary>
    public static int Known(int value) => Common.Any(s => Encode(s.W, s.H) == value) ? value : 0;

    /// <summary>Подпись размера в списке настроек.</summary>
    public static string Label((int W, int H) size) => $"{size.W} × {size.H}";
}
