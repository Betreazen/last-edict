using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2.Data.Ui;

/// <summary>Кадр интерфейса, вынутый из архива игры в обычную картинку (сборка — WebP без потерь; у мода — и <c>.png</c>, и
/// <c>.webp</c>). <see cref="Name"/> — имя кадра в архиве (по нему на кадр ссылаются экраны), <see cref="File"/> — путь к файлу
/// относительно корня <c>assets/ui</c>, <see cref="Sha256"/> — первые 16 знаков sha256 пикселей RGBA (не байтов файла: от кодека и
/// его настроек не зависит): по нему видно, что на диске лежит именно тот кадр,
/// который ожидался, а не чужая или устаревшая распаковка.</summary>
public sealed record UiAsset
{
    public required string Name { get; init; }
    public required string Archive { get; init; }
    public required string File { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public required string Sha256 { get; init; }

    /// <summary>Имя анимации, если это её кадр. Тогда <see cref="Name"/> — «анимация/номер»: имена кадров внутри
    /// архива односимвольные и у разных анимаций совпадают, а экран ссылается на анимацию целиком.</summary>
    public string? Anim { get; init; }
}

/// <summary>Список кадров интерфейса: чтение <c>data/ui/assets.json</c> и наложение модов.
/// Сами картинки в git не лежат — их пересобирает <c>tools/ui-assets/build_ui_assets.py</c>;
/// в репозитории только этот список, и по нему порт проверяет, что подсунули под него на диск.</summary>
public static class UiAssets
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(IEnumerable<UiAsset> assets) => JsonSerializer.Serialize(assets, Json);

    public static IReadOnlyList<UiAsset> FromJson(string json) =>
        JsonSerializer.Deserialize<List<UiAsset>>(json, Json) ?? throw new FormatException("пустой список кадров");

    public static IReadOnlyList<UiAsset> Load(string path) => FromJson(System.IO.File.ReadAllText(path));

    /// <summary>Анимации: имя — кадры по порядку номера.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<UiAsset>> Animations(IEnumerable<UiAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return assets.Where(a => a.Anim is not null)
            .GroupBy(a => a.Anim!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<UiAsset>)[.. g.OrderBy(a => a.Name, StringComparer.Ordinal)],
                          StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Путь к файлу кадра: корень распаковки плюс <see cref="UiAsset.File"/>.</summary>
    public static string PathOf(UiAsset asset, string assetsRoot) =>
        Path.Combine(assetsRoot, asset.File.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Наложить набор поверх базового — как <see cref="UiScreens.Merge"/>, но опознаётся кадр парой
    /// архив+имя: одно имя встречается в разных архивах с разным размером (10 пар <c>Interf</c>/<c>IntfScen</c>,
    /// например <c>DRAGDROP_SMALL_VALID</c>), так что одного имени для замены мало.</summary>
    public static IReadOnlyList<UiAsset> Merge(IEnumerable<UiAsset> baseSet, IEnumerable<UiAsset> overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var over = overlay.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var result = baseSet.Select(a => over.Remove(Key(a), out var replaced) ? replaced : a).ToList();
        result.AddRange(over.Values);
        return result;

        static string Key(UiAsset a) => $"{a.Archive}/{a.Name}";
    }

    /// <summary>Поиск по одному имени, как его пишут экраны. Порядок архивов в списке и есть порядок поиска:
    /// при совпадении имён берётся кадр из первого архива.</summary>
    public static IReadOnlyDictionary<string, UiAsset> ByName(IEnumerable<UiAsset> assets)
    {
        var map = new Dictionary<string, UiAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assets) map.TryAdd(a.Name, a);
        return map;
    }
}
