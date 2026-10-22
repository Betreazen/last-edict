using D2.Data.Menu;
using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Data.Ui.Windows;

/// <summary>Цена словами (<c>0x61c910</c>): золото <c>X005TA0055</c>, Руны <c>0056</c>, Смерть <c>0057</c>, Жизнь <c>0058</c>, Преисподняя
/// <c>0059</c>, Лес <c>X160TA0001</c>; части — через «, » (<c>0x6e12f4</c>), нулевые пропускаются, всё нулевое — «0».</summary>
public static class PriceText
{
    public static string Words(Func<string, string> text, Bank price)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<string>();
        void Add(long qty, string id)
        {
            if (qty > 0) parts.Add(UiTemplate.Fill(text(id), ("QTY", qty)));
        }
        Add(price.Gold, "X005TA0055");
        Add(price.Runic, "X005TA0056");
        Add(price.Death, "X005TA0057");
        Add(price.Life, "X005TA0058");
        Add(price.Infernal, "X005TA0059");
        Add(price.Grove, "X160TA0001");
        return parts.Count == 0 ? "0" : string.Join(", ", parts);
    }
}

/// <summary>
/// Карточка предмета (<c>DLG_R_C_ITEM</c>, <c>CEncLayoutItem</c> <c>0x578547</c>, заполнение <c>0x57868f</c>) — правой кнопкой по предмету
/// в окнах и по слоту в бою (<c>0x6327cb</c>): значок <c>IconItem</c>, <c>X005TA0660</c> — имя, описание и у жезла стоимость использования
/// (<c>X005TA0661</c>, <c>GItem.CASTING</c>); цена <c>X005TA0529</c> — только у торговца (<c>0x4fe330</c>): <c>VALUE</c>, у талисмана золото
/// × заряды (у типа — 5), каждая валюта × (100 − скидка героя) / 100 (<c>0x578873…0x578886</c>: <c>0x600a20</c>, <c>0x5868d6</c>; скидка —
/// <paramref name="lowerCostPercent"/>, <see cref="Rules.Map.SiteTrade.Discounted"/>). Категория не пишется. Закрывается отпусканием кнопки.
/// </summary>
public sealed class ItemCardLogic(Func<string, string> text, Item item, string nameTxt, string descTxt, int? talismanCharges = null,
                                  bool withPrice = false, string? icon = null, int lowerCostPercent = 0) : IWindowLogic
{
    public const string Screen = "DLG_R_C_ITEM";
    private const int TypeCharges = 5;
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"предмет={item.Id}";

    /// <summary>Цена в карточке; без цены — null.</summary>
    public Bank? Price => !withPrice ? null
        : Rules.Map.SiteTrade.Discounted(item.Category == ItemCategory.Talisman ? item.Value with { Gold = item.Value.Gold * (talismanCharges ?? TypeCharges) }
                                         : item.Value, lowerCostPercent);

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetImage("IMG_SPELL_ICON", icon ?? $"IconItem:{item.Id.ToUpperInvariant()}");
        string cast = item.Category == ItemCategory.Wand
            ? UiTemplate.Fill(text("X005TA0661"), ("CASTCOST", PriceText.Words(text, item.CastingCost))) : "";
        ui.SetText("TXT_SPELL_INFO", UiTemplate.Fill(text("X005TA0660"), ("NAME", text(nameTxt)), ("DESC", text(descTxt)),
                                                     ("CASTCOST", cast), ("ITEMCAT", "")));
        ui.SetText("TXT_PRICE", Price is { } price ? UiTemplate.Fill(text("X005TA0529"), ("PRICE", PriceText.Words(text, price))) : "");
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => Close();

    public void Release(int x, int y, UiScreenState ui) => Close();

    private void Close()
    {
        if (_requests.Count == 0) _requests.Add(new UiClose());
    }
}

/// <summary>Открыть карточку предмета из окон карты.</summary>
public static class ItemCards
{
    /// <summary>Предмет (экземпляр <c>IM</c> или тип <c>IG</c>) — просьба открыть его карточку; нет такого — null. Цена — со скидкой
    /// героя <paramref name="lowerCostPercent"/>.</summary>
    public static UiOpen? For(WindowContext c, string itemId, StackTexts? texts = null, bool withPrice = false, int lowerCostPercent = 0)
    {
        ArgumentNullException.ThrowIfNull(c);
        texts ??= c.ItemTexts ?? new StackTexts(new Dictionary<string, string>(), new Dictionary<string, string>());
        string type = c.Map.ItemTypes.TryGetValue(itemId, out var t) ? t : itemId;
        if (!c.Session.RequireBook().Items.TryGetValue(type, out var item)) return null;
        int? charges = c.Map.TalismanCharges.TryGetValue(itemId, out int n) ? n : null;
        return new UiOpen(ItemCardLogic.Screen, new ItemCardLogic(c.Text, item, texts.Items.GetValueOrDefault(type, ""),
            texts.ItemDescs?.GetValueOrDefault(type, "") ?? "", charges, withPrice, ItemCells.IconFrame(type, c), lowerCostPercent));
    }
}
