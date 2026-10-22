using D2.Rules.Battle;

namespace D2.Rules.Map;

/// <summary>Класс обёртки отряда ИИ — фабрика <c>0x465af9</c> (ранг 1): у каждого класса свой построитель списка
/// действий (слот 18 таблицы <c>IAiStack</c>).</summary>
public enum AiStackKind
{
    /// <summary><c>CAiStackFighter</c>, построитель <c>0x4633fe</c>.</summary>
    Fighter,
    /// <summary><c>CAiStackNeutral</c> (хозяин нейтрален, <c>0x5e5358</c>), построитель <c>0x46395b</c>.</summary>
    Neutral,
    /// <summary><c>CAiStackThief</c> (лидер <c>L_NOBLE</c>), построитель <c>0x46465a</c>.</summary>
    Thief,
    /// <summary><c>CAiStackSummon</c> (лидер <c>L_SUMMON</c>/<c>L_ILLUSION</c>), построитель <c>0x464341</c>.</summary>
    Summon,
    /// <summary><c>CAiStackRod</c> (способность лидера <c>L_ROD</c>), построитель <c>0x463fd8</c>.</summary>
    Rod,
}

/// <summary>Категория юнита-лидера (<c>LUnitCategory</c>, слот 2 реализации +8), по которой фабрика выбирает
/// класс отряда: <c>0x838198</c> <c>L_NOBLE</c>, <c>0x838168</c> <c>L_SUMMON</c>, <c>0x838178</c>
/// <c>L_ILLUSION</c>. Имена категорий — по таблице загрузчика (ранг 1).</summary>
public enum AiLeaderKind { Other, Noble, Summon, Illusion }

/// <summary>
/// Признаки обёртки отряда, которые читает построитель (слоты <c>IAiStack</c>, имена — заголовок
/// <c>aistack.h</c> UnveN, смысл полей — конструктор <c>0x46751d</c>, ранг 1):
/// </summary>
/// <param name="RiotCity">Слот 8 (<c>+0x11</c>): стоит в городе с бунтом (<c>+0x98</c> деревни ≠ 0) — список пуст.</param>
/// <param name="OwnerCaptured">Слот 6 (<c>0x5e4e73</c>): столица хозяина захвачена (<c>CMidPlayer +0x8c</c>).</param>
/// <param name="InjuredInside">Слот 7 (<c>0x4676ed</c>): в крепости и есть кого лечить/воскрешать; вне крепости
/// всегда ложь.</param>
/// <param name="WaterOnly">Слот 5 (<c>+0x10</c>, слот 0x70 реализации лидера): ходит только по воде.</param>
/// <param name="LeaderAlive"><c>CMidStack +0x2c</c>.</param>
/// <param name="Inside"><c>CMidStack +0x94</c> ≠ пусто — строится <c>StayInside</c>.</param>
/// <param name="Steal">Только вор: действующий приказ (<see cref="AiTacticLists.EffectiveOrder"/>) —
/// <c>L_STEAL</c> (<c>0x8378f0</c>).</param>
public sealed record AiStackFlags(bool RiotCity, bool OwnerCaptured, bool InjuredInside, bool WaterOnly,
                                  bool LeaderAlive, bool Inside, bool Steal = false);

/// <summary>
/// Какие списки действий строит отряд ИИ и в каком порядке (слот 18 обёртки, ранг 1 по пяти листингам). Порядок
/// важен: вставка сохраняет его, а при равном счёте выигрывает первый (<see cref="AiTactic.Top{T}"/>).
/// Категории приказов опознаны по таблице загрузчика <c>LOrder.dbf</c> (<c>0x588417</c>): <c>0x837860</c>
/// <c>L_NORMAL</c>, <c>0x837910</c> <c>L_STAND</c>, <c>0x837890</c> <c>L_GUARD</c>, <c>0x837880</c>
/// <c>L_ASSIST</c>, <c>0x8378f0</c> <c>L_STEAL</c>, <c>0x8378c0</c> <c>L_DEFEND_CITY</c>, <c>0x837870</c>
/// <c>L_DEFEND_LOCATION</c>.
/// </summary>
public static class AiTacticLists
{
    /// <summary>Фабрика <c>0x465af9</c>: нейтрал → вор → призыв/иллюзия → жезл (способность <c>0x837618</c>
    /// <c>L_ROD</c>) → боец.</summary>
    public static AiStackKind Classify(bool ownerNeutral, AiLeaderKind leader, bool leaderHasRod) =>
        ownerNeutral ? AiStackKind.Neutral
        : leader == AiLeaderKind.Noble ? AiStackKind.Thief
        : leader is AiLeaderKind.Summon or AiLeaderKind.Illusion ? AiStackKind.Summon
        : leaderHasRod ? AiStackKind.Rod
        : AiStackKind.Fighter;

    /// <summary>Действующий приказ: при <c>L_NORMAL</c> (<c>+0xa8</c>) движок берёт приказ ИИ (<c>+0xb8</c>), и
    /// так же цель (<c>+0xbc</c> вместо <c>+0xac</c>). Повторяется во всех построителях, ранг 1.</summary>
    public static StackOrder EffectiveOrder(StackOrder order, StackOrder aiOrder) =>
        order == StackOrder.Normal ? aiOrder : order;

    /// <summary>
    /// Список типов действий отряда в порядке построителя.
    /// <para>Бродяга и берсерк — не отдельные оценщики: <c>CAiAttackStackRoam</c> (<c>0x4517b4</c>) и
    /// <c>CAiAttackStackBezerk</c> зовут тот же построитель кандидатов <c>0x4533fb</c>, а их классы оценок —
    /// тот же счёт <c>0x453d15</c>; <c>CAiAttackCityRoam</c> (<c>0x4508fc</c>) и <c>CAiAttackCityBezerk</c> — тот
    /// же <c>0x452636</c> и счёт <c>0x452baf</c>; <c>CAiTakeBagRoam</c> (<c>0x469889</c>) — тот же
    /// <c>0x469418</c> и счёт <c>0x46951a</c>. Отличий ровно два (ранг 1): у всех трёх «бродяг» построитель
    /// сперва требует, чтобы отряд не стоял в крепости (<c>CMidStack +0x94</c> = пусто — крепость стоянки, как у фильтров
    /// <c>0x454c78</c> и <c>0x5deb24</c>; до 13.10 здесь было «хозяин не захвачен»; проверка — в построителях
    /// <c>AiTactics.Build</c>), а у берсерка такой проверки нет; и слот 6 оценки — свой порог силы (<see cref="AiTacticScore.Threshold"/>: бродяга бросает кандидата
    /// при <c>p &lt; 0,7</c>, берсерк — только при <c>p = 0</c>). У бродяги с мешком добавлен ещё фильтр
    /// расстояния, см. <see cref="AiTacticEval.TakeBagRoamAdmits"/>.</para>
    /// </summary>
    public static IReadOnlyList<AiActionType> Build(AiStackKind kind, AiStackFlags f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var l = new List<AiActionType>();
        switch (kind)
        {
            case AiStackKind.Summon: Summon(l, f); break;
            case AiStackKind.Neutral when !f.RiotCity: Neutral(l, f); break;
            case AiStackKind.Fighter or AiStackKind.Rod or AiStackKind.Thief when !f.RiotCity: Leader(l, kind, f); break;
        }
        return l;
    }

    // Боец, жезл и вор (0x4633fe, 0x463fd8, 0x46465a) — один каркас, различаются голова и хвост.
    private static void Leader(List<AiActionType> l, AiStackKind kind, AiStackFlags f)
    {
        if (!f.InjuredInside && !f.OwnerCaptured)
        {
            l.Add(AiActionType.Retreat);
            if (f.LeaderAlive) LeaderHead(l, kind, f);
        }
        l.Add(AiActionType.AttackStackBezerk);
        if (!f.OwnerCaptured)
        {
            if (kind != AiStackKind.Rod) l.Add(AiActionType.AttackParty);
            l.AddRange([AiActionType.AttackStackRoam, AiActionType.MoveToLocation, AiActionType.Guard,
                        AiActionType.DefendParty, AiActionType.DefendLocation]);
        }
        if (!f.WaterOnly)
        {
            l.AddRange([AiActionType.AssistMove, AiActionType.GiveItems]);
            if (f.LeaderAlive)
            {
                l.Add(AiActionType.AttackCityBezerk);
                if (f.OwnerCaptured) return;
                l.AddRange([AiActionType.AttackCityRoam, AiActionType.SecureCity]);
            }
        }
        if (f.OwnerCaptured) return;
        if (!f.InjuredInside)
        {
            if (!f.WaterOnly)
            {
                l.Add(AiActionType.HealRevive);
                if (kind != AiStackKind.Rod) l.Add(AiActionType.DefendCity);
            }
            if (kind == AiStackKind.Fighter || kind == AiStackKind.Thief && f.Steal) l.Add(AiActionType.TakeBag);
        }
        if (f.Inside) l.Add(AiActionType.StayInside);
    }

    private static void LeaderHead(List<AiActionType> l, AiStackKind kind, AiStackFlags f)
    {
        switch (kind)
        {
            case AiStackKind.Fighter:
                l.AddRange([AiActionType.AttackStack, AiActionType.MoveToStack]);
                if (!f.WaterOnly)
                    l.AddRange([AiActionType.AttackCity, AiActionType.VisitRuin, AiActionType.HireUnit,
                                AiActionType.HireMercs, AiActionType.VisitMage, AiActionType.VisitMerchant,
                                AiActionType.VisitTemple, AiActionType.VisitTrainer]);
                break;
            case AiStackKind.Rod when !f.WaterOnly:
                l.AddRange([AiActionType.PlantRod, AiActionType.AttackRod, AiActionType.HireUnit,
                            AiActionType.VisitMage, AiActionType.VisitTemple]);
                break;
            case AiStackKind.Thief when !f.WaterOnly:
                l.AddRange([AiActionType.NobleStack, AiActionType.NobleCity]);
                if (f.Steal) l.Add(AiActionType.NobleMerchant);
                l.Add(AiActionType.VisitTemple);
                break;
        }
    }

    // 0x46395b: без бегства и найма; своё — EnterCityRoam и TakeBagRoam.
    private static void Neutral(List<AiActionType> l, AiStackFlags f)
    {
        if (!f.InjuredInside && !f.OwnerCaptured && f.LeaderAlive)
        {
            if (!f.WaterOnly) l.Add(AiActionType.AttackCity);
            l.AddRange([AiActionType.AttackStack, AiActionType.MoveToStack]);
        }
        l.Add(AiActionType.AttackStackBezerk);
        if (!f.OwnerCaptured)
            l.AddRange([AiActionType.AttackParty, AiActionType.AttackStackRoam, AiActionType.MoveToLocation,
                        AiActionType.Guard, AiActionType.DefendParty, AiActionType.DefendLocation,
                        AiActionType.EnterCityRoam]);
        if (!f.WaterOnly && f.LeaderAlive)
        {
            l.Add(AiActionType.AttackCityBezerk);
            if (f.OwnerCaptured) return;
            l.AddRange([AiActionType.AttackCityRoam, AiActionType.SecureCity]);
        }
        if (f.OwnerCaptured) return;
        if (!f.InjuredInside)
        {
            if (!f.WaterOnly) l.Add(AiActionType.DefendCity);
            l.AddRange([AiActionType.TakeBag, AiActionType.TakeBagRoam]);
        }
        if (f.Inside) l.Add(AiActionType.StayInside);
    }

    // 0x464341: смотрит только на «только по воде»; бунт, захват и лидер не проверяются.
    private static void Summon(List<AiActionType> l, AiStackFlags f)
    {
        l.AddRange([AiActionType.AttackParty, AiActionType.AttackStackBezerk, AiActionType.AttackStackRoam]);
        if (!f.WaterOnly)
            l.AddRange([AiActionType.AttackCityBezerk, AiActionType.AttackCityRoam, AiActionType.SecureCity,
                        AiActionType.DefendCity]);
        l.AddRange([AiActionType.MoveToLocation, AiActionType.Guard, AiActionType.DefendParty,
                    AiActionType.DefendLocation, AiActionType.SummonAttack]);
        if (!f.WaterOnly) l.Add(AiActionType.AttackCity);
        l.Add(AiActionType.MoveToStack);
    }
}
