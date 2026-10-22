using System.Text.Json;
using LastEdict.Rules.Map;

namespace LastEdict.Rules.Tests;

/// <summary>Валюты казны по id (этап Э2, задача E): шесть оригинала — поля <see cref="Bank"/>, валюта мода — <see cref="Bank.Extra"/> с той же
/// арифметикой, равенством, проверкой цены и записью в сохранение; у казны оригинала словаря нет вовсе.</summary>
public class BankCurrencyTests
{
    [Fact]
    public void ModCurrency_AddsSubtractsComparesAndCovers()
    {
        var purse = new Bank(100, 0, 25, 0, 0, 0).With("mymod:amber", 3);
        Assert.Equal((100L, 25L, 3L, 0L), (purse["gold"], purse["life"], purse["mymod:amber"], purse["mymod:other"]));
        Assert.Equal(["gold", "life", "mymod:amber"], purse.Amounts().Select(p => p.Key));

        var cost = Bank.Zero.With("gold", 50).With("mymod:amber", 5);
        Assert.False(purse.Covers(cost));
        Assert.True((purse + Bank.Zero.With("mymod:amber", 2)).Covers(cost));
        Assert.True(Economy.NotEnough(purse, cost));

        var spent = purse - Bank.Zero.With("mymod:amber", 3);
        Assert.Null(spent.Extra);   // ноль валюты — валюты нет: та же казна, что без неё
        Assert.Equal(new Bank(100, 0, 25, 0, 0, 0), spent);
        Assert.Equal(purse, new Bank(100, 0, 25, 0, 0, 0).With("mymod:amber", 3));
        Assert.NotEqual(purse, spent);
    }

    /// <summary>Казна правил — доход, трата, обрезка, доход ИИ, нехватка на литьё — ведёт и валюту мода (прежде пересборка казны из шести
    /// полей её роняла).</summary>
    [Fact]
    public void Treasury_KeepsModCurrency_ThroughIncomeSpendAndClamp()
    {
        var purse = Bank.Zero.With("mymod:amber", 10);
        Assert.Equal(15, Treasury.Apply(purse, Bank.Zero.With("mymod:amber", 5))["mymod:amber"]);
        Assert.Equal(6, Treasury.Pay(purse, Bank.Zero.With("mymod:amber", 4))["mymod:amber"]);
        Assert.Null(Treasury.Pay(purse, Bank.Zero.With("mymod:amber", 40)).Extra);   // обрезка снизу — ноль, валюты нет
        Assert.Equal(Economy.CurrencyMax, Economy.Clamp(Bank.Zero.With("mymod:amber", 99999))["mymod:amber"]);
        var (toPurse, toSpells) = Treasury.AiIncome(Bank.Zero, Bank.Zero, new Bank(100, 0, 25, 0, 0, 0).With("mymod:amber", 9));
        Assert.Equal((100L, 12L, 4L), (toPurse.Gold, toPurse.Life, toPurse["mymod:amber"]));   // валюта мода — как мана: половина в казну
        Assert.Equal((0L, 13L, 5L), (toSpells.Gold, toSpells.Life, toSpells["mymod:amber"]));
        Assert.Equal(new Bank(1, 0, 0, 0, 0, 0) { Extra = new Dictionary<string, long> { ["b"] = 1, ["a"] = 0 } },
                     new Bank(1, 0, 0, 0, 0, 0).With("b", 1));   // нули и порядок — один вид
    }

    [Fact]
    public void Json_KeepsModCurrency_AndOriginalBankHasNoExtra()
    {
        var plain = new Bank(1, 2, 3, 4, 5, 6);
        string json = JsonSerializer.Serialize(plain);
        Assert.DoesNotContain("Extra", json, StringComparison.Ordinal);
        Assert.Equal(plain, JsonSerializer.Deserialize<Bank>(json));

        var modded = plain.With("mymod:amber", 7);
        Assert.Equal(modded, JsonSerializer.Deserialize<Bank>(JsonSerializer.Serialize(modded)));
    }
}
