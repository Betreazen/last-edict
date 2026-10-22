using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;
using static BattleAiTests;

/// <summary>Боевые предметы в политике ИИ — <c>0x5ce917</c> (когда) и <c>0x5cfed3</c> (какой и в кого): разбор §3,
/// Lua тулсета <c>findEquipmentAttackTarget</c>.</summary>
public class AiItemsTests
{
    private static readonly RulesConstants C = new(BatInit: 10, BatDamage: 6);
    private static readonly AttackType OrbAttack = new("g000aa9001", 50, AttackClass.Damage, 100, AttackReach.Any, 30, 0, false, 1, Source: 4);

    private static Item Of(string id, ItemCategory cat, int heal = 0, string attack = "") =>
        new(id, cat, Bank.Zero, "", "", heal, "", Bank.Zero, attack, "");

    private static readonly Item Heal = Of("base:item/life-potion", ItemCategory.PotionHeal, heal: 50);
    private static readonly Item Revive = Of("base:item/potion-of-protection", ItemCategory.PotionRevive);
    private static readonly Item Orb = Of("base:item/treebark-potion", ItemCategory.Orb, attack: OrbAttack.Id);
    private static readonly Item Boost = Of("base:item/iron-skin-potion", ItemCategory.PotionBoost);

    private static BattleUnit Leader(int slot = 0)
    {
        var u = U("L", true, slot);
        u.Type = u.Type with { IsLeader = true, Category = UnitCategory.Leader };
        return u;
    }

    /// <summary>Своя сторона слабее вдвое (rel ≤ 0.7), но не настолько, чтобы бежать; добить некого.</summary>
    private static BattleState Battle(BattleUnit leader, params BattleUnit[] more)
    {
        var rules = new GameRules(new Dictionary<string, UnitType>(), new Dictionary<string, AttackType> { [OrbAttack.Id] = OrbAttack }, C);
        return new BattleState
        {
            AttackerGroup = "S1KC0001", DefenderGroup = "S1KC0002", Round = 1, Rules = rules,
            Units = [leader, .. more, U("E", false, 0, dam: 60, hp: 200)],
        };
    }

    [Fact] public void HealPotion_OnlyForAllyAtHalfHpOrLess()
    {
        var l = Leader();
        l.BattleItems.Add(Heal);
        var ally = U("A", true, 2); ally.Hp = 60;
        var state = Battle(l, ally);
        Assert.Equal(AiAction.Attack, BattleAi.Choose(state, l, new ScriptedRng([]), C).Action);
        ally.Hp = 50;
        Assert.Equal(new AiDecision(AiAction.UseItem, ally, 0), BattleAi.Choose(state, l, new ScriptedRng([]), C));
    }

    [Fact] public void RevivePotion_OnlyOddSlots()
    {
        var l = Leader();
        l.BattleItems.Add(Revive);
        var front = U("F", true, 2); front.Hp = 0;
        var state = Battle(l, front);
        Assert.Equal(AiAction.Attack, BattleAi.Choose(state, l, new ScriptedRng([]), C).Action);
        var back = U("B", true, 3); back.Hp = 0;
        state.Units.Add(back);
        Assert.Equal(new AiDecision(AiAction.UseItem, back, 0), BattleAi.Choose(state, l, new ScriptedRng([]), C));
    }

    /// <summary>Ценность слотов: сфера (3) в слоте 2 раньше лечебного зелья (1) в слоте 1; зелье усиления (0)
    /// не применяется вовсе.</summary>
    [Fact] public void MoreValuableSlotFirst_ValuelessNever()
    {
        var l = Leader();
        l.BattleItems.Add(Heal);
        l.BattleItems.Add(Orb);
        var hurt = U("A", true, 2); hurt.Hp = 10;
        var d = BattleAi.Choose(Battle(l, hurt), l, new ScriptedRng([]), C);
        Assert.Equal((AiAction.UseItem, "E", 1), (d.Action, d.Target?.Id, d.ItemIndex));

        var m = Leader();
        m.BattleItems.Add(Boost);
        Assert.Equal(AiAction.Attack, BattleAi.Choose(Battle(m), m, new ScriptedRng([]), C).Action);
    }

    [Fact] public void NoItem_WhenStrongEnough_OrKillAvailable_OrHuman_OrNotLeader()
    {
        var strong = Leader();
        strong.BattleItems.Add(Orb);
        var s = Battle(strong);
        s.Units.Single(u => u.Id == "E").Attack = s.Units.Single(u => u.Id == "E").Attack with { QtyDamage = 5 };
        Assert.Equal(AiAction.Attack, BattleAi.Choose(s, strong, new ScriptedRng([]), C).Action);

        var killer = Leader();
        killer.BattleItems.Add(Orb);
        var k = Battle(killer, U("E2", false, 2, dam: 60, hp: 200));   // второй сильный — rel остаётся низким
        k.Units.Single(u => u.Id == "E").Hp = 20;
        Assert.Equal(AiAction.Attack, BattleAi.Choose(k, killer, new ScriptedRng([]), C).Action);

        var human = Leader();
        human.BattleItems.Add(Orb);
        var h = Battle(human);
        var hs = new BattleState { AttackerGroup = h.AttackerGroup, DefenderGroup = h.DefenderGroup, Round = 1, Rules = h.Rules,
                                   Units = h.Units, AttackerSide = new BattleSide { IsHuman = true } };
        Assert.Equal(AiAction.Attack, BattleAi.Choose(hs, human, new ScriptedRng([]), C).Action);

        var soldier = U("S", true, 0);
        soldier.BattleItems.Add(Orb);
        Assert.Equal(AiAction.Attack, BattleAi.Choose(Battle(soldier), soldier, new ScriptedRng([]), C).Action);
    }

    /// <summary>Выпитое зелье второй раз не выбирается.</summary>
    [Fact] public void AppliedPotion_NotChosenAgain()
    {
        var l = Leader();
        l.BattleItems.Add(Heal);
        l.AppliedItems.Add(0);
        var hurt = U("A", true, 2); hurt.Hp = 10;
        Assert.Equal(AiAction.Attack, BattleAi.Choose(Battle(l, hurt), l, new ScriptedRng([]), C).Action);
    }
}
