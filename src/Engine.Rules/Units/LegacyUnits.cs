namespace LastEdict.Rules.Units;

/// <summary>
/// Признаки юнитов, которые движок оригинала держит не в <c>Gunits</c>, а списками id в коде. В Last Edict это поля юнита в содержимом
/// (<c>mageLeader</c>, <c>leaderDamageBonus</c>, <c>copyable</c>); импорт выписывает их явно по этим спискам, а юнит без поля (содержимое
/// старого импорта) получает признак по своему id оригинала. Здесь — единственное место, где эти id записаны.
/// </summary>
public static class LegacyUnits
{
    /// <summary>«Маги-лидеры» — штраф точности площадной атаки по месту цели (<see cref="Battle.Formulas.MageLeaderPowerReduction"/>):
    /// список зашит в код (GOG <c>0x627db1</c>, прочитан до <c>ret 8</c>) — строки <c>0x6f1f30</c> G000UU8010, <c>0x6d95e8</c> G000UU0021,
    /// <c>0x6f1f24</c> G000UU0046, <c>0x6f1f18</c> G000UU0098, <c>0x6f1f0c</c> G000UU0097, <c>0x6f1f00</c> G000UU0072. Ни UNIT_CAT, ни
    /// LEADER_CAT не читаются: нейтральный лидер G000UU5262 (UNIT_CAT 2, площадной огонь) штрафа не получает — корпус это подтверждает.</summary>
    private static readonly HashSet<string> MageLeaders = new(StringComparer.OrdinalIgnoreCase)
        { "g000uu8010", "g000uu0021", "g000uu0046", "g000uu0098", "g000uu0097", "g000uu0072" };

    /// <summary>Десять лидеров с прибавкой к потолку урона (<see cref="Battle.Formulas.DamageMax"/>): список <c>typeIndex</c> зашит в
    /// <c>getUnitImplDamageMax</c> (GOG <c>0x61dc4a</c>–<c>0x61dc7b</c>), это не свойство <c>Gunits.dbf</c> (<c>docs/re-damage-max.md</c>
    /// эталона). Pegasus Knight, Ranger, King's Guard, Engineer, Dwarf Champion, Duke, Counselor, Death Knight, Forest Liege, Guardian.
    /// Движок сравнивает только тип <c>UU</c> и <c>typeIndex</c>: категория, LEADER_CAT, BASE_UNIT, уровень не читаются.</summary>
    private static readonly HashSet<int> DamageBonusTypeIndices = [0x0019, 0x0020, 0x0044, 0x0045, 0x0047, 0x0070, 0x0071, 0x0096, 0x8009, 0x8011];

    /// <summary>Не копируются двойником никогда — прошитый список <c>0x661bdc</c> (строки <c>0x6f34f4</c>–<c>0x6f3524</c>).</summary>
    private static readonly HashSet<string> NotCopyable = new(StringComparer.OrdinalIgnoreCase)
        { "g000uu8003", "g000uu8006", "g000uu8103", "g000uu8106", "g000uu8150" };

    public static bool IsMageLeader(string? legacyId) => legacyId is not null && MageLeaders.Contains(legacyId);

    public static bool HasDamageBonus(string? legacyId) =>
        legacyId is { Length: 10 } id && id.AsSpan(4, 2).Equals("uu", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(id.AsSpan(6, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int index)
        && DamageBonusTypeIndices.Contains(index);

    public static bool IsCopyable(string? legacyId) => legacyId is null || !NotCopyable.Contains(legacyId);
}
