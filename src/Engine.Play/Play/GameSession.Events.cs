using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>События сценария в партии (<c>docs/scenario-events.md</c>): проверка в точках движка и применение эффектов.
/// Выбор события и кандидатов — <see cref="ScenarioEvents.Next"/>; здесь — цикл «пока срабатывают» и изменения карты.</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Проверка событий (<c>0x41f762</c> → <c>FilterAndProcessEvents</c>): начало хода игрока <paramref name="turnPlayer"/>
    /// (без триггерера) или действие отряда <paramref name="triggererId"/> (шаг, конец боя, вход). Событие за событием, пока
    /// срабатывают; каждое выполняется один раз за проверку. Эффекты применяются по порядку сразу после выбора события.
    /// Без событий или правил — ничего.
    /// </summary>
    /// <returns>Сработавшие события (<c>событие игрок</c>) — для отчёта дня и проверок.</returns>
    /// <param name="executed">Выполненные в этой проверке события — чтобы продолжить её (вход после шага того же хода);
    /// null — новая проверка.</param>
    public IReadOnlyList<string> RunEvents(string turnPlayer, string triggererId, List<string>? report = null,
                                           HashSet<string>? executed = null) =>
        Over ? [] : Check(turnPlayer, triggererId, report, executed);

    /// <summary>Проверка без оглядки на победу: победа объявляется в конце текущей команды (<c>0x421077</c> после обработчика команды,
    /// ранг 1), а флаг «окончена» проверки событий не читает — события и эффекты того же действия после <c>L_WIN</c> (в той же проверке
    /// и в проверках созданных и убитых эффектами отрядов) идут до конца; вторая победа не засчитывается (<see cref="Win"/>).</summary>
    private IReadOnlyList<string> Check(string turnPlayer, string triggererId, List<string>? report, HashSet<string>? executed)
    {
        if (Book is null || State.Events.Events.Count == 0) return [];
        var rules = new EventRules(Book.Economy.Races, Book.Diplomacy);
        executed ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report is not null && !_unsupportedReported)
        {
            // Условия вне таблицы движка (категории 1 и 13 у движка — NULL) ложны; видно в отчёте, а не молча.
            _unsupportedReported = true;
            foreach (var e in State.Events.Events.Where(e => e.Conditions.Any(c => c is UnsupportedCondition)))
                report.Add($"событие {e.Id}: условие {e.Conditions.First(c => c is UnsupportedCondition).Name} не поддержано — ложно");
        }
        // Вложенная проверка (конец боя эффекта L_BATTLE → FinishBattle) — свой кадр, как рекурсия 0x41f762 у движка: очередь T
        // и остановка внешней проверки ей не видны и после неё прежние.
        var outerTriggered = _triggered.ToList();
        var (outerStop, outerCell) = (_stop, _triggererCell);
        _triggered.Clear();
        try
        {
            return RunEventsLoop(turnPlayer, triggererId, report, executed, rules);
        }
        finally
        {
            _triggered.Clear();
            _triggered.AddRange(outerTriggered);
            (_stop, _triggererCell) = (outerStop, outerCell);
        }
    }

    /// <summary>
    /// Цели сценария глазами игрока <paramref name="playerId"/> (обычно — человек, чей ход): id объектов, которые экран карты обводит
    /// кругом <c>SYM_OBJECTIVE</c> — столицы и деревни, отряды вне крепостей, руины, площадки, мешки, области (<see cref="ScenarioObjectives.For"/>).
    /// Считается заново на каждый вызов из текущих событий и карты, как у движка при перерисовке. Без правил или событий — пусто.
    /// </summary>
    public IReadOnlyList<string> Objectives(string playerId) =>
        Book is null || State.Events.Events.Count == 0 ? []
        : ScenarioObjectives.For(Map, State.Events, State.EventProgress, new EventRules(Book.Economy.Races, Book.Diplomacy), playerId);

    private List<string> RunEventsLoop(string turnPlayer, string triggererId, List<string>? report, HashSet<string> executed,
                                       EventRules rules)
    {
        var fired = new List<string>();
        while (true)
        {
            var firings = ScenarioEvents.Next(State.Events, State.EventProgress, Map, rules, turnPlayer, triggererId, Rng, executed);
            if (firings.Count == 0) break;
            _stop = false;
            // Результаты эффектов движок создаёт до применения (0x4422e1): клетка триггерера для L_MOVE_BESIDE и L_BATTLE — его
            // позиция (в крепости — вход) на момент срабатывания, а не после эффектов раньше по списку (0x43d21b → 0x5ed9b2).
            _triggererCell = Map.Stacks.TryGetValue(triggererId, out var trig) ? PathFinder.Start(Map, trig) : null;
            foreach (var f in firings)
            {
                fired.Add($"{f.Event.Id} {f.PlayerId}");
                Fire(f, rules, report);
            }
            // Бой эффекта (CEffectBattle.stopProcessing) обрывает проверки; иначе новые и уничтоженные отряды эффектов — свои
            // проверки (0x41f762: T — созданные CEffectCreateStack и убитые CEffectKillStack; убитого нет — проверка без отряда).
            var triggered = _triggered.ToList();
            _triggered.Clear();
            if (_stop) break;
            // Рекурсия 0x41f8c9 передаёт «отряд в ходу» пустым.
            string playing = _playing;
            _playing = "";
            try { foreach (string id in triggered) fired.AddRange(Check("", Map.Stacks.ContainsKey(id) ? id : "", report, null)); }
            finally { _playing = playing; }
        }
        return fired;
    }

    /// <summary>Срабатывание события: у <c>OCCUR_ONCE</c> сначала выключение, затем эффекты по порядку.</summary>
    private void Fire(EventFiring f, EventRules rules, List<string>? report)
    {
        report?.Add($"событие {f.Event.Id} {f.PlayerId}");
        if (f.DisablesItself) Progress(State.EventProgress.Enable(f.Event.Id, false));
        for (int n = 0; n < f.Event.Effects.Count; n++)
            if (!ApplyEffect(f, n, rules) && report is not null)
                report.Add($"событие {f.Event.Id}: эффект {f.Event.Effects[n].Name} не перенесён");
        Observer?.EventFired(f);   // зонд порта: строка event
        // Начало партии (NewGame.Begin: события дня 1 до подписки зонда) — запомнить для строки event при подписке (Attached).
        if (_watchers.Count == 0 && Map.Turn == 1 && _unwatched.Count < MaxUnwatched) _unwatched.Add(f);
    }

    private const int MaxUnwatched = 256;

    private readonly List<EventFiring> _unwatched = [];

    /// <summary>Сработавшие в день 1 до первой подписки (<see cref="Watch"/>) — события старта партии (<c>NewGame.Begin</c>: их
    /// проверка идёт до того, как экран и зонд подписаны). Забираются один раз: зонд пишет их строками <c>event</c> при подписке — прежде
    /// условия старта (карта 9, C8w/C8p: L_DIPLOMACY в день 1) срабатывали, а в журнале порта их не было.</summary>
    public IReadOnlyList<EventFiring> TakeUnwatchedFirings()
    {
        var taken = _unwatched.ToList();
        _unwatched.Clear();
        return taken;
    }

    /// <summary>Отряды, созданные и уничтоженные эффектами текущего события, — триггеры следующих проверок.</summary>
    private readonly List<string> _triggered = [];

    /// <summary>Эффект события начал бой — дальнейшие проверки этого действия не идут.</summary>
    private bool _stop;

    private bool _unsupportedReported;

    /// <summary>Проверка после шага отряда (<see cref="MoveStack"/>): отряд, день, клетка и выполненные события. Вход в объект
    /// тем же ходом (<see cref="Enter"/>) продолжает её, а не начинает новую: у движка шаг и вход — один список эффектов хода
    /// и одна проверка (<c>StackMove 0x41e46b</c>).</summary>
    private (string Stack, int Turn, int X, int Y, HashSet<string> Executed)? _moveCheck;

    /// <summary>Проверка хода отряда <paramref name="stackId"/>; <paramref name="executed"/> — продолжение прежней проверки
    /// (<see cref="MoveCheckOf"/>), null — новая.</summary>
    internal void RunMoveEvents(string stackId, HashSet<string>? executed = null)
    {
        // Эффекты упреждающей проверки пути (MoveStack, pathEvents) применены вне RunEvents: их бой (CEffectBattle.stopProcessing)
        // обрывает и проверку после хода. Иначе очередь T верхнего уровня (0x41f762, StackMove 0x41e46b): сначала сам отряд
        // (он кладётся в T до применения эффектов, 0x41f7a7), затем созданные и убитые эффектами пути — у каждого свои
        // «выполненные»; «отряд в ходу» (playingStack) — этот отряд (получатель L_GIVE_ITEM, когда триггерера нет).
        var pending = _triggered.ToList();
        bool stopped = _stop;
        _triggered.Clear();
        _stop = false;
        if (stopped) return;
        _playing = stackId;
        try
        {
            if (Map.Stacks.TryGetValue(stackId, out var stack))
            {
                executed ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _moveCheck = (stackId, Map.Turn, stack.X, stack.Y, executed);
                RunEvents("", stackId, executed: executed);
            }
            foreach (string id in pending) RunEvents("", Map.Stacks.ContainsKey(id) ? id : "");
        }
        finally { _playing = ""; }
    }

    /// <summary>Проверка событий после отдельной команды отряда (<c>0x41f46e</c>, триггерер — отряд): действие вора <c>0x41e6e6</c> и его
    /// отмена <c>0x41e771</c>, «бросить мешок» <c>0x41f257</c>, постановка жезла <c>0x41f30d</c>, «Меню героя» после боя
    /// (<see cref="RequestPaperdoll"/>, <c>0x41f397</c>; <c>docs/scenario-events.md</c> §1.2). Новая проверка, а не продолжение хода:
    /// следующий вход тем же отрядом начнёт свою (<see cref="MoveCheckOf"/>).</summary>
    private void RunActionEvents(string stackId)
    {
        if (Book is null) return;
        RunMoveEvents(stackId);
        _moveCheck = null;
    }

    /// <summary>
    /// «Меню героя» после боя (<c>BTN_PAPERDOLL</c> → <c>closeBattleWindow(true)</c> <c>0x633e35</c> → <c>CTaskBattle</c> <c>0x4d856d</c>:
    /// <c>CRequestPaperdollMsg</c> с нападавшим отрядом, единственный отправитель — <c>0x4062ac</c>). Сервер (<c>RequestPaperdoll</c>
    /// <c>0x41f397</c>, обработчик <c>0x423983</c>) кладёт в список <c>CEffectPaperdoll</c> (<c>0x43aa7f</c>: хозяину отряда
    /// <c>CCmdStackActionMsg</c>(отряд, 0) — окно отряда, а в укреплении — окно укрепления, клиент <c>0x48ee26</c>) и проверяет события
    /// с триггерером-отрядом (<c>0x41f46e</c>) — как у «бросить мешок». Окно сцена открывает сама; ложь — отряда нет, ни окна, ни проверки.
    /// Переключатель куклы в окнах отряда сервер не зовёт (<c>TOG_PAPERDOLL</c> — местный, отправитель запроса один).
    /// </summary>
    public bool RequestPaperdoll(string stackId)
    {
        ArgumentNullException.ThrowIfNull(stackId);
        if (!Map.Stacks.ContainsKey(stackId)) return false;
        RunActionEvents(stackId);
        return true;
    }

    /// <summary>Отряд, чей ход сейчас проверяется на верхнем уровне (<c>playingStack</c>, только <c>StackMove</c>); пусто — нет.</summary>
    private string _playing = "";

    /// <summary>Клетка триггерера на момент срабатывания текущих событий (<c>PathFinder.Start</c>); null — триггерера нет.</summary>
    private (int X, int Y)? _triggererCell;

    /// <summary>Клетка триггерера для эффекта: снимок срабатывания или (эффекты пути в <see cref="MoveStack"/>) текущая.</summary>
    private (int X, int Y)? TriggererCell(MapStack? triggerer) =>
        _triggererCell ?? (triggerer is null ? null : PathFinder.Start(Map, triggerer));

    /// <summary>Выполненные события проверки последнего шага, если отряд с тех пор не сдвигался (тот же день и клетка) —
    /// вход сразу после шага продолжает её; иначе null.</summary>
    internal HashSet<string>? MoveCheckOf(MapStack stack) =>
        _moveCheck is { } last && last.Stack.Equals(stack.Id, StringComparison.OrdinalIgnoreCase) && last.Turn == Map.Turn
        && last.X == stack.X && last.Y == stack.Y ? last.Executed : null;

    /// <summary>Попапы событий для игрока — снимаются с очереди (окно их показало).</summary>
    public IReadOnlyList<EventMessage> TakeMessages(string playerId)
    {
        var mine = State.Messages.Where(m => m.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count > 0) State = State with { Messages = [.. State.Messages.Except(mine)] };
        return mine;
    }

    /// <summary>Отряд-триггерер выполнения, если он ещё на карте.</summary>
    private MapStack? TriggererOf(EventFiring f) => f.TriggererId.Length > 0 ? Map.Stacks.GetValueOrDefault(f.TriggererId) : null;

    private void Progress(EventProgress progress) => State = State with { EventProgress = progress };

    /// <summary>Один эффект события для кандидата. Ложь — эффект ещё не перенесён (виден в отчёте дня, а не молчит).</summary>
    private bool ApplyEffect(EventFiring f, int n, EventRules rules)
    {
        var effect = f.Event.Effects[n];
        switch (effect)
        {
            case PopupEffect p:
            {
                // CEffectPopup 0x43b290: запись в квест-лог каждому получателю и сообщение ему; BOOLVALUE — в сообщении отряд-триггерер,
                // окно показывает имя и портрет его лидера (0x4bd3ae…0x4bd50b, ранг 1).
                var (leaderName, leaderType, ownName) = p.Flag ? PopupLeader(f.TriggererId) : ("", "", false);
                foreach (string to in ScenarioEvents.PopupRecipients(p, f.Event, f.PlayerId, Map, rules))
                {
                    Progress(State.EventProgress.Logged(to, Map.Turn, f.Event.Id, n));
                    // «Горячий стул»: клиент показывает попап только местному игроку — человеку, чей ход начался последним (в ходе ИИ —
                    // тот, кто ходил перед ним; 0x4177bf → 0x403834, фильтр 0x489536); попап другому человеку пропадает, остаётся
                    // запись в журнале заданий (решение пользователя 28.09 — как в оригинале).
                    if (State.Info.Humans.Contains(to, StringComparer.OrdinalIgnoreCase)
                        && (!Hotseat || to.Equals(ActiveHuman, StringComparison.OrdinalIgnoreCase)))
                    {
                        State = State with { Messages = [.. State.Messages, new EventMessage(to, Map.Turn, f.Event.Id, p, leaderName, leaderType, ownName)] };
                        Observer?.Happened(new TurnSight(TurnSightKind.EventPopup, to, -1, -1, f.Event.Id));   // показ сразу — и в ходе ИИ
                    }
                }
                return true;
            }
            case EnableEventEffect e:
                Progress(State.EventProgress.Enable(e.EventId, e.Enable));
                return true;
            case ModifyVariableEffect v:
                Progress(State.EventProgress.Modify(v));
                return true;
            case ChangeObjectiveEffect o:
                Progress(State.EventProgress with { Objective = o.Text });
                return true;
            case WinEffect w:
                // 0x43f7d6: WIN_SCEN 1 — победитель ID_PLAYER1, 0 — победителя нет.
                Win(w.Win && w.PlayerId.Length > 0 ? w.PlayerId : GameInfo.NoWinner);
                return true;
            case ChangeOrderEffect o:
                ChangeOrders(o);
                return true;
            case RemoveStackEffect r:
                foreach (var s in Targets(r.StackId, r.FirstOnly))
                {
                    RemoveStack(s.Id, killer: Neutral());
                    _triggered.Add(s.Id);   // убитый отряд — проверка без отряда (CEffectKillStack → T)
                }
                return true;
            case GiveSpellEffect g:
                GiveSpell(f.PlayerId, g.SpellId, f.Event.Id);
                return true;
            case CreateStackEffect c:
                return CreateStack(c);
            case CastSpellOnTriggererEffect c when TriggererOf(f) is var trig:
                return trig is null || CastByEvent(c.SpellId, c.PlayerId, trig.X, trig.Y, trig, onTriggerer: true);
            case CastSpellAtEffect c:
                return State.Events.Zones.GetValueOrDefault(c.LocationId) is not { } at
                       || CastByEvent(c.SpellId, c.PlayerId, at.X, at.Y, TriggererOf(f), onTriggerer: false);
            case RemoveLandmarkEffect r:
                return RemoveLandmark(r);
            case ChangeLandmarkEffect c:
                return ChangeLandmark(c);
            case UnfogEffect u:
                return Unfog(u);
            case ChangeOwnerEffect c:
                return ChangeOwner(c, TriggererOf(f));
            case RemoveMountainsEffect m:
                return RemoveMountains(m, f.PlayerId);
            case MoveStackEffect m:
            {
                string who = m.Flag ? f.TriggererId : Targets(m.StackId, firstOnly: true).FirstOrDefault()?.Id ?? "";
                return who.Length == 0 || State.Events.Zones.GetValueOrDefault(m.LocationId) is not { } to || MoveByEvent(who, to.X, to.Y);
            }
            case MoveBesideEffect m:
                return TriggererOf(f) is not { } near || Targets(m.StackId, firstOnly: true).FirstOrDefault() is not { } mover
                       || TriggererCell(near) is not { } cell || MoveByEvent(mover.Id, cell.X, cell.Y, beside: true);
            case GiveItemEffect g:
                return GiveItem(g, f.PlayerId, TriggererOf(f) ?? Map.Stacks.GetValueOrDefault(_playing), f.Event.Id);
            case DestroyItemEffect d:
                return DestroyItem(d, TriggererOf(f));
            case BattleEffect b:
                return BattleByEvent(b, TriggererOf(f));
            case ChangeTerrainEffect c:
                return ChangeTerrain(c);
            case DiplomacyEffect d:
                return DiplomacyByEvent(d);
            case AllyEffect a:
                return AllyByEvent(a);
            default:
                return false;
        }
    }

    /// <summary>Отряды эффекта (<c>0x43f3ae</c>): id отряда — он сам, если есть; id шаблона — все его экземпляры, при
    /// <c>FIRST_ONLY</c> — первый (порядок обхода — по id, ранг 2).</summary>
    private IReadOnlyList<MapStack> Targets(string id, bool firstOnly)
    {
        if (id.Length >= 6 && id.AsSpan(4, 2).Equals("KC", StringComparison.OrdinalIgnoreCase))
            return Map.Stacks.TryGetValue(id, out var s) ? [s] : [];
        var all = Map.Stacks.Values.Where(x => x.TemplateId.Equals(id, StringComparison.OrdinalIgnoreCase))
                                   .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        return firstOnly ? [.. all.Take(1)] : all;
    }

    /// <summary><c>L_CHANGE_ORDER</c> (<c>0x43642f</c>): приказ и его цель; «обычный» у нейтрала становится «стоять»; у подрасы
    /// категории <c>L_NEUTRAL</c> (<c>[0x837b00]</c>) «защита отряда» и «охрана города» — «стоять» без цели (<c>0x436576</c>). Приказ
    /// ставится через проверку <c>0x60ed92</c> (<see cref="OrderAllowed"/>, <c>0x5e811a</c>): не прошёл — прежний. Пустая цель — и
    /// <c>G000000000</c> файла: до 16.10 он доходил до проверки как непустая цель, и «бродить» без цели (E16 карты 3) отвергался, а у
    /// движка приказ ставится (<c>20261016b-port-synth</c>, сохранение дня 1: KC0015 приказ 7).</summary>
    private void ChangeOrders(ChangeOrderEffect o)
    {
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var s in Targets(o.StackId, o.FirstOnly))
        {
            var (order, target) = (o.Order, o.TargetId.Equals("G000000000", StringComparison.OrdinalIgnoreCase) ? "" : o.TargetId);
            if (order == (int)StackOrder.Normal && IsNeutral(s.Owner)) order = (int)StackOrder.Stand;
            if (NeutralSubrace(s) && order is (int)StackOrder.DefendStack or (int)StackOrder.SecureCity) (order, target) = ((int)StackOrder.Stand, "");
            if (!OrderAllowed(s, (StackOrder)order, target, Map)) continue;
            stacks[s.Id] = s with { Order = order, OrderTarget = target };
        }
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
    }

    private bool IsNeutral(string playerId) =>
        Map.Players.TryGetValue(playerId, out var p) && Book!.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Neutral;

    private string Neutral() => Map.Players.Values.FirstOrDefault(p => IsNeutral(p.Id))?.Id ?? "";

    /// <summary>Отряд снят с карты (<c>CEffectKillStack</c>): вещи — отряду-убийце <paramref name="killerGroup"/>, без него — мешком
    /// (<see cref="Loot"/>; эффект события — без сообщения 44, флаг <c>+0x10</c>); юниты, план, картинка; запись в уничтоженные
    /// (<c>CMidStackDestroyed</c>, по ней <c>L_KILL_STACK</c>).</summary>
    internal void RemoveStack(string stackId, string killer, string killerGroup = "")
    {
        LootNow(stackId, killerGroup, announce: killerGroup.Length > 0);
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return;
        var stacks = Map.Stacks.Where(p => !p.Key.Equals(stackId, StringComparison.OrdinalIgnoreCase))
                               .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var units = Map.Units.Where(u => !stack.UnitIds.Contains(u.Key, StringComparer.OrdinalIgnoreCase))
                             .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var sites = Map.Sites.Select(s => s.StackId.Equals(stackId, StringComparison.OrdinalIgnoreCase) ? s with { StackId = "" } : s).ToList();
        var map = With(Map, stacks: stacks, units: units, plan: PlanWithout(Map, stackId), sites: sites);
        map = WithDestroyed(map, [.. map.Destroyed, new DestroyedStack(stack.Id, killer, stack.TemplateId)]);
        State = State with
        {
            Looks = Retold(Looks, map, [.. Looks.Stacks.Where(s => !s.Id.Equals(stackId, StringComparison.OrdinalIgnoreCase))])
                    with { Forts = FortLooks(Looks.Forts, map) },
        };
    }

    /// <summary>
    /// Помощники погибших отрядов (<c>CEffectKillStack</c> → <c>0x438d31</c>, вызов <c>0x4389ac</c>): обход всех отрядов карты
    /// (<c>0x419aac</c>/<c>0x419b52</c>); у кого действующий приказ (<c>+0xa8</c> = <c>L_NORMAL</c> — приказ ИИ <c>+0xb0</c>, иначе
    /// основной <c>+0xa0</c>) — <c>L_ASSIST</c>, а его цель (<c>+0xbc</c>/<c>+0xac</c>) среди погибших, тому приказ ИИ ставится
    /// <c>L_ASSIST</c> без цели (<c>0x5e811a</c>, флаг 1; <c>ai_order</c> с возвратом <c>0x438e23</c>). Без цели такого помощника тут же
    /// назначает наблюдатель тактики своего хода (<see cref="TacticTurn"/>, <c>0x45603b</c>). Ранг 1 — листинг и журнал карты 23
    /// (<c>20261020-port-synth</c>, день 58): <c>S143KC0007</c> пал в бою с <c>S143KC000a</c> — <c>ai_order</c> помощников
    /// <c>S143KC0002</c>, <c>S143KC0049</c> без цели (seq 9484052), назначение к <c>S143KC0017</c> с причиной <c>ai_45603b</c>
    /// (seq 9484088) и их <c>AssistMove</c> к нему тем же ходом. Прежде порт оставлял помощникам цель-покойника, и до утра они не ходили
    /// (реестр И151).
    /// </summary>
    private static IReadOnlyDictionary<string, MapStack> ReleasedHelpers(IReadOnlyDictionary<string, MapStack> stacks, IReadOnlyList<string> killed)
    {
        if (killed.Count == 0) return stacks;
        bool Freed(MapStack s) => AiTactics.OrderOf(s) is (StackOrder.Assist, var t) && t.Length > 0 && killed.Any(k => Same(k, t));
        if (!stacks.Values.Any(Freed)) return stacks;
        return stacks.ToDictionary(p => p.Key, p => Freed(p.Value) ? p.Value with { AiOrder = (int)StackOrder.Assist, AiOrderTarget = "" } : p.Value,
                                   StringComparer.OrdinalIgnoreCase);
    }

    /// <summary><c>L_GIVE_SPELL</c> (<c>CEffectGiveSpell</c> <c>0x438762</c>, <c>canApply</c> <c>0x60c104</c>, ранг 1): кандидату, если заклинание есть
    /// в таблице и ещё не известно; ни расы, ни уровня движок не смотрит.</summary>
    /// <summary><c>L_GIVE_SPELL</c> (<c>CEffectGiveSpell</c> <c>0x438762</c>): заклинание — кандидату в известные (есть в таблице, ещё не
    /// известно); затем сообщение 43 (<c>0x47ab13</c>) — окно полученного (<see cref="TurnSightKind.SpellsGiven"/>; клетка — его столица,
    /// объект — событие <paramref name="eventId"/>).</summary>
    private void GiveSpell(string playerId, string spellId, string eventId)
    {
        if (!Map.Players.TryGetValue(playerId, out var player) || player.KnownSpells.Contains(spellId, StringComparer.OrdinalIgnoreCase)
            || !RequireBook().Spells.ContainsKey(spellId)) return;
        var players = Map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[playerId] = player with { KnownSpells = [.. player.KnownSpells, spellId.ToUpperInvariant()] };
        State = State with { Looks = Retold(Looks, With(Map, players: players), Looks.Stacks) };
        var capital = Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && Same(s.Owner, playerId));
        Given(TurnSightKind.SpellsGiven, playerId, capital?.X ?? 0, capital?.Y ?? 0, eventId, [spellId.ToUpperInvariant()]);
    }

    /// <summary>Уничтоженные отряды карты — <paramref name="destroyed"/> (прежние и новые). Помощники новых погибших отпускаются
    /// (<see cref="ReleasedHelpers"/>).</summary>
    private static MapState WithDestroyed(MapState map, IReadOnlyList<DestroyedStack> destroyed) => new()
    {
        Size = map.Size, Tiles = map.Tiles, Stacks = ReleasedHelpers(map.Stacks, [.. destroyed.Skip(map.Destroyed.Count).Select(d => d.StackId)]),
        Units = map.Units, Players = map.Players,
        Objects = map.Objects, Plan = map.Plan, Fog = map.Fog, Turn = map.Turn, Rods = map.Rods, Sites = map.Sites,
        Relations = map.Relations, TalismanCharges = map.TalismanCharges, SpellCasts = map.SpellCasts,
        ItemCasts = map.ItemCasts, SpellEffects = map.SpellEffects, ItemTypes = map.ItemTypes, Limits = map.Limits, MapSeed = map.MapSeed, Subraces = map.Subraces,
        Destroyed = destroyed, Tombs = map.Tombs,
        IdNext = map.IdNext,
    };
}
