using System.Text.Json;

namespace D2.Data.Ui;

/// <summary>Ролик смены экранов: с какого экрана на какой и что играет. В <see cref="Video"/> может стоять
/// <c>{RACE}</c> — код выбранной расы (<c>TRANS_GOD2LORD_{RACE}</c> → <c>TRANS_GOD2LORD_UN</c>).</summary>
public sealed record UiTransitionVideo(string From, string To, string Video);

/// <summary>Ролики смены экранов меню (<c>data/ui/transitions.json</c>). Граф знает, что смена идёт через
/// промежуточный экран (<c>Via = DLG_MENU_TRANS</c>), но не знает, какой ролик на нём играет: игра выбирала его
/// кодом. Здесь это данные — мод может заменить ролик пары экранов или добавить свой. Пары, которой нет, — смена
/// без ролика (так в игре «Назад»).</summary>
public sealed class UiTransitions
{
    private sealed record File(IReadOnlyList<string>? Sources, IReadOnlyList<UiTransitionVideo>? Transitions);

    private readonly Dictionary<(string, string), string> _videos;

    private UiTransitions(Dictionary<(string, string), string> videos) => _videos = videos;

    public static UiTransitions Empty { get; } = new([]);

    public int Count => _videos.Count;

    /// <summary>Ролик смены <paramref name="from"/> → <paramref name="to"/>, с подставленным кодом расы;
    /// <c>null</c> — смена без ролика.</summary>
    public string? VideoFor(string from, string to, string raceCode = "") =>
        _videos.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var video)
            ? video.Replace("{RACE}", raceCode, StringComparison.Ordinal)
            : null;

    public static UiTransitions FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<File>(json) ?? throw new FormatException("пустой список роликов смены");
        return new((file.Transitions ?? []).ToDictionary(t => (t.From.ToUpperInvariant(), t.To.ToUpperInvariant()), t => t.Video));
    }

    public static UiTransitions Load(string path) => FromJson(System.IO.File.ReadAllText(path));

    /// <summary>Наложить мод: пара экранов мода заменяет ту же пару базы.</summary>
    public static UiTransitions Merge(UiTransitions baseSet, UiTransitions overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var result = new Dictionary<(string, string), string>(baseSet._videos);
        foreach (var (key, video) in overlay._videos) result[key] = video;
        return new(result);
    }
}
