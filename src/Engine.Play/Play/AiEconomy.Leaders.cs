using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Отряды игрока по ролям лидера и приказу ИИ (<c>0x44a405</c>): бойцы (обычный / защита города / помощь),
/// жезлоносцы, воры (обычный / помощь / кража).</summary>
public sealed record AiLeaderCounts(int Fighters, int FighterNormal, int FighterDefend, int FighterAssist,
                                    int Rods, int Thieves, int ThiefNormal, int ThiefAssist, int ThiefSteal);

/// <summary>Допуски выбора (<c>0x44a207</c>, <c>0x44a232</c>, <c>0x44b1d7</c>): роли «жезл» и «вор», подприказы
/// «помощь», «защита города», «обычный» у вора и «кража».</summary>
public sealed record AiLeaderFlags(bool Rod, bool Thief, bool Assist, bool DefendCity, bool ThiefNormal, bool Steal);

/// <summary>Список 0 — найм лидера (<c>0x44a7c2</c>, разбор 23.09.2026, <c>docs/ai-economy.md</c>).</summary>
public static partial class AiEconomy
{
    /// <summary>
    /// Счётчики <c>0x44a405</c> (ранг 1): свои отряды с приказом <c>L_NORMAL</c> (<c>+0xa8</c>, <c>0x44a6c8</c>), чей
    /// лидер своей расы (<c>IUsSoldier</c> слот <c>+0xc</c> против расы игрока <c>+0x24</c>); роль лидера
    /// <c>0x44a6e0</c> (призыв — не считается); у бойца и вора — ещё по приказу ИИ (<c>+0xb8</c>); отряд с <c>AI_IGNORE</c>
    /// (<c>+0x9c</c>, <c>0x44a4e7</c>) не считается.
    /// </summary>
    public static AiLeaderCounts LeaderCounts(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        int f = 0, fn = 0, fd = 0, fa = 0, r = 0, t = 0, tn = 0, ta = 0, ts = 0;
        foreach (var s in map.Stacks.Values)
        {
            if (!s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase) || s.Order != (int)StackOrder.Normal || s.AiIgnore) continue;
            if (!map.Units.TryGetValue(s.LeaderId, out var leader)
                || !book.RaceOf(leader.TypeId).Equals(player.RaceId, StringComparison.OrdinalIgnoreCase)) continue;
            if (UnitCategory.IsSummonImpl(book.CategoryOf(leader.TypeId))) continue;   // 0x44a6e6 → 0x431d8c: иллюзия — тоже IUsSummon
            var order = (StackOrder)s.AiOrder;
            switch (AiStrategicRules.RoleOf(book.LeaderCategoryOf(leader.TypeId)))
            {
                case AiLeaderRole.Fighter:
                    f++;
                    if (order == StackOrder.DefendCity) fd++;
                    else if (order == StackOrder.Assist) fa++;
                    else fn++;
                    break;
                case AiLeaderRole.Rod:
                    r++;
                    break;
                case AiLeaderRole.Thief:
                    t++;
                    if (order == StackOrder.Steal) ts++;
                    else if (order == StackOrder.Assist) ta++;
                    else tn++;
                    break;
            }
        }
        return new AiLeaderCounts(f, fn, fd, fa, r, t, tn, ta, ts);
    }

    /// <summary>
    /// Допуски (<c>0x44a207</c>, <c>0x44a232</c>, <c>0x44b1d7</c>): вор — в списке лидеров найма (<c>0x5d475e</c>) есть вор, а он
    /// там только при гильдии (<see cref="Economy.HasGuild"/>); помощь — есть
    /// боец с обычным приказом; защита города — бойцов-защитников меньше, чем своих деревень («угрозы» <c>0x474072</c>);
    /// кража — на карте есть лавка (<c>0x601e55</c> — сумма по лавкам <c>0x6012c5</c>; в журналах всегда 1, ранг 2).
    /// Жезл (<c>0x44a207</c>, ранг 1 по листингу 28.09) — от расы не зависит: на карте есть кристалл (<c>0x44b545</c> — число объектов
    /// типа 0x2c, <c>iterator_omap&lt;0x2c&gt;</c>) или событие сценария, чей последний эффект — <c>L_WIN</c>, а среди условий есть
    /// <c>L_TRANSFORM_LAND</c> (<c>0x5f2701</c> с категорией <c>0x837df0</c>). «Обычный» подприказ вора (<c>0x44b282</c>, флаг —
    /// отрицание его результата) — есть другой игрок, не нейтрал (<c>0x5e5358</c>), которого игрок ещё не разведал
    /// (<c>0x5e46c2</c>, <see cref="MapPlayer.Spied"/>).
    /// </summary>
    public static AiLeaderFlags LeaderFlags(GameSession session, string playerId, AiLeaderCounts counts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(counts);
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        var leaders = book.RaceLeaders.GetValueOrDefault(player.RaceId) ?? [];
        bool rod = map.Objects.Values.Any(o => o.Kind == "CR")
                   || session.State.Events.Events.Any(e => e.Effects.Count > 0 && e.Effects[^1] is WinEffect
                                                           && e.Conditions.Any(c => c is TransformLandCondition));
        // Список лидеров найма 0x5d475e берёт вора, только если у игрока есть гильдия (0x5d4857; карта 21: Кланы и Нежить строят её
        // проходами 8 и 9 дня 1 и нанимают вора днём 2, Легионы с гильдией с начала — днём 1).
        bool thief = Economy.HasGuild(player.Buildings, book.Economy.Buildings)
                     && leaders.Any(l => AiStrategicRules.RoleOf(book.LeaderCategoryOf(l)) == AiLeaderRole.Thief);
        int villages = map.Sites.Count(x => x.Kind == SiteKind.Village && x.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        bool steal = map.Sites.Any(x => x.Kind == SiteKind.Merchant);
        bool thiefNormal = map.Players.Keys.Any(p => !p.Equals(playerId, StringComparison.OrdinalIgnoreCase) && !AiTactics.NeutralOwner(session, p)
                                                     && !player.Spied.Contains(p, StringComparer.OrdinalIgnoreCase));
        return new AiLeaderFlags(rod, thief, counts.FighterNormal > 0, counts.FighterDefend < villages, thiefNormal, steal);
    }

    /// <summary>
    /// Порядок городов <c>0x44809c</c>: свои укрепления, сила гарнизона и гостя (<c>0x5dcec7</c>: «без города» 0, врагов 3,
    /// здоровье текущее, броня укрепления <c>PROT_*</c>), <see cref="AiStrategicRules.RankCities"/> — слабейший первым.
    /// Порядок обхода укреплений — порядок объектов карты (ранг 3).
    /// </summary>
    public static IReadOnlyList<(string Id, int Priority)> CityRank(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        return AiStrategicRules.RankCities(map.Sites
            .Where(x => x.Kind is SiteKind.Capital or SiteKind.Village && x.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                var ids = x.Guards.ToList();
                if (x.StackId.Length > 0 && map.Stacks.TryGetValue(x.StackId, out var guest)) ids.AddRange(guest.UnitIds);
                return (x.Id, AiTactics.UnitsStrength(session, ids, ignoreCity: false, enemies: CityRankEnemies,
                                                       AiTactics.AiFortArmor(session, x.Id)));
            }));
    }

    /// <summary>
    /// Правила бюджета дня игрока — базы, которые списки считают сами (слот 2 списков, ранг 1): найм лидера —
    /// <see cref="AiStrategicRules.LeaderBudgetBase"/> по счётчикам ролей (<see cref="LeaderCounts"/>) и допускам
    /// (<see cref="LeaderFlags"/>); столица — <see cref="AiStrategicRules.CapitalBudgetBase"/> (храм, полевые лидеры
    /// <see cref="AiTactics.FieldLeaders"/>); найм юнита — <see cref="AiStrategicRules.HireUnitBudgetBase"/> (день 1 — 100). Прочие — таблица.
    /// «Первого дня» отдельно нет: 95 % в день 1 — это база 90 + 5 за день без траты.
    /// </summary>
    public static AiBudgetRules BudgetRules(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        var vanilla = AiBudgetRules.Vanilla;
        var bases = vanilla.Base.ToDictionary(p => p.Key, p => p.Value);
        // Доли строки GAI отношения игрока (B_UNITS, B_LEADERS, B_CAP, B_CITIES); нет строки — таблица ванили.
        if (book.AiBudgetBases.TryGetValue(player.Attitude, out var row))
            foreach (var (type, value) in row) bases[type] = value;
        bases[AiStepType.HireUnit] = AiStrategicRules.HireUnitBudgetBase(map.Turn, bases[AiStepType.HireUnit]);
        bases[AiStepType.Capital] = AiStrategicRules.CapitalBudgetBase(Healing.HasTemple(player.Buildings, book.Economy.Buildings),
                                                                       AiTactics.FieldLeaders(session, playerId), bases[AiStepType.Capital]);
        if (book.AiLeaderShares.TryGetValue(player.Attitude, out var own))
        {
            var shares = Limits(session, player, own);
            var c = LeaderCounts(session, playerId);
            var flags = LeaderFlags(session, playerId, c);
            bases[AiStepType.HireLeader] = AiStrategicRules.LeaderBudgetBase(
                c.Fighters, c.Rods, c.Thieves, shares, flags.Rod, flags.Thief,
                session.State.Info.SuggestedLevel >= AiStrategicRules.LeaderLimitsFromLevel, bases[AiStepType.HireLeader]);
        }
        return vanilla with { Base = bases, FirstDay = new Dictionary<AiStepType, int>() };
    }

    /// <summary>Доли строки отношения игрока с пределами <see cref="AiStrategicRules.LeaderLimits"/> (<c>0x44a71e</c>).</summary>
    private static AiStrategicRules.AiLeaderShares Limits(GameSession session, MapPlayer player, AiStrategicRules.AiLeaderShares own)
    {
        var book = session.Book!;
        return AiStrategicRules.LeaderLimits(own, book.AiLeaderShares.GetValueOrDefault(AiStrategicRules.HighLevelLimitsCategory),
                                             session.State.Info.SuggestedLevel >= AiStrategicRules.LeaderLimitsFromLevel,
                                             book.LordCategory(player.LordId) == AiStrategicRules.DiplomatLordCategory);
    }

    /// <summary>Число врагов в силе города для порядка (<c>0x448147</c>/<c>0x4481a2</c>: <c>push 3</c>).</summary>
    private const int CityRankEnemies = 3;

    /// <summary>
    /// Список 0 (<c>0x44a7c2</c>, ранг 1): по городам <see cref="CityRank"/> без гостя — роль
    /// (<see cref="AiStrategicRules.PickByShare"/>: боец всегда, жезл и вор — по допускам, доли <c>PCT_*</c>), пределы
    /// <c>L/T/R_MAX</c> с уровня сценария 10, подприказ (боец — обычный / защита города / помощь, вор — помощь / обычный /
    /// кража), тип (<see cref="AiStrategicRules.PickLeader"/> на R250 партии: бросок — при каждой сборке списка). Пустой
    /// тип обрывает перебор; не прошедший бюджет (<c>0x4465ba</c>) город пропускается; счётчики растут, будто лидер уже
    /// нанят. Оценка — по городу (память дня блокирует его).
    /// Пределы — <see cref="Limits"/> (с уровня 10 — строка <c>L_AI_SMALL</c>, дипломату вором больше).
    /// </summary>
    private static IEnumerable<AiEvaluation> HireLeader(GameSession session, string playerId, AiBudget budget)
    {
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        if (!book.AiLeaderShares.TryGetValue(player.Attitude, out var own)) yield break;
        var shares = Limits(session, player, own);
        var c = LeaderCounts(session, playerId);
        var flags = LeaderFlags(session, playerId, c);
        bool limits = session.State.Info.SuggestedLevel >= AiStrategicRules.LeaderLimitsFromLevel;
        var options = (book.RaceLeaders.GetValueOrDefault(player.RaceId) ?? [])
            .Where(id => book.Economy.Hire.ContainsKey(id))
            .Select(id => new AiLeaderOption(id, AiStrategicRules.RoleOf(book.LeaderCategoryOf(id)),
                                             Economy.HireCost(id, book.Economy), book.MoveByType.GetValueOrDefault(id)))
            .ToList();
        foreach (var (fortId, priority) in CityRank(session, playerId))
        {
            var fort = map.Sites.First(x => x.Id.Equals(fortId, StringComparison.OrdinalIgnoreCase));
            if (fort.StackId.Length > 0) continue;
            var role = (AiLeaderRole)AiStrategicRules.PickByShare([true, flags.Rod, flags.Thief], [c.Fighters, c.Rods, c.Thieves],
                [shares.PctFighter * 0.01f, shares.PctRod * 0.01f, shares.PctThief * 0.01f], [0, 1, 2]);
            int sub;
            if (role == AiLeaderRole.Thief)
            {
                if (limits && c.Thieves >= shares.ThiefMax) continue;
                sub = AiStrategicRules.PickByShare([flags.Assist, flags.ThiefNormal, flags.Steal],
                    [c.ThiefAssist, c.ThiefNormal, c.ThiefSteal], AiStrategicRules.ThiefOrderShares, [1, 0, 2]);
            }
            else
            {
                if (limits && (role == AiLeaderRole.Rod ? c.Rods >= shares.RodMax : c.Fighters >= shares.FighterMax)) continue;
                sub = AiStrategicRules.PickByShare([true, flags.Assist, flags.DefendCity],
                    [c.FighterNormal, c.FighterAssist, c.FighterDefend], AiStrategicRules.FighterOrderShares, [0, 2, 1]);
            }
            bool assist = role == AiLeaderRole.Thief ? sub == 1 : sub == 2;
            var pick = AiStrategicRules.PickLeader(options, role, map.Players[playerId].Bank, assist, session.AiRng);
            if (pick is null) yield break;
            if (!budget.Allows(AiStepType.HireLeader, pick.Cost)) continue;
            var order = StackOrder.Normal;
            switch (role)
            {
                case AiLeaderRole.Thief:
                    order = sub == 1 ? StackOrder.Assist : sub == 2 ? StackOrder.Steal : StackOrder.Normal;
                    c = c with
                    {
                        Thieves = c.Thieves + 1, ThiefAssist = c.ThiefAssist + (sub == 1 ? 1 : 0),
                        ThiefSteal = c.ThiefSteal + (sub == 2 ? 1 : 0), ThiefNormal = c.ThiefNormal + (sub is 1 or 2 ? 0 : 1),
                    };
                    break;
                case AiLeaderRole.Fighter:
                    order = sub == 1 ? StackOrder.DefendCity : sub == 2 ? StackOrder.Assist : StackOrder.Normal;
                    c = c with
                    {
                        Fighters = c.Fighters + 1, FighterDefend = c.FighterDefend + (sub == 1 ? 1 : 0),
                        FighterAssist = c.FighterAssist + (sub == 2 ? 1 : 0), FighterNormal = c.FighterNormal + (sub is 1 or 2 ? 0 : 1),
                    };
                    break;
                default:
                    c = c with { Rods = c.Rods + 1 };
                    break;
            }
            string target = $"{pick.UnitId}|{(int)order}";
            _ = priority;   // приоритет города идёт в счёт оценки; выбор стратегии — первое действие, счёт на него не влияет
            yield return new AiEvaluation(fortId, () => [new AiStepAction(AiStepType.HireLeader, fortId, target)]);
        }
    }

    /// <summary>Исполнение найма лидера (<c>0x44b09f</c>): имя — CRT <c>rand()</c> по мужским именам расы
    /// (<c>0x44b126</c>, косметика), затем <see cref="GameSession.HireLeader"/>.</summary>
    private static bool ExecuteHireLeader(GameSession session, AiStepAction action)
    {
        var parts = action.Target.Split('|');
        var order = parts.Length > 1 && int.TryParse(parts[1], out int o) ? (StackOrder)o : StackOrder.Normal;
        var fort = session.Map.Sites.First(x => x.Id.Equals(action.Id, StringComparison.OrdinalIgnoreCase));
        var names = session.Book!.LeaderNames.GetValueOrDefault(session.Map.Players[fort.Owner].RaceId) ?? [];
        string name = names.Count > 0 ? names[session.Crt.Next() % names.Count] : "";
        return session.HireLeader(action.Id, parts[0], order, name) is null;
    }
}
