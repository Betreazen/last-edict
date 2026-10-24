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
    [InlineData(800, WideUnits.Wrapper, 800)]
    [InlineData(989, WideUnits.Spread, 800)]
    [InlineData(1067, WideUnits.Wrapper, 990)]
    [InlineData(1067, WideUnits.Spread, 1067)]
    public void Width_ByCanvasAndUnits(int canvas, WideUnits units, int expected) => Assert.Equal(expected, WideBattle.Width(canvas, units));

    [Fact]
    public void Place_NarrowCanvas_KeepsScreen() => Assert.Same(Screen, WideBattle.Place(Screen, 800, WideUnits.Wrapper));

    [Fact]
    public void Place_Wide_MovesRightColumnAndCentre()
    {
        var wide = WideBattle.Place(Screen, 1067, WideUnits.Spread);
        Assert.Equal(1067, wide.Design.Width);
        Assert.Equal(7, At(wide, "IMG_LSLOT1").X);
        Assert.Equal(640 + 267, At(wide, "IMG_RSLOT1").X);
        Assert.Equal(380 + 133, At(wide, "BTN_DEFEND").X);
        Assert.Equal(-1000, At(wide, "TOG_RIGHTUNITS").X);
        var panel = wide.Elements.Single(e => e.Id == "IMG_BIGFACESBG");
        Assert.Equal((0, 1067), (panel.Rect.X, panel.Rect.Width));
        Assert.Equal($"Widen:PANEL@1067/{WideBattle.BottomCuts}", panel.Image);
    }

    /// <summary>Места врапера (холст 990) — на холсте 990 как есть у обоих вариантов; разведённые на холсте шире — дальше от середины.</summary>
    [Fact]
    public void Units_WrapperPlaces_SpreadWider()
    {
        Assert.Equal((531, 157), (At(WideBattle.Place(Screen, 990, WideUnits.Wrapper), "IMG_AUNIT1").X, At(WideBattle.Place(Screen, 990, WideUnits.Wrapper), "IMG_AUNIT1").Y));
        Assert.Equal(531, At(WideBattle.Place(Screen, 990, WideUnits.Spread), "IMG_AUNIT1").X);
        Assert.Equal(531 + 205, At(WideBattle.Place(Screen, 1400, WideUnits.Wrapper), "IMG_AUNIT1").X);
        int left = At(WideBattle.Place(Screen, 1400, WideUnits.Spread), "IMG_AUNIT1").X;
        int right = At(WideBattle.Place(Screen, 1400, WideUnits.Spread), "IMG_DUNIT1").X;
        Assert.True(right - left > 622 - 531, $"разведённые {left}…{right}");
    }

    [Fact]
    public void Layout_Wide_NoMirrorShift_BackgroundCentred()
    {
        var mirrored = new BattleLayout(OwnAttacks: false, Width: 1067);
        Assert.True(mirrored.Wide);
        Assert.True(mirrored.Mirrored);
        Assert.Equal(0, mirrored.Shift);
        Assert.Equal((1067 - BattleLayout.BackgroundWidth) / 2, mirrored.BackgroundX);
        var rect = new UiRect(10, 0, 50, 50);
        Assert.Equal(rect, mirrored.Mirror(rect));
        Assert.False(new BattleLayout(OwnAttacks: false).Wide);
        Assert.NotEqual(rect, new BattleLayout(OwnAttacks: false).Mirror(rect));
    }
}
