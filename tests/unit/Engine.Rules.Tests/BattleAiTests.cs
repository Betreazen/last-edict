using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;

/// <summary>Политика ИИ в бою (<c>aiChooseBattleAction</c> <c>0x5ce2b9</c>) — случаи из разбора
/// docs/synthetic-maps/analysis/G-ai-battle.md §2 и §5B. Модель на Python, по которой сверялся
/// алгоритм, — <c>re-ai/ai.py</c> (713 из 733 боёв корпуса).</summary>
public class BattleAiTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);
    private static readonly IReadOnlyList<ImmunityGrant> None = [];

    // --- Choose: верхний уровень ---

    [Fact] public void Choose_NoTargets_Defends()
    {
        var h = U("H", true, 0, cls: AttackClass.Heal, reach: AttackReach.Any);
        var d = BattleAi.Choose(S(h, U("E", false, 0)), h, new ScriptedRng([]), C);
        Assert.Equal(new AiDecision(AiAction.Defend, null), d);
    }

    [Fact] public void Choose_AreaAttack_AllTargetsAlwaysImmune_Defends()
    {
        var a = U("A", true, 0, reach: AttackReach.All);
        var e1 = U("E1", false, 0, bySource: [new(0, ImmunityLevel.Always)]);
        var e2 = U("E2", false, 2, bySource: [new(0, ImmunityLevel.Always)]);
        Assert.Equal(AiAction.Defend, BattleAi.Choose(S(a, e1, e2), a, new ScriptedRng([]), C).Action);
        e2.Type = e2.Type with { ImmunitiesBySource = None };
        var d = BattleAi.Choose(S(a, e1, e2), a, new ScriptedRng([]), C);
        // Первая цель — E2: список целей ИИ уже без Always-иммунных (0x65a1a5 → 0x65a528, BattleTargets.Possible).
        // Площадной удар цель не читает — E1 он всё равно накроет.
        Assert.Equal((AiAction.Attack, "E2"), (d.Action, d.Target?.Id));
    }

    /// <summary>Убиваемая цель (<c>0x5cedc9</c>) важнее той, что выбрал бы <c>FindAny</c> по опыту.</summary>
    [Fact] public void Choose_PrefersKillableTarget()
    {
        var a = U("A", true, 1, reach: AttackReach.Any, dam: 25);
        var weak = U("W", false, 0, hp: 20, xpk: 10, reach: AttackReach.Any);
        var rich = U("R", false, 2, xpk: 50, reach: AttackReach.Any);
        Assert.Equal("W", BattleAi.Choose(S(a, weak, rich), a, new ScriptedRng([]), C).Target?.Id);
        weak.Hp = 30;   // не добить — берётся самый дорогой
        Assert.Equal("R", BattleAi.Choose(S(a, weak, rich), a, new ScriptedRng([]), C).Target?.Id);
    }

    /// <summary>Призванного <c>SelectKill</c> не рассматривает.</summary>
    [Fact] public void SelectKill_SkipsSummoned()
    {
        var a = U("A", true, 1, reach: AttackReach.Any);
        var s = U("S", false, 0, hp: 10); s.Summoned = true;
        Assert.Null(AiTargets.SelectKill(Ctx(a, s), [s], 25, 0));
    }

    /// <summary>ADJ без убиваемых — минимальная эффективная HP (броня считается), из равных — меньший слот.</summary>
    [Fact] public void Choose_Adjacent_MinEffectiveHp_ThenLowerSlot()
    {
        var a = U("A", true, 2);
        var e0 = U("E0", false, 0, hp: 60, armor: 50);   // 60 + 30 = 90
        var e2 = U("E2", false, 2, hp: 80);              // 80
        var e4 = U("E4", false, 4, hp: 80);              // 80, но слот больше
        Assert.Equal("E2", BattleAi.Choose(S(a, e0, e2, e4), a, new ScriptedRng([]), C).Target?.Id);
    }

    /// <summary>Большая цель, «соседняя» или бафер: <c>e ≤ dmg ? 10000 + e : 10000 − e</c>.</summary>
    [Fact] public void Priority_EffectiveHpBranch()
    {
        var a = U("A", true, 0);
        var big = U("B", false, 0, hp: 30, small: false, reach: AttackReach.Any);
        var ctx = Ctx(a, big);
        Assert.Equal(9970, AiTargets.Priority(ctx, big, 25));
        Assert.Equal(10030, AiTargets.Priority(ctx, big, 40));
        var adj = U("J", false, 0, hp: 77);
        Assert.Equal(9923, AiTargets.Priority(Ctx(a, adj), adj, 25));
    }

    [Theory]
    [InlineData(AttackClass.Heal, 2)] [InlineData(AttackClass.Paralyze, 8)] [InlineData(AttackClass.Petrify, 8)]
    [InlineData(AttackClass.Summon, 10)] [InlineData(AttackClass.TransformOther, 9)]
    [InlineData(AttackClass.GiveAttack, 3)] [InlineData(AttackClass.Damage, 1)]
    public void Priority_XpKilledMultiplier(AttackClass cls, int k)
    {
        var a = U("A", true, 0);
        var t = U("T", false, 0, cls: cls, reach: AttackReach.Any, xpk: 7);
        Assert.Equal(10000 + 7 * k, AiTargets.Priority(Ctx(a, t), t, 25));
    }

    /// <summary>Множитель по второй атаке цели (a1 урон, a2 лечение).</summary>
    [Fact] public void Priority_XpKilledMultiplier_BySecondAttack()
    {
        var a = U("A", true, 0);
        var t = U("T", false, 0, reach: AttackReach.Any, xpk: 7, second: AttackClass.Heal);
        Assert.Equal(10014, AiTargets.Priority(Ctx(a, t), t, 25));
    }

    /// <summary><c>Once</c> к источнику — <c>(int)(p · 0.7f)</c>; потраченный заряд — без штрафа; <c>Always</c> — мимо.</summary>
    [Fact] public void FindAny_OnceImmunityCutsPriority_AlwaysSkipped()
    {
        var a = U("A", true, 1, reach: AttackReach.Any);
        var once = U("O", false, 0, xpk: 50, reach: AttackReach.Any, bySource: [new(0, ImmunityLevel.Once)]);
        var plain = U("P", false, 2, xpk: 10, reach: AttackReach.Any);
        var ctx = Ctx(a, once, plain);
        Assert.Equal("P", AiTargets.FindAny(ctx, [once, plain], 25, AttackClass.Damage, 0)?.Id);
        once.SourceImmunityUsed = 1;
        Assert.Equal("O", AiTargets.FindAny(ctx, [once, plain], 25, AttackClass.Damage, 0)?.Id);
        once.Type = once.Type with { ImmunitiesBySource = [new(0, ImmunityLevel.Always)] };
        Assert.Equal("P", AiTargets.FindAny(ctx, [once, plain], 25, AttackClass.Damage, 0)?.Id);
        Assert.Equal(7034, AiTargets.Reduce(10050));   // 0.7f = 0.69999998…: 7034,9999 → 7034
    }

    [Fact] public void SelectKill_OnceCutsPriority_AlwaysNeverSelected()
    {
        var a = U("A", true, 1, reach: AttackReach.Any);
        var once = U("O", false, 0, hp: 10, xpk: 50, bySource: [new(0, ImmunityLevel.Once)]);
        var plain = U("P", false, 2, hp: 10, xpk: 10);
        var ctx = Ctx(a, once, plain);
        Assert.Equal("P", AiTargets.SelectKill(ctx, [once, plain], 25, 0)?.Id);
        var always = U("W", false, 4, hp: 10, xpk: 90, bySource: [new(0, ImmunityLevel.Always)]);
        Assert.Null(AiTargets.SelectKill(Ctx(a, always), [always], 25, 0));
    }

    /// <summary>DoT (<c>0x5d20de</c>): класс и источник — от ПЕРВОЙ атаки юнита (<c>0x5d2159</c>), цели с
    /// тем же DoT — во второй проход. Squire 025c в passive-003 [12598304]: 9923 &gt; 9835.</summary>
    [Fact] public void Choose_SecondAttackPoison_UsesFirstAttackSource_PrefersUnpoisoned()
    {
        var a = U("A", true, 1, reach: AttackReach.Any, dam: 10, second: AttackClass.Poison, secondSource: 3);
        var squire = U("Q", false, 0, hp: 77, bySource: [new(3, ImmunityLevel.Always)]);   // к яду (Death) неуязвим
        var other = U("X", false, 2, hp: 165);
        var ctx = Ctx(a, squire, other);
        Assert.Equal("Q", BattleAi.Choose(ctx.State, a, new ScriptedRng([]), C).Target?.Id);
        squire.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 5, AppliedRound = 1, Long = true });
        Assert.Equal("X", BattleAi.Choose(ctx.State, a, new ScriptedRng([]), C).Target?.Id);
    }

    /// <summary>Лечение: ничья долей HP — <c>rng(2)</c> (57/135 = 19/45, passive-003 [12518978]).</summary>
    [Fact] public void FindHeal_TieOnHpRatio_SpendsRng2()
    {
        var h = U("H", true, 1, cls: AttackClass.Heal, reach: AttackReach.Any);
        var knight = U("K", true, 0, hp: 135); knight.Hp = 57;
        var healer = U("L", true, 2, hp: 45); healer.Hp = 19;
        var ctx = Ctx(h, knight, healer);
        var one = new ScriptedRng([1]);
        Assert.Equal("L", AiTargets.FindHeal(ctx with { Rng = one }, [knight, healer])?.Id);
        Assert.Equal(1, one.Used);
        Assert.Equal("K", AiTargets.FindHeal(ctx with { Rng = new ScriptedRng([0]) }, [knight, healer])?.Id);
    }

    /// <summary>Призванный — только запасная цель: не призванный берётся, даже если ранен меньше.</summary>
    [Fact] public void FindHeal_SummonedIsFallbackOnly()
    {
        var h = U("H", true, 1, cls: AttackClass.Heal, reach: AttackReach.Any);
        var summ = U("S", true, 0); summ.Hp = 10; summ.Summoned = true;
        var hurt = U("W", true, 2); hurt.Hp = 90;
        var ctx = Ctx(h, summ, hurt);
        Assert.Equal("W", AiTargets.FindHeal(ctx, [summ, hurt])?.Id);
        Assert.Equal("S", AiTargets.FindHeal(ctx, [summ])?.Id);
    }

    /// <summary>Паралич, когда своя группа бить не может (<c>0x5cf206</c>), — защита.</summary>
    [Fact] public void Choose_ParalyzeWithoutGroupOffence_Defends()
    {
        var p = U("P", true, 1, cls: AttackClass.Paralyze, reach: AttackReach.Any);
        var e = U("E", false, 0);
        Assert.Equal(AiAction.Defend, BattleAi.Choose(S(p, e), p, new ScriptedRng([]), C).Action);
        var fighter = U("F", true, 0);
        var d = BattleAi.Choose(S(p, fighter, e), p, new ScriptedRng([]), C);
        Assert.Equal((AiAction.Attack, "E"), (d.Action, d.Target?.Id));
    }

    /// <summary>Паралич: атакующая цель с большим опытом, уже парализованные — мимо.</summary>
    [Fact] public void FindParalyze_PrefersAttackingTargetWithMostXp()
    {
        var p = U("P", true, 1, cls: AttackClass.Paralyze, reach: AttackReach.Any);
        var fighter = U("F", true, 0);
        var big = U("B", false, 0, xpk: 90); big.Paralyzed = true;
        var mid = U("M", false, 2, xpk: 40);
        var low = U("L", false, 4, xpk: 20);
        var ctx = Ctx(p, fighter, big, mid, low);
        Assert.Equal("M", AiTargets.FindParalyze(ctx, p, [big, mid, low])?.Id);
    }

    /// <summary>Бафф: цель с долей HP &lt; 0.25 — только запасная.</summary>
    [Fact] public void FindBoost_LowHpIsFallback()
    {
        var b = U("B", true, 1, cls: AttackClass.BoostDamage, reach: AttackReach.Any);
        var low = U("L", true, 0, xpk: 50); low.Hp = 20;
        var fit = U("F", true, 2, xpk: 10); fit.Hp = 80;
        var ctx = Ctx(b, low, fit);
        Assert.Equal("F", AiTargets.FindBoost(ctx, [low, fit])?.Id);
        Assert.Equal("L", AiTargets.FindBoost(ctx, [low])?.Id);
    }

    /// <summary>Оценка урона (<c>0x5d2f3d</c>): сумма двух атак с уроном, без бросков.</summary>
    [Fact] public void DamageEstimate_SumsDamagingAttacks()
    {
        var a = U("A", true, 0, dam: 25, second: AttackClass.Poison);
        Assert.Equal(25 + 10, AiTargets.DamageEstimate(Ctx(a), a));
        var h = U("H", true, 0, cls: AttackClass.Heal);
        Assert.Equal(0, AiTargets.DamageEstimate(Ctx(h), h));
    }

    // --- сборка ---

    internal static BattleState S(params BattleUnit[] units) =>
        new() { AttackerGroup = "G1", DefenderGroup = "G2", Round = 1, Units = [.. units] };

    internal static AiContext Ctx(params BattleUnit[] units) => new(S(units), new ScriptedRng([]), C);

    internal static BattleUnit U(string id, bool attacker, int slot, AttackClass cls = AttackClass.Damage,
                                 int reach = AttackReach.Adjacent, int dam = 25, int hp = 100, int armor = 0,
                                 int xpk = 20, bool small = true, IReadOnlyList<ImmunityGrant>? bySource = null,
                                 AttackClass? second = null, int secondSource = 0) => new()
    {
        Id = id,
        Type = new UnitType("base:unit/squire-empire", 1, hp, armor, "base:attack/squire-empire", second is null ? "g000000000" : "base:attack/knight",
                            false, small, xpk, 80, bySource ?? None, None),
        Attack = new AttackType("base:attack/squire-empire", 50, cls, 80, reach, dam, 30, false, 1, Source: 0),
        Attack2 = second is { } s ? new AttackType("base:attack/knight", 50, s, 80, reach, 10, 0, false, 1, true, Source: secondSource) : null,
        Attacker = attacker, Slot = slot, HpMax = hp, Hp = hp,
    };
}
