using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LastEdict.Data.Import;
using Godot;

namespace LastEdict.Godot;

/// <summary>Главная сцена проекта: проверяет, что данные игры импортированы, и уходит в меню (<c>UiFlow.tscn</c>). Публичная сборка порта не
/// несёт ни графики, ни данных Disciples II — при первом запуске они импортируются из купленной копии игры (<c>docs/publishing.md</c>):
/// папку игры берём из <c>--game-dir</c>, сохранённой настройки, реестра GOG или спрашиваем у игрока, импорт идёт в <see cref="GameData.GameRoot"/>
/// (у игрока — <c>user://game</c>) в фоне с ходом на экране, затем — меню.
///
/// <para>Команда разработчика (без окна, код выхода 0/1): <c>godot --headless --path godot -- --import [--game-dir &lt;копия&gt;] [--l10n
/// &lt;слой русификации&gt;] [--out &lt;корень&gt;] [--steps rules,gfx,…] [--ffmpeg &lt;путь&gt;] [--threads N]</c>; без <c>--out</c> — в корень
/// данных (<see cref="GameData.GameRoot"/>: в репозитории — сам репозиторий). Обёртка — <c>tools/import_game.py</c>.</para></summary>
public partial class Boot : Control
{
    public const string MainScene = "res://scenes/ui/UiFlow.tscn";
    private const string Settings = "user://import.cfg";

    private LineEdit _game = null!, _l10n = null!;
    private Label _status = null!;
    private ProgressBar _bar = null!;
    private Button _start = null!;
    private RichTextLabel _log = null!;
    private Task<IReadOnlyDictionary<string, string>>? _task;
    private readonly CancellationTokenSource _cancel = new();
    private volatile string _progress = "";
    private double _fraction;
    private readonly Queue<string> _lines = new();
    private bool _headless, _direct;
    private string _out = "";

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _headless = DisplayServer.GetName() == "headless";
        // Окно, синхронизация и предел кадров из настроек — при запуске игры (без окна и в проверках — нет, DisplaySettings).
        if (DisplaySettings.Allowed(args)) DisplaySettings.Apply(LastEdict.Data.Map.MapSettings.Load(DisplaySettings.FilePath));
        if (args.Contains("--import"))
        {
            GetTree().Quit(ImportCommand(args));
            return;
        }
        if (IsReady(GameData.GameRoot))
        {
            CallDeferred(MethodName.Go);
            return;
        }
        _out = GameData.GameRoot;
        BuildUi();
        string? given = Arg(args, "--game-dir");
        string? l10n = Arg(args, "--l10n");
        var cfg = new ConfigFile();
        cfg.Load(Settings);
        string saved = (string)cfg.GetValue("import", "game_dir", "");
        _game.Text = given ?? (GameImporter.CheckGameDir(saved) is null ? saved : GameDirFinder.Find() ?? saved);
        _l10n.Text = l10n ?? (string)cfg.GetValue("import", "l10n_dir", "");
        Say(Tr2($"Данные игры будут импортированы в {_out}", $"Game data will be imported into {_out}"));
        // Папка задана явно (или запуск без окна) — импорт сразу, без вопросов.
        if (given is not null || _headless) StartImport();
    }

    public override void _Process(double delta)
    {
        lock (_lines)
            while (_lines.Count > 0)
            {
                string line = _lines.Dequeue();
                if (_headless) GD.Print(line);
                else _log?.AppendText(line + "\n");
            }
        if (_task is null) return;
        _bar.Value = _fraction * 100;
        if (_progress.Length > 0) _status.Text = _progress;
        if (!_task.IsCompleted) return;
        var task = _task;
        _task = null;
        if (task.IsCompletedSuccessfully && GameImporter.IsComplete(_out))
        {
            var cfg = new ConfigFile();
            cfg.Load(Settings);
            cfg.SetValue("import", "game_dir", _game.Text.Trim());
            cfg.SetValue("import", "l10n_dir", _l10n.Text.Trim());
            cfg.Save(Settings);
            GD.Print($"import done -> {_out}");
            GameData.UseGameRoot(_out);
            Go();
            return;
        }
        string error = task.Exception?.GetBaseException().Message ?? Tr2("импорт не завершён", "import incomplete");
        GD.PrintErr($"Boot: импорт не удался — {error}");
        _status.Text = Tr2($"Ошибка: {error}", $"Error: {error}");
        _start.Disabled = false;
        if (_headless) GetTree().Quit(1);
    }

    public override void _ExitTree() => _cancel.Cancel();

    private void Go() => GetTree().ChangeSceneToFile(MainScene);

    /// <summary>Данные готовы: в корне проекта (репозиторий разработчика, переносная сборка) — есть данные; в отдельном каталоге импорта —
    /// ещё и отметка «импорт целиком» этой версии (прерванный импорт или старая раскладка — импорт заново).</summary>
    private static bool IsReady(string root) =>
        GameData.HasGameData(root) && (Path.GetFullPath(root) == Path.GetFullPath(GameData.Root) || GameImporter.IsComplete(root));

    private void StartImport()
    {
        string game = _game.Text.Trim(), l10n = _l10n.Text.Trim();
        if (GameImporter.CheckGameDir(game) is { } why)
        {
            _status.Text = Tr2($"Папка не подходит: {why}", $"Not a game folder: {why}");
            GD.PrintErr($"Boot: {game}: {why}");
            if (_headless) GetTree().Quit(1);
            return;
        }
        if (l10n.Length > 0 && !GameImporter.IsL10nDir(l10n))
        {
            _status.Text = Tr2("В папке русификации нет Globals/Tglobal.dbf", "Localization folder has no Globals/Tglobal.dbf");
            if (_headless) GetTree().Quit(1);
            return;
        }
        _start.Disabled = true;
        var ctx = Context(game, l10n.Length > 0 ? l10n : null, _out, null, null);
        _task = Task.Run(() => GameImporter.Run(ctx), _cancel.Token);
    }

    private ImportContext Context(string game, string? l10n, string outRoot, string? ffmpeg, int? threads) =>
        new(game, outRoot, new GodotImageEncoder())
        {
            L10nDir = l10n,
            Ffmpeg = ffmpeg ?? LocalTool("ffmpeg"),
            Ffprobe = LocalTool("ffprobe"),
            ExtraDlgs = [GameData.FromRoot(Path.Combine("data", "import", "CustomLobby.dlg")),
                         GameData.FromRoot(Path.Combine("tools", "d2probe", "Examples", "Custom Lobby", "CustomLobby.dlg"))],
            Threads = threads ?? Math.Clamp(System.Environment.ProcessorCount, 1, 16),
            Cancel = _cancel.Token,
            Log = new LineWriter(Say),
            Progress = new Progress(this),
        };

    /// <summary>ffmpeg рядом с игрой (<c>ffmpeg/ffmpeg.exe</c> у исполняемого файла), иначе — из <c>PATH</c>.</summary>
    private static string? LocalTool(string name)
    {
        string local = GameData.FromRoot(Path.Combine("ffmpeg", name + (OperatingSystem.IsWindows() ? ".exe" : "")));
        return File.Exists(local) ? local : null;
    }

    private int ImportCommand(string[] args)
    {
        string game = Arg(args, "--game-dir") ?? LastEdict.Data.GamePaths.GameDir;
        string outRoot = Arg(args, "--out") is { } o ? GameData.FromProject(o) : OS.HasFeature("editor") ? GameData.Root : GameData.GameRoot;
        string? l10n = Arg(args, "--l10n");
        var steps = (Arg(args, "--steps") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int? threads = int.TryParse(Arg(args, "--threads"), out int n) ? n : null;
        _headless = _direct = true;
        GD.Print($"import game={game} l10n={l10n ?? "-"} out={outRoot} steps={(steps.Length == 0 ? "all" : string.Join(",", steps))}");
        try
        {
            var ctx = Context(game, l10n, outRoot, Arg(args, "--ffmpeg"), threads);
            var results = GameImporter.Run(ctx, steps);
            Flush();
            GD.Print($"import result {string.Join(" ", results.Select(r => $"{r.Key}={r.Value.Replace(' ', '_')}"))} complete={GameImporter.IsComplete(outRoot)}");
            return 0;
        }
        // Граница команды: любая ошибка импорта — строкой и кодом 1 (необработанное исключение Godot не роняет процесс).
        catch (Exception e)
        {
            Flush();
            var inner = GameImporter.Unwrap(e);
            GD.PrintErr($"import failed: {inner.GetType().Name}: {inner.Message}");
            GD.PrintErr(e.StackTrace ?? "");
            return 1;
        }
    }

    private void Flush()
    {
        lock (_lines) while (_lines.Count > 0) GD.Print(_lines.Dequeue());
    }

    private void Say(string line)
    {
        if (_direct)
        {
            GD.Print(line);
            return;
        }
        lock (_lines) _lines.Enqueue(line);
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color(0.08f, 0.07f, 0.06f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(820, 0) };
        box.SetAnchorsPreset(LayoutPreset.Center);
        box.Position = new Vector2(-410, -260);
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);
        box.AddChild(new Label
        {
            Text = Tr2("Укажите папку Disciples II (GOG)", "Select your Disciples II (GOG) folder"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        box.AddChild(new Label
        {
            Text = Tr2("Порт не содержит данных игры: графика, звук и таблицы импортируются один раз из вашей купленной копии (GOG 3.01).",
                       "The port ships no game data: art, sound and tables are imported once from your purchased copy (GOG 3.01)."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _game = Row(box, Tr2("Папка игры", "Game folder"), dir => _game.Text = dir);
        _l10n = Row(box, Tr2("Русификация (необязательно)", "Russian localization (optional)"), dir => _l10n.Text = dir);
        _start = new Button { Text = Tr2("Импортировать", "Import") };
        _start.Pressed += StartImport;
        box.AddChild(_start);
        _bar = new ProgressBar { CustomMinimumSize = new Vector2(0, 24) };
        box.AddChild(_bar);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);
        _log = new RichTextLabel { CustomMinimumSize = new Vector2(0, 220), ScrollFollowing = true };
        box.AddChild(_log);
    }

    private LineEdit Row(VBoxContainer box, string caption, Action<string> picked)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = caption, CustomMinimumSize = new Vector2(230, 0) });
        var edit = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(edit);
        var browse = new Button { Text = Tr2("Обзор…", "Browse…") };
        browse.Pressed += () =>
        {
            var dialog = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true,
                Title = caption, CurrentDir = Directory.Exists(edit.Text) ? edit.Text : "",
            };
            dialog.DirSelected += dir => picked(dir);
            dialog.Canceled += dialog.QueueFree;
            dialog.DirSelected += _ => dialog.QueueFree();
            AddChild(dialog);
            dialog.PopupCentered(new Vector2I(800, 500));
        };
        row.AddChild(browse);
        box.AddChild(row);
        return edit;
    }

    /// <summary>Подписи окна импорта — по языку системы (русская или английская): строк игры до импорта ещё нет.</summary>
    private static string Tr2(string ru, string en) => OS.GetLocaleLanguage() == "ru" ? ru : en;

    private static string? Arg(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>Ход импорта — в поля, их читает кадр (<see cref="_Process"/>); без окна — строкой раз в 10 %.</summary>
    private sealed class Progress(Boot boot) : IProgress<ImportProgress>
    {
        private string _step = "";
        private int _decile = -1;

        public void Report(ImportProgress value)
        {
            double share = value.Total > 0 ? (double)value.Done / value.Total : 0;
            int index = Array.IndexOf(GameImporter.Steps, value.Step);
            boot._fraction = (Math.Max(0, index) + share) / GameImporter.Steps.Length;
            boot._progress = $"{value.Step}: {value.Done}/{value.Total}";
            int decile = (int)(share * 10);
            if (value.Step == _step && decile == _decile) return;
            _step = value.Step;
            _decile = decile;
            if (value.Total > 1) boot.Say($"  {value.Step} {value.Done}/{value.Total}");
        }
    }

    /// <summary>Журнал импорта построчно — в окно и в вывод.</summary>
    private sealed class LineWriter(Action<string> line) : TextWriter
    {
        private readonly System.Text.StringBuilder _buffer = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                line(_buffer.ToString().TrimEnd('\r'));
                _buffer.Clear();
            }
            else _buffer.Append(value);
        }

        public override void WriteLine(string? value) => line(value ?? "");
    }
}
