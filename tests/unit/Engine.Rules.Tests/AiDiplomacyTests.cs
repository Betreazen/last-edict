using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Tests;

/// <summary>
/// Дипломатия ИИ на карте (<see cref="AiDiplomacy"/>) на числах разбора
/// <c>docs/synthetic-maps/analysis/J-map-ai.md</c> (карты ai-q45, ai-q45b, ai-q46, ai-q49, q09);
/// броски — <see cref="ScriptedRng"/>, как их дал шаг R250 между сохранениями.
/// </summary>
public class AiDiplomacyTests
{
    private static readonly DiplomacyRules Rules = DiplomacyRules.Vanilla;
    private const int Empire = 0, Legions = 2, Elves = 5;

    /// <summary>Карта size×size: <paramref name="plain"/> клеток равнины, из них <paramref name="own"/>
    /// с террейном <paramref name="terrain"/>, остальная равнина нейтральная; хвост — вода и лес
    /// своего террейна (не должны считаться).</summary>
    private static MapState Map(int size, int plain, int own, int terrain)
    {
        var tiles = new Tile[size, size];
        int i = 0;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++, i++)
            {
                int t = i < own ? terrain : 5;
                int g = i < plain ? (int)Ground.Plain : (i % 2 == 0 ? (int)Ground.Water : (int)Ground.Forest);
                if (i >= plain) t = terrain;
                tiles[x, y] = new Tile((byte)(t | (g << 3)));
            }
        return new MapState
        {
            Size = size, Tiles = tiles, Turn = 1,
            Stacks = new Dictionary<string, MapStack>(), Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(), Objects = new Dictionary<string, MapObject>(),
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(),
        };
    }

    [Fact]
    public void Q45_share_4_chance_56_roll_25_accepts()
    {
        int s = AiDiplomacy.PlainLandShare(Map(67, 4391, 176, 1), Empire);
        Assert.Equal(4, s);
        Assert.Equal(56, AiDiplomacy.AnswerAllianceChance(70, s));
        var rng = new ScriptedRng([25]);
        Assert.True(AiDiplomacy.AnswerAlliance(rng, 70, s));
        Assert.Equal(1, rng.Used);
    }

    [Fact]
    public void Q45b_share_30_chance_27()
    {
        int s = AiDiplomacy.PlainLandShare(Map(20, 349, 108, 1), Empire);
        Assert.Equal(30, s);
        Assert.Equal(27, AiDiplomacy.AnswerAllianceChance(67, s));
        Assert.False(AiDiplomacy.AnswerAlliance(new ScriptedRng([27]), 67, s));
        Assert.True(AiDiplomacy.AnswerAlliance(new ScriptedRng([26]), 67, s));
    }

    [Fact]
    public void Alliance_chance_floor_is_5() => Assert.Equal(5, AiDiplomacy.AnswerAllianceChance(20, 40));

    [Theory]
    [InlineData(50, 1000, 97, 91)]
    [InlineData(50, 400, 93, 37)]
    [InlineData(200, 250, 60, 24)]
    [InlineData(50, 150, 83, 45)]
    [InlineData(300, 400, 62, 62)] // q09: ИИ–ИИ, отказ
    public void Q46_trade_chances(int gold, int price, int chance, int roll)
    {
        Assert.Equal(chance, AiDiplomacy.TradeChance(gold, price));
        var rng = new ScriptedRng([roll]);
        Assert.Equal(roll < chance, AiDiplomacy.AnswerTrade(rng, gold, price, aiGold: 5000, known: false));
        Assert.Equal(1, rng.Used);
    }

    [Theory]
    [InlineData(300, 200, 34)] // доплата больше цены — шанс ниже половины
    [InlineData(250, 250, 50)]
    [InlineData(0, 250, 100)]
    public void Trade_chance_other_branch(int gold, int price, int chance) =>
        Assert.Equal(chance, AiDiplomacy.TradeChance(gold, price));

    [Fact]
    public void Trade_refused_without_roll_when_ai_short_of_gold_or_spell_known()
    {
        var rng = new ScriptedRng([]);
        Assert.False(AiDiplomacy.AnswerTrade(rng, 300, 400, aiGold: 299, known: false));
        Assert.False(AiDiplomacy.AnswerTrade(rng, 300, 400, aiGold: 5000, known: true));
        Assert.Equal(0, rng.Used);
    }

    [Fact]
    public void Spell_price_q09_is_400() => Assert.Equal(400, AiDiplomacy.SpellTradePrice(200, 1));

    [Fact]
    public void Exchange_map_accepts_below_30()
    {
        Assert.True(AiDiplomacy.AnswerExchangeMap(new ScriptedRng([29])));
        Assert.False(AiDiplomacy.AnswerExchangeMap(new ScriptedRng([30])));
    }

    [Fact]
    public void Offer_no_roll_at_or_below_neutral()
    {
        Assert.Null(AiDiplomacy.OfferChance(Rules, 66, turn: 1, share: 0));
        var rng = new ScriptedRng([]);
        Assert.False(AiDiplomacy.ShouldOffer(rng, Rules, 66, 1, 0));
        Assert.Equal(0, rng.Used);
    }

    [Theory]
    [InlineData(95, 1, 40, 100)] // день 1, rel ≥ 90 — всегда
    [InlineData(95, 2, 40, 45)]
    [InlineData(89, 1, 10, 69)]
    [InlineData(67, 3, 60, 5)]
    public void Offer_chance(int rel, int turn, int share, int chance) =>
        Assert.Equal(chance, AiDiplomacy.OfferChance(Rules, rel, turn, share));

    [Fact]
    public void Offer_rolls_once_even_at_100()
    {
        var rng = new ScriptedRng([99]);
        Assert.True(AiDiplomacy.ShouldOffer(rng, Rules, 95, 1, 40));
        Assert.Equal(1, rng.Used);
    }

    private static Relation Ally(int allianceTurn, bool noBreak = false) =>
        new(Elves, Legions, 1u | (100u << 1) | ((uint)allianceTurn << 15) | (noBreak ? 1u << 31 : 0));

    [Fact]
    public void Q49_legions_stronger_T5_d9_chance_8()
    {
        var pair = Ally(allianceTurn: 1);
        Assert.Equal(8, AiDiplomacy.BreakChance(pair, turn: 10, ownForts: 2, otherForts: 1));
        Assert.True(AiDiplomacy.ShouldBreak(new ScriptedRng([7]), pair, 10, 2, 1));
        Assert.False(AiDiplomacy.ShouldBreak(new ScriptedRng([8]), pair, 10, 2, 1));
        Assert.Null(AiDiplomacy.BreakChance(pair, turn: 10, ownForts: 1, otherForts: 1)); // без «сильнее» — нет
    }

    [Fact]
    public void Q09_equal_forts_T10_d15_chance_5() =>
        Assert.Equal(5, AiDiplomacy.BreakChance(Ally(allianceTurn: 1), turn: 16, ownForts: 2, otherForts: 2));

    [Fact]
    public void Break_no_roll_within_T_or_with_bit31()
    {
        var rng = new ScriptedRng([]);
        Assert.False(AiDiplomacy.ShouldBreak(rng, Ally(1), 11, 1, 1));            // d = 10 = T
        Assert.False(AiDiplomacy.ShouldBreak(rng, Ally(1, noBreak: true), 40, 2, 1));
        Assert.Equal(0, rng.Used);
    }

    [Theory]
    [InlineData(0, 16, 2, 2, 5)]  // run17 q09: равные силы, d = 16, T = 10 → min(6, 5)
    [InlineData(0, 6, 2, 1, 2)]   // сильнее: T = 5, min(1, 5)·2
    public void Run17_break_chance(int allianceTurn, int turn, int own, int other, int chance) =>
        Assert.Equal(chance, AiDiplomacy.BreakChance(Ally(allianceTurn), turn, own, other));

    [Fact]
    public void Run17_break_d10_equal_no_roll()
    {
        Assert.Null(AiDiplomacy.BreakChance(Ally(0), turn: 10, ownForts: 2, otherForts: 2));
        var rng = new ScriptedRng([]);
        Assert.False(AiDiplomacy.ShouldBreak(rng, Ally(0), 10, 2, 2));
        Assert.Equal(0, rng.Used);
    }

    [Fact] // seq 11505827: PL4→PL3, день 16, шанс 5, бросок 2
    public void Run17_seq11505827_break_fires() =>
        Assert.True(AiDiplomacy.ShouldBreak(new ScriptedRng([2]), Ally(0), 16, 2, 2));

    [Fact] // seq 9200672: бросок 20 при пороге 20 — подарка нет, вид не бросается
    public void Run17_gift_roll_20_at_threshold_20_no_gift()
    {
        var rng = new ScriptedRng([20]);
        Assert.Null(AiDiplomacy.GiftRoll(rng, Rules, 100));
        Assert.Equal(1, rng.Used);
    }

    private static Relation Peace(int rel) => new(Elves, Legions, (uint)rel << 1);

    [Fact]
    public void Evaluate_ally_break_skips_gift()
    {
        var rng = new ScriptedRng([2]);
        var d = AiDiplomacy.Evaluate(rng, Rules, Ally(0), 16, 2, 2, otherShare: 0);
        Assert.Equal(new AiDiploDecision(AiDiploAction.Break, null), d);
        Assert.Equal(1, rng.Used);
    }

    [Fact]
    public void Evaluate_ally_no_break_then_gift()
    {
        var rng = new ScriptedRng([5, 3, 1]); // разрыв 5 ≥ 5 — нет; подарок 3 < 20; вид 1
        var d = AiDiplomacy.Evaluate(rng, Rules, Ally(0), 16, 2, 2, otherShare: 0);
        Assert.Equal(new AiDiploDecision(AiDiploAction.Gift, AiGiftKind.Spell), d);
        Assert.Equal(3, rng.Used);
    }

    [Fact]
    public void Evaluate_offer_success_skips_gift()
    {
        var rng = new ScriptedRng([99]);
        Assert.Equal(AiDiploAction.Offer,
                     AiDiplomacy.Evaluate(rng, Rules, Peace(95), 1, 1, 1, otherShare: 0).Action);
        Assert.Equal(1, rng.Used);
    }

    [Fact]
    public void Evaluate_day1_offer_fails_hate_then_gift()
    {
        var rng = new ScriptedRng([80, 0, 2]); // предложение: шанс 70, 80 — нет; подарок: 0 < 16; вид 2
        var d = AiDiplomacy.Evaluate(rng, Rules, Peace(80), 1, 1, 1, otherShare: 0);
        Assert.Equal(new AiDiploDecision(AiDiploAction.Hate, AiGiftKind.Gold), d);
        Assert.Equal(3, rng.Used);
    }

    [Fact]
    public void Evaluate_day1_neutral_hate_without_rolls()
    {
        var rng = new ScriptedRng([]);
        Assert.Equal(new AiDiploDecision(AiDiploAction.Hate, null),
                     AiDiplomacy.Evaluate(rng, Rules, Peace(50), 1, 1, 1, otherShare: 0));
        Assert.Equal(0, rng.Used);
    }

    [Fact]
    public void Evaluate_later_day_neutral_none()
    {
        var rng = new ScriptedRng([]);
        Assert.Equal(AiDiploAction.None, AiDiplomacy.Evaluate(rng, Rules, Peace(50), 2, 1, 1, 0).Action);
        Assert.Equal(0, rng.Used);
    }

    [Fact] // run17 q09: PL2→PL4, шанс 100, ret=True шесть проходов (9191916…9191979), исполнено на седьмом (9191986)
    public void Run17_offer_discarded_by_economy_six_passes_executed_on_seventh()
    {
        var rng = new ScriptedRng([10, 20, 30, 40, 50, 60, 70]);
        bool[] economy = [true, true, true, true, true, true];
        var done = AiDiplomacy.RunPasses(["PL4"], economy,
            _ => AiDiplomacy.Evaluate(rng, Rules, Peace(95), 1, 1, 1, otherShare: 0));
        Assert.Equal([new AiDiploExecuted(7, "PL4", new AiDiploDecision(AiDiploAction.Offer, null))], done);
        Assert.Equal(7, rng.Used);
    }

    [Fact]
    public void RunPasses_executed_target_excluded_one_action_per_pass()
    {
        var calls = new List<string>();
        var done = AiDiplomacy.RunPasses(["PL3", "PL1"], [],
            t => { calls.Add(t); return new AiDiploDecision(AiDiploAction.Offer, null); });
        Assert.Equal([1, 2], done.Select(e => e.Pass));
        Assert.Equal(["PL1", "PL3"], done.Select(e => e.Target));
        Assert.Equal(["PL1", "PL3", "PL3"], calls); // порядок id; исполненная цель выпала
    }

    [Fact]
    public void Gift_two_rolls_then_kind()
    {
        var rng = new ScriptedRng([18, 2]);
        Assert.Equal(AiGiftKind.Gold, AiDiplomacy.GiftRoll(rng, Rules, 95)); // 95·20/100 = 19
        Assert.Equal(2, rng.Used);
        var miss = new ScriptedRng([19]);
        Assert.Null(AiDiplomacy.GiftRoll(miss, Rules, 95));
        Assert.Equal(1, miss.Used);
        var none = new ScriptedRng([]);
        Assert.Null(AiDiplomacy.GiftRoll(none, Rules, 66));
        Assert.Equal(0, none.Used);
    }
}
