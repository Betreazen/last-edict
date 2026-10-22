using LastEdict.Data.Menu;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Общее окна дипломатии (<c>CDiplomacyInterf</c>): игроки поля — сам игрок первым, затем до трёх других, ведущих дипломатию
/// (играбельная раса, столица не взята; порядок id), и выбранный. Поле и панель читают его же; выбор меняет поле щелчком.
/// <para>Больше трёх соперников (Р15, Э4) — страницы по три (<see cref="Page"/>, <see cref="Pages"/>): поле листает их стрелками.</para>
/// </summary>
public sealed class DiplomacyModel(WindowContext context, IReadOnlyDictionary<string, string> raceNames)
{
    public const int Slots = 4;

    public WindowContext Context => context;

    public IReadOnlyDictionary<string, string> RaceNames => raceNames;

    /// <summary>Все, с кем игрок ведёт дипломатию: играбельная раса, столица не взята, кроме него самого; порядок id.</summary>
    public IReadOnlyList<MapPlayer> Others
    {
        get
        {
            var book = context.Session.RequireBook();
            bool Plays(MapPlayer p) => book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Playable && p.CapturedBy.Length == 0;
            return [.. context.Map.Players.Values.Where(p => !p.Id.Equals(context.Viewer, StringComparison.OrdinalIgnoreCase) && Plays(p))
                                                 .OrderBy(p => p.Id, StringComparer.Ordinal)];
        }
    }

    /// <summary>Страниц соперников по <see cref="Slots"/> − 1; хотя бы одна.</summary>
    public int Pages => Math.Max(1, (Others.Count + Slots - 2) / (Slots - 1));

    /// <summary>Показанная страница соперников (0…<see cref="Pages"/> − 1); смена страницы снимает выбор на себя.</summary>
    public int Page
    {
        get => Math.Clamp(_page, 0, Pages - 1);
        set
        {
            _page = Math.Clamp(value, 0, Pages - 1);
            Selected = 0;
        }
    }

    private int _page;

    /// <summary>Игроки поля по слотам: [0] — сам игрок, дальше — соперники страницы <see cref="Page"/>.</summary>
    public IReadOnlyList<MapPlayer> Players =>
        [context.Map.Players[context.Viewer], .. Others.Skip(Page * (Slots - 1)).Take(Slots - 1)];

    /// <summary>Выбранный слот (0 — сам игрок).</summary>
    public int Selected { get; set; }

    public MapPlayer SelectedPlayer => Players[Math.Clamp(Selected, 0, Players.Count - 1)];

    /// <summary>Выбор сменился или отношения поменялись — хозяин перерисовывает поле и панель.</summary>
    public event Action? Changed;

    public void Notify() => Changed?.Invoke();

    /// <summary>Вид расы игрока (портреты лорда, <see cref="LordFaces"/>).</summary>
    public LastEdict.Data.Content.Schema.RaceLook Look(MapPlayer p) => context.Session.RequireBook().LookOf(p.RaceId);

    public string RaceName(MapPlayer p) => raceNames.TryGetValue(p.RaceId, out var txt) ? context.Text(txt) : p.RaceId;

    /// <summary>Сторона дипломатии игрока (ключ отношений); нет расы — −1.</summary>
    public int Party(MapPlayer p) => Diplomacy.PartyOf(p, context.Session.RequireBook().Economy.Races) ?? -1;

    public Relation? RelationOf(MapPlayer a, MapPlayer b) =>
        context.Map.Relations.FirstOrDefault(r => r.Pairs(Party(a), Party(b))) is { Raw: > 0 } rel ? rel : null;

    public int Current(MapPlayer a, MapPlayer b) => Diplomacy.Current(context.Map.Relations, Party(a), Party(b));

    public bool Allies(MapPlayer a, MapPlayer b) => Diplomacy.AreAllies(context.Map.Relations, Party(a), Party(b));

    public bool Peaceful(MapPlayer a, MapPlayer b) => Diplomacy.IsPeaceful(context.Session.RequireBook().Diplomacy, Current(a, b));

    public bool AtWar(MapPlayer a, MapPlayer b) => Diplomacy.IsAtWar(context.Session.RequireBook().Diplomacy, Current(a, b));
}

/// <summary>
/// Поле дипломатии (<c>DLG_ISO_DIPLO</c>, <c>0x482278</c>) на месте карты: слоты игроков <c>IMG_RACE1..4</c>/<c>TXT_RACE1..4</c> (лишние
/// скрыты, щелчок выбирает), <c>TXT_PLAYER_CUR</c>/<c>TXT_PLAYER_SEL</c> — <c>X005TA0314</c> (лорд, раса, тип лорда: маг <c>X005TA0066</c>,
/// воин <c>0065</c>, иначе <c>0067</c>; по строке на каждого другого игрока: война <c>0374</c>, нейтралитет <c>0375</c>, союз <c>0376</c>,
/// мир <c>0373</c>), шкалы <c>IMG_METER1..3</c> — отношение самого игрока с остальными (<c>0x48311f</c>: полоса <c>DLG_ISO_DIPLO_METER</c>
/// 30×360, сверху синяя, снизу красная, сдвигается так, что в окне шкалы видна часть по отношению — война внизу, красная; курсор
/// <c>DLG_ISO_DIPLO_CURSOR</c> — от низа шкалы вверх по 0…100; обе по центру шкалы по ширине),
/// тенденции <c>DLG_ISO_DIPLO_REL_{WAR|PEACE|NEUTRAL}_{DOWN|UP}</c> и стрелка изменения за ход.
/// Картинка слота — круглый портрет лорда (<see cref="LordFaces.Round"/>), у чужих слотов отражённый слева направо (снимки 28.09: 2,2 против
/// 50 без отражения); выбранный слот — кольцо <c>DRAGDROP_BIGFACE_RIGHT</c> у своего, <c>_LEFT</c> у чужого (<c>0x5c96ae</c>), 180×180 по
/// центру слота, сложением цветов (режим 9, <c>0x484b53</c>). ponytail: кадров <c>DLG_ISO_DIPLO_TEND_*</c> в архиве нет —
/// стрелки <c>DLG_ISO_DIPLO_ARROW_{UP|DOWN}_N</c>, без изменения — прячется; подпись слота — имя игрока.
/// </summary>
public sealed class DiplomacyFieldLogic(DiplomacyModel model) : IWindowLogic
{
    public const string Screen = "DLG_ISO_DIPLO";
    private const string Cursor = "DLG_ISO_DIPLO_CURSOR", Meter = "DLG_ISO_DIPLO_METER";
    private const int CursorW = 30, CursorH = 15, MeterW = 30, MeterH = 360, RingSize = 180;

    /// <summary>Кольцо выбранного слота поверх портрета (<c>IMG_RACE{n}</c> + этот хвост).</summary>
    public const string RingSuffix = "_SEL";

    /// <summary>Отношение 0…100 (<c>n − 1</c> делителя <c>0x483177</c>).</summary>
    private const int RelationMax = 100;

    /// <summary>Окно полосы шкалы для отношения (<c>0x48315a</c>…: полоса стоит с y = низ − 360 + v·(360 − H)/100, x по центру,
    /// обрезана прямоугольником шкалы).</summary>
    public static UiRect MeterWindow(UiRect meter, int value)
    {
        int offset = Math.Clamp(value, 0, RelationMax) * (MeterH - meter.Height) / RelationMax;
        return new UiRect(-((meter.Width - MeterW) / 2), MeterH - meter.Height - offset, meter.Width, meter.Height);
    }

    /// <summary>Верх курсора шкалы (<c>0x4831e1</c>…: y = низ − 15 − v·(H − 15)/100).</summary>
    public static int CursorTop(UiRect meter, int value) =>
        meter.Y + meter.Height - CursorH - (Math.Clamp(value, 0, RelationMax) * (meter.Height - CursorH) / RelationMax);
    private readonly UiRequests _requests = new();
    private readonly Dictionary<int, UiRect> _slots = [];

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"дипломатия выбран={model.SelectedPlayer.Id} игроков={model.Players.Count} страница={model.Page + 1}/{model.Pages}";

    /// <summary>Листание соперников (Р15): кнопки поверх поля внизу, есть только при нескольких страницах. Вид (Э5) — стрелки «влево/вправо»
    /// меню (<see cref="PrevSprites"/>, <see cref="NextSprites"/>, 37×37 — место кнопки) со всеми кадрами кнопки: обычный, наведение, нажатие,
    /// погашен; прежде — значки тенденции окна «вверх/вниз» одним кадром, кнопка не отзывалась на мышь.</summary>
    public const string NextPage = "BTN_PAGE_NEXT", PrevPage = "BTN_PAGE_PREV", PageText = "TXT_PAGE";

    public static readonly UiSprites PrevSprites = new("_MENU_ARROW_LEFT_N", "_MENU_ARROW_LEFT_H", "_MENU_ARROW_LEFT_C", "_MENU_ARROW_LEFT_D"),
        NextSprites = new("_MENU_ARROW_RIGHT_N", "_MENU_ARROW_RIGHT_H", "_MENU_ARROW_RIGHT_C", "_MENU_ARROW_RIGHT_D");

    /// <summary>Курсоры шкал — отдельные картинки поверх шкал (место — по значению отношения).</summary>
    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var elements = screen.Elements.ToList();
        for (int i = 1; i <= DiplomacyModel.Slots; i++)
            if (elements.FirstOrDefault(e => e.Id == $"IMG_RACE{i}") is { } race)
            {
                _slots[i - 1] = race.Rect;
                elements.Add(new UiElement
                {
                    Id = race.Id + RingSuffix, Kind = UiKind.Image,
                    Rect = new UiRect(race.Rect.X + ((race.Rect.Width - RingSize) / 2), race.Rect.Y + ((race.Rect.Height - RingSize) / 2),
                                      RingSize, RingSize),
                });
            }
        if (model.Pages > 1)
            elements.AddRange([
                new UiElement { Id = PrevPage, Kind = UiKind.Button, Rect = new UiRect(250, 562, 37, 37), Sprites = PrevSprites },
                new UiElement { Id = PageText, Kind = UiKind.Text, Rect = new UiRect(290, 570, 60, 20) },
                new UiElement { Id = NextPage, Kind = UiKind.Button, Rect = new UiRect(353, 562, 37, 37), Sprites = NextSprites },
            ]);
        var players = model.Players;
        for (int i = 1; i < DiplomacyModel.Slots; i++)
        {
            if (elements.FirstOrDefault(e => e.Id == $"IMG_METER{i}") is not { } meter || i >= players.Count) continue;
            int value = model.Current(players[0], players[i]);
            elements.Add(new UiElement
            {
                Id = $"IMG_CURSOR{i}", Kind = UiKind.Image, Image = Cursor,
                Rect = new UiRect(meter.Rect.X + ((meter.Rect.Width - CursorW) / 2), CursorTop(meter.Rect, value), CursorW, CursorH),
            });
        }
        return screen with { Elements = elements };
    }

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Resume(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (e is not UiClicked { Element: NextPage or PrevPage } click) return;
        model.Page = (model.Page + (click.Element == NextPage ? 1 : model.Pages - 1)) % model.Pages;
        Refresh(ui);
        model.Notify();
    }

    /// <summary>Правая кнопка над портретом слота — карточка его игрока (<see cref="PlayerCardLogic"/>, <c>0x48528f</c>…<c>0x48530e</c>), пока
    /// кнопка держится.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var players = model.Players;
        foreach (var (slot, r) in _slots)
            if (slot < players.Count && x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height)
            {
                _requests.Add(new UiOpen(PlayerCardLogic.Screen, new PlayerCardLogic(model.Context, players[slot].Id)));
                return;
            }
    }

    /// <summary>Щелчок по слоту игрока выбирает его (<c>0x484a6b</c>).</summary>
    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        foreach (var (slot, r) in _slots)
            if (slot < model.Players.Count && x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height)
            {
                model.Selected = slot;
                Refresh(ui);
                model.Notify();
                return;
            }
    }

    private void Refresh(UiScreenState ui)
    {
        var players = model.Players;
        for (int i = 0; i < DiplomacyModel.Slots; i++)
        {
            bool shown = i < players.Count;
            ui.SetHidden($"IMG_RACE{i + 1}", !shown);
            ui.SetHidden($"TXT_RACE{i + 1}", !shown);
            if (!shown) continue;
            string face = LordFaces.Round(model.Look(players[i]), players[i].Face);
            ui.SetImage($"IMG_RACE{i + 1}", i == 0 ? face : UiFrameRef.Mirrored(face));
            ui.SetText($"TXT_RACE{i + 1}", players[i].Name);
        }
        for (int i = 0; i < DiplomacyModel.Slots; i++)
            if (ui.Screen.Elements.Any(e => e.Id == $"IMG_RACE{i + 1}{RingSuffix}"))
                ui.SetImage($"IMG_RACE{i + 1}{RingSuffix}", i == model.Selected && i < players.Count
                    ? UiFrameRef.Added(i == 0 ? "DRAGDROP_BIGFACE_RIGHT" : "DRAGDROP_BIGFACE_LEFT") : null);
        if (ui.Screen.Elements.Any(e => e.Id == PageText)) ui.SetText(PageText, $"{model.Page + 1}/{model.Pages}");
        ui.SetText("TXT_PLAYER_CUR", Line(players[0]));
        ui.SetText("TXT_PLAYER_SEL", model.Selected == 0 ? "" : Line(model.SelectedPlayer));
        for (int i = 1; i < DiplomacyModel.Slots; i++)
        {
            bool shown = i < players.Count;
            foreach (string id in (string[])[$"IMG_METER{i}", $"IMG_TENDENCY{i}_L", $"IMG_TENDENCY{i}_R", $"IMG_TENDENCYA{i}"])
                if (ui.Screen.Elements.Any(e => e.Id == id)) ui.SetHidden(id, !shown);
            if (!shown) continue;
            var self = players[0];
            if (ui.Screen.Elements.FirstOrDefault(e => e.Id == $"IMG_METER{i}") is { } meter)
                ui.SetImage(meter.Id, UiFrameRef.Cropped(Meter, MeterWindow(meter.Rect, model.Current(self, players[i]))));
            string kind = model.AtWar(self, players[i]) ? "WAR" : model.Peaceful(self, players[i]) ? "PEACE" : "NEUTRAL";
            ui.SetImage($"IMG_TENDENCY{i}_L", $"DLG_ISO_DIPLO_REL_{kind}_DOWN");
            ui.SetImage($"IMG_TENDENCY{i}_R", $"DLG_ISO_DIPLO_REL_{kind}_UP");
            var rel = model.RelationOf(self, players[i]);
            int delta = rel is { } r ? r.Current - r.Previous : 0;
            ui.SetHidden($"IMG_TENDENCYA{i}", delta == 0);
            ui.SetImage($"IMG_TENDENCYA{i}", delta < 0 ? "DLG_ISO_DIPLO_ARROW_DOWN_N" : "DLG_ISO_DIPLO_ARROW_UP_N");
        }
    }

    /// <summary>Строка игрока <c>X005TA0314</c>: лорд, раса, тип и отношения с каждым другим игроком поля.</summary>
    public string Line(MapPlayer p)
    {
        var c = model.Context;
        int category = c.Session.RequireBook().LordCategory(p.LordId);
        string type = c.Text(category switch { 0 => "X005TA0066", 1 => "X005TA0065", _ => "X005TA0067" });
        string state = string.Concat(model.Players.Where(q => !q.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase)).Select(q =>
            UiTemplate.Fill(c.Text(model.AtWar(p, q) ? "X005TA0374" : !model.Peaceful(p, q) ? "X005TA0375"
                                   : model.Allies(p, q) ? "X005TA0376" : "X005TA0373"), ("RACE", q.Name)) + @"\n"));
        return UiTemplate.Fill(c.Text("X005TA0314"), ("LORD", p.Name), ("RACE", model.RaceName(p)), ("TYPE", type), ("STATE", state));
    }
}

/// <summary>
/// Панель дипломатии (<c>DLG_DIPLOMACY</c>) на месте панели команд: лицо выбранного лорда, <c>X005TA0325</c> (выбран сам) или
/// <c>X005TA0324</c>; кнопки (<c>0x482bb0</c>): при себе — все выключены; иначе торговля, дань и обмен областями включены, «Союз» — без
/// союза, «Разорвать союз» — при союзе.
/// <list type="bullet">
/// <item>Дань (<c>0x484153</c>): золота меньше <c>D_GOLD</c> — <c>X005TA0317</c>, иначе окно <see cref="GiveResourceLogic"/>.</item>
/// <item>Обмен областями (<c>0x4842ed</c>): не мир — <c>X005TA0597</c> (<c>X160TA0005</c>), иначе вопрос <c>X160TA0006</c>.</item>
/// <item>Союз (<c>0x4846e9</c>): не мир и выбран ИИ — <c>X005TA0597</c> (<c>X005TA0600</c>), иначе вопрос <c>X005TA0462</c>; ИИ отвечает
/// сразу — <c>X005TA0290</c>/<c>X005TA0300</c>, человеку предложение ждёт его хода.</item>
/// <item>Разорвать союз (<c>0x48494f</c>): вопрос <c>X005TA0319</c>.</item>
/// <item>Продать предмет/заклинание: заклинаний на продажу нет — <c>X005TA0505</c>.</item>
/// </list>
/// Продать предмет — <see cref="TradeItemLogic"/>, заклинание — <see cref="TradeSpellLogic"/> (цена — <see cref="TradePriceLogic"/>,
/// сделка сессии А); обмен областями — раз в ход на цель (<c>X160TA0023</c>), итог <c>X160TA0009</c>/<c>X160TA0010</c>
/// (<see cref="Play.GameSession.ProposeExchangeMapBy"/>, сессия А). «Назад» уводит по графу.
/// </summary>
public sealed class DiplomacyPanelLogic(DiplomacyModel model, IReadOnlyDictionary<string, SpellText>? spellTexts = null) : IWindowLogic
{
    public const string Screen = "DLG_DIPLOMACY";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string? LastResult { get; private set; }

    public string Summary => $"панель выбран={model.SelectedPlayer.Id} итог={(LastResult ?? "-").Replace(' ', '_')}";

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Resume(UiScreenState ui) => Refresh(ui);

    public bool Allows(string element, UiScreenState ui) => element == "BTN_BACK";

    /// <summary>Правая кнопка над лицом выбранного (<c>IMG_LORD_FACE</c>, <c>0x485254</c>) — карточка выбранного игрока
    /// (<see cref="PlayerCardLogic"/>).</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Screen.Elements.Any(e => e.Id == "IMG_LORD_FACE") && SiteLogicLists.Inside(ui, "IMG_LORD_FACE", x, y))
            _requests.Add(new UiOpen(PlayerCardLogic.Screen, new PlayerCardLogic(model.Context, model.SelectedPlayer.Id)));
    }

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var c = model.Context;
        var self = model.Players[0];
        var sel = model.SelectedPlayer;
        bool me = model.Selected == 0;
        ui.SetImage("IMG_LORD_FACE", LordFaces.Small(model.Look(sel), sel.Face));
        ui.SetText("TXT_LABEL1", me ? c.Text("X005TA0325") : UiTemplate.Fill(c.Text("X005TA0324"), ("LORDNAME", sel.Name)));
        ui.SetText("TXT_LABEL2", "");
        foreach (string b in (string[])["BTN_TRADE_ITEM", "BTN_TRADE_SPELL", "BTN_GIVE_RESS", "BTN_EXCHANGE_FOG"]) ui.SetEnabled(b, !me);
        bool allies = !me && model.Allies(self, sel);
        ui.SetEnabled("BTN_PROPOSE_ALLIANCE", !me && !allies);
        ui.SetEnabled("BTN_BREAK_ALLIANCE", allies);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: var button } || model.Selected == 0) return;
        var c = model.Context;
        var self = model.Players[0];
        var sel = model.SelectedPlayer;
        var book = c.Session.RequireBook();
        switch (button)
        {
            case "BTN_GIVE_RESS":
                if (self.Bank.Gold < book.Diplomacy.GoldPerPoint) Tell("X005TA0317");
                else _requests.Add(new UiOpen(GiveResourceLogic.Screen, new GiveResourceLogic(model)));
                break;
            case "BTN_EXCHANGE_FOG":
                if (!model.Peaceful(self, sel)) _requests.Add(new UiTell(UiTemplate.Fill(c.Text("X005TA0597"), ("WHAT", c.Text("X160TA0005")))));
                // 0x48445e: день последнего обмена с целью — поле игрока (MapPlayer.MapExchanges, пишет итог обмена), не память окна.
                else if (self.LastMapExchange(sel.Id) is int last && last >= c.Map.Turn) Tell("X160TA0023");
                else _requests.Add(new UiAsk(c.Text("X160TA0006"), () =>
                {
                    var result = c.Session.Run<LastEdict.Data.Play.ProposalResult>(new ProposeExchangeMapCommand(self.Id, sel.Id));
                    LastResult = result.Refusal ?? result.Accepted switch { true => "обмен областями", false => "отказ обмена областями", _ => "ждёт ответа" };
                    if (result.Refusal is { } refused)
                    {
                        _requests.Add(new UiTell(refused));
                        return;
                    }
                    if (result.Accepted is { } accepted)
                        _requests.Add(new UiTell(UiTemplate.Fill(c.Text(accepted ? "X160TA0009" : "X160TA0010"), ("PLAYER", sel.Name),
                                                                 ("RACE", model.RaceName(sel)))));
                    model.Notify();
                }));
                break;
            case "BTN_TRADE_ITEM":
                _requests.Add(new UiOpen(TradeItemLogic.Screen, new TradeItemLogic(c, sel.Id, c.ItemTexts ?? StackTexts.Empty)));
                break;
            case "BTN_PROPOSE_ALLIANCE":
                if (!model.Peaceful(self, sel) && !c.Session.State.Info.Humans.Contains(sel.Id, StringComparer.OrdinalIgnoreCase))
                {
                    _requests.Add(new UiTell(UiTemplate.Fill(c.Text("X005TA0597"), ("WHAT", c.Text("X005TA0600")))));
                    break;
                }
                _requests.Add(new UiAsk(UiTemplate.Fill(c.Text("X005TA0462"), ("NAME", sel.Name), ("RACE", model.RaceName(sel))), () =>
                {
                    bool? accepted = c.Session.Run<bool?>(new ProposeAllianceCommand(self.Id, sel.Id));
                    LastResult = accepted switch { true => "союз", false => "отказ", _ => "ждёт ответа" };
                    if (accepted is { } answered)
                        c.Announce(answered ? Play.TurnSightKind.AllianceMade : Play.TurnSightKind.AllianceRefused, sel.Id);   // звук — показ (сессия Г)
                    if (accepted is { } yes)
                        _requests.Add(new UiTell(UiTemplate.Fill(c.Text(yes ? "X005TA0290" : "X005TA0300"), ("PLAYER", sel.Name),
                                                                 ("RACE", model.RaceName(sel)))));
                    model.Notify();
                }));
                break;
            case "BTN_BREAK_ALLIANCE":
                _requests.Add(new UiAsk(UiTemplate.Fill(c.Text("X005TA0319"), ("PLAYER", sel.Name)), () =>
                {
                    string? refused = c.Session.Run<string?>(new BreakAllianceCommand(self.Id, sel.Id));
                    LastResult = refused ?? "союз разорван";
                    if (refused is null) c.Announce(Play.TurnSightKind.AllianceBroken, sel.Id);   // звук — показ (сессия Г)
                    model.Notify();
                }));
                break;
            case "BTN_TRADE_SPELL" when !self.KnownSpells.Any(s => book.Spells.TryGetValue(s, out var sp)
                                                                 && sp.Category != LastEdict.Rules.Magic.SpellCategory.Summon):
                Tell("X005TA0505");
                break;
            case "BTN_TRADE_SPELL" when spellTexts is not null:
                _requests.Add(new UiOpen(TradeSpellLogic.Screen, new TradeSpellLogic(c, sel.Id, spellTexts)));
                break;
        }
        Refresh(ui);
    }

    private void Tell(string id) => _requests.Add(new UiTell(model.Context.Text(id)));
}

/// <summary>
/// Дань золотом (<c>DLG_GIVE_RESOURCE</c>, <c>CGiveResInterf</c>): <c>X005TA0329</c> (кому), <c>X005TA0322</c>, лица обоих лордов, сумма —
/// кратная <c>D_GOLD</c>, стрелками по <c>D_GOLD</c> от 0 до казны (<c>0x4b5e26</c>); «ОК» при сумме больше 0 — дань
/// (<see cref="Play.GameSession.GiveGold"/>), окно закрывается.
/// </summary>
public sealed class GiveResourceLogic(DiplomacyModel model) : IWindowLogic
{
    public const string Screen = "DLG_GIVE_RESOURCE";
    private readonly UiRequests _requests = new();

    public long Amount { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"дань={Amount}";

    private long Step => model.Context.Session.RequireBook().Diplomacy.GoldPerPoint;

    private long Max => model.Players[0].Bank.Gold / Step * Step;

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or "BTN_CANCEL");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var c = model.Context;
        var self = model.Players[0];
        var sel = model.SelectedPlayer;
        ui.SetText("TXT_ACTION", UiTemplate.Fill(c.Text("X005TA0329"), ("PLAYER", sel.Name)));
        ui.SetText("TXT_INFO", c.Text("X005TA0322"));
        ui.SetImage("IMG_FACE1", LordFaces.Small(model.Look(self), self.Face));
        ui.SetImage("IMG_FACE2", LordFaces.Small(model.Look(sel), sel.Face));
        Refresh(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiClicked { Element: "BTN_UP" }: Amount = Math.Min(Max, Amount + Step); break;
            case UiClicked { Element: "BTN_DOWN" }: Amount = Math.Max(0, Amount - Step); break;
            case UiClicked { Element: "BTN_CANCEL" }: _requests.Add(new UiClose()); return;
            case UiClicked { Element: "BTN_OK" } when Amount > 0:
                string? refused = model.Context.Session.Run<string?>(new GiveGoldCommand(model.Players[0].Id, model.SelectedPlayer.Id, Amount));
                _requests.Add(new UiClose());
                if (refused is not null) _requests.Add(new UiTell(refused));
                model.Notify();
                return;
        }
        Refresh(ui);
    }

    private void Refresh(UiScreenState ui)
    {
        ui.SetText("TXT_GOLD", $"{Amount}");
        ui.SetEnabled("BTN_UP", Amount < Max);
        ui.SetEnabled("BTN_DOWN", Amount > 0);
        ui.SetEnabled("BTN_OK", Amount > 0);
    }
}

/// <summary>Золото игрока поверх поля дипломатии (<c>DLG_ISO_GOLDPOPUP</c>) — вместо полосы всех ресурсов <c>DLG_ISO_RESPOPUP</c>: на обоих
/// снимках дипломатии 28.09 видно только золото, на снимках столицы и карты — вся полоса.</summary>
public sealed class GoldPanelLogic(Func<long> gold) : IScreenLogic
{
    public const string Screen = "DLG_ISO_GOLDPOPUP";

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_GOLD", gold().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
