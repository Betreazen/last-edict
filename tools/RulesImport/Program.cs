using D2.Data;
using D2.Data.Content;
using D2.Data.Dbf;

// Таблицы игры -> data/rules/*.json (выгрузка таблиц оригинала для сверок) и содержимое правил data/content/base (мод base,
// его читают правила порта) со схемами data/content/schema. Принцип проекта: старые форматы только на входе.
//
//   dotnet run --project tools/RulesImport [-- --game <папка игры>] [--out <каталог>]
//
// Перевод повторяем: те же .dbf дают тот же байт в байт JSON, это проверяет RulesDataTests.

string[] tables =
[
    "Gattacks.dbf", "Gunits.dbf", "GVars.dbf", "Gimmu.dbf", "GimmuC.dbf", "Gtransf.dbf", "GDynUpgr.dbf",
    "Gmodif.dbf", "GmodifL.dbf", "GleaUpg.dbf", "Glord.dbf", "Grace.dbf", "GLabi.dbf", "GItem.dbf",
    "Gspells.dbf", "GAI.dbf", "GcityInf.dbf", "Gbuild.dbf", "Gaction.dbf", "GLmark.dbf", "GMabi.dbf",
    "LleadA.dbf", "LmagItm.dbf", "LDthAnim.dbf", "LattS.dbf", "LAttR.dbf", "LattC.dbf", "GbuiList.dbf", "Tleader.dbf", "GspellR.dbf",
    "TAiMsg.dbf",
];

string game = Arg("--game") ?? GamePaths.GameDir;
string outDir = Arg("--out") ?? Path.Combine(RepoRoot(), "data", "rules");
string globals = Path.Combine(game, "Globals");
if (!Directory.Exists(globals))
{
    Console.Error.WriteLine($"нет Globals в {game}");
    return 1;
}

Directory.CreateDirectory(outDir);
long rows = 0;
foreach (string name in tables)
{
    var table = GameTables.Read(globals, name);
    File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(name) + ".json"), GameTables.ToJson(table));
    rows += table.Rows.Count;
}
Console.WriteLine($"таблиц {tables.Length}, строк {rows} -> {outDir}");

// Содержимое правил (мод base) и схемы JSON — рядом с data/rules: data/content/base, data/content/schema (docs/content-schema.md).
string dataDir = Path.GetDirectoryName(Path.GetFullPath(outDir))!;
var content = LegacyContent.Import(globals);
ContentFiles.WriteMod(content, Path.Combine(dataDir, ContentFiles.BaseDir), "base", "Disciples II");
ContentFiles.WriteSchemas(Path.Combine(dataDir, ContentFiles.SchemaDir));
Console.WriteLine($"сущностей {ContentSet.Kinds.Sum(k => content.Of(k.Type).Count)} -> {Path.Combine(dataDir, ContentFiles.BaseDir)}");
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
