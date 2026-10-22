using D2.Rules.Rng;

namespace D2.Rules.Battle.Effects;

/// <summary>Удар, дошедший до эффекта (после броска точности и иммунитета): кто, чем (атака уже с модификаторами атакующего), по кому.
/// <see cref="DrainPercent"/> — прибавка вампиризма цепочки атакующего (<c>GmodifL TYPE 15</c>), её читает только вампиризм.
/// <see cref="State"/> нужен эффектам, которые смотрят за пределы пары «атакующий — цель»: переток, призыв, понижение уровня,
/// превращения, обереги.</summary>
public readonly record struct EffectHit(BattleUnit Attacker, AttackType Attack, BattleUnit Target, int DrainPercent, IRng Rng,
                                        RulesConstants Constants, int Round, BattleState? State);

/// <summary>Подсказки ИИ эффекта — числа оценки силы отряда (<c>AiStrength</c>, <c>0x5dd574</c>…) и выбора цели (<c>AiTargets</c>).
/// Данные, а не код: у эффекта мода свои. У оригинала зашиты в движке по классам атаки; здесь — по эффекту.</summary>
/// <param name="ClassFactor">Множитель силы по классу первой атаки (<c>0x5dd574</c>): паралич 30, призыв 200…, прочее 1.</param>
/// <param name="ClassFactorAlt">Второй множитель — у эффектов, чей множитель ветвится (двойник: без учёта города; превращение другого:
/// не площадное); иначе не читается.</param>
/// <param name="SecondFactor">Множитель силы по классу второй атаки (<c>0x5dd9a2</c>): исцеление 1.1, яды 1.15, воскрешение 3.</param>
/// <param name="FullPower">Сила считается с точностью 100, а не точностью атаки (лечение, усиление, исцеление, призыв, передача атаки, двойник).</param>
/// <param name="InfiniteCounts">Флаг «до конца боя» прибавляет силе 25 %. <c>0x5dd8b6</c>: флаг <c>INFINITE</c> атаки (слот <c>0x34</c>)
/// читается только у девяти классов — паралич, окаменение, усиление, ослабление, снижение инициативы, яд, обморожение, ожог, превращение
/// другого (сравнения с <c>[0x837488]</c> … <c>[0x837468]</c>, <c>attackclasscat.cpp</c>); у прочих — ложь, и тогда множитель второй
/// атаки. Разбор 28.09 (probe65, <c>KC0096</c>: лидер с бесконечным призывом — порт 300, движок 240 = 300 / 1,25).</param>
/// <param name="XpFactor">Множитель опыта цели в приоритете выбора цели ИИ (<c>0x5ceff1</c>): лекарь 2, паралич 8, призыв 10…; 1 — нет.</param>
/// <param name="XpRank">Порядок проверки множителей опыта (движок идёт по списку и берёт первый, совпавший с первой или второй атакой
/// цели); нет — эффект в списке не значится, множитель 1.</param>
public sealed record EffectTuning(double ClassFactor = 1, double ClassFactorAlt = 1, double SecondFactor = 1, bool FullPower = false,
                                  bool InfiniteCounts = false, int XpFactor = 1, int? XpRank = null);

/// <summary>Облик эффекта — строки и звуки, которые показ берёт по эффекту, а не по классу атаки: имя класса в карточке юнита и
/// списке стойкостей (<c>X005TA0791</c> «Урон»…), подпись действия в строке атаки (<c>X005TA0503</c> «Урон», <c>X005TA0504</c>
/// «Лечить»…), звук второй атаки (<c>HEAL</c>, <c>POISON</c>…, архив <c>Battle</c>) и звук тика урона со временем.</summary>
public sealed record EffectLooks(string? NameText = null, string? ActionText = null, string? Sound = null, string? TickSound = null);

/// <summary>
/// Эффект атаки — что делает удар: урон, лечение, паралич, призыв, превращение… Реестр эффектов (<see cref="AttackEffects"/>) — по
/// строковому id; двадцать три класса оригинала (<c>LattC.dbf</c>) — встроенные реализации, новый эффект мода — данные (свои числа
/// ИИ поверх встроенного поведения) или своя реализация этого класса (доверенный мод с кодом). Раньше каждая система — применение,
/// иммунитет, выбор цели и оценка силы ИИ — держала свой <c>switch</c> по <see cref="AttackClass"/>, и новая механика была правкой ядра
/// в пяти местах.
/// <para><b>Признаки</b> — слоты виртуальной таблицы <c>CBatAttack*</c> движка, прочитанные по классам (ранг 1, ссылки — в описаниях
/// прежних списков: <see cref="Formulas.ClassHasPower"/>, <see cref="Immunity.Checked"/>, <see cref="Attacks.TargetsAllies"/>,
/// <see cref="Attacks.RejectsRetreating"/>). Порядок бросков генератора задаёт <see cref="Attacks.Apply"/>, эффект его не меняет.</para>
/// </summary>
public abstract record AttackEffect
{
    protected AttackEffect(string behavior, AttackClass legacyClass)
    {
        Behavior = behavior;
        Id = BuiltInId(behavior);
        LegacyClass = legacyClass;
    }

    /// <summary>Пространство встроенных эффектов — то же, что у содержимого игры (<c>content/base</c>).</summary>
    public const string BuiltInNamespace = "base";

    /// <summary>Id встроенного эффекта: <c>base:effect/</c> и поведение через дефис (<c>boostDamage</c> → <c>base:effect/boost-damage</c>) —
    /// ровно id сущности эффекта, которую импорт кладёт в <c>base</c>. Так атака с эффектом из содержимого без правок равна атаке с
    /// эффектом по умолчанию (запись <see cref="AttackType"/> сравнивает и эффект).</summary>
    public static string BuiltInId(string behavior, string ns = BuiltInNamespace) =>
        $"{ns}:effect/{System.Text.Json.JsonNamingPolicy.KebabCaseLower.ConvertName(behavior)}";

    /// <summary>Поведение — код эффекта в реестре: имя класса в camelCase (<c>damage</c>, <c>boostDamage</c>) у встроенных.</summary>
    public string Behavior { get; }

    /// <summary>Id эффекта — id сущности содержимого (<c>base:effect/damage</c>, <c>mymod:effect/acid</c>); у встроенного без
    /// содержимого — <see cref="BuiltInId"/>.</summary>
    public string Id { get; init; }

    /// <summary>Класс оригинала (<c>LattC</c>): его номер пишут журналы зонда и сохранения. У нового эффекта мода — класс поведения, на
    /// котором он построен.</summary>
    public AttackClass LegacyClass { get; init; }

    /// <summary>Подсказки ИИ.</summary>
    public EffectTuning Tuning { get; init; } = new();

    /// <summary>Строки и звуки показа.</summary>
    public EffectLooks Looks { get; init; } = new();

    // ---- признаки (слоты движка)

    /// <summary>Удар бросает точность (<c>canMiss</c>, два <c>rng(100)</c>).</summary>
    public virtual bool HasPower => false;

    /// <summary>Иммунитет цели спрашивается (<c>isImmune</c>, слот 8).</summary>
    public virtual bool ChecksImmunity => true;

    /// <summary>Применяется к своим (<c>getTargetGroupId</c> с флагом «свои»).</summary>
    public virtual bool TargetsAllies => false;

    /// <summary>Не накладывается на бегущего (бит 29, <c>canPerform</c>).</summary>
    public virtual bool RejectsRetreating => false;

    /// <summary>Атака «с уроном» движка (<c>attackHasDamage</c>): для неё ИИ зовёт <c>computeDamageWithBuffs</c>.</summary>
    public virtual bool HasDamage => false;

    /// <summary>Урон, который считает оценка урона ИИ (<c>0x5d2f3d</c>).</summary>
    public virtual bool IsDamaging => false;

    /// <summary>Прямой урон: урон, вампиризм, переток (<c>0x65cd37</c>).</summary>
    public virtual bool IsDirectDamage => false;

    /// <summary>Урон со временем: яд, обморожение, ожог.</summary>
    public virtual bool IsDot => false;

    /// <summary>Наступательный для группы ИИ (<c>GroupOffence</c> <c>0x5cf29d</c>): урон, яды, призыв.</summary>
    public virtual bool IsOffence => IsDamaging;

    // ---- поведение

    /// <summary>Можно ли наложить на цель (<c>canPerform</c>, слот 1) — сверх общей проверки бегущего.</summary>
    public virtual bool CanPerform(BattleUnit attacker, AttackType attack, BattleUnit target) => true;

    /// <summary>Удар дошёл до цели (<c>onHit</c>).</summary>
    public abstract AttackOutcome Hit(in EffectHit hit);

    /// <summary>Цель ИИ для атаки этого эффекта (<c>FindAttackTarget</c> <c>0x5d056b</c>); null — цели нет.</summary>
    public virtual BattleUnit? FindTarget(AiContext ctx, BattleUnit unit, AttackType attack, IReadOnlyList<BattleUnit> targets, int dmg) => null;

    /// <summary>Множитель силы ИИ по первой атаке (<c>0x5dd574</c>).</summary>
    public virtual double AiClassFactor(AttackType attack, bool ignoreCity) => Tuning.ClassFactor;

    /// <summary>Урон в силе ИИ (<c>0x5dd7fd</c>): прямой урон — <c>min(QTY_DAM, DamageMax)</c>, прочее 1 (лечение — своё).</summary>
    public virtual double AiDamageFactor(BattleUnit unit, RulesConstants constants) =>
        IsDirectDamage ? Math.Min(unit.Attack.QtyDamage, Formulas.DamageMax(constants, unit.Type.GlobalId)) : 1.0;
}
