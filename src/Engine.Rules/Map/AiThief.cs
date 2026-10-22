using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Map;

/// <summary>
/// Признаки цели, которые читает выбор действия вора ИИ (<c>0x45b98a</c>) сверх списка доступного
/// (<see cref="Thieves.Available"/>). Считает их вызывающий по карте; каждое поле значимо только для своей проверки.
/// </summary>
public sealed record AiThiefTarget
{
    /// <summary>Вид цели (<c>0x57f3b6</c>: тип id — отряд <c>0x23</c> или прочее).</summary>
    public ThiefTargetKind Kind { get; init; }

    /// <summary>Дуэль возможна (<c>0x45c081</c>): у лидера отряда-цели нет способности <c>L_ROD</c> (<c>0x837618</c>).</summary>
    public bool DuelAllowed { get; init; }

    /// <summary>Группа цели (<c>0x5f4f87</c>) есть — у отряда всегда.</summary>
    public bool HasGroup { get; init; }

    /// <summary>Юнитов в группе цели (<c>0x5f4441</c>, павшие тоже).</summary>
    public int UnitCount { get; init; }

    /// <summary>Предметов в сумке отряда-цели (<c>+0x64</c>, <c>0x45bf2f</c>).</summary>
    public int ItemCount { get; init; }

    /// <summary>Подкуп по карману (<c>0x45bee2</c>): цена <c>0x5d3ca7</c> строго меньше золота игрока-вора.</summary>
    public bool BribeAffordable { get; init; }

    /// <summary>Есть что украсть из заклинаний (<c>0x45c0f1</c> → <c>0x5cdde7</c>: список не пуст).</summary>
    public bool SpellToSteal { get; init; }

    /// <summary>У торговца есть товар (<c>0x45bf99</c>: <c>0x6012c5</c> ≠ 0).</summary>
    public bool MerchantStocked { get; init; }

    /// <summary>Подложный приказ стоит того (<c>0x45c1e5</c>): в группе цели не меньше двух юнитов и у живого юнита первая атака
    /// бьёт «всех» или «любого» (<c>L_ALL</c>/<c>L_ANY</c>) — стрелки и маги.</summary>
    public bool MisfitUseful { get; init; }

    /// <summary>Лидер цели слаб для дуэли (<c>0x45c2b3</c>): здоровье меньше 100 и первая атака — площадной урон меньше 40 или
    /// класса поддержки (лечение, усиление и ослабление урона, ослабление инициативы, воскрешение, исцеление, обереги,
    /// разрушение брони, дар атаки).</summary>
    public bool DuelWeakLeader { get; init; }

    /// <summary>Хозяин крепости-цели — нейтрал (<c>0x5e5358</c> от <c>+0x84</c>).</summary>
    public bool OwnerNeutral { get; init; }
}

/// <summary>Выбор ИИ: действие и, у кражи предмета, номер экземпляра в сумке цели (бросок R250).</summary>
public readonly record struct AiThiefChoice(ThiefAction Action, int ItemIndex = -1);

/// <summary>
/// Выбор действия вора ИИ — <c>0x45b98a</c> (колбэк свитка вора у действия-перемещения, слот 23 второй таблицы
/// <c>0x45ae96</c>), ранг 1 по листингу 24.09 (<c>docs/ai-tactic-candidates.md</c> §12). Две ветки по действующему приказу
/// вора:
/// <list type="bullet">
/// <item><b><c>L_ASSIST</c></b> (<c>0x837880</c>) — жёсткий порядок без бросков (<see cref="ChooseAssist"/>);</item>
/// <item><b>прочие</b> — бунт, если доступен (<c>0x45c0ec</c> всегда да); иначе список доступного перемешивается
/// (<c>0x45c7c3</c>: i = 1…n − 1, обмен с <c>R250(i)</c>) и берётся первое действие, чья проверка прошла
/// (<see cref="Passes"/>); кража у мага обрывает выбор отказом.</item>
/// </list>
/// Нет выбора — ИИ отменяет свиток (<c>0x41e771</c>). Журналы human и legions: броски перемешивания стоят сразу перед
/// броском вора (<c>thief_roll</c>) — харнесс <c>AiThiefHarnessTests</c> восстанавливает их по траектории R250.
/// </summary>
public static class AiThief
{
    /// <summary>Отравление отряда — в группе цели не меньше трёх юнитов (<c>0x45c3f2</c>: <c>cmp eax, 3; setge</c>).</summary>
    public const int PoisonUnits = 3;

    /// <summary>Выбор ИИ; null — отмена свитка.</summary>
    /// <param name="assist">Действующий приказ вора — <c>L_ASSIST</c>.</param>
    /// <param name="diplomat">Лорд игрока-вора — <c>L_DIPLOMAT</c> (<c>0x5e471a</c>).</param>
    /// <param name="available">Доступные действия (<see cref="Thieves.Available"/>) в порядке движка.</param>
    public static AiThiefChoice? Choose(bool assist, bool diplomat, IReadOnlyList<ThiefAction> available,
                                        AiThiefTarget target, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        if (available.Count == 0) return null;
        if (assist) return ChooseAssist(diplomat, available, target) is { } a ? new AiThiefChoice(a) : null;
        if (available.Contains(ThiefAction.RiotCity)) return new AiThiefChoice(ThiefAction.RiotCity);
        var order = available.ToList();
        for (int i = 1; i < order.Count; i++)
        {
            int j = rng.Next(i);
            (order[i], order[j]) = (order[j], order[i]);
        }
        foreach (var action in order)
        {
            if (action == ThiefAction.StealMage) return null;   // 0x45bda3 → 0x45be4e: выход без выбора
            if (!Passes(action, target)) continue;
            // 0x45bf2f: экземпляр — R250 от размера сумки (проверка сама же и бросает).
            return action == ThiefAction.StealItem ? new AiThiefChoice(action, rng.Next(target.ItemCount)) : new AiThiefChoice(action);
        }
        return null;
    }

    /// <summary>Проверка действия в ветке с перемешиванием (<c>0x45bc74</c>…): подкуп — по карману; кража предмета — сумка
    /// не пуста; кража заклинания — есть что; дуэль — у лидера цели нет жезла; кража у торговца — есть товар; отравление
    /// отряда — есть группа и в ней не меньше трёх юнитов; шпионаж (<c>0x45c0ec</c>) и прочие — всегда да.</summary>
    public static bool Passes(ThiefAction action, AiThiefTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return action switch
        {
            ThiefAction.Bribe => target.BribeAffordable,
            ThiefAction.StealItem => target.ItemCount > 0,
            ThiefAction.StealSpell => target.SpellToSteal,
            ThiefAction.Duel => target.DuelAllowed,
            ThiefAction.StealMerchant => target.MerchantStocked,
            ThiefAction.PoisonStack => target.HasGroup && target.UnitCount >= PoisonUnits,
            _ => true,
        };
    }

    /// <summary>
    /// Ветка помощника (<c>0x45ba49</c>): отряд-цель — у Дипломата подложный приказ (<see cref="AiThiefTarget.MisfitUseful"/>),
    /// дуэль (<see cref="AiThiefTarget.DuelWeakLeader"/>), отравление при трёх юнитах, убийство, отравление; у прочих — дуэль,
    /// затем отравление. Крепость-цель: нейтрал — отравление города; иначе подложный приказ, затем отравление города. Помощник
    /// в чужих не ходит — в корпусе ветки нет.
    /// </summary>
    private static ThiefAction? ChooseAssist(bool diplomat, IReadOnlyList<ThiefAction> available, AiThiefTarget target)
    {
        bool Has(ThiefAction a) => available.Contains(a);
        bool duel = Has(ThiefAction.Duel) && target.DuelAllowed && target.DuelWeakLeader;
        if (target.Kind == ThiefTargetKind.Stack)
        {
            if (diplomat)
            {
                if (Has(ThiefAction.Misfit) && target.MisfitUseful) return ThiefAction.Misfit;
                if (duel) return ThiefAction.Duel;
                if (Has(ThiefAction.PoisonStack) && target.HasGroup && target.UnitCount >= PoisonUnits) return ThiefAction.PoisonStack;
                if (Has(ThiefAction.Assassinate)) return ThiefAction.Assassinate;
            }
            else if (duel) return ThiefAction.Duel;
            return Has(ThiefAction.PoisonStack) ? ThiefAction.PoisonStack : null;
        }
        if (target.Kind != ThiefTargetKind.Fortification) return null;
        if (!target.OwnerNeutral && Has(ThiefAction.Misfit) && target.MisfitUseful) return ThiefAction.Misfit;
        return Has(ThiefAction.PoisonCity) ? ThiefAction.PoisonCity : null;
    }
}
