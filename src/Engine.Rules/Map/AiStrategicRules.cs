using LastEdict.Rules.Magic;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Map;

/// <summary>Юнит места лечения: <paramref name="CanRevive"/> — <c>0x5de06e</c> == 0, <paramref name="CanHeal"/> —
/// <c>0x5de32f</c> == 0; цены — <c>vft +0x58</c> (воскрешение) и <c>+0x5c</c> (лечение).</summary>
public sealed record AiCareUnit(bool CanRevive, bool CanHeal, Bank ReviveCost, Bank HealCost);

/// <summary>Юнит списка найма (<c>0x5d4911</c>), уже отфильтрованный по позиции 0..5 (<c>0x5e78f7</c>).
/// <paramref name="SmallMelee"/> — ближний бой, урон, малый; <paramref name="Big"/> — большой.</summary>
public sealed record AiHireOption(string UnitId, bool SmallMelee, bool Big);

/// <summary>Роль лидера (<c>0x44b5c0</c>): 0 боец, 1 жезл, 2 вор, 3 призванный.</summary>
public enum AiLeaderRole
{
    Fighter = 0,
    Rod = 1,
    Thief = 2,
    Summoned = 3,
}

/// <summary>Тип лидера для найма: роль, цена (<c>enrollCost</c>), движение (<c>movement</c>).</summary>
public sealed record AiLeaderOption(string UnitId, AiLeaderRole Role, Bank Cost, int Move);

/// <summary>Юнит карты для стратегии S2 столицы: здания, для которых его тип — исходный (<c>0x44786e</c>: первый
/// по <c>Gunits</c> тип с <c>UPGRADE_B</c> = здание, его <c>PREV_ID</c>; у типа бывает два), опыт, порог уровня.</summary>
public sealed record AiCapitalUnit(IReadOnlyList<string> UpgradeBuildings, int Xp, int XpNext);

/// <summary>Заклинание-кандидат исследования: <paramref name="Researchable"/> — <c>0x5dbf3d(…, 1)</c> == 0;
/// <paramref name="AiCategory"/> — <c>Gspells.AI_CAT</c> (<c>LAiSpell</c>: 1 <c>L_AIS_ALL</c>, 2 <c>L_AIS_NONE</c>, …).</summary>
public sealed record AiSpellOption(string Id, int Level, SpellCategory Category, bool Researchable, int AiCategory = 1);

/// <summary>
/// Правила кандидатов по типам стратегии (<see cref="AiStrategic"/>). Чистые функции над малой моделью —
/// построители списков и исполнение остаются за вызывающим. Разбор — <c>O-re65-strategic.md</c>
/// (disciples-modding), ранг 1 — листинг, если не сказано иное.
/// </summary>
public static class AiStrategicRules
{
    // ── 5 HealRevive: 0x449ad7 / 0x449e4a / 0x449fcb ──────────────────────────────────────────

    /// <summary>Цена ухода за юнитом (<c>0x449d68…0x449d92</c>): цена лечения (<c>vft +0x5c</c>) всегда и цена воскрешения
    /// (<c>+0x58</c>), если юнита можно воскресить; <c>null</c> — юнит не кандидат (ни воскресить, ни лечить). Бюджет типа 5 проверяет
    /// построитель (<c>0x449da1</c>) — по юниту, не по месту. Исполнение — воскрешение <c>0x41f1e6</c>, затем лечение <c>0x41f1af</c>.
    /// Без бросков.</summary>
    public static Bank? CareCost(AiCareUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!unit.CanRevive && !unit.CanHeal) return null;
        return unit.HealCost + (unit.CanRevive ? unit.ReviveCost : Bank.Zero);
    }

    // ── 1 UpgradeLeader: 0x44ef81 / 0x44f0e6 / 0x44f3d2 ────────────────────────────────────────

    /// <summary>Улучшение лидера (<c>0x5d8b84</c> + фильтр <c>0x5e84c2</c>) с наибольшим <c>PRIORITY</c> из
    /// <c>GleaUpg</c> лорда (<c>0x58b6cb</c>): строгий максимум, при равенстве первое. Отряды — с
    /// <c>upgCount +0x9a &gt; 0</c> (фильтр вызывающего); одно действие на отряд в день. Бюджет не проверяется.</summary>
    public static string? PickUpgrade(IReadOnlyList<(string Id, int Priority)> upgrades)
    {
        ArgumentNullException.ThrowIfNull(upgrades);
        (string Id, int Priority)? best = null;
        foreach (var u in upgrades)
            if (best is null || u.Priority > best.Value.Priority) best = u;
        return best?.Id;
    }

    // ── 2 HireUnit: 0x44bc4a / 0x44c3ab / 0x44c6c6 ────────────────────────────────────────────

    /// <summary>
    /// Выборка найма <c>0x44bec7</c> для одной крепости: (1) в гарнизоне нет малого бойца ближнего боя
    /// (<c>0x44c1d6</c>) — случайный такой; (2) места остались и нет большого (<c>0x44c248</c>) — случайный
    /// большой; (3) на каждое оставшееся место — случайный любой. Каждая удачная выборка (1) и (2) снимает
    /// <b>одно</b> место (<c>0x44bf46</c>, <c>0x44bf80</c>: <c>dec ebx</c>, большой тоже), шаг (3) снимает место
    /// и при пустом пуле (<c>0x44bfb2</c>). Случайный — <paramref name="pick"/> над непустым пулом
    /// (<c>list[rand() % count]</c>, <see cref="CrtPicker"/>); пустой пул <c>rand()</c> не тратит (<c>0x44c108</c>).
    /// Пул не зависит от уже выбранных: позиция (<c>0x44c174</c>) проверяется по гарнизону как он есть, выбор
    /// его не меняет, — поэтому <paramref name="options"/> фильтрует вызывающий один раз (<see cref="Placeable"/>).
    /// Выборка идёт при построении списка на каждом проходе; каждый выбранный юнит — своя оценка с проверкой
    /// бюджета по его цене (<c>0x44bdfc</c>); действие — если позиция ≠ 6 и <c>hire_check 0x5deea7</c> == 0.
    /// Ранг 1 — листинг; корпус 20260918-probe65 (<c>AiStrategicHarnessTests</c>).
    /// </summary>
    /// <param name="freeSlots">Свободные места гарнизона (<c>0x5f4485</c>: предел группы − занятые клетки).</param>
    public static IReadOnlyList<AiHireOption> PickUnits(int freeSlots, bool hasSmallMelee, bool hasBig,
        IReadOnlyList<AiHireOption> options, Func<IReadOnlyList<AiHireOption>, AiHireOption> pick)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pick);
        var picked = new List<AiHireOption>();
        int slots = freeSlots;
        if (slots > 0 && !hasSmallMelee && Pool(o => o.SmallMelee) is { Count: > 0 } melee)
        {
            picked.Add(pick(melee));
            slots--;
        }
        if (slots > 0 && !hasBig && Pool(o => o.Big) is { Count: > 0 } big)
        {
            picked.Add(pick(big));
            slots--;
        }
        while (slots > 0 && options.Count > 0)
        {
            picked.Add(pick(options));
            slots--;
        }
        return picked;

        List<AiHireOption> Pool(Func<AiHireOption, bool> filter) => options.Where(filter).ToList();
    }

    /// <summary>Случайный выбор <c>0x44c06a</c>: <c>list[rand() % count]</c> на CRT <c>rand()</c> (<c>0x44c137</c>).</summary>
    public static Func<IReadOnlyList<AiHireOption>, AiHireOption> CrtPicker(CrtRand rand)
    {
        ArgumentNullException.ThrowIfNull(rand);
        return pool => pool[rand.Next() % pool.Count];
    }

    /// <summary>
    /// Фильтр позиции <c>0x44c174</c>: юнит годится, если хоть одна позиция 0..5 проходит проверку группы
    /// <c>0x5e78f7</c>. <paramref name="cells"/> — шесть клеток гарнизона (<see cref="MapStack.Slots"/>: индекс юнита
    /// или −1); малому нужна пустая клетка и одно свободное место, большому — пустая колонка (передняя 0/2/4 и
    /// задняя за ней) и два места. <paramref name="freeSlots"/> — предел группы минус занятые клетки (большой
    /// занимает две): предел гарнизона деревни — её уровень (<see cref="Fortifications.GarrisonCapacity"/>, с ростом
    /// в этот же день), столицы — 6.
    /// </summary>
    // ponytail: ранг 2 — проверка группы 0x5e68af (команда 0x612156) не прочитана; модель «клетка/колонка и
    // места» сходится со всеми выборками корпуса 20260918-probe65. Лидерство не смотрится: у гарнизона лидера нет.
    public static bool Placeable(IReadOnlyList<int> cells, int freeSlots, bool big)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (freeSlots < (big ? 2 : 1)) return false;
        if (!big) return cells.Any(c => c < 0);
        for (int front = 0; front + 1 < cells.Count; front += 2)
            if (cells[front] < 0 && cells[front + 1] < 0) return true;
        return false;
    }

    // ── 0 HireLeader: 0x44a7c2 / 0x44afed / 0x44b09f ──────────────────────────────────────────

    /// <summary>
    /// Порядок городов <c>0x44809c</c> (общий с ростом): <c>v = trunc(сила гарнизона + гостя)</c> (<c>0x5dcec7</c>,
    /// усечение <c>0x66c000</c> в <c>0x4481d0</c>), устойчивая сортировка по возрастанию, приоритет <c>max v − v</c>
    /// по усечённым — слабейший первым. Для найма лидера берутся только города без гостя (<c>+0x8c</c>) — фильтр
    /// вызывающего.
    /// <para>Сама сила — <see cref="Battle.AiStrength.Unit"/> по юнитам гарнизона (<c>+0x3c</c>) и гостя
    /// (<c>+0x30</c>) с бронёй укрепления <c>GVars.PROT_*</c> (<c>0x5dcf60</c>), числом врагов 3 и «без города» = 0
    /// — аргументы вызовов <c>0x448147</c> и <c>0x4481a2</c>, ранг 1. Сверка с журналом — в харнессе
    /// (<c>AiStrategicHarnessTests.CityRank_OrderAndStrengthFromSaves</c>): приоритет журнала воспроизводится
    /// числом, а не только порядком.</para>
    /// </summary>
    // ponytail: сила приходит готовой — считать её здесь нечем, юнитов карты правила порядка не знают.
    public static IReadOnlyList<(string Id, int Priority)> RankCities(IEnumerable<(string Id, double Strength)> cities)
    {
        ArgumentNullException.ThrowIfNull(cities);
        var truncated = cities.Select(c => (c.Id, V: (int)c.Strength)).OrderBy(c => c.V).ToList();
        int max = truncated.Count == 0 ? 0 : truncated[^1].V;
        return [.. truncated.Select(c => (c.Id, max - c.V))];
    }

    /// <summary>
    /// Выбор по долям — <c>0x44b5c0</c> (роль города), <c>0x44b6f3</c> и <c>0x44b7d0</c> (подприказ): три
    /// функции побайтно одинаковы, различаются аргументами вызова. <paramref name="allowed"/> — три флага,
    /// <paramref name="counts"/> — сколько уже есть каждого, <paramref name="shares"/> — целевые доли (float),
    /// <paramref name="answers"/> — что вернуть за каждый из трёх. Два и более запрета — ответ без счёта
    /// (0 и 1 запрещены → третий; 1 и 2 → первый; 0 и 2 → второй). Иначе доля запрещённого делится пополам
    /// между разрешёнными (<c>0x44b69d</c>: половина — первому ненулевому из двух других, вторая — второму, если
    /// он ненулевой, иначе первому), и ответ — первый, чья фактическая доля <c>count / Σcount</c> строго меньше
    /// целевой; никто — первый. Σcount = 0 — 0/0 на FPU, сравнение неупорядоченное, ответ первый.
    /// Ранг 1 — листинг; сходится со всеми 600 вызовами корпуса 20260918-probe65 (роль 300, боец 56, вор 244).
    /// </summary>
    /// <remarks>Вызовы: роль (<c>0x44a9af</c>) — доли PCT 0,33, ответы (0, 1, 2); <c>0x44b6f3</c> (<c>0x44aa06</c>) зовётся
    /// только для роли 2 (вор) — (0,5; 0,3; 0,2), ответы (1, 0, 2); <c>0x44b7d0</c> (<c>0x44aa5f</c>) — для ролей 0 и 1,
    /// (0,4; 0,4; 0,1), ответы (0, 2, 1). Помощь (<c>assist</c> в <see cref="PickLeader"/>) — ответ 1 первого или 2
    /// второго (<c>0x44aa67</c>). Откуда флаги и счётчики (<c>0x44a405</c>, <c>0x44a71e</c>), не разобрано.
    /// Зонд 6.5 называет <c>0x44b6f3</c> «fighter», а <c>0x44b7d0</c> «thief» — наоборот по вызову (корпус: 56
    /// вызовов «fighter» = 56 ответов роли 2).</remarks>
    public static int PickByShare(IReadOnlyList<bool> allowed, IReadOnlyList<int> counts, IReadOnlyList<float> shares,
                                  IReadOnlyList<int> answers)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(answers);
        if (allowed.Count != 3 || counts.Count != 3 || shares.Count != 3 || answers.Count != 3)
            throw new ArgumentException("выбор по долям — ровно три варианта");
        var (a0, a1, a2) = (allowed[0], allowed[1], allowed[2]);
        if (!a0 && !a1) return answers[2];
        if (a0 && !a1 && !a2) return answers[0];
        if (!a0 && !a2) return answers[1];

        int total = counts[0] + counts[1] + counts[2];
        if (total == 0) return answers[0];
        float t = total;
        float r1 = (float)(counts[1] / (double)t), r2 = (float)(counts[2] / (double)t);
        double r0 = counts[0] / (double)t;
        var p = new[] { shares[0], shares[1], shares[2] };
        Redistribute(a0, p, 0, 1, 2);
        Redistribute(a1, p, 1, 0, 2);
        Redistribute(a2, p, 2, 0, 1);
        if (r0 < p[0]) return answers[0];
        if (r1 < p[1]) return answers[1];
        if (r2 < p[2]) return answers[2];
        return answers[0];
    }

    /// <summary><c>0x44b69d</c>: доля запрещённого <paramref name="self"/> — пополам другим (float, как FPU пишет в dword).</summary>
    private static void Redistribute(bool allowed, float[] p, int self, int first, int second)
    {
        if (allowed) return;
        float half = p[self] * 0.5f;
        p[self] = 0;
        if (p[first] != 0) p[first] += half; else p[second] += half;
        if (p[second] != 0) p[second] += half; else p[first] += half;
    }

    /// <summary>
    /// Роль типа лидера <c>0x44a6e0</c>: <c>0x431d8c</c> → 3, вор (<c>0x41b399</c>) → 2, умеет ставить жезл
    /// (способность <c>0x837618</c>) → 1, иначе 0.
    /// </summary>
    // ponytail: ранг 2 — 0x431d8c не прочитан (в списках рас корпуса роли 3 нет); вор — LEADER_CAT 5
    // (Fortifications.ThiefLeaderCategory), жезл — LEADER_CAT 4 (L_ROD, у всех таких GLabi 13).
    public static AiLeaderRole RoleOf(int leaderCategory) => leaderCategory switch
    {
        Fortifications.ThiefLeaderCategory => AiLeaderRole.Thief,
        RodLeaderCategory => AiLeaderRole.Rod,
        _ => AiLeaderRole.Fighter,
    };

    private const int RodLeaderCategory = 4;   // LleadC L_ROD

    /// <summary>
    /// Тип лидера <c>0x44ad0f</c>. Список <c>0x5d475e</c> — <c>Grace.LEADER_1…LEADER_4</c>, затем <c>NOBLE</c> (порядок
    /// несущий: у Эльфов 8009, 8011, 8010). Отброс чужой роли (<see cref="RoleOf"/>) и недоступных по казне
    /// (<c>0x5869e6</c>, казна — пятый аргумент, в журнале <c>bank</c>); пусто — пустой id без броска. Помощь
    /// (<paramref name="assist"/>, шестой аргумент: подприказ <c>0x44b6f3</c> = 1 или <c>0x44b7d0</c> = 2) — устойчивая сортировка по
    /// движению (<c>0x44af20</c>, <c>&lt;</c>) и <c>back()</c>: максимум, при равенстве последний; иначе
    /// <c>list[R250(count)]</c> (<c>0x44aee1</c>). Бросок тратится при каждой пересборке списка (ранг 2). Бюджет
    /// типа 0 — по цене выбранного (<c>0x44aaf3</c>); исполнение отказывает при <c>0x5de921</c> ≠ 0, имя — CRT
    /// <c>rand()</c> (<c>0x44b126</c>, косметика). Ранг 1 — листинг; корпус 20260918-probe65.
    /// </summary>
    /// <param name="role">Роль города — <see cref="PickByShare"/> с аргументами <c>0x44a9af</c>.</param>
    public static AiLeaderOption? PickLeader(IReadOnlyList<AiLeaderOption> options, AiLeaderRole role, Bank purse,
                                             bool assist, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rng);
        var fit = options.Where(o => o.Role == role && !Economy.NotEnough(purse, o.Cost)).ToList();
        if (fit.Count == 0) return null;
        if (assist) return fit.OrderBy(o => o.Move).Last();
        return fit[rng.Next(fit.Count)];
    }

    /// <summary>Доли и пределы лидеров строки <c>GAI</c> (<c>0x44ac95</c>, <c>0x44a71e</c>): <c>PCT_FIGHTR</c>,
    /// <c>PCT_ROD</c>, <c>PCT_THIEF</c> (×0,01 — доли ролей) и <c>L/T/R_MIN/MAX</c> (бойцы, воры, жезлоносцы).</summary>
    public sealed record AiLeaderShares(int Category, int PctFighter, int PctRod, int PctThief,
                                        int FighterMin, int FighterMax, int ThiefMin, int ThiefMax, int RodMin, int RodMax);

    /// <summary>Доли подприказа (<c>0x44ac95</c>, константы): боец — обычный 0,4 (<c>0x6cf6f4</c>), помощь 0,4,
    /// защита города 0,1; вор — помощь 0,5, обычный 0,3, кража 0,2.</summary>
    public static readonly IReadOnlyList<float> FighterOrderShares = [0.4f, 0.4f, 0.1f];

    public static readonly IReadOnlyList<float> ThiefOrderShares = [0.5f, 0.3f, 0.2f];

    /// <summary>Уровень сценария (<c>CScenarioInfo +0x70</c>), с которого действуют пределы лидеров (<c>0x61eb31</c>:
    /// <c>cmp eax, 0xa; setge</c>).</summary>
    public const int LeaderLimitsFromLevel = 10;

    /// <summary>Строка <c>GAI</c>, из которой с уровня 10 берутся пределы лидеров всех игроков (<c>0x44a732</c>: <c>[0x8380a0]</c> —
    /// <c>L_AI_SMALL</c> из <c>LaiAtt.dbf</c>, <c>CATEGORY</c> 0).</summary>
    public const int HighLevelLimitsCategory = 0;

    /// <summary>Категория лорда «дипломат» (<c>LLord.dbf</c> <c>L_DIPLOMAT</c> = 2, <c>[0x837778]</c>).</summary>
    public const int DiplomatLordCategory = 2;

    /// <summary>
    /// Пределы лидеров (<c>0x44a71e</c>, ранг 1 по листингу; зовут список 0 <c>0x44a8ce</c> и база его бюджета <c>0x44a3a3</c>):
    /// ниже уровня 10 — <c>L/T/R_MIN/MAX</c> строки отношения игрока; с уровня 10 — строки <see cref="HighLevelLimitsCategory"/>
    /// (<paramref name="highLevelRow"/>; нет её — строка игрока), и у лорда-дипломата <c>T_MAX</c> на 1 больше (<c>0x44a7b9</c>:
    /// <c>inc [edi]</c>, четвёртое из шести полей — <c>+0x48</c>). Доли <c>PCT_*</c> всегда свои.
    /// </summary>
    public static AiLeaderShares LeaderLimits(AiLeaderShares own, AiLeaderShares? highLevelRow, bool highLevel, bool diplomat)
    {
        ArgumentNullException.ThrowIfNull(own);
        if (!highLevel) return own;
        var row = highLevelRow ?? own;
        return own with
        {
            FighterMin = row.FighterMin, FighterMax = row.FighterMax, ThiefMin = row.ThiefMin,
            ThiefMax = row.ThiefMax + (diplomat ? 1 : 0), RodMin = row.RodMin, RodMax = row.RodMax,
        };
    }

    /// <summary>База бюджета «с запасом» (<c>push 0x5a</c>: столица <c>0x446a8e</c>/<c>0x446ac7</c>, найм лидера <c>0x44a1c0</c>).</summary>
    public const int ShortBudgetBase = 90;

    /// <summary>База бюджета «всё» (<c>push 0x64</c>: найм юнита в день 1 <c>0x44bc27</c>, найм лидера без бойцов <c>0x44a06d</c>).</summary>
    public const int FullBudgetBase = 100;

    /// <summary>
    /// База бюджета найма лидера (слот 2 <c>CAiHireLeaderList</c> <c>0x44a339</c> → <c>0x44a053</c>, ранг 1): бойцов нет — 100;
    /// бойцов меньше <c>L_MIN</c>, воров меньше <c>T_MIN</c> при допуске вора (<c>0x44a232</c>) или жезлоносцев меньше
    /// <c>R_MIN</c> при допуске жезла (<c>0x44a207</c>) — 90; дальше доля <c>B_LEADERS</c> (<c>0x58f666</c>), если бойцов меньше
    /// <c>L_MAX</c>, а с уровня сценария 10 — ещё если воров меньше <c>T_MAX</c> (при допуске) или жезлоносцев меньше <c>R_MAX</c>
    /// (при допуске); иначе 0. (Без уровня 10 вторые проверки воров и жезлов повторяют первые и не срабатывают.)
    /// </summary>
    /// <param name="bLeaders">Доля <c>B_LEADERS</c> строки лорда (<see cref="AiBudgetRules.Vanilla"/> — 15).</param>
    public static int LeaderBudgetBase(int fighters, int rods, int thieves, AiLeaderShares shares, bool rodOk, bool thiefOk,
                                       bool highLevel, int bLeaders)
    {
        ArgumentNullException.ThrowIfNull(shares);
        if (fighters == 0) return FullBudgetBase;
        if (fighters < shares.FighterMin || (thieves < shares.ThiefMin && thiefOk) || (rods < shares.RodMin && rodOk))
            return ShortBudgetBase;
        bool more = fighters < shares.FighterMax
                    || (highLevel && ((thieves < shares.ThiefMax && thiefOk) || (rods < shares.RodMax && rodOk)));
        return more ? bLeaders : 0;
    }

    /// <summary>База бюджета столицы (слот 2 <c>CAiCapitalList</c> <c>0x446a48</c>, ранг 1): нет храма (<c>0x5dcc28</c> == 2) или ни
    /// одного полевого лидера (боец, разведчик, маг; не <c>AI_IGNORE</c> — <c>0x446b59</c>) — 90, иначе <c>B_CAPITAL</c>.</summary>
    public static int CapitalBudgetBase(bool hasTemple, int fieldLeaders, int bCapital) =>
        !hasTemple || fieldLeaders == 0 ? ShortBudgetBase : bCapital;

    /// <summary>База бюджета найма юнита (слот 2 <c>CAiHireUnitList</c> <c>0x44bba0</c> → <c>0x44bc09</c>, ранг 1): в день 1 — 100,
    /// иначе <c>B_UNITS</c> (<c>0x58f648</c>).</summary>
    public static int HireUnitBudgetBase(int day, int bUnits) => day == 1 ? FullBudgetBase : bUnits;

    // ── 4 Capital: 0x446ccb / 0x447cb4 / 0x447d5c ─────────────────────────────────────────────

    private static readonly int[] UtilityOrder = [1, 2, 0];   // S1: лечение 0x8379f0, магия 0x837a00, гильдия 0x8379e0
    private const int XpMargin = 5;                           // 0x44773c: xp > xpNext − 5

    /// <summary>Постройка без проверки казны: лечение, магия, гильдия (<c>0x446dd9…0x446e2a</c>) — для них
    /// <c>getBuildingStatus</c> зовётся с <c>ignoreCost</c> и бюджет не проверяется.</summary>
    public static bool IgnoresCost(BuildingType building) =>
        building.Category != Economy.UnitBuildingCategory;

    /// <summary>
    /// Выбор постройки (<c>0x446ccb</c>). Нет храма (<c>0x5dcc28</c> == 2: у игрока нет постройки категории
    /// «лечение»; 1 — нет игрока, 3 — золото ≤ 0) — сначала S1, иначе S2; пусто — запасная S1.
    /// S1 <c>0x44790a</c>: первая доступная лечения, затем магии, затем гильдии; нет — здание найма (<c>ENROLL_B</c>,
    /// <c>0x447ee5</c> vft+0xc) базового юнита ветки <c>0x837f28</c>, если оно доступно. S2 <c>0x446ea3</c>: здание
    /// улучшения, у которого больше всего юнитов <b>всей карты</b> (обход <c>CMidUnit</c> без проверки владельца) с
    /// <c>xp &gt; xpNext − 5</c> (<c>0x44773c</c>, <c>jle</c> — строго), строгий максимум &gt; 0, при равенстве первое.
    /// Опыт — после дневного чита ИИ (<see cref="Units.Experience.AiDailyXp"/>), он идёт раньше стратегии (корпус q09).
    /// </summary>
    /// <param name="available">Доступные постройки в порядке списка лорда (<c>0x5f9988</c>, см. <see cref="Available"/>).</param>
    /// <param name="units">Все юниты карты (<see cref="AiCapitalUnit"/>).</param>
    /// <param name="sideshowBuilding">Здание найма базового юнита ветки <c>0x837f28</c> — по корпусу q09 это
    /// <c>L_SIDESHOW</c> (4, <c>Grace.SOLDIER_5</c>): Легионы день 8 строят Fiend <c>g000bb0071</c>. Ранг 2.</param>
    // ponytail: ранг 2 — ветка 0x837f28 = L_SIDESHOW выведена из одного наблюдения; 0x581ee1 не прочитан.
    public static BuildingType? PickBuilding(IReadOnlyList<BuildingType> available, bool hasTemple,
                                             IReadOnlyList<AiCapitalUnit> units, string sideshowBuilding)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(sideshowBuilding);
        if (available.Count == 0) return null;   // 0x447bf3: нужен непустой список
        return hasTemple
            ? Upgrades(available, units) ?? Utility(available, sideshowBuilding)
            : Utility(available, sideshowBuilding) ?? Upgrades(available, units);
    }

    private static BuildingType? Utility(IReadOnlyList<BuildingType> available, string sideshowBuilding) =>
        UtilityOrder.Select(cat => available.FirstOrDefault(b => b.Category == cat)).FirstOrDefault(b => b is not null)
        ?? available.FirstOrDefault(b => b.Id.Equals(sideshowBuilding, StringComparison.OrdinalIgnoreCase));

    private static BuildingType? Upgrades(IReadOnlyList<BuildingType> available, IReadOnlyList<AiCapitalUnit> units)
    {
        BuildingType? best = null;
        int bestCount = 0;
        foreach (var b in available)
        {
            int n = units.Count(u => u.Xp > u.XpNext - XpMargin
                                     && u.UpgradeBuildings.Contains(b.Id, StringComparer.OrdinalIgnoreCase));
            if (n > bestCount) (best, bestCount) = (b, n);
        }
        return best;
    }

    /// <summary>Кандидат столицы: <c>getBuildingStatus</c> (<c>0x5dc9e8</c>, с <see cref="IgnoresCost"/>) == 0 и,
    /// для построек ветки юнитов, бюджет типа 4 (<c>0x446e60</c>). Исполнение <c>0x41ea0f</c>, без бросков.</summary>
    public static bool CapitalAllowed(BuildingType building, BuildStatus status, AiBudget budget)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(budget);
        return status == BuildStatus.CanBeBuilt
               && (IgnoresCost(building) || budget.Allows(AiStepType.Capital, building.Cost));
    }

    /// <summary>
    /// Список <c>0x5f9988</c>: постройки из списка лорда (<c>GbuiList</c>), которые не построены, требуемая
    /// построена, нет построенной «сестры» ветки и уровень юнитов не выше предела сценария — ровно
    /// <see cref="Economy.CanBuild"/> с <c>ignoreCost</c>. Порядок — порядок <paramref name="lordBuildings"/>.
    /// </summary>
    public static IReadOnlyList<BuildingType> Available(MapState map, string playerId,
        IEnumerable<string> lordBuildings, EconomyRules rules, int maxUnitLevel = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(lordBuildings);
        ArgumentNullException.ThrowIfNull(rules);
        return [.. lordBuildings
            .Where(id => rules.Buildings.ContainsKey(id)
                         && Economy.CanBuild(map, playerId, id, rules, ignoreCost: true, maxUnitLevel)
                            == BuildStatus.CanBeBuilt)
            .Select(id => rules.Buildings[id])];
    }

    // ── 3 GrowCities: 0x44972d / 0x449949 / 0x4499d2 ──────────────────────────────────────────

    /// <summary>Рост деревень в порядке <see cref="RankCities"/>: <c>0x5dc62a</c> == 0 и цена <c>0x5dc722</c> в
    /// бюджете типа 3; одно действие на деревню в день. Исполнение <c>0x41e99c</c>, без бросков.</summary>
    public static IReadOnlyList<string> GrowCandidates(IEnumerable<(string Id, bool CanGrow, Bank Cost)> villages,
                                                       AiBudget budget)
    {
        ArgumentNullException.ThrowIfNull(villages);
        ArgumentNullException.ThrowIfNull(budget);
        return [.. villages.Where(v => v.CanGrow && budget.Allows(AiStepType.GrowCities, v.Cost)).Select(v => v.Id)];
    }

    // ── 6 SpellRes: 0x44ce1e / 0x44d28a / 0x44d30c ────────────────────────────────────────────

    private const int ResearchPeriod = 5;
    private const int KnownLimit = 12;
    private const int KnownLimitMage = 15;
    private const int MaxResearchLevel = 4;
    private const int AiSpellNone = 2;   // LAiSpell L_AIS_NONE — 0x838100

    private static readonly SpellCategory[] ResearchOrder =
        [SpellCategory.Summon, SpellCategory.Attack, SpellCategory.Heal, SpellCategory.Boost, SpellCategory.Lower];

    /// <summary>
    /// Резерв бюджета исследования на день (<c>ai_budget.flag</c> типа 6, байт <c>+4</c> описателя бюджета — его строит слот 2
    /// списка <c>CAiSpellResList</c> <c>0x44cd3e</c>): известных заклинаний (<c>CPlayerKnownSpells</c>, <c>0x5fec30</c> →
    /// <c>0x597fbb</c>) меньше половины предела <c>0x44cdeb</c> — 12, у мага (<c>0x837760</c>) 15; то есть меньше 6 (у мага 7).
    /// При резерве база бюджета — казна плюс казна магии (<see cref="AiBudget.StartOf"/>). Ранг 1 по листингу; по корпусу
    /// (<c>AiTurnStartHarnessTests.DayBudget_ByType</c>) — 549 из 549 строк, у всех лордов корпуса категория «воин».
    /// </summary>
    public static bool ResearchReserve(int known, LordCategory lord) =>
        known < (lord == LordCategory.Mage ? KnownLimitMage : KnownLimit) / 2;

    /// <summary>
    /// Исследование: только в день, кратный 5, и при известных &lt; 12 (у лорда <c>0x837760</c> — &lt; 15);
    /// уровни 1..4 (ранг 2), внутри уровня SUMMON → ATTACK → HEAL → BOOST → LOWER, первое подходящее.
    /// Цена <c>0x582afa</c> в бюджете типа 6 (<c>0x44cee6</c>) — у вызывающего; исполнение <c>0x41e915</c>.
    /// </summary>
    /// <remarks>Отброс <c>0x44d1ab</c>: AI-категория заклинания (<c>0x572688</c>, <c>+8</c>) равна <c>0x838100</c> —
    /// по корпусу 20260918-probe65 это <c>L_AIS_NONE</c> (2): без него порт выбирал 0042/0101/0065 вместо 0043/0103/0066
    /// (ранг 2, таблица категорий заполняется при запуске).</remarks>
    // ponytail: лорд 0x837760 принят за мага (Llord 0 L_MAGE) — ранг 2; компаратор 0x44d7da не переносится:
    // порядок кандидатов внутри категории — порядок входа (GspellR), корпус 20260918-probe65 это не опровергает.
    public static AiSpellOption? PickResearch(int day, int known, LordCategory lord, IReadOnlyList<AiSpellOption> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        if (day % ResearchPeriod != 0) return null;
        if (known >= (lord == LordCategory.Mage ? KnownLimitMage : KnownLimit)) return null;
        for (int level = 1; level <= MaxResearchLevel; level++)
            foreach (var category in ResearchOrder)
                if (spells.FirstOrDefault(s => s.Researchable && s.AiCategory != AiSpellNone
                                                         && s.Level == level && s.Category == category) is { } hit)
                    return hit;
        return null;
    }
}
