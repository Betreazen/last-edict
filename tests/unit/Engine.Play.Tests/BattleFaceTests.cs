using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using LastEdict.Rules.Battle;

namespace LastEdict.Data.Tests;

/// <summary>Состояния портрета панели боя (<see cref="BattleFaces"/>): таблицы рисования движка (<c>0x5b191f</c>, <c>0x5b19fa</c>,
/// <c>0x5b1a91</c>, <c>0x5b1add</c>) и выбор вспышки по удару (<c>0x63bcc2…0x63bfa8</c>).</summary>
public class BattleFaceTests
{
    /// <summary>Все 30 состояний: заливка (цвет или нет), красная доля ран, сила смешения — как в таблицах движка (разбор 20.10).</summary>
    [Theory]
    [InlineData(0, null, true, 128)]
    [InlineData(1, null, false, 128)]
    [InlineData(2, "255;000;000", false, 128)]
    [InlineData(3, "255;255;000", false, 128)]
    [InlineData(4, null, false, 128)]
    [InlineData(5, "000;000;255", false, 128)]
    [InlineData(6, "000;255;000", false, 128)]
    [InlineData(7, "128;128;128", true, 128)]
    [InlineData(8, "128;128;128", true, 128)]
    [InlineData(9, null, false, 128)]
    [InlineData(10, "000;000;000", false, 128)]
    [InlineData(11, "255;255;064", false, 128)]
    [InlineData(12, null, false, 128)]
    [InlineData(13, "000;000;000", false, 128)]
    [InlineData(14, "255;000;255", false, 128)]
    [InlineData(15, null, false, 128)]
    [InlineData(16, "128;128;000", true, 128)]
    [InlineData(17, "000;000;128", true, 128)]
    [InlineData(18, "192;128;000", true, 128)]
    [InlineData(19, "000;255;000", false, 128)]
    [InlineData(20, "094;218;249", false, 128)]
    [InlineData(21, "000;000;255", false, 128)]
    [InlineData(22, "000;000;255", false, 128)]
    [InlineData(23, "255;000;255", false, 128)]
    [InlineData(24, null, false, 128)]
    [InlineData(25, null, true, 128)]
    [InlineData(26, null, true, 128)]
    [InlineData(27, "000;000;000", false, 255)]
    [InlineData(28, "000;000;000", false, 255)]
    [InlineData(29, "000;000;000", false, 255)]
    public void Look_IsEngineTables(int state, string? whole, bool wounds, int alpha)
    {
        var look = BattleFaces.Look((BattleFace)state);
        Assert.Equal(whole, look.Whole?.ToString());
        Assert.Equal(wounds, look.Wounds);
        Assert.Equal(alpha, look.Alpha);
    }

    /// <summary>Вспышка цели удара: защита раньше промаха, промах раньше класса; превращение и призыв держатся; паралич и окаменение
    /// вспышкой не ложатся (<c>0x64d05f</c>); прочие классы урона — красным.</summary>
    [Fact]
    public void Strike_ShieldThenMissThenClass()
    {
        Assert.Equal(new BattleFlash(BattleFace.Shielded, false), BattleFaces.Strike(AttackClass.Damage, missed: true, shielded: true));
        Assert.Equal(new BattleFlash(BattleFace.Missed, false), BattleFaces.Strike(AttackClass.Heal, missed: true, shielded: false));
        Assert.Equal(new BattleFlash(BattleFace.Healed, false), BattleFaces.Strike(AttackClass.Heal, false, false));
        Assert.Equal(new BattleFlash(BattleFace.Hit, false), BattleFaces.Strike(AttackClass.Drain, false, false));
        Assert.Equal(new BattleFlash(BattleFace.Hit, false), BattleFaces.Strike(AttackClass.Fear, false, false));
        Assert.Equal(new BattleFlash(BattleFace.DrainLevel, false), BattleFaces.Strike(AttackClass.DrainLevel, false, false));
        Assert.Equal(new BattleFlash(BattleFace.Transformed, true), BattleFaces.Strike(AttackClass.Doppelganger, false, false));
        Assert.Equal(new BattleFlash(BattleFace.Summoned, true), BattleFaces.Strike(AttackClass.Summon, false, false));
        Assert.Null(BattleFaces.Strike(AttackClass.Paralyze, false, false));
        Assert.Null(BattleFaces.Strike(AttackClass.Petrify, false, false));
        Assert.Equal(BattleFace.Frostbitten, BattleFaces.Tick(AttackClass.Frostbite).Face);
    }

    /// <summary>Смешение «цвет вместо точки» (сила 255, превращение и призыв): прозрачные точки не трогаются, раны не красятся.</summary>
    [Fact]
    public void Shade_FullAlphaReplacesColour()
    {
        byte[] face = [200, 100, 50, 255, 10, 20, 30, 0];
        var look = BattleFaces.Look(BattleFace.Transformed);
        BattlePanel.Shade(face, 2, look.Whole, kept: 1, alpha: look.Alpha);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 10, 20, 30, 0 }, face);
        byte[] healed = [200, 100, 50, 255];
        BattlePanel.Shade(healed, 1, BattleFaces.Look(BattleFace.Healed).Whole, kept: 1);
        Assert.Equal(new byte[] { 100, 50, 152, 255 }, healed);
        Assert.Equal(new UiColor(255, 255, 64), BattleFaces.Color(BattleFace.Experience));
    }
}
