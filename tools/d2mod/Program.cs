using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Validation;
using LastEdict.Data.Content;
using LastEdict.Data.Ui.Windows;

// d2mod — CLI Студии Last Edict (Э6): каждая операция окна Студии есть здесь и печатает числа (docs/tools/studio-architecture.md).
//
//   d2mod new <мод> --id <id> [--name <имя>]          новый мод: mod.json и content/
//   d2mod validate <мод> [--data <data>]             проверка поверх базы: mod=<id> entities=N errors=E warnings=W (код 1 — есть ошибки)
//   d2mod add <мод> <вид> <имя> [--json <тело>] [--file]   новая сущность каталогом (или в файле вида)
//   d2mod set <мод> <id> <путь> <значение JSON>       поле сущности (правка чужой — в слой мода)
//   d2mod unset <мод> <id> <путь>                     убрать поле из слоя мода
//   d2mod text <мод> <id> <язык> <ключ> <текст>       строка сущности-каталога (id строки — автоматический)
//   d2mod apply <мод> <сценарий.json> [--undo-check] [--dry]   команды по порядку; --undo-check — отменить всё и повторить
//   d2mod tree <мод> --lord <id> [--data <data>]      дерево построек лорда с модом: ветки, клетки, связи
//   d2mod run <мод> [--scenario <id> --player <id>] [--watch] [--godot <exe>]   игра отдельным процессом с модом
//
// База — data/content/base репозитория (или D2_DATA_DIR, --data). Пишет только внутри каталога мода.

if (args.Length < 2)
{
    Console.Error.WriteLine("d2mod <new|validate|add|set|unset|text|apply|tree|run> <мод> …  (подробно — tools/d2mod/Program.cs)");
    return 2;
}

string verb = args[0], modDir = args[1];
try
{
    return verb switch
    {
        "new" => New(),
        "validate" => Validate(),
        "add" => Edit(new AddEntity(args[2], args[3], Opt("--json") is { } body
            ? JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException("--json: ждали объект сущности { … }") : [], Folder: !Has("--file"))),
        "set" => Edit(new SetField(args[2], args[3], JsonNode.Parse(args[4]))),
        "unset" => Edit(new SetField(args[2], args[3], null, Unset: true)),
        "text" => Edit(new SetText(args[2], args[3], args[4], args[5])),
        "apply" => Apply(),
        "tree" => Tree(),
        "run" => Run(),
        _ => Fail($"неизвестная команда «{verb}»"),
    };
}
catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException
                              or ArgumentException or IndexOutOfRangeException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"d2mod {verb}: {e.Message}");
    return 1;
}

int New()
{
    var project = ModProject.Create(modDir, Opt("--id") ?? throw new ArgumentException("нужен --id"), Opt("--name"));
    Console.WriteLine($"mod={project.Id} dir={project.Dir}");
    return 0;
}

int Validate()
{
    var project = ModProject.Open(modDir);
    var doc = ModDocument.Load(project);
    var report = ModCheck.Run(doc, project, BaseDir());
    foreach (var item in report.Items) Console.WriteLine(item);
    Console.WriteLine($"mod={project.Id} entities={doc.Entities.Count} errors={report.Errors} warnings={report.Warnings} executes_code={(project.ExecutesCode ? 1 : 0)}");
    return report.Errors > 0 ? 1 : 0;
}

int Edit(IEditCommand command)
{
    var project = ModProject.Open(modDir);
    var history = new EditHistory(ModDocument.Load(project));
    history.Do(command);
    var (written, deleted) = history.Current.Save(project);
    Console.WriteLine($"mod={project.Id} {command.Name}: entities={history.Current.Entities.Count} written={written} deleted={deleted}");
    return 0;
}

int Apply()
{
    var project = ModProject.Open(modDir);
    var commands = EditCommands.ParseScript(File.ReadAllText(args[2]));
    var start = ModDocument.Load(project);
    var history = new EditHistory(start);
    foreach (var command in commands) history.Do(command);
    var result = history.Current;
    string undo = "-";
    if (Has("--undo-check"))
    {
        while (history.Undo()) { }
        bool back = history.Current.Fingerprint() == start.Fingerprint();
        while (history.Redo()) { }
        bool again = history.Current.Fingerprint() == result.Fingerprint();
        undo = back && again ? "ok" : $"FAIL(back={back},again={again})";
    }
    var (written, deleted) = Has("--dry") ? (0, 0) : result.Save(project);
    Console.WriteLine($"mod={project.Id} applied={commands.Count} entities={result.Entities.Count} written={written} deleted={deleted} undo={undo}");
    return undo.StartsWith("FAIL", StringComparison.Ordinal) ? 1 : 0;
}

int Tree()
{
    var project = ModProject.Open(modDir);
    var report = ModCheck.Run(ModDocument.Load(project), project, BaseDir());
    if (report.Content is not { } content)
    {
        foreach (var item in report.Items) Console.WriteLine(item);
        return 1;
    }
    string lord = Opt("--lord") ?? throw new ArgumentException("нужен --lord");
    var tree = BuildTree.OfLord(content, lord);
    foreach (var branch in tree.Branches)
    {
        var (w, h) = BuildTreeGeometry.Content(branch);
        Console.WriteLine($"branch {branch.Id} nodes={branch.Nodes.Count} size={w}x{h}");
        foreach (var n in branch.Nodes)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {n.Building} @{n.Column:0.##},{n.Row} <- {n.Parent ?? "-"}"));
    }
    Console.WriteLine($"tree lord={lord} branches={tree.Branches.Count} nodes={tree.Branches.Sum(b => b.Nodes.Count)} errors={report.Errors}");
    return 0;
}

int Run()
{
    var project = ModProject.Open(modDir);
    string repo = RepoRoot();
    string godot = Opt("--godot") ?? Environment.GetEnvironmentVariable("GODOT") ?? @"C:\Tools\bin\godot.cmd";
    var arguments = new List<string> { "--path", Path.Combine(repo, "godot") };
    string? scenario = Opt("--scenario");
    if (scenario is not null) arguments.Add("res://scenes/map/MapScreen.tscn");
    arguments.AddRange(["--", "--mods", project.Dir]);
    if (scenario is not null) arguments.AddRange(["--scenario", scenario]);
    if (Opt("--player") is { } player) arguments.AddRange(["--player", player]);
    if (Has("--watch")) arguments.Add("--watch");
    if (project.ExecutesCode) arguments.Add("--allow-plugins");   // свой мод автора с кодом — доверенный
    var start = new ProcessStartInfo(godot) { UseShellExecute = false };
    foreach (string a in arguments) start.ArgumentList.Add(a);
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"не запустился {godot}");
    Console.WriteLine($"run pid={process.Id} mod={project.Id} scenario={scenario ?? "-"} watch={(Has("--watch") ? 1 : 0)}");
    return 0;
}

string BaseDir()
{
    string data = Opt("--data")
                  ?? (Environment.GetEnvironmentVariable("D2_DATA_DIR") is { Length: > 0 } env ? Path.Combine(env, "data") : Path.Combine(RepoRoot(), "data"));
    string dir = Path.Combine(data, ContentFiles.BaseDir);
    return Directory.Exists(dir) ? dir : throw new DirectoryNotFoundException($"нет базы содержимого {dir} — импорт копии игры (py tools/import_game.py) или --data");
}

static string RepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "LastEdict.sln"))) return dir.FullName;
    return Directory.GetCurrentDirectory();
}

string? Opt(string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

bool Has(string name) => args.Contains(name);

static int Fail(string message)
{
    Console.Error.WriteLine($"d2mod: {message}");
    return 2;
}
