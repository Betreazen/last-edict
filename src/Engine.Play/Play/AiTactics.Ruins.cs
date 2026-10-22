using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>
/// Посещение руин ИИ из карты (<c>VisitRuin</c>, <c>docs/ai-tactic-candidates.md</c> §8): кэш руин, сила против охраны и
/// построитель. Ранг 1 по листингам 24.09, если не сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>Сторона пятна руины (<c>MidRuin</c> — 3×3).</summary>
    private const int RuinSize = 3;

    /// <summary>Вход руины (<c>0x5fdf70</c>/<c>0x5fdaaf</c>: <c>pos + size − 1</c>) — точка действия и позиция в кэше.</summary>
    public static (int X, int Y) RuinEntrance(MapSite ruin)
    {
        ArgumentNullException.ThrowIfNull(ruin);
        return AiTacticScore.AnchorCell(ruin.X, ruin.Y, RuinSize);
    }

    /// <summary>
    /// Кэш руин (<c>0x45f0ae</c>, вектор <c>+0x3c</c> <c>CMidServerLogicData2</c>): нетронутые (<c>0x45f1cb</c>: грабитель
    /// <c>+0x80</c> пуст), расстояние — евклид от позиции отряда до входа; радиус у чужих столиц и исключения — как у прочих
    /// векторов (<c>0x45f5f1</c>, <c>0x45f793</c>); до трёх.
    /// </summary>
    public static IReadOnlyList<MapSite> RuinTargets(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        return Nearest(map.Sites.Where(r => r.Kind == SiteKind.Ruin && !r.Gone && r.LooterId.Length == 0),
                       r =>
                       {
                           var (x, y) = RuinEntrance(r);
                           return (r.Id, x, y);
                       }, Position(map, stack), turn);
    }

    /// <summary>
    /// Сила отряда против охраны руины (<c>0x5dda98</c> из счёта <c>0x46c02a</c>): B — своя «без города» против числа
    /// юнитов охраны, A — охрана против числа своих юнитов (брони укрепления у руины нет), C — поддержка у входа
    /// (<see cref="Support"/>, флаг <c>[ebp+0x24]</c> = 1), итог — как у атаки (<c>0x5dda25</c>).
    /// </summary>
    public static AiAttackStrength RuinStrength(GameSession session, MapStack own, MapSite ruin)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(ruin);
        double b = GroupStrength(session, own, ignoreCity: true, ruin.Guards.Count);
        double a = UnitsStrength(session, ruin.Guards, ignoreCity: false, own.UnitIds.Count, fortArmor: 0);
        double c = Support(session, own, RuinEntrance(ruin));
        var mine = Fighters(session, own.UnitIds).ToList();
        var theirs = Fighters(session, ruin.Guards).ToList();
        return new AiAttackStrength(a, b, c, AiStrength.CanDamage(mine, theirs));
    }

    /// <summary>Поддержка вокруг точки (<c>0x5ddb38</c>): отряды в клетках <c>x ∈ [px − 10, px + 10)</c>,
    /// <c>y ∈ [py − 10, py + 10)</c> (с обрезкой краем карты), чужой подрасы (<c>+0x90</c> против подрасы своего отряда,
    /// <c>0x5f505a</c>) и с приказом не <c>L_STAND</c> (<c>+0xa8</c> против <c>[0x837910]</c>) — сумма их силы «с городом»
    /// против числа своих юнитов (<c>0x5dcec7</c>). Клетки берутся из индекса карты (<c>0x5f54e2</c>, объект вида
    /// <c>0x23</c>), проверки «внутри крепости», как у силы крепости, нет; в корпусе с ней и без неё — одно и то же.
    /// <para>Попутно запоминается сильнейший (обход — строки <c>y</c>, внутри <c>x</c>; заменяет только строго больший), и если это
    /// сама защищающаяся группа <paramref name="defender"/> (<c>[ebp+0x10]</c>, <c>0x57f529</c>) — поддержка 0 (<c>[0x6edea8]</c>).
    /// У руины защитник — не отряд, правило молчит; у атаки отряда на отряд (<see cref="AttackOn"/>) — цель.</para></summary>
    private static double Support(GameSession session, MapStack own, (int X, int Y) at, string? defender = null)
    {
        var map = session.Map;
        int x0 = Math.Max(at.X - SupportRange, 0), x1 = Math.Min(at.X + SupportRange, map.Size);
        int y0 = Math.Max(at.Y - SupportRange, 0), y1 = Math.Min(at.Y + SupportRange, map.Size);
        double sum = 0, max = 0;
        string strongest = "";
        foreach (var s in map.Stacks.Values
                     .Where(s => s.X >= x0 && s.X < x1 && s.Y >= y0 && s.Y < y1)
                     .Where(s => !Same(s.Subrace, own.Subrace) && s.Order != (int)StackOrder.Stand)
                     .OrderBy(s => s.Y).ThenBy(s => s.X))
        {
            double v = GroupStrength(session, s, ignoreCity: false, own.UnitIds.Count);
            sum += v;
            if (v > max) (max, strongest) = (v, s.Id);
        }
        return defender is not null && Same(strongest, defender) ? 0 : sum;
    }

    /// <summary>
    /// «Группа может драться» (<c>0x5ddf35</c>; зовут построители атаки отряда <c>0x4533fb</c>, крепости <c>0x452636</c> через
    /// <c>0x4526fc</c> и руины <c>0x46bdb6</c>): в группе есть живой юнит (<c>0x5de027</c> → <c>0x5e98ad</c>), у чьей первой атаки
    /// (<c>0x5ddf70</c>: реализация <c>0x5e97d8</c>, <c>IUsSoldier +0x48</c>, атака <c>+0xc</c>) класс — урон, похищение жизни,
    /// похищение с избытком, яд, обморожение или ожог (<c>[0x837428]</c>, <c>[0x837580]</c>, <c>[0x8374b8]</c>, <c>[0x8374f8]</c>,
    /// <c>[0x837550]</c>, <c>[0x837528]</c> — поля <c>+8</c> категорий <c>LAttackClass</c> Gog). Карта 20, R1: Хранитель обетов
    /// (<c>g000uu8133</c>, первая атака — обереги) у пустой руины — у движка <c>VisitRuin</c> нет, у порта был (сила пустой руины 1).
    /// </summary>
    internal static bool GroupCanFight(GameSession session, MapStack stack) =>
        stack.UnitIds.Any(id => session.Map.Units.TryGetValue(id, out var u) && u.Hp > 0
                                && FirstAttack(session, u) is { Class: AttackClass.Damage or AttackClass.Drain or AttackClass.DrainOverflow
                                                                   or AttackClass.Poison or AttackClass.Frostbite or AttackClass.Blister });

    /// <summary>
    /// <c>VisitRuin</c>: построитель <c>0x46bdb6</c> — столица хозяина не взята (слот <c>0x18</c> обёртки), лидер категории
    /// <c>L_LEADER</c> (<c>0x838188</c>: не вор, не знатный, не призыв); по руинам кэша (<see cref="RuinTargets"/>) — оценщик
    /// <c>CAiVisitRuinActionEval</c> (<c>0x46bf1e</c>), точка — вход, счёт <c>0x46c02a</c> (<see cref="AiTacticEval.VisitRuin"/>:
    /// сила ниже 0,7 — нет). Вплотную ко входу — посещение (бой с охраной), иначе перемещение к входу (<c>0x45a915</c>, N = 0).
    /// Группа может драться — <see cref="GroupCanFight"/>.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> VisitRuins(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        if (!map.Units.TryGetValue(stack.LeaderId, out var leader) || session.Book!.CategoryOf(leader.TypeId) != UnitCategory.Leader)
            yield break;
        if (!GroupCanFight(session, stack)) yield break;   // 0x46be41
        var self = Position(map, stack);
        foreach (var ruin in RuinTargets(session, turn, stack))
        {
            var at = RuinEntrance(ruin);
            double p = RuinStrength(session, stack, ruin).P;
            int yesterday = turn.Memo.Status(stack.Id, ruin.Id, at.X, at.Y, turn.Day - 1);
            yield return (new AiTacticOption(AiActionType.VisitRuin, stack.Id, ruin.Id, at.X, at.Y),
                          AiTacticEval.VisitRuin(turn.Weights, p, AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                                 turn.CapturedByOther(stack.Id, ruin.Id), yesterday));
        }
    }
}
