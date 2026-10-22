using LastEdict.Data.Menu;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Предложение ИИ продать человеку предмет или заклинание (<c>CCmdProposeTradeMsg</c>: кто, кому, вещь, цена); у предмета
/// <see cref="ThingId"/> — тип (<c>GItem</c>), у заклинания — его id.</summary>
public sealed record TradeOffer(string From, string To, string ThingId, bool IsItem, long Price)
{
    /// <summary>Из сделки партии (<see cref="Play.AllianceOffer.Trade"/>, модель сессии А): экземпляр предмета — в его тип; «золото в
    /// подарок» и не сделка — null.</summary>
    public static TradeOffer? Of(Play.AllianceOffer offer, LastEdict.Rules.Map.MapState map)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(map);
        if (offer.Trade is not { Kind: not LastEdict.Rules.Map.AiGiftKind.Gold } trade) return null;
        bool item = trade.Kind == LastEdict.Rules.Map.AiGiftKind.Item;
        string thing = item && map.ItemTypes.TryGetValue(trade.Object, out var type) ? type : trade.Object;
        return new TradeOffer(offer.From, offer.To, thing, item, trade.Gold);
    }
}

/// <summary>
/// Окно предложения ИИ (<c>DLG_OFFER</c>, <c>COfferInterf</c> <c>0x4b6126</c>) — ИИ продаёт человеку предмет или заклинание за золото:
/// <c>X005TA0586</c> (предмет) / <c>X005TA0587</c> (заклинание) с именем и расой предлагающего, вещью, ценой и реальной стоимостью
/// (<c>VALUE</c> предмета, у заклинания — <c>BUY_C</c>), имя вещи, её значок, лицо лорда, золото получателя. «Отказать» — ответ
/// «нет»; «Принять»: золота меньше цены — <c>X005TA0609</c> и отказ, иначе ответ «да» (<c>CAcceptTradeMsg</c>). Окно закрывается;
/// реплику ИИ показывает хозяин.
/// </summary>
public sealed class TradeOfferLogic(WindowContext context, TradeOffer offer, IReadOnlyDictionary<string, string> raceNames,
                                    Func<string, string> thingName, Action<bool> answer) : IWindowLogic
{
    public const string Screen = "DLG_OFFER";
    private readonly UiRequests _requests = new();

    public TradeOffer Offer => offer;

    /// <summary>Ответ дан: true — принято, false — отказ; нет — null.</summary>
    public bool? Answered { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"сделка={offer.ThingId} цена={offer.Price} ответ={Answered switch { true => "да", false => "нет", _ => "-" }}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var map = context.Map;
        var book = context.Session.RequireBook();
        var from = map.Players[offer.From];
        string race = raceNames.TryGetValue(from.RaceId, out var txt) ? context.Text(txt) : from.RaceId;
        long value = offer.IsItem
            ? book.Items.TryGetValue(offer.ThingId, out var item) ? item.Value.Gold : 0
            : book.Spells.TryGetValue(offer.ThingId, out var spell) ? spell.BuyCost.Gold : 0;
        string name = thingName(offer.ThingId);
        ui.SetText("TXT_INFO", UiTemplate.Fill(context.Text(offer.IsItem ? "X005TA0586" : "X005TA0587"), ("PLAYER", from.Name), ("RACE", race),
            ("OFFER", name), ("PRICE", offer.Price), ("VALUE", value)));
        ui.SetText("TXT_ITEM_NAME", name);
        ui.SetImage("IMG_TRADED_ITEM", offer.IsItem ? ItemCells.IconFrame(offer.ThingId, context) : $"IconSpel:{ArtNames.Of(offer.ThingId)}");
        int type = book.Economy.Races.TryGetValue(from.RaceId, out var r) ? r.RaceType : 0;
        ui.SetImage("IMG_FACE2", LordFaces.Large(type, from.Face));
        ui.SetText("TXT_GOLD", $"{Gold}");
    }

    private long Gold => context.Map.Players.TryGetValue(offer.To, out var to) ? to.Bank.Gold : 0;

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_ACCEPT" or "BTN_CANCEL");

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiClicked { Element: "BTN_CANCEL" }:
                Answer(false);
                break;
            case UiClicked { Element: "BTN_ACCEPT" } when Gold < offer.Price:
                // 0x4b6ab6: не хватает золота — сообщение, по его «ОК» — отказ (0x4b6be6).
                _requests.Add(new UiClose());
                _requests.Add(new UiTell(context.Text("X005TA0609")));
                Answered = false;
                answer(false);
                break;
            case UiClicked { Element: "BTN_ACCEPT" }:
                Answer(true);
                break;
        }
    }

    private void Answer(bool yes)
    {
        Answered = yes;
        _requests.Add(new UiClose());
        answer(yes);
    }
}
