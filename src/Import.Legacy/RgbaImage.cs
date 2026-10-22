namespace LastEdict.Data.Import;

/// <summary>Картинка импорта в памяти: RGBA по 8 бит, строка за строкой. Без движка — разбор, сборка кадров и хеши живут в обычном C#,
/// кодек записи (<see cref="IImageEncoder"/>) подставляет тот, кто запускает импорт.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Точки RGBA, <c>Width × Height × 4</c> байт.</summary>
    public byte[] Pixels { get; }

    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != width * height * 4)
            throw new ArgumentException($"картинка {width}×{height}: {pixels.Length} байт вместо {width * height * 4}", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Холст, залитый одним цветом.</summary>
    public static RgbaImage Filled(int width, int height, byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
        return new RgbaImage(width, height, pixels);
    }

    /// <summary>Скопировать прямоугольник <paramref name="w"/>×<paramref name="h"/> из <paramref name="source"/> (угол
    /// <paramref name="sx"/>, <paramref name="sy"/>) в точку <paramref name="dx"/>, <paramref name="dy"/> — как <c>paste(crop(...))</c> у PIL: точки
    /// вне источника — чёрные, вне холста — отбрасываются.</summary>
    public void Paste(RgbaImage source, int sx, int sy, int w, int h, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (int y = 0; y < h; y++)
        {
            int ty = dy + y, fy = sy + y;
            if (ty < 0 || ty >= Height) continue;
            for (int x = 0; x < w; x++)
            {
                int tx = dx + x, fx = sx + x;
                if (tx < 0 || tx >= Width) continue;
                int to = ((ty * Width) + tx) * 4;
                if (fx < 0 || fy < 0 || fx >= source.Width || fy >= source.Height)
                {
                    Pixels[to] = Pixels[to + 1] = Pixels[to + 2] = 0;
                    Pixels[to + 3] = 255;
                    continue;
                }
                int from = ((fy * source.Width) + fx) * 4;
                Buffer.BlockCopy(source.Pixels, from, Pixels, to, 4);
            }
        }
    }

    /// <summary>Часть картинки — прямоугольник внутри неё.</summary>
    public RgbaImage Crop(int x, int y, int w, int h)
    {
        var pixels = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(Pixels, (((y + row) * Width) + x) * 4, pixels, row * w * 4, w * 4);
        return new RgbaImage(w, h, pixels);
    }
}

/// <summary>Кодек записи картинок импорта. Графика порта — WebP без потерь (точки после разбора те же бит в бит, RGB под нулевой альфой
/// хранится как есть); кодировщик WebP есть у движка (Godot <c>Image.SaveWebpToBuffer</c>), а в обычном .NET его нет — поэтому его подставляет
/// тот, кто запускает импорт (игра, команда разработчика), а проверки без движка подставляют свой.</summary>
public interface IImageEncoder
{
    /// <summary>WebP без потерь (lossless, exact).</summary>
    byte[] EncodeWebpLossless(RgbaImage image);

    /// <summary>Разбор WebP обратно в RGBA — для выборочной сверки записанного; <c>null</c>, если кодек разбирать не умеет.</summary>
    RgbaImage? DecodeWebp(byte[] data);
}
