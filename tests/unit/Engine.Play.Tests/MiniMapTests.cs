using D2.Data.Gfx;
using D2.Data.Map;
using D2.Rules.Map;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Мини-карта (<see cref="MiniMap"/>, <c>docs/map-screen.md</c> §1): геометрия растра и то, что клетки
/// закрашиваются узором, а скрытые — туманом.</summary>
public class MiniMapTests
{
    private const int Size = 8;

    /// <summary>Узор: сплошной цвет, свой на каждое имя. Значков нет — проверяется земля.</summary>
    private static RgbaImage? Art(string name)
    {
        byte tone = name switch { "ISOWA" => 10, "ISOFO" => 20, "ISOMO" => 30, "ISONE" => 40, _ => 0 };
        if (tone == 0) return null;
        var image = new RgbaImage(8, 8);
        for (int i = 0; i < image.Pixels.Length; i += 4)
            (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]) = (tone, tone, tone, 255);
        return image;
    }

    private static MapLooks Looks()
    {
        var tiles = new Tile[Size, Size];
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                tiles[x, y] = new Tile((byte)((x == 0 ? (int)Ground.Water : (int)Ground.Plain) << 3 | 5));  // раса 5 — нейтралы
        var map = new MapState
        {
            Size = Size, Tiles = tiles, Stacks = new Dictionary<string, MapStack>(), Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(), Objects = new Dictionary<string, MapObject>(),
            Plan = new Dictionary<(int, int), IReadOnlyList<string>>(), Fog = new Dictionary<string, bool[,]>(), Turn = 1,
        };
        return new MapLooks { Map = map, TreeImage = new byte[Size, Size], TreeGroup = new byte[Size, Size] };
    }

    [Fact]
    public void Raster_IsFourByTwoCellsAndPointsAreCentred()
    {
        var mini = new MiniMap(Looks(), Art);

        Assert.Equal((4 * Size, 2 * Size), (mini.Width, mini.Height));
        Assert.Equal((mini.Width / 2, 0), mini.PointOf(0, 0));
        Assert.Equal(((2 * (5 - 3)) + (mini.Width / 2), 8), mini.PointOf(5, 3));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 3)]
    [InlineData(7, 7)]
    public void CellAt_InvertsPointOf(int x, int y)
    {
        var mini = new MiniMap(Looks(), Art);

        var (px, py) = mini.PointOf(x, y);

        Assert.Equal((x, y), mini.CellAt(px, py));
    }

    /// <summary>Вода красится всегда, суша — только открытая, скрытая клетка получает туман поверх.</summary>
    [Fact]
    public void Render_DrawsWaterAlwaysAndFogOverHiddenLand()
    {
        var looks = Looks();
        var mini = new MiniMap(looks, Art, (x, _) => x >= 4);

        var image = mini.Render();

        byte At(int cx, int cy)
        {
            var (px, py) = mini.PointOf(cx, cy);
            return image.Pixels[image.Offset(px + 1, py)];   // точка (1,0) блока
        }

        Assert.Equal(40, At(2, 2));   // открытая суша — узор расы
        Assert.Equal(20, At(5, 2));   // скрытая суша — туман
        Assert.Equal(10, At(0, 2));   // вода открыта
    }
}
