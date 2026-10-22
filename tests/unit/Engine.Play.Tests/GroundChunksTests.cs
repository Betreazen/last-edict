using LastEdict.Data.Gfx;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Земля кусками (<see cref="GroundChunks"/>): кусок совпадает с той же областью, собранной целиком, куски
/// привязаны к миру, кэш не пересобирает и вытесняет самый давний.</summary>
public class GroundChunksTests
{
    private static GroundComposer Composer(int size = 6)
    {
        var cells = new GroundType[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                cells[x, y] = (x + y) % 3 == 0 ? GroundType.Water : x < 3 ? GroundType.Human : GroundType.Neutral;
        return new GroundComposer(cells, 7746743, new GroundComposerTests.FakeArt());
    }

    [Fact]
    public void Chunk_EqualsSameAreaRenderedWhole()
    {
        var composer = Composer();
        var chunks = new GroundChunks(composer, chunkSize: 128);
        var whole = composer.Render(-256, 0, 512, 256);

        var chunk = chunks.Get((-1, 1));  // мир x −128..0, y 128..256

        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                int a = chunk.Offset(x, y), b = whole.Offset(x + 128, y + 128);
                Assert.True(chunk.Pixels.AsSpan(a, 4).SequenceEqual(whole.Pixels.AsSpan(b, 4)), $"точка ({x},{y})");
            }
    }

    /// <summary>Кэш кусками (<see cref="GroundChunks.Refresh"/>): туман не менялся — все куски остаются; скрылся угол карты — забыт
    /// только кусок с его чёрными ромбами, а пересобранный совпадает с картинкой, собранной заново с нуля.</summary>
    [Fact]
    public void Refresh_KeepsUnchangedChunks_RebuildsOnlyAffected()
    {
        var hidden = new bool[12, 12];
        var cells = new GroundType[12, 12];
        for (int x = 0; x < 12; x++)
            for (int y = 0; y < 12; y++)
                cells[x, y] = (x * y) % 5 == 0 ? GroundType.Water : GroundType.Neutral;
        var composer = new GroundComposer(cells, 7746743, new GroundComposerTests.FakeArt()) { Hidden = (x, y) => hidden[x, y] };
        var chunks = new GroundChunks(composer, chunkSize: 128);
        var (left, top, width, height) = GroundChunks.MapBounds(12);
        var all = chunks.Covering(left, top, width, height).ToList();
        foreach (var key in all) chunks.Get(key);

        Assert.Equal(all.Count, chunks.Refresh().Count);   // ничего не изменилось

        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
                hidden[x, y] = true;   // клетки (0..2, 0..2) скрыты вместе с соседями — чёрные ромбы
        var kept = chunks.Refresh();
        Assert.InRange(kept.Count, 1, all.Count - 1);
        int composed = chunks.Composed;
        foreach (var key in all)
        {
            var image = chunks.Get(key);
            var (l, t) = chunks.Origin(key);
            var fresh = new GroundComposer(cells, 7746743, new GroundComposerTests.FakeArt()) { Hidden = (x, y) => hidden[x, y] }
                .Render(l, t, 128, 128);
            Assert.True(image.Pixels.AsSpan().SequenceEqual(fresh.Pixels), $"кусок {key}");
        }
        Assert.Equal(all.Count - kept.Count, chunks.Composed - composed);
    }

    [Fact]
    public void Covering_IsWorldAligned_IncludingNegativeCoordinates()
    {
        var chunks = new GroundChunks(Composer(), chunkSize: 256);

        var list = chunks.Covering(-300, 10, 400, 250).ToList();

        Assert.Equal([(-2, 0), (-1, 0), (0, 0), (-2, 1), (-1, 1), (0, 1)], list);
        Assert.Equal((-512, 256), chunks.Origin((-2, 1)));
    }

    [Fact]
    public void Cache_ReturnsWithoutComposing_AndEvictsLeastRecent()
    {
        var chunks = new GroundChunks(Composer(), chunkSize: 64, capacity: 2);

        var first = chunks.Get((0, 0));
        chunks.Get((1, 0));
        Assert.Same(first, chunks.Get((0, 0)));  // из кэша и стал самым свежим
        chunks.Get((2, 0));                      // вытесняет (1,0)

        Assert.Equal(3, chunks.Composed);
        Assert.Equal(2, chunks.Cached);
        Assert.True(chunks.IsCached((0, 0)));
        Assert.False(chunks.IsCached((1, 0)));
    }

    /// <summary>Каждая точка ромба клетки по таблице строк движка (<see cref="GroundGeometry.Row"/>) переводится обратно
    /// в эту клетку, в том числе с отрицательными мировыми координатами.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 2)]
    [InlineData(1, 9)]
    public void CellAt_InvertsCellDiamond(int x, int y)
    {
        var (cx, cy) = GroundGeometry.CellOrigin(x, y);
        for (int r = 0; r < GroundGeometry.CellHeight; r++)
        {
            var (x0, width) = GroundGeometry.Row(r);
            for (int c = x0; c < x0 + width; c++)
                Assert.Equal((x, y), GroundGeometry.CellAt(cx + c + 0.5, cy + r + 0.5));
        }
    }

    /// <summary>Клетка, скрытая со всеми соседями, — чёрный ромб; скрытая, но с открытым соседом, — обычная земля.</summary>
    [Fact]
    public void Hidden_BlacksOutOnlyFullyHiddenCells()
    {
        var cells = new GroundType[6, 6];
        var composer = new GroundComposer(cells, 1, new GroundComposerTests.FakeArt()) { Hidden = (x, y) => (x, y) != (5, 5) };
        var plain = new GroundComposer(cells, 1, new GroundComposerTests.FakeArt());

        uint Center(GroundComposer c, int x, int y)
        {
            var (cx, cy) = GroundGeometry.CellOrigin(x, y);
            var img = c.Render(cx + 32, cy + 16, 1, 1);
            return BitConverter.ToUInt32(img.Pixels, 0);
        }

        Assert.Equal(0xFF000000u, Center(composer, 2, 2));
        Assert.Equal(Center(plain, 4, 4), Center(composer, 4, 4));   // сосед (5,5) открыт
        Assert.NotEqual(0xFF000000u, Center(plain, 2, 2));
    }

    [Fact]
    public void MapBounds_CoversDiamondOfMap()
    {
        Assert.Equal((-(48 - 1) * 32, 0, 48 * 64, 48 * 32), GroundChunks.MapBounds(48));
    }
}
