// Загрузчики правил ДО схемы содержимого (main 639961ee): правила читались прямо из строк таблиц оригинала. Эталон сверки
// «старая модель = новая» (ContentRulesParityTests) — не править; удаляется вместе с ней, когда таблицы уйдут из проверок.
using D2.Data.Battle;
using D2.Data.Dbf;
using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Data.Tests.LegacyLoaders;

/// <summary><c>Gspells.dbf</c> -> <see cref="Spell"/>. Запись живёт в <c>D2.Rules.Magic</c>, а не
/// здесь: ссылка идёт <c>D2.Data → D2.Rules</c>, кольца не будет.
///
/// <para><b>Долг задачи 9, закрытый задачей 10.</b> Прежде загрузчик отдавал собственную запись
/// <c>SpellType</c>, а перевод в правило жил в тестовом хелпере <c>SpellTests.ToRule</c> — задаче 9
/// правка <c>D2.Data</c> была запрещена. Заодно ушло имя, расходившееся с данными: колонку
/// <c>RESTORE_M</c> задача 1 прочла как <c>RestoreMana</c>, а это <b>проценты очков хода</b>
/// (ранг 1 по текстам самой игры — см. <see cref="Spell.RestoreMovePercent"/>).</para>
///
/// <para>Ссылочные поля приведены к пустой строке, когда в файле стоит <c>g000000000</c>
/// («не задано»): у 74 из 120 заклинаний нет <c>MODIF_ID</c>, и это не висячая ссылка, а её
/// отсутствие. Цена — строковая казна <c>g0000:r0000:y0100:e0000:w0000[:b0000]</c>, разбор —
/// <see cref="Bank.Parse"/>. <c>Wards</c> — до четырёх модификаторов-вардов (<c>WARD1..WARD4</c>,
/// в ванили пусты у всех 120), пустые отброшены.</para>
///
/// <para>Не берутся: три текстовые колонки (для игрока), <c>AI_CAT</c> (подсказка выбора для ИИ,
/// то есть Ф7), <c>QTY_WARDS</c> (длину списка задаёт сам список) и <c>CHNGTERCAT</c> (логическое
/// поле, ложное у всех 120 строк ванили; смену земли несут категория и <c>GROUND_CAT</c> —
/// см. <see cref="SpellCategory"/>). Утверждения о двух последних колонках проверяются в
/// <c>SpellTests</c> прямо по <c>.dbf</c>, чтобы «не взято» не превратилось в «не проверено».</para></summary>
public static class LegacySpellLoader
{
    public static IReadOnlyDictionary<string, Spell> Load(string globalsDir)
    {
        var spells = new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gspells.dbf").Rows)
        {
            var id = r.GetString("SPELL_ID");
            var spell = new Spell(id, Category(id, r.GetInt("CATEGORY")), r.GetInt("LEVEL"),
                Bank.Parse(r.GetString("CASTING_C")), Bank.Parse(r.GetString("BUY_C")),
                r.GetInt("AREA"), LegacyRulesLoader.Ref(r.GetString("MODIF_ID")),
                LegacyRulesLoader.Ref(r.GetString("UNIT_ID")), r.GetInt("RESTORE_M"),
                r.GetInt("DAMAGE_SRC"), r.GetInt("DAMAGE_QTY"), r.GetInt("HEAL_QTY"),
                r.GetInt("GROUND_CAT"), LegacyRulesLoader.Wards(r));
            if (!spells.TryAdd(id, spell))
                throw new InvalidDataException($"Gspells.dbf: SPELL_ID \"{id}\" встречается дважды");
        }
        return spells;
    }

    /// <summary>Неизвестная категория — падение с именем заклинания: перечень закрыт таблицей игры
    /// <c>Lspell.dbf</c> (двенадцать значений, пятого нет).</summary>
    private static SpellCategory Category(string id, int raw) =>
        Enum.IsDefined((SpellCategory)raw)
            ? (SpellCategory)raw
            : throw new InvalidDataException($"Gspells.dbf: заклинание \"{id}\" — CATEGORY {raw} вне Lspell.dbf");
}
