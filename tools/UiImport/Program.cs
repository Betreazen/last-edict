using D2.Data;
using D2.Data.Ui;

// Перевод старой разметки (.dlg) в набор экранов порта data/ui/screens.json.
//
//   dotnet run --project tools/UiImport [-- --game <папка игры>] [--out <файл>]
//
// Разбор полей и правила (привязка, Legacy, устойчивый Id) живут в D2.Data.Ui.DlgImport — здесь только
// разбор параметров и запись файла. Перевод повторяем: те же исходники дают тот же байт в байт JSON,
// это проверяет UiScreensTests.Screens_Json_IsUpToDate.

string? game = Arg("--game");
string outPath = Arg("--out") ?? Path.Combine(RepoRoot(), "data", "ui", "screens.json");

var sources = DlgImport.Sources(game);
if (sources.Count == 0)
{
    Console.Error.WriteLine($"не найдено ни одного .dlg рядом с {game ?? GamePaths.GameDir}");
    return 1;
}

var screens = DlgImport.LoadAll(sources);
// Перевод строк приводится к \n: файл лежит в git и не должен меняться от того, на какой системе его собрали.
string json = UiScreens.ToJson(screens).ReplaceLineEndings("\n");
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, json);

Console.WriteLine($"исходники: {string.Join(", ", sources)}");
Console.WriteLine($"экранов {screens.Count}, элементов {screens.Sum(s => s.Elements.Count)} -> {outPath}" +
                  $" ({new FileInfo(outPath).Length / 1024} КБ)");
return 0;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Корень репозитория — ближайшая вверх папка с Disciples.sln: так вызов работает и из рабочего дерева git.
static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("не найден Disciples.sln выше текущей папки");
}
