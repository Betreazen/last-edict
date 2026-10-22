using D2.Data.Ui;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Курсор окна по разметке (<see cref="UiCursor"/>, <c>0x50ca59</c>/<c>0x50cd54</c>): имя как есть и горячая точка из полей 7–8.</summary>
public class UiCursorTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<IReadOnlyList<UiScreen>> Screens = new(() => UiScreens.Load(ImportedData.PathOf("ui/screens.json")));

    private static UiScreen Screen(string id) => Screens.Value.Single(s => s.Id == id);

    /// <summary>Окна карты и меню — кинжал <c>_CUDEFAUL</c> с точкой 0,0; панели и столица курсора не задают (курсор не меняется).</summary>
    [SkippableTheory]
    [InlineData("DLG_OPTIONS", "_CUDEFAUL")]
    [InlineData("DLG_MANAGE_STACK", "_CUDEFAUL")]
    [InlineData("DLG_MERCHANT", "_CUDEFAUL")]
    [InlineData("DLG_CITY_STACK", "_CUDEFAUL")]
    [InlineData("DLG_MAIN_MENU", "_CUDEFAUL")]
    [InlineData("DLG_MESSAGE_BOX", "_CUDEFAUL")]
    [InlineData("DLG_CAPITAL", null)]
    [InlineData("DLG_DROP_BAG", null)]
    [InlineData("DLG_CINEMATIC", null)]
    public void Of_TakesNameAsIsWithTopLeftHotspot(string screen, string? frame)
    {
        var cursor = UiCursor.Of(Screen(screen));
        Assert.Equal(frame, cursor?.Frame);
        if (cursor is { } c) Assert.Equal((0, 0), (c.HotspotX, c.HotspotY));
    }

    /// <summary>Все курсоры разметки есть в описи кадров одной картинкой — кинжалом <c>Interf.ff</c> 321×321, а не <c>DLG_*_CUDEFAUL</c>.</summary>
    [SkippableFact]
    public void AllScreenCursors_ResolveToDaggerOfInterf()
    {
        var assets = UiAssets.ByName(UiAssets.Load(ImportedData.PathOf("ui/assets.json")));
        var frames = Screens.Value.Select(UiCursor.Of).OfType<UiCursor>().Select(c => c.Frame).Distinct().ToList();
        Assert.Equal(["_CUDEFAUL"], frames);
        var dagger = assets["_CUDEFAUL"];
        Assert.Equal(("Interf.ff", 321, 321), (dagger.Archive, dagger.W, dagger.H));
        Assert.Equal(91, Screens.Value.Count(s => UiCursor.Of(s) is not null));
    }

    /// <summary>Горячая точка — из полей 7–8; у своего экрана без них — левый верхний угол.</summary>
    [Fact]
    public void Of_HotspotFromLegacyOrTopLeft()
    {
        var own = new UiScreen { Id = "MY", Cursor = "MY_CURSOR", Legacy = ["5", "7", "0"] };
        Assert.Equal(new UiCursor("MY_CURSOR", 5, 7), UiCursor.Of(own));
        Assert.Equal(new UiCursor("MY_CURSOR", 0, 0), UiCursor.Of(own with { Legacy = [] }));
        Assert.Null(UiCursor.Of(own with { Cursor = "" }));
        Assert.Null(UiCursor.Of(null));
    }
}
