namespace LastEdict.Data.Gfx;

/// <summary>Насколько кадр порта совпал со снимком игры: сдвиг снимка относительно кадра, доля точек точно равных и равных с
/// допуском <see cref="MapComposite.Tolerance"/> по каналу, средняя разница по каналу (0…255) и сколько точек сравнено.</summary>
public sealed record ShotMatch(int Dx, int Dy, double Equal, double Near, double MeanDiff, int Points);

/// <summary>
/// Попиксельная сверка карты со снимком игры (<c>docs/ai-screen.md</c> §14): кадр карты собирается без окна — земля
/// (<see cref="GroundComposer"/>) и картинки сцены по порядку отрисовки со смешением кадра (<see cref="GfxBlend"/>, DirectDraw: половина —
/// <c>⌊d/2⌋+⌊s/2⌋</c>, сложение и вычитание с насыщением), — и сравнивается с областью вида на снимке. Где именно на снимке стоит
/// камера, заранее не известно: сдвиг ищется перебором (грубо по сетке, затем точно вокруг лучшего).
/// </summary>
public static class MapComposite
{
    /// <summary>Допуск «близко» по каналу: снимок DirectDraw идёт через RGB565 — младшие биты каналов теряются.</summary>
    public const int Tolerance = 8;

    private const int CoarseStep = 4;

    /// <summary>Сдвиг канала к 565: R и B — 5 бит (сдвиг 3), G — 6 бит (сдвиг 2).</summary>
    private static readonly int[] Shift565 = [3, 2, 3];

    /// <summary>Канал 565 в 8 бит так, как расширяет обёртка DisciplesGL снимок (<c>OpenDraw::TakeSnapshot</c>): повтор старших бит —
    /// <c>v5·8 + v5/4</c>, <c>v6·4 + v6/16</c>. Проверено на снимках 27.09: у каналов ровно 32/64/32 значения этого вида.</summary>
    public static byte Expand565(int value, int channel) =>
        channel == 1 ? (byte)((value << 2) | (value >> 4)) : (byte)((value << 3) | (value >> 2));

    /// <summary>Картинка, приведённая к 16 битам экрана движка (DirectDraw 565) и обратно — как её покажет снимок.</summary>
    public static void To565(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var p = image.Pixels;
        for (int o = 0; o < p.Length; o += 4)
            for (int ch = 0; ch < 3; ch++)
                p[o + ch] = Expand565(p[o + ch] >> Shift565[ch], ch);
    }

    /// <summary>Кадр мира <c>[left, left+width) × [top, top+height)</c>: земля того же прямоугольника и картинки поверх.
    /// <paramref name="art"/> — картинка и смешение картинки сцены (null — нет файла, пропуск).</summary>
    public static RgbaImage Render(int left, int top, RgbaImage ground, IReadOnlyList<MapSprite> sprites,
                                   Func<MapSprite, (RgbaImage? Image, GfxBlend Blend)> art, bool rgb565 = false, int[]? owner = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(art);
        var frame = new RgbaImage(ground.Width, ground.Height);
        ground.Pixels.CopyTo(frame.Pixels, 0);
        if (rgb565) To565(frame);
        if (owner is not null) Array.Fill(owner, -1);
        for (int i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            var (image, blend) = art(sprite);
            if (image is null || blend == GfxBlend.Hidden) continue;
            Draw(frame, image, sprite.X - left, sprite.Y - top, blend, rgb565, owner is null ? null : (owner, i));
        }
        return frame;
    }

    /// <summary>Картинка в кадр с точки (<paramref name="x"/>, <paramref name="y"/>); прозрачные точки пропускаются. <paramref name="rgb565"/> —
    /// как у движка: точки в 565, смешение в единицах 565 (<c>0x678c7b</c>: <c>((d»1)&amp;0x7BEF)+((s»1)&amp;0x7BEF)</c>, насыщение — по
    /// 31/63/31), результат расширяется как у снимка (<see cref="Expand565"/>); кадр уже должен быть в 565 (<see cref="To565"/>).</summary>
    public static void Draw(RgbaImage frame, RgbaImage image, int x, int y, GfxBlend blend, bool rgb565 = false,
                            (int[] Map, int Index)? owner = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(image);
        int x0 = Math.Max(0, -x), y0 = Math.Max(0, -y);
        int x1 = Math.Min(image.Width, frame.Width - x), y1 = Math.Min(image.Height, frame.Height - y);
        var src = image.Pixels;
        var dst = frame.Pixels;
        for (int iy = y0; iy < y1; iy++)
            for (int ix = x0; ix < x1; ix++)
            {
                int s = image.Offset(ix, iy);
                if (src[s + 3] == 0) continue;
                int d = frame.Offset(x + ix, y + iy);
                for (int ch = 0; ch < 3; ch++)
                {
                    int shift = rgb565 ? Shift565[ch] : 0, max = 255 >> shift;
                    int dv = dst[d + ch] >> shift, sv = src[s + ch] >> shift;
                    int v = blend switch
                    {
                        GfxBlend.Half => (dv >> 1) + (sv >> 1),
                        GfxBlend.Add => Math.Min(max, dv + sv),
                        GfxBlend.Subtract => Math.Max(0, dv - sv),
                        _ => sv,
                    };
                    dst[d + ch] = rgb565 ? Expand565(v, ch) : (byte)v;
                }
                dst[d + 3] = 255;
                if (owner is { } o) o.Map[d / 4] = o.Index;   // чья точка — для разбора расхождений
            }
    }

    /// <summary>Лучший сдвиг области снимка (<paramref name="area"/>) внутри кадра: кадр больше области на поле
    /// <paramref name="margin"/> с каждой стороны, сдвиг (0, 0) — область ровно в середине. Сначала каждая 4-я точка по сетке
    /// сдвигов с шагом 4, затем все точки вокруг лучшего.</summary>
    public static ShotMatch Match(RgbaImage frame, RgbaImage shot, (int X, int Y, int Width, int Height) area, int margin,
                                  IReadOnlyList<(int X, int Y, int Width, int Height)>? mask = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(shot);
        if (frame.Width < area.Width + (2 * margin) || frame.Height < area.Height + (2 * margin))
            throw new ArgumentException("кадр меньше области снимка с полем", nameof(frame));
        (int Dx, int Dy, double Cost) best = (0, 0, double.MaxValue);
        for (int dy = -margin; dy <= margin; dy += CoarseStep)
            for (int dx = -margin; dx <= margin; dx += CoarseStep)
            {
                double cost = Score(frame, shot, area, margin, dx, dy, CoarseStep, mask).MeanDiff;
                if (cost < best.Cost) best = (dx, dy, cost);
            }
        (int Dx, int Dy, double Cost) fine = best;
        for (int dy = best.Dy - CoarseStep; dy <= best.Dy + CoarseStep; dy++)
            for (int dx = best.Dx - CoarseStep; dx <= best.Dx + CoarseStep; dx++)
            {
                if (Math.Abs(dx) > margin || Math.Abs(dy) > margin) continue;
                double cost = Score(frame, shot, area, margin, dx, dy, 1, mask).MeanDiff;
                if (cost < fine.Cost) fine = (dx, dy, cost);
            }
        return Score(frame, shot, area, margin, fine.Dx, fine.Dy, 1, mask);
    }

    /// <summary>Сравнение при сдвиге (<paramref name="dx"/>, <paramref name="dy"/>) по каждой <paramref name="step"/>-й точке; точки в
    /// прямоугольниках <paramref name="mask"/> (координаты области: интерфейс поверх карты) не считаются.</summary>
    public static ShotMatch Score(RgbaImage frame, RgbaImage shot, (int X, int Y, int Width, int Height) area, int margin,
                                  int dx, int dy, int step = 1, IReadOnlyList<(int X, int Y, int Width, int Height)>? mask = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(shot);
        long diff = 0;
        int points = 0, equal = 0, near = 0;
        for (int y = 0; y < area.Height; y += step)
            for (int x = 0; x < area.Width; x += step)
            {
                if (mask is not null && Masked(mask, x, y)) continue;
                int s = shot.Offset(area.X + x, area.Y + y), f = frame.Offset(margin + dx + x, margin + dy + y);
                int worst = 0;
                for (int ch = 0; ch < 3; ch++)
                {
                    int d = Math.Abs(shot.Pixels[s + ch] - frame.Pixels[f + ch]);
                    diff += d;
                    worst = Math.Max(worst, d);
                }
                points++;
                if (worst == 0) equal++;
                if (worst <= Tolerance) near++;
            }
        return new ShotMatch(dx, dy, equal / (double)points, near / (double)points, diff / (3.0 * points), points);
    }

    /// <summary>Кто виноват в несовпавших точках: по каждой точке области, не равной снимку, — индекс картинки, легшей на неё последней
    /// (<paramref name="owner"/> из <see cref="Render"/>; −1 — земля), и сколько таких точек.</summary>
    public static IReadOnlyDictionary<int, int> Blame(RgbaImage frame, RgbaImage shot, (int X, int Y, int Width, int Height) area, int margin,
                                                      ShotMatch match, int[] owner,
                                                      IReadOnlyList<(int X, int Y, int Width, int Height)>? mask = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(owner);
        var counts = new Dictionary<int, int>();
        for (int y = 0; y < area.Height; y++)
            for (int x = 0; x < area.Width; x++)
            {
                if (mask is not null && Masked(mask, x, y)) continue;
                int s = shot.Offset(area.X + x, area.Y + y), f = frame.Offset(margin + match.Dx + x, margin + match.Dy + y);
                if (shot.Pixels.AsSpan(s, 3).SequenceEqual(frame.Pixels.AsSpan(f, 3))) continue;
                int who = owner[f / 4];
                counts[who] = counts.GetValueOrDefault(who) + 1;
            }
        return counts;
    }

    private static bool Masked(IReadOnlyList<(int X, int Y, int Width, int Height)> mask, int x, int y)
    {
        foreach (var r in mask)
            if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return true;
        return false;
    }

    /// <summary>Картинка разницы для глаз: точка — модуль разности по каналу, усиленный вчетверо.</summary>
    public static RgbaImage Difference(RgbaImage frame, RgbaImage shot, (int X, int Y, int Width, int Height) area, int margin, ShotMatch match)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(match);
        var diff = new RgbaImage(area.Width, area.Height);
        for (int y = 0; y < area.Height; y++)
            for (int x = 0; x < area.Width; x++)
            {
                int s = shot.Offset(area.X + x, area.Y + y), f = frame.Offset(margin + match.Dx + x, margin + match.Dy + y), o = diff.Offset(x, y);
                for (int ch = 0; ch < 3; ch++)
                    diff.Pixels[o + ch] = (byte)Math.Min(255, 4 * Math.Abs(shot.Pixels[s + ch] - frame.Pixels[f + ch]));
                diff.Pixels[o + 3] = 255;
            }
        return diff;
    }
}
