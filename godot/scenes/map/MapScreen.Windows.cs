using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Map;
using D2.Data.Menu;
using D2.Data.Play;
using D2.Data.Ui;
using D2.Data.Ui.Windows;
using D2.Rules.Map;
using Godot;

namespace D2.Godot;

/// <summary>Окна поверх карты. Стек — навигатор графа экранов с корнем <c>DLG_ISO_PAL</c>: настройки, запись и загрузка
/// открываются переходами графа, окна игры (армия города, найм, вопросы) — ещё и по решению игры (вход в свой город,
/// просьбы логики окна: <see cref="UiRequest"/>). Пока окно открыто, карта и панели ввода не получают. Окно меньше
/// экрана 800×600 стоит по его середине в том же масштабе, что и экран карты.</summary>
public partial class MapScreen
{
    private UiNavigator? _dialogs;
    private readonly List<UiScreenView> _dialogViews = new();

    /// <summary>Логика окон по стеку над корнем (индекс — место в стеке); null — окно без логики.</summary>
    private readonly List<IScreenLogic?> _logics = new();
    private bool _dialogsStale;

    /// <summary>Последнее окно записи — для проверок.</summary>
    private SaveGameLogic? _saving;

    private WindowContext? _windowContext;

    private UnitFaces? _faces;

    /// <summary>Портреты юнитов по базовому типу (<c>Gunits.BASE_UNIT</c>) — общие у окон и панели команд.</summary>
    private UnitFaces Faces => _faces ??= UnitFaces.Load(GameData.Rules, name => GameData.Content.Gfx.Image(UnitFaces.Archive, name) is not null);

    private UiNavigator Dialogs => _dialogs ??= new UiNavigator(GameData.Content.Flow, "DLG_ISO_PAL", UiPhase.Game);

    /// <summary>Окна партии видят её глазами игрока; без игрока (наблюдатель) окон города нет.</summary>
    private WindowContext? Windows =>
        _map.Viewer is { } viewer && _map.Session.Book is not null
            ? _windowContext ??= new WindowContext(_map.Session, viewer, _strings, Faces)
            {
                Rules = RuleTexts, LeaderNames = LeaderNameList.Load(GameData.FromGame("data/ui"), GameData.Language),
                ItemTexts = _stackTexts ??= StackTexts.Load(GameData.Rules),
            }
            : null;

    /// <summary>Карточка юнита в бою с карты — та же, что на карте (<see cref="UnitCardLogic.InBattle"/>): опыт «N / до уровня», иммунитеты
    /// и стойкости. Без зрителя-игрока или для юнита не с карты — null, экран боя строит простую карточку.</summary>
    private Func<D2.Rules.Battle.BattleUnit, IScreenLogic?> BattleCard() => unit =>
        Windows is { } context ? UnitCardLogic.InBattle(context, unit.Id, unit.Type.Id, unit.Type.Level, unit.Hp, unit.Xp) : null;

    /// <summary>Значки повышения и высокого уровня у портретов панели боя — по юниту карты, как в окнах отряда.</summary>
    private Func<D2.Rules.Battle.BattleUnit, (bool Upgrade, int HighLevel)> BattleLevelMarks() => unit =>
        Windows is { } context ? UnitCellsView.BattleLevelMarks(context, unit.Id) : (false, 0);

    private void OnWindowEvents(UiScreenView from, IReadOnlyList<UiEvent> events)
    {
        var nav = Dialogs;
        foreach (var e in events)
        {
            if (_dialogsStale) break;
            string? element = e switch { UiClicked c => c.Element, UiToggled tog => tog.Element, UiListConfirmed l => l.Element, _ => null };
            if (element is null || from.Hosted != nav.Top) continue;
            if (from.Logic is { } logic && !logic.Allows(element, from.State)) continue;
            // Выход в меню: в игре — окно подтверждения X005TA0089; здесь сразу.
            if (from.Hosted == "DLG_OPTIONS" && element == "BTN_QUIT")
            {
                _leaving = true;
                QuitRequested?.Invoke();
                return;
            }
            // «Задание» — брифинг поверх карты окном, а не переход графа (Goto сменил бы корень окон).
            if (from.Hosted is MapPalette.Pal or MapPalette.LandScreen && element == "BTN_REPORT")
            {
                OpenBriefing();
                continue;
            }
            if (nav.Activate(element) is not { } t) continue;
            GD.Print($"dialog {t.Action} {t.From}.{t.Element} -> {t.Target ?? "-"}");
            switch (t.Action)
            {
                case UiFlowAction.Open:
                    // Окно сообщения перехода графа — со своим текстом (строка перехода), одна «ОК».
                    _logics.Add(t.Target == MessageBoxLogic.Screen && t.Message is { } message ? new MessageBoxLogic(Text(message), null) : null);
                    break;
                case UiFlowAction.Goto: _logics[^1] = null; break;
                case UiFlowAction.Close: _logics.RemoveAt(_logics.Count - 1); break;
            }
            if (t.Action is UiFlowAction.Open or UiFlowAction.Goto or UiFlowAction.Close) _dialogsStale = true;
        }
        Drain(from);
        if (_dialogsStale) Callable.From(SyncDialogs).CallDeferred();
    }

    /// <summary>Открыть окно поверх остальных (вход в город, просьба логики).</summary>
    private void OpenWindow(string screen, IScreenLogic? logic)
    {
        Dialogs.Push(screen);
        _logics.Add(logic);
        if (_dialogsStale) return;
        _dialogsStale = true;
        Callable.From(SyncDialogs).CallDeferred();
    }

    /// <summary>Просьбы логики окна к хозяину: вопрос и сообщение — окно <c>DLG_MESSAGE_BOX</c>, открыть — окно поверх,
    /// закрыть — снять окно, если оно верхнее.</summary>
    private void Drain(UiScreenView view)
    {
        if (view.Logic is not IWindowLogic window) return;
        while (window.TakeRequest() is { } request)
        {
            switch (request)
            {
                case UiAsk ask: OpenWindow(MessageBoxLogic.Screen, MessageBoxLogic.For(ask)); break;
                case UiTell tell: OpenWindow(MessageBoxLogic.Screen, MessageBoxLogic.For(tell)); break;
                case UiOpen open: OpenWindow(open.Screen, open.Logic); break;
                case UiFocus focus: Callable.From(() => FocusStack(focus.StackId)).CallDeferred(); break;
                case UiCast cast: Callable.From(() => StartAim(cast.SpellId, cast.StackId, cast.ItemId)).CallDeferred(); break;
                case UiDrop drop: Callable.From(() => StartDrop(drop.StackId)).CallDeferred(); break;
                case UiClose when _logics.Count > 0 && ReferenceEquals(_logics[^1], view.Logic):
                    if (view.Logic is ThiefActionsLogic { DropsRoute: true }) _map.DropRoute();   // итог «Разведать руины» (0x4d23a2, Ин82)
                    Dialogs.Pop();
                    _logics.RemoveAt(_logics.Count - 1);
                    if (!_dialogsStale) Callable.From(SyncDialogs).CallDeferred();
                    _dialogsStale = true;
                    break;
            }
        }
    }

    /// <summary>Узлы окон — по стеку навигатора (над корнем). Снимаются и строятся на следующем кадре: просьба
    /// пришла из обработчика ввода окна, которое сейчас может уйти. Окно, снова ставшее верхним, обновляет содержимое
    /// (<see cref="IWindowLogic.Resume"/>) и отдаёт отложенные просьбы; карта после закрытия окон перечитывается.</summary>
    private void SyncDialogs()
    {
        _dialogsStale = false;
        var want = Dialogs.Stack.Skip(1).ToList();
        int keep = 0;
        while (keep < _dialogViews.Count && keep < want.Count && _dialogViews[keep].Hosted == want[keep]
               && ReferenceEquals(_dialogViews[keep].Logic, _logics[keep])) keep++;
        bool closed = keep < _dialogViews.Count;
        for (int i = _dialogViews.Count - 1; i >= keep; i--)
        {
            if (_dialogViews[i].Hosted is { } hosted) WindowSound(hosted, opened: false);   // звук окна — MapScreen.Sounds.cs (сессия Г)
            RemoveChild(_dialogViews[i]);
            _dialogViews[i].QueueFree();
            _dialogViews.RemoveAt(i);
        }
        for (int i = keep; i < want.Count; i++)
        {
            var logic = _logics[i] ?? DialogLogic(want[i]);
            _logics[i] = logic;
            var view = new UiScreenView { Hosted = want[i], Name = want[i], SlotHost = _host, Logic = logic };
            if (Centered(want[i]) is { } at) view.SlotAt = at;
            view.Fired += events => OnWindowEvents(view, events);
            view.DragCursorChanged += cursor => DragCursor(view, cursor);
            view.Pointed += () =>
            {
                Drain(view);
                if (_dialogsStale) Callable.From(SyncDialogs).CallDeferred();
            };
            AddChild(view);
            _dialogViews.Add(view);
            WindowSound(want[i], opened: true);
        }
        if (DropFailed(keep)) return;
        for (int i = 0; i < _dialogViews.Count; i++) _dialogViews[i].SetProcessUnhandledInput(i == _dialogViews.Count - 1);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        // Просьбы, сделанные логикой при открытии (площадке нечего предложить, нет места), — сразу.
        if (want.Count > keep && _dialogViews.Count > 0)
        {
            Drain(_dialogViews[^1]);
            if (_dialogsStale) Callable.From(SyncDialogs).CallDeferred();
        }
        CourierOffers();   // предложение человека человеку — гонцом сразу (MapScreen.Courier.cs)
        if (!closed) return;
        _map.Reload();
        SyncPanels();
        if (want.Count == keep && keep > 0 && _dialogViews[^1] is { Logic: IWindowLogic top } view2)
        {
            top.Resume(view2.State);
            view2.Sync();
            Drain(view2);
            if (_dialogsStale) Callable.From(SyncDialogs).CallDeferred();
        }
    }

    /// <summary>Окно, чья логика упала при открытии (<see cref="UiScreenView.OpenFailed"/>: разметка мода без нужного элемента), снимается
    /// вместе с окнами над ним, а не остаётся модальным без кнопки — так игра стояла в прогоне 15.10 (пометка 22). У движка такого окна не
    /// бывает: разметку он читает свою.</summary>
    private bool DropFailed(int from)
    {
        int failed = _dialogViews.FindIndex(from, v => v.OpenFailed);
        if (failed < 0) return false;
        GD.PrintErr($"MapScreen: окно {_dialogViews[failed].Hosted} не открылось (логика упала) — снято");
        while (_logics.Count > failed)
        {
            Dialogs.Pop();
            _logics.RemoveAt(_logics.Count - 1);
        }
        _dialogsStale = true;
        Callable.From(SyncDialogs).CallDeferred();
        return true;
    }

    /// <summary>Курсор тащения в окне: тащат — картинка тащимого по середине курсора (<c>DEFAULT</c>, над местом, которое не примет, —
    /// <c>NODRAGDROP</c>; <c>0x56efc5</c>/<c>0x56f025</c>), инструмент — его курсор, иначе курсор окна из разметки (кинжал, <c>0x5308d5</c>).
    /// ponytail: имена основы курсоров тащения движка (<c>CDDUnitGroup</c> <c>+0x44</c>/<c>+0x4c</c>) не дочитаны — взяты <c>DEFAULT</c> и
    /// <c>NODRAGDROP</c>, ранг 2.</summary>
    private void DragCursor(UiScreenView view, string? cursor)
    {
        if (view.DragImage is { } image) Cursor.ShowWith("IsoCursr", cursor ?? "DEFAULT", image, view.DragImageKey);
        else if (cursor is not null) Cursor.Show(cursor);
        else ShowWindowCursor(view);
    }

    /// <summary>Левый верхний угол окна меньше экрана карты — по середине 800×600; во весь экран — null.</summary>
    private Vector2? Centered(string screen)
    {
        var design = GameData.Content.Screens.FirstOrDefault(s => s.Id == screen)?.Design;
        if (design is not { } d || (d.Width >= _host.X && d.Height >= _host.Y)) return null;
        return new Vector2(MathF.Floor((_host.X - d.Width) / 2f), MathF.Floor((_host.Y - d.Height) / 2f));
    }

    private IScreenLogic? DialogLogic(string id)
    {
        string dir = UiFlowView.SavesDir(OS.GetCmdlineUserArgs());
        var session = _map.Session;
        return id switch
        {
            "DLG_SAVE_GAME" => _saving = new SaveGameLogic(dir, session.Snapshot, AutoSave.Title(session.State)),
            "DLG_OPTIONS" => OptionsLogic(),   // показ карты: скорости и ходы ИИ (MapScreen.AiTurn.cs)
            "DLG_LOAD_GAME" => new LoadGameLogic(dir, path =>
            {
                if (_leaving) return;
                _leaving = true;
                GD.Print($"dialog load {path}");
                LoadRequested?.Invoke(path);
            }),
            QuestLogLogic.Screen when Windows is { } context => new QuestLogLogic(context),
            _ => null,
        };
    }

    /// <summary>«Задание» (<c>BTN_REPORT</c>, F1): брифинг сценария партии поверх карты с текущей целью (<see cref="GameBriefingLogic"/>).
    /// Сценарий — из каталога по <c>ScenarioId</c>; карта из файла (каталога нет) — только имя.</summary>
    private void OpenBriefing()
    {
        var info = _map.Session.State.Info;
        var scenario = GameData.Catalog.Scenarios.FirstOrDefault(s => s.Id.Equals(info.ScenarioId, StringComparison.OrdinalIgnoreCase))
                       ?? new D2.Data.Game.ScenarioInfo { Id = info.ScenarioId.Length > 0 ? info.ScenarioId : info.ScenarioName, File = "", Name = info.ScenarioName };
        OpenWindow(GameBriefingLogic.Screen, new GameBriefingLogic(Text, scenario, _map.Session.State.EventProgress.Objective));
    }

    /// <summary>Отряд вошёл в своё укрепление: столица — панель столицы, деревня — окно армии.</summary>
    private void OnEntered(string siteId)
    {
        var site = _map.Map.Sites.FirstOrDefault(s => s.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase));
        if (site is null || Windows is not { } context) return;
        if (RiotRefused(site)) return;
        if (site.Kind == SiteKind.Capital) OpenCapital(site.Id);
        else if (site.Kind == SiteKind.Village) OpenWindow(CityStackLogic.Screen, new CityStackLogic(context, site.Id, GameData.Content.Screens));
    }

    /// <summary>Своя деревня в бунте: вместо окна — «Пока город бунтует, в нем не могут быть произведены никакие действия.»
    /// (<c>X005TA0689</c>; прогон 13.10, ai-q22 — 7 раз). true — окно сообщения открыто.</summary>
    private bool RiotRefused(MapSite site)
    {
        if (site.Kind != SiteKind.Village || !Fortifications.Rioting(site.RiotTurn)) return false;
        OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(Text("X005TA0689"), null));
        return true;
    }

    /// <summary>Окно армии своего укрепления (<c>CCityStackInterf</c>) — щелчок по укреплению с выбранным гостем (<c>0x4d0e2c</c>) или по
    /// портрету гостя в панели столицы (<c>0x4867ca</c>).</summary>
    private void OpenCityArmy(string siteId)
    {
        if (Windows is not { } context || _dialogViews.Count > 0) return;
        if (_map.Map.Sites.FirstOrDefault(s => s.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase)) is { } site && RiotRefused(site)) return;
        OpenWindow(CityStackLogic.Screen, new CityStackLogic(context, siteId, GameData.Content.Screens));
    }

    /// <summary>Отпускание мыши на портрете героя панели команд (<c>0x49072c</c>): выбран отряд — окно отряда
    /// <c>DLG_MANAGE_STACK</c>, отряд в укреплении — окно этого укрепления.</summary>
    private void PanelReleased(int x, int y)
    {
        var leader = GameData.Content.Screens.First(s => s.Id == "DLG_STRATEGIC").Elements.First(e => e.Id == "IMG_LEADER").Rect;
        if (x < leader.X || x >= leader.Right || y < leader.Y || y >= leader.Bottom) return;
        if (_map.SelectedStack is not { } stack || Windows is not { } context || _dialogViews.Count > 0) return;
        if (!stack.Owner.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase)) return;
        if (stack.Inside.Length > 0)
        {
            OnEntered(stack.Inside);
            return;
        }
        OpenWindow(ManageStackLogic.Screen, new ManageStackLogic(context, stack.Id, GameData.Content.Screens,
                                                                 _stackTexts ??= StackTexts.Load(GameData.Rules)));
    }

    private StackTexts? _stackTexts;

    /// <summary>«Меню героя» после боя (<see cref="BattleScreen.Paperdoll"/>): запрос серверу (<see cref="D2.Data.Play.GameSession.RequestPaperdoll"/>
    /// — проверка событий с отрядом-триггерером) и ответ <c>CCmdStackActionMsg</c>(отряд, 0) (<c>0x48ee26</c>): отряд вне укрепления —
    /// окно отряда (<c>CMidTaskOpenInterf&lt;CManageStkInterf&gt;</c>, <c>0x4925cf</c>), внутри — окно укрепления (<c>CCityStackInterf</c>,
    /// <c>0x486c8e</c>, и у столицы). Отряд не пережил итог боя или события — окна нет.</summary>
    private void OpenPaperdoll(string stackId)
    {
        var session = _map.Session;
        bool asked = session.Run<bool>(new D2.Data.Play.Commands.RequestPaperdollCommand(stackId));
        var stack = asked ? session.Map.Stacks.GetValueOrDefault(stackId) : null;
        string screen = stack is null ? "-" : stack.Inside.Length > 0 ? CityStackLogic.Screen : ManageStackLogic.Screen;
        GD.Print($"paperdoll stack={stackId} {(asked ? "запрос" : "нет_отряда")} окно={screen}");
        if (stack is null || Windows is not { } context) return;
        _map.Reload();
        SyncPanels();
        if (stack.Inside.Length > 0) OpenWindow(CityStackLogic.Screen, new CityStackLogic(context, stack.Inside, GameData.Content.Screens));
        else OpenWindow(ManageStackLogic.Screen, new ManageStackLogic(context, stack.Id, GameData.Content.Screens,
                                                                      _stackTexts ??= StackTexts.Load(GameData.Rules)));
    }

    /// <summary>Отряд у площадки — её окно (<c>0x48b218</c>: по категории площадки).</summary>
    private RulesTexts? _ruleTexts;

    private RulesTexts RuleTexts => _ruleTexts ??= RulesTexts.Load(GameData.Rules);

    private ThiefTexts? _thiefTexts;

    /// <summary>Отряд с лидером-вором дошёл до цели (отряд, крепость, руина, лавка): окно вора (<see cref="ThiefWindows.For"/>) вместо боя
    /// или окна площадки. true — окно вора открыто.</summary>
    private bool OpenThief(string stackId, string targetId)
    {
        if (Windows is not { } context || !ThiefWindows.IsThief(context, stackId)) return false;
        if (ThiefWindows.For(context, stackId, targetId, _thiefTexts ??= ThiefTexts.Load(GameData.Rules)) is not { } open) return false;
        GD.Print($"thief {stackId} -> {targetId} {open.Screen}");
        OpenWindow(open.Screen, open.Logic);
        return true;
    }

    private void OnSiteVisited(string stackId, string siteId)
    {
        if (Windows is not { } context || _dialogViews.Count > 0 || Aiming || _dropStack is not null) return;
        if (OpenThief(stackId, siteId)) return;
        var site = _map.Map.Sites.FirstOrDefault(s => s.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase));
        if (site is null) return;
        (string Screen, IScreenLogic Logic)? window = site.Kind switch
        {
            SiteKind.Merchant => (MerchantLogic.Screen, new MerchantLogic(context, stackId, site.Id, _stackTexts ??= StackTexts.Load(GameData.Rules),
                                                                          GameData.Content.Screens)),
            SiteKind.Mage => (MageLogic.Screen, new MageLogic(context, stackId, site.Id, SpellTexts, RuleTexts)),
            SiteKind.Trainer => (TrainerLogic.Screen, new TrainerLogic(context, stackId, site.Id)),
            SiteKind.Mercenaries => (MercenaryLogic.Screen, new MercenaryLogic(context, stackId, site.Id, RuleTexts)),
            _ => null,
        };
        GD.Print($"visit site {site.Id} {site.Kind}");
        if (window is { } w) OpenWindow(w.Screen, w.Logic);
    }
    private IReadOnlyDictionary<string, string>? _raceNames;

    /// <summary>Предложения союза от ИИ и обмена картами от человека, ждущие ответа игрока (<see cref="GameState.Offers"/>), — окнами
    /// в начале его хода (после конца хода и при открытии партии); первое предложение — сверху.</summary>
    private void ShowOffers()
    {
        if (Windows is not { } context) return;
        var shown = _logics.OfType<AllianceOfferLogic>().Select(l => l.Offer).ToHashSet();
        var offers = _map.Session.State.Offers
            .Where(o => (o.IsAlliance || o.Exchange) && o.To.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase) && !shown.Contains(o)).ToList();
        for (int i = offers.Count - 1; i >= 0; i--)
            OpenWindow(AllianceOfferLogic.Screen, new AllianceOfferLogic(context, offers[i], _raceNames ??= RaceNames.Load(GameData.Rules)));
        if (offers.Count > 0) GD.Print($"offers shown {offers.Count} from={string.Join(',', offers.Select(o => o.From))} {WindowLine()}");
        ShowTradeOffers(context);
        ShowTurnSummary(context);
    }

    /// <summary>Сделки ИИ «предмет или заклинание за золото» (<see cref="GameState.Offers"/> с <c>Trade</c>, сессия А) — окном
    /// <c>DLG_OFFER</c> (<see cref="TradeOfferLogic"/>); ответ — <see cref="D2.Data.Play.GameSession.AnswerTradeOffer"/>. Согласие, которое
    /// не проходит (предмета больше нет), — сообщением и отказом.</summary>
    private void ShowTradeOffers(WindowContext context)
    {
        string viewer = _map.Viewer ?? "";
        var shown = _logics.OfType<TradeOfferLogic>().Select(l => l.Offer.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trades = _map.Session.State.Offers
            .Where(o => o.Trade is { Kind: not D2.Rules.Map.AiGiftKind.Gold } && o.To.Equals(viewer, StringComparison.OrdinalIgnoreCase)
                        && !shown.Contains(o.From)).ToList();
        foreach (var o in trades)
        {
            if (TradeOffer.Of(o, _map.Map) is not { } offer) continue;
            string from = o.From;
            OpenWindow(TradeOfferLogic.Screen, new TradeOfferLogic(context, offer, _raceNames ??= RaceNames.Load(GameData.Rules), TradeThingName(offer.IsItem), yes =>
            {
                string? refused = (string?)_map.Session.Execute(new D2.Data.Play.Commands.AnswerTradeCommand(from, viewer, yes)).Value;
                if (refused is not null && yes)
                {
                    _map.Session.Execute(new D2.Data.Play.Commands.AnswerTradeCommand(from, viewer, false));
                    GD.Print($"trade refused {from} {refused.Replace(' ', '_')}");
                }
                // Ответ на сделку (сообщение 16, TRADITEM…) движок шлёт предложившему (0x48b53b): ИИ беззвучен, человек «горячего стула» слышит
                // его сразу после ответа (без фильтра игрока, разбор 28.09, docs/ai-screen.md §11).
                if (_map.Session.State.Info.Humans.Contains(from, StringComparer.OrdinalIgnoreCase))
                    context.Announce(yes && refused is null ? D2.Data.Play.TurnSightKind.Traded : D2.Data.Play.TurnSightKind.TradeRefused, offer.ThingId);
                _map.Reload();
                SyncPanels();
            }));
        }
        if (trades.Count > 0) GD.Print($"trades shown {trades.Count} from={string.Join(',', trades.Select(o => o.From))} {WindowLine()}");
    }

    /// <summary>Имя вещи сделки: предмет — по типу (<c>GItem.NAME_TXT</c>), заклинание — <c>Gspells.NAME_TXT</c>.</summary>
    private Func<string, string> TradeThingName(bool item) => id =>
        item ? (_stackTexts ??= StackTexts.Load(GameData.Rules)).Items.TryGetValue(id, out var txt) ? Text(txt) : id
             : SpellTexts.TryGetValue(id, out var spell) ? Text(spell.NameTxt) : id;

    /// <summary>Сборщики итогов хода людей (<see cref="TurnSummaryWatch"/>): подписаны на ход партии (<see cref="D2.Data.Play.GameSession.Watch"/>)
    /// от конца хода игрока до начала его следующего.</summary>
    private readonly Dictionary<string, TurnSummaryWatch> _summaryWatches = new(StringComparer.OrdinalIgnoreCase);

    private D2.Data.Play.GameSession? _summarySession;

    /// <summary>Итоги хода в сохранении (v9, <c>docs/day-cycle.md</c> §20): новой партии (и загруженной) — поставщик записей для снимка
    /// (<see cref="D2.Data.Play.GameSession.SummariesForSave"/>: каждый сборщик — его день и записи на этот миг) и сборщики, продолженные с
    /// итогами из сохранения (<see cref="D2.Data.Play.GameSession.TakeSummaries"/>). У движка итоги лежат в <c>.sg</c> (<c>CTurnSummary</c>).</summary>
    private void WatchSummaries()
    {
        var session = _map.Session;
        if (ReferenceEquals(_summarySession, session) || Windows is not { } context) return;
        if (_summarySession is not null)
            foreach (var old in _summaryWatches.Values) _summarySession.Unwatch(old);
        _summaryWatches.Clear();
        _summarySession = session;
        session.SummariesForSave = () => _summaryWatches.ToDictionary(
            p => p.Key, p => new D2.Data.Play.PendingSummary(p.Value.Day, p.Value.Finish(session.Looks)), StringComparer.OrdinalIgnoreCase);
        foreach (var (human, pending) in session.TakeSummaries())
        {
            var watch = new TurnSummaryWatch(context, human, session.Looks, SpellTexts, _raceNames ??= RaceNames.Load(GameData.Rules), pending);
            _summaryWatches[human] = watch;
            session.Watch(watch);
            GD.Print($"turn summary resumed {human} день={pending.Day} записей={pending.Entries.Count}");
        }
    }

    /// <summary>«Конец хода» панели команд.</summary>
    private const string EndTurnButton = "BTN_END_TURN";

    /// <summary>Конец хода игрока: автосохранение, сборщик его итогов, ходы ИИ, затем его предложения. «Конец хода», погашенный здесь,
    /// снова доступен в <c>OnAiFinished</c>: <see cref="MapView.EndTurn"/> без раннего выхода всегда доходит до <c>AiTurnFinished</c> (и в
    /// «горячем стуле» — синхронно).</summary>
    private void EndTurn()
    {
        WatchSummaries();
        if (!_map.Walking && !_map.AiRunning)   // иначе MapView.EndTurn ход не кончит
        {
            WriteAutoSave();
            // Движок гасит «Конец хода» по щелчку (0x48f911 → 0x48f945), до смены панели на DLG_STRAT_WAIT; к ходу человека панель новая
            // (OnAiFinished). Зс57.
            _panelView?.State.SetEnabled(EndTurnButton, false);
            _panelView?.Sync();
        }
        if (_map.Viewer is { } viewer && Windows is { } context)
        {
            if (_summaryWatches.Remove(viewer, out var old)) _map.Session.Unwatch(old);
            var watch = new TurnSummaryWatch(context, viewer, _map.Session.Looks, SpellTexts, _raceNames ??= RaceNames.Load(GameData.Rules));
            _summaryWatches[viewer] = watch;
            _map.Session.Watch(watch);
        }
        _map.EndTurn();
        ShowOffers();
    }

    /// <summary>Автосохранение по «Конец хода» человека (<see cref="AutoSave"/>): флажок настроек включён — партия на этот миг в каталог
    /// сохранений архивом «&lt;сценарий&gt; T####», лишние архивы сценария сверх 15 удаляются; в чате — те же строки, что у быстрого
    /// сохранения (<see cref="SaveWithChat"/>). Проверка (<c>--check</c>) пишет его только в свой каталог <c>--saves</c>: иначе прогоны без
    /// него писали бы в сохранения пользователя.</summary>
    private void WriteAutoSave()
    {
        if (!_settings.AutoSave || SavesDirOrNull() is not { } dir) return;
        var state = _map.Session.Snapshot();
        if (SaveWithChat(AutoSave.ArchiveName(state), () => AutoSave.Write(dir, enabled: true, state)) is { } path)
            GD.Print($"autosave {System.IO.Path.GetFileName(path).Replace(' ', '_')} день={_map.Map.Turn} " +
                     $"архивов={AutoSave.Archives(dir, AutoSave.ArchiveBase(state.Info.ScenarioName)).Count}");
    }

    /// <summary>Бой человека (горячий стул): нападение для итогов хода других людей — их сборщики его записывают.</summary>
    private void NoteAttack(string attackerId, string defenderId)
    {
        foreach (var watch in _summaryWatches.Values) watch.Attacked(attackerId, defenderId);
    }

    /// <summary>Итоги хода (<see cref="TurnSummaryLogic"/>): что сборщик игрока записал с конца его хода; записей нет — окна нет.
    /// Одиночная игра без флажка «Всегда показывать результаты хода» — окна нет (<see cref="D2.Data.Map.MapSettings.TurnSummaryShown"/>).</summary>
    private void ShowTurnSummary(WindowContext context)
    {
        // Сборщик — от прошлого дня этого игрока: сразу после конца хода (ИИ ещё ходит) день прежний — ждём.
        if (_map.Viewer is not { } viewer || !_summaryWatches.TryGetValue(viewer, out var watch) || _map.Map.Turn <= watch.Day) return;
        // Итоги — за окном начала хода (журналы: DLG_BEGIN_TURN → DLG_TURNSUMMARY): его «ОК» снова зовёт итоги (MapScreen.Overlays.cs).
        if (BeginTurnPending) return;
        _summaryWatches.Remove(viewer);
        _map.Session.Unwatch(watch);
        if (!_settings.TurnSummaryShown(_map.Session.State.Info.Humans.Count)) return;
        var entries = watch.Finish(_map.Session.Looks);
        GD.Print($"turn summary {viewer} записей={entries.Count} виды={string.Join(",", entries.Select(e => e.Kind))}");
        if (entries.Count > 0) OpenWindow(TurnSummaryLogic.Screen, new TurnSummaryLogic(context, entries, SpellTexts));
    }

    /// <summary>Выделить отряд и показать его на карте (после вывода героя из укрепления).</summary>
    private void FocusStack(string stackId)
    {
        CloseCapital();   // движок после вывода героя показывает карту с выделенным отрядом
        _map.Select(stackId);
        _map.Reload();
        SyncPanels();
    }

    /// <summary>Шаг проверки: окно армии своего укрепления по id, первой своей деревни (<c>village</c>) или столицы
    /// (<c>capital</c>).</summary>
    private void OpenCityForCheck(string which)
    {
        var own = _map.Map.Sites.Where(s => s.Owner.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase)
                                            && s.Kind is SiteKind.Capital or SiteKind.Village).ToList();
        var site = which switch
        {
            "village" => own.Where(s => s.Kind == SiteKind.Village).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault(),
            "capital" => own.FirstOrDefault(s => s.Kind == SiteKind.Capital),
            _ => own.FirstOrDefault(s => s.Id.Equals(which, StringComparison.OrdinalIgnoreCase)),
        };
        if (site is not null && Windows is { } context) OpenWindow(CityStackLogic.Screen, new CityStackLogic(context, site.Id, GameData.Content.Screens));
        else GD.PrintErr($"city {which}: нет своего укрепления");
    }

    /// <summary>Строка проверки верхнего окна: стек и числа его логики.</summary>
    private string WindowLine()
    {
        string stack = _dialogs is null ? "-" : string.Join(">", _dialogs.Stack.Skip(1).DefaultIfEmpty("-"));
        var top = _dialogViews.Count > 0 ? _dialogViews[^1] : null;
        string summary = top?.Logic is IWindowLogic w ? w.Summary : "";
        string cards = top is null ? "" : string.Join(" ", top.State.Screen.Elements
            .Where(e => e.Kind == UiKind.List && top.CardsShown(e.Id) > 0).Select(e => $"{e.Id}={top.CardsShown(e.Id)}"));
        string missing = top is { MissingFrames.Count: > 0 } ? $"нет_кадров={string.Join(",", top.MissingFrames)}" : "";
        // Строки текстовых списков, как нарисованы (текст без разметки и цвет): «строки_TLBOX_ACTIONS=Заслать_шпиона#000000|…».
        if (top is not null)
            foreach (var list in top.State.Screen.Elements.Where(e => e.Kind == UiKind.TextList && top.RowsLine(e.Id) != "-"))
                missing += $" строки_{list.Id}={top.RowsLine(list.Id)}";
        if (top?.DragCursor is { } dragCursor) missing += $" тащение={dragCursor}";
        if (top is { ImagelessCards.Count: > 0 }) missing += $" без_значка={string.Join(",", top.ImagelessCards)}";
        if (top is { AdditiveOverlays: > 0 }) missing += $" сложением={top.AdditiveOverlays}";
        if (top is not null && top.State.Screen.Elements.Any(e => e.Id == "TXT_LEMPTY"))
        {
            string hint = new([.. D2.Data.Ui.UiTextStyle.Parse(top.State.TextOf("TXT_LEMPTY") ?? "").Text.Where(char.IsLetter).Take(16)]);
            missing += $" подсказка_найма={(hint.Length > 0 ? hint : "-")}";
        }
        if (top is { DragImageKey.Length: > 0 }) missing += $" тащимое={top.DragImageKey} курсор={Cursor.Current ?? "-"}:{Cursor.Frames}";
        if (top is not null && top.State.Screen.Elements.Any(e => e.Id == "LBOX_UNIT_LIST"))
            missing += $" ростер0={top.FirstCard("LBOX_UNIT_LIST").Replace(' ', '_')}";
        if (top?.ImagesOutside() is { Count: > 0 } outside) missing += $" за_местом={string.Join(",", outside)}";
        if (top?.DropFrameSprites() is { Count: > 0 } frames) missing += $" рамки={frames.Count} {string.Join(",", frames)}";
        if (top?.CutLabels() is { Count: > 0 } cut) missing += $" срезано={string.Join(",", cut)}";   // текст выше места — срезан местом
        return $"окна={stack} {summary} {cards} {missing} {AimLine()}".TrimEnd();
    }
}
