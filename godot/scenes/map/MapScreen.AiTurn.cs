using System;
using System.Linq;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using Godot;

namespace LastEdict.Godot;

/// <summary>Ход ИИ на экране карты (<c>docs/ai-screen.md</c>): на время хода панель команд <c>DLG_STRATEGIC</c> подменяется
/// окном ожидания <c>DLG_STRAT_WAIT</c> (<see cref="AiWaitLogic"/>), ввод карты и панелей выключен; бой ИИ с человеком —
/// экран боя поверх карты, человек ведёт свою сторону, журнал уходит обратно в ход ИИ. По концу хода — панель назад,
/// предложения ИИ и окна событий.</summary>
public partial class MapScreen
{
    private UiScreenView? _waitView;
    private AiWaitLogic? _wait;
    private AiWaitState _waitState = AiWaitState.Empty;
    private MapView.AiBattle? _aiBattle;

    /// <summary>Подписка на ход ИИ вида карты (из <see cref="_Ready"/>).</summary>
    private void WireAiTurn()
    {
        ApplySettings(_settings);   // прочитаны в LoadSettings
        GameAudio.Of(this).PlayMap();   // музыка карты: ingame/amb по очереди (docs/ai-screen.md §12)
        _map.AiStarted += OnAiStarted;
        _map.AiPlayer += OnAiPlayer;
        _map.AiBattleRequested += OpenAiBattle;
        // Предложение ИИ человеку в ход ИИ (у движка — сразу, CEffectProposeAlliance 0x43b5c8): окна предложений, ответ человека
        // уходит тем же Answer*Offer; ход ИИ ждёт, пока предложение не уйдёт из Offers (MapView.AiOfferRequest).
        _map.AiOfferRequested += _ => ShowOffers();
        // Попап события человеку у ПК в ход ИИ (у движка — сразу, 0x489e90): окна поверх панели ожидания (MapScreen.Overlays.cs, сессия В),
        // ход ИИ ждёт их закрытия.
        _map.AiPopupRequested += ShowAiPopups;
        _map.AiPopupOpen = () => AiPopupShowing;
        _map.AiTurnFinished += OnAiFinished;
        _map.AiBattleEnded += EndAiBattle;
    }

    /// <summary>Экран боя ИИ, ждущий конца боя модели: он и снимок до конца боя (<see cref="BattleScreen.BeforeEnd"/>).</summary>
    private (BattleScreen Battle, BattleScreen.EndSnapshot Before)? _aiBattleEnding;

    /// <summary>Конец боя модели записан: уход призванных, откат превращений и рост — на ещё открытом экране (решение пользователя 28.09),
    /// потом экран снимается и ход ИИ продолжается.</summary>
    private void EndAiBattle(LastEdict.Rules.Battle.BattleState state, int heard, Action resume)
    {
        if (_aiBattleEnding is not { } ending)
        {
            _map.PlayBattleNotes(state, heard);
            resume();
            return;
        }
        _aiBattleEnding = null;
        GameLog.Verbose($"aibattle ending сообщений={state.Notes.Count - heard}");
        bool resumed = false;
        void Finish()
        {
            if (resumed) return;
            resumed = true;
            try
            {
                // Снятие — отложенно: «Закрыть» приходит из обработчика ввода самого экрана, а TreeExiting — посреди снятия узлов.
                var screen = ending.Battle;
                Callable.From(() =>
                {
                    if (!IsInstanceValid(screen) || screen.GetParent() != this) return;
                    RemoveChild(screen);
                    screen.QueueFree();
                }).CallDeferred();
                if (IsInsideTree()) ShowAiPanels();
            }
            catch (Exception e)
            {
                Failures.Report(this, "MapScreen.EndAiBattle", e);
            }
            finally
            {
                resume();   // ход ИИ продолжается в любом случае
            }
        }
        try
        {
            // Конец боя показан — опыт и «Закрыть» (BattleScreen.ShowEnd); экран снимается и ход ИИ идёт дальше по «Закрыть», как у боя
            // человека (замечание 33 прогона 11.10). Экран сняли иначе — ход ИИ всё равно продолжается.
            ending.Battle.Closed += Finish;
            ending.Battle.TreeExiting += Finish;
            ending.Battle.PlayEnd(ending.Before, _map.UnitRaceType, ending.Battle.ShowEnd);
        }
        catch (Exception e)
        {
            // Исключение в показе конца боя не должно оставить ход ИИ ждать навсегда: экран снимается, ход продолжается.
            Failures.Report(this, "MapScreen.EndAiBattle", e);
            Finish();
        }
    }

    private MapSettings _settings = new();
    private string? _settingsPath;

    /// <summary>Настройки показа — user://settings.json; проверка без --settings их не читает и не пишет (числа не зависят от машины).
    /// Знамёна (<c>TOG_BANNERS</c>) — из них же: включены в прошлый раз — включены и сейчас (ответ пользователя 11.10).</summary>
    private void LoadSettings(string[] args)
    {
        _settingsPath = MapSource.Arg(args, "--settings")
                        ?? (args.Contains("--check") ? null : ProjectSettings.GlobalizePath("user://settings.json"));
        _settings = (_settingsPath is null ? new MapSettings() : MapSettings.Load(_settingsPath)).Clamped();
        MapPalette.Banners = _settings.Banners;
    }

    /// <summary>Записать в файл настроек (если он есть у этого запуска) изменение <paramref name="change"/> поверх прочитанного из файла
    /// (<see cref="MapSettings.Update"/>: поля, которые меняют другие хозяева файла, — режим окна по <c>Alt+Enter</c> и т. п. — не
    /// затираются копией экрана карты); не записались — только до перезапуска.</summary>
    private void SaveSettings(Func<MapSettings, MapSettings> change)
    {
        try
        {
            if (_settingsPath is not null) MapSettings.Update(_settingsPath, change);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"MapScreen: настройки не записаны ({_settingsPath}): {e.Message}");
        }
    }

    /// <summary>Переключатель знамён сменился — в настройки и в файл.</summary>
    private void SaveBanners()
    {
        if (_settings.Banners == MapPalette.Banners) return;
        bool banners = MapPalette.Banners;
        _settings = _settings with { Banners = banners };
        SaveSettings(s => s with { Banners = banners });
        GD.Print($"settings знамёна={(_settings.Banners ? 1 : 0)}");
    }

    /// <summary>Настройки показа — в вид карты: скорость шага своих и чужих отрядов, показ ходов ИИ, громкости звука и музыки. В проверке ход ИИ идёт
    /// быстрее (<see cref="MapView.CheckAiSpeed"/>), но всё равно по клеткам.</summary>
    private void ApplySettings(MapSettings settings, bool preview = false)
    {
        _settings = settings.Clamped();
        PortProbe.Av?.Settings(_settings.PlayerSpeed, _settings.OpponentSpeed, _settings.BattleSpeed, _settings.BattleAnim,
            _settings.StackSmoothScroll, _settings.SmoothScroll, _settings.AnimatedIso);   // журнал зонда: при каких настройках показ
        _map.WalkSpeed = MapSettings.SpeedFactor(_settings.PlayerSpeed);
        _map.AiSpeed = OS.GetCmdlineUserArgs().Contains("--check") ? MapView.CheckAiSpeed : MapSettings.SpeedFactor(_settings.OpponentSpeed);
        _map.AiAnimate = _settings.PlayAiMoves;
        GameAudio.Of(this).Apply(_settings, preview);
        InputBindings.Apply(_settings.Bindings);
        UiScale.Apply(_settings);
        _map.RegionCount = _settings.AudioRegions;
        _map.AnimatedIso = _settings.AnimatedIso;
        _map.Clouds = _settings.IsoClouds;
        _map.ScrollDelayMs = _settings.ScrollSpeed;
        if (_placed) Place();   // масштаб карты — сразу
        _map.StackSmooth = _settings.StackSmoothScroll;
        _map.SmoothScroll = _settings.SmoothScroll;   // «Прокрутка между действиями» (TOG_SMOOTHSCROLL): вид к событию плавно или сразу
    }

    /// <summary>Логика окна настроек в части показа карты: изменение сразу в вид и в файл.</summary>
    private MapOptionsLogic OptionsLogic() => new(_settings, settings =>
    {
        ApplySettings(settings with { Banners = _settings.Banners }, preview: true);   // знамёна — переключатель палитры, не окно настроек
        var window = _settings;
        SaveSettings(onDisk => MapOptionsLogic.Adopt(window, onDisk));   // в файл — только поля окна
        GD.Print($"settings скорость={_settings.PlayerSpeed} противник={_settings.OpponentSpeed} ходы_ИИ={(_settings.PlayAiMoves ? 1 : 0)} " +
                 $"итоги={(_settings.ShowTurnSummary ? 1 : 0)} автосохранение={(_settings.AutoSave ? 1 : 0)} " +
                 $"прокрутка={_settings.ScrollSpeed} за_отрядом={(_settings.StackSmoothScroll ? 1 : 0)} между_действиями={(_settings.SmoothScroll ? 1 : 0)} " +
                 $"фоны={_settings.AudioRegions} " +
                 $"голоса={_settings.Acknowledgements} слабая={(_settings.LowEndVideoCard ? 1 : 0)}");
    }, Text);

    /// <summary>Окно настроек Last Edict в партии (Э5): выбор — сразу в вид и звук, «Применить» — ещё и в файл (весь: окно держит все поля),
    /// знамёна — и в палитру. Язык в партии не меняется (<see cref="LastEdict.Data.Settings.SettingsLogic.LanguageLocked"/>).</summary>
    private LastEdict.Data.Settings.SettingsLogic SettingsLogic() => new(_settings, inGame: true,
        settings => ApplySettings(settings, preview: true),
        settings =>
        {
            ApplySettings(settings);
            MapPalette.Banners = settings.Banners;
            SaveSettings(_ => settings);
            GD.Print($"settings применено противник={_settings.OpponentSpeed} ходы_ИИ={(_settings.PlayAiMoves ? 1 : 0)} " +
                     $"итоги={(_settings.ShowTurnSummary ? 1 : 0)} музыка={_settings.MusicVolume} знамёна={(_settings.Banners ? 1 : 0)}");
        });

    private void OnAiStarted()
    {
        _waitState = AiWaitState.Empty;
        if (_waitView is null)
        {
            var palette = HostSlot("IMG_PALETTE");
            _wait = new AiWaitLogic(() => _waitState);
            _waitView = new UiScreenView
            {
                Hosted = AiWaitLogic.Screen, Name = AiWaitLogic.Screen, SlotAt = new Vector2(palette.X, palette.Y), SlotHost = _host,
                Logic = _wait,
            };
            AddChild(_waitView);
        }
        ShowAiPanels();
        SyncWait();
    }

    /// <summary>Карта видна, ввода нет; вместо панели команд (и портрета её лидера) — окно ожидания.</summary>
    private void ShowAiPanels()
    {
        ShowMap(false, keepVisible: true);
        if (_panelView is not null) _panelView.Visible = false;
        if (_face is not null) _face.Visible = false;
        if (_waitView is not null)
        {
            _waitView.Visible = true;
            _waitView.SetProcessUnhandledInput(false);
        }
        CoverPalette();   // палитра гаснет с панелью ожидания (0x48d858, Зс57)
    }

    private void OnAiPlayer(string playerId)
    {
        var map = _map.Map;
        string? lord = map.Players.TryGetValue(playerId, out var player) && _map.Session.Book is { } book
            ? AiWaitLogic.LordFace(player, book) : null;
        _waitState = new AiWaitState(lord);
        SyncWait();
        GameLog.Verbose($"aiplayer {playerId} лорд={lord ?? "-"}");
    }

    private void SyncWait()
    {
        if (_waitView is null || _wait is null) return;
        _wait.Refresh(_waitView.State);
        _waitView.Sync();
    }

    /// <summary>Бой ИИ с человеком: состояние уже собрано ядром (<see cref="GameSession.StartBattle"/>), экран боя играет его
    /// на генераторе партии, человек — защита. Закрытие отдаёт журнал ходу ИИ, а итог в карту вносит ядро.</summary>
    private void OpenAiBattle(MapView.AiBattle request)
    {
        var session = _map.Session;
        if (_battle is not null || session.Book is null)
        {
            request.Done(AutoLog(request));
            return;
        }
        OpenAiBattleNow(request);
    }

    private void OpenAiBattleNow(MapView.AiBattle request)
    {
        var session = _map.Session;
        var state = request.State;
        if (_battle is not null || session.Book is null)
        {
            request.Done(AutoLog(request));
            return;
        }
        try
        {
            StartAiBattleScreen(request, session, state);
        }
        catch
        {
            // Экран не встал: снять его и вернуть панель ожидания; журнал боя отдаёт вид карты (MapView.BeginGuarded).
            if (_battle is { } broken)
            {
                broken.Closed -= CloseAiBattle;
                broken.EndReached -= CloseAiBattle;
                if (broken.GetParent() == this) RemoveChild(broken);
                broken.QueueFree();
            }
            _battle = null;
            _aiBattle = null;
            _aiBattleEnding = null;
            ShowAiPanels();
            throw;
        }
    }

    private void StartAiBattleScreen(MapView.AiBattle request, GameSession session, LastEdict.Rules.Battle.BattleState state)
    {
        _aiBattle = request;
        ShowMap(false);
        if (_waitView is not null) _waitView.Visible = false;
        // Как у боя человека (OpenBattle): зеркало из настроек, фон — два rand() CRT при открытии экрана, собственные имена лидеров.
        // Сеть: бой по запросу хоста — на копиях его генератора боя и CRT; бой людей — своя сторона та, что не у другого человека.
        _battle = new BattleScreen
        {
            Name = "Battle", Standalone = false, Prepared = state, Rules = session.Book!.Rules, Rng = request.Rng ?? session.BattleRng,   // Book проверен в OpenAiBattle
            PlaceName = MapBattle.PlaceOf(session.Map, state.DefenderGroup), PlayerIsAttacker = request.RemoteSide is { } remote && !remote,
            OwnsAttackerStack = false, RemoteSide = request.RemoteSide, Scripted = _netScript is not null && request.RemoteSide is not null,
            Flipped = _settings.FlippedBattle, OwnName = session.OwnName, BackgroundVariant = BackgroundDraw(request.Crt ?? session.Crt, session.Map, state),
            QueueStrip = _settings.QueueStrip, Wide = _settings.WideBattle,
            CardLogic = BattleCard(),
            LevelMarks = BattleLevelMarks(),
        };
        var screen = _battle;
        request.Resolve = () =>
        {
            screen.Session.Auto = true;
            screen.Session.PlayOut(int.MaxValue);   // автобой без предела раундов, не «Быстрый бой»
            return screen.Session.Log;
        };
        request.Played = () => screen.Session.Played(BackgroundDraws);
        if (request.RemoteSide is not null) _battle.LocalDecision += input => Net?.SendBattleStep(input);
        _battle.Closed += CloseAiBattle;
        _battle.EndReached += CloseAiBattle;   // бой кончился — журнал ядру сразу, конец боя модели и его показ — до «Закрыть»
        AddChild(_battle);
        GameLog.Verbose($"aibattle start attacker={state.AttackerGroup} defender={state.DefenderGroup} units={state.Units.Count} " +
                 $"place={_battle.PlaceName} flip={(_battle.Flipped ? 1 : 0)} bg={_battle.BackgroundVariant}");
        if (OS.GetCmdlineUserArgs().Contains("--check") || _netScript is not null && request.RemoteSide is null)
            Callable.From(() => _battle?.AutoFinish()).CallDeferred();
    }

    /// <summary>Бой без экрана (экран занят) — автобой ядра: журнала нет (null), и <see cref="GameSession"/> играет бой сам
    /// (<see cref="ITurnObserver.Battle"/>). Пустой журнал здесь значил бы «бой сыгран без ходов».</summary>
    private static System.Collections.Generic.IReadOnlyList<LastEdict.Rules.Battle.TurnRecord>? AutoLog(MapView.AiBattle request) =>
        request.Resolve?.Invoke();

    private void CloseAiBattle()
    {
        if (_battle is not { } battle || _aiBattle is not { } request) return;
        _battle = null;
        _aiBattle = null;
        battle.Closed -= CloseAiBattle;
        battle.EndReached -= CloseAiBattle;
        GameLog.Verbose($"aibattle end winner={battle.Session.Winner ?? "-"} turns={battle.Session.Log.Count}");
        // Экран остаётся до конца боя модели и его анимаций (EndAiBattle).
        _aiBattleEnding = (battle, battle.BeforeEnd());
        request.Done(battle.Session.Log, battle.NotesHeard);
    }

    /// <summary>Проверка: дождаться конца хода ИИ (бои в проверке доигрываются сами, попапы событий и окно защитника «горячего стула»
    /// закрываются «ОК», как шагом <c>ok</c>).</summary>
    private async System.Threading.Tasks.Task AiTurnDone()
    {
        var began = DateTime.UtcNow;
        // Ход ИИ кончился — или ждёт ответа человека на предложение (окно открыто, дальше отвечает шаг «ui»).
        while (_map.AiRunning && !_map.AiOfferShowing && (DateTime.UtcNow - began).TotalSeconds < MapView.CheckAiSeconds)
        {
            if (_overlay is { Hosted: "DLG_EVENT_POPUP" } popup)
            {
                GameLog.Verbose($"aipopup ok {popup.Hosted}");
                DismissPopups();
            }
            CloseChats();   // сообщения ИИ (DLG_CHAT_AI) — «Закрыть»
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (_map.AiRunning && !_map.AiOfferShowing)
        {
            GD.PrintErr($"MapScreen: ход ИИ не кончился за {MapView.CheckAiSeconds} с — зависла передача хода?");
            GetTree().Quit(1);
        }
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void OnAiFinished()
    {
        // Окно ожидания к концу хода: портрета лидера и имени нет (движок их в DLG_STRAT_WAIT не ставит, AiWaitLogic, Ин39).
        if (_waitView is not null)
        {
            var w = _waitView.State;
            GameLog.Verbose($"aiwait лидер={w.Frame("IMG_LEADER") ?? "-"} имя={(w.TextOf("TXT_LEADER_NAME") is { Length: > 0 } n ? n : "-")} " +
                     $"лорд={w.Frame("IMG_LORD_FACE") ?? "-"}");
        }
        if (_waitView is not null) _waitView.Visible = false;
        if (_panelView is not null)
        {
            // Панель команд у движка к ходу человека — новая (кнопки включены, конструктор 0x48eb51 гасит жезл, посох, обмен и «Продолжить»):
            // «Конец хода», погашенный щелчком, снова доступен.
            _panelView.Visible = true;
            _panelView.State.SetEnabled(EndTurnButton, true);
            _panelView.RenewButtons();
        }
        ShowMap(true);
        SyncPanels();
        CoverPalette();   // палитра зажигается с панелью команд (0x48fc5e, Зс57)
        ShowOffers();
        if (_map.AiFailed && OS.GetCmdlineUserArgs().Contains("--check")) GetTree().Quit(1);
    }
}
