namespace D2.Rules.Units;

/// <summary>Почему выбор навыка не состоялся. <see cref="None"/> — состоялся.</summary>
public enum SkillRefusal
{
    None = 0,

    /// <summary>Нечего выбирать: отложенных повышений нет (<c>MidStack.UPGCOUNT</c> = 0).</summary>
    NothingPending,

    /// <summary>Навыка нет в накопительном списке лорда на этом уровне — либо чужой лорд, либо
    /// <c>MIN_LEVEL</c> выше уровня, либо он уже взят.</summary>
    NotAvailable,

    /// <summary>Навыков у лидера уже <c>GVars.LDRMAXABIL</c>.</summary>
    LimitReached,
}

/// <summary>Итог выбора: что выбрано, что осталось в очереди и каким стал набор взятого.
/// <see cref="Refusal"/> не <see cref="SkillRefusal.None"/> — не изменилось ничего.</summary>
public sealed record SkillChosen(SkillRefusal Refusal, LeaderUpgrade? Upgrade, int Pending,
                                 IReadOnlyList<string> Taken);

/// <summary>
/// Выбор уровневого навыка лидера — задача 11 Ф7, план §1.5.
///
/// <para><b>Что выбирают.</b> Одну строку <see cref="LeaderUpgrade"/> из накопительного списка
/// <see cref="LeaderUpgrades.Available"/>; её <c>MODIF_ID</c> ложится в цепочку
/// <c>MidUnit.MODIF_ID</c>, и дальше её эффект считает та же
/// <see cref="EffectiveStats"/>, что и эффект предмета или варда. Отдельного механизма у навыка
/// нет — это модификатор (задача 9, <see cref="LeaderAbilities"/>).</para>
///
/// <para><b>Очередь.</b> Повышение уровня само ничего не выдаёт и окна не открывает: оно
/// прибавляет отложенный выбор (<c>MidStack.UPGCOUNT</c>), а окно навыка игрок видит на следующем
/// ходу (план §1.5). Поэтому <see cref="ByPlayer"/> — операция над очередью, а не над уровнем:
/// один выбор снимает с очереди единицу. <b>Ранг 1</b> (отчёт A §1, ai-q32 и ai-t08c): уровень, HP и
/// <c>UPGCOUNT</c> растут в бою, в день боя (<c>level_up_in_battle</c> 3/0); окно выбора открывается после
/// <c>income</c> владельца на следующем ходу (<c>skill_window_next_owner_turn</c> 3/0). Человек снимает за ход
/// всю очередь, ИИ — один выбор на героя за ход (<c>ai_one_pick_per_turn</c> 30/0).
/// Правило «ИИ — один за ход» держит память дня стратегии: оценка списка 1 — по отряду
/// (<c>D2.Data.Play.AiEconomy</c>, <c>docs/ai-economy.md</c> §6).</para>
///
/// <para><b>Кто выбирает.</b> Человек — <see cref="ByPlayer"/> (проверяет, что названная строка в
/// списке). Герой ИИ — <see cref="ByAi"/>: <b>наибольший <c>PRIORITY</c></b>.</para>
/// </summary>
public static class SkillChoice
{
    /// <summary>Сколько навыков лидер может носить — <c>GVars.LDRMAXABIL</c> = 15.
    /// <b>Ранг 1 на число</b> (поле таблицы, <c>MapVariables.LeaderMaxAbilities</c>);
    /// <b>ранг 3 на смысл</b> — потребителя в экзешнике не искали, а в корпусе в него никто не
    /// упёрся: самый прокачанный герой больших партий — <c>S073UN01fc</c> 12-го уровня с 11
    /// навыками (харнесс задачи 11).
    /// Совпадение с потолком уровня сценария случайным не выглядит (у лорда-Воина 27 строк
    /// <c>GleaUpg</c>, то есть список кончается позже потолка), но различить «потолок навыков» от
    /// «потолка уровня лидера» корпус не даёт.
    /// <para>ponytail: величина берётся параметром <c>limit</c>, а не читается здесь из таблицы —
    /// <c>D2.Rules</c> файлов не читает; подаёт её <see cref="D2.Rules.Map.MapVariables"/>.</para>
    /// </summary>
    public const int DefaultLimit = 15;

    /// <summary>Выбор человека: снять с очереди один отложенный выбор и взять названную строку.
    /// <para>Порядок отказов — <b>ранг 3</b>: наблюдений отказа в корпусе нет вовсе (окно навыка
    /// событий зонда не пишет, план §3 п. 32), и следствий у порядка тоже нет — любой отказ ничего
    /// не меняет. Сначала «нечего выбирать», потом потолок, потом «нет в списке».</para></summary>
    /// <param name="pending"><c>MidStack.UPGCOUNT</c> — отложенные повышения.</param>
    /// <param name="taken">Уже взятые <c>MODIF_ID</c> — они же исключаются из списка.</param>
    /// <param name="limit"><c>GVars.LDRMAXABIL</c>, см. <see cref="DefaultLimit"/>.</param>
    /// <param name="innate">Врождённые способности типа героя и <paramref name="modifiers"/> — фильтр
    /// <see cref="LeaderUpgrades.Available"/>; у человека он <b>ранг 3</b> (см. там).</param>
    public static SkillChosen ByPlayer(IEnumerable<LeaderUpgrade> all, string lordId, int level,
                                       int pending, IReadOnlyList<string> taken, string modifId,
                                       int limit = DefaultLimit, IReadOnlySet<int>? innate = null,
                                       IReadOnlyDictionary<string, Modifier>? modifiers = null)
    {
        if (pending <= 0) return Refused(SkillRefusal.NothingPending, pending, taken);
        if (taken.Count >= limit) return Refused(SkillRefusal.LimitReached, pending, taken);
        var pick = LeaderUpgrades.Available(all, lordId, level, taken, innate, modifiers)
            .FirstOrDefault(u => string.Equals(u.ModifId, modifId, StringComparison.OrdinalIgnoreCase));
        if (pick is null) return Refused(SkillRefusal.NotAvailable, pending, taken);
        return new SkillChosen(SkillRefusal.None, pick, pending - 1, [.. taken, pick.ModifId]);
    }

    /// <summary>
    /// Выбор героя ИИ — <b>наибольший <c>PRIORITY</c></b>, при равенстве — первый в порядке строк лорда
    /// (порядок <paramref name="available"/> — табличный, <see cref="LeaderUpgrades.Available"/>), без броска.
    ///
    /// <para><b>Ранг 1.</b> Код отбора — <c>0x44F1FA</c>–<c>0x44F2E1</c> (<c>spec-leaders.md</c>): строгое
    /// <c>&gt;</c> по <c>PRIORITY</c>. Журналы ai-q02 (отчёт A §2): 38 выборов ИИ из 38 совпали с argmax
    /// среди навыков, не дающих врождённую способность героя (<c>ai_skill_argmax_filtered</c> 38/0; без
    /// фильтра — 22/16); взвешенный случайный выбор (решение пользователя 11.09) дал бы все 38 с
    /// вероятностью 2,3·10⁻³¹. Решение пользователя 17.09.2026 — выбирать как в игре. Фильтр
    /// врождённых — в <see cref="LeaderUpgrades.Available"/>, его и нужно подать сюда.</para>
    /// </summary>
    /// <returns>Пустой список — <c>null</c>: выбирать не из чего.</returns>
    public static LeaderUpgrade? ByAi(IReadOnlyList<LeaderUpgrade> available) =>
        available.MaxBy(u => u.Priority);   // MaxBy оставляет первый из равных — то же строгое «>»

    private static SkillChosen Refused(SkillRefusal why, int pending, IReadOnlyList<string> taken) =>
        new(why, null, pending, taken);
}
