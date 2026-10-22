using D2.Rules.Magic;
using D2.Rules.Units;

namespace D2.Rules.Battle;

/// <summary>Боевые предметы в политике ИИ. Когда — <c>0x5ce917</c> (лидер, <c>rel ≤ 0.7</c>, добить некого, цели
/// предмета есть); какой и в кого — <c>FindEquipment</c> <c>0x5cfed3</c>. Разбор §3 (корпус: 5 применений из 5
/// согласуются); перебор по Lua тулсета <c>findEquipmentAttackTarget</c> (ранг 2).
/// Слоты <c>Battle1</c>/<c>Battle2</c> — <see cref="BattleUnit.BattleItems"/>[0] и [1].</summary>
public static class AiItems
{
    /// <summary>Порог силы, ниже которого лидер тянется к предметам (<c>.rdata</c> 0.7).</summary>
    public const double UseThreshold = 0.7;

    /// <summary>Ценность слота <c>0x5d002a</c>: талисман 4, сфера 3, воскрешение 2, лечебное зелье 1, прочее 0.</summary>
    public static int Value(Item item) => item.Category switch
    {
        ItemCategory.Talisman => 4,
        ItemCategory.Orb => 3,
        ItemCategory.PotionRevive => 2,
        ItemCategory.PotionHeal => 1,
        _ => 0,
    };

    /// <summary>Решение применить предмет или null. Сторона человека в автобое предметов не применяет
    /// (<c>0x4285d5</c>–<c>0x428625</c>, ранг 2). Сначала более ценный слот, из равных — первый.
    /// <para>ponytail: применённые зелье и сфера второй раз не выбираются (ранг 3 — как движок исключает их
    /// из действий, не прочитано); заряды талисмана не проверяются.</para></summary>
    public static AiDecision? Choose(AiContext ctx, BattleUnit unit, double rel)
    {
        var items = unit.BattleItems;
        if (!unit.Type.IsLeader || rel > UseThreshold || items.Count == 0 || ctx.State.SideOf(unit).IsHuman) return null;
        int first = items.Count > 1 && Value(items[0]) < Value(items[1]) ? 1 : 0;
        foreach (int i in new[] { first, 1 - first })
        {
            if (i >= items.Count || Value(items[i]) <= 0) continue;
            if (items[i].IsDestroyedAfterBattle(unit.AppliedItems.Contains(i))) continue;
            if (Target(ctx, unit, items[i]) is { } t) return new AiDecision(AiAction.UseItem, t, i);
        }
        return null;
    }

    /// <summary><c>0x5d00a3</c>: предмет с атакой — цель по отфильтрованному списку (<c>0x5d2493</c>: призыв — первый
    /// ряд, лечение — доля HP ≤ 0.5); зелье — фильтр <c>0x5d229b</c>, затем <c>0x5d018e</c>.</summary>
    private static BattleUnit? Target(AiContext ctx, BattleUnit unit, Item item)
    {
        if (item.AttackId.Length == 0) return PotionTarget(ctx, unit, item);
        if (ctx.State.Rules?.Attacks.GetValueOrDefault(item.AttackId) is not { } attack) return null;
        // ponytail: цели атаки предмета — те же, что у атаки носителя с этим классом и досягаемостью.
        var possible = BattleTargets.Possible(ctx.State, unit, attack).Where(t => attack.Class switch
        {
            AttackClass.Summon => Formation.IsFront(t.Slot),
            AttackClass.Heal => HalfOrLess(t),
            _ => true,
        }).ToList();
        return possible.Count == 0 ? null : AiTargets.FindItemTarget(ctx, unit, attack, possible);
    }

    /// <summary>Лечебное — свои раненые с долей HP ≤ 0.5 (<c>0x5d242e</c>), затем лечение; воскрешение — павшие на
    /// нечётных позициях, затем воскрешение.</summary>
    private static BattleUnit? PotionTarget(AiContext ctx, BattleUnit unit, Item item)
    {
        var allies = ctx.State.Allies(unit).OrderBy(u => u.Slot);
        return item.Category switch
        {
            ItemCategory.PotionHeal => AiTargets.FindHeal(ctx, allies.Where(u => u.OnField && u.Hp < u.HpMax && HalfOrLess(u)).ToList()),
            ItemCategory.PotionRevive => AiRareTargets.Revive(allies.Where(u => !u.Alive && !u.Retreated && Formation.IsBack(u.Slot)).ToList()),
            _ => null,
        };
    }

    private static bool HalfOrLess(BattleUnit u) => (double)u.Hp / u.HpMax <= 0.5;
}
