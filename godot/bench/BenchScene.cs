using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LastEdict.Data.Battle;
using LastEdict.Data.Gfx;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Бенчмарки сцен Last Edict (этап 6 пайплайна gdscript-knowledge; Э5, задача 0 — на своих данных, без корпуса эталона): открытие карты,
/// кадр карты, сборка земли, ход ИИ, экран боя, окна UI, память. Формат JSON — как у <c>bench.gd</c> скилла (<c>header</c>,
/// <c>iterations</c>, <c>results</c>: имя → отсортированные замеры, мкс; у памяти — байты), сравнение — <c>bench_compare.gd</c>.
/// 3 прогрева, 15 замеров.
///
/// <para>Партия карты — сохранение середины партии стенда (<c>D2_BENCH_STATE</c>, <c>.d2save</c> из <c>tools/CheckSaves</c>; его пишет
/// <c>tools/bench/run_all.py</c>), бой — бой-образец сценария импорта (<see cref="BattleScenario"/>). Запуск — <c>run_all.py</c> или
/// <c>godot --headless --path godot res://bench/BenchScene.tscn -- --no-mods [--filter=a,b] [--save=../bench/before.json]</c>;
/// <c>--loop=имя --seconds=N</c> — крутить один случай (снять стеки <c>dotnet-stack report -p PID</c>).</para>
///
/// <para>Headless: рендера нет (заглушка RenderingServer), GPU и draw calls не мерятся. Кадр — время между соседними <c>process_frame</c>
/// при выключенном сне headless (весь шаг главного цикла на CPU), среднее на кадр за <see cref="Frames"/> кадров; открытие —
/// Stopwatch вокруг <c>AddChild</c> (весь синхронный <c>_Ready</c>). Сборка C# из редактора — Debug: цифры годятся для сравнения
/// «до/после» на этой же сборке, не как абсолютные. Память (<c>mem_*</c>) — байты после открытия карты и боя: рабочий набор процесса,
/// управляемая куча, текстуры графики карты (<see cref="GfxTextures.Bytes"/>) — условие Э5 «память не хуже ×1» (HD-графика).</para>
/// </summary>
public partial class BenchScene : Node
{
    private const int Warmup = 3;
    private const int Runs = 15;

    /// <summary>Кадров на один замер сценария кадра.</summary>
    private const int Frames = 64;

    /// <summary>Сколько кадров ждать готовности земли под камерой, прежде чем считать открытие сорванным.</summary>
    private const int GroundTimeoutFrames = 2000;

    /// <summary>Бой-образец: сценарий импорта и пара отрядов (тот же, что у шагов боя ui-render-check).</summary>
    private static readonly (string Scenario, string Attacker, string Defender) BattleScenario = ("Mercantile", "S135KC0000", "S135KC0020");

    /// <summary>Второй замер открытия карты — сценарий импорта 120² (самая большая из карт снимков «порт против порта»).</summary>
    private const string BigScenario = "S142SC0000";

    private sealed record SceneCase(string Name, Func<Task<double>> Sample, Func<Task>? Setup = null, Func<Task>? Teardown = null);

    private string _state = "";
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);

    public override void _Ready() => _ = Main();

    private async Task Main()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int code = 0;
        try
        {
            OS.LowProcessorUsageModeSleepUsec = 0;   // headless иначе спит ~7 мс на кадр (окно не рисуется) — замеру не мешает, но тянет время
            _state = System.Environment.GetEnvironmentVariable("D2_BENCH_STATE") is { Length: > 0 } s
                ? s : throw new InvalidOperationException("нет D2_BENCH_STATE — сохранения партии для замеров карты (tools/bench/run_all.py)");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var cases = Cases();
            if (Arg(args, "--loop=") is { } loop) await Loop(cases.First(x => x.Name == loop), args);
            else await RunAll(cases, args);
        }
        catch (Exception e)
        {
            // Необработанное исключение C# процесс не роняет (docs/godot-notes.md) — выйти с кодом самим.
            GD.PrintErr($"BenchScene: {e}");
            code = 1;
        }
        // Выход — на всех путях: без него процесс остаётся крутить пустые кадры без сна (LowProcessorUsageModeSleepUsec = 0).
        GetTree().Quit(code);
    }

    private async Task RunAll(List<SceneCase> cases, string[] args)
    {
        string header = $"Godot {Engine.GetVersionInfo()["string"]}, debug={OS.IsDebugBuild()}, csharp={CSharpConfig}, headless";
        GD.Print(header);
        var only = (Arg(args, "--filter=") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var results = new global::Godot.Collections.Dictionary();
        foreach (var one in cases.Where(x => only.Length == 0 || only.Contains(x.Name)))
            results[one.Name] = await Run(one);
        foreach (var (name, note) in _notes) GD.Print($"note {name}: {note}");
        if (Arg(args, "--save=") is { } save) Save(save, header, results);
    }

    private static string CSharpConfig =>
        typeof(BenchScene).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false)
            is [DebuggableAttribute { IsJITOptimizerDisabled: true }] ? "Debug" : "Release";

    private List<SceneCase> Cases()
    {
        var cases = new List<SceneCase>
        {
            // Открытие экрана карты: MapScreen._Ready (MapScene, вода, объекты, облака, панели, мини-карта) — синхронная часть;
            // и до готовности земли под камерой (куски собираются в фоновых потоках, текстуры — на главном).
            new("map_open", () => OpenMap(SaveSession, groundReady: false)),
            new("map_open_ground", () => OpenMap(SaveSession, groundReady: true)),
            // То же с пустыми кэшами картинок (GfxTextures, UiArt) перед каждым замером — первое открытие карты в партии: разбор файлов
            // графики (WebP/PNG) целиком в замере. Кэш файлов ОС при этом тёплый.
            new("map_open_cold", () => Cold(() => OpenMap(SaveSession, groundReady: false))),
            new("map_open_ground_cold", () => Cold(() => OpenMap(SaveSession, groundReady: true))),
        };
        if (File.Exists(ScenarioFile.PathOf(GameData.ScenariosDir, BigScenario)))
            cases.Add(new($"map_open_ground_{BigScenario}", () => OpenMap(() => ScenarioSession(BigScenario), groundReady: true)));
        cases.AddRange(MapFrameCases());
        cases.Add(new("ground_compose_view", ComposeView));
        cases.Add(new("ai_turn_view", AiTurnOnView));
        cases.Add(new("ai_turn_logic", AiTurnLogic));
        cases.AddRange(BattleCases());
        foreach (string screen in new[] { "DLG_CITY_STACK", "DLG_OPTIONS", "DLG_BATTLE_A", "DLG_CAST_SPELL", "DLG_MAIN_MENU" })
            cases.Add(new($"ui_open_{screen}", () => OpenUi(screen)));
        // Окно построек (Э6): дерево данными на Control — открытие с деревом лорда игрока и шаг зума колесом с перерисовкой вида.
        cases.Add(new("ui_open_build_tree", () => OpenBuild(zoom: false)));
        cases.Add(new("build_tree_zoom", () => OpenBuild(zoom: true)));
        cases.AddRange(MemoryCases());
        return cases;
    }

    // --- карта -------------------------------------------------------------------------------------------------

    private GameSession SaveSession() => new(GameSave.Read(_state), GameData.Content.MapArt, GameData.Book);

    private static GameSession ScenarioSession(string id)
    {
        var scenario = ScenarioFile.Read(ScenarioFile.PathOf(GameData.ScenariosDir, id));
        var looks = scenario.Looks;
        var state = new GameState(GameInfo.FromMap(id, looks), looks) with
        {
            Events = scenario.Events, EventProgress = LastEdict.Rules.Map.EventProgress.From(scenario.Events),
        };
        return new GameSession(state, GameData.Content.MapArt, GameData.Book);
    }

    private static string? ViewerOf(GameSession session) =>
        session.State.Info.Humans.FirstOrDefault() ?? session.Map.Players.Values.FirstOrDefault(p => p.IsHuman)?.Id
        ?? session.Map.Players.Keys.Order(StringComparer.Ordinal).FirstOrDefault();

    private MapScreen? _mapScreen;
    private MapView? _mapView;

    /// <summary>Открыть экран карты. Партия читается вне замера; замер — <c>AddChild</c> (синхронный <c>_Ready</c>), при
    /// <paramref name="groundReady"/> — ещё и кадры до готовности всей земли под камерой.</summary>
    private async Task<double> OpenMap(Func<GameSession> session, bool groundReady)
    {
        var s = session();
        var screen = new MapScreen { Session = s, Viewer = ViewerOf(s), Name = "BenchMap" };
        long t0 = Stopwatch.GetTimestamp();
        AddChild(screen);
        double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        var view = (MapView)screen.FindChild("MapView", true, false);
        if (groundReady)
        {
            await GroundUnder(view);
            us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        }
        _notes[groundReady ? $"map_open_ground {s.State.Info.ScenarioName}" : "map_open"] =
            $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} sprites={view.SpriteCount} size={s.Map.Size}";
        await Close(screen);
        return us;
    }

    private async Task GroundUnder(MapView view)
    {
        int frames = 0;
        while (!view.GroundReady)
        {
            if (++frames > GroundTimeoutFrames) throw new TimeoutException("земля под камерой не собралась");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Замер с пустыми кэшами картинок: предыдущий экран уже снят (<see cref="Close"/>), так что отпустить текстуры можно.</summary>
    private static Task<double> Cold(Func<Task<double>> sample)
    {
        GfxTextures.Release();
        UiArt.ForgetImages();
        return sample();
    }

    private async Task Close(Node node)
    {
        RemoveChild(node);
        node.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task OpenMapScreen()
    {
        var s = SaveSession();
        _mapScreen = new MapScreen { Session = s, Viewer = ViewerOf(s), Name = "BenchMap" };
        AddChild(_mapScreen);
        _mapView = (MapView)_mapScreen.FindChild("MapView", true, false);
        await GroundUnder(_mapView);
        _notes["map_frame"] = $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} sprites={_mapView.SpriteCount} " +
                              $"draw_calls={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} (headless — 0)";
    }

    private async Task ShutMapScreen()
    {
        if (_mapScreen is not null) await Close(_mapScreen);
        _mapScreen = null;
        _mapView = null;
    }

    /// <summary>Кадр боя поверх карты, как в партии (MapScreen.OpenBattle: карта скрыта, экран боя — ребёнок экрана карты): свой отряд
    /// зрителя против первого чужого.</summary>
    private async Task OpenBattleOverMap()
    {
        await OpenMapScreen();
        var s = _mapScreen!.Session ?? throw new InvalidOperationException("карта без партии");
        string viewer = ViewerOf(s)!;
        var mine = s.Map.Stacks.Values.Where(x => x.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase) && x.Inside.Length == 0)
                                     .OrderBy(x => x.Id, StringComparer.Ordinal).First();
        var other = s.Map.Stacks.Values.Where(x => !x.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase) && x.Inside.Length == 0)
                                      .OrderBy(x => x.Id, StringComparer.Ordinal).First();
        typeof(MapScreen).GetMethod("OpenBattleNow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(_mapScreen, [mine.Id, other.Id, (int?)0]);
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Кадр карты: стоим (анимации воды, облаков, объектов, фон мест) и прокрутка камеры на клетку каждый кадр
    /// по кругу (куски земли — подгрузка, текстуры на главном потоке).</summary>
    private IEnumerable<SceneCase> MapFrameCases()
    {
        yield return new("map_frame_idle", () => FrameMean("map_frame_idle", null), OpenMapScreen, ShutMapScreen);
        yield return new("battle_over_map_frame", () => FrameMean("battle_over_map_frame", null), OpenBattleOverMap, ShutMapScreen);
        // Пересборка картинок объектов (MapView.Reload: MapScene.Build + MapObjects.Build) — после каждого шага отряда и хода ИИ.
        // Текстуры уже в кэше вида: замер — сборка сцены и пересоздание узлов Sprite2D.
        yield return new("map_reload", () =>
        {
            long t0 = Stopwatch.GetTimestamp();
            _mapView!.Reload();
            return Task.FromResult(Stopwatch.GetElapsedTime(t0).TotalMicroseconds);
        }, OpenMapScreen, ShutMapScreen);
        // Восемь направлений по восемь шагов (8 клеток = кусок земли 256 точек по горизонтали): за замер камера обходит круг и
        // возвращается на место, каждый замер одинаков.
        (int, int)[] ring = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
        yield return new("map_frame_scroll",
                         () => FrameMean("map_frame_scroll", i => { var (dx, dy) = ring[i / 8 % ring.Length]; _mapView!.Step(dx, dy); }),
                         OpenMapScreen, ShutMapScreen);
    }

    /// <summary>Среднее время кадра (мкс) за <see cref="Frames"/> кадров: между соседними <c>process_frame</c> при выключенном сне
    /// headless — весь шаг главного цикла на CPU (process, physics, заглушка рендера). <c>TIME_PROCESS</c> не годится: движок
    /// обновляет его раз в секунду максимумом за секунду. <paramref name="perFrame"/> — действие перед кадром; самый долгий кадр —
    /// в заметки.</summary>
    private async Task<double> FrameMean(string name, Action<int>? perFrame)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        long start = Stopwatch.GetTimestamp(), last = start;
        double worst = 0;
        for (int i = 0; i < Frames; i++)
        {
            perFrame?.Invoke(i);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            long now = Stopwatch.GetTimestamp();
            worst = Math.Max(worst, Stopwatch.GetElapsedTime(last, now).TotalMicroseconds);
            last = now;
        }
        _worst[name] = Math.Max(worst, _worst.GetValueOrDefault(name));
        _notes[$"{name} worst_frame_us"] = $"{_worst[name]:F0}";
        return Stopwatch.GetElapsedTime(start, last).TotalMicroseconds / Frames;
    }

    private readonly Dictionary<string, double> _worst = new(StringComparer.Ordinal);

    /// <summary>Ход ИИ через вид карты: <see cref="MapView.EndTurn"/> (рабочий поток + заявки главному на каждый ход отряда, бой,
    /// событие) до <see cref="MapView.AiTurnFinished"/> (с перерисовкой карты). Показ ходов выключен (<c>AiAnimate</c>), скорость
    /// пауз — 1000×: остаются логика хода, передачи между потоками и перестройка картинок. Вид без <see cref="MapScreen"/>: подписчика
    /// боя нет, бой с человеком — автобой ядра.</summary>
    private async Task<double> AiTurnOnView()
    {
        var s = SaveSession();
        var view = new MapView { Standalone = false, Name = "BenchAiView" };
        AddChild(view);
        view.Show(s, "bench", ViewerOf(s));
        view.AiAnimate = false;
        view.AiSpeed = 1000f;
        await GroundUnder(view);
        var done = new TaskCompletionSource();
        view.AiTurnFinished += () => done.TrySetResult();
        ulong frame0 = Engine.GetProcessFrames();
        long t0 = Stopwatch.GetTimestamp();
        view.EndTurn();
        await done.Task;
        double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        _notes["ai_turn_view"] = $"frames={Engine.GetProcessFrames() - frame0} day={s.Map.Turn}";
        // Продолжение идёт прямо из MapView._Process (событие AiTurnFinished) — снимать вид только со следующего кадра.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Close(view);
        return us;
    }

    /// <summary>Тот же ход ИИ без вида — чистая логика в сборке Godot (Debug): разница с <c>ai_turn_view</c> — цена вида.</summary>
    private Task<double> AiTurnLogic()
    {
        var s = SaveSession();
        long t0 = Stopwatch.GetTimestamp();
        s.EndTurn();
        return Task.FromResult(Stopwatch.GetElapsedTime(t0).TotalMicroseconds);
    }

    private GroundArt? _groundArt;

    /// <summary>Сборка земли без потоков: все куски 256×256, покрывающие вид 1024×640 в центре карты, свежим сборщиком
    /// (картинки узоров уже в памяти — как в игре после первого открытия). Тумана нет.</summary>
    private Task<double> ComposeView()
    {
        var map = GameSave.Read(_state).Looks.Map;
        _groundArt ??= new GroundArt(GameData.Content.Gfx, GameData.GfxRoot);
        var chunks = new GroundChunks(new GroundComposer(MapSource.GroundCells(map), map.MapSeed, _groundArt), 256, capacity: 96);
        var (cx, cy) = GroundGeometry.CellOrigin(map.Size / 2, map.Size / 2);
        var keys = chunks.Covering(cx - 512, cy - 320, 1024, 640).ToList();
        long t0 = Stopwatch.GetTimestamp();
        long pixels = 0;
        foreach (var key in keys) pixels += chunks.Get(key).Pixels.Length;
        double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        _notes["ground_compose_view"] = $"chunks={keys.Count} bytes={pixels}";
        return Task.FromResult(us);
    }

    // --- бой ---------------------------------------------------------------------------------------------------

    private BattleScreen? _battle;

    /// <summary>Бой-образец с карты сценария импорта (<see cref="BattleScenario"/>), как <c>--scenario --stacks</c> у сцены боя.</summary>
    private static BattleState SampleBattle()
    {
        var (scenario, attacker, defender) = BattleScenario;
        var map = ScenarioFile.Read(ScenarioFile.PathOf(GameData.ScenariosDir, scenario)).Looks.Map;
        var state = MapBattle.Build(map, attacker, defender, GameData.Book, difficulty: 1, attackerMove: 0);
        state.ProcessRand ??= new LastEdict.Rules.Rng.CrtRand(1);
        return state;
    }

    private IEnumerable<SceneCase> BattleCases()
    {
        var rules = RulesLoader.Load(GameData.Rules);
        BattleScreen New() => new() { Prepared = SampleBattle(), Rules = rules, Name = "BenchBattle" };

        // Открытие экрана боя: разметка DLG_BATTLE_A, поле, анимации юнитов, панели.
        yield return new("battle_open", async () =>
        {
            var screen = New();
            long t0 = Stopwatch.GetTimestamp();
            AddChild(screen);
            double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
            _notes["battle_open"] = $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}";
            await Close(screen);
            return us;
        });
        // Первый бой партии: открытие с пустыми кэшами картинок (разбор файлов графики в замере).
        yield return new("battle_open_cold", () => Cold(async () =>
        {
            var screen = New();
            long t0 = Stopwatch.GetTimestamp();
            AddChild(screen);
            double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
            _notes["battle_open_cold"] = $"texture_mem={Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed)} gfx_bytes={GfxTextures.Bytes}";
            await Close(screen);
            return us;
        }));
        // Кадр экрана боя в ожидании хода человека (анимации стояния, курсор).
        yield return new("battle_frame_idle", () => FrameMean("battle_frame_idle", null),
            async () => { _battle = New(); AddChild(_battle); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); },
            async () => { if (_battle is not null) await Close(_battle); _battle = null; });
        // Показ шагов автобоя с окном (без окна показ мгновенный): среднее время кадра за 64 кадра показа.
        yield return new("battle_frame_playback", () => FrameMean("battle_frame_playback", null),
            async () =>
            {
                _battle = New();
                AddChild(_battle);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _battle.Session.Auto = true;
                _battle.Continue();
            },
            async () => { if (_battle is not null) await Close(_battle); _battle = null; });
        // Автобой на открытом экране (BattleScreen.AutoFinish: модель доигрывает бой и экран обновляется).
        yield return new("battle_autofinish_screen", async () =>
        {
            var screen = New();
            AddChild(screen);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            long t0 = Stopwatch.GetTimestamp();
            screen.AutoFinish();
            double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
            await Close(screen);
            return us;
        });
    }

    // --- память ------------------------------------------------------------------------------------------------

    /// <summary>Память после открытия карты партии с землёй под камерой и боя поверх неё (кэши картинок с нуля): рабочий набор
    /// процесса, управляемая куча после сборки мусора, байты текстур графики карты. Значения — байты, а не мкс (сравнение
    /// <c>bench_compare.gd</c> то же: «slower» — больше памяти).</summary>
    private IEnumerable<SceneCase> MemoryCases()
    {
        double working = 0, managed = 0, textures = 0;
        async Task Measure()
        {
            GfxTextures.Release();
            UiArt.ForgetImages();
            await OpenBattleOverMap();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var self = Process.GetCurrentProcess();
            self.Refresh();
            (working, managed, textures) = (self.WorkingSet64, GC.GetTotalMemory(forceFullCollection: true), GfxTextures.Bytes);
            _notes["mem"] = $"working_set={working / 1048576:F0} МБ peak={self.PeakWorkingSet64 / 1048576:F0} МБ managed={managed / 1048576:F0} МБ " +
                            $"gfx_textures={textures / 1048576:F0} МБ static={OS.GetStaticMemoryUsage() / 1048576} МБ";
            await ShutMapScreen();
        }
        yield return new("mem_working_set", async () => { await Measure(); return working; });
        yield return new("mem_managed", () => Task.FromResult(managed));
        yield return new("mem_gfx_textures", () => Task.FromResult(textures));
    }

    // --- окна UI -----------------------------------------------------------------------------------------------

    /// <summary>Открытие экрана разметки (<see cref="UiScreenView"/> под хозяином): разбор экрана, кадры, надписи, узлы.</summary>
    private async Task<double> OpenUi(string screen)
    {
        var view = new UiScreenView { Hosted = screen, Name = screen };
        long t0 = Stopwatch.GetTimestamp();
        AddChild(view);
        double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        _notes[$"ui_open_{screen}"] = $"nodes_in_screen={CountNodes(view)}";
        await Close(view);
        return us;
    }

    private GameSession? _buildSession;

    /// <summary>Окно построек на партии сохранения: открытие (разбор экрана, дерево веток, вид) или — с <paramref name="zoom"/> — десять шагов
    /// колеса к середине дерева с перерисовкой (зум и вид — в логике, узлы — в BuildTreeView).</summary>
    private async Task<double> OpenBuild(bool zoom)
    {
        var session = _buildSession ??= SaveSession();
        string viewer = ViewerOf(session)!;
        var context = new LastEdict.Data.Ui.Windows.WindowContext(session, viewer, GameData.Strings(GameData.Language),
            LastEdict.Data.Ui.Windows.UnitFaces.Load(GameData.Rules, _ => true));
        var logic = LastEdict.Data.Ui.Windows.BuildLogic.For(context, GameData.Rules, LastEdict.Data.Ui.Windows.BuildLogic.LoadTexts(GameData.Rules));
        var view = new UiScreenView { Hosted = LastEdict.Data.Ui.Windows.BuildLogic.Screen, Name = "build", Logic = logic };
        long t0 = Stopwatch.GetTimestamp();
        AddChild(view);
        double us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        if (zoom && logic.TreeArea is { } area)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 10; i++)
            {
                logic.Wheel(area.X + (area.Width / 2), area.Y + (area.Height / 2), i < 5 ? 1 : -1, view.State);
                view.Sync();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds / 10;
        }
        _notes[zoom ? "build_tree_zoom" : "ui_open_build_tree"] = $"nodes_in_screen={CountNodes(view)} drawn={view.TreeDrawn} {logic.Summary.Split(' ')[0]}";
        await Close(view);
        return us;
    }

    private static int CountNodes(Node node) => 1 + node.GetChildren().Sum(CountNodes);

    // --- харнесс -----------------------------------------------------------------------------------------------

    private async Task<global::Godot.Collections.Array> Run(SceneCase one)
    {
        if (one.Setup is not null) await one.Setup();
        try
        {
            for (int i = 0; i < Warmup; i++) await one.Sample();
            var samples = new List<long>();
            for (int i = 0; i < Runs; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                samples.Add((long)Math.Round(await one.Sample()));
            }
            samples.Sort();
            long median = samples[samples.Count >> 1];
            GD.Print($"{one.Name,-26} median {median,10} | min {samples[0],10} | max {samples[^1],10} | " +
                     $"IQR {samples[samples.Count / 4]}-{samples[3 * samples.Count / 4]}");
            var array = new global::Godot.Collections.Array();
            foreach (long s in samples) array.Add(s);
            return array;
        }
        finally
        {
            if (one.Teardown is not null) await one.Teardown();
        }
    }

    private async Task Loop(SceneCase one, string[] args)
    {
        double seconds = double.Parse(Arg(args, "--seconds=") ?? "30", System.Globalization.CultureInfo.InvariantCulture);
        GD.Print($"loop {one.Name} pid={OS.GetProcessId()}");
        if (one.Setup is not null) await one.Setup();
        var until = DateTime.UtcNow.AddSeconds(seconds);
        int n = 0;
        while (DateTime.UtcNow < until) { await one.Sample(); n++; }
        if (one.Teardown is not null) await one.Teardown();
        GD.Print($"loop done runs={n}");
    }

    private static void Save(string path, string header, global::Godot.Collections.Dictionary results)
    {
        string full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), path));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var report = new global::Godot.Collections.Dictionary { ["header"] = header, ["iterations"] = 1, ["results"] = results };
        File.WriteAllText(full, Json.Stringify(report, "\t"));
        GD.Print($"saved {full}");
    }

    private static string? Arg(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal)) is { } a ? a[prefix.Length..] : null;
}
