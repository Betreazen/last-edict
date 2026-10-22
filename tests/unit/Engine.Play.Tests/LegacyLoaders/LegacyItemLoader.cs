// Загрузчики правил ДО схемы содержимого (main 639961ee): правила читались прямо из строк таблиц оригинала. Эталон сверки
// «старая модель = новая» (ContentRulesParityTests) — не править; удаляется вместе с ней, когда таблицы уйдут из проверок.
using D2.Data.Battle;
using D2.Data.Dbf;
using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Data.Tests.LegacyLoaders;

/// <summary><c>GItem.DBF</c> -> <see cref="Item"/>. Запись живёт в <c>D2.Rules.Magic</c>, а не
/// здесь: ссылка идёт <c>D2.Data → D2.Rules</c>, и второй копии быть не должно — так же поступили
/// задачи 3 и 11 с <c>Modifier</c> и <c>DynUpgrade</c>.
/// <para>Ссылочные поля приведены к пустой строке, когда в файле пусто или стоит
/// <c>g000000000</c>: из 336 живых предметов <c>MOD_EQUIP</c> несёт 41, <c>MOD_POTION</c> — 20,
/// <c>SPELL_ID</c> — 141, <c>ATTACK_ID</c> — 90, <c>UNIT_ID</c> — 108. Казна пустой строкой даёт
/// <see cref="Bank.Zero"/>: у <c>CASTING</c> это 315 предметов из 336 — «применение ничего не
/// стоит».</para>
/// <para>Ключи регистронезависимы: в этой таблице id как раз в верхнем регистре
/// (<c>G000IG0001</c>), в отличие от остальных.</para></summary>
public static class LegacyItemLoader
{
    public static IReadOnlyDictionary<string, Item> Load(string globalsDir)
    {
        var categories = LegacyEquipLoader.LoadItemCategories(globalsDir);
        foreach (var (raw, name) in categories)
            if (!Enum.IsDefined((ItemCategory)raw))
                throw new InvalidDataException(
                    $"LmagItm.dbf: категория {raw} \"{name}\" есть в таблице игры, но не в ItemCategory");

        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "GItem.dbf").Rows)
        {
            var id = r.GetString("ITEM_ID");
            var item = new Item(id, Category(id, r.GetInt("ITEM_CAT"), categories),
                Bank.Parse(r.GetString("VALUE")),
                LegacyRulesLoader.Ref(r.GetString("MOD_EQUIP")), LegacyRulesLoader.Ref(r.GetString("MOD_POTION")),
                r.GetInt("HP_POTION"), LegacyRulesLoader.Ref(r.GetString("SPELL_ID")),
                Bank.Parse(r.GetString("CASTING")), LegacyRulesLoader.Ref(r.GetString("ATTACK_ID")),
                LegacyRulesLoader.Ref(r.GetString("UNIT_ID")));
            if (!items.TryAdd(id, item))
                throw new InvalidDataException($"GItem.DBF: ITEM_ID \"{id}\" встречается дважды");
        }
        return items;
    }

    /// <summary>Неизвестная категория — падение с именем предмета, а не молчаливое приведение:
    /// перечень закрыт <b>самой таблицей игры</b> <c>LmagItm.dbf</c>
    /// (<see cref="LegacyEquipLoader.LoadItemCategories"/>, 15 живых строк), а не перечислением в коде —
    /// до задачи 9 здесь стоял <c>Enum.IsDefined</c>, и шестнадцатая строка таблицы прошла бы молча.
    /// Теперь расхождение ловится с двух сторон: категория предмета обязана быть в таблице, а каждая
    /// строка таблицы — в <see cref="ItemCategory"/>.</summary>
    private static ItemCategory Category(string id, int raw, IReadOnlyDictionary<int, string> categories) =>
        categories.ContainsKey(raw)
            ? (ItemCategory)raw
            : throw new InvalidDataException($"GItem.DBF: предмет \"{id}\" — ITEM_CAT {raw} вне LmagItm.dbf");
}
