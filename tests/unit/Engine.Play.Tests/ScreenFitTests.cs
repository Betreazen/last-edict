using LastEdict.Data.Ui;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Раскладка показа экрана 800×600 в окне (<see cref="ScreenFit"/>): в буфере экраны получают целый масштаб, буфер ложится в окно
/// целиком и по центру.</summary>
public class ScreenFitTests
{
    /// <summary>Масштаб, который экран получит в буфере: вписывание 800×600 по меньшей стороне (как UiScreenView.Letterbox).</summary>
    private static float DesignScale(ScreenFit fit) =>
        Math.Min(fit.StageWidth / (float)ScreenFit.DesignWidth, fit.StageHeight / (float)ScreenFit.DesignHeight);

    [Theory]
    [InlineData(1280, 720, 2)]    // k = 1,2 → буфер 2133×1200, сжатие 0,6
    [InlineData(1920, 1080, 2)]   // k = 1,8
    [InlineData(1366, 768, 2)]
    [InlineData(2560, 1440, 3)]   // k = 2,4
    [InlineData(800, 600, 1)]     // k = 1 — буфер равен окну, без фильтра
    [InlineData(1600, 1200, 2)]   // ровно 2
    [InlineData(1024, 1280, 2)]   // окно выше, чем шире: ограничивает ширина, k = 1,28
    public void Sharp_StageGivesIntegerDesignScale(int w, int h, int n)
    {
        var fit = ScreenFit.For(w, h, ScreenScaling.Sharp);
        Assert.Equal(n, DesignScale(fit), 3);
        Assert.InRange(fit.StageWidth * fit.Scale, w - 1, w + 1);    // буфер закрывает окно целиком
        Assert.InRange(fit.StageHeight * fit.Scale, h - 1, h + 1);
        Assert.InRange(fit.X, -1, 1);
        Assert.InRange(fit.Y, -1, 1);
        Assert.True(fit.Scale <= 1.0001f, "чёткий режим только сжимает: n не меньше масштаба окна");
    }

    [Fact]
    public void Sharp_1280x720_Numbers()
    {
        var fit = ScreenFit.For(1280, 720, ScreenScaling.Sharp);
        Assert.Equal((2133, 1200), (fit.StageWidth, fit.StageHeight));
        Assert.Equal(0.6f, fit.Scale, 4);
        Assert.True(fit.Filtered);
    }

    [Fact]
    public void Sharp_ExactIntegerWindow_IsPixelExact()
    {
        var fit = ScreenFit.For(1600, 1200, ScreenScaling.Sharp);
        Assert.Equal(1f, fit.Scale);
        Assert.False(fit.Filtered);   // точка в точку — фильтр не нужен
    }

    [Theory]
    [InlineData(1280, 720, 1, 240, 60)]    // k = 1,2 → целый 1: 800×600 в рамке
    [InlineData(1920, 1080, 1, 560, 240)]  // k = 1,8 → 1
    [InlineData(2560, 1440, 2, 480, 120)]  // k = 2,4 → 2: 1600×1200
    public void Integer_FramesDesignAtWholeScale(int w, int h, int n, int frameX, int frameY)
    {
        var fit = ScreenFit.For(w, h, ScreenScaling.Integer);
        Assert.Equal(n, DesignScale(fit), 3);
        Assert.Equal(1f, fit.Scale);
        Assert.False(fit.Filtered);
        // Экран 800n×600n по центру буфера, буфер по центру окна — рамка до экрана слева и сверху.
        int left = fit.X + ((fit.StageWidth - (ScreenFit.DesignWidth * n)) / 2);
        int top = fit.Y + ((fit.StageHeight - (ScreenFit.DesignHeight * n)) / 2);
        Assert.InRange(left, frameX - 1, frameX + 1);
        Assert.InRange(top, frameY - 1, frameY + 1);
    }

    [Fact]
    public void Nearest_StageIsWindow()
    {
        var fit = ScreenFit.For(1280, 720, ScreenScaling.Nearest);
        Assert.Equal(new ScreenFit(1280, 720, 1f, 0, 0, false), fit);
    }

    /// <summary>HD: экраны — сразу в точках окна, без буфера ×n и сжатия.</summary>
    [Fact]
    public void Hd_StageIsWindow()
    {
        Assert.Equal(new ScreenFit(1920, 1080, 1f, 0, 0, false), ScreenFit.For(1920, 1080, ScreenScaling.Hd));
    }

    [Fact]
    public void SmallWindow_SharpShrinksFromDesign()
    {
        var fit = ScreenFit.For(640, 480, ScreenScaling.Sharp);
        Assert.Equal((800, 600), (fit.StageWidth, fit.StageHeight));
        Assert.Equal(0.8f, fit.Scale, 4);
        Assert.Equal(fit, ScreenFit.For(640, 480, ScreenScaling.Integer));   // целым не впишется — как чёткий, без обрезки
    }

    [Fact]
    public void Sharp_HugeWindow_CapsStage()
    {
        var fit = ScreenFit.For(7680, 4320, ScreenScaling.Sharp);   // k = 7,2
        Assert.Equal(ScreenFit.MaxStageScale, DesignScale(fit), 3);
        Assert.True(fit.Scale > 1f);
    }

    [Theory]
    [InlineData("sharp", ScreenScaling.Sharp)]
    [InlineData("Integer", ScreenScaling.Integer)]
    [InlineData(" NEAREST ", ScreenScaling.Nearest)]
    [InlineData("HD", ScreenScaling.Hd)]
    public void Parse_KnownNames(string name, ScreenScaling expected) => Assert.Equal(expected, ScreenFit.Parse(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bilinear")]
    public void Parse_Unknown_IsNull(string? name) => Assert.Null(ScreenFit.Parse(name));

    [Fact]
    public void ZeroWindow_DoesNotDivideByZero() => Assert.True(ScreenFit.For(0, 0, ScreenScaling.Sharp).StageWidth >= 1);
}
