namespace LastEdict.Rules.Map;

/// <summary>
/// Числа рас движка по роду расы — колонке <c>Grace.RACE_TYPE</c> (0 Империя, 1 Орды, 2 Легионы, 3 Кланы, 4 нейтралы, 5 Эльфы, ранг 1).
/// Правила читают признаки расы из содержимого (<see cref="RaceInfo.Terrain"/>, <see cref="RaceInfo.TurnOrder"/>,
/// <see cref="RaceInfo.EventFlag"/>, <see cref="RaceInfo.Neutral"/>): эти таблицы — только для импорта содержимого из таблиц оригинала
/// и для расы без содержимого (проверки, собранные в коде). Здесь — единственное место, где род расы оригинала — число.
/// </summary>
public static class LegacyRaces
{
    public const int Human = 0, Undead = 1, Heretic = 2, Dwarf = 3, Neutral = 4, Elf = 5;

    /// <summary>Id расы нейтралов мода <c>base</c> — для правил без содержимого (проверки, собранные в коде); с содержимым нейтралы —
    /// расы с признаком <see cref="RaceInfo.Neutral"/> (<see cref="EconomyRules.NeutralRaces"/>).</summary>
    public const string NeutralId = "base:race/neutral";

    /// <summary>
    /// Земля какой расы своя для рода <paramref name="raceType"/> (<c>Grace.RACE_TYPE</c> → <c>Lterrain.ID</c>). Ранг 1:
    /// <c>GetTerrainByRace</c> (Gog <c>0x5e515e</c>) возвращает категории <c>0x837978 human</c>, <c>0x837960 heretic</c>,
    /// <c>0x837950 undead</c>, <c>0x837940 dwarf</c>, <c>0x837920 elf</c>, иначе <c>0x837930 neutral</c> (адреса —
    /// <c>tools/d2probe/mss32/src/terraincat.cpp</c>, блок Gog); номера — <c>Lterrain.dbf</c>: <c>L_HU 1, L_DW 2, L_HE 3, L_UN 4, L_NE 5,
    /// L_EL 6</c>.
    /// </summary>
    public static int Terrain(int raceType) => raceType switch
    {
        Human => 1,     // L_HU
        Undead => 4,    // L_UN
        Heretic => 3,   // L_HE
        Dwarf => 2,     // L_DW
        Elf => 6,       // L_EL
        _ => 5,         // нейтралы и всё неизвестное -> L_NE
    };

    /// <summary>Ключ очереди хода <c>0x42e1fe</c>: Империя, Легионы, Кланы, Орды, Эльфы, нейтралы; неизвестный род — в конце.</summary>
    public static int TurnOrder(int raceType) => raceType switch
    {
        Human => 0, Heretic => 1, Dwarf => 2, Undead => 3, Elf => 4, Neutral => 5, _ => int.MaxValue,
    };

    /// <summary>
    /// Флаг расы события: номер поля записи <c>HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF</c> (поток <c>0x5f0c93</c>); неизвестный род — −1
    /// (флага нет).
    /// </summary>
    public static int EventFlag(int raceType) => raceType switch
    {
        Human => 0, Dwarf => 1, Undead => 2, Heretic => 3, Neutral => 4, Elf => 5, _ => -1,
    };

    /// <summary>Код рода в именах графики и звука (<c>LORDHU…</c>, <c>UPGRADEHU</c>); неизвестный — нейтральный <c>NE</c>.</summary>
    public static string Code(int raceType) => raceType switch
    {
        Human => "HU", Undead => "UN", Heretic => "HE", Dwarf => "DW", Elf => "EL", _ => "NE",
    };
}
