using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Content;

/// <summary>Картинка сущности мода «папка = сущность»: файл каталога (<c>icon.png</c>), архив графики игры, приставка к имени кадра и
/// логический размер — тот, что у картинок оригинала этого места (значок постройки — 77×74).</summary>
public sealed record EntityArtSlot(string Kind, string File, string Archive, string Suffix, int W, int H, string Title)
{
    /// <summary>Имя архива без расширения — так к нему обращаются кадры (<c>IconBld:…</c>).</summary>
    public string ArchiveName => Path.GetFileNameWithoutExtension(Archive);
}

/// <summary>
/// Картинки сущностей мода (Студия 2, <c>docs/content-schema.md</c> §10): файл каталога сущности встаёт в архив графики игры под именем
/// графики сущности (<see cref="ArtNames"/>) — окна находят его без описи. Картинка крупнее логического размера в целое число раз — HD
/// (<see cref="GfxImage.Scale"/>), как у остальной графики: место и раскладка — по логическому размеру. Студия вписывает выбранную картинку
/// в такой размер сама (<see cref="Fit"/>).
/// </summary>
public static class EntityArt
{
    /// <summary>Наибольший множитель HD картинки сущности.</summary>
    public const int MaxScale = 4;

    public static IReadOnlyList<EntityArtSlot> Slots { get; } =
    [
        new("building", EntityFolders.IconFile, "IconBld.ff", "", 77, 74, "значок"),
    ];

    public static IEnumerable<EntityArtSlot> Of(string kind) => Slots.Where(s => s.Kind == kind);

    public static EntityArtSlot? Find(string kind, string file) =>
        Slots.FirstOrDefault(s => s.Kind == kind && s.File.Equals(file, StringComparison.OrdinalIgnoreCase));

    /// <summary>Имя кадра картинки сущности в архиве (<c>TOWERS:BUILDING/SPIRE</c>).</summary>
    public static string FrameName(string id, EntityArtSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return ArtNames.Of(id) + slot.Suffix;
    }

    /// <summary>Множитель файла <paramref name="w"/>×<paramref name="h"/>: целый, одинаковый по сторонам, 1…<see cref="MaxScale"/>; иначе null
    /// (картинка не того размера — показывается как есть, проверка мода предупреждает).</summary>
    public static int? ScaleOf(EntityArtSlot slot, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (w <= 0 || h <= 0 || w % slot.W != 0 || h % slot.H != 0) return null;
        int k = w / slot.W;
        return k == h / slot.H && k <= MaxScale ? k : null;
    }

    /// <summary>Размер, в который Студия вписывает выбранную картинку <paramref name="w"/>×<paramref name="h"/>: логический × k, где k —
    /// сколько целых раз картинка покрывает логический размер (1…<see cref="MaxScale"/>; мельче логического — 1).</summary>
    public static (int W, int H, int Scale) Fit(EntityArtSlot slot, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(slot);
        int k = Math.Clamp(Math.Min(w / slot.W, h / slot.H), 1, MaxScale);
        return (slot.W * k, slot.H * k, k);
    }

    /// <summary>Размер PNG по заголовку (IHDR); не PNG — 0×0.</summary>
    public static (int W, int H) PngSize(ReadOnlySpan<byte> head)
    {
        if (head.Length < 24 || !IsPng(head)) return (0, 0);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..]));
    }

    /// <summary>Начало файла — подпись PNG.</summary>
    public static bool IsPng(ReadOnlySpan<byte> head) =>
        head.Length >= 8 && head[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
}
