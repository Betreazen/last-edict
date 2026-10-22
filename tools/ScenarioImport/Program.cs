using D2.Data;
using D2.Data.Play;

// Сценарии игры в данные порта: Exports/*.sg и главы саг Scens/*.dbf -> data/scenarios/<имя>.json (формат d2-port-scenario: карта с
// объектами отрисовки и события). Старт партии читает уже их; .sg — только здесь (docs/principles.md §3).
//
//   dotnet run --project tools/ScenarioImport [-- --game <папка игры>] [--out <каталог>]
//
// Разбор живёт в D2.Data (ScenarioFile.FromSg) — здесь только параметры и запись. Перевод повторяем: те же исходники
// дают тот же JSON, это проверяет ScenarioFileTests.Imported_IsUpToDate.

string game = Arg("--game") ?? GamePaths.GameDir;

// Отладка саг: байты .sg глав из Scens\*.dbf — в каталог (для сверки внешними разборщиками).
if (Arg("--dump-sagas") is { } dump)
{
    var tables = D2.Data.Sg.ScensTables.Load(Path.Combine(game, "Scens"));
    Directory.CreateDirectory(dump);
    foreach (string scen in tables.Scenarios) File.WriteAllBytes(Path.Combine(dump, scen + ".sg"), D2.Data.Sg.SagaSg.Build(tables, scen));
    Console.WriteLine($"саг {tables.Scenarios.Count}; поля без источника: {string.Join(", ", D2.Data.Sg.SagaSg.Defaulted.Order())}");
    return 0;
}
// Готовый перевод сценариев из локализованной копии игры -> data/scenarios/lang/<язык>/<сценарий>.json (D2.Data.Play.ScenarioTexts):
//   dotnet run --project tools/ScenarioImport -- --texts ru [--from <корень слоя>] [--source <откуда текст>]
// Без --from — слой Руссобит-М disciples-vanilla/l10n/russobit (Exports/*.sg квестов, Scens/Sinfo и Tscen глав саг). Пропуски слоя,
// уже дописанные в файл перевода, сохраняются; сценарии, которых в слое нет, не трогаются.
if (Arg("--texts") is { } language)
{
    string scenarios = Path.Combine(RepoRoot(), "data", "scenarios");
    string layer = Arg("--from") ?? D2.Data.Game.ScenarioTextsImport.DefaultRoot;
    string source = Arg("--source") ?? "Руссобит-М";
    var catalog = D2.Data.Game.GameCatalog.Load(Path.Combine(RepoRoot(), "data", "game"));
    int written = 0, texts = 0;
    foreach (var r in D2.Data.Game.ScenarioTextsImport.Read(scenarios, catalog, game, layer, language, source,
                                                             id => ScenarioTexts.Load([scenarios], language, id)))
    {
        if (r.File is null)
        {
            Console.WriteLine($"{r.Id}: нет в слое ({r.Missing.Count} текстов)");
            continue;
        }
        ScenarioTexts.Write(ScenarioTexts.PathOf(scenarios, language, r.Id), r.File);
        written++;
        texts += r.File.Texts.Count;
        if (r.Missing.Count + r.Extra.Count > 0)
            Console.WriteLine($"{r.Id}: без перевода {r.Missing.Count} ({string.Join(", ", r.Missing.Take(5))}), " +
                              $"лишних {r.Extra.Count} ({string.Join(", ", r.Extra.Take(5))})");
    }
    Console.WriteLine($"переводов {written}, текстов {texts} -> {Path.Combine(scenarios, ScenarioTexts.LangDir, language)}");
    return 0;
}

// Свои сценарии слоем мода (синтетические карты порта, tools/synth-maps): .sg каталога -> <мод>/data/scenarios/<имя>.json и карточки меню
// <мод>/data/game/scenarios.json (тот же разбор, что у штатных). Мод уже должен быть (mod.json); прежние файлы мода с теми же именами
// заменяются, чужие сценарии мода не трогаются.
//   dotnet run --project tools/ScenarioImport -- --sg-dir tools/synth-maps/out --mod mods/port-synth
if (Arg("--sg-dir") is { } sgDir)
{
    string mod = Arg("--mod") ?? throw new ArgumentException("--sg-dir требует --mod <каталог мода>");
    if (!File.Exists(Path.Combine(mod, "mod.json")))
    {
        Console.Error.WriteLine($"{mod}: нет mod.json — не мод");
        return 1;
    }
    var races = D2.Data.Game.GameCatalog.Load(Path.Combine(RepoRoot(), "data", "game")).Races;
    var cards = D2.Data.Game.GameImport.Scenarios(sgDir, races);
    string modScenarios = Path.Combine(mod, "data", "scenarios"), modGame = Path.Combine(mod, "data", "game");
    foreach (var card in cards)
    {
        var scenario = ScenarioFile.FromSg(Path.Combine(sgDir, card.File));
        ScenarioFile.Write(ScenarioFile.PathOf(modScenarios, scenario.Id), scenario);
        Console.WriteLine($"{scenario.Id}: отрядов {scenario.Looks.Map.Stacks.Count}, объектов {scenario.Looks.Map.Sites.Count}, " +
                          $"событий {scenario.Events.Events.Count}");
    }
    string catalogPath = Path.Combine(modGame, "scenarios.json");
    var kept = File.Exists(catalogPath)
        ? D2.Data.Game.GameCatalog.FromJson<D2.Data.Game.ScenarioInfo>(File.ReadAllText(catalogPath))
            .Where(c => !cards.Any(n => n.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase))).ToList()
        : [];
    Directory.CreateDirectory(modGame);
    File.WriteAllText(catalogPath, D2.Data.Game.GameCatalog.ToJson([.. kept, .. cards]));
    Console.WriteLine($"сценариев {cards.Count} -> {modScenarios}, карточки -> {catalogPath}");
    return 0;
}

string outDir = Arg("--out") ?? Path.Combine(RepoRoot(), "data", "scenarios");
string exports = Path.Combine(game, "Exports");
if (!Directory.Exists(exports))
{
    Console.Error.WriteLine($"нет Exports в {game}");
    return 1;
}

int count = 0, events = 0;
foreach (string sg in Directory.GetFiles(exports, "*.sg").Order(StringComparer.Ordinal))
{
    var scenario = ScenarioFile.FromSg(sg);
    ScenarioFile.Write(ScenarioFile.PathOf(outDir, scenario.Id), scenario);
    count++;
    events += scenario.Events.Events.Count;
}
// Главы саг — из таблиц Scens (D2.Data.Sg.SagaSg), id главы — SCEN_ID (S107SC0000).
string scens = Path.Combine(game, "Scens");
int chapters = 0;
if (Directory.Exists(scens))
{
    var sagas = D2.Data.Sg.ScensTables.Load(scens);
    foreach (string scen in sagas.Scenarios)
    {
        var chapter = ScenarioFile.FromSg(scen, D2.Data.Sg.SgFile.Load(D2.Data.Sg.SagaSg.Build(sagas, scen)));
        ScenarioFile.Write(ScenarioFile.PathOf(outDir, chapter.Id), chapter);
        chapters++;
        events += chapter.Events.Events.Count;
    }
}
Console.WriteLine($"сценариев {count}, глав саг {chapters}, событий {events} -> {outDir}");
return 0;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("не найден Disciples.sln выше текущей папки");
}
