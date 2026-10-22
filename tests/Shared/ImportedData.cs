namespace D2.Tests;

/// <summary>Данные, выведенные из файлов игры (<c>data/game</c>, <c>data/gfx</c>, <c>data/map</c>, <c>data/rules</c>,
/// <c>data/scenarios</c>, <c>data/sound</c>, <c>data/video</c>, опись и разметка интерфейса <c>data/ui/{assets,screens,strings.*,capital,
/// leader-names.*}.json</c>). В публичном дереве их нет — они импортируются у пользователя из купленной копии, — и тест, которому они нужны,
/// на таком дереве пропускается с понятной причиной, а не падает. Наш собственный контент (<c>data/fonts</c>, <c>data/music</c>,
/// <c>data/ui/{defaults,flow,flow.manual,fonts,transitions}.json</c>, <c>mods/_examples</c>) проверкой не закрывается.
/// <para>Проверка бросает <c>SkipException</c> — она работает только в <c>[SkippableFact]</c>/<c>[SkippableTheory]</c>, и звать её надо
/// не из статического конструктора или инициализатора поля (там исключение обернётся в <c>TypeInitializationException</c>).</para></summary>
internal static class ImportedData
{
    /// <summary>Каталог данных рядом со сборкой тестов (<c>D2.Data.Tests.csproj</c> кладёт туда <c>data/**</c>
    /// репозитория, <c>D2.Rules.Tests.csproj</c> — <c>data/content</c> и <c>data/rules</c>).
    /// <para>Переменная <c>D2_DATA_DIR</c> (корень импорта, как у игры: в нём <c>data/</c> и <c>assets/</c>) главнее: так тесты берут импорт из
    /// копии игры, сделанный вне репозитория (<c>user://game</c>, Last Edict без данных в git); от него же <see cref="Content"/> и
    /// <see cref="Tables"/>.</para></summary>
    public static string Dir { get; } = Environment.GetEnvironmentVariable("D2_DATA_DIR") is { Length: > 0 } root
        ? Path.Combine(Path.GetFullPath(root), "data") : Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Опись интерфейса, разметка экранов и строки (английские есть в любом импорте, русские — только с русификацией) — без них
    /// не собирается <see cref="D2.Data.Content.GameContent"/>.</summary>
    public static readonly string[] ContentFiles = ["ui/screens.json", "ui/assets.json", "ui/strings.en.json", "game", "gfx", "map"];

    /// <summary>Пропускает тест, если в <see cref="Dir"/> нет хоть одного из путей (файл или непустой каталог).</summary>
    public static void Require(params string[] relativePaths) => RequireIn(Dir, relativePaths);

    /// <summary>То же для другого каталога данных (<c>SharedContent.Load</c> берёт каталог параметром).</summary>
    public static void RequireIn(string dataDir, params string[] relativePaths)
    {
        foreach (string relative in relativePaths)
        {
            string full = Full(dataDir, relative);
            bool present = File.Exists(full) || (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any());
            Skip.IfNot(present, $"нет данных игры data/{relative}: импортируйте их из копии Disciples II (docs/publishing.md)");
        }
    }

    /// <summary>Путь внутри <see cref="Dir"/> (<c>"rules"</c>, <c>"ui/screens.json"</c>) — после проверки, что он есть: для свойств вида
    /// <c>Rules =&gt; ...</c>, через которые тесты класса берут данные.</summary>
    public static string PathOf(string relative)
    {
        Require(relative);
        return Full(Dir, relative);
    }

    /// <summary>Собранная графика, звук и шрифты импорта: <c>D2_ASSETS_DIR</c>, иначе <c>assets</c> корня импорта <c>D2_DATA_DIR</c>, иначе
    /// <c>assets</c> репозитория.</summary>
    public static string AssetsDir { get; } =
        Environment.GetEnvironmentVariable("D2_ASSETS_DIR") is { Length: > 0 } assets ? Path.GetFullPath(assets)
        : Environment.GetEnvironmentVariable("D2_DATA_DIR") is { Length: > 0 } root ? Path.Combine(Path.GetFullPath(root), "assets")
        : Path.Combine(RepoPaths.Root, "assets");

    /// <summary>Содержимое правил из импорта — мод <c>base</c> (<c>data/content/base</c>): его берут загрузчики правил тестов вместо таблиц
    /// копии игры (<c>GamePaths.Globals</c>), — после проверки, что оно есть.</summary>
    public static string Content => PathOf("content/base");

    /// <summary>Таблицы правил из импорта в JSON порта (<c>data/rules</c>): для тестов, которые читают строки таблиц сами
    /// (<c>GameTables.Read</c>), — после проверки, что они есть.</summary>
    public static string Tables => PathOf("rules");

    /// <summary>Пропускает тест, если правил из импорта нет (<see cref="Content"/> и <see cref="Tables"/>).</summary>
    public static void RequireRules() => Require("content/base", "rules");

    /// <summary>Каталог данных для <see cref="D2.Data.Content.GameContent.Load"/> — после проверки всего, что нужно содержимому игры.</summary>
    public static string ContentDir()
    {
        Require(ContentFiles);
        return Dir;
    }

    /// <summary>Каталог <c>data/ui</c> для <see cref="D2.Data.Ui.UiStrings.Load"/> — после проверки, что строки игры импортированы (свои файлы
    /// интерфейса — <c>fonts.json</c>, <c>flow.json</c> — в нём лежат и без них).</summary>
    public static string StringsDir()
    {
        Require("ui/strings.ru.json", "ui/strings.en.json");
        return Full(Dir, "ui");
    }

    private static string Full(string dataDir, string relative) =>
        Path.Combine(dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
}
