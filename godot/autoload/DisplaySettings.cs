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
        // Размер окна и разрешение полного экрана — те же, что у «Применить» (файл настроек с новым режимом).
        SetSize(MapSettings.Load(FilePath).Clamped() with { WindowMode = DisplayModes.Name(next) });
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

    /// <summary>Разрешение, в котором корень рисует кадр в полном экране (<see cref="MapSettings.Resolution"/>); null — в размере окна.
    /// Растяжением корня распоряжается <see cref="ScreenStage"/> (<see cref="RootScale"/>): он же меняет его при смене вписывания.</summary>
    public static Vector2I? RenderSize { get; private set; }

    /// <summary>Размер окна, применённый последним (<see cref="MapSettings.WindowSize"/>), и размер окна до первого выбранного — для отката
    /// на «по умолчанию».</summary>
    private static int _appliedSize;
    private static Vector2I? _sizeBefore;

    /// <summary>Растяжение корня: разрешение полного экрана — кадр в нём (<c>Viewport</c>), движок растягивает его на экран с полями по
    /// форме; иначе — по вписыванию (<paramref name="nearest"/>: растяжение холстов, иначе без растяжения).</summary>
    public static void RootScale(Window root, bool nearest)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (RenderSize is { } size)
        {
            root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            root.ContentScaleSize = size;
            return;
        }
        root.ContentScaleMode = nearest ? Window.ContentScaleModeEnum.CanvasItems : Window.ContentScaleModeEnum.Disabled;
    }

    /// <summary>Размер окна (окно, без рамки — по центру экрана, вместе с рамкой не больше его рабочей области; только когда выбор сменился,
    /// иначе размер, который пользователь задал мышью, остаётся) и разрешение полного экрана (<see cref="RenderSize"/>).</summary>
    private static void SetSize(MapSettings settings)
    {
        bool full = settings.Display == DisplayMode.Fullscreen;
        var render = full && DisplaySizes.Decode(settings.Resolution) is { } res ? new Vector2I(res.W, res.H) : (Vector2I?)null;
        if (render != RenderSize)
        {
            RenderSize = render;
            if (ScreenStage.Current is { } stage) stage.RefreshRootScale();
            else if (render is not null) RootScale(((SceneTree)Engine.GetMainLoop()).Root, nearest: false);
        }
        if (full || settings.WindowSize == _appliedSize) return;
        if (DisplaySizes.Decode(settings.WindowSize) is { } want)
        {
            _sizeBefore ??= DisplayServer.WindowGetSize();
            Resize(new Vector2I(want.W, want.H));
        }
        else if (_sizeBefore is { } before)
        {
            Resize(before);
            _sizeBefore = null;
        }
        _appliedSize = settings.WindowSize;
    }

    /// <summary>Окно этого размера по центру рабочей области экрана; с рамкой и заголовком — не больше неё.</summary>
    private static void Resize(Vector2I want)
    {
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var decorations = DisplayServer.WindowGetSizeWithDecorations() - DisplayServer.WindowGetSize();
        var room = usable.Size - decorations;
        var size = new Vector2I(Math.Min(want.X, room.X), Math.Min(want.Y, room.Y));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + ((usable.Size - size - decorations) / 2));
    }

    private static void SetMode(DisplayMode mode)
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, mode == DisplayMode.Borderless);
        DisplayServer.WindowSetMode(mode == DisplayMode.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }
}
