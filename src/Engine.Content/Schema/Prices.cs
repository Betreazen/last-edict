using D2.Rules.Map;

namespace D2.Data.Content.Schema;

/// <summary>Цена в содержимом — словарь «валюта → количество» (<c>{"gold": 50, "life": 25}</c>), нулевые валюты не пишутся. Валюты —
/// строковые id: шесть оригинала (<see cref="Currencies"/>) и любые валюты мода — новый ключ, а не новое поле; в правилах они уходят в
/// <see cref="Bank.Extra"/> (этап Э2, задача E).</summary>
public static class Prices
{
    /// <summary>Валюты оригинала (<see cref="Bank.Builtin"/>): золото и пять видов маны, в порядке букв казны <c>g r y e w b</c>.</summary>
    public static IReadOnlyList<string> Currencies => Bank.Builtin;

    public static readonly IReadOnlyDictionary<string, long> Free = new Dictionary<string, long>();

    /// <summary>Казна оригинала строкой (<c>g0050:r0000:y0000:e0000:w0000</c>) → цена; пусто — бесплатно.</summary>
    public static IReadOnlyDictionary<string, long> FromLegacy(string text) =>
        string.IsNullOrWhiteSpace(text) ? Free : Of(Bank.Parse(text));

    public static IReadOnlyDictionary<string, long> Of(Bank bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        return bank.Amounts().ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>Цена → казна правил: валюты оригинала — в поля, валюты мода — в <see cref="Bank.Extra"/>.</summary>
    public static Bank ToBank(IReadOnlyDictionary<string, long>? price)
    {
        if (price is null || price.Count == 0) return Bank.Zero;
        return price.Aggregate(Bank.Zero, (bank, p) => bank.With(p.Key, p.Value));
    }
}
