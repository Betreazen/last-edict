using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Rng;

namespace D2.Data.Play;

/// <summary>
/// Тактика ИИ в ходе игрока (срез 5, <c>docs/ai-turn-plan.md</c>): назначение помощников (<c>0x45603b</c>), очередь
/// отрядов (<see cref="AiTacticOrders.TurnQueue"/>), выбор действия (<see cref="AiTactics.Choose"/>), исполнение и
/// память статусов (<c>0x461fb2</c>). Атак ещё нет (срез 6) — отряды ходят к мешкам, кристаллам, своим городам и
/// к тем, кому помогают.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Потолок оценок одного отряда за ход. Движок оценивает отряд снова, пока у него остались очки хода
    /// (журналы human и legions: после исполнения с запасом больше нуля — тот же отряд в 463 из 500 и 575 из 598
    /// случаев), и выходит, когда кандидатов нет (статус 1 сегодня их снимает); потолок — только страховка.</summary>
    private const int MaxTacticCycles = 16;

    private const int StatusDone = 2;

    /// <summary>Ход тактики игрока ИИ. Отчёт дня получает строки «тактика …» — что выбрал и чем кончилось.</summary>
    private void TacticTurn(string playerId, List<string> report)
    {
        if (Book is null) return;
        AssignHelpers(playerId, report);
        var memo = AiTacticMemo.From(State.Tactic);
        if (AiTactics.Begin(this, playerId, memo) is not { } turn) return;
        var queue = new List<string>(PrepareTactic(playerId, report, turn));
        // Страховка: призванные удлиняют очередь, но не без предела (мана); общий потолок выборов хода.
        for (int guard = (queue.Count + 1) * MaxTacticCycles * 2; queue.Count > 0 && guard > 0; guard--)
        {
            string id = queue[0];
            bool interrupted = false;
            for (int cycle = 0; cycle < MaxTacticCycles; cycle++)
            {
                if (!Map.Stacks.TryGetValue(id, out var current) || !CanStep(current)) break;   // фаза 0: 0x466b86 → 0x466bcb
                if (cycle == 0) turn.Select();            // новый выбор отряда: свежая обёртка (0x466a9f)
                AiStackItems.Prepare(this, id, report, turn);   // шаг выбора отряда (0x466b68)
                var stack = Map.Stacks[id];
                MapStack Reselect()                             // повтор оценки — снова шаг выбора (0x466aba → 0x466b68)
                {
                    AiStackItems.Prepare(this, id, report, turn);
                    return Map.Stacks.TryGetValue(id, out var now) ? now : stack;
                }
                if (AiTactics.Choose(this, turn, stack, Reselect) is not { } pick) break;
                var a = pick.Action;
                int destroyed = Map.Destroyed.Count;
                var known = Map.Stacks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var (status, key) = ExecuteTactic(a, turn);
                if (Map.Destroyed.Count != destroyed)
                {
                    AiTactics.StackDestroyed(this, turn);   // CCmdStackDestroyedMsg → списки сил заново (0x471d4b)
                    _forces = turn.Forces;
                }
                // Приказ своего отряда сменился на L_ASSIST без цели (смена приказа) — наблюдатель тактики (0x46726f) тут же назначает
                // помощников (0x467307 → 0x45603b), и следующая оценка того же отряда — уже помощником. Карта 22 (20261019b): день 6
                // KC0004, день 9 KC0017 — ai_order «assist» с причиной ai_45603b сразу за ai_tactic_result 1, затем AssistMove.
                // Так же — после гибели лидера: CEffectKillStack отпускает его помощников (L_ASSIST без цели, ReleasedHelpers), и
                // наблюдатель назначает их снова (карта 23, день 58: помощники павшего S143KC0007 — к S143KC0017, ai_45603b).
                // Назначение повторяется и для помощников без цели, оставшихся от начала хода (призванные — их AssignHelpers пропускает
                // снова, итог тот же).
                bool orderChanged = a.Type == AiActionType.ChangeOrder && Map.Stacks.TryGetValue(id, out var changed)
                                    && AiTactics.OrderOf(changed) is (StackOrder.Assist, { Length: 0 });
                bool released = Map.Destroyed.Count != destroyed
                                && Map.Stacks.Values.Any(s => Same(s.Owner, playerId) && AiTactics.OrderOf(s) is (StackOrder.Assist, { Length: 0 }));
                if (orderChanged || released) AssignHelpers(playerId, report);
                turn.Memo = turn.Memo.With(key.Stack, key.Target, key.X, key.Y, turn.Day, status);
                if (status == AiTactic.StatusProgress) turn.Captured.TryAdd(key.Target, key.Stack);
                string prep = a.Prep is { } p ? $" {p.Kind} {p.Id}".TrimEnd() : "";
                report.Add($"тактика {key.Stack} {a.Type}{prep} {key.Target} ({key.X},{key.Y}) → {status}");
                // Призванный отряд игрока (CCmdCreateStackMsg → 0x467313: лидер L_SUMMON) — в начало очереди (0x467439), а выбор текущего
                // сбрасывается (0x4673e5): следующим ходит призванный, затем текущий заново, свежим выбором (0x466a28 → 0x465af9).
                var summoned = Summoned(playerId, known);
                if (summoned.Count > 0)
                {
                    queue.InsertRange(0, summoned);   // каждый новый — вперёд (0x467439): последний созданный — первым
                    interrupted = true;
                    break;
                }
            }
            if (!interrupted) queue.RemoveAt(0);
        }
        State = State with { Tactic = [.. turn.Memo.Records] };
    }

    /// <summary>Соседи клетки отряда в порядке проверки <c>0x466bcb</c> (таблица <c>0x6d1e40</c>, сдвиг x, y); у отряда в крепости — с
    /// четвёртого: клетки вне крепости от её входа.</summary>
    private static readonly (int Dx, int Dy)[] StepNeighbours = [(-1, -1), (0, -1), (-1, 0), (1, -1), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary>Отряд в начале очереди ещё ходит (<c>0x466bcb</c>, фаза 0 — <c>0x466b86</c> снимает из начала очереди всех, кто не
    /// ходит): запас хода (<c>+0x98</c>) не ноль и хоть одна соседняя клетка его позиции (<see cref="AiTactics.Position"/>, у гостя
    /// крепости — вход; соседи — <see cref="StepNeighbours"/> с 0, у гостя с 3) проходима по сетке без масок (<c>0x602692(…, 0, 0, 1)</c>
    /// → <c>0x602a03</c> не ноль: <see cref="PathFinder.CellCost"/>). Карта 24 (<c>20261020-replay-q09</c>), день 3: помощник
    /// <c>S143KC0095</c> после подмоги встал в (20,87) с запасом 11, вокруг — враг, свой отряд и непроходимое; движок снимает его
    /// (фаза 0 → 5, seq 261026), а порт по запасу хода выбирал ему AssistMove со статусом 1.</summary>
    private bool CanStep(MapStack stack)
    {
        if (stack.Move <= 0) return false;
        var (x, y) = AiTactics.Position(Map, stack);
        var opt = MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        for (int i = stack.Inside.Length > 0 ? 3 : 0; i < StepNeighbours.Length; i++)
        {
            var (cx, cy) = (x + StepNeighbours[i].Dx, y + StepNeighbours[i].Dy);
            if (Map.Contains(cx, cy) && PathFinder.CellCost(Map, stack, cx, cy, opt, MovementRules.Vanilla) != Movement.Forbidden) return true;
        }
        return false;
    }

    /// <summary>Отряды игрока <paramref name="playerId"/>, появившиеся за исполнение (нет в <paramref name="known"/>), с лидером категории
    /// <c>L_SUMMON</c> (<c>0x467313</c>: <c>[0x838168]</c> — как группа «призыв» очереди, иллюзия не входит) — с последнего созданного:
    /// каждый вставляется в начало очереди.
    /// Карта 24 (<c>20261020-replay-q09</c>), день 19: заклинание призыва <c>G000SS0031</c> перед атакой <c>S143KC0002</c> на
    /// <c>S143KC005e</c> — призванный <c>S143KC00aa</c> выбирается сразу (<c>ai_tactic_pick</c> 0 → 1 без текущего отряда, seq 3426051) и
    /// бьёт той же цели (<c>AttackParty</c>); прежде порт оставлял его без хода до следующего дня.</summary>
    private List<string> Summoned(string playerId, IReadOnlySet<string> known)
    {
        var book = RequireBook();
        return [.. Map.Stacks.Values.Where(s => !known.Contains(s.Id) && Same(s.Owner, playerId) && Map.Units.TryGetValue(s.LeaderId, out var leader)
                                                && book.CategoryOf(leader.TypeId) == UnitCategory.Summon)
                                    .OrderByDescending(s => IdKey(s.Id)).Select(s => s.Id)];
    }

    /// <summary>Начало хода тактики после помощников (<c>0x465984</c>): очередь отрядов (<see cref="Queue"/>) и
    /// подготовка каждого по её порядку (<see cref="AiStackItems.Prepare"/>; <paramref name="turn"/> — ход тактики, чей
    /// кошелёк получает подготовка). Возвращает очередь.</summary>
    public IReadOnlyList<string> PrepareTactic(string playerId, List<string> report, AiTacticTurn? turn = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var queue = Queue(playerId);
        foreach (string id in queue)
            if (Map.Stacks.ContainsKey(id)) AiStackItems.Prepare(this, id, report, turn);
        return queue;
    }

    /// <summary>Очередь отрядов игрока (<c>0x467b07</c>/<c>0x467c0e</c>): действующий приказ не <c>L_STAND</c>; группы
    /// по приказу и лидеру; ключ — номер id.</summary>
    private IReadOnlyList<string> Queue(string playerId)
    {
        var book = RequireBook();
        var queued = new List<AiQueued>();
        foreach (var s in Map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)))
        {
            var (order, target) = AiTactics.OrderOf(s);
            if (order == StackOrder.Stand) continue;
            string type = Map.Units.TryGetValue(s.LeaderId, out var leader) ? leader.TypeId : "";
            var group = order == StackOrder.Assist ? AiStackGroup.Assist
                : order == StackOrder.DefendCity ? AiStackGroup.DefendCity
                : book.CategoryOf(type) == UnitCategory.Summon ? AiStackGroup.Summon
                : book.CategoryOf(type) == UnitCategory.Noble ? AiStackGroup.Noble
                : book.LeaderCategoryOf(type) == 4 ? AiStackGroup.Rod
                : AiStackGroup.Other;
            queued.Add(new AiQueued(s.Id, IdKey(s.Id), group, target));
        }
        return AiTacticOrders.TurnQueue(queued, null);
    }

    /// <summary>Номер объекта в <c>CMidgardID</c> — последние четыре шестнадцатеричных знака id.</summary>
    private static uint IdKey(string id) =>
        uint.TryParse(id[^4..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint k) ? k : 0;

    /// <summary>
    /// Исполнение действия (слот 7). Цель рядом (мешок, жезл, снос жезла — с соседней клетки; вход в крепость по
    /// <see cref="AiFortStep.Enter"/>) — действие у цели (<see cref="Arrive"/>). Иначе — действие-перемещение
    /// <c>CAiMoveStackAction</c> (<c>0x45abd8</c>): путь ИИ (<see cref="AiTactics.Route"/>), команда движения по
    /// сегодняшней части; статус всегда 5 — в журналах human и legions у 1 413 исполнений этого класса только 5, а
    /// действие у цели делает следующая оценка. Путь к своей крепости пройден целиком — отряд входит в неё в том же
    /// исполнении (последняя <c>stack_move</c> — в угол крепости). Сдвинуться некуда — 1.
    /// ponytail: выселение из крепости не делается.
    /// </summary>
    private int Execute(AiTacticOption a, AiTacticTurn? turn)
    {
        if (a.Type == AiActionType.ChangeOrder) return ExecuteChangeOrder(a);
        if (a.Type == AiActionType.StayInside) return AiTactic.StatusStuck;   // 0x6d1c94: остаться, статус 1
        if (a.Type is AiActionType.NobleStack or AiActionType.NobleCity or AiActionType.NobleMerchant) return ExecuteThief(a, turn);
        if (a.Type == AiActionType.GiveItems) return ExecuteGive(a, turn);
        // Сценарные приказы (AiTactics.Defend.cs): атака отряда — как у атаки; на месте — приказ «стоять».
        if (a.Act == AiMoveAct.Attack)
        {
            int status = ExecuteAttack(a with { Reach = 0 }, turn);
            // DefendParty — действие-перемещение (vft 0x6d0834: исполнение 0x453095 → 0x45abd8, статус 0x45af69 — +0x4c хода, не
            // 0x4545d8 атаки): 5 и с боем. Корпус — 72 исполнения против отряда, все 5; карта 18 (20261018b) — назавтра счёт ×1,25: 187,5.
            return a.Type == AiActionType.DefendParty && status == StatusDone ? AiTactic.StatusProgress : status;
        }
        if (a.Act == AiMoveAct.Stand) return ExecuteStand(a);
        // Охрана (0x45763d) — та же атака перемещением без «напрямую» (N = 0).
        // Берсерк (CAiAttackStackBezerkAction 0x6d01ec: исполнение 0x451921 → 0x453095, статус 0x4545d8) — тоже.
        if (a.Type is AiActionType.AttackStack or AiActionType.AttackParty or AiActionType.AttackStackRoam
                or AiActionType.AttackCity or AiActionType.AttackCityRoam or AiActionType.Guard
                or AiActionType.AttackStackBezerk or AiActionType.AttackCityBezerk or AiActionType.SummonAttack)
            return ExecuteAttack(a, turn);   // SummonAttack (0x465042): дальше клетки — шаг, вплотную — CAiSummonAction (бой, ранг 2)
        if (a.Type == AiActionType.VisitTrainer) return VisitTrainer(a, turn);
        if (a.Type is AiActionType.VisitMerchant or AiActionType.VisitMage && a.Direct) return VisitShop(a, turn);
        if (a.Type == AiActionType.HireMercs) return VisitMercs(a, turn);
        if (a.Type == AiActionType.VisitTemple && a.Direct)
        {
            AiTactics.BuyAtTemple(this, turn, a.Stack, a.Target);
            return AiTactic.StatusStuck;
        }
        if (a.Fort == AiFortStep.KickOut) return KickOutGuest(a);
        var stack = Map.Stacks[a.Stack];
        if (a.Fort == AiFortStep.Stay) return AiTactic.StatusStuck;
        var self = AiTactics.Position(Map, stack);
        bool adjacentAction = a.Type is AiActionType.PlantRod or AiActionType.TakeBag or AiActionType.TakeBagRoam
            or AiActionType.AttackRod or AiActionType.VisitRuin;
        if (Near(self, (a.X, a.Y), adjacentAction ? 1 : 0) || a.Fort == AiFortStep.Enter) return Arrive(a);
        var path = AiTactics.Route(this, stack, a);
        var step = AiToday(a.Stack, path, a);
        var (today, fired, cut) = step;
        if (today.Count == 0)
            return TakeBagAtFirstStep(a.Stack, path) || StrikeInPlace(a.Stack, step, turn) ? AiTactic.StatusProgress : AiTactic.StatusStuck;
        var end = today[^1];
        var before = today.Count > 1 ? today[^2] : path.Points[0];
        // Враг у последней достижимой (объект действия команды, 0x5cd11f) — нападение вместо входа, мешка, руины; статус перемещения тот же.
        if (AiMove(a.Stack, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, step.Path, strike: step.Attack, turn: turn))
            return AiTactic.StatusProgress;
        // Оборван событием — ни входа, ни мешка (0x43fc13: генератор вернул 0), статус перемещения тот же.
        if (cut) return AiTactic.StatusProgress;
        if (today.Count == path.Points.Count - 1 && IntoOwnFort(a))
            Enter(a.Stack, a.Target);
        // Сценарный приказ, чей объект у цели — крепость (0x460fa0 → CAiMoveStackAction), дошёл до конца пути: генератор хода берёт
        // крепость в клетке цели объектом нападения (0x43ff9d → 0x440667) — своя: вход, чужая пустая: вход с захватом, с гарнизоном: бой.
        // Журнал 20261018b-port-synth, карта 18, день 1: MoveToLocation S143KC0010 → S143FT0006 — stack_move (22,120), fort_capture
        // seq 7093960, stack_move в угол (20,116), статус 5; порт до 19.10 вставал у ворот, а деревню назавтра брал AttackCity S143KC0002.
        // Союзная крепость — ни входа, ни боя (как у щелчка человека по союзнику, 0x4d1590 → 0x4d0ca4; журналом не проверено, ранг 3).
        // Пустая чужая без очков хода после пути — Strike не входит, как и в ExecuteAttack (случая в журналах нет).
        else if (today.Count == path.Points.Count - 1 && ScenarioFort(a) is { } fort && Map.Stacks.TryGetValue(a.Stack, out var mover)
                 && !(fort.Owner.Length > 0 && AiTactics.Allied(this, mover.Owner, fort.Owner)))
        {
            if (fort.Owner.Equals(mover.Owner, StringComparison.OrdinalIgnoreCase)) Enter(a.Stack, a.Target);
            else if (MapBattle.DefenderOf(Map, a.Target) is not null) AiBattle(a.Stack, a.Target, arrival: true);
            else Strike(a.Stack, a.Target);
        }
        // Дошёл до конца пути, цель — мешок (снят как непроходимый): эффекты хода берут его сразу (0x4408cb по клетке цели +0x10).
        else if (today.Count == path.Points.Count - 1 && Map.Stacks.ContainsKey(a.Stack)
                 && BagAt(a.X, a.Y) is { } targetBag && Near((end.X, end.Y), (a.X, a.Y), 1))
            Enter(a.Stack, targetBag.Id);
        // Дошёл сегодня до клетки у входа руины — посещение тем же исполнением (объект за концом пути 0x4408cb → 0x440a53: бой с
        // охраной), статус «в пути» 5. Журнал 20261016e-av, карта 5, день 3: VisitRuin KC0005 → RU0000 — подход и бой одним выбором
        // (seq 4250199 → battle_start 4250231); прежде порт делал второй выбор VisitRuin.
        // Бой — и с нулём очков хода после пути (как у атаки по прибытии): карта 5, день 13 — S143KC0010 доходит до (112,130) с запасом 0
        // (ap_restore seq 6078555) и бьёт охрану S143RU0002 (battle_start 6078574); порт отказывал «нет очков хода», и руину назавтра брал
        // S143KC0011 (реестр И155).
        else if (a.Type == AiActionType.VisitRuin && today.Count == path.Points.Count - 1 && Map.Stacks.ContainsKey(a.Stack)
                 && Near((end.X, end.Y), (a.X, a.Y), 1))
        {
            if (MapBattle.DefenderOf(Map, a.Target) is not null) AiBattle(a.Stack, a.Target, arrival: true);
            else Strike(a.Stack, a.Target);
        }
        // Чужой жезл за концом пути — снос тем же исполнением (объект за концом пути, как у руины), статус «в пути» 5. Журнал
        // 20261019b-port-synth, карта 22, день 1: AttackRod S143KC0004 → S143RD0000 — stack_move в (128,117), relation rod_destroyed
        // (seq 4841413) и ai_tactic_result 5 одним выбором; порт до 19.10 делал второй выбор AttackRod (102 → 2).
        else if (a.Type == AiActionType.AttackRod && today.Count == path.Points.Count - 1 && Map.Stacks.ContainsKey(a.Stack)
                 && Near((end.X, end.Y), (a.X, a.Y), 1))
            BreakRod(a.Stack, a.Target);
        return AiTactic.StatusProgress;
    }

    /// <summary>
    /// Атака отряда (<c>CAiAttackStackAction</c>, vtable <c>0x6d010c</c>, исполнение <c>0x4514fc</c>), по флагам фабрики
    /// (<c>docs/ai-attack.md</c> §8):
    /// <list type="bullet">
    /// <item>«напрямую» (<c>+0x5c</c>, только слабая атака в пределах обзора) — <c>StackEmptyMoveAllowance</c>
    /// (<c>0x41e507</c>): отряд стоит, очки хода обнуляются, статус 1 (журнал human: 18 из 18 таких исполнений);</item>
    /// <item>иначе — перемещение <c>0x45abd8</c> в режиме 1 с N фабрики (<see cref="AiTacticOption.Reach"/>), статус 5
    /// отдаётся как 2 (<c>0x4545d8</c>, <c>+0x58</c>); при N = 0 (порог силы взят) путь ведёт в цель, и дошедший сегодня
    /// отряд дерётся в том же исполнении (журнал: бой у 65 из 125 таких атак), при N &gt; 0 — остановка за N − 1 клеток.</item>
    /// </list>
    /// Атака крепости (<c>CAiAttackCityAction</c>, <c>AttackCity</c>/<c>AttackCityRoam</c>) исполняется так же, цель — вход
    /// крепости, удар — бой с визитёром и гарнизоном, а в пустую крепость — вход с захватом (<see cref="Enter"/>). Ранг 3:
    /// в корпусе ИИ выбирал атаку крепости дважды (подход без боя; бой и захват).
    /// Бой с человеком — автобоем, если его не сыграл наблюдатель (экран боя, <see cref="ITurnObserver.Battle"/>).
    /// </summary>
    private int ExecuteAttack(AiTacticOption a, AiTacticTurn? turn = null)
    {
        var stack = Map.Stacks[a.Stack];
        if (AttackPoint(a.Target) is not { } at) return AiTactic.StatusStuck;
        if (a.Direct)
        {
            EmptyMoveAllowance(a.Stack);
            return AiTactic.StatusStuck;
        }
        var self = AiTactics.Position(Map, stack);
        if (a.Reach == 0 && Near(self, at, 1)) return Strike(a.Stack, a.Target) ? StatusDone : AiTactic.StatusStuck;
        // SummonAttack шагом — CAiMoveStackAction (vft 0x6d1184, статус 0x45af69 — +0x4c хода): 5, не 2 атаки (0x4545d8). Карта 18
        // (20261018b): иллюзия S143KC000a, дни 1 и 2 — ai_tactic_result 5.
        int moved = a.Type == AiActionType.SummonAttack ? AiTactic.StatusProgress : StatusDone;
        var path = AiTactics.Route(this, stack, a);
        var step = AiToday(a.Stack, path, a);
        var (today, fired, cut) = step;
        if (today.Count == 0) return TakeBagAtFirstStep(a.Stack, path) || StrikeInPlace(a.Stack, step, turn) ? moved : AiTactic.StatusStuck;
        var end = today[^1];
        var before = today.Count > 1 ? today[^2] : path.Points[0];
        // Объект действия команды — другой враг (вплотную к последней достижимой раньше цели, 0x5cd11f; или невидимый на пути): бой с ним.
        if (step.Attack is { } other && !other.Equals(a.Target, StringComparison.OrdinalIgnoreCase))
        {
            AiMove(a.Stack, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, step.Path, strike: other, turn: turn);
            return moved;
        }
        // Ход кончится ударом — для показа (звук ATTK на последнем шаге; флаг движка 0x43fc3c — только у отряда или крепости, не у
        // руины и лавки); правило удара — ниже, после хода, как было. Путь оборван событием — удара нет: цель нападения генератор
        // ставит только без обрыва (0x43ff9d), и статус атаки тот же (2, 0x4545d8).
        bool arrives = !cut && a.Reach == 0 && Near((end.X, end.Y), at, 1);
        bool strikes = arrives
                       && (Map.Stacks.ContainsKey(a.Target) || a.Target.Length >= 6 && a.Target.Substring(4, 2).Equals("FT", StringComparison.OrdinalIgnoreCase));
        AiMove(a.Stack, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, path, strikes);
        if (arrives && (Map.Stacks.ContainsKey(a.Target) || Map.Sites.Any(f => f.Id.Equals(a.Target, StringComparison.OrdinalIgnoreCase))))
        {
            // Бой по прибытии — и с нулём очков хода после подхода (журнал human, день 37: KC008b встаёт в 11,40 с 0 очков,
            // ap_restore seq 10269675, и бьёт KC0092 — battle_start seq 10269684).
            if (MapBattle.DefenderOf(Map, a.Target) is not null) AiBattle(a.Stack, a.Target, arrival: true);
            else Strike(a.Stack, a.Target);
        }
        return moved;
    }

    /// <summary>Сегодняшняя часть пути ИИ после упреждающей проверки событий (<see cref="PathEvents"/>, <c>0x440284</c>): отряд
    /// встаёт на клетке, где сработало бы событие; события — в ход (<see cref="MoveStack"/>). <c>Cut</c> — путь оборван событием
    /// (у ИИ — любое срабатывание, и на последней клетке): генератор хода <c>0x43fc3c</c> тогда не ставит цель нападения (<c>0x43ff9d</c>:
    /// нет нападения на отряд или крепость <c>0x440667</c>, а с ним и вора) и возвращает 0 — эффекты хода не зовут объект за концом пути
    /// (<c>0x43fc13</c> → <c>0x4408cb</c>: вход в свою крепость, руина, место, мешок, жезл), постановка жезла тоже не идёт (<c>0x440e0f</c>).
    /// Остаётся только <c>CEffectMove</c> до клетки обрыва и раскрытие (<c>0x43fe8c</c>).</summary>
    /// <para>С действием <paramref name="a"/> — и команда хода (<see cref="AiCommand"/>): путь, пересчитанный с невидимыми, и объект действия
    /// <see cref="AiStep.Attack"/> (при обрыве событием — нет).</para>
    private AiStep AiToday(string stackId, StackPath path, AiTacticOption? a = null)
    {
        string? attack = null;
        if (a is not null && Map.Stacks.TryGetValue(stackId, out var stack)) (path, attack) = AiCommand(stack, path, a);
        var (today, fired, cut) = AiToday(stackId, path.Today);
        return new AiStep(today, fired, cut, cut ? null : attack, path);
    }

    /// <summary>Команда хода ИИ (исполнение <c>0x45abd8</c> → <c>0x5cc120(…, 1, 1)</c>, затем генератор <c>0x43fc3c</c>): путь ИИ
    /// (<see cref="AiTactics.Route"/>, сквозь чужих невидимых) с последней достижимой, видящей невидимых, — отряд встаёт перед невидимым; и
    /// враждебный отряд — объект действия (<see cref="CommandPath"/>, цель — <see cref="AiTactics.CommandGoal"/>), на который команда нападёт.</summary>
    private (StackPath Path, string? Attack) AiCommand(MapStack stack, StackPath path, AiTacticOption a) =>
        CommandPath(stack, path, AiTactics.CommandGoal(this, stack, a, path), other => !Same(other.Owner, stack.Owner),
                    MoveOptionsOf(stack) with { ForbidWaterOnLand = true });

    /// <summary>Шаг ИИ: сегодняшняя часть пути, события проверки и обрыв (<see cref="AiToday(string, StackPath, AiTacticOption)"/>) и объект
    /// действия команды — отряд, на который она нападёт (<see cref="AiMove"/>); <c>Path</c> — путь команды (последняя достижимая и обрыв
    /// мешком — с невидимыми, цены точек те же).</summary>
    private readonly record struct AiStep(IReadOnlyList<PathPoint> Today, IReadOnlyList<EventFiring> Fired, bool Cut, string? Attack,
                                          StackPath Path)
    {
        public void Deconstruct(out IReadOnlyList<PathPoint> today, out IReadOnlyList<EventFiring> fired, out bool cut) =>
            (today, fired, cut) = (Today, Fired, Cut);
    }

    private (IReadOnlyList<PathPoint> Today, IReadOnlyList<EventFiring> Fired, bool Cut) AiToday(string stackId, IReadOnlyList<PathPoint> today)
    {
        var (walk, fired, cut) = PathEvents(stackId, today);
        return (walk == today.Count ? today : [.. today.Take(walk)], fired, cut);
    }

    /// <summary>Ход ИИ по сегодняшней части пути (<see cref="AiToday"/>): команда движения в конец и подбор мешков на пройденных
    /// клетках; мешок в клетке шага (и в первой) обрывает путь (<c>0x5cda67</c>), его берут эффекты хода с соседней клетки
    /// (<see cref="TakeBagAtFirstStep"/>, <c>docs/map-path.md</c> §8).</summary>
    /// <para><paramref name="strike"/> — объект действия команды (<see cref="AiStep.Attack"/>): по приходу нападение на него
    /// (<see cref="StrikeObject"/>) вместо объекта за концом пути — генератор с целью нападения эффекты <c>0x4408cb</c> не зовёт. true — напал.</para>
    private bool AiMove(string stackId, IEnumerable<PathPoint> walked, PathPoint before, int cost, IReadOnlyList<EventFiring> fired,
                        bool cut, StackPath? path = null, bool attack = false, string? strike = null, AiTacticTurn? turn = null)
    {
        var cells = walked.ToList();
        var end = cells[^1];
        MoveStack(stackId, end.X, end.Y, PathFinder.Facing(before, end), cost, fired, cells, attack || strike is not null);
        if (!cut && strike is not null && Map.Stacks.ContainsKey(stackId) && Map.Stacks.ContainsKey(strike))
            return StrikeObject(stackId, strike, turn);
        foreach (var p in cells)
            if (Map.Stacks.ContainsKey(stackId) && BagAt(p.X, p.Y) is { } bag)
                Enter(stackId, bag.Id);
        // Встал перед мешком на пути (0x5cda67: мешок в клетке — стоп) — берёт его с соседней клетки: эффекты хода 0x43fbac → 0x4408cb
        // (объект в клетке за концом — мешок 0x27) → CEffectTakeBag. Журнал human, день 10: S073KC0076 встаёт на 24,34 с 21 очком перед
        // S073BG0027 (23,33), и к следующей оценке предмет мешка S073IM0043 уже в отряде, мешка в сохранении дня 11 нет.
        // Мешок — клетка обрыва пути (+0x10 искателя после «идти» = 0, 0x5cd984; StackPath.CutAt): её и читает 0x4408cb.
        // Путь оборван событием — эффекты хода объект за концом не зовут (0x43fc13).
        if (!cut && path is not null && cells.Count == path.Today.Count && path.CutAt is { } bagCell
            && Map.Stacks.ContainsKey(stackId) && BagAt(bagCell.X, bagCell.Y) is { } ahead)
            Enter(stackId, ahead.Id);
        return false;
    }

    /// <summary>Команда ИИ без шагов, у которой есть объект действия (<see cref="AiStep.Attack"/>: враг вокруг старта, <c>0x5cd11f</c>) — генератор
    /// хода (<c>0x43fc3c</c>) всё равно строит <c>CEffectMove</c> со списанием конца присланного списка (<see cref="StackPath.ChargeFor"/>) и нападает
    /// (<c>0x43ff9d</c>). Журнал 20261018-port-synth, карта 13, CA2: охотник Кланов, оборванный событием на (12,82) вплотную к невидимке (13,82),
    /// вторым выбором <c>AttackStack</c> на приманку (17,82) — <c>stack_move</c> (12,82) seq 855759, списано 6 (16 → 10, seq 855767), 10 за нападение,
    /// засада (<c>battle_start</c> 855776: нападает невидимый <c>S143KC001c</c>). Порт до 18.10 такой команды не исполнял (статус 1, охотник стоял с 16).
    /// Отряд в крепости — как прежде (выхода без шага движок не описан). Объект действия у команды есть, только если очки хода были
    /// (<see cref="CommandPath"/>, <c>0x43fe51</c>); списание — до боя, как <c>CEffectMove</c> до <c>CEffectBattle</c>. true — напал.</summary>
    private bool StrikeInPlace(string stackId, AiStep step, AiTacticTurn? turn)
    {
        if (step.Cut || step.Attack is not { } foe || !Map.Stacks.TryGetValue(stackId, out var stack) || stack.Inside.Length > 0
            || !Map.Stacks.ContainsKey(foe)) return false;
        ActInPlace(stackId, attack: true, step.Path.ChargeFor(0));
        return Map.Stacks.ContainsKey(stackId) && StrikeObject(stackId, foe, turn);
    }

    /// <summary>Нападение команды хода ИИ на объект действия пути (<c>0x43ff9d</c> → <c>0x440667</c>): вор у видимой цели — свиток вора
    /// (<c>0x4407c2</c> → <c>0x442de5</c>), иначе бой (очки хода были на момент команды — бой и с нулём после подхода; невидимый объект
    /// нападает сам, <see cref="StartBattle"/>). Возвращает, было ли действие.</summary>
    private bool StrikeObject(string stackId, string foe, AiTacticTurn? turn)
    {
        var stack = Map.Stacks[stackId];
        if (AiTactics.KindOf(this, stack) == AiStackKind.Thief && !Invisible(foe))
        {
            AiTactics.ThiefVisit(this, turn?.Rng ?? (IRng)Rng, stackId, foe);
            return true;
        }
        return AiBattle(stackId, foe, arrival: true);
    }

    /// <summary>Мешок в первой клетке шага (<c>0x5cda67</c>: «идти» = 0, путь обрезан на клетке отряда <c>0x5cd4f5</c>): шага и списания
    /// нет, отряд берёт мешок (вход в него) — возвращает, взят ли; эффекты хода берут мешок с соседней клетки (<c>0x43fbac</c> → <c>0x4408cb</c> по клетке <c>+0x10</c>). Журналы: legions день 17
    /// (<c>KC0079</c>, <c>BG0001</c>), human дни 1 и 6 — предмет мешка в отряде к следующей оценке (разбор 28.09).</summary>
    private bool TakeBagAtFirstStep(string stackId, StackPath path)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var s) || s.Move <= 0 || path.Reachable != 0
            || path.CutAt is not { } cut || BagAt(cut.X, cut.Y) is not { } bag) return false;
        Enter(stackId, bag.Id);
        return true;
    }

    private MapSite? BagAt(int x, int y) => Map.Sites.FirstOrDefault(b => b.Kind == SiteKind.Bag && !b.Gone && b.X == x && b.Y == y);

    /// <summary>Выселение (<c>CAiKickOutStackAction</c>, исполнение <c>0x45a82a</c>): путь гостя из крепости в точку действия
    /// (<c>0x5cc6da</c>, режим 1) и команда хода (<c>0x41e46b</c>) — шаг за ворота бесплатен; статус 2 (конструктор ставит
    /// <c>+0x58</c> = 2). Выселяющий в этом исполнении не входит — вход делает следующая оценка.</summary>
    private int KickOutGuest(AiTacticOption a)
    {
        if (!Map.Stacks.TryGetValue(a.Guest, out var guest)) return AiTactic.StatusStuck;
        var path = AiTactics.Route(this, guest, a with { Stack = guest.Id, Fort = null, Guest = "" });
        // Гость без очков хода (только что взял крепость) выходит за ворота: команда хода (0x41e46b) проходит бесплатные шаги, хотя
        // достижимая часть пути при нуле очков пуста (0x5cd5b2). 20261016d день 1: S143KC0034 взял S143FT000c (запас 0), S143KC003b
        // выселяет его — stack_move в (112,132) с move_left 0 (seq 738359), затем входит сам; порт оставлял гостя, и вход срывался.
        var (today, fired, cut) = path.Reachable == 0 && guest.Move <= 0 && guest.Inside.Length > 0
            ? AiToday(guest.Id, [.. path.Points.Skip(1).TakeWhile(p => p.Cost == 0)])
            : AiToday(guest.Id, path.Today);
        if (today.Count == 0) return TakeBagAtFirstStep(guest.Id, path) ? StatusDone : AiTactic.StatusStuck;
        var before = today.Count > 1 ? today[^2] : path.Points[0];
        AiMove(guest.Id, today, before, path.Points[today.Count].Cost, fired, cut, path);
        return StatusDone;
    }

    /// <summary><c>StackEmptyMoveAllowance</c> (<c>0x41e507</c> → <c>0x42eb1d</c>): очки хода отряда — 0.</summary>
    private void EmptyMoveAllowance(string stackId)
    {
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stacks[stackId] with { Move = 0 };
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
    }

    /// <summary>Точка атаки: отряд — его позиция (<c>0x5ed9b2</c>), крепость — вход; нет цели — null.</summary>
    private (int X, int Y)? AttackPoint(string targetId)
    {
        if (Map.Stacks.TryGetValue(targetId, out var target)) return AiTactics.Position(Map, target);
        return AiTactics.Forts(Map).FirstOrDefault(f => f.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase)) is { } fort
            ? AiTactics.Entrance(fort) : null;
    }

    /// <summary>Порог «напрямую» (<c>0x450558</c>, ранг 1): клетка цели под туманом игрока — обзор лидера (<c>IUsStackLeader</c> слот
    /// <c>0x10</c>), иначе — видна или вне карты — 10. <c>0x5f3c10</c> — <c>CMidgardMapFog::getFog</c>: истина, когда клетка СКРЫТА (тулсет,
    /// <c>luaApi.md</c> «Fog»), и 10 берётся при нуле. До 25.09 порт читал наоборот: human, <c>KC0088</c> дни 56–71 — слабая атака на
    /// видимый <c>KC0035</c> в 8 клетках у движка ждёт на месте (N = 10), у порта шла на подход (N = обзор 5).</summary>
    internal int DirectReach(MapStack stack, (int X, int Y) at)
    {
        bool hidden = Map.Players.TryGetValue(stack.Owner, out var owner) && Map.Fog.GetValueOrDefault(owner.FogId) is { } fog
                      && Map.Contains(at.X, at.Y) && fog[at.X, at.Y];
        return hidden ? ScoutRange(stack) : AttackBlindReach;
    }

    /// <summary>Исполнение выбранного действия тактики (слот 7): статус и ключ памяти (отряд, цель, точка) — у производных
    /// кандидатов при готовой атаке ключ их собственный (<see cref="ExecutePrepared"/>). <paramref name="turn"/> — ход тактики
    /// игрока: его кошелёк тратит обучение у тренера.</summary>
    public (int Status, AiTacticOption Key) ExecuteTactic(AiTacticOption a, AiTacticTurn? turn = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.Prep is null ? (Execute(a, turn), a with { Stack = a.KeyStack }) : ExecutePrepared(a, turn);
    }

    /// <summary>
    /// Посещение торговца или мага вплотную (<c>CAiVisitMerchantAction</c> vtable <c>0x6d247c</c>, <c>CAiVisitMageAction</c>
    /// <c>0x6d239c</c>; <see cref="AiTacticOption.Direct"/>): исполнение <c>0x46b59b</c> — шаг на площадку (<c>site_visit</c>, очки
    /// хода не тратятся), колбэк посещения — решение ИИ (<see cref="AiTactics.BuyAtMerchant"/>, <see cref="AiTactics.BuyAtMage"/>)
    /// на R250 партии; статус <c>0x46a9de</c> — при 5 <c>+0x64</c> = 2. Издалека к ним идёт обычное перемещение (статус 5):
    /// дойдя, движок тоже посещает площадку, но без покупок — покупает следующая оценка (журналы: 12 и 7 таких пар).
    /// </summary>
    private int VisitShop(AiTacticOption a, AiTacticTurn? turn)
    {
        D2.Rules.Rng.IRng rng = turn?.Rng ?? (D2.Rules.Rng.IRng)Rng;
        if (a.Type == AiActionType.VisitMerchant) AiTactics.BuyAtMerchant(this, turn, rng, a.Stack, a.Target);
        else AiTactics.BuyAtMage(this, turn, rng, a.Stack, a.Target);
        return StatusDone;
    }

    /// <summary>
    /// Посещение тренера (<c>CAiVisitTrainerAction</c>, vtable <c>0x6d2714</c>): исполнение <c>0x46c982</c> — перемещение
    /// <c>0x45abd8</c> к входу (режим 1, N = 0), и если путь сегодня заходит на площадку (или отряд уже вплотную), движок
    /// посещает её (<c>site_visit</c>) и зовёт решение ИИ (<see cref="AiTactics.TrainAtSite"/>, колбэк <c>0x46d26c</c>). Статус
    /// (<c>0x46cc93</c>) — статус перемещения, а при 5 — <c>+0x58</c>, который исполнение ставит в 1: всегда 1. Журналы human
    /// и legions: у всех 22 исполнений 1 — и дойдя, и на полпути, так что в тот же день тренер больше не предлагается.
    /// </summary>
    private int VisitTrainer(AiTacticOption a, AiTacticTurn? turn)
    {
        var stack = Map.Stacks[a.Stack];
        bool arrived = Near(PathFinder.Start(Map, stack), (a.X, a.Y), 1);
        if (!arrived)
        {
            var path = AiTactics.Route(this, stack, a);
            var step = AiToday(a.Stack, path, a);
            var (today, fired, cut) = step;
            if (today.Count == 0) return AiTactic.StatusStuck;
            var end = today[^1];
            var before = today.Count > 1 ? today[^2] : path.Points[0];
            // Напал на врага у последней достижимой (0x5cd11f) — посещения нет: генератор с целью нападения объект за концом не зовёт.
            if (AiMove(a.Stack, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, step.Path, strike: step.Attack, turn: turn))
                return AiTactic.StatusStuck;
            // Оборван событием — посещения нет (0x43fc13: объект за концом не зовётся), а с ним и колбэка решения ИИ.
            arrived = !cut && today.Count == path.Points.Count - 1 && Near((end.X, end.Y), (a.X, a.Y), 1);
        }
        if (arrived) AiTactics.TrainAtSite(this, turn, a.Stack, a.Target);
        return AiTactic.StatusStuck;
    }

    /// <summary>
    /// Найм у наёмника (<c>CAiHireMercsAction</c>, vtable <c>0x6d0ea4</c>): исполнение <c>0x4594e2</c> — перемещение <c>0x45abd8</c> к
    /// входу (режим 1, N = 0); путь сегодня заходит в лагерь (или отряд уже вплотную) — посещение и решение ИИ
    /// (<see cref="AiTactics.HireAtMercs"/>, колбэк <c>0x459504</c>) на R250 партии. Статус (<c>0x46cc93</c>) — статус перемещения, а
    /// при 5 — <c>+0x58</c>: 2; правка на 1 после найма в посещении в память не попадает (колбэк позже). Сдвинуться некуда — 1. Посещение храма вплотную
    /// (<c>CAiVisitTempleAction</c>: исполнение <c>0x46c982</c> — то же, что у тренера) — покупки <see cref="AiTactics.BuyAtTemple"/>,
    /// статус всегда 1.
    /// </summary>
    private int VisitMercs(AiTacticOption a, AiTacticTurn? turn)
    {
        var stack = Map.Stacks[a.Stack];
        bool arrived = Near(PathFinder.Start(Map, stack), (a.X, a.Y), 1);
        if (!arrived)
        {
            var path = AiTactics.Route(this, stack, a);
            var step = AiToday(a.Stack, path, a);
            var (today, fired, cut) = step;
            if (today.Count == 0) return AiTactic.StatusStuck;
            var end = today[^1];
            var before = today.Count > 1 ? today[^2] : path.Points[0];
            // Напал на врага у последней достижимой (0x5cd11f) — посещения нет: генератор с целью нападения объект за концом не зовёт.
            if (AiMove(a.Stack, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, step.Path, strike: step.Attack, turn: turn))
                return StatusDone;
            // Оборван событием — посещения нет (0x43fc13: объект за концом не зовётся), а с ним и колбэка решения ИИ.
            arrived = !cut && today.Count == path.Points.Count - 1 && Near((end.X, end.Y), (a.X, a.Y), 1);
        }
        D2.Rules.Rng.IRng rng = turn?.Rng ?? (D2.Rules.Rng.IRng)Rng;
        // Найм идёт в колбэке посещения уже после того, как фаза 4 прочла статус (как у знатного): в память ложится 2, а не 1 найма —
        // 4 из 4 наймов в корпусе (2026101[6bcd]-port-synth, S143KC001a у S143SI0004: ai_tactic_result 2 раньше site_buy_unit).
        if (arrived) AiTactics.HireAtMercs(this, turn, rng, a.Stack, a.Target);
        return StatusDone;
    }

    /// <summary>
    /// Производный кандидат атаки (<c>docs/ai-spells.md</c> §5). Итоговая атака (<c>CAiTacticActionRefCount</c>) и любой
    /// производный, пока атака не готова (флаг <c>+0x50</c>, <see cref="AiTactics.AttackReady"/>), — это
    /// исполнение атаки: подход, отдаёт её статус, память — по её ключу. Готова — своё действие:
    /// <list type="bullet">
    /// <item>заклинание (<c>0x458982</c>): проверка <c>0x5dbd75</c> в точке литья со вторым кошельком действия; можно —
    /// литьё (<see cref="CastSpell"/>), призыву — приказ <c>L_ATTACK_STACK</c> на цель; статус всегда 2 (<c>+0x5c</c>),
    /// ключ — (атакующий, в кого бросали, точка литья);</item>
    /// <item>подмога (<c>0x458c45</c> → <c>0x45abd8</c>): перемещение помощника к точке атаки в режиме 1, статус 5
    /// отдаётся как 2 (<c>0x4545d8</c>), некуда — 1; ключ — (помощник, цель, точка атаки).</item>
    /// </list>
    /// Помощник, дошедший вплотную к цели (или уже стоявший рядом), действует по ней, как ход в клетку врага: боец — бой
    /// (<see cref="Strike"/>), знатный — свиток вора (<see cref="AiTactics.ThiefVisit"/>). Журналы (28.09, класс <c>0x6d0dc4</c>, четыре
    /// партии): из 27 исполнений подмоги 16 кончились вплотную — у всех бой (9) или <c>noble_choose</c> (7); у 11 не дошедших — ни
    /// того ни другого (human, день 37: вор <c>KC0077</c> и боец <c>KC0079</c> у <c>KC006c</c>). Проверка 26.09 («не нападает» лучше)
    /// шла при ключе памяти по атакующему (<see cref="AiTacticOption.KeyStack"/>).
    /// </summary>
    private (int Status, AiTacticOption Key) ExecutePrepared(AiTacticOption a, AiTacticTurn? turn)
    {
        var prep = a.Prep!;
        var stack = Map.Stacks[a.Stack];
        if (prep.Kind == AiPrepKind.Attack || !AiTactics.AttackReady(this, stack, a)) return (ExecuteAttack(a, turn), a);
        if (prep.Kind == AiPrepKind.Spell)
        {
            var key = a with { Target = prep.CastTarget, X = prep.X, Y = prep.Y };
            bool summon = RequireBook().Spells.TryGetValue(prep.Id, out var spell) && spell.Category == SpellCategory.Summon;
            CastSpell(stack.Owner, prep.Id, prep.X, prep.Y, prep.SpellBank, summon ? a.Target : null);
            return (StatusDone, key);
        }
        var move = a with { Stack = prep.Id, Direct = false, Reach = 0 };
        if (!Map.Stacks.TryGetValue(prep.Id, out var helper)) return (AiTactic.StatusStuck, move);
        var at = AttackPoint(a.Target);
        if (at is { } near && Near(AiTactics.Position(Map, helper), near, 1))
            return (HelperStrike(helper.Id, a.Target, turn) ? StatusDone : AiTactic.StatusStuck, move);
        var path = AiTactics.Route(this, helper, move);
        var step = AiToday(helper.Id, path, move);
        var (today, fired, cut) = step;
        if (today.Count == 0) return (AiTactic.StatusStuck, move);
        var end = today[^1];
        var before = today.Count > 1 ? today[^2] : path.Points[0];
        // Объект действия команды (враг у последней достижимой или сама цель, 0x5cd38a) — нападение по приходу (вор — свиток).
        if (AiMove(helper.Id, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, step.Path, strike: step.Attack, turn: turn))
            return (StatusDone, move);
        // Оборван событием — ни боя, ни вора (0x43ff9d: цели нападения нет).
        if (!cut && at is { } point && Near((end.X, end.Y), point, 1)) HelperStrike(helper.Id, a.Target, turn);
        return (StatusDone, move);
    }

    /// <summary>Помощник рядом с целью атаки: знатный — свиток вора, иначе удар (<see cref="Strike"/>). Цели уже нет — ничего.</summary>
    private bool HelperStrike(string helperId, string targetId, AiTacticTurn? turn)
    {
        if (!Map.Stacks.TryGetValue(helperId, out var helper)
            || !(Map.Stacks.ContainsKey(targetId) || Map.Sites.Any(f => f.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))))
            return false;
        if (AiTactics.KindOf(this, helper) != AiStackKind.Thief) return Strike(helperId, targetId);
        AiTactics.ThiefVisit(this, turn?.Rng ?? (IRng)Rng, helperId, targetId);
        return true;
    }

    /// <summary>Смена приказа (<c>CAiChangeOrderAction</c>, vtable <c>0x6cfbec</c>): команда приказа (<c>CmdChangeOrder</c>) с новым
    /// приказом <see cref="AiTacticOrders.ChangeOrder"/> и пустой целью — основной или приказ ИИ; статус 1 (журналы: 4 из 4 —
    /// <c>ai_order</c> «bezerk» с причиной <c>change_order_action</c>, затем <c>ai_tactic_result</c> 1).</summary>
    private int ExecuteChangeOrder(AiTacticOption a)
    {
        if (!Map.Stacks.TryGetValue(a.Stack, out var stack)) return AiTactic.StatusStuck;
        if (AiTacticOrders.ChangeOrder((StackOrder)stack.Order, (StackOrder)stack.AiOrder,
                                       AiTactics.KindOf(this, stack) is AiStackKind.Thief or AiStackKind.Rod,
                                       AiTactics.NeutralOwner(this, stack.Owner)) is not var (order, ai))
            return AiTactic.StatusStuck;
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = ai ? stack with { AiOrder = (int)order, AiOrderTarget = "" } : stack with { Order = (int)order, OrderTarget = "" };
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
        return AiTactic.StatusStuck;
    }

    /// <summary>Дошёл до места (<c>0x45b2c8</c> → <c>0x4500bb</c>): команда приказа <c>0x41f21d</c> — <c>L_STAND</c> без цели; при
    /// действующем <c>L_NORMAL</c> — приказ ИИ (ранг 2 — как флаг трактует <see cref="ExecuteChangeOrder"/>); статус 1.</summary>
    private int ExecuteStand(AiTacticOption a)
    {
        if (!Map.Stacks.TryGetValue(a.Stack, out var stack)) return AiTactic.StatusStuck;
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack.Order == (int)StackOrder.Normal
            ? stack with { AiOrder = (int)StackOrder.Stand, AiOrderTarget = "" }
            : stack with { Order = (int)StackOrder.Stand, OrderTarget = "" };
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
        return AiTactic.StatusStuck;
    }

    /// <summary>Удар по цели рядом: бой, а крепость без защитников (у отряда-цели защитник есть всегда) — вход с захватом.</summary>
    private bool Strike(string attackerId, string targetId) =>
        MapBattle.DefenderOf(Map, targetId) is null
            ? Map.Stacks.TryGetValue(attackerId, out var s) && s.Move > 0 && Enter(attackerId, targetId).Refused is null
            : AiBattle(attackerId, targetId);

    /// <summary>Порог «напрямую» для невидимой цели (<c>0x4505c0</c>: <c>push 0xa</c>).</summary>
    private const int AttackBlindReach = 10;

    /// <summary>Бой ИИ без экрана: <see cref="StartBattle"/>, автобой обеих сторон моделью на генераторе партии,
    /// <see cref="FinishBattle"/>. Отряд без очков хода или цель без защитников — боя нет; дуэль вора (очки уже списаны
    /// действием) идёт и без очков. Бой может сыграть наблюдатель (<see cref="ITurnObserver.Battle"/>).</summary>
    private bool AiBattle(string attackerId, string targetId, bool duel = false, bool arrival = false)
    {
        if (!Map.Stacks.TryGetValue(attackerId, out var attacker) || attacker.Move <= 0 && !duel && !arrival
            || MapBattle.DefenderOf(Map, targetId) is null) return false;
        var state = StartBattle(attackerId, targetId, duel);
        // Наблюдатель может сыграть бой снаружи (экран боя с человеком); нет журнала — автобой.
        if (Observer?.DecideBattle(state) is { Log: var played })
        {
            FinishBattle(played);
            return true;
        }
        var rules = Book!.Rules;
        var battle = new D2.Data.Battle.BattleSession(state, new BattleSim(state, Rng, rules.Constants), rules.Constants) { Auto = true };
        battle.PlayOut();
        FinishBattle(battle.Log);
        return true;
    }

    /// <summary>
    /// Знатный у цели (<c>docs/ai-tactic-candidates.md</c> §12). Издалека <c>NobleStack</c>/<c>NobleCity</c> — действие-перемещение
    /// (<c>0x6d1184</c>), вплотную и у торговца — свои классы (<c>0x6d15ac</c>, <c>0x6d1404</c>, <c>0x6d14d4</c>) с тем же
    /// исполнением <c>0x45abd8</c>: путь к цели, и если сегодня он её достигает, сервер открывает свиток вора, а ИИ решает в колбэке
    /// (слот 23 второй таблицы, <see cref="AiTactics.ThiefVisit"/>). Колбэк приходит уже после того, как фаза 4 прочла статус
    /// (журнал: <c>noble_choose</c> → <c>ai_tactic_result</c> 5 → <c>thief_roll</c>), поэтому записан статус перемещения — 5, а
    /// его правка колбэком (1 у своих классов <c>0x45e346</c>, 2 у отмены <c>0x45af47</c>) в память не попадает. Путь «достигает»
    /// цели: у отряда — сегодняшняя часть кончается рядом с ним (дальше шагнуть нельзя — клетка рядом с врагом), и тогда действие идёт
    /// даже без очков хода (журнал human, день 54: дуэль <c>S073KC009d</c> с запасом 0); у крепости и площадки — сегодняшняя часть
    /// заходит в точку, а вор встаёт перед ней (legions, день 32: <c>S073KC0089</c> крадёт у <c>S073SI0007</c> с 7,29 при входе
    /// 6,30). Сдвинуться некуда и цель не рядом — 1.
    /// </summary>
    private int ExecuteThief(AiTacticOption a, AiTacticTurn? turn)
    {
        var stack = Map.Stacks[a.Stack];
        bool stackTarget = Map.Stacks.ContainsKey(a.Target);
        var at = (a.X, a.Y);
        var start = PathFinder.Start(Map, stack);
        bool reached = stackTarget && Near(start, at, 1);
        bool moved = false;
        if (!reached)
        {
            // Команда видит невидимых и ищет объект действия (0x5cc120(…, 1, 1), 0x5cd38a): другой враг у последней достижимой — свиток вора
            // ему (или бой, если он невидим, 0x4407c2), цель — как прежде.
            var (path, foe) = AiCommand(stack, AiTactics.Route(this, stack, a), a);
            // Вор встаёт перед точкой: клетка точки в ход не входит — и в упреждающую проверку событий тоже.
            bool intoPoint = path.Today.Count > 0 && (path.Today[^1].X, path.Today[^1].Y) == at;
            var steps = intoPoint ? path.Today.Take(path.Today.Count - 1).ToList() : path.Today;
            var (today, fired, cut) = AiToday(a.Stack, steps);
            if (cut) intoPoint = false;   // событие оборвало путь — и на последней клетке перед точкой (0x43fc13)
            int walk = today.Count;
            if (walk > 0)
            {
                var end = today[walk - 1];
                var before = walk > 1 ? today[walk - 2] : path.Points[0];
                string? other = foe is not null && !foe.Equals(a.Target, StringComparison.OrdinalIgnoreCase) ? foe : null;
                if (AiMove(a.Stack, today.Take(walk), before, path.ChargeFor(walk, cut), fired, cut, path, strike: other, turn: turn))
                    return AiTactic.StatusProgress;
                moved = true;
            }
            else if (TakeBagAtFirstStep(a.Stack, path)) return AiTactic.StatusProgress;
            var stand = walk > 0 ? (today[walk - 1].X, today[walk - 1].Y) : start;
            // Оборван событием — свитка вора нет: вор идёт из нападения 0x440667, а цели нападения при обрыве нет (0x43ff9d).
            // У крепости и площадки путь может и не заходить в точку, а кончаться рядом с ней (вход торговца, к которому путь не ведёт):
            // пройден сегодня целиком и вор у точки — достиг (20261016d день 1: S143KC002e у S143SI0005 — путь до (25,124) рядом с входом
            // (26,123), остаток хода 7, noble_action 646794 в том же исполнении; порт оставлял вора без действия, и тот шёл к мешку).
            bool besidePoint = !cut && !intoPoint && walk == steps.Count && walk == path.Points.Count - 1 && Near(stand, at, 1);
            reached = stackTarget ? !cut && Near(stand, at, 1) && path.Points.Count - 1 == walk + 1 : intoPoint || besidePoint;
            if (!reached && !moved) return AiTactic.StatusStuck;
            // Не достиг — только перемещение, свитка вора нет (28.09: прежде порт открывал его и издалека — human день 18, KC0083 из
            // 11,29 бил KC007c в 14,48, а у движка вор встаёт в 11,29 без действия, seq 4058965).
            if (!reached) return AiTactic.StatusProgress;
        }
        IRng rng = turn?.Rng ?? (IRng)Rng;
        AiTactics.ThiefVisit(this, rng, a.Stack, a.Target);
        return AiTactic.StatusProgress;
    }

    /// <summary>
    /// Передача предметов (<c>CAiGiveItemsAction</c>, vtable <c>0x6d09c4</c>, исполнение <c>0x4571f0</c>): перемещение к цели
    /// (<c>0x45abd8</c>, режим 1, N = 0), и если после него до точки ровно одна клетка (слот <c>0x34</c> обёртки), все предметы
    /// сумки уходят цели (команда <c>0x41f0c4</c>, <see cref="GiveItems"/>) и статус 1; иначе — статус перемещения 5.
    /// </summary>
    private int ExecuteGive(AiTacticOption a, AiTacticTurn? turn = null)
    {
        var stack = Map.Stacks[a.Stack];
        var path = AiTactics.Route(this, stack, a);
        var step = AiToday(a.Stack, path, a);
        var (today, fired, cut) = step;
        var start = AiTactics.Position(Map, stack);
        if (today.Count > 0)
        {
            var end = today[^1];
            var before = today.Count > 1 ? today[^2] : path.Points[0];
            if (AiMove(a.Stack, today, before, step.Path.ChargeFor(today.Count, cut), fired, cut, step.Path, strike: step.Attack, turn: turn))
                return AiTactic.StatusProgress;
        }
        var now = AiTactics.Position(Map, Map.Stacks[a.Stack]);
        if (Math.Max(Math.Abs(now.X - a.X), Math.Abs(now.Y - a.Y)) == 1)
        {
            GiveItems(a.Stack, a.Target);
            return AiTactic.StatusStuck;
        }
        return today.Count > 0 || now != start ? AiTactic.StatusProgress : AiTactic.StatusStuck;
    }

    private bool IntoOwnFort(AiTacticOption a) =>
        Map.Sites.FirstOrDefault(f => f.Id.Equals(a.Target, StringComparison.OrdinalIgnoreCase)) is { } fort
        && (a.Type is AiActionType.HealRevive or AiActionType.DefendCity or AiActionType.SecureCity or AiActionType.Retreat
                or AiActionType.HireUnit
                && fort.Owner.Equals(Map.Stacks[a.Stack].Owner, StringComparison.OrdinalIgnoreCase)
            // EnterCityRoam (0x455cdb): крепость своей подрасы, хозяина построитель не сравнивает (ранг 2 — вход после пути).
            || a.Type == AiActionType.EnterCityRoam && fort.Subrace.Equals(Map.Stacks[a.Stack].Subrace, StringComparison.OrdinalIgnoreCase));

    /// <summary>Крепость — объект сценарного приказа (<see cref="AiActionType.MoveToLocation"/>, <see cref="AiActionType.DefendLocation"/>,
    /// <see cref="AiActionType.DefendParty"/>: действие у цели <c>0x460fa0</c>, точка — угол крепости); иначе null.</summary>
    private MapSite? ScenarioFort(AiTacticOption a) =>
        a.Type is AiActionType.MoveToLocation or AiActionType.DefendLocation or AiActionType.DefendParty
            ? AiTactics.Forts(Map).FirstOrDefault(f => f.Id.Equals(a.Target, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Действие у цели. Отказ — «не сдвинулся» (1), чтобы сегодня оно больше не предлагалось.</summary>
    private int Arrive(AiTacticOption a)
    {
        switch (a.Type)
        {
            case AiActionType.PlantRod:
                return PlaceRod(a.Stack, a.X, a.Y) is null ? StatusDone : AiTactic.StatusStuck;
            case AiActionType.AttackRod:
                return BreakRod(a.Stack, a.Target) is null ? StatusDone : AiTactic.StatusStuck;
            case AiActionType.TakeBag or AiActionType.TakeBagRoam:
                return Enter(a.Stack, a.Target).Refused is null ? StatusDone : AiTactic.StatusStuck;
            case AiActionType.VisitRuin:
                // Вплотную ко входу (слот 0x2c обёртки: Чебышёв 1) — посещение: бой с охраной, после победы — добыча.
                return Strike(a.Stack, a.Target) ? StatusDone : AiTactic.StatusStuck;
            // HireUnit (0x459f0d): вход в свою крепость; юнитов гарнизона забирает подготовка (AiStackUnits.TakeFromGarrison).
            // EnterCityRoam (0x455bea, CAiEnterCityAction): нейтрал входит в пустую деревню своей подрасы; хозяина построитель не
            // сравнивает — вход проверяет Enter (своя крепость — вход, чужая пустая — захват).
            case AiActionType.EnterCityRoam:
                return Enter(a.Stack, a.Target).Refused is null ? StatusDone : AiTactic.StatusStuck;
            case AiActionType.HealRevive or AiActionType.DefendCity or AiActionType.SecureCity or AiActionType.HireUnit:
                var fort = Map.Sites.FirstOrDefault(f => f.Id.Equals(a.Target, StringComparison.OrdinalIgnoreCase));
                if (fort is null || !fort.Owner.Equals(Map.Stacks[a.Stack].Owner, StringComparison.OrdinalIgnoreCase))
                    return AiTactic.StatusStuck;
                return Enter(a.Stack, a.Target).Refused is null ? StatusDone : AiTactic.StatusStuck;
            default:
                return StatusDone;
        }
    }

    private static bool Near((int X, int Y) a, (int X, int Y) b, int reach) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) <= reach;

    /// <summary>
    /// Назначение помощников (<c>0x45603b</c>, <c>docs/ai-tactic-candidates.md</c> §3): отрядам с приказом ИИ
    /// <c>L_ASSIST</c> без цели — лидер из <see cref="AiTactics.AssistLeaders"/> по вместимости (первому 2 бойца и
    /// 1 знатного, у дипломата 2; второму 2 и 1; остальным по 1), иначе первый с наименьшим числом. Лидеров нет —
    /// помощники возвращаются к <c>L_NORMAL</c>.
    /// </summary>
    private void AssignHelpers(string playerId, List<string> report)
    {
        var book = RequireBook();
        var player = Map.Players[playerId];
        if (!(book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable)) return;
        bool diplomat = book.LordCategory(player.LordId) == 2;
        var leaders = AiTactics.AssistLeaders(this, playerId);
        var own = Map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)).ToList();
        var changes = new Dictionary<string, MapStack>(StringComparer.OrdinalIgnoreCase);
        if (leaders.Count == 0)
        {
            foreach (var s in own.Where(s => AiTactics.OrderOf(s).Order == StackOrder.Assist))
                changes[s.Id] = s with { AiOrder = (int)StackOrder.Normal, AiOrderTarget = "" };
            Apply(changes, report);
            return;
        }
        var count = leaders.Select(l => own.Count(s => s.AiOrder == (int)StackOrder.Assist
                                                    && s.AiOrderTarget.Equals(l.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
        var nobles = new int[leaders.Count];   // n1 = 0: 0x456f39 смотрит категорию лидера, не помощника
        int Cap(int i, bool noble) => i == 0 ? (noble ? (diplomat ? 2 : 1) : 2) : i == 1 ? (noble ? 1 : 2) : 1;
        foreach (var s in own.Where(s => s.AiOrder == (int)StackOrder.Assist && s.AiOrderTarget.Length == 0)
                             .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            string type = Map.Units.TryGetValue(s.LeaderId, out var leader) ? leader.TypeId : "";
            if (UnitCategory.IsSummonImpl(book.CategoryOf(type))) continue;   // 0x45673a → 0x431d8c: иллюзия — тоже IUsSummon (вид 3)
            bool noble = book.CategoryOf(type) == UnitCategory.Noble;
            var n = noble ? nobles : count;
            int pick = Enumerable.Range(0, leaders.Count).FirstOrDefault(i => n[i] < Cap(i, noble), -1);
            if (pick < 0) pick = Array.IndexOf(n, n.Min());
            n[pick]++;
            changes[s.Id] = s with { AiOrderTarget = leaders[pick].Id };
        }
        Apply(changes, report);
    }

    /// <summary>
    /// Раздача стратегических приказов — начало хода ИИ, слот 2 <c>CAiStrategic</c> (<c>0x4719f8</c> → <c>0x44e26e</c>): при
    /// <c>CScenarioInfo +0x20</c> (текущий день) = 1 — слот 3 <c>0x44e29e</c>, затем бюджет дня <c>0x446292</c>; списки сил
    /// (<c>0x4760b0</c>) к этому моменту уже собраны. Отряды игрока с действующим <c>L_NORMAL</c> по силе
    /// (<see cref="AiSpells.NormalStacks"/>): вор — в один список, жезлоносец пропускается, прочие — в другой; допуски
    /// <c>0x44b1d7</c> (<see cref="AiEconomy.LeaderFlags"/>), раздача <see cref="AiTacticOrders.Strategic"/>, приказ ИИ без
    /// цели (<c>0x5e811a</c>, флаг 1; <c>ai_order</c> с возвратом <c>0x44e6cc</c>). Отряд с новым <c>L_DEFEND_CITY</c> дальше в
    /// том же ходе берёт оперативный слой (<see cref="OperationalOrders"/>): без своих городов — снова <c>L_NORMAL</c>.
    /// Нейтралы (<c>0x5e5358</c>) не раздают. Ранг 1: журнал 20261014-replay, харнесс <c>AiStrategicOrdersHarnessTests</c>.
    /// </summary>
    private void StrategicOrders(string playerId, List<string> report)
    {
        if (Map.Turn != StrategicOrdersDay || AiTactics.NeutralOwner(this, playerId)) return;
        var book = Book!;
        var map = Map;
        var roles = AiSpells.NormalStacks(this, playerId)
            .Select(s => (s.Id, Role: map.Units.TryGetValue(s.LeaderId, out var l)
                                          ? AiStrategicRules.RoleOf(book.LeaderCategoryOf(l.TypeId)) : AiLeaderRole.Fighter))
            .ToList();
        var flags = AiEconomy.LeaderFlags(this, playerId, AiEconomy.LeaderCounts(this, playerId));
        var changes = AiTacticOrders.Strategic([.. roles.Where(r => r.Role == AiLeaderRole.Thief).Select(r => r.Id)],
                                               [.. roles.Where(r => r.Role == AiLeaderRole.Fighter).Select(r => r.Id)],
                                               flags.Assist, flags.DefendCity, flags.Steal);
        if (changes.Count == 0) return;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var c in changes)
        {
            stacks[c.Stack] = stacks[c.Stack] with { AiOrder = (int)c.Order, AiOrderTarget = c.Target };
            report.Add($"раздача {c.Stack} {c.Order}");
        }
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks), Looks.Stacks) };
    }

    /// <summary>День раздачи стратегических приказов (<c>0x44e26e</c>: <c>cmp [day], 1</c>).</summary>
    private const int StrategicOrdersDay = 1;

    /// <summary>
    /// Смена приказов оперативного слоя (<c>CAiOperational</c> фаза 2, <c>0x46dee2</c> → <c>0x46defb</c>) — после стратегии,
    /// до тактики. Свои укрепления, кроме столицы, по удалённости от неё (<c>0x474072</c>, ранг 1 по листингу: Чебышёв между
    /// входами — правыми нижними углами пятен <c>0x5e9563</c>; обход укреплений — порядок объектов карты, ранг 3) →
    /// <see cref="AiTacticOrders.CityOrder"/>; защитник без цели, с чужой целью или не при одном из двух дальних
    /// городов получает дальний (<see cref="AiTacticOrders.Operational"/>). Приказ ИИ меняется командой
    /// <c>0x41f21d</c> (<c>ai_order</c>, флаг 1). Заклинания оперативного слоя (<c>0x46d691</c>, <c>0x46e322</c>) — до и
    /// после этого шага, <see cref="OperationalBefore"/>. Нейтралы его не ведут.
    /// </summary>
    private void OperationalOrders(string playerId, List<string> report)
    {
        var map = Map;
        var capital = map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (capital is null) return;
        var home = AiTactics.Entrance(capital);
        var cities = AiTacticOrders.CityOrder(
            AiTactics.Forts(map).Where(f => f.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                            && !f.Id.Equals(capital.Id, StringComparison.OrdinalIgnoreCase))
                                .Select(f => (f.Id, Math.Max(Math.Abs(AiTactics.Entrance(f).X - home.X), Math.Abs(AiTactics.Entrance(f).Y - home.Y)))));
        // Порядок кандидатов важен («город получает последний»): список своих отрядов 0x4760b0 берётся по номеру id, как
        // очередь хода (ранг 3), а не по порядку словаря.
        var defenders = map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(s => IdKey(s.Id)).Select(s =>
        {
            var (order, target) = AiTactics.OrderOf(s);
            bool own = map.Sites.Any(f => f.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
                                          && f.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
            return new AiDefender(s.Id, order, target, own);
        });
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var changes = AiTacticOrders.Operational(cities, defenders);
        foreach (var c in changes)
        {
            stacks[c.Stack] = stacks[c.Stack] with { AiOrder = (int)c.Order, AiOrderTarget = c.Target };
            report.Add($"приказ {c.Stack} {c.Order} {c.Target}");
        }
        if (changes.Count > 0) State = State with { Looks = Retold(Looks, With(map, stacks: stacks), Looks.Stacks) };
    }

    private void Apply(Dictionary<string, MapStack> changes, List<string> report)
    {
        if (changes.Count == 0) return;
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => changes.GetValueOrDefault(p.Key) ?? p.Value, StringComparer.OrdinalIgnoreCase);
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
        foreach (var s in changes.Values) report.Add($"помощник {s.Id} → {(s.AiOrderTarget.Length > 0 ? s.AiOrderTarget : "L_NORMAL")}");
    }
}
