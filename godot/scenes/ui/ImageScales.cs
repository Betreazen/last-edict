using System.IO;
using System.Runtime.CompilerServices;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Множитель HD-картинок (Р19, <c>scale</c> в описях): файл крупнее кадра в <c>scale</c> раз, а раскладка, попадания и сложение кадров
/// считаются в логических точках. Множитель живёт рядом с картинкой (<see cref="ConditionalWeakTable{TKey,TValue}"/>: не держит её),
/// текстура получает логический размер (<see cref="Texture"/>, <c>SetSizeOverride</c>) — спрайт рисуется в логическом месте, а точки берёт
/// из полной картинки. Кто складывает картинки по точкам, берёт логическую копию (<see cref="ToLogical"/>). Сжатая копия файла (DDS BC7 с
/// mip-уровнями рядом с PNG, <c>godot/bench/hd_compress.gd</c>) идёт в текстуру вместо несжатой картинки — в видеопамяти ≈ ×0,33.
/// </summary>
public static class ImageScales
{
    private sealed record Info(int Scale, string? Dds);

    /// <summary>Текстур, созданных из сжатой копии (DDS), — для проверок.</summary>
    public static int FromDds { get; private set; }

    private static readonly ConditionalWeakTable<Image, Info> Scales = new();

    /// <summary>Множитель картинки; не задан — 1.</summary>
    public static int Of(Image image) => image is not null && Scales.TryGetValue(image, out var info) ? info.Scale : 1;

    /// <summary>Запомнить множитель картинки (больше 1) и вернуть её; <paramref name="file"/> — файл, из которого она прочитана как есть:
    /// рядом с ним ищется сжатая копия <c>.dds</c> для текстуры.</summary>
    public static Image With(Image image, int scale, string? file = null)
    {
        if (image is not null && scale > 1) Scales.AddOrUpdate(image, new Info(scale, file is null ? null : Dds(file)));
        return image!;
    }

    /// <summary>Картинку изменили (подкраска, маска): текстура — из её точек, а не из сжатой копии файла; множитель остаётся.</summary>
    public static Image Detach(Image image)
    {
        if (image is not null && Scales.TryGetValue(image, out var info) && info.Dds is not null) Scales.AddOrUpdate(image, info with { Dds = null });
        return image!;
    }

    /// <summary>Сжатая копия файла (<c>.dds</c> рядом, не старше файла); нет — null.</summary>
    public static string? Dds(string file)
    {
        string dds = Path.ChangeExtension(file, ".dds");
        return File.Exists(dds) && File.GetLastWriteTimeUtc(dds) >= File.GetLastWriteTimeUtc(file) ? dds : null;
    }

    /// <summary>Картинка сжатой копии (BC7 с mip-уровнями) или null, если не читается.</summary>
    public static Image? LoadDds(string dds)
    {
        var image = new Image();
        return image.LoadDdsFromBuffer(File.ReadAllBytes(dds)) == Error.Ok ? image : null;
    }

    /// <summary>Логический размер картинки — в точках кадра.</summary>
    public static Vector2I Logical(Image image)
    {
        int scale = Of(image);
        var size = image.GetSize();
        return scale <= 1 ? size : new Vector2I(Mathf.Max(1, size.X / scale), Mathf.Max(1, size.Y / scale));
    }

    /// <summary>Картинка в логических точках: без множителя — она сама, иначе уменьшенная копия (для масок, сложения кадров, проверок точек;
    /// показ идёт текстурой полной картинки, <see cref="Texture"/>).</summary>
    public static Image ToLogical(Image image)
    {
        if (Of(image) <= 1) return image;
        var copy = (Image)image.Duplicate();
        var size = Logical(image);
        copy.Resize(size.X, size.Y, Image.Interpolation.Nearest);   // ближайшим: край альфы чёткий — маски и попадания (A > 0) не растут ореолом
        return copy;
    }

    /// <summary>Картинка в множителе <paramref name="scale"/> (для сложения с картинкой этого множителя: маска, значок, рамка): свой
    /// множитель — она сама, иначе копия, растянутая из логических точек (<paramref name="smooth"/> — билинейно, иначе ближайшим).</summary>
    public static Image AtScale(Image image, int scale, bool smooth = true)
    {
        System.ArgumentNullException.ThrowIfNull(image);
        if (Of(image) == scale) return image;
        var logical = Logical(image);
        var copy = (Image)image.Duplicate();
        copy.Resize(logical.X * scale, logical.Y * scale, smooth ? Image.Interpolation.Bilinear : Image.Interpolation.Nearest);
        return With(copy, scale);
    }

    /// <summary>HD-картинка, текстура которой пойдёт из её точек (сжатой копии файла нет: собранная картинка — рамка с портретом, вырезка,
    /// портрет с ранами), получит mip-уровни (<see cref="Texture"/>): окно показывает её мельче полного размера.</summary>
    private static bool GetsMipmaps(Image image) =>
        Of(image) > 1 && !(Scales.TryGetValue(image, out var info) && info.Dds is not null) && !image.IsCompressed() && !image.HasMipmaps();

    /// <summary>Байт картинки вместе с mip-уровнями, которые ей добавит текстура (+⅓), — для предела кэша картинок (<see cref="UiArt"/>).</summary>
    public static long Bytes(Image image)
    {
        System.ArgumentNullException.ThrowIfNull(image);
        long size = image.GetDataSize();
        return GetsMipmaps(image) ? size + (size / 3) : size;
    }

    /// <summary>Текстура картинки: с множителем — логического размера (<c>SetSizeOverride</c>), точки — полные (сжатая копия файла, если
    /// она есть и картинку не меняли, иначе свои с mip-уровнями).</summary>
    public static ImageTexture Texture(Image image)
    {
        System.ArgumentNullException.ThrowIfNull(image);
        if (Scales.TryGetValue(image, out var info) && info.Dds is { } dds && LoadDds(dds) is { } packed)
        {
            FromDds++;
            var fromDds = ImageTexture.CreateFromImage(packed);
            packed.Dispose();   // BC7 с mip-уровнями уже в видеопамяти — копия на процессоре не ждёт сборщика мусора
            Mark(fromDds, Logical(image));
            return fromDds;
        }
        if (GetsMipmaps(image)) image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        if (Of(image) > 1) Mark(texture, Logical(image));
        return texture;
    }

    /// <summary>Метка HD-текстуры: по ней узел берёт сглаживание с mip-уровнями (<see cref="Filter"/>).</summary>
    public static readonly StringName HdMeta = "hd";

    /// <summary>HD-текстуры по обёртке C#: <see cref="Filter"/> зовётся на каждый спрайт карты и боя (тысячи при открытии карты), а
    /// <c>HasMeta</c> — вызов в движок; на EliteBook он стоил открытию карты +30 мс (Э5). Метка в движке остаётся — для обёртки, которую
    /// движок отдал заново.</summary>
    private static readonly ConditionalWeakTable<Texture2D, object> HdTextures = new();
    private static int _hdMarked;

    /// <summary>HD-текстура: логический размер и метка.</summary>
    public static void Mark(ImageTexture texture, Vector2I logical)
    {
        texture.SetSizeOverride(logical);
        MarkHd(texture);
    }

    /// <summary>Метка HD (сглаживание с mip-уровнями, <see cref="Filter"/>) — и вырезке HD-текстуры (<see cref="AtlasTexture"/>).</summary>
    public static void MarkHd(Texture2D texture)
    {
        texture.SetMeta(HdMeta, true);
        HdTextures.AddOrUpdate(texture, HdMeta);
        _hdMarked++;
    }

    /// <summary>Фильтр узла текстуры: HD — сглаживание с mip-уровнями (у HD при показе в окне меньше полного размера ближайший рябит),
    /// иначе — <paramref name="pixel"/> (пиксельная графика — «ближайший»). Без HD-текстур за запуск — без вызовов в движок.</summary>
    public static CanvasItem.TextureFilterEnum Filter(Texture2D? texture, CanvasItem.TextureFilterEnum pixel) =>
        texture is not null && _hdMarked > 0 && (HdTextures.TryGetValue(texture, out _) || texture.HasMeta(HdMeta))
            ? CanvasItem.TextureFilterEnum.LinearWithMipmaps : pixel;
}
