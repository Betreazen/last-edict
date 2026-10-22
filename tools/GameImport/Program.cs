using D2.Data;
using D2.Data.Game;
using D2.Data.Map;

// Каталог меню из копии игры: data/game/races.json (Globals/Grace.dbf + Glord.dbf) и
// data/game/scenarios.json (Exports/*.sg), data/game/campaigns.json (Campaign/CCamp.dbf, CScen.dbf, главы — Scens/*.dbf); данные отрисовки карты data/map/*.json (ImgData/IIsoLay.dbf,
// Globals/GLmark.dbf, Globals/GMabi.dbf).
//
//   dotnet run --project tools/GameImport [-- --game <папка игры>] [--out <каталог>]
//
// Разбор живёт в D2.Data.Game.GameImport — здесь только параметры и запись. Перевод повторяем: те же исходники
// дают тот же байт в байт JSON, это проверяет GameCatalogTests.Catalog_Json_IsUpToDate.

string game = Arg("--game") ?? GamePaths.GameDir;
string outDir = Arg("--out") ?? Path.Combine(RepoRoot(), "data", "game");
string globals = Path.Combine(game, "Globals"), exports = Path.Combine(game, "Exports");
if (!Directory.Exists(globals) || !Directory.Exists(exports))
{
    Console.Error.WriteLine($"нет Globals или Exports в {game}");
    return 1;
}

var races = GameImport.Races(globals);
var scenarios = GameImport.Scenarios(exports, races, Path.Combine(game, "Campaign"));
Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "races.json"), GameCatalog.ToJson(races));
File.WriteAllText(Path.Combine(outDir, "scenarios.json"), GameCatalog.ToJson(scenarios));
var campaigns = GameImport.Campaigns(game, races);
File.WriteAllText(Path.Combine(outDir, "campaigns.json"), GameCatalog.ToJson(campaigns));
var mapArt = MapArtData.Import(game);
string mapDir = Arg("--map-out") ?? Path.Combine(RepoRoot(), "data", "map");
mapArt.Save(mapDir);
Console.WriteLine($"слоёв карты {mapArt.Layers.Count}, ориентиров {mapArt.Landmarks.Count}, юнитов с бонусом хода {mapArt.MoveBonuses.Count} -> {mapDir}");
Console.WriteLine($"рас {races.Count} (лордов {races.Sum(r => r.Lords.Count)}), сценариев {scenarios.Count}, саг {campaigns.Count} " +
                  $"(глав {campaigns.Sum(c => c.Chapters.Count)}) -> {outDir}");
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
