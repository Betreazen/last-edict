using D2.Rules.Map;
using D2.Rules.Battle;

namespace D2.Data.Play;

/// <summary>
/// Построители, которых в журналах корпуса нет (0 кандидатов), — перенос по листингу (ранг 1 по коду, сверить нечем; 28.09,
/// <c>docs/ai-tactic-candidates.md</c> §22): <c>EnterCityRoam</c> нейтрала и <c>SummonAttack</c> призванного без цели.
/// <c>MoveToStack</c> (0x12, <c>0x45b610</c>) у движка мёртв — его цель, голова плана операции (<c>0x473049</c>), всегда пуста.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// <c>EnterCityRoam</c> (0x1f, <c>CAiEnterCityRoam</c> <c>0x455cdb</c>), список нейтрала (<c>0x46395b</c>; хозяин захвачен — списка нет):
    /// отряд не в крепости (<c>0x455cf4</c>: <c>+0x94</c> не пуст — выход), не «только по воде» (слот <c>0x14</c>, <c>0x455d16</c>), лидер не призыв
    /// и не иллюзия (<c>0x455ec0</c>); крепости своей подрасы (<c>+0x88</c> = <c>+0x90</c>), не деревня в бунте и не столица захваченного
    /// (<see cref="OwnForts"/>), пустые (<c>+0x8c</c>); хозяина построитель не сравнивает. Точка — вход (<c>0x5e9563</c>); вплотную (слот
    /// <c>0x2c</c>: Чебышёв 1) — вход в крепость (<c>CAiEnterCityAction</c> <c>0x455bea</c>), иначе шаг. Счёт —
    /// <see cref="AiTacticEval.EnterCityRoam"/> (250 − d).
    /// </summary>
    internal static IEnumerable<(AiTacticOption, double)> EnterCityRoam(GameSession session, MapStack stack)
    {
        var map = session.Map;
        if (stack.Inside.Length > 0 || session.WaterOnly(stack) || SummonOrIllusionLeader(session, stack)) yield break;
        var self = Position(map, stack);
        foreach (var fort in OwnForts(map, stack))
        {
            if (fort.StackId.Length > 0) continue;
            var p = Entrance(fort);
            int d = Cheb(self, p);
            var step = d == 1 ? AiFortStep.Enter : AiFortStep.Move;
            yield return (new AiTacticOption(AiActionType.EnterCityRoam, stack.Id, fort.Id, p.X, p.Y, step), AiTacticEval.EnterCityRoam(d));
        }
    }

    /// <summary>Лидер — призыв или иллюзия (<c>0x455ec0</c>: <c>IUsSummon</c> <c>0x431d8c</c> / <c>IUsIllusion</c> <c>0x438ff3</c>).</summary>
    private static bool SummonOrIllusionLeader(GameSession session, MapStack stack) =>
        session.Map.Units.TryGetValue(stack.LeaderId, out var leader)
        && session.Book!.CategoryOf(leader.TypeId) is UnitCategory.Summon or UnitCategory.Illusion;

    /// <summary>
    /// <c>SummonAttack</c> (4, <c>CAiSummonAttack</c> <c>0x464dcb</c>), список призыва (<c>0x464341</c>); фильтр приказа пропускает его только
    /// при <c>L_NORMAL</c> — призыв ИИ с целью (<c>L_ATTACK_STACK</c>) его не строит, отсюда 0 в журналах; живой путь — призыв без цели.
    /// Все отряды карты по id (<c>0x419aac</c>); водному — только на воде (<c>0x464fa1</c>). Нейтральному хозяину цели
    /// (<c>0x5e5358</c>) — <see cref="AiTacticEval.SummonNeutral"/> (<c>0x465322</c>); прочие — не своей подрасы (<c>0x464ed6</c>), видимые
    /// (<c>0x464ef0</c>), другой подрасы типа лидера (<c>0x464eff</c> → <c>0x5edb9c</c>, без исключения нейтральной подрасы) —
    /// <see cref="AiTacticEval.SummonAttack"/>. Точка — позиция цели; действие — атака перемещением (исполнение — как у атаки).
    /// </summary>
    internal static IEnumerable<(AiTacticOption, double)> SummonAttack(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        var self = Position(map, stack);
        bool water = session.WaterOnly(stack);
        int own = LeaderSubraceOf(session, stack);
        foreach (var s in map.Stacks.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (water && !session.IsWater(s.X, s.Y)) continue;
            var at = Position(map, s);
            int d = Cheb(self, at);
            double score;
            if (NeutralOwner(session, s.Owner)) score = AiTacticEval.SummonNeutral(true, d);
            else
            {
                if (Same(s.Subrace, stack.Subrace) || session.Invisible(s.Id) || LeaderSubraceOf(session, s) == own) continue;
                bool human = map.Players.TryGetValue(s.Owner, out var p) && p.IsHuman;
                score = AiTacticEval.SummonAttack(turn.Weights, false, human, Allied(session, stack.Owner, s.Owner), d);
            }
            yield return (new AiTacticOption(AiActionType.SummonAttack, stack.Id, s.Id, at.X, at.Y), score);
        }
    }

    /// <summary>Подраса типа лидера (<c>0x5edb9c</c>: <c>IUsSoldier</c> слот <c>+0x10</c>); лидера нет — 0.</summary>
    private static int LeaderSubraceOf(GameSession session, MapStack stack) =>
        session.Map.Units.TryGetValue(stack.LeaderId, out var leader) ? session.Book!.SubraceOf(leader.TypeId) : 0;
}
