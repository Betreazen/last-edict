using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using D2.Data.Diagnostics;
using D2.Data.Play;
using Godot;

namespace D2.Godot;

/// <summary>
/// Зонд порта: всё, что видно при игре, — в журнал <c>user://probe/port-&lt;время&gt;.jsonl</c> (<see cref="PortJournal"/>; разбор —
/// <c>tools/port_log_report.py</c>).
/// <list type="bullet">
/// <item>Ошибки и предупреждения движка Godot и необработанные исключения C# — со стеком и последними сообщениями игры перед ними
/// (перехватчик журнала движка <see cref="OS.AddLogger"/>; исключение C# в обработчике Godot процесс не роняет — только пишет).</item>
/// <item>Все сообщения игры (<c>GD.Print</c>: ходы, бои, карточки, звук…).</item>
/// <item>Окна и экраны: открылось и закрылось (<c>UiScreenView.Hosted</c>, экран боя, карта).</item>
/// <item>Ввод: клавиши и кнопки мыши (с точкой).</item>
/// <item>Ход партии: чей ход, шаги отрядов, бои и их итог, события карты и сценария, предложения; в начале хода человека — снимок мира
/// и сохранение дня; инварианты карты; трасса решений ИИ (<see cref="PortJournalWatcher"/>).</item>
/// <item>Тормоза: кадр дольше <see cref="SlowFrameMs"/> мс; раз в <see cref="HeartbeatSeconds"/> с — кадры в секунду и память.</item>
/// <item><b>F9 — пометка</b>: снимок экрана, сохранение партии и строка <c>mark</c> в журнале; на экране — подтверждение с номером.</item>
/// </list>
/// Выключен без окна (проверки <c>--headless</c>), если не задано <c>D2_PORT_PROBE=1</c>; <c>D2_PORT_PROBE=0</c> выключает всегда;
/// <c>D2_PORT_PROBE_DIR</c> — другой каталог.
/// </summary>
public partial class PortProbe : Node
{
    public const double SlowFrameMs = 250;
    public const double HeartbeatSeconds = 30;

    /// <summary>Окно строки <c>frames</c> (схема 7.3): кадров и самый долгий кадр за столько секунд — как у зонда оригинала.</summary>
    public const double FramesSeconds = 10;

    private static PortProbe? _instance;
    private PortJournal? _journal;
    private AvTrace? _av;
    private string _dir = "";
    private int _marks;
    private double _beat;
    private ulong _frameAt;
    private long _frames;
    private double _framesMax, _framesFor;
    private readonly HashSet<GameSession> _watched = new(ReferenceEqualityComparer.Instance);
    private GameSession? _current;
    private CanvasLayer? _toastLayer;
    private Label? _toast;
    private double _toastLeft;

    /// <summary>Журнал зонда (или null — зонд выключен). Для строк из кода игры: <c>PortProbe.Journal?.Write(…)</c>.</summary>
    public static PortJournal? Journal => _instance?._journal;

    /// <summary>Строки звука, музыки, роликов и анимаций в виде зонда оригинала (схема 7.3, <see cref="AvTrace"/>); null — зонд
    /// выключен. Звать с главного потока: <see cref="Top"/> читает список экранов сцены.</summary>
    public static AvTrace? Av => _instance?._av;

    /// <summary>Верхний видимый экран (разметка <c>DLG_*</c>, карта, бой) — поле <c>top</c> строк звука; без зонда — «-».</summary>
    public static string Top => _instance?.TopScreen() ?? "-";

    /// <summary>Класс верхнего видимого экрана — поле <c>top_class</c> строк интерфейса (у зонда оригинала — класс окна движка,
    /// <c>CBriefingInterf</c>): у окна разметки — класс его логики, у прочих — класс сцены; нет экрана или зонда — null.</summary>
    public static string? TopClass => _instance?.TopNode() switch
    {
        null => null,
        UiScreenView { Logic: { } logic } => logic.GetType().Name,
        var node => node.GetType().Name,
    };

    /// <summary>Ролик <paramref name="name"/> в журнал зонда (схема 7.3): <c>video open</c> сейчас — кадры и кадры в секунду из описи
    /// роликов, <c>video close</c> — по концу или снятию проигрывателя (показано кадров по времени, мс). Без зонда — ничего.</summary>
    public static void Video(VideoStreamPlayer player, string name)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (Av is not { } av) return;
        var info = GameData.Content.VideosByName.GetValueOrDefault(name);
        long frames = info?.Frames ?? -1;   // нет в описи — -1, а не 0: сверка не примет его за ролик без кадров
        double fps = info?.Fps ?? 0;
        av.Video("open", name, frames, fps, 0, 0);
        ulong start = Time.GetTicksMsec();
        bool closed = false;
        void Close()
        {
            if (closed) return;
            closed = true;
            long ms = (long)(Time.GetTicksMsec() - start);
            long shown = fps > 0 ? Math.Min(frames, (long)Math.Round(ms * fps / 1000)) : 0;
            Av?.Video("close", name, frames, fps, shown, ms);
        }
        player.Finished += Close;
        player.TreeExiting += Close;
    }

    /// <summary>Поставить зонд (зовёт <see cref="GameData"/> первым делом в <c>_Ready</c>).</summary>
    public static void Attach(Node host)
    {
        if (_instance is not null || !Enabled()) return;
        var probe = new PortProbe { Name = "PortProbe" };
        _instance = probe;
        probe.Start();
        host.AddChild(probe);
    }

    private static bool Enabled()
    {
        string? flag = System.Environment.GetEnvironmentVariable("D2_PORT_PROBE");
        if (flag == "0") return false;
        return flag == "1" || DisplayServer.GetName() != "headless";
    }

    private void Start()
    {
        _dir = System.Environment.GetEnvironmentVariable("D2_PORT_PROBE_DIR") is { Length: > 0 } dir
            ? dir : ProjectSettings.GlobalizePath("user://probe");
        Directory.CreateDirectory(_dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        _journal = new PortJournal(Path.Combine(_dir, $"port-{stamp}.jsonl"));
        _av = new AvTrace(_journal);
        _journal.Write("start", new Dictionary<string, object?>
        {
            ["when"] = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
            ["commit"] = Commit(), ["godot"] = Engine.GetVersionInfo()["string"].AsString(),
            ["os"] = OS.GetName(), ["display"] = DisplayServer.GetName(), ["args"] = OS.GetCmdlineUserArgs(),
            ["screen"] = new[] { DisplayServer.ScreenGetSize().X, DisplayServer.ScreenGetSize().Y },
            ["adapter"] = RenderingServer.GetVideoAdapterName(), ["journal"] = _journal.Path,
            // Схема строк показа, общих с зондом оригинала (AvTrace): 7.6 — вспышки и надписи портретов боя с местом, метки карты,
            // погашенные кнопки (ui_enable) и нажатия элементов (ui_click).
            ["schema"] = "7.6",
        });
        OS.AddLogger(new ProbeLogger(_journal));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _journal?.Error("unhandled", e.ExceptionObject?.ToString() ?? "?", Where(e.ExceptionObject as Exception));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            _journal?.Error("task", e.Exception.ToString(), Where(e.Exception));
        GD.Print($"probe journal={_journal.Path.Replace(' ', '_')} F9=пометка");
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // и на паузе
        GetTree().NodeAdded += OnNodeAdded;
        GetTree().NodeRemoved += OnNodeRemoved;
        _toastLayer = new CanvasLayer { Layer = 128 };
        _toast = new Label
        {
            Position = new Vector2(16, 16), Visible = false, Modulate = new Color(1, 1, 0.6f),
        };
        _toast.AddThemeFontSizeOverride("font_size", 20);
        _toast.AddThemeColorOverride("font_outline_color", Colors.Black);
        _toast.AddThemeConstantOverride("outline_size", 6);
        _toastLayer.AddChild(_toast);
        AddChild(_toastLayer);
    }

    public override void _Process(double delta)
    {
        if (_journal is null) return;
        // Длина кадра — по часам, не по delta: Godot урезает delta до max_physics_steps_per_frame / physics_ticks_per_second
        // (8/60 ≈ 133 мс, проверено прогоном — docs/godot-notes.md), и порог 250 мс по delta не срабатывал никогда (Г20).
        ulong now = Time.GetTicksUsec();
        double frameMs = _frameAt == 0 ? 0 : (now - _frameAt) / 1000.0;
        _frameAt = now;
        _frames++;
        _framesMax = Math.Max(_framesMax, frameMs);
        if ((_framesFor += delta) >= FramesSeconds)
        {
            _av?.Frames(_frames, (long)Math.Round(_framesFor * 1000), (long)Math.Round(_framesMax), TopScreen());
            _frames = 0;
            _framesMax = 0;
            _framesFor = 0;
        }
        if (frameMs > SlowFrameMs)
            _journal.Write("slow_frame", new Dictionary<string, object?> { ["ms"] = Math.Round(frameMs), ["screen"] = TopScreen() });
        if ((_beat += delta) >= HeartbeatSeconds)
        {
            _beat = 0;
            _journal.Write("heartbeat", new Dictionary<string, object?>
            {
                ["fps"] = Engine.GetFramesPerSecond(), ["memory_mb"] = Math.Round(OS.GetStaticMemoryUsage() / 1048576.0, 1),
                ["objects"] = Performance.GetMonitor(Performance.Monitor.ObjectCount),
                ["nodes"] = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), ["screen"] = TopScreen(),
                ["day"] = _current?.Map.Turn,
            });
        }
        WatchSession(delta);
        if (_toast is not null && _toast.Visible && (_toastLeft -= delta) <= 0) _toast.Visible = false;
    }

    public override void _Input(InputEvent e)
    {
        if (_journal is null) return;
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                _journal.Write("key", new Dictionary<string, object?>
                {
                    ["key"] = OS.GetKeycodeString(key.Keycode), ["mods"] = key.GetModifiersMask().ToString(), ["screen"] = TopScreen(),
                });
                if (key.Keycode == Key.F9) Mark();
                break;
            case InputEventMouseButton { Pressed: true } mouse when mouse.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle:
                _journal.Write("click", new Dictionary<string, object?>
                {
                    ["button"] = mouse.ButtonIndex.ToString(), ["at"] = new[] { (int)mouse.Position.X, (int)mouse.Position.Y },
                    ["double"] = mouse.DoubleClick, ["screen"] = TopScreen(),
                });
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) _journal?.Close("closed");
        else if (what == NotificationCrash) _journal?.Close("crash");
    }

    public override void _ExitTree() => _journal?.Close("exit");

    /// <summary>F9: снимок экрана, сохранение партии, строка <c>mark</c>; на экране — «Пометка N записана».</summary>
    private void Mark()
    {
        if (_journal is null) return;
        int n = ++_marks;
        string shot = Path.Combine(_dir, $"{Path.GetFileNameWithoutExtension(_journal.Path)}-mark{n}.png");
        string? save = null;
        try
        {
            GetViewport().GetTexture().GetImage().SavePng(shot);
        }
        catch (Exception e)
        {
            shot = $"снимок не записан: {e.Message}";
        }
        if (_current is { } session)
        {
            save = Path.ChangeExtension(shot, ".d2save");
            try
            {
                GameSave.Write(save, session.State, $"пометка {n}");
            }
            catch (Exception e)
            {
                save = $"сохранение не записано: {e.Message}";
            }
        }
        _journal.Write("mark", new Dictionary<string, object?>
        {
            ["n"] = n, ["shot"] = shot, ["save"] = save, ["screen"] = TopScreen(), ["day"] = _current?.Map.Turn,
        });
        if (_toast is not null)
        {
            _toast.Text = $"Пометка {n} записана (F9) — {Path.GetFileName(_journal.Path)}";
            _toast.Visible = true;
            _toastLeft = 3;
        }
    }

    // ------------------------------------------------------------------ окна и экраны

    private readonly List<Node> _screens = new();

    private void OnNodeAdded(Node node)
    {
        if (_journal is null) return;   // без журнала WatchSession не зовётся и список не чистился бы — держал бы все карты
        if (node is MapView mapView) _views.Add(mapView);
        if (ScreenName(node) is not { } name) return;
        _screens.Add(node);
        // Имя окна (Hosted) хозяин ставит сразу после добавления — прочитать кадром позже.
        Callable.From(() => _journal?.Write("screen", new Dictionary<string, object?>
        {
            ["action"] = "open", ["name"] = ScreenName(node) ?? name, ["class"] = node.GetType().Name,
        })).CallDeferred();
    }

    private void OnNodeRemoved(Node node)
    {
        if (_journal is null || !_screens.Remove(node)) return;
        _journal.Write("screen", new Dictionary<string, object?>
        {
            ["action"] = "close", ["name"] = ScreenName(node), ["class"] = node.GetType().Name,
        });
    }

    /// <summary>Имя экрана: окно — его разметка (<c>Hosted</c>), иначе класс сцены (карта, бой, меню).</summary>
    private static string? ScreenName(Node node) => node switch
    {
        UiScreenView v => v.Hosted ?? "UiScreenView",
        MapScreen => "MapScreen",
        BattleScreen => "BattleScreen",
        UiFlowView => "UiFlowView",
        _ => null,
    };

    private string TopScreen() => TopNode() is { } node ? ScreenName(node) ?? "-" : "-";

    private Node? TopNode()
    {
        for (int i = _screens.Count - 1; i >= 0; i--)
            if (IsInstanceValid(_screens[i]) && _screens[i] is CanvasItem { Visible: true } && ScreenName(_screens[i]) is not null)
                return _screens[i];
        return null;
    }

    // ------------------------------------------------------------------ партия

    private readonly List<MapView> _views = new();
    private double _sessionCheck;

    /// <summary>Сессия текущей карты (загрузка создаёт новую) — подписать наблюдателя хода один раз; проверка раз в секунду.</summary>
    private void WatchSession(double delta)
    {
        if ((_sessionCheck += delta) < 1) return;
        _sessionCheck = 0;
        _views.RemoveAll(v => !IsInstanceValid(v));
        if (_views.Count == 0 || _views[^1] is not { } view) return;
        GameSession? session;
        try
        {
            session = view.Session;
        }
        catch (NullReferenceException)
        {
            return;   // вид ещё без партии
        }
        if (session is null || ReferenceEquals(session, _current)) return;
        _current = session;
        PortJournalWatcher? watcher = null;
        if (_watched.Add(session))
        {
            watcher = new PortJournalWatcher(_journal!, session);   // своя партия, не текущая: загрузка посреди хода ИИ не смешивает карты
            session.Watch(watcher);
            session.AiTrace = watcher;   // трасса ИИ: ai_pick, ai_step, ai_turn
        }
        _journal!.Write("session", new Dictionary<string, object?>
        {
            ["scenario"] = session.State.Info.ScenarioId, ["day"] = session.Map.Turn, ["humans"] = session.State.Info.Humans,
            ["viewer"] = view.Viewer,
        });
        // Снимок дня, сохранение и инварианты: первый ход человека начался до подписки. Идёт ход ИИ (рабочий поток меняет партию) — не
        // здесь: его конец даст ход человека, и снимок запишет HumanTurn.
        if (!view.AiRunning) watcher?.Attached(session.ActiveHuman);
    }

    // ------------------------------------------------------------------ служебное

    /// <summary>Коммит порта: <c>.git/HEAD</c> рядом с проектом (или «?»).</summary>
    private static string Commit()
    {
        try
        {
            string git = GameData.FromRoot(".git");
            string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head[..Math.Min(8, head.Length)];
            string refFile = Path.Combine(git, head[5..]);
            return File.Exists(refFile) ? File.ReadAllText(refFile).Trim()[..8] : head[5..];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "?";
        }
    }

    /// <summary>Первое место в коде порта из стека исключения.</summary>
    private static string? Where(Exception? e) => e?.StackTrace?.Split('\n')
        .FirstOrDefault(l => l.Contains("disciples-godot", StringComparison.OrdinalIgnoreCase))?.Trim();

    /// <summary>Перехватчик журнала движка: сообщения и ошибки (с любого потока).</summary>
    private sealed partial class ProbeLogger(PortJournal journal) : Logger
    {
        public override void _LogMessage(string message, bool error) => journal.Message(message, error);

        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify,
                                        int errorType, global::Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            string kind = errorType switch { 1 => "warning", 2 => "script", 3 => "shader", _ => "error" };
            string message = rationale.Length > 0 ? $"{code}: {rationale}" : code;
            string trace = string.Join("\n", scriptBacktraces.Select(b => b.Format()));
            // Исключение C# в обработчике Godot: текст — сам стек, место — первая строка кода порта в нём.
            string? where = (message + "\n" + trace).Split('\n')
                .FirstOrDefault(l => l.Contains("disciples-godot", StringComparison.OrdinalIgnoreCase) && !l.Contains(".godot", StringComparison.Ordinal))?.Trim();
            journal.Error(kind, message, where ?? $"{file}:{line} {function}", trace.Length > 0 ? trace : null);
        }
    }
}
