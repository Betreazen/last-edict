using System.Globalization;

namespace LastEdict.Rules.Map;

/// <summary>
/// Порядок изученных заклинаний игрока у движка. <c>CPlayerKnownSpells</c> — список (<c>+0x8</c>, голова <c>+0xc</c>); поток объекта
/// (слот 1 vft <c>0x6ef9f4</c>, <c>0x5fe90a</c>: чтение или запись <c>0x5fea50</c>, затем <c>0x5fe926</c>) сортирует его
/// <c>list::sort</c> <c>0x44b3df</c> со сравнением <c>0x468c76</c> — беззнаковое «меньше» 32-битных <c>CMidgardID</c>. После загрузки
/// сценария или сохранения список — по возрастанию id, что бы ни лежало в файле (карта 19: в сценарии Нежити <c>SS0067, SS0063, SS0061…</c>,
/// в сохранении движка t01 — <c>SS0061…SS0067</c>). От порядка зависит первая встреченная категория заклинания перед атакой
/// (<c>0x47263e</c>, <c>AiSpells.BeforeAttack</c>).
/// </summary>
public static class KnownSpellOrder
{
    /// <summary>Id по возрастанию числа <c>CMidgardID</c>: категория (<c>G</c> 0, <c>C</c> 1, <c>S</c> 2, <c>E</c> 3; биты 30–31), номер
    /// категории (биты 22–29), тип (биты 16–21; у заклинаний один — <c>SS</c>), номер (биты 0–15). Нечитаемый id — после читаемых,
    /// по тексту.</summary>
    public static IReadOnlyList<string> Sort(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return [.. ids.OrderBy(Key).ThenBy(id => id, StringComparer.OrdinalIgnoreCase)];
    }

    private static (int Bad, int Category, int Index, int Type, int Number) Key(string? id)
    {
        if (id is not { Length: 10 }) return (1, 0, 0, 0, 0);
        int category = "GCSE".IndexOf(char.ToUpperInvariant(id[0]), StringComparison.Ordinal);
        if (category < 0
            || !int.TryParse(id.AsSpan(1, 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index)
            || !int.TryParse(id.AsSpan(6, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int number))
            return (1, 0, 0, 0, 0);
        return (0, category, index, char.ToUpperInvariant(id[4]) * 256 + char.ToUpperInvariant(id[5]), number);
    }
}
