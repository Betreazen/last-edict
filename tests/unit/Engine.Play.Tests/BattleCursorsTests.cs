using D2.Data.Battle;
using D2.Rules.Battle;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Курсор цели в бою (<c>0x633d7d</c>, <c>0x630890</c>): пара по классу атаки, «нельзя» — с <c>N</c>.</summary>
public class BattleCursorsTests
{
    [Theory]
    [InlineData(AttackClass.Damage, true, "CUATTACK")]
    [InlineData(AttackClass.Paralyze, false, "CUATTACKN")]
    [InlineData(AttackClass.Summon, true, "CUATTACK")]
    [InlineData(AttackClass.Heal, true, "CUHEAL")]
    [InlineData(AttackClass.Revive, false, "CUHEALN")]
    [InlineData(AttackClass.GiveAttack, true, "CUHEAL")]
    [InlineData(AttackClass.Doppelganger, true, "CUTRANSF")]
    [InlineData(AttackClass.TransformOther, false, "CUTRANSFN")]
    public void Of_PairByAttackClass(AttackClass attack, bool allowed, string cursor) =>
        Assert.Equal(cursor, BattleCursors.Of(attack, allowed));
}

/// <summary>Анимация применения боевого предмета (<c>0x682eb0</c>): по категории, а не по <c>GItem.UNIT_ID</c>.</summary>
public class BattleItemAnimationTests
{
    [Theory]
    [InlineData(D2.Rules.Magic.ItemCategory.PotionHeal, "G000IG1111")]
    [InlineData(D2.Rules.Magic.ItemCategory.PotionRevive, "G000IG1111")]
    [InlineData(D2.Rules.Magic.ItemCategory.PotionBoost, "G000IG1111")]
    [InlineData(D2.Rules.Magic.ItemCategory.PotionPermanent, "G000IG1111")]
    [InlineData(D2.Rules.Magic.ItemCategory.Orb, "G000IG2222")]
    [InlineData(D2.Rules.Magic.ItemCategory.Talisman, "G000IG3333")]
    public void ItemAnimation_ByCategory(D2.Rules.Magic.ItemCategory category, string id) =>
        Assert.Equal(id, D2.Data.Gfx.BattleScene.ItemAnimation(category));
}
