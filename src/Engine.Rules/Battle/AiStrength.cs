using D2.Rules.Battle.Effects;
using D2.Rules.Units;

namespace D2.Rules.Battle;

/// <summary>Сила отряда для бегства ИИ: <c>0x5ceb4b</c> → <c>0x5dda98</c> → <c>0x5dcec7</c> → <c>0x5dd06c</c>
/// (разбор §3, модель <c>re-ai/coeff.py</c>). Константы <c>.rdata</c>: 60, 90, 0.5.
/// <para>ponytail: бой против крепости (<c>0x5ddcb3</c>, ранг 2) и защита города не учитываются — флаги
/// игнорирования взведены с обеих сторон (разбор, «Открытое»).</para></summary>
public static class AiStrength
{
    /// <summary>Относительная сила своей группы: <c>min(1, 0.5 · C_свои / C_враг)</c>; своя группа не может
    /// ранить (<c>0x5dcd53</c>) — 0; враг без силы — 1.</summary>
    public static double Relative(BattleState state, BattleUnit unit, RulesConstants c)
    {
        var mine = state.Allies(unit).ToList();
        var enemy = state.Enemies(unit).ToList();
        double c1 = Group(mine.Select(Seen), ignoreCity: true, enemy.Count, c);
        double c2 = Group(enemy.Select(Seen), ignoreCity: false, mine.Count, c);
        if (!CanDamage(mine.Select(Effective).ToList(), enemy.Select(Effective).ToList())) return 0.0;
        if (c2 == 0) return 1.0;
        return Math.Min(1.0, c1 / c2 * 0.5);
    }

    /// <summary>Юнит, каким его видит сила <c>0x5dd06c</c> в бою: <see cref="Effective"/>, кроме превращённого Ведьмой.
    /// <para><b>Превращённый Ведьмой</b> (статус Transform, бит 22 — <see cref="BattleUnit.Transformed"/>) считается <b>исходной</b>
    /// реализацией: <c>0x5dd09a</c> — при <c>BattleMsgData</c> и <c>getUnitStatus(0x16)</c> (<c>0x5dd0a7</c>) солдат берётся
    /// <c>0x5ea4dd</c> (реализация <c>origTypeId</c> из таблицы юнитов, без обёрток модификаторов), иначе <c>0x5e97d8</c> (текущая с
    /// цепочкой). Броня укрепления у защитника (<see cref="BattleUnit.FortificationArmor"/> — в движке это модификатор-обёртка юнита,
    /// <c>CBattleResultStack</c> <c>0x41b3d9</c>) у исходного солдата поэтому не учитывается; прибавка брони аргументом (<c>0x5dd10c</c>) — та же.
    /// Ранг 1 по листингу и журналу: <c>20261018b-port-synth [5062040]</c> — Бес <c>S143UN000f</c> из Ветерана,
    /// <c>ai_strength_units</c> 124/250 → s 79,36 (= 0,8 · 80 · 124 / 100, атака Ветерана); атака Беса дала бы 19,84. Понижение уровня,
    /// превращение себя и двойник считаются новой реализацией (бита 22 у них нет): s 61,6, 198, 160.</para>
    /// <para>Проверка «может ли ранить» (<c>0x5dcd53</c>) смотрит текущих (<see cref="Effective"/>).</para></summary>
    public static BattleUnit Seen(BattleUnit u) =>
        u is { Transformed: true, OrigType: { } orig, OrigAttack: { } origAttack }
            ? new BattleUnit
            {
                Id = u.Id, Type = orig, Attack = origAttack, Attack2 = u.OrigAttack2,
                Attacker = u.Attacker, Slot = u.Slot, HpMax = u.HpMax, Hp = u.Hp, Xp = u.Xp,
            }
            : Effective(u);

    /// <summary>Юнит, каким его видит сила <c>0x5dd06c</c> в бою: через обёртки модификаторов цепочки (<see cref="BattleUnit.Chain"/>) —
    /// броня, атака (урон, точность) и иммунитеты действующие, как у ИИ на карте (<c>AiTactics.Fighter</c>). Ранг 1 по журналу (С2, 16.10):
    /// <c>20260919-human [12429316]</c> seq 12429354 — <c>ai_strength</c> движка 239,2266 / 145,7512, без цепочки порт 238,692 / 139,336
    /// (<c>S073UN01f9</c> с «Рунным клинком» <c>G000UM2301</c> +10 % урона: 64,152 → 70,567; <c>S073UN01fa</c> 10,692 → 11,227), с цепочкой —
    /// как у движка; без неё защитник считал себя слабее 0,3 и прикрывал отход, а движок бил.
    /// <para>Копия только для счёта силы (<see cref="Unit"/>): переносит лишь то, что сила читает (HP, тип с бронёй и иммунитетами,
    /// атаки, броня укрепления), в состояние боя её не класть.</para></summary>
    public static BattleUnit Effective(BattleUnit u)
    {
        var chain = u.Chain;
        if (chain.Count == 0) return u;
        var eff = Units.EffectiveStats.Of(u.Type, chain);
        var type = u.Type with
        {
            Armor = eff.Armor,
            ImmunitiesBySource = eff.Immunities.Count > 0 ? [.. u.Type.ImmunitiesBySource ?? [], .. eff.Immunities] : u.Type.ImmunitiesBySource,
            ImmunitiesByClass = eff.ClassImmunities.Count > 0 ? [.. u.Type.ImmunitiesByClass ?? [], .. eff.ClassImmunities] : u.Type.ImmunitiesByClass,
        };
        return new BattleUnit
        {
            Id = u.Id, Type = type, Attack = Units.EffectiveStats.OfAttack(u.Attack, chain).Attack, Attack2 = u.Attack2,
            Attacker = u.Attacker, Slot = u.Slot, HpMax = u.HpMax, Hp = u.Hp, Xp = u.Xp,
            FortificationArmor = u.FortificationArmor,
        };
    }

    public static double Group(IEnumerable<BattleUnit> units, bool ignoreCity, int enemies, RulesConstants c) =>
        units.Where(u => u.Alive).Sum(u => Unit(u, ignoreCity, enemies, c));

    /// <summary><c>0x5dcd53</c> (ранг 2, Lua <c>groupCanInflictDamageToOther</c>): другой группы нет — да;
    /// своей нет — нет; иначе живой с уроном первой атакой, которую хоть один враг не держит <c>Always</c>.</summary>
    public static bool CanDamage(IReadOnlyList<BattleUnit> group, IReadOnlyList<BattleUnit> other)
    {
        if (other.Count == 0) return true;
        foreach (var u in group)
        {
            var a = u.Attack;
            if (u.Alive && a.Effect.IsDamaging && other.Any(o => !AiTargets.AlwaysImmune(o, a.Source, a.Class)))
                return true;
        }
        return false;
    }

    /// <summary>Сила юнита <c>0x5dd06c</c>: <c>hp/(60 − 0.6·armor) / (hp/60) · power·0.01 · dmgC · reachC · classC ·
    /// (imm·0.01 + 1)·hp · 0.01</c>; ×1.25 при INFINITE первой атаки из классов <see cref="InfiniteCounts"/>, иначе × коэффициент второй; ×2 при
    /// ATCK_TWICE. Броня — типа плюс <paramref name="fortArmor"/>, не выше 90; при нуле первый множитель 1.</summary>
    /// <param name="fortArmor">Броня укрепления, в котором стоит группа (<c>0x5dcf60</c>, прибавляется в
    /// <c>0x5dd10c</c> до потолка 90): у столицы <c>GVars.PROT_CAP</c> = 50, у деревни <c>GVars.PROT_1..5</c>
    /// = 10…30 по её уровню (<c>0x5878ee</c>: уровень вне 1..5 — 0). Ранг 1 — листинг: загрузчик кладёт
    /// <c>PROT_</c> по смещению <c>+0xf0</c>, <c>PROT_CAP</c> — <c>+0x104</c> (<c>0x5870e3</c>, <c>0x587041</c>).
    /// Ноль, когда группа не в крепости или взведён флаг «без города» (<c>0x5dcfe5</c>). Модификаторы
    /// <c>GcityInf.PROTECTION</c>/<c>Grace.PROTECTION</c> (<see cref="D2.Rules.Map.Fortifications.Protection"/>)
    /// сюда НЕ идут: это разные величины — бой берёт модификатор, ИИ на карте берёт <c>PROT_*</c>. В бою же модификатор защиты висит
    /// на юнитах защитников (<c>CBattleResultStack</c>/<c>CBattleResultVillage</c> слот 1 <c>0x41b3d9</c>/<c>0x41bb6b</c>: <c>dynamic_cast</c>
    /// к <c>CFortification</c>, список <c>[vft+0x28]</c>, каждому юниту группы <c>0x5f5117</c> → <c>addModifier 0x5e9902</c>; снимает слот 2
    /// <c>0x41b564</c>/<c>0x41bc7e</c>), и сила бегства (<c>0x5ceb4b</c>) видит его в броне юнита: probe72 — 41 строка <c>ai_strength_units</c>
    /// <c>0x5ddacd</c>/<c>0x5ddae5</c> с <c>UM3001</c>/<c>UM3002</c> у защитников деревень, <c>armor</c> 0. В порте этот слой —
    /// <see cref="BattleUnit.FortificationArmor"/> (<c>battle_start.fort</c>), он прибавляется к броне здесь же, до потолка 90.</param>
    public static double Unit(BattleUnit u, bool ignoreCity, int enemies, RulesConstants c, int fortArmor = 0)
    {
        if (!u.Alive) return 0.0;
        var a = u.Attack;
        double hp = u.Hp;
        int armor = Math.Min(u.Type.Armor + u.FortificationArmor + fortArmor, 90);
        double v17 = armor == 0 ? hp / 60.0 : hp / (60.0 - 60.0 * armor * 0.01);
        double x = Power(a) * 0.01 * DamageFactor(u, c) * Reach(a, enemies) * ClassFactor(a, ignoreCity)
                   * ((ImmunityWeight(u.Type) * 0.01 + 1.0) * hp);
        double k = v17 / (hp / 60.0) * x * 0.01;
        if (a.Infinite && a.Effect.Tuning.InfiniteCounts) k *= 1.25;
        else if (u.Attack2 is { } a2) k *= a2.Effect.Tuning.SecondFactor;
        if (u.Type.AttackTwice) k *= 2;
        return k;
    }

    private static readonly (int Source, int Weight)[] SourceWeights =
        [(0, 57), (1, 5), (2, 6), (3, 10), (4, 9), (5, 2), (7, 5), (6, 3)];

    /// <summary><c>0x5dd21b</c>: оружие 57, разум 5, жизнь 6, смерть 10, огонь 9, вода 2, воздух 5, земля 3;
    /// +5 за иммунитет к классу Poison. Любой уровень; юнит читается через обёртки модификаторов, так что для ИИ на карте
    /// сюда идут и выданные модификаторами (варды, навыки лидера — их добавляет <c>AiTactics.Fighters</c>).</summary>
    public static int ImmunityWeight(UnitType t)
    {
        int w = 0;
        foreach (var (source, weight) in SourceWeights)
            if (Has(t.ImmunitiesBySource, source)) w += weight;
        return Has(t.ImmunitiesByClass, (int)AttackClass.Poison) ? w + 5 : w;
    }

    private static bool Has(IReadOnlyList<ImmunityGrant>? grants, int category) =>
        grants is not null && grants.Any(g => g.Category == category && g.Level != ImmunityLevel.NotImmune);

    /// <summary>Точность для силы: у эффектов с <see cref="EffectTuning.FullPower"/> (лечение, усиление, исцеление, призыв, передача
    /// атаки, двойник) — 100, у прочих — точность атаки.</summary>
    private static double Power(AttackType a) => a.Effect.Tuning.FullPower ? 100.0 : a.Power;

    /// <summary><c>0x5dd7fd</c>: прямой урон — <c>min(QTY_DAM, DamageMax)</c>, лечение — <c>QTY_HEAL</c>, прочее 1
    /// (<see cref="AttackEffect.AiDamageFactor"/>).</summary>
    private static double DamageFactor(BattleUnit u, RulesConstants c) => u.Attack.Effect.AiDamageFactor(u, c);

    /// <summary><c>0x5dd791</c>: ANY 1.5, ALL <c>(n − 1)·0.4 + 1</c>, иначе 1.</summary>
    private static double Reach(AttackType a, int enemies) => a.Reach switch
    {
        AttackReach.Any => 1.5,
        AttackReach.All => (enemies - 1) * 0.4 + 1.0,
        _ => 1.0,
    };

    /// <summary><c>0x5dd574</c>: множитель по эффекту первой атаки (<see cref="AttackEffect.AiClassFactor"/>): паралич, окаменение, страх,
    /// дробление 30; вампиризм 1.5; усиление, обереги, ослабление, замедление 40; переток 2; призыв 200; понижение уровня и превращение
    /// в себя 100; передача атаки 50; двойник 200 (без учёта города 100); превращение другого 100 (не площадное 60); прочее 1. Второй
    /// атаки (<c>0x5dd9a2</c>) — <see cref="EffectTuning.SecondFactor"/>: исцеление 1.1, яды 1.15, воскрешение 3, прочее 1.</summary>
    private static double ClassFactor(AttackType a, bool ignoreCity) => a.Effect.AiClassFactor(a, ignoreCity);
}
