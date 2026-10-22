using LastEdict.Rules.Battle.Effects;
using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Выбор цели политикой ИИ — функции дерева <c>aiChooseBattleAction</c> (<c>0x5ce2b9</c>).
/// Ранг 1: разбор <c>docs/synthetic-maps/analysis/G-ai-battle.md</c> §2, перенос один в один с модели
/// <c>re-ai/ai.py</c> (корпус: 2533 цели из 2533, 3018 решений с тем же числом <c>rng(2)</c>).
/// Все ничьи — <see cref="AiContext.Tie(int, int)"/>; других бросков выбор цели не тратит.</summary>
public static class AiTargets
{
    private static readonly double Cut = 0.7f;   // .rdata, float: (int)(p · 0.7f)

    /// <summary><c>(int)(p · 0.7f)</c> — штраф за живой заряд <c>Once</c>.</summary>
    public static int Reduce(int p) => (int)(p * Cut);

    /// <summary>Оценка урона <c>0x5d2f3d</c>: <c>DWB(a1) + DWB(a2)</c> по классам <see cref="IsDamaging"/>,
    /// у TransformSelf — только <c>DWB(ALT_ATTACK)</c>; <c>DWB</c> —
    /// <c>computeDamageWithBuffs(max = DamageMax, random = false, easy = false)</c> (<c>0x5d2df2</c> не бросает).</summary>
    public static int DamageEstimate(AiContext ctx, BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return DamageEstimate(unit, ctx.Constants, ctx.State.Rules);
    }

    /// <summary>Та же оценка вне боя (ИИ на карте, <c>0x452475</c>): без бросков, правила — для второй атаки
    /// превращения в себя.</summary>
    public static int DamageEstimate(BattleUnit unit, RulesConstants c, GameRules? rules)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Attack.Class == AttackClass.TransformSelf)
            return Dwb(c, unit, rules?.AltAttackOf(unit.Attack));
        return Dwb(c, unit, unit.Attack) + Dwb(c, unit, unit.Attack2);
    }

    private static int Dwb(RulesConstants c, BattleUnit unit, AttackType? attack)
    {
        if (attack is null || !attack.Effect.IsDamaging) return 0;
        var effective = EffectiveStats.OfAttack(attack, unit.Chain).Attack;
        return Formulas.DamageWithBuffs(effective, Formulas.DamageMax(c, unit.Type),
                                        c.BoostDamagePercent(unit.BoostDamageLevel),
                                        c.LowerDamagePercent(unit.LowerDamageLevel), false, NoRolls, c);
    }

    /// <summary>Оценка урона не бросает (<c>addRandom</c> = false) — генератор ей не нужен.</summary>
    private static readonly LastEdict.Rules.Rng.IRng NoRolls = new LastEdict.Rules.Rng.ScriptedRng([]);

    /// <summary>Источник для <see cref="SelectKill"/> (<c>0x5ce802</c>): первой атаки, если она с уроном,
    /// иначе второй, если с уроном, иначе снова первой.</summary>
    public static int KillSource(BattleUnit unit) =>
        unit.Attack.Effect.IsDamaging ? unit.Attack.Source
        : unit.Attack2 is { } a2 && a2.Effect.IsDamaging ? a2.Source : unit.Attack.Source;

    /// <summary><c>Prio</c> <c>0x5ceff1</c>. Цель большая, её первая атака «соседняя» или бафф —
    /// по эффективной HP: <c>e ≤ dmg ? 10000 + e : 10000 − e</c>; иначе <c>10000 + XP_KILLED · k</c>, k по
    /// первой или второй атаке цели: Heal 2, Paralyze/Petrify 8, Summon 10, TransformOther 9, GiveAttack 3,
    /// прочие 1.</summary>
    public static int Priority(AiContext ctx, BattleUnit u, int dmg)
    {
        if (!u.Type.SizeSmall || u.Attack.Reach == AttackReach.Adjacent || u.Attack.Class == AttackClass.BoostDamage)
        {
            int e = ctx.EffectiveHp(u);
            return e <= dmg ? 10000 + e : 10000 - e;
        }
        return 10000 + u.Type.XpKilled * XpFactor(u.Attack.Effect, u.Attack2?.Effect);
    }

    /// <summary>Множитель опыта по эффектам первой и второй атаки цели: движок идёт по своему списку (лекарь, паралич, окаменение,
    /// призыв, превращение другого, передача атаки) и берёт первый, совпавший с любой из двух, — здесь это наименьший
    /// <see cref="EffectTuning.XpRank"/>. Ни одного — 1.</summary>
    private static int XpFactor(AttackEffect first, AttackEffect? second)
    {
        var pick = second?.Tuning.XpRank is { } r2 && (first.Tuning.XpRank is not { } r1 || r2 < r1) ? second : first;
        return pick.Tuning.XpRank is null ? 1 : pick.Tuning.XpFactor;
    }

    /// <summary><c>SelectKill</c> <c>0x5cedc9</c>: цель, которую удар добивает. Пропуск — призванные,
    /// <c>hp &gt; dmg</c>, <c>hp == 0</c>, эффективная HP больше урона; живой <c>Once</c> к источнику —
    /// <see cref="Reduce"/>; берётся строго большее при не-<c>Always</c>.</summary>
    public static BattleUnit? SelectKill(AiContext ctx, IReadOnlyList<BattleUnit> targets, int dmg, int source)
    {
        int best = 0;
        BattleUnit? sel = null;
        foreach (var x in targets)
        {
            if (x.Summoned || x.Hp > dmg || x.Hp == 0 || ctx.EffectiveHp(x) > dmg) continue;
            int p = Priority(ctx, x, dmg);
            var (s, _) = Immunity.LevelsFor(x, source, AttackClass.Damage);
            if (s == ImmunityLevel.Once && Immunity.OnceLive(x, source)) p = Reduce(p);
            if (p > best && s != ImmunityLevel.Always) (best, sel) = (p, x);
        }
        return sel;
    }

    /// <summary>Иммунен ли <c>Always</c> к источнику или к классу.</summary>
    public static bool AlwaysImmune(BattleUnit x, int source, AttackClass cls)
    {
        var (s, c) = Immunity.LevelsFor(x, source, cls);
        return s == ImmunityLevel.Always || c == ImmunityLevel.Always;
    }

    private static bool OnceLive(BattleUnit x, int source, AttackClass cls)
    {
        var (s, c) = Immunity.LevelsFor(x, source, cls);
        return s == ImmunityLevel.Once && Immunity.OnceLive(x, source)
            || c == ImmunityLevel.Once && Immunity.OnceLive(x, cls);
    }

    /// <summary><c>FindAny</c> <c>0x5cfc22</c>: наибольший <see cref="Priority"/>. Пропуск — цели с DoT
    /// класса <paramref name="dot"/> (фильтр статусов 15/17/19), <c>Always</c> к источнику или классу,
    /// <c>p &lt; 0</c>; живой <c>Once</c> — <see cref="Reduce"/>; строго <c>&gt;</c>.</summary>
    public static BattleUnit? FindAny(AiContext ctx, IReadOnlyList<BattleUnit> targets, int dmg,
                                      AttackClass cls, int source, AttackClass? dot = null)
    {
        int best = 0;
        BattleUnit? sel = null;
        foreach (var x in targets)
        {
            if (dot is { } d && x.Dots.Any(e => e.Class == d)) continue;
            if (AlwaysImmune(x, source, cls)) continue;
            int p = Priority(ctx, x, dmg);
            if (p < 0) continue;
            if (OnceLive(x, source, cls)) p = Reduce(p);
            if (p > best) (best, sel) = (p, x);
        }
        return sel;
    }

    /// <summary><c>FindAdjacent</c> <c>0x5cfa31</c>: минимальная эффективная HP (&gt; 0, строго меньше, из
    /// равных — первая по слоту) среди непризванных без <c>Always</c>; призванные — запасные, с ничьей
    /// <c>rng(2)</c>; найдена непризванная — призванных больше не смотрят.</summary>
    public static BattleUnit? FindAdjacent(AiContext ctx, IReadOnlyList<BattleUnit> targets, AttackClass cls, int source)
    {
        BattleUnit? prim = null, nons = null, sec = null;
        int mn = int.MaxValue, smn = int.MaxValue;
        foreach (var x in targets)
        {
            if (x.Summoned && prim is not null) continue;
            int e = ctx.EffectiveHp(x);
            if (e > 0 && e < mn && !AlwaysImmune(x, source, cls))
            {
                if (!x.Summoned) (mn, nons) = (e, x);
                else if (e < smn || ctx.Tie(e, smn)) (smn, sec) = (e, x);
            }
            prim = nons;
        }
        return prim ?? sec;
    }

    /// <summary><c>0x5cf94c</c>: «соседняя» — <see cref="FindAdjacent"/>, иначе <see cref="FindAny"/>.</summary>
    public static BattleUnit? FindNonAll(AiContext ctx, AttackType attack, int dmg, IReadOnlyList<BattleUnit> targets) =>
        attack.Reach == AttackReach.Adjacent
            ? FindAdjacent(ctx, targets, attack.Class, attack.Source)
            : FindAny(ctx, targets, dmg, attack.Class, attack.Source);

    /// <summary>Лечение <c>0x5d023c</c>: наименьшая доля HP (<c>double</c>), пороги первичной и запасной
    /// цели — 1.0, ничья — <c>rng(2)</c>. Не призванный и не отступающий — первичная; призванный —
    /// запасная; отступающий — запасная, если запасная ещё не призванный. (В Lua тулсета ветка
    /// призванных перевёрнута.)</summary>
    public static BattleUnit? FindHeal(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        double pr = 1.0, sr = 1.0;
        BattleUnit? prim = null, sec = null;
        foreach (var x in targets)
        {
            double r = (double)x.Hp / x.HpMax;
            if (!(r < pr || ctx.Tie(r, pr))) continue;
            if (x.Retreating)
            {
                if (sec is { Summoned: true }) continue;
            }
            else if (!x.Summoned) { (prim, pr) = (x, r); continue; }
            if (r < sr || ctx.Tie(r, sr)) (sec, sr) = (x, r);
        }
        return prim ?? sec;
    }

    /// <summary><c>0x5cf4fd</c>: может ли юнит бить первой атакой — не уходит, цели есть, и по своим
    /// либо не все цели <c>Always</c>. ponytail: боевые предметы не учтены (как в модели re-ai).</summary>
    public static bool CanAttack(BattleState state, BattleUnit x)
    {
        if (x.Retreated || x.Retreating) return false;
        var a = x.Attack;
        var tg = BattleTargets.Possible(state, x, a);
        if (tg.Count == 0) return false;
        if (a.Effect.TargetsAllies) return true;
        return !tg.All(y => AlwaysImmune(y, a.Source, a.Class));
    }

    /// <summary><c>GroupOffence</c> <c>0x5cf29d</c>: может ли группа <paramref name="member"/> ранить —
    /// живой союзник с атакующим классом, который бьёт хоть одну не-<c>Always</c> цель, или призыватель.</summary>
    public static bool GroupOffence(BattleState state, BattleUnit member)
    {
        foreach (var x in state.Allies(member))
        {
            var a = x.Attack;
            if (!x.Alive || !a.Effect.IsOffence) continue;
            if (!CanAttack(state, x)) continue;
            // Ранг 3 (разбор говорил «кроме призыва»): призыватель, которому есть куда призывать, делает группу
            // наступательной — иначе группа из призывателей бежала бы целиком. Журнал: passive-003 [13656814] и
            // [16729389] — призыв в раунде 1, не бегство; остальной корпус не меняется.
            if (a.Class == AttackClass.Summon) return true;
            if (BattleTargets.Possible(state, x, a).Any(y => !AlwaysImmune(y, a.Source, a.Class))) return true;
        }
        return false;
    }

    /// <summary>Паралич/окаменение <c>0x5d1166</c>. Своя группа не может бить (<c>0x5cf206</c>) — цели нет.
    /// Пропуск — уже парализованные/окаменевшие; отступающий запоминается отдельно; <c>xp ≤ maxA</c> — мимо;
    /// цель может бить — атакующая (<c>maxA = xp</c>), иначе при <c>xp &gt; maxN</c> или ничьей — неатакующая.
    /// Итог — атакующая, неатакующая, отступающий (если группа может бить).</summary>
    public static BattleUnit? FindParalyze(AiContext ctx, BattleUnit unit, IReadOnlyList<BattleUnit> targets)
    {
        if (!GroupOffence(ctx.State, unit)) return null;
        BattleUnit? att = null, non = null, ret = null;
        int ma = 0, mn = 0;
        foreach (var x in targets)
        {
            if (x.Paralyzed || x.Petrified) continue;
            if (x.Retreating) { ret = x; continue; }
            int xp = x.Type.XpKilled;
            if (xp <= ma) continue;
            if (CanAttack(ctx.State, x)) (att, ma) = (x, xp);
            else if (xp > mn || ctx.Tie(xp, mn)) (non, mn) = (x, xp);
        }
        if (att is not null || non is not null) return att ?? non;
        return ret is not null && GroupOffence(ctx.State, unit) ? ret : null;
    }

    /// <summary>Бафф <c>0x5d091a</c>: пропуск — отступающие, парализованные, окаменевшие, первая атака не
    /// прямой урон; условие <c>xp &gt; m</c> или ничья; доля HP &lt; 0.25 — в запасные (своя ничья).</summary>
    public static BattleUnit? FindBoost(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? tgt = null, low = null;
        int m = 0, md = 0;
        foreach (var x in targets)
        {
            if (x.Retreating || x.Paralyzed || x.Petrified || !x.Attack.Effect.IsDirectDamage) continue;
            int xp = x.Type.XpKilled;
            if (!(xp > m || ctx.Tie(xp, m))) continue;
            if ((double)x.Hp / x.HpMax < 0.25)
            {
                if (xp > md || ctx.Tie(xp, md)) (low, md) = (x, xp);
            }
            else (tgt, m) = (x, xp);
        }
        return tgt ?? low;
    }

    /// <summary>Цель атаки предмета: для движка «ходящий» здесь — сам предмет (Lua передаёт id предмета), поэтому
    /// DoT берёт класс и источник атаки предмета, а прямой урон — потолок без надбавки лидера.</summary>
    public static BattleUnit? FindItemTarget(AiContext ctx, BattleUnit bearer, AttackType attack, IReadOnlyList<BattleUnit> targets)
    {
        var c = ctx.Constants;
        int dmg = attack.Effect.IsDamaging
            ? Formulas.DamageWithBuffs(attack, c.MaxDamage, 0, 0, false, ctx.Rng, c) : 0;
        var effect = attack.Effect;
        if (effect.IsDirectDamage && attack.Reach != AttackReach.All)
            return FindNonAll(ctx, attack, Math.Min(c.MaxDamage, attack.QtyDamage), targets);
        if (effect.IsDot)
            return FindAny(ctx, targets, dmg, attack.Class, attack.Source, attack.Class)
                   ?? FindAny(ctx, targets, dmg, attack.Class, attack.Source);
        return FindAttackTarget(ctx, bearer, attack, targets, dmg);
    }

    /// <summary><c>FindAttackTarget</c> <c>0x5d056b</c> — цель для атаки <paramref name="attack"/> по
    /// списку <paramref name="targets"/> (построен для первой атаки). Редкие классы — <see cref="AiRareTargets"/>.</summary>
    public static BattleUnit? FindAttackTarget(AiContext ctx, BattleUnit unit, AttackType attack,
                                               IReadOnlyList<BattleUnit> targets, int dmg)
    {
        ArgumentNullException.ThrowIfNull(attack);
        // Выбор по эффекту (этап Э2): лечение — FindHeal, прямой урон — FindDirect, яды — FindDotTarget, паралич и окаменение —
        // FindParalyze, усиление — FindBoost, ослабление и замедление — цели нет, редкие классы — AiRareTargets.
        return attack.Effect.FindTarget(ctx, unit, attack, targets, dmg);
    }

    /// <summary>Прямой урон: площадная атака — первая цель, иначе <see cref="FindNonAll"/> по урону <c>min(потолок, QTY_DAM)</c>.</summary>
    internal static BattleUnit? FindDirect(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets)
    {
        if (attack.Reach == AttackReach.All) return targets.FirstOrDefault();
        int raw = Math.Min(Formulas.DamageMax(ctx.Constants, unit.Type), attack.QtyDamage);
        return FindNonAll(ctx, attack, raw, targets);
    }

    /// <summary>Яды: <c>0x5d2159</c> — класс и источник первой атаки юнита, не яда; урон — <see cref="DamageEstimate(AiContext, BattleUnit)"/>.</summary>
    internal static BattleUnit? FindDotTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg)
    {
        var a1 = unit.Attack;
        return FindAny(ctx, targets, dmg, a1.Class, a1.Source, attack.Class) ?? FindAny(ctx, targets, dmg, a1.Class, a1.Source);
    }
}
