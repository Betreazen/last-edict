using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Maps;
using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Map;

// Замеры карт без потолка площади (Э6, Р6, Р16, roadmap §3.4): на картах 256², 512², 1024² (MapFactory — как редактор Студии) —
// сборка и чтение сценария, начало партии, память тумана, A* через всю карту, ход ИИ (день всех ИИ) и память процесса.
//
//   dotnet run -c Release --project tools/MapScale -- [--sizes 256,512,1024] [--content content/test-pack] [--seats 4] [--days 3]
//       [--out bench/map-scale.json]
//
// По числам решается, нужна ли разбивка карты на зоны (рост стоимости хода ИИ и A* сверх площади).

Console.OutputEncoding = System.Text.Encoding.UTF8;
string content = Path.GetFullPath(Arg("--content") ?? Path.Combine(Repo(), "content", "test-pack"));
int[] sizes = [.. (Arg("--sizes") ?? "256,512,1024").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))];
int seats = int.Parse(Arg("--seats") ?? "4", CultureInfo.InvariantCulture);
int days = int.Parse(Arg("--days") ?? "3", CultureInfo.InvariantCulture);
string output = Arg("--out") ?? Path.Combine(Repo(), "bench", "map-scale.json");

var set = ContentSource.Open(content);
var book = GameBook.Load(set);
var races = set.Races.Where(r => r.Playable == true && r.Neutral != true).Select(r => r.Id).ToList();
if (races.Count == 0)
{
    Console.Error.WriteLine($"в содержимом {content} нет играбельных рас");
    return 1;
}
var rows = new JsonArray();
Console.WriteLine("сторона | клеток | сборка мс | чтение мс | старт мс | туман МБ (игроков) | A* мс (клеток пути, раскрыто) | день ИИ мс | куча МБ | рабочий набор МБ");
foreach (int size in sizes)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    long before = GC.GetTotalMemory(forceFullCollection: true);
    var clock = Stopwatch.StartNew();
    var options = new NewMapOptions($"scale-{size}", size, [.. Enumerable.Range(0, seats).Select(i => new MapSeat(races[i % races.Count]))], Seed: 7);
    var scenario = MapFactory.Create(set, options);
    double build = clock.Elapsed.TotalMilliseconds;
    clock.Restart();
    string json = ScenarioFile.ToJson(scenario);
    scenario = ScenarioFile.FromJson(json);
    double read = clock.Elapsed.TotalMilliseconds;
    clock.Restart();
    var session = BalanceStand.Start(scenario, book, 1);
    double start = clock.Elapsed.TotalMilliseconds;
    var map = session.Map;
    long fogBytes = map.Fog.Values.Sum(f => (long)f.Length);
    // A* — от героя первой столицы к противоположному углу карты (длинный путь по всей карте).
    var hero = map.Stacks.Values.Where(s => s.Inside.Length > 0).OrderBy(s => s.Id, StringComparer.Ordinal).First();
    var site = map.Sites.First(s => s.Id == hero.Inside);
    int tx = site.X < size / 2 ? size - 6 : 5, ty = site.Y < size / 2 ? size - 6 : 5;
    var trace = new List<ExpandedNode>();
    clock.Restart();
    var path = PathFinder.Find(map, hero, tx, ty, session.MoveOptionsOf(hero), MovementRules.Vanilla, session.DayAllowance(hero), trace: trace);
    double astar = clock.Elapsed.TotalMilliseconds;
    clock.Restart();
    for (int d = 0; d < days && !session.Over; d++) session.Execute(new EndTurnCommand());
    double day = clock.Elapsed.TotalMilliseconds / days;
    long heap = GC.GetTotalMemory(forceFullCollection: true) - before;
    using var self = Process.GetCurrentProcess();
    self.Refresh();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{size} | {size * size} | {build:0} | {read:0} | {start:0} | {fogBytes / 1048576.0:0.0} ({map.Fog.Count}) | {astar:0.0} ({path.Points.Count}, {trace.Count}) | {day:0} | {heap / 1048576.0:0} | {self.WorkingSet64 / 1048576.0:0}"));
    rows.Add(new JsonObject
    {
        ["size"] = size, ["cells"] = size * size, ["build_ms"] = Math.Round(build), ["read_ms"] = Math.Round(read), ["start_ms"] = Math.Round(start),
        ["fog_bytes"] = fogBytes, ["fog_players"] = map.Fog.Count, ["astar_ms"] = Math.Round(astar, 1), ["path_points"] = path.Points.Count,
        ["astar_expanded"] = trace.Count, ["ai_day_ms"] = Math.Round(day), ["heap_bytes"] = heap, ["working_set_bytes"] = self.WorkingSet64,
        ["json_bytes"] = json.Length, ["stacks"] = map.Stacks.Count, ["sites"] = map.Sites.Count,
    });
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, new JsonObject { ["content"] = Path.GetFileName(content), ["seats"] = seats, ["days"] = days, ["rows"] = rows }
    .ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"-> {output}");
return 0;

string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

static string Repo()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) return dir.FullName;
    return Directory.GetCurrentDirectory();
}
