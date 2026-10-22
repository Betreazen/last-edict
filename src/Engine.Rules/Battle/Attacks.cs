using D2.Rules.Battle.Effects;
using D2.Rules.Rng;

namespace D2.Rules.Battle;

/// <summary>Итог одного удара. Applied — эффект наложен/урон нанесён (ложь при промахе, при
/// лечении здорового и при иммунитете). Immune — удар остановлен иммунитетом цели
/// (<see cref="Immunity"/>). Miss — исход броска точности, и у иммунного удара он тоже есть: движок
/// бросает ДО проверки иммунитета (задача 20 Ф7, <see cref="Attacks.Apply"/>). Цель с Miss или Immune
/// второй атакой не бьётся (<see cref="BattleSim.Act"/>).
/// <para>DrainHealed — HP, вернувшиеся <b>атакующему</b> (Drain, DrainOverflow и Damage с вампиризмом
/// от модификатора); Healed — HP, полученные целью. У вампиризма это разные юниты, поэтому и поля
/// разные.</para>
/// <para>Damage и CritDamage — выходы <c>computeDamage</c> (<c>*attackDamage</c>, <c>*critDamage</c>):
/// урон после брони и крит поверх него, ровно поля <c>dmg</c>/<c>crit_dmg</c> журнала. HP цели падает
/// на их сумму. Деление при добивании (<c>0x65b5dc</c>–<c>0x65b612</c>: <c>crit = min(crit, hpLost)</c>)
/// идёт только в запись попадания <c>0x62c508</c>; зонд пишет выходы <c>computeDamage</c> до него
/// (<c>probehooks.cpp:246-264</c>), поэтому здесь его нет.</para></summary>
public sealed record AttackOutcome(bool Miss, int Damage, int Healed, bool Applied, bool Immune = false,
                                   int DrainHealed = 0, int CritDamage = 0);

/// <summary>Применение атаки. Ф4: Damage, Heal, Paralyze, Poison/Frostbite/Blister, вторая атака
/// юнита (ATTACK2_ID — <see cref="BattleSim.Act"/>), двойная атака (<see cref="AttacksPerTurn"/>).
/// Задача 5 Ф6 добавила первый круг статусов без урона: Fear, Petrify, Cure, BoostDamage,
/// LowerDamage, LowerInitiative; задача 6 — жизнь и смерть (<see cref="LifeAttacks"/>); задача 7 —
/// превращения и копии (<see cref="TransformAttacks"/>). Остальные классы — исключение вместо
/// тихого «ничего» (<see cref="NotImplemented"/>).
///
/// <para><see cref="Apply"/> — диспетчер: каждая ветвь одна строка, тело класса рядом приватным
/// <c>ApplyX</c> либо в соседнем файле. Разбор сделан задачей 7 по долгу ревью задачи 5 (метод
/// разросся до 110 строк) и поведения не меняет.</para>
///
/// Ранги: 1 — измерено корпусом (20260906-a..e) или прочитано в дизассемблере GOG до ret,
/// 2 — из источника, корпусу не противоречит, 3 — гипотеза, корпус слеп. Источники —
/// battle-rules-survey.md (A — тулсет UnveN, B — Disciples2-RL).</summary>
public static class Attacks
{
    /// <summary>Классы с уроном (A attackutils.cpp:144-151 attackHasDamage): для них движок зовёт
    /// computeDamageWithBuffs при оценке хода ИИ (событие dmg_buffs random=false — по одному на
    /// каждую такую атаку юнита, C1HarnessTests).</summary>
    /// <summary>Юнит-лекарь: класс Heal у первой или второй атаки. После гибели стороны движок даёт
    /// ход только таким (BattleSim.Finished).</summary>
    public static bool IsHealer(BattleUnit u) =>
        u.Attack.Class == AttackClass.Heal || u.Attack2?.Class == AttackClass.Heal;

    public static bool HasDamage(AttackClass cls) => AttackEffects.Of(cls).HasDamage;

    /// <summary>Бросает ли удар точность (два <c>rng(100)</c> и <c>attackShouldMiss</c>). Ранг 1 (задача 32 Ф7):
    /// <c>CBatLogic::battleTurn</c> (GOG <c>0x624d2a</c>) спрашивает <c>canMiss</c> (слот 6 vftable атаки) и только
    /// при «да» зовёт <c>attackShouldMiss</c> (<c>0x624db2</c>). У обёрток предмета <c>CBatAttackItem</c>,
    /// <c>CBatAttackUseOrb</c>, <c>CBatAttackUseTalisman</c> (vftable <c>0x6f3674</c>, <c>0x6f36c4</c>, <c>0x6f3714</c>)
    /// слот 6 — <c>0x6642ab</c>: <c>xor al, al; ret 8</c>, всегда «нет», внутренняя атака не спрашивается; слоты 7
    /// и 8 (<c>0x6642ba</c>, <c>0x6642ee</c>) ей делегируют через <c>this+4</c>, поэтому иммунитет у предмета
    /// обычный. Для сравнения у <c>CBatAttackPoison</c> слот 6 — <c>0x65e34b</c>: <c>mov al, 1</c>. Корпус:
    /// <c>WikiBattleCorpusTests</c>, счётчик <c>item_attack_no_roll</c>. Без предмета — по классу
    /// (<see cref="Formulas.ClassHasPower"/>).</summary>
    public static bool RollsAccuracy(AttackClass cls, bool itemAttack) => !itemAttack && Formulas.ClassHasPower(cls);

    private static bool RollsAccuracy(AttackEffect effect, bool itemAttack) => !itemAttack && effect.HasPower;

    /// <summary>Один удар <paramref name="attack"/> (первая или вторая атака <paramref name="attacker"/>)
    /// по target. Damage — бит в бит прежний BattleSim.Act: ShouldMiss по двум rng(100), затем
    /// DamageWithBuffs(boost 0, lower 0, addRandom) и ComputeDamage по броне цели.
    ///
    /// Heal бросков НЕ тратит и attackShouldMiss не зовёт — ранг 1: corpus/20260906-b, 48 ходов
    /// Acolyte и 59 ходов Oracle Elf без событий miss/power и без rng(100) между turn_before и
    /// turn_after (у Oracle — только броски перемешивания площадной цели, <see cref="Shuffle"/>).
    /// Прежняя гипотеза Task 6 «два rng(100) при power 100» корпусом опровергнута.
    /// Величина — min(QTY_HEAL, HpMax − Hp): ранг 1 (b: +20 у Acolyte, 60 с потолком у Oracle).</summary>
    /// <param name="state">Состояние боя; нужно только трём классам, которые смотрят за пределы
    /// пары «атакующий — цель»: DrainOverflow (излишек уходит союзникам), Summon (добавляет юнита в
    /// бой) и DrainLevel (берёт тип предыдущего уровня из справочников). Остальным классам null не
    /// мешает — они его не читают.</param>
    /// <param name="powerReduction">Штраф точности, накопленный движком к этой цели
    /// (<see cref="Formulas.MageLeaderPowerReduction"/>); у всех, кроме магов-лидеров, ноль.</param>
    /// <param name="itemAttack">Удар атакой предмета (сфера, талисман — <c>attack_new</c> с <c>impl</c> типа
    /// <c>IM</c>): броска точности нет (<see cref="RollsAccuracy"/>), иммунитет спрашивается как обычно.</param>
    /// <param name="sureHit">Промах этой цели не останавливает удар (<see cref="BattleSim.SureFirstHit"/>): броски
    /// тратятся, <see cref="AttackOutcome.Miss"/> остаётся исходом броска (так его пишет журнал), эффект наложен.</param>
    public static AttackOutcome Apply(BattleUnit attacker, AttackType attack, BattleUnit target, IRng rng,
                                      RulesConstants c, int round, BattleState? state = null, int powerReduction = 0,
                                      bool itemAttack = false, bool sureHit = false)
    {
        // Эффект атаки — что делает удар (реестр эффектов, этап Э2): признаки движка (точность, иммунитет) и тело onHit.
        var effect = attack.Effect;
        // Девять классов иммунитета не спрашивают вовсе — Immunity.Checked, ранг 1 по слоту 8 vftable.
        bool checksImmunity = effect.ChecksImmunity;

        // Эффективная атака: база из Gattacks плюс цепочка модификаторов атакующего (предмет,
        // заклинание, вард — задачи 9-11). Пустая цепочка возвращает ту же самую ссылку, поэтому
        // Ф4/Ф5 не меняются ни на бит, ни на аллокацию. DrainPercent — прибавка вампиризма
        // (GmodifL TYPE 15), её читает только LifeAttacks.Drain.
        var effective = Units.EffectiveStats.OfAttack(attack, attacker.Chain);
        attack = effective.Attack;

        // Классы БЕЗ точности (Formulas.ClassHasPower == false) броска на промах не делают вовсе.
        // Ранг 1 для Heal (corpus/20260906-b: 48 ходов Acolyte и 59 ходов Oracle Elf без событий
        // miss/power и без rng(100)); для остальных членов списка — ранг 2 по A attackutils.cpp:131-142
        // (attackHasPower), но список подпёрт с трёх сторон: Heal вне его не бросает (ранг 1), а
        // Paralyze и Fear внутри него бросают ровно два rng(100) (ранг 1, corpus/20260906-c).
        // Задача 8 закрыла последние два класса (BestowWards и Shatter), так что нереализованных в
        // этой группе больше нет; класс вне LattC — эффект UnknownEffect (удар — NotSupportedException).
        if (!effect.HasPower)
            return checksImmunity && Immunity.Blocks(target, attack)
                ? new AttackOutcome(false, 0, 0, false, Immune: true)
                : effect.Hit(new EffectHit(attacker, attack, target, effective.DrainPercent, rng, c, round, state));

        // Бросок на промах идёт ДО иммунитета, и заряд Once тратится даже на промахе — ранг 1
        // (задача 20 Ф7). CBatLogic::battleTurn (GOG 0x62481a) по каждой цели: canMiss (слот 6,
        // 0x624d2a) → attackShouldMiss (0x624db2) → isImmune (слот 8, 0x624e03); иммунная ветка
        // (0x624ec2) снимает Once по источнику (0x625020, RemoveUnitAttackSourceWard) и по классу
        // (0x625073) без проверки промаха и уходит к следующей цели — onHit (0x6251ab) не зовётся.
        // Корпус (WikiBattleCorpusTests, immune_after_roll): у классов с точностью 1452 события immune с
        // always_only = false из 1452 идут сразу за своим miss; ещё 10 — атаки предметов, у которых
        // броска точности нет вовсе (itemAttack → RollsAccuracy, задача 32). Прежний порядок
        // «иммунитет, бросков нет» и слова вики «промах вард не снимает» — неверны.
        // Сдвиг ИИ по сложности — до штрафа мага-лидера (getAttackPower 0x62790c…0x627a08, ранг 1).
        int shifted = Formulas.ShiftPower(Formulas.AttackPower(attack), state?.PowerShift(attacker) ?? 0);
        int power = Math.Max(0, shifted - powerReduction);
        bool miss = RollsAccuracy(effect, itemAttack) && Formulas.ShouldMiss(power, rng.Next(100), rng.Next(100));
        if (checksImmunity && Immunity.Blocks(target, attack))
            return new AttackOutcome(miss, 0, 0, false, Immune: true);
        if (miss && !sureHit)
            return new AttackOutcome(true, 0, 0, false);

        var hit = effect.Hit(new EffectHit(attacker, attack, target, effective.DrainPercent, rng, c, round, state));
        return miss ? hit with { Miss = true } : hit;
    }

    /// <summary>Удар запасной атакой (<c>Gattacks.ALT_ATTACK</c>) — вторая ветвь
    /// <see cref="AttackClass.Doppelganger"/> и <see cref="AttackClass.TransformSelf"/>. Долг задачи 7,
    /// закрыт задачей 8: поля <c>AltAttackId</c> в <see cref="AttackType"/> тогда не было, потому что
    /// <c>D2.Data.RulesLoader.Load</c> был отдан другой задаче волны.
    /// <para>Рекурсия, а не отдельный путь, — потому что движок делает ровно это: у обоих классов
    /// слоты <c>canMiss</c>, <c>isImmune</c> и <c>onHit</c> в этой ветви вызывают одноимённые слоты
    /// объекта запасной атаки с теми же аргументами (<c>0x6614bf</c> и <c>0x662336</c>). Значит
    /// запасная атака приносит с собой и свою точность (две кости, если её класс промахивается), и
    /// свой иммунитет — а <see cref="Apply"/> и есть то место, где всё это уже собрано. Ранг 1.</para>
    /// <para>Ссылки нет — это отказ, а не «ничего не делаем»: в ванили обе ветвящиеся атаки
    /// (<c>g000aa0164</c>, <c>g000aa0171</c>) запасную имеют, и её отсутствие означает мод, который
    /// модель молча портить не должна.</para></summary>
    internal static AttackOutcome AltAttack(BattleUnit attacker, AttackType attack, BattleUnit target,
                                           IRng rng, RulesConstants c, int round, BattleState? state)
    {
        var rules = state?.Rules ?? throw new InvalidOperationException(
            $"атака {attack.Id} ({attack.Class}) уходит к запасной (ALT_ATTACK {attack.AltAttackId}), " +
            "а для неё нужен BattleState.Rules; бой собран без справочников");
        var alt = rules.AltAttackOf(attack) ?? throw new NotSupportedException(
            $"{attack.Class} атакой {attack.Id}: движок в этом случае отдаёт удар запасной атаке, " +
            "но ALT_ATTACK у неё пуст — воспроизводить нечем");
        return Apply(attacker, alt, target, rng, c, round, state);
    }

    /// <summary><c>Damage::onHit</c> (GOG <c>0x65b4f4</c>) после попадания. boost и lower — проценты
    /// статусов, которые <c>computeDamageWithBuffs</c> принимает сам (см. Formulas.DamageWithBuffs).
    /// <code>
    /// ret = computeDamage(..., computeCriticalHit = 1, &amp;damage, &amp;crit)   0x65b577–0x65b593
    /// hpBefore = hp(target)                                                0x65b5a3–0x65b5b3
    /// changeUnitHp(target, −ret); checkUnitDeath                           0x65b5c6, 0x65b5d7
    /// hpLost = hpBefore − hp(target)
    /// heal = attack-&gt;getDrain(hpLost)                                     0x65b667–0x65b67b (слот 10)
    /// if heal &gt; 0: changeUnitHp(attacker, +heal)                          0x65b6ba
    /// </code>
    /// Крит — задача 15 Ф7, вампиризм от модификатора — задача 17 Ф7; обе ранг 1 по коду. Лечение
    /// считается от потерянного HP, то есть с критом и с усечением добивающего удара; бросков не тратит
    /// (генератора в хвосте onHit нет). Без модификатора <c>getDrain</c> — ноль
    /// (<see cref="LifeAttacks.ModifierDrain"/>).</summary>
    internal static AttackOutcome ApplyDamage(BattleUnit attacker, AttackType attack, BattleUnit target,
                                             int drainPercent, IRng rng, RulesConstants c, BattleState? state)
    {
        int dwb = Formulas.DamageWithBuffs(attack, Formulas.DamageMax(c, attacker.Type.GlobalId),
                                           c.BoostDamagePercent(attacker.BoostDamageLevel),
                                           c.LowerDamagePercent(attacker.LowerDamageLevel), true, rng, c,
                                           state?.HumanOnEasy(attacker) == true);
        int damage = Formulas.ComputeDamage(dwb, Formulas.Armor(target, c));
        int crit = Formulas.HasCriticalHit(attack, attacker) ? Formulas.CriticalDamage(dwb) : 0;
        int before = target.Hp;
        target.Hp = Math.Max(0, target.Hp - (damage + crit));
        int healed = LifeAttacks.Restore(attacker, LifeAttacks.ModifierDrain(before - target.Hp, drainPercent));
        return new AttackOutcome(false, damage, 0, true, DrainHealed: healed, CritDamage: crit);
    }

    /// <summary>Паралич (бит 2) и окаменение (бит 3) — отдельные статусы с общим сроком. onHit Paralyze
    /// <c>0x65ae85</c> / Petrify <c>0x65b207</c>: бит, раунд наложения (<c>0x623369</c>), при INFINITE —
    /// бит 4 DisableLong (<c>0x65af40</c> <c>getInfinite</c>). Короткий снимается в ближайший ход жертвы
    /// без бросков (ранг 1, corpus/20260906-c, Ghost); длинный — броском в каждый ход жертвы
    /// (BattleSim.BeginTurn, ранг 1: 20260920-gaps [393470]).</summary>
    internal static AttackOutcome ApplyDisable(AttackType attack, BattleUnit target, int round, bool petrify)
    {
        if (petrify) target.Petrified = true; else target.Paralyzed = true;
        target.DisableLong = attack.Infinite;
        target.DisableAppliedRound = round;
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Poison/Frostbite/Blister. Ранг 1 (corpus/20260906-e, Poison INFINITE у Master Thug 20
    /// и Giant Black Spider 35): немедленного урона нет (0013: 100 → 65 от удара 35, яд не вычтен),
    /// тик = QTY_DAM атаки в начале хода жертвы (TickDots), повтор того же класса перекрывает
    /// (A: один &lt;x&gt;AppliedRound на юнита, battlemsgdata.h:210). Не-INFINITE DoT — ранг 2 по
    /// дизассемблеру updatePoisonLongEffect 0x62832b: без бита PoisonLong снимается сразу.</summary>
    internal static AttackOutcome ApplyDot(AttackClass cls, AttackType attack, BattleUnit target, int round)
    {
        target.Dots.RemoveAll(d => d.Class == cls);
        target.Dots.Add(new DotEffect { Class = cls, Damage = attack.QtyDamage, AppliedRound = round, Long = attack.Infinite });
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Страх. Ранг 1 (corpus/20260906-c, seq 53135-53147): попадание ставит ОДИН бит 29 у
    /// ОДНОЙ цели и не тратит ни одного броска сверх двух на точность. Отступление ИНДИВИДУАЛЬНОЕ:
    /// в том же ходе Fear (площадной, три цели) попал по одной из трёх, а двое живых союзников
    /// жертвы (hp 11 и 15) остались в очереди и ходили дальше — групповое решение пометило бы всех.
    /// Уход с поля — BattleSim.BeginTurn.
    /// <para><b>Бежать некуда — паралич (задача 25 Ф7, ранг 1).</b> <c>onHit</c> Fear (GOG
    /// <c>0x65c76f</c>) спрашивает <c>0x65c85b</c> → <c>0x5f51bf</c> (<see cref="BattleState.CanRetreat"/>):
    /// «можно» — бит 29 (<c>push 0x1d</c>, <c>0x65c814</c>); «нельзя» — бит 2 Paralyze
    /// (<c>0x65c826</c>) и раунд наложения (<c>0x65c846</c>), бита 4 DisableLong нет, то есть паралич
    /// короткий. Права вики; тулсет (ранг 2, <c>hooks.cpp:3196-3231</c>) повторяет то же. Корпус:
    /// 12 попаданий Fear, все по отрядам в поле, все — бит 29; по гарнизону и руинам — ни одного.</para></summary>
    internal static AttackOutcome ApplyFear(BattleUnit target, BattleState? state)
    {
        if (state?.CanRetreat(target) == false) target.Paralyzed = true;
        else target.Retreating = true;
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Снижение урона. Уровень — LEVEL атаки (в ванили единственная атака класса
    /// g000aa1168 имеет LEVEL 2, то есть −33 %; уровень 1 (−50 %) вани́льной атакой не встречается
    /// ни разу). Ранг 2: числа BATLOWERD1..2 — из GVars (ранг 1), а то, что уровень берётся из
    /// Gattacks.LEVEL и последнее наложение вытесняет прежнее, — ранг 3.
    /// <para>«Long damage boost (until battle is over or lower damage applied)» —
    /// A battlemsgdata.h:52, ранг 2: снижение урона гасит буст, в том числе длинный.</para></summary>
    internal static AttackOutcome ApplyLowerDamage(AttackType attack, BattleUnit target)
    {
        target.LowerDamageLevel = attack.Level;
        target.LowerDamageLong = attack.Infinite;
        target.BoostDamageLevel = 0;
        target.BoostDamageLong = false;
        return new AttackOutcome(false, 0, 0, attack.Level > 0);
    }

    /// <summary>Снижение инициативы. Значащий уровень только 1
    /// (RulesConstants.LowerInitiativePercent); применяется в TurnOrder поверх инициативы, уже
    /// посчитанной цепочкой модификаторов — порядок как в computeInitiative
    /// (A attackdescriptor.cpp:176-186), ранг 2.</summary>
    internal static AttackOutcome ApplyLowerInitiative(AttackType attack, BattleUnit target)
    {
        target.LowerInitiativeLevel = attack.Level;
        target.LowerInitiativeLong = attack.Infinite;
        return new AttackOutcome(false, 0, 0, attack.Level > 0);
    }

    /// <summary>Лечение. Math.Max — на случай Hp &gt; HpMax (снимок корпуса), чтобы лечение не
    /// отнимало HP; павшего не трогает.</summary>
    internal static AttackOutcome ApplyHeal(AttackType attack, BattleUnit target)
    {
        // Павшего лечение не поднимает: у Патриарха его поднимает вторая атака (Revive), HP 0 → ровно половина потолка
        // (corpus/20260908-f6/map-001 seq 1286030: 0 → 60 при потолке 120, без прибавки лечения 120).
        if (!target.Alive) return new AttackOutcome(false, 0, 0, false);
        int healed = Math.Max(0, Math.Min(attack.QtyHeal, target.HpMax - target.Hp));
        target.Hp += healed;
        return new AttackOutcome(false, 0, healed, healed > 0);
    }

    /// <summary>Cure (задача 22 Ф7, ранг 1). Бросков не тратит (класс без точности).
    /// <para><c>onHit</c> (GOG <c>0x6600c8</c>) снимает сразу только три яда с их битами Long —
    /// статусы 15…20 (<c>push 0xf</c>…<c>push 0x14</c>) — и ставит Cured (21). Больше ничего:
    /// <b>страх (29), снижение урона (10–12) и снижение инициативы (13–14) «Исцеление» не снимает</b>.
    /// Паралич, окаменение и превращение снимаются позже, по биту Cured, —
    /// <see cref="ResolveCured"/>. Прежний список (ранг 2, чужой реверс B) снимал страх и оба
    /// снижения — неверно; вики права про страх и снижение урона, но ошибается, говоря, что
    /// снижение инициативы «лечится».</para>
    /// <para>Корпус: три снятия яда (биты 15, 16) в ход лекаря; бит 29 у троих остался
    /// (<c>_failed-20260908-f6-112158/map-001</c>, seq 96968); длинный паралич S073UN01a2 остался в ход
    /// лекаря и в <c>turn_before</c> цели (0x200014), а в её <c>turn_after</c> снят, и цель в этот ход
    /// ударила (<c>passive-004</c>, seq 5037704–5037728).</para>
    /// <para>Applied — было ли что лечить по <see cref="CanBeCured"/>; Cured ставится всегда, как в
    /// движке (<c>push 1; push 0x15</c> безусловно).</para></summary>
    internal static AttackOutcome ApplyCure(BattleUnit target)
    {
        bool curable = CanBeCured(target);
        target.Dots.Clear();
        target.Cured = true;
        return new AttackOutcome(false, 0, 0, curable);
    }

    /// <summary><c>unitCanBeCured</c> (GOG <c>0x65c310</c>): статус 2, 3, 15, 17, 19 или 22 —
    /// паралич, окаменение, любой из трёх ядов, превращение чужим. Это же — <c>canPerform</c>
    /// класса Cure (<c>0x65fff2</c> → <c>0x65c310</c>). Ранг 1.</summary>
    public static bool CanBeCured(BattleUnit u) => u.Paralyzed || u.Petrified || u.Dots.Count > 0 || u.Transformed;

    /// <summary>Отложенная часть «Исцеления»: у каждого юнита с битом Cured бит снимается, паралич
    /// и окаменение снимаются, превращение чужим откатывается. Ранг 1 — <c>0x628874</c>, первый
    /// вызов обработки хода <c>0x627ffa</c>: обход всех юнитов (<c>0x6220c1</c>), Cured → сброс 21
    /// (<c>0x62892e</c>), при 2 или 3 — сброс 2, 3, 4 и раунда наложения (<c>0x6289cb</c>–<c>0x6289fb</c>),
    /// при 22 — <c>0x625d9e</c>. Идёт после снимка <c>turn_before</c> ходящего (корпус выше), поэтому
    /// модель зовёт это из <see cref="BattleSim.BeginTurn"/>.
    /// <para><b>Ранг 2</b> — что <c>0x625d9e</c> и есть откат превращения: он создаёт боевой объект
    /// (<c>0x6458ae</c>, тип 3) и зовёт его <c>onHit</c>, тело объекта не дочитано; модель откатывает
    /// тем же <see cref="TransformAttacks.Untransform"/>, что и конец боя. Превращений под
    /// «Исцелением» в корпусе нет.</para></summary>
    public static void ResolveCured(BattleState state)
    {
        foreach (var u in state.Units)
        {
            if (!u.Cured) continue;
            u.Cured = false;
            u.Paralyzed = u.Petrified = u.DisableLong = false;
            if (u.Transformed) TransformAttacks.Untransform(u, state);
        }
    }

    /// <summary>Можно ли наложить атаку на цель — слот 1 (<c>canPerform</c>) классов, которые
    /// смотрят на уже наложенное. Движок зовёт его по каждой цели ДО бросков
    /// (<c>0x624c88</c>, <c>CanPerformAttackOnUnitWithStatusCheck</c> <c>0x645b7d</c> → <c>call [vft+4]</c>):
    /// «нельзя» — ни бросков, ни эффекта. Ранг 1 (задача 23 Ф7).
    /// <list type="bullet">
    /// <item>отступающий — <see cref="RejectsRetreating"/>;</item>
    /// <item>яд, обморожение, ожог — новый не ложится, если у цели тот же вид СИЛЬНЕЕ: Poison
    /// <c>0x65e1ef</c> (статус 15, атака наложившего <c>0x62317c</c>, сравнение <c>getQtyDamage</c>
    /// слот 8 <c>+0x20</c>, <c>jge 0x65e338</c>), Frostbite <c>0x65e816</c> (17), Blister <c>0x65ee0f</c>
    /// (19). Равный или сильнее — ложится и перезаписывает (<c>onHit</c> <c>0x65e39d</c>). Права вики:
    /// остаётся сильнейший;</item>
    /// <item>усиление урона — <see cref="BoostAccepted"/>;</item>
    /// <item>«Исцеление» — <see cref="CanBeCured"/>;</item>
    /// <item>разбить броню, понизить уровень, замедлить — <see cref="ShatterAccepted"/>, <see cref="HasPrevLevel"/>,
    /// <see cref="SlowAccepted"/> (16.10.2026, по журналу третьей партии оригинала); ослабить — <see cref="WeakenAccepted"/>.</item>
    /// </list></summary>
    public static bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target)
    {
        var effect = attack.Effect;
        if (target.Retreating && effect.RejectsRetreating) return false;
        // Проверки — у эффекта (ранг 1 по слотам canPerform классов): Shatter, DrainLevel, LowerInitiative, LowerDamage, три яда,
        // BoostDamage, Cure; Fear, Paralyze, Petrify — «нельзя» на статусах 2 и 3 (Fear 0x65c6a1, Paralyze 0x65add3, Petrify 0x65b155;
        // корпус 20260920-gaps [393470]); GiveAttack 0x660283 — не сам передающий и не другой передающий.
        return effect.CanPerform(attacker, attack, target);
    }

    /// <summary><c>canPerform</c> Shatter (GOG <c>0x65d741</c>), ранг 1 по коду: остаток брони цели
    /// <c>soldier − shattered</c> (<c>IUsSoldier::getArmor</c>, слот 10, <c>0x65d82d</c>, минус
    /// <c>getUnitShatteredArmor</c> <c>0x621ebc</c> на <c>0x65d851</c>) должен быть больше укрепления
    /// (<c>getUnitFortificationArmor</c> <c>0x621f21</c>, <c>0x65d876 cmp; jg</c>) и больше нуля
    /// (<c>0x65d881 setg</c>), иначе «нельзя» — бросать нечего: ниже укрепления <see cref="Formulas.Armor"/>
    /// броню всё равно не опустит. Перед этим — бит 29 (<see cref="RejectsRetreating"/>). Журнал: 20261016b
    /// seq 531219 — по Гномам с бронёй 0 вторая атака Тиурга без броска; 20261016c — по Латникам 20 → 5 → 0
    /// (shattered 15, 30), по броне 5 бросок есть.</summary>
    internal static bool ShatterAccepted(BattleUnit target)
    {
        int left = Formulas.SoldierArmor(target) - target.ShatteredArmor;
        return left > target.FortificationArmor && left > 0;
    }

    /// <summary><c>canPerform</c> DrainLevel (GOG <c>0x662608</c>), ранг 1 по коду: «нельзя», если у реализации
    /// цели нет предыдущего уровня (<c>getPrevUnitImplId</c> <c>0x66275c</c> вернул пустой id —
    /// <c>0x6626e1</c> сравнение с <c>emptyId</c>) или цель бежит (бит 29, <c>0x662710</c>). Журнал: 20261016b
    /// seq 522314 — по Гномам без <c>PREV_ID</c> три раза без броска; 20261016c — Ветеран → Воин → Гном.</summary>
    internal static bool HasPrevLevel(BattleUnit target) => target.Type.PrevId.Length > 0;

    /// <summary><c>canPerform</c> LowerInitiative (GOG <c>0x65de8f</c>), ранг 1 по коду: «нельзя» на статусах
    /// 2 Paralyze (<c>0x65df35</c>), 3 Petrify (<c>0x65df4c</c>), 29 Retreat (<c>0x65df63</c>) и 13 — уже
    /// замедлен (<c>0x65df92</c>). Журнал: 20261016b seq 537802 — по окаменевшему Хугину без броска;
    /// 20261016c — шесть замедлений, ни одного по цели с битом 13.</summary>
    internal static bool SlowAccepted(BattleUnit target) =>
        !target.Paralyzed && !target.Petrified && target.LowerInitiativeLevel == 0;

    /// <summary><c>canPerform</c> LowerDamage (GOG <c>0x65da76</c>), ранг 1 по коду: у цели есть атака с уроном
    /// (<c>0x65cd37</c> — первая атака списка <c>0x644815</c>; иначе вторая, если в списке больше одной (<c>0x645d91</c>) и
    /// вторая — собственная вторая атака юнита, источник 1 (<c>0x645e69</c>), а не атака предмета — <see cref="HasDamageAttack"/>);
    /// не статусы 2, 3, 29 (<c>0x65db94</c>–<c>0x65dbd7</c>); не статус 11 — ослаблен уровнем 2 (<c>0x65dbf1</c>); статус 10 —
    /// ослаблен уровнем 1 — пускает только атаку уровня выше 1 (<c>0x65dc0c</c>, <c>cmp [уровень], 1; jg</c>; уровень —
    /// слот 11 атаки, <c>0x65dbeb</c>). Сторону цели (<c>0x62ac90</c>) порт выбирает раньше. Журнал: вторая атака
    /// <c>G000AA1168</c> (уровень 2) не бросается по уже ослабленным — 20260909-arena2 seq 373912 (6 целей, 4 броска), 374298,
    /// 374498, 20261016 seq 560293, 560464, 20261016c seq 538881; С1 <c>second_can_perform</c> 68/68 (без правила 6 расхождений).</summary>
    internal static bool WeakenAccepted(AttackType attack, BattleUnit target) =>
        HasDamageAttack(target) && !target.Paralyzed && !target.Petrified && target.LowerDamageLevel != 2
        && !(target.LowerDamageLevel == 1 && attack.Level <= 1);

    /// <summary><c>canPerform</c> BoostDamage (GOG <c>0x65ca1e</c>), ранг 1: нельзя, если у цели уже
    /// взведён уровень не ниже нового (биты 8, 7, 6, 5 против <c>getLevel</c>, <c>0x65cb95</c>–<c>0x65cc0f</c>) —
    /// права вики, слабое сильного не перебивает, а равное тоже не ложится; если у цели нет атаки с
    /// уроном (<see cref="HasDamageAttack"/>) или она парализована/окаменела. Во всех трёх случаях
    /// остаётся запасной путь: вторая атака самого бафера — Cure, и цель можно лечить
    /// (<c>0x65cc3d</c>–<c>0x65cd09</c>) — так Матриарх лечит того, кого не бафает.</summary>
    internal static bool BoostAccepted(BattleUnit attacker, AttackType attack, BattleUnit target)
    {
        bool accepted = target.BoostDamageLevel < attack.Level && HasDamageAttack(target)
                        && !target.Paralyzed && !target.Petrified;
        return accepted || attacker.Attack2 is { Class: AttackClass.Cure } && CanBeCured(target);
    }

    /// <summary>У юнита есть атака с уроном — <c>0x65cd37</c>: класс первой или второй атаки —
    /// Damage (<c>0x837420</c>), Drain (<c>0x837578</c>) или DrainOverflow (<c>0x8374b0</c>; имена —
    /// порядок GOG-таблицы <c>attackclasscat.cpp:82-105</c>, ранг 2 для имён). Ранг 1.</summary>
    public static bool HasDamageAttack(BattleUnit u) =>
        IsDamageClass(u.Attack.Class) || u.Attack2 is { } second && IsDamageClass(second.Class);

    private static bool IsDamageClass(AttackClass cls) => AttackEffects.Of(cls).IsDirectDamage;

    /// <summary>BoostDamage: уровень 1..4 из <c>Gattacks.LEVEL</c> (в ванили есть все четыре —
    /// g000aa0033/0034/0035/0160), проценты — BATBOOSTD1..4 из GVars. Бросков не тратит.
    /// <para>Ранг 1 (задача 23 Ф7), <c>onHit</c> GOG <c>0x65cdca</c>: цели без атаки с уроном —
    /// ничего (<c>0x65cdfe</c>–<c>0x65ce4b</c>); иначе сброс битов 5…9 и новый уровень с Long — то есть
    /// дошедшее до <c>onHit</c> наложение вытесняет прежнее. Слабое или равное сюда не доходит:
    /// его отсекает <see cref="CanPerform"/>.</para></summary>
    internal static AttackOutcome ApplyBoostDamage(AttackType attack, BattleUnit target)
    {
        if (!HasDamageAttack(target)) return new AttackOutcome(false, 0, 0, false);
        target.BoostDamageLevel = attack.Level;
        target.BoostDamageLong = attack.Infinite;
        return new AttackOutcome(false, 0, 0, attack.Level > 0);
    }

    /// <summary>Классы, которые не накладываются на юнита, собравшегося бежать (BattleStatus Retreat,
    /// бит 29): их <c>canPerform</c> (слот 1 vftable) зовёт <c>getUnitStatus(target, 29)</c>
    /// (<c>0x62271c</c>) и при взведённом бите отвечает «нельзя».
    /// <para><b>Ранг 1 по дизассемблеру</b> (задача 7, слот 1 каждого <c>CBatAttack*</c>, найденного
    /// по RTTI): Fear <c>0x65c5ed</c>, BoostDamage <c>0x65ca1e</c>, BestowWards <c>0x65d079</c>,
    /// Shatter <c>0x65d741</c>, LowerDamage <c>0x65da76</c>, LowerInitiative <c>0x65de8f</c>, Poison
    /// <c>0x65e1ef</c> (vft <c>0x6f30ec</c>), Frostbite <c>0x65e816</c>, Blister <c>0x65ee0f</c>,
    /// GiveAttack <c>0x660283</c>, DrainLevel <c>0x662608</c>. У Damage, Drain, Paralyze и прочих такой
    /// проверки нет, а общая <c>canPerformAttackOnUnitWithStatusCheck</c> (<c>0x645b7d</c>) бит 29 не
    /// смотрит — только 0, 1, 28, 30 и 32.</para>
    /// <para><b>Корпус, ранг 1:</b> <c>20260911-passive/passive-001</c> — Master Thug бьёт
    /// отступающего (<c>[393173]</c> seq 393535, <c>[631904]</c> seq 632258): <c>attack_new</c> второй
    /// атаки (Poison) есть, а ни <c>power</c>, ни <c>miss</c>, ни <c>immune</c> по ней нет; Paralyze по
    /// отступающему (seq 778765) бросается как обычно. Остальные проверки тех же <c>canPerform</c>
    /// (сравнение с уже наложенным ядом, уровень буста) — <see cref="CanPerform"/>.</para></summary>
    /// <para>С этапа Э2 — признак эффекта (<see cref="AttackEffect.RejectsRetreating"/>): Fear, BoostDamage, BestowWards, Shatter,
    /// LowerDamage, LowerInitiative, Poison, Frostbite, Blister, GiveAttack, DrainLevel.</para>
    public static bool RejectsRetreating(AttackClass cls) => AttackEffects.Of(cls).RejectsRetreating;

    /// <summary>Классы, которые применяются к СВОИМ, а не к врагу (выбор группы в BattleSim.Act).
    /// Ранг 2 (A: цели считает getTargetsToSelectOrAttack по REACH и стороне); ниже он поднят до
    /// ранга 1 и с задачи 8 полон — все двадцать три класса разложены.
    /// <para>Revive и Summon добавлены задачей 6. У Revive это ранг 1 по vftable: слот 14
    /// (<c>method14</c>) — единственный на все 23 класса не-заглушка (<c>0x65f606</c>,
    /// <c>mov al,1</c>), то есть только он целится в мёртвых, а мёртвые бывают свои. Summon в
    /// пару цели вообще не ходит — он занимает слот в отряде атакующего, — но группа у него та
    /// же, своя.</para>
    /// <para><b>Задача 7 подняла весь список до ранга 1</b> — он читается прямо в движке. Слот 2
    /// vftable (<c>getTargetGroupId</c>) у каждого класса зовёт <c>getAllyOrEnemyGroupId</c>
    /// (GOG <c>0x659a32</c>) с литеральным флагом «свои»: <c>push 1</c> у Heal
    /// (<c>0x65c02b</c>), BoostDamage, Cure, Revive, Summon, BestowWards и <b>GiveAttack</b>
    /// (<c>0x660218</c>), <c>push 0</c> у Damage и прочих. Прежний ранг 2 по survey §6 совпал.</para>
    /// <para><b>Doppelganger и TransformSelf — свои, но с условием (ранг 1).</b> У них слот 2 не
    /// константа, а развилка: <c>Doppelganger</c> (<c>0x661979</c>) отдаёт свою группу, пока
    /// <c>cannotUseDoppelgangerAttack</c> ложно и есть во что превращаться, иначе — чужую (там
    /// работает уже запасная атака); <c>TransformSelf</c> (<c>0x660d94</c>) отдаёт группу запасной
    /// атаки, если та есть, иначе свою (<c>getAllyOrEnemyGroupId(..., 1)</c> по собственному id).
    /// Модель держит первую ветвь обоих — ту, в которой класс и делает своё дело; вторая ветвь
    /// (запасная атака, <c>Gattacks.ALT_ATTACK</c>) с задачи 8 тоже смоделирована — <see cref="Apply"/>
    /// применяет её рекурсией, — но группу она выбирает уже свою, а выбор цели это политика ИИ (Ф7).</para>
    /// <para><b>BestowWards добавлен задачей 8</b> и это ранг 1 из того же замера слота 2: варды
    /// кладутся на своих.</para></summary>
    /// <para>С этапа Э2 — признак эффекта (<see cref="AttackEffect.TargetsAllies"/>): Heal, Cure, BoostDamage, Revive, Summon,
    /// GiveAttack, Doppelganger, TransformSelf, BestowWards.</para>
    public static bool TargetsAllies(AttackClass cls) => AttackEffects.Of(cls).TargetsAllies;

    /// <summary>Тик DoT в начале хода жертвы: урон каждого эффекта, затем решение о снятии.
    /// Ранг 1 (corpus/20260906-e, 20 тиков): turn_before несёт HP до тика, turn_after — после;
    /// умерший от тика не ходит и броска не делает (0013: 12 → 0, между turn_before и turn_after
    /// ничего); живой делает ОДИН rng(100) на длинный эффект — <see cref="LongEffectBreaks"/>.
    /// Порядок эффектов при нескольких DoT (Poison, Frostbite, Blister — порядок update*LongEffect в
    /// движке) — ранг 3, в корпусе по одному DoT на юнита.</summary>
    public static void TickDots(BattleUnit unit, IRng rng, RulesConstants c, int round)
    {
        foreach (var dot in unit.Dots.OrderBy(d => d.Class).ToList())
        {
            unit.Hp = Math.Max(0, unit.Hp - dot.Damage);
            if (!unit.Alive) { unit.Dots.Clear(); return; }
            if (!dot.Long || LongEffectBreaks(rng, c, round - dot.AppliedRound)) unit.Dots.Remove(dot);
        }
    }

    /// <summary>checkLongEffectDuration (GOG 0x626413, дизассемблирован до ret 07.09.2026):
    /// rng(100) + 1 &gt;= BAT_BREAK − BAT_BMODIF · roundsPassed (GlobalVariables+0x44/+0x48 — 90 и 10).
    /// Зовётся из updatePoisonLongEffect (0x62832b) с roundsPassed = currentRound − poisonAppliedRound.
    /// Корпус 20260906-e, 20 бросков: при 0 раундов 85 держит, 96 снимает; при 1 — 60 держит,
    /// 79 снимает — всё по формуле. Ранг 1.</summary>
    public static bool LongEffectBreaks(IRng rng, RulesConstants c, int roundsPassed) =>
        rng.Next(100) + 1 >= c.BatBreak - c.BatBmodif * roundsPassed;

    /// <summary>Перемешивание целей площадной атаки (REACH = 1): n − 1 бросков rng(1)…rng(n − 1) по
    /// всей группе цели (мёртвые включительно), swap(L[i], L[rng(i)]) для i = 1…n − 1.
    /// Ранг 1 для числа бросков (c: всегда rng(1), rng(2) при группе из 3 при 3/2/1 живых; b: всегда
    /// rng(1), rng(2), rng(3) при группе из 4 с одним мёртвым) и для перестановки на 10 ходах с тремя
    /// целями.
    /// <para><b>Исходный порядок L — ОБРАТНЫЙ порядку юнитов группы в состоянии боя</b> (то есть
    /// порядку <c>unitsInfo</c>, в котором их пишет <c>battle_start</c>), а не по id. Ранг 1 по
    /// корпусу, задача 7: по всем 76 площадным ходам корпуса, где журнал называет цели по порядку
    /// (события <c>immune</c> с <c>always_only = false</c> зовутся на каждую цель, и на промах тоже),
    /// обратный порядок воспроизводит порядок целей в 76 из 76, порядок по id — в 50, по слоту — в
    /// 18. На синтетических картах прежнего корпуса обе редакции совпадали; разошлись они на большой
    /// карте — HP 151/152 в <c>passive-004 [521206]</c>: огненный удар 18/16/19 лёг на других юнитов.</para></summary>
    public static List<BattleUnit> Shuffle(IEnumerable<BattleUnit> group, IRng rng)
    {
        var list = group.Reverse().ToList();
        for (int i = 1; i < list.Count; i++)
        {
            int j = rng.Next(i);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>Ударов за ход: два при ATCK_TWICE из Gunits (A: battlemsgdata.h:130-134
    /// attackCount=2; survey §1, расхождение 4 — B держит список имён, мы данные). Ранг 2:
    /// в корпусе двойных атак нет; С1 сверяет с order[].attacks из battle_units (attacks_per_turn) —
    /// пока только отрицательно, у всех типов корпуса ATCK_TWICE = false.</summary>
    public static int AttacksPerTurn(BattleUnit unit) => unit.Type.AttackTwice ? 2 : 1;
}
