namespace LastEdict.Data.Ui;

/// <summary>Режим окна игры (настройка порта <c>display.windowMode</c> файла настроек, <see cref="LastEdict.Data.Map.SettingsFile"/>): применяется
/// при запуске игры и переключается <c>Alt+Enter</c> (полный экран ↔ окно).</summary>
public enum DisplayMode
{
    /// <summary>Обычное окно с рамкой (по умолчанию).</summary>
    Windowed,

    /// <summary>Окно без рамки и заголовка того же размера.</summary>
    Borderless,

    /// <summary>Полный экран (у Godot — окно без рамки во весь экран, без смены видеорежима: переключение задач не мигает).</summary>
    Fullscreen,
}

/// <summary>Имена режимов окна в файле настроек.</summary>
public static class DisplayModes
{
    /// <summary>Режим по имени файла (<c>windowed</c>, <c>borderless</c>, <c>fullscreen</c>, без учёта регистра); неизвестное — null.</summary>
    public static DisplayMode? Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "windowed" => DisplayMode.Windowed,
            "borderless" => DisplayMode.Borderless,
            "fullscreen" => DisplayMode.Fullscreen,
            _ => null,
        };

    /// <summary>Имя режима в файле — строчными.</summary>
    public static string Name(DisplayMode mode) => mode.ToString().ToLowerInvariant();
}
