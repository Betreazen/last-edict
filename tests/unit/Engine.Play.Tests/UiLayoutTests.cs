using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Раскладка экрана на холсте формы окна (<see cref="UiLayout"/>, <see cref="UiLayouts"/>; Э5, Р13): холст по меньшей стороне, сдвиг
/// по якорю, растяжение, правила <c>ui/layout.json</c> с шаблонами и слоями модов, смена холста у состояния экрана.</summary>
public class UiLayoutTests
{
    private static UiElement El(string id, int x, int y, int w, int h, UiAnchor anchor = UiAnchor.TopLeft, UiStretch stretch = UiStretch.None) =>
        new() { Id = id, Kind = UiKind.Button, Rect = new UiRect(x, y, w, h), Anchor = anchor, Stretch = stretch };

    private static UiScreen Screen(UiFit fit, params UiElement[] elements) =>
        new() { Id = "DLG_TEST", Fit = fit, Elements = elements };

    [Theory]
    [InlineData(1920, 1080, 1067, 600)]   // 16:9
    [InlineData(1280, 800, 960, 600)]     // 16:10 (Steam Deck)
    [InlineData(2560, 1080, 1422, 600)]   // 21:9
    [InlineData(1024, 768, 800, 600)]     // 4:3 — как разметка
    [InlineData(1280, 1024, 800, 640)]    // 5:4 — прирост по высоте
    public void Canvas_KeepsDesignScale_GrowsTheLongerSide(int viewW, int viewH, int w, int h)
    {
        Assert.Equal((w, h), UiLayout.Canvas(Screen(UiFit.Anchors), viewW, viewH));
        Assert.Equal((800, 600), UiLayout.Canvas(Screen(UiFit.Letterbox), viewW, viewH));
    }

    [Fact]
    public void Place_ShiftsByAnchorShare_StretchGrowsSize()
    {
        var screen = Screen(UiFit.Anchors,
            El("LEFT", 10, 20, 50, 50, UiAnchor.Left),
            El("MIDDLE", 375, 275, 50, 50, UiAnchor.Center),
            El("RIGHT", 740, 0, 60, 30, UiAnchor.TopRight),
            El("CORNER", 740, 570, 60, 30, UiAnchor.BottomRight),
            El("MAP", 0, 0, 640, 600, UiAnchor.Center, UiStretch.Both),
            El("BAR", 0, 580, 800, 20, UiAnchor.Bottom, UiStretch.Width));
        var placed = UiLayout.Place(screen, 1067, 640);
        var at = placed.Elements.ToDictionary(e => e.Id, e => e.Rect);

        Assert.Equal(new UiRect(0, 0, 1067, 640), placed.Design);
        Assert.Equal(new UiRect(10, 40, 50, 50), at["LEFT"]);          // по вертикали — половина прироста 40
        Assert.Equal(new UiRect(509, 295, 50, 50), at["MIDDLE"]);      // 375 + 267/2 (133,5 → 134), 275 + 20
        Assert.Equal(new UiRect(1007, 0, 60, 30), at["RIGHT"]);
        Assert.Equal(new UiRect(1007, 610, 60, 30), at["CORNER"]);
        Assert.Equal(new UiRect(0, 0, 907, 640), at["MAP"]);           // растянуто на весь прирост, якорь не сдвигает
        Assert.Equal(new UiRect(0, 620, 1067, 20), at["BAR"]);         // по ширине — растяжение, по высоте — якорь низа
    }

    [Fact]
    public void Place_LetterboxOrDesignCanvas_SameScreen()
    {
        var anchors = Screen(UiFit.Anchors, El("A", 1, 2, 3, 4, UiAnchor.Right));
        Assert.Same(anchors, UiLayout.Place(anchors, 800, 600));
        var letterbox = Screen(UiFit.Letterbox, El("A", 1, 2, 3, 4, UiAnchor.Right));
        Assert.Same(letterbox, UiLayout.Place(letterbox, 1067, 600));
    }

    [Fact]
    public void Rules_ExactBeatsPattern_LongPatternBeatsShort_DefaultAnchorForOthers()
    {
        var screen = Screen(UiFit.Letterbox, El("IMG_LSLOT1", 0, 0, 1, 1, UiAnchor.Top), El("IMG_LBIGFACE", 0, 0, 1, 1, UiAnchor.Top),
                            El("IMG_AUNIT1", 0, 0, 1, 1, UiAnchor.Left), El("IMG_MAIN", 0, 0, 1, 1));
        var rules = UiLayouts.FromJson("""
            {
              // комментарии и висячие запятые — файл правят руками
              "dlg_test": {
                "fit": "anchors", "anchor": "center",
                "elements": { "IMG_L*": { "anchor": "left" }, "IMG_LBIG*": { "anchor": "bottomLeft" },
                              "IMG_MAIN": { "anchor": "topLeft", "stretch": "both" }, },
              },
            }
            """);
        var applied = UiLayouts.Apply([screen], rules)[0];
        var by = applied.Elements.ToDictionary(e => e.Id);

        Assert.Equal(UiFit.Anchors, applied.Fit);
        Assert.Equal(UiAnchor.Left, by["IMG_LSLOT1"].Anchor);
        Assert.Equal(UiAnchor.BottomLeft, by["IMG_LBIGFACE"].Anchor);
        Assert.Equal(UiAnchor.Center, by["IMG_AUNIT1"].Anchor);
        Assert.Equal((UiAnchor.TopLeft, UiStretch.Both), (by["IMG_MAIN"].Anchor, by["IMG_MAIN"].Stretch));
    }

    [Fact]
    public void Rules_AnyScreenRule_ForScreensWithoutOwn()
    {
        var rules = UiLayouts.FromJson("""{ "*": { "border": "BORDER_1280" }, "OWN": { "fit": "anchors" } }""");
        var applied = UiLayouts.Apply([new UiScreen { Id = "MENU" }, new UiScreen { Id = "OWN" }], rules);
        Assert.Equal("BORDER_1280", applied[0].Border);
        Assert.Null(applied[1].Border);
        Assert.Equal(UiFit.Anchors, applied[1].Fit);
    }

    [Fact]
    public void Merge_ModOverridesFieldsAndElements_KeepsTheRest()
    {
        var baseSet = UiLayouts.FromJson("""{ "A": { "fit": "anchors", "border": "BORDER_1280", "elements": { "X": { "anchor": "left" }, "Y": { "anchor": "right" } } } }""");
        var mod = UiLayouts.FromJson("""{ "A": { "border": "MY_BORDER", "elements": { "Y": { "anchor": "top" } } }, "B": { "fit": "anchors" } }""");
        var merged = UiLayouts.Merge(baseSet, mod);

        Assert.Equal(UiFit.Anchors, merged["A"].Fit);
        Assert.Equal("MY_BORDER", merged["A"].Border);
        Assert.Equal(UiAnchor.Left, merged["A"].Elements["X"].Anchor);
        Assert.Equal(UiAnchor.Top, merged["A"].Elements["Y"].Anchor);
        Assert.Equal(UiFit.Anchors, merged["B"].Fit);
    }

    [Fact]
    public void State_Place_NewRectsForHits_KeepsState()
    {
        var screen = Screen(UiFit.Anchors, El("BTN_OK", 700, 500, 100, 100, UiAnchor.BottomRight),
                            new UiElement { Id = "TOG_X", Kind = UiKind.Toggle, Rect = new UiRect(0, 0, 50, 50) });
        var state = new UiScreenState(screen);
        state.Press(10, 10);
        state.Release(10, 10);
        Assert.True(state.IsChecked("TOG_X"));

        state.Place(UiLayout.Place(screen, 1067, 600));

        Assert.True(state.IsChecked("TOG_X"));
        Assert.Equal(new UiRect(967, 500, 100, 100), state.Screen.Elements[0].Rect);
        Assert.Throws<ArgumentException>(() => state.Place(Screen(UiFit.Anchors, El("OTHER", 0, 0, 1, 1))));
    }

    [Fact]
    public void Serialization_DefaultPolicyNotWritten_SetPolicyRoundTrips()
    {
        var plain = Screen(UiFit.Letterbox, El("A", 0, 0, 1, 1));
        string json = UiScreens.ToJson([plain]);
        Assert.DoesNotContain("Fit", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch", json, StringComparison.Ordinal);

        var set = Screen(UiFit.Anchors, El("A", 0, 0, 1, 1, stretch: UiStretch.Width));
        var back = UiScreens.FromJson(UiScreens.ToJson([set]))[0];
        Assert.Equal((UiFit.Anchors, UiStretch.Width), (back.Fit, back.Elements[0].Stretch));
    }
}
