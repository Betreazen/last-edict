using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Rng;

namespace D2.Rules.Tests;

/// <summary>Правила выбора заклинаний ИИ на карте (<see cref="AiSpellRules"/>, <c>docs/ai-spells.md</c>) и оплата
/// применения со вторым кошельком (<see cref="SpellCast.Pay(Bank, Bank, Spell, bool)"/>).</summary>
public class AiSpellRulesTests
{
    [Theory]
    [InlineData(new int[0], 1)]
    [InlineData(new[] { 25 }, 1)]
    [InlineData(new[] { 26 }, 2)]
    [InlineData(new[] { 65 }, 2)]
    [InlineData(new[] { 66 }, 3)]
    [InlineData(new[] { 125 }, 3)]
    [InlineData(new[] { 210 }, 4)]
    [InlineData(new[] { 211 }, 5)]
    [InlineData(new[] { 20, 33 }, 2)]   // (20 + 33) / 2 = 26 — целое деление
    public void Tier_ByAverageXpKilled(int[] xp, int tier) => Assert.Equal(tier, AiSpellRules.Tier(xp));

    [Theory]
    [InlineData(1, 5, 2, 2, 2)]
    [InlineData(2, 5, 2, 2, 3)]
    [InlineData(3, 5, 2, 3, 4)]
    [InlineData(4, 5, 3, 3, 5)]
    [InlineData(5, 5, 3, 4, 99)]
    [InlineData(1, 25, 2, 3, 3)]    // день 21–40: наименьший уровень 3, наибольший — не ниже
    [InlineData(4, 44, 3, 4, 5)]
    [InlineData(1, 44, 2, 4, 4)]
    public void StrikePlan_ByTierAndDay(int tier, int day, int count, int min, int max) =>
        Assert.Equal((count, min, max), AiSpellRules.StrikePlan(tier, day));

    /// <summary>0x476bc4 с флагом 0 (заклинание перед атакой): первый ярус — уровни 1…2, остальные как у удара.</summary>
    [Theory]
    [InlineData(1, false, 2, 1, 2)]
    [InlineData(1, true, 2, 2, 2)]
    [InlineData(3, false, 2, 3, 4)]
    [InlineData(5, false, 3, 4, 99)]
    public void TierPlan_RaiseFirstOnlyForStrike(int tier, bool raise, int count, int min, int max) =>
        Assert.Equal((count, min, max), AiSpellRules.TierPlan(tier, raise));

    [Fact]
    public void BadlyHurt_AverageAtMostHalf()
    {
        Assert.True(AiSpellRules.BadlyHurt([(50, 100), (0, 100)]));     // 25 ≤ 50
        Assert.True(AiSpellRules.BadlyHurt([(50, 101)]));               // 101 / 2 = 50
        Assert.False(AiSpellRules.BadlyHurt([(51, 100)]));
        Assert.False(AiSpellRules.BadlyHurt([]));
    }

    [Fact]
    public void KeepHighest_DropsLowerFromTheFrontUntilCount()
    {
        var levels = new List<int> { 1, 2, 2, 3 };
        AiSpellRules.KeepHighest(levels, l => l, keep: 2);
        Assert.Equal([2, 3], levels);
        var same = new List<int> { 2, 2, 2 };
        AiSpellRules.KeepHighest(same, l => l, keep: 1);
        Assert.Equal([2, 2, 2], same);
    }

    /// <summary>Перемешивание <c>0x43949e</c>: <c>n − 1</c> бросков, <c>rng(1)</c> тоже бросок (журнал human, день 44: два
    /// заклинания — ровно один бросок, и первое с последним меняются всегда).</summary>
    [Fact]
    public void Shuffle_SwapsWithRngOfIndex()
    {
        var rng = new ScriptedRng([0, 1]);
        var items = new List<string> { "a", "b", "c" };
        AiSpellRules.Shuffle(items, rng);
        Assert.Equal(2, rng.Used);
        Assert.Equal(["b", "c", "a"], items);   // i=1: обмен с 0 → b a c; i=2: обмен с 1 → b c a
    }

    /// <summary><c>std::list::sort</c> с предикатом «всё меньше» разворачивает список; призывы уходят в конец.</summary>
    [Fact]
    public void ListSort_WithCastPredicate()
    {
        var plain = new[] { SpellCategory.Attack, SpellCategory.Lower, SpellCategory.Attack };
        var sorted = AiSpellRules.ListSort([0, 1, 2], (a, b) => AiSpellRules.SummonsLast(plain[a], plain[b]));
        Assert.Equal([2, 1, 0], sorted);
        var mixed = new[] { SpellCategory.Summon, SpellCategory.Attack };
        Assert.Equal([1, 0], AiSpellRules.ListSort([0, 1], (a, b) => AiSpellRules.SummonsLast(mixed[a], mixed[b])));
        Assert.Equal([1, 2, 3], AiSpellRules.ListSort([3, 1, 2], (a, b) => a < b));
    }

    /// <summary>Журнал human, день 44 (<c>seq</c> 12168909): казна смерти 149 при цене 200 — второй кошелёк смерти 2267 → 2216,
    /// основная казна обнулена целиком, золото тоже.</summary>
    [Fact]
    public void Pay_ShortfallFromSpellBank_ZeroesTreasury()
    {
        var spell = new Spell("g000ss0070", SpellCategory.Attack, 2, new Bank(0, 0, 0, 200, 0, 0), Bank.Zero, 1, "", "", 0, 3, 30, 0, 0, []);
        var (bank, spellBank) = SpellCast.Pay(new Bank(153, 0, 100, 149, 0, 0), new Bank(200, 870, 745, 2267, 445, 0), spell, useSpellBank: true);
        Assert.Equal(Bank.Zero, bank);
        Assert.Equal(new Bank(200, 870, 745, 2216, 445, 0), spellBank);
        var (enough, untouched) = SpellCast.Pay(new Bank(0, 0, 0, 300, 0, 0), new Bank(0, 0, 0, 50, 0, 0), spell, useSpellBank: true);
        Assert.Equal(new Bank(0, 0, 0, 100, 0, 0), enough);
        Assert.Equal(new Bank(0, 0, 0, 50, 0, 0), untouched);
        Assert.False(SpellCast.CanPay(new Bank(0, 0, 0, 149, 0, 0), new Bank(0, 0, 0, 2267, 0, 0), spell, useSpellBank: false));
    }

    /// <summary>Мана для заклинаний на всю карту (<c>0x46d98a</c>): адская, жизни, рун, смерти — каждая больше 1000; золото и
    /// роща не смотрятся; казна — 16-битные слова со знаком.</summary>
    [Theory]
    [InlineData(0, 1001, 1001, 1001, 1001, 0, true)]
    [InlineData(0, 1000, 1001, 1001, 1001, 0, false)]
    [InlineData(0, 1001, 1000, 1001, 1001, 0, false)]
    [InlineData(0, 1001, 1001, 1000, 1001, 0, false)]
    [InlineData(0, 1001, 1001, 1001, 1000, 0, false)]
    [InlineData(0, 40000, 1001, 1001, 1001, 0, false)]   // 40000 в int16 — отрицательное
    public void RichInMana_FourManasAbove1000(long gold, long infernal, long life, long death, long runic, long grove, bool rich) =>
        Assert.Equal(rich, AiSpellRules.RichInMana(new Bank(gold, infernal, life, death, runic, grove)));

    /// <summary>Стоит ли бросать заклинание на всю карту (<c>0x46da60</c>…<c>0x46ddc6</c>).</summary>
    [Theory]
    [InlineData(SpellCategory.Heal, true, 0, 0, true)]
    [InlineData(SpellCategory.Heal, false, 5, 5, false)]
    [InlineData(SpellCategory.Boost, false, 2, 0, true)]
    [InlineData(SpellCategory.Boost, false, 1, 9, false)]
    [InlineData(SpellCategory.Invisibility, false, 2, 0, true)]
    [InlineData(SpellCategory.Attack, false, 9, 1, false)]
    [InlineData(SpellCategory.Attack, false, 0, 2, true)]
    [InlineData(SpellCategory.RestoreMove, true, 9, 9, false)]
    [InlineData(SpellCategory.Unfog, true, 9, 9, false)]
    public void WholeMapWorth_ByCategory(SpellCategory category, bool wounded, int own, int foes, bool worth) =>
        Assert.Equal(worth, AiSpellRules.WholeMapWorth(category, wounded, own, foes));

    /// <summary>«Натиск» (<c>0x46fe5e</c>): проход при <c>rng(100) ≤ 49</c>; число — 1 у далёких (без броска), <c>rng(4) + 2</c> у
    /// близких; категории — ослабление, у близких ещё удар и призыв.</summary>
    [Fact]
    public void Harass_RollsCountAndCategories()
    {
        Assert.True(AiSpellRules.HarassGo(new ScriptedRng([49])));
        Assert.False(AiSpellRules.HarassGo(new ScriptedRng([50])));
        var none = new ScriptedRng([]);
        Assert.Equal(1, AiSpellRules.HarassCount(far: true, none));
        Assert.Equal(0, none.Used);
        Assert.Equal(2, AiSpellRules.HarassCount(far: false, new ScriptedRng([0])));
        Assert.Equal(5, AiSpellRules.HarassCount(far: false, new ScriptedRng([3])));
        Assert.Equal([SpellCategory.Lower], AiSpellRules.HarassCategories(far: true));
        Assert.Equal([SpellCategory.Lower, SpellCategory.Attack, SpellCategory.Summon], AiSpellRules.HarassCategories(far: false));
    }
}
