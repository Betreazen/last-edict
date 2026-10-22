using LastEdict.Rules.Battle;

namespace LastEdict.Data.Battle;

/// <summary>Действия боя — коды движка (<c>notifyBattleAction</c>, таблица переходов <c>0x63061f</c>,
/// <c>docs/battle-screen.md</c>): 0 удар, 1 пропустить, 2 отступить, 3 ждать, 4 защищаться, 5 автобой,
/// 6 предмет, 7 быстрый бой.</summary>
public enum BattleCommand
{
    Attack = 0,
    Skip = 1,
    Retreat = 2,
    Wait = 3,
    Defend = 4,
    Auto = 5,
    UseItem = 6,
    Resolve = 7,
}

/// <summary>Ход без удара, который экран показывает надписью и вспышкой на портрете (шаги просмотра <c>0x630f56</c>): «Пас» парализованного
/// (эффект 1, <c>0x628dce</c> → шаг 1 <c>0x635a9c</c>), «Ждать» (шаг 9), «Защита» (шаг 10; и ход окаменевшего — эффект 10, <c>0x628e81</c>).</summary>
public enum BattleTurnShow
{
    Pass,
    Wait,
    Defend,
}

/// <summary>Показ хода без удара (<see cref="BattleTurnShow"/>): чей ход и что показать.</summary>
/// <paramref name="AtTurnStart"/> — ход занят в начале хода (паралич, окаменение), а не командой «Ждать»/«Защищаться».
public sealed record BattleTurnNote(string UnitId, BattleTurnShow Kind, bool AtTurnStart = false);

/// <summary>Пометка места в очереди ходов раунда (<see cref="BattleSession.Upcoming"/>).</summary>
public enum QueueMark
{
    /// <summary>Сходит в свой черёд.</summary>
    Next,

    /// <summary>Ходит сейчас (<see cref="BattleSession.Current"/>).</summary>
    Current,

    /// <summary>Пропустит ход: паралич или окаменение (ход занят в начале хода, <c>BattleSim.BeginTurn</c>) или уйдёт с поля бегством.</summary>
    Skips,

    /// <summary>Нажал «Ждать» в этом раунде — ходит в конце раунда.</summary>
    Waits,
}

/// <summary>Место в очереди ходов: юнит и пометка; <paramref name="NextRound"/> — место следующего раунда (<see cref="BattleSession.UpcomingNext"/>).
/// Неизменяемое — снимок на миг чтения.</summary>
public sealed record QueueEntry(BattleUnit Unit, QueueMark Mark, bool NextRound = false);

/// <summary>
/// Бой по шагам для экрана: ведёт <see cref="BattleSim"/> в том же порядке, что <see cref="BattleSim.Run"/>
/// (раунд → очередь → начало хода → действие → конец хода → внеочередные), но на ходу юнита игрока
/// останавливается и ждёт команду. Ход чужого (и свой под автобоем) делает ИИ той же моделью.
///
/// <para>Экран — только отображение: какие команды сейчас доступны и какие цели допустимы, решает эта
/// сторона, ровно как в игре решает сервер (<c>docs/battle-screen.md</c> §1).</para>
/// </summary>
public sealed class BattleSession
{
    private readonly BattleSim _sim;
    private readonly BattleState _state;
    private readonly RulesConstants _constants;
    private List<BattleUnit> _queue = [];
    private int _index;
    private int _round;

    /// <summary>Предраунд двойников начала боя уже был (<see cref="BattleSim.BeginPreRound"/>).</summary>
    private bool _preRoundDone;
    private int _hit;

    public BattleSession(BattleState state, BattleSim sim, RulesConstants constants, bool attackerIsPlayer = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sim);
        _state = state;
        _sim = sim;
        _constants = constants;
        PlayerIsAttacker = attackerIsPlayer;
        _round = state.Round;
        Advance();
    }

    /// <summary>Чья сторона — игрока (левая в бою).</summary>
    public bool PlayerIsAttacker { get; }

    public BattleState State => _state;

    public BattleSim Sim => _sim;

    /// <summary>Номер раунда.</summary>
    public int Round => _round;

    /// <summary>Ходы без удара по порядку (<see cref="BattleTurnNote"/>) — для показа; модель их не читает.</summary>
    public IReadOnlyList<BattleTurnNote> TurnShows => _turnShows;

    private readonly List<BattleTurnNote> _turnShows = [];

    /// <summary>Чей сейчас ход; null — бой кончился.</summary>
    public BattleUnit? Current { get; private set; }

    /// <summary>
    /// Кто ходит сейчас и дальше в текущем раунде, по порядку — для полосы очереди ходов (<c>docs/own-version-roadmap.md</c> §3.3). Первым —
    /// ходящий (<see cref="QueueMark.Current"/>); между ходами (<see cref="TurnStartPending"/>) ходящего нет, и первым стоит тот, чьё начало хода
    /// следующее. Павшие, ушедшие с поля, спрятанные и изгнанные не показываются: в свой черёд они не ходят (<see cref="Advance"/> пропускает
    /// юнита не на поле), а воскрешённый до своего черёда — снова на поле и снова в полосе. Ходы раунда после «Ждать» и внеочередные (дар атаки)
    /// — отдельными местами, как в очереди. Следующий раунд — <see cref="UpcomingNext"/>. Бой кончился — пусто.
    /// <para>Пометки предсказывают начало хода (<c>BattleSim.BeginTurn</c>) без бросков и без изменения состояния: <see cref="QueueMark.Skips"/>
    /// — паралич или окаменение без «Исцеления» (оно снимает их в начале любого хода раньше) или бегство, если начала хода в этом раунде у юнита
    /// ещё не было (после него модель отвечает «ходит», что бы ни случилось); гибель от яда в начале хода заранее не видна.
    /// <see cref="QueueMark.Waits"/> — юнит ждал в этом раунде (<see cref="BattleUnit.Waited"/>).</para>
    /// </summary>
    public IReadOnlyList<QueueEntry> Upcoming
    {
        get
        {
            if (Finished) return [];
            var entries = new List<QueueEntry>(_queue.Count - _index);
            for (int i = _index; i < _queue.Count; i++)
            {
                var unit = _queue[i];
                if (i == _index && ReferenceEquals(unit, Current)) entries.Add(new QueueEntry(unit, QueueMark.Current));
                else if (unit.OnField) entries.Add(new QueueEntry(unit, Skips(unit) ? QueueMark.Skips : unit.Waited ? QueueMark.Waits : QueueMark.Next));
            }
            return entries;
        }
    }

    /// <summary>
    /// Очередь следующего раунда (Р4, Р17): его случайная часть инициативы брошена в начале текущего раунда (<see cref="BattleSim.NextRound"/>),
    /// порядок — по характеристикам на этот миг, поэтому замедление, превращение, гибель, призыв и воскрешение двигают её сразу; если до конца
    /// раунда ничего не изменится, раунд пойдёт ровно так (ходы «Ждать» и дар атаки следующего раунда — ещё не известны). Места —
    /// <see cref="QueueMark.Next"/> с <see cref="QueueEntry.NextRound"/>. Сторона пала (дальше — лекари победителя или конец) — пусто.
    /// </summary>
    public IReadOnlyList<QueueEntry> UpcomingNext =>
        Finished || _healers || _sim.Winner is not null ? [] : [.. _sim.NextRound().Select(u => new QueueEntry(u, QueueMark.Next, NextRound: true))];

    /// <summary>Полоса очереди на два раунда: остаток текущего (<see cref="Upcoming"/>) и следующий (<see cref="UpcomingNext"/>).</summary>
    public IReadOnlyList<QueueEntry> TwoRounds => [.. Upcoming, .. UpcomingNext];

    /// <summary>Очередь ходов (<see cref="TwoRounds"/>) изменилась: новый раунд, конец хода, «Ждать», дар атаки, гибель, бегство, воскрешение,
    /// паралич, замедление, очередь лекарей после боя. Зовётся в конце команды сессии, только если очередь стала другой.</summary>
    public event Action? QueueChanged;

    /// <summary><see cref="TwoRounds"/> на миг последнего <see cref="QueueChanged"/>.</summary>
    private IReadOnlyList<QueueEntry>? _shownQueue;

    /// <summary>Сообщить об изменении очереди, если оно есть (без подписчиков — ничего не считается).</summary>
    private void NotifyQueue()
    {
        if (QueueChanged is null) return;
        var now = TwoRounds;
        if (_shownQueue is not null && _shownQueue.SequenceEqual(now)) return;
        _shownQueue = now;
        QueueChanged();
    }

    /// <summary>Пропустит ли юнит свой черёд — <c>BattleSim.BeginTurn</c> без бросков: повторное начало хода в раунде — ходит; паралич или
    /// окаменение (не снятые «Исцелением», <c>Attacks.ResolveCured</c>) — ход занят; бегство — уходит с поля.</summary>
    private bool Skips(BattleUnit unit) =>
        unit.TurnBegunRound != _state.Round && (((unit.Paralyzed || unit.Petrified) && !unit.Cured) || unit.Retreating);

    /// <summary>Ход игрока (и автобой выключен) — экран ждёт команду.</summary>
    public bool WaitsPlayer => Current is not null && IsPlayers(Current) && !AutoOf(Current.Attacker);

    /// <summary>Автобой обеих сторон: ИИ ведёт весь бой (проверки, бой без окна). Чтение — включён ли у обеих.</summary>
    public bool Auto
    {
        get => AutoAttackers && AutoDefenders;
        set => Act(new SetAutoAction(null, value), () => (_autoAttackers, _autoDefenders) = (value, value));
    }

    /// <summary>Автобой нападающих (<c>data2+0x38</c>, переключатель <c>TOG_AUTOBATTLE</c> на их ходу).</summary>
    public bool AutoAttackers
    {
        get => _autoAttackers;
        set => SetAuto(attacker: true, value);
    }

    /// <summary>Автобой защитников (<c>data2+0x39</c>).</summary>
    public bool AutoDefenders
    {
        get => _autoDefenders;
        set => SetAuto(attacker: false, value);
    }

    private bool _autoAttackers, _autoDefenders;

    /// <summary>Автобой стороны: включён — её юниты ходят ИИ, как у движка таймер шлёт <c>Auto</c> за юнита этой стороны (<c>0x63402f</c>).</summary>
    public bool AutoOf(bool attacker) => attacker ? AutoAttackers : AutoDefenders;

    /// <summary>Включить или выключить автобой одной стороны (обработчик <c>TOG_AUTOBATTLE</c> <c>0x633f49</c>).</summary>
    public void SetAuto(bool attacker, bool on) => Act(new SetAutoAction(attacker, on), () =>
    {
        if (attacker) _autoAttackers = on;
        else _autoDefenders = on;
    });

    public bool Finished => _sim.Finished || _over || (Current is null && !TurnStartPending);

    /// <summary>
    /// Начало хода следующего юнита держит экран (<see cref="StartTurn"/>), а не конец прежнего действия. Без этого (по умолчанию) действие
    /// сразу ведёт к следующему ходящему, как <see cref="BattleSim.Run"/>, — так удобно проверкам и бою без окна. Экран боя показывает
    /// действие, его анимации и надписи, и лишь потом — начало хода следующего (тики яда, мороза, ожога, снятие паралича, откат
    /// превращения): у движка это отдельные шаги показа после показа удара (тик — <c>0x635ecc</c>, со своей картинкой, звуком и надписью).
    /// Прежде сессия считала начало следующего хода в том же вызове, что и действие, и экран показывал тик раньше удара (партия 15.10:
    /// «яд сработает ещё до окончания анимации действия предыдущего юнита»).
    /// </summary>
    public bool HoldTurnStart { get; init; }

    /// <summary>Ход кончился, а начало следующего ещё не сделано (<see cref="HoldTurnStart"/>): ходящего нет, бой не кончен.</summary>
    public bool TurnStartPending { get; private set; }

    /// <summary>Начать ход следующего юнита, если он отложен (<see cref="HoldTurnStart"/>): тики, снятие эффектов, пропуск не способных
    /// ходить, новый раунд, конец боя. Не отложен — ничего.</summary>
    public void StartTurn() => Act(new StartTurnAction(), () =>
    {
        if (!TurnStartPending) return;
        TurnStartPending = false;
        Advance();
    });

    /// <summary>Идут ходы лекарей после гибели стороны (<c>BattleMsgData+0xf48</c> бит 5 <c>afterBattle</c>).</summary>
    public bool AfterBattle => _healers && !_over;

    /// <summary>Переход к ходам лекарей уже сделан (очередь лекарей построена или их нет).</summary>
    private bool _healers;

    /// <summary>Бой окончен сессией: лекарей нет или их очередь кончилась.</summary>
    private bool _over;

    public string? Winner => _sim.Winner;

    /// <summary>Записи ходов боя, как их ведёт модель.</summary>
    public IReadOnlyList<TurnRecord> Log => _sim.Log;

    /// <summary>Команды, доступные сейчас (у движка их разрешает сервер, <c>0x62f823</c>).</summary>
    public IReadOnlySet<BattleCommand> Allowed
    {
        get
        {
            var set = new HashSet<BattleCommand>();
            if (Current is null && !TurnStartPending) return set;
            // Автобой и быстрый бой экран не выключает никогда (docs/battle-screen.md §1.2) — и между ходами, пока показывается действие.
            set.Add(BattleCommand.Auto);
            set.Add(BattleCommand.Resolve);
            if (Current is null) return set;
            if (!WaitsPlayer) return set;
            if (AfterBattle)
            {
                // Ход лекаря после победы — только лечение раненых своих: ни «Защищаться», ни «Пропустить», ни «Ждать», ни «Отступить»,
                // ни предметов (ответ пользователя 11.10, замечание 18). В updateBattleActions (0x623d93) флага afterBattle (0xf48 бит 5,
                // геттер 0x6216db) нет — где движок урезает набор, по коду не найдено.
                if (Targets().Count > 0) set.Add(BattleCommand.Attack);
                return set;
            }
            // updateBattleActions (0x623d93): «Защищаться» — пока не сделан первый из двух ударов (бит 5 attackedOnceOfTwice, 0x623ebb →
            // 0x622f24); «Ждать» — ещё и если юнит в этом раунде не ждал (бит 4 waited, 0x623ef6 → 0x622eaf) и раунд не нулевой (0x623f19):
            // второй раз за раунд тот же юнит ждать не может — флаг снимает только новый раунд (0x622078, BattleSim.BeginRound); «Отступить» —
            // если группа может бежать (0x6240e9 → 0x5f51bf: не гарнизон, не руина, не обороняющийся отряд в городе), без первого из двух
            // ударов и с первого раунда (0x6240fe, 0x62410f).
            bool firstHit = _hit == 0;
            if (firstHit) set.Add(BattleCommand.Defend);
            set.Add(BattleCommand.Skip);
            if (firstHit && _state.Round >= 1 && _state.CanRetreat(Current)) set.Add(BattleCommand.Retreat);
            if (Targets().Count > 0 || SummonSlots().Count > 0) set.Add(BattleCommand.Attack);
            if (Enumerable.Range(0, Current.BattleItems.Count).Any(i => ItemTargets(i).Count > 0 || ItemSummonSlots(i).Count > 0))
                set.Add(BattleCommand.UseItem);
            if (firstHit && _state.Round >= 1 && !Current.Waited) set.Add(BattleCommand.Wait);
            return set;
        }
    }

    /// <summary>Кого ходящий может бить (или лечить) — <see cref="BattleTargets.Possible"/>; у хода человека без автобоя — список сервера
    /// человеку (<c>human</c>: без фильтра <c>Always</c>, у превращения себя — и сам). У призыва человека целей-юнитов нет — цели позиции,
    /// <see cref="SummonSlots"/>.</summary>
    public IReadOnlyList<BattleUnit> Targets()
    {
        if (Current is not { } unit) return [];
        if (WaitsPlayer && unit.Attack.Class == AttackClass.Summon) return [];
        return BattleTargets.Possible(_state, unit, unit.Attack, human: WaitsPlayer);
    }

    /// <summary>
    /// Позиции, куда человек может призвать атакой ходящего (класс <c>Summon</c>): <c>fillTargetsList</c> призыва (<c>0x662ac6</c> →
    /// <c>0x662b9a</c> → по досягаемости <c>0x662ca6</c>/<c>0x662d1c</c>) обходит позиции 0..5 своей группы, цель — позиция, а не юнит, и
    /// годна она и на месте павшего или ушедшего (<c>method17</c> призыва <c>0x66386f</c> — <c>mov al,1</c>; корпус
    /// <c>20260909-arena1/map-001 [124189]</c> seq 124380: призванный <c>S143UN001c</c> встал на позицию 4 павшего <c>S143UN0011</c>). Годна
    /// позиция без живого своего — <see cref="BattleState.SummonCells"/> (обе клетки живого большого заняты). Игрок выбирает её щелчком по
    /// клетке поля или по месту в панели (метки <c>MRKEMPTYSMALL</c>/<c>EMPTYSELSMALL</c>, как у сферы призыва) — прежде порт призывал сам в
    /// первую свободную, и выбрать было нельзя (партия 15.10, пометка 3). ИИ и автобой — как прежде.
    /// </summary>
    public IReadOnlyList<int> SummonSlots()
    {
        if (!WaitsPlayer || AfterBattle || Current is not { Attack.Class: AttackClass.Summon } unit) return [];
        return _state.SummonCells(unit);
    }

    /// <summary>Призыв на выбранную позицию (<see cref="SummonSlots"/>); не годна — ничего.</summary>
    public IReadOnlyList<TurnRecord> SummonAt(int slot) => Act(new SummonAction(slot), () => SummonTurn(slot));

    private IReadOnlyList<TurnRecord> SummonTurn(int slot)
    {
        if (Current is not { } unit || !SummonSlots().Contains(slot)) return [];
        _state.SummonSlot = slot;
        try
        {
            var records = _sim.Act(_round, unit);
            FinishTurn();
            return records;
        }
        finally
        {
            _state.SummonSlot = null;
        }
    }

    public bool CanTarget(BattleUnit target) =>
        Current is not null && target is not null && Targets().Any(u => ReferenceEquals(u, target));

    /// <summary>Удар (или лечение) по цели; у атаки «по всем» цель не нужна.</summary>
    public IReadOnlyList<TurnRecord> Attack(BattleUnit? target) => Act(new AttackAction(target?.Id), () =>
    {
        if (Current is not { } unit) return [];
        if (target is not null && !CanTarget(target)) return [];
        return Strike(unit, target);
    });

    /// <summary>Удар без проверки цели по списку игрока — ход ИИ: его цель решает политика (<see cref="BattleSim.Decide"/>), и она бывает вне
    /// списка игрока — превращение себя ИИ выбирает сам (<see cref="AiRareTargets"/>), а в списке без человека себя нет. Прежде такой ход
    /// отклонялся без конца хода, и автобой стоял на месте до потолка действий (A3 карты port-synth-1-battle: Двойник скопировал Повелителя
    /// волков, 10 000 действий во 2-м раунде).</summary>
    private IReadOnlyList<TurnRecord> Strike(BattleUnit unit, BattleUnit? target)
    {
        var records = _sim.Act(_round, unit, unit.Attack.Reach == AttackType.ReachAll ? null : target);
        _hit++;
        // У ATCK_TWICE ход состоит из двух ударов: решение о второй цели принимается заново.
        if (_hit < Attacks.AttacksPerTurn(unit) && Winner is null && unit.OnField && Targets().Count > 0)
        {
            NotifyQueue();   // первый удар двойной атаки мог убить или парализовать стоящих в очереди
            return records;
        }
        FinishTurn();
        return records;
    }

    /// <summary>«Защищаться» (действие 4) человека: после первого удара двойной атаки нельзя (<c>updateBattleActions</c> <c>0x623ebb</c>, бит 5
    /// <c>attackedOnceOfTwice</c>), как и «Ждать» (<see cref="Wait"/>) и «Отступить» (<see cref="Retreat"/>) — ход доводится только ударом.</summary>
    /// <returns>Принята ли команда.</returns>
    public bool Defend() => Act(new DefendAction(), () =>
    {
        if (Current is not { } unit || _hit > 0) return false;
        DefendTurn(unit);
        return true;
    });

    /// <summary>«Отступить» (действие 2) человека: после первого удара двойной атаки нельзя (<c>0x6240fe</c>), как <see cref="Defend"/>.</summary>
    /// <returns>Принята ли команда.</returns>
    public bool Retreat() => Act(new RetreatAction(), () =>
    {
        if (Current is not { } unit || _hit > 0) return false;
        RetreatTurn(unit);
        return true;
    });

    // Ход ИИ (BattleSim.Perform) защищается и бежит и после первого удара двойной атаки — без проверки, как у модели.
    private void DefendTurn(BattleUnit unit)
    {
        _sim.Defend(unit);
        _turnShows.Add(new BattleTurnNote(unit.Id, BattleTurnShow.Defend));
        FinishTurn();
    }

    private void RetreatTurn(BattleUnit unit)
    {
        _sim.Retreat(unit);
        FinishTurn();
    }

    /// <summary>Пропустить ход — ничего не делать (движок шлёт <c>Skip</c>).</summary>
    public void Skip() => Act(new SkipAction(), FinishTurn);

    /// <summary>Ждать: юнит уходит в конец очереди раунда (<see cref="BattleSim.Wait"/>, <c>0x6224dd</c> — первое свободное место
    /// <c>turnsOrder</c>); ход не кончается — его черёд придёт в конце этого же раунда. Сессия ходит по своей очереди (копии очереди
    /// модели), поэтому ждущий дописывается и в неё: прежде он дописывался только в очередь модели и терял ход (замечание D2 11.10).</summary>
    public bool Wait() => Act(new WaitAction(), WaitTurn);

    private bool WaitTurn()
    {
        if (Current is not { } unit || AfterBattle || _hit > 0 || !_sim.Wait(unit)) return false;
        BattleSim.RequeueWaiter(_queue, _index, unit);
        _turnShows.Add(new BattleTurnNote(unit.Id, BattleTurnShow.Wait));
        _sim.EndTurn(unit);
        _index++;
        _hit = 0;
        Current = null;
        NextTurn();
        return true;
    }

    /// <summary>Ход ИИ за текущего юнита — та же модель, что у движка (<see cref="BattleSim.Decide"/>).</summary>
    public IReadOnlyList<TurnRecord> AiStep() => Act(new AiDecideAction(), () =>
    {
        // Отложенное начало хода (HoldTurnStart) — сначала оно; дошло до хода человека — ему и решать.
        if (TurnStartPending)
        {
            StartTurn();
            if (WaitsPlayer) return [];
        }
        return AiTurn();
    });

    /// <summary>Решение ИИ за текущего юнита, чей бы он ни был (и человека — у быстрого боя и автобоя).</summary>
    private IReadOnlyList<TurnRecord> AiTurn()
    {
        if (Current is not { } unit) return [];
        var decision = _sim.Decide(unit);
        switch (decision.Action)
        {
            case AiAction.Defend: DefendTurn(unit); return [];
            case AiAction.Retreat: RetreatTurn(unit); return [];
            case AiAction.UseItem:
                if (decision.Target is { } bearerTarget && decision.ItemIndex >= 0
                    && decision.ItemIndex < unit.BattleItems.Count)
                {
                    UseItem(decision.ItemIndex, bearerTarget);
                    return [];
                }
                Skip();
                return [];
            default:
                if (decision.SummonSlot >= 0) _state.SummonSlot = decision.SummonSlot;   // место призыва, выбранное ИИ (0x5d1079)
                return Strike(unit, decision.Target);
        }
    }

    /// <summary>
    /// Цели предмета из слота ходящего (<c>getItemAttackTargets</c> <c>0x62425b</c>; действие 6 разрешает <c>updateBattleActions</c>
    /// <c>0x623d93</c>): ходит не превращённый лидер, раунд не нулевой, предмет в этом бою ещё не применён. Сфера и талисман
    /// (<c>CBatAttackItem</c> <c>0x6640ab</c>) — как атака юнита того же класса и досягаемости с лидером в роли атакующего; зелье
    /// (<c>CBatAttackUsePotion</c> <c>0x66479b</c>) — своя группа без ушедших и спрятанных (<c>0x645b7d</c>): воскрешение — только
    /// павшие, лечение — живые раненые, усиление и постоянное — живые без модификатора этого зелья (<c>0x5de495</c>).
    /// </summary>
    public IReadOnlyList<BattleUnit> ItemTargets(int index)
    {
        if (Current is not { } unit || index < 0 || index >= unit.BattleItems.Count || !unit.Type.IsLeader || unit.IsTransformed
            || _state.Round < 1 || unit.AppliedItems.Contains(index)) return [];
        var item = unit.BattleItems[index];
        if (!item.IsPotion)
            return _state.Rules?.Attacks.GetValueOrDefault(item.AttackId) is { Class: not AttackClass.Summon } attack
                ? BattleTargets.Possible(_state, unit, attack) : [];
        var group = _state.Allies(unit).Where(u => !u.Retreated && !u.Hidden);
        return [.. item.Category switch
        {
            LastEdict.Rules.Magic.ItemCategory.PotionRevive => group.Where(u => !u.Alive && !u.Revived),
            LastEdict.Rules.Magic.ItemCategory.PotionHeal => group.Where(u => u.Alive && u.Hp < u.HpMax),
            _ => group.Where(u => u.Alive && !u.Chain.Any(m => m.Id.Equals(item.PotionModifierId, StringComparison.OrdinalIgnoreCase))),
        }];
    }

    /// <summary>
    /// Щелчок по юниту, который целью быть не может (<c>0x63cce6</c>, зовёт <c>0x6345ab</c> при негодной цели): почти всегда — ничего; окно
    /// сообщения только в двух случаях. «Каждый отряд можно воскрешать один раз за сражение.» <c>X008TA0031</c> — у ходящего атака
    /// воскрешения (первая или вторая, <c>0x63cf67</c>; запасные в ванили — только урон) или тащимое зелье воскрешения <paramref name="item"/>
    /// (<c>0x63cd7d</c>), а цель — <b>павший своей стороны</b> (сторона по карте юнитов боя <c>0x63e370</c> = стороне хода <c>+0xfa1</c>,
    /// здоровье не больше 0 — <c>0x5e98ad</c>): своего павшего негодной целью воскрешения делает только прошлое воскрешение. Признак
    /// «воскрешён» движок здесь не читает. Двойник по стражу — <c>X008TA0032</c>.
    /// Партия 20.10 (<c>corpus/20261020-faces</c>, seq 525823 и 526282, <c>ui_msgbox</c> с возвратом <c>0x63d180</c>): Иерофант
    /// S143UN0011 — щелчок по павшему Сквайру S143UN0013, уже воскрешённому в этом бою.
    /// </summary>
    /// <returns>Id строки сообщения или null.</returns>
    public string? RefusalText(BattleUnit target, int? item = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Current is not { } unit) return null;
        bool fallenAlly = target.Attacker == unit.Attacker && target.Hp <= 0;
        if (item is { } i && i >= 0 && i < unit.BattleItems.Count)
            return unit.BattleItems[i] is { IsPotion: true, Category: LastEdict.Rules.Magic.ItemCategory.PotionRevive } && fallenAlly
                ? RevivedOnce : null;
        // Первая подходящая атака по порядку «первая, вторая» решает (0x63cf31: перебор до воскрешения или двойника).
        foreach (var attack in new[] { unit.Attack, unit.Attack2 })
        {
            if (attack?.Class == AttackClass.Revive) return fallenAlly ? RevivedOnce : null;
            if (attack?.Class == AttackClass.Doppelganger) return target.Type.Category == UnitCategory.Guardian ? NoGuardianCopy : null;
        }
        return null;
    }

    public const string RevivedOnce = "X008TA0031", NoGuardianCopy = "X008TA0032";

    /// <summary>Позиции призыва предмета из слота ходящего: у предмета с атакой-призывом цели — позиции призыва своей группы
    /// (<see cref="BattleState.SummonCells"/>; метка <c>EMPTYSELSMALL</c>, <c>0x63460d</c>), при тех же условиях, что <see cref="ItemTargets"/>.</summary>
    public IReadOnlyList<int> ItemSummonSlots(int index)
    {
        if (Current is not { } unit || index < 0 || index >= unit.BattleItems.Count || !unit.Type.IsLeader || unit.IsTransformed
            || _state.Round < 1 || unit.AppliedItems.Contains(index)) return [];
        var item = unit.BattleItems[index];
        if (item.IsPotion || _state.Rules?.Attacks.GetValueOrDefault(item.AttackId) is not { Class: AttackClass.Summon }) return [];
        return _state.SummonCells(unit);
    }

    /// <summary>Предмет-призыв, брошенный на пустую позицию: призванный встаёт на неё (<see cref="BattleState.SummonSlot"/>).</summary>
    /// <returns>Применён ли.</returns>
    public bool UseItemAt(int index, int slot) => Act(new UseItemAtAction(index, slot), () => UseItemAtSlot(index, slot));

    private bool UseItemAtSlot(int index, int slot)
    {
        if (!WaitsPlayer || Current is not { } unit || !ItemSummonSlots(index).Contains(slot)) return false;
        _state.SummonSlot = slot;
        try
        {
            UseItem(index, unit);
        }
        finally
        {
            _state.SummonSlot = null;
        }
        return true;
    }

    /// <summary>Предмет, брошенный на цель мышью (<c>notifyUseItem</c> <c>0x4d8516</c> → действие 6): цель не из
    /// <see cref="ItemTargets"/> — ничего (у движка такой бросок не принимается, <c>canDrop</c> <c>0x658d45</c>).</summary>
    /// <returns>Применён ли.</returns>
    public bool UseItemOn(int index, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Act(new UseItemOnAction(index, target.Id), () =>
        {
            if (!WaitsPlayer || !ItemTargets(index).Any(u => ReferenceEquals(u, target))) return false;
            UseItem(index, target);
            return true;
        });
    }

    /// <summary>Боевой предмет или зелье из слота ходящего (действие 6, <c>UseItem</c>). Применение ложится в <see cref="ItemUses"/> — для
    /// показа (шаг 12 просмотра движка): записей хода (<see cref="Log"/>) применение предмета не даёт.</summary>
    public void UseItem(int index, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Act(new UseItemAction(index, target.Id), () => UseItemTurn(index, target));
    }

    private void UseItemTurn(int index, BattleUnit target)
    {
        if (Current is not { } unit || index < 0 || index >= unit.BattleItems.Count) return;
        var item = unit.BattleItems[index];
        var shown = BattleItemUse.ShownAttack(item, _state.Rules);
        var notes = StrikeNotes.Before(_state, unit, shown);
        int hpBefore = target.Hp;
        AttackOutcome? outcome = null;
        if (item.IsPotion) _sim.DrinkPotion(unit, index, target);
        else if (_state.Rules?.Attacks.GetValueOrDefault(item.AttackId) is { } attack)
            outcome = _sim.UseBattleItem(unit, index, attack, target);
        else return;
        _itemUses.Add(new BattleItemUse(_state.Round, unit.Id, item, target.Id, shown, outcome, hpBefore, target.Hp, notes));
        FinishTurn();
    }

    private readonly List<BattleItemUse> _itemUses = [];

    /// <summary>Применения боевых предметов и зелий за бой, по порядку (человек и ИИ).</summary>
    public IReadOnlyList<BattleItemUse> ItemUses => _itemUses;

    /// <summary>Потолок действий <see cref="PlayOut"/> — страховка от боя, где никто никого не может ранить: у движка такой
    /// бой идёт вечно (пока человек не отступит), бой без человека кончается пределом раундов.</summary>
    public const int MaxActions = 10_000;

    /// <summary>Быстрый бой (<c>BTN_RESOLVE</c>, действие 7): флаг быстрого боя (<c>0xf48</c> бит 7) включает предел раундов
    /// и в бою с человеком (<see cref="BattleState.LimitRounds"/>), затем бой доигрывается моделью (<see cref="PlayOut"/>).</summary>
    public IReadOnlyList<TurnRecord> Resolve(int maxActions = MaxActions) => Act(new ResolveAction(maxActions), () =>
    {
        _state.LimitRounds = true;
        return PlayOut(maxActions);
    });

    /// <summary>Доиграть бой моделью до конца (автобой обеих сторон), не больше <paramref name="maxActions"/> действий.</summary>
    public IReadOnlyList<TurnRecord> PlayOut(int maxActions = MaxActions) => Act(new PlayOutAction(maxActions), () =>
    {
        int from = _sim.Log.Count;
        // Отложенное начало хода — и на ходу человека сразу к решению (AiStep на нём остановился бы лишним проходом).
        while (!Finished && maxActions-- > 0)
        {
            StartTurn();
            AiTurn();
        }
        return (IReadOnlyList<TurnRecord>)[.. _sim.Log.Skip(from)];
    });

    // ---- запись действий (BattleInput): бой повторяется по ним (BattlePlay.Replay)

    private readonly List<BattleInput> _actions = [];

    /// <summary>Глубина вызова действий: пишется только внешний вызов (автобой зовёт <see cref="StartTurn"/>, ИИ — <see cref="UseItem"/>).</summary>
    private int _acting;

    /// <summary>Действия, поданные сессии снаружи, по порядку (<see cref="BattleInput"/>).</summary>
    public IReadOnlyList<BattleInput> Actions => _actions;

    /// <summary>Сыгранное до сих пор: устройство сессии и её действия; <paramref name="crtDraws"/> — числа <c>rand()</c> экрана.</summary>
    public BattlePlay Played(int crtDraws = 0) => new(new BattleSetup(PlayerIsAttacker, HoldTurnStart), [.. _actions], crtDraws);

    private T Act<T>(BattleInput action, Func<T> body)
    {
        if (_acting++ == 0) _actions.Add(action);
        try
        {
            return body();
        }
        finally
        {
            _acting--;
        }
    }

    private void Act(BattleInput action, Action body) => Act(action, () =>
    {
        body();
        return 0;
    });

    /// <summary>Исполнить записанное действие (<see cref="BattlePlay.Replay"/>): тот же метод, юниты — по id.</summary>
    public void Apply(BattleInput action)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action)
        {
            case StartTurnAction: StartTurn(); break;
            case AttackAction a: Attack(a.Target is null ? null : UnitOf(a.Target)); break;
            case SummonAction a: SummonAt(a.Slot); break;
            case DefendAction: Defend(); break;
            case RetreatAction: Retreat(); break;
            case SkipAction: Skip(); break;
            case WaitAction: Wait(); break;
            case AiDecideAction: AiStep(); break;
            case UseItemAtAction a: UseItemAt(a.Index, a.Slot); break;
            case UseItemOnAction a: UseItemOn(a.Index, UnitOf(a.Target)); break;
            case UseItemAction a: UseItem(a.Index, UnitOf(a.Target)); break;
            case ResolveAction a: Resolve(a.MaxActions); break;
            case PlayOutAction a: PlayOut(a.MaxActions); break;
            case SetAutoAction { Attacker: { } side } a: SetAuto(side, a.On); break;
            case SetAutoAction a: Auto = a.On; break;
            default: throw new InvalidDataException($"неизвестное действие боя {action.GetType().Name}");
        }
    }

    private BattleUnit UnitOf(string id) => _state.Unit(id) ?? throw new InvalidDataException($"в бою нет юнита {id}");

    /// <summary>Юнит на стороне игрока.</summary>
    public bool IsPlayers(BattleUnit unit) => unit is not null && unit.Attacker == PlayerIsAttacker;

    private void FinishTurn()
    {
        if (Current is not { } unit) return;
        _sim.EndTurn(unit);
        _sim.InsertExtraTurns(_queue, _index);
        _index++;
        _hit = 0;
        Current = null;
        NextTurn();
    }

    /// <summary>Ход кончился: начать следующий сразу или отложить до <see cref="StartTurn"/> (<see cref="HoldTurnStart"/>).</summary>
    private void NextTurn()
    {
        if (HoldTurnStart) TurnStartPending = true;
        else Advance();
        NotifyQueue();
    }

    /// <summary>Найти следующего ходящего: добирает раунды и пропускает тех, кто ходить не может, как
    /// <see cref="BattleSim.Run"/>.</summary>
    private void Advance()
    {
        while (!_sim.Finished && !_over)
        {
            // Сторона пала (ударом или в начале хода) — остаток очереди раунда брошен, дальше — ходы лекарей победителя.
            if (_sim.Winner is not null && !_healers)
            {
                StartHealers();
                continue;
            }
            if (_index >= _queue.Count)
            {
                if (_healers)
                {
                    EndHealers();
                    break;
                }
                _round++;
                _queue = [.. _sim.BeginRound(_round)];
                // Раунд 1 движок строит дважды, а между построениями — предраунд двойников (BattleSim.BeginPreRound, 0x6290bc): они ходят
                // в раунде 0, и второе построение — после них; без двойников — сразу.
                if (_round == 1 && !_preRoundDone)
                {
                    _preRoundDone = true;
                    var pre = _sim.BeginPreRound();
                    if (pre.Count > 0)
                    {
                        _queue = [.. pre];
                        _round = 0;
                    }
                    else _queue = [.. _sim.BeginRound(_round)];
                }
                _index = 0;
                if (_sim.Finished || _queue.Count == 0) break;
            }
            var unit = _queue[_index];
            if (!unit.OnField || !_sim.BeginTurn(unit))
            {
                // Ход занят параличом или окаменением (не снятым в этом начале хода): движок шлёт эффект 1 «Пас» или 10 «Защита».
                if (unit is { OnField: true, Alive: true, Paralyzed: true }) _turnShows.Add(new BattleTurnNote(unit.Id, BattleTurnShow.Pass, true));
                else if (unit is { OnField: true, Alive: true, Petrified: true }) _turnShows.Add(new BattleTurnNote(unit.Id, BattleTurnShow.Defend, true));
                if (_sim.Finished) break;
                _index++;
                continue;
            }
            Current = unit;
            _hit = 0;
            NotifyQueue();
            return;
        }
        Current = null;
        NotifyQueue();
    }

    /// <summary>
    /// Ходы лекарей после победы (переход раунда <c>0x62689c</c>, ранг 1 по коду и корпусу): сторона пала и флага <c>afterBattle</c> ещё
    /// нет — <c>0x626d49</c> строит новую очередь (раунд + 1, бросок инициативы на каждого на поле, как обычный раунд) и оставляет в ней
    /// только лекарей победителя (класс <c>Heal</c> у первой или второй атаки, <c>0x626f3b</c>; воскрешение не в счёт), у которых есть
    /// годная цель — раненый свой (<c>0x626ff5</c> → <c>0x62a290</c>). Очередь не пуста — со всех снимаются яд, мороз и ожог
    /// (<c>0x627142</c>), флаг взводится (<c>0x62698a</c>), каждый лекарь ходит один раз по инициативе — у человека сам выбирает цель, у ИИ
    /// решает ИИ. Пуста — бой окончен (броски нового раунда уже потрачены, как у <see cref="BattleSim.Run"/>). Корпус: 84 из 670 боёв с
    /// ходом лекарей, всегда один проход; 10/10 — ход есть ровно тогда, когда у победителя есть раненый (<c>20260906-b/battle-001</c> seq
    /// 53584–53601: round 7, очередь из одной Оракула, лечение 97 → 125, round 8, battle_end). Замечание 18 прогона 11.10: прежде сессия
    /// доигрывала остаток очереди раунда, и послушница ходила, только если стояла в нём следующей.
    /// </summary>
    private void StartHealers()
    {
        _healers = true;
        _round++;
        // Очередь лекарей, снятие ядов и конец боя при пустой — правило модели (BattleSim.BeginRound → AfterBattleQueue), одно на
        // прогон и экран.
        var healers = _sim.BeginRound(_round);
        if (_sim.Finished || healers.Count == 0)
        {
            _over = true;
            return;
        }
        _queue = [.. healers];
        _index = 0;
    }

    /// <summary>Очередь лекарей кончилась (<c>0x6268d2</c>: флаг снят, опыт): строится обычный раунд — броски инициативы, — и первый же
    /// ход даёт конец боя (<c>0x628208</c>).</summary>
    private void EndHealers()
    {
        _round++;
        _sim.BeginRound(_round);
        _over = true;
    }
}
