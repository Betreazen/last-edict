using LastEdict.Rules.Map;

namespace LastEdict.Rules.Units;

/// <summary>
/// Вход поиска цели повышения не лидера (<c>0x5d7fe7</c>, псевдокод REA/Ghidra 17.10): цель вообще ищется, только если юнит «у своей
/// расы». Игрок расы — хозяин подрасы <c>Gunits.SUBRACE</c> типа (<c>0x5d8883</c> → <c>0x5e508f</c>: <c>CMidSubRace</c> этой подрасы,
/// <c>+4</c> — игрок); «раса на карте» — у этого игрока есть столица (<c>0x5d88be</c>: <c>CFortification</c> категории столицы с
/// <c>OWNER</c> = игрок расы). Цель ищется, если расы на карте нет, или хозяин группы юнита (<c>0x5eaad1</c>) и есть игрок расы, или
/// хозяина нет; иначе — сразу «цели нет» (ответный байт 1). Дальше — наследники по <c>PREV_ID</c> с постройками, а без наследников
/// (и при <c>DYNLEVEL</c>) — порождённая реализация уровнем выше (рост на месте, <c>0x5d8514</c>).
/// <para>Ранг 1 по листингу и корпусу (строки <c>upgrade_target</c>, поля <c>race_player</c>/<c>race_ok</c>):
/// <c>20261016e-av</c>, карта 5 — нейтральные G000UU5017 (подраса 8, игрок PL0000 без столицы) в гарнизонах деревень 48 + 4 → 50 =
/// XP_NEXT, цель G000UG71a9 (первое её появление в журнале), в сохранении следующего дня уровень 2 опыт 0; карта 8 — G000UU0036 у нейтрала
/// PL0000, игрок расы PL0001 со столицей: цели нет (байт 1), опыт сел на XP_NEXT; <c>20260918-probe65</c> ai-q09 — G000UU0018 (подраса 1,
/// PL0001 со столицей) у нейтрала PL0000 шесть дней на 475 без повышения.</para>
/// </summary>
public static class UpgradeRace
{
    private const string EmptyId = "G000000000";

    /// <summary>Игрок расы: хозяин первой подрасы карты с номером <paramref name="subrace"/>; null — такой подрасы на карте нет.</summary>
    /// <param name="subraces"><c>SUBRACE_ID</c> → <c>SUBRACE</c> (<see cref="MapState.Subraces"/>).</param>
    /// <param name="subraceOwners"><c>SUBRACE_ID</c> → <c>PLAYER_ID</c>.</param>
    public static string? RacePlayer(IReadOnlyDictionary<string, int> subraces, IReadOnlyDictionary<string, string> subraceOwners,
                                     int subrace)
    {
        ArgumentNullException.ThrowIfNull(subraces);
        ArgumentNullException.ThrowIfNull(subraceOwners);
        foreach (var (id, value) in subraces)
            if (value == subrace) return subraceOwners.GetValueOrDefault(id);
        return null;
    }

    /// <summary>Ищет ли движок цель повышения юнита подрасы <paramref name="subrace"/> в группе хозяина <paramref name="owner"/>.</summary>
    public static bool Allows(MapState map, IReadOnlyDictionary<string, string> subraceOwners, string? owner, int subrace)
    {
        ArgumentNullException.ThrowIfNull(map);
        string? race = RacePlayer(map.Subraces, subraceOwners, subrace);
        bool onMap = race is { Length: > 0 }
                     && map.Sites.Any(s => s.Kind == SiteKind.Capital && string.Equals(s.Owner, race, StringComparison.OrdinalIgnoreCase));
        return !onMap || string.IsNullOrEmpty(owner) || string.Equals(owner, EmptyId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(owner, race, StringComparison.OrdinalIgnoreCase);
    }
}
