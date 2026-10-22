using System.Buffers.Binary;
using System.Text;

namespace LastEdict.Data.Import;

/// <summary>Имена героев по умолчанию (окно имени нового героя, <c>DLG_GETINFO_BOX</c>, 0x4b02f3): <c>Globals/Tleader.dbf</c> игры (cp1252) →
/// <c>data/ui/leader-names.en.json</c>, та же таблица слоя русификации «Руссобит-М» (cp866) → <c>leader-names.ru.json</c> (перенос
/// <c>tools/ui-assets/import_leader_names.py</c>; те же байты). Раса — <c>RACE_ID</c>, пол — <c>SEX_M</c> (T — мужское; F и пусто — женское).
/// Правила (<c>data/rules/Tleader.json</c>, имена ИИ) шаг не трогает.</summary>
public static class LeaderNamesImport
{
    public const string Step = "leader-names";

    /// <exception cref="FileNotFoundException">Нет <c>Globals/Tleader.dbf</c> в копии игры.</exception>
    public static void Run(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string en = ctx.Game("Globals", "Tleader.dbf");
        if (!File.Exists(en)) throw new FileNotFoundException($"нет {en}", en);
        Write(ctx, "en", en, Encoding.GetEncoding(1252));
        string? ru = ctx.L10n("Globals", "Tleader.dbf");
        if (ru is not null && File.Exists(ru)) Write(ctx, "ru", ru, Encoding.GetEncoding(866));
        else ctx.Say($"нет {ru ?? "слоя русификации"} — ru пропущен");
    }

    private static void Write(ImportContext ctx, string lang, string path, Encoding encoding)
    {
        var (doc, count) = Build(path, encoding);
        ImportFiles.WriteText(Path.Combine(ctx.DataDir, "ui", $"leader-names.{lang}.json"), PyJson.Indented(doc, 1) + "\n");
        ctx.Say($"{lang}: {count} имён -> data/ui/leader-names.{lang}.json");
    }

    /// <summary>Опись <c>{"Races": {раса: {"Male": [...], "Female": [...]}}}</c> (расы — по первому появлению) и число имён.</summary>
    internal static (PyObj Doc, int Count) Build(string path, Encoding encoding)
    {
        var races = new PyObj();
        int count = 0;
        foreach (var row in ReadDbf(path, encoding))
        {
            string race = PyCompat.Upper(Field(row, "RACE_ID", path));
            if (races[race] is not PyObj sexes) races[race] = sexes = new PyObj { ["Male"] = new List<string>(), ["Female"] = new List<string>() };
            string sex = PyCompat.Upper(Field(row, "SEX_M", path)) == "T" ? "Male" : "Female";
            ((List<string>)sexes[sex]!).Add(Field(row, "TEXT", path));
            count++;
        }
        return (new PyObj { ["Races"] = races }, count);
    }

    private static string Field(Dictionary<string, string> row, string name, string path) =>
        row.TryGetValue(name, out string? v) ? v : throw new InvalidDataException($"{path}: нет поля {name}");

    /// <summary>Строки dBASE как у прежнего сборщика: поля по заголовку (до 0x0D), удалённые записи («*») пропускаются, значения — без пробелов
    /// по краям (<c>str.strip</c>).</summary>
    internal static List<Dictionary<string, string>> ReadDbf(string path, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 32) throw new InvalidDataException($"{path}: не dBASE (короче заголовка)");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        int header = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)), record = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
        var fields = new List<(string Name, int Length)>();
        for (int offset = 32; ; offset += 32)
        {
            if (offset >= data.Length) throw new InvalidDataException($"{path}: заголовок полей без конца (0x0D)");
            if (data[offset] == 0x0D) break;
            if (offset + 17 > data.Length) throw new InvalidDataException($"{path}: описание поля обрывается");
            var raw = data.AsSpan(offset, 11);
            int zero = raw.IndexOf((byte)0);
            fields.Add((Encoding.ASCII.GetString(zero >= 0 ? raw[..zero] : raw), data[offset + 16]));
        }
        var rows = new List<Dictionary<string, string>>();
        for (long i = 0; i < count; i++)
        {
            long start = header + (i * record);
            if (start + record > data.Length) throw new InvalidDataException($"{path}: запись {i} за концом файла");
            var raw = data.AsSpan((int)start, record);
            if (raw.Length > 0 && raw[0] == (byte)'*') continue;
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            int pos = 1;
            foreach (var (name, length) in fields)
            {
                int len = Math.Clamp(raw.Length - pos, 0, length);
                row[name] = PyCompat.Strip(encoding.GetString(raw.Slice(Math.Min(pos, raw.Length), len)));
                pos += length;
            }
            rows.Add(row);
        }
        return rows;
    }
}
