using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2.Data.Ui;

/// <summary>Одно начертание: файл шрифта и кегль в точках сетки экрана (<see cref="UiScreen.Design"/>). Кегль — в тех же единицах, что и
/// прямоугольники элементов, поэтому при растяжении окна текст растёт вместе с разметкой.
///
/// <para>Файл — растровый шрифт BMFont (<c>.fnt</c> + PNG-атлас: шрифты самой игры, импорт <c>tools/ui-assets/import_fonts.py</c>) или
/// TrueType/OpenType. Путь <c>assets/…</c> — от корня сгенерированной графики (<c>D2_ASSETS_DIR</c>), остальные — от корня репозитория,
/// у мода — от каталога мода. У растрового шрифта кегль — его родной размер (высота ячейки), иначе движок его масштабирует.</para>
///
/// <para><paramref name="Line"/> — шаг строки (от базовой линии до базовой линии) в тех же точках, как у растрового шрифта оригинала:
/// высота ячейки MFT плюс его межстрочный интервал (<c>Interf/*.mft</c>: u32 height, u32 lineSpacing; замер снимков 28.09 — шаг 17 у
/// Normal/MedBold). 0 — шаг самого шрифта. Межстрочный интервал подписи подбирается так, чтобы шаг совпал (<c>UiArt.Settings</c>).</para>
///
/// <para><paramref name="Fallback"/> — запасной шрифт (TrueType): встаёт вместо основного, если файла основного нет (растровые шрифты игры
/// лежат в несобранной графике — <c>tools/ui-assets/import_fonts.py</c>). Кегль при этом тот же.</para></summary>
public sealed record UiFont(string File, int Size, int Line = 0, string? Fallback = null);

/// <summary>Чем порт рисует текст: набор начертаний по ключам старой разметки (<c>\fLarge;</c>),
/// шрифт по умолчанию и цвет по умолчанию. Живёт данными — <c>data/ui/fonts.json</c>, — а не кодом:
/// сменить гарнитуру или кегль можно правкой файла, не трогая сцену.
///
/// <para>Ключи — имена шрифтов исходной игры (<c>Interf/*.mft</c>: small, normal, medbold, medium, menu,
/// large, vlarge). Регистр не значим: в разметке встречаются и <c>\fLarge;</c>, и <c>\flarge;</c>.</para></summary>
public sealed record UiFonts
{
    /// <summary>Ключ начертания, которым рисуется текст без <c>\f…;</c>.</summary>
    public string Default { get; init; } = "Normal";

    /// <summary>Цвет текста без <c>\c…;</c>.</summary>
    public UiColor Color { get; init; } = new(255, 255, 255);

    public IReadOnlyDictionary<string, UiFont> Faces { get; init; } = new Dictionary<string, UiFont>();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static UiFonts FromJson(string json) =>
        JsonSerializer.Deserialize<UiFonts>(json, Json) ?? throw new FormatException("пустой набор шрифтов");

    public static UiFonts Load(string path) => FromJson(System.IO.File.ReadAllText(path));

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Наложить набор мода (<c>data/ui/fonts.json</c> мода) поверх базового: начертание опознаётся по ключу (регистр не значим),
    /// совпал — берётся начертание мода, новое — добавляется; <c>Default</c> и <c>Color</c> меняются, только если они есть в файле мода.
    /// Файл начертания мода (и запасной) лежит у мода: относительный путь разворачивается от <paramref name="root"/> (каталог мода) в полный — потребители
    /// склеивают путь с корнем репозитория через <c>Path.Combine</c>, а тот отдаёт полный путь как есть (так же, как кадры модов).</summary>
    public static UiFonts Merge(UiFonts baseSet, string overlayJson, string root)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlayJson);
        var layer = JsonSerializer.Deserialize<Layer>(overlayJson, Json) ?? throw new FormatException("пустой набор шрифтов мода");
        var faces = new Dictionary<string, UiFont>(baseSet.Faces, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, face) in layer.Faces ?? new Dictionary<string, UiFont>())
            faces[key] = face with { File = InMod(face.File, root), Fallback = face.Fallback is null ? null : InMod(face.Fallback, root) };
        return new UiFonts
        {
            Default = string.IsNullOrEmpty(layer.Default) ? baseSet.Default : layer.Default,
            Color = layer.Color ?? baseSet.Color,
            Faces = faces,
        };
    }

    private static string InMod(string file, string root) =>
        System.IO.Path.IsPathRooted(file) ? file
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(root, file.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    /// <summary>Набор мода: все поля необязательны — чего нет, то остаётся от базового.</summary>
    private sealed record Layer(string? Default, UiColor? Color, Dictionary<string, UiFont>? Faces);

    /// <summary>Начертание по ключу разметки. Пустой или неизвестный ключ даёт <see cref="Default"/>
    /// (о неизвестном сообщает <see cref="Knows"/> — молча подменять и молчать нельзя).</summary>
    public UiFont Resolve(string? key) =>
        Find(key) ?? Find(Default)
        ?? throw new FormatException($"в наборе шрифтов нет начертания по умолчанию «{Default}»");

    /// <summary>Знает ли набор такой ключ. Пустой ключ — знает: это шрифт по умолчанию.</summary>
    public bool Knows(string? key) => string.IsNullOrEmpty(key) || Find(key) is not null;

    private UiFont? Find(string? key) =>
        string.IsNullOrEmpty(key)
            ? null
            : Faces.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
}
