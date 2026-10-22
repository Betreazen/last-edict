using System.Text.RegularExpressions;
using D2.Data.Content;
using D2.Data.Game;
using D2.Data.Play;
using D2.Rules.Map;
using Xunit;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>Языковой слой сценариев (<c>data/scenarios/lang/&lt;язык&gt;</c>, <see cref="ScenarioTexts"/>): с переводом — русский текст,
/// без перевода — оригинал; файлы слоя в git согласованы со сценариями и с импортом Руссобит-М.</summary>
public partial class ScenarioTextsTests(ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static string Scenarios => ImportedData.PathOf("scenarios");
    private static string LangRu => Path.Combine(Scenarios, ScenarioTexts.LangDir, "ru");

    /// <summary>Русский слой сценариев есть только в импорте с русификацией (<c>--l10n</c>) — без неё тест пропускается.</summary>
    private static void RequireRussian() => ImportedData.Require($"scenarios/{ScenarioTexts.LangDir}/ru");

    private static ScenarioData Read(string id) => ScenarioFile.Read(ScenarioFile.PathOf(Scenarios, id));

    [SkippableFact]
    public void Russian_Layer_TranslatesEventsSitesStacksAndCard()
    {
        RequireRussian();
        var english = Read("Hatred");
        var texts = ScenarioTexts.Load([Scenarios], "ru", "Hatred");
        var ru = ScenarioTexts.Apply(english, texts);

        var popup = (PopupEffect)ru.Events.Events.Single(e => e.Id == "S073EV0002").Effects[0];
        Assert.Equal(texts["event:S073EV0002:0"], popup.Text);
        Assert.Matches(Cyrillic(), popup.Text);
        Assert.StartsWith("Bradobar", ((PopupEffect)english.Events.Events.Single(e => e.Id == "S073EV0002").Effects[0]).Text);
        Assert.Matches(Cyrillic(), ru.Looks.Map.Sites.Single(s => s.Id == "S073FT0000").Name);
        Assert.Matches(Cyrillic(), ru.Looks.Stacks.Single(s => s.Id == "S073KC0000").LeaderName);
        Assert.Equal("Fergal City", english.Looks.Map.Sites.Single(s => s.Id == "S073FT0000").Name);   // исходник не тронут

        var card = ScenarioTexts.Apply(new GameCatalog { Scenarios = [new ScenarioInfo { Id = "Hatred", File = "", Name = "Hatred" }] },
                                       id => ScenarioTexts.Load([Scenarios], "ru", id)).Scenarios[0];
        Assert.Equal("Ненависть", card.Name);
    }

    [SkippableFact]
    public void NoTranslation_KeepsOriginal()
    {
        var english = Read("Hatred");
        Assert.Same(ScenarioTexts.None, ScenarioTexts.Load([Scenarios], "de", "Hatred"));
        Assert.Same(english, ScenarioTexts.Apply(english, ScenarioTexts.Load([Scenarios], "de", "Hatred")));
        Assert.Same(english, SharedContent.Load(Data).Localized(english, "en"));
    }

    /// <summary>Пропущенный ключ и пустой текст — оригинал, чужой ключ пропускается, ключ мода перекрывает базу.</summary>
    [SkippableFact]
    public void PartialLayers_FallBackPerKey_AndModOverrides()
    {
        string root = Path.Combine(Path.GetTempPath(), "d2-scentexts-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseDir = Path.Combine(root, "base"), modDir = Path.Combine(root, "mod");
            var english = Read("Hatred");
            string site = "site:S073FT0000:name", popup = "event:S073EV0002:0";
            ScenarioTexts.Write(ScenarioTexts.PathOf(baseDir, "ru", "Hatred"), new ScenarioTexts.LangFile("Hatred", "ru", "тест",
                [new(site, "Фергал"), new(popup, ""), new("event:НЕТ:0", "лишний")]));
            ScenarioTexts.Write(ScenarioTexts.PathOf(modDir, "ru", "Hatred"), new ScenarioTexts.LangFile("Hatred", "ru", "мод",
                [new(site, "Град Фергала")]));

            var only = ScenarioTexts.Apply(english, ScenarioTexts.Load([baseDir], "ru", "Hatred"));
            Assert.Equal("Фергал", only.Looks.Map.Sites.Single(s => s.Id == "S073FT0000").Name);
            Assert.Equal(((PopupEffect)english.Events.Events.Single(e => e.Id == "S073EV0002").Effects[0]).Text,
                         ((PopupEffect)only.Events.Events.Single(e => e.Id == "S073EV0002").Effects[0]).Text);
            Assert.Equal(english.Looks.Stacks[0].LeaderName, only.Looks.Stacks[0].LeaderName);

            var both = ScenarioTexts.Apply(english, ScenarioTexts.Load([baseDir, modDir], "ru", "Hatred"));
            Assert.Equal("Град Фергала", both.Looks.Map.Sites.Single(s => s.Id == "S073FT0000").Name);

            ScenarioTexts.Write(ScenarioTexts.PathOf(baseDir, "ru", "Tutorial"), new ScenarioTexts.LangFile("Hatred", "ru", "тест", []));
            Assert.Throws<InvalidDataException>(() => ScenarioTexts.Load([baseDir], "ru", "Tutorial"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Каталог меню на языке: квест и глава саги — по-русски, на английском — как в <c>data/game</c>.</summary>
    [SkippableFact]
    public void Content_CatalogIn_Russian_And_English()
    {
        RequireRussian();
        var content = SharedContent.Load(Data);
        var ru = content.CatalogIn("ru");
        Assert.Equal("Ненависть", ru.Scenario("Hatred")!.Name);
        Assert.Equal("Hatred", content.CatalogIn("en").Scenario("Hatred")!.Name);
        Assert.Same(ru, content.CatalogIn("ru"));
        var chapter = ru.Scenario("S107SC0000")!;
        Assert.Matches(Cyrillic(), chapter.Name);
        Assert.Matches(Cyrillic(), chapter.BriefingLong);
        Assert.Equal("The Necromancer's Spirit", content.Catalog.Scenario("S107SC0000")!.Name);
    }

    /// <summary>Каждый файл слоя — перевод своего сценария: ключи только из сценария, тексты непустые, подстановки <c>%…%</c> как
    /// в оригинале. То же проверяет <c>tools/scenario-ru/check.py</c> для переводчика.</summary>
    [SkippableFact]
    public void RussianFiles_MatchTheirScenarios()
    {
        RequireRussian();
        var catalog = GameCatalog.Load(ImportedData.PathOf("game"));
        var problems = new List<string>();
        int files = 0, texts = 0;
        foreach (string path in Directory.GetFiles(LangRu, "*" + ScenarioFile.Extension))
        {
            var file = ScenarioTexts.Read(path);
            string id = Path.GetFileNameWithoutExtension(path);
            if (file.Id != id || file.Language != "ru") problems.Add($"{id}: Id «{file.Id}», язык «{file.Language}»");
            var scenario = Read(id);
            var en = (catalog.Scenario(id) is { } info ? ScenarioTexts.Extract(info) : [])
                .Concat(ScenarioTexts.Extract(scenario)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var (key, text) in file.Texts)
            {
                if (!en.TryGetValue(key, out var original)) problems.Add($"{id}: лишний ключ {key}");
                else if (string.IsNullOrWhiteSpace(text)) problems.Add($"{id}: пустой {key}");
                else if (!Placeholders(original).SequenceEqual(Placeholders(text))) problems.Add($"{id}: подстановки {key}");
            }
            if (file.Texts.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != file.Texts.Count) problems.Add($"{id}: повтор ключа");
            files++;
            texts += file.Texts.Count;
        }
        output.WriteLine($"файлов {files}, текстов {texts}");
        Assert.True(files >= 75, $"файлов перевода {files}");
        Assert.Empty(problems);
    }

    /// <summary>Тексты Руссобит-М в git — ровно результат импорта (<c>tools/ScenarioImport -- --texts ru</c>): правка оригинального
    /// перевода руками или изменение импорта не пройдут молча. Пропускается без слоя русификации на машине.</summary>
    [SkippableFact]
    public void Russobit_Texts_AreUpToDate()
    {
        RequireRussian();
        string layer = ScenarioTextsImport.DefaultRoot;
        Skip.IfNot(GamePaths.IsAvailable && ScenarioTextsImport.IsAvailable(layer), $"нет игры или слоя русификации: {layer}");
        var catalog = GameCatalog.Load(ImportedData.PathOf("game"));
        var results = ScenarioTextsImport.Read(Scenarios, catalog, GamePaths.GameDir, layer, "ru", "Руссобит-М",
                                               id => ScenarioTexts.Load([Scenarios], "ru", id));
        var stale = new List<string>();
        foreach (var r in results.Where(r => r.File is not null))
        {
            string path = ScenarioTexts.PathOf(Scenarios, "ru", r.Id);
            if (!File.Exists(path))
            {
                stale.Add($"{r.Id}: нет файла");
                continue;
            }
            var file = ScenarioTexts.Read(path);
            if (!file.Texts.SequenceEqual(r.File!.Texts) || file.Source != r.File.Source) stale.Add(r.Id);
        }
        output.WriteLine($"сценариев в слое {results.Count(r => r.File is not null)}, без слоя {results.Count(r => r.File is null)}");
        Assert.True(results.Count(r => r.File is not null) >= 75);
        Assert.True(stale.Count == 0, $"устарели: {string.Join(", ", stale)}; пересобрать: dotnet run --project tools/ScenarioImport -- --texts ru");
    }

    [Fact]
    public void Write_Read_RoundTrip_KeepsOrder()
    {
        string path = Path.Combine(Path.GetTempPath(), "d2-scentext-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var file = new ScenarioTexts.LangFile("X", "ru", "тест", [new("name", "Имя \"в кавычках\"\nстрока"), new("briefing", "Цель")]);
            ScenarioTexts.Write(path, file);
            var back = ScenarioTexts.Read(path);
            Assert.Equal(file.Texts, back.Texts);
            Assert.Contains("Имя", File.ReadAllText(path));   // кириллица без \u-экранирования
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IEnumerable<string> Placeholders(string text) => PlaceholderRe().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal);

    [GeneratedRegex("%[A-Z][A-Z_0-9]*%")]
    private static partial Regex PlaceholderRe();

    [GeneratedRegex("[А-Яа-яЁё]")]
    private static partial Regex Cyrillic();
}
