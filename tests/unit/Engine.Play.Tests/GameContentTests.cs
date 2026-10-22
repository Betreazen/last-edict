using LastEdict.Data.Content;
using LastEdict.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Слой модов от начала до конца: настоящие данные порта (<c>data/ui</c>) плюс настоящий пример мода
/// из <c>mods/_examples</c> — те же файлы, что читает игра с <c>--mods mods/_examples</c>, а не выдуманные в тесте. Проверяется и то, ради чего слой
/// затевался: экран заменяется целиком по <c>Id</c>, кадр добавляется и его PNG ищется в каталоге мода,
/// а не в общем <c>assets/ui</c>.</summary>
public class GameContentTests(ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static string Mods => Path.Combine(AppContext.BaseDirectory, "mods");

    /// <summary>Примеры модов — не в корне <c>mods/</c>: корень включён при обычном запуске, а пример меняет окно игры (прогон 15.10,
    /// пометка 22: окно итога вора стало заливкой во весь экран без кнопки).</summary>
    private static string Examples => Path.Combine(Mods, "_examples");
    private const string Example = "example-plain-backdrop";

    [SkippableFact]
    public void BaseContent_LoadsWithoutMods()
    {
        var content = GameContent.Load(ImportedData.ContentDir());

        Assert.Empty(content.Mods);
        Assert.NotEmpty(content.Screens);
        Assert.NotEmpty(content.Assets);
        Assert.Equal("_PG0250IX", Screen(content, "DLG_ACTION_RESULT").Background);
        Assert.False(content.AssetsByName.ContainsKey("EXAMPLE_BACKDROP"));
        output.WriteLine($"база: экранов {content.Screens.Count}, кадров {content.Assets.Count}");
    }

    [SkippableFact]
    public void ExampleMod_ReplacesOneScreen_AndAddsOneFrame()
    {
        var mods = GameContent.Discover(Examples);
        Assert.Contains(mods, m => m.Id == Example);

        var @base = GameContent.Load(ImportedData.ContentDir());
        var modded = GameContent.Load(ImportedData.ContentDir(), mods);

        // Экран заменён целиком, а не дополнен: тот же Id, другой фон и другой размер — но элементы, которые заполняет окно итога вора,
        // все на месте (иначе логика окна падает на открытии).
        var screen = Screen(modded, "DLG_ACTION_RESULT");
        Assert.Equal("EXAMPLE_BACKDROP", screen.Background);
        Assert.Equal(800, screen.Design.Width);
        Assert.Equal(Screen(@base, "DLG_ACTION_RESULT").Elements.Select(e => e.Id).Order(), screen.Elements.Select(e => e.Id).Order());

        // Замена, а не добавление: экранов столько же, и порядок базовых сохранён.
        Assert.Equal(@base.Screens.Count, modded.Screens.Count);
        Assert.Equal(@base.Screens.Select(s => s.Id), modded.Screens.Select(s => s.Id));

        // Кадр добавлен, и PNG берётся из каталога мода — базовый assets/ui при этом не при чём.
        Assert.Equal(@base.Assets.Count + 1, modded.Assets.Count);
        var asset = modded.AssetsByName["EXAMPLE_BACKDROP"];
        string path = UiAssets.PathOf(asset, Path.Combine(AppContext.BaseDirectory, "assets", "ui"));
        Assert.StartsWith(Path.Combine(Examples, Example), path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), path);

        output.WriteLine($"мод {Example}: экран {screen.Id} -> {screen.Background}, кадр {path}");
    }

    /// <summary>Моды корня <c>mods/</c> включены при обычном запуске (<c>godot/autoload/GameData.cs</c>), поэтому окна игры они не ломают:
    /// экран, который мод заменил, сохраняет все элементы базового — их заполняет логика окна (<c>UiScreenState.SetText</c> на
    /// отсутствующий элемент бросает). Пример мода, сменивший <c>DLG_ACTION_RESULT</c> на одну надпись, держал игру (прогон 15.10,
    /// пометка 22) — теперь он в <c>mods/_examples</c>, а этот тест не даст положить такой слой в корень снова.</summary>
    [SkippableFact]
    public void DefaultMods_KeepEveryElementOfReplacedScreens()
    {
        var mods = GameContent.Discover(Mods);
        Assert.DoesNotContain(mods, m => m.Id == Example);
        var @base = GameContent.Load(ImportedData.ContentDir());
        var modded = GameContent.Load(ImportedData.ContentDir(), mods);
        foreach (var screen in modded.Screens)
        {
            if (@base.Screens.FirstOrDefault(s => s.Id == screen.Id) is not { } original) continue;   // новый экран мода
            var lost = original.Elements.Select(e => e.Id).Except(screen.Elements.Select(e => e.Id), StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(lost.Count == 0, $"мод заменил {screen.Id} без элементов {string.Join(", ", lost)}");
        }
        output.WriteLine($"моды по умолчанию: {string.Join(", ", mods.Select(m => m.Id))}");
    }

    /// <summary>Каталог без <c>mod.json</c> модом не считается — иначе README, распакованный архив или .git
    /// в каталоге модов ломали бы запуск.</summary>
    [Fact]
    public void FolderWithoutModJson_IsNotAMod()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "just-a-folder"));
        try
        {
            Assert.Empty(GameContent.Discover(root));
            Assert.Empty(GameContent.Discover(Path.Combine(root, "нет-такого")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Строки и шрифты идут через слои, как экраны (docs/principles.md §2): строка мода на языке перекрывает базу, английская
    /// строка мода не перекрывает русский перевод базы, но новая строка мода есть и в русском; шрифт мода перекрывает начертание по ключу,
    /// и его файл ищется у мода. Таблица одна на язык за запуск — второе окно её не разбирает.</summary>
    [SkippableFact]
    public void ModLayer_OverlaysStringsAndFonts()
    {
        string root = Path.Combine(Path.GetTempPath(), $"d2-modstrings-{Guid.NewGuid():N}");
        string mod = Path.Combine(root, "strings-mod");
        string ui = Path.Combine(mod, "data", "ui");
        Directory.CreateDirectory(ui);
        try
        {
            var baseRu = UiStrings.Load(ImportedData.StringsDir(), "ru");
            string replaced = baseRu.Keys.Order(StringComparer.Ordinal).First();
            string kept = baseRu.Keys.Order(StringComparer.Ordinal).Skip(1).First();
            File.WriteAllText(Path.Combine(mod, "mod.json"), """{ "id": "strings-mod" }""");
            UiStrings.Save(Path.Combine(ui, UiStrings.FileName("ru")), new Dictionary<string, string> { [replaced] = "мод" });
            UiStrings.Save(Path.Combine(ui, UiStrings.FileName("en")),
                           new Dictionary<string, string> { [kept] = "mod english", ["MOD_NEW"] = "new" });
            File.WriteAllText(Path.Combine(ui, "fonts.json"),
                              """{ "Faces": { "normal": { "File": "data/fonts/Mod.ttf", "Size": 16 } } }""");

            var content = GameContent.Load(ImportedData.ContentDir(), GameContent.Discover(root));
            var ru = content.Strings("ru");

            Assert.Equal("мод", ru[replaced]);
            Assert.Equal(baseRu[kept], ru[kept]);          // английский мода не перекрыл русский перевод базы
            Assert.Equal("new", ru["MOD_NEW"]);            // новой строки нет по-русски — берётся английская мода
            Assert.Equal("mod english", content.Strings("en")[kept]);
            Assert.Same(ru, content.Strings("RU"));        // один разбор на язык

            var fonts = content.Fonts;
            Assert.Equal(16, fonts.Resolve("Normal").Size);
            Assert.Equal(Path.GetFullPath(Path.Combine(mod, "data", "fonts", "Mod.ttf")), fonts.Resolve("Normal").File);
            Assert.Equal("assets/fonts/medbold.fnt", fonts.Resolve("MedBold").File);   // базовое начертание — как было
            Assert.Equal(UiFonts.Load(Path.Combine(Data, "ui", "fonts.json")).Color, fonts.Color);
            Assert.Same(fonts, content.Fonts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Без модов строки и шрифты — те же, что прямой разбор базовых файлов.</summary>
    [SkippableFact]
    public void BaseContent_StringsAndFonts_MatchFiles()
    {
        var content = GameContent.Load(ImportedData.ContentDir());
        var direct = UiStrings.Load(ImportedData.StringsDir(), "ru");
        Assert.Equal(direct.OrderBy(p => p.Key, StringComparer.Ordinal), content.Strings("ru").OrderBy(p => p.Key, StringComparer.Ordinal));
        var fonts = UiFonts.Load(Path.Combine(Data, "ui", "fonts.json"));
        Assert.Equal(fonts.Faces.OrderBy(p => p.Key, StringComparer.Ordinal), content.Fonts.Faces.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(fonts.Default, content.Fonts.Default);
    }

    private static UiScreen Screen(GameContent content, string id) =>
        content.Screens.Single(s => s.Id == id);
}
