using LastEdict.Data.Play;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Продажа предмета другому игроку из окна дипломатии (<c>DLG_TRADE_ITEM</c>, <c>CTradeItemInterf</c> <c>0x4b7409</c>): <c>TXT_TITLE</c> —
/// <c>X005TA0584</c>; <c>TLBOX_CITY_LISTING</c> — все свои крепости (столица и деревни, <c>0x4b783c</c>); <c>MLBOX_RINVENTORY</c> — склад
/// выбранной (<c>0x4b79b8</c>, сумки отрядов не входят); «Продать» (<c>BTN_TRADE</c>, доступна при непустом складе, <c>0x4b77f0</c>) или
/// двойной щелчок по предмету — окно цены (<see cref="TradePriceLogic"/>). Купить у другого игрока нельзя — только продать.
/// ponytail: карта города <c>IMG_PALMAP</c> — нет.
/// </summary>
public sealed class TradeItemLogic(WindowContext context, string targetId, StackTexts texts) : IWindowLogic
{
    public const string Screen = "DLG_TRADE_ITEM";
    private const string Cities = "TLBOX_CITY_LISTING", Store = "MLBOX_RINVENTORY";
    private const int IconW = 70, RowW = 314, RowH = 90;
    private readonly UiRequests _requests = new();
    private int _fort, _item;

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Свои крепости: столица, затем деревни по id.</summary>
    public IReadOnlyList<MapSite> Forts =>
        [.. context.Map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.Owner.Equals(context.Viewer, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(s => s.Kind == SiteKind.Capital ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal)];

    /// <summary>Склад выбранной крепости.</summary>
    public IReadOnlyList<string> Items => _fort < Forts.Count ? Forts[_fort].Items : [];

    public string Summary => $"продажа крепость={(_fort < Forts.Count ? Forts[_fort].Id : "-")} склад={Items.Count} выбран={_item}";

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_TRADE" or "BTN_CLOSE");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_TITLE", context.Text("X005TA0584"));
        ui.SetItems(Cities, [.. Forts.Select(f => f.Name.Length > 0 ? f.Name : f.Id)]);
        if (Forts.Count > 0) ui.Select(Cities, _fort);
        FillStore(ui);
    }

    public void Resume(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        FillStore(ui);
    }

    private void FillStore(UiScreenState ui)
    {
        var items = Items;
        ui.SetCards(Store, [.. items.Select(Row)]);
        _item = Math.Clamp(_item, 0, Math.Max(0, items.Count - 1));
        if (items.Count > 0) ui.Select(Store, _item);
        ui.SetEnabled("BTN_TRADE", items.Count > 0);
    }

    private UiCard Row(string item)
    {
        string type = context.Map.ItemTypes.TryGetValue(item, out var t) ? t : item;
        string name = texts.Items.TryGetValue(type, out var n) ? context.Text(n) : type;
        string desc = texts.ItemDescs is { } d && d.TryGetValue(type, out var dd) ? context.Text(dd) : "";
        return new UiCard(ItemCells.IconFrame(type, context), new UiPoint(0, 2), @"\vC;\fMedBold;" + name + @"\n\fNormal;" + desc,
                          new UiRect(IconW + 10, 0, RowW - IconW - 10, RowH));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiListSelected { Element: Cities } s:
                _fort = s.Index;
                _item = 0;
                FillStore(ui);
                break;
            case UiListSelected { Element: Store } s:
                _item = s.Index;
                break;
            case UiListConfirmed { Element: Store } c:
                _item = c.Index;
                Sell();
                break;
            case UiClicked { Element: "BTN_TRADE" }:
                Sell();
                break;
            case UiClicked { Element: "BTN_CLOSE" }:
                _requests.Add(new UiClose());
                break;
        }
    }

    private void Sell()
    {
        if (_item >= Items.Count) return;
        var fort = Forts[_fort];
        string item = Items[_item];
        string type = context.Map.ItemTypes.TryGetValue(item, out var t) ? t : item;
        if (!context.Session.RequireBook().Items.TryGetValue(type, out var def)) return;
        long price = Treasury.ItemValue(def, context.Map.TalismanCharges.TryGetValue(item, out int charges) ? charges : null).Gold;
        string name = texts.Items.TryGetValue(type, out var n) ? context.Text(n) : type;
        _requests.Add(new UiOpen(TradePriceLogic.Screen, new TradePriceLogic(context, targetId,
            new AiTradeOffer(AiGiftKind.Item, item, fort.Id, 0), ItemCells.IconFrame(type, context), name, price)));
    }
}

/// <summary>
/// Продажа заклинания из окна дипломатии (<c>CTradeSpellInterf</c> <c>0x4b9107</c>): книга <c>DLG_CAST_SPELL</c> без «Сотворить», с кнопкой
/// <c>BTN_TRADE_SPELL</c>; в списке — все известные заклинания (цель в отбор не входит). Призыв — <c>X005TA0638</c>, иначе окно цены
/// (<see cref="TradePriceLogic"/>, цена — золото <c>BUY_C</c>). Нет заклинаний, кроме призыва, — окно не открывается (<c>X005TA0505</c>).
/// </summary>
public sealed class TradeSpellLogic(WindowContext context, string targetId, IReadOnlyDictionary<string, SpellText> texts)
    : SpellBookLogic(context, texts)
{
    public const string Screen = "DLG_CAST_SPELL";

    protected override string ActionButton => "BTN_TRADE_SPELL";

    protected override IReadOnlyList<string> SpellsOf(int level)
    {
        var spells = C.Session.RequireBook().Spells;
        return [.. C.Player.KnownSpells.Where(s => spells.TryGetValue(s, out var spell) && spell.Level == level)
                                       .Order(StringComparer.OrdinalIgnoreCase)];
    }

    protected override int StartLevel()
    {
        for (int level = 1; level <= 5; level++)
            if (SpellsOf(level).Count > 0) return level;
        return 1;
    }

    protected override void Remember(int level)
    {
    }

    protected override string? Unavailable(int level) => null;

    protected override bool Marked(string spellId) => false;

    protected override bool CanAct(string spellId) => true;

    protected override string Cost(string spellId) =>
        C.Text("X005TA0207") + ManaLines(C.Session.RequireBook().Spells[spellId].CastingCost);

    protected override void Opened(UiScreenState ui) => ui.SetHidden("BTN_CAST_SPELL", true);

    protected override void Act(string spellId)
    {
        if (!C.Session.RequireBook().Spells.TryGetValue(spellId, out var spell)) return;
        if (spell.Category == SpellCategory.Summon)
        {
            Requests.Add(new UiTell(C.Text("X005TA0638")));
            return;
        }
        string capital = C.Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(C.Viewer, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
        Requests.Add(new UiOpen(TradePriceLogic.Screen, new TradePriceLogic(C, targetId, new AiTradeOffer(AiGiftKind.Spell, spellId, capital, 0),
            $"IconSpel:{spellId.ToUpperInvariant()}", Name(spellId), spell.BuyCost.Gold)));
    }
}

/// <summary>
/// Цена сделки (<c>DLG_GIVE_ITEM_SPELL</c>, <c>CPriceInterf</c> <c>0x4b6dc0</c>/<c>CTradePriceInterf</c> <c>0x4b89af</c>): <c>TXT_ACTION</c> —
/// <c>X005TA0330</c> «Продать %PLAYER%»; <c>TXT_INFO</c> — <c>X005TA0321</c> (предмет) / <c>X005TA0320</c> (заклинание): имя, цена
/// (<paramref name="price"/>: ценность предмета, у талисмана × заряды; <c>BUY_C</c> заклинания), <c>%HIGHER%</c> = 2 × цена; значок, лица
/// обоих лордов. Сумма <c>TXT_GOLD</c> — от цены стрелками по 50 (<c>0x4b723b</c>), не больше 2 × цена и не меньше 50 (<c>0x4b71c8</c>).
/// «ОК» (<c>0x4b8e90</c>) — сделка сессии А (<see cref="GameSession.ProposeTradeBy(string, string, AiGiftKind, string, int, string?, List{string}?)"/>), окно закрывается, итог — <c>X005TA0302</c> «думает над
/// ответом» и <c>X005TA0038</c> принято / <c>X005TA0037</c> отвергнуто (<c>CTaskTradeAccept</c> <c>0x4d69d3</c>).
/// </summary>
public sealed class TradePriceLogic(WindowContext context, string targetId, AiTradeOffer draft, string icon, string name, long price) : IWindowLogic
{
    public const string Screen = "DLG_GIVE_ITEM_SPELL";
    private const int Step = 50;

    /// <summary>Наименьшая сумма (<c>0x4b71c8</c>: «не меньше 50»).</summary>
    public const int MinGold = 50;
    private readonly UiRequests _requests = new();

    public long Amount { get; private set; } = Math.Max(MinGold, price);

    /// <summary>Итог отправки (<see cref="ProposalResult"/>); не отправлено — null.</summary>
    public ProposalResult? Answer { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"цена={Amount} ответ={Answer switch { { Accepted: true } => "да", { Accepted: false } => "нет", { Refusal: null } => "ждёт", null => "-", _ => "отказ" }}";

    private long Max => 2 * price;

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or "BTN_CANCEL" or "BTN_UP" or "BTN_DOWN");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var target = context.Map.Players[targetId];
        ui.SetText("TXT_ACTION", UiTemplate.Fill(context.Text("X005TA0330"), ("PLAYER", target.Name)));
        ui.SetText("TXT_INFO", UiTemplate.Fill(context.Text(draft.Kind == AiGiftKind.Item ? "X005TA0321" : "X005TA0320"),
                                               ("NAME", name), ("PRICE", price), ("HIGHER", Max)));
        ui.SetImage("IMG_ITEM", icon);
        ui.SetImage("IMG_FACE1", Face(context.Player));
        ui.SetImage("IMG_FACE2", Face(target));
        Show(ui);
    }

    private string Face(MapPlayer player)
    {
        int type = context.Session.RequireBook().Economy.Races.TryGetValue(player.RaceId, out var r) ? r.RaceType : 0;
        return LordFaces.Small(type, player.Face);
    }

    private void Show(UiScreenState ui) => ui.SetText("TXT_GOLD", Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiClicked { Element: "BTN_UP" }:
                Amount = Clamp(Amount + Step);
                Show(ui);
                break;
            case UiClicked { Element: "BTN_DOWN" }:
                Amount = Clamp(Amount - Step);
                Show(ui);
                break;
            case UiClicked { Element: "BTN_CANCEL" }:
                _requests.Add(new UiClose());
                break;
            case UiClicked { Element: "BTN_OK" }:
                Send();
                break;
        }
    }

    /// <summary>Сначала не больше 2 × цена, затем не меньше 50 (так у движка: при цене меньше 25 выходит 50).</summary>
    private long Clamp(long v) => Math.Max(MinGold, Math.Min(v, Max));

    /// <summary>Что продано: тип предмета (экземпляр в складе — по его типу) или заклинание — для уведомления показа.</summary>
    private string TradedType(AiTradeOffer offer) =>
        offer.Kind == AiGiftKind.Item && context.Session.ItemOf(offer.Object) is { } item ? item.Id : offer.Object;

    private void Send()
    {
        var result = context.Session.Run<ProposalResult>(new ProposeTradeCommand(context.Viewer, targetId, draft.Kind, draft.Object, (int)Amount,
                                                                                draft.Kind == AiGiftKind.Item ? draft.Container : null));
        Answer = result;
        _requests.Add(new UiClose());
        if (result.Refusal is { } refused)
        {
            _requests.Add(new UiTell(refused));
            return;
        }
        // Ждёт ответа человека («горячий стул»): сразу гонец и окно адресата (HotseatCourierLogic), итог — после его ответа.
        if (result.Accepted is not { } yes) return;
        var target = context.Map.Players[targetId];
        context.Announce(yes ? TurnSightKind.Traded : TurnSightKind.TradeRefused, TradedType(draft));   // звук — показ (сессия Г)
        _requests.Add(new UiTell(Result(context, target.Name, yes)));
    }

    /// <summary>Итог сделки предложившему (<c>CTaskTradeAccept</c> <c>0x4d69d3</c>): <c>X005TA0302</c> «думает над ответом» и
    /// <c>X005TA0038</c> принято / <c>X005TA0037</c> отвергнуто (склейка — ранг 2).</summary>
    public static string Result(WindowContext context, string addressee, bool accepted)
    {
        ArgumentNullException.ThrowIfNull(context);
        return UiTemplate.Fill(context.Text("X005TA0302"), ("PLAYER", addressee)) + context.Text(accepted ? "X005TA0038" : "X005TA0037");
    }
}
