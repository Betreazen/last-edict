using D2.Rules.Rng;

namespace D2.Rules.Battle;

/// <summary>Действие, которое выбирает ИИ. Ждать (Wait) ИИ не выбирает никогда (разбор §2).</summary>
public enum AiAction { Attack, Defend, Retreat, UseItem }

/// <summary>Решение ИИ: действие, цель (у площадной атаки — первая цель, в <c>Act</c> не передаётся) и
/// номер предмета в <see cref="BattleUnit.BattleItems"/> для <see cref="AiAction.UseItem"/>.</summary>
public sealed record AiDecision(AiAction Action, BattleUnit? Target, int ItemIndex = -1)
{
    /// <summary>Позиция призыва, выбранная ИИ (<c>0x5d1079</c>, <c>AiRareTargets</c>), −1 — не призыв. Исполнитель кладёт её в
    /// <see cref="BattleState.SummonSlot"/> перед ударом.</summary>
    public int SummonSlot { get; init; } = -1;

    public static readonly AiDecision Defend = new(AiAction.Defend, null);
    public static readonly AiDecision Retreat = new(AiAction.Retreat, null);

    public static AiDecision AttackOrDefend(BattleUnit? target) =>
        target is null ? Defend : new AiDecision(AiAction.Attack, target);
}

/// <summary>Что нужно функциям выбора: состояние, генератор (только для ничьих <c>rng(2)</c>,
/// <c>0x5cfc04</c>/<c>0x5d03ee</c>) и константы (броня, потолок урона).</summary>
public sealed record AiContext(BattleState State, IRng Rng, RulesConstants Constants)
{
    /// <summary>Позиция призыва, выбранная по ходу решения (<c>0x5d1079</c>); −1 — не выбиралась.</summary>
    public int SummonSlot { get; set; } = -1;

    /// <summary>Ничья по целым (<c>0x5cfc04</c>): <c>a == b ? rng(2) == 1 : false</c>.</summary>
    public bool Tie(int a, int b) => a == b && Rng.Next(2) == 1;

    /// <summary>Ничья по <c>double</c> (<c>0x5d03ee</c>).</summary>
    public bool Tie(double a, double b) => a == b && Rng.Next(2) == 1;

    /// <summary><c>EffHp</c> <c>0x5cef84</c>: <c>hp + hp · computeArmor / 100</c>, у мёртвого 0 (защита
    /// учитывается — <see cref="Formulas.Armor"/>).</summary>
    public int EffectiveHp(BattleUnit u) =>
        u.Hp > 0 ? u.Hp + u.Hp * Formulas.Armor(u, Constants) / 100 : 0;
}

/// <summary>Политика ИИ в бою — <c>aiChooseBattleAction</c> (GOG <c>0x5ce2b9</c>, единственный вызов —
/// <c>0x428698</c>). Ранг 1: разбор <c>docs/synthetic-maps/analysis/G-ai-battle.md</c> §2; модель на Python
/// (<c>re-ai/ai.py</c>) сходится во всех решениях в 713 из 733 боёв корпуса. Сложность и
/// <c>aiattitudes</c> на решение не влияют.</summary>
public static class BattleAi
{
    /// <summary>Решение ходящего <paramref name="unit"/>: бегство (<see cref="AiRetreat"/>), затем удар или защита.
    /// Броски — только ничьи <c>rng(2)</c> (и <c>rng(n)</c> призыва). Меняет статус бегства сторон в
    /// <paramref name="state"/>, как движок — в <c>BattleMsgData</c>.</summary>
    public static AiDecision Choose(BattleState state, BattleUnit unit, IRng rng, RulesConstants constants)
    {
        var ctx = new AiContext(state, rng, constants);
        var d = Choose(ctx, unit);
        return d.Action == AiAction.Attack && ctx.SummonSlot >= 0 ? d with { SummonSlot = ctx.SummonSlot } : d;
    }

    private static AiDecision Choose(AiContext ctx, BattleUnit unit)
    {
        var (state, constants) = (ctx.State, ctx.Constants);
        double rel = AiStrength.Relative(state, unit, constants);                 // 0x5ceb4b
        if (AiRetreat.Check(ctx, unit, rel) is { } retreat) return retreat;       // 0x5d2a4d
        if (AiRetreat.LeaderRetreats(state, unit, rel)) return AiDecision.Retreat;
        var a1 = unit.Attack;
        var targets = BattleTargets.Possible(state, unit, a1);
        int dmg = 0;
        BattleUnit? kill = null;
        if (targets.Count > 0)
        {
            dmg = AiTargets.DamageEstimate(ctx, unit);
            if (dmg > 0) kill = AiTargets.SelectKill(ctx, targets, dmg, AiTargets.KillSource(unit));
        }
        if (kill is null && AiItems.Choose(ctx, unit, rel) is { } use) return use;   // 0x5ce917
        bool selfTransform = a1.Class == AttackClass.TransformSelf && AiRareTargets.TransformSelfPossible(state, unit);
        if (targets.Count == 0 && !selfTransform) return AiDecision.Defend;
        if (a1.Reach == AttackReach.All) return ChooseArea(ctx, unit, targets);
        if (kill is not null) return new AiDecision(AiAction.Attack, kill);
        return ChooseSingle(ctx, unit, targets, dmg);
    }

    /// <summary>Площадная первая атака: первая цель, если не все <c>Always</c> (<c>0x5cecb5</c>) и паралич
    /// не бесполезен (<c>0x5cf206</c>), иначе защита.</summary>
    private static AiDecision ChooseArea(AiContext ctx, BattleUnit unit, List<BattleUnit> targets)
    {
        var a1 = unit.Attack;
        bool counts = !a1.Effect.TargetsAllies && (a1.Effect.IsDamaging || a1.Class == AttackClass.Summon);
        if (counts && targets.All(x => AiTargets.AlwaysImmune(x, a1.Source, a1.Class))) return AiDecision.Defend;
        if (a1.Class is AttackClass.Paralyze or AttackClass.Petrify && !AiTargets.GroupOffence(ctx.State, unit))
            return AiDecision.Defend;
        return new AiDecision(AiAction.Attack, targets[0]);
    }

    /// <summary>Одиночная первая атака без добивания: прямой урон с безобидной второй атакой
    /// (<c>0x5ce99d</c>) — <c>FindNonAll</c>; иначе цель «главной» атаки (<c>0x5cebef</c>), затем
    /// запасной (<c>0x5cec59</c>, <c>0x5d25f0</c>).</summary>
    private static AiDecision ChooseSingle(AiContext ctx, BattleUnit unit, List<BattleUnit> targets, int dmg)
    {
        var (a1, a2) = (unit.Attack, unit.Attack2);
        bool direct = a1.Effect.IsDirectDamage;
        if (direct && (a2 is null || a2.Effect.IsDirectDamage ||
                       a2.Class is AttackClass.LowerDamage or AttackClass.LowerInitiative))
            return AiDecision.AttackOrDefend(AiTargets.FindNonAll(ctx, a1, dmg, targets));

        var first = direct ? a2! : a1;
        var x = AiTargets.FindAttackTarget(ctx, unit, first, targets, dmg);
        if (x is not null) return new AiDecision(AiAction.Attack, x);

        var alt = a1.Class == AttackClass.TransformSelf ? ctx.State.Rules?.AltAttackOf(a1)
                : ReferenceEquals(first, a1) ? a2 : a1;
        if (alt is null) return AiDecision.Defend;
        if (!alt.Effect.IsDirectDamage) x = AiTargets.FindAttackTarget(ctx, unit, alt, targets, dmg);
        else if (alt.Reach != AttackReach.All) x = AiTargets.FindNonAll(ctx, alt, dmg, targets);
        else if (targets.All(t => AiTargets.AlwaysImmune(t, alt.Source, alt.Class))) x = null;
        else x = targets.FirstOrDefault();
        return AiDecision.AttackOrDefend(x);
    }
}
