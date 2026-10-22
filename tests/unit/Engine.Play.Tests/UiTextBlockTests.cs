using LastEdict.Data.Ui;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Положение блока строк в месте подписи по вертикали — правило движка (<c>0x52ad04</c>), сверенное со снимками оригинала 28.09:
/// блок — число строк × шаг строки шрифта (ячейка + межстрочный MFT, вместе с последним), не влез — от верха.</summary>
public class UiTextBlockTests
{
    /// <summary>Снимки 28.09 (CCapitalInterf-58839, DLG_CITY_STACK, DLG_MANAGE_STACK): верх ячейки первой строки от верха места.</summary>
    [Theory]
    [InlineData(85, 2, 17, UiVAlign.Center, 25)]   // TXT_LEADER_STATS «Скорость:\n20 / 20»: у оригинала 25, у Godot было 27
    [InlineData(18, 1, 17, UiVAlign.Center, 0)]    // TXT_LEADER_NAME «Nigel»: у оригинала 0 (имя у верха плашки)
    [InlineData(24, 1, 18, UiVAlign.Center, 3)]    // TXT_CITYNAME, Large (14 + 4): у оригинала 3, у Godot было 5
    [InlineData(83, 3, 17, UiVAlign.Center, 16)]   // TXT_STACK_STATS: (83 − 51) / 2
    [InlineData(20, 1, 18, UiVAlign.Top, 0)]       // главное меню: без \v — от верха
    [InlineData(40, 1, 17, UiVAlign.Bottom, 23)]   // низ: блок кончается у нижнего края места (вместе с межстрочным)
    public void Top_FollowsEngineRule(int box, int lines, int step, UiVAlign v, int expected) =>
        Assert.Equal(expected, UiTextBlock.Top(box, lines, step, v));

    /// <summary>Блок выше места — от верха при любом выравнивании (<c>0x52ad13</c>: высота текста больше места — выравнивание сбрасывается
    /// в 0): плашка постройки 16 точек и шаг Normal 17 — подпись от верха, а не вниз за кромку (снимок 43 прогона 3 от 11.10).</summary>
    [Theory]
    [InlineData(UiVAlign.Center)]
    [InlineData(UiVAlign.Bottom)]
    [InlineData(UiVAlign.Top)]
    public void Top_BlockTallerThanBox_StartsAtTop(UiVAlign v)
    {
        Assert.Equal(0, UiTextBlock.Top(16, 1, 17, v));
        Assert.Equal(0, UiTextBlock.Top(40, 3, 17, v));   // TXT_ITEM свитка: три строки в 40 точках (снимок 45)
    }

    [Fact]
    public void Height_IsLinesTimesStep()
    {
        Assert.Equal(34, UiTextBlock.Height(2, 17));
        Assert.Equal(0, UiTextBlock.Height(0, 17));
    }

    [Fact]
    public void Top_NoLines_IsZero() => Assert.Equal(0, UiTextBlock.Top(18, 0, 17, UiVAlign.Top));
}
