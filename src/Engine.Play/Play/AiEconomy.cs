using LastEdict.Data.Dbf;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Найм лидера из журнала для стратегии харнесса (<see cref="GameSession.BeginAiTurn"/>, <c>journalLeaders</c>): крепость,
/// тип и применение строки журнала к состоянию (null — не применилась).</summary>
public sealed record JournalLeaderHire(string Fort, string UnitType, Func<GameState, GameState?> Apply);

/// <summary>Память стратегии ИИ одного игрока, которая живёт между днями и пишется в сохранение: день последней
/// траты по типу списка (для бюджета, <see cref="AiBudget.StartOf"/>) и память дня «действие → день»
/// (<see cref="AiStrategic.MemoBlocks"/>).</summary>
public sealed record AiMemory(IReadOnlyDictionary<string, int> LastSpend, IReadOnlyDictionary<string, int> Memo)
{
    public static readonly AiMemory Empty = new(new Dictionary<string, int>(), new Dictionary<string, int>());

    public int LastSpendOf(AiStepType type) => LastSpend.GetValueOrDefault(((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public IReadOnlyDictionary<(string Id, AiStepType Type), int> MemoKeys() =>
        Memo.ToDictionary(p => (p.Key[(p.Key.IndexOf('|') + 1)..], (AiStepType)int.Parse(p.Key[..p.Key.IndexOf('|')],
                                                                                            System.Globalization.CultureInfo.InvariantCulture)),
                          p => p.Value);

    public static AiMemory From(IReadOnlyDictionary<AiStepType, int> lastSpend,
                                IReadOnlyDictionary<(string Id, AiStepType Type), int> memo) => new(
        lastSpend.ToDictionary(p => ((int)p.Key).ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.Value),
        memo.ToDictionary(p => $"{(int)p.Key.Type}|{p.Key.Id}", p => p.Value));
}

/// <summary>Экономика ИИ из карты (<c>docs/ai-turn-plan.md</c> срез 3): построители стратегических списков для
/// <see cref="AiStrategic.RunTurn"/> и исполнение их действий через действия партии (<c>GameSession.City.cs</c>).
/// Перенесены список 4 — постройка в столице (правила <see cref="AiStrategicRules.PickBuilding"/>,
/// <see cref="AiStrategicRules.Available"/>, <see cref="AiStrategicRules.CapitalAllowed"/>; сверка выбора — харнесс
/// <c>AiStrategicCorpusTests</c>), список 2 — найм юнитов в гарнизоны, список 5 — лечение и воскрешение, список 0 — найм
/// лидеров (<c>AiEconomy.Leaders.cs</c>), список 3 — рост деревень, список 1 — навык лидера, список 6 — исследование, список 8 — дипломатия
/// (<c>GameSession.Diplomacy.cs</c>).</summary>
public static partial class AiEconomy
{
    /// <summary>Оценки списка <paramref name="type"/> игрока на текущем состоянии партии.</summary>
    public static IEnumerable<AiEvaluation> Lists(GameSession session, string playerId, AiStepType type, AiBudget budget) =>
        type switch
        {
            AiStepType.Capital => [new AiEvaluation("", () => Capital(session, playerId, budget))],
            AiStepType.HireUnit when session.HireUnitsEnabled => HireUnit(session, playerId, budget),
            AiStepType.HealRevive => HealRevive(session, playerId, budget),
            AiStepType.HireLeader when session.HireLeadersEnabled => HireLeader(session, playerId, budget),
            AiStepType.HireLeader when session.JournalLeaders is [var next, ..] =>
                [new AiEvaluation(next.Fort, () => [new AiStepAction(AiStepType.HireLeader, next.Fort, next.UnitType)])],
            AiStepType.GrowCities => GrowCities(session, playerId, budget),
            AiStepType.UpgradeLeader => UpgradeLeader(session, playerId),
            AiStepType.SpellResearch => SpellResearch(session, playerId, budget),
            AiStepType.Diplomacy => Diplomacy(session, playerId),
            _ => [],
        };

    /// <summary>Исполнение действия; <c>true</c> — успех (память дня и трата бюджета). В <paramref name="report"/> —
    /// сообщения ИИ и итоги дипломатии.</summary>
    public static bool Execute(GameSession session, string playerId, AiStepAction action, List<string>? report = null) => action.Type switch
    {
        AiStepType.Capital => session.Build(playerId, action.Target, ai: true) == BuildStatus.CanBeBuilt,
        AiStepType.HireUnit => session.HireUnit(action.Id, action.Target) is null,
        AiStepType.HealRevive => Care(session, action.Id, action.Target),
        AiStepType.HireLeader => session.JournalLeaders is null ? ExecuteHireLeader(session, action) : session.ExecuteJournalLeader(action),
        AiStepType.GrowCities => session.GrowCity(action.Id) == CityGrowCheck.Allowed,
        AiStepType.UpgradeLeader => session.ChooseSkill(action.Id, action.Target) == LastEdict.Rules.Units.SkillRefusal.None,
        AiStepType.SpellResearch => session.ResearchSpell(playerId, action.Target) is null,
        AiStepType.Diplomacy => Enum.TryParse<AiDiploAction>(action.Target, out var diplo)
                                && session.ExecuteDiplomacy(playerId, action.Id, diplo, report),
        _ => false,
    };

    /// <summary>Список 8 (<c>0x4486f5</c>, ранг 1 по листингу): оценка на каждого другого не-нейтрала в порядке id
    /// (<see cref="AiStrategic.DiplomacyEvaluations"/>), решение — <see cref="GameSession.DiplomacyDecision"/> на R250
    /// партии; броски тратятся на каждом проходе, пока цель не обслужена сегодня. Бюджет не проверяется. Подарок, которого
    /// не нашлось (<see cref="AiGift"/> вернул <c>null</c>), действия не даёт: цель остаётся в списке и на следующем проходе
    /// бросает снова.</summary>
    private static IEnumerable<AiEvaluation> Diplomacy(GameSession session, string playerId) =>
        AiStrategic.DiplomacyEvaluations(session.DiplomacyTargets(playerId), target => session.DiplomacyDecision(playerId, target));

    /// <summary>Список 2 (<c>0x44bc4a</c>, <c>O-re65-strategic.md</c>): свои укрепления без бунта (итератор
    /// <c>0x41983f</c>; порядок — порядок объектов карты, ранг 3), свободные места гарнизона (<c>0x5f4485</c>), выборка
    /// <see cref="AiStrategicRules.PickUnits"/> на CRT <c>rand()</c> партии (<see cref="GameSession.Crt"/>) — заново на
    /// каждом проходе; каждый выбранный — оценка с проверкой бюджета по его цене. Действие оценка даёт, только если найм
    /// проходит проверку <c>hire_check 0x5deea7</c> на казне сейчас (<c>evaluate 0x44c3ab</c>: <c>0x44c3de</c> → ненулевой код —
    /// выход без <c>0x44dd87</c>; <see cref="GameSession.HireUnitCheck"/>): бюджет типа — доля казны начала хода, и после трат
    /// других типов он бывает больше казны (human, день 38: бюджет 190, казна 40, у движка оценка FT000B пустая — иначе
    /// неисполнимый найм повторялся бы без конца). Исполняется первое действие — «нанимается в первом городе итератора».
    /// Ранг 1 по корпусу probe65 (<c>AiStrategicHarnessTests.UnitPick…</c>).</summary>
    private static IEnumerable<AiEvaluation> HireUnit(GameSession session, string playerId, AiBudget budget)
    {
        var book = session.Book!;
        var map = session.Map;
        var list = Economy.HireList(map, playerId, book.Economy);
        foreach (var fort in map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village
                                                  && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                                  && !Fortifications.Rioting(s.RiotTurn)))
        {
            var cells = fort.GuardSlots.Count == Formation.Cells ? fort.GuardSlots : Formation.EmptySlots();
            int capacity = fort.Kind == SiteKind.Capital ? 6 : Fortifications.GarrisonCapacity(fort.Size ?? 1);
            int free = Math.Max(capacity - cells.Count(c => c >= 0), 0);
            if (free == 0) continue;
            var options = list.Select(id => (Id: id, Impl: book.Rules.ImplOf(id)))
                              .Where(u => u.Impl is not null)
                              .Select(u => new AiHireOption(u.Id, SmallMelee(u.Impl!), !u.Impl!.Type.SizeSmall))
                              .Where(o => AiStrategicRules.Placeable(cells, free, o.Big)).ToList();
            var garrison = fort.Guards.Select(id => map.Units.TryGetValue(id, out var u) ? book.Rules.ImplOf(u.TypeId) : null)
                                      .OfType<LastEdict.Rules.Units.UnitImpl>().ToList();
            var picked = AiStrategicRules.PickUnits(free, garrison.Any(SmallMelee), garrison.Any(u => !u.Type.SizeSmall), options,
                                                    AiStrategicRules.CrtPicker(session.Crt));
            string fortId = fort.Id;
            foreach (var option in picked)
                yield return new AiEvaluation("", () =>
                    budget.Allows(AiStepType.HireUnit, Economy.HireCost(option.UnitId, book.Economy))
                    && session.HireUnitCheck(fortId, option.UnitId) is null
                        ? [new AiStepAction(AiStepType.HireUnit, fortId, option.UnitId)]
                        : []);
        }
    }

    /// <summary>Список 1 (<c>0x44ef81</c>, ранг 1): свои отряды с отложенным выбором навыка (<c>upgCount +0x9a</c> &gt; 0),
    /// в порядке отрядов карты; предложение <c>0x5d8b84</c> с фильтром <c>0x5e84c2</c> (<see cref="GameSession.SkillOffer"/>),
    /// выбор — наибольший <c>PRIORITY</c>, первый при равенстве (<see cref="LastEdict.Rules.Units.SkillChoice.ByAi"/>). Бюджет не
    /// проверяется, броска нет. Оценка — по отряду (память дня: один навык на героя за ход).</summary>
    private static IEnumerable<AiEvaluation> UpgradeLeader(GameSession session, string playerId)
    {
        foreach (var stack in session.Map.Stacks.Values.Where(x => x.UpgradeCount > 0
                                                                   && x.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            string stackId = stack.Id;
            yield return new AiEvaluation(stackId, () =>
                LastEdict.Rules.Units.SkillChoice.ByAi(session.SkillOffer(stackId)) is { } pick
                    ? [new AiStepAction(AiStepType.UpgradeLeader, stackId, pick.ModifId)]
                    : []);
        }
    }

    /// <summary>Список 6 (<c>0x44ce1e</c>, ранг 1 по корпусу probe65 — <c>AiStrategicHarnessTests.SpellResearch_DayAndChoice</c>):
    /// заклинания лорда (<c>GspellR</c>, порядок таблицы), доступные <see cref="GameSession.ResearchCheck"/>; выбор
    /// <see cref="AiStrategicRules.PickResearch"/> (день кратен 5, предел известных, уровень → категория). Оценка — одна, с
    /// проверкой цены в бюджете типа 6 (<c>0x44cee6</c>).</summary>
    private static IEnumerable<AiEvaluation> SpellResearch(GameSession session, string playerId, AiBudget budget)
    {
        var book = session.Book!;
        var player = session.Map.Players[playerId];
        var options = (book.LordResearch.GetValueOrDefault(player.LordId) ?? [])
            .Where(x => book.Spells.ContainsKey(x.SpellId))
            .Select(x => new AiSpellOption(x.SpellId, book.Spells[x.SpellId].Level, book.Spells[x.SpellId].Category,
                                           session.ResearchCheck(playerId, x.SpellId).Refused is null,
                                           book.SpellAiCategory.GetValueOrDefault(x.SpellId, 1)))
            .ToList();
        if (AiStrategicRules.PickResearch(session.Map.Turn, player.KnownSpells.Count, (LordCategory)book.LordCategory(player.LordId), options)
            is not { } pick) yield break;
        var price = session.ResearchCheck(playerId, pick.Id).Price;
        string spellId = pick.Id;
        yield return new AiEvaluation(spellId, () =>
            budget.Allows(AiStepType.SpellResearch, price) ? [new AiStepAction(AiStepType.SpellResearch, playerId, spellId)] : []);
    }

    /// <summary>Список 3 (<c>0x44972d</c>, ранг 1): свои деревни в порядке <see cref="CityRank"/>; рост разрешён
    /// (<c>0x5dc62a</c> = <see cref="Fortifications.CheckGrow"/>) и цена <c>0x5dc722</c>
    /// (<see cref="Treasury.CityGrowthPrice"/>) в бюджете типа 3 (<see cref="AiStrategicRules.GrowCandidates"/>). Оценка —
    /// по деревне (память дня: одна деревня — раз в день). Без бросков.</summary>
    private static IEnumerable<AiEvaluation> GrowCities(GameSession session, string playerId, AiBudget budget)
    {
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        foreach (var (id, _) in CityRank(session, playerId))
        {
            var village = map.Sites.First(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (village.Kind != SiteKind.Village) continue;
            var price = Treasury.CityGrowthPrice(book.Economy, player.RaceId, village.Size ?? 1, book.LordCategory(player.LordId));
            bool can = Fortifications.CheckGrow(village.Size ?? 1, village.RiotTurn ?? 0, village.GrowthTurn ?? 0, map.Turn,
                                                player.Bank, price) == CityGrowCheck.Allowed;
            string villageId = village.Id;
            yield return new AiEvaluation(villageId, () =>
                AiStrategicRules.GrowCandidates([(villageId, can, price)], budget).Count > 0
                    ? [new AiStepAction(AiStepType.GrowCities, villageId, villageId)]
                    : []);
        }
    }

    /// <summary>Список 5 (<c>0x449ad7</c>): оценка на <b>юнит</b> места (<c>0x449cde</c>: перебор группы <c>0x5f4441</c>/<c>0x5f44b9</c>),
    /// а не на место. Места — сначала свои отряды в крепостях (итератор отрядов <c>0x419aac</c>, <c>+0x94</c> не пуст, порядок отрядов
    /// карты), затем свои крепости — гарнизон <c>+0x3c</c> (итератор <c>0x41983f</c>), кроме бунтующих (<c>0x5e9582</c>; у гостя бунт не
    /// смотрится — ai-q11, UN0098). Юнит — кандидат, если его можно воскресить (<c>0x5de06e</c>, <see cref="Healing.CheckRevive"/>) или
    /// лечить (<c>0x5de32f</c>: жив, ранен, Храм); цена — <see cref="AiStrategicRules.CareCost"/> в бюджете типа (<c>0x4465ba</c>).
    /// Действие (<c>0x449f7b</c>: место <c>+8</c>, юнит <c>+0xc</c>) — уход за одним юнитом, поэтому раненых в одном месте движок
    /// лечит по шагу на юнита (карта 23: Нежить дня 48 — три шага HealRevive подряд за S143KC000a).</summary>
    private static IEnumerable<AiEvaluation> HealRevive(GameSession session, string playerId, AiBudget budget)
    {
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        bool temple = Healing.HasTemple(player.Buildings, book.Economy.Buildings);
        foreach (var (placeId, fortId, units, garrison) in CarePlaces(map, playerId))
        {
            var fort = map.Sites.First(s => s.Id.Equals(fortId, StringComparison.OrdinalIgnoreCase));
            // Бунт закрывает только место-гарнизон: гостя бунтующей деревни ИИ лечит за деньги (прогон 13.10, ai-q11: UN0098 72/90 вылечен
            // за 18, seq 1738754…1738758; бунт движок смотрит у места гарнизона +0x3c, у места гостя — нет).
            if (garrison && Fortifications.Rioting(fort.RiotTurn)) continue;
            foreach (string unitId in units.ToList())
            {
                if (!map.Units.TryGetValue(unitId, out var unit)
                    || CareUnit(session, book, unit, player.Bank, temple) is not { } care
                    || AiStrategicRules.CareCost(care) is not { } cost || !budget.Allows(AiStepType.HealRevive, cost)) continue;
                yield return new AiEvaluation("", () => [new AiStepAction(AiStepType.HealRevive, placeId, unitId)]);
            }
        }
    }

    /// <summary>Места лечения игрока: (id места, укрепление, юниты, гарнизон ли) — сначала свои отряды в своих укреплениях в порядке
    /// отрядов карты, затем гарнизоны своих укреплений в порядке объектов.</summary>
    private static IEnumerable<(string Place, string Fort, IReadOnlyList<string> Units, bool Garrison)> CarePlaces(MapState map, string playerId)
    {
        bool Own(string owner) => owner.Equals(playerId, StringComparison.OrdinalIgnoreCase);
        var forts = map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && Own(s.Owner)).ToList();
        foreach (var stack in map.Stacks.Values.Where(s => Own(s.Owner) && s.Inside.Length > 0).ToList())
            if (forts.FirstOrDefault(f => f.Id.Equals(stack.Inside, StringComparison.OrdinalIgnoreCase)) is { } fort)
                yield return (stack.Id, fort.Id, stack.UnitIds, false);
        foreach (var fort in forts) yield return (fort.Id, fort.Id, fort.Guards, true);
    }

    private static AiCareUnit? CareUnit(GameSession session, GameBook book, MapUnit unit, Bank purse, bool temple)
    {
        if (!book.Treasury.Units.TryGetValue(unit.TypeId, out var costs)) return null;
        bool revive = Healing.CheckRevive(unit.Hp, purse, Treasury.RevivePrice(costs), temple, insideFort: true) == ReviveCheck.Allowed;
        int hpMax = session.HpMax(unit);
        bool heal = temple && unit.Hp > 0 && unit.Hp < hpMax && Treasury.Affordable(purse, costs.Heal) >= 1;
        return new AiCareUnit(revive, heal, Treasury.RevivePrice(costs), costs.Heal);
    }

    /// <summary>Исполнение ухода за юнитом (<c>0x449fcb</c>): воскрешение, если можно (<c>0x5de06e</c> → <c>0x41f1e6</c>), затем лечение,
    /// если можно (<c>0x5de32f</c> → <c>0x41f1af</c>). Успех — всегда (<c>mov al, 1</c>): шаг пишется и бюджет тратится.</summary>
    private static bool Care(GameSession session, string placeId, string unitId)
    {
        var map = session.Map;
        string fortId = map.Stacks.TryGetValue(placeId, out var s) ? s.Inside : placeId;
        if (session.Map.Units.TryGetValue(unitId, out var dead) && dead.Hp <= 0) session.Revive(fortId, unitId);
        if (session.Map.Units.TryGetValue(unitId, out var alive) && alive.Hp > 0) session.Heal(fortId, unitId);
        // 0x44a045: mov al, 1 — отказ лечения успеха не снимает; кандидатом юнит снова станет, только если построитель (те же проверки
        // CheckRevive и Affordable) его снова пропустит, а казна после траты бюджет уже уменьшила.
        return true;
    }

    /// <summary>Малый ближний боец (<c>0x44bec7</c> шаг 1): малый, атака по соседу, класс «урон».</summary>
    private static bool SmallMelee(LastEdict.Rules.Units.UnitImpl u) =>
        u.Type.SizeSmall && u.Attack is { Reach: LastEdict.Rules.Battle.AttackReach.Adjacent, Class: LastEdict.Rules.Battle.AttackClass.Damage };

    /// <summary>Список 4 (<c>0x446ccb</c>): доступные постройки лорда, выбор <see cref="AiStrategicRules.PickBuilding"/>
    /// (нет храма — сначала храм, магия, гильдия; иначе здание улучшения, у которого больше всего юнитов карты у
    /// порога опыта), допуск по бюджету — <see cref="AiStrategicRules.CapitalAllowed"/>. Опыт — после дневного чита
    /// (он уже начислен в начале хода).</summary>
    private static IReadOnlyList<AiStepAction> Capital(GameSession session, string playerId, AiBudget budget)
    {
        var book = session.Book!;
        var map = session.Map;
        var player = map.Players[playerId];
        // Порядок доступных (0x5f9988 → 0x447142) — порядок id: при равенстве счёта S2 строгий максимум 0x4476fc берёт первое по id —
        // 491 проход с ничьей по корпусу из 491 (BuildPick_OrderAndChoiceFromSaves, build_pick.tie; И113, ранг 2 — журнал).
        var lordList = session.Buildable(playerId);
        int maxUnit = map.Limits?.MaxUnit is > 0 and var max ? max : int.MaxValue;
        var available = AiStrategicRules.Available(map, playerId, lordList, book.Economy, maxUnit);
        bool temple = player.Buildings.Any(id => book.Economy.Buildings.TryGetValue(id, out var t) && t.Category == 1);
        var upgrades = UpgradeBuildingsBySource(book);
        // Выросший на месте юнит (UnitUpgrade.IsGrown) зданий улучшения не ждёт: его реализация порождённая, и PREV_ID на неё
        // не ссылается (0x5d7e07) — карта 4 корпуса 20261016b, UU0052 уровня 8 у порога: движок BB0050 не строит.
        var units = map.Units.Values.Select(u => new AiCapitalUnit(
            LastEdict.Rules.Units.UnitUpgrade.IsGrown(book.Rules.Units, u.TypeId, u.Level) ? [] : upgrades.GetValueOrDefault(u.TypeId, []), u.Xp,
            book.Rules.Units.TryGetValue(u.TypeId, out var t) ? t.XpNext : int.MaxValue)).ToList();
        var pick = AiStrategicRules.PickBuilding(available, temple, units, book.SideshowBuilding(player.RaceId));
        if (pick is null) return [];
        // 0x446e37 → getBuildingStatus 0x5dc9e8 с флагом IgnoresCost: у здания юнитов проверяются казна (статус 3, 0x5dca78)
        // и «сегодня уже строили» (статус 6, 0x5dca91) — без них порт строил при нуле золота и дважды за день.
        var status = Economy.CanBuild(map, playerId, pick.Id, book.Economy, ignoreCost: AiStrategicRules.IgnoresCost(pick),
                                      maxUnitLevel: maxUnit, builtThisDay: player.ConstructionTurn == map.Turn);
        return AiStrategicRules.CapitalAllowed(pick, status, budget)
            ? [new AiStepAction(AiStepType.Capital, pick.Id, pick.Id)]
            : [];
    }

    /// <summary>Здания улучшения, для которых тип — исходный (<c>0x44786e</c>): первый по файлу тип с этим
    /// <c>UPGRADE_B</c> и его <c>PREV_ID</c>; тип → здания.</summary>
    private static Dictionary<string, List<string>> UpgradeBuildingsBySource(GameBook book)
    {
        var bySource = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in book.Extras.Units.Values.Where(u => u.UpgradeBuildingId.Length > 0)
                                  .GroupBy(u => u.UpgradeBuildingId, StringComparer.OrdinalIgnoreCase))
        {
            string source = book.Rules.Units.TryGetValue(group.First().Id, out var t) ? t.PrevId : "";
            if (source.Length == 0) continue;
            if (!bySource.TryGetValue(source, out var list)) bySource[source] = list = [];
            list.Add(group.Key);
        }
        return bySource;
    }
}
