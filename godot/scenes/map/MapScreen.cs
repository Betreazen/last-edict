using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using LastEdict.Data.Map;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>Экран карты, собранный как в игре: <c>DLG_ISO_VIEW</c> (800×600) задаёт три места — <c>IMG_MAIN</c>
/// 640×600 (карта и поверх неё <c>DLG_ISO</c>), <c>IMG_RESOURCE</c> (<c>DLG_ISO_PAL</c>, мини-карта и кнопки) и
/// <c>IMG_PALETTE</c> (<c>DLG_STRATEGIC</c>, команды). Игра открывает их одним махом (журнал зонда: <c>DLG_ISO_VIEW</c>,
/// <c>DLG_ISO</c>, <c>DLG_STRATEGIC</c>, <c>DLG_ISO_PAL</c>). Карта рисуется в своём вьюпорте размером с
/// <c>IMG_MAIN</c> и масштабируется вместе с экраном.
///
/// <para>Аргументы — как у <see cref="MapView"/> (<c>--scenario</c>/<c>--sg</c>, <c>--at</c>, <c>--player</c>);
/// <c>--check</c> печатает числа и выходит, с окном и <c>--shot файл.png</c> — снимок.</para></summary>
public partial class MapScreen : Node
{
    private const string View = "DLG_ISO_VIEW";
    private static readonly (string Screen, string Slot)[] Panels =
        [("DLG_ISO", "IMG_MAIN"), ("DLG_ISO_PAL", "IMG_RESOURCE"), ("DLG_STRATEGIC", "IMG_PALETTE")];

    private readonly List<UiScreenView> _screens = new();
    private SubViewportContainer _mapBox = null!;
    private MapView _map = null!;
    private Vector2 _host;
    private Rect2 _mapRect;
    private MiniMapView? _mini;
    private Rect2 _miniRect;
    private StrategicPanelLogic? _panel;
    private Sprite2D? _face;
    private string _faceName = "";
    private string _faceKey = "";
    private PanelFace? _faceLook;
    private Rect2 _faceSlot;
    /// <summary>Сдвиг портрета внутри места <c>IMG_LEADER</c> (по центру, без масштаба).</summary>
    private Vector2 _faceOffset;
    private ResourcePanelLogic? _resources;
    private UiScreenView? _panelView;
    private UiScreenView? _resourceView;
    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        try
        {
            var screens = GameData.Content.Screens;
            var view = screens.First(s => s.Id == View);
            LayoutView(view);   // холст формы окна: место карты во всё окно, панели к правому краю (MapScreen.Layout.cs)
            var main = HostSlot("IMG_MAIN");
            _mapRect = new Rect2(main.X, main.Y, main.Width, main.Height);

            LoadSettings(args);   // до панелей и знамён карты: переключатель TOG_BANNERS — из настроек (MapScreen.AiTurn.cs)

            // Карта — снизу, в своём вьюпорте размером с место IMG_MAIN.
            var viewport = new SubViewport
            {
                Size = new Vector2I(main.Width, main.Height), HandleInputLocally = true,
                // Иначе вьюпорт карты перерисовывается не каждый кадр и после смены размера окна остаётся пустым.
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            _mapBox = new SubViewportContainer { Name = "Map", Stretch = true };
            _mapBox.AddChild(viewport);
            AddChild(_mapBox);
            _map = new MapView { Standalone = false, Name = "MapView" };
            viewport.AddChild(_map);

            var session = Session ?? LoadSession(args);
            var looks = session.Looks;
            string? viewer = Session is not null ? Viewer : MapSource.Arg(args, "--player") switch
            {
                "none" => null,
                { } id => id,
                null => looks.Map.Players.Values.FirstOrDefault(p => p.IsHuman)?.Id,
            };
            session.EventBattlesOnScreen = true;   // бой эффекта L_BATTLE в ход человека — экраном (MapScreen.Overlays.cs); до показа — журнал команд её запомнит
            _map.Show(session, session.State.Info.ScenarioName, viewer);
            if (Session is null && MapSource.Arg(args, "--at") is { } at) _map.CenterOn(ParseCell(at));
            else if (StartCell(session, viewer) is { } start) _map.CenterOn(start);

            _strings = GameData.Strings(GameData.Language);
            _panel = new StrategicPanelLogic(PanelState, Text);
            _resources = new ResourcePanelLogic(PanelState);
            AddScreen(View, null, new IsoViewLogic());   // рамка края поля ISO_OUTLINE над картой (сессия Г)
            BuildMiniMap(screens, looks, viewer);
            foreach (var (screen, slot) in Panels)
            {
                var r = HostSlot(slot);
                var added = AddScreen(screen, new Vector2(r.X, r.Y), screen switch
                {
                    "DLG_STRATEGIC" => _panel,
                    MapPalette.Pal => PaletteLogic,   // TOG_RESPOPUP, TOG_BANNERS, BTN_RES_PAL (MapScreen.Palette.cs)
                    IsoChatLogic.Screen => _chat = new IsoChatLogic(Text),   // строки чата: сообщения о записи (MapScreen.QuickSave.cs)
                    _ => null,
                });
                if (screen == "DLG_STRATEGIC") _panelView = added;
                if (screen == IsoChatLogic.Screen) _chatView = added;
            }
            // Полоса ресурсов лежит поверх карты (её место — весь экран), и открывается она отдельно (TOG_RESPOPUP).
            _resourceView = AddScreen("DLG_ISO_RESPOPUP", Vector2.Zero, _resources);
            ApplyResources();   // в начале партии полоса видна (ответ пользователя 11.10); TOG_RESPOPUP её прячет и показывает
            var strategic = screens.First(s => s.Id == "DLG_STRATEGIC");
            var leader = Slot(strategic, "IMG_LEADER");
            var palette = HostSlot("IMG_PALETTE");
            _faceSlot = new Rect2(palette.X + leader.X, palette.Y + leader.Y, leader.Width, leader.Height);
            if (_panelView is not null)
            {
                _panelView.Fired += Commands;
                _panelView.Released += PanelReleased;
            }
            if (PaletteView is { } pal) pal.Fired += events => OnWindowEvents(pal, events);
            _map.Changed += SyncPanels;
            _map.AttackRequested += OpenBattle;
            _map.Entered += OnEntered;
            _map.FortOpened += OnEntered;
            _map.CityArmyOpened += OpenCityArmy;
            WireAiTurn();
            WireCards();
            _map.SiteVisited += OnSiteVisited;
            // Снос жезла жезлоносцем — вопрос X005TA0272 (0x4d144d), «Да» — ход к жезлу и снос (0x4d1ec7), «Нет» — путь стёрт (0x4d1f06, Ин82).
            _map.RodBreakAsked += (stack, rod) => OpenWindow(LastEdict.Data.Ui.Windows.MessageBoxLogic.Screen,
                new LastEdict.Data.Ui.Windows.MessageBoxLogic(Text(GameSession.RodBreakQuestion), () => _map.ConfirmRodBreak(stack, rod), _map.DropRoute));
            // Щелчок по союзнику — «Перед тем, как атаковать союзника, расторгните альянс с ним.» (0x4d0ca4 → 0x4d0da1), кнопка одна; «ОК» —
            // путь стёрт (0x4d201e, Ин82).
            _map.AllyRefused += () => OpenWindow(LastEdict.Data.Ui.Windows.MessageBoxLogic.Screen,
                new LastEdict.Data.Ui.Windows.MessageBoxLogic(Text(GameSession.AllyRefusalText), null, _map.DropRoute));
            SyncPanels();
            Place();
            GetViewport().SizeChanged += Place;
            _placed = true;
            Callable.From(ShowOffers).CallDeferred();   // предложения, ждущие с прошлого хода (загруженная партия)
            if (args.Contains("--check"))
            {
                UiScreenView.ManualTime = true;   // автоповтор и подсказки — только шагом «wait N»
                Failures.Run(this, () => RunCheck(MapSource.Arg(args, "--shot")), "MapScreen.RunCheck");
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or InvalidOperationException)
        {
            GD.PrintErr($"MapScreen: {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }

    /// <summary>Партия от хозяина (игровой цикл); null — сцена запущена сама и берёт карту из аргументов.</summary>
    public GameSession? Session { get; set; }

    /// <summary>Чьими глазами показывать партию хозяина.</summary>
    public string? Viewer { get; set; }

    private static GameSession LoadSession(string[] args)
    {
        // --state (проверка): партия из сохранения порта .d2save — пометки F9 и сохранения дней журнала зонда (docs/port-probe.md).
        if (MapSource.Arg(args, "--state") is { } saved) return new GameSession(GameSave.Read(saved), GameData.Content.MapArt, GameData.Book);
        var (looks, path) = MapSource.LoadLooks(args);
        string name = Path.GetFileNameWithoutExtension(path);
        var state = new GameState(GameInfo.FromMap(name, looks), looks);
        // Сценарий порта несёт события; --winner (проверка) — партия уже кончилась победой этого игрока.
        if (path.EndsWith(ScenarioFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var events = GameData.ReadScenario(path).Events;
            state = state with { Events = events, EventProgress = LastEdict.Rules.Map.EventProgress.From(events), Info = state.Info with { ScenarioId = name } };
        }
        // --events (проверка): события сценария из сохранения .sg (импорт EventLoader) — у сохранений корпуса их иначе нет.
        if (args.Contains("--events") && path.EndsWith(".sg", StringComparison.OrdinalIgnoreCase))
        {
            var events = EventLoader.Load(path);
            state = state with { Events = events, EventProgress = EventLoader.LoadProgress(path, events) };
        }
        if (MapSource.Arg(args, "--winner") is { } winner) state = state with { Info = state.Info with { Winner = winner } };
        // --humans a,b (проверка «горячего стула»): люди партии по очереди ходов. Без него зритель --player — человек: им ходит
        // проверка, и ход ИИ не должен водить его отряды (в Tutorial в файле людей нет, а у отряда зрителя приказ «идти в область»).
        if (MapSource.Arg(args, "--humans") is { } humans)
            state = state with { Info = state.Info with { Humans = [.. humans.Split(',', StringSplitOptions.RemoveEmptyEntries)] } };
        else if (MapSource.Arg(args, "--player") is { } player && player != "none" && state.Info.Humans.Count == 0
                 && looks.Map.Players.ContainsKey(player))
            state = state with { Info = state.Info with { Humans = [player] } };
        return new GameSession(state, GameData.Content.MapArt, GameData.Book);
    }

    /// <summary>С чего начинается вид: отряд игрока с лидером, иначе его столица.</summary>
    private static (int X, int Y)? StartCell(GameSession session, string? viewer)
    {
        if (viewer is null) return null;
        var map = session.Map;
        var stack = map.Stacks.Values.Where(s => s.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase) && s.Inside.Length == 0)
                                     .OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        if (stack is not null) return (stack.X, stack.Y);
        var capital = map.Sites.FirstOrDefault(f => f.Kind == SiteKind.Capital && f.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase));
        return capital is null ? null : (capital.X + 2, capital.Y + 2);
    }

    public override void _ExitTree()
    {
        if (!_placed) return;   // до подписки (ранний отказ при загрузке) отписываться не от чего
        _placed = false;
        GetViewport().SizeChanged -= Place;
    }

    private bool _placed;

    /// <summary>Экран карты удаляется: панели, снятые с дерева на время подмены (панель команд под прицелом, столицей или дипломатией —
    /// <see cref="ReplacePanel"/>, <c>OpenCapital</c>, <c>OpenDiplomacy</c>; полоса ресурсов под золотом дипломатии; <c>DLG_ISO_PAL</c> под
    /// панелью постройки — <c>_palHidden</c>), детьми не числятся и сами не освободились бы. Освобождаются здесь, а не в <c>_ExitTree</c>:
    /// на выходе из дерева дети уже вне дерева, и отличить снятую панель можно только по отсутствию родителя.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        foreach (var node in new Node?[] { _panelView, _resourceView, _palHidden }.Concat(_screens).Distinct())
            if (node is not null && IsInstanceValid(node) && node.GetParent() is null) node.QueueFree();
    }

    private BattleScreen? _battle;

    /// <summary>Выбрано сохранение для загрузки; партию меняет хозяин (игровой цикл).</summary>
    public event Action<string>? LoadRequested;

    /// <summary>Игрок выходит из партии в главное меню.</summary>
    public event Action? QuitRequested;

    /// <summary>Бой с карты: партия списывает очки хода нападающему и собирает бой, карта прячется, поверх —
    /// экран боя. Закрытие боя возвращает карту с исходом (<see cref="CloseBattle"/>).</summary>
    private void OpenBattle(string attackerId, string defenderId) => OpenBattle(attackerId, defenderId, null);

    /// <summary>Вариант фона боя, как у движка: при открытии экрана боя два <c>rand()</c> потока CRT — <c>% n</c> вариантов места
    /// (<c>0x5b35ab</c>) и <c>% 1</c>. Журнал зонда 28.09 (passive-002): после <c>ui_open DLG_BATTLE_A</c> — <c>rng_std max 3</c> (r = 1 и 2 —
    /// фоны <c>NEUTRAL_1</c>/<c>NEUTRAL_2</c> на снимках) и <c>rng_std max 1</c> — у каждого боя.</summary>
    private static int BackgroundDraw(GameSession session, LastEdict.Rules.Battle.BattleState state)
    {
        int variant = session.Crt.Next() % BattleScene.BackgroundVariants(GameData.Content.Gfx, MapBattle.PlaceOf(session.Map, state.DefenderGroup));
        for (int i = 1; i < BackgroundDraws; i++) session.Crt.Next();
        return variant;
    }

    /// <summary>Чисел <c>rand()</c>, которые берёт <see cref="BackgroundDraw"/>: бой экрана в журнале команд (<c>BattlePlay.CrtDraws</c>).</summary>
    private const int BackgroundDraws = 2;

    /// <summary>Сколько чисел <c>rand()</c> взял экран открытого боя человека (фон): уходит в журнал с его действиями.</summary>
    private int _battleCrtDraws;

    /// <summary>Конец боя на экране — командой: журнал ходов и действия экрана (повтор журнала сыграет бой по ним).</summary>
    private void EndBattleCommand(BattleScreen battle) =>
        _map.Session.Execute(new LastEdict.Data.Play.Commands.EndBattleCommand([.. battle.Session.Log], battle.Session.Played(_battleCrtDraws)));

    private void OpenBattle(string attackerId, string defenderId, int? variant)
    {
        var session = _map.Session;
        if (_battle is not null || session.Book is null) return;
        if (OpenThief(attackerId, defenderId)) return;   // вор у цели — окно вора вместо боя (MapScreen.Windows.cs)
        OpenBattleNow(attackerId, defenderId, variant);
    }

    private void OpenBattleNow(string attackerId, string defenderId, int? variant)
    {
        var session = _map.Session;
        if (_battle is not null || session.Book is null) return;
        var state = (LastEdict.Rules.Battle.BattleState)session.Execute(new LastEdict.Data.Play.Commands.StartBattleCommand(attackerId, defenderId)).Value!;
        NoteAttack(attackerId, defenderId);   // нападение — в итоги хода других людей (MapScreen.Windows.cs)
        // Стороны — по собранному бою: цель-невидимка нападает сама (GameSession.StartBattle, CEffectBattle 0x433a57).
        bool playerAttacks = _map.Viewer is null
            || session.Map.Stacks[state.AttackerGroup].Owner.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase);
        ShowMap(false);
        _battle = new BattleScreen
        {
            Name = "Battle", Standalone = false, Prepared = state, Rules = session.Book.Rules, Rng = session.BattleRng,
            PlaceName = MapBattle.PlaceOf(session.Map, state.DefenderGroup), PlayerIsAttacker = playerAttacks,
            OwnsAttackerStack = MapBattle.OwnsAttackerStack(session.Map, state.AttackerGroup, _map.Viewer),   // «Меню героя» (CTaskBattle +0x2c, 0x4d77b8)
            Flipped = _settings.FlippedBattle,   // «Поменяться сторонами» (MapScreen.AiTurn.cs — настройки)
            Speed = _settings.BattleSpeed, BattleAnim = _settings.BattleAnim,   // «Скорость битвы», «Дополнительная анимация в бою»
            QueueStrip = _settings.QueueStrip,   // полоса очереди ходов (своя настройка, MapSettings.QueueStrip)
            OwnName = session.OwnName,   // подпись крупного портрета X100TA0608: у героя — своё имя, у прочих — имя типа (0x5e97dc)
            BackgroundVariant = variant ?? BackgroundDraw(session, state),
            CardLogic = BattleCard(),   // карточка юнита по правой кнопке — общая с картой
            LevelMarks = BattleLevelMarks(),
        };
        _battleCrtDraws = variant is null ? BackgroundDraws : 0;
        _battle.Closed += CloseBattle;
        _battle.EndReached += EndBattleOnScreen;
        AddChild(_battle);
        GD.Print($"mapbattle start attacker={attackerId} defender={defenderId} units={state.Units.Count} " +
                 $"place={_battle.PlaceName} move_left={session.Map.Stacks[attackerId].Move}");
    }

    /// <summary>«Закрыть» после боя человека: исход — в карту (<see cref="LastEdict.Data.Play.GameSession.CommitBattle"/>: выигранные сражения, снятые
    /// отряды, могилы, добыча, вход, события), экран боя снимается, карта пересобирается. Конец боя модели и его показ уже были
    /// (<see cref="EndBattleOnScreen"/>). Узел снимается отложенно: закрытие приходит из его же обработчика ввода.</summary>
    private void CloseBattle()
    {
        if (_battle is not { } battle) return;
        battle.Closed -= CloseBattle;
        battle.EndReached -= EndBattleOnScreen;
        var session = _map.Session;
        string attacker = battle.Session.State.AttackerGroup, defender = battle.Session.State.DefenderGroup;
        try
        {
            if (!_battleEnded) EndBattleCommand(battle);   // закрыт без показа конца (проверка автобоем)
            var visit = (VisitReport?)session.Execute(new LastEdict.Data.Play.Commands.CommitBattleCommand()).Value;
            bool defenderAlive = session.Map.Stacks.ContainsKey(defender) || MapBattle.DefenderOf(session.Map, defender) is not null;
            GD.Print($"mapbattle end winner={battle.Session.Winner ?? "-"} attacker_alive={(session.Map.Stacks.ContainsKey(attacker) ? 1 : 0)} " +
                     $"defender_alive={(defenderAlive ? 1 : 0)} turns={battle.Session.Log.Count}" +
                     (visit is null ? "" : $" visit={visit.SiteId}:{(visit.Refused is null ? (visit.Captured ? "захват" : "добыча") : "отказ")}"));
        }
        catch (Exception e)
        {
            // Сбой итога не должен оставить экран боя висеть: он снимается, карта возвращается.
            Failures.Report(this, "MapScreen.CloseBattle", e);
        }
        _battleEnded = false;
        _battle = null;
        Callable.From(() =>
        {
            if (!IsInstanceValid(battle)) return;
            RemoveChild(battle);
            battle.QueueFree();
        }).CallDeferred();
        ShowMap(true);
        _map.RefreshGround();
        _map.Reload();
        _map.RestoreShownPath();   // путь к цели команды, кончившейся боем, — снова с новой клетки (0x4cfead)
        SyncPanels();
        if (battle.Paperdoll) OpenPaperdoll(attacker);
    }

    /// <summary>Бой человека кончился (после ходов лекарей): конец боя модели (<see cref="LastEdict.Data.Play.GameSession.EndBattle"/> — предметы,
    /// уход призванных, откат превращений, опыт, рост) и его показ на ещё открытом экране (<see cref="BattleScreen.PlayEnd"/>, снимок
    /// <see cref="BattleScreen.BeforeEnd"/> — до конца боя), затем «Опыт: +N» и кнопки «Закрыть»/«Меню героя» (<see cref="BattleScreen.ShowEnd"/>).
    /// Так у движка: <c>updateGroupsIfBattleIsOver</c> <c>0x62953c</c> → эффекты шагов 13–16 → <c>battleEnd</c> <c>0x630a3c</c> (замечание 33
    /// прогона 11.10; прежде всё это шло только после «Закрыть»). Карта меняется по «Закрыть» (<see cref="CloseBattle"/>).</summary>
    private void EndBattleOnScreen()
    {
        if (_battle is not { } battle || _battleEnded || !battle.Session.Finished) return;
        try
        {
            var before = battle.BeforeEnd();
            EndBattleCommand(battle);
            _battleEnded = true;
            GD.Print($"mapbattle over сообщений={battle.Session.State.Notes.Count - before.NotesFrom}");
            battle.PlayEnd(before, _map.UnitRaceType, battle.ShowEnd);
        }
        catch (Exception e)
        {
            // Сбой конца боя не должен оставить экран без «Закрыть»: кнопки появляются, конец боя досчитает CloseBattle.
            Failures.Report(this, "MapScreen.EndBattleOnScreen", e);
            battle.ShowEnd();
        }
    }

    /// <summary>Конец боя человека посчитан (<see cref="EndBattleOnScreen"/>), а в карту ещё не записан.</summary>
    private bool _battleEnded;

    /// <summary>Карта и её панели принимают ввод — или нет (бой, окно поверх). Под боем они ещё и спрятаны; под окном
    /// настроек остаются видны (<paramref name="keepVisible"/>).</summary>
    private void ShowMap(bool shown, bool keepVisible = false)
    {
        bool visible = shown || keepVisible;
        _mapBox.Visible = visible && !CoversMap;
        _map.PointerEnabled = shown && !CoversMap && !Aiming && _dropStack is null && _exchangeStack is null && _rodStack is null;
        _map.ScrollEnabled = shown && !CoversMap;
        if (_capitalView is not null) _capitalView.Visible = visible;
        _screensVisible = visible;
        foreach (var screen in _screens)
        {
            screen.Visible = visible;
            screen.SetProcessUnhandledInput(shown);
        }
        ApplyResources();
        if (_mini is not null)
        {
            _mini.Visible = visible && !_palette.Land;
            // Под окном мини-карта мышь не берёт: иначе её прямоугольник (644…794 × 62…194) съедает нажатия окна — кнопку роста деревни,
            // начало перетаскивания (Control ловит мышь раньше _UnhandledInput окон). На открытой карте — Pass: щелчок, не взятый
            // мини-картой, уходит экрану панели (MiniMapView.Covered).
            _mini.MouseFilter = shown ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        }
        if (_face is not null) _face.Visible = visible && _faceName.Length > 0 && !CoversMap;
    }

    /// <summary>Мини-карта в окне <c>IMG_PALMAP</c> панели <c>DLG_ISO_PAL</c> (её место — <c>IMG_RESOURCE</c>).</summary>
    private void BuildMiniMap(IReadOnlyList<UiScreen> screens, MapLooks looks, string? viewer)
    {
        var palette = screens.FirstOrDefault(s => s.Id == "DLG_ISO_PAL");
        if (palette is null) return;
        var window = Slot(palette, "IMG_PALMAP");
        var at = HostSlot("IMG_RESOURCE");
        _miniRect = new Rect2(at.X + window.X, at.Y + window.Y, window.Width, window.Height);
        _mini = new MiniMapView { Name = "MiniMap" };
        AddChild(_mini);
        _mini.Build(looks, GameData.Content.Gfx, GameData.GfxRoot, viewer is null ? null : _map.Scene.Hidden,
                    new Vector2(window.Width, window.Height));
        _mini.Covered = PaletteCovers;
        _mini.Picked += cell =>
        {
            _map.CenterOn(cell);
            _mini.Centre = cell;
            _mini.Place();
        };
    }

    private UiScreenView AddScreen(string id, Vector2? at, IScreenLogic? logic = null)
    {
        var screen = new UiScreenView { Hosted = id, Name = id, SlotAt = at ?? Vector2.Zero, SlotHost = _host, Logic = logic };
        // Холст подэкрана (ui/layout.json): DLG_ISO_VIEW — весь экран карты, экраны поверх карты (рамка края поля, полоса ресурсов) — место карты.
        if (id == View) screen.SlotSize = new Vector2I((int)_host.X, (int)_host.Y);
        else if (id is "DLG_ISO" or "DLG_ISO_RESPOPUP") screen.SlotSize = new Vector2I((int)_mapRect.Size.X, (int)_mapRect.Size.Y);
        AddChild(screen);
        _screens.Add(screen);
        return screen;
    }

    /// <summary>Что показывать на панелях: выбранный отряд, день и казна игрока.</summary>
    private MapPanelState PanelState()
    {
        var stack = _map.SelectedStack;
        var bank = _map.Viewer is { } id && _map.Map.Players.TryGetValue(id, out var player) ? player.Bank : Bank.Zero;
        if (stack is null) return MapPanelState.Empty with { Turn = _map.Map.Turn, Bank = bank, CanNextLeader = _map.NextLeaders().Count > 0 };
        string type = _map.Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        var unit = GameData.Content.MapArt.Unit(type);
        var look = _map.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase));
        string name = look is { LeaderName.Length: > 0 } ? look.LeaderName : Text(unit?.NameTxt ?? "");
        string? face = type.Length == 0 ? null : Faces.PanelName(type);
        return new MapPanelState(name, face, stack.Move, _map.DayAllowance(stack), _map.Map.Turn, bank, _map.Session.CanExchange(stack.Id),
                                 _map.NextLeaders().Count > 0, PanelFaceOf(stack, face), _map.CanContinueMove, CanDropRod: true, CanGuard: true,
                                 CanWandScroll: Windows is { } context && LastEdict.Data.Ui.Windows.WandScrollLogic.CanOpen(context, stack.Id));
    }

    /// <summary>Портрет панели целиком (<see cref="PanelFace"/>): павший — маска, раненый — красная доля, значки; ширина картинки — из описи.</summary>
    private PanelFace? PanelFaceOf(MapStack stack, string? face)
    {
        if (face is null || Windows is not { } context || !_map.Map.Units.TryGetValue(stack.LeaderId, out var leader)) return null;
        var gfx = GameData.Content.Gfx;
        var info = gfx.Image("Faces", face) ?? gfx.Image("Faces", face + "B");
        return PanelFace.Of(context, leader, face, info?.W ?? PanelFace.SmallWidth);
    }

    /// <summary>Портрет лидера: картинка <c>{база типа}FACES</c>/<c>FACE</c> (<see cref="LastEdict.Data.Ui.Windows.UnitFaces.PanelName"/>) лежит в графике карты (<c>Faces.ff</c>), а не в наборе
    /// кадров интерфейса, поэтому рисуется своим спрайтом в окне <c>IMG_LEADER</c> панели. Движок кладёт её флагами 9 (<c>0x48dc0b</c> →
    /// <c>setImage</c> <c>0x530e29</c>): по центру места, без масштаба; что шире места 70×85, обрезается по нему. Прежде порт вписывал
    /// картинку целиком: широкий <c>FACE</c> 150×85 большого юнита без своего <c>FACES</c> (призванный «Оживший доспех» <c>G000UU0024</c>)
    /// сжимался в полосу 70×40 у верха места, а ниже был виден фон панели — «портрет из двух картинок» (партия 15.10, карта 2).</summary>
    private void Portrait(MapPanelState state)
    {
        if (_faceSlot.Size == Vector2.Zero) return;
        _face ??= AddFace();
        string? face = state.LeaderFace;
        if (face is null)
        {
            _face.Visible = false;
            return;
        }
        // Тот же портрет, что был, — показать снова: его могли спрятать столица, дипломатия или ход ИИ (герой, выведенный из
        // столицы, — тот же лидер, что был выбран до неё). Ключ — с состоянием: павший, доля здоровья, значки.
        string key = state.Face?.Key ?? face;
        if (key == _faceKey)
        {
            _face.Visible = _face.Texture is not null;
            return;
        }
        _faceName = face;
        _faceKey = key;
        _faceLook = state.Face;
        var gfx = GameData.Content.Gfx;
        var info = gfx.Image("Faces", face) ?? gfx.Image("Faces", face + "B");
        if (info is null || GfxAssets.PathOf(info, GameData.GfxRoot) is not { } path || !File.Exists(path))
        {
            // Картинки нет — снять и прежнюю: иначе при возврате к лидеру с тем же ключом показался бы чужой портрет.
            var stale = _face.Texture;
            _face.Texture = null;
            stale?.Dispose();
            _faceKey = "";
            _face.Visible = false;
            return;
        }
        var canvas = GfxTextures.Canvas(path);
        var built = state.Face is { } look ? PanelFaceImage(canvas, look) : canvas;
        var texture = ImageTexture.CreateFromImage(built);
        built.Dispose();
        var old = _face.Texture;
        _face.Texture = texture;
        old?.Dispose();   // прежний портрет больше никто не держит — видеопамять сразу, а не сборкой мусора
        _face.Visible = true;
        var size = texture.GetSize();
        var shown = new Vector2(Mathf.Min(size.X, _faceSlot.Size.X), Mathf.Min(size.Y, _faceSlot.Size.Y));
        _face.RegionEnabled = true;
        _face.RegionRect = new Rect2(((size - shown) / 2f).Floor(), shown);
        _faceOffset = ((_faceSlot.Size - shown) / 2f).Floor();
    }

    /// <summary>Портрет панели по движку (<see cref="PanelFace"/>): у павшего — маска-череп наполовину по всем точкам лица, у раненого —
    /// нижняя потерянная доля наполовину красным (<see cref="BattlePanel.Shade"/>, тот же блиттер <c>0x5b1af4</c>), затем отражение слева
    /// направо вместе с маской и долей; значки (<c>Battle.ff</c>) — поверх по своим местам, без отражения.</summary>
    private static Image PanelFaceImage(Image face, PanelFace look)
    {
        face.Convert(Image.Format.Rgba8);
        int w = face.GetWidth(), h = face.GetHeight();
        byte[] data = face.GetData();
        var mask = look.Mask is { } name ? FaceMask(name) : null;
        BattlePanel.Shade(data, w, null, look.KeptRows(h), mask?.Data, mask?.W ?? 0, mask?.H ?? 0);
        face.SetData(w, h, false, Image.Format.Rgba8, data);
        if (PanelFace.Mirrored) face.FlipX();
        var gfx = GameData.Content.Gfx;
        foreach (var icon in look.Icons)
        {
            if (gfx.Image("Battle", icon.Name) is not { } info || GfxAssets.PathOf(info, GameData.GfxRoot) is not { } path || !File.Exists(path))
                continue;
            var image = GfxTextures.Canvas(path);
            image.Convert(Image.Format.Rgba8);
            int x = icon.Centered ? (w / 2) - (image.GetWidth() / 2) : icon.X;   // 0x48dbd4…0x48dbf5: половина ширины − половина значка
            face.BlendRect(image, new Rect2I(Vector2I.Zero, image.GetSize()), new Vector2I(x, icon.Y));
            image.Dispose();
        }
        return face;
    }

    /// <summary>Строка проверки портрета панели: средняя разница (на канал, видимые точки) показанной картинки с исходной (<c>сырое=</c>) и с
    /// исходной, отражённой слева направо (<c>отражённое=</c>). Здоровый лидер без значков — отражённое=0; павший — оба больше нуля (череп).</summary>
    private string FaceStats()
    {
        if (_face?.Texture is not { } texture
            || (GameData.Content.Gfx.Image("Faces", _faceName) ?? GameData.Content.Gfx.Image("Faces", _faceName + "B")) is not { } info
            || GfxAssets.PathOf(info, GameData.GfxRoot) is not { } path || !File.Exists(path))
            return "сырое=- отражённое=-";
        var shown = texture.GetImage();
        var raw = GfxTextures.Canvas(path);
        raw.Convert(Image.Format.Rgba8);
        shown.Convert(Image.Format.Rgba8);
        var flipped = (Image)raw.Duplicate();
        flipped.FlipX();
        string line = FormattableString.Invariant($"сырое={Diff(shown, raw):F1} отражённое={Diff(shown, flipped):F1}");
        raw.Dispose();
        flipped.Dispose();
        shown.Dispose();
        return line;

        static double Diff(Image a, Image b)
        {
            if (a.GetSize() != b.GetSize()) return -1;
            byte[] x = a.GetData(), y = b.GetData();
            long sum = 0, n = 0;
            for (int i = 0; i + 3 < x.Length; i += 4)
            {
                if (x[i + 3] == 0 && y[i + 3] == 0) continue;
                sum += Math.Abs(x[i] - y[i]) + Math.Abs(x[i + 1] - y[i + 1]) + Math.Abs(x[i + 2] - y[i + 2]);
                n += 3;
            }
            return n == 0 ? 0 : (double)sum / n;
        }
    }

    /// <summary>Точки маски павшего (<c>Faces</c>, <c>MASKDEADS</c>/<c>MASKDEADL</c>) — по одному разу на запуск; нет в графике — null.</summary>
    private static (byte[] Data, int W, int H)? FaceMask(string name)
    {
        if (_faceMasks.TryGetValue(name, out var cached)) return cached;
        (byte[], int, int)? mask = null;
        if (GameData.Content.Gfx.Image("Faces", name) is { } info && GfxAssets.PathOf(info, GameData.GfxRoot) is { } path && File.Exists(path))
        {
            var image = GfxTextures.Canvas(path);
            image.Convert(Image.Format.Rgba8);
            mask = (image.GetData(), image.GetWidth(), image.GetHeight());
            image.Dispose();
        }
        return _faceMasks[name] = mask;
    }

    private static readonly Dictionary<string, (byte[] Data, int W, int H)?> _faceMasks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Портрет — над панелью команд: панель, вернувшаяся после столицы, дипломатии или режима с панелью на её месте, встаёт в
    /// конец детей и закрывала портрет своим фоном (замечание 11 прогона 11.10: «нет портрета у отряда, выведенного из столицы»).</summary>
    private void FaceOverPanel()
    {
        if (_face is null || _panelView is null || _face.GetParent() != this || _panelView.GetParent() != this) return;
        if (_face.GetIndex() < _panelView.GetIndex()) MoveChild(_face, _panelView.GetIndex());
    }

    /// <summary>Самый новый отряд зрителя (наибольший номер id) — шаги проверки «arrive new», «select new» после призыва.</summary>
    private string NewestOwnStack() =>
        _map.Map.Stacks.Values.Where(s => s.Owner.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Id.Length > 6 && int.TryParse(s.Id.AsSpan(6), System.Globalization.NumberStyles.HexNumber,
                                                                     System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : -1)
            .Select(s => s.Id).FirstOrDefault() ?? "-";

    /// <summary>Видимая часть портрета и её сдвиг в месте (строка проверки): «70x85+0,0» — место заполнено целиком.</summary>
    private string FaceRegion => FaceShown && _face is { } face
        ? $"{(int)face.RegionRect.Size.X}x{(int)face.RegionRect.Size.Y}+{(int)_faceOffset.X},{(int)_faceOffset.Y}" : "-";

    /// <summary>Портрет виден и лежит над панелью команд (строка проверки).</summary>
    private bool FaceShown => _face is { Visible: true } face && face.Texture is not null && _panelView is { } panel
                              && panel.GetParent() == this && face.GetIndex() > panel.GetIndex();

    private Sprite2D AddFace()
    {
        var sprite = new Sprite2D { Name = "Face", Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(sprite);
        return sprite;
    }

    /// <summary>Кнопки панели команд: конец хода, следующий лидер, столица (остальные — пункты 5 и 6).</summary>
    private void Commands(IReadOnlyList<UiEvent> events)
    {
        foreach (var e in events)
        {
            if (e is not UiClicked click) continue;
            if (click.Element == "BTN_END_TURN") EndTurn();
            if (click.Element == "BTN_NEXT_LEADER") _map.NextLeader();
            if (click.Element == "BTN_CONT_MOVE") _map.ContinueMove();   // «Продолжить движение», M (MapView.Routes.cs)
            if (click.Element == "BTN_CAPITAL") OpenCapital();
            if (click.Element == "BTN_CAST_SPELL") OpenSpellBook();
            if (click.Element == "BTN_DIPLOMACY") OpenDiplomacy();
            if (click.Element == "BTN_EXCHANGE") StartExchange();
            if (click.Element == "BTN_DROP_ROD") StartDropRod();
            if (click.Element == "BTN_WAND_SCROLL") OpenWandScroll();
        }
    }

    private void SyncPanels()
    {
        RodAfterWalk();   // жезл после хода отряда (MapScreen.Rod.cs)
        if (_panelView is not null && _panel is not null)
        {
            _panel.Refresh(_panelView.State);
            _panelView.Sync();
        }
        if (_resourceView is not null && _resources is not null)
        {
            _resources.Refresh(_resourceView.State);
            _resourceView.Sync();
        }
        SyncCapital();
        if (CoversMap) { if (_face is not null) _face.Visible = false; }
        else
        {
            FaceOverPanel();
            Portrait(PanelState());
        }
        Place();
        PumpOverlays();
    }

    private string Text(string id) =>
        id.Length > 0 && _strings.TryGetValue(id, out string? value) ? value : "";

    /// <summary>Карта масштабируется так же, как экраны (вписывание 800×600 в окно по центру).</summary>
    private void Place()
    {
        Relayout();   // окно сменило форму — холст, места и подэкраны заново (MapScreen.Layout.cs)
        var size = GetViewport().GetVisibleRect().Size;
        float k = Mathf.Min(size.X / _host.X, size.Y / _host.Y);
        _mapBox.Position = ((size - (_host * k)) / 2f) + (_mapRect.Position * k);
        // Вьюпорт карты — в точках экрана (не место карты 640×600, растянутое на окно): HD-графика видна, карты видно столько, сколько
        // даёт масштаб карты (MapZoom: 100 % — точка картинки на точку экрана, как у врапера; «по окну» — как в оригинале).
        _mapBox.Scale = Vector2.One;
        _mapBox.Size = _mapRect.Size * k;
        float zoom = MapZoom(k);
        _map.Zoom = zoom;
        PlaceCapital(size, k);
        if (_face is not null && _faceSlot.Size != Vector2.Zero)
        {
            _face.Position = ((size - (_host * k)) / 2f) + ((_faceSlot.Position + _faceOffset) * k);
            _face.Scale = new Vector2(k, k);
        }
        if (_mini is null) return;
        _mini.Position = ((size - (_host * k)) / 2f) + (_miniRect.Position * k);
        _mini.Scale = new Vector2(k, k);
        _mini.Centre = _map.CentreCell;
        _mini.ViewSize = _mapRect.Size * k / zoom;   // рамка вида — по видимой части карты
        _mini.Place();
    }

    /// <summary>Зум камеры карты: «по окну» — как место карты растянуто на окно (<paramref name="k"/>); проценты — от точки экрана (показ
    /// экранов через буфер ScreenStage уменьшает его на свой масштаб).</summary>
    private float MapZoom(float k)
    {
        if (_settings.MapZoom == MapSettings.MapZoomFit) return k;
        float stage = ScreenStage.Current is { } st && st.Stage == GetViewport() && st.Fit.Scale > 0 ? st.Fit.Scale : 1f;
        return _settings.MapZoom / 100f / stage;
    }

    private static UiRect Slot(UiScreen screen, string id) =>
        screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect
        ?? throw new InvalidOperationException($"{screen.Id}: нет места {id}");

    private static (int X, int Y) ParseCell(string text)
    {
        var parts = text.Split(',');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool _clicksDone;

    /// <summary>Партия уходит (загрузка другой или выход в меню): хозяин снимет экран, проверка кончается.</summary>
    private bool _leaving;

    private async System.Threading.Tasks.Task RunCheck(string? shot)
    {
        var start = DateTime.UtcNow;
        while (!_map.GroundReady && (DateTime.UtcNow - start).TotalSeconds < 60)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // Щелчки проверки — только в первой партии запуска: загрузка из партии открывает новую, и та их не повторяет.
        string clicks = _clicksDone ? "" : MapSource.Arg(OS.GetCmdlineUserArgs(), "--clicks") ?? "";
        _clicksDone = true;
        foreach (string step in clicks.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (step is "end" or "конец")
            {
                DismissBeginTurn();   // окно начала хода — каждый день (MapScreen.Overlays.cs); попапы за ним — по-прежнему шагом «ok»
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await AcceptUpgrades();   // окна выбора навыка лидера (MapScreen.Overlays.cs)
                AcceptLeaderName();
                EndTurn();
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await AiTurnDone();   // ход ИИ идёт в фоне (MapScreen.AiTurn.cs)
                // Окно начала хода нового дня — «ОК» (оно первое из окон хода; за ним — итоги, навыки, как прежде видел шаг «end»).
                if (_overlay is { Logic: LastEdict.Data.Ui.Windows.BeginTurnLogic })
                {
                    DismissBeginTurn();
                    for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GD.Print($"end день={_map.Map.Turn} {WindowLine()}");
                continue;
            }
            if (step == "win" && _map.Viewer is { } viewer)   // победа зрителя (конец главы кампании, docs/day-cycle.md §15)
            {
                AcceptLeaderName();
                _map.Session.Run(new LastEdict.Data.Play.Commands.WinCommand(viewer));
                SyncPanels();   // окна конца партии выкладывает очередь окон
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                continue;
            }
            if (step == "pause")   // 150 мс без ввода: зонд порта отделяет следствия нажатия (ui_enable_click — 100 мс) от следующего
            {
                var at = DateTime.UtcNow;
                while ((DateTime.UtcNow - at).TotalMilliseconds < 150) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                continue;
            }
            if (step == "aidone")   // ход ИИ, начатый щелчком «Конец хода» (panel click BTN_END_TURN), до конца; окно начала хода — «ОК»
            {
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await AiTurnDone();
                if (_overlay is { Logic: LastEdict.Data.Ui.Windows.BeginTurnLogic })
                {
                    DismissBeginTurn();
                    for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GD.Print($"aidone день={_map.Map.Turn} {WindowLine()}");
                continue;
            }
            if (step == "ok")
            {
                // Окна хода по очереди движка: начало хода → имя героя (день 1) → выбор навыка → попапы событий; каждое следующее
                // открывается кадром позже — круги, пока закрывать нечего.
                for (int round = 0; round < 8; round++)
                {
                    DismissPopups();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    AcceptLeaderName();   // окно имени первого хода открывается за окном начала хода кадром позже
                    await AcceptUpgrades();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (!TurnWindowsPending) break;
                }
                if (_battle is not null)   // за окнами событий открылся бой эффекта — доиграть
                {
                    _battle.AutoFinish();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                continue;
            }
            if (step.StartsWith("padcell ", StringComparison.Ordinal) && GamepadPointer.Current is { } pointer)
            {
                // Геймпад над картой (Э5, Р12): вид на клетку, указатель геймпада в середину поля карты, A — нажатие и отпускание кнопки
                // геймпада настоящим путём (GamepadPointer → щелчок мыши → вьюпорт карты).
                var at = ParseCell(step[8..].Trim());
                _map.CenterOn(at);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var middle = _mapBox.GetGlobalRect().GetCenter();
                pointer.MoveTo(ScreenStage.Current is { } stage && stage.Stage == GetViewport() ? stage.ToRoot * middle : middle);
                var root = GetTree().Root;
                root.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true });
                root.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false });
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                SyncPanels();
                GD.Print($"padcell {at.X},{at.Y} выбран={_map.Selected ?? "-"} {pointer.Line()} {WindowLine()}");
                continue;
            }
            if (step == "pointer" && GamepadPointer.Current is { } padPointer)   // где указатель геймпада (после кадра: шаг к фокусу окна)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                PadHints.Current?.Refresh();
                GD.Print($"{padPointer.Line()} подсказки={(PadHints.Current?.Shown ?? "-").Replace(' ', '_')} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("cursor ", StringComparison.Ordinal))   // курсор карты над клеткой (MapScreen.Cursor.cs)
            {
                var at = ParseCell(step[7..].Trim());
                string name = CursorAt(at);
                GD.Print($"cursor {at.X},{at.Y} курсор={name} картинка={(AnimatedCursor.FirstFrame(name) is { } c ? $"{c.GetWidth()}x{c.GetHeight()}" : "-")}");
                continue;
            }
            if (step == "route")   // путь выбранного отряда, флажки и «Продолжить движение» (MapView.Routes.cs)
            {
                SyncPanels();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"route {_map.RouteLine()} кнопка={(_panelView?.State.IsEnabled("BTN_CONT_MOVE") == true ? 1 : 0)} день={_map.Map.Turn}");
                continue;
            }
            if (step == "selmark")   // эллипс выделения под выбранным отрядом (MapView.Selection.cs, Ин49)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"selmark выбран={_map.Selected ?? "-"} эллипс={(_map.SelectionShown ? 1 : 0)} снят={(_map.SelectionHeld ? 1 : 0)} " +
                         $"верх={TopWindow()?.Hosted ?? "-"}");
                continue;
            }
            if (step == "wcursor")   // курсор над окнами или картой (MapScreen.Cursor.cs) — после кадров, в которых его ставит UpdateCursor
            {
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                string hotspot = Cursor.Hotspot is { } h ? FormattableString.Invariant($"{h.X:0.#},{h.Y:0.#}") : "-";
                string owner = Cursor.Current is not { } now ? "-" : now.StartsWith("ui:", StringComparison.Ordinal) ? "окно"
                    : now == _cursorName ? "карта" : "другой";
                GD.Print($"wcursor окна={_dialogViews.Count + (_overlay is null ? 0 : 1)} верх={TopWindow()?.Hosted ?? "-"} " +
                         $"курсор={Cursor.Current ?? "-"} кадров={Cursor.Frames} точка={hotspot} чей={owner}");
                continue;
            }
            if (step.StartsWith("highlight ", StringComparison.Ordinal))   // подсветка клетки «под мышью» (MapScreen.Cursor.cs): фаза при смене клетки
            {
                for (int i = 0; i < 300 && _map.HighlightPhase < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                int before = _map.HighlightPhase;
                _highlightCell = ParseCell(step[10..].Trim());
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var shown = _map.Highlighted;
                var (seen, all) = _map.HighlightVisibility();
                GD.Print($"highlight {_highlightCell.Value.X},{_highlightCell.Value.Y} клетка={(shown is { } h ? $"{h.X},{h.Y}" : "-")} " +
                         $"фаза_до={before} фаза={_map.HighlightPhase} настройка={(_settings.IsoHighlight ? 1 : 0)} видно={seen}/{all}");
                continue;
            }
            if (step == "arrows")   // прокрутка стрелкой: «вправо» зажата 10 кадров — на сколько точек уехал вид (MapView.Scroll)
            {
                var before = _map.ViewCenter;
                Input.ActionPress("ui_right");
                for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ActionRelease("ui_right");
                GD.Print($"arrows сдвиг={_map.ViewCenter.X - before.X:0} {WindowLine()}");
                continue;
            }
            if (step is "wheel up" or "wheel down")   // колесо над картой — шаг вида на клетку (MapView: WM_MOUSEWHEEL = VK_UP/VK_DOWN, 0x5418fb)
            {
                var before = _map.ViewCenter;
                // Во вьюпорт карты (контейнер SubViewportContainer передаёт туда события мыши над картой сам).
                var port = _map.GetViewport();
                var at = port.GetVisibleRect().Size / 2;
                var button = step == "wheel up" ? MouseButton.WheelUp : MouseButton.WheelDown;
                port.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = true, Factor = 1 });
                port.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = false, Factor = 1 });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"wheel сдвиг={_map.ViewCenter.X - before.X:0},{_map.ViewCenter.Y - before.Y:0} {WindowLine()}");
                continue;
            }
            if (step == "clouds")   // облака (MapView.Clouds.cs): число, порядок под туманом, сдвиг за полсекунды
            {
                int x0 = _map.FirstCloudX;
                var (at, of) = _map.LayerOrder("clouds");
                for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var began = DateTime.UtcNow;
                while ((DateTime.UtcNow - began).TotalMilliseconds < 500) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"clouds облаков={_map.CloudCount} в_порядке={at.Count}/{of} сдвиг={_map.FirstCloudX - x0} " +
                         $"настройка={(_settings.IsoClouds ? 1 : 0)}");
                continue;
            }
            if (step.StartsWith("fogtest ", StringComparison.Ordinal))   // «fogtest x,y,r» — эффект CEffectFog зрителю: квадрат ±r без углов открыт
            {
                var parts = step[8..].Trim().Split(',');
                var (fx, fy, fr) = (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture),
                                    int.Parse(parts[2], CultureInfo.InvariantCulture));
                int opened = _map.OpenFogForCheck(fx, fy, fr);
                _map.HoldFog(fx, fy, fr);
                bool started = _map.PlayFogChanges(derive: false);
                for (int i = 0; i < 2000 && _map.FogChanging; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"fogtest done {fx},{fy},{fr} открыто={opened} показ={(started ? 1 : 0)} идёт={(_map.FogChanging ? 1 : 0)} " +
                         $"расхождение={_map.FogMismatch()} карта={_map.Map.Size}");
                continue;
            }
            if (step.StartsWith("mountains ", StringComparison.Ordinal))   // снос гор (MapView.PlayMountains) в клетке — как по точке MountainsRemoved
            {
                var at = ParseCell(step[10..].Trim());
                bool played = _map.ScrolledPlay(at, onlyIfFar: true, () => _map.PlayMountains("CHECK", at.X, at.Y, MapView.CheckAiSpeed));
                for (int i = 0; i < 600 && _map.SpellPlaying; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"mountains done {at.X},{at.Y} показ={(played ? 1 : 0)} играет={(_map.SpellPlaying ? 1 : 0)}");
                continue;
            }
            if (step.StartsWith("aim ", StringComparison.Ordinal))   // прицел заклинанием напрямую, без книги (проверка квадрата под туманом)
            {
                if (_aimSpell is not null) StopAim();   // прежний прицел — снять: шаги сравнивают заклинания по очереди
                StartAim(step[4..].Trim());
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                continue;
            }
            if (step.StartsWith("order ", StringComparison.Ordinal))   // места картинок канала (marks, aim) в общем порядке отрисовки
            {
                string channel = step[6..].Trim();
                var (places, of) = _map.LayerOrder(channel);
                var (under, fogged) = _map.UnderFog(channel);
                GD.Print($"order {channel} картинок={places.Count} всего={of} в_конце={places.Count(p => p >= of - places.Count)} " +
                         $"под_туманом={under}/{fogged} места={string.Join(",", places.Take(8))}");
                continue;
            }
            if (step.StartsWith("arrive ", StringComparison.Ordinal))   // «arrive S073KC0076 S073KC0032» — отряд у цели (MapView.ArriveAt)
            {
                var parts = step[7..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    GD.PrintErr($"MapScreen: шаг «{step}» — нужно «arrive <отряд> <цель>»");
                    GetTree().Quit(1);
                    return;
                }
                // «new» — самый новый отряд зрителя (призванный только что).
                string arriving = parts[0] == "new" ? NewestOwnStack() : parts[0];
                _map.ArriveAt(arriving, parts[1]);
                for (int i = 0; i < 200 && _map.SpellPlaying; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_battle is not null)   // приход — нападение (охрана руины, отряд): бой до конца, как у шага щелчка
                {
                    _battle?.AutoFinish(OS.GetCmdlineUserArgs().Contains("--paperdoll"));
                    for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                string inside = _map.Map.Stacks.TryGetValue(arriving, out var arrived) && arrived.Inside.Length > 0 ? arrived.Inside : "-";
                string holder = _map.Map.Sites.FirstOrDefault(f => f.Id.Equals(parts[1], StringComparison.OrdinalIgnoreCase))?.Owner ?? "-";
                GD.Print($"arrive {parts[0]} {parts[1]} {WindowLine()} отряд={arriving} внутри={inside} хозяин_цели={(holder.Length > 0 ? holder : "-")} " +
                         $"сообщение={_overlay?.Hosted ?? "-"}");
                continue;
            }
            if (step == "frame")   // рамка края поля вида (IsoViewLogic) и фон мини-карты
            {
                string outline = _screens.FirstOrDefault(v => v.Hosted == View)?.State.Background ?? "-";
                int circles = _map.Scene.Sprites.Count(s => s.Name.StartsWith("OBJECTIVE", StringComparison.Ordinal));
                GD.Print($"frame рамка={(outline.Length > 0 ? outline : "-")} мини_фон={(_mini is null ? "-" : MiniMapView.Backdrop.ToHtml(false))} " +
                         $"цели={_map.Session.Objectives(_map.Viewer ?? "").Count} кругов={circles}");
                continue;
            }
            if (step == "forts")   // свои укрепления зрителя: вход (правый нижний угол пятна) и гость — для шагов щелчка по укреплению
            {
                var own = _map.Map.Sites.Where(f => f.Kind is SiteKind.Capital or SiteKind.Village
                                                    && f.Owner.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase));
                GD.Print($"forts {string.Join(" ", own.Select(f => $"{f.Id}:{f.Kind}@{(LastEdict.Rules.Map.PathFinder.EntranceOf(_map.Map, f.Id) is { } e ? $"{e.X},{e.Y}" : "-")}:гость={(f.StackId.Length > 0 ? f.StackId : "-")}"))}");
                continue;
            }
            if (step == "tombs")   // могилы модели (MapState.Tombs): клетка и число записей
            {
                GD.Print($"tombs {string.Join(" ", _map.Session.Map.Tombs.Select(t => $"{t.Id}@{t.X},{t.Y}:{t.Entries.Count}"))}");
                continue;
            }
            if (step == "blend")   // смешение картинок карты по opacity кадра (GfxBlending.cs)
            {
                var (half, add, sub) = _map.BlendCounts();
                GD.Print($"blend половина={half} сложение={add} вычитание={sub}");
                continue;
            }
            if (step.StartsWith("regions ", StringComparison.Ordinal))   // фон мест у клетки в центре вида (MapView.Sounds.cs)
            {
                var at = ParseCell(step[8..].Trim());
                _map.CenterOn(at);
                var voices = _map.RegionVoices();
                var muted = _map.RegionMuted();
                GD.Print($"regions {at.X},{at.Y} источников={_map.RegionSourceCount} звучат={string.Join(",", voices.Select(v => $"{v.Source.Name}:{v.Volume}:{v.Pan}"))} " +
                         $"молчат={(muted.Count == 0 ? "-" : string.Join(";", muted.Select(v => $"{v.Source.Name}@{v.Source.X},{v.Source.Y}")))}");
                continue;
            }
            if (step == "palette" || step.StartsWith("minimap ", StringComparison.Ordinal))
            {
                // Палитра (MapScreen.Palette.cs): «palette» — строка состояния; «minimap 10,20» — щелчок в точку окна мини-карты: возьмёт ли
                // его мини-карта или кнопка панели над ней.
                string extra = "";
                if (step != "palette")
                {
                    var (mx, my) = ParseCell(step[8..].Trim());
                    extra = $" кнопка_над_окном={(PaletteCovers(new Vector2(mx, my)) ? 1 : 0)}";
                }
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"{step.Replace(' ', '_')} {PaletteLine()}{extra}");
                continue;
            }
            if (step == "next")
            {
                // «Следующий лидер» (BTN_NEXT_LEADER, Tab): выбранный отряд и доступность кнопки.
                bool enabled = _panelView?.State.IsEnabled("BTN_NEXT_LEADER") == true;
                _map.NextLeader();
                SyncPanels();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"next выбран={_map.Selected ?? "-"} кнопка={(enabled ? 1 : 0)}");
                continue;
            }
            if (step.StartsWith("card ", StringComparison.Ordinal))   // карточка правой кнопки над клеткой (MapScreen.Cards.cs)
            {
                // «card 50,30 keep» — карточка остаётся открытой (для снимка --shot).
                var parts = step[5..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var at = ParseCell(parts[0]);
                OpenCard(at);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"card {at.X},{at.Y} {CardLine()}");
                if (parts is not [_, "keep"]) CloseCard();
                continue;
            }
            if (step.StartsWith("drop ", StringComparison.Ordinal))
            {
                // Клетка для мешка («Бросить предмет»): «drop 30,12».
                DropAt(ParseCell(step[5..].Trim()));
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"drop {step[5..].Trim()} панель={(_dropPanel is not null ? LastEdict.Data.Ui.Windows.DropBagPanelLogic.Screen : "-")} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("select ", StringComparison.Ordinal))
            {
                // Выделить отряд по id: «select S073KC0001»; в строке — какая картинка в слоте портрета панели (лицо=, «-» — пусто).
                _map.Select(step[7..].Trim() == "new" ? NewestOwnStack() : step[7..].Trim());
                SyncPanels();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"panel select {step[7..].Trim()} выделен={_map.SelectedStack?.Id ?? "-"} в={(_map.SelectedStack is { } at ? $"{at.X},{at.Y}_{at.Owner}" : "-")} обмен={(_panelView?.State.IsEnabled("BTN_EXCHANGE") == true ? 1 : 0)} портрет={(FaceShown ? 1 : 0)} лицо={(FaceShown ? _faceName : "-")} кадр={FaceRegion} {(FaceShown ? $"{_faceLook?.Line} {FaceStats()}" : "")} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("thief ", StringComparison.Ordinal))
            {
                // Вор у цели без хода: «thief S073KC0076 S073KC0032» — окно вора, как по приходе отряда.
                var ids = step[6..].Trim().Split(' ');
                bool opened = ids.Length == 2 && OpenThief(ids[0], ids[1]);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"ui thief {step[6..].Trim().Replace(' ', '_')} открыто={(opened ? 1 : 0)} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("given", StringComparison.Ordinal))
            {
                // Окно полученного без действия (MapScreen.Given.cs): «given» — предметы первого отряда зрителя с сумкой, «given empty» —
                // пустой мешок, «given spell» — два заклинания правил из библиотеки столицы.
                int given = GivenForCheck(step[5..].Trim());
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"given {step[5..].Trim()} получено_id={given} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("rod ", StringComparison.Ordinal))
            {
                // Режим «Поставить жезл» (BTN_DROP_ROD): «rod 12,34» — мышь над клеткой и щелчок; путь и ход ждут конца шага.
                var at = ParseCell(step[4..].Trim());
                RodHover(at);
                string line = RodLine();
                RodAt(at);
                while (_map.Walking) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"rod {step[4..].Trim()} {line} после: {RodLine()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("pick ", StringComparison.Ordinal))
            {
                // Отряд для обмена (BTN_EXCHANGE): «pick 85,66» — курсор над клеткой и щелчок.
                var at = ParseCell(step[5..].Trim());
                ExchangeHover(at);   // курсор и отметка цели SELECT_YES/NO
                string cursor = ExchangeCursorAt(at) ?? "-", mark = MarksLine(_exchangeMarks);
                ExchangeAt(at);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"pick {step[5..].Trim()} курсор={cursor} отметка={mark} панель={(_exchangePanel is not null ? LastEdict.Data.Ui.Windows.DropBagPanelLogic.Screen : "-")} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("fight ", StringComparison.Ordinal))
            {
                // Бой без доигрыша — для сверки экрана боя со снимком: «fight 32,38 34,38» — отряд из первой клетки против отряда
                // (или укрепления) во второй; бой остаётся на экране (--shot снимет его). «fight 32,38 34,38 1» — вариант фона задан.
                var cells = step[6..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string? From((int X, int Y) c) => _map.Map.Plan.TryGetValue(c, out var ids) ? ids.FirstOrDefault(_map.Map.Stacks.ContainsKey) : null;
                var (a, d) = (ParseCell(cells[0]), ParseCell(cells[1]));
                string? target = From(d) ?? _map.Map.Sites.FirstOrDefault(s => s.X == d.X && s.Y == d.Y)?.Id;
                int? bg = cells.Length > 2 && int.TryParse(cells[2], out int v) ? v : null;
                if (From(a) is { } attacker && target is not null) OpenBattle(attacker, target, bg);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"fight {step[6..].Trim()} бой={(_battle is null ? 0 : 1)} {_battle?.LayoutLine() ?? ""}");
                continue;
            }
            if (step.StartsWith("battle ", StringComparison.Ordinal) && _battle is { } open)
            {
                // Ввод в экран боя: «battle ui click TOG_RIGHTUNITS», «battle hover 10,200» (мышь над точкой поля).
                string input = step[7..].Trim();
                if (input.StartsWith("ui ", StringComparison.Ordinal)) open.Step(input[3..]);
                else if (input == "card") open.Card(open.Session.State.Units.FirstOrDefault(u => u.OnField && u.Attacker == open.PlayerIsAttacker));
                else if (input.StartsWith("hover ", StringComparison.Ordinal) && ParseCell(input[6..].Trim()) is var (hx, hy)) open.Pointer(new Vector2(hx, hy));
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"battle {input.Replace(' ', '_')} {(input == "card" ? open.CardLine() : open.LayoutLine())}");
                continue;
            }
            if (step.StartsWith("diplo ", StringComparison.Ordinal))
            {
                // Окно дипломатии: «diplo 2» — выбрать игрока слота 2 (1 — сам).
                DiplomacySelect(int.Parse(step[6..].Trim(), System.Globalization.CultureInfo.InvariantCulture));
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"diplo {step[6..].Trim()} {DiplomacyLine()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("cap ", StringComparison.Ordinal))
            {
                // Картинка столицы без мыши: «cap 300,200» — наведение на точку картинки, «cap click» — щелчок по ней.
                string arg = step[4..].Trim();
                if (arg == "click") _capitalView?.Click();
                else if (arg.StartsWith("rclick ", StringComparison.Ordinal))
                {
                    var (rx, ry) = ParseCell(arg[7..].Trim());
                    _capitalView?.RightClickAt(new Vector2(rx, ry));
                }
                else
                {
                    var (px, py) = ParseCell(arg);
                    _capitalView?.HoverAt(new Vector2(px, py));
                }
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"cap {arg} {CapitalLine()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("hover ", StringComparison.Ordinal))
            {
                // Прицел заклинания над клеткой без мыши: «hover 30,12» — курсор, область, отметка цели.
                AimAt(ParseCell(step[6..].Trim()));
                GD.Print($"hover {step[6..].Trim()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("aimpress ", StringComparison.Ordinal) || step.StartsWith("aimrelease ", StringComparison.Ordinal))
            {
                // Кнопка мыши прицелу (SpellAim) над клеткой: «aimrelease 73,84» — одно отпускание (хвост двойного щелчка книги), «aimpress» —
                // нажатие и отпускание; клетка под мышью подставляется — у прогона без окна мыши нет.
                bool press = step.StartsWith("aimpress ", StringComparison.Ordinal);
                var aimCell = ParseCell(step[(press ? 9 : 11)..].Trim());
                if (_aimInput is { } aim)
                {
                    var mouse = aim.MouseCell;
                    aim.MouseCell = () => aimCell;
                    // Прямо узлу прицела: событие с места (0, 0) вьюпорта забрал бы интерфейс над ним.
                    if (press) aim._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    aim._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (_aimInput == aim) aim.MouseCell = mouse;
                }
                for (int i = 0; i < 600 && _map.Scrolling; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"{(press ? "aimpress" : "aimrelease")} {aimCell.X},{aimCell.Y} {AimLine()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("target ", StringComparison.Ordinal))
            {
                // Щелчок прицелом заклинания по клетке: «target 30,12».
                Target(ParseCell(step[7..].Trim()));
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                // Литьё ждёт, пока вид доедет к клетке (плавная прокрутка, MapView.Scroll.cs).
                for (int i = 0; i < 600 && _map.Scrolling; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"target {step[7..].Trim()} {AimLine()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("rename ", StringComparison.Ordinal))   // «rename Длинное_имя» — имя лидера выбранного отряда (подчёркивание — пробел)
            {
                string name = step[7..].Trim().Replace('_', ' ');
                string? refused = _map.Selected is { } renamed ? _map.Session.Run<string?>(new LastEdict.Data.Play.Commands.RenameLeaderCommand(renamed, name)) : "нет отряда";
                GD.Print($"rename {(refused ?? "ok").Replace(' ', '_')} имя={name.Replace(' ', '_')}");
                continue;
            }
            if (step.StartsWith("site ", StringComparison.Ordinal))
            {
                // Окно площадки напрямую выбранным отрядом: «site S073SI0003» или по виду — «site merchant».
                string which = step[5..].Trim();
                string siteId = Enum.TryParse<SiteKind>(which, ignoreCase: true, out var kind)
                    ? _map.Map.Sites.FirstOrDefault(s => s.Kind == kind)?.Id ?? which : which;
                string mapCursor = Cursor.Current ?? "-";
                if (_map.Selected is { } chosen) OnSiteVisited(chosen, siteId);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                // Курсор карты до окна и после (окно ставит обычный, MapScreen.Cursor.cs); фон места группы (IMG_PAPERDOLL_BG) — кадр, место,
                // нарисованный размер, непрозрачные точки.
                string background = _dialogViews.Count > 0 ? _dialogViews[^1].SpriteLine("IMG_PAPERDOLL_BG") : "-";
                GD.Print($"site {step[5..].Trim()} {WindowLine()} курсор_карты={mapCursor} курсор={Cursor.Current ?? "-"} фон={background}");
                continue;
            }
            if (step.StartsWith("city ", StringComparison.Ordinal))
            {
                // Окно армии своего укрепления напрямую: «city S073FT0005», «city village» — первая своя деревня.
                OpenCityForCheck(step[5..].Trim());
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"city {step[5..].Trim()} {WindowLine()}");
                continue;
            }
            if (step.StartsWith("ui ", StringComparison.Ordinal) || step.StartsWith("panel ", StringComparison.Ordinal))
            {
                // «ui …» — ввод в верхнее окно (попап события, окно или панель DLG_ISO_PAL, если окон нет): «ui click BTN_OPTIONS», «ui type Z»,
                // «ui press 200,80», «ui release 470,90»; «panel …» — в панель команд (DLG_STRATEGIC или DLG_CAPITAL).
                bool toPanel = step.StartsWith("panel ", StringComparison.Ordinal);
                string input = step[(toPanel ? 6 : 3)..].Trim();
                var top = toPanel ? (_diploPanel ?? _exchangePanel ?? _capitalPanel ?? _panelView!)
                    : _overlay ?? (_dialogViews.Count > 0 ? _dialogViews[^1] : PaletteView!);
                if (toPanel && input.StartsWith("field ", StringComparison.Ordinal) && _diploField is { } diploField)
                {
                    top = diploField;   // «panel field rpress IMG_RACE2» — в поле дипломатии DLG_ISO_DIPLO
                    input = input[6..].Trim();
                }
                bool ok = top.Step(input, out _);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var walkFrom = DateTime.UtcNow;   // «Да» на жезл дальше соседней клетки — отряд идёт: дождаться прихода (MapScreen.Rod.cs)
                while (_map.Walking && (DateTime.UtcNow - walkFrom).TotalSeconds < 30)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_map.AiRunning) await AiTurnDone();   // ответ на предложение в ход ИИ отпустил ход — до конца или до следующего окна
                GD.Print($"{(toPanel ? "panel" : "ui")} {input.Replace(' ', '_')} on={top.Hosted} ok={(ok ? 1 : 0)} " +
                         $"панель={(_diploPanel is not null ? "DLG_DIPLOMACY" : _exchangePanel is not null || _dropPanel is not null ? LastEdict.Data.Ui.Windows.DropBagPanelLogic.Screen : _capitalPanel is null ? "DLG_STRATEGIC" : "DLG_CAPITAL")} {WindowLine()} " +
                         $"записано={(_saving?.Saved is { } saved ? Path.GetFileName(saved).Replace(' ', '_') : "-")} " +
                         $"подсказка={top.TooltipShown?.Replace(' ', '_') ?? "-"} портрет={(FaceShown ? 1 : 0)} " +
                         $"выбран={_map.Selected ?? "-"} укрепление={_map.SelectedFort ?? "-"} жезлов={_map.Map.Rods.Count}");
                if (_leaving) return;   // загрузка или выход: хозяин снимает экран, дальше проверяет новая партия
                continue;
            }
            var cell = ParseCell(step);
            _map.Click(cell.X, cell.Y);
            GD.Print($"click {cell.X},{cell.Y} выбран={_map.Selected ?? "-"} путь={_map.Preview?.Points.Count ?? 0} " +
                     $"дойдёт={_map.Preview?.Reachable ?? 0} идёт={(_map.Walking ? 1 : 0)} бой={(_battle is null ? 0 : 1)} " +
                     $"укрепление={_map.SelectedFort ?? "-"} столица={(CapitalOpen ? 1 : 0)} жезлов={_map.Map.Rods.Count} {WindowLine()}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var waited = DateTime.UtcNow;
            while (_map.Walking && (DateTime.UtcNow - waited).TotalSeconds < 30)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_battle is not null)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _battle?.AutoFinish(OS.GetCmdlineUserArgs().Contains("--paperdoll"));   // --paperdoll — закрыть «Меню героя»
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        while (!_map.GroundReady && (DateTime.UtcNow - start).TotalSeconds < 90)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // Задержка перед итогом (--hold-ms N): экран живёт N мс — для замера кадра (StageHost, --stage-stats) и снимка после анимаций.
        if (int.TryParse(MapSource.Arg(OS.GetCmdlineUserArgs(), "--hold-ms"), out int hold) && hold > 0)
        {
            var held = DateTime.UtcNow;
            while ((DateTime.UtcNow - held).TotalMilliseconds < hold) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // --shot-window — всё окно, как видит игрок (с верхними слоями: подсказки геймпада), а не буфер стенда показа.
        var shotView = OS.GetCmdlineUserArgs().Contains("--shot-window") ? GetTree().Root : GetViewport();
        if (shot is not null) shotView.GetTexture().GetImage().SavePng(shot);
        _mini?.Place();
        // Места подписей экранов (--labels): верх блока строк от верха места — проверка вертикального правила движка (UiTextBlock).
        if (OS.GetCmdlineUserArgs().Contains("--labels"))
            foreach (var screen in FindChildren("*", "", true, false).OfType<UiScreenView>().Where(s => s.IsVisibleInTree()))
                foreach (string line in screen.LabelLines()) GD.Print(line);
        var panel = PanelState();
        GD.Print(LayoutLine());
        GD.Print($"mapscreen map={_map.MapName.Replace(' ', '_')} viewer={_map.Viewer ?? "none"} screens={_screens.Count} " +
                 $"ground_ready={_map.GroundReady} sprites={_map.SpriteCount} minimap={(_mini is null ? 0 : 1)} " +
                 $"лидер={(panel.LeaderName.Length == 0 ? "-" : panel.LeaderName)} ходы={panel.MoveLeft}/{panel.MoveMax} " +
                 $"день={panel.Turn} золото={panel.Bank.Gold} звук={GetTree().Root.GetChildren().Count(n => n is GameAudio)} " +
                 $"фильтр={(int)_map.TextureFilter} фильтр_вьюпорта={(int)_map.GetViewport().CanvasItemDefaultTextureFilter} " +
                 $"ms={(DateTime.UtcNow - start).TotalMilliseconds:F0}" +
                 (shot is null ? "" : $" shot={shot}"));
        GetTree().Quit(_map.GroundReady ? 0 : 1);
    }
}
