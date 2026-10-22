using LastEdict.Data.Gfx;

namespace LastEdict.Data.Tests;

/// <summary>Сверка карты со снимком (<see cref="MapComposite"/>): смешение кадра по формулам DirectDraw и поиск сдвига снимка.</summary>
public class MapCompositeTests
{
    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b)
    {
        var image = new RgbaImage(w, h);
        for (int i = 0; i < image.Pixels.Length; i += 4) (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]) = (r, g, b, 255);
        return image;
    }

    [Theory]
    [InlineData(GfxBlend.Opaque, 50)]
    [InlineData(GfxBlend.Half, 125)]      // ⌊200/2⌋ + ⌊50/2⌋
    [InlineData(GfxBlend.Add, 250)]
    [InlineData(GfxBlend.Subtract, 150)]
    public void Draw_BlendsLikeDirectDraw(GfxBlend blend, int expected)
    {
        var frame = Solid(4, 4, 200, 200, 200);
        var sprite = Solid(2, 2, 50, 50, 50);
        sprite.Pixels[3] = 0;   // прозрачная точка (0,0) — пропуск
        MapComposite.Draw(frame, sprite, 1, 1, blend);
        Assert.Equal(expected, frame.Pixels[frame.Offset(2, 2)]);
        Assert.Equal(200, frame.Pixels[frame.Offset(1, 1)]);   // прозрачная
        Assert.Equal(200, frame.Pixels[frame.Offset(0, 0)]);   // вне картинки
    }

    /// <summary>16 бит, как у снимка обёртки: канал расширяется повтором старших бит (у снимков ровно 32/64/32 значения), смешение —
    /// в единицах 565: половина 5-битных 25 и 6 — 12 + 3 = 15 → 123; сложение насыщается на 31 → 255.</summary>
    [Fact]
    public void Rgb565_ExpandsLikeWrapper_AndBlendsInUnits()
    {
        Assert.Equal((0, 255, 123, 255), (MapComposite.Expand565(0, 0), MapComposite.Expand565(31, 0), MapComposite.Expand565(15, 0),
                                          MapComposite.Expand565(63, 1)));
        Assert.Equal(130, MapComposite.Expand565(32, 1));   // 32·4 + 32/16
        var frame = Solid(2, 2, 200, 200, 200);   // 200 → 25 (5 бит), 50 (6 бит)
        MapComposite.To565(frame);
        Assert.Equal((206, 203), (frame.Pixels[0], frame.Pixels[1]));
        MapComposite.Draw(frame, Solid(1, 1, 50, 50, 50), 0, 0, GfxBlend.Half, rgb565: true);   // 50 → 6 (5 бит), 12 (6 бит)
        Assert.Equal((MapComposite.Expand565(12 + 3, 0), MapComposite.Expand565(25 + 6, 1)), (frame.Pixels[0], frame.Pixels[1]));
        MapComposite.Draw(frame, Solid(1, 1, 250, 250, 250), 1, 0, GfxBlend.Add, rgb565: true);
        Assert.Equal(255, frame.Pixels[frame.Offset(1, 0)]);
    }

    /// <summary>Маска интерфейса: точки в прямоугольниках не считаются; «виновник» — картинка, легшая на точку последней.</summary>
    [Fact]
    public void Mask_AndBlame()
    {
        var frame = Solid(20, 20, 10, 10, 10);
        var owner = new int[20 * 20];
        var sprite = new MapSprite("A", "S", false, 1, 4, 4, 2, 2, 1, 0, 0, "src");
        frame = MapComposite.Render(0, 0, frame, [sprite], _ => (Solid(2, 2, 90, 90, 90), GfxBlend.Opaque), owner: owner);
        var shot = Solid(20, 20, 10, 10, 10);   // картинки на снимке нет
        var area = (X: 0, Y: 0, Width: 20, Height: 20);
        var all = MapComposite.Score(frame, shot, area, 0, 0, 0);
        var masked = MapComposite.Score(frame, shot, area, 0, 0, 0, 1, [(4, 4, 2, 2)]);
        Assert.Equal((396.0 / 400, 1.0, 396), (all.Equal, masked.Equal, masked.Points));
        Assert.Equal(4, Assert.Single(MapComposite.Blame(frame, shot, area, 0, all, owner)).Value);
    }

    /// <summary>Снимок — вырезка кадра со сдвигом (−7, 5) (не кратным шагу грубого поиска) на поле 32: сдвиг находится, точки равны.</summary>
    [Fact]
    public void Match_FindsShift_OfCrop()
    {
        const int margin = 32, w = 160, h = 120;
        var frame = new RgbaImage(w + (2 * margin), h + (2 * margin));
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                int o = frame.Offset(x, y);
                (frame.Pixels[o], frame.Pixels[o + 1], frame.Pixels[o + 2], frame.Pixels[o + 3]) =
                    ((byte)(x * 2), (byte)(y * 3), (byte)((x * x / 7 + y) & 255), 255);
            }
        var shot = new RgbaImage(w + 20, h + 10);
        var area = (X: 20, Y: 10, Width: w, Height: h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                frame.Pixels.AsSpan(frame.Offset(margin - 7 + x, margin + 5 + y), 4).CopyTo(shot.Pixels.AsSpan(shot.Offset(area.X + x, area.Y + y), 4));

        var match = MapComposite.Match(frame, shot, area, margin);

        Assert.Equal((-7, 5, 1.0, 0.0), (match.Dx, match.Dy, match.Equal, match.MeanDiff));
        Assert.Equal(w * h, match.Points);
        Assert.All(MapComposite.Difference(frame, shot, area, margin, match).Pixels.Where((_, i) => i % 4 != 3), p => Assert.Equal(0, p));
    }
}
