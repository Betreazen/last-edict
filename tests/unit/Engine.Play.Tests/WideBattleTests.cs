using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Широкий бой (<see cref="WideBattle"/>, <see cref="BattleLayout"/>): разметка своя, выдуманная (не разметка игры) — проверяется
/// только перенос мест.</summary>
public sealed class WideBattleTests
{
    private static UiElement Image(string id, int x, int y = 100, int w = 70, int h = 85, string? image = null) =>
        new() { Id = id, Kind = UiKind.Image, Rect = new UiRect(x, y, w, h), Image = image };

    private static readonly UiScreen Screen = new()
    {
        Id = "BATTLE",
        Elements =
        [
            Image("IMG_LSLOT1", 7), Image("IMG_RSLOT1", 640), Image("IMG_BIGFACESBG", 0, 400, 800, 200, "PANEL"),
            Image("IMG_AUNIT1", 300), Image("IMG_DUNIT1", 400), Image("TOG_RIGHTUNITS", 630, 400, 30, 30),
            new UiElement { Id = "BTN_DEFEND", Kind = UiKind.Button, Rect = new UiRect(380, 500, 40, 40) },
        ],
    };

    private static UiRect At(UiScreen screen, string id) => screen.Elements.Single(e => e.Id == id).Rect;

    [Theory]
    [InlineData(800, 800)]
    [InlineData(989, 800)]
    [InlineData(1067, 990)]
    [InlineData(2520, 990)]
    public void Width_WrapperCanvas(int canvas, int expected) => Assert.Equal(expected, WideBattle.Width(canvas));

    [Fact]
    public void Place_NarrowCanvas_KeepsScreen() => Assert.Same(Screen, WideBattle.Place(Screen, 800));

    [Fact]
    public void Place_Wide_MovesRightColumnCentreAndUnits()
    {
        var wide = WideBattle.Place(Screen, 990);
        Assert.Equal(990, wide.Design.Width);
        Assert.Equal(7, At(wide, "IMG_LSLOT1").X);
        Assert.Equal(640 + 190, At(wide, "IMG_RSLOT1").X);
        Assert.Equal(380 + 95, At(wide, "BTN_DEFEND").X);
        Assert.Equal(-1000, At(wide, "TOG_RIGHTUNITS").X);
        Assert.Equal((531, 157), (At(wide, "IMG_AUNIT1").X, At(wide, "IMG_AUNIT1").Y));   // место врапера (холст 990)
        Assert.Equal((622, 216), (At(wide, "IMG_DUNIT1").X, At(wide, "IMG_DUNIT1").Y));
        var panel = wide.Elements.Single(e => e.Id == "IMG_BIGFACESBG");
        Assert.Equal((0, 990), (panel.Rect.X, panel.Rect.Width));
        Assert.Equal($"Widen:PANEL@990/{WideBattle.BottomCuts}", panel.Image);
    }

    [Fact]
    public void Layout_Wide_NoMirrorShift_BackgroundCentred()
    {
        var mirrored = new BattleLayout(OwnAttacks: false, Width: 990);
        Assert.True(mirrored.Wide);
        Assert.True(mirrored.Mirrored);
        Assert.Equal(0, mirrored.Shift);
        Assert.Equal(20, mirrored.BackgroundX);   // фон 950 по середине холста 990
        var rect = new UiRect(10, 0, 50, 50);
        Assert.Equal(rect, mirrored.Mirror(rect));
        Assert.False(new BattleLayout(OwnAttacks: false).Wide);
        Assert.NotEqual(rect, new BattleLayout(OwnAttacks: false).Mirror(rect));
    }
}
