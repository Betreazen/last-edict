using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>
/// Охрана ИИ из карты (<c>Guard</c>, <c>docs/ai-tactic-candidates.md</c> §9): страж бросается на чужой отряд рядом.
/// Ранг 1 по листингу 24.09, если не сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// <c>Guard</c>: построитель <c>0x457317</c> — по всем отрядам карты (<c>iterator_omap&lt;0x23&gt;</c>) отбор
    /// <see cref="AiTacticEval.GuardAdmits"/>: действующий приказ не <c>L_STAND</c>, Чебышёв от позиции стража до клетки
    /// отряда не больше <c>GU_RANGE / 2</c>, другой подрасы, не в крепости, видимый, не союзник, вид расы лидера
    /// (<see cref="LeaderRaceType"/>) другой. Точка — клетка отряда (цель не в крепости — это позиция для ИИ), счёт 150
    /// (<c>0x6d0a10</c>). Действие <c>0x45763d</c> — атака перемещением (N = 0, проверка пути <c>0x45ab0b</c>, исполнение
    /// <c>0x453095</c>, статус 5 → 2), как атака со взятым порогом. Страж «только по воде» берёт только отряды на воде
    /// (<c>0x45753f</c>, ранг 2 — по аналогии с <c>0x45e9db</c>).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> Guard(GameSession session, MapStack stack)
    {
        var map = session.Map;
        var self = Position(map, stack);
        int range = session.Book!.GuardRange;
        int ownRace = LeaderRaceType(session, stack);
        bool selfWaterOnly = session.WaterOnly(stack);
        foreach (var s in map.Stacks.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = new AiGuardCandidate(
                EffectiveOrder: OrderOf(s).Order, Distance: Cheb(self, (s.X, s.Y)), SameSubrace: Same(s.Subrace, stack.Subrace),
                Inside: s.Inside.Length > 0, OnWater: session.IsWater(s.X, s.Y), Invisible: session.Invisible(s.Id),
                Allied: Allied(session, stack.Owner, s.Owner), SameLeaderCategory: LeaderRaceType(session, s) == ownRace);
            if (!AiTacticEval.GuardAdmits(candidate, selfWaterOnly, range)) continue;
            yield return (new AiTacticOption(AiActionType.Guard, stack.Id, s.Id, s.X, s.Y), AiTacticScore.Constant(AiActionType.Guard)!.Value);
        }
    }

    /// <summary>Вид расы юнита-лидера отряда (<c>0x5edb1a</c>: слот <c>0xc</c> <c>IUsSoldier</c> — раса, её запись из
    /// таблицы рас, поле <c>+8</c>; что это <c>Grace.RACE_TYPE</c> — ранг 2). Нет лидера или расы — −1 (у движка лидер
    /// есть всегда; ранг 3).</summary>
    private static int LeaderRaceType(GameSession session, MapStack stack)
    {
        var book = session.Book!;
        return session.Map.Units.TryGetValue(stack.LeaderId, out var leader)
               && book.Economy.Races.TryGetValue(book.RaceOf(leader.TypeId), out var race)
            ? race.RaceType : -1;
    }
}
