using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>Что делает действие «защитить отряд, защитить место, дойти до места» (<c>0x460d27</c>, <c>0x455bac</c>, <c>0x45b2c8</c>).</summary>
public enum AiMoveAct
{
    /// <summary>Перемещение (<c>CAiMoveStackAction</c>).</summary>
    Move,

    /// <summary>Атака отряда (<c>CAiAttackStackAction</c> <c>0x45149a</c>).</summary>
    Attack,

    /// <summary>Мешок (<c>0x469787</c>).</summary>
    Bag,

    /// <summary>На месте — приказ <c>L_STAND</c> без цели (<c>0x45b2c8</c>, наследник <c>CAiChangeOrderAction</c>).</summary>
    Stand,
}

/// <summary>
/// Построители сценарных приказов (27.09, ранг 1 по листингу, <c>docs/ai-tactic-candidates.md</c> §18): <c>DefendParty</c> (0x16,
/// приказ <c>L_DEFEND_STACK</c>), <c>DefendLocation</c> (0x17, <c>L_DEFEND_LOCATION</c>), <c>MoveToLocation</c> (0x14,
/// <c>L_MOVE_TO_LOCATION</c>) и их общий поиск помехи на пути (<c>0x460711</c>) и выбор действия у цели (<c>0x460d27</c>). Сверены
/// партиями 13.10 (20261014-replay, r2a/r2b: DefendParty 4 + 4, DefendLocation 2 + 2, MoveToLocation со сменой приказа, помеха-мешок
/// у NM и пустой NL — <c>AiTacticFromMapHarnessTests</c>); в сценариях и главах саг таких отрядов 127.
/// </summary>
public static partial class AiTactics
{
    /// <summary>Объект-цель поиска помехи: отряд или мешок (<c>IMapElement</c>, позиция — сырая клетка).</summary>
    private readonly record struct Blocking(string Id, int X, int Y, bool IsBag);

    /// <summary>Соседи точки пути у <c>0x460a3b</c> (таблица <c>0x6d1610</c>, с самой точки).</summary>
    private static readonly (int Dx, int Dy)[] BlockerCells =
        [(0, 0), (1, 1), (0, 1), (1, 0), (-1, 1), (1, -1), (0, -1), (-1, -1), (-1, 0)];

    /// <summary>
    /// <c>DefendParty</c> (построитель <c>0x45541d</c>, оценка <c>CAiDefendPartyActionEval</c> <c>0x455a2a</c>): охраняемый — цель
    /// действующего приказа; нет его или подраса другая — смена приказа. Кандидаты по отрядам карты (порядок id): не свой, другой
    /// подрасы, не в крепости, не дальше <c>PA_RANGE / 2</c> от охраняемого (Чебышёв), водному — на воде, видимый, враждебный
    /// (<see cref="HostileTo"/>), не союзник; последним — сам охраняемый, если мы от него дальше двух клеток. По порядку: помеха на
    /// пути (<see cref="FindBlocker"/>, начальный набор — мы и охраняемый) или сам кандидат; путь (режим 1, мешки непроходимы) длиннее
    /// одной клетки — одно действие, счёт 150; пути нет — цель в исключения кэша. Действие — перемещение с готовым путём, N: мешок 0,
    /// охраняемый 2 (встать рядом), прочие 1 (враг — атака перемещением).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> DefendParty(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        var (_, target) = OrderOf(stack);
        if (!map.Stacks.TryGetValue(target, out var guarded) || !Same(guarded.Subrace, stack.Subrace))
        {
            if (ChangeOrderOption(session, stack, target) is { } change) yield return change;
            yield break;
        }
        int radius = AiTacticEval.DefendPartyRadius(session.Book!.PartyRange);
        var center = Position(map, guarded);
        bool waterOnly = session.WaterOnly(stack);
        var list = map.Stacks.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Where(s => !Same(s.Id, stack.Id) && !Same(s.Subrace, stack.Subrace) && s.Inside.Length == 0
                        && Cheb((s.X, s.Y), center) <= radius && (!waterOnly || session.IsWater(s.X, s.Y))
                        && !session.Invisible(s.Id) && HostileTo(session, stack, s) && !Allied(session, stack.Owner, s.Owner))
            .ToList();
        list.Add(guarded);
        var self = Position(map, stack);
        foreach (var e in list)
        {
            bool isGuarded = ReferenceEquals(e, guarded);
            if (isGuarded && Cheb(self, center) <= 2) continue;
            var excl = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stack.Id, guarded.Id };
            var pos = Position(map, e);
            var goal = FindBlocker(session, turn, stack, pos, excl, 0) ?? new Blocking(e.Id, pos.X, pos.Y, false);
            var path = SearchPath(session, stack, (goal.X, goal.Y), mode: 1, bags: false);
            if (path.Points.Count == 0)
            {
                turn.Excluded.Add(goal.Id);
                continue;
            }
            if (path.Points.Count <= 1) continue;
            int n = goal.IsBag ? 0 : Same(goal.Id, target) ? 2 : 1;
            var act = goal.IsBag ? AiMoveAct.Bag : n == 1 && map.Stacks.ContainsKey(goal.Id) ? AiMoveAct.Attack : AiMoveAct.Move;
            yield return (new AiTacticOption(AiActionType.DefendParty, stack.Id, goal.Id, goal.X, goal.Y, Reach: n, Act: act,
                                             Eval: new AiEvalTarget(goal.Id, goal.X, goal.Y)),
                          AiTacticScore.Constant(AiActionType.DefendParty)!.Value);
            yield break;
        }
    }

    /// <summary>
    /// <c>DefendLocation</c> (построитель <c>0x454c78</c>, оценка <c>0x45511b</c>): место — цель действующего приказа (локация
    /// <c>CMidLocation</c>: центр и радиус). Враги — отряды карты (порядок id): не мы, не в крепости, не дальше радиуса от центра, водному
    /// — на воде, видимые, враждебные, не союзники. По порядку: помеха на пути к врагу (начальный набор — мы) или сам враг; путь есть
    /// (режим 1) — цель, иначе цель в исключения кэша. Врагов нет — само место; стоим на нём — действия нет. Действие у цели —
    /// <see cref="ActionAt"/> (у места порог длины пути 2), счёт 150.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> DefendLocation(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        var (_, target) = OrderOf(stack);
        if (session.State.Events.Zones.GetValueOrDefault(target) is not { } zone) yield break;
        var center = (zone.X, zone.Y);
        bool waterOnly = session.WaterOnly(stack);
        var enemies = map.Stacks.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Where(s => !Same(s.Id, stack.Id) && s.Inside.Length == 0 && Cheb(center, (s.X, s.Y)) <= zone.Radius
                        && (!waterOnly || session.IsWater(s.X, s.Y)) && !session.Invisible(s.Id)
                        && HostileTo(session, stack, s) && !Allied(session, stack.Owner, s.Owner))
            .ToList();
        (string Id, int X, int Y)? goal = null;
        foreach (var e in enemies)
        {
            var excl = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stack.Id };
            var found = FindBlocker(session, turn, stack, (e.X, e.Y), excl, 0) ?? new Blocking(e.Id, e.X, e.Y, false);
            if (SearchPath(session, stack, (found.X, found.Y), mode: 1, bags: false).Points.Count > 0)
            {
                goal = (found.Id, found.X, found.Y);
                break;
            }
            turn.Excluded.Add(found.Id);
        }
        int minLen = 0;
        if (goal is null)
        {
            if (Position(map, stack) == center) yield break;
            goal = (zone.Id, zone.X, zone.Y);
            minLen = 2;
        }
        if (ActionAt(session, turn, stack, AiActionType.DefendLocation, goal.Value.Id, (goal.Value.X, goal.Value.Y), minLen) is { } option)
            yield return (option, AiTacticScore.Constant(AiActionType.DefendLocation)!.Value);
    }

    /// <summary>
    /// <c>MoveToLocation</c> (построитель <c>0x45afe8</c>, оценка <c>0x45b1fe</c>): место — цель действующего приказа, водному — на
    /// воде. Стоим на нём (сырая клетка отряда) — приказ <c>L_STAND</c> без цели (при действующем <c>L_NORMAL</c> — приказ ИИ), иначе
    /// <see cref="ActionAt"/>; счёт 150.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> MoveToLocation(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var (_, target) = OrderOf(stack);
        if (session.State.Events.Zones.GetValueOrDefault(target) is not { } zone) yield break;
        if (session.WaterOnly(stack) && !session.IsWater(zone.X, zone.Y)) yield break;
        var option = (stack.X, stack.Y) == (zone.X, zone.Y)
            ? new AiTacticOption(AiActionType.MoveToLocation, stack.Id, zone.Id, zone.X, zone.Y, Act: AiMoveAct.Stand,
                                 Eval: new AiEvalTarget(zone.Id, zone.X, zone.Y))
            : ActionAt(session, turn, stack, AiActionType.MoveToLocation, zone.Id, (zone.X, zone.Y), 0);
        if (option is not null) yield return (option, AiTacticScore.Constant(AiActionType.MoveToLocation)!.Value);
    }

    /// <summary>
    /// Действие у цели (<c>0x460d27</c> → выбор объекта <c>0x460fa0</c>): путь (режим 1, мешки непроходимы) есть и годен
    /// (<see cref="PathUseful"/>) — объект в конечной клетке пути: отряд, место, крепость (нет — ничего); иначе путь с проходимыми
    /// мешками — мешок в конечной клетке или первый по пути; иначе помеха (<see cref="FindBlocker"/>, начальный набор — мы и цель-отряд).
    /// Путь короче <paramref name="minLen"/> — ничего. По объекту: мешок — мешок; место — перемещение режима 2; крепость — режима 1;
    /// враждебный отряд — атака, если сила атаки на него не ноль или это цель нашего приказа <c>L_ATTACK_STACK</c>; свой или союзный —
    /// перемещение режима 1, если он не вплотную.
    /// </summary>
    private static AiTacticOption? ActionAt(GameSession session, AiTacticTurn turn, MapStack stack, AiActionType type, string id,
                                            (int X, int Y) point, int minLen) =>
        // Оценщик держит цель построителя (id, точка); действие у цели (0x460d27) может быть помехой — фильтр судит оценщик.
        ObjectAction(session, turn, stack, type, id, point, minLen) is { } action
            ? action with { Eval = new AiEvalTarget(id, point.X, point.Y) }
            : null;

    private static AiTacticOption? ObjectAction(GameSession session, AiTacticTurn turn, MapStack stack, AiActionType type, string id,
                                                (int X, int Y) point, int minLen)
    {
        var map = session.Map;
        var pick = PickObject(session, turn, stack, id, point, minLen);
        if (pick is not { } r) return null;
        if (r.IsBag) return new AiTacticOption(type, stack.Id, r.Id, r.X, r.Y, Act: AiMoveAct.Bag);
        if (session.State.Events.Zones.ContainsKey(r.Id)) return new AiTacticOption(type, stack.Id, r.Id, r.X, r.Y);
        // Крепость — точка в её углу (позиция объекта), путь разрешает её во вход (0x5cc23d). Журнал 20261018b-port-synth, карта 18:
        // MoveToLocation S143KC0010 → S143FT0006 точка (20,116), path_expand target (20,116) → resolved (23,119).
        if (map.Sites.FirstOrDefault(f => Same(f.Id, r.Id)) is { } fort)
            return new AiTacticOption(type, stack.Id, fort.Id, fort.X, fort.Y);
        if (!map.Stacks.TryGetValue(r.Id, out var other)) return null;
        var pos = Position(map, other);
        if (HostileTo(session, stack, other, countInside: true) && !Allied(session, stack.Owner, other.Owner))
        {
            bool ordered = stack.Order == (int)StackOrder.AttackStack && Same(stack.OrderTarget, other.Id);
            // Сила без поддержки: у этих вызовов 0x5dda98 восьмой аргумент 0 (0x45b700 push ecx = 0 и 0x460eb6 push ebx = 0), как у Retreat; на P == 0 не влияет.
            if (AttackOn(session, stack, other, support: false).P == 0.0 && !ordered) return null;
            return new AiTacticOption(type, stack.Id, other.Id, pos.X, pos.Y, Act: AiMoveAct.Attack);
        }
        return Cheb(Position(map, stack), pos) == 1 ? null : new AiTacticOption(type, stack.Id, other.Id, pos.X, pos.Y);
    }

    /// <summary>Выбор объекта у цели (<c>0x460fa0</c>), см. <see cref="ActionAt"/>.</summary>
    private static Blocking? PickObject(GameSession session, AiTacticTurn turn, MapStack stack, string id, (int X, int Y) point, int minLen)
    {
        var map = session.Map;
        var first = SearchPath(session, stack, point, mode: 1, bags: false);
        if (first.Points.Count > 0 && PathUseful(session, stack, first, point))
        {
            if (first.Points.Count < minLen) return null;
            // Объект — в цели искателя +0x10 (0x59d5b3 у 0x460fa0): после обрыва «идти» = 0 это клетка обрыва, а не цель поиска.
            var goal = first.CutAt ?? Goal(session, stack, point);
            if (map.Plan.GetValueOrDefault(goal)?.FirstOrDefault(o => Kind(o) == "KC") is { } k)
                return new Blocking(k, goal.X, goal.Y, false);
            // Место — по плану карты, как у движка (CMidgardPlan::getObjectId 0x5f54e2, тип 0x2f), а не по центру места событий:
            // редактор кладёт место в план клеткой центра, а без записи в плане движок места в конце пути не видит (port-synth-4-ai
            // до 16.10: LO0000…LO0002 без строк плана — у движка ни одного MoveToLocation/DefendLocation, 20261016c).
            if (map.Plan.GetValueOrDefault(goal)?.FirstOrDefault(o => Kind(o) == "LO") is { } place
                && session.State.Events.Zones.ContainsKey(place))
                return new Blocking(place, goal.X, goal.Y, false);
            if (map.Plan.GetValueOrDefault(goal)?.FirstOrDefault(o => Kind(o) == "FT") is { } f)
                return new Blocking(f, goal.X, goal.Y, false);
            return null;
        }
        var second = SearchPath(session, stack, point, mode: 1, bags: true);
        if (second.Points.Count > 0 && PathUseful(session, stack, second, point))
        {
            if (second.Points.Count < minLen) return null;
            // Мешок на пути обрывает поиск: список — до последней достижимой точки, мешок — в цели искателя +0x10 (клетка обрыва), её
            // 0x460fa0 смотрит первой, затем точки списка.
            var goal = second.CutAt ?? Goal(session, stack, point);
            foreach (var cell in (IEnumerable<(int X, int Y)>)[goal, .. second.Points.Select(p => (p.X, p.Y))])
                if (map.Plan.GetValueOrDefault(cell)?.FirstOrDefault(o => Kind(o) == "BG") is { } bag)
                    return new Blocking(bag, cell.X, cell.Y, true);
            return null;
        }
        var excl = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stack.Id };
        if (map.Stacks.ContainsKey(id)) excl.Add(id);
        return FindBlocker(session, turn, stack, point, excl, minLen);
    }

    /// <summary>
    /// Помеха на пути (<c>0x460711</c>, рекурсивно): путь «сквозь» враждебные отряды (режим 1, мешки проходимы, <c>a10</c>); нет —
    /// клетка цели в исключения кэша (<see cref="ExcludeCell"/>); путь негоден (<see cref="PathUseful"/>) — ничего. Точки пути —
    /// с конца (от цели к отряду); у каждой — помехи рядом (<see cref="BlockersNear"/>): каждая сразу в <paramref name="excl"/>, и
    /// первая, до которой есть путь (режим 1, мешки непроходимы) не короче <paramref name="minLen"/>, — ответ; затем те же помехи
    /// рекурсивно. Не нашлось — клетка цели в исключения кэша.
    /// </summary>
    private static Blocking? FindBlocker(GameSession session, AiTacticTurn turn, MapStack stack, (int X, int Y) point,
                                         HashSet<string> excl, int minLen)
    {
        var through = SearchPath(session, stack, point, mode: 1, bags: true, allStacks: true);
        if (through.Points.Count == 0)
        {
            ExcludeCell(session, turn, point);
            return null;
        }
        if (!PathUseful(session, stack, through, point)) return null;
        for (int i = through.Points.Count - 1; i >= 0; i--)
        {
            var near = BlockersNear(session, stack, (through.Points[i].X, through.Points[i].Y), excl);
            foreach (var o in near)
            {
                excl.Add(o.Id);
                var path = SearchPath(session, stack, (o.X, o.Y), mode: 1, bags: false);
                if (path.Points.Count > 0 && path.Points.Count >= minLen) return o;
            }
            foreach (var o in near)
                if (FindBlocker(session, turn, stack, (o.X, o.Y), excl, minLen) is { } deeper) return deeper;
        }
        ExcludeCell(session, turn, point);
        return null;
    }

    /// <summary>Помехи у точки пути (<c>0x460a3b</c>): девять клеток (<see cref="BlockerCells"/>) — отряд в клетке, враждебный
    /// (<see cref="HostileTo"/>) и не в <paramref name="excl"/>; нет в клетке никакого отряда — мешок не из <paramref name="excl"/>.</summary>
    private static List<Blocking> BlockersNear(GameSession session, MapStack stack, (int X, int Y) point, HashSet<string> excl)
    {
        var map = session.Map;
        var found = new List<Blocking>();
        foreach (var (dx, dy) in BlockerCells)
        {
            var c = (X: point.X + dx, Y: point.Y + dy);
            var ids = map.Plan.GetValueOrDefault(c);
            if (ids is null) continue;
            if (ids.FirstOrDefault(o => Kind(o) == "KC") is { } k)
            {
                if (!excl.Contains(k) && map.Stacks.TryGetValue(k, out var s) && HostileTo(session, stack, s))
                    found.Add(new Blocking(k, s.X, s.Y, false));
            }
            else if (ids.FirstOrDefault(o => Kind(o) == "BG") is { } bag && !excl.Contains(bag))
                found.Add(new Blocking(bag, c.X, c.Y, true));
        }
        return found;
    }

    /// <summary>
    /// Годен ли путь для поиска (<c>0x460bdb</c>): длиннее одной точки — да; иначе на конечной клетке отряд, которого нельзя атаковать
    /// (<see cref="HostileTo"/> со стоящими внутри) — нет; наш собственный приказ не <c>L_MOVE_TO_LOCATION</c> — да; иначе в конечной
    /// клетке крепость, жила, руина, ориентир, жезл или лавка — нет. ponytail: второе условие «да» (<c>0x5cd0e9</c> — цена последней
    /// точки) при длине 1 всегда ложно — не перенесено (ранг 2).
    /// </summary>
    private static bool PathUseful(GameSession session, MapStack stack, StackPath path, (int X, int Y) point)
    {
        if (path.Points.Count > 1) return true;
        var map = session.Map;
        var goal = path.CutAt ?? Goal(session, stack, point);   // цель искателя +0x10 — после обрыва клетка обрыва
        var ids = map.Plan.GetValueOrDefault(goal) ?? [];
        if (ids.FirstOrDefault(o => Kind(o) == "KC") is { } k && map.Stacks.TryGetValue(k, out var s)
            && !HostileTo(session, stack, s, countInside: true))
            return false;
        if (stack.Order != (int)StackOrder.MoveToLocation) return true;
        return !ids.Any(o => Kind(o) is "FT" or "CR" or "RU" or "MM" or "RD" or "SI");
    }

    /// <summary>Исключение клетки в кэш тактики (<c>0x45f902</c>): её крепость, отряд, мешок, лавка, руина, жила.</summary>
    private static void ExcludeCell(GameSession session, AiTacticTurn turn, (int X, int Y) point)
    {
        foreach (string o in session.Map.Plan.GetValueOrDefault(point) ?? [])
            if (Kind(o) is "FT" or "KC" or "BG" or "SI" or "RU" or "CR") turn.Excluded.Add(o);
    }

    /// <summary>
    /// Враждебен ли отряд <paramref name="other"/> отряду <paramref name="stack"/> (<c>0x5deb24</c>): другой отряд; подраса другая
    /// или наша нейтральная; раса хозяев другая или наша нейтральная (<paramref name="ignoreRace"/> — не сравнивать); не внутри
    /// крепости или руины (<paramref name="countInside"/> — считать и таких); видимый (<paramref name="seeInvisible"/> — и невидимый).
    /// </summary>
    private static bool HostileTo(GameSession session, MapStack stack, MapStack other, bool seeInvisible = false,
                                  bool countInside = false, bool ignoreRace = false)
    {
        if (Same(stack.Id, other.Id)) return false;
        var map = session.Map;
        bool neutralSubrace = map.Subraces.TryGetValue(stack.Subrace, out int kind) && kind == NeutralSubrace;
        if (!neutralSubrace && Same(stack.Subrace, other.Subrace)) return false;
        if (!ignoreRace)
        {
            var races = session.Book!.Economy.Races;
            RaceInfo? Race(string owner) => map.Players.TryGetValue(owner, out var p) && races.TryGetValue(p.RaceId, out var r) ? r : null;
            if (Race(stack.Owner) is { Neutral: false } && Diplomacy.PartyOf(map, races, stack.Owner) is { } own
                && own == Diplomacy.PartyOf(map, races, other.Owner)) return false;
        }
        if (!countInside && other.Inside.Length > 0) return false;
        return seeInvisible || !session.Invisible(other.Id);
    }

    /// <summary>Враждебность в масках пути ИИ (<c>0x5ccac4</c> → <c>0x5cc6b4</c> → <c>0x5deb24</c> с «видеть невидимых» = 1 и «без рас» =
    /// отряд на цели; отряд внутри крепости маски судят сами — <see cref="PathFinder.AiMasks"/>). Ранг 1 по листингу.</summary>
    public static Func<MapStack, bool, bool> PathHostile(GameSession session, MapStack stack) =>
        (other, atTarget) => HostileTo(session, stack, other, seeInvisible: true, countInside: true, ignoreRace: atTarget);

    /// <summary>Враждебность объекта действия пути команды хода (<c>0x5cd301</c> → <c>0x5cc6b4</c> → <c>0x5deb24(карта, мы, он, видеть невидимых =
    /// a5 = 1, считать внутри = 0, без рас = на цели)</c>, затем не союзник <c>0x5cd2ed</c>; <see cref="PathFinder.ActionObject"/>). Ранг 1 по
    /// листингу.</summary>
    public static Func<MapStack, bool, bool> ObjectHostile(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        return (other, atTarget) => HostileTo(session, stack, other, seeInvisible: true, countInside: false, ignoreRace: atTarget)
                                    && !Allied(session, stack.Owner, other.Owner);
    }

    /// <summary>Враждебность последней достижимой у пути ИИ (<c>0x5cd5b2</c> искателя <c>0x46903e</c> с <c>a5</c> = 0): чужой видимый отряд —
    /// чужие невидимые путь не останавливают (их видит только команда хода, <see cref="GameSession.CommandPath"/>).</summary>
    public static Func<MapStack, bool> ReachHostile(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        return other => !Same(other.Owner, stack.Owner) && !session.Invisible(other.Id);
    }

    /// <summary>Невидимый отряд для масок пути ИИ (<c>a5</c> = 0 у всех поисков ИИ: <c>0x46903e</c>, <c>0x460711</c>, <c>0x460fa0</c>,
    /// <c>0x454f0a</c>, <c>0x4557a9</c>, <c>0x455aec</c>, <c>0x45a898</c>): чужой невидимый — «можно», зоны контроля нет (<c>0x5ccc68</c>).</summary>
    public static Func<MapStack, bool> MaskInvisible(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return s => session.Invisible(s.Id);
    }

    /// <summary>Путь поиска объекта (<c>0x5cc6da</c> с <c>forbidWaterOnlyOnLand</c> = 1 и маской <see cref="PathFinder.AiMasks"/>) — список
    /// так, как его видит движок: оборванный «идти» = 0 (мешок в клетке шага, клетка рядом с врагом старта) — до последней достижимой
    /// точки (<c>0x5cd984</c> → <c>0x5cd4f5</c>), а цель искателя <c>+0x10</c> — клетка обрыва (<see cref="StackPath.CutAt"/>). Прежде
    /// порт брал весь список, и путь «сквозь» шёл за мешок до самой цели: 20261014-replay, NL KC009a → KC0099 (32,3) — у движка
    /// <c>path_expand</c> seq 246207: цель (26,2) = мешок BG0028, путь [(26,0), (26,1)], помеха — мешок, кандидата нет; у порта помехой
    /// выходила сама жезлоносица — лишний <c>DefendLocation</c> все 10 и 9 дней. Так же NM KC009b (seq 246465): мешок BG001a, а не KC0074.</summary>
    private static StackPath SearchPath(GameSession session, MapStack stack, (int X, int Y) point, int mode, bool bags,
                                        bool allStacks = false)
    {
        var map = session.Map;
        var opt = session.MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        var masks = PathFinder.AiMasks(map, stack, point.X, point.Y, mode, opt, MovementRules.Vanilla, (x, y) => Allied(session, x, y),
                                       bags, allStacks, PathHostile(session, stack), MaskInvisible(session));
        var path = PathFinder.Find(map, stack, point.X, point.Y, opt, MovementRules.Vanilla, session.DayAllowance(stack),
                                   hostile: ReachHostile(session, stack), masks: masks);
        return path.CutAt is null ? path : path with { Points = [.. path.Points.Take(path.Reachable + 1)] };
    }

    /// <summary>Разрешённая цель поиска (<c>+0x10</c> искателя, <see cref="PathFinder.Target"/>).</summary>
    private static (int X, int Y) Goal(GameSession session, MapStack stack, (int X, int Y) point)
    {
        var opt = session.MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        return PathFinder.Target(session.Map, stack, point.X, point.Y, opt, MovementRules.Vanilla);
    }
}
