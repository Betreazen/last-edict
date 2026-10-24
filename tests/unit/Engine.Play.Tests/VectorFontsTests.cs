using LastEdict.Data.Ui;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Векторный текст (<see cref="VectorFonts"/>): растровые шрифты игры — PT Serif своего начертания и кегля, шаг строки прежний;
/// шрифты модов не трогаются.</summary>
public sealed class VectorFontsTests
{
    private static readonly UiFonts Game = new()
    {
        Faces = new Dictionary<string, UiFont>(StringComparer.OrdinalIgnoreCase)
        {
            ["Normal"] = new("assets/fonts/normal.fnt", 13, 17, "data/fonts/EBGaramond-Regular.ttf"),
            ["MedBold"] = new("assets/fonts/medbold.fnt", 13, 17),
            ["VLarge"] = new("assets/fonts/vlarge.fnt", 19, 25),
            ["Mod"] = new("C:/mods/x/fonts/own.ttf", 15, 20),
            ["Small"] = new("C:/mods/x/fonts/small.fnt", 12, 16),   // растровый шрифт мода — его, не вектор
        },
    };

    [Fact]
    public void Apply_ReplacesBitmapFaces_KeepsLine()
    {
        var fonts = VectorFonts.Apply(Game);
        Assert.Equal(new UiFont(VectorFonts.Regular, 13, 17), fonts.Faces["normal"]);
        Assert.Equal(new UiFont(VectorFonts.Bold, 12, 17), fonts.Faces["MEDBOLD"]);
        Assert.Equal(new UiFont(VectorFonts.Italic, 20, 25), fonts.Faces["VLarge"]);
        Assert.Equal(Game.Faces["Mod"], fonts.Faces["Mod"]);
        Assert.Equal(Game.Faces["Small"], fonts.Faces["small"]);
        Assert.True(VectorFonts.IsVector(fonts.Faces["Normal"].File));
        Assert.False(VectorFonts.IsVector(Game.Faces["Normal"].File));
    }
}
