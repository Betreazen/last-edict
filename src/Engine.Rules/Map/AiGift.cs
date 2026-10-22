using D2.Rules.Rng;

namespace D2.Rules.Map;

/// <summary>
/// Сделка-«подарок» ИИ (<c>0x448bda</c>, вид — <see cref="AiGiftKind"/>): предмет или заклинание за золото
/// (<c>CAiDiploItem</c> <c>0x6cf5dc</c>, <c>CAiDiploSpell</c> <c>0x6cf5f4</c> → <c>ProposeTrade 0x41eb74</c>) или просто
/// золото (<c>0x6cf60c</c> → <c>GiveGold 0x41eafb</c>).
/// </summary>
/// <param name="Object">Экземпляр предмета <c>IM</c>, заклинание <c>SS</c>; у золота пусто.</param>
/// <param name="Container">Крепость, в складе которой лежит предмет; у заклинания и золота пусто.</param>
/// <param name="Gold">Сколько цель платит за предмет или заклинание; у вида «золото» — сколько ИИ дарит.</param>
public sealed record AiTradeOffer(AiGiftKind Kind, string Object, string Container, int Gold);

/// <summary>Предмет, который ИИ может предложить: крепость, экземпляр, его ценность золотом (<c>0x600a20</c>).</summary>
public readonly record struct AiGiftItem(string Fort, string Item, int Value);

/// <summary>Заклинание, которое ИИ может предложить: id, <c>LEVEL</c>, золото <c>BUY_C</c>.</summary>
public readonly record struct AiGiftSpell(string Spell, int Level, int BuyGold);

/// <summary>
/// Выбор «подарка» (ранг 1 по листингу; сверка с журналами — <c>AiDiplomacyHarnessTests.Gift_FromSaves</c>). Броски — только
/// перемешивание кандидатов (<see cref="Shuffle{T}"/>), их <see cref="Draws"/>; вид «золото» не бросает.
/// </summary>
public static class AiGift
{
    /// <summary>Предмет дороже этого не предлагается (<c>0x448dd6</c>: <c>cmp 0x3e8; jl</c>).</summary>
    public const int ItemValueLimit = 1000;

    /// <summary>Заклинание этого уровня и выше не предлагается (<c>0x448f36</c>: <c>cmp 3; jl</c>).</summary>
    public const int SpellLevelLimit = 3;

    /// <summary>Золото в подарок — отношение × 2 (<c>0x449010</c>: <c>shl esi, 1</c>).</summary>
    public const int GoldPerRelation = 2;

    /// <summary>
    /// Перемешивание движка (<c>0x449550</c> для пар «крепость, предмет», <c>0x4495d8</c> для заклинаний): для
    /// <c>i = 1 … n − 1</c> — <c>j = rng(i)</c>, обмен <c>a[i]</c> и <c>a[j]</c> (<c>0x449629</c>/<c>0x44966a</c>). Верхняя граница
    /// броска — <c>i</c>, а не <c>i + 1</c>, как у <c>std::random_shuffle</c>: элемент никогда не остаётся на месте броском, а
    /// <c>rng(1)</c> шаг ГСЧ тратит.
    /// </summary>
    public static void Shuffle<T>(IRng rng, IList<T> list)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(list);
        for (int i = 1; i < list.Count; i++)
        {
            int j = rng.Next(i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Шагов ГСЧ на перемешивание <paramref name="candidates"/> кандидатов.</summary>
    public static int Draws(int candidates) => Math.Max(candidates - 1, 0);

    /// <summary>
    /// Предмет (<c>0x448c51</c>): пары «крепость, предмет» из складов своих крепостей перемешиваются, берётся первая с
    /// ценностью меньше <see cref="ItemValueLimit"/>; цена сделки — ценность + отношение. Порядок кандидатов до
    /// перемешивания — порядок крепостей карты и предметов склада.
    /// </summary>
    /// <param name="candidates">Предметы из складов своих крепостей, кроме деревень в бунте (<c>0x5e9582</c>).</param>
    public static AiTradeOffer? PickItem(IRng rng, IReadOnlyList<AiGiftItem> candidates, int relation)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var list = candidates.ToList();
        Shuffle(rng, list);
        return list.FirstOrDefault(c => c.Value < ItemValueLimit) is { Item: not null } pick
            ? new AiTradeOffer(AiGiftKind.Item, pick.Item, pick.Fort, pick.Value + relation)
            : null;
    }

    /// <summary>
    /// Заклинание (<c>0x448e84</c>): известные ИИ, кроме призыва (<c>0x5cdf6f</c>, категория <c>L_SUMMON</c>) и тех, что цель
    /// уже знает (<c>0x5cdde7</c>), — множеством по id, затем перемешивание; берётся первое уровня меньше
    /// <see cref="SpellLevelLimit"/>, цена сделки — золото <c>BUY_C</c> + отношение (без множителя уровня — он есть только
    /// в ответе цели, <see cref="AiDiplomacy.SpellTradePrice"/>).
    /// </summary>
    /// <param name="candidates">Отобранные заклинания в порядке id.</param>
    public static AiTradeOffer? PickSpell(IRng rng, IReadOnlyList<AiGiftSpell> candidates, int relation)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var list = candidates.ToList();
        Shuffle(rng, list);
        return list.FirstOrDefault(c => c.Level < SpellLevelLimit) is { Spell: not null } pick
            ? new AiTradeOffer(AiGiftKind.Spell, pick.Spell, "", pick.BuyGold + relation)
            : null;
    }

    /// <summary>Золото (<c>0x448fc6</c>): союзнику не дарится (<c>0x5f7ebd</c>); подарок — отношение ×
    /// <see cref="GoldPerRelation"/>, если столько есть в казне.</summary>
    public static AiTradeOffer? Gold(bool allies, int relation, int gold)
    {
        int amount = relation * GoldPerRelation;
        return allies || amount > gold ? null : new AiTradeOffer(AiGiftKind.Gold, "", "", amount);
    }
}
