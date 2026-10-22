using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace D2.Data.Import;

/// <summary>Звуки игры: <c>Sounds/*.wdb</c> → <c>assets/sound/&lt;Архив&gt;/&lt;ИМЯ&gt;.mp3|.wav</c> и описи <c>data/sound/&lt;Архив&gt;.json</c>,
/// звуки юнитов <c>Sounds/Battle.wdt</c> → <c>data/sound/UnitSounds.json</c>, каталог <c>Music/</c> → <c>assets/sound/Music</c> и
/// <c>data/sound/Music.json</c> (перенос <c>tools/sound-assets/build_sound_assets.py</c>; описи и файлы — те же байты). Формат — <c>.wdb</c>
/// это MQDB, запись 1 — оглавление «WDB\0» + u32 + N × [u32 id записи][имя 16][u32], прочие записи — целые RIFF WAVE: MP3 внутри (0x55)
/// пишется байтами чанка <c>data</c> без перекодирования, PCM — заново собранным WAV из <c>fmt</c> и <c>data</c> (без cue/LIST): Godot
/// читает MP3 из буфера, а WAV — только PCM (<c>docs/ai-screen.md</c> §9, §12).</summary>
public static class SoundImport
{
    public const string Step = "sound";
    private const int ShaLength = 16, Mp3 = 0x55, Pcm = 1, UnitRecord = 192;

    /// <exception cref="DirectoryNotFoundException">Нет каталога <c>Sounds</c>.</exception>
    /// <exception cref="InvalidDataException">Архив не MQDB или битое оглавление.</exception>
    public static void Run(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string sounds = ctx.Game("Sounds");
        if (!Directory.Exists(sounds)) throw new DirectoryNotFoundException($"нет каталога звуков {sounds}");
        var archives = Directory.GetFiles(sounds).Where(p => HasExt(p, ".wdb")).Order(StringComparer.OrdinalIgnoreCase).ToList();
        string music = ctx.Game("Music");
        if (archives.Count == 0 && !Directory.Exists(music)) throw new DirectoryNotFoundException($"нет архивов в {sounds}");
        string final = Path.Combine(ctx.AssetsDir, "sound");
        ImportFiles.Recover(final);
        string built = ImportFiles.Fresh(final + ".new");
        var texts = new List<(string Name, string Text)>();
        int done = 0, total = archives.Count + 2;
        ctx.Report(Step, 0, total);
        foreach (string path in archives)
        {
            ctx.Cancel.ThrowIfCancellationRequested();
            var rows = BuildArchive(ctx, path, built);
            ctx.Report(Step, ++done, total);
            string archive = Path.GetFileName(path);
            if (rows.Count == 0)
            {
                ctx.Say($"{archive}: оглавление пустое — пропуск");
                continue;
            }
            texts.Add((Path.GetFileNameWithoutExtension(path) + ".json", Manifest(archive, rows)));
            int mp3 = rows.Count(r => (string)r["codec"]! == "mp3"), pcm = rows.Count(r => (string)r["codec"]! == "pcm16");
            ctx.Say($"{archive}: звуков {rows.Count} (mp3 {mp3}, pcm {pcm}), {Minutes(rows)} мин");
        }
        string wdt = Path.Combine(sounds, "Battle.wdt");
        if (File.Exists(wdt))
        {
            var (text, units, lists) = UnitSounds(wdt);
            texts.Add(("UnitSounds.json", text));
            ctx.Say($"Battle.wdt: юнитов {units}, списков {lists} -> data/sound/UnitSounds.json");
        }
        ctx.Report(Step, ++done, total);
        if (Directory.Exists(music))
        {
            var rows = BuildMusic(ctx, music, built);
            texts.Add(("Music.json", Manifest("Music", rows)));
            ctx.Say($"Music: дорожек {rows.Count}, {Minutes(rows)} мин");
        }
        ctx.Report(Step, ++done, total);
        ImportFiles.Swap(built, final);
        foreach (var (name, text) in texts) ImportFiles.WriteText(Path.Combine(ctx.DataDir, "sound", name), text);
    }

    /// <summary>Звуки одного <c>.wdb</c> в <c>&lt;out&gt;/&lt;Архив&gt;</c>; запись, которую не разобрать, пропускается с пометкой в журнале.</summary>
    private static List<PyObj> BuildArchive(ImportContext ctx, string path, string outRoot)
    {
        byte[] data = File.ReadAllBytes(path);
        var recs = Records(data, path);
        string stem = Path.GetFileNameWithoutExtension(path), name = Path.GetFileName(path);
        var rows = new List<PyObj>();
        if (!recs.TryGetValue(1, out var toc) || toc.Size <= 8) return rows;
        string dir = Path.Combine(outRoot, stem);
        Directory.CreateDirectory(dir);
        foreach (var (sound, rid) in Table(data, toc, path))
        {
            byte[] wav;
            Converted c;
            try
            {
                if (rid > int.MaxValue || !recs.TryGetValue((int)rid, out var rec)) throw new InvalidDataException($"нет записи {rid}");
                wav = Slice(data, rec.Offset, rec.Size);
                c = ToFile(wav);
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
            {
                ctx.Say($"{name}: {sound} пропущен — {e.Message}");
                continue;
            }
            File.WriteAllBytes(Path.Combine(dir, $"{sound}.{c.Ext}"), c.Body);
            rows.Add(Row(sound, $"{stem}/{sound}.{c.Ext}", c, wav));
        }
        return rows;
    }

    /// <summary>Каталог <c>Music/</c>: каждый файл — целый RIFF WAVE; имя — имя файла без расширения в верхнем регистре.</summary>
    private static List<PyObj> BuildMusic(ImportContext ctx, string folder, string outRoot)
    {
        string dir = Path.Combine(outRoot, "Music");
        Directory.CreateDirectory(dir);
        var rows = new List<PyObj>();
        var files = Directory.GetFiles(folder).Where(p => HasExt(p, ".wav"))
                             .OrderBy(p => PyCompat.Upper(Path.GetFileName(p)), StringComparer.Ordinal).ToList();
        foreach (string path in files)
        {
            ctx.Cancel.ThrowIfCancellationRequested();
            string sound = PyCompat.Upper(Path.GetFileNameWithoutExtension(path));
            byte[] wav = File.ReadAllBytes(path);
            Converted c;
            try
            {
                c = ToFile(wav);
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
            {
                ctx.Say($"Music: {Path.GetFileName(path)} пропущен — {e.Message}");
                continue;
            }
            File.WriteAllBytes(Path.Combine(dir, $"{sound}.{c.Ext}"), c.Body);
            rows.Add(Row(sound, $"Music/{sound}.{c.Ext}", c, wav));
        }
        return rows;
    }

    private static PyObj Row(string name, string file, Converted c, byte[] wav) => new()
    {
        ["name"] = name, ["file"] = file, ["codec"] = c.Codec, ["rate"] = c.Rate, ["channels"] = c.Channels, ["samples"] = c.Samples,
        ["seconds"] = c.Rate != 0 ? PyCompat.Round((double)c.Samples / c.Rate, 3) : 0,
        ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(wav))[..ShaLength],
    };

    /// <summary>Опись архива: по записи на строку (читаемый diff в git), как <c>write_manifest</c>.</summary>
    private static string Manifest(string archive, List<PyObj> rows) =>
        $"{{\"archive\":{PyCompat.EnsureAscii(PyJson.Compact(archive))},\n\"sounds\":[\n" + string.Join(",\n", rows.Select(PyJson.Compact)) + "\n]}\n";

    private static string Minutes(List<PyObj> rows) =>
        (rows.Sum(r => r["seconds"] is double d ? d : 0) / 60).ToString("0.0", CultureInfo.InvariantCulture);

    private static bool HasExt(string path, string ext) => string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>Записи MQDB: id → (смещение данных, размер); цепочка <c>MQRC</c> после 28 байт заголовка, при повторе id — последняя.</summary>
    internal static Dictionary<int, (int Offset, int Size)> Records(byte[] data, string path)
    {
        if (data.Length < 4 || !data.AsSpan(0, 4).SequenceEqual("MQDB"u8)) throw new InvalidDataException($"{path}: не архив MQDB");
        var records = new Dictionary<int, (int, int)>();
        int pos = 28;
        while (pos + 28 <= data.Length && data.AsSpan(pos, 4).SequenceEqual("MQRC"u8))
        {
            int rid = I32(data, pos + 8), size = I32(data, pos + 12), real = I32(data, pos + 16);
            if (real < 0) throw new InvalidDataException($"{path}: запись {rid} с отрицательной длиной {real}");
            records[rid] = (pos + 28, size);
            pos += 28 + real;
        }
        return records;
    }

    /// <summary>Оглавление WDB (запись 1): имя до первого нуля (хвост после него — мусор) и id записи.</summary>
    private static List<(string Name, uint Rid)> Table(byte[] data, (int Offset, int Size) toc, string path)
    {
        var (off, size) = toc;
        if (off + 4 > data.Length || !data.AsSpan(off, 4).SequenceEqual("WDB\0"u8)) throw new InvalidDataException($"{path}: нет оглавления WDB");
        var table = new List<(string, uint)>();
        for (int i = 0; i < (size - 8) / 24; i++)
        {
            int p = off + 8 + (i * 24);
            if (p + 20 > data.Length) throw new InvalidDataException($"{path}: оглавление WDB обрывается на записи {i}");
            uint rid = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
            var raw = data.AsSpan(p + 4, 16);
            int zero = raw.IndexOf((byte)0);
            if (zero >= 0) raw = raw[..zero];
            if (raw.ContainsAnyInRange((byte)0x80, (byte)0xFF)) throw new InvalidDataException($"{path}: имя записи {i} не ASCII");
            table.Add((Encoding.ASCII.GetString(raw), rid));
        }
        return table;
    }

    private readonly record struct Converted(string Ext, byte[] Body, string Codec, int Rate, int Channels, long Samples);

    /// <summary>Запись RIFF WAVE → расширение, байты файла и описание.</summary>
    private static Converted ToFile(byte[] wav)
    {
        var chunks = Chunks(wav);
        if (!chunks.TryGetValue("fmt ", out var fmt)) throw new InvalidDataException("нет чанка fmt");
        if (fmt.Offset + 16 > wav.Length) throw new InvalidDataException("чанк fmt обрывается");
        var f = wav.AsSpan(fmt.Offset);
        int tag = BinaryPrimitives.ReadUInt16LittleEndian(f), channels = BinaryPrimitives.ReadUInt16LittleEndian(f[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(f[4..]), avg = BinaryPrimitives.ReadUInt32LittleEndian(f[8..]);
        int align = BinaryPrimitives.ReadUInt16LittleEndian(f[12..]), bits = BinaryPrimitives.ReadUInt16LittleEndian(f[14..]);
        if (!chunks.TryGetValue("data", out var dataChunk)) throw new InvalidDataException("нет чанка data");
        byte[] body = Slice(wav, dataChunk.Offset, dataChunk.Size);
        if (rate > int.MaxValue) throw new InvalidDataException($"частота {rate}");
        if (tag == Mp3)
        {
            long samples = 0;
            if (chunks.TryGetValue("fact", out var fact))
            {
                if (fact.Offset + 4 > wav.Length) throw new InvalidDataException("чанк fact обрывается");
                samples = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(fact.Offset));
            }
            return new Converted("mp3", body, "mp3", (int)rate, channels, samples);
        }
        if (tag == Pcm)
        {
            // RIFF, длина, WAVE, fmt (16 байт), data — как struct.pack прежнего сборщика.
            var file = new byte[44 + body.Length];
            var s = file.AsSpan();
            "RIFF"u8.CopyTo(s);
            BinaryPrimitives.WriteUInt32LittleEndian(s[4..], (uint)(36 + body.Length));
            "WAVEfmt "u8.CopyTo(s[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(s[16..], 16);
            BinaryPrimitives.WriteUInt16LittleEndian(s[20..], Pcm);
            BinaryPrimitives.WriteUInt16LittleEndian(s[22..], (ushort)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(s[24..], rate);
            BinaryPrimitives.WriteUInt32LittleEndian(s[28..], avg);
            BinaryPrimitives.WriteUInt16LittleEndian(s[32..], (ushort)align);
            BinaryPrimitives.WriteUInt16LittleEndian(s[34..], (ushort)bits);
            "data"u8.CopyTo(s[36..]);
            BinaryPrimitives.WriteUInt32LittleEndian(s[40..], (uint)body.Length);
            body.CopyTo(s[44..]);
            return new Converted("wav", file, "pcm16", (int)rate, channels, body.Length / Math.Max(1, align));
        }
        throw new InvalidDataException($"формат 0x{tag:x} не поддержан");
    }

    /// <summary>Чанки RIFF WAVE: имя → (смещение данных, размер); при повторе — первый.</summary>
    private static Dictionary<string, (int Offset, int Size)> Chunks(byte[] wav)
    {
        if (wav.Length < 12 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("не RIFF WAVE");
        var chunks = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        long pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.Latin1.GetString(wav, (int)pos, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan((int)pos + 4));
            chunks.TryAdd(id, ((int)pos + 8, (int)Math.Min(size, int.MaxValue)));
            pos += 8 + size + (size & 1);
        }
        return chunks;
    }

    /// <summary>Срез как у Python: за концом массива — короче, без ошибки.</summary>
    private static byte[] Slice(byte[] data, int offset, int size)
    {
        if (offset >= data.Length || size <= 0) return [];
        return data.AsSpan(offset, Math.Min(size, data.Length - offset)).ToArray();
    }

    /// <summary><c>Battle.wdt</c>: звуки юнитов (<c>SNDINFO.DAT</c> — 192 байта на юнит: id, замах и его кадр, кадр удара, эффект и его кадр,
    /// удар по цели, «больно», шаг) и списки вариантов (<c>*.LST</c>: «имя\0, u32 n, n × имя\0») — текст описи и сколько чего.</summary>
    internal static (string Text, int Units, int Lists) UnitSounds(string wdt)
    {
        using var archive = new FfArchive(wdt);
        var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var units = new Dictionary<string, PyObj>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            byte[] raw = archive.Raw(entry);
            string name = PyCompat.Upper(entry.Name);
            if (name.EndsWith(".LST", StringComparison.Ordinal))
            {
                int pos = Array.IndexOf(raw, (byte)0);
                if (pos < 0 || pos + 5 > raw.Length) throw new InvalidDataException($"{wdt}: {entry.Name} без заголовка списка");
                pos++;
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(pos));
                pos += 4;
                var items = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    int end = Array.IndexOf(raw, (byte)0, pos);
                    if (end < 0) throw new InvalidDataException($"{wdt}: {entry.Name} обрывается на варианте {i}");
                    items.Add(PyCompat.Upper(Encoding.Latin1.GetString(raw, pos, end - pos)));
                    pos = end + 1;
                }
                lists[name[..^4]] = items;
            }
            else if (name == "SNDINFO.DAT")
            {
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(raw);
                for (int i = 0; i < count; i++)
                {
                    int at = 4 + (i * UnitRecord);
                    if (at + UnitRecord > raw.Length) throw new InvalidDataException($"{wdt}: SNDINFO.DAT обрывается на юните {i}");
                    var r = raw.AsSpan(at, UnitRecord);
                    uint uid = BinaryPrimitives.ReadUInt32LittleEndian(r);
                    units[$"G000UU{uid & 0xFFFF:X4}"] = new PyObj
                    {
                        ["attack"] = Upper(r, 0x04), ["attackFrame"] = I32(r, 0x28), ["hitFrame"] = I32(r, 0x2C),
                        ["effect"] = Upper(r, 0x30), ["effectFrame"] = I32(r, 0x54),
                        ["hit"] = Upper(r, 0x5C), ["hurt"] = Upper(r, 0x7D), ["walk"] = Upper(r, 0x9E),
                    };
                }
            }
        }
        var sb = new StringBuilder("{\"units\":{\n");
        sb.AppendJoin(",\n", units.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                 .Select(kv => PyCompat.EnsureAscii(PyJson.Compact(kv.Key) + ":" + PyJson.Compact(kv.Value))));
        sb.Append("\n},\n\"lists\":{\n");
        sb.AppendJoin(",\n", lists.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                 .Select(kv => PyCompat.EnsureAscii(PyJson.Compact(kv.Key) + ":[" + string.Join(", ", kv.Value.Select(PyJson.Compact)) + "]")));
        sb.Append("\n}}\n");
        return (sb.ToString(), units.Count, lists.Count);
    }

    /// <summary>Строка до нуля (или до конца записи) в латинице-1, в верхнем регистре.</summary>
    private static string Upper(ReadOnlySpan<byte> r, int at)
    {
        var s = r[at..];
        int zero = s.IndexOf((byte)0);
        return PyCompat.Upper(Encoding.Latin1.GetString(zero >= 0 ? s[..zero] : s));
    }

    private static int I32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
}
