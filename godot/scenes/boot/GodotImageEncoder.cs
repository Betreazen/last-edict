using System;
using LastEdict.Data.Import;
using Godot;

namespace LastEdict.Godot;

/// <summary>Кодек импорта на движке: WebP без потерь (<c>Image.SaveWebpToBuffer(lossy: false)</c> — lossless и exact: точки, в том числе RGB под
/// нулевой альфой, после разбора те же бит в бит; проверено прогоном 21.10.2026). Картинки — свои на каждый вызов, так что зовётся из потоков
/// импорта параллельно.</summary>
public sealed class GodotImageEncoder : IImageEncoder
{
    public byte[] EncodeWebpLossless(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var godot = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels);
        return godot.SaveWebpToBuffer(false);
    }

    public RgbaImage? DecodeWebp(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var godot = new Image();
        if (godot.LoadWebpFromBuffer(data) != Error.Ok) return null;
        if (godot.GetFormat() != Image.Format.Rgba8) godot.Convert(Image.Format.Rgba8);
        return new RgbaImage(godot.GetWidth(), godot.GetHeight(), godot.GetData());
    }
}
