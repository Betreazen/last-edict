using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Цели редких классов политики ИИ — в корпусе почти не встречаются (разбор §2), поэтому
/// перенесены по Lua тулсета: <b>ранг 2</b>, <c>reference/D2ModdingToolset/Scripts/battleAi.lua</c>
/// (<c>findAttackTarget</c>, строки 2607–2700; адреса движка — из разбора). Ничьи — <c>rng(2)</c>, как
/// <c>pickRandomIfEqual</c>.</summary>
public static class AiRareTargets
{
    /// <summary>Воскрешение <c>0x5d040f</c> (Lua 1488): павший с наибольшим опытом, из равных — последний
    /// (<c>&gt;=</c>, ничья броска не тратит). Живых в списке нет — <see cref="BattleTargets.Eligible"/>.</summary>
    public static BattleUnit? Revive(IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? best = null;
        int max = 0;
        foreach (var x in targets)
            if (!x.Alive && x.Type.XpKilled >= max) (best, max) = (x, x.Type.XpKilled);
        return best;
    }

    /// <summary>Порядок «Исцеления» (Lua 1890): превращение 1, паралич/окаменение 2, яд 3, обморожение 4,
    /// ожог 5, прочее 999.</summary>
    private static int CureRank(BattleUnit u) =>
        u.Transformed ? 1 : u.Paralyzed || u.Petrified ? 2 : Dot(u, AttackClass.Poison) ? 3
        : Dot(u, AttackClass.Frostbite) ? 4 : Dot(u, AttackClass.Blister) ? 5 : 999;

    private static bool Dot(BattleUnit u, AttackClass cls) => u.Dots.Any(d => d.Class == cls);

    /// <summary>«Исцеление» <c>0x5d0dfe</c> (Lua 1918): наименьший <see cref="CureRank"/>, из равных — больший
    /// опыт; отступающие и призванные — запасные.</summary>
    internal static BattleUnit? Cure(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? prim = null, sec = null;
        int pv = 99, pxp = 0, sv = 99, sxp = 0;
        foreach (var x in targets)
        {
            int v = CureRank(x), xp = x.Type.XpKilled;
            if (x.Retreating || x.Summoned)
            {
                if (prim is not null || x.Retreating && sec is { Summoned: true } || v > sv) continue;
                if (v < sv) sxp = 0;
                if (xp > sxp || ctx.Tie(xp, sxp)) (sec, sv, sxp) = (x, v, xp);
            }
            else if (v <= pv)
            {
                if (v < pv) pxp = 0;
                if (xp > pxp || ctx.Tie(xp, pxp)) (prim, pv, pxp) = (x, v, xp);
            }
        }
        return prim ?? sec;
    }

    /// <summary>Призыв <c>0x5d1079</c>, ранг 1. Места — список целей призыва (<c>[esi]</c> — набор позиций, <see cref="BattleState.SummonCells"/>:
    /// пустые, павших, ушедших; обе клетки живого большого — не места), по возрастанию; стартовый индекс — <c>rng(число мест)</c> (<c>0x5d1099</c>), от него по кругу
    /// берётся первое чётное место (первый ряд, <c>% 2 == 0</c>, цикл <c>0x5d10b4</c>–<c>0x5d1104</c>); чётных нет — стартовое.
    /// Выбранная позиция — <see cref="AiContext.SummonSlot"/>, из него — <see cref="AiDecision.SummonSlot"/> (исполнитель кладёт её в
    /// <see cref="BattleState.SummonSlot"/>, <see cref="LifeAttacks.Summon"/> ставит туда призванного); цель решения — сам призывающий.
    /// <para>Сверено журналом (С2, 16.10): 42 призыва ИИ корпуса (<c>20260918-probe65</c>, <c>20260928-shots</c>,
    /// <c>20260930-probe72</c>, <c>20261014-replay</c>, <c>20260922-ui</c>) — бросок и позиция нового юнита в следующем снимке,
    /// расхождений 0 (<c>20261014-replay [524286]</c> seq 525076: места 0, 2, 3, 5, бросок 2 → 3, 5 нечётные → 0).</para>
    /// <para>Партия 17.10 (бой у города орков): Лиф в полном отряде (два тролля, орк, сам) ставил призванных во вторые клетки троллей —
    /// у движка их там нет в списке целей, и в полном отряде призывать некуда.</para></summary>
    internal static BattleUnit? Summon(AiContext ctx, BattleUnit unit)
    {
        var free = ctx.State.SummonCells(unit);
        if (free.Count == 0) return null;
        int start = ctx.Rng.Next(free.Count);
        int pick = start;
        for (int i = 0; i < free.Count; i++)
        {
            int at = (start + i) % free.Count;
            if (free[at] % 2 == 0) { pick = at; break; }
        }
        ctx.SummonSlot = free[pick];
        return unit;
    }

    /// <summary>Превращение другого <c>0x5d1cba</c> (Lua 2031): не превращённый с наибольшим опытом; доля HP
    /// ≤ 0.2 или отступает — запасной.</summary>
    internal static BattleUnit? TransformOther(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? prim = null, sec = null;
        int pxp = 0, sxp = 0;
        foreach (var x in targets)
        {
            if (x.Transformed) continue;
            int xp = x.Type.XpKilled;
            if (!(xp > pxp || ctx.Tie(xp, pxp))) continue;
            if ((double)x.Hp / x.HpMax <= 0.2 || x.Retreating)
            {
                if (xp > sxp || ctx.Tie(xp, sxp)) (sec, sxp) = (x, xp);
            }
            else (prim, pxp) = (x, xp);
        }
        return prim ?? sec;
    }

    /// <summary>Двойник <c>0x5d133a</c> (ранг 1, прочитан до <c>ret</c>). Список — <see cref="BattleTargets.Possible"/>
    /// (копии <c>0x661a7a</c>, иначе цели запасной атаки). По каждому кандидату берётся ГЛОБАЛЬНАЯ реализация
    /// (<c>getGlobalUnitImplId</c>, <c>0x5d148d</c>): её первая атака и её <c>XP_KILLED</c>. Пропуск — атака не действует
    /// на «чужую» группу (<see cref="EffectiveAgainst"/>, <c>0x5d15cd</c>; чужая — не та, чьи позиции в списке
    /// положительны: при копиях это враги, при запасной атаке — свои). Первичная — «соседняя» атака у стоящего в
    /// первом ряду (<c>0x8380b8</c>), не «соседняя» у стоящего во втором (<c>0x8380c8</c>/<c>0x8380d8</c>); прочие —
    /// запасные и только пока первичной нет; <c>xp &gt; max || tie</c> (<c>0x5cfc04</c>). Журнал: 4 превращения из 4
    /// (map-001 [944403] и [1281528] ×2, passive-001 [5344774]).</summary>
    internal static BattleUnit? Doppelganger(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets)
    {
        bool copying = BattleTargets.CopyTargets(ctx.State, unit, attack).Count > 0;
        var foes = (copying ? ctx.State.Enemies(unit) : ctx.State.Allies(unit)).ToList();
        bool front = Formation.IsFront(unit.Slot);
        BattleUnit? prim = null, sec = null;
        int pxp = 0, sxp = 0;
        foreach (var x in targets)
        {
            var global = ctx.State.Rules?.Units.GetValueOrDefault(x.Type.GlobalId) ?? x.Type;
            var a = ctx.State.Rules?.AttackOf(global) ?? x.Attack;
            if (!EffectiveAgainst(a, foes)) continue;
            int xp = global.XpKilled;
            if ((a.Reach == AttackReach.Adjacent) == front)
            {
                if (xp > pxp || ctx.Tie(xp, pxp)) (prim, pxp) = (x, xp);
            }
            else if (prim is null && (xp > sxp || ctx.Tie(xp, sxp))) (sec, sxp) = (x, xp);
        }
        return prim ?? sec;
    }

    /// <summary><c>0x5d15cd</c>: класс не из {Damage, Drain, DrainOverflow, Summon, Poison, Frostbite, Blister}
    /// (<c>0x5cf8d3</c>) — всегда; иначе хоть один юнит группы не <c>Always</c> ни к источнику, ни к классу.</summary>
    private static bool EffectiveAgainst(AttackType a, IReadOnlyList<BattleUnit> group) =>
        !a.Effect.IsOffence ||
        group.Any(u => !AiTargets.AlwaysImmune(u, a.Source, a.Class));

    /// <summary>Самопревращение <c>0x5d16f0</c> (Lua 2202): ещё не превращён; во втором ряду — только без
    /// своих в первом; доля HP &gt; 0.25; живых врагов меньше трёх.</summary>
    public static bool TransformSelfPossible(BattleState state, BattleUnit unit)
    {
        if (unit.TransformedSelf) return false;
        if (Formation.IsBack(unit.Slot) && state.Allies(unit).Any(u => Formation.IsFront(u.Slot))) return false;
        if ((double)unit.Hp / unit.HpMax <= 0.25) return false;
        return state.Enemies(unit).Count(u => u.OnField) < 3;
    }

    /// <summary>Понижение уровня <c>0x5d1e45</c> (Lua 2286): наибольший уровень (не первый), из равных —
    /// больший опыт; отступающий — запасной.</summary>
    internal static BattleUnit? DrainLevel(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? prim = null, sec = null;
        int pxp = 0, sxp = 0, maxLevel = 1;
        foreach (var x in targets)
        {
            int level = x.Type.Level;
            if (level == 1 || level < maxLevel) continue;
            if (level > maxLevel) pxp = 0;
            maxLevel = level;
            int xp = x.Type.XpKilled;
            if (x.Retreating)
            {
                if (xp > sxp || ctx.Tie(xp, sxp)) (sec, sxp) = (x, xp);
            }
            else if (xp > pxp || ctx.Tie(xp, pxp)) (prim, pxp) = (x, xp);
        }
        return prim ?? sec;
    }

    /// <summary>Передача хода <c>0x5d1ab4</c> (ранг 1, прочитан до <c>ret</c>): мимо статусов 29, 2, 3; прямой урон —
    /// первичная при <c>xp &gt; pxp || tie</c> и <c>0x5cf4fd</c>; прочие — только пока первичной нет, при
    /// <c>xp &gt; sxp || tie</c> со СВОИМ максимумом запасных (в Lua — с максимумом первичной, отсюда лишняя/недостающая
    /// ничья), бафер — только поверх бафера или пустого места. Журнал map-001: 4 решения с <c>rng(2)</c> (946633 —
    /// две ничьи, 1286418/1286694/1286977 — по одной) и следующий ходящий сходятся. Себя и других передающих
    /// в списке нет — <see cref="Attacks.CanPerform"/>.</summary>
    internal static BattleUnit? GiveAttack(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? prim = null, sec = null;
        int pxp = 0, sxp = 0;
        bool secBooster = true;
        foreach (var x in targets)
        {
            if (x.Retreating || x.Paralyzed || x.Petrified) continue;
            int xp = x.Type.XpKilled;
            if (x.Attack.Effect.IsDirectDamage)
            {
                if ((xp > pxp || ctx.Tie(xp, pxp)) && AiTargets.CanAttack(ctx.State, x)) (prim, pxp) = (x, xp);
            }
            else if (prim is null && (xp > sxp || ctx.Tie(xp, sxp)))
            {
                bool boost = x.Attack.Class == AttackClass.BoostDamage;
                if ((!boost || secBooster) && AiTargets.CanAttack(ctx.State, x)) (sec, sxp, secBooster) = (x, xp, boost);
            }
        }
        return prim ?? sec;
    }

    /// <summary>Обереги <c>0x5ce0d0</c> → <c>0x5d0ae4</c> (Lua 2411, 2443): во вражеской группе (так в
    /// движке) нет никого на поле и не бегущего — цель лечения; иначе по опыту, доля HP &lt; 0.25 — запасной.</summary>
    internal static BattleUnit? BestowWards(AiContext ctx, BattleUnit unit, IReadOnlyList<BattleUnit> targets)
    {
        if (!ctx.State.Enemies(unit).Any(u => u.OnField && !u.Retreating)) return AiTargets.FindHeal(ctx, targets);
        return ByValue(ctx, targets, u => u.Type.XpKilled);
    }

    /// <summary>Общий перебор Lua 2443/2499: мимо отступающих и обездвиженных; наибольшее значение (или
    /// ничья); доля HP &lt; 0.25 — запасной. Обереги — по опыту, дробление брони — по броне типа.</summary>
    internal static BattleUnit? ByValue(AiContext ctx, IReadOnlyList<BattleUnit> targets, Func<BattleUnit, int> value)
    {
        BattleUnit? prim = null, sec = null;
        int pm = 0, sm = 0;
        foreach (var x in targets)
        {
            if (x.Retreating || x.Paralyzed || x.Petrified) continue;
            int v = value(x);
            if (!(v > pm || ctx.Tie(v, pm))) continue;
            if ((double)x.Hp / x.HpMax < 0.25)
            {
                if (v > sm || ctx.Tie(v, sm)) (sec, sm) = (x, v);
            }
            else (prim, pm) = (x, v);
        }
        return prim ?? sec;
    }

    /// <summary>Страх <c>0x5d1faa</c> (Lua 2555): не отступающий с наибольшим опытом; способный бить —
    /// первичный, прочие — запасные.</summary>
    internal static BattleUnit? Fear(AiContext ctx, IReadOnlyList<BattleUnit> targets)
    {
        BattleUnit? prim = null, sec = null;
        int pxp = 0, sxp = 0;
        foreach (var x in targets)
        {
            if (x.Retreating) continue;
            int xp = x.Type.XpKilled;
            if (!(xp > pxp || ctx.Tie(xp, pxp))) continue;
            if (AiTargets.CanAttack(ctx.State, x)) (prim, pxp) = (x, xp);
            else if (xp > sxp || ctx.Tie(xp, sxp)) (sec, sxp) = (x, xp);
        }
        return prim ?? sec;
    }
}
