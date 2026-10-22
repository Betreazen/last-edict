// Сохранения середины партии для проверок экранов (Э5, задача 0): в Last Edict нет сохранений живых партий корпуса эталона, и шаги
// ui-render-check берут положение из партии стенда (BalanceStand.Snapshots) — ИИ за всех с постоянным зерном, игрок --player в снимке —
// человек. Партия повторяется бит в бит, поэтому клетки и отряды снимка — те же при каждом прогоне на той же копии игры.
//
//   dotnet run -c Release --project tools/CheckSaves -- --scenario "Power to Corrupt" --player S073PL000a --days 5,21,30
//       [--seed 1] [--content data/content/base] [--scenarios data/scenarios] [--out .cache/check-saves]
//
// Файлы — <out>/<сценарий>-d<день>.d2save; печатает строку на снимок.

using System.Globalization;
using LastEdict.Data.Content;
using LastEdict.Data.Play;

string root = Environment.GetEnvironmentVariable("D2_DATA_DIR") is { Length: > 0 } dataDir ? dataDir : ".";
string content = Path.GetFullPath(Arg("--content") ?? Path.Combine(root, "data", "content", "base"));
string scenarios = Arg("--scenarios") ?? Path.Combine(root, "data", "scenarios");
string name = Arg("--scenario") ?? throw new ArgumentException("нужен --scenario");
string player = Arg("--player") ?? throw new ArgumentException("нужен --player");
var days = (Arg("--days") ?? throw new ArgumentException("нужен --days")).Split(',').Select(d => int.Parse(d, CultureInfo.InvariantCulture)).ToList();
uint seed = uint.Parse(Arg("--seed") ?? "1", CultureInfo.InvariantCulture);
string output = Arg("--out") ?? Path.Combine(".cache", "check-saves");

var book = GameBook.Load(ContentSource.Open(content));
var scenario = ScenarioFile.Read(ScenarioFile.PathOf(scenarios, name));
int written = 0;
foreach (var (day, state) in BalanceStand.Snapshots(scenario, book, seed, days, player))
{
    string path = Path.Combine(output, $"{name}-d{day:00}.d2save");
    GameSave.Write(path, state, $"{name} — день {day}");
    var map = state.Looks.Map;
    Console.WriteLine($"{path} день={day} отрядов_игрока={map.Stacks.Values.Count(s => s.Owner.Equals(player, StringComparison.OrdinalIgnoreCase) && s.Inside.Length == 0)}");
    written++;
}
if (written != days.Count)
{
    Console.Error.WriteLine($"партия кончилась раньше: снимков {written} из {days.Count}");
    return 1;
}
return 0;

string? Arg(string key) => Array.IndexOf(args, key) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
