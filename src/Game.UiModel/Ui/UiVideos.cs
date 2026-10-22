using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2.Data.Ui;

/// <summary>Ролик игры, переведённый из Bink в Ogg Theora (<c>.ogv</c>) — единственный видеоформат, который Godot
/// читает сам. <see cref="Name"/> — имя ролика в игре, ВЕРХНИМ регистром и без расширения (<c>TRANS_MAIN2SINGLE</c>,
/// <c>BRIEFH01</c>, <c>INTRO</c>); <see cref="Source"/> — откуда взят: <c>MenuAnim</c> (архив
/// <c>Interf/MenuAnim.ff</c>), <c>Briefing</c>, <c>Video</c>; <see cref="File"/> — путь относительно корня
/// <c>assets/video</c>. Числа — из исходного ролика, <see cref="Sha256"/> — первые 16 знаков sha256 исходного
/// <c>.BIK</c>: опись не зависит от версии ffmpeg, которым переводили.</summary>
public sealed record UiVideo
{
    public required string Name { get; init; }
    public required string Source { get; init; }
    public required string File { get; init; }
    public int W { get; init; }
    public int H { get; init; }

    /// <summary>Кадров в секунду. У всех роликов 15, кроме чёрной заглушки <c>NOIR</c> (50000/3333 ≈ 15.002).</summary>
    public double Fps { get; init; }

    /// <summary>Кадров в исходнике. В <c>.ogv</c> неизменный кадр — пустой пакет, но место в счёте он занимает.</summary>
    public int Frames { get; init; }

    /// <summary>Длительность в секундах, три знака.</summary>
    public double Duration { get; init; }

    public bool Audio { get; init; }
    public required string Sha256 { get; init; }

    /// <summary>Есть ли картинка. Озвучка брифингов кампании эльфов (<c>BRIEFEL1</c>…<c>BRIEFEL8</c>) — звук
    /// без видео (<c>.ogg</c>), у неё размер и кадры нулевые.</summary>
    [JsonIgnore]
    public bool HasVideo => W > 0 && H > 0;
}

/// <summary>Опись роликов: чтение <c>data/video/videos.json</c> и наложение модов. Сами <c>.ogv</c> в git не лежат
/// (≈200 МБ) — их пересобирает <c>tools/video-import/build_videos.py</c>; в репозитории только опись.</summary>
public static class UiVideos
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(IEnumerable<UiVideo> videos) => JsonSerializer.Serialize(videos, Json);

    public static IReadOnlyList<UiVideo> FromJson(string json) =>
        JsonSerializer.Deserialize<List<UiVideo>>(json, Json) ?? throw new FormatException("пустая опись роликов");

    public static IReadOnlyList<UiVideo> Load(string path) => FromJson(System.IO.File.ReadAllText(path));

    /// <summary>Путь к файлу ролика: корень <c>assets/video</c> плюс <see cref="UiVideo.File"/>.</summary>
    public static string PathOf(UiVideo video, string videoRoot) =>
        Path.Combine(videoRoot, video.File.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Наложить набор поверх базового — как <see cref="UiAssets.Merge"/>, но ролик опознаётся одним
    /// именем: имена роликов уникальны во всех трёх источниках, и игра зовёт ролик только по имени.</summary>
    public static IReadOnlyList<UiVideo> Merge(IEnumerable<UiVideo> baseSet, IEnumerable<UiVideo> overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var over = overlay.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var result = baseSet.Select(v => over.Remove(v.Name, out var replaced) ? replaced : v).ToList();
        result.AddRange(over.Values);
        return result;
    }

    /// <summary>Поиск по имени без учёта регистра: в ресурсах игры имена пишутся как попало
    /// (<c>briefh01.bik</c> на диске, <c>TRANS_MAIN2SINGLE.BIK</c> в архиве).</summary>
    public static IReadOnlyDictionary<string, UiVideo> ByName(IEnumerable<UiVideo> videos)
    {
        ArgumentNullException.ThrowIfNull(videos);
        var map = new Dictionary<string, UiVideo>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in videos) map.TryAdd(v.Name, v);
        return map;
    }
}
