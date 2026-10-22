using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Бегство в политике ИИ — разбор §3 (ранг 1 по коду; ошибки Lua тулсета исправлены: флаг
/// «решено» читается как «ещё не решено», при прикрытии и &lt; 2 живых — бегство, а не автобой).
/// Корпус: 13 решений из 13 при известных приказах (разбор §3).</summary>
public static class AiRetreat
{
    /// <summary>Порог слабости стороны (<c>.rdata</c> 0.3).</summary>
    public const double WeakThreshold = 0.3;

    /// <summary><c>RetreatCheck</c> <c>0x5d2a4d</c>: статус бегства стороны. Защитник один раз за бой решает о
    /// прикрытии (слаб, у нападающего нет хода, бежать можно); любая сторона, которая не может бить и может
    /// уйти (без оглядки на человека и расу), бежит целиком. null — решения нет.</summary>
    public static AiDecision? Check(AiContext ctx, BattleUnit unit, double rel)
    {
        var state = ctx.State;
        if (state.Round < 1) return null;
        if (Status(state, unit) == RetreatStatus.NoRetreat)
        {
            if (!state.RetreatDecided && !unit.Attacker)
            {
                if (rel < WeakThreshold && state.AttackerSide.Movement == 0 && CanRetreat(state, unit, false, false))
                    state.DefendersRetreat = RetreatStatus.Cover;
                state.RetreatDecided = true;
            }
            if (Status(state, unit) == RetreatStatus.NoRetreat && CanRetreat(state, unit, true, true)
                && !AiTargets.GroupOffence(state, unit))
                SetStatus(state, unit, RetreatStatus.Full);
        }
        return Status(state, unit) switch
        {
            RetreatStatus.Full => AiDecision.Retreat,
            RetreatStatus.Cover => Cover(state, unit),
            _ => null,
        };
    }

    /// <summary>Бегство лидера (<c>0x5ce2b9</c>): слабая сторона, бежать можно, живых (с ушедшими) не меньше
    /// двух (<c>0x5ce74c</c>), и либо он нападает, либо у нападающего нет хода (<c>0x5d2a12</c>).</summary>
    public static bool LeaderRetreats(BattleState state, BattleUnit unit, double rel) =>
        unit.Type.IsLeader && rel <= WeakThreshold && CanRetreat(state, unit, false, false)
        && !LessThanTwoAlive(state, unit, countRetreated: true)
        && (unit.Attacker || state.AttackerSide.Movement == 0);

    /// <summary><c>CanRetreat</c> <c>0x5d2779</c>: действие Retreat доступно (раунд ≥ 1, группа — отряд, защитник
    /// не в городе), не фаза лекарей, игрок не человек и раса играбельна (если не велено их не смотреть),
    /// лидер отряда есть и он не вор, не призыв и не иллюзия, приказ Normal/Roam или AttackStack не на этого врага, у нападающего
    /// приказ ИИ не Assist. Сведений о стороне нет — нельзя.</summary>
    public static bool CanRetreat(BattleState state, BattleUnit unit, bool ignoreRace, bool ignoreHuman)
    {
        var side = state.SideOf(unit);
        if (!side.Known || !side.IsStack || state.Round < 1 || !state.CanRetreat(unit)) return false;
        if (!state.Enemies(unit).Any(u => u.OnField)) return false;
        if (!ignoreHuman && side.IsHuman || !ignoreRace && !side.PlayableRace) return false;
        var leader = state.Allies(unit).FirstOrDefault(u => u.Type.IsLeader);
        // 0x5d28ea → 0x431d8c (IUsSummon: призыв и иллюзия), затем 0x41b399 (вор).
        if (leader is null || leader.Type.Category == UnitCategory.Noble || UnitCategory.IsSummonImpl(leader.Type.Category)) return false;
        string enemyGroup = unit.Attacker ? state.DefenderGroup : state.AttackerGroup;
        bool order = side.Order is StackOrder.Normal or StackOrder.Roam
                     || side.Order == StackOrder.AttackStack
                        && !string.Equals(side.OrderTarget, enemyGroup, StringComparison.OrdinalIgnoreCase);
        return order && (!unit.Attacker || side.AiOrder != StackOrder.Assist);
    }

    /// <summary><c>0x5ce74c</c>: живых в группе меньше двух; ушедших с поля считать — по флагу.</summary>
    public static bool LessThanTwoAlive(BattleState state, BattleUnit unit, bool countRetreated) =>
        state.Allies(unit).Count(u => u.Alive && (countRetreated || !u.Retreated)) < 2;

    /// <summary>Прикрытие <c>0x5d2b85</c>: второй ряд и лидер уходят; уходят и все, если кто-то другой из первого
    /// ряда жив и защищается; иначе последний живой уходит, остальные защищаются.</summary>
    private static AiDecision Cover(BattleState state, BattleUnit unit)
    {
        if (Formation.IsBack(unit.Slot) || unit.Type.IsLeader) return AiDecision.Retreat;
        if (state.Allies(unit).Any(u => u != unit && Formation.IsFront(u.Slot) && u.Alive && u.Defending)) return AiDecision.Retreat;
        return LessThanTwoAlive(state, unit, countRetreated: false) ? AiDecision.Retreat : AiDecision.Defend;
    }

    private static RetreatStatus Status(BattleState state, BattleUnit unit) =>
        unit.Attacker ? state.AttackersRetreat : state.DefendersRetreat;

    private static void SetStatus(BattleState state, BattleUnit unit, RetreatStatus status)
    {
        if (unit.Attacker) state.AttackersRetreat = status;
        else state.DefendersRetreat = status;
    }
}
