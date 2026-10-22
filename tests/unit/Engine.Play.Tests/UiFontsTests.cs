using LastEdict.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Набор шрифтов порта: <c>data/ui/fonts.json</c>. Проверяется, что он покрывает все ключи, которые
/// встречаются в разметке экранов, и что файлы начертаний на месте: растровые шрифты игры (BMFont из <c>Interf/*.mft</c>,
/// <c>tools/ui-assets/import_fonts.py</c>) — в собранной графике, запасные TrueType — в репозитории. Растр в движке (кириллица нарисовалась)
/// проверяет <c>tools/ui-render-check.py</c>.</summary>
public class UiFontsTests(ITestOutputHelper output)
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "ui");

    private static UiFonts Fonts => UiFonts.Load(Path.Combine(DataDir, "fonts.json"));

    /// <summary>Корень репозитория: пути к начертаниям в <c>fonts.json</c> отсчитываются от него, а не от
    /// каталога сборки. Ближайшая вверх папка с <c>Disciples.sln</c> — так же, как в импортёрах.</summary>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>Семь начертаний — по одному на шрифт исходной игры (<c>Interf/*.mft</c>), сами растры игры, переведённые в BMFont. Кегль —
    /// родной размер растра, высота ячейки MFT (small 12, normal 13, medbold 13, medium 14, menu 14, large 14, vlarge 19): другой кегль
    /// движок растягивал бы. Запасной — EB Garamond того же начертания.</summary>
    [Fact]
    public void Faces_CoverEveryGameFont()
    {
        var fonts = Fonts;
        foreach (var (key, face) in fonts.Faces.OrderBy(p => p.Key, StringComparer.Ordinal))
            output.WriteLine($"{key,-8} {face.Size,3}  {face.File}");

        Assert.Equal(["Large", "MedBold", "Medium", "Menu", "Normal", "Small", "VLarge"],
                     fonts.Faces.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new UiFont("assets/fonts/normal.fnt", 13, 17, "data/fonts/EBGaramond-Regular.ttf"), fonts.Resolve("Normal"));
        Assert.Equal(19, fonts.Resolve("vlarge").Size);          // регистр ключа не значим
        Assert.Equal("data/fonts/EBGaramond-Italic.ttf", fonts.Resolve("MENU").Fallback);
        var cells = new Dictionary<string, int>
        {
            ["Small"] = 12, ["Normal"] = 13, ["MedBold"] = 13, ["Medium"] = 14, ["Menu"] = 14, ["Large"] = 14, ["VLarge"] = 19,
        };
        Assert.All(cells, p => Assert.Equal(p.Value, fonts.Resolve(p.Key).Size));
        Assert.All(fonts.Faces, p => Assert.Equal($"assets/fonts/{p.Key.ToLowerInvariant()}.fnt", p.Value.File));
    }

    /// <summary>Шаг строки каждого начертания — как у растрового шрифта оригинала: высота ячейки MFT плюс его межстрочный интервал
    /// (заголовки <c>Interf/*.mft</c>: small 12+4, normal 13+4, medbold 13+4, medium 14+4, menu 14+4, large 14+4, vlarge 19+6; снимки
    /// 28.09 — базовые линии строк через 17 точек у Normal). Шрифт порта со своим шагом (1,305 кегля, 18–29 точек) строки разносил, и
    /// многострочные подписи налезали на соседние элементы (снимок пользователя 12, карточка города).</summary>
    [Fact]
    public void Faces_LinePitchMatchesGameFonts()
    {
        var want = new Dictionary<string, int>
        {
            ["Small"] = 16, ["Normal"] = 17, ["MedBold"] = 17, ["Medium"] = 18, ["Menu"] = 18, ["Large"] = 18, ["VLarge"] = 25,
        };
        Assert.All(want, p => Assert.Equal(p.Value, Fonts.Resolve(p.Key).Line));
    }

    /// <summary>Пустой и неизвестный ключ дают шрифт по умолчанию, но неизвестный при этом честно назван
    /// неизвестным: молча подменять и молчать нельзя — иначе опечатка в моде осталась бы незаметной.</summary>
    [Fact]
    public void UnknownKey_FallsBackButIsReported()
    {
        var fonts = Fonts;
        Assert.Equal(fonts.Resolve("Normal"), fonts.Resolve(""));
        Assert.Equal(fonts.Resolve("Normal"), fonts.Resolve("НетТакого"));
        Assert.True(fonts.Knows(""));
        Assert.True(fonts.Knows("large"));
        Assert.False(fonts.Knows("НетТакого"));
    }

    /// <summary>Каждый ключ шрифта из <c>screens.json</c> набору известен: иначе экран молча рисовался бы
    /// не тем кеглем.</summary>
    [SkippableFact]
    public void EveryFontKeyInScreens_IsKnown()
    {
        var fonts = Fonts;
        var keys = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).SelectMany(s => s.Elements)
                            .Select(e => UiTextStyle.Parse(e.Style).Font).Where(f => f.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        output.WriteLine($"ключи шрифтов в экранах: {string.Join(", ", keys)}");

        Assert.Equal(5, keys.Count);       // Large, Small, VLarge, Menu, Medbold — без учёта регистра
        Assert.All(keys, k => Assert.True(fonts.Knows(k), $"неизвестный шрифт {k}"));
    }

    /// <summary>Запасные шрифты лежат в репозитории, а не подбираются из системы: порт не должен зависеть от того, что установлено на
    /// машине, и без собранной графики текст всё равно рисуется.</summary>
    [SkippableFact]
    public void FallbackFonts_ExistInRepository()
    {
        string? root = RepoRoot();
        Skip.If(root is null, "не найден Disciples.sln выше каталога сборки");
        foreach (var file in Fonts.Faces.Values.Select(f => f.Fallback).Distinct(StringComparer.Ordinal))
        {
            Assert.NotNull(file);
            string path = Path.Combine(root!, file!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"нет файла шрифта {path}");
            output.WriteLine($"{file}  {new FileInfo(path).Length / 1024} КБ");
        }
        Assert.True(File.Exists(Path.Combine(root!, "data", "fonts", "OFL.txt")), "рядом со шрифтами нет лицензии");
    }

    /// <summary>Растровые шрифты в собранной графике (<c>D2_ASSETS_DIR</c>, иначе <c>assets</c> репозитория) покрывают каждый символ строк
    /// игры обоих языков (<c>strings.ru.json</c>, <c>strings.en.json</c>): ячейка как у MFT (строка BMFont <c>common</c>: высота строки и
    /// базовая линия — кегль начертания), атлас на месте. Пропускается, когда шрифты не собраны.</summary>
    [SkippableFact]
    public void BitmapFonts_CoverGameStrings()
    {
        string assets = ImportedData.AssetsDir;
        string fonts = Path.Combine(assets, "fonts");
        Skip.IfNot(Directory.Exists(fonts), $"шрифты не собраны: {fonts} (python tools/ui-assets/import_fonts.py)");

        // Покрытие — у полного импорта: без русификации шрифты собираются без кириллицы, а она есть и в английских строках копии GOG.
        ImportedData.Require("ui/strings.ru.json", "ui/strings.en.json");
        var used = new[] { "ru", "en" }.SelectMany(lang => UiStrings.Load(DataDir, lang).Values).SelectMany(t => t)
                                        .Where(c => !char.IsControl(c)).ToHashSet();
        foreach (var (key, face) in Fonts.Faces)
        {
            string path = Path.Combine(assets, face.File["assets/".Length..].Replace('/', Path.DirectorySeparatorChar));
            var lines = File.ReadAllLines(path);
            var chars = lines.Where(l => l.StartsWith("char id=", StringComparison.Ordinal))
                             .Select(l => (char)int.Parse(l["char id=".Length..l.IndexOf(' ', "char id=".Length)],
                                                          System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            string missing = new(used.Where(c => !chars.Contains(c)).Order().ToArray());
            output.WriteLine($"{key,-8} глифов {chars.Count}, нет {missing.Length}");
            Assert.True(missing.Length == 0, $"{key}: нет глифов «{missing}»");
            Assert.Contains($"common lineHeight={face.Size} base={face.Size} ", lines.Single(l => l.StartsWith("common ", StringComparison.Ordinal)));
            string page = lines.Single(l => l.StartsWith("page id=0 ", StringComparison.Ordinal)).Split("file=\"")[1].TrimEnd('"');
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, page)), $"{key}: нет атласа {page}");
        }
    }

    /// <summary>Свой формат читается и пишется без потерь. Сравнение по тексту: словарь начертаний
    /// у записи сравнивается по ссылке, а форматом хранения является именно текст.</summary>
    [Fact]
    public void Json_RoundTrips()
    {
        string json = Fonts.ToJson();
        Assert.Equal(json, UiFonts.FromJson(json).ToJson());
    }
}
