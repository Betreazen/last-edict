using D2.Data.Map;

namespace D2.Data.Tests;

/// <summary>Плавная прокрутка вида (<see cref="SmoothScrollPath"/>, <c>CMidAnimSmoothScroll</c>).</summary>
public class SmoothScrollPathTests
{
    /// <summary>Положения прокрутки: номера точек 0, 4, 12, 24, … до половины и симметрично с конца (<c>0x4f4b51</c>); последнее — цель.
    /// 400 точек по оси — 20 положений, 19 тиков по 33 мс; шаг не больше 128 (на 4000 точек — 64 положения).</summary>
    [Fact]
    public void SmoothScroll_AcceleratesAndBrakes()
    {
        var path = SmoothScrollPath.Offsets(400, 0);
        int[] want = [400, 396, 388, 376, 360, 340, 316, 288, 256, 220, 180, 144, 112, 84, 60, 40, 24, 12, 4, 0];
        Assert.Equal(want, path.Select(p => p.X));
        Assert.All(path, p => Assert.Equal(0, p.Y));
        Assert.Equal([(0, 0)], SmoothScrollPath.Offsets(0, 0));
        var far = SmoothScrollPath.Offsets(-4000, 1200);
        Assert.Equal((-4000, 1200), far[0]);
        Assert.Equal((0, 0), far[^1]);
        Assert.All(far.Zip(far.Skip(1)), p => Assert.InRange(Math.Abs(p.Second.X - p.First.X), 1, SmoothScrollPath.MaxStep));
        Assert.Equal(64, far.Count);

        Assert.False(SmoothScrollPath.Wanted((10, 10), (11, 9), onlyIfFar: true));
        Assert.True(SmoothScrollPath.Wanted((10, 10), (12, 10), onlyIfFar: true));   // ≥ 2 (0x4f4a7c: jge)
        Assert.True(SmoothScrollPath.Wanted((10, 10), (10, 8), onlyIfFar: true));
        Assert.True(SmoothScrollPath.Wanted((10, 10), (11, 10), onlyIfFar: false));
    }
}
