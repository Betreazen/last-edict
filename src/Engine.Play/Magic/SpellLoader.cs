using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Magic;

/// <summary>Заклинания содержимого (<see cref="SpellDef"/>) → <see cref="Spell"/> правил. Запись живёт в <c>LastEdict.Rules.Magic</c>: ссылка
/// идёт <c>LastEdict.Data → LastEdict.Rules</c>, кольца нет.
/// <para>Ссылки «не задано» — пустая строка: у 74 из 120 заклинаний оригинала нет модификатора, и это не висячая ссылка, а её
/// отсутствие. <see cref="Spell.RestoreMovePercent"/> — <b>проценты очков хода</b> (ранг 1 по текстам самой игры). Обереги — до
/// четырёх модификаторов (в оригинале пусты у всех 120).</para>
/// <para>Не берутся в правила: тексты (для игрока), подсказка выбора ИИ (<see cref="SpellDef.AiUse"/> — её читает
/// <c>GameBook.SpellAiCategory</c>), число оберегов (длину задаёт сам список) и <see cref="SpellDef.ChangesTerrain"/> (ложь у всех 120
/// строк оригинала; смену земли несут категория и <see cref="SpellDef.Terrain"/>) — утверждения о двух последних проверяются в
/// <c>SpellTests</c>.</para></summary>
public static class SpellLoader
{
    public static IReadOnlyDictionary<string, Spell> Load(string globalsDir) => Load(ContentSource.Open(globalsDir));

    public static IReadOnlyDictionary<string, Spell> Load(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var spells = new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in content.Spells)
            if (!spells.TryAdd(s.Key, new Spell(s.Key, s.Category, s.Level, Prices.ToBank(s.CastCost), Prices.ToBank(s.BuyCost), s.Area,
                    content.KeyOf(s.Modifier), content.KeyOf(s.Unit), s.RestoreMovePercent ?? 0, (int?)s.DamageSource ?? 0, s.Damage ?? 0,
                    s.Heal ?? 0, (int?)s.Terrain ?? 0, RulesLoader.Wards(content, s.Wards))))
                throw new InvalidDataException($"заклинание «{s.Key}» объявлено дважды");
        return spells;
    }
}
