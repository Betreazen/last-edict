using System.Globalization;
using System.Numerics;
using System.Text;

namespace D2.Data.Import;

/// <summary>Мелочи Python, от которых зависят байты описей прежних сборщиков: <c>round</c> по точному двоичному значению, <c>str.strip</c>,
/// <c>str.splitlines</c>, <c>str.upper</c> и JSON с <c>ensure_ascii=True</c> (по умолчанию у <c>json.dumps</c>).</summary>
internal static class PyCompat
{
    /// <summary><c>round(x, n)</c> Python: по ТОЧНОМУ значению числа (а не по его короткому виду), половина — к чётному. Отличие от
    /// округления по виду — у чисел, чей вид кончается на 5 сразу за знаком <paramref name="digits"/>: 0.0005 в double чуть больше
    /// половины, и Python даёт 0.001, а не 0.0.</summary>
    public static double Round(double x, int digits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digits);
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x;
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool negative = bits < 0;
        int exp = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp = 1;
        else mantissa |= 1L << 52;
        exp -= 1075;   // |x| = mantissa · 2^exp
        BigInteger scaled = mantissa * BigInteger.Pow(10, digits), q, rem, den;
        if (exp >= 0)
        {
            q = scaled << exp;
            rem = BigInteger.Zero;
            den = BigInteger.One;
        }
        else
        {
            den = BigInteger.One << -exp;
            q = BigInteger.DivRem(scaled, den, out rem);
        }
        int half = (rem * 2).CompareTo(den);
        if (half > 0 || (half == 0 && !q.IsEven)) q += 1;
        string s = q.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        if (digits > 0) s = s[..^digits] + "." + s[^digits..];
        double y = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -y : y;
    }

    /// <summary>Пробел по <c>str.isspace</c> Python: пробелы .NET и ещё разделители U+001C…U+001F.</summary>
    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || c is >= '\x1c' and <= '\x1f';

    /// <summary><c>str.strip()</c>.</summary>
    public static string Strip(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        int a = 0, b = s.Length;
        while (a < b && IsSpace(s[a])) a++;
        while (b > a && IsSpace(s[b - 1])) b--;
        return s[a..b];
    }

    /// <summary><c>str.splitlines()</c>: \r\n, \n, \r, \v, \f, U+001C…U+001E, U+0085, U+2028, U+2029; хвост без перевода строки — тоже строка.</summary>
    public static List<string> SplitLines(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is not ('\n' or '\r' or '\v' or '\f' or '\x1c' or '\x1d' or '\x1e' or '\u0085' or '\u2028' or '\u2029')) continue;
            lines.Add(s[start..i]);
            if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < s.Length) lines.Add(s[start..]);
        return lines;
    }

    /// <summary><c>str.upper()</c> для имён игры (ASCII и латиница-1): как <see cref="string.ToUpperInvariant"/>, но «ß» — «SS».</summary>
    public static string Upper(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string up = s.ToUpperInvariant();
        return up.Contains('ß', StringComparison.Ordinal) ? up.Replace("ß", "SS", StringComparison.Ordinal) : up;
    }

    /// <summary>Готовый JSON как с <c>ensure_ascii=True</c> (по умолчанию у <c>json.dumps</c>): всё от U+007F — <c>\uXXXX</c> (вне строк JSON
    /// таких знаков нет).</summary>
    public static string EnsureAscii(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.All(c => c < 0x7F)) return json;
        var sb = new StringBuilder(json.Length + 16);
        foreach (char c in json)
        {
            if (c < 0x7F) sb.Append(c);
            else sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
        }
        return sb.ToString();
    }
}
