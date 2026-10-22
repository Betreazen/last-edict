using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Действия в укреплении — общие для окна города человека и экономики ИИ (<c>docs/ai-turn-plan.md</c> срез 3):
/// постройка в столице, найм в гарнизон, рост деревни, лечение и воскрешение. Проверки — правила движка
/// (<see cref="Economy.CanBuild"/>, <see cref="Economy.HireList"/>, <see cref="Fortifications.CheckGrow"/>,
/// <see cref="Healing"/>); отказ возвращается причиной, состояние тогда не меняется.</summary>
public sealed partial class GameSession
{
    /// <summary>Постройка в столице игрока: только из списка его лорда (<c>GbuiList</c>), дальше
    /// <see cref="Economy.CanBuild"/> (раз в день — <c>CONSTR_T</c>, потолок уровня сценария <c>MAX_UNIT</c>); цена
    /// <c>Gbuild.COST</c> списывается, день постройки запоминается.</summary>
    /// <exception cref="ArgumentException">Постройки нет в списке лорда игрока.</exception>
    /// <param name="ai">Постройка ИИ: движок зовёт проверку с <c>ignoreCost</c> (<c>0x5dc9e8</c>) — ни казны, ни «раз в
    /// день». Храм, магия и гильдия (<see cref="AiStrategicRules.IgnoresCost"/>) ещё и бесплатны: построитель кладёт этот признак
    /// в байт <c>+0xc</c> действия (<c>0x446e2a</c>, конструктор <c>0x447d0c</c>), исполнение <c>0x447d5c</c> передаёт его командой
    /// постройки (<c>0x41ea0f</c>), и цена не списывается. Ранг 1 по корпусу: 207 строк <c>build</c> с флагом 1 из 207 без строки
    /// <c>bank</c> списания (все — день 1, по три постройки у каждого ИИ, <c>AiTurnHarnessTests</c>); здания юнитов (флаг 0) — 77 из
    /// 77 со списанием. До 14.10 порт брал цену и здесь: ИИ начинал день 2 без золота и отставал в найме и постройках (Нежить
    /// в долгой партии порта к дню 20 — 2 победы над нейтралами против 10 у оригинала).</param>
    public BuildStatus Build(string playerId, string buildingId, bool ai = false)
    {
        var book = RequireBook();
        var map = Map;
        var player = map.Players[playerId];
        if (!Buildable(playerId).Contains(buildingId, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"постройки {buildingId} нет в списке лорда {player.LordId}", nameof(buildingId));
        var status = Economy.CanBuild(map, playerId, buildingId, book.Economy, ignoreCost: ai,
                                      maxUnitLevel: map.Limits?.MaxUnit is > 0 and var max ? max : int.MaxValue,
                                      builtThisDay: player.ConstructionTurn == map.Turn);
        if (status != BuildStatus.CanBeBuilt) return status;
        var building = book.Economy.Buildings[buildingId];
        bool free = ai && AiStrategicRules.IgnoresCost(building);
        SetPlayer(map, player with
        {
            Bank = free ? player.Bank : Treasury.Pay(player.Bank, building.Cost), Buildings = [.. player.Buildings, building.Id.ToUpperInvariant()],
            ConstructionTurn = map.Turn,
        });
        Observer?.Happened(new TurnSight(TurnSightKind.Built, playerId, 0, 0, building.Id));   // звук окна (сессия Г)
        return status;
    }

    /// <summary>Постройки лорда игрока (<c>GbuiList</c>) — всё, что его столица вообще может строить.</summary>
    public IReadOnlyList<string> Buildable(string playerId) =>
        RequireBook().LordBuildings.GetValueOrDefault(Map.Players[playerId].LordId) ?? [];

    /// <summary>Найм юнита в гарнизон своего укрепления: тип из списка найма игрока (<see cref="Economy.HireList"/>),
    /// место — по <see cref="AiStrategicRules.Placeable"/> (малому — пустая клетка, большому — пустая колонка;
    /// предел гарнизона деревни — её уровень, столицы — 6), цена <c>ENROLL_C</c> базового уровня. Клетка —
    /// <paramref name="cell"/> или первая подходящая. Юнит приходит базового уровня с полным здоровьем.</summary>
    /// <returns>Причина отказа; <c>null</c> — нанят.</returns>
    public string? HireUnit(string fortId, string unitType, int? cell = null)
    {
        if (HireUnitCheck(fortId, unitType, cell) is { } refused) return refused;
        var book = RequireBook();
        var map = Map;
        var fort = SiteOf(map, fortId);
        var player = map.Players[fort.Owner];
        var type = book.Rules.Units[unitType];
        var hire = book.Economy.Hire[unitType];
        var cost = Economy.HireCost(unitType, book.Economy);
        var cells = fort.GuardSlots.Count == Formation.Cells ? fort.GuardSlots.ToList() : [.. Formation.EmptySlots()];
        bool big = !type.SizeSmall;
        int at = cell ?? FirstFit(cells, big);
        var grown = book.Grown(unitType, hire.Level)!;
        var ids = map.IdNext;
        string id = NewId(ref ids, map.Units.Keys, fortId, "UN");
        int index = fort.Guards.Count;
        cells[at] = index;
        if (big) cells[at + 1] = index;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[id] = new MapUnit { Id = id, TypeId = type.Id.ToUpperInvariant(), Level = grown.Level, Hp = grown.Type.HitPoints };
        var sites = map.Sites.Select(s => ReferenceEquals(s, fort) ? s with { Guards = [.. s.Guards, id], GuardSlots = cells } : s).ToList();
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = Treasury.Pay(player.Bank, cost) };
        State = State with { Looks = Retold(Looks, With(map, units: units, players: players, sites: sites, idNext: ids), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Bought, player.Id, 0, 0, id));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>Проверка найма юнита без найма — <c>hire_check 0x5deea7</c> (коды движка: 8 — не в списке найма, 7 — казна меньше
    /// <c>ENROLL_C</c>, 10 — нет места, 11 — клетка занята): её зовут и окно найма (<c>0x5d5683</c>), и оценка списка 2 стратегии ИИ
    /// (<c>0x44c3de</c>, <see cref="AiEconomy"/>). Правила — как у <see cref="HireUnit"/>.</summary>
    /// <returns>Причина отказа; <c>null</c> — нанять можно.</returns>
    public string? HireUnitCheck(string fortId, string unitType, int? cell = null)
    {
        var book = RequireBook();
        var map = Map;
        var fort = SiteOf(map, fortId);
        if (fort.Kind is not (SiteKind.Capital or SiteKind.Village)) return $"{fortId} — не укрепление";
        var player = map.Players[fort.Owner];
        if (!Economy.HireList(map, player.Id, book.Economy).Contains(unitType, StringComparer.OrdinalIgnoreCase))
            return $"{unitType} нет в списке найма {player.Id}";
        if (!book.Rules.Units.TryGetValue(unitType, out var type) || !book.Economy.Hire.ContainsKey(unitType))
            return $"{unitType} нет в правилах";
        if (Economy.NotEnough(player.Bank, Economy.HireCost(unitType, book.Economy))) return "не хватает казны";
        IReadOnlyList<int> cells = fort.GuardSlots.Count == Formation.Cells ? fort.GuardSlots : Formation.EmptySlots();
        int capacity = fort.Kind == SiteKind.Capital ? 6 : Fortifications.GarrisonCapacity(fort.Size ?? 1);
        bool big = !type.SizeSmall;
        if (!AiStrategicRules.Placeable(cells, capacity - cells.Count(c => c >= 0), big)) return "в гарнизоне нет места";
        int at = cell ?? FirstFit(cells, big);
        if (at < 0 || cells[at] >= 0 || (big && (at % 2 != 0 || cells[at + 1] >= 0))) return $"клетка {at} занята";
        return null;
    }

    /// <summary>
    /// Найм лидера в укреплении (<c>0x41ef71</c>; у человека — окно столицы): тип из списка лидеров расы
    /// (<c>Grace.LEADER_1..4</c>, <c>NOBLE</c>), в укреплении нет гостя, хватает казны на <c>ENROLL_C</c>. Появляется новый
    /// отряд-гость: лидер своего уровня с полным здоровьем, приказ <c>L_NORMAL</c>, приказ ИИ
    /// <paramref name="aiOrder"/>, полный дневной запас хода: нанятый лидер ходит в тот же день (журналы: первый шаг нового
    /// отряда — 57 из 65 и 39 из 45 наймов в день найма, <c>ap_restore</c> с запасом «до» = наибольшему; строка
    /// <c>stack_move</c> самой постановки пишет 0 — запас выставляется после неё).
    /// Клетка лидера — <see cref="LeaderSlots"/> (сервер <c>0x5d6957</c> → <c>0x5e6e43</c> → <c>CVisitorCreateStackInside</c>).
    /// ponytail: проверка <c>0x5de921</c> (в журнале всегда 0) не делается.
    /// </summary>
    /// <returns>Причина отказа; <c>null</c> — нанят.</returns>
    public string? HireLeader(string fortId, string leaderType, StackOrder aiOrder = StackOrder.Normal, string name = "")
    {
        var book = RequireBook();
        var map = Map;
        var fort = SiteOf(map, fortId);
        if (fort.Kind is not (SiteKind.Capital or SiteKind.Village)) return $"{fortId} — не укрепление";
        if (fort.StackId.Length > 0) return $"в {fortId} уже стоит отряд {fort.StackId}";
        var player = map.Players[fort.Owner];
        if (!(book.RaceLeaders.GetValueOrDefault(player.RaceId)?.Contains(leaderType, StringComparer.OrdinalIgnoreCase) ?? false))
            return $"{leaderType} нет в списке лидеров расы {player.RaceId}";
        if (!book.Rules.Units.TryGetValue(leaderType, out var type) || book.Grown(leaderType, type.Level) is not { } grown)
            return $"{leaderType} нет в правилах";
        var cost = Economy.HireCost(leaderType, book.Economy);
        if (Economy.NotEnough(player.Bank, cost)) return "не хватает казны";
        var ids = map.IdNext;
        string unitId = NewId(ref ids, map.Units.Keys, fortId, "UN");
        string stackId = NewId(ref ids, map.Stacks.Keys, fortId, "KC");
        int[] slots = LeaderSlots(grown.Type, book.Rules);
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = new MapUnit { Id = unitId, TypeId = type.Id.ToUpperInvariant(), Level = grown.Level, Hp = grown.Type.HitPoints };
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = new MapStack
        {
            Id = stackId, Owner = player.Id, Subrace = fort.Subrace, LeaderId = unitId, LeaderAlive = true, X = fort.X, Y = fort.Y,
            Inside = fort.Id, UnitIds = [unitId], Slots = slots, Order = (int)StackOrder.Normal, AiOrder = (int)aiOrder,
        };
        stacks[stackId] = stacks[stackId] with { Move = Allowance(With(map, units: units), stacks[stackId]) };
        var sites = map.Sites.Select(x => ReferenceEquals(x, fort) ? x with { StackId = stackId } : x).ToList();
        // Отряд внутри укрепления стоит и в плане своей клетки, как на карте (все сохранения корпуса — MapInvariantsTests); до 13.10 найм
        // его в план не клал — нашла проверка инвариантов зонда (код plan, Power to Corrupt день 6).
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[(fort.X, fort.Y)] = [.. plan.GetValueOrDefault((fort.X, fort.Y)) ?? [], stackId];
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = Treasury.Pay(player.Bank, cost) };
        var look = new LastEdict.Data.Map.StackLook(stackId, fort.X, fort.Y, player.Id, fort.Subrace, type.Id.ToUpperInvariant(),
                                             LeaderAlive: true, AnyAlive: true, Facing: 4, Inside: fort.Id, Invisible: false, LeaderName: name);
        State = State with
        {
            Looks = Retold(Looks, With(map, units: units, stacks: stacks, players: players, sites: sites, plan: plan, idNext: ids),
                          [.. Looks.Stacks, look]),
        };
        Observer?.Happened(new TurnSight(TurnSightKind.Hire, player.Id, fort.X, fort.Y, stackId));
        return null;
    }

    /// <summary>
    /// Клетки нового отряда из одного лидера (ранг 1 по листингу, 28.09): <c>CMidStack</c> слот 1 (<c>0x5ebe7a</c>, vtable <c>0x6eea34</c>)
    /// ставит лидера в клетку 3, а в клетку 2 — если первая атака типа ближняя (<c>IUsSoldier +0x48</c> → <c>IAttack::getAttackReach</c>
    /// <c>+0x1c</c> = <c>LAttR</c> <c>L_ADJACENT</c>, <c>[0x8380b8]</c>) или воин большой (<c>getSizeSmall</c> <c>+0x18</c> ложно; занимает 2 и 3).
    /// Через создание отряда <c>0x60cffa</c> → <c>0x60d0cd</c> идут найм лидера (<c>0x5d6957</c> → <c>0x5e6e43</c> →
    /// <c>CVisitorCreateStackInside</c> <c>0x60e7fe</c>, apply <c>0x60e8da</c>), призыв (<c>CVisitorCreateStack</c> <c>0x60ceaf</c>) и отряд пустой
    /// столицы (<see cref="NewGame"/>). Прежде порт ставил всех в клетку 2 — по сохранениям, где ИИ успевал расставить отряд.
    /// </summary>
    internal static int[] LeaderSlots(LastEdict.Rules.Battle.UnitType type, LastEdict.Rules.Battle.GameRules rules)
    {
        bool front = !type.SizeSmall || rules.Attacks.TryGetValue(type.AttackId, out var attack) && attack.Reach == LastEdict.Rules.Battle.AttackReach.Adjacent;
        int[] slots = Formation.EmptySlots();
        slots[front ? 2 : 3] = 0;
        if (!type.SizeSmall) slots[3] = 0;
        return slots;
    }

    /// <summary>Первая подходящая клетка: малому — пустая, большому — передняя клетка пустой колонки.</summary>
    private static int FirstFit(IReadOnlyList<int> cells, bool big)
    {
        for (int i = 0; i < cells.Count; i += big ? 2 : 1)
            if (cells[i] < 0 && (!big || cells[i + 1] < 0)) return i;
        return -1;
    }

    /// <summary>Рост деревни на уровень (<see cref="Fortifications.CheckGrow"/>: не в бунт, не выше 5, раз в день,
    /// хватает казны); цена — <see cref="Treasury.CityGrowthPrice"/> (гильдмастеру вдвое дешевле).</summary>
    public CityGrowCheck GrowCity(string villageId)
    {
        var book = RequireBook();
        var map = Map;
        var village = SiteOf(map, villageId);
        var player = map.Players[village.Owner];
        var price = Treasury.CityGrowthPrice(book.Economy, player.RaceId, village.Size ?? 1, book.LordCategory(player.LordId));
        var check = Fortifications.CheckGrow(village.Size ?? 1, village.RiotTurn ?? 0, village.GrowthTurn ?? 0, map.Turn,
                                             player.Bank, price);
        if (check != CityGrowCheck.Allowed) return check;
        var sites = map.Sites.Select(s => ReferenceEquals(s, village) ? Fortifications.Grow(s, map.Turn) : s).ToList();
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = Treasury.Pay(player.Bank, price) };
        var next = With(map, players: players, sites: sites);
        State = State with
        {
            Looks = Retold(Looks, next, Looks.Stacks) with
            {
                Forts = [.. Looks.Forts.Select(f => f.Id.Equals(villageId, StringComparison.OrdinalIgnoreCase)
                                                    ? f with { Level = f.Level + 1 } : f)],
            },
        };
        Observer?.Happened(new TurnSight(TurnSightKind.CityGrown, player.Id, village.X, village.Y, village.Id));   // звук (сессия Г)
        return check;
    }

    /// <summary>Можно ли игроку исследовать заклинание (<c>0x604039</c>): оно в списке лорда (<c>GspellR</c>), ещё не
    /// известно, сегодня исследований не было (<c>RESEAR_T</c>) и хватает казны на цену
    /// (<see cref="Treasury.SpellResearchPrice"/>: магу вдвое дешевле).
    /// ponytail: уровень заклинания против магических построек столицы не проверяется — ИИ его не проверяет
    /// (корпус: выбор совпадает без него), а правило окна человека не сверено.</summary>
    /// <returns>Причина отказа и цена; причина <c>null</c> — можно.</returns>
    public (string? Refused, Bank Price) ResearchCheck(string playerId, string spellId)
    {
        var book = RequireBook();
        var player = Map.Players[playerId];
        var row = (book.LordResearch.GetValueOrDefault(player.LordId) ?? [])
            .FirstOrDefault(x => x.SpellId.Equals(spellId, StringComparison.OrdinalIgnoreCase));
        if (row.SpellId is null) return ($"{spellId} нет в списке лорда {player.LordId}", Bank.Zero);
        var price = Treasury.SpellResearchPrice(row.Price, book.LordCategory(player.LordId));
        if (player.KnownSpells.Contains(spellId, StringComparer.OrdinalIgnoreCase)) return ("уже известно", price);
        if (player.ResearchTurn >= Map.Turn && Map.Turn > 0) return ("сегодня уже исследовали", price);
        if (Economy.NotEnough(player.Bank, price)) return ("не хватает казны", price);
        return (null, price);
    }

    /// <summary>Исследование заклинания (<c>0x41e915</c>; у человека — окно столицы): <see cref="ResearchCheck"/>, цена
    /// списывается, заклинание становится известным, день запоминается.</summary>
    /// <returns>Причина отказа; <c>null</c> — изучено.</returns>
    public string? ResearchSpell(string playerId, string spellId)
    {
        var (refused, price) = ResearchCheck(playerId, spellId);
        if (refused is not null) return refused;
        var map = Map;
        var player = map.Players[playerId];
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with
        {
            Bank = Treasury.Pay(player.Bank, price), ResearchTurn = map.Turn,
            KnownSpells = [.. player.KnownSpells, spellId.ToUpperInvariant()],
        };
        State = State with { Looks = Retold(Looks, With(map, players: players), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Researched, playerId, 0, 0, spellId));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>Лечение юнита в укреплении (<see cref="Healing.Heal"/>): <c>HEAL_C</c> за недостающий HP, пока хватает
    /// казны; нужен Храм у игрока. Юнит — в гарнизоне или в отряде-визитёре.</summary>
    /// <returns>Причина отказа; <c>null</c> — вылечен (может быть частично).</returns>
    public string? Heal(string fortId, string unitId) => Care(fortId, unitId, revive: false);

    /// <summary>Воскрешение павшего юнита в укреплении (<see cref="Healing.Revive"/>): цена <c>REVIVE_C</c>, 1 HP.</summary>
    public string? Revive(string fortId, string unitId) => Care(fortId, unitId, revive: true);

    private string? Care(string fortId, string unitId, bool revive)
    {
        var book = RequireBook();
        var map = Map;
        var fort = SiteOf(map, fortId);
        bool inside = fort.Guards.Contains(unitId, StringComparer.OrdinalIgnoreCase)
                      || (fort.StackId.Length > 0 && map.Stacks.TryGetValue(fort.StackId, out var visitor)
                          && visitor.UnitIds.Contains(unitId, StringComparer.OrdinalIgnoreCase));
        if (!inside) return $"{unitId} не в {fortId}";
        var player = map.Players[fort.Owner];
        var unit = map.Units[unitId];
        if (!book.Treasury.Units.TryGetValue(unit.TypeId, out var costs)) return $"нет цен типа {unit.TypeId}";
        bool temple = Healing.HasTemple(player.Buildings, book.Economy.Buildings);
        HealResult result;
        if (revive) result = Healing.Revive(unit.Hp, Treasury.RevivePrice(costs), player.Bank, temple, insideFort: true);
        else result = Healing.Heal(unit.Hp, HpMax(unit), costs.Heal, player.Bank, temple, insideFort: true);
        if (result.Refused is { } why) return why;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = unit with { Hp = result.Hp };
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = result.Purse };
        var next = With(map, units: units, players: players);
        if (revive) next = LeaderRevived(next, unitId);
        State = State with { Looks = Retold(Looks, next, Looks.Stacks) };
        Observer?.Happened(new TurnSight(revive ? TurnSightKind.Revived : TurnSightKind.Healed, player.Id, 0, 0, unitId));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>Максимум здоровья юнита: тип, выращенный до уровня, с цепочкой модификаторов (как в бою).</summary>
    public int HpMax(MapUnit unit)
    {
        var book = RequireBook();
        if (book.Grown(unit.TypeId, unit.Level) is not { } grown) return unit.Hp;
        var chain = unit.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        return EffectiveStats.Of(grown.Type, chain).HitPoints;
    }

    /// <summary>Воскрешённый лидер снова жив в своём отряде (<c>LEADR_ALIV</c>).</summary>
    private static MapState LeaderRevived(MapState map, string unitId)
    {
        var stack = map.Stacks.Values.FirstOrDefault(s => s.LeaderId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
        if (stack is null || stack.LeaderAlive) return map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with { LeaderAlive = true };
        return With(map, stacks: stacks);
    }

    private void SetPlayer(MapState map, MapPlayer player)
    {
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player;
        State = State with { Looks = Retold(Looks, With(map, players: players), Looks.Stacks) };
    }

    private static MapSite SiteOf(MapState map, string id) =>
        map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"объекта {id} нет на карте");

    internal GameBook RequireBook() => Book ?? throw new InvalidOperationException("партия без правил");
}
