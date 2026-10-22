using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Magic;

/// <summary>Предметы содержимого (<see cref="ItemDef"/>) → <see cref="Item"/> правил (запись живёт в <c>LastEdict.Rules.Magic</c>, второй копии
/// нет). Ссылки «не задано» — пустая строка; пустая цена — <see cref="LastEdict.Rules.Map.Bank.Zero"/> (у 315 предметов оригинала из 336
/// применение ничего не стоит). Категория — перечень <see cref="ItemCategory"/>: значение вне него не проходит импорт и схему.</summary>
public static class ItemLoader
{
    public static IReadOnlyDictionary<string, Item> Load(string globalsDir) => Load(ContentSource.Open(globalsDir));

    public static IReadOnlyDictionary<string, Item> Load(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in content.Items)
            if (!items.TryAdd(i.Key, new Item(i.Key, i.Category, Prices.ToBank(i.Value), content.KeyOf(i.EquipModifier),
                    content.KeyOf(i.PotionModifier), i.PotionHeal ?? 0, content.KeyOf(i.Spell), Prices.ToBank(i.UseCost),
                    content.KeyOf(i.Attack), content.KeyOf(i.Unit))))
                throw new InvalidDataException($"предмет «{i.Key}» объявлен дважды");
        return items;
    }
}
