// Стенд симуляции баланса (Э4, docs/own-rules.md §5): N партий без людей на сценарии × зерно, параллельно по зёрнам, → JSONL —
// строка на игрока партии (BalanceRow). Метрики по расам — tools/balance_report.py (или design_metrics.py скилла game-design).
//
//   dotnet run -c Release --project tools/BalanceStand -- --content content/test-pack --scenario six-crowns --games 200 --days 40
//       [--seed 1] [--threads 0] [--scenarios <каталог>] [--out .cache/balance/<сценарий>.jsonl]
//
// --content — каталог содержимого (content/test-pack, data/content/base); сценарии — <content>/scenarios, у базы — data/scenarios.
// Зёрна — seed, seed + 1, …; партия с тем же зерном повторяется бит в бит, сколько бы потоков ни шло.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using LastEdict.Data.Content;
using LastEdict.Data.Play;

string content = Path.GetFullPath(Arg("--content") ?? Path.Combine("content", "test-pack"));
string scenarios = Arg("--scenarios") ?? (Directory.Exists(Path.Combine(content, "scenarios")) ? Path.Combine(content, "scenarios")
    : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(content)!)!, "scenarios"));
var names = Args("--scenario");
if (names.Count == 0) names = [.. Directory.GetFiles(scenarios, "*" + ScenarioFile.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];
int games = int.Parse(Arg("--games") ?? "100", System.Globalization.CultureInfo.InvariantCulture);
int days = int.Parse(Arg("--days") ?? "40", System.Globalization.CultureInfo.InvariantCulture);
uint seed0 = uint.Parse(Arg("--seed") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
int threads = int.Parse(Arg("--threads") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
if (threads <= 0) threads = Environment.ProcessorCount;

var book = GameBook.Load(ContentSource.Open(content));
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
foreach (string name in names)
{
    var scenario = ScenarioFile.Read(ScenarioFile.PathOf(scenarios, name));
    var rows = new ConcurrentBag<BalanceRow>();
    var clock = Stopwatch.StartNew();
    Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads },
                 i => { foreach (var row in BalanceStand.Play(scenario, book, seed0 + (uint)i, days)) rows.Add(row); });
    var ordered = rows.OrderBy(r => r.Seed).ThenBy(r => r.Order).ToList();
    string output = Arg("--out") is { } given && names.Count == 1 ? given : Path.Combine(".cache", "balance", $"{name}.jsonl");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllLines(output, ordered.Select(r => JsonSerializer.Serialize(r, json)));
    Console.WriteLine($"{name}: партий {games}, дней до {days}, решено захватом {ordered.Where(r => r.Decided).Select(r => r.Seed).Distinct().Count()}, " +
                      $"{clock.Elapsed.TotalSeconds:0.0} с на {threads} потоках -> {output}");
    foreach (var race in ordered.GroupBy(r => r.Race).OrderBy(g => g.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {race.Key}: мест {race.Count()}, побед {race.Count(r => r.Won)} ({100.0 * race.Count(r => r.Won) / race.Count():0.0} %)");
}
return 0;

string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

List<string> Args(string name) => [.. args.Select((a, i) => (a, i)).Where(p => p.a == name && p.i + 1 < args.Length).Select(p => args[p.i + 1])];
