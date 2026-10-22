using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using D2.Data.Gfx;
using D2.Data.Play;
using D2.Rules.Battle;
using Godot;

namespace D2.Godot;

/// <summary>
/// Ход ИИ на экране (<c>docs/ai-screen.md</c>). «Конец хода» проводит <see cref="GameSession.EndTurn"/> в фоновом потоке,
/// а вид карты — его наблюдатель (<see cref="ITurnObserver"/>). Передача хода строгая: крючок на рабочем потоке кладёт
/// заявку и ждёт, главный поток её показывает (чей ход, отряд идёт по клеткам, бой с человеком) и отпускает рабочий.
/// Партию в любой момент исполняет один поток; пока идёт ход ИИ, вид читает снимок партии, взятый на последней заявке
/// (<see cref="Map"/>, <see cref="Looks"/>), а не живую партию.
///
/// <para>Со слов пользователя 25.09: видны только ходы по открытым человеку клеткам (шаг, где обе клетки под туманом, —
/// без картинки и без времени), камера следует за отрядом. Бой ИИ с отрядом или укреплением человека — экран боя, где
/// человек ведёт свою сторону (<see cref="AiBattleRequested"/>); без подписчика — автобой ядра.</para>
/// </summary>
public partial class MapView
{
    /// <summary>Заявка рабочего потока: чей ход, ход отряда или бой.</summary>
    private abstract record AiRequest;

    private sealed record AiPlayerRequest(string PlayerId) : AiRequest;

    private sealed record AiMoveRequest(StackMove Move) : AiRequest;

    private sealed record AiBattleRequest(AiBattle Battle) : AiRequest;

    /// <summary>Предложение ИИ человеку у ПК (<see cref="ITurnObserver.Offered"/>): хозяин открывает окно предложения, ход ИИ ждёт,
    /// пока предложение не уйдёт из <see cref="GameState.Offers"/> — человек ответил (<c>Answer*Offer</c>).</summary>
    private sealed record AiOfferRequest(AllianceOffer Offer) : AiRequest;

    /// <summary>Попап события человеку у ПК в ход ИИ (<see cref="GameState.Messages"/>): хозяин показывает окна, ход ИИ ждёт их закрытия.</summary>
    private sealed record AiPopupRequest(string PlayerId) : AiRequest;

    /// <summary>Ход ИИ стоит на заявке попапа человеку у ПК: главный поток может подать команду (снять попапы, <c>takeMessages</c>) — рабочий
    /// ждёт на вызове наблюдателя (<see cref="GameSession.Execute"/>).</summary>
    public bool AiPopupWaits => _aiShowing is AiPopupRequest;

    /// <summary>Что-то случилось на виду без хода по клеткам: захват, наём, заклинание (<see cref="TurnSight"/>) или бой ИИ с ИИ
    /// (<c>Kind</c> — «бой»). Показ — камера к месту и пауза (ответ пользователя 26.09).</summary>
    private sealed record AiSightRequest(string Kind, string Id, int X, int Y, TurnSight? Sight = null) : AiRequest;

    /// <summary>Пауза показа события без хода, секунд при скорости 1.</summary>
    private const double SightSeconds = 0.6;

    private double _aiPause;

    private const string SpellKind = "заклинание", ShakeKind = "тряска", DeathKind = "гибель", TombDeathKind = "гибель-могила",
                         BattleKind = "бой", GrowKind = "рост", LootKind = "разграбление", LandKind = "земля", NobleKind = "знатный",
                         CreateKind = "создание", MountainKind = "горы",
        OwnerKind = "владелец";

    private System.Threading.Thread? _mainThread;

    /// <summary>Идущий отряд ИИ: номер шага (от <c>Cells[Step]</c> к <c>Cells[Step + 1]</c>) и последний показанный ключевой кадр (−1 — ещё ни
    /// одного).</summary>
    private sealed record AiWalk(StackMove Move, int Step, int Shown);

    /// <summary>Часы хода отряда ИИ — та же группа, что у своих (<see cref="StackSmooth"/>, <c>0x4f5e7c</c>).</summary>
    private readonly D2.Data.Map.AnimTicker _aiWalkTicker = new(D2.Data.Map.AnimClock.StackWalk(D2.Data.Map.StackWalk.SmoothScroll));

    private Thread? _aiThread;
    private readonly SemaphoreSlim _aiResume = new(0, 1);
    private volatile AiRequest? _aiRequest;
    private volatile bool _aiDone;
    private volatile bool _aiCancel;
    private Exception? _aiError;
    private AiRequest? _aiShowing;
    private AiWalk? _aiWalk;

    /// <summary>Заявка, ждущая конца смены тумана (<see cref="PlayFogChanges"/>).</summary>
    private AiRequest? _aiAfterFog;
    private GameState? _aiShown;
    private DateTime _aiStart;
    private int _aiPlayers, _aiMoves, _aiShownMoves, _aiBattles, _aiHumanBattles, _aiSights;

    /// <summary>Счётчики мигания на начало хода ИИ (проверка): провалы земли, пересборки объектов и узлы, сохранившие кадр.</summary>
    private (int Gaps, int Held, int Frames) _aiGapsFrom;
    private int _aiBuildsFrom, _aiKeptFrom, _aiJumpsFrom;

    /// <summary>Идёт ход ИИ.</summary>
    public bool AiRunning => _aiThread is not null;

    /// <summary>Скорость показа хода ИИ (множитель к скорости шага); проверки без окна ставят больше.</summary>
    public float AiSpeed { get; set; } = 1f;

    /// <summary>Ходы ИИ идут по клеткам (настройка <c>TOG_PLAYAIISOANIM</c>); нет — отряд встаёт сразу в конечную клетку.</summary>
    public bool AiAnimate { get; set; } = true;

    /// <summary>Ход ИИ начался (главный поток): хозяин прячет панель команд и выключает ввод.</summary>
    public event Action? AiStarted;

    /// <summary>Начался ход игрока ИИ.</summary>
    public event Action<string>? AiPlayer;

    /// <summary>Отряд ИИ пошёл по видимым человеку клеткам.</summary>
    public event Action<StackMove>? AiMoving;

    /// <summary>Бой ИИ с человеком: хозяин показывает экран боя и по его закрытии отдаёт журнал (<see cref="AiBattle.Done"/>).</summary>
    public event Action<AiBattle>? AiBattleRequested;

    /// <summary>ИИ предложил человеку у ПК союз, сделку или обмен картами в свой ход: хозяин открывает окно предложения
    /// (<see cref="AiOfferRequest"/>); без подписчика предложение ждёт начала хода человека.</summary>
    public event Action<AllianceOffer>? AiOfferRequested;

    /// <summary>Ход ИИ ждёт ответа человека на предложение (<see cref="AiOfferRequested"/>).</summary>
    public bool AiOfferShowing => _aiShowing is AiOfferRequest;

    /// <summary>
    /// В ход ИИ человеку у ПК пришёл попап события (<see cref="TurnSightKind.EventPopup"/>; <c>CCmdPopupMsg</c> → <c>0x489e90</c>: у движка окно
    /// сразу, проверки хода нет; очередь команд клиента стоит до «ОК», <c>0x4bd98a</c>): хозяин выкладывает окна
    /// (<c>MapScreen.ShowAiPopups</c>, сессия В). Ход ИИ стоит, пока <see cref="AiPopupOpen"/> — true; без подписчика попап ждёт начала хода
    /// человека, как прежде.
    /// </summary>
    public event Action? AiPopupRequested;

    /// <summary>Окна попапов ещё открыты или в очереди (хозяин).</summary>
    public Func<bool>? AiPopupOpen { get; set; }

    /// <summary>Ход ИИ кончился, день сменился (или партия кончилась): карта уже перерисована.</summary>
    public event Action? AiTurnFinished;

    /// <summary>Бой ИИ, который играет хозяин. <see cref="Resolve"/> — доиграть автобоем с рабочего потока, если вид уходит
    /// посреди боя (экран уже сдвинул состояние и генератор — начинать бой заново нельзя).</summary>
    public sealed class AiBattle(BattleState state, MapView view)
    {
        /// <summary>Журнал боя ровно один: от экрана (<see cref="Done(IReadOnlyList{TurnRecord}?)"/>) или от автобоя при уходе вида
        /// (<see cref="ResolveOnce"/>), который могут позвать и главный поток (<see cref="CancelAi"/>), и рабочий — второй ждёт первого.</summary>
        private readonly ResolveOnce<IReadOnlyList<TurnRecord>> _log = new();

        public BattleState State { get; } = state;

        internal IReadOnlyList<TurnRecord>? Log => _log.Value;

        /// <summary>Доиграть бой без экрана (только C#, без узлов Godot).</summary>
        public Func<IReadOnlyList<TurnRecord>>? Resolve { get; set; }

        /// <summary>Действия экрана боя (<see cref="D2.Data.Battle.BattlePlay"/>) — ответ ядру данными: по ним журнал команд повторит бой.</summary>
        public Func<D2.Data.Battle.BattlePlay>? Played { get; set; }

        /// <summary>Бой сыгран: журнал уходит в ход ИИ, рабочий поток продолжает. null — экран бой не сыграл (занят): ядро сыграет
        /// автобой само (<see cref="ITurnObserver.Battle"/>). Журнал уже есть — повтор ничего не делает.</summary>
        public void Done(IReadOnlyList<TurnRecord>? log)
        {
            if (_log.TrySet(log)) view.AiRelease();
        }

        /// <summary>Доиграть автобоем, если журнала ещё нет; ровно один раз на оба потока.</summary>
        internal IReadOnlyList<TurnRecord>? ResolveOnce() => _log.Run(() => Resolve?.Invoke());

        /// <summary>Бой сыгран экраном, прозвучало <paramref name="notesHeard"/> сообщений боя: остальные (конец боя модели) — звуком на
        /// следующей заявке, когда ядро уже сыграло <see cref="GameSession.FinishBattle"/>.</summary>
        public void Done(IReadOnlyList<TurnRecord> log, int notesHeard)
        {
            if (_log.Done) return;   // журнал уже есть (бой доигран автобоем после ухода вида) — сообщения не того боя
            view._battleNotes = (State, notesHeard);
            Done(log);
        }
    }

    /// <summary>Сообщения боя ИИ с человеком, ждущие конца боя модели: состояние боя и сколько уже прозвучало.</summary>
    private (BattleState State, int Heard)? _battleNotes;

    /// <summary>Бой ИИ с человеком сыгран экраном, и конец боя модели (<see cref="GameSession.FinishBattle"/>) уже записан (пришла следующая
    /// заявка или ход кончился): хозяин показывает конец боя на ещё открытом экране (<c>BattleScreen.PlayEnd</c>) и зовёт продолжение —
    /// до него показ хода ИИ стоит. Без подписчика — звуки на карте (<see cref="PlayBattleNotes"/>).</summary>
    public event Action<BattleState, int, Action>? AiBattleEnded;

    /// <summary>Идёт показ конца боя ИИ на экране (<see cref="AiBattleEnded"/>).</summary>
    private bool _battleEnding;

    /// <summary>Сыграть концевые сообщения боя ИИ (рабочий поток ждёт — конец боя модели уже записан).</summary>
    private void FlushBattleNotes()
    {
        if (_battleNotes is not { } notes) return;
        _battleNotes = null;
        PlayBattleNotes(notes.State, notes.Heard);
    }

    /// <summary>Конец хода человека: ход ИИ в фоновом потоке. Выбор и показанный путь остаются (у движка флажки видны и в ход ИИ —
    /// <c>path</c> выбранного отряда с прежней последней достижимой, журнал <c>20261019c-cov</c> seq 2164076); с новым запасом путь
    /// пересчитывается в начале своего хода (<see cref="RouteAtTurnStart"/>).</summary>
    public void EndTurn()
    {
        if (Walking || AiRunning) return;
        if (PassesToHuman())
        {
            // «Горячий стул»: ход сразу переходит к следующему человеку, ИИ не ходит — показывать нечего.
            Session.Execute(new D2.Data.Play.Commands.EndTurnCommand());
            AfterTurn();
            return;
        }
        _aiShown = Session.State;
        (_aiDone, _aiCancel, _aiError, _aiRequest, _aiShowing, _aiWalk, _aiAfterFog) = (false, false, null, null, null, null, null);
        (_aiPlayers, _aiMoves, _aiShownMoves, _aiBattles, _aiHumanBattles, _aiSights) = (0, 0, 0, 0, 0, 0);
        _aiStart = DateTime.UtcNow;
        (_aiGapsFrom, _aiBuildsFrom, _aiKeptFrom, _aiJumpsFrom) = (GroundGaps, _objects.Builds, _objects.PhaseKept, _objects.PhaseJumpsAvoided);
        Session.Observer = new AiHooks(this);   // вид мог смениться (загрузка) — наблюдатель снова свой
        // Поток стартует на следующем кадре (StepAi): хозяин после EndTurn ещё читает партию в этом кадре
        // (MapScreen.EndTurn → ShowOffers), и рабочий не должен её менять в это время.
        _aiThread = new Thread(RunAi) { IsBackground = true, Name = "ai-turn" };
        AiStarted?.Invoke();
    }

    /// <summary>Тело рабочего потока. Исключение не должно уйти из потока (в .NET оно роняет процесс): оно передаётся
    /// главному, и тот сообщает об ошибке — партия после него в полуходе.</summary>
    private void RunAi()
    {
        try
        {
            // Командой: ответ человека на предложение, пока ход ИИ ждёт, — вложенная команда этого хода (docs/game-commands.md §2).
            Session.Execute(new D2.Data.Play.Commands.EndTurnCommand());
        }
        catch (Exception e)
        {
            _aiError = e;
        }
        finally
        {
            _aiDone = true;
        }
    }

    /// <summary>Главный поток, каждый кадр: взять заявку, вести показ, по концу хода перерисовать карту.</summary>
    private void StepAi(double delta)
    {
        if (_aiThread is null) return;
        if (_aiThread.ThreadState.HasFlag(ThreadState.Unstarted)) _aiThread.Start();
        if (_battleEnding) return;
        if (_battleNotes is { } ended && AiBattleEnded is { } show && _aiShowing is null && (_aiRequest is not null || _aiDone))
        {
            _battleNotes = null;
            _battleEnding = true;
            show(ended.State, ended.Heard, () => _battleEnding = false);
            return;
        }
        if (_aiShowing is null && _aiRequest is { } request)
        {
            _aiRequest = null;
            _aiShowing = request;
            _aiShown = Session.State;   // рабочий ждёт — снимок согласован
            // Туман зрителя сменился с прошлой заявки (эффект CEffectFog в ходе ИИ) — сначала кольца, потом показ заявки (у движка команда
            // смены тумана — своя задача очереди перед следующими).
            Guarded(request, () =>
            {
                if (PlayFogChanges(derive: true)) _aiAfterFog = request;
                else Begin(request);
            });
        }
        if (_aiAfterFog is { } waiting)
        {
            if (FogChanging) return;
            _aiAfterFog = null;
            BeginGuarded(waiting);
        }
        if (_aiWalk is not null && !Scrolling) StepAiWalk(delta);
        if (_aiShowing is AiSightRequest && !SpellPlaying && (_aiPause -= delta * AiSpeed) <= 0) AiRelease();
        // Ответ дан — ход ИИ идёт дальше, когда закрыта и реплика предлагавшего ИИ (DLG_CHAT_AI за окном предложения).
        if (_aiShowing is AiOfferRequest { Offer: var offer } && !Session.State.Offers.Contains(offer) && AiPopupOpen?.Invoke() != true) AiRelease();
        if (_aiShowing is AiPopupRequest && AiPopupOpen?.Invoke() != true) AiRelease();
        if (_aiDone && _aiShowing is null && _aiRequest is null && !FogChanging && !FinalFog()) FinishAi();
    }

    /// <summary>Показ заявки; исключение в нём (хозяин, сцена) не вешает ход ИИ: ошибка — в журнал, заявка отпускается без показа
    /// (бой с человеком — автобоем ядра), под <c>--check</c> — выход с кодом 1.</summary>
    private void BeginGuarded(AiRequest request) => Guarded(request, () => Begin(request));

    /// <summary>Часть показа заявки (и её продолжение после прокрутки или смены тумана — оно идёт из <c>_Process</c>, мимо
    /// <see cref="BeginGuarded"/>) под защитой: исключение — в журнал, заявка отпускается.</summary>
    private void Guarded(AiRequest request, Action show)
    {
        try
        {
            show();
        }
        catch (Exception e)
        {
            Failures.Report(this, $"MapView.Begin {request.GetType().Name}", e);
            _aiWalk = null;
            if (!ReferenceEquals(_aiShowing, request)) return;   // показ уже отпущен до исключения
            if (request is AiBattleRequest { Battle: var battle })
            {
                // Экран боя уже успел начать (генератор сдвинут) — доиграть его автобоем; не успел — журнала нет, бой сыграет ядро.
                if (battle.Resolve is not null) ResolveQuietly(battle);
                else battle.Done(null);
                if (ReferenceEquals(_aiShowing, request)) AiRelease();
            }
            else AiRelease();
        }
    }

    private void Begin(AiRequest request)
    {
        FlushBattleNotes();
        switch (request)
        {
            case AiPlayerRequest p:
                _aiPlayers++;
                Reload();
                AiPlayer?.Invoke(p.PlayerId);
                AiRelease();
                break;
            case AiMoveRequest m:
                _aiShownMoves++;
                var cells = m.Move.Cells;
                int visible = cells.Count(c => !Scene.Hidden(c.X, c.Y));
                GameLog.Verbose($"aimove {m.Move.StackId} игрок={m.Move.Owner} клеток={cells.Count - 1} видно={visible} " +
                         $"из=({cells[0].X},{cells[0].Y}) в=({cells[^1].X},{cells[^1].Y}) прыжок={(AiAnimate ? 0 : 1)}");
                if (!AiAnimate || cells.Count < 2)
                {
                    // Без показа ходов отряд просто встаёт в конечную клетку: карта перерисуется на следующей заявке. Ход без шагов (команда
                    // с места, GameSession.StrikeInPlace — охотник карты 13 CA2) показывать нечем: шаг ShowAiStep берёт две клетки.
                    _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
                    Reload();
                    if (cells.Where(c => !Scene.Hidden(c.X, c.Y)).Select(c => ((int, int)?)c).LastOrDefault() is { } seen)
                        CenterOn(seen);
                    AiMoving?.Invoke(m.Move);
                    AiRelease();
                    break;
                }
                // Перед ходом — вид к первой видной точке пути, если она дальше 2 клеток (0x4f7f9b, CMidAnimSmoothScroll), затем шаги.
                if (FirstSeenCell(m.Move, 0) is { } first) ScrollThen(first, onlyIfFar: true, () => Guarded(m, () => BeginAiWalk(m.Move)));
                else BeginAiWalk(m.Move);
                break;
            case AiSightRequest s:
                _aiSights++;
                _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
                _aiPause = 0;   // пока вид едет (SpellPlaying), пауза не идёт; показ задаст её сам
                var (scrollTo, far) = SightScroll(s);
                ScrollThen(scrollTo, far, () => Guarded(s, () => ShowSight(s)));
                break;
            case AiOfferRequest { Offer: var o }:
                Reload();
                GameLog.Verbose($"aioffer {o.From} → {o.To} {(o.Trade is not null ? "сделка" : o.Exchange ? "обмен" : "союз")}");
                AiOfferRequested?.Invoke(o);
                break;
            case AiPopupRequest { PlayerId: var to }:
                _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
                Reload();
                GameLog.Verbose($"aipopup для={to} сообщений={Session.State.Messages.Count(m => m.PlayerId.Equals(to, StringComparison.OrdinalIgnoreCase))}");
                AiPopupRequested?.Invoke();
                break;
            case AiBattleRequest { Battle: var b }:
                _aiHumanBattles++;
                _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
                Reload();
                AiBattleRequested?.Invoke(b);
                break;
        }
    }

    /// <summary>Ход отряда ИИ на виду: картинки идущего отряда и первый ключевой кадр (по концу прокрутки к пути).</summary>
    private void BeginAiWalk(StackMove move)
    {
        Reload(hiddenStack: move.StackId);
        AiMoving?.Invoke(move);
        _aiWalk = new AiWalk(move, 0, -1);
        _aiWalkAnim = (Time.GetTicksMsec(), 0);
        _aiWalkTicker.Reset();
        ShowAiStep(_aiWalk);
        StepAiWalk(0);
    }

    /// <summary>Первая видная клетка пути начиная с шага <paramref name="step"/> — начало видного куска (у движка путь режется на видные куски, и
    /// перед каждым — прокрутка к его началу, <c>0x4f7dbc</c>); видных шагов нет — null.</summary>
    private (int X, int Y)? FirstSeenCell(StackMove move, int step)
    {
        for (int i = step; i + 1 < move.Cells.Count; i++)
        {
            var (from, to) = (move.Cells[i], move.Cells[i + 1]);
            if (!Scene.Hidden(from.X, from.Y)) return (from.X, from.Y);
            if (!Scene.Hidden(to.X, to.Y)) return (to.X, to.Y);
        }
        return null;
    }

    /// <summary>Куда и как ехать виду перед показом события (флаги вызова <c>0x4f4a0c</c> по командам, разбор 16.10): литьё — всегда
    /// (<c>CCmdCastSpellMsg</c>, <c>0x4f3301</c>/<c>0x4f33e4</c>: «только если далеко» = 0), начало боя — к нападающему, прочее — к месту, если
    /// оно дальше 2 клеток (<c>CCmdStackDestroyed</c>, <c>CreateStack</c>, <c>ChangeOwner</c>, <c>NobleAction</c>, <c>CityGrow</c>,
    /// <c>LootRuin</c>, <c>TerrainChange</c>, <c>MapChange</c>/<c>ChangeLandmark</c>, <c>BattleStart</c>: 1).</summary>
    private ((int X, int Y) Cell, bool OnlyIfFar) SightScroll(AiSightRequest s)
    {
        if (s.Kind is SpellKind or ShakeKind) return ((s.X, s.Y), false);
        if (s.Kind == BattleKind && Session.Map.Stacks.TryGetValue(s.Id, out var attacker)) return ((attacker.X, attacker.Y), true);
        return ((s.X, s.Y), true);
    }

    /// <summary>Показ события без хода по клеткам — по концу прокрутки к нему: анимация или звук, иначе пауза.</summary>
    private void ShowSight(AiSightRequest s)
    {
        if (s.Kind == MountainKind)
        {
            // Горы видны до середины анимации: карта перерисуется на её середине (PlayMountains).
            _aiPause = PlayMountains(s.Id, s.X, s.Y, AiSpeed) ? 0 : SightSeconds;
            GameLog.Verbose($"aisight {s.Kind} {s.Id} в=({s.X},{s.Y})");
            return;
        }
        Reload();
        // Заклинание — его анимация или тряска (движок ждёт их конца), прочее — пауза.
        if (s.Kind == BattleKind)
        {
            _aiPause = PlayBattleStart(s.X, s.Y, AiSpeed, s.Id) ? 0 : SightSeconds;   // анимация — над целью
            GameLog.Verbose($"aisight {s.Kind} {s.Id} в=({s.X},{s.Y})");
            return;
        }
        // Звук без картинки (CMidAnim2CityGrow — CMidAnim2Sound; разграбление — LOOTRUIN), растекание — OCCUPY по ходу обхода,
        // вор у цели — ANOBLE между ним и целью.
        _aiPause = s.Kind switch
        {
            GrowKind or LootKind => SoundPause(s.Kind == GrowKind ? "CITYGROW" : "LOOTRUIN"),
            LandKind => PlayLandSpread(s.Sight!, AiSpeed),
            NobleKind => PlayNoble(s.Id, s.X, s.Y, AiSpeed) ? 0 : SightSeconds,
            _ => -1,
        };
        if (_aiPause >= 0)
        {
            GameLog.Verbose($"aisight {s.Kind} {s.Id} в=({s.X},{s.Y})");
            return;
        }
        _aiPause = s.Kind is DeathKind or TombDeathKind
                ? (PlayDeath(s.Id, s.X, s.Y, AiSpeed, tomb: s.Kind == TombDeathKind) ? 0 : SightSeconds)
            : s.Kind == CreateKind ? (PlayCreate(s.Id, s.X, s.Y, AiSpeed) ? 0 : SightSeconds)
            : s.Kind.StartsWith(OwnerKind + ":", StringComparison.Ordinal)
                ? (PlayOwnerChange(s.Id, s.Kind[(OwnerKind.Length + 1)..], s.X, s.Y, AiSpeed) ? 0 : SightSeconds)
            : s.Kind is SpellKind or ShakeKind && ShowSpell(s.Id, s.X, s.Y, s.Kind == ShakeKind, AiSpeed) ? 0 : SightSeconds;
        // Развеивание и туман: модель сменит туман после этого показа, кольца — на следующей заявке (PlayFogChanges) с центром и радиусом
        // эффекта.
        if (s.Kind is SpellKind or ShakeKind && s.Sight is { } cast && SpellFog(cast.Id, cast.X, cast.Y) is { } fog)
            HoldFog(fog.X, fog.Y, fog.Radius);
        GameLog.Verbose($"aisight {s.Kind} {s.Id} в=({s.X},{s.Y})");
    }

    /// <summary>Звук события без картинки и обычная пауза показа.</summary>
    private double SoundPause(string name)
    {
        GameAudio.Of(this).PlayFx("Midgard", name);
        return SightSeconds;
    }

    /// <summary>Картинки шага: отряд в клетке начала шага лицом к следующей; шаг целиком под туманом — без картинки. Вид следует за отрядом
    /// по ключевым кадрам (<see cref="StepAiWalk"/>).</summary>
    private void ShowAiStep(AiWalk walk)
    {
        var from = walk.Move.Cells[walk.Step];
        var to = walk.Move.Cells[walk.Step + 1];
        bool seen = !Scene.Hidden(from.X, from.Y) || !Scene.Hidden(to.X, to.Y);
        var look = Looks.Stacks.FirstOrDefault(s => s.Id.Equals(walk.Move.StackId, StringComparison.OrdinalIgnoreCase));
        _objects.MoveOffset(Vector2.Zero);
        if (!seen || look is null)
        {
            _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
            return;
        }
        // Ход с нападением (StackMove.Attack) — ATTK на последнем шаге, прочие молчат, как у человека (MapView.Sounds.cs).
        StepSound(walk.Move.StackId, to.X, to.Y, first: walk.Step == 0,
                  walk.Move.Attack ? walk.Step + 2 == walk.Move.Cells.Count : null);
        var facing = D2.Rules.Map.PathFinder.Facing(new D2.Rules.Map.PathPoint(from.X, from.Y, 0, 0),
                                                    new D2.Rules.Map.PathPoint(to.X, to.Y, 0, 0));
        _objects.ShowMoving(Scene.StackSprites(look with { X = from.X, Y = from.Y }, facing, moving: true),
                            GameData.Content.Gfx, GameData.GfxRoot);
    }

    /// <summary>Начало хода отряда ИИ на виду и показано ключевых кадров — строка <c>map_anim CMidAnim2Unit</c> зонда.</summary>
    private (ulong StartMs, int Frames) _aiWalkAnim;

    /// <summary>Шаг отряда ИИ между клетками ключевыми кадрами, как у человека (<see cref="StepWalk"/>, <see cref="D2.Data.Map.StackWalk"/>; скорость —
    /// <c>OpponentSpeed</c>): ключевой кадр за тик часов хода (<see cref="_aiWalkTicker"/>), отряд встаёт в точку кадра, вид центрируется на ней
    /// (<c>0x4f6539</c>); шаг под туманом проходит сразу. Дошёл — картинка остаётся в последней клетке до следующей заявки, рабочий переставляет
    /// отряд.</summary>
    private void StepAiWalk(double delta)
    {
        var walk = _aiWalk!;
        var from = walk.Move.Cells[walk.Step];
        var to = walk.Move.Cells[walk.Step + 1];
        bool seen = !Scene.Hidden(from.X, from.Y) || !Scene.Hidden(to.X, to.Y);
        var frames = StepFrames(from, to, AiSpeed);
        int k = seen ? walk.Shown + 1 : frames.Count;
        if (seen && walk.Shown >= 0 && !_aiWalkTicker.Advance(delta * 1000)) return;
        if (k < frames.Count)
        {
            _aiWalkAnim.Frames++;
            _aiWalk = walk with { Shown = k };
            if (k == 0) _aiWalkTicker.Reset();
            var offset = new Vector2(frames[k].X, frames[k].Y);
            _objects.MoveOffset(offset);
            FollowStack(from, offset);
            return;
        }
        if (walk.Step + 2 >= walk.Move.Cells.Count)
        {
            var (fx, fy) = GroundGeometry.CellOrigin(from.X, from.Y);
            var (tx, ty) = GroundGeometry.CellOrigin(to.X, to.Y);
            _objects.MoveOffset(seen ? new Vector2(tx - fx, ty - fy) : Vector2.Zero);
            if (seen) CenterOn(to);   // по концу пути вид — на последней клетке (0x4f6671)
            PortProbe.Av?.MapAnim("end", "CMidAnim2Unit", to.X, to.Y, walk.Move.StackId,
                                  (long)(Time.GetTicksMsec() - _aiWalkAnim.StartMs), _aiWalkAnim.Frames,
                                  group: _aiWalkTicker.Group == D2.Data.Map.AnimGroup.Fast ? 1 : 0);
            _aiWalk = null;
            StepsDone();
            AiRelease();
            return;
        }
        var next = new AiWalk(walk.Move, walk.Step + 1, -1);
        _aiWalk = next;
        // Видный кусок пути после шагов под туманом — сначала вид к его началу (0x4f7f35…0x4f7f9b), затем шаги.
        if (!seen && FirstSeenCell(walk.Move, next.Step) is { } start && start == StepStart(next)
            && _aiShowing is { } moving && StartScroll(start, onlyIfFar: true, () => Guarded(moving, () => ShowAiStep(next))))
            return;
        ShowAiStep(next);
        StepAiWalk(0);   // первый кадр нового шага — в этом же тике
    }

    /// <summary>Видная клетка шага, с которой начинается видный кусок: начало шага, если видно, иначе его конец; шаг под туманом — null.</summary>
    private (int X, int Y)? StepStart(AiWalk walk)
    {
        var (from, to) = (walk.Move.Cells[walk.Step], walk.Move.Cells[walk.Step + 1]);
        return !Scene.Hidden(from.X, from.Y) ? (from.X, from.Y) : !Scene.Hidden(to.X, to.Y) ? (to.X, to.Y) : null;
    }

    /// <summary>Смена тумана перед концом хода ИИ (эффекты последнего игрока и начала хода человека); исключение — в журнал, ход кончается.</summary>
    private bool FinalFog()
    {
        try
        {
            return PlayFogChanges(derive: true);
        }
        catch (Exception e)
        {
            Failures.Report(this, "MapView.PlayFogChanges", e);
            ResetAnimations();
            return false;
        }
    }

    /// <summary>Показ заявки кончился: рабочий поток продолжает.</summary>
    private void AiRelease()
    {
        _aiShowing = null;
        // Отпускает только главный поток; поздний повтор (экран боя закрылся после ухода вида) не должен переполнить семафор.
        if (_aiResume.CurrentCount == 0) _aiResume.Release();
    }

    private void FinishAi()
    {
        FlushBattleNotes();
        _aiThread = null;
        _aiShown = null;
        _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
        _objects.MoveOffset(Vector2.Zero);
        if (_aiError is { } error) GD.PrintErr($"MapView: ход ИИ прерван: {error.GetType().Name}: {error.Message}\n{error.StackTrace}");
        GameLog.Verbose($"aiturn игроков={_aiPlayers} ходов={_aiMoves} показано={_aiShownMoves} под_туманом={_aiMoves - _aiShownMoves} " +
                 $"боёв={_aiBattles} с_человеком={_aiHumanBattles} день={Map.Turn} ошибка={(_aiError is null ? 0 : 1)} " +
                 $"без_хода={_aiSights} " +
                 $"провалов_земли={GroundGaps.Gaps - _aiGapsFrom.Gaps} удержано={GroundGaps.Held - _aiGapsFrom.Held} кадров={GroundGaps.Frames - _aiGapsFrom.Frames} " +
                 $"пересборок={_objects.Builds - _aiBuildsFrom} кадр_сохранён={_objects.PhaseKept - _aiKeptFrom} " +
                 $"скачков_кадра_снято={_objects.PhaseJumpsAvoided - _aiJumpsFrom} " +
                 $"ms={(DateTime.UtcNow - _aiStart).TotalMilliseconds:F0}");
        AfterTurn();
    }

    /// <summary>Ход кончился: карта перерисована, хозяину — конец хода.</summary>
    private void AfterTurn()
    {
        RefreshGround();
        Reload();
        RouteAtTurnStart();   // путь выбранного отряда — с новым запасом очков (MapView.Routes.cs)
        Marks();
        Changed?.Invoke();
        AiTurnFinished?.Invoke();
    }

    /// <summary>За человеком этого дня ходит следующий человек (<see cref="GameSession.EndTurn"/>, «горячий стул»), а не ИИ.</summary>
    private bool PassesToHuman()
    {
        var humans = Session.State.Info.Humans;
        int at = -1;
        for (int i = 0; i < humans.Count; i++)
            if (humans[i].Equals(Session.ActiveHuman, StringComparison.OrdinalIgnoreCase)) at = i;
        return Session.Book is not null && !Session.Over && at >= 0 && at + 1 < humans.Count;
    }

    /// <summary>Ошибка хода ИИ (для хозяина: в проверке — код выхода).</summary>
    public bool AiFailed => _aiError is not null;

    /// <summary>Вид уходит посреди хода ИИ: крючки больше не ждут, начатый бой доигрывается автобоем, и ход ИИ
    /// доходит до конца здесь же — чтобы партию, которую возьмёт кто-то другой, не менял брошенный поток.</summary>
    private void CancelAi()
    {
        if (_aiThread is not { } thread) return;
        _aiCancel = true;
        Session.Observer = null;
        _aiWalk = null;
        ResetAnimations();   // продолжения показа (после прокрутки и тумана) — не для ушедшего вида
        // Автобой — один раз: рабочий поток, увидев отмену, зовёт тот же ResolveOnce и ждёт этот итог, а не играет бой вторым.
        if (_aiShowing is AiBattleRequest { Battle: var battle }) ResolveQuietly(battle);
        if (_aiShowing is not null) AiRelease();
        if (thread.ThreadState.HasFlag(ThreadState.Unstarted)) thread.Start();   // ход ИИ всё равно проводится целиком
        if (!thread.Join(TimeSpan.FromSeconds(CancelJoinSeconds)))
            GD.PrintErr($"MapView: ход ИИ не кончился за {CancelJoinSeconds} с после ухода вида");
        else if (_aiError is { } error)
            GD.PrintErr($"MapView: ход ИИ прерван: {error.GetType().Name}: {error.Message}");
        _aiThread = null;
    }

    private const int CancelJoinSeconds = 30;

    /// <summary>Автобой брошенного боя на главном потоке; исключение в нём — в журнал, а отпуск заявки и ожидание потока идут дальше
    /// (рабочий тогда получит null и бой сыграет ядро).</summary>
    private void ResolveQuietly(AiBattle battle)
    {
        try
        {
            battle.ResolveOnce();
        }
        catch (Exception e)
        {
            Failures.Report(this, "MapView.ResolveOnce", e);
        }
    }

    /// <summary>Крючки хода — на рабочем потоке. Вызов с другого потока (ход человека, вывод из города) пропускается:
    /// ждать главному потоку некого.</summary>
    private sealed class AiHooks(MapView view) : ITurnObserver
    {
        private bool OnWorker => Thread.CurrentThread == view._aiThread && !view._aiCancel;

        public void PlayerTurn(string playerId)
        {
            if (OnWorker) Ask(new AiPlayerRequest(playerId));
        }

        public void StackMoving(StackMove move)
        {
            if (!OnWorker) return;
            view._aiMoves++;
            var session = view.Session;
            if (session.State.Info.Humans.Contains(move.Owner, StringComparer.OrdinalIgnoreCase)) return;
            if (move.Cells.All(c => Hidden(session, c))) return;
            Ask(new AiMoveRequest(move));
        }

        public void Offered(AllianceOffer offer)
        {
            // Ответ здесь и сейчас — только человеку у ПК; другому человеку «горячего стула» предложение ждёт его хода.
            if (!OnWorker || view.AiOfferRequested is null
                || !offer.To.Equals(view.Viewer, StringComparison.OrdinalIgnoreCase)) return;
            Ask(new AiOfferRequest(offer));
        }

        public IReadOnlyList<TurnRecord>? Battle(BattleState state) => DecideBattle(state)?.Log;

        /// <summary>Запрос решения боя (<see cref="ITurnObserver.DecideBattle"/>): бой ИИ с человеком — экраном; ответ — журнал и действия
        /// экрана.</summary>
        public BattleDecision? DecideBattle(BattleState state)
        {
            if (!OnWorker) return null;
            view._aiBattles++;
            // Бой ИИ с ИИ — автобой ядра; на виду его уже показало начало боя (TurnSightKind.BattleStarted: камера и ABATTLE).
            if (view.AiBattleRequested is null || !HumanDefends(view.Session, state)) return null;
            var battle = new AiBattle(state, view);
            Ask(new AiBattleRequest(battle));
            var log = battle.Log ?? (view._aiCancel ? battle.ResolveOnce() : null);
            return log is null ? null : new BattleDecision([.. log], battle.Played?.Invoke());
        }

        /// <summary>Действие без хода по клеткам. На рабочем потоке (ход ИИ) — заявка, если видно и ходы ИИ показываются (при
        /// выключенном <c>PlayAIIsoAnim</c> движок команды ИИ не анимирует вовсе, <c>0x4f2b02</c>). На главном (литьё человека) —
        /// анимация литья без ожидания: эффект применяется сразу, картинка доигрывает поверх.</summary>
        public void Happened(TurnSight sight)
        {
            if (Thread.CurrentThread == view._mainThread)
            {
                // Действие человека в окне, захват, рост своей деревни — звук интерфейса (WindowSounds, docs/ai-screen.md §12); вор человека —
                // анимацией у цели (MapView.Input.cs).
                if (sight.Kind is not (TurnSightKind.Looted or TurnSightKind.LandSpread or TurnSightKind.NobleAction)
                    && D2.Data.Sound.WindowSounds.OfSight(sight, view.Session.Book) is { } actionSound)
                    GameAudio.Of(view).PlayFx(D2.Data.Sound.WindowSounds.Archive, actionSound);
                // Своя руина разграблена — входом или победой над её стражей: LOOTRUIN (CMidAnim2LootRuin); ENTRRUIN — окно «Вы победили!»
                // (MapScreen.Notices.cs). Журнал зонда 7.3 (20261016g-port-synth, карта 8): после боя у руины движок играет оба (до окна
                // предметов с TAKEBAG); у порта до 17.10 звучал только вход (VisitSound), после боя — тишина.
                if (sight.Kind == TurnSightKind.Looted) view.LootSound();
                // Растекание земли в начале хода человека (ход передаётся на главном потоке): в ход человека показ всегда (0x61a639), если
                // видна точка показа с окрестностью (0x4f4a0c → 0x4f4ce9).
                if (sight.Kind == TurnSightKind.LandSpread && Seen(view.Session, sight))
                    view.QueueEffect(() => view.ScrolledPlay((sight.X, sight.Y), onlyIfFar: true, () => view.PlayLandSpread(sight) > 0));
                // Несколько событий подряд (гибель нескольких отрядов в одном бою) — по очереди, не обрывая друг друга; перед каждым — вид к
                // месту (литьё — всегда, прочее — если дальше 2 клеток; 0x4f4a0c).
                var cell = (sight.X, sight.Y);
                if (sight.Kind == TurnSightKind.Spell && SpellPoint(sight) is { } at)
                    view.QueueEffect(() => view.ScrolledPlay((at.X, at.Y), onlyIfFar: false, () => view.ShowSpell(sight.Id, at.X, at.Y, at.Shake, 1f)));
                // Развеивание и туман: модель сменит туман сразу после этого вызова, показ — кольцами после литья (CMidAnim2ChangeFog).
                if (sight.Kind == TurnSightKind.Spell && view.SpellFog(sight.Id, sight.X, sight.Y) is { } fog)
                {
                    view.HoldFog(fog.X, fog.Y, fog.Radius);
                    view.QueueEffect(() => view.PlayFogChanges(derive: false));
                }
                if (sight.Kind == TurnSightKind.Death && Seen(view.Session, sight))
                    view.QueueEffect(() => view.ScrolledPlay(cell, onlyIfFar: true, () => view.PlayDeath(sight.Id, sight.X, sight.Y, tomb: sight.Tomb)));
                if (sight.Kind == TurnSightKind.Created && Seen(view.Session, sight))
                    view.QueueEffect(() => view.ScrolledPlay(cell, onlyIfFar: true, () => view.PlayCreate(sight.Id, sight.X, sight.Y)));
                if (sight.Kind == TurnSightKind.OwnerChanged && Seen(view.Session, sight))
                    view.QueueEffect(() => view.ScrolledPlay(cell, onlyIfFar: true,
                                                             () => view.PlayOwnerChange(sight.Id, sight.PlayerId, sight.X, sight.Y)));
                if (sight.Kind == TurnSightKind.MountainsRemoved && Seen(view.Session, sight))
                    view.QueueEffect(() => view.ScrolledPlay(cell, onlyIfFar: true, () => view.PlayMountains(sight.Id, sight.X, sight.Y)));
                return;
            }
            if (sight.Kind == TurnSightKind.EventPopup || D2.Data.Ui.Windows.NoticeWatch.IsMessage(sight.Kind))
            {
                // Попап человеку у ПК — сразу, ход ИИ ждёт «ОК» (клетки нет: −1, −1). Другому человеку «горячего стула» — не здесь.
                if (OnWorker && view.AiPopupRequested is not null && sight.PlayerId.Equals(view.Viewer, StringComparison.OrdinalIgnoreCase))
                    Ask(new AiPopupRequest(sight.PlayerId));
                return;
            }
            if (sight.Kind == TurnSightKind.BattleStarted)
            {
                // Бой на виду (CMidAnim2Battle): только ход ИИ с показом — свой бой человек видит из Attack; видно, если открыта клетка
                // рядом с нападающим (0x4f4ce9, та же таблица, что у литья).
                if (OnWorker && view.AiAnimate && view.Session.Map.Stacks.TryGetValue(sight.Id, out var attacker)
                    && SpellMapAnim.SeenFrom.Any(d => !Hidden(view.Session, (attacker.X + d.Dx, attacker.Y + d.Dy))))
                    Ask(new AiSightRequest(BattleKind, sight.Id, sight.X, sight.Y));
                return;
            }
            if (!OnWorker) return;
            // Захват (сообщение 30, 0x48b415): OCCUPY слышен всем и без показа ходов — сервер рассылает его, туман не проверяется (ранг 2).
            if (sight.Kind == TurnSightKind.Capture && sight.Occupy)   // деревня уже бывала у этой расы — без OCCUPY (0x434080)
                Callable.From(() => GameAudio.Of(view).PlayFx(D2.Data.Sound.WindowSounds.Archive, "OCCUPY")).CallDeferred();
            // Анимации карты — при показе ходов ИИ или в ход человека (0x61a639), и если место видно (0x4f4a0c).
            bool shown = view.AiAnimate || view.Session.State.Info.Humans.Contains(sight.PlayerId, StringComparer.OrdinalIgnoreCase);
            switch (sight.Kind)
            {
                case TurnSightKind.CityGrown or TurnSightKind.Looted:
                    if (shown && Seen(view.Session, sight))
                        Ask(new AiSightRequest(sight.Kind == TurnSightKind.CityGrown ? GrowKind : LootKind, sight.Id, sight.X, sight.Y));
                    return;
                case TurnSightKind.LandSpread:
                    // Одна точка на всё растекание (место источника или столица) с окрестностью, туман по клеткам не смотрится (0x4f4a0c);
                    // камера — к точке, пачки клеток — по группам (MapView.Land.cs).
                    if (shown && Seen(view.Session, sight))
                        Ask(new AiSightRequest(LandKind, sight.Cells.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), sight.X, sight.Y, sight));
                    return;
                case TurnSightKind.MountainsRemoved:
                    // Снос гор (CMidAnim2Mountain): видна хоть одна из клеток у центра (0x4f4a0c/0x4f4ce9); не видно — карта обновится
                    // со следующей перерисовкой.
                    if (shown && Seen(view.Session, sight)) Ask(new AiSightRequest(MountainKind, sight.Id, sight.X, sight.Y));
                    return;
                case TurnSightKind.NobleAction:
                    // Картинка — между вором и целью; в тумане у зрителя — ни картинки, ни звука (0x5f3eac, ранг 2: видна хоть одна из клеток).
                    if (shown && view.Session.Map.Stacks.TryGetValue(sight.Id, out var thief)
                        && (!Hidden(view.Session, (thief.X, thief.Y)) || !Hidden(view.Session, (sight.X, sight.Y))))
                        Ask(new AiSightRequest(NobleKind, sight.Id, sight.X, sight.Y));
                    return;
            }
            if (!view.AiAnimate || D2.Data.Sound.WindowSounds.IsWindowAction(sight.Kind)) return;
            if (sight.Kind == TurnSightKind.Spell)
            {
                if (SpellPoint(sight) is { } point) Ask(new AiSightRequest(point.Shake ? ShakeKind : SpellKind, sight.Id, point.X, point.Y, sight));
                return;
            }
            if (sight.Kind is TurnSightKind.Death or TurnSightKind.Created or TurnSightKind.OwnerChanged)
            {
                string kind = sight.Kind switch
                {
                    TurnSightKind.Death => sight.Tomb ? TombDeathKind : DeathKind, TurnSightKind.Created => CreateKind,
                    _ => $"{OwnerKind}:{sight.PlayerId}",
                };
                if (Seen(view.Session, sight)) Ask(new AiSightRequest(kind, sight.Id, sight.X, sight.Y));
                return;
            }
            if (Hidden(view.Session, (sight.X, sight.Y))) return;
            Ask(new AiSightRequest(sight.Kind == TurnSightKind.Capture ? "захват" : "наём", sight.Id, sight.X, sight.Y));
        }

        /// <summary>Где показывать литьё (<c>0x4f31d0</c>, <c>0x4f4ce9</c>): заклинание на всю карту (<c>AREA</c> 999) — у столицы
        /// заклинателя (её дальний угол), свои — всегда, чужое — если угол виден; прочее — в клетке литья, если видна хоть одна клетка
        /// рядом (<see cref="SpellMapAnim.SeenFrom"/>) или это туман и его развеивание. Чужая столица не видна — вид к своей столице и
        /// тряска экрана вместо анимации (<c>CMidAnim2Shake</c>, <see cref="Shake"/>). Не показывать — null.</summary>
        private (int X, int Y, bool Shake)? SpellPoint(TurnSight sight)
        {
            var session = view.Session;
            var map = session.Map;
            if (session.Book?.Spells.TryGetValue(sight.Id, out var spell) != true || spell is null) return null;
            if (spell.Area == WholeMap)
            {
                var capital = map.Sites.FirstOrDefault(s => s.Kind == D2.Rules.Map.SiteKind.Capital
                                                            && s.Owner.Equals(sight.PlayerId, StringComparison.OrdinalIgnoreCase));
                if (capital is null) return (sight.X, sight.Y, false);
                var corner = Corner(map, capital);
                bool own = string.Equals(sight.PlayerId, view.Viewer, StringComparison.OrdinalIgnoreCase);
                if (own || !Hidden(session, corner)) return (corner.X, corner.Y, false);
                var mine = map.Sites.FirstOrDefault(s => s.Kind == D2.Rules.Map.SiteKind.Capital
                                                         && s.Owner.Equals(view.Viewer, StringComparison.OrdinalIgnoreCase));
                return mine is null ? null : (Corner(map, mine).X, Corner(map, mine).Y, true);
            }
            bool fog = spell.Category is D2.Rules.Magic.SpellCategory.Fog or D2.Rules.Magic.SpellCategory.Unfog;
            return fog || SpellMapAnim.SeenFrom.Any(d => !Hidden(session, (sight.X + d.Dx, sight.Y + d.Dy))) ? (sight.X, sight.Y, false) : null;
        }

        /// <summary>Видна ли точка события у зрителя — открыта хоть одна клетка окна рядом (<c>0x4f4ce9</c>, как у литья).</summary>
        private bool Seen(GameSession session, TurnSight sight) =>
            SpellMapAnim.SeenFrom.Any(d => !Hidden(session, (sight.X + d.Dx, sight.Y + d.Dy)));

        /// <summary>Дальний угол пятна укрепления — позиция + размер − 1 (<c>0x5e9563</c>).</summary>
        private static (int X, int Y) Corner(D2.Rules.Map.MapState map, D2.Rules.Map.MapSite site) =>
            D2.Rules.Map.PathFinder.EntranceOf(map, site.Id) ?? (site.X, site.Y);

        private const int WholeMap = 999;

        /// <summary>Клетка под туманом зрителя (без зрителя — всё видно).</summary>
        private bool Hidden(GameSession session, (int X, int Y) c)
        {
            if (view.Viewer is not { } viewer) return false;
            var map = session.Map;
            return map.Players.TryGetValue(viewer, out var p) && map.Fog.TryGetValue(p.FogId, out var fog)
                   && (!map.Contains(c.X, c.Y) || fog[c.X, c.Y]);
        }

        /// <summary>Защищается человек: хозяин отряда или укрепления защиты — среди людей партии.</summary>
        private static bool HumanDefends(GameSession session, BattleState state)
        {
            var map = session.Map;
            string group = state.DefenderGroup;
            string owner = map.Stacks.TryGetValue(group, out var stack) ? stack.Owner
                : map.Sites.FirstOrDefault(s => s.Id.Equals(group, StringComparison.OrdinalIgnoreCase))?.Owner ?? "";
            return session.State.Info.Humans.Contains(owner, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Положить заявку и ждать, пока главный поток её покажет. Ожидание с опросом отмены: вид мог уйти.</summary>
        private void Ask(AiRequest request)
        {
            view._aiRequest = request;
            while (!view._aiResume.Wait(100))
                if (view._aiCancel) break;
        }
    }
}
