using System;
using System.Linq;
using LastEdict.Data.Map;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Окно и кадры из раздела <c>display</c> файла настроек (<see cref="SettingsFile"/>): режим окна (<see cref="DisplayMode"/>), вертикальная
/// синхронизация и предел кадров (<see cref="MapSettings.VSync"/>, <see cref="MapSettings.MaxFps"/>). Применяются при запуске игры — в
/// главной сцене <see cref="Boot"/> (сцены, которые инструменты запускают напрямую, со своим окном и разрешением, их не берут); временно, до
/// окна настроек (Э5), <c>Alt+Enter</c> переключает полный экран и окно и пишет режим в файл (<see cref="GameData"/>, <c>_Input</c>).
/// Без окна (<c>--headless</c>) и в проверках (<c>--check</c>, <c>--input</c>) ничего не применяется и не пишется: числа проверок не
/// зависят от машины и файла пользователя.
/// </summary>
public static class DisplaySettings
{
    /// <summary>Файл настроек пользователя (тот же, что у карты и меню).</summary>
    public static string FilePath => ProjectSettings.GlobalizePath("user://settings.json");

    /// <summary>Можно ли трогать окно и файл: окно есть и прогон — не проверка.</summary>
    public static bool Allowed(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return DisplayServer.GetName() != "headless" && !args.Any(a => a is "--check" or "--input");
    }

    /// <summary>Применить режим окна, синхронизацию и предел кадров.</summary>
    public static void Apply(MapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Clamped();
        SetMode(settings.Display);
        SetSize(settings);
        DisplayServer.WindowSetVsyncMode(settings.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = settings.MaxFps;
        GD.Print($"display окно={DisplayModes.Name(settings.Display)} vsync={(settings.VSync ? 1 : 0)} fps={settings.MaxFps} " +
                 $"движок: режим={DisplayServer.WindowGetMode()} без_рамки={(DisplayServer.WindowGetFlag(DisplayServer.WindowFlags.Borderless) ? 1 : 0)} " +
                 $"vsync={DisplayServer.WindowGetVsyncMode()} fps={Engine.MaxFps}");
    }

    /// <summary><c>Alt+Enter</c>: полный экран ↔ окно (по настоящему режиму окна, а не по файлу; окно без рамки — тоже «окно»: из него
    /// полный экран, обратно — обычное окно); новый режим — в файл (<see cref="MapSettings.Update"/>: прочие поля файла не трогаются).</summary>
    public static void ToggleFullscreen()
    {
        bool full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        var next = full ? DisplayMode.Windowed : DisplayMode.Fullscreen;
        SetMode(next);
        try
        {
            MapSettings.Update(FilePath, s => s with { WindowMode = DisplayModes.Name(next) });
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"DisplaySettings: режим окна не записан ({FilePath}): {e.Message}");
        }
        GD.Print($"display окно={DisplayModes.Name(next)} (Alt+Enter)");
    }

    /// <summary>Размер окна (окно, без рамки — по центру экрана, не больше его рабочей области) и разрешение полного экрана: корень рисует
    /// кадр в выбранном размере (<c>ContentScaleMode.Viewport</c>), движок растягивает его на экран с полями по форме. По умолчанию —
    /// окно как есть, полный экран в разрешении экрана.</summary>
    private static void SetSize(MapSettings settings)
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        if (settings.Display == DisplayMode.Fullscreen && DisplaySizes.Decode(settings.Resolution) is { } res)
        {
            root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            root.ContentScaleSize = new Vector2I(res.W, res.H);
            return;
        }
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        if (settings.Display == DisplayMode.Fullscreen || DisplaySizes.Decode(settings.WindowSize) is not { } want) return;
        int screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var size = new Vector2I(Math.Min(want.W, usable.Size.X), Math.Min(want.H, usable.Size.Y));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + ((usable.Size - size) / 2));
    }

    private static void SetMode(DisplayMode mode)
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, mode == DisplayMode.Borderless);
        DisplayServer.WindowSetMode(mode == DisplayMode.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }
}
