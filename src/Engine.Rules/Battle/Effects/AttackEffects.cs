using System.Collections.Concurrent;

namespace LastEdict.Rules.Battle.Effects;

/// <summary>
/// Реестр эффектов атак: встроенные двадцать три класса оригинала с числами ИИ движка и эффекты модов. Встроенный эффект находится
/// по классу (<see cref="Of(AttackClass)"/> — индекс массива, без словаря: его спрашивают в самых горячих местах боя и ИИ) и по id
/// (<see cref="Find"/>). Новый эффект мода — запись с тем же поведением и своими данными (<c>basic with { Id = …, Tuning = … }</c>) или
/// своя реализация <see cref="AttackEffect"/> (доверенный мод с кодом) — <see cref="Register"/>.
/// </summary>
public static class AttackEffects
{
    /// <summary>Встроенные эффекты с числами ИИ оригинала (таблицы <c>AiStrength</c> <c>0x5dd574</c>, <c>0x5dd9a2</c>, <c>0x5dd8b6</c> и
    /// приоритета цели <c>0x5ceff1</c>).</summary>
    public static readonly IReadOnlyList<AttackEffect> Vanilla =
    [
        new DamageEffect() { Looks = new(NameText: "X005TA0791", ActionText: "X005TA0503") },
        new DrainEffect(Overflow: false) { Tuning = new(ClassFactor: 1.5), Looks = new(NameText: "X005TA0792", ActionText: "X005TA0503") },
        new DisableEffect(Petrify: false) { Tuning = new(ClassFactor: 30, InfiniteCounts: true, XpFactor: 8, XpRank: 1), Looks = new(NameText: "X005TA0789", ActionText: "X005TA0503") },
        new HealEffect() { Tuning = new(FullPower: true, XpFactor: 2, XpRank: 0), Looks = new(NameText: "X005TA0793", ActionText: "X005TA0504", Sound: "HEAL") },
        new FearEffect() { Tuning = new(ClassFactor: 30), Looks = new(NameText: "X005TA0794", ActionText: "X005TA0503") },
        new BoostDamageEffect() { Tuning = new(ClassFactor: 40, FullPower: true, InfiniteCounts: true), Looks = new(NameText: "X005TA0795", ActionText: "X005TA0534") },
        new DisableEffect(Petrify: true) { Tuning = new(ClassFactor: 30, InfiniteCounts: true, XpFactor: 8, XpRank: 2), Looks = new(NameText: "X005TA0790", ActionText: "X005TA0503") },
        new LowerEffect(Initiative: false) { Tuning = new(ClassFactor: 40, InfiniteCounts: true), Looks = new(NameText: "X005TA0796", ActionText: "X005TA0547") },
        new LowerEffect(Initiative: true) { Tuning = new(ClassFactor: 40, InfiniteCounts: true), Looks = new(NameText: "X005TA0797", ActionText: "X005TA0551") },
        new DamageOverTimeEffect(AttackClass.Poison) { Tuning = new(SecondFactor: 1.15, InfiniteCounts: true), Looks = new(NameText: "X005TA0798", ActionText: "X005TA0503", Sound: "POISON", TickSound: "POISON") },
        new DamageOverTimeEffect(AttackClass.Frostbite) { Tuning = new(SecondFactor: 1.15, InfiniteCounts: true), Looks = new(NameText: "X005TA0799", ActionText: "X005TA0503", Sound: "FRSTBITE", TickSound: "FRSTBITE") },
        new ReviveEffect() { Tuning = new(SecondFactor: 3), Looks = new(NameText: "X005TA0800", ActionText: "X005TA0503") },
        new DrainEffect(Overflow: true) { Tuning = new(ClassFactor: 2), Looks = new(NameText: "X005TA0801", ActionText: "X005TA0503") },
        new CureEffect() { Tuning = new(SecondFactor: 1.1, FullPower: true), Looks = new(NameText: "X005TA0802", ActionText: "X005TA0503") },
        new SummonEffect() { Tuning = new(ClassFactor: 200, FullPower: true, XpFactor: 10, XpRank: 3), Looks = new(NameText: "X005TA0803", ActionText: "X005TA0503") },
        new DrainLevelEffect() { Tuning = new(ClassFactor: 100), Looks = new(NameText: "X005TA0804", ActionText: "X005TA0503") },
        new GiveAttackEffect() { Tuning = new(ClassFactor: 50, FullPower: true, XpFactor: 3, XpRank: 5), Looks = new(NameText: "X005TA0805", ActionText: "X005TA0503") },
        // Двойник: 200, без учёта города — 100.
        new DoppelgangerEffect() { Tuning = new(ClassFactor: 200, ClassFactorAlt: 100, FullPower: true), Looks = new(NameText: "X005TA0806", ActionText: "X005TA0503") },
        new TransformSelfEffect() { Tuning = new(ClassFactor: 100), Looks = new(NameText: "X005TA0807", ActionText: "X005TA0503") },
        // Превращение другого: площадное — 100, иначе 60.
        new TransformOtherEffect() { Tuning = new(ClassFactor: 100, ClassFactorAlt: 60, InfiniteCounts: true, XpFactor: 9, XpRank: 4), Looks = new(NameText: "X005TA0808", ActionText: "X005TA0503") },
        new DamageOverTimeEffect(AttackClass.Blister) { Tuning = new(SecondFactor: 1.15, InfiniteCounts: true), Looks = new(NameText: "X160TA0012", ActionText: "X005TA0503", Sound: "BLISTER", TickSound: "BLISTER") },
        new BestowWardsEffect() { Tuning = new(ClassFactor: 40), Looks = new(NameText: "X160TA0014", ActionText: "X005TA0504") },
        new ShatterEffect() { Tuning = new(ClassFactor: 30), Looks = new(NameText: "X160TA0020", ActionText: "X005TA0503", Sound: "SHATTER") },
    ];

    private const int ClassSlots = 32;
    private static readonly AttackEffect?[] ByClass = Index();
    private static readonly ConcurrentDictionary<string, AttackEffect> ById = new(Vanilla.ToDictionary(e => e.Behavior, StringComparer.Ordinal));
    private static readonly ConcurrentDictionary<AttackClass, AttackEffect> Unknown = new();

    private static AttackEffect?[] Index()
    {
        var slots = new AttackEffect?[ClassSlots];
        foreach (var e in Vanilla) slots[(int)e.LegacyClass] = e;
        return slots;
    }

    /// <summary>Встроенный эффект класса оригинала. Класс вне <c>LattC</c> (мод оригинала) — эффект, который падает на ударе с внятным
    /// текстом (как прежний <c>switch</c>), а не на загрузке правил: харнесс С2 считает ходы, сошедшиеся до него.</summary>
    public static AttackEffect Of(AttackClass cls) =>
        (uint)cls < ClassSlots && ByClass[(int)cls] is { } known ? known : Unknown.GetOrAdd(cls, static c => new UnknownEffect(c));

    /// <summary>Эффект по поведению (<see cref="AttackEffect.Behavior"/>); нет — null.</summary>
    public static AttackEffect? Find(string behavior) => ById.GetValueOrDefault(behavior);

    /// <summary>Зарегистрировать эффект мода. Id занят другим эффектом — ошибка: тихая подмена встроенного поведения испортила бы бой.</summary>
    public static void Register(AttackEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var known = ById.GetOrAdd(effect.Behavior, effect);
        if (!ReferenceEquals(known, effect) && known != effect)
            throw new InvalidOperationException($"поведение «{effect.Behavior}» уже есть в реестре ({known.GetType().Name})");
    }
}

/// <summary>Класс атаки, которого нет в <c>LattC</c>, — значит мод оригинала. Признаков нет; удар — отказ с внятным текстом (молчаливая
/// заглушка испортила бы С2 незаметно).</summary>
public sealed record UnknownEffect(AttackClass Class) : AttackEffect($"unknown{(int)Class}", Class)
{
    public override AttackOutcome Hit(in EffectHit hit) => throw new NotSupportedException(
        $"класс атаки {Class} ({(int)Class}) не реализован: в Globals/LattC.dbf двадцать три класса, и все двадцать три модель умеет " +
        "(встроенные эффекты LastEdict.Rules.Battle.Effects). Значит это класс из мода — ему нужен свой эффект в реестре");
}
