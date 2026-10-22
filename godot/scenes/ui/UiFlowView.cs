using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>Игра по графу экранов: стек открытых экранов ведёт <see cref="UiNavigator"/> (обычный C#), а здесь
/// каждому экрану стека соответствует свой <see cref="UiScreenView"/>. Ввод получает только верхний экран; экраны
/// под окном (<c>Open</c>) остаются видны. Событие элемента верхнего экрана превращается в переход по графу,
/// и стек узлов подтягивается к стеку навигатора — на следующем кадре, а не изнутри обработчика ввода
/// (узел, который сейчас обрабатывает событие, удалять нельзя).
///
/// <para>Аргументы после <c>--</c>: <c>--start DLG_X</c> — первый экран (по умолчанию главное меню), <c>--lang</c>,
/// <c>--input "click BTN_SINGLE; key 13; …"</c> — сценарий без окна: каждый шаг идёт в верхний экран настоящим
/// вводом через вьюпорт, после шага печатается строка <c>flow step=… events=… go=… stack=… phase=…</c>.
/// <c>--probe ЭЛЕМЕНТ,ЭЛЕМЕНТ</c> — после каждого шага строка <c>probe …</c> с содержимым этих элементов верхнего
/// экрана (текст, кадр, ролик, пункты и выбор списка): им проверяется логика экранов числами. Сценарий, который
/// дошёл до партии (<c>DLG_STRATEGIC</c>), печатает строку <c>game …</c>; с <c>--check</c> дальше проверяет себя экран
/// карты (строка <c>mapscreen …</c>).</para>
///
/// <para>Смена экранов меню играет ролик (<c>data/ui/transitions.json</c>) поверх всего; пока он идёт, ввод не
/// принимается, по окончании (или щелчку/клавише — пропуск) виден новый экран. Окно сообщения (<c>Message</c>
/// перехода) показывает текст своей строки. Экран «подождите» (<c>Via = DLG_WAIT</c>) не показывается: загрузки
/// партии, которую он прикрывает, ещё нет.</para></summary>
public partial class UiFlowView : Node
{
    private readonly List<UiScreenView> _views = new();
    private readonly List<UiEvent> _events = new();
    private readonly List<UiTransition> _transitions = new();

    private UiNavigator _nav = new(new UiFlow(), "-");
    private string _language = "ru";
    private bool _scripted;
    private string[] _probe = [];
    private MenuContext? _menu;
    private VideoStreamPlayer? _transition;
    private string? _message;

    /// <summary>Стек навигатора сменился, узлы ещё не перестроены — ввод старого экрана не принимается.</summary>
    private bool _stale;

    /// <summary>Последний переход — <c>Goto</c>: верхний экран строится заново, даже если имя то же.</summary>
    private bool _replacedTop;

    /// <summary>Каталог своих кампаний меню (каталог меню на другом языке собирается заново, <see cref="SettingsApplied"/>).</summary>
    private string _campaignsDir = "";

    /// <summary>Экраны меню строятся заново целиком (сменился язык строк).</summary>
    private bool _rebuildAll;

    /// <summary>«Применить» окна настроек меню (<see cref="LastEdict.Data.Settings.SettingsLogic"/>): звук, файл настроек (весь — окно держит все
    /// поля), язык — строки и каталог меню заново, экраны меню строятся заново на новом языке. <c>--lang</c> главнее выбора окна.</summary>
    private Action<LastEdict.Data.Map.MapSettings> SettingsApplied(string[] args) => settings =>
    {
        GameAudio.Of(this).Apply(settings);
        InputBindings.Apply(settings.Bindings);
        UiScale.Apply(settings);
        if (SettingsPath(args) is { } path)
        {
            try
            {
                settings.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PrintErr($"UiFlowView: настройки не записаны ({path}): {e.Message}");
            }
        }
        GameData.UseLanguage(Array.IndexOf(args, "--lang") >= 0 ? GameData.LanguageOf(args) : settings.Language);
        GD.Print($"settings применено язык={GameData.Language}");
        if (GameData.Language == _language || _menu is null) return;
        _language = GameData.Language;
        var content = GameData.Content;
        var catalog = content.CatalogIn(_language);
        _menu = _menu with
        {
            Catalog = catalog with { Campaigns = [.. catalog.Campaigns, .. CampaignStore.List(_campaignsDir)] }, Strings = GameData.Strings(_language),
        };
        _menu.Settings = settings;
        _rebuildAll = true;
        Callable.From(Sync).CallDeferred();
    };

    /// <summary>Сколько узлов экранов построено за прогон — по нему видно, что экран пересоздан.</summary>
    private int _built;

    /// <summary>Курсор меню — курсор верхнего экрана из разметки (кинжал <c>_CUDEFAUL</c>, <see cref="UiCursor"/>); в партии курсором
    /// ведает экран карты.</summary>
    private readonly AnimatedCursor _cursor = new() { Name = "Cursor" };

    /// <summary>Экран игры по графу: на нём меню кончается и начинается партия (<see cref="StartGame"/>).</summary>
    private const string GameScreen = "DLG_STRATEGIC";

    private const string MessageBox = "DLG_MESSAGE_BOX";

    /// <summary>Идущая партия — экран карты вместо стека экранов меню.</summary>
    private MapScreen? _game;

    /// <summary>Файл настроек показа (тот же, что у карты, <c>MapScreen.AiTurn.cs</c>); сценарий ввода и проверка его не трогают.</summary>
    private static string? SettingsPath(string[] args) =>
        args.Contains("--check") || args.Contains("--input") ? null : ProjectSettings.GlobalizePath("user://settings.json");

    /// <summary>Изменение в окне настроек меню: громкости — в звук сразу, всё — в файл.</summary>
    private Action<LastEdict.Data.Map.MapSettings> SettingsChanged(string[] args) => settings =>
    {
        GameAudio.Of(this).Apply(settings, preview: true);   // шаг ползунка — проба звука или голоса, как у окна движка
        if (SettingsPath(args) is not { } path) return;
        try
        {
            // В файл — только поля окна: знамёна (переключатель палитры карты), режим окна (Alt+Enter) и прочее меняют другие хозяева
            // файла, и в нём — их последние значения, а не прочитанные при старте меню.
            LastEdict.Data.Map.MapSettings.Update(path, onDisk => LastEdict.Data.Map.MapOptionsLogic.Adopt(settings, onDisk));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"UiFlowView: настройки не записаны ({path}): {e.Message}");
        }
    };

    /// <summary>Показ экранов в окне (<see cref="ScreenStage"/>): экраны, партия и ролики смены живут в его буфере.</summary>
    private ScreenStage _stage = null!;

    /// <summary>Родитель экранов — буфер показа.</summary>
    private Node Host => _stage.Stage;

    /// <summary>Вписывание экрана в окно: <c>--scale sharp|integer|nearest</c>, иначе поле <c>Scaling</c> файла настроек (по умолчанию
    /// чёткий). Сценарий проверки (<c>--check</c>, <c>--input</c>) без ключа — прежний дробный масштаб: числа его строк — в точках окна
    /// 1280×720, а не буфера.</summary>
    private static ScreenScaling ScalingMode(string[] args)
    {
        if (Arg(args, "--scale") is { } named)
        {
            if (ScreenFit.Parse(named) is { } mode) return mode;
            GD.PrintErr($"UiFlowView: --scale {named} — неизвестный режим (sharp, integer, nearest), показ чёткий");
            return ScreenScaling.Sharp;
        }
        if (args.Contains("--check") || args.Contains("--input")) return ScreenScaling.Nearest;
        return SettingsPath(args) is { } path ? LastEdict.Data.Map.MapSettings.Load(path).ScreenScaling : ScreenScaling.Sharp;
    }

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _stage = new ScreenStage { Name = "Stage", Mode = ScalingMode(args) };
        AddChild(_stage);
        AddChild(_cursor);
        _stage.Hub.OnInput = StageInput;
        _stage.Hub.OnUnhandled = StageUnhandledInput;
        if (args.Contains("--input") || args.Contains("--check")) GD.Print($"stage {_stage.Describe()}");
        _language = GameData.Language;
        _probe = (Arg(args, "--probe") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            var content = GameData.Content;
            _menu = new MenuContext(content.CatalogIn(_language), GameData.Strings(_language), new NewGameSetup(),
                                    SystemName()) { SavesDir = SavesDir(args), Screens = content.Screens, LeadersDir = LeadersDir(args),
                                        Book = GameData.Book, ItemTexts = StackTexts.Load(GameData.Rules),
                                        Rules = RulesTexts.Load(GameData.Rules), SettingsChanged = SettingsChanged(args),
                                        SettingsPreview = settings =>
                                        {
                                            GameAudio.Of(this).Apply(settings, preview: true);
                                            InputBindings.Apply(settings.Bindings);
                                            UiScale.Apply(settings);
                                        },
                                        SettingsApplied = SettingsApplied(args) };
            if (SettingsPath(args) is { } settings)
            {
                _menu.Settings = LastEdict.Data.Map.MapSettings.Load(settings);
                InputBindings.Apply(_menu.Settings.Bindings);
                UiScale.Apply(_menu.Settings);
            }
            // Герои игры (Exports\*.ldr) — в каталог героев порта один раз; дальше меню читает только его (docs/day-cycle.md §12).
            int imported = LastEdict.Data.Leaders.LeaderStore.Import(Arg(args, "--ldr") ?? LastEdict.Data.GamePaths.Exports, _menu.LeadersDir);
            if (imported > 0) GD.Print($"leaders imported={imported}");
            // Свои кампании игры (Campaign\*.csg) — в каталог кампаний порта один раз, в каталог меню — вместе с сагами (§15).
            string campaigns = _campaignsDir = CampaignsDir(args);
            int camps = CampaignStore.Import(Arg(args, "--csg") ?? Path.Combine(LastEdict.Data.GamePaths.GameDir, "Campaign"), campaigns,
                                             content.Catalog.Races);
            if (camps > 0) GD.Print($"campaigns imported={camps}");
            _menu = _menu with { Catalog = _menu.Catalog with { Campaigns = [.. _menu.Catalog.Campaigns, .. CampaignStore.List(campaigns)] } };
            // Запуск игры (CIntroAnim, 0x40455e): заставка SF и, если в настройках Intro, ролик INTRO — затем меню; музыка меню — после них.
            // Проверки (--input, --check) начинают с меню; --intro — показ запуска и в проверке.
            string? start = Arg(args, "--start");
            if ((start is null && !args.Contains("--input") && !args.Contains("--check"))
                || (args.Contains("--intro") && (start is null || start == CinematicLogic.Screen)))
            {
                _menu.Cinematic = Cinematic.Startup(_menu.Settings.Intro);
                start ??= CinematicLogic.Screen;
            }
            _nav = new UiNavigator(content.Flow, start ?? "DLG_MAIN_MENU");
            if (_nav.Top != CinematicLogic.Screen) GameAudio.Of(this).PlayMenu();   // музыка меню (сессия Г, docs/ai-screen.md §12)
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException
                                      or System.Text.Json.JsonException or KeyNotFoundException)
        {
            // Необработанное исключение Godot не роняет — без явного выхода игра висела бы с пустым экраном.
            GD.PrintErr($"UiFlowView: данные не читаются — {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
            return;
        }
        Sync();

        if (Arg(args, "--input") is not { } script) return;
        _scripted = true;
        bool ok = Run(script);
        // С --check партия, начатая сценарием, проверяет себя сама (MapScreen ждёт землю, печатает числа и выходит).
        if (!ok || _game is null || !args.Contains("--check")) GetTree().Quit(ok ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        // В сценарии проверки экраны меняет сам сценарий; но загрузку из партии подхватывает кадр.
        if (_scripted && _menu?.Setup.LoadPath is null) return;
        if (_views.Count > 0) Drain(_views[^1]);   // просьбы по времени (ролики вступления уходят по таймеру)
        Sync();
        if (_scripted) return;
        if (_nav.Quit) GetTree().Quit();
    }

    /// <summary>Узлы экранов — по стеку навигатора: общий низ остаётся, выше — снимается и строится заново.
    /// Ввод включён только у верхнего.</summary>
    private void Sync()
    {
        if (_game is not null) return;
        if (_menu?.Setup.LoadPath is { } saved)
        {
            _menu.Setup.LoadPath = null;
            if (!ShowLoadedBriefing(saved))
            {
                StartGame(saved);
                return;
            }
        }
        if (_nav.Top == GameScreen && _menu is not null)
        {
            string? loaded = _loaded;
            _loaded = null;
            StartGame(loaded);
            return;
        }
        foreach (int gone in _windows.Keys.Where(k => k >= _nav.Stack.Count).ToList()) _windows.Remove(gone);
        int keep = 0;
        // Сменился язык (_rebuildAll) — ни одного экрана не оставить: все строятся заново на новом языке.
        while (!_rebuildAll && keep < _views.Count && keep < _nav.Stack.Count && _views[keep].Hosted == _nav.Stack[keep]
               && (!_windows.TryGetValue(keep, out var kept) || ReferenceEquals(_views[keep].Logic, kept))) keep++;
        // Goto заменяет верхний экран, даже если цель — он же («ОК» в окне предмета ведёт в новое окно предмета):
        // экран строится заново, с начальным состоянием, а не остаётся со старым выбором.
        if (_replacedTop) keep = Math.Max(0, Math.Min(keep, _nav.Stack.Count - 1));
        _replacedTop = false;
        _rebuildAll = false;
        _stale = false;
        for (int i = _views.Count - 1; i >= keep; i--)
        {
            // Из дерева — сразу: QueueFree снимет узел только в конце кадра, а сценарий идёт внутри одного кадра,
            // и старый экран ловил бы ввод вместе с новым.
            _views[i].Fired -= OnFired;
            Host.RemoveChild(_views[i]);
            _views[i].QueueFree();
            _views.RemoveAt(i);
        }
        for (int i = keep; i < _nav.Stack.Count; i++)
        {
            string id = _nav.Stack[i];
            var view = new UiScreenView
            {
                Hosted = id, Language = _language, Name = id, Framed = true,
                Logic = _windows.TryGetValue(i, out var window) ? window : _menu is null ? null : MenuLogic.For(id, _menu),
            };
            if (_windows.ContainsKey(i) && Centered(id) is { } at) view.SlotAt = at;
            if (i == _nav.Stack.Count - 1 && _message is { } message) view.Logic = new MessageLogic(_menu!.Text(message));
            view.Fired += OnFired;
            view.Pointed += () => Drain(view);   // правая кнопка окна (карточка, пока держится)
            Host.AddChild(view);
            _views.Add(view);
            _built++;
        }
        _message = null;
        for (int i = 0; i < _views.Count; i++)
            _views[i].SetProcessUnhandledInput(i == _views.Count - 1 && _transition is null);
        // Курсор отвечает верхний экран (0x53ca03 → 0x5308d5); без курсора в разметке (ролик) — прежний (0x5401c7).
        if (_views.Count > 0 && UiCursor.Of(GameData.Content.Screens.FirstOrDefault(s => s.Id == _views[^1].Hosted)) is { } cursor)
            _cursor.ShowUi(cursor);
    }

    /// <summary>Сохранение, выбранное для загрузки: его партия начнётся, когда брифинг уйдёт на карту.</summary>
    private string? _loaded;

    /// <summary>Загруженная партия, как у движка (разбор: фаза игры <c>0x40529f</c> открывает <c>CBriefingInterf</c> и при загрузке;
    /// пользователь 26.09 — «брифинг есть»), сначала показывает брифинг своего сценария за расу человека, чей ход; «Продолжить» —
    /// карта. Сценарий не найден в каталоге — сразу карта (<c>false</c>). Окно «Подождите» не показывается (решение пользователя
    /// 26.09: загрузка мгновенная).</summary>
    private bool ShowLoadedBriefing(string path)
    {
        GameState state;
        try { state = GameSave.Read(path); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return false; }
        var setup = _menu!.Setup;
        if (_menu.Catalog.Scenario(state.Info.ScenarioId) is not { } scenario) return false;
        setup.Scenario = scenario;
        string human = state.ActiveHuman.Length > 0 ? state.ActiveHuman : state.Info.Humans.FirstOrDefault() ?? "";
        if (state.Looks.Map.Players.TryGetValue(human, out var player)
            && _menu.Catalog.Races.FirstOrDefault(r => r.Id.Equals(player.RaceId, StringComparison.OrdinalIgnoreCase)) is { } race)
            setup.RaceId = race.Id;
        _loaded = path;
        _nav = new UiNavigator(GameData.Content.Flow, Briefing, UiPhase.Game);
        _replacedTop = true;
        PlayBriefingMusic();
        GD.Print($"load briefing save={Path.GetFileName(path).Replace(' ', '_')} scenario={scenario.Id.Replace(' ', '_')}");
        // Проверка сценарием: шаги меню уже кончились, «Продолжить» нажать некому — брифинг сразу уходит на карту.
        if (_scripted && OS.GetCmdlineUserArgs().Contains("--check")) _nav = new UiNavigator(GameData.Content.Flow, GameScreen, UiPhase.Game);
        return true;
    }

    private const string Briefing = "DLG_SCENARIO_BRIEFING";

    /// <summary>Меню пройдено: экраны снимаются, партия собирается из выбранного сценария и выбора игрока
    /// (<see cref="NewGame"/>) или читается из сохранения (<paramref name="savePath"/>), и её показывает экран карты.
    /// Сценарий — <c>Exports</c> копии игры (<c>.sg</c> только на входе: дальше партия живёт в модели порта).</summary>
    private void StartGame(string? savePath)
    {
        foreach (var view in _views)
        {
            view.Fired -= OnFired;
            Host.RemoveChild(view);
            view.QueueFree();
        }
        _views.Clear();
        var setup = _menu!.Setup;
        try
        {
            GameSession session;
            // «Random» движка (RandomReseed): новая партия и загрузка — генератор боя от времени, а не одно и то же состояние.
            // Проверки сцен (--check) — без пересева: их прогоны обязаны повторяться.
            uint? seed = OS.GetCmdlineUserArgs().Contains("--check") ? null : (uint)System.Environment.TickCount;
            if (savePath is not null)
            {
                var saved = GameSave.Read(savePath);
                session = new GameSession(seed is { } s ? saved.Reseeded(s) : saved, GameData.Content.MapArt, GameData.Book);
            }
            else
            {
                if (setup.Scenario is not { } scenario) throw new InvalidOperationException("сценарий не выбран");
                // Сценарий из данных порта (data/scenarios и слои модов, импорт tools/ScenarioImport), а не .sg копии игры.
                // Глава саги — по id (S107SC0000): файла игры у неё нет, она собрана из таблиц Scens.
                // Своя кампания — файл главы в user://campaigns (полный путь).
                string path = Path.IsPathRooted(scenario.File) ? scenario.File
                    : GameData.Content.ScenarioPath(scenario.File.Length > 0 ? Path.GetFileNameWithoutExtension(scenario.File) : scenario.Id);
                session = NewGame.Begin(GameData.ReadScenario(path), setup, _menu.Catalog, GameData.Book, GameData.Content.MapArt, reseed: seed);
            }
            var state = session.State;
            // Зритель — человек, чей ход (у «горячего стула» после загрузки — не обязательно первый).
            _game = new MapScreen
            {
                Name = "Game", Session = session, Viewer = state.Info.Humans.Count > 0 ? session.ActiveHuman : null, NewGame = savePath is null,
            };
            _game.LoadRequested += LoadFromGame;
            _game.QuitRequested += QuitGame;
            _game.NextChapterRequested += NextChapter;
            Host.AddChild(_game);
            string from = savePath is null ? "new" : "save";
            if (state.Info.Humans.FirstOrDefault() is not { } humanId || !session.Map.Players.TryGetValue(humanId, out var human))
                GD.Print($"game scenario={state.Info.ScenarioName.Replace(' ', '_')} from={from} human=- day={session.Map.Turn}");
            else
                GD.Print($"game scenario={state.Info.ScenarioName.Replace(' ', '_')} from={from} " +
                         $"human={human.Id} race={human.RaceId} lord={human.LordId} " +
                         $"name={human.Name.Replace(' ', '_')} face={human.Face} difficulty={state.Info.Difficulty} " +
                         $"day={session.Map.Turn} gold={human.Bank.Gold} stacks={session.Map.Stacks.Values.Count(x => x.Owner == human.Id)} " +
                         $"buildings={string.Join(",", human.Buildings.DefaultIfEmpty("-"))}");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            GD.PrintErr($"UiFlowView: партия не начинается — {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }

    /// <summary>В партии выбрано сохранение: текущая партия закрывается, открывается сохранённая (на следующем кадре —
    /// просьба пришла из обработчика ввода экрана, который сейчас будет снят).</summary>
    private void LoadFromGame(string path)
    {
        CloseGame();
        _menu!.Setup.LoadPath = path;
    }

    /// <summary>Выход из партии в главное меню. Победа в последней главе саги — сначала её финальный ролик (<c>CCamp.CONCLUSION</c>,
    /// <c>CScenEndAnim</c> <c>0x404eb7</c>; у эльфов движок сразу выходит в меню — ранг 2).</summary>
    private void QuitGame()
    {
        var session = _game?.Session;
        CloseGame();
        // Общий кэш текстур карты и боя (GfxTextures) — отпустить вслед за снятием партии (отложенный вызов идёт после снятия в CloseGame).
        // Загрузка сохранения и следующая глава кэш оставляют: та же графика, повторное открытие карты не разбирает PNG заново.
        Callable.From(GfxTextures.Release).CallDeferred();
        _nav = new UiNavigator(GameData.Content.Flow, "DLG_MAIN_MENU");
        GameAudio.Of(this).PlayMenu();
        if (session?.State.Info.Campaign is { LastChapter: true, Custom: false } run && session.Won(session.ActiveHuman) == true
            && _menu!.Catalog.Campaigns.FirstOrDefault(c => c.Id == run.CampaignId) is { Conclusion.Length: > 0, Race: not 5 } saga)
        {
            GD.Print($"campaign conclusion video={saga.Conclusion}");
            PlayTransition(saga.Conclusion.ToUpperInvariant());
        }
    }

    /// <summary>Следующая глава кампании (<c>CNextScenarioMsg</c>): раса, лорд и сложность — прежние (<c>0x8379b8</c>), перенос — из
    /// окна переноса; брифинг главы, затем карта.</summary>
    private void NextChapter(CarryOver carry)
    {
        var session = _game?.Session;
        CloseGame();
        var setup = _menu!.Setup;
        if (session?.State.Info.Campaign is not { LastChapter: false } run
            || _menu.Catalog.Campaigns.FirstOrDefault(c => c.Id == run.CampaignId) is not { } saga || run.Chapter + 1 >= saga.Chapters.Count)
        {
            // Окно переноса открывается только не в последней главе — сюда попадаем, лишь если каталог разошёлся с сохранённой главой.
            GD.PrintErr($"UiFlowView: следующей главы нет в каталоге ({session?.State.Info.Campaign}) — перенос не применён, выход в меню");
            _nav = new UiNavigator(GameData.Content.Flow, "DLG_MAIN_MENU");
            return;
        }
        var human = session.Map.Players.GetValueOrDefault(session.ActiveHuman);
        if (human is not null && _menu.Catalog.Races.FirstOrDefault(r => r.Id.Equals(human.RaceId, StringComparison.OrdinalIgnoreCase)) is { } race)
        {
            setup.RaceId = race.Id;
            setup.LordCategory = race.Lords.FirstOrDefault(l => l.Id.Equals(human.LordId, StringComparison.OrdinalIgnoreCase))?.Category ?? setup.LordCategory;
            setup.Face = human.Face;
            setup.LordName = human.Name;
        }
        setup.Difficulty = session.State.Info.Difficulty;
        setup.Seats = [];
        setup.ImportedLeader = null;
        if (saga.Custom && !File.Exists(saga.Chapters[run.Chapter + 1].Scenario.File))
        {
            // Глава своей кампании не перевелась (оборванный файл в .csg) — как у движка при неудачной распаковке главы.
            GD.PrintErr($"UiFlowView: глава {saga.Chapters[run.Chapter + 1].Scenario.Id} своей кампании не читается");
            _nav = new UiNavigator(GameData.Content.Flow, "DLG_MAIN_MENU");
            _nav.Push(MessageBox);
            _message = "X005TA0740";
            return;
        }
        setup.Scenario = saga.Chapters[run.Chapter + 1].Scenario;
        setup.Campaign = run.Next();
        setup.Carry = carry;
        GD.Print($"next chapter campaign={run.CampaignId} chapter={run.Chapter + 1} scenario={setup.Scenario.Id}");
        _nav = new UiNavigator(GameData.Content.Flow, Briefing, UiPhase.Game);
        _replacedTop = true;
        PlayBriefingMusic();
        // Проверка сценарием: шаги меню кончились — брифинг сразу уходит на карту, и кадр сценария строит её сам.
        if (_scripted && OS.GetCmdlineUserArgs().Contains("--check"))
        {
            _nav = new UiNavigator(GameData.Content.Flow, GameScreen, UiPhase.Game);
            Callable.From(Sync).CallDeferred();
        }
    }

    private void CloseGame()
    {
        if (_game is not { } game) return;
        _game = null;
        // Настройки, изменённые в партии (окно DLG_OPTIONS карты пишет файл само), — в меню: иначе окно настроек меню открылось бы с
        // прочитанными при запуске и при первом же изменении записало бы их поверх (пометка 5 прогона 14.10).
        if (_menu is not null && SettingsPath(OS.GetCmdlineUserArgs()) is { } path) _menu.Settings = LastEdict.Data.Map.MapSettings.Load(path);
        game.LoadRequested -= LoadFromGame;
        game.QuitRequested -= QuitGame;
        game.NextChapterRequested -= NextChapter;
        Callable.From(() =>
        {
            Host.RemoveChild(game);
            game.QueueFree();
        }).CallDeferred();
    }

    /// <summary>Каталог своих кампаний: <c>--campaigns</c> (проверки), иначе <c>user://campaigns</c>.</summary>
    public static string CampaignsDir(string[] args) =>
        Arg(args, "--campaigns") ?? ProjectSettings.GlobalizePath("user://campaigns");

    /// <summary>Каталог героев: <c>--leaders</c> (проверки), иначе <c>user://leaders</c>.</summary>
    public static string LeadersDir(string[] args) =>
        Arg(args, "--leaders") ?? ProjectSettings.GlobalizePath("user://leaders");

    /// <summary>Каталог сохранений: <c>--saves</c> (проверки), иначе <c>user://saves</c>.</summary>
    public static string SavesDir(string[] args) =>
        Arg(args, "--saves") is { } dir ? Path.GetFullPath(dir) : ProjectSettings.GlobalizePath("user://saves");

    /// <summary>События одного нажатия верхнего экрана. Как только переход сменил стек, остаток нажатия и всё, что
    /// придёт до перестройки узлов, отбрасывается: оно относится к экрану, которого в стеке уже нет, а навигатор
    /// искал бы его элементы на новом верхнем.</summary>
    private void OnFired(IReadOnlyList<UiEvent> events)
    {
        foreach (var e in events)
        {
            if (_stale) break;
            _events.Add(e);
            // Окно, открытое просьбой логики, ведёт себя само (просьбы ниже), по графу не ходит.
            if (_windows.ContainsKey(_views.Count - 1)) continue;
            if (ElementOf(e) is not { } element) continue;
            if (_views[^1].Logic is { } logic && !logic.Allows(element, _views[^1].State))
            {
                // Отказ со своим окном сообщения (лобби «горячего стула»: X006TA0032, X005TA0344) — поверх экрана.
                if (logic.Refusal(element, _views[^1].State) is not { } refusal) continue;
                _nav.Push(MessageBox);
                _message = refusal;
                _stale = true;
                GD.Print($"flow refusal {_views[^1].Hosted}.{element} message={refusal}");
                return;
            }
            if (_nav.Activate(element) is not { } t) continue;
            Follow(t);
        }
        if (_views.Count > 0) Drain(_views[^1]);
    }

    /// <summary>Музыка брифа выбранного сценария: своя у главы (озвучка саги эльфов, <c>0x48091e</c>) или общая <c>GENBRIEF</c>.</summary>
    private void PlayBriefingMusic() => GameAudio.Of(this).PlayBriefing(_menu?.Catalog.BriefingMusic(_menu.Setup.Scenario?.Id));

    /// <summary>Совершённый переход: перестроить стек, окно сообщения перехода, ролик смены.</summary>
    private void Follow(UiTransition t)
    {
        _transitions.Add(t);
        GD.Print($"flow {t.Action} {t.From}.{t.Element ?? "-"} -> {t.Target ?? "-"} via={t.Via ?? "-"} " +
                 $"phase={t.Phase?.ToString() ?? "-"} message={t.Message ?? "-"}");
        if (t.Action is UiFlowAction.Open or UiFlowAction.Goto or UiFlowAction.Close) _stale = true;
        if (t.Action == UiFlowAction.Goto) _replacedTop = true;
        if (t.Message is not null && t.Action == UiFlowAction.Open) _message = t.Message;
        // «Вступление» из меню: музыка затухает 1500 мс до ролика (0x4eb21a), у ролика свой звук.
        if (t.Target == CinematicLogic.Screen) GameAudio.Of(this).StopMusic(Cinematic.MenuMusicFadeMs);
        if (t.Target == Briefing) PlayBriefingMusic();   // бриф — своя музыка (GENBRIEF или озвучка главы, 0x48091e)
        if (TransitionVideo(t) is { } video) PlayTransition(video);
    }

    /// <summary>Щелчок или клавиша, не доставшиеся элементу: экран с переходом без элемента в графе (титры <c>DLG_CREDITS</c> → главное меню
    /// через <c>DLG_MENU_TRANS</c>, журнал зонда) уходит по нему. Окна поверх так не закрываются.</summary>
    /// <returns>Был ли переход.</returns>
    private bool Dismiss()
    {
        if (_views.Count == 0 || _windows.ContainsKey(_views.Count - 1) || _views[^1].Logic is not IScreenLogic { AnyInputLeaves: true }
            || _nav.EdgesOf(null).Count == 0) return false;
        if (_nav.Activate(null) is not { } t) return false;
        Follow(t);
        return true;
    }

    /// <summary>Ввод, не взятый экранами (узел ввода в буфере показа, <see cref="ScreenStage.Hub"/>).</summary>
    private void StageUnhandledInput(InputEvent @event)
    {
        if (_transition is not null || _game is not null || _scripted) return;
        if (@event is not (InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true })) return;
        if (Dismiss()) _stage.Stage.SetInputAsHandled();
    }

    /// <summary>Логика окон, открытых просьбой логики экрана (<see cref="UiOpen"/>, вопрос, сообщение), — по месту в стеке навигатора.</summary>
    private readonly Dictionary<int, IScreenLogic> _windows = new();

    /// <summary>Просьбы логики окна к меню, как у экрана карты (<c>MapScreen.Drain</c>): вопрос и сообщение — <c>DLG_MESSAGE_BOX</c>,
    /// открыть — окно поверх, закрыть — снять верхнее; окно, снова ставшее верхним, обновляется и отдаёт отложенные просьбы.</summary>
    private void Drain(UiScreenView view)
    {
        if (view.Logic is not IWindowLogic window) return;
        while (window.TakeRequest() is { } request)
        {
            switch (request)
            {
                case UiAsk ask: PushWindow(MessageBoxLogic.Screen, MessageBoxLogic.For(ask)); break;
                case UiTell tell: PushWindow(MessageBoxLogic.Screen, MessageBoxLogic.For(tell)); break;
                case UiOpen open: PushWindow(open.Screen, open.Logic); break;
                // Звук, который решает логика (смена лица лорда — CHNGFACE, только принятое нажатие; сессия Г).
                case UiSound sound: GameAudio.Of(this).PlayFx(LastEdict.Data.Sound.WindowSounds.Archive, sound.Name); break;
                // Экран уходит по переходу без элемента (ролики вступления → главное меню через TRANS_INTRO2MAIN), музыка меню — с ним.
                case UiLeave when _views.Count > 0 && ReferenceEquals(_views[^1], view):
                    if (_nav.Activate(null) is { } leave) Follow(leave);
                    else
                    {
                        // Перехода без элемента в графе нет (мод без ребра) — в меню напрямую, а не чёрный экран навсегда.
                        GD.PrintErr($"UiFlowView: у {_nav.Top} нет перехода без элемента — главное меню");
                        _nav = new UiNavigator(GameData.Content.Flow, "DLG_MAIN_MENU");
                        _stale = true;
                    }
                    GameAudio.Of(this).PlayMenu();
                    return;
                // Узлы перестраиваются позже (Sync), поэтому окно ищется по месту в стеке навигатора, а не по списку узлов.
                case UiClose when _windows.TryGetValue(_nav.Stack.Count - 1, out var top) && ReferenceEquals(top, view.Logic) && _nav.Pop():
                    _windows.Remove(_nav.Stack.Count);
                    _stale = true;
                    int at = _nav.Stack.Count - 1;
                    if (at < 0 || at >= _views.Count) return;
                    var under = _views[at];
                    if (under.Logic is IWindowLogic back)
                    {
                        back.Resume(under.State);
                        under.Sync();
                        Drain(under);
                    }
                    else under.Sync();
                    return;
            }
        }
    }

    private void PushWindow(string screen, IScreenLogic logic)
    {
        _nav.Push(screen);
        _windows[_nav.Stack.Count - 1] = logic;
        _stale = true;
        GD.Print($"flow window {screen}");
    }

    /// <summary>Окно меньше экрана меню — по центру (как окна карты).</summary>
    private static Vector2? Centered(string screen)
    {
        var design = GameData.Content.Screens.FirstOrDefault(s => s.Id == screen)?.Design;
        return design is { } d && (d.Width < 800 || d.Height < 600) ? new Vector2((800 - d.Width) / 2, (600 - d.Height) / 2) : null;
    }

    /// <summary>Какой элемент сработал с точки зрения графа: кнопка и переключатель — сами, в группе — выбранный
    /// участник (в графе стоят переключатели, а не группа), в списке — подтверждение выбора.</summary>
    private static string? ElementOf(UiEvent e) => e switch
    {
        UiClicked c => c.Element,
        UiToggled t => t.Element,
        UiGroupChanged g => g.Member,
        UiListConfirmed l => l.Element,
        _ => null,
    };

    private bool Run(string script)
    {
        var steps = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Цвет полей окна вокруг экрана 800×600 (GameData ставит чёрный) — для проверки числом.
        GD.Print($"flow clear={RenderingServer.GetDefaultClearColor().ToHtml(false)}");
        for (int i = 0; i < steps.Length; i++)
        {
            if (_nav.Quit)
            {
                GD.PrintErr($"UiFlowView: шаг «{steps[i]}» после выхода из игры");
                return false;
            }
            _events.Clear();
            _transitions.Clear();
            if (_game is not null)
            {
                GD.PrintErr($"UiFlowView: шаг «{steps[i]}» после начала партии — ввод карты сценарием меню не идёт");
                return false;
            }
            if (!_views[^1].Step(steps[i], out string at))
            {
                GD.PrintErr($"UiFlowView: шаг «{steps[i]}» не разобран на {_nav.Top}");
                return false;
            }
            Drain(_views[^1]);   // просьбы логики по шагу без событий (таймер роликов вступления — «wait N»)
            if (_events.Count == 0 && _transitions.Count == 0 && steps[i].Split(' ')[0] is "click" or "key") Dismiss();
            Sync();
            string go = _transitions.Count == 0 ? "-" : string.Join(",", _transitions.Select(t => $"{t.Action}:{t.Target ?? "-"}"));
            string stack = _nav.Quit ? "-" : _game is not null ? "GAME" : string.Join(">", _nav.Stack);
            string video = _transition is null ? "-" : (string)_transition.GetMeta("video");
            string place = TransitionRect();
            SkipTransition();
            GD.Print($"flow step={i + 1} do={steps[i].Split(' ')[0]} at={at} " +
                     $"events={List(_events.Select(UiScreenView.Describe))} go={go} stack={stack} phase={_nav.Phase} " +
                     $"quit={(_nav.Quit ? 1 : 0)} built={_built} video={video} vrect={place} cursor={_cursor.Current ?? "-"}");
            if (!_nav.Quit && _game is null) foreach (string id in _probe) Probe(id);
            if (_views.Count > 0 && _views[^1].OverlayLine() is { } overlay) GD.Print($"flow {overlay}");   // окно настроек Last Edict
        }
        return true;
    }

    /// <summary>Ролик смены для перехода: пара экранов из <c>transitions.json</c>, с кодом выбранной расы.</summary>
    private string? TransitionVideo(UiTransition t)
    {
        if (t.Target is null || t.Action is not (UiFlowAction.Goto or UiFlowAction.Open) || _menu is null) return null;
        string race = _menu.Catalog.Races.FirstOrDefault(r => r.Id.Equals(_menu.Setup.RaceId, StringComparison.OrdinalIgnoreCase))?.Code ?? "";
        return GameData.Content.Transitions.VideoFor(t.From, t.Target, race);
    }

    /// <summary>Ролик смены поверх всех экранов, на месте экрана меню 800×600 (как экраны, <see cref="UiScreenView.Letterbox"/>): движок
    /// рисует его по центру экрана (<c>CMenuFullScreenAnim</c>, конструктор <c>0x4da6fa</c>, место <c>0x4da85c</c>), без растяжения;
    /// растворение старого экрана в новый — в самом ролике (кадры <c>TRANS_*</c>). Ввод экранов на это время выключен. Файла нет —
    /// строка в журнал, смена без ролика.</summary>
    private void PlayTransition(string name)
    {
        if (!GameData.Content.VideosByName.TryGetValue(name, out var video)
            || UiVideos.PathOf(video, GameData.VideoRoot) is not { } path || !File.Exists(path))
        {
            GD.PrintErr($"UiFlowView: нет ролика смены {name} (python tools/video-import/build_videos.py)");
            return;
        }
        SkipTransition();
        var layer = new CanvasLayer { Name = "Transition", Layer = 10 };
        _transition = new VideoStreamPlayer
        {
            Stream = new VideoStreamTheora { File = path }, Expand = true, MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _transition.SetMeta("video", name);
        _transitionSize = new Vector2(video.W > 0 ? video.W : MenuSize.X, video.H > 0 ? video.H : MenuSize.Y);
        var player = _transition;
        // Конец своего ролика, а не того, что окажется в поле к моменту сигнала.
        player.Finished += () => { if (_transition == player) SkipTransition(); };
        layer.AddChild(_transition);
        Host.AddChild(layer);
        PlaceTransition();
        _stage.Stage.SizeChanged += PlaceTransition;
        _transition.Play();
        PortProbe.Video(_transition, name);
    }

    /// <summary>Экран меню в точках разметки — по нему вписывается ролик смены (ролики <c>MenuAnim</c> — 800×600, <c>videos.json</c>).</summary>
    private static readonly Vector2 MenuSize = new(800, 600);

    private Vector2 _transitionSize = MenuSize;

    /// <summary>Ролик смены — в место экрана меню: масштаб по меньшей стороне окна, по центру, поля — цвет очистки (чёрный).</summary>
    private void PlaceTransition()
    {
        if (_transition is null) return;
        var (at, k) = UiScreenView.Letterbox(_stage.Stage.GetVisibleRect().Size, MenuSize);
        _transition.Size = _transitionSize * k;
        _transition.Position = at + (((MenuSize - _transitionSize) / 2f) * k);
    }

    /// <summary>Место ролика смены в окне одной строкой (<c>x,y,ш×в</c>) — для строки шага сценария; ролика нет — прочерк.</summary>
    private string TransitionRect() =>
        _transition is null ? "-"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"{_transition.Position.X:0},{_transition.Position.Y:0},{_transition.Size.X:0}x{_transition.Size.Y:0}");

    /// <summary>Ролик смены кончился или пропущен: слой убирается, ввод возвращается верхнему экрану.</summary>
    private void SkipTransition()
    {
        if (_transition is null) return;
        _stage.Stage.SizeChanged -= PlaceTransition;
        // Из дерева — сразу, как и снятые экраны: до конца кадра слой ролика перехватывал бы щелчки нового экрана.
        var layer = _transition.GetParent();
        Host.RemoveChild(layer);
        layer.QueueFree();
        _transition = null;
        if (_views.Count > 0) _views[^1].SetProcessUnhandledInput(true);
    }

    /// <summary>Ввод буфера показа до экранов не доходит раньше них: узел ввода — первый в буфере (<see cref="ScreenStage.Hub"/>).</summary>
    private void StageInput(InputEvent @event)
    {
        // Щелчок или клавиша во время ролика смены — пропуск, как в игре.
        if (_transition is null || @event is not (InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true })) return;
        SkipTransition();
        _stage.Stage.SetInputAsHandled();
    }

    /// <summary>Содержимое элемента верхнего экрана одной строкой — для проверок без окна.</summary>
    private void Probe(string id)
    {
        var ui = _views[^1].State;
        if (id == "UNDERLAY")   // подложка под фоном (UiScreenState.Underlay): картинка расы под рамкой выбора
        {
            GD.Print($"probe screen={ui.Screen.Id} id={id} text=- frame={ui.Underlay ?? "-"} video=- items=-");
            return;
        }
        if (!ui.Screen.Elements.Any(e => e.Id == id) && id != UiScreenState.BackgroundId) return;
        string Clean(string? s) => s is null ? "-" : s.Replace(' ', '_').Replace('\n', '/');
        string items = "-", video = ui.VideoOf(id) is { } v ? $"{v.Name}#{v.Run}" : "-";
        var element = ui.Screen.Elements.FirstOrDefault(e => e.Id == id);
        if (element?.Kind is UiKind.List or UiKind.TextList)
            items = $"{ui.Items(id).Count}@{ui.ListSelected(id)}";
        string frame = id == UiScreenState.BackgroundId ? (ui.Background.Length > 0 ? ui.Background : "-") : element?.Kind is UiKind.Image or UiKind.Button or UiKind.Toggle ? ui.Frame(id) ?? "-" : "-";
        string text = element?.Kind is UiKind.Text or UiKind.Edit or UiKind.Spin ? Clean(ui.TextOf(id)) : "-";
        GD.Print($"probe screen={ui.Screen.Id} id={id} text={text} frame={frame} video={video} items={items}");
    }

    private static string SystemName()
    {
        string name = OS.GetEnvironment("USERNAME");
        return name.Length == 0 ? OS.GetEnvironment("USER") : name;
    }


    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "-" : string.Join(",", list);
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
