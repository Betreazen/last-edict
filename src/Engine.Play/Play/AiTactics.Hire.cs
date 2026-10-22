using D2.Rules.Battle;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>
/// Построитель <c>HireUnit</c> (0xa, <c>CAiHireUnitTactic</c> <c>0x459f0d</c>, <c>docs/ai-tactic-candidates.md</c> §19): «за юнитами
/// гарнизона» — отряд идёт к своей крепости, юнитов забирает подготовка в крепости. Есть в списках бойца (<c>0x4633fe</c>) и
/// жезлоносца (<c>0x463fd8</c>), у вора нет. Зонд 7.1 (<c>ai_hire_score</c>, 27.09): ai-q09 — 3 оценки за 20 дней, все у жезлоносцев
/// с одним свободным местом, условия сошлись во всех окнах, где фильтр приказа их пропускает.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// <c>0x459f0d</c>, ранг 1: хозяин не захвачен (слот 6 обёртки), свободные места (<c>0x5f4485</c>: вместимость группы минус
    /// занятые клетки) есть, отряд не в крепости (слот 4, <c>+0x94</c>); крепости своего игрока (<c>+0x84</c>), не деревня в бунте
    /// (<c>0x5e9582</c>) и не столица захваченного хозяина (<c>0x5e4e73</c>) — <see cref="FortUsable"/>; по id, как у остальных
    /// «шагов к крепости». Точка — вход (<c>0x5e9563</c>), действие — как у <c>HealRevive</c>: вход, выселение гостя или шаг.
    /// Счёт — <see cref="AiTacticEval.HireUnit"/> (<c>0x45a34c</c>).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> HireUnit(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        int free = session.FreeCells(stack);
        if (free == 0 || stack.Inside.Length > 0) yield break;
        var self = Position(map, stack);
        foreach (var fort in Forts(map).Where(f => Same(f.Owner, stack.Owner) && FortUsable(map, f))
                                       .OrderBy(f => f.Id, StringComparer.OrdinalIgnoreCase))
        {
            var p = Entrance(fort);
            var step = FortStepOf(map, stack, fort, self, p);
            var score = AiTacticEval.HireUnit(turn.Weights, occupied: fort.StackId.Length > 0, free, Cheb(self, p),
                                              CanTakeFromGarrison(session, fort, free), turn.CapturedByOther(stack.Id, fort.Id));
            if (step == AiFortStep.KickOut)
            {
                if (KickOut(session, AiActionType.HireUnit, stack, fort) is { } kick) yield return (kick, score);
                continue;
            }
            yield return (new AiTacticOption(AiActionType.HireUnit, stack.Id, fort.Id, p.X, p.Y, step), score);
        }
    }

    /// <summary>
    /// Есть кого забрать (<c>0x45a498</c>, ранг 1): снимаемых из гарнизона (<c>0x45a50e</c> — юниты без стражей, ранг 2) хотя бы
    /// один; при одном свободном месте — в гарнизоне есть малый (<c>0x5e9830</c> по всем юнитам, ранг 2).
    /// </summary>
    private static bool CanTakeFromGarrison(GameSession session, MapSite fort, int free)
    {
        var map = session.Map;
        var book = session.Book!;
        var units = fort.Guards.Where(map.Units.ContainsKey).Select(id => map.Units[id]).ToList();
        if (!units.Any(u => book.CategoryOf(u.TypeId) != UnitCategory.Guardian)) return false;
        return free > 1 || units.Any(u => book.Rules.Units.TryGetValue(u.TypeId, out var t) && t.SizeSmall);
    }
}
