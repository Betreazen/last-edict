using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;

// Повтор журнала команд партии (docs/game-commands.md §3): сохранение начала и журнал (их пишет сцена карты с D2_COMMAND_JOURNAL) → та же
// партия заново, хеш состояния после каждой команды сверяется с записанным. Баг-репорт игрока — эти два файла.
//
//   dotnet run --project tools/JournalReplay -- <журнал.jsonl> [--start <сохранение>] [--data <каталог data>]
//
// Сохранение по умолчанию — <журнал>.start.d2save; данные — data/ репозитория (содержимое base без модов, как у проверок).
// Код возврата 0 — повтор сошёлся; 1 — расхождение (первая команда с другим хешем); 2 — нет файлов.

string? journalPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
if (journalPath is null || !File.Exists(journalPath))
{
    Console.Error.WriteLine("журнал не задан или его нет: JournalReplay <журнал.jsonl> [--start <сохранение>] [--data <каталог data>]");
    return 2;
}
string start = Arg("--start") ?? journalPath + ".start.d2save";
if (!File.Exists(start))
{
    Console.Error.WriteLine($"нет сохранения начала {start}");
    return 2;
}
string data = Arg("--data") ?? Path.Combine(RepoRoot(), "data");

if (CommandJournal.FromJsonLines(File.ReadAllText(journalPath)).Mods is { Count: > 0 } mods)
{
    Console.WriteLine($"повтор невозможен: партия с модами {string.Join(", ", mods)}, а повтор берёт содержимое base без модов");
    return 2;
}
var content = GameContent.Load(data);
var book = GameBook.Load(Path.Combine(data, "content", "base"));
var journal = CommandJournal.FromJsonLines(File.ReadAllText(journalPath));
var session = new GameSession(GameSave.Read(start), content.MapArt, book);
int answers = journal.Entries.Sum(e => e.Answers?.Count ?? 0);
var kinds = journal.Entries.GroupBy(e => e.Command.GetType().Name.Replace("Command", "", StringComparison.Ordinal))
                   .Select(g => $"{g.Key} {g.Count()}");
try
{
    journal.Replay(session);
}
catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException)
{
    Console.WriteLine($"повтор разошёлся: {e.Message}");
    return 1;
}
Console.WriteLine($"повтор сошёлся: команд {journal.Entries.Count}, решений снаружи {answers}, хеш {StateHash.Of(session)}; {string.Join(", ", kinds)}");
return 0;

string? Arg(string name) => Array.IndexOf(args, name) is >= 0 and var i && i + 1 < args.Length ? args[i + 1] : null;

static string RepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) return dir.FullName;
    throw new DirectoryNotFoundException("нет корня репозитория (Disciples.sln)");
}
