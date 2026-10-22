using D2.Data.Ui;

// Перевод строк игры (Globals/Tglobal.dbf, Interf/TApp.DBF, cp866) в данные порта
// data/ui/strings.<язык>.json.
//
//   dotnet run --project tools/UiStringsImport -- --lang en
//   dotnet run --project tools/UiStringsImport -- --lang ru
//   dotnet run --project tools/UiStringsImport -- --lang ru --from <корень> --from <слой> --out <файл>
//
// Чтение dbf и слои живут в D2.Data.Ui.UiStringsImport — здесь только разбор параметров и запись файла.
// Перевод повторяем: те же исходники дают тот же байт в байт JSON, это проверяет
// UiStringsTests.Strings_Json_IsUpToDate.

string language = Arg("--lang") ?? "en";
var roots = Args("--from");
if (roots.Count == 0) roots = UiStringsImport.DefaultRoots(language).ToList();
string outPath = Arg("--out") ?? Path.Combine(RepoRoot(), "data", "ui", UiStrings.FileName(language));

var found = roots.Where(UiStringsImport.IsAvailable).ToList();
if (found.Count == 0)
{
    Console.Error.WriteLine($"не найдено таблиц строк ни в одном из корней: {string.Join(", ", roots)}");
    return 1;
}
if (found.Count < roots.Count)
    Console.Error.WriteLine($"пропущены корни без таблиц: {string.Join(", ", roots.Except(found))}");

var strings = UiStringsImport.Read(found);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
UiStrings.Save(outPath, strings);

Console.WriteLine($"слои: {string.Join(" + ", found)}");
Console.WriteLine($"язык {language}: строк {strings.Count} -> {outPath} " +
                  $"({new FileInfo(outPath).Length / 1024} КБ)");
return 0;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

List<string> Args(string name) =>
    args.Select((a, i) => (a, i)).Where(p => p.a == name && p.i + 1 < args.Length)
        .Select(p => args[p.i + 1]).ToList();

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("не найден Disciples.sln выше текущей папки");
}
