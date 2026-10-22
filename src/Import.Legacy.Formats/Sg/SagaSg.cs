using System.Text;
using D2.Data.Dbf;

namespace D2.Data.Sg;

/// <summary>
/// Таблицы сценариев саг игры (<c>Scens\*.dbf</c>; разбор — <c>docs/day-cycle.md</c> §15). Движок пишет объект одним и тем же методом
/// <c>stream()</c> и в файл <c>.sg</c>, и в DBF (<c>CMidStreamDB</c>, vtable <c>0x6ef4f4</c>), поэтому колонка таблицы названа как поле
/// потока. Строки объектов (имена, описания, тексты окон событий) лежат отдельно — <c>Tscen</c>: объект, поле, номер (у текста окна
/// события — номер эффекта), текст. Только для импорта (<c>docs/principles.md</c> §3).
/// </summary>
public sealed class ScensTables
{
    private readonly Dictionary<string, Dictionary<string, List<DbfRow>>> _byScen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Obj, string Field, int Seq), string> _texts = [];

    private ScensTables()
    {
    }

    /// <summary>Сценарии саг в порядке <c>Sinfo</c> (<c>S107SC0000</c>…).</summary>
    public IReadOnlyList<string> Scenarios { get; private set; } = [];

    /// <summary>Таблицы из каталогов по порядку: таблица следующего каталога заменяет одноимённую целиком (слой русификации
    /// <c>l10n/russobit/Scens</c> несёт только тексты — <c>Sinfo</c> и <c>Tscen</c>, остальное берётся из игры).</summary>
    public static ScensTables Load(params string[] scensDirs)
    {
        ArgumentNullException.ThrowIfNull(scensDirs);
        var t = new ScensTables();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in scensDirs)
            foreach (string file in Directory.GetFiles(dir, "*.dbf", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }))
                paths[Path.GetFileNameWithoutExtension(file)] = file;
        foreach (string path in paths.Values)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var byScen = new Dictionary<string, List<DbfRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in DbfTable.Read(path).Rows)
            {
                string scen = row.GetString("NO_SCEN");
                if (!byScen.TryGetValue(scen, out var list)) byScen[scen] = list = [];
                list.Add(row);
            }
            t._byScen[name] = byScen;
        }
        foreach (var row in t._byScen.GetValueOrDefault("Tscen")?.Values.SelectMany(r => r) ?? [])
            t._texts[(row.GetString("ID_OBJECT").ToUpperInvariant(), row.GetString("ID_STRING"), row.GetInt("SEQ_NUM"))] = row.GetString("TEXT");
        t.Scenarios = [.. t.Rows("Sinfo", null).Select(r => r.GetString("NO_SCEN"))];
        return t;
    }

    /// <summary>Строки таблицы сценария <paramref name="scen"/>; null — всех сценариев.</summary>
    internal IReadOnlyList<DbfRow> Rows(string table, string? scen)
    {
        if (!_byScen.TryGetValue(table, out var byScen)) return [];
        return scen is null ? [.. byScen.Values.SelectMany(r => r)] : byScen.GetValueOrDefault(scen) ?? [];
    }

    internal string? Text(string obj, string field, int seq) => _texts.GetValueOrDefault((obj.ToUpperInvariant(), field, seq));
}

/// <summary>
/// Сценарий саги из таблиц (<see cref="ScensTables"/>) — байтами файла <c>.sg</c> формата <c>D2EESFISIG</c>, которые разбирает тот же
/// Kaitai-разбор, что и файлы <c>Exports</c>. Объекты — по <c>SObjMap</c>, тело каждого — поля потока в порядке <c>sg.ksy</c>
/// (<see cref="SagaLayouts"/>): значение — колонка строки объекта с именем поля, иначе строка <c>Tscen</c>, иначе пусто/0.
/// Двоичные поля (плитки блока карты, строка тумана) в DBF — текстом: 6 бит на знак старшими вперёд, знак = биты + <c>0x21</c>
/// (кодер <c>0x67bc9c</c>, декодер <c>0x67bd20</c>: байт — ⌊6·длина/8⌋).
/// </summary>
public static class SagaSg
{
    private static readonly Encoding Cp1251;

    static SagaSg()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>Поля, которых нет ни в таблице, ни в <c>Tscen</c> (класс и поле), — для проверок: список известен и мал.</summary>
    public static HashSet<string> Defaulted { get; } = new(StringComparer.Ordinal);

    /// <summary>Байты <c>.sg</c> сценария <paramref name="scen"/> (<c>S135SC0000</c>).</summary>
    /// <exception cref="InvalidDataException">Класса объекта нет в раскладке или у объекта нет своей строки.</exception>
    public static byte[] Build(ScensTables tables, string scen)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var info = tables.Rows("Sinfo", scen).SingleOrDefault() ?? throw new InvalidDataException($"нет сценария {scen}");
        var objects = tables.Rows("SObjMap", scen);
        var w = new Writer(tables, scen);
        w.Raw(Header(info));
        w.Raw(Encoding.ASCII.GetBytes(scen[..4] + "OB0000"));
        w.U32(objects.Count);
        foreach (var o in objects)
        {
            string id = o.GetString("OBJ_ID"), what = o.GetString("WHAT");
            string cls = what.Length > 6 ? what[5..^2] : what;
            if (!SagaLayouts.Classes.TryGetValue(cls, out var layout)) throw new InvalidDataException($"{scen}: нет раскладки класса {what} ({id})");
            w.Raw(Encoding.ASCII.GetBytes("WHAT"));
            w.U32(what.Length + 1);
            w.Raw(Encoding.ASCII.GetBytes(what + "\0"));
            w.Raw(Encoding.ASCII.GetBytes("OBJ_ID"));
            w.U32(11);
            w.Raw(Encoding.ASCII.GetBytes(id + "\0BEGOBJECT\0"));
            var row = layout.Table is null ? null
                : tables.Rows(layout.Table, scen).FirstOrDefault(r => r.GetString(layout.Key!).Equals(id, StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidDataException($"{scen}: нет строки {layout.Table}.{layout.Key} = {id}");
            w.Run(layout.Ops, new Ctx(cls, id, row, 0));
            w.Raw(Encoding.ASCII.GetBytes("ENDOBJECT\0"));
        }
        return w.Bytes();
    }

    /// <summary>Двоичное поле из текста DBF (<c>0x67bd20</c>).</summary>
    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string s = text.TrimEnd();
        var bytes = new byte[s.Length * 6 / 8];
        int acc = 0, bits = 0, n = 0;
        foreach (char c in s)
        {
            acc = (acc << 6) | ((c - 0x21) & 0x3f);
            bits += 6;
            if (bits < 8) continue;
            bits -= 8;
            if (n < bytes.Length) bytes[n++] = (byte)(acc >> bits);
            acc &= (1 << bits) - 1;
        }
        return bytes;
    }

    /// <summary>Заголовок <c>D2EESFISIG</c> (<c>sg.ksy</c> <c>d2ee_header</c>): id и имя сценария, размер карты, сложность, кампания,
    /// уровень; списка рас нет (порт его не читает), генератор случайных чисел — нули.</summary>
    private static byte[] Header(Dbf.DbfRow info)
    {
        const int Fixed = 12 + 11 + 256 + 21 + 1 + 256 + 16 + 11 + 4 + 1 + 256 + 4 + 815 + 4 + 1000 + 4 + 4;
        var b = new MemoryStream();
        void Bytes(byte[] v) => b.Write(v);
        void U32(int v) => Bytes(BitConverter.GetBytes(v));
        void Fix(string v, int size)
        {
            var raw = new byte[size];
            var s = Cp1251.GetBytes(v);
            Array.Copy(s, raw, Math.Min(s.Length, size - 1));
            Bytes(raw);
        }
        Bytes(Encoding.ASCII.GetBytes("D2EESFISIG"));
        Bytes([0, 0]);
        U32(-1);
        U32(Fixed);
        U32(1);
        U32(0x23);
        U32(0);
        Fix(info.GetString("NO_SCEN"), 11);
        Fix(info.GetString("DESC"), 256);
        Fix(info.GetString("CREATOR"), 21);
        Bytes([1]);
        Fix(info.GetString("NAME"), 256);
        U32(info.GetInt("MAP_SIZE"));
        U32(info.GetInt("DIFFGAME"));
        U32(info.GetInt("CUR_TURN"));
        U32(0);
        Fix(info.GetString("CAMPAIGN"), 11);
        U32(info.GetInt("SUGG_LVL"));
        Bytes([0]);
        Fix("", 256);
        U32(0);
        Bytes(new byte[815]);
        U32(0);
        Bytes(new byte[1000]);
        U32(0);
        U32(0);
        return b.ToArray();
    }

    internal sealed record Ctx(string Class, string Id, DbfRow? Row, int Seq);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "MemoryStream не держит неуправляемых ресурсов")]
    internal sealed class Writer(ScensTables tables, string scen)
    {
        private readonly MemoryStream _out = new();

        public byte[] Bytes() => _out.ToArray();

        public void Raw(byte[] v) => _out.Write(v);

        public void U32(int v) => Raw(BitConverter.GetBytes(v));

        private void Marker(string m) => Raw(Encoding.ASCII.GetBytes(m));

        public void Run(IEnumerable<Op> ops, Ctx c)
        {
            foreach (var op in ops) Emit(op, c);
        }

        private void Emit(Op op, Ctx c)
        {
            switch (op)
            {
                case Str s:
                    String(s.Marker, StringOf(c, s.Marker, s.Column));
                    break;
                case Money m:
                {
                    string bank = StringOf(c, m.Marker, null);
                    String(m.Marker, bank.Split(':').Length == 5 ? bank + ":B0000" : bank);
                    break;
                }
                case Int i:
                    Marker(i.Marker);
                    U32(IntOf(c, i.Marker, i.Column));
                    break;
                case Bool b:
                    Marker(b.Marker);
                    Raw([(byte)(BoolOf(c, b.Marker) ? 1 : 0)]);
                    break;
                case Binary bin:
                {
                    var data = Decode(c.Row?.GetString(bin.Marker) ?? "");
                    Marker(bin.Marker);
                    U32(data.Length);
                    Raw(data);
                    break;
                }
                case From f:
                {
                    var row = tables.Rows(f.Table, scen).FirstOrDefault(r => r.GetString(f.Key).Equals(c.Id, StringComparison.OrdinalIgnoreCase));
                    Run(f.Ops, c with { Row = row });
                    break;
                }
                case ListOf l:
                {
                    var rows = ListRows(l, c);
                    if (l.CountMarker is null)
                    {
                        Marker(c.Id);
                        U32(rows.Count);
                    }
                    else
                    {
                        Marker(l.CountMarker);
                        U32(rows.Count);
                    }
                    foreach (var row in rows) Run(l.Item, c with { Row = row, Seq = Has(row, "NUM") ? row.GetInt("NUM") : c.Seq });
                    break;
                }
                case Count n:
                    Marker(c.Id);
                    U32(n.Value(tables, scen, c));
                    break;
                case When w:
                    if (w.Condition(c)) Run(w.Ops, c);
                    break;
                case Switch sw:
                {
                    int key = IntOf(c, sw.Marker, null);
                    Marker(sw.Marker);
                    U32(key);
                    if (sw.Then is not null) Run(sw.Then, c);
                    if (!sw.Cases.TryGetValue(key, out var ops)) throw new InvalidDataException($"{c.Id}: {sw.Marker} {key} нет в раскладке");
                    Run(ops, c);
                    break;
                }
            }
        }

        private void String(string marker, string value)
        {
            Marker(marker);
            var raw = Cp1251.GetBytes(value);
            U32(raw.Length + 1);
            Raw(raw);
            Raw([0]);
        }

        private List<DbfRow> ListRows(ListOf l, Ctx c)
        {
            var all = tables.Rows(l.Table, scen);
            var rows = l.Key is null ? [.. all] : all.Where(r => r.GetString(l.Key).Equals(c.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            return l.OrderBy is null ? rows : [.. rows.OrderBy(r => r.GetInt(l.OrderBy))];
        }

        private string StringOf(Ctx c, string marker, string? column)
        {
            // Номер строки Tscen — номер эффекта только у текста окна события; у прочих строк (и у OBJECT_TXT) — 0.
            if (tables.Text(c.Id, marker, marker == "POPUP_TXT" ? c.Seq : 0) is { } text) return text;
            if (c.Row is { } row && Has(row, column ?? marker)) return row.GetString(column ?? marker);
            Miss(c, marker);
            return "";
        }

        private int IntOf(Ctx c, string marker, string? column)
        {
            if (c.Row is { } row && Has(row, column ?? marker)) return row.GetInt(column ?? marker);
            Miss(c, marker);
            return 0;
        }

        private bool BoolOf(Ctx c, string marker)
        {
            // Маркер и колонка DBF совпадают и у L_ALLY: PERMALLI — с опечаткой у самого движка (поток 0x616384).
            if (c.Row is { } row && Has(row, marker)) return row.GetBool(marker);
            Miss(c, marker);
            return false;
        }

        private static bool Has(DbfRow row, string column)
        {
            try
            {
                _ = row[column];
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        private static void Miss(Ctx c, string marker)
        {
            lock (Defaulted) Defaulted.Add($"{c.Class}.{marker}");
        }
    }
}
