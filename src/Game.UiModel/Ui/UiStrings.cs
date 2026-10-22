using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2.Data.Ui;

/// <summary>Строки интерфейса порта: <c>data/ui/strings.&lt;язык&gt;.json</c>, плоская таблица
/// «идентификатор → текст».
///
/// <para>Идентификатор — тот же, на который ссылается <see cref="UiElement.TextId"/> (<c>X005TA0020</c>).
/// Регистр в исходниках игры гуляет (<c>X005TA0020</c> в разметке, <c>x000tg0000</c> в таблицах), поэтому
/// ключи приводятся к верхнему регистру при импорте, а сравниваются без учёта регистра.</para>
///
/// <para>Язык — в имени файла, а не в содержимом: добавить перевод значит положить рядом ещё один файл.
/// <see cref="Load(string, string, string)"/> берёт запасной язык, накладывает поверх запрошенный
/// и потому даёт запасной текст для непереведённых строк, а не пустоту.</para>
///
/// <para>Мод накладывается так же, как экраны (<see cref="UiScreens.Merge"/>): <see cref="Merge"/> по
/// идентификатору, совпал — текст мода, новый — добавляется.</para></summary>
public static class UiStrings
{
    /// <summary>Язык, к которому откатываются, когда запрошенного файла нет или в нём нет строки.
    /// Английский: это язык исходной игры, в нём заполнены все 3284 строки.</summary>
    public const string FallbackLanguage = "en";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string FileName(string language) => $"strings.{language}.json";

    /// <summary>Сортировка по ключу — файл лежит в git: без неё порядок зависел бы от порядка записей в dbf
    /// и дифф прыгал бы на ровном месте.</summary>
    public static string ToJson(IReadOnlyDictionary<string, string> strings) =>
        JsonSerializer.Serialize(strings.OrderBy(p => p.Key, StringComparer.Ordinal)
                                        .ToDictionary(p => p.Key, p => p.Value), Json);

    public static IReadOnlyDictionary<string, string> FromJson(string json) =>
        new Dictionary<string, string>(
            JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json)
            ?? throw new FormatException("пустая таблица строк"),
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> LoadFile(string path) => FromJson(File.ReadAllText(path));

    public static void Save(string path, IReadOnlyDictionary<string, string> strings) =>
        File.WriteAllText(path, ToJson(strings).ReplaceLineEndings("\n"));

    /// <summary>Наложить таблицу поверх базовой: совпал идентификатор — берётся текст наложения.</summary>
    public static IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string> baseSet,
                                                           IReadOnlyDictionary<string, string> overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var result = new Dictionary<string, string>(baseSet, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, text) in overlay) result[id] = text;
        return result;
    }

    /// <summary>Строки одного языка из каталога данных: запасной язык плюс запрошенный поверх.
    /// Нет ни того ни другого файла — <see cref="FileNotFoundException"/>: молча показывать пустые надписи хуже,
    /// чем упасть на запуске.</summary>
    public static IReadOnlyDictionary<string, string> Load(string dir, string language,
                                                           string fallback = FallbackLanguage) =>
        LoadLayers([dir], language, fallback);

    /// <summary>Строки одного языка из слоёв данных (базовый <c>data/ui</c>, затем <c>data/ui</c> модов по порядку): сначала
    /// запасной язык всех слоёв, поверх — запрошенный язык всех слоёв. Так перевод базы не перекрывается английской строкой мода,
    /// а строка, которую мод дал только по-английски, всё равно есть. Каталога или файла у слоя нет — слой пропускается; нет ни
    /// одного файла ни в одном слое — <see cref="FileNotFoundException"/>.</summary>
    public static IReadOnlyDictionary<string, string> LoadLayers(IEnumerable<string> dirs, string language,
                                                                 string fallback = FallbackLanguage)
    {
        ArgumentNullException.ThrowIfNull(dirs);
        var layers = dirs.ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int loaded = 0;
        foreach (string lang in fallback.Equals(language, StringComparison.OrdinalIgnoreCase)
                     ? [language] : new[] { fallback, language })
            foreach (string dir in layers)
            {
                string path = Path.Combine(dir, FileName(lang));
                if (!File.Exists(path)) continue;
                foreach (var (id, text) in LoadFile(path)) result[id] = text;
                loaded++;
            }
        if (loaded == 0)
            throw new FileNotFoundException(
                $"нет строк ни для «{language}», ни для «{fallback}» в {string.Join(", ", layers)}");
        return result;
    }

    /// <summary>Какие языки лежат в каталоге. Порядок — как их вернула файловая система, отсортирован.</summary>
    public static IReadOnlyList<string> Languages(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "strings.*.json").Select(p => Path.GetFileName(p)[8..^5])
                       .OrderBy(s => s, StringComparer.Ordinal).ToList()
            : [];
}
