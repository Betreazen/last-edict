using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Время интерфейса в целых миллисекундах (<c>UiScreenView</c>: автоповтор, подсказка, анимации): остаток кадра копится, и
/// счёт не зависит от частоты кадров.</summary>
public class FrameClockTests
{
    [Theory]
    [InlineData(300)]
    [InlineData(144)]
    [InlineData(60)]
    [InlineData(30)]
    public void SecondOfFramesIsThousandMs(int fps)
    {
        var clock = new FrameClock();
        int total = 0;
        for (int i = 0; i < fps; i++) total += clock.Advance(1.0 / fps);

        Assert.InRange(total, 999, 1000);
    }

    [Fact]
    public void ShortFramesAddUp()
    {
        var clock = new FrameClock();
        Assert.Equal(0, clock.Advance(0.0004));
        Assert.Equal(0, clock.Advance(0.0004));
        Assert.Equal(1, clock.Advance(0.0004));
    }
}
