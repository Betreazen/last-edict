using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LastEdict.Data.Content;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Картинки сущностей мода в окне Студии: выбранный файл (PNG, JPG, WebP) вписывается в место картинки (<see cref="EntityArt.Fit"/>: логический
/// размер × целый множитель, HD — как у остальной графики) с обрезкой по середине, без искажения пропорций, и отдаётся байтами PNG — их
/// кладёт команда ядра. Текстуры для показа — по байтам документа (несохранённая картинка видна сразу).
/// </summary>
public static class StudioPictures
{
    /// <summary>Файлы, которые можно выбрать.</summary>
    public static readonly string[] Filters = ["*.png, *.jpg, *.jpeg, *.webp ; Картинки"];

    /// <summary>Картинка из файла, вписанная в место <paramref name="slot"/>; не читается — null и причина.</summary>
    public static (ImmutableArray<byte>? Png, string? Problem) FromFile(string path, EntityArtSlot slot)
    {
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty()) return (null, $"«{path}» не читается как картинка");
        return (Fit(image, slot), null);
    }

    /// <summary>Вписать картинку в место: размер <see cref="EntityArt.Fit"/>, масштаб «покрыть» и обрезка по середине.</summary>
    public static ImmutableArray<byte> Fit(Image image, EntityArtSlot slot)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(slot);
        image.Convert(Image.Format.Rgba8);
        var (w, h, _) = EntityArt.Fit(slot, image.GetWidth(), image.GetHeight());
        if (image.GetWidth() != w || image.GetHeight() != h)
        {
            double scale = Math.Max((double)w / image.GetWidth(), (double)h / image.GetHeight());
            int sw = Math.Max(w, (int)Math.Ceiling(image.GetWidth() * scale)), sh = Math.Max(h, (int)Math.Ceiling(image.GetHeight() * scale));
            image.Resize(sw, sh, Image.Interpolation.Lanczos);
            image = image.GetRegion(new Rect2I((sw - w) / 2, (sh - h) / 2, w, h));
        }
        return [.. image.SavePngToBuffer()];
    }

    private static readonly Dictionary<string, ImageTexture> _textures = new(StringComparer.Ordinal);

    /// <summary>Текстура по байтам PNG (кэш по хешу) с размером логического места, если картинка — его кратное (HD).</summary>
    public static ImageTexture? Texture(ImmutableArray<byte> png, EntityArtSlot? slot = null)
    {
        // Ключ — байты и место: одна и та же картинка с местом и без него — разные текстуры (размер логического места у HD).
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png.AsSpan())) + (slot is null ? "" : $"|{slot.Kind}/{slot.File}");
        if (_textures.TryGetValue(key, out var cached)) return cached;
        var image = new Image();
        if (image.LoadPngFromBuffer([.. png]) != Error.Ok) return null;
        image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        if (slot is not null && EntityArt.ScaleOf(slot, image.GetWidth(), image.GetHeight()) is > 1) texture.SetSizeOverride(new Vector2I(slot.W, slot.H));
        if (_textures.Count > 256) _textures.Clear();
        _textures[key] = texture;
        return texture;
    }
}
