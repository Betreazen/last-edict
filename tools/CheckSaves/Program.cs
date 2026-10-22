// Сохранения середины партии для проверок экранов (Э5, задача 0): в Last Edict нет сохранений живых партий корпуса эталона, и шаги
// ui-render-check берут положение из партии стенда (BalanceStand.Snapshots): новая партия, как из меню (NewGame.Start — человек играет
// расой игрока --player, лорд-воин, сложность «средняя»), затем ИИ за всех с постоянным зерном; в снимке игрок --player — человек.
// Партия повторяется бит в бит, поэтому клетки и отряды снимка — те же при каждом прогоне на той же копии игры. День 1 — начало партии.
//
//   dotnet run -c Release --project tools/CheckSaves -- --scenario "Power to Corrupt" --player S073PL000a --days 5,21,30
//       [--seed 1] [--gold N] [--content data/content/base] [--scenarios data/scenarios] [--out .cache/check-saves]
//
// --gold — золото человека в снимке не меньше N: ИИ за него тратит казну, а шагам окон нужны найм и дань.
//
// Файлы — <out>/<сценарий>-d<день>.d2save; печатает строку на снимок.

using System.Globalization;
using LastEdict.Data.Content;
using LastEdict.Data.Game;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;

string root = Environment.GetEnvironmentVariable("D2_DATA_DIR") is { Length: > 0 } dataDir ? dataDir : ".";
string content = Path.GetFullPath(Arg("--content") ?? Path.Combine(root, "data", "content", "base"));
string scenarios = Arg("--scenarios") ?? Path.Combine(root, "data", "scenarios");
string name = Arg("--scenario") ?? throw new ArgumentException("нужен --scenario");
string player = Arg("--player") ?? throw new ArgumentException("нужен --player");
var days = (Arg("--days") ?? throw new ArgumentException("нужен --days")).Split(',').Select(d => int.Parse(d, CultureInfo.InvariantCulture)).ToList();
uint seed = uint.Parse(Arg("--seed") ?? "1", CultureInfo.InvariantCulture);
string output = Arg("--out") ?? Path.Combine(".cache", "check-saves");
long gold = long.Parse(Arg("--gold") ?? "0", CultureInfo.InvariantCulture);

var book = GameBook.Load(ContentSource.Open(content));
var scenario = ScenarioFile.Read(ScenarioFile.PathOf(scenarios, name));
var catalog = GameCatalog.Load(Path.Combine(root, "data", "game"));
string race = scenario.Looks.Map.Players[player].RaceId;
var setup = new NewGameSetup
{
    Scenario = catalog.Scenario(name) ?? throw new ArgumentException($"сценария «{name}» нет в каталоге"),
    RaceId = catalog.Race(race).Id,
};
var start = NewGame.Start(scenario, setup, catalog, book, new Random(1));
if (!start.Info.Humans.Contains(player, StringComparer.OrdinalIgnoreCase))
    throw new ArgumentException($"новая партия за расу {race} досталась {string.Join(",", start.Info.Humans)}, а не {player}");
int written = 0;
foreach (var (day, snapshot) in BalanceStand.Snapshots(start, book, seed, days, player))
{
    var state = WithGold(snapshot);
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

GameState WithGold(GameState state)
{
    var map = state.Looks.Map;
    var human = map.Players[player];
    if (human.Bank.Gold >= gold) return state;
    var players = new Dictionary<string, LastEdict.Rules.Map.MapPlayer>(map.Players, StringComparer.OrdinalIgnoreCase)
    {
        [player] = human with { Bank = human.Bank with { Gold = gold } },
    };
    return state with { Looks = GameSession.Retold(state.Looks, GameSession.With(map, players: players), state.Looks.Stacks) };
}

string? Arg(string key) => Array.IndexOf(args, key) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
