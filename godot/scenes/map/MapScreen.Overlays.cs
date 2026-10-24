using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>Окна поверх карты, которые открывает сама партия, а не кнопка: попапы событий сценария (<c>DLG_EVENT_POPUP</c>)
/// по одному в порядке срабатывания, а при конце партии — окно сообщения (<c>DLG_MESSAGE_BOX</c>) и эпилог
/// (<c>DLG_END_WIN_LOSE_</c>), после которого партия уходит в главное меню (<c>docs/scenario-events.md</c> §4).</summary>
public partial class MapScreen
{
    /// <summary>Очередь окон: экран, его логика и кнопки, которые его закрывают (через запятую).</summary>
    private readonly Queue<(string Screen, IScreenLogic Logic, string Close)> _overlayQueue = new();
    private UiScreenView? _overlay;
    private string _overlayClose = "";
    private bool _endQueued;

    /// <summary>Ответы окон-вопросов очереди: логика окна → что делать по нажатой кнопке (после закрытия окна).</summary>
    private readonly Dictionary<IScreenLogic, Action<string>> _overlayAnswers = new();

    /// <summary>Выбывшие игроки, о чьём покорении уже сказано (<c>X005TA0775</c>); null — ещё не собраны (при загрузке старые не
    /// объявляются).</summary>
    private HashSet<string>? _conquered;

    /// <summary>Партия только что начата (не загружена): в первый ход каждого человека — окно имени его героя (<see cref="AskLeaderName"/>).</summary>
    public bool NewGame { get; set; }

    /// <summary>Люди, которым окно имени героя первого хода уже показано.</summary>
    private readonly HashSet<string> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Первый ход человека в новой партии (<c>0x4d8bd0</c> → <c>CChangeFirstLeaderNameInterf</c>, в журнале <c>DLG_GETINFO_BOX</c>
    /// сразу за началом хода): окно имени героя его столицы с именем по умолчанию (<see cref="NewGame"/> выбрал его из реже всего
    /// носимых), кнопка переименовывает. После окон начала хода и до его конца; одному человеку — один раз.</summary>
    private void AskLeaderName()
    {
        var session = _map.Session;
        if (!LeaderNameDue(out string viewer, out var hero) || _map.AiRunning || BeginTurnPending || Windows is not { } context) return;
        _named.Add(viewer);
        GD.Print($"first leader name human={viewer} default={hero.Name.Replace(' ', '_')}");
        OpenWindow(LeaderNameLogic.Screen, new LeaderNameLogic(context, hero.LeaderType, hero.Name, name => session.Run<string?>(new LastEdict.Data.Play.Commands.RenameLeaderCommand(hero.StackId, name))));
    }

    /// <summary>Окно имени героя этому человеку ещё впереди (условия <see cref="AskLeaderName"/>) — попапы событий его ждут: у движка окно имени
    /// — продолжение окна начала хода (<c>0x4d8bd0</c>), а попапы — следующие команды очереди (журнал 20261016g, карта 9: <c>DLG_BEGIN_TURN</c>
    /// 3207023 → <c>DLG_GETINFO_BOX</c> 3207029 → <c>DLG_EVENT_POPUP</c> 3207048).</summary>
    private bool LeaderNameDue(out string viewer, out (string StackId, string LeaderType, string Name) hero)
    {
        var session = _map.Session;
        (viewer, hero) = ("", default);
        // Глава кампании после первой: герой перенесён со своим именем — окна нет (ранг 2: условие 0x4d8bd0 по кампании не разобрано).
        if (!NewGame || _leaving || Windows is null || session.Map.Turn != 1 || session.State.Info.Campaign is { Chapter: > 0 } || _map.Viewer is not { } who
            || !session.State.Info.Humans.Contains(who, StringComparer.OrdinalIgnoreCase) || _named.Contains(who) || _summaryWatches.ContainsKey(who)
            || session.FirstLeader(who) is not { } found) return false;
        (viewer, hero) = (who, found);
        return true;
    }

    /// <summary>Окно имени открыто или ещё впереди.</summary>
    private bool LeaderNamePending => _logics.Any(l => l is LeaderNameLogic) || LeaderNameDue(out _, out _);

    /// <summary>Для какого человека и дня уже поставлено окно начала хода (<see cref="AnnounceTurn"/>); null — ещё ни для кого.</summary>
    private (string Human, int Day)? _turnAnnounced;

    /// <summary>Окно начала хода открыто, ждёт в очереди или ещё не поставлено (ход человека начался, а открытое окно держит очередь).</summary>
    private bool BeginTurnPending =>
        _overlay?.Logic is BeginTurnLogic || _overlayQueue.Any(o => o.Logic is BeginTurnLogic) || BeginTurnDue(out _, out _);

    /// <summary>Окно начала хода зрителю этого дня ещё не поставлено, но будет (<see cref="AnnounceTurn"/>): человек, ход ИИ кончился, партия
    /// идёт, доход начала хода есть (после загрузки его нет — окна нет). Первый вызов после загрузки день помечает без окна.</summary>
    private bool BeginTurnDue(out string viewer, out LastEdict.Rules.Map.Bank income)
    {
        var session = _map.Session;
        (viewer, income) = ("", LastEdict.Rules.Map.Bank.Zero);
        if (_leaving || _map.AiRunning || session.Over || _map.Viewer is not { } who
            || !session.State.Info.Humans.Contains(who, StringComparer.OrdinalIgnoreCase)) return false;
        var key = (who, session.Map.Turn);
        if (_turnAnnounced == key) return false;
        if ((_turnAnnounced is null && !NewGame) || session.TurnIncome(who) is not { } found)
        {
            _turnAnnounced = key;
            return false;
        }
        (viewer, income) = (who, found);
        return true;
    }

    /// <summary>
    /// Окно начала хода (<see cref="BeginTurnLogic"/>, <c>DLG_BEGIN_TURN</c>): каждому человеку в начале его хода — «Начинается N день» и доход
    /// этого начала хода (<see cref="GameSession.TurnIncome"/>), раз на человека и день, первым из окон хода. Загруженная партия окна не показывает до следующего
    /// хода — у неё сообщение «Повтор миссии» (<see cref="ResumeMessage"/>, <c>CCmdTurnInfoMsg</c> с «загрузка» = 1). Прежде у порта этого окна
    /// не было вовсе, а в день 1 попапы событий шли раньше окна имени (разбор партий 17.10, карта 9).
    /// </summary>
    private void AnnounceTurn(GameSession session)
    {
        // Открытое окно (ответ ИИ на предложение, окно площадки) держит очередь команд клиента до закрытия (0x4d97b6): окно начала хода — после.
        if (!BeginTurnDue(out string viewer, out var income) || _logics.Count > 0) return;
        _turnAnnounced = (viewer, session.Map.Turn);
        _overlayQueue.Enqueue((BeginTurnLogic.Screen, new BeginTurnLogic(session.Map.Turn, income, Text), "BTN_OK"));
        GD.Print($"begin turn human={viewer} day={session.Map.Turn} доход={income.Gold}/{income.Infernal}/{income.Life}/{income.Death}/" +
                 $"{income.Runic}/{income.Grove}");
    }

    /// <summary>Победа в главе кампании, которая не последняя: окно переноса отдало героя и вещи — хозяин начинает следующую главу.</summary>
    public event Action<CarryOver>? NextChapterRequested;

    /// <summary>Окно продолжения загруженной партии (<see cref="ResumeMessage"/>): уже поставлено в очередь; и его логика — для шага
    /// проверки <c>ok</c>.</summary>
    private bool _resumeQueued;
    private IScreenLogic? _resumeLogic;

    /// <summary>
    /// Загруженная партия (хозяин есть, партия не новая): сервер движка при загрузке начала хода не проводит (<c>0x421667</c> →
    /// <c>0x4218a0</c>: только <c>CCmdBeginTurnMsg</c> и <c>CCmdTurnInfoMsg</c> с «загрузка» = 1), и клиент вместо окна начала хода
    /// показывает сообщение (<c>CTaskBeginTurn</c> <c>0x4d881a</c> → <c>0x4d8950</c>): «Повтор миссии с дня N» (<c>X005TA0079</c>), в
    /// партии нескольких людей (флаг сетевой игры, ранг 2) — <c>X005TA0828</c>. Окон навыка лидера, итогов хода,
    /// попапов и имени героя после загрузки у движка нет — до следующего начала хода (ранг 1; журналы 20260920-gaps, -legions,
    /// 20260928-shots: брифинг → карта → это окно).
    /// </summary>
    private void ResumeMessage(GameSession session)
    {
        if (_resumeQueued || Session is null || NewGame || session.Over) return;
        _resumeQueued = true;
        string text = Text(session.SeveralHumans ? "X005TA0828" : "X005TA0079").Replace("%NUMBER%", session.Map.Turn.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        _resumeLogic = new MessageLogic(text, okOnly: true);
        _overlayQueue.Enqueue(("DLG_MESSAGE_BOX", _resumeLogic, "BTN_OK,BTN_YES,BTN_NO"));
        GD.Print($"resume message day={session.Map.Turn} людей={(session.SeveralHumans ? 2 : 1)}");
    }

    /// <summary>Окна поверх карты: выложить новые попапы и конец партии в очередь и показать первое, если ничего не открыто.</summary>
    private void PumpOverlays()
    {
        if (_battle is not null || _leaving) return;
        var session = _map.Session;
        WatchGiven();   // окна полученных предметов (MapScreen.Given.cs)
        WatchNotices();   // выделение отряда, шпион, иллюзия, взятые ресурсы (MapScreen.Notices.cs)
        WatchSummaries();   // итоги хода в сохранении v9 (MapScreen.Windows.cs)
        ResumeMessage(session);
        AnnounceTurn(session);
        AnnounceConquests(session);
        // За «Слава победителю» (команда 0x34) — окно взятых ресурсов (0x35, 0x434d61); шпион и иллюзия — раньше попапов событий.
        PumpNotices(messagesOnly: _map.AiRunning);
        QueueUpgrades();
        // Окно имени первого хода — раньше окон выбора навыка (журналы: DLG_GETINFO_BOX → DLG_UPGRADE_LEADER).
        if (_overlay is null && _overlayQueue.Count == 0) AskLeaderName();
        if (_overlay is null && _overlayQueue.Count == 0) AskUpgrade();
        if (_overlay is null && _overlayQueue.Count == 0 && !UpgradesPending) AskGiven();
        // Попапы событий — после окна начала хода и окна имени (0x4d8bd0), окон выбора навыка (журналы: DLG_UPGRADE_LEADER →
        // DLG_EVENT_POPUP, обратного перехода нет) и окон полученных предметов (сообщение 44 — сразу за подбором, эффекты событий — после);
        // события действия вора — после закрытия окна его итога (ThiefResultOpen).
        if (_map.Viewer is { } viewer && !_map.AiRunning && !UpgradesPending && !GivenPending && !BeginTurnPending && !LeaderNamePending
            && !ThiefResultOpen)
            foreach (var m in session.Run<IReadOnlyList<EventMessage>>(new LastEdict.Data.Play.Commands.TakeMessagesCommand(viewer)))
            {
                _overlayQueue.Enqueue(("DLG_EVENT_POPUP", new EventPopupLogic(m, Windows is { } w ? w.UnitName : null), "BTN_RIGHTSIDE"));
                GD.Print($"event popup {m.EventId} for={m.PlayerId} day={m.Day} text={PopupBrief(m.Popup.Text)}");
            }
        if (session.Over && !_endQueued)
        {
            _endQueued = true;
            string viewerId = _map.Viewer ?? "";
            string message = GameEnd.Message(session.State.Info, viewerId, session.Map, GameData.Catalog, Text) ?? "";
            if (session.ExportOffered(viewerId)) AskExport(message);
            else
            {
                // Сообщение — одна «ОК» (yesNo = 0); закрывает любая кнопка.
                _overlayQueue.Enqueue(("DLG_MESSAGE_BOX", new MessageLogic(message, okOnly: true), "BTN_OK,BTN_YES,BTN_NO"));
                QueueEpilogue();
            }
            GD.Print($"game over winner={session.State.Info.Winner} viewer={viewerId} won={(session.Won(viewerId) == true ? 1 : 0)}");
        }
        if (_overlay is null && _overlayQueue.Count > 0) ShowOverlay();
        else if (_overlay is null)
        {
            AskLeaderName();
            AskUpgrade();
            AskGiven();
            OpenEventBattle();
        }
        ReleaseHeldSelection();
    }

    /// <summary>Эллипс выделения, снятый ходом своего отряда (<see cref="MapView.SelectionHeld"/>, Ин49), — назад, когда попапов событий этого
    /// хода нет ни на экране, ни в очереди, ни у партии (ждут окна навыка или итога вора).</summary>
    private void ReleaseHeldSelection()
    {
        if (!_map.SelectionHeld || _map.Walking || _overlay?.Logic is EventPopupLogic || _overlayQueue.Any(o => o.Logic is EventPopupLogic)
            || _map.SelectionPopupsPending)
            return;
        _map.ReleaseSelection();
    }

    /// <summary>
    /// Попап события человеку у ПК в ходе ИИ (<see cref="TurnSightKind.EventPopup"/> с рабочего потока хода ИИ; заявку ставит и держит
    /// показ хода ИИ — сессия Г, <c>MapView.AiTurn.cs</c>): движок показывает его сразу (<c>0x489e90</c>) поверх панели <c>DLG_STRAT_WAIT</c>,
    /// очередь команд клиента стоит до «ОК» (<c>0x4bd98a</c>). Зовётся с главного потока, пока рабочий ждёт; ход ИИ продолжается, когда
    /// <see cref="AiPopupShowing"/> ложно. Попапы уходят из <see cref="GameState.Messages"/> — в начале хода человека их уже нет.
    /// </summary>
    private void ShowAiPopups()
    {
        if (_leaving || _battle is not null || _map.Viewer is not { } viewer || !_map.AiPopupWaits) return;
        PumpNotices(messagesOnly: true);   // раскрытый шпион, гибель иллюзии — тоже сразу (0x489f16, 0x489840)
        foreach (var m in _map.Session.Run<IReadOnlyList<EventMessage>>(new LastEdict.Data.Play.Commands.TakeMessagesCommand(viewer)))
        {
            _overlayQueue.Enqueue(("DLG_EVENT_POPUP", new EventPopupLogic(m, Windows is { } w ? w.UnitName : null), "BTN_RIGHTSIDE"));
            GD.Print($"event popup {m.EventId} for={m.PlayerId} day={m.Day} ai=1 text={PopupBrief(m.Popup.Text)}");
        }
        if (_overlay is null && _overlayQueue.Count > 0) ShowOverlay();
    }

    /// <summary>Начало текста попапа одним словом — для проверок без окна (язык сценария: <c>tools/ui-render-check.py</c>).</summary>
    private static string PopupBrief(string text)
    {
        string flat = text.Replace("\r", "").Replace('\n', '/').Replace(' ', '_');
        return flat.Length > 40 ? flat[..40] : flat;
    }

    /// <summary>Попап события или окно сообщения ИИ (<c>DLG_CHAT_AI</c>) открыто, ждёт в очереди окон или ещё у сборщика (реплика на ответ
    /// человека в окне предложения приходит до того, как окно закрылось и очередь разобрана): показ хода ИИ стоит (<see cref="ShowAiPopups"/>).</summary>
    private bool AiPopupShowing => _overlay?.Logic is EventPopupLogic or NoticeLogic
                                   || _overlayQueue.Any(o => o.Logic is EventPopupLogic or NoticeLogic)
                                   || _logics.Any(l => l is ChatLogic)
                                   || (_map.Viewer is { } viewer && _noticeWatch?.HasMessages(viewer) == true);

    /// <summary>Окно итога вора (<c>DLG_ACTION_RESULT</c>) открыто: попапы событий, сработавших от действия (<c>0x41e6e6</c>), ждут его
    /// закрытия (Ин42). У движка окно итога модально, и сообщение события клиент берёт после «Да»: журнал <c>20261016g-port-synth</c>, карта 8,
    /// K2 — <c>DLG_ACTION_RESULT</c> seq 1415653, «Да» 1415660, закрытие 1415674, затем <c>DLG_EVENT_POPUP</c> 1415724. Порт до 18.10
    /// открывал попап в том же кадре поверх итога (<c>port-20261017-134341</c> seq 15887/15888).</summary>
    private bool ThiefResultOpen => _logics.Any(l => l is ThiefResultLogic);

    /// <summary>Для какого человека и дня собрана очередь окон выбора навыка (<see cref="QueueUpgrades"/>); null — ещё ни для кого.</summary>
    private (string Human, int Day)? _upgradesFor;

    /// <summary>Отряды, которым ещё откроется окно выбора навыка в этот ход (отряд — столько раз, сколько у него выборов).</summary>
    private readonly Queue<string> _upgradeQueue = new();

    private SkillTexts? _skillTexts;

    /// <summary>Окна выбора навыка ещё впереди или одно открыто: попапы событий ждут.</summary>
    private bool UpgradesPending => _upgradeQueue.Count > 0 || _logics.Any(l => l is UpgradeLeaderLogic);

    /// <summary>Начало хода человека-зрителя (фаза <c>0x4d9f71</c> → очередь <c>0x5edbf7</c>, <see cref="UpgradeLeaderQueue"/>): собрать
    /// очередь окон выбора навыка — раз на человека и день. Повышение посреди хода окна не даёт до следующего хода (у движка очередь
    /// собирается только в начале хода). Загруженная партия окон не показывает до следующего хода (ранг 1: <c>CCmdUpgradeLeadersMsg</c>
    /// шлёт только <c>CEffectPlayerTurn</c> <c>0x43acc6</c> обычного начала хода, загрузка его не проводит); новая — с первого хода.</summary>
    private void QueueUpgrades()
    {
        var session = _map.Session;
        if (_leaving || _map.AiRunning || _map.Viewer is not { } viewer || session.Book is null) return;
        var key = (viewer, session.Map.Turn);
        bool first = _upgradesFor is null;
        if (_upgradesFor == key) return;
        _upgradesFor = key;
        _upgradeQueue.Clear();
        if ((first && !NewGame) || !session.State.Info.Humans.Contains(viewer, StringComparer.OrdinalIgnoreCase)) return;
        foreach (string stackId in UpgradeLeaderQueue.For(session, viewer)) _upgradeQueue.Enqueue(stackId);
        if (_upgradeQueue.Count > 0) GD.Print($"upgrade queue human={viewer} day={session.Map.Turn} stacks={string.Join(",", _upgradeQueue)}");
    }

    /// <summary>Следующее окно выбора навыка (<see cref="UpgradeLeaderLogic"/>), когда других окон нет: по одному, в порядке очереди.
    /// Пустое предложение окна не отменяет (очередь движка его не проверяет, <see cref="UpgradeLeaderQueue.Window"/>); пропускается только
    /// отряд без живого лидера.</summary>
    private void AskUpgrade()
    {
        if (_leaving || _map.AiRunning || _battle is not null || _logics.Count > 0 || _dialogsStale || Windows is not { } context) return;
        if (_upgradeQueue.Count == 0) return;
        var texts = _skillTexts ??= SkillTexts.Load(GameData.Rules);
        while (_upgradeQueue.Count > 0)
        {
            string stackId = _upgradeQueue.Dequeue();
            if (UpgradeLeaderQueue.Window(context, stackId, texts) is not { } open) continue;
            GD.Print($"upgrade leader stack={stackId} pending={_map.Map.Stacks[stackId].UpgradeCount} offer={UpgradeLeaderQueue.Offer(_map.Session, stackId).Count}");
            OpenWindow(open.Screen, open.Logic);
            return;
        }
        // Очередь кончилась без окна (лидеров уже нет) — попапы событий, которые ждали окон, выложить сейчас.
        Callable.From(PumpOverlays).CallDeferred();
    }

    /// <summary>Проверка: окна выбора навыка этого хода — «ОК» по первой строке, пока очередь не кончится (шаги <c>ok</c>, <c>end</c>); окно
    /// имени первого хода над ними — принять. Потом — кадры, за которые выкладываются ждавшие попапы событий. Нечего выбирать — ни одного
    /// кадра ожидания.</summary>
    private async System.Threading.Tasks.Task AcceptUpgrades()
    {
        if (!UpgradesPending) return;
        for (int i = 0; i < 64 && UpgradesPending; i++)
        {
            AcceptLeaderName();
            if (_dialogViews.Count > 0 && _dialogViews[^1] is { Logic: UpgradeLeaderLogic logic } view)
            {
                view.Step("click BTN_OK", out _);
                GD.Print($"upgrade accept {logic.Summary} отложено={(_map.Map.Stacks.TryGetValue(logic.StackId, out var s) ? s.UpgradeCount : -1)}");
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Бой эффекта <c>L_BATTLE</c> в ход человека (<see cref="GameSession.PendingEventBattle"/>) — после окон событий того
    /// же действия: экран боя на собранном ядром состоянии, как бой ИИ с человеком; «Закрыть» — обычный <see cref="CloseBattle"/>
    /// (итог и проверка после боя — <see cref="GameSession.FinishBattle"/>).</summary>
    private void OpenEventBattle()
    {
        var session = _map.Session;
        if (_battle is not null || _leaving || session.Book is null || !session.EventBattleWaiting
            || session.Execute(new LastEdict.Data.Play.Commands.NextEventBattleCommand()).Value is not LastEdict.Rules.Battle.BattleState state) return;
        bool playerAttacks = _map.Viewer is { } viewer && session.Map.Stacks.TryGetValue(state.AttackerGroup, out var attacker)
                             && attacker.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase);
        ShowMap(false);
        _battle = new BattleScreen
        {
            Name = "Battle", Standalone = false, Prepared = state, Rules = session.Book.Rules, Rng = session.BattleRng,
            PlaceName = MapBattle.PlaceOf(session.Map, state.DefenderGroup), PlayerIsAttacker = playerAttacks,
            OwnsAttackerStack = MapBattle.OwnsAttackerStack(session.Map, state.AttackerGroup, _map.Viewer),   // «Меню героя» (0x4d77b8)
            OwnName = session.OwnName,   // подпись крупного портрета — как у боя с карты
            QueueStrip = _settings.QueueStrip, Wide = _settings.WideBattle,
            CardLogic = BattleCard(),
            LevelMarks = BattleLevelMarks(),
        };
        _battleCrtDraws = 0;   // у боя эффекта вариант фона не тянется (BackgroundDraw) — rand() экран не берёт
        _battle.Closed += CloseBattle;
        _battle.EndReached += EndBattleOnScreen;
        AddChild(_battle);
        GD.Print($"eventbattle start attacker={state.AttackerGroup} defender={state.DefenderGroup} units={state.Units.Count} " +
                 $"place={_battle.PlaceName}");
        if (!playerAttacks) _battle.Continue();   // нападает сторона события — первыми могут ходить её юниты
    }

    /// <summary>Эпилог (<c>DLG_END_WIN_LOSE_</c>) — последнее окно партии, после него главное меню.</summary>
    private void QueueEpilogue()
    {
        var session = _map.Session;
        var scenario = GameData.Catalog.Scenario(session.State.Info.ScenarioId);
        _overlayQueue.Enqueue(("DLG_END_WIN_LOSE_", new EndScenarioLogic(session.Won(_map.Viewer ?? "") == true, scenario, Text), "BTN_CLOSE"));
    }

    /// <summary>Победа с героями, которых можно вывезти (<c>CCmdExportLeaderMsg</c>, <c>0x489a63</c>): вместо сообщения о победе —
    /// вопрос <c>X005TA0894</c> (<c>%WIN%</c> — то сообщение). «Да» — окно вывоза (<see cref="LeaderPickerLogic"/>), после него эпилог;
    /// «Нет» — сразу эпилог.</summary>
    private void AskExport(string message)
    {
        var ask = MessageBoxLogic.For(new UiAsk(Text("X005TA0894").Replace("%WIN%", message, StringComparison.Ordinal), () => { }));
        _overlayQueue.Enqueue((MessageBoxLogic.Screen, ask, "BTN_YES,BTN_NO"));
        _overlayAnswers[ask] = button =>
        {
            GD.Print($"export ask answer={button}");
            if (button != "BTN_YES" || Windows is not { } context)
            {
                EpilogueNow();
                return;
            }
            var texts = _stackTexts ??= StackTexts.Load(GameData.Rules);
            OpenWindow(LeaderPickerLogic.Screen, new LeaderPickerLogic(context, UiFlowView.LeadersDir(OS.GetCmdlineUserArgs()),
                () => Callable.From(EpilogueNow).CallDeferred(),
                id => new ManageStackLogic(context, id, GameData.Content.Screens, texts)));
        };
    }

    private void EpilogueNow()
    {
        QueueEpilogue();
        if (_overlay is null) ShowOverlay();
    }

    /// <summary>Выбывший человек в свой ход (команда <c>0x16</c> → <c>0x4912b8</c>): вопрос <c>X005TA0914</c> — «Да»: ход кончается сам,
    /// остальные играют дальше; «Нет»: партия кончается, выход в меню.</summary>
    private void AskCaptured(string human)
    {
        var ask = MessageBoxLogic.For(new UiAsk(Text("X005TA0914"), () => { }));
        _overlayQueue.Enqueue((MessageBoxLogic.Screen, ask, "BTN_YES,BTN_NO"));
        _overlayAnswers[ask] = button =>
        {
            GD.Print($"captured human={human} answer={button}");
            if (button == "BTN_YES")
            {
                EndTurn();
                return;
            }
            _leaving = true;
            QuitRequested?.Invoke();
            if (Session is null) GetTree().Quit(0);
        };
        GD.Print($"captured ask human={human}");
    }

    /// <summary>Захватчик-человек взял чужую столицу (команда <c>0x34</c> → <c>0x48b761</c>): «Слава победителю! Вы покорили расу:
    /// %RACE%» (<c>X005TA0775</c>) — тому, кто сейчас смотрит на карту.</summary>
    private void AnnounceConquests(GameSession session)
    {
        var captured = session.Map.Players.Values.Where(p => p.CapturedBy.Length > 0).ToList();
        if (_conquered is null)
        {
            _conquered = captured.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return;
        }
        foreach (var loser in captured.Where(p => !_conquered.Contains(p.Id)))
        {
            // Захватчик-человек, который сейчас не смотрит на карту, узнает в свой ход; захват ИИ не объявляется.
            bool viewer = string.Equals(loser.CapturedBy, _map.Viewer, StringComparison.OrdinalIgnoreCase);
            if (!viewer && session.State.Info.Humans.Contains(loser.CapturedBy, StringComparer.OrdinalIgnoreCase)) continue;
            _conquered.Add(loser.Id);
            if (!viewer) continue;
            var race = GameData.Catalog.Races.FirstOrDefault(r => r.Id.Equals(loser.RaceId, StringComparison.OrdinalIgnoreCase));
            string text = Text("X005TA0775").Replace("%RACE%", race is null ? loser.RaceId : Text(race.NameText), StringComparison.Ordinal);
            _overlayQueue.Enqueue(("DLG_MESSAGE_BOX", new MessageLogic(text, okOnly: true), "BTN_OK,BTN_YES,BTN_NO"));
            GD.Print($"conquered race={loser.RaceId} by={loser.CapturedBy}");
        }
    }

    private void ShowOverlay()
    {
        var (screen, logic, close) = _overlayQueue.Dequeue();
        _overlay = new UiScreenView { Hosted = screen, Name = screen, SlotHost = _host, Logic = logic };
        // По середине холста карты в его масштабе, как окна (MapScreen.Windows.cs): без места свиток вписывался во всё окно сам —
        // 337×367 во всю высоту экрана (ответ пользователя 24.10). Без градиента по бокам: у окна события (800×600) прозрачные места, сквозь
        // них карта видна, как в оригинале (градиент закрывал её — «карта и интерфейс исчезли», прогон 24.10).
        if (Centered(screen) is { } at)
        {
            _overlay.SlotAt = at;
            if (!FullScreen(screen)) _overlay.SlotScale = WindowScale;
        }
        _overlayClose = close;
        var shown = _overlay;
        shown.Fired += events =>
        {
            OnOverlay(shown, events);
            OverlayRequests(shown);
        };
        AddChild(shown);
        OverlayRequests(shown);   // просьбы логики при открытии (звук эпилога)
        if (_map.AiRunning) ShowAiPanels();   // попап в ходе ИИ — поверх панели ожидания DLG_STRAT_WAIT
        else ShowMap(false, keepVisible: true);
        shown.SetProcessUnhandledInput(true);
        WindowSound(screen, opened: true);   // звук окна — MapScreen.Sounds.cs (сессия Г)
        if (logic is NoticeLogic { Sound: { } sound }) GameAudio.Of(this).PlayFx(LastEdict.Data.Sound.WindowSounds.Archive, sound);   // руина — ENTRRUIN
        GD.Print($"overlay {screen}");
    }

    /// <summary>Просьбы логики попапа: мелодия поверх музыки (<see cref="UiTune"/>, звук эпилога) — её конец логике (<see cref="ITuneListener"/>),
    /// пока попап на месте.</summary>
    private void OverlayRequests(UiScreenView view)
    {
        // Только логика, ждущая своей мелодии (эпилог): прочие просьбы попапов здесь не разбираются, и забирать их нельзя.
        if (view.Logic is not (IWindowLogic window and ITuneListener)) return;
        while (window.TakeRequest() is { } request)
        {
            if (request is not UiTune tune) continue;
            GD.Print($"overlay tune {tune.Name} затухание={tune.FadeMs}");
            GameAudio.Of(this).PlayTune(tune.Name, tune.FadeMs, () =>
            {
                if (!IsInstanceValid(view) || view != _overlay || view.Logic is not ITuneListener listener) return;
                listener.TuneEnded(view.State);
                view.Sync();
                GD.Print($"overlay tune ended {tune.Name} повтор={(view.State.IsEnabled("BTN_REPLAY") ? 1 : 0)}");
            });
        }
    }

    private void OnOverlay(UiScreenView view, IReadOnlyList<UiEvent> events)
    {
        if (view != _overlay || events.OfType<UiClicked>().FirstOrDefault(c => _overlayClose.Split(',').Contains(c.Element)) is not { } clicked) return;
        bool ending = view.Hosted == "DLG_END_WIN_LOSE_";
        _overlay = null;
        view.SetProcessUnhandledInput(false);   // окно снимается отложенно — до того оно не должно принять ещё щелчок этого кадра
        Callable.From(() =>
        {
            RemoveChild(view);
            view.QueueFree();
        }).CallDeferred();
        // Ответ — после снятия окна (ход, начатый ответом, не должен застать старое окно в дереве).
        if (view.Logic is { } answered && _overlayAnswers.Remove(answered, out var answer))
            Callable.From(() => answer(clicked.Element)).CallDeferred();
        if (ending && CarryToNextChapter()) return;
        if (ending)
        {
            GD.Print("game over closed → menu");
            _leaving = true;
            QuitRequested?.Invoke();
            if (Session is null) GetTree().Quit(0);   // сцена запущена сама — хозяина нет
            return;
        }
        ReleaseHeldSelection();   // последний попап события хода закрыт — эллипс под ходоком снова (Ин49)
        if (_overlayQueue.Count > 0) ShowOverlay();
        else if (_map.AiRunning) ShowAiPanels();   // попап в ходе ИИ закрыт: карта без ввода, панель ожидания (MapScreen.AiTurn.cs)
        else
        {
            ShowMap(true);
            if (Windows is { } context) ShowTurnSummary(context);   // итоги хода ждали окна начала хода
            AskLeaderName();
            AskUpgrade();
            AskGiven();
            OpenEventBattle();
        }
    }

    /// <summary>Эпилог главы кампании закрыт, зритель победил и глава не последняя (<c>0x40505b</c>): вместо меню — окно переноса
    /// (<see cref="CarryOverLogic"/>; пустая подложка движка <c>DLG_FAKE_CARRY_OVER</c> не показывается), затем следующая глава.</summary>
    private bool CarryToNextChapter()
    {
        var session = _map.Session;
        if (Session is null || session.State.Info.Campaign is not { LastChapter: false } || session.Won(_map.Viewer ?? "") != true
            || Windows is not { } context) return false;
        GD.Print("chapter won → carry over");
        OpenWindow(CarryOverLogic.Screen, new CarryOverLogic(context, carry => Callable.From(() =>
        {
            if (_leaving) return;
            _leaving = true;
            GD.Print($"carry hero={(carry.Hero?.TypeId ?? "-")} items={carry.ItemTypes.Count}");
            NextChapterRequested?.Invoke(carry);
        }).CallDeferred()));
        return true;
    }

    /// <summary>Мини-карта глазами нового зрителя (туман другого игрока).</summary>
    private void RebuildMiniMap()
    {
        if (_mini is null) return;
        RemoveChild(_mini);
        _mini.QueueFree();
        _mini = null;
        BuildMiniMap(GameData.Content.Screens, _map.Looks, _map.Viewer);
        Place();
    }

    /// <summary>Шаг проверки «ok»: закрыть все попапы событий, окна передачи хода, окно начала хода и окно продолжения загруженной партии
    /// (не окна конца партии).</summary>
    private void DismissPopups()
    {
        while (_overlay is { } view && Dismissable(view))
            OnOverlay(view, [new UiClicked(view.Hosted == "DLG_EVENT_POPUP" ? "BTN_RIGHTSIDE" : "BTN_OK")]);
        AcceptLeaderName();
        CloseChats();
    }

    /// <summary>Проверка: верхнее окно — сообщение ИИ (<see cref="ChatLogic"/>) — «Закрыть», как кнопкой; строка <c>aichat ok реплика=…</c>.</summary>
    private void CloseChats()
    {
        if (_dialogViews.Count > 0 && _dialogViews[^1] is { Logic: ChatLogic chat } view && !_dialogsStale)
        {
            GD.Print($"aichat ok {chat.Summary}");
            view.Step("click BTN_OK", out _);
        }
    }

    /// <summary>Шаг «ok»: ещё есть что закрыть — окно очереди, окно имени, окна выбора навыка, сообщения ИИ.</summary>
    private bool TurnWindowsPending => (_overlay is { } open && Dismissable(open)) || _logics.Any(l => l is LeaderNameLogic or ChatLogic)
                                       || UpgradesPending;

    /// <summary>Окно очереди, которое шаг «ok» закрывает.</summary>
    private bool Dismissable(UiScreenView view) =>
        view.Hosted is "DLG_EVENT_POPUP" || view.Logic is BeginTurnLogic
        || (_resumeLogic is not null && view.Logic == _resumeLogic);

    /// <summary>Шаг проверки «end»: окно начала хода открыто — «ОК» (оно теперь каждый день, а прогоны «end;end;…» его не ждут).</summary>
    private void DismissBeginTurn()
    {
        while (_overlay is { Logic: BeginTurnLogic } view) OnOverlay(view, [new UiClicked("BTN_OK")]);
    }

    /// <summary>Проверка: окно имени героя первого хода (<see cref="AskLeaderName"/>) открыто — принять имя по умолчанию, как кнопкой
    /// (шаги <c>ok</c>, <c>end</c>, <c>win</c>: человек не кончит ход и не выиграет из-под окна).</summary>
    private void AcceptLeaderName()
    {
        if (_dialogViews.FirstOrDefault(v => v.Logic is LeaderNameLogic) is { } named)
            named.Step("click BTN_CLOSE", out _);
    }
}
