using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>
/// Подготовка отряда ИИ к оценке — слот <c>0x44</c> обёртки отряда (<c>docs/ai-items.md</c>): у бойца и нейтрала
/// <c>0x463863</c>, у вора и жезлоносца <c>0x464562</c>, у призыва нет. Движок зовёт её в начале хода тактики для всех
/// отрядов очереди по порядку (<c>0x465984</c>, после помощников и очереди) и в шаге выбора отряда (0 → 1,
/// <c>0x466b68</c>) — при каждом выборе, и повторном тоже. Порядок: в крепости — склад (боец и нейтрал забирают его,
/// <c>0x45193d</c>; вор и жезлоносец сдают на него сумку, <c>0x451a43</c>), юниты гарнизона в отряд и, если столица хозяина
/// не взята (<c>0x5e4e73</c>), лечение (<see cref="AiStackUnits"/>); вне крепости — расстановка; затем зелья
/// (<c>0x451b45</c>) и снаряжение (<c>0x451ff3</c>). Ранг 1 — листинг.
/// </summary>
public static class AiStackItems
{
    /// <summary>Живой юнит пьёт лечебное зелье, если здоровье ниже этой доли наибольшего (<c>[0x6d02f0]</c>,
    /// сравнение <c>hpMax·0,75 &gt; hp</c>).</summary>
    public const double HealBelow = 0.75;

    /// <summary>Подготовка отряда. Отчёт получает строки «зелье …», «надеть …», «снять …», «склад …».</summary>
    public static void Prepare(GameSession session, string stackId, List<string> report, AiTacticTurn? turn = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);
        if (!session.Map.Stacks.TryGetValue(stackId, out var stack)) return;
        var kind = AiTactics.KindOf(session, stack);
        if (kind == AiStackKind.Summon) return;
        bool inside = stack.Inside.Length > 0;
        if (kind is AiStackKind.Fighter or AiStackKind.Neutral && inside)
        {
            int before = stack.Items.Count;
            session.TakeFortItems(stackId);
            if (session.Map.Stacks[stackId].Items.Count > before) report.Add($"склад {stackId} {stack.Inside}");
        }
        else if (kind is AiStackKind.Thief or AiStackKind.Rod && inside && stack.Items.Count > 0)
        {
            session.GiveFortItems(stackId);
            if (session.Map.Stacks[stackId].Items.Count == 0) report.Add($"сдать склад {stackId} {stack.Inside}");
        }
        if (!AiTactics.NeutralOwner(session, stack.Owner))
        {
            if (!inside) AiStackUnits.Arrange(session, stackId, center: true);
            else
            {
                AiStackUnits.TakeFromGarrison(session, stackId, report);
                bool capitalTaken = session.Map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0;
                if (!capitalTaken) AiStackUnits.CareInFort(session, stackId, report, turn);
            }
        }
        Potions(session, stackId, report);
        Equip(session, stackId, report);
    }

    /// <summary>
    /// Зелья (<c>0x451b45</c>): при непустой сумке — по юнитам группы по порядку: павшему — первое в сумке зелье
    /// воскрешения (<c>0x451c50</c>); затем, если юнит жив (в том числе только что воскрешён, здоровье 1) и ниже
    /// <see cref="HealBelow"/> наибольшего здоровья, — первое лечебное (<c>0x451d2d</c>). Журнал human, день 6: воскрешение
    /// и лечение одного юнита подряд. Команда <c>0x41f172</c>; ворота зелья проверяет сервер
    /// (<see cref="GameSession.DrinkPotion"/>).
    /// </summary>
    public static void Potions(GameSession session, string stackId, List<string> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);
        if (!session.Map.Stacks.TryGetValue(stackId, out var stack) || stack.Items.Count == 0) return;
        foreach (string unitId in stack.UnitIds.ToList())
        {
            if (!session.Map.Units.TryGetValue(unitId, out var unit)) continue;
            if (unit.Hp <= 0) Drink(session, stackId, unitId, ItemCategory.PotionRevive, report);
            if (!session.Map.Units.TryGetValue(unitId, out unit) || unit.Hp <= 0) continue;
            if (session.HpMax(unit) * HealBelow > unit.Hp) Drink(session, stackId, unitId, ItemCategory.PotionHeal, report);
        }
    }

    private static void Drink(GameSession session, string stackId, string unitId, ItemCategory category, List<string> report)
    {
        if (First(session, session.Map.Stacks[stackId], category) is not { } item) return;
        string? why = session.DrinkPotion(stackId, item, unitId);
        report.Add(why is null ? $"зелье {stackId} {item} → {unitId}" : $"зелье {stackId} {item} → {unitId} отказ: {why}");
    }

    /// <summary>Первое в сумке зелье категории (<c>0x451c97</c>: обход сумки по порядку).</summary>
    private static string? First(GameSession session, MapStack stack, ItemCategory category) =>
        stack.Items.FirstOrDefault(i => session.ItemOf(i)?.Category == category);

    /// <summary>
    /// Снаряжение (<c>0x451ff3</c>): при непустой сумке — по семи слотам лидера. Оценка надетого
    /// (<see cref="Score"/>); надето, но оценка 0 — снять (<c>0x5e746d</c>). Среди предметов сумки, что не надеты
    /// (<c>0x5ec119</c>) и годятся в слот (<see cref="Equipment.Check"/> = <c>0x5df6b7</c>), — наибольшая оценка,
    /// строго больше текущей; нашёлся другой — надеть (<c>0x41f0fe</c>). После слота 2 (первый боевой): он пуст —
    /// слот 3 пропускается; занят — его категория идёт в оценку слота 3.
    /// </summary>
    public static void Equip(GameSession session, string stackId, List<string> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);
        if (!session.Map.Stacks.TryGetValue(stackId, out var stack) || stack.Items.Count == 0) return;
        var book = session.RequireBook();
        ItemCategory? previous = null;
        for (int slot = 0; slot < Equipment.SlotCount; slot++)
        {
            stack = session.Map.Stacks[stackId];
            string current = Equipment.Slots(stack)[slot];
            string chosen = current;
            int best = Score(session, stack, current, slot, previous);
            if (current.Length > 0 && best == 0)
            {
                session.UnequipItem(stackId, slot);
                report.Add($"снять {stackId} слот {slot} {current}");
                stack = session.Map.Stacks[stackId];
            }
            bool alive = session.Map.Units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0;
            var abilities = session.LeaderAbilitiesOf(stack);
            var has = session.LeaderEffectsOf(stack);
            foreach (string id in stack.Items)
            {
                if (stack.Equipment.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                if (session.ItemOf(id) is not { } item
                    || Equipment.Check(item, slot, abilities, alive, book.Rules.Modifiers, has) != EquipRefusal.Ok) continue;
                int score = Score(session, stack, id, slot, previous);
                if (score > best) (best, chosen) = (score, id);
            }
            if (!Same(chosen, current) && chosen.Length > 0)
            {
                var refusal = session.EquipItem(stackId, chosen, slot);
                report.Add($"надеть {stackId} слот {slot} {chosen}{(refusal == EquipRefusal.Ok ? "" : " отказ: " + refusal)}");
            }
            if (slot == Equipment.SlotBattle1)
            {
                string first = Equipment.Slots(session.Map.Stacks[stackId])[slot];
                if (first.Length == 0) slot = Equipment.SlotBattle2;
                else previous = session.ItemOf(first)?.Category;
            }
        }
    }

    /// <summary>
    /// Оценка предмета в слоте (<c>0x45224b</c>). Пусто — 0; не боевой слот — 1. Боевые (2, 3): годны только лечебное
    /// зелье, воскрешение, талисман и сфера (<c>0x5df939</c>), иначе 0. Слот 2 (<paramref name="previous"/> пусто):
    /// талисман — цена в золоте + 40000, сфера — + 30000 (обе — 0, если их атака не сильнее атаки лидера,
    /// <see cref="WeakerThanLeader"/>), лечебное — сколько лечит + 20000, воскрешение — 10000. Слот 3 после талисмана или
    /// сферы: лечебное + 40000, воскрешение 30000, талисман + 20000, сфера + 10000; после зелья: лечебное + 40000,
    /// воскрешение 30000, прочее 0; после прочего — 0.
    /// </summary>
    public static int Score(GameSession session, MapStack stack, string itemId, int slot, ItemCategory? previous)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        if (itemId.Length == 0 || session.ItemOf(itemId) is not { } item) return 0;
        if (slot is not (Equipment.SlotBattle1 or Equipment.SlotBattle2)) return 1;
        int gold = (int)item.Value.Gold;
        int heal = item.HealHitPoints;
        bool weak = item.Category is ItemCategory.Talisman or ItemCategory.Orb && WeakerThanLeader(session, stack, item);
        return (previous, item.Category) switch
        {
            (null, ItemCategory.Talisman) => weak ? 0 : gold + 40000,
            (null, ItemCategory.Orb) => weak ? 0 : gold + 30000,
            (null, ItemCategory.PotionHeal) => heal + 20000,
            (null, ItemCategory.PotionRevive) => 10000,
            (ItemCategory.Talisman or ItemCategory.Orb or ItemCategory.PotionHeal or ItemCategory.PotionRevive, ItemCategory.PotionHeal) => heal + 40000,
            (ItemCategory.Talisman or ItemCategory.Orb or ItemCategory.PotionHeal or ItemCategory.PotionRevive, ItemCategory.PotionRevive) => 30000,
            (ItemCategory.Talisman or ItemCategory.Orb, ItemCategory.Talisman) => weak ? 0 : gold + 20000,
            (ItemCategory.Talisman or ItemCategory.Orb, ItemCategory.Orb) => weak ? 0 : gold + 10000,
            _ => 0,
        };
    }

    /// <summary>
    /// Талисман или сфера не нужны (<c>0x452475</c>): атака предмета из классов с уроном (урон, высасывание, избыток,
    /// яд, обморожение, ожог) и её урон (<c>QTY_DAM</c>, ×3 «по всем») не больше оценки урона лидера
    /// (<see cref="AiTargets.DamageEstimate(BattleUnit, RulesConstants, GameRules?)"/> = <c>0x5d2f3d</c>, ×3, если его
    /// атака «по всем», <c>[0x8380c8]</c>). Атака другого класса — предмет нужен.
    /// </summary>
    public static bool WeakerThanLeader(GameSession session, MapStack stack, Item item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(item);
        var rules = session.RequireBook().Rules;
        if (!rules.Attacks.TryGetValue(item.AttackId, out var attack) || !attack.Effect.IsDamaging) return false;
        if (!session.Map.Units.TryGetValue(stack.LeaderId, out var leader)) return false;
        var unit = MapBattle.Unit(leader, 0, rules, isAttacker: true);
        int mine = AiTargets.DamageEstimate(unit, rules.Constants, rules) * (unit.Attack.Reach == AttackReach.All ? 3 : 1);
        int its = attack.QtyDamage * (attack.Reach == AttackReach.All ? 3 : 1);
        return mine >= its;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
