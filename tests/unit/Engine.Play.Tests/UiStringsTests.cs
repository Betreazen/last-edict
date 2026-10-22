using D2.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>Строки интерфейса: свой формат, выбор языка с откатом, наложение мода и сами файлы
/// <c>data/ui/strings.&lt;язык&gt;.json</c>. Числа сверены пересчётом таблиц игры (GOG 3.01 + русификация
/// Руссобит-М).</summary>
public class UiStringsTests(ITestOutputHelper output)
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data", "ui");

    /// <summary>Идентификаторы, на которые ссылаются экраны, но которых нет ни в одной таблице игры.
    /// Это мусор в самой разметке, а не пропажа перевода: в поле подсказки боевого экрана записаны имена
    /// кадров, у одного элемента вместо идентификатора число, а <c>Login</c>/<c>Register</c> — английские
    /// литералы сетевого лобби. Тот же список продублирован в <c>tools/ui-render-check.py</c>.</summary>
    private static readonly string[] KnownMissing =
    [
        "255",
        "DLG_BATTLE_A_LITEMSBG", "DLG_BATTLE_A_LUNITGROUP", "DLG_BATTLE_A_MAINCOMBATBG",
        "DLG_BATTLE_A_RITEMSBG", "DLG_BATTLE_A_RUNITGROUP",
        "Login", "Register",
    ];

    // --- свой формат ---

    /// <summary>Запись и чтение JSON не теряют строку, ключи сортируются (файл лежит в git), регистр ключа
    /// при поиске не значим: в разметке <c>X005TA0020</c>, в таблицах игры <c>x000tg0000</c>.</summary>
    [Fact]
    public void Json_RoundTrips_AndLooksUpCaseInsensitively()
    {
        var table = new Dictionary<string, string> { ["X005TA0002"] = "Второй", ["X005TA0001"] = "Первый" };
        string json = UiStrings.ToJson(table);
        Assert.True(json.IndexOf("X005TA0001", StringComparison.Ordinal)
                    < json.IndexOf("X005TA0002", StringComparison.Ordinal), json);

        var back = UiStrings.FromJson(json);
        Assert.Equal("Первый", back["x005ta0001"]);
        Assert.Equal(2, back.Count);
    }

    /// <summary>Мод накладывается по идентификатору: совпал — текст мода, новый — добавляется.</summary>
    [Fact]
    public void Merge_OverlayReplacesById()
    {
        var merged = UiStrings.Merge(
            new Dictionary<string, string> { ["A"] = "раз", ["B"] = "два" },
            new Dictionary<string, string> { ["b"] = "ДВА", ["C"] = "три" });

        Assert.Equal("раз", merged["A"]);
        Assert.Equal("ДВА", merged["B"]);
        Assert.Equal("три", merged["C"]);
        Assert.Equal(3, merged.Count);
    }

    /// <summary>Язык выбирается по имени файла, а непереведённая строка берётся из запасного языка,
    /// а не пропадает. Нет ни одного файла — падение, а не пустые надписи.</summary>
    [Fact]
    public void Load_PicksLanguage_AndFallsBackPerId()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"d2-strings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            UiStrings.Save(Path.Combine(dir, UiStrings.FileName("en")),
                           new Dictionary<string, string> { ["A"] = "one", ["B"] = "two" });
            UiStrings.Save(Path.Combine(dir, UiStrings.FileName("ru")),
                           new Dictionary<string, string> { ["A"] = "раз" });

            var ru = UiStrings.Load(dir, "ru");
            Assert.Equal("раз", ru["A"]);
            Assert.Equal("two", ru["B"]);          // перевода нет — остался английский
            Assert.Equal(["en", "ru"], UiStrings.Languages(dir));

            Assert.Equal("one", UiStrings.Load(dir, "en")["A"]);
            Assert.Equal("one", UiStrings.Load(dir, "de")["A"]);   // языка нет — весь откат
            Assert.Throws<FileNotFoundException>(() => UiStrings.Load(dir, "de", "fr"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --- файлы порта ---

    /// <summary>В git лежат два языка, в каждом одни и те же 3284 идентификатора: 2292 из
    /// <c>Globals/Tglobal.dbf</c> и 992 из <c>Interf/TApp.DBF</c>, пересечений между таблицами нет.</summary>
    [SkippableFact]
    public void Files_HaveBothLanguagesWithSameIds()
    {
        ImportedData.Require("ui/strings.ru.json", "ui/strings.en.json");
        Assert.Equal(["en", "ru"], UiStrings.Languages(DataDir));
        var en = UiStrings.LoadFile(Path.Combine(DataDir, UiStrings.FileName("en")));
        var ru = UiStrings.LoadFile(Path.Combine(DataDir, UiStrings.FileName("ru")));
        output.WriteLine($"en {en.Count}, ru {ru.Count}");

        Assert.Equal(3284, en.Count);
        Assert.Equal(3284, ru.Count);
        Assert.Empty(en.Keys.Except(ru.Keys, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Русский файл действительно русский: кириллица есть у подавляющего большинства строк.
    /// Непереведённых (только латиница) остаётся 88 — столько их в самой русификации.</summary>
    [SkippableFact]
    public void Russian_IsActuallyRussian()
    {
        ImportedData.Require("ui/strings.ru.json", "ui/strings.en.json");
        var ru = UiStrings.LoadFile(Path.Combine(DataDir, UiStrings.FileName("ru")));
        var latin = ru.Where(p => p.Value.Trim().Length > 0 && !p.Value.Any(c => c is >= 'Ѐ' and <= 'ӿ'))
                      .Select(p => p.Key).ToList();
        output.WriteLine($"без кириллицы {latin.Count}: {string.Join(", ", latin.Take(10))}…");

        Assert.Equal(3183, ru.Count(p => p.Value.Any(c => c is >= 'Ѐ' and <= 'ӿ')));
        Assert.Equal(88, latin.Count);
    }

    /// <summary>Каждому <c>TextId</c> из набора экранов находится строка — кроме восьми, которых нет
    /// ни в одной таблице игры. Это и есть проверка, ради которой строки импортировались.</summary>
    [SkippableFact]
    public void Screens_TextIds_AreCoveredExceptKnownMissing()
    {
        var strings = UiStrings.Load(ImportedData.StringsDir(), "ru");
        var ids = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).SelectMany(s => s.Elements)
                           .Select(e => e.TextId).Where(t => !string.IsNullOrEmpty(t))
                           .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missing = ids.Where(t => !strings.ContainsKey(t!)).ToList();
        output.WriteLine($"идентификаторов {ids.Count}, без строки {missing.Count}: {string.Join(", ", missing)}");

        Assert.Equal(167, ids.Count);
        Assert.Equal(KnownMissing.OrderBy(s => s, StringComparer.OrdinalIgnoreCase),
                     missing.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Файлы в git — это в точности результат импорта: повторный прогон
    /// <c>tools/UiStringsImport</c> даёт тот же текст. Тест нужен, чтобы правка таблиц или правил импорта
    /// не прошла молча мимо файла. Пропускается, когда исходников на машине нет.</summary>
    [SkippableTheory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Strings_Json_IsUpToDate(string language)
    {
        var roots = UiStringsImport.DefaultRoots(language);
        Skip.IfNot(roots.All(UiStringsImport.IsAvailable),
                   $"нет исходников строк: {string.Join(", ", roots.Where(r => !UiStringsImport.IsAvailable(r)))}");
        output.WriteLine(string.Join(Environment.NewLine, roots));

        string expected = UiStrings.ToJson(UiStringsImport.Read(roots)).ReplaceLineEndings("\n");
        string actual = File.ReadAllText(Path.Combine(DataDir, UiStrings.FileName(language))).ReplaceLineEndings("\n");
        if (expected == actual) return;

        var a = actual.Split('\n');
        var b = expected.Split('\n');
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        Assert.Fail($"data/ui/{UiStrings.FileName(language)} устарел (строка {i + 1}):\n" +
                    $"  в файле:  {(i < a.Length ? a[i] : "<конец>")}\n" +
                    $"  импорт:   {(i < b.Length ? b[i] : "<конец>")}\n" +
                    $"  пересобрать: dotnet run --project tools/UiStringsImport -- --lang {language}");
    }
}
