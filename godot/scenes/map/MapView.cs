using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LastEdict.Data.Gfx;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>Вид карты: картинки объектов (<see cref="MapObjects"/>) над землёй кусками (<see cref="GroundChunks"/>) под камерой, прокрутка. Куски собираются в одном
/// фоновом потоке и появляются по готовности; видимые запрашиваются каждый кадр, невидимые текстуры сверх
/// <see cref="TextureLimit"/> освобождаются. Мировые координаты — как у движка: клетка <c>(x, y)</c> начинается в
/// <c>((x−y)·32, (x+y)·16)</c>.
///
/// <para>Прокрутка: стрелки, край окна мышью (зона 4 точки), перетаскивание средней кнопкой. Шаг — ровно клетка,
/// как в движке (<c>docs/map-screen.md</c> §2).</para>
///
/// <para>Запуск: <c>--scenario "Имя"</c> или <c>--sg путь</c>, <c>--at x,y</c> — клетка в центре, <c>--player id</c>
/// (по умолчанию первый человек, <c>none</c> — без тумана). <c>--check</c> — без
/// окна: обходит несколько точек карты, ждёт, пока все видимые куски появятся, печатает числа и выходит с кодом
/// 0/1.</para></summary>
public partial class MapView : Node2D
{
    private const int ChunkSize = 256;
    private const int TextureLimit = 96;
    /// <summary>Прокрутка как в движке (<c>docs/map-screen.md</c> §2): шаг ровно в клетку — <c>(±32, ±16)</c>
    /// мировых точек, зона у края окна 4 точки, между шагами краем окна — задержка <see cref="ScrollDelayMs"/>.</summary>
    private const int EdgeZone = 4;
    private const int CheckTimeoutMs = 60_000;
    /// <summary>Задержка шага прокрутки краем окна в мс — «Скорость прокрутки экрана» (<see cref="MapSettings.ScrollSpeed"/>: 0 быстро … 100
    /// медленно; в поставляемом <c>Disciple.ini</c> — 0).</summary>
    public double ScrollDelayMs { get; set; }

    /// <summary>Проверка без окна: ход ИИ показывается быстрее, но целиком, и ждётся не дольше этого.</summary>
    internal const float CheckAiSpeed = 40f;
    internal const double CheckAiSeconds = 120;

    private readonly Dictionary<(int X, int Y), Sprite2D> _shown = new();
    /// <summary>Куски, чья картинка устарела (туман, клетки), но новая ещё собирается: старый спрайт стоит, пока не ляжет новый, — иначе на
    /// месте куска на несколько кадров дыра (мигание земли при каждом шаге и каждой пачке растекания в ход ИИ).</summary>
    private readonly Dictionary<(int X, int Y), Sprite2D> _stale = new();
    private readonly HashSet<(int X, int Y)> _requested = new();
    private readonly BlockingCollection<((int X, int Y) Key, int Age)> _queue = new();
    private readonly ConcurrentQueue<((int X, int Y) Key, RgbaImage Image, int Age)> _done = new();
    private readonly ConcurrentQueue<Exception> _failed = new();
    /// <summary>Сборщик земли; volatile — его читает фоновый поток, а рост земли подменяет целиком.</summary>
    private volatile GroundChunks _chunks = null!;
    private Camera2D _camera = null!;
    private Node2D _groundLayer = null!;
    /// <summary>Потоки сборки кусков земли: куски независимы, сборщик и его картинки потокобезопасны.</summary>
    private readonly List<Thread> _workers = new();
    private static int ComposeThreads => Math.Clamp(System.Environment.ProcessorCount - 2, 1, 6);
    private Rect2 _bounds;
    private bool _dragging;
    private double _sinceScroll;
    private int _groundAge;
    private MapObjects _objects = null!;

    /// <summary>Знамёна отрядов видны (<see cref="MapObjects.BannersShown"/>); карта ещё не собрана — false.</summary>
    public bool BannersShown
    {
        get => _objects is not null && _objects.BannersShown;
        set { if (_objects is not null) _objects.BannersShown = value; }
    }

    /// <summary>Узлы знамён: всего и видно (строка проверки).</summary>
    public (int All, int Visible) BannerCount() => _objects is null ? (0, 0) : _objects.BannerCount();
    private double _objectsMs;

    /// <summary>Партия, которую показывает вид: карта и команды хода.</summary>
    public GameSession Session { get; private set; } = null!;

    /// <summary>Карта партии; во время хода ИИ — снимок последней заявки (<see cref="StepAi"/>).</summary>
    public MapState Map => Looks.Map;

    public MapScene Scene { get; private set; } = null!;

    /// <summary>Объекты карты, из которых собрана сцена.</summary>
    public MapLooks Looks => (_aiShown ?? Session.State).Looks;

    public string MapName { get; private set; } = "";

    /// <summary>Игрок, чьими глазами показана карта (его туман); null — вид редактора.</summary>
    public string? Viewer { get; private set; }

    /// <summary>Сцена запущена сама и берёт карту из аргументов; внутри экрана карты (<see cref="MapScreen"/>) карту
    /// передаёт хозяин через <see cref="Show"/>.</summary>
    public bool Standalone { get; set; } = true;

    /// <summary>Графика карты — точками, без сглаживания: вид живёт во вьюпорте экрана карты, а у <see cref="SubViewport"/> свой фильтр по
    /// умолчанию — Linear (настройка проекта <c>default_texture_filter</c> действует только на корневой вьюпорт). Дети с
    /// <c>ParentNode</c> (отметки целей, области заклинаний) берут его отсюда.</summary>
    public override void _EnterTree() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Ready()
    {
        if (!Standalone) return;
        string[] args = OS.GetCmdlineUserArgs();
        try
        {
            var (looks, path) = MapSource.LoadLooks(args);
            // Глазами игрока: --player id, --player none — вид редактора; по умолчанию первый человек.
            string? viewer = MapSource.Arg(args, "--player") switch
            {
                "none" => null,
                { } id => id,
                null => looks.Map.Players.Values.FirstOrDefault(p => p.IsHuman)?.Id,
            };
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            Show(new GameSession(new GameState(GameInfo.FromMap(name, looks), looks), GameData.Content.MapArt, GameData.Book), name, viewer);
            var at = MapSource.Arg(args, "--at") is { } text ? ParseCell(text) : (looks.Map.Size / 2, looks.Map.Size / 2);
            CenterOn(at);
            if (args.Contains("--check")) Failures.Run(this, () => RunCheck(MapSource.Arg(args, "--shot"), at), "MapView.RunCheck");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException)
        {
            GD.PrintErr($"MapView: {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }

    /// <summary>Журнал команд партии (<c>D2_COMMAND_JOURNAL</c> — путь файла): начало — сохранение рядом (<c>…start.d2save</c>), команды —
    /// строками JSON при выходе из сцены. Сохранение и журнал — баг-репорт: <c>tools/JournalReplay</c> повторяет партию и сверяет хеши.</summary>
    private string? _journalPath;

    private void StartJournal(GameSession session, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        GameSave.Write(path + ".start.d2save", session.Snapshot());
        session.Journal = LastEdict.Data.Play.Commands.CommandJournal.Start(session, GameData.ModIds);
        _journalPath = path;
    }

    private void WriteJournal()
    {
        if (_journalPath is not { } path || Session is not { Journal: { } journal }) return;
        File.WriteAllText(path, journal.ToJsonLines());
        GD.Print($"command-journal команд={journal.Entries.Count} мимо_команд={journal.Entries.Count(e => e.Outside)} -> {path}");
    }

    /// <summary>Партия под аудитом (отписка при смене партии).</summary>
    private GameSession? _audited;

    /// <summary>Места, где партия уже менялась мимо команд (печатаются раз).</summary>
    private readonly HashSet<string> _auditedSites = [];

    /// <summary>Аудит перевода интерфейса на команды (<c>docs/game-commands.md</c> §3, ui-render-check с <c>D2_COMMAND_AUDIT=1</c>): партия
    /// меняется вне <see cref="GameSession.Execute"/> — строка <c>command-audit</c> с первым вызывающим вне сессии.</summary>
    private void AuditOutsideCommands()
    {
        var frames = new System.Diagnostics.StackTrace(1, false).GetFrames();
        var outside = frames.Select(f => f.GetMethod()).FirstOrDefault(m => m?.DeclaringType is { } t
            && !t.FullName!.StartsWith("LastEdict.Data.Play.GameSession", StringComparison.Ordinal) && t != typeof(MapView) && t.DeclaringType != typeof(MapView));
        var session = frames.Select(f => f.GetMethod()).LastOrDefault(m => m?.DeclaringType?.FullName?.StartsWith("LastEdict.Data.Play.GameSession", StringComparison.Ordinal) == true);
        string site = $"{outside?.DeclaringType?.Name}.{outside?.Name} → GameSession.{session?.Name}";
        if (_auditedSites.Add(site)) GD.Print($"command-audit {site}");
    }

    /// <summary>Показать карту: земля собирается заново, над ней — картинки объектов (<see cref="MapScene"/>), камера —
    /// в центр карты.</summary>
    public void Show(GameSession session, string name, string? viewer = null)
    {
        Viewer = viewer;
        Session = session;
        _routes.Clear();   // пути отрядов живут в интерфейсе, не в партии: новая или загруженная партия их не помнит (MapView.Routes.cs)
        _mainThread = System.Threading.Thread.CurrentThread;
        session.Observer = new AiHooks(this);   // ход ИИ ждёт показа на своих заявках; литьё человека — анимация без ожидания
        if (System.Environment.GetEnvironmentVariable("D2_COMMAND_AUDIT") == "1")
        {
            if (_audited is { } old) old.ChangedOutsideCommands -= AuditOutsideCommands;
            (_audited = session).ChangedOutsideCommands += AuditOutsideCommands;
        }
        if (System.Environment.GetEnvironmentVariable("D2_COMMAND_JOURNAL") is { Length: > 0 } journal) StartJournal(session, journal);
        var looks = session.Looks;
        var map = looks.Map;
        MapName = name;
        var t0 = DateTime.UtcNow;
        _shownFog = null;
        ResetAnimations();
        Scene = MapScene.Build(looks, GameData.Content.MapArt, GameData.Content.Gfx, AnimatedIso, viewer: viewer, objectives: Objectives(),
                               fog: SceneFog());
        var (left, top, width, height) = GroundChunks.MapBounds(map.Size);
        _bounds = new Rect2(left, top, width, height);

        BuildWater();   // под землёй: точки воды в кусках прозрачны
        _chunks = Chunks(map);   // после воды: куски знают, прозрачна ли вода
        _groundLayer = new Node2D { Name = "Ground" };
        AddChild(_groundLayer);
        _objects = new MapObjects { Name = "Objects" };
        AddChild(_objects);
        _objects.Build(Scene, GameData.Content.Gfx, GameData.GfxRoot);
        BuildClouds();   // облака — MapView.Clouds.cs
        _objectsMs = (DateTime.UtcNow - t0).TotalMilliseconds;
        _camera = new Camera2D { Name = "Camera" };
        AddChild(_camera);
        _camera.MakeCurrent();
        CenterOn((map.Size / 2, map.Size / 2));

        for (int i = 0; i < ComposeThreads; i++)
        {
            var worker = new Thread(Compose) { IsBackground = true, Name = $"ground-chunks-{i}" };
            _workers.Add(worker);
            worker.Start();
        }
    }

    /// <summary>Пересобрать картинки по изменившимся объектам партии (отряд сходил с места). Земля и туман не
    /// трогаются.</summary>
    public void Reload(string? hiddenStack = null)
    {
        _hiddenNow = hiddenStack;
        Scene = MapScene.Build(Looks, GameData.Content.MapArt, GameData.Content.Gfx, AnimatedIso, viewer: Viewer,
                               hiddenStack: hiddenStack, objectives: Objectives(), fog: SceneFog());
        _objects.Build(Scene, GameData.Content.Gfx, GameData.GfxRoot);
        DropShownTombs();   // могила гибели уже в модели — MapView.Spells.cs
    }

    /// <summary>Отряд, скрытый последней перерисовкой (идёт по клеткам): перерисовка кольцом тумана его не показывает.</summary>
    private string? _hiddenNow;

    /// <summary>Цели сценария глазами зрителя — круг <c>SYM_OBJECTIVE</c> (<see cref="GameSession.Objectives"/>: набор строится заново на
    /// каждую перерисовку, как у движка). Без зрителя — нет (у движка набор пуст).</summary>
    private MapObjectives? Objectives() =>
        Viewer is { } viewer && Session is { } session ? new MapObjectives(session.Objectives(viewer), session.State.Events.Zones) : null;

    /// <summary>Центр вида — на середину клетки.</summary>
    public void CenterOn((int X, int Y) cell)
    {
        var (x, y) = GroundGeometry.CellOrigin(cell.X, cell.Y);
        _camera.Position = Clamp(new Vector2(x + 32, y + 16));
    }

    public override void _Process(double delta)
    {
        if (_chunks is null) return;
        ForgetLeftDown();
        StepSmoothScroll(delta);   // прокрутка к событию — до показа, который её ждёт (MapView.Scroll.cs)
        StepFog(delta);   // смена тумана кольцами (MapView.Fog.cs)
        StepWalk(delta);
        StepAi(delta);
        StepSpell(delta);
        StepWater(delta);
        StepClouds(delta);
        StepRegions(delta);   // фон мест — MapView.Sounds.cs
        Scroll((float)delta);
        while (_done.TryDequeue(out var item))
            if (item.Age == _groundAge) Attach(item.Key, item.Image);
            else _requested.Remove(item.Key);   // кусок собран до смены тумана — выбрасываем
        if (_failed.TryDequeue(out var error))
        {
            // В партии — ошибка в журнал зонда и дыра в земле вместо вылета игры; под --check — выход с кодом 1, как раньше.
            Failures.Report(this, "MapView: сборка куска земли", error);
            return;
        }
        var visible = VisibleChunks().ToHashSet();
        foreach (var key in visible)
            if (!_shown.ContainsKey(key) && _requested.Add(key)) _queue.Add((key, _groundAge));
        if (_shown.Count > TextureLimit)
            foreach (var key in _shown.Keys.Where(k => !visible.Contains(k)).ToList())
            {
                FreeChunk(_shown[key]);
                _shown.Remove(key);
            }
        foreach (var key in _stale.Keys.Where(k => !visible.Contains(k)).ToList())
        {
            FreeChunk(_stale[key]);
            _stale.Remove(key);
        }
        CountGroundGaps(visible);
    }

    /// <summary>Кадров, где кусок земли под камерой, показанный в прошлом кадре, пропал (проверка мигания); кадров, где кусок держала только
    /// устаревшая картинка (без неё там была бы дыра — так было до 11.10), и сколько кадров смотрели.</summary>
    public (int Gaps, int Held, int Frames) GroundGaps { get; private set; }

    private HashSet<(int X, int Y)> _drawnBefore = [];

    private void CountGroundGaps(HashSet<(int X, int Y)> visible)
    {
        var drawn = visible.Where(k => _shown.ContainsKey(k) || _stale.ContainsKey(k)).ToHashSet();
        bool gap = _drawnBefore.Any(k => visible.Contains(k) && !drawn.Contains(k));
        bool held = visible.Any(k => !_shown.ContainsKey(k) && _stale.ContainsKey(k));
        GroundGaps = (GroundGaps.Gaps + (gap ? 1 : 0), GroundGaps.Held + (held ? 1 : 0), GroundGaps.Frames + 1);
        _drawnBefore = drawn;
    }

    /// <summary>Снять кусок земли и сразу отпустить его текстуру: обёртка C# держала бы видеопамять до сборки мусора. Узел ещё живёт до
    /// конца кадра, но держит текстуру сам (ссылка движка), а другой ссылки на неё у порта нет — текстура куска своя (<see cref="Attach"/>).</summary>
    private static void FreeChunk(Sprite2D sprite)
    {
        var texture = sprite.Texture;
        sprite.QueueFree();
        texture?.Dispose();
    }

    /// <summary>Щелчки, правая кнопка и перетаскивание вида средней — карте (выставляет <c>MapScreen.ShowMap</c>): в режимах прицела
    /// (заклинание, бросок мешка, обмен, посох) щелчки берёт прицел, а колесо, как и стрелки, двигает вид (<see cref="ScrollEnabled"/>).</summary>
    public bool PointerEnabled { get; set; } = true;

    /// <summary>Левая кнопка нажата над картой (нажатие дошло до карты): щелчок — только по отпусканию после своего нажатия. Окно, закрытое
    /// нажатием «ОК» поверх карты, снимается раньше отпускания — отпускание того же щелчка уходило карте, и у выбранного отряда появлялся путь
    /// к клетке под кнопкой окна (партия 17.10, пометка 34: вор K1 после убийства, «Закрыть» окна итога — флажки до клетки под кнопкой).</summary>
    private bool _leftDown;

    /// <summary>Отпускание, съеденное панелью или окном, не оставляет нажатие «висеть»: следующее отпускание без своего нажатия — не щелчок.</summary>
    private void ForgetLeftDown()
    {
        if (_leftDown && !Input.IsMouseButtonPressed(MouseButton.Left)) _leftDown = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Колесо — стрелки ↑/↓ (WndProc 0x5626a3: WM_MOUSEWHEEL → нажатие VK_UP/VK_DOWN тем же путём, что WM_KEYDOWN, 0x562993); вид карты
        // на нажатие стрелки делает шаг (0x5418fb: VK_UP — направление 0, VK_DOWN — 4) — по щелчку колеса одна клетка. Стрелки двигают вид
        // и в режимах прицела — колесо тоже (прежде в прицеле карта ввод не брала вовсе, и колесо стояло).
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel)
        {
            if (ScrollEnabled) Step(0, wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            return;
        }
        if (!PointerEnabled)
        {
            (_dragging, _leftDown) = (false, false);
            return;
        }
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } b:
                _dragging = b.Pressed;
                break;
            case InputEventMouseMotion m when _dragging:
                _camera.Position = Clamp(_camera.Position - m.Relative / _camera.Zoom);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _leftDown = true;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (!_leftDown) break;
                _leftDown = false;
                var world = GetGlobalMousePosition();
                var (cx, cy) = GroundGeometry.CellAt(world.X, world.Y);
                Click(cx, cy);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                var at = GetGlobalMousePosition();
                RightButton?.Invoke(right.Pressed ? GroundGeometry.CellAt(at.X, at.Y) : null);
                break;
        }
    }

    /// <summary>Правая кнопка над картой: нажата над клеткой — клетка, отпущена — null (карточка объекта, MapScreen.Cards.cs).</summary>
    public event System.Action<(int X, int Y)?>? RightButton;

    public override void _ExitTree()
    {
        StopRegions();
        CancelAi();
        _queue.CompleteAdding();
        foreach (var worker in _workers) worker.Join(TimeSpan.FromSeconds(10));
        WriteJournal();   // ход ИИ снят (CancelAi ждёт рабочий поток) — журнал больше не растёт
    }

    /// <summary>Сборщик земли по клеткам карты; запоминает, по каким клеткам собран.</summary>
    private GroundChunks Chunks(MapState map) => new(Composer(map), ChunkSize, capacity: TextureLimit);

    private GroundComposer Composer(MapState map)
    {
        _groundTiles = map.Tiles;
        _groundArt ??= new GroundArt(GameData.Content.Gfx, GameData.GfxRoot);   // картинки земли читаются один раз на вид
        var cells = MapSource.GroundCells(map);
        HoldLand(cells, map);   // растекание земли по пачкам (MapView.Land.cs)
        return new GroundComposer(cells, map.MapSeed, _groundArt)
        {
            // Туман — текущей сцены (Reload меняет её, смена зрителя тоже), а не той, что была при создании сборщика; без зрителя
            // тумана нет (MapScene.Hidden — ложь).
            Hidden = (x, y) => Scene.Hidden(x, y),
            AnimatedWater = WaterAnimated && AnimatedIso,
        };
    }

    private Tile[,]? _groundTiles;
    private GroundArt? _groundArt;

    /// <summary>Собрать землю заново: туман игрока изменился, и чёрные ромбы под ним уже не те; выросла земля
    /// расы — клетки перекрашены, и сборщик строится по новым клеткам. <paramref name="land"/> — пачка растекания сменила подмену
    /// клеток (<see cref="PlayLandSpread"/>): сборщик строится заново и при тех же клетках модели.</summary>
    public void RefreshGround(bool land = false)
    {
        SceneFog();   // показанный туман — к модели, если смена тумана не держится (MapView.Fog.cs): куски сравнивают его
        _groundAge++;
        // Пересобираются только куски, у которых изменились входы (тип клетки, чёрный ромб), — после шага отряда это куски у
        // края открытой области, а не вся земля под камерой (27.09).
        var kept = _chunks.Refresh(!land && ReferenceEquals(_groundTiles, Map.Tiles) ? null : Composer(Map));
        foreach (var key in _shown.Keys.Where(k => !kept.Contains(k)).ToList())
        {
            // Старая картинка остаётся до новой (Attach): кусок не пропадает с экрана на время сборки.
            if (_stale.Remove(key, out var older)) FreeChunk(older);
            _stale[key] = _shown[key];
            _shown.Remove(key);
        }
        _requested.Clear();
    }

    /// <summary>Клетка в центре вида.</summary>
    public (int X, int Y) CentreCell
    {
        get
        {
            var p = _camera?.Position ?? Vector2.Zero;
            return GroundGeometry.CellAt(p.X, p.Y);
        }
    }

    /// <summary>Все куски земли под камерой уже показаны.</summary>
    public bool GroundReady => _chunks is not null && VisibleChunks().All(_shown.ContainsKey);

    /// <summary>Картинок объектов на карте.</summary>
    public int SpriteCount => _objects?.Shown ?? 0;

    /// <summary>Куски, которые задевает вид камеры.</summary>
    public IEnumerable<(int X, int Y)> VisibleChunks()
    {
        var size = GetViewportRect().Size / _camera.Zoom;
        var topLeft = _camera.GetScreenCenterPosition() - (size / 2);
        return _chunks.Covering((int)Math.Floor(topLeft.X), (int)Math.Floor(topLeft.Y),
                                (int)Math.Ceiling(size.X) + 1, (int)Math.Ceiling(size.Y) + 1);
    }

    private void Compose()
    {
        try
        {
            foreach (var (key, age) in _queue.GetConsumingEnumerable())
                _done.Enqueue((key, _chunks.Get(key), age));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            _failed.Enqueue(e);
        }
    }

    private void Attach((int X, int Y) key, RgbaImage image)
    {
        _requested.Remove(key);
        if (_shown.ContainsKey(key)) return;
        // Картинка куска — сразу Dispose: данные уже в текстуре, а сотни брошенных обёрток Image ждали бы финализатора (на выходе —
        // гонка финализатора с остановкой языка C#, GameData._ExitTree).
        using var chunk = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels);
        var texture = ImageTexture.CreateFromImage(chunk);
        var (left, top) = _chunks.Origin(key);
        var sprite = new Sprite2D { Texture = texture, Centered = false, Position = new Vector2(left, top),
                                    TextureFilter = TextureFilterEnum.Nearest };
        _groundLayer.AddChild(sprite);
        _shown[key] = sprite;
        if (_stale.Remove(key, out var old)) FreeChunk(old);   // новая картинка легла поверх — старую снять
    }

    /// <summary>Шаг прокрутки: 8 направлений по клеткам. Направление берётся от клавиш и от края окна под мышью;
    /// краем окна шаг делается не чаще, чем раз в <see cref="ScrollDelayMs"/>, стрелками — без задержки (у движка темп клавиш — автоповтор,
    /// <c>0x5418fb</c>).</summary>
    private void Scroll(float delta)
    {
        if (!ScrollEnabled) return;
        _sinceScroll += delta * 1000;
        int dx = 0, dy = 0;
        if (Input.IsActionPressed("ui_left")) dx -= 1;
        if (Input.IsActionPressed("ui_right")) dx += 1;
        if (Input.IsActionPressed("ui_up")) dy -= 1;
        if (Input.IsActionPressed("ui_down")) dy += 1;
        bool keyed = (dx, dy) != (0, 0);
        if (DisplayServer.GetName() != "headless" && GetWindow().HasFocus() && MouseOverWindow())
        {
            var mouse = GetViewport().GetMousePosition();
            var size = GetViewportRect().Size;
            if (mouse.X >= 0 && mouse.Y >= 0 && mouse.X < size.X && mouse.Y < size.Y)
            {
                if (mouse.X < EdgeZone) dx -= 1;
                if (mouse.X >= size.X - EdgeZone) dx += 1;
                if (mouse.Y < EdgeZone) dy -= 1;
                if (mouse.Y >= size.Y - EdgeZone) dy += 1;
            }
        }
        if ((dx, dy) == (0, 0) || (!keyed && _sinceScroll < ScrollDelayMs)) return;
        _sinceScroll = 0;
        Step(dx, dy);
    }

    /// <summary>Мышь над окном игры (точки экрана). Точка мыши вьюпорта карты — последнее событие, которое в него пришло: мышь ушла за
    /// окно у края — край крутил бы вид без конца, а до первого события она (0, 0) — окно в фокусе ехало вверх-влево (снимки
    /// <c>tools/publish/screenshots.py</c>, окно в стороне от мыши).</summary>
    private bool MouseOverWindow()
    {
        var window = GetWindow();
        return new Rect2I(window.Position, window.Size).HasPoint(DisplayServer.MouseGetPosition());
    }

    /// <summary>Прокрутка стрелками и краем окна. Как у движка: под окном поверх карты, в бою и в ход ИИ карта стоит
    /// (выставляет <c>MapScreen.ShowMap</c>); в режимах прицела (заклинание, бросок, обмен, посох) — едет, к дальней цели.</summary>
    public bool ScrollEnabled { get; set; } = true;

    /// <summary>Центр вида (камеры) в точках карты — для проверок.</summary>
    public Vector2 ViewCenter => _camera.Position;

    /// <summary>Шаг вида на клетку в указанную сторону экрана. Вид не уходит за карту: шаг, после которого
    /// центральная клетка оказалась бы вне карты, отменяется.</summary>
    public void Step(int dx, int dy)
    {
        var next = _camera.Position + new Vector2(dx * 32, dy * 16);
        var cell = GroundGeometry.CellAt(next.X, next.Y);
        if (!Map.Contains(cell.X, cell.Y)) return;
        _camera.Position = next;
    }

    /// <summary>Центр камеры не уходит за прямоугольник карты.</summary>
    private Vector2 Clamp(Vector2 p) =>
        new(Math.Clamp(p.X, _bounds.Position.X, _bounds.End.X), Math.Clamp(p.Y, _bounds.Position.Y, _bounds.End.Y));

    private static (int X, int Y) ParseCell(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 2) throw new FormatException($"--at ждёт x,y, получено «{text}»");
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Проверка без окна: центр и четыре угла карты; на каждой точке ждём все видимые куски.</summary>
    /// <param name="shot">Снимок вида в клетке <paramref name="at"/> (только с окном); тогда обходится одна эта точка.</param>
    private async System.Threading.Tasks.Task RunCheck(string? shot, (int X, int Y) at)
    {
        int n = Map.Size;
        (int, int)[] points = shot is not null ? [at] : [(n / 2, n / 2), (0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)];
        var lines = new List<string>();
        bool ok = true;
        var start = DateTime.UtcNow;
        foreach (var cell in points)
        {
            CenterOn(cell);
            var t0 = DateTime.UtcNow;
            while (true)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var visible = VisibleChunks().ToList();
                if (visible.All(_shown.ContainsKey))
                {
                    lines.Add($"({cell.Item1},{cell.Item2}) chunks={visible.Count} ms={(DateTime.UtcNow - t0).TotalMilliseconds:F0}");
                    break;
                }
                if ((DateTime.UtcNow - start).TotalMilliseconds > CheckTimeoutMs)
                {
                    lines.Add($"({cell.Item1},{cell.Item2}) ТАЙМАУТ: показано {visible.Count(_shown.ContainsKey)} из {visible.Count}");
                    ok = false;
                    break;
                }
            }
        }
        foreach (string step in (MapSource.Arg(OS.GetCmdlineUserArgs(), "--clicks") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (step is "end" or "конец")
            {
                EndTurn();
                AiSpeed = CheckAiSpeed;
                var began = DateTime.UtcNow;
                do await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                while (AiRunning && (DateTime.UtcNow - began).TotalSeconds < CheckAiSeconds);
                if (AiRunning) { lines.Add("ход ИИ: ТАЙМАУТ"); ok = false; }
                continue;
            }
            var cell = ParseCell(step);
            Click(cell.X, cell.Y);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var waited = DateTime.UtcNow;
            while (Walking && (DateTime.UtcNow - waited).TotalSeconds < 30)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            lines.Add($"click {cell.X},{cell.Y} выбран={Selected ?? "-"} путь={Preview?.Points.Count ?? 0} " +
                      $"дойдёт={Preview?.Reachable ?? 0} цена={Preview?.Spend ?? 0}");
        }
        var settle = DateTime.UtcNow;
        while (!GroundReady && (DateTime.UtcNow - settle).TotalSeconds < 60)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (shot is not null)
        {
            // Снимок есть только с окном (docs/godot-notes.md): несколько кадров, чтобы отрисовка догнала.
            for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(shot);
            lines.Add($"shot={shot}");
        }
        int hidden = 0;
        if (Viewer is { } who && Map.Players.TryGetValue(who, out var me) && Map.Fog.TryGetValue(me.FogId, out var mask))
            foreach (bool cell in mask) if (cell) hidden++;
        var view = GetViewportRect().Size;
        GameLog.Verbose($"map scenario={MapName.Replace(' ', '_')} size={n} seed={Map.MapSeed} viewer={Viewer ?? "none"} fog={Scene.Sprites.Count(x => x.Layer >= 1024)} скрыто={hidden} view={view.X}x{view.Y} " +
                 $"chunk={ChunkSize} composed={_chunks.Composed} shown={_shown.Count} sprites={_objects.Shown} " +
                 $"missing_names={Scene.Missing.Count} missing_files={_objects.MissingFiles} objects_ms={_objectsMs:F0} | " +
                 string.Join(" | ", lines));
        GetTree().Quit(ok && Scene.Missing.Count == 0 && _objects.MissingFiles == 0 ? 0 : 1);
    }
}
