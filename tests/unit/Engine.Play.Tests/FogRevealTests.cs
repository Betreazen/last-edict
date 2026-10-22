using D2.Data.Map;

namespace D2.Data.Tests;

/// <summary>Смена тумана кольцами (<see cref="FogReveal"/>, <c>CMidAnim2ChangeFog</c>).</summary>
public class FogRevealTests
{
    /// <summary>Туман кольцами: центр, затем кольца радиусов 1…r, углы кольца сдвинуты внутрь; всего тронуто — квадрат <c>±r</c> без четырёх
    /// угловых клеток (форма модели, <c>0x5d4dfc</c>). У края карты прямоугольник зажат, и центр колец сдвинут.</summary>
    [Fact]
    public void FogReveal_RingsFromCentre()
    {
        var fog = new FogReveal(10, 10, 2, 50);
        Assert.Equal(3, fog.Steps);
        Assert.Equal([(10, 10)], fog.Cells(0));
        Assert.Equal([(10, 10), (10, 9), (10, 10), (9, 10), (11, 10), (10, 10), (10, 11), (10, 10)], fog.Cells(1));
        var touched = Enumerable.Range(0, fog.Steps).SelectMany(fog.Cells).ToHashSet();
        var square = (from x in Enumerable.Range(8, 5) from y in Enumerable.Range(8, 5)
                      where !(Math.Abs(x - 10) == 2 && Math.Abs(y - 10) == 2) select (x, y)).ToHashSet();
        Assert.True(square.SetEquals(touched));

        var edge = new FogReveal(0, 0, 3, 10);   // прямоугольник 0…4: сторона 4, центр (2, 2)
        Assert.Equal(2, edge.Steps);
        Assert.Equal([(2, 2)], edge.Cells(0));
        Assert.DoesNotContain(edge.Cells(1), c => c.X == 0 || c.Y == 0);
        Assert.Single(new FogReveal(5, 5, 0, 10).Cells(0));
        Assert.Equal(1, new FogReveal(5, 5, 0, 10).Steps);
    }
}
