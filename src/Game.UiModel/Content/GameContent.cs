using System.Text.Json;
using LastEdict.Data.Game;
using LastEdict.Data.Gfx;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Content;

/// <summary>Содержимое игры: базовый набор данных из <c>data/</c> плюс наложенные поверх моды.
///
/// Слои складываются теми же <see cref="UiScreens.Merge"/>, <see cref="UiAssets.Merge"/> и <see cref="UiFlows.Merge"/>:
/// экран опознаётся по <c>Id</c>, кадр — по паре архив+имя, переход — по паре экран+элемент, последний слой выигрывает. Порядок модов —
/// порядок корней, внутри корня — по имени каталога.
///
/// Кадры мода лежат в его собственном <c>assets/ui</c>, поэтому при наложении их <see cref="UiAsset.File"/>
/// разворачивается в полный путь: <see cref="UiAssets.PathOf"/> склеивает через <c>Path.Combine</c>,
/// а тот отдаёт абсолютный путь как есть и базовый корень не подставляет. Сцене от этого ничего знать не надо.</summary>
public sealed class GameContent
{
    private IReadOnlyDictionary<string, UiAsset>? _byName;

    private GameContent(IReadOnlyList<UiScreen> screens, IReadOnlyList<UiAsset> assets, UiFlow flow,
        UiDefaults defaults, GameCatalog catalog, UiTransitions transitions, IReadOnlyList<UiVideo> videos,
        GfxAssets gfx, MapArtData mapArt, IReadOnlyList<ModInfo> mods, string dataDir, string gameDataDir,
        LastEdict.Data.Input.InputConfig inputSetup)
    {
        InputSetup = inputSetup;
        Input = LastEdict.Data.Input.InputActions.Build(inputSetup, screens);
        _dataDir = dataDir;
        _gameDir = gameDataDir;
        Gfx = gfx;
        MapArt = mapArt;
        Videos = videos;
        Transitions = transitions;
        _catalog = catalog;
        Screens = screens;
        Assets = assets;
        Flow = flow;
        Defaults = defaults;
        Mods = mods;
    }

    public IReadOnlyList<UiScreen> Screens { get; }
    public IReadOnlyList<UiAsset> Assets { get; }

    /// <summary>Действия ввода (<c>ui/input.json</c> и горячие клавиши разметки экранов, <see cref="LastEdict.Data.Input.InputActions"/>):
    /// что переназначает вкладка «Управление». Нет файла — только выведенные из разметки без групп, то есть ничего.</summary>
    public IReadOnlyList<LastEdict.Data.Input.InputAction> Input { get; }

    /// <summary>Данные ввода целиком (<c>ui/input.json</c> с модами): группы, экраны стрелок.</summary>
    public LastEdict.Data.Input.InputConfig InputSetup { get; }

    /// <summary>Граф экранов (<c>ui/flow.json</c>, выведенный из журналов, плюс ручные дополнения
    /// <c>ui/flow.manual.json</c> — переходы, которых зонд не видел): какой элемент куда ведёт. Нет файла — граф пуст.</summary>
    public UiFlow Flow { get; }

    /// <summary>Каталог меню (<c>game/races.json</c>, <c>game/scenarios.json</c>): расы, лорды, сценарии.</summary>
    public GameCatalog Catalog
    {
        get
        {
            lock (_catalogLock)
            {
                if (_withRaces is not null) return _withRaces;
                if (_catalogError is not null) throw new InvalidDataException(_catalogError);
                if (Rules is not { } rules) return _withRaces = _catalog;
                // Расы меню — из содержимого (Э5): расы модов есть и в выборе расы.
                var catalog = _catalog.WithRaces(LastEdict.Data.Game.MenuRaces.From(rules()));
                if (catalog.Problems() is { Count: > 0 } problems)
                {
                    _catalogError = $"расы содержимого для меню: {string.Join("; ", problems)}";
                    throw new InvalidDataException(_catalogError);
                }
                return _withRaces = catalog;
            }
        }
    }

    private readonly GameCatalog _catalog;
    private GameCatalog? _withRaces;
    private string? _catalogError;
    private readonly object _catalogLock = new();

    /// <summary>Содержимое правил (база и моды, <see cref="LastEdict.Data.Content.ContentSet"/>): из него — расы меню (<see cref="Catalog"/>).
    /// Не задано — расы из данных импорта (<c>game/races.json</c>; проверки без содержимого).</summary>
    public Func<LastEdict.Data.Content.ContentSet>? Rules { get; init; }

    /// <summary>Графика карты и боя (<c>gfx/*.json</c>, файлы — <c>assets/gfx</c>). Нет описей — пусто.</summary>
    public GfxAssets Gfx { get; }

    /// <summary>Кадры картинок сущностей модов («Архив:ИМЯ», <see cref="EntityArt"/>): свой портрет юнита мода главнее графики базового
    /// юнита (<see cref="EntityArt.Prefer"/>), графика оригинала так не подменяется.</summary>
    public IReadOnlySet<string> EntityFrames { get; private init; } = new HashSet<string>();

    /// <summary>Данные отрисовки карты (<c>map/*.json</c>): слои, следы ориентиров, бонусы хода. Нет файлов — пусто.</summary>
    public MapArtData MapArt { get; }

    /// <summary>Опись роликов (<c>video/videos.json</c>, файлы — <c>assets/video</c>). Нет файла — пусто.</summary>
    public IReadOnlyList<UiVideo> Videos { get; }

    /// <summary>Поиск ролика по имени без учёта регистра.</summary>
    public IReadOnlyDictionary<string, UiVideo> VideosByName => _videosByName ??= UiVideos.ByName(Videos);

    private IReadOnlyDictionary<string, UiVideo>? _videosByName;

    /// <summary>Ролики смены экранов меню (<c>ui/transitions.json</c>). Нет файла — смена без роликов.</summary>
    public UiTransitions Transitions { get; }

    /// <summary>Начальные значения экранов (<c>ui/defaults.json</c>). Нет файла — пусто.</summary>
    public UiDefaults Defaults { get; }

    /// <summary>Моды, попавшие в этот набор, в порядке наложения.</summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    /// <summary>Поиск кадра по имени, как на него ссылаются экраны.</summary>
    public IReadOnlyDictionary<string, UiAsset> AssetsByName => _byName ??= UiAssets.ByName(Assets);

    private readonly string _dataDir;

    /// <summary>Данные, выведенные из игры (импорт): в репозитории разработчика — тот же <c>data/</c>, у игрока — <c>data/</c> каталога импорта.</summary>
    private readonly string _gameDir;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stringsLock = new();
    private UiFonts? _fonts;

    /// <summary>Строки интерфейса языка (<c>ui/strings.&lt;язык&gt;.json</c> базы и модов, <see cref="UiStrings.LoadLayers"/>), по одному разбору
    /// на язык за запуск: экраны, карточки, карта и бой берут одну и ту же таблицу, а не разбирают 270 КБ на каждое окно. Таблица
    /// неизменяемая (<see cref="IReadOnlyDictionary{TKey, TValue}"/>), делить её безопасно.</summary>
    /// <exception cref="FileNotFoundException">Строк нет ни для языка, ни для запасного ни в одном слое.</exception>
    /// <exception cref="InvalidDataException">Файл строк мода не читается.</exception>
    public IReadOnlyDictionary<string, string> Strings(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        lock (_stringsLock)
        {
            if (_strings.TryGetValue(language, out var cached)) return cached;
            // Строки игры (импорт), затем свои строки Last Edict (data/ui/own: окно настроек и прочее новое, ключи LE_*), затем моды.
            var dirs = new List<string> { Path.Combine(_gameDir, "ui"), Path.Combine(_dataDir, "ui", "own") };
            dirs.AddRange(Mods.Select(m => Path.Combine(m.Dir, "data", "ui")));
            IReadOnlyDictionary<string, string> loaded;
            try
            {
                loaded = UiStrings.LoadLayers(dirs, language);
                // Строки сущностей модов «папка = сущность» (автоматические ключи <id>#name, EntityFolders) — поверх.
                if (Mods.Select(m => EntityFolders.Strings(m, language)).Where(s => s.Count > 0).ToList() is { Count: > 0 } entity)
                {
                    var merged = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, text) in entity.SelectMany(s => s)) merged[key] = text;
                    loaded = merged;
                }
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"строки «{language}» ({string.Join(", ", dirs)}): {e.Message}", e);
            }
            return _strings[language] = loaded;
        }
    }

    private readonly Dictionary<string, GameCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Каталоги сценариев слоёв по порядку наложения: база (<c>data/scenarios</c>), затем <c>data/scenarios</c> модов.</summary>
    public IReadOnlyList<string> ScenarioDirs =>
        [Path.Combine(_gameDir, "scenarios"), .. Mods.Select(m => Path.Combine(m.Dir, "data", "scenarios"))];

    /// <summary>Файл сценария <paramref name="id"/> (<c>data/scenarios/&lt;id&gt;.json</c>): последний слой, где он есть — мод может добавить
    /// свои сценарии (<c>mods/port-synth</c> — синтетические карты для проверок зондом) и заменить базовый; нигде нет — путь в базе.</summary>
    public string ScenarioPath(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        foreach (string dir in ScenarioDirs.Reverse())
        {
            string path = ScenarioFile.PathOf(dir, id);
            if (File.Exists(path)) return path;
        }
        return ScenarioFile.PathOf(ScenarioDirs[0], id);
    }

    /// <summary>Перевод сценария <paramref name="id"/> на язык (<c>scenarios/lang/&lt;язык&gt;/&lt;id&gt;.json</c> базы и модов,
    /// <see cref="ScenarioTexts.Load"/>); перевода нет — пусто, и сценарий остаётся как в файле.</summary>
    public IReadOnlyDictionary<string, string> ScenarioTextsOf(string language, string id) =>
        ScenarioTexts.Load(ScenarioDirs, language, id);

    /// <summary>Сценарий с переводом на язык (<see cref="ScenarioTexts.Apply(ScenarioData, IReadOnlyDictionary{string, string})"/>).</summary>
    public ScenarioData Localized(ScenarioData scenario, string language)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return ScenarioTexts.Apply(scenario, ScenarioTextsOf(language, scenario.Id));
    }

    /// <summary>Каталог меню (<see cref="Catalog"/>) с карточками сценариев на языке: название, описание, брифинг, эпилог квестов и
    /// глав саг. Собирается один раз на язык за запуск.</summary>
    public GameCatalog CatalogIn(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        lock (_stringsLock)
        {
            if (_catalogs.TryGetValue(language, out var cached)) return cached;
            return _catalogs[language] = ScenarioTexts.Apply(Catalog, id => ScenarioTextsOf(language, id));
        }
    }

    /// <summary>Шрифты интерфейса (<c>ui/fonts.json</c> базы, поверх — <c>data/ui/fonts.json</c> модов, <see cref="UiFonts.Merge"/>); читаются
    /// один раз. Пути начертаний базы — от корня данных порта (каталог над <c>data/</c>), модов — полные.</summary>
    /// <exception cref="InvalidDataException">Набор шрифтов мода не читается.</exception>
    public UiFonts Fonts
    {
        get
        {
            if (_fonts is not null) return _fonts;
            var fonts = UiFonts.Load(Path.Combine(_dataDir, "ui", "fonts.json"));
            foreach (var mod in Mods)
            {
                string path = Path.Combine(mod.Dir, "data", "ui", "fonts.json");
                if (!File.Exists(path)) continue;
                try
                {
                    fonts = UiFonts.Merge(fonts, File.ReadAllText(path), mod.Dir);
                }
                catch (Exception e) when (e is JsonException or FormatException)
                {
                    throw new InvalidDataException($"мод {mod.Id} ({path}): {e.Message}", e);
                }
            }
            return _fonts = fonts;
        }
    }

    /// <summary>Базовый набор с наложенными поверх модами. Наш контент (<c>ui/flow.json</c>, <c>ui/flow.manual.json</c>, <c>ui/defaults.json</c>, <c>ui/layout.json</c>,
    /// <c>ui/transitions.json</c>, <c>ui/fonts.json</c>) — из <paramref name="dataDir"/>; выведенное из игры (<c>ui/screens.json</c>,
    /// <c>ui/assets.json</c>, строки, <c>game</c>, <c>gfx</c>, <c>map</c>, <c>video</c>, <c>scenarios</c>) — из <paramref name="gameDataDir"/>
    /// (импорт из копии игры, <c>docs/publishing.md</c>; не задан — тот же <paramref name="dataDir"/>, как в репозитории разработчика).</summary>
    /// <exception cref="InvalidDataException">Слой мода не читается.</exception>
    /// <param name="rules">Содержимое правил — для рас меню (<see cref="Rules"/>); нет — расы данных импорта.</param>
    public static GameContent Load(string dataDir, IEnumerable<ModInfo>? mods = null, string? gameDataDir = null,
                                   Func<LastEdict.Data.Content.ContentSet>? rules = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDir);
        string gameDir = string.IsNullOrEmpty(gameDataDir) ? dataDir : gameDataDir;
        var screens = UiScreens.Load(Path.Combine(gameDir, "ui", "screens.json"));
        // Свои экраны Last Edict (data/ui/own/screens.json: окно настроек и прочее новое) — поверх импорта, под модами.
        string ownScreens = Path.Combine(dataDir, "ui", "own", "screens.json");
        if (File.Exists(ownScreens)) screens = UiScreens.Merge(screens, UiScreens.Load(ownScreens));
        var assets = UiAssets.Load(Path.Combine(gameDir, "ui", "assets.json"));
        var flow = LoadFlow(Path.Combine(dataDir, "ui", "flow.json")) ?? new UiFlow();
        if (LoadFlow(Path.Combine(dataDir, "ui", "flow.manual.json")) is { } manual) flow = UiFlows.Merge(flow, manual);
        var defaults = LoadDefaults(Path.Combine(dataDir, "ui", "defaults.json")) ?? UiDefaults.Empty;
        var layout = UiLayouts.Load(Path.Combine(dataDir, "ui", "layout.json")) ?? new Dictionary<string, UiLayoutRule>();
        var input = LastEdict.Data.Input.InputConfig.Load(Path.Combine(dataDir, "ui", "input.json")) ?? new LastEdict.Data.Input.InputConfig();
        var catalog = GameCatalog.Load(Path.Combine(gameDir, "game"));
        var transitions = LoadTransitions(Path.Combine(dataDir, "ui", "transitions.json")) ?? UiTransitions.Empty;
        string videosJson = Path.Combine(gameDir, "video", "videos.json");
        IReadOnlyList<UiVideo> videos = File.Exists(videosJson) ? UiVideos.Load(videosJson) : [];
        var gfx = GfxAssets.Load(Path.Combine(gameDir, "gfx"));
        var mapArt = MapArtData.Load(Path.Combine(gameDir, "map"));
        var entityFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var layers = mods?.ToList() ?? [];
        foreach (var mod in layers)
        {
            string screensJson = Path.Combine(mod.Dir, "data", "ui", "screens.json");
            string assetsJson = Path.Combine(mod.Dir, "data", "ui", "assets.json");
            try
            {
                if (File.Exists(screensJson)) screens = UiScreens.Merge(screens, UiScreens.Load(screensJson));
                if (LoadFlow(Path.Combine(mod.Dir, "data", "ui", "flow.json")) is { } modFlow)
                    flow = UiFlows.Merge(flow, modFlow);
                if (UiLayouts.Load(Path.Combine(mod.Dir, "data", "ui", "layout.json")) is { } modLayout)
                    layout = UiLayouts.Merge(layout, modLayout);
                if (LastEdict.Data.Input.InputConfig.Load(Path.Combine(mod.Dir, "data", "ui", "input.json")) is { } modInput)
                    input = LastEdict.Data.Input.InputConfig.Merge(input, modInput);
                if (LoadDefaults(Path.Combine(mod.Dir, "data", "ui", "defaults.json")) is { } modDefaults)
                    defaults = UiDefaults.Merge(defaults, modDefaults);
                catalog = GameCatalog.Merge(catalog, GameCatalog.Load(Path.Combine(mod.Dir, "data", "game")));
                if (LoadTransitions(Path.Combine(mod.Dir, "data", "ui", "transitions.json")) is { } modTransitions)
                    transitions = UiTransitions.Merge(transitions, modTransitions);
                gfx = GfxAssets.Merge(gfx, GfxAssets.Load(Path.Combine(mod.Dir, "data", "gfx")),
                                      Path.Combine(mod.Dir, "assets", "gfx"));
                var entityArt = EntityIcons(mod);
                gfx = GfxAssets.Merge(gfx, entityArt, mod.Dir);
                foreach (var archive in entityArt.Archives)
                    foreach (var image in archive.Images) entityFrames.Add($"{archive.Name}:{image.Name}");
                mapArt = MapArtData.Merge(mapArt, MapArtData.Load(Path.Combine(mod.Dir, "data", "map")));
                string modVideos = Path.Combine(mod.Dir, "data", "video", "videos.json");
                if (File.Exists(modVideos))
                    videos = UiVideos.Merge(videos, RootedVideos(UiVideos.Load(modVideos), Path.Combine(mod.Dir, "assets", "video")));
                if (File.Exists(assetsJson))
                    assets = UiAssets.Merge(assets,
                        Rooted(UiAssets.Load(assetsJson), Path.Combine(mod.Dir, "assets", "ui")));
            }
            catch (Exception e) when (e is JsonException or FormatException or IOException or KeyNotFoundException)
            {
                throw new InvalidDataException($"мод {mod.Id} ({mod.Dir}): {e.Message}", e);
            }
        }

        if (catalog.Problems() is { Count: > 0 } problems)
            throw new InvalidDataException($"каталог игры (data/game и моды): {string.Join("; ", problems)}");
        // Политика вписывания (ui/layout.json) — после всех слоёв: правило базы ложится и на экран, заменённый модом.
        screens = UiLayouts.Apply(screens, layout);
        return new GameContent(screens, assets, flow, defaults, catalog, transitions, videos, gfx, mapArt, layers, dataDir, gameDir, input)
        {
            Rules = rules, EntityFrames = entityFrames,
        };
    }

    /// <summary>Картинки каталогов сущностей мода (<see cref="EntityArt"/>: <c>icon.png</c> постройки…) — в архивы графики под именем графики
    /// сущности (<see cref="ArtNames"/>): окна находят значок постройки мода без описи. Файл крупнее места в целое число раз — HD
    /// (<see cref="GfxImage.Scale"/>, размер — логический); иного размера — как есть.</summary>
    internal static GfxAssets EntityIcons(ModInfo mod) => GfxAssets.Of(EntityFolders.Scan(mod)
        .SelectMany(f => EntityArt.Of(f.Kind.Name).Select(slot => (Folder: f, Slot: slot, Path: Path.Combine(f.Dir, slot.File))))
        .Where(a => File.Exists(a.Path))
        .GroupBy(a => a.Slot.Archive)
        .Select(g => new GfxArchive
        {
            Archive = g.Key,
            Images = [.. g.Select(a => Icon(a.Folder.Id, a.Slot, a.Path))],
        }));

    private static GfxImage Icon(string id, EntityArtSlot slot, string path)
    {
        var (w, h) = PngSize(path);
        string name = EntityArt.FrameName(id, slot);
        return EntityArt.ScaleOf(slot, w, h) is { } k
            ? new GfxImage { Name = name, File = path, W = slot.W, H = slot.H, Scale = k > 1 ? k : null }
            : new GfxImage { Name = name, File = path, W = w, H = h };
    }

    /// <summary>Размер PNG по заголовку (IHDR); не PNG — 0×0.</summary>
    private static (int W, int H) PngSize(string path)
    {
        Span<byte> head = stackalloc byte[24];
        using var file = File.OpenRead(path);
        return file.Read(head) < 24 ? (0, 0) : EntityArt.PngSize(head);
    }

    private static UiFlow? LoadFlow(string path) => File.Exists(path) ? UiFlows.Load(path) : null;

    private static UiDefaults? LoadDefaults(string path) => File.Exists(path) ? UiDefaults.Load(path) : null;

    private static UiTransitions? LoadTransitions(string path) => File.Exists(path) ? UiTransitions.Load(path) : null;

    /// <summary>Моды из перечисленных корней: подкаталог считается модом, если в нём есть <c>mod.json</c> (корень с <c>mod.json</c> — сам мод).
    /// Несуществующий корень — не ошибка (у игрока может не быть <c>user://mods</c>), прочее содержимое корня
    /// (README, архивы, чужие каталоги) просто не мод.</summary>
    public static IReadOnlyList<ModInfo> Discover(params string[] roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        // Корень с mod.json — сам мод (Студия запускает игру с одним модом: --mods <каталог мода>).
        return roots.Where(Directory.Exists)
            .SelectMany(root => File.Exists(Path.Combine(root, "mod.json")) ? (IEnumerable<string>)[root] : Directory.EnumerateDirectories(root)
                .Where(dir => File.Exists(Path.Combine(dir, "mod.json")))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            .Select(Read)
            .ToList();
    }

    /// <summary>Ролики мода лежат в его <c>assets/video</c> — путь разворачивается в полный, как у кадров.</summary>
    private static IEnumerable<UiVideo> RootedVideos(IEnumerable<UiVideo> videos, string root) =>
        videos.Select(v => v with
        {
            File = Path.GetFullPath(Path.Combine(root, v.File.Replace('/', Path.DirectorySeparatorChar))),
        });

    private static IEnumerable<UiAsset> Rooted(IEnumerable<UiAsset> assets, string root) =>
        assets.Select(a => a with
        {
            File = Path.GetFullPath(Path.Combine(root, a.File.Replace('/', Path.DirectorySeparatorChar))),
        });

    private static ModInfo Read(string dir)
    {
        string path = Path.Combine(dir, "mod.json");
        ModJson? mod;
        try
        {
            mod = JsonSerializer.Deserialize<ModJson>(File.ReadAllText(path), Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }

        if (string.IsNullOrWhiteSpace(mod?.Id)) throw new InvalidDataException($"{path}: нет поля id");
        return new ModInfo(mod.Id, string.IsNullOrWhiteSpace(mod.Name) ? mod.Id : mod.Name, dir)
        {
            Dependencies = mod.Dependencies ?? [],
            LoadAfter = mod.LoadAfter ?? [],
            Plugins = ModInfo.PluginsIn(dir),
        };
    }

    /// <summary>Мод по каталогу (<c>mod.json</c> в нём).</summary>
    public static ModInfo ReadMod(string dir) => Read(dir);

    private sealed record ModJson(string? Id, string? Name, string? Description, string[]? Dependencies, string[]? LoadAfter);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}
