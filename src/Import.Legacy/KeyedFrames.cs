using System.Security.Cryptography;

namespace D2.Data.Import;

/// <summary>Запись кадра графики с вырезанным ключом — перенос <c>tools/ui-assets/keyed_webp.py</c>, общий для интерфейса и графики мира.
/// Пурпур игры (всё, что в RGB565 движка равно 0xF81F) становится прозрачной чёрной точкой; хеш описи — первые 16 знаков sha256 от точек
/// RGBA холста (не от байтов файла: опись не зависит от кодека); кадр мира пишется только занятой частью, если она не больше половины
/// холста (правило рантайма <c>GfxTextures.Trim</c>, Г19 <c>docs/perf-log.md</c>).</summary>
public static class KeyedFrames
{
    public const string Ext = ".webp";
    public const int ShaLength = 16;

    /// <summary>Вырезать ключ на месте: точки ключа — (0, 0, 0, 0). Возвращает их число.</summary>
    public static int CutKey(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var p = image.Pixels;
        int keyed = 0;
        for (int i = 0; i < p.Length; i += 4)
        {
            if (p[i] >> 3 != 31 || p[i + 1] >> 2 != 0 || p[i + 2] >> 3 != 31) continue;
            p[i] = p[i + 1] = p[i + 2] = p[i + 3] = 0;
            keyed++;
        }
        return keyed;
    }

    /// <summary>Хеш описи: 16 знаков sha256 от байтов RGBA строка за строкой.</summary>
    public static string PixelSha(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Convert.ToHexStringLower(SHA256.HashData(image.Pixels))[..ShaLength];
    }

    /// <summary>Занятый прямоугольник (альфа не 0), как <c>Image.get_used_rect</c> Godot; пустой кадр — одна точка в углу.</summary>
    public static (int X, int Y, int W, int H) UsedRect(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++)
        {
            int row = y * image.Width * 4;
            for (int x = 0; x < image.Width; x++)
            {
                if (image.Pixels[row + (x * 4) + 3] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        return maxX < 0 ? (0, 0, 1, 1) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Что записать от холста: занятый прямоугольник, если он не больше половины площади, иначе <c>null</c> — холст целиком.</summary>
    public static (int X, int Y, int W, int H)? TrimBox(RgbaImage image)
    {
        var (x, y, w, h) = UsedRect(image);
        return (long)w * h * 2 > (long)image.Width * image.Height ? null : (x, y, w, h);
    }

    /// <summary>Холст из записанной части: часть в углу <paramref name="at"/>, вокруг — нулевые точки.</summary>
    public static RgbaImage Restore(RgbaImage part, (int X, int Y) at, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(part);
        var canvas = new RgbaImage(width, height, new byte[width * height * 4]);
        canvas.Paste(part, 0, 0, part.Width, part.Height, at.X, at.Y);
        return canvas;
    }

    /// <summary>Подготовить кадр к записи: вырезать ключ, при <paramref name="trim"/> — взять занятую часть. Возвращает то, что пишется в файл,
    /// и итог описи (холст, хеш холста, точек ключа, угол части или <c>null</c> — без обрезки).</summary>
    public static (RgbaImage Part, FrameDone Done) Prepare(string rel, RgbaImage image, bool trim)
    {
        ArgumentNullException.ThrowIfNull(image);
        int keyed = CutKey(image);
        (int, int)? at = null;
        var part = image;
        var canvas = image;
        if (trim)
        {
            var box = TrimBox(image);
            at = box is { } b ? (b.X, b.Y) : (0, 0);
            if (box is { } r)
            {
                part = image.Crop(r.X, r.Y, r.W, r.H);
                canvas = Restore(part, (r.X, r.Y), image.Width, image.Height);
            }
        }
        return (part, new FrameDone(rel, image.Width, image.Height, PixelSha(canvas), keyed, at));
    }
}

/// <summary>Записанный кадр: путь от корня выхода, холст, хеш точек холста, сколько было точек ключа, угол записанной части в холсте
/// (<c>null</c> — записан весь холст без обрезки).</summary>
public sealed record FrameDone(string Rel, int Width, int Height, string Sha, int Keyed, (int X, int Y)? At);
