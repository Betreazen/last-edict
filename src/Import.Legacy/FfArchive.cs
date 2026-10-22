using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace LastEdict.Data.Import;

/// <summary>Архив графики и звуков игры (<c>.ff</c>, <c>.wdt</c>, <c>.wdb</c> — контейнер MQDB) — только импорт (<c>docs/principles.md</c> §3).
/// Перенос разбора <c>tools/ui-assets/ff_extract.py</c> (его происхождение — Karnah/Disciples.Net, MIT, <c>tools/ui-assets/NOTICE</c>) один
/// в один: заголовок 28 байт <c>MQDB</c>, цепочка записей <c>MQRC</c> (id +8, размер +12, выделено +16, данные с +28), запись 2 — список
/// файлов (число; имя 256 байт и id записи); у архивов графики — <c>-INDEX.OPT</c> (кадр → лист-атлас, -1 — имя анимации),
/// <c>-IMAGES.OPT</c> (кадры из кусков листа, индекс ключа палитры и смешение) и <c>-ANIMS.OPT</c> (кадры анимаций по порядку).
/// Файл читается позиционно (<see cref="RandomAccess"/>): архив до 346 МБ в память целиком не грузится, читать можно из разных потоков.</summary>
public sealed class FfArchive : IDisposable
{
    public const int FilesRecordId = 2, AnimationIndexId = -1;
    private const int RecordsStart = 28, HeaderSize = 28, NameFieldLength = 256, PaletteBytes = 1024;

    private readonly SafeFileHandle _file;
    private readonly long _length;
    private readonly Dictionary<int, (long Offset, int Size)> _records = [];
    private readonly Dictionary<string, FfEntry> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, FfEntry> _byId = [];
    private readonly OrderedDictionary<string, FfFrame> _frames = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, IReadOnlyList<string>> _anims = new(StringComparer.Ordinal);

    /// <summary>Путь к файлу архива.</summary>
    public string Path { get; }

    /// <summary>Файлы архива по порядку списка (записи без данных пропущены, как у <c>ff_extract</c>).</summary>
    public IReadOnlyList<FfEntry> Entries { get; }

    /// <summary>Кадры из <c>-IMAGES.OPT</c> по порядку (первое имя побеждает).</summary>
    public IReadOnlyDictionary<string, FfFrame> Frames => _frames;

    /// <summary>Анимации: имя → кадры по порядку (<c>-ANIMS.OPT</c>), порядок — как в <c>-INDEX.OPT</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Anims => _anims;

    /// <exception cref="InvalidDataException">Не MQDB или нет списка файлов.</exception>
    public FfArchive(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
        _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            _length = RandomAccess.GetLength(_file);
            if (!Read(0, 4).AsSpan().SequenceEqual("MQDB"u8)) throw new InvalidDataException($"{path}: не архив MQDB");
            LoadRecords();
            Entries = LoadEntries();
            // Как словари ff_extract: при повторе имени или id побеждает последний.
            foreach (var e in Entries)
            {
                _byName[e.Name] = e;
                _byId[e.Id] = e;
            }
            LoadOpt();
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    public void Dispose() => _file.Dispose();

    /// <summary>Файл архива по имени (с учётом регистра, как в архиве), иначе <c>null</c>.</summary>
    public FfEntry? Entry(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Файл архива по id записи, иначе <c>null</c>.</summary>
    public FfEntry? EntryById(int id) => _byId.GetValueOrDefault(id);

    /// <summary>Байты файла архива.</summary>
    public byte[] Raw(FfEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Read(entry.Offset, entry.Size);
    }

    /// <summary>Листы-атласы, из которых режутся кадры (имена файлов).</summary>
    public IReadOnlySet<string> BaseSheets() =>
        _frames.Values.Where(f => _byId.ContainsKey(f.BaseFileId)).Select(f => _byId[f.BaseFileId].Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Кадр собирается (<see cref="BuildFrame"/> даёт картинку): лист есть и размер положительный.</summary>
    public bool Buildable(FfFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _byId.ContainsKey(frame.BaseFileId) && frame.Width > 0 && frame.Height > 0;
    }

    /// <summary>Самостоятельная картинка архива как есть (маджента в ней остаётся), RGBA.</summary>
    public RgbaImage PlainImage(string name)
    {
        var entry = Entry(name) ?? throw new KeyNotFoundException($"{Path}: нет {name}");
        return PngCodec.Decode(Raw(entry));
    }

    /// <summary>Лист-атлас в RGB (альфа 255). <paramref name="transparent"/> — индекс палитры ключа из <c>-IMAGES.OPT</c>: движок блитит его как
    /// ключ прозрачности независимо от цвета в палитре, поэтому у палитровых листов этот индекс становится маджентой
    /// (<c>ff_extract.base_image</c>).</summary>
    public RgbaImage? BaseImage(int fileId, int? transparent, Dictionary<(int, int?), RgbaImage> cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (cache.TryGetValue((fileId, transparent), out var cached)) return cached;
        if (EntryById(fileId) is not { } entry) return null;
        var image = PngCodec.Decode(Raw(entry), transparent, opaque: true);
        cache[(fileId, transparent)] = image;
        return image;
    }

    /// <summary>Собрать кадр из кусков листа: холст размером кадра в мадженте, куски — копией прямоугольников листа (обрезанных по листу и
    /// холсту, как <c>ff_extract.build_frame</c>). Нет листа или размер не положительный — <c>null</c>.</summary>
    public RgbaImage? BuildFrame(FfFrame frame, Dictionary<(int, int?), RgbaImage> cache)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var sheet = BaseImage(frame.BaseFileId, frame.TransparentIndex, cache);
        if (sheet is null || frame.Width <= 0 || frame.Height <= 0) return null;
        var out_ = RgbaImage.Filled(frame.Width, frame.Height, 255, 0, 255, 255);
        foreach (var p in frame.Pieces)
        {
            if (p.BaseX >= sheet.Width || p.BaseY >= sheet.Height) continue;
            int w = Math.Min(p.W, Math.Min(sheet.Width - p.BaseX, frame.Width - p.FrameX));
            int h = Math.Min(p.H, Math.Min(sheet.Height - p.BaseY, frame.Height - p.FrameY));
            if (w <= 0 || h <= 0) continue;
            out_.Paste(sheet, p.BaseX, p.BaseY, w, h, p.FrameX, p.FrameY);
        }
        return out_;
    }

    private byte[] Read(long offset, int size)
    {
        if (offset < 0 || size < 0 || offset + size > _length) throw new InvalidDataException($"{Path}: чтение за концом файла ({offset}+{size})");
        var buffer = new byte[size];
        int done = 0;
        while (done < size)
        {
            int n = RandomAccess.Read(_file, buffer.AsSpan(done), offset + done);
            if (n <= 0) throw new EndOfStreamException($"{Path}: файл обрывается");
            done += n;
        }
        return buffer;
    }

    private void LoadRecords()
    {
        long pos = RecordsStart;
        var header = new byte[HeaderSize];
        while (pos + HeaderSize <= _length)
        {
            RandomAccess.Read(_file, header, pos);
            if (!header.AsSpan(0, 4).SequenceEqual("MQRC"u8)) break;
            int id = I32(header, 8), size = I32(header, 12), real = I32(header, 16);
            long offset = pos + HeaderSize;
            _records[id] = (offset, size);
            pos = offset + real;
        }
    }

    private List<FfEntry> LoadEntries()
    {
        if (!_records.TryGetValue(FilesRecordId, out var list)) throw new InvalidDataException($"{Path}: нет списка файлов (запись {FilesRecordId})");
        var data = Read(list.Offset, Math.Max(list.Size, 4));
        int count = I32(data, 0), pos = 4;
        var entries = new List<FfEntry>();
        for (int i = 0; i < count && pos + NameFieldLength + 4 <= data.Length; i++)
        {
            string name = Ascii(data.AsSpan(pos, NameFieldLength));
            int id = I32(data, pos + NameFieldLength);
            pos += NameFieldLength + 4;
            if (_records.TryGetValue(id, out var r)) entries.Add(new FfEntry(id, name, r.Size, r.Offset));
        }
        return entries;
    }

    private void LoadOpt()
    {
        if (Entry("-INDEX.OPT") is not { } idx || Entry("-IMAGES.OPT") is not { } img) return;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var animNames = new List<string>();
        byte[] data = Raw(idx);
        int pos = 4;
        for (int i = 0, count = I32(data, 0); i < count; i++)
        {
            int id = I32(data, pos);
            (string name, pos) = CStr(data, pos + 4);
            pos += 8;   // смещение и размер связанной записи
            index.TryAdd(name, id);
            if (id == AnimationIndexId) animNames.Add(name);
        }

        data = Raw(img);
        pos = 0;
        int end = data.Length - 1;
        while (pos < end)
        {
            int transparent = data[pos];
            int opacity = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(pos + 1));
            pos += 3 + 8 + PaletteBytes;   // размер X, Y не нужен; палитра — у самого листа
            int frames = I32(data, pos);
            pos += 4;
            for (int f = 0; f < frames; f++)
            {
                (string name, pos) = CStr(data, pos);
                int pieces = I32(data, pos), w = I32(data, pos + 4), h = I32(data, pos + 8);
                pos += 12;
                var list = new FfPiece[pieces];
                for (int k = 0; k < pieces; k++, pos += 24)
                    list[k] = new FfPiece(I32(data, pos), I32(data, pos + 4), I32(data, pos + 8), I32(data, pos + 12), I32(data, pos + 16), I32(data, pos + 20));
                int baseId = index.GetValueOrDefault(name, -1);
                _frames.TryAdd(name, new FfFrame(name, baseId, EntryById(baseId)?.Name ?? "", w, h, transparent, opacity, list));
            }
        }

        if (Entry("-ANIMS.OPT") is not { } an) return;
        data = Raw(an);
        pos = 0;
        end = data.Length - 1;
        for (int i = 0; pos < end && i < animNames.Count; i++)
        {
            int n = I32(data, pos);
            pos += 4;
            var names = new string[n];
            for (int k = 0; k < n; k++) (names[k], pos) = CStr(data, pos);
            _anims[animNames[i]] = names;
        }
    }

    private static int I32(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));

    private (string, int) CStr(byte[] data, int pos)
    {
        int end = Array.IndexOf(data, (byte)0, pos);
        if (end < 0) throw new InvalidDataException($"{Path}: строка без нуля на {pos}");
        return (Ascii(data.AsSpan(pos, end - pos)), end + 1);
    }

    /// <summary>ASCII до первого нуля; прочие байты — U+FFFD (как <c>decode("ascii", "replace")</c>).</summary>
    internal static string Ascii(ReadOnlySpan<byte> raw)
    {
        int zero = raw.IndexOf((byte)0);
        if (zero >= 0) raw = raw[..zero];
        return string.Create(raw.Length, raw.ToArray(), static (chars, bytes) =>
        {
            for (int i = 0; i < bytes.Length; i++) chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : '�';
        });
    }
}

/// <summary>Файл архива MQDB: id записи, имя, размер и смещение данных в файле.</summary>
public sealed record FfEntry(int Id, string Name, int Size, long Offset);

/// <summary>Кусок кадра: место в кадре (<c>FrameX</c>, <c>FrameY</c>), место в листе (<c>BaseX</c>, <c>BaseY</c>), размер.</summary>
public readonly record struct FfPiece(int FrameX, int FrameY, int BaseX, int BaseY, int W, int H);

/// <summary>Кадр из кусков листа-атласа (<c>-IMAGES.OPT</c>).</summary>
public sealed record FfFrame(string Name, int BaseFileId, string BaseFileName, int Width, int Height, int TransparentIndex, int Opacity,
                             IReadOnlyList<FfPiece> Pieces);
