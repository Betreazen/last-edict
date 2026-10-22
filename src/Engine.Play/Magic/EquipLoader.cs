using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Dbf;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Magic;

/// <summary>Три справочника гейта экипировки: категории предметов (<c>LmagItm.dbf</c>), имена
/// способностей лидера (<c>LleadA.dbf</c>) и врождённые способности по типу юнита
/// (<c>GLabi.dbf</c>).
///
/// <para><b>Зачем вместе.</b> Гейт (<see cref="Equipment"/>) читает все три: категорию предмета —
/// из <c>GItem.ITEM_CAT</c> по перечню <c>LmagItm</c>, нужную способность — числом из <c>LleadA</c>,
/// а есть ли она у лидера — из <c>GLabi</c> плюс цепочки модификаторов
/// (<see cref="Rules.Units.LeaderAbilities"/>). До задачи 9 перечень категорий был зашит в код
/// (<c>Enum.IsDefined</c>), а <c>GLabi</c> не загружался вовсе — харнесс задачи 8 читал его сам.</para>
///
/// <para><b>Ранг 1 на все три таблицы.</b> Движок регистрирует их теми же именами и в том же
/// порядке, что лежат строки: <c>LMagItm.dbf</c> — <c>0x00585AA2</c> (разбор
/// <c>0x00585A9C</c>–<c>0x00585C0B</c> заполняет пятнадцать глобальных категорий по порядку строк),
/// <c>LLeadA.dbf</c> — <c>0x0058535E</c>. Гейт сравнивает не биты, а объекты этих таблиц
/// (<c>cmp eax, [0x837698]</c> и далее, <c>0x005DF6B7</c>).</para>
///
/// <para><b>Удалённые строки</b> (флаг <c>0x2A</c>) отбрасывает сам <see cref="DbfTable"/>:
/// в <c>LleadA.dbf</c> их восемь из двадцати (<c>L_STAND_GROUND</c>, <c>L_RICH</c>,
/// <c>L_FAMOUS</c>, …), в <c>LmagItm.dbf</c> — одна (<c>L_COIN</c>), и живые id идут с пропусками:
/// у способностей 1, 3, 6…10, 13…17, у категорий 0…14 подряд.</para></summary>
public static class EquipLoader
{
    /// <summary>Категории предметов: <c>ID → TEXT</c> (<c>LmagItm.dbf</c>, 15 живых строк
    /// <c>0 L_ARMOR … 14 L_SPECIAL</c>). Перечень <see cref="ItemCategory"/> обязан совпасть с этой
    /// таблицей — проверяет <see cref="ItemLoader.Load"/>.</summary>
    public static IReadOnlyDictionary<int, string> LoadItemCategories(string globalsDir) =>
        Names(globalsDir, "LmagItm.dbf");

    /// <summary>Способности лидера: <c>ID → TEXT</c> (<c>LleadA.dbf</c>, 12 живых строк). Числа —
    /// те самые, что стоят в <c>GLabi.L_ABILITY</c> и <c>GmodifL.ABILITY</c> при <c>TYPE = 11</c>.</summary>
    public static IReadOnlyDictionary<int, string> LoadAbilityNames(string globalsDir) =>
        Names(globalsDir, "LleadA.dbf");

    /// <summary>Врождённые способности лидера по типу юнита (<see cref="UnitDef.LeaderAbilities"/>, в оригинале — <c>GLabi.dbf</c>, 26
    /// строк). У одного типа их бывает несколько — у магов <c>L_WAND_SCROLL_USE</c> 7 и <c>L_ORB_USE</c> 14. Строку <c>GLabi</c> на тип,
    /// которого нет среди юнитов (<c>g000uu0000</c>), содержимое не несёт: ни к одному юниту она не привязана.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> LoadInnateAbilities(string globalsDir) =>
        LoadInnateAbilities(ContentSource.Open(globalsDir));

    public static IReadOnlyDictionary<string, IReadOnlyList<int>> LoadInnateAbilities(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Units.Where(u => u.LeaderAbilities.Count > 0)
            .ToDictionary(u => u.Key, u => (IReadOnlyList<int>)[.. u.LeaderAbilities.Select(a => (int)a)], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Таблица-перечень <c>L…</c>: две колонки <c>ID</c> и <c>TEXT</c>. Повтор id — данные
    /// разошлись с движком, где эти строки становятся глобальными объектами по одной на id.</summary>
    private static IReadOnlyDictionary<int, string> Names(string globalsDir, string file)
    {
        var names = new Dictionary<int, string>();
        foreach (var r in GameTables.Read(globalsDir, file).Rows)
            if (!names.TryAdd(r.GetInt("ID"), r.GetString("TEXT")))
                throw new InvalidDataException($"{file}: ID {r.GetInt("ID")} встречается дважды");
        return names;
    }
}
