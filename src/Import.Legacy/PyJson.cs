using System.Collections;
using System.Globalization;
using System.Text;

namespace LastEdict.Data.Import;

/// <summary>JSON ровно как <c>json.dumps(..., ensure_ascii=False)</c> Python — описи графики, звука и роликов собирались сборщиками на
/// Python, и импорт порта пишет те же байты (так их можно сверить с прежними). Объект — <see cref="PyObj"/> (порядок ключей как при
/// добавлении), список — любой <see cref="IEnumerable"/>, числа — <see cref="int"/>/<see cref="long"/> и <see cref="double"/> (вид — как
/// <c>repr</c> у Python: <c>36.0</c>, <c>0.067</c>).</summary>
public static class PyJson
{
    /// <summary>Плотно, как <c>separators=(",", ":")</c>.</summary>
    public static string Compact(object? value)
    {
        var sb = new StringBuilder();
        Write(sb, value, null, 0, ",", ":");
        return sb.ToString();
    }

    /// <summary>С отступом <paramref name="indent"/>, как <c>indent=N</c> (разделители по умолчанию: <c>","</c> в конце строки и <c>": "</c>).</summary>
    public static string Indented(object? value, int indent)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent, 0, ",", ": ");
        return sb.ToString();
    }

    /// <summary>Число с плавающей точкой как <c>repr</c> Python.</summary>
    public static string Float(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsInfinity(value)) return value > 0 ? "Infinity" : "-Infinity";
        string s = value.ToString("R", CultureInfo.InvariantCulture);
        if (s.Contains('E', StringComparison.Ordinal))
        {
            // repr Python: 1e-05, 1e+16 — мантисса без лишнего «.0», порядок двумя знаками и со знаком.
            int e = s.IndexOf('E', StringComparison.Ordinal);
            string mantissa = s[..e];
            int exp = int.Parse(s[(e + 1)..], CultureInfo.InvariantCulture);
            return $"{mantissa}e{(exp < 0 ? "-" : "+")}{Math.Abs(exp):00}";
        }
        return s.Contains('.', StringComparison.Ordinal) ? s : s + ".0";
    }

    /// <summary>Округление как <c>round(x, n)</c> Python — по точному двоичному значению, половина к чётному (<see cref="PyCompat.Round"/>).</summary>
    public static double Round(double value, int digits) => PyCompat.Round(value, digits);

    private static void Write(StringBuilder sb, object? value, int? indent, int level, string itemSep, string keySep)
    {
        switch (value)
        {
        case null: sb.Append("null"); return;
        case bool b: sb.Append(b ? "true" : "false"); return;
        case string s: Str(sb, s); return;
        case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
        case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
        case double d: sb.Append(Float(d)); return;
        case PyObj obj:
            if (obj.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool first = true;
            foreach (var (k, v) in obj)
            {
                if (!first) sb.Append(itemSep);
                first = false;
                NewLine(sb, indent, level + 1);
                Str(sb, k);
                sb.Append(keySep);
                Write(sb, v, indent, level + 1, itemSep, keySep);
            }
            NewLine(sb, indent, level);
            sb.Append('}');
            return;
        case IEnumerable list:
            var items = list.Cast<object?>().ToList();
            if (items.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int k = 0; k < items.Count; k++)
            {
                if (k > 0) sb.Append(itemSep);
                NewLine(sb, indent, level + 1);
                Write(sb, items[k], indent, level + 1, itemSep, keySep);
            }
            NewLine(sb, indent, level);
            sb.Append(']');
            return;
        default: throw new ArgumentException($"PyJson: тип {value.GetType().Name} не поддержан", nameof(value));
        }
    }

    private static void NewLine(StringBuilder sb, int? indent, int level)
    {
        if (indent is not { } n) return;
        sb.Append('\n').Append(' ', n * level);
    }

    private static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            case '\b': sb.Append("\\b"); break;
            case '\f': sb.Append("\\f"); break;
            default:
                if (c < 0x20) sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                else sb.Append(c);
                break;
            }
        }
        sb.Append('"');
    }
}

/// <summary>Объект JSON с порядком ключей как при добавлении (словарь Python).</summary>
public sealed class PyObj : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly List<KeyValuePair<string, object?>> _items = [];

    public int Count => _items.Count;

    /// <summary>Значение ключа; новый ключ встаёт в конец, прежний меняется на месте.</summary>
    public object? this[string key]
    {
        get => _items.FirstOrDefault(p => p.Key == key).Value;
        set
        {
            int at = _items.FindIndex(p => p.Key == key);
            if (at >= 0) _items[at] = new(key, value);
            else _items.Add(new(key, value));
        }
    }

    public void Add(string key, object? value) => this[key] = value;

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
