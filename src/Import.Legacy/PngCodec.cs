using System.Buffers.Binary;
using System.IO.Compression;

namespace D2.Data.Import;

/// <summary>PNG для импорта без движка: разбор картинок архивов игры и запись атласов шрифтов. Разбор — 8 бит на канал (серый, RGB, серый с
/// альфой, RGBA) и палитра 1/2/4/8 бит с <c>tRNS</c>, без чересстрочности: в архивах игры только RGB 8 бит и палитра 8 бит (замер всех 12 778
/// картинок <c>Interf</c> и <c>Imgs</c>, 21.10.2026). Итог — RGBA, как <c>convert("RGBA")</c> у PIL.</summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Разобрать PNG. <paramref name="keyIndex"/> — у палитровой картинки этот индекс становится маджентой (255, 0, 255) независимо от
    /// цвета в палитре (ключ прозрачности движка, <see cref="FfArchive.BaseImage"/>); <paramref name="opaque"/> — альфа 255 у всех точек (как
    /// <c>convert("RGB")</c> PIL: <c>tRNS</c> и альфа отбрасываются).</summary>
    /// <exception cref="InvalidDataException">Не PNG или вид, который разбор не знает.</exception>
    public static RgbaImage Decode(byte[] data, int? keyIndex = null, bool opaque = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("не PNG");
        int width = 0, height = 0, depth = 0, color = -1;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            var type = data.AsSpan(pos + 4, 4);
            if (length < 0 || pos + 12 + length > data.Length) throw new InvalidDataException("PNG обрывается");
            var body = data.AsSpan(pos + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                depth = body[8];
                color = body[9];
                if (body[12] != 0) throw new InvalidDataException("чересстрочный PNG не разбирается");
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = body.ToArray();
            else if (type.SequenceEqual("tRNS"u8)) trns = body.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(body);
            else if (type.SequenceEqual("IEND"u8)) break;
            pos += 12 + length;
        }
        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG без IHDR");
        int channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException($"PNG: вид цвета {color}") };
        if (color != 3 && depth != 8) throw new InvalidDataException($"PNG: {depth} бит на канал не разбирается");
        if (color == 3 && depth is not (1 or 2 or 4 or 8)) throw new InvalidDataException($"PNG: палитра {depth} бит");
        int bitsPerPixel = channels * depth, stride = ((width * bitsPerPixel) + 7) / 8, bpp = Math.Max(1, bitsPerPixel / 8);
        var raw = Inflate(idat.ToArray(), (stride + 1) * height);
        Unfilter(raw, stride, height, bpp);

        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int row = (y * (stride + 1)) + 1;
            for (int x = 0; x < width; x++)
            {
                int o = ((y * width) + x) * 4;
                switch (color)
                {
                case 0:
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = raw[row + x];
                    pixels[o + 3] = 255;
                    break;
                case 2:
                    pixels[o] = raw[row + (x * 3)];
                    pixels[o + 1] = raw[row + (x * 3) + 1];
                    pixels[o + 2] = raw[row + (x * 3) + 2];
                    pixels[o + 3] = 255;
                    break;
                case 4:
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = raw[row + (x * 2)];
                    pixels[o + 3] = raw[row + (x * 2) + 1];
                    break;
                case 6:
                    Buffer.BlockCopy(raw, row + (x * 4), pixels, o, 4);
                    break;
                default:
                    int index = depth == 8 ? raw[row + x] : (raw[row + (x * depth / 8)] >> (8 - depth - (x * depth % 8))) & ((1 << depth) - 1);
                    if (keyIndex == index && palette is not null && (index * 3) + 2 < palette.Length)
                    {
                        pixels[o] = 255;
                        pixels[o + 1] = 0;
                        pixels[o + 2] = 255;
                    }
                    else if (palette is not null && (index * 3) + 2 < palette.Length)
                    {
                        pixels[o] = palette[index * 3];
                        pixels[o + 1] = palette[(index * 3) + 1];
                        pixels[o + 2] = palette[(index * 3) + 2];
                    }
                    pixels[o + 3] = trns is not null && index < trns.Length ? trns[index] : (byte)255;
                    break;
                }
                if (opaque) pixels[o + 3] = 255;
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    /// <summary>Записать RGBA в PNG (8 бит, без фильтров строк, сжатие zlib).</summary>
    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int stride = image.Width * 4;
        var raw = new byte[(stride + 1) * image.Height];
        for (int y = 0; y < image.Height; y++) Buffer.BlockCopy(image.Pixels, y * stride, raw, (y * (stride + 1)) + 1, stride);
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
        using var png = new MemoryStream();
        png.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR"u8, header);
        Chunk(png, "IDAT"u8, packed.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> type, byte[] body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        png.Write(length);
        uint crc = Crc(Crc(0xFFFFFFFFu, type), body) ^ 0xFFFFFFFFu;
        png.Write(type);
        png.Write(body);
        Span<byte> sum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sum, crc);
        png.Write(sum);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static byte[] Inflate(byte[] packed, int expected)
    {
        var raw = new byte[expected];
        using var z = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
        int done = 0;
        while (done < expected)
        {
            int n = z.Read(raw, done, expected - done);
            if (n <= 0) throw new InvalidDataException("PNG: данных меньше, чем строк");
            done += n;
        }
        return raw;
    }

    private static void Unfilter(byte[] raw, int stride, int height, int bpp)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * (stride + 1), at = row + 1, prev = at - (stride + 1);
            byte filter = raw[row];
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? raw[at + i - bpp] : 0;
                int b = y > 0 ? raw[prev + i] : 0;
                int c = i >= bpp && y > 0 ? raw[prev + i - bpp] : 0;
                raw[at + i] = filter switch
                {
                    0 => raw[at + i],
                    1 => (byte)(raw[at + i] + a),
                    2 => (byte)(raw[at + i] + b),
                    3 => (byte)(raw[at + i] + ((a + b) >> 1)),
                    4 => (byte)(raw[at + i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"PNG: фильтр строки {filter}"),
                };
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
