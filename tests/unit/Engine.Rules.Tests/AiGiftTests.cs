using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Tests;

/// <summary>Выбор «подарка» ИИ (<see cref="AiGift"/>, <c>0x448c51</c>/<c>0x448e84</c>/<c>0x448fc6</c>) на заданных бросках.</summary>
public class AiGiftTests
{
    /// <summary>Перемешивание движка бросает <c>rng(i)</c> для <c>i = 1 … n − 1</c> и меняет местами <c>a[i]</c> и <c>a[j]</c>:
    /// первый шаг всегда меняет первые два.</summary>
    [Fact]
    public void Shuffle_UsesUpperBoundI()
    {
        var list = new List<string> { "a", "b", "c", "d" };
        var rng = new ScriptedRng([0, 1, 0]);   // rng(1)=0, rng(2)=1, rng(3)=0
        AiGift.Shuffle(rng, list);
        // i=1: swap(1,0) → b a c d; i=2: swap(2,1) → b c a d; i=3: swap(3,0) → d c a b
        Assert.Equal(["d", "c", "a", "b"], list);
        Assert.Equal(3, rng.Used);
        Assert.Equal(3, AiGift.Draws(4));
        Assert.Equal(0, AiGift.Draws(1));
        Assert.Equal(0, AiGift.Draws(0));
    }

    /// <summary>Предмет: после перемешивания берётся первый дешевле 1000, цена сделки — ценность + отношение
    /// (probe65 seq 233142: ценность 400, отношение 100 → 500).</summary>
    [Fact]
    public void PickItem_FirstBelowLimit_PricePlusRelation()
    {
        var items = new List<AiGiftItem> { new("FT1", "IM1", 1000), new("FT1", "IM2", 400), new("FT2", "IM3", 150) };
        // rng(1)=0 → IM2 IM1 IM3; rng(2)=1 → IM2 IM3 IM1: первый подходящий — IM2.
        var offer = AiGift.PickItem(new ScriptedRng([0, 1]), items, 100);
        Assert.Equal(new AiTradeOffer(AiGiftKind.Item, "IM2", "FT1", 500), offer);
        Assert.Null(AiGift.PickItem(new ScriptedRng([0]), [new("FT1", "IM1", 1000), new("FT1", "IM2", 5000)], 100));
        Assert.Null(AiGift.PickItem(new ScriptedRng([]), [], 100));
    }

    /// <summary>Заклинание: уровень 3 и выше не предлагается, цена — золото <c>BUY_C</c> + отношение (probe65: 200 + 100 = 300).</summary>
    [Fact]
    public void PickSpell_BelowLevel3_BuyPlusRelation()
    {
        var spells = new List<AiGiftSpell> { new("G000SS0003", 3, 500), new("G000SS0097", 1, 200) };
        var offer = AiGift.PickSpell(new ScriptedRng([0]), spells, 100);   // rng(1)=0 → SS0097 первым
        Assert.Equal(new AiTradeOffer(AiGiftKind.Spell, "G000SS0097", "", 300), offer);
        Assert.Null(AiGift.PickSpell(new ScriptedRng([]), [new("G000SS0003", 4, 500)], 100));
    }

    /// <summary>Золото: союзнику нет; иначе отношение × 2, если столько есть в казне.</summary>
    [Fact]
    public void Gold_NotToAlly_TwiceRelation()
    {
        Assert.Null(AiGift.Gold(allies: true, 90, 5000));
        Assert.Equal(new AiTradeOffer(AiGiftKind.Gold, "", "", 180), AiGift.Gold(allies: false, 90, 180));
        Assert.Null(AiGift.Gold(allies: false, 90, 179));
    }

    /// <summary>Оценка с выбором: вид выпал, а выбора нет — действия нет; выбор есть — «подарок» с содержимым.</summary>
    [Fact]
    public void Evaluate_GiftNeedsOffer()
    {
        var pair = new Relation(0, 5, 100u << 1);
        // Не союзник, rel 100 > D_NEUTRAL: бросок предложения (≥ шанса — нет), порог подарка 20, вид.
        var none = AiDiplomacy.Evaluate(new ScriptedRng([99, 0, 0]), DiplomacyRules.Vanilla, pair, 5, 1, 1, 0, pickGift: _ => null);
        Assert.Equal(AiDiploAction.None, none.Action);
        Assert.Equal(AiGiftKind.Item, none.Gift);
        var offer = new AiTradeOffer(AiGiftKind.Item, "IM1", "FT1", 500);
        var gift = AiDiplomacy.Evaluate(new ScriptedRng([99, 0, 0]), DiplomacyRules.Vanilla, pair, 5, 1, 1, 0, pickGift: _ => offer);
        Assert.Equal(new AiDiploDecision(AiDiploAction.Gift, AiGiftKind.Item, offer), gift);
    }
}
