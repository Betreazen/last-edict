using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>Торговля у сайтов на синтетике — правила, закрытые разбором синтетических карт 16.09.2026
/// (<c>disciples-modding/docs/synthetic-maps/analysis</c>: A-leaders §5, E-map-economy §2).</summary>
public class SiteTradeTests
{
    private static Bank G(int gold) => Bank.Parse($"g{gold:0000}");

    private static Item Of(string id, ItemCategory category, int value) =>
        new(id, category, G(value), "", "", 0, "", Bank.Zero, "", "");

    /// <summary>ai-q53 (ранг 1, 1/0): ростер лагеря S001SI0002 [(5040, uniq), (5039, 4), (5013, 1)] после найма
    /// Barbarian Warrior — [(5013, 1), (5039, 4)]: на место удалённого встал последний.</summary>
    [Fact]
    public void HireUnit_UniqueLeaves_LastOfferTakesItsPlace()
    {
        var roster = new List<MercenaryOffer>
        {
            new("G000UU5040", 1, true), new("G000UU5039", 4, false), new("G000UU5013", 1, false),
        };

        var hire = SiteTrade.HireUnit(roster, "G000UU5040", true, 6, G(850), G(4580));

        Assert.Equal([roster[2], roster[1]], hire.State);
        Assert.Equal(G(3730), hire.Purse);
        Assert.Equal(3, roster.Count);                       // исходный ростер не тронут
    }

    /// <summary>ai-q20b (M-run17 §q20b, <c>0x498291</c>): свободно = <c>max(0, min(лидерство, 6) − занятые)</c>.
    /// KC002f: лидерство 4 + 3 навыка = 7 при 6 занятых — <c>no_space</c> (seq 699012, 709729), вместимость ≤ 6.</summary>
    [Fact]
    public void FreeCells_LeadershipCappedAtSix()
    {
        Assert.Equal(0, SiteTrade.FreeCells(7, 6));
        Assert.Equal(1, SiteTrade.FreeCells(7, 5));
        Assert.Equal(2, SiteTrade.FreeCells(4, 2));
        Assert.Equal(0, SiteTrade.FreeCells(3, 4));
    }

    /// <summary>ai-q20b: свободно 0 — <c>X005TA0247</c>; свободно 1 — большой не нанимается (<c>X005TA0501</c>),
    /// малый — да; пустой ростер — <c>X005TA0514</c>. При отказе ростер и казна прежние.</summary>
    [Fact]
    public void HireUnit_RefusesWithoutRoom()
    {
        var roster = new List<MercenaryOffer> { new("G000UU5039", 1, false) };

        var full = SiteTrade.HireUnit(roster, "G000UU5039", true, 0, G(100), G(1000));
        Assert.StartsWith("X005TA0247", full.Refused);
        Assert.Same(roster, full.State);
        Assert.Equal(G(1000), full.Purse);

        Assert.StartsWith("X005TA0501", SiteTrade.HireUnit(roster, "G000UU5039", false, 1, G(100), G(1000)).Refused);
        Assert.True(SiteTrade.HireUnit(roster, "G000UU5039", true, 1, G(100), G(1000)).Done);
        Assert.True(SiteTrade.HireUnit(roster, "G000UU5039", false, 2, G(100), G(1000)).Done);
        Assert.StartsWith("X005TA0514", SiteTrade.HireUnit([], "G000UU5039", true, 6, G(100), G(1000)).Refused);
    }

    /// <summary>ai-q20 (ранг 1): при <c>BUY_WEAPON=0</c> Runic Blade (L_WEAPON) продан за 400; Totem (L_SPECIAL)
    /// — отказ, казна прежняя. Флагов у <see cref="SiteTrade.SellItem"/> нет — сервер их не читает.</summary>
    [Fact]
    public void SellItem_RefusesOnlySpecial()
    {
        var stock = new List<MerchantStock> { new("G000IG0001", 1) };

        var blade = SiteTrade.SellItem(stock, Of("G000IG2001", ItemCategory.Weapon, 2000), 20, G(100));
        Assert.True(blade.Done);
        Assert.Equal(G(500), blade.Purse);

        var totem = SiteTrade.SellItem(stock, Of("G000IG9001", ItemCategory.Special, 500), 20, G(100));
        Assert.False(totem.Done);
        Assert.Equal(G(100), totem.Purse);
        Assert.Same(stock, totem.State);
    }

    /// <summary>И47: скидка лидера у торговца и мага (<c>0x5868d6</c>) — каждая валюта × (100 − скидка) / 100 с отбросом дроби; 0 — та же цена.</summary>
    [Fact]
    public void Discounted_EveryCurrency_Truncated()
    {
        var cost = new Bank(155, 33, 0, 9, 0, 1);

        Assert.Same(cost, SiteTrade.Discounted(cost, 0));
        Assert.Equal(new Bank(139, 29, 0, 8, 0, 0), SiteTrade.Discounted(cost, 10));
    }

    /// <summary>И47: покупка у торговца и мага со скидкой — казна платит цену со скидкой; хватает только на неё — покупка есть.</summary>
    [Fact]
    public void Buy_WithLeaderDiscount_PaysDiscountedPrice()
    {
        var item = Of("G000IG0005", ItemCategory.PotionHeal, 150);
        var stock = new List<MerchantStock> { new("G000IG0005", 1) };

        Assert.False(SiteTrade.BuyItem(stock, item, G(140)).Done);
        var bought = SiteTrade.BuyItem(stock, item, G(140), lowerCostPercent: 10);

        Assert.True(bought.Done);
        Assert.Equal(G(5), bought.Purse);
    }
}
