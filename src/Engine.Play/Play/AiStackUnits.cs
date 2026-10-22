using D2.Rules.Battle;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>
/// Подготовка отряда ИИ, часть «юниты» (<c>docs/ai-items.md</c> §6): в крепости — юниты гарнизона в отряд (<c>0x451f5a</c> →
/// команда <c>StackReorder</c> <c>0x41ef3a</c> → сервер <c>0x5d5d73</c>) и лечение с воскрешением за казну (<c>0x451d74</c>),
/// вне крепости — расстановка (<c>0x451fc4</c> → <c>0x41ef06</c> → сервер <c>0x5d57de</c>). Нейтралам (<c>0x5e5358</c>) ничего
/// из этого нет. Ранг 1 — листинг; сверка — <c>AiStackItemsHarnessTests</c>.
/// </summary>
public static class AiStackUnits
{
    /// <summary>Уровень сценария, с которого жезлоносец не берёт из гарнизона юнитов выше первого уровня (<c>0x61eb31</c>).</summary>
    public const int HighSuggestedLevel = 10;

    /// <summary>Юнит для перестановки (<c>0x5d58dd</c>): сила <c>0x5dd06c(юнит, 0, 0, 0, 3, 0)</c> — без брони крепости,
    /// три врага, павший — 0; ряд по размеру и первой атаке; роль по классу атаки и ветке. Атака — с модификаторами юнита
    /// (движок читает его через обёртки).</summary>
    public static ArrangeUnit Describe(GameSession session, string unitId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var rules = session.RequireBook().Rules;
        var unit = MapBattle.Unit(session.Map.Units[unitId], 0, rules, isAttacker: false);
        var attack = unit.Modifiers.Count == 0 ? unit.Attack : EffectiveStats.OfAttack(unit.Attack, unit.Modifiers).Attack;
        var line = !unit.Type.SizeSmall ? ArrangeLine.Big
            : attack.Reach == AttackReach.Adjacent && attack.Class == AttackClass.Damage ? ArrangeLine.Melee
            : ArrangeLine.Back;
        var role = !attack.Effect.IsDamaging ? ArrangeRole.Support
            : unit.Type.Branch switch { UnitBranch.Sideshow => ArrangeRole.Sideshow, UnitBranch.Archer => ArrangeRole.Archer, _ => ArrangeRole.Other };
        double score = AiTactics.UnitsStrength(session, [unitId], ignoreCity: false, enemies: 3, fortArmor: 0);
        return new ArrangeUnit(unitId, score, line, role);
    }

    /// <summary>Расстановка группы (<c>0x5d57de</c>, <see cref="GroupArrange.Layout"/>): у группы из одного юнита — ничего.
    /// Юнит, которому не нашлось клетки, расстановку отменяет (движок не меняет группу, если <c>0x5e7991</c> отказал).</summary>
    public static void Arrange(GameSession session, string groupId, bool center)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.GroupCells(groupId) is not { } group || group.Units.Count == 1) return;
        var cells = GroupArrange.Layout(group.Units.Select(id => Describe(session, id)), center);
        var placed = new HashSet<string>(cells.Where(c => c.Length > 0), StringComparer.OrdinalIgnoreCase);
        if (placed.Count != group.Units.Count) return;
        session.SetCells(groupId, group with { Cells = cells });
    }

    /// <summary>
    /// Юниты гарнизона в отряд (<c>0x5d5d73</c>): отряд стоит в укреплении. Очередь — <see cref="GroupArrange.Queue"/> из
    /// юнитов отряда без лидера и гарнизона без стражей (<c>0x5e7a1d</c> с <c>apply = 0</c>); приказ — действующий
    /// (<see cref="AiTactics.OrderOf"/>). Отряд расставляется без центровки, затем каждый в очереди по порядку —
    /// <see cref="Place"/> и уходит из очереди; в конце гарнизон и отряд — с центровкой.
    /// </summary>
    public static void TakeFromGarrison(GameSession session, string stackId, List<string> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);
        var map = session.Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack) || stack.Inside.Length == 0) return;
        var fort = map.Sites.FirstOrDefault(s => Same(s.Id, stack.Inside) && s.Kind is SiteKind.Capital or SiteKind.Village);
        if (fort is null) return;
        var book = session.RequireBook();
        var removable = fort.Guards.Where(id => map.Units.TryGetValue(id, out var u) && book.CategoryOf(u.TypeId) != UnitCategory.Guardian);
        var order = AiTactics.OrderOf(stack).Order;
        var queue = GroupArrange.Queue(stack.UnitIds.Where(id => !Same(id, stack.LeaderId)).Concat(removable)
                                                    .Select(id => Describe(session, id)).ToList(),
                                       order is StackOrder.Assist or StackOrder.DefendCity);
        Arrange(session, stackId, center: false);
        bool rod = session.LeaderAbilitiesOf(stack).Contains(Rods.LeaderAbilityRod);
        bool high = session.State.Info.SuggestedLevel >= HighSuggestedLevel;
        while (queue.Count > 0)
        {
            Place(session, queue, stackId, fort.Id, rod, high, report);
            queue.RemoveAt(0);
        }
        Arrange(session, fort.Id, center: true);
        Arrange(session, stackId, center: true);
    }

    /// <summary>
    /// Один юнит очереди (<c>0x5d6152</c>), первый в <paramref name="queue"/>. У жезлоносца (способность <c>L_ROD</c>,
    /// <c>0x5d6469</c>) — не поддержку и, при высоком уровне сценария, не выше первого уровня. Юнит уже в отряде — ничего.
    /// Из гарнизона — в свой ряд отряда (большой и ближнего боя — передний, прочие — задний): в первую пустую клетку, иначе
    /// обменом с юнитом клетки, который ещё в очереди (слабее — очередь по силе), иначе (только задний ряд) обменом с юнитом
    /// переднего ряда из очереди, вдвое более слабым. Каждая клетка — с проверкой обмена
    /// (<see cref="GameSession.SwapRefusal"/>). После обмена отряд расставляется без центровки.
    /// </summary>
    private static void Place(GameSession session, List<ArrangeUnit> queue, string stackId, string fortId, bool rod, bool high,
                              List<string> report)
    {
        var rec = queue[0];
        if (rod && (rec.Role == ArrangeRole.Support || (high && session.Map.Units[rec.Id].Level > 1))) return;
        if (session.GroupCells(stackId) is not { } stack || stack.PositionOf(rec.Id) >= 0) return;
        int pos = session.GroupCells(fortId)?.PositionOf(rec.Id) ?? -1;
        if (pos < 0) return;
        var row = rec.Line is ArrangeLine.Big or ArrangeLine.Melee ? Formation.Front : Formation.Back;
        bool Can(int cell) => session.SwapRefusal(fortId, pos, stackId, cell) is null;
        ArrangeUnit? Queued(int cell) => queue.FirstOrDefault(q => Same(q.Id, stack.Cells[cell]));
        int target = row.FirstOrDefault(c => stack.Cells[c].Length == 0 && Can(c), -1);
        if (target < 0) target = row.FirstOrDefault(c => stack.Cells[c].Length > 0 && Queued(c) is not null && Can(c), -1);
        if (target < 0 && rec.Line == ArrangeLine.Back)
            target = Formation.Front.FirstOrDefault(c => stack.Cells[c].Length > 0 && Queued(c) is { } x && x.Score * 2 < rec.Score && Can(c), -1);
        if (target < 0) return;
        string other = stack.Cells[target];
        if (session.SwapUnits(fortId, pos, stackId, target) is { } why)
        {
            report.Add($"перенос {fortId}[{pos}] {rec.Id} → {stackId}[{target}] отказ: {why}");
            return;
        }
        report.Add($"перенос {fortId}[{pos}] {rec.Id} → {stackId}[{target}] {(other.Length > 0 ? other : "-")}");
        Arrange(session, stackId, center: false);
    }

    /// <summary>
    /// Лечение в крепости за казну (<c>0x451d74</c>): по юнитам отряда по порядку — павшим воскрешение (<c>0x5de06e</c> → команда
    /// <c>0x41f1e6</c>), затем раненым лечение (<c>0x5de32f</c> → <c>0x41f1af</c>); действия и проверки —
    /// <see cref="GameSession.Revive"/>, <see cref="GameSession.Heal"/>.
    /// <para>Из кошелька тактики (<c>CAiTactic +0x515c</c>, <see cref="AiTacticTurn.Purse"/>; читает его построитель тренера)
    /// вычитается (<c>0x586578</c>) только золото: у воскрешения — <c>REVIVE_C</c> (<c>0x5de873</c>, слот <c>0x58</c>), а у
    /// лечения цена считается <b>после</b> команды (<c>0x5de299</c> = <c>HEAL_C</c> × здоровье, которое казна ещё оплатит, у
    /// вылеченного — 0), так что лечение кошелёк не трогает. Ошибка оригинала, но так в листинге и в журналах: 152 окна с
    /// тренером human и legions сходятся с кошельком «70 % казны минус воскрешения и обучение», а с вычетом лечения — нет
    /// (день 45 human: лечение на 149 при кошельке 105, тренер всё равно предложен).</para>
    /// </summary>
    public static void CareInFort(GameSession session, string stackId, List<string> report, AiTacticTurn? turn = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);
        if (!session.Map.Stacks.TryGetValue(stackId, out var stack) || stack.Inside.Length == 0) return;
        string fortId = stack.Inside;
        if (!session.Map.Sites.Any(s => Same(s.Id, fortId) && s.Kind is SiteKind.Capital or SiteKind.Village)) return;
        foreach (string id in stack.UnitIds)
            if (session.Map.Units.TryGetValue(id, out var unit) && unit.Hp <= 0 && session.Revive(fortId, id) is null)
            {
                report.Add($"воскрешение {id}");
                if (turn is not null && session.ReviveRate(id) is { } price) turn.Spend(Bank.Zero with { Gold = price.Gold });
            }
        foreach (string id in stack.UnitIds)
            if (session.Map.Units.TryGetValue(id, out var unit) && unit.Hp < session.HpMax(unit) && session.Heal(fortId, id) is null)
                report.Add($"лечение {id}");
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
