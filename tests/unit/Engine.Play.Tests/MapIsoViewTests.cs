using D2.Data.Map;
using D2.Data.Ui;

namespace D2.Data.Tests;

/// <summary>Вид карты <c>DLG_ISO_VIEW</c> (<see cref="IsoViewLogic"/>): рамка края поля <c>ISO_OUTLINE</c> над картой — уголок справа
/// внизу, которого у порта не было (сверка снимков 28.09, <c>docs/ai-screen.md</c> §11).</summary>
public class MapIsoViewTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [SkippableFact]
    public void IsoView_DrawsOutline_FromUiAssets()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == IsoViewLogic.Screen);
        var ui = new UiScreenState(screen);

        new IsoViewLogic().Open(ui);

        Assert.Equal(IsoViewLogic.Outline, ui.Background);
        var outline = UiAssets.Load(ImportedData.PathOf("ui/assets.json")).Single(a => a.Name == IsoViewLogic.Outline);
        Assert.Equal((800, 600), (outline.W, outline.H));   // во весь экран: место — начало вида (0, 0)
        Assert.Equal((0, 0, 800, 600), (screen.Design.X, screen.Design.Y, screen.Design.Width, screen.Design.Height));
    }
}
