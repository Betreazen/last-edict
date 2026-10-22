using LastEdict.Data.Gfx;
using LastEdict.Rules.Map;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Сборка земли по разбору движка (<c>docs/ground-render.md</c>) на синтетической графике: в узоре каждая
/// точка кодирует свой тип, вариант и координаты в узоре — по готовой картинке видно, откуда она взята.</summary>
public class GroundComposerTests
{
    /// <summary>Узор: R = x в узоре (до 191), G = y, B = 16·тип + вариант. Маска: вес = номер маски·8. Берег: весь
    /// спрайт непрозрачен, цвет (0, номер, 250). Фон воды: (7, 7, 7).</summary>
    internal sealed class FakeArt(int variants = 4, int maskWeight = 255) : IGroundArt
    {
        // Картинки рисуются один раз и отдаются из кэша, как у настоящей графики (GroundArt): композитор спрашивает узор
        // и фон воды на каждую точку, и без кэша один тест кусков шёл больше двух минут.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(char Kind, int A, int B), RgbaImage> _cache = new();

        public int PatternVariants(GroundType type) => type == GroundType.Black ? 1 : variants;

        public RgbaImage Pattern(GroundType type, int variant) => _cache.GetOrAdd(('p', (int)type, variant), _ =>
        {
            var img = new RgbaImage(192, 192);
            for (int y = 0; y < 192; y++)
                for (int x = 0; x < 192; x++)
                    Put(img, x, y, (byte)x, (byte)y, (byte)((16 * (int)type) + variant));
            return img;
        });

        public int MaskVariants(int mask) => 1;

        public RgbaImage? Mask(int mask, int variant) => _cache.GetOrAdd(('m', mask, variant), _ =>
        {
            var img = new RgbaImage(64, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 64; x++) Put(img, x, y, (byte)maskWeight, 0, 0);
            return img;
        });

        public int ShoreVariants(int mask) => 1;

        public RgbaImage? Shore(int mask, int variant) => _cache.GetOrAdd(('s', mask, variant), _ =>
        {
            var img = new RgbaImage(64, 32);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 64; x++) Put(img, x, y, 0, (byte)mask, 250);
            return img;
        });

        public RgbaImage? WaterBackground => _cache.GetOrAdd(('w', 0, 0), _ =>
        {
            var img = new RgbaImage(128, 128);
            for (int i = 0; i < 128 * 128; i++) Put(img, i % 128, i / 128, 7, 7, 7);
            return img;
        });

        private static void Put(RgbaImage img, int x, int y, byte r, byte g, byte b)
        {
            int o = img.Offset(x, y);
            (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2], img.Pixels[o + 3]) = (r, g, b, 255);
        }
    }

    private static GroundType[,] Map(int size, GroundType fill)
    {
        var cells = new GroundType[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++) cells[x, y] = fill;
        return cells;
    }

    private static (byte R, byte G, byte B) At(RgbaImage img, int x, int y)
    {
        int o = img.Offset(x, y);
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2]);
    }

    /// <summary>Ромб 64×32 по таблице строк движка: 2, 6, … 58, 62, 62, 58, … 2 точки. Ромбы соседних клеток
    /// покрывают плоскость без щелей и без наложений — проверено на окне 256×128 внутри карты.</summary>
    [Fact]
    public void Diamonds_TilePlaneWithoutGapsOrOverlaps()
    {
        Assert.Equal((30, 2), GroundGeometry.Row(0));
        Assert.Equal((0, 62), GroundGeometry.Row(15));
        Assert.Equal((0, 62), GroundGeometry.Row(16));
        Assert.Equal((30, 2), GroundGeometry.Row(31));

        var hits = new int[256, 128];
        for (int x = 0; x < 20; x++)
            for (int y = 0; y < 20; y++)
            {
                var (cx, cy) = GroundGeometry.CellOrigin(x, y);
                for (int r = 0; r < 32; r++)
                {
                    var (x0, w) = GroundGeometry.Row(r);
                    for (int c = x0; c < x0 + w; c++)
                    {
                        int wx = cx + c, wy = cy + r - 160;
                        if (wx >= 0 && wx < 256 && wy >= 0 && wy < 128) hits[wx, wy]++;
                    }
                }
            }
        int gaps = 0, overlaps = 0;
        foreach (int h in hits)
            if (h == 0) gaps++;
            else if (h > 1) overlaps++;
        // У ромба ширина средней строки 62, а шаг — 64: в каждой паре строк у вершин остаётся столбец в две точки
        // между соседями по X; это место закрывает ромб следующего ряда. Итог — ни щелей, ни наложений.
        Assert.Equal((0, 0), (gaps, overlaps));
    }

    /// <summary>rand() MSVCRT: известная последовательность после srand(1) — 41, 18467, 6334.</summary>
    [Fact]
    public void MsvcrtRand_KnownSequence()
    {
        uint s = 1;
        Assert.Equal([41, 18467, 6334], new[] { GroundVariants.Rand(ref s), GroundVariants.Rand(ref s), GroundVariants.Rand(ref s) });
        var table = new GroundVariants(1);
        Assert.Equal(41 % 10, table.At(0, 0));
        Assert.Equal(18467 % 10, table.At(192, 0));
        Assert.Equal(41 % 10, table.At(48 * 192, 24 * 192)); // таблица зациклена 48×24 блока
    }

    /// <summary>Тип клетки: вода, горы — нейтральная земля, равнина и лес — по расе (<c>Lterrain</c>).</summary>
    [Theory]
    [InlineData(0 << 3 | 1, GroundType.Human)]
    [InlineData(1 << 3 | 4, GroundType.Undead)]
    [InlineData(3 << 3 | 1, GroundType.Water)]
    [InlineData(4 << 3 | 1, GroundType.Neutral)]
    [InlineData(0 << 3 | 6, GroundType.Elf)]
    [InlineData(0 << 3 | 2, GroundType.Dwarf)]
    public void TypeOf_FollowsGroundThenRace(int raw, GroundType expected) =>
        Assert.Equal(expected, GroundGeometry.TypeOf(new Tile((byte)raw)));

    /// <summary>Точка ромба берётся из узора по мировой координате: <c>((X + N·64) mod 192, (Y + N·32) mod 192)</c>,
    /// вариант — из таблицы по блоку.</summary>
    [Fact]
    public void Pattern_IsWorldAnchored()
    {
        const int n = 10;
        var composer = new GroundComposer(Map(n, GroundType.Human), 12345, new FakeArt());
        var (cx, cy) = GroundGeometry.CellOrigin(4, 3);
        var img = composer.Render(cx, cy, 64, 32);
        var (r, g, b) = At(img, 32, 16);
        Assert.Equal((cx + 32 + (n * 64)) % 192, r);
        Assert.Equal((cy + 16 + (n * 32)) % 192, g);
        int variant = new GroundVariants(12345).At(cx + (n * 64), cy + (n * 32)) % 4;
        Assert.Equal((16 * (int)GroundType.Human) + variant, b);
    }

    /// <summary>Границы: на клетку ложится только сосед старшего типа; рёбра — биты (−1,0), (0,−1), (+1,0), (0,+1);
    /// угол — только если оба его ребра свободны.</summary>
    [Fact]
    public void Borders_OnlySeniorNeighbours_EdgeAndCornerBits()
    {
        var cells = Map(5, GroundType.Neutral);
        cells[1, 2] = GroundType.Human;   // сосед (−1,0) клетки (2,2)
        cells[3, 3] = GroundType.Dwarf;   // сосед (+1,+1) — угол, его рёбра (+1,0) и (0,+1) свободны
        cells[2, 1] = GroundType.Water;   // младше нейтральной — на (2,2) не наползает
        var composer = new GroundComposer(cells, 1, new FakeArt());

        Assert.Equal([new GroundBorder(GroundType.Human, 0b0001, 0), new GroundBorder(GroundType.Dwarf, 0, 16 + 0b0100)],
                     composer.Borders(2, 2));
        Assert.DoesNotContain(composer.Borders(1, 2), b => b.Type < GroundType.Human);
        // У воды соседняя суша даёт берег.
        Assert.NotEqual((0, 0), composer.Shore(2, 1));
    }

    /// <summary>Смешение по маске: <c>dst + ⌊(src − dst)·m/256⌋</c>. Маска 128 — ровно середина между узором клетки и
    /// соседа (с округлением вниз).</summary>
    [Fact]
    public void Blend_HalfMask_IsMidwayRoundedDown()
    {
        var cells = Map(5, GroundType.Neutral);
        cells[1, 2] = GroundType.Human;
        var composer = new GroundComposer(cells, 7, new FakeArt(variants: 1, maskWeight: 128));
        var (cx, cy) = GroundGeometry.CellOrigin(2, 2);
        var img = composer.Render(cx, cy, 64, 32);
        var (_, _, b) = At(img, 32, 16);
        int own = 16 * (int)GroundType.Neutral, other = 16 * (int)GroundType.Human;
        Assert.Equal(own + (((other - own) * 128) >> 8), b);
    }

    /// <summary>Вода: фон по миру, у берега поверх — спрайт берега; суша накладывается на воду по маске.</summary>
    [Fact]
    public void Water_BackgroundThenShoreSprite()
    {
        var cells = Map(5, GroundType.Water);
        var composer = new GroundComposer(cells, 7, new FakeArt());
        var (cx, cy) = GroundGeometry.CellOrigin(2, 2);
        Assert.Equal((7, 7, 7), At(composer.Render(cx, cy, 64, 32), 32, 16));

        cells[1, 2] = GroundType.Human;
        var shore = new GroundComposer(cells, 7, new FakeArt(maskWeight: 0)).Render(cx, cy, 64, 32);
        Assert.Equal((0, 1, 250), At(shore, 32, 16)); // спрайт берега с маской рёбер 1
    }

    /// <summary>Анимированная вода: точки воды прозрачны (фон рисует вид), спрайт берега ложится, суша по маске на воду не
    /// смешивается.</summary>
    [Fact]
    public void AnimatedWater_TransparentExceptShoreSprite()
    {
        var cells = Map(5, GroundType.Water);
        var (cx, cy) = GroundGeometry.CellOrigin(2, 2);
        var open = new GroundComposer(cells, 7, new FakeArt()) { AnimatedWater = true }.Render(cx, cy, 64, 32);
        Assert.Equal(0, open.Pixels[open.Offset(32, 16) + 3]);

        cells[1, 2] = GroundType.Human;
        var shore = new GroundComposer(cells, 7, new FakeArt(maskWeight: 0)) { AnimatedWater = true }.Render(cx, cy, 64, 32);
        Assert.Equal((0, 1, 250), At(shore, 32, 16));   // берег — тот же спрайт, что и без анимации
        Assert.Equal(255, shore.Pixels[shore.Offset(32, 16) + 3]);
        var blended = new GroundComposer(cells, 7, new FakeArt(maskWeight: 255)) { AnimatedWater = true }.Render(cx, cy, 64, 32);
        int transparent = 0;
        for (int i = 3; i < blended.Pixels.Length; i += 4) if (blended.Pixels[i] == 0) transparent++;
        Assert.True(transparent > 0, "без берега точки воды остаются прозрачными и под маской суши");
    }

    /// <summary>За краем карты — чёрный узор (<c>BL_00</c>), у крайних клеток границ с ним нет.</summary>
    [Fact]
    public void OutsideMap_IsBlack_NoBordersAtEdge()
    {
        var composer = new GroundComposer(Map(3, GroundType.Human), 7, new FakeArt());
        Assert.Equal(GroundType.Black, composer.TypeAt(-1, 0));
        Assert.Empty(composer.Borders(0, 0));
        var (cx, cy) = GroundGeometry.CellOrigin(-1, 0);
        var (_, _, b) = At(composer.Render(cx, cy, 64, 32), 32, 16);
        Assert.Equal(16 * (int)GroundType.Black, b);
    }
}
