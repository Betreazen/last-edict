namespace D2.Rules.Units;

/// <summary>Строка <c>GleaUpg.dbf</c>: начиная с уровня <see cref="MinLevel"/> лорду
/// <see cref="LordId"/> может быть выдан модификатор <see cref="ModifId"/>.
/// <para><b><see cref="LordId"/> — это лорд из <c>Glord.dbf</c>, а не юнит</b> (колонка
/// <c>BELONGS_TO</c>, 391 из 391 разрешается в <c>Glord</c>, 0 — в <c>Gunits</c>; измерено задачей 1,
/// тестом закреплено задачей 3). Лорд — класс игрока, а не боец в отряде.</para>
/// <para>Дом записи — <c>D2.Rules</c>: способность лидера есть <b>модификатор</b>, и складывает её
/// та же <see cref="EffectiveStats"/>, что и заклинание, предмет или вард. Отдельного механизма у
/// роста лидера нет. Колонки <c>NAME_TXT</c>/<c>DESC_TXT</c> сюда не поехали — это подписи для
/// интерфейса, правилам они не нужны (тот же выбор, что задача 3 сделала для <c>COMMENTS</c>).</para>
/// </summary>
public sealed record LeaderUpgrade(string LordId, string ModifId, int MinLevel, int Priority);

/// <summary>Способности лидера по уровню — <c>GleaUpg.dbf</c> (391 живая строка, 15 лордов из 18).
///
/// <para><b>Список накопительный — ранг 1 по сохранениям (задача 11 Ф7, план §1.5).</b>
/// Предлагаются <b>все</b> строки лорда с <c>MIN_LEVEL ≤ уровень</c>, <b>ещё не взятые</b>, а не
/// только строки нового уровня. Доказательство — поздние выборы навыков второго уровня в больших
/// партиях корпуса: «Знаменосец» взят на 10-м уровне, «Знание артефактов» на 5-м, «Магия
/// талисманов» на 12-м (29 таких случаев у шести героев, П§20). При правиле «только новый уровень»
/// это невозможно. Сверяет счётчик <c>skill_available</c>.</para>
///
/// <para><b>Один навык за уровень.</b> У героя уровня N ровно N − 1 выбранных строк
/// <c>GleaUpg</c> в цепочке <c>MidUnit.MODIF_ID</c>, минус отложенные (<c>MidStack.UPGCOUNT</c>) —
/// счётчик <c>skill_count</c>. Само повышение уровня (GOG <c>0x5d89c6</c>, вызывается из
/// <c>changeUnitXpCheckUpgrade</c> <c>0x5d7412</c>) <b>ни одного модификатора не накладывает</b>:
/// оно только перевыпускает реализацию юнита на уровень выше
/// (<c>0x5d8514</c> → <c>CUnitGenerator::generateUnitImplId</c>/<c>generateUnitImpl</c>) и упирается
/// в <c>CScenarioInfo.leaderMaxLevel</c> — <b>ранг 1</b>, дизассемблер GOG. Модификатор приходит
/// отдельным выбором (<see cref="SkillChoice"/>), и до выбора очередь копится в <c>UPGCOUNT</c>.
/// Повышение — в бою, в день боя; окно навыка — после <c>income</c> владельца на следующем ходу
/// (ранг 1, отчёт A §1, ai-q32: 3/0 и 3/0).</para>
///
/// <para><b><c>PRIORITY</c> — вес выбора ИИ.</b> Колонка есть и читается движком в
/// <c>LeaderUpgData.priority</c> (смещение 8; чтение строки — GOG <c>0x5924e4</c>, поле кладётся в
/// собираемую структуру, которая уходит в список <c>CLordLeaderUpg</c> вызовом <c>0x59279d</c>) —
/// это <b>ранг 1</b>. Потребитель — отбор ИИ <c>0x44F1FA</c>–<c>0x44F2E1</c>: наибольший <c>PRIORITY</c>,
/// при равенстве первый по таблице лорда (<see cref="SkillChoice.ByAi"/>; ai-q02 38/0, отчёт A §2).
/// Старое «самый тяжёлый (90) взят 1 раз из 7» (П§20) этим и объясняется: у бойцов (врождённая 8)
/// <c>g000um4001</c> отфильтрован, и argmax падал на вес 85. Поэтому <see cref="Available"/> приоритетом
/// <b>не сортирует</b> — порядок строк лорда и есть правило равенства.</para>
///
/// <para><b>Фильтр врождённых — ранг 1 для ИИ</b> (<c>canApply</c> <c>0x44f35d</c>; ai-q02: бойцы
/// <c>G000UU0070/0096/8009</c> пропускают <c>g000um4001</c>, маги <c>0072/0098/8010</c> — <c>g000um4005</c>).
/// Для человека — <b>ранг 3</b>: единственное наблюдённое предложение (ai-q32, Архимаг <c>g000uu0021</c>,
/// врождённые 7 и 14, <c>skill_offer</c> [4004, 4003, 4002, 4001]) фильтр не различает — ни один навык
/// 2-го уровня не даёт 7 или 14. Фильтр оставлен в общем списке: функция отбора одна.</para>
/// </summary>
public static class LeaderUpgrades
{
    /// <summary>Строки лорда, доступные на уровне <paramref name="level"/> и ещё не взятые, —
    /// в порядке таблицы (не приоритета: см. про <c>PRIORITY</c> выше).
    /// <para><paramref name="taken"/> — уже выбранные <c>MODIF_ID</c> (часть цепочки
    /// <c>MidUnit.MODIF_ID</c>, попавшая в <c>GleaUpg</c>); <c>null</c> — ничего не взято.
    /// Повторяющийся навык различается по <c>MODIF_ID</c>, а не по подписи: три строки «Leadership»
    /// лорда <c>g000LR0001</c> — это <c>g000um4006</c> (уровень 3), <c>g000um4101</c> (8) и
    /// <c>g000um4102</c> (10) с одним <c>NAME_TXT x000tga006</c>, так что «ещё не взят» их не
    /// съедает: +1 лидерства можно взять трижды.</para>
    /// <para>Набор взятого сравнивается без учёта регистра здесь, а не доверяется сравнителю
    /// переданного множества: в сохранении <c>MODIF_ID</c> лежит в верхнем регистре, а в
    /// <c>GleaUpg.dbf</c> — в нижнем, и набор с сравнителем по умолчанию тихо не вычёл бы
    /// ни одного навыка.</para></summary>
    /// <param name="innate">Врождённые способности типа героя (<see cref="LeaderAbilities.Innate"/>):
    /// навык, чей <c>GmodifL TYPE 11 ABILITY</c> среди них, отсекается. <c>null</c> — без фильтра.</param>
    /// <param name="modifiers">Справочник <c>Gmodif</c> для фильтра; навыка в нём нет — исключение.</param>
    public static IReadOnlyList<LeaderUpgrade> Available(IEnumerable<LeaderUpgrade> all,
                                                         string lordId, int level,
                                                         IEnumerable<string>? taken = null,
                                                         IReadOnlySet<int>? innate = null,
                                                         IReadOnlyDictionary<string, Modifier>? modifiers = null)
    {
        var already = taken?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(u => u.MinLevel <= level
                              && string.Equals(u.LordId, lordId, StringComparison.OrdinalIgnoreCase)
                              && (already is null || !already.Contains(u.ModifId))
                              && !GrantsInnate(u, innate, modifiers))
                  .ToList();
    }

    private static bool GrantsInnate(LeaderUpgrade u, IReadOnlySet<int>? innate,
                                     IReadOnlyDictionary<string, Modifier>? modifiers)
    {
        if (innate is null || innate.Count == 0 || modifiers is null) return false;
        if (!modifiers.TryGetValue(u.ModifId, out var m))
            throw new InvalidDataException($"GleaUpg: MODIF_ID \"{u.ModifId}\" в Gmodif не найден");
        return LeaderAbilities.FromChain([m]).Overlaps(innate);
    }
}
