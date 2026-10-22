using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>
/// Подготовка к атаке (срез 7, <c>docs/ai-spells.md</c> §5): построители атаки отряда (<c>0x453d15</c>) и крепости
/// (<c>0x452baf</c>) после счёта зовут планировщик <c>0x471ec7</c> и вместо одной атаки вставляют производных
/// кандидатов — заклинания (<see cref="AiSpells.BeforeAttack"/>) или подмогу, а при не взятом пороге силы ещё и итоговую
/// атаку. Ранг 1 — листинг, числа сверки — <c>AiTacticFromMapHarnessTests</c>.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// Кандидаты одной цели атаки. Счёт <see cref="AiTactic.NoScore"/> — ничего (у отряда и 0 тоже: <c>fcomp [0x6d0528]</c>
    /// у <c>0x453dc1</c>). Подготовка — только у атаки отряда и крепости при играбельной расе атакующего и действующем
    /// <c>L_NORMAL</c> (фильтр приказа стоит до построителя, поэтому <c>AttackParty</c> при <c>L_NORMAL</c> сюда не
    /// доходит). План (<see cref="Plan"/>): пуст — атака, если план её допускает; есть — каждый элемент (счёт S, затем
    /// на <see cref="AiBeforeAttack.ScoreStep"/> меньше), и если порог силы не взят — итоговая атака последней.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> WithPreparations(GameSession session, AiTacticTurn turn, MapStack stack,
                                                                          AiTacticOption attack, double score, AttackSide target)
    {
        bool city = attack.Type == AiActionType.AttackCity;
        if (score == AiTactic.NoScore || (!city && score == 0)) yield break;
        if (!Prepares(session, stack, attack.Type))
        {
            yield return (attack, score);
            yield break;
        }
        var (alone, preps) = Plan(session, turn, stack, attack, target);
        if (preps.Count == 0)
        {
            // 0x453f8b: план отказал — кандидата нет (так бывает только при взятом пороге: иначе хвост плана атаку допускает).
            if (alone) yield return (attack, score);
            yield break;
        }
        double s = score;
        foreach (var prep in preps)
        {
            yield return (attack with { Prep = prep }, s);
            s -= AiBeforeAttack.ScoreStep;   // движок сверяет с NoScore, но сюда счёт NoScore не доходит
        }
        if (!target.Passed) yield return (attack with { Prep = new AiPrep(AiPrepKind.Attack) }, s);
    }

    /// <summary>Сторона цели для планировщика: сила <c>p</c> и байт «порог взят» оценщика, хозяин цели — человек,
    /// раса хозяина цели неиграбельна, юниты цели (группа отряда или гарнизон), цель — отряд вне крепости, второй
    /// кошелёк (<c>0x45422e</c>).</summary>
    private sealed record AttackSide(double P, bool Passed, bool Human, bool RaceNeutral, IReadOnlyList<string> Units,
                                     bool StackOutside, bool SpellBank);

    /// <summary>Условия построителя (<c>0x453e2b</c>…, <c>0x452c54</c>…): тип 3/0x1b (отряд, слот <c>0x1c</c> оценщика —
    /// да у всех) или 2 (крепость), раса хозяина атакующего играбельна (<c>0x5e53a6</c>), действующий приказ
    /// <c>L_NORMAL</c>.</summary>
    private static bool Prepares(GameSession session, MapStack stack, AiActionType type) =>
        type is AiActionType.AttackStack or AiActionType.AttackParty or AiActionType.AttackCity
        && AiTactic.Admits(StackOrder.Normal, type) && OrderOf(stack).Order == StackOrder.Normal
        && !NeutralOwner(session, stack.Owner);

    /// <summary>
    /// Планировщик <c>0x471ec7</c>: сила не ниже порога (<see cref="AiBeforeAttack.Prepares"/>) — атака без подготовки.
    /// Иначе, если у цели есть юниты (длина вектора группы): хозяин атакующего без взятой столицы (<c>0x5e4e73</c>) —
    /// заклинание (<see cref="AiSpells.BeforeAttack"/>); не нашлось — подмога (<see cref="Helpers"/>). Пусто — хвост
    /// <see cref="AiBeforeAttack.AttackAlone"/>.
    /// </summary>
    private static (bool Alone, IReadOnlyList<AiPrep> Preps) Plan(GameSession session, AiTacticTurn turn, MapStack stack,
                                                                  AiTacticOption attack, AttackSide t)
    {
        if (!AiBeforeAttack.Prepares(t.P, t.Human, t.RaceNeutral)) return (true, []);
        if (t.Units.Count > 0)
        {
            bool captured = session.Map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0;
            if (!captured && AiSpells.BeforeAttack(session, stack, attack.Target, t.Units, t.StackOutside, (attack.X, attack.Y),
                                                   t.P, t.Passed, t.SpellBank, turn.Rng) is { } spell)
                return (true, [spell]);
            var helpers = Helpers(session, stack, attack);
            if (helpers.Count > 0) return (true, helpers);
        }
        return (AiBeforeAttack.AttackAlone(t.P, t.Passed, t.Human, t.RaceNeutral), []);
    }

    /// <summary>
    /// Подмога <c>0x472909</c>: отряды карты с очками хода (<c>+0x98</c> ≥ 1), той же подрасы, действующий приказ
    /// <c>L_ASSIST</c> с целью — атакующий, лидер не призыв и не иллюзия (<c>0x472a44</c> → <c>0x431d8c</c>, <c>IUsSummon</c>); сначала воры (<c>0x41b399</c>, <c>LEADER_CAT</c> 5), затем
    /// прочие, каждая группа по силе (<c>0x4763fc</c>: по убыванию, при равенстве — по номеру id). Точка — точка атаки.
    /// </summary>
    private static IReadOnlyList<AiPrep> Helpers(GameSession session, MapStack stack, AiTacticOption attack)
    {
        var book = session.Book!;
        var map = session.Map;
        var all = map.Stacks.Values
            .Where(s => s.Move >= 1 && Same(s.Subrace, stack.Subrace)
                        && OrderOf(s) is (StackOrder.Assist, var target) && Same(target, stack.Id)
                        && !(map.Units.TryGetValue(s.LeaderId, out var l) && UnitCategory.IsSummonImpl(book.CategoryOf(l.TypeId))))
            .ToList();
        bool Thief(MapStack s) => map.Units.TryGetValue(s.LeaderId, out var l) && book.LeaderCategoryOf(l.TypeId) == ThiefLeaderCategory;
        IEnumerable<MapStack> ByStrength(IEnumerable<MapStack> xs) =>
            xs.OrderByDescending(s => Strength(session, s)).ThenBy(s => IdKey(s.Id));
        return [.. ByStrength(all.Where(Thief)).Concat(ByStrength(all.Where(s => !Thief(s))))
                   .Select(h => new AiPrep(AiPrepKind.Assist, h.Id, attack.Target, attack.X, attack.Y))];
    }

    private const int ThiefLeaderCategory = 5;

    private static uint IdKey(string id) =>
        uint.TryParse(id[^4..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint k) ? k : 0;
}
