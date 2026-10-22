namespace D2.Rules.Battle.Effects;

// Двадцать три класса атак оригинала (LattC.dbf) — встроенные эффекты. Признаки — ровно прежние списки по классам (Formulas.ClassHasPower,
// Immunity.Checked, Attacks.TargetsAllies, Attacks.RejectsRetreating, Attacks.HasDamage, AiTargets.IsDamaging/IsDirect), числа ИИ —
// прежние таблицы AiStrength и AiTargets; тела ударов, проверок и выбора цели — прежние функции (Attacks, LifeAttacks, TransformAttacks,
// WardAttacks, AiTargets, AiRareTargets). Порядок бросков не меняется: его задаёт Attacks.Apply.

/// <summary>Урон (<c>L_DAMAGE</c>, <c>Damage::onHit</c> <c>0x65b4f4</c>): крит и вампиризм цепочки.</summary>
public sealed record DamageEffect() : AttackEffect("damage", AttackClass.Damage)
{
    public override bool HasPower => true;
    public override bool HasDamage => true;
    public override bool IsDamaging => true;
    public override bool IsDirectDamage => true;
    public override AttackOutcome Hit(in EffectHit hit) =>
        Attacks.ApplyDamage(hit.Attacker, hit.Attack, hit.Target, hit.DrainPercent, hit.Rng, hit.Constants, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiTargets.FindDirect(ctx, unit, attack, targets);
}

/// <summary>Вампиризм (<c>L_DRAIN</c>) и вампиризм с перетоком к союзникам (<c>L_DRAIN_OVERFLOW</c>): урон тем же
/// <c>computeDamage</c>, лечение атакующего бросков не тратит (<see cref="LifeAttacks.Drain"/>).</summary>
public sealed record DrainEffect(bool Overflow) : AttackEffect(Overflow ? "drainOverflow" : "drain",
                                                              Overflow ? AttackClass.DrainOverflow : AttackClass.Drain)
{
    public override bool HasPower => true;
    public override bool HasDamage => true;
    public override bool IsDamaging => true;
    public override bool IsDirectDamage => true;
    public override AttackOutcome Hit(in EffectHit hit) =>
        LifeAttacks.Drain(hit.Attacker, hit.Attack, hit.Target, hit.DrainPercent, hit.Rng, hit.Constants, hit.State, overflow: Overflow);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiTargets.FindDirect(ctx, unit, attack, targets);
}

/// <summary>Паралич (<c>L_PARALYZE</c>) и окаменение (<c>L_PETRIFY</c>) — отдельные статусы с общим сроком.</summary>
public sealed record DisableEffect(bool Petrify) : AttackEffect(Petrify ? "petrify" : "paralyze",
                                                               Petrify ? AttackClass.Petrify : AttackClass.Paralyze)
{
    public override bool HasPower => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => !target.Paralyzed && !target.Petrified;
    public override AttackOutcome Hit(in EffectHit hit) => Attacks.ApplyDisable(hit.Attack, hit.Target, hit.Round, Petrify);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiTargets.FindParalyze(ctx, unit, targets);
}

/// <summary>Страх (<c>L_FEAR</c>): бегство одной цели, бежать некуда — короткий паралич.</summary>
public sealed record FearEffect() : AttackEffect("fear", AttackClass.Fear)
{
    public override bool HasPower => true;
    public override bool RejectsRetreating => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => !target.Paralyzed && !target.Petrified;
    public override AttackOutcome Hit(in EffectHit hit) => Attacks.ApplyFear(hit.Target, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.Fear(ctx, targets);
}

/// <summary>Лечение (<c>L_HEAL</c>): бросков нет, павшего не поднимает.</summary>
public sealed record HealEffect() : AttackEffect("heal", AttackClass.Heal)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override AttackOutcome Hit(in EffectHit hit) => Attacks.ApplyHeal(hit.Attack, hit.Target);
    public override double AiDamageFactor(BattleUnit unit, RulesConstants constants) => unit.Attack.QtyHeal;
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiTargets.FindHeal(ctx, targets);
}

/// <summary>Усиление урона (<c>L_BOOST_DAMAGE</c>): уровень 1…4 атаки, проценты из глобальных величин.</summary>
public sealed record BoostDamageEffect() : AttackEffect("boostDamage", AttackClass.BoostDamage)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override bool RejectsRetreating => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => Attacks.BoostAccepted(attacker, attack, target);
    public override AttackOutcome Hit(in EffectHit hit) => Attacks.ApplyBoostDamage(hit.Attack, hit.Target);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiTargets.FindBoost(ctx, targets);
}

/// <summary>Ослабление урона (<c>L_LOWER_DAMAGE</c>) и замедление (<c>L_LOWER_INITIATIVE</c>). Цели ИИ у них нет (<c>0x5d056b</c>).</summary>
public sealed record LowerEffect(bool Initiative) : AttackEffect(Initiative ? "lowerInitiative" : "lowerDamage",
                                                                Initiative ? AttackClass.LowerInitiative : AttackClass.LowerDamage)
{
    public override bool HasPower => true;
    public override bool RejectsRetreating => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) =>
        Initiative ? Attacks.SlowAccepted(target) : Attacks.WeakenAccepted(attack, target);
    public override AttackOutcome Hit(in EffectHit hit) =>
        Initiative ? Attacks.ApplyLowerInitiative(hit.Attack, hit.Target) : Attacks.ApplyLowerDamage(hit.Attack, hit.Target);
}

/// <summary>Яд (<c>L_POISON</c>), обморожение (<c>L_FROSTBITE</c>), ожог (<c>L_BLISTER</c>) — урон в начале хода жертвы.</summary>
public sealed record DamageOverTimeEffect(AttackClass Kind) : AttackEffect(Kind switch
{
    AttackClass.Poison => "poison",
    AttackClass.Frostbite => "frostbite",
    AttackClass.Blister => "blister",
    _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "урон со временем — яд, обморожение или ожог"),
}, Kind)
{
    public override bool HasPower => true;
    public override bool RejectsRetreating => true;
    public override bool HasDamage => true;
    public override bool IsDamaging => true;
    public override bool IsDot => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) =>
        !target.Dots.Any(d => d.Class == attack.Class && d.Damage > attack.QtyDamage);
    public override AttackOutcome Hit(in EffectHit hit) => Attacks.ApplyDot(Kind, hit.Attack, hit.Target, hit.Round);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiTargets.FindDotTarget(ctx, unit, attack, targets, dmg);
}

/// <summary>Воскрешение (<c>L_REVIVE</c>): павший встаёт с половиной потолка.</summary>
public sealed record ReviveEffect() : AttackEffect("revive", AttackClass.Revive)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override AttackOutcome Hit(in EffectHit hit) => LifeAttacks.Revive(hit.Target);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.Revive(targets);
}

/// <summary>Исцеление (<c>L_CURE</c>): снимает яды сразу, паралич, окаменение и превращение — по биту Cured.</summary>
public sealed record CureEffect() : AttackEffect("cure", AttackClass.Cure)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => Attacks.CanBeCured(target);
    public override AttackOutcome Hit(in EffectHit hit) => Attacks.ApplyCure(hit.Target);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.Cure(ctx, targets);
}

/// <summary>Призыв (<c>L_SUMMON</c>): юнит из списка превращений атаки встаёт в свободную клетку своего отряда.</summary>
public sealed record SummonEffect() : AttackEffect("summon", AttackClass.Summon)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override bool IsOffence => true;
    public override AttackOutcome Hit(in EffectHit hit) => LifeAttacks.Summon(hit.Attacker, hit.Attack, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.Summon(ctx, unit);
}

/// <summary>Понижение уровня (<c>L_DRAIN_LEVEL</c>): цель становится типом уровнем ниже.</summary>
public sealed record DrainLevelEffect() : AttackEffect("drainLevel", AttackClass.DrainLevel)
{
    public override bool HasPower => true;
    public override bool RejectsRetreating => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => Attacks.HasPrevLevel(target);
    public override AttackOutcome Hit(in EffectHit hit) => LifeAttacks.DrainLevel(hit.Target, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.DrainLevel(ctx, targets);
}

/// <summary>Передача атаки (<c>L_GIVE_ATTACK</c>): союзник ходит ещё раз.</summary>
public sealed record GiveAttackEffect() : AttackEffect("giveAttack", AttackClass.GiveAttack)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override bool RejectsRetreating => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) =>
        !ReferenceEquals(target, attacker) && target.Attack.Class != AttackClass.GiveAttack;
    public override AttackOutcome Hit(in EffectHit hit) => TransformAttacks.GiveAttack(hit.Target);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.GiveAttack(ctx, targets);
}

/// <summary>Двойник (<c>L_DOPPELGANGER</c>): копия цели; уже превращённый бьёт запасной атакой.</summary>
public sealed record DoppelgangerEffect() : AttackEffect("doppelganger", AttackClass.Doppelganger)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override AttackOutcome Hit(in EffectHit hit) => hit.Attacker.IsTransformed
        ? Attacks.AltAttack(hit.Attacker, hit.Attack, hit.Target, hit.Rng, hit.Constants, hit.Round, hit.State)
        : TransformAttacks.Doppelganger(hit.Attacker, hit.Target, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.Doppelganger(ctx, unit, attack, targets);
    /// <summary>Без учёта города — <see cref="EffectTuning.ClassFactorAlt"/>, иначе <see cref="EffectTuning.ClassFactor"/>.</summary>
    public override double AiClassFactor(AttackType attack, bool ignoreCity) => ignoreCity ? Tuning.ClassFactorAlt : Tuning.ClassFactor;
}

/// <summary>Превращение в себя (<c>L_TRANSFORM_SELF</c>): по своим — превращение, по врагу — запасная атака.</summary>
public sealed record TransformSelfEffect() : AttackEffect("transformSelf", AttackClass.TransformSelf)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override AttackOutcome Hit(in EffectHit hit) => hit.Attacker.Attacker == hit.Target.Attacker
        ? TransformAttacks.TransformSelf(hit.Attack, hit.Target, hit.State)
        : Attacks.AltAttack(hit.Attacker, hit.Attack, hit.Target, hit.Rng, hit.Constants, hit.Round, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.TransformSelfPossible(ctx.State, unit) ? unit : null;
}

/// <summary>Превращение другого (<c>L_TRANSFORM_OTHER</c>).</summary>
public sealed record TransformOtherEffect() : AttackEffect("transformOther", AttackClass.TransformOther)
{
    public override bool HasPower => true;
    public override AttackOutcome Hit(in EffectHit hit) => TransformAttacks.TransformOther(hit.Attack, hit.Target, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.TransformOther(ctx, targets);
    /// <summary>Площадное — <see cref="EffectTuning.ClassFactor"/>, иначе <see cref="EffectTuning.ClassFactorAlt"/>.</summary>
    public override double AiClassFactor(AttackType attack, bool ignoreCity) =>
        attack.Reach == AttackReach.All ? Tuning.ClassFactor : Tuning.ClassFactorAlt;
}

/// <summary>Обереги (<c>L_BESTOW_WARDS</c>): модификаторы атаки ложатся на союзника.</summary>
public sealed record BestowWardsEffect() : AttackEffect("bestowWards", AttackClass.BestowWards)
{
    public override bool ChecksImmunity => false;
    public override bool TargetsAllies => true;
    public override bool RejectsRetreating => true;
    public override AttackOutcome Hit(in EffectHit hit) => WardAttacks.BestowWards(hit.Attacker, hit.Attack, hit.Target, hit.State);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.BestowWards(ctx, unit, targets);
}

/// <summary>Дробление брони (<c>L_SHATTER</c>): снятое + урон атаки, до 100; урона по здоровью нет.</summary>
public sealed record ShatterEffect() : AttackEffect("shatter", AttackClass.Shatter)
{
    public override bool RejectsRetreating => true;
    public override bool HasDamage => true;
    public override bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => Attacks.ShatterAccepted(target);
    public override AttackOutcome Hit(in EffectHit hit) => WardAttacks.Shatter(hit.Attack, hit.Target);
    public override BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) =>
        AiRareTargets.ByValue(ctx, targets, u => u.Type.Armor);
}
