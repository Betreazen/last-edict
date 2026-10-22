using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace D2.Data.Import;

/// <summary>Слои картинки столицы: <c>Imgs/capital.dat</c> игры (ini в cp1251: раздел на расу, строки
/// <c>LAYER_&lt;номер&gt;=&lt;постройка&gt;,&lt;картинка&gt;,&lt;?&gt;,&lt;x&gt;,&lt;y&gt;,&lt;…&gt;,&lt;x1&gt;,&lt;y1&gt;,&lt;x2&gt;,&lt;y2&gt;</c>) → <c>data/ui/capital.json</c>
/// (перенос <c>tools/ui-assets/import_capital.py</c>; те же байты). Номер слоя — порядок наложения (меньший ниже), картинка — кадр или
/// анимация <c>Capital.ff</c>, последние четыре числа — прямоугольник постройки под мышью (-1 — нет), прочие поля — как есть (<c>Legacy</c>).</summary>
public static partial class CapitalImport
{
    public const string Step = "capital";

    /// <summary>Раздел <c>capital.dat</c> → <c>Grace.RACE_TYPE</c> (0 Империя, 1 Орды нежити, 2 Легионы, 3 Кланы, 5 Эльфы).</summary>
    private static readonly Dictionary<string, int> RaceTypes = new(StringComparer.Ordinal)
    {
        ["HUMAN"] = 0, ["UNDEAD"] = 1, ["HERETIC"] = 2, ["DWARF"] = 3, ["ELF"] = 5,
    };

    /// <exception cref="FileNotFoundException">Нет <c>Imgs/capital.dat</c>.</exception>
    /// <exception cref="InvalidDataException">Неизвестный раздел или битая строка слоя.</exception>
    public static void Run(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string path = ctx.Game("Imgs", "capital.dat");
        if (!File.Exists(path)) throw new FileNotFoundException($"нет {path}", path);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var races = Parse(Encoding.GetEncoding(1251).GetString(File.ReadAllBytes(path)));
        ImportFiles.WriteText(Path.Combine(ctx.DataDir, "ui", "capital.json"), PyJson.Indented(new PyObj { ["Races"] = races }, 2) + "\n");
        ctx.Say($"data/ui/capital.json: рас {races.Count}, слоёв {races.Sum(r => ((List<PyObj>)r["Layers"]!).Count)}");
    }

    [GeneratedRegex(@"\A\[(\w+)\]\z")]
    private static partial Regex SectionLine();

    [GeneratedRegex(@"\ALAYER_(\d+)=(.*)\z")]
    private static partial Regex LayerLine();

    internal static List<PyObj> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var races = new List<PyObj>();
        List<PyObj>? layers = null;
        foreach (string raw in PyCompat.SplitLines(text))
        {
            string line = PyCompat.Strip(raw);
            if (SectionLine().Match(line) is { Success: true } s)
            {
                string race = s.Groups[1].Value;
                if (!RaceTypes.TryGetValue(race, out int type)) throw new InvalidDataException($"capital.dat: неизвестный раздел [{race}]");
                layers = [];
                races.Add(new PyObj { ["Race"] = race, ["RaceType"] = type, ["Layers"] = layers });
                continue;
            }
            if (LayerLine().Match(line) is not { Success: true } m || layers is null) continue;
            string[] f = m.Groups[2].Value.Split(',');
            if (f.Length < 5) throw new InvalidDataException($"capital.dat: в строке «{line}» меньше пяти полей");
            int[] rect = [.. f[^4..].Select(v => Int(v, line))];
            var layer = new PyObj
            {
                ["Z"] = Int(m.Groups[1].Value, line), ["Image"] = f[1], ["X"] = f[3].Length > 0 ? Int(f[3], line) : 0,
                ["Y"] = f[4].Length > 0 ? Int(f[4], line) : 0,
            };
            if (f[0].Length > 0) layer["Building"] = PyCompat.Upper(f[0]);
            if (rect[0] >= 0) layer["Hotspot"] = new PyObj { ["X"] = rect[0], ["Y"] = rect[1], ["Width"] = rect[2] - rect[0], ["Height"] = rect[3] - rect[1] };
            layer["Legacy"] = new List<string> { f[2] }.Concat(f.Length > 9 ? f[5..^4] : []).ToList();
            layers.Add(layer);
        }
        foreach (var race in races)
        {
            var sorted = ((List<PyObj>)race["Layers"]!).OrderBy(l => (int)l["Z"]!).ToList();   // OrderBy устойчив, как list.sort
            race["Layers"] = sorted;
        }
        return races;
    }

    /// <summary><c>int()</c> Python: пробелы по краям допустимы.</summary>
    private static int Int(string v, string line) =>
        int.TryParse(PyCompat.Strip(v), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n)
            ? n : throw new InvalidDataException($"capital.dat: «{v}» не число в строке «{line}»");
}
