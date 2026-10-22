using System.Buffers.Binary;
using System.Text;

namespace LastEdict.Data.Sg;

/// <summary>
/// Своя кампания игры (<c>Campaign\*.csg</c>) — только импорт. Контейнер MQDB (как <c>.ff</c>): заголовок 28 байт <c>MQDB</c>, записи
/// <c>MQRC</c> (id, размер, выделено, занято) с данными; запись 2 — список файлов (число; имя 256 байт и id записи). Файлы кампании:
/// <c>HEADER.CPN</c> (0x154 байта, <c>0x66aeea</c>: +0x04 имя 33, +0x25 описание 256, +0x125 автор 35, +0x148 id расы, +0x14C уровень
/// лидера, +0x150 флаг «начата»), <c>ORDER.CPN</c> (число и имена глав через ноль) и сами <c>.sg</c> глав. Разбор сверен с
/// <c>tools/ui-maps/common.py</c> (<c>parse_csg</c>) и четырьмя кампаниями <c>game-dev\Campaign</c>.
/// </summary>
public sealed record CsgFile(string Name, string Description, string Author, int RaceIndex, int LeaderLevel, bool Started,
                             IReadOnlyList<(string Name, byte[] Data)> Chapters)
{
    public const string Extension = ".csg";
    private const int HeaderSize = 0x154, FilesRecord = 2, NameSize = 256;
    private static readonly Encoding Cp1251;

    static CsgFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    /// <exception cref="InvalidDataException">Не MQDB, нет заголовка или порядка глав, глава не найдена.</exception>
    public static CsgFile Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 28 || !data.AsSpan(0, 4).SequenceEqual("MQDB"u8)) throw new InvalidDataException("не контейнер MQDB");
        var records = new Dictionary<int, byte[]>();
        int pos = 28;
        while (pos + 28 <= data.Length && data.AsSpan(pos, 4).SequenceEqual("MQRC"u8))
        {
            int id = I32(data, pos + 8), size = I32(data, pos + 12), alloc = I32(data, pos + 16), used = I32(data, pos + 20);
            if (size < 0 || alloc < size || pos + 28 + alloc > data.Length) throw new InvalidDataException($"запись {id} выходит за файл");
            if (used != 0) records[id] = data.AsSpan(pos + 28, size).ToArray();
            pos += 28 + alloc;
        }
        if (!records.TryGetValue(FilesRecord, out var listing)) throw new InvalidDataException("нет списка файлов");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int k = 0, n = I32(listing, 0); k < n; k++)
        {
            int at = 4 + (k * (NameSize + 4));
            string name = Str(listing.AsSpan(at, NameSize));
            if (records.TryGetValue(I32(listing, at + NameSize), out var file)) files[name] = file;
        }
        if (!files.TryGetValue("HEADER.CPN", out var head) || head.Length < HeaderSize) throw new InvalidDataException("нет HEADER.CPN");
        if (!files.TryGetValue("ORDER.CPN", out var order)) throw new InvalidDataException("нет ORDER.CPN");
        var chapters = new List<(string, byte[])>();
        int p = 4;
        for (int k = 0, n = I32(order, 0); k < n; k++)
        {
            int end = Array.IndexOf(order, (byte)0, p);
            if (end < 0) throw new InvalidDataException("ORDER.CPN обрывается");
            string chapter = Cp1251.GetString(order, p, end - p);
            p = end + 1;
            if (!files.TryGetValue(chapter, out var sg)) throw new InvalidDataException($"нет главы {chapter}");
            chapters.Add((chapter, sg));
        }
        return new CsgFile(Str(head.AsSpan(4, 33)), Str(head.AsSpan(0x25, 256)), Str(head.AsSpan(0x125, 35)),
                           I32(head, 0x148) & 0xffff, I32(head, 0x14C), head[0x150] != 0, chapters);
    }

    private static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));

    private static string Str(ReadOnlySpan<byte> raw)
    {
        int end = raw.IndexOf((byte)0);
        return Cp1251.GetString(end < 0 ? raw : raw[..end]);
    }
}
