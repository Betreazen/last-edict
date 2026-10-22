using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Content;
using Godot;

namespace LastEdict.Godot;

/// <summary>Автозагрузка: единственное место, где сцены берут содержимое игры — базовые данные из <c>data/</c>
/// и наложенные поверх моды. Сама сборка слоёв живёт в <see cref="GameContent"/> (обычный C#, без Godot),
/// здесь только то, что без движка не сделать: разбор пути <c>res://</c>/<c>user://</c> и аргументы запуска.
///
/// Корни модов по умолчанию — <c>mods/</c> рядом с репозиторием (разработка) и <c>user://mods</c>
/// (каталог игрока). Аргументы после <c>--</c>:
/// <c>--no-mods</c> — только базовые данные (так гоняются проверки), <c>--mods &lt;каталог&gt;</c> — вместо
/// умолчаний взять свои корни (путь от каталога проекта; несколько — через <c>;</c>, слои в том же порядке). Примеры модов
/// (<c>mods/_examples</c>) по умолчанию не включены: <c>--mods ../mods/_examples</c>.
///
/// В <c>_Ready</c> читаются только <c>mod.json</c> — это дёшево, и строка <c>mods: …</c> печатается при каждом
/// запуске любой сцены: по журналу всегда видно, какие слои были включены. Сами данные читаются лениво, при
/// первом обращении к <see cref="Content"/>: сценам вроде <c>AnimTest</c> экраны не нужны, и отсутствие
/// <c>data/ui</c> не должно им мешать.</summary>
public partial class GameData : Node
{
    private static GameContent? _content;
    private static IReadOnlyList<ModInfo> _mods = [];

    /// <summary>Id подключённых модов по порядку загрузки (заголовок журнала команд — повтор берёт то же содержимое).</summary>
    public static IReadOnlyList<string> ModIds => [.. _mods.Select(m => m.Id)];
    private static string _dataDir = "";

    /// <summary>Корень распакованных кадров интерфейса (<c>assets/ui</c>). Кадры модов лежат у модов, и путь
    /// к ним <see cref="LastEdict.Data.Ui.UiAssets.PathOf"/> берёт из самого кадра.</summary>
    public static string UiAssetsRoot { get; private set; } = "";

    /// <summary>Корень графики карты и боя (<c>assets/gfx</c>, собирает <c>tools/gfx-assets/build_gfx_assets.py</c>).</summary>
    public static string GfxRoot { get; private set; } = "";

    /// <summary>База содержимого правил (<c>data/content/base</c>: юниты, атаки, предметы, заклинания, постройки, расы… — выпускает
    /// импорт таблиц оригинала, <c>docs/content-schema.md</c>); в старом импорте без неё — таблицы <c>data/rules</c>. Правила с модами
    /// поверх — <see cref="Rules"/>.</summary>
    public static string RulesDir { get; private set; } = "";

    /// <summary>Содержимое правил: база <see cref="RulesDir"/> и включённые моды поверх (их <c>content/</c>, <see cref="ContentLayers"/>).
    /// Читается при первом обращении; что моды добавили и поправили — строкой <c>rules: …</c> в журнал.</summary>
    public static ContentSet Rules => _rules ??= LoadRules();

    private static ContentSet? _rules;

    private static ContentSet LoadRules()
    {
        if (!File.Exists(Path.Combine(RulesDir, ContentSet.KindNamed("unit").File))) return ContentSource.Open(RulesDir);   // старый импорт
        var rules = ContentLayers.Load(RulesDir, _mods, out var report);
        if (report.Count > 0)
            GD.Print("rules: " + string.Join(", ", report.GroupBy(r => (r.Mod, r.Action))
                .Select(g => $"{g.Key.Mod} {g.Key.Action.ToString().ToLowerInvariant()} {g.Count()}")));
        return rules;
    }

    /// <summary>Сценарии в данных порта (<c>data/scenarios</c>, импорт <c>tools/ScenarioImport</c>): партия начинается из
    /// них, а не из <c>Exports/*.sg</c> копии игры.</summary>
    public static string ScenariosDir { get; private set; } = "";

    /// <summary>Корень роликов (<c>assets/video</c>, собирает <c>tools/video-import/build_videos.py</c>).</summary>
    public static string VideoRoot { get; private set; } = "";

    /// <summary>Корень сгенерированной графики (<c>assets</c> рядом с репозиторием; не в git). Переменная <c>D2_ASSETS_DIR</c>
    /// задаёт другой — так рабочая копия git без своей графики берёт её из основного репозитория, без ссылок на каталоги
    /// (правило проекта, <c>CLAUDE.md</c>).</summary>
    public static string AssetsRoot { get; private set; } = "";

    /// <summary>Правила партии (бой, опыт, рост юнитов) из <see cref="Rules"/>. Читаются при первом обращении.</summary>
    public static LastEdict.Data.Play.GameBook Book => _book ??= LastEdict.Data.Play.GameBook.Load(Rules);

    private static LastEdict.Data.Play.GameBook? _book;

    /// <summary>Экраны и кадры с наложенными модами. Читается при первом обращении.</summary>
    public static GameContent Content => _content ??= Report(GameContent.Load(_dataDir, _mods, GameDataDir));

    /// <summary>Язык строк игры (<c>data/ui</c>) — одна настройка на весь запуск: <c>--lang</c>, по умолчанию <c>ru</c>. Её берут все
    /// экраны (меню, окна, карта, бой), а не каждый свой разбор аргументов.</summary>
    public static string Language => _language ??= LanguageOf(OS.GetCmdlineUserArgs());

    private static string? _language;

    /// <summary>Каталог меню на языке запуска (<see cref="GameContent.CatalogIn"/>): карточки сценариев — с переводом
    /// <c>data/scenarios/lang/&lt;язык&gt;</c>, нет перевода — как в файле.</summary>
    public static LastEdict.Data.Game.GameCatalog Catalog => Content.CatalogIn(Language);

    /// <summary>Сценарий порта с переводом на язык запуска (<see cref="GameContent.Localized"/>): тексты событий, имена городов,
    /// отрядов и лордов. Партия начинается из него, так что сохранение несёт уже переведённые тексты, как у локализованной игры.</summary>
    public static LastEdict.Data.Play.ScenarioData ReadScenario(string path) =>
        Content.Localized(LastEdict.Data.Play.ScenarioFile.Read(path), Language);

    /// <summary>Строки интерфейса языка — база и моды (<see cref="GameContent.Strings"/>), один разбор на язык за запуск: экраны, карточки,
    /// меню, карта и бой берут одну таблицу.</summary>
    public static IReadOnlyDictionary<string, string> Strings(string language) => Content.Strings(language);

    /// <summary>Шрифты интерфейса — база и моды (<see cref="GameContent.Fonts"/>), читаются один раз.</summary>
    public static LastEdict.Data.Ui.UiFonts Fonts => Content.Fonts;

    /// <summary>Язык из аргументов: <c>--lang xx</c>, иначе <c>ru</c>, если импортированы русские строки (слой русификации), иначе <c>en</c>
    /// (копия игры GOG — английская).</summary>
    public static string LanguageOf(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int at = Array.IndexOf(args, "--lang");
        if (at >= 0 && at + 1 < args.Length && args[at + 1].Length > 0) return args[at + 1];
        return GameDataDir.Length == 0 || File.Exists(Path.Combine(GameDataDir, "ui", LastEdict.Data.Ui.UiStrings.FileName("ru"))) ? "ru" : "en";
    }

    /// <summary>Корень данных, выведенных из копии игры (<c>data/**</c> и <c>assets/**</c> импорта, <c>docs/publishing.md</c>): <c>--data-dir</c>,
    /// переменная <c>D2_DATA_DIR</c>, сам <see cref="Root"/>, если в нём уже есть импорт (репозиторий разработчика, переносная сборка),
    /// иначе каталог пользователя <c>user://game</c> — туда пишет импорт при первом запуске.</summary>
    public static string GameRoot { get; private set; } = "";

    /// <summary><c>data/</c> импорта (<see cref="GameRoot"/>).</summary>
    public static string GameDataDir { get; private set; } = "";

    /// <summary>Путь от корня импорта (<see cref="GameRoot"/>).</summary>
    public static string FromGame(string relative) => Path.GetFullPath(Path.Combine(GameRoot, relative));

    /// <summary>Данные игры импортированы: есть таблицы правил, экраны и опись кадров интерфейса.</summary>
    public static bool HasGameData(string root) =>
        File.Exists(Path.Combine(root, "data", "rules", "Gunits.json")) && File.Exists(Path.Combine(root, "data", "ui", "screens.json"))
        && File.Exists(Path.Combine(root, "data", "ui", "assets.json")) && Directory.Exists(Path.Combine(root, "data", "gfx"));

    /// <summary>Каталог импорта по умолчанию у игрока.</summary>
    public static string UserGameRoot => ProjectSettings.GlobalizePath("user://game");

    private static string ResolveGameRoot(string[] args)
    {
        int at = Array.IndexOf(args, "--data-dir");
        if (at >= 0 && at + 1 < args.Length) return FromProject(args[at + 1]);
        if (System.Environment.GetEnvironmentVariable("D2_DATA_DIR") is { Length: > 0 } env) return Path.GetFullPath(env);
        return HasGameData(Root) ? Root : UserGameRoot;
    }

    /// <summary>Пути к данным игры заново (после импорта при первом запуске): корень импорта, графика, правила, сценарии; разобранное
    /// содержимое и язык сбрасываются и читаются заново при первом обращении.</summary>
    public static void UseGameRoot(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        GameRoot = Path.GetFullPath(root);
        GameDataDir = Path.Combine(GameRoot, "data");
        AssetsRoot = System.Environment.GetEnvironmentVariable("D2_ASSETS_DIR") is { Length: > 0 } assets
            ? Path.GetFullPath(assets) : Path.Combine(GameRoot, "assets");
        UiAssetsRoot = Path.Combine(AssetsRoot, "ui");
        VideoRoot = Path.Combine(AssetsRoot, "video");
        GfxRoot = Path.Combine(AssetsRoot, "gfx");
        RulesDir = Directory.Exists(Path.Combine(GameDataDir, "content", "base")) ? Path.Combine(GameDataDir, "content", "base")
            : Path.Combine(GameDataDir, "rules");
        ScenariosDir = Path.Combine(GameDataDir, "scenarios");
        _content = null;
        _book = null;
        _rules = null;
        _language = null;
        // Копия игры, из которой импортировано: меню берёт из неё героев (Exports/*.ldr) и свои кампании (Campaign/*.csg).
        // В корне проекта (репозиторий разработчика) копию игры задают D2_GAME_DIR и соседний game-pristine, а не отметка импорта.
        if (GameRoot != Root && ImportedFrom(GameRoot) is { } game) LastEdict.Data.GamePaths.Use(game);
    }

    /// <summary>Папка копии игры из отметки импорта (<c>import.json</c>), если она ещё есть на диске.</summary>
    public static string? ImportedFrom(string root)
    {
        string marker = Path.Combine(root, LastEdict.Data.Import.GameImporter.MarkerFile);
        if (!File.Exists(marker)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(marker));
            return doc.RootElement.TryGetProperty("game", out var g) && g.GetString() is { Length: > 0 } dir && Directory.Exists(dir) ? dir : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Каталог проекта Godot: в редакторе — <c>res://</c> (<c>godot/</c>), в экспорте — каталог исполняемого файла
    /// (<c>res://</c> там внутри пакета и пути на диске не имеет). От него считаются относительные пути аргументов.</summary>
    public static string ProjectDir => _projectDir ??= OS.HasFeature("editor")
        ? ProjectSettings.GlobalizePath("res://")
        : Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";

    private static string? _projectDir;

    /// <summary>Корень данных порта (<c>data/</c>, <c>mods/</c>, <c>assets/</c>, <c>.git</c>): в редакторе — репозиторий (на уровень выше
    /// <c>godot/</c>), в экспорте — каталог исполняемого файла, рядом с которым лежат <c>data/</c> и <c>assets/</c>. Единственный разбор
    /// корня: остальные берут пути через <see cref="FromRoot"/>. Графику можно взять из другого места переменной <c>D2_ASSETS_DIR</c>.</summary>
    public static string Root => _root ??= OS.HasFeature("editor")
        ? Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."))
        : ProjectDir;

    private static string? _root;

    /// <summary>Путь от корня данных (<see cref="Root"/>).</summary>
    public static string FromRoot(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    /// <summary>Путь аргумента: абсолютный — как есть, относительный — от каталога проекта (<see cref="ProjectDir"/>).</summary>
    public static string FromProject(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectDir, path));

    /// <summary>Окно проверки без окна: <c>--window-size ШxВ</c>, по умолчанию 1280×720; с окном — null.</summary>
    public static Vector2I? HeadlessWindow
    {
        get
        {
            if (DisplayServer.GetName() != "headless") return null;
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--window-size");
            string[] wh = at >= 0 && at + 1 < args.Length ? args[at + 1].Split('x') : [];
            return wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h) && w > 0 && h > 0
                ? new Vector2I(w, h) : new Vector2I(1280, 720);
        }
    }

    public override void _Ready()
    {
        PortProbe.Attach(this);   // зонд порта — первым: перехват журнала движка до всего остального
        _ = GameLog.ToOutput;   // правило болтливого вывода — здесь, на главном потоке, а не при первой строке с потока ИИ
        // Поля окна шире или выше экрана 800×600 (экраны вписываются по меньшей стороне, UiScreenView.Letterbox) — чёрные, как поля
        // игры, растянутой на широкий экран, а не серый цвет очистки Godot по умолчанию. Цвет — кодом: настройку
        // rendering/environment/defaults/default_clear_color в project.godot не трогаем.
        RenderingServer.SetDefaultClearColor(Colors.Black);
        _dataDir = FromRoot("data");
        string[] args = OS.GetCmdlineUserArgs();
        if (HeadlessWindow is { } window)
        {
            // Без окна корень у Godot 64×64 — раскладка экранов по форме окна (ui/layout.json) считалась бы на квадрате. Проверки идут на
            // окне --window-size (по умолчанию основное 16:9, 1280×720): вьюпорт корня — ровно этого размера.
            var root = GetTree().Root;
            root.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
            root.ContentScaleSize = window;
        }
        UseGameRoot(ResolveGameRoot(args));
        int at = Array.IndexOf(args, "--mods");
        string[] roots =
            args.Contains("--no-mods") ? []
            : at >= 0 && at + 1 < args.Length ? [.. args[at + 1].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(FromProject)]
            : [FromRoot("mods"), ProjectSettings.GlobalizePath("user://mods")];

        try
        {
            _mods = GameContent.Discover(roots);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            GD.PrintErr($"GameData: слой мода не прочитан — {e.Message}");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"mods: {(_mods.Count == 0 ? "-" : string.Join(", ", _mods.Select(m => m.Id)))}");
    }

    /// <summary><c>Alt+Enter</c> — полный экран ↔ окно (<see cref="DisplaySettings.ToggleFullscreen"/>), во всех сценах. Экраны получают ввод
    /// только из <c>_UnhandledInput</c> корня (<see cref="ScreenStage"/> переправляет его в буфер, <c>PushInput</c>), то есть после всех
    /// <c>_Input</c>, и этот тоже: событие помечается обработанным, и экраны не видят его как Enter. Enter без Alt идёт экранам как прежде.
    /// Без окна и в проверках — нет.</summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, AltPressed: true } key || key.Keycode is not (Key.Enter or Key.KpEnter)) return;
        if (!DisplaySettings.Allowed(OS.GetCmdlineUserArgs())) return;
        DisplaySettings.ToggleFullscreen();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Выход из процесса (автозагрузка покидает дерево последней — после главной сцены, но до остановки языка C#). Здесь, пока
    /// движок и язык C# живы: (1) дождаться фоновых разборов картинок (<see cref="GfxTextures.Stop"/>); (2) прогнать сборку мусора и
    /// финализаторы. Иначе обёртки C# объектов Godot (<see cref="Image"/>, события ввода и т. п.), которые уже мусор, но ещё не
    /// финализированы, финализирует поток финализаторов во время <c>CSharpLanguage::finalize</c> — FATAL «!rc_owner» /
    /// «script_bindings.is_empty()» в csharp_script.cpp, 0xC000001D, 0xC0000005 на выходе (разбор — docs/godot-notes.md).</summary>
    public override void _ExitTree()
    {
        if (!GfxTextures.Stop(TimeSpan.FromSeconds(10)))
            GD.PrintErr("GameData: фоновые разборы картинок не кончились за 10 с до выхода");
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static GameContent Report(GameContent content)
    {
        GD.Print($"content: screens={content.Screens.Count} assets={content.Assets.Count}");
        return content;
    }
}
