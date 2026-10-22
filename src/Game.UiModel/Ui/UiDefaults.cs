using System.Text.Json;

namespace LastEdict.Data.Ui;

/// <summary>Начальные значения экранов (<c>data/ui/defaults.json</c>): какой участник группы переключателей
/// отмечен, когда экран открывается. Разметка старой игры этого не хранит — значение ставил код экрана, — поэтому
/// оно живёт отдельным файлом, который правится руками и накладывается модами.
///
/// <code>{ "DLG_CHOOSE_LORD": { "RAD_DIFF": "TOG_DIFF_AVERAGE" } }</code>
///
/// Группа, которой здесь нет, открывается с первым участником (<see cref="UiScreenState"/>).</summary>
public sealed class UiDefaults
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    private readonly Dictionary<string, Dictionary<string, string>> _screens;

    private UiDefaults(Dictionary<string, Dictionary<string, string>> screens) => _screens = screens;

    public static UiDefaults Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Группа → отмеченный участник для экрана; пусто, если экрану ничего не задано.</summary>
    public IReadOnlyDictionary<string, string> For(string screen) =>
        _screens.TryGetValue(screen, out var groups) ? groups : None;

    public static UiDefaults FromJson(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json)
                  ?? throw new FormatException("пустые начальные значения экранов");
        return new(raw.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal),
                                    StringComparer.OrdinalIgnoreCase));
    }

    public static UiDefaults Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Наложить мод: группа мода заменяет ту же группу базы, прочие группы экрана остаются.</summary>
    public static UiDefaults Merge(UiDefaults baseSet, UiDefaults overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var result = baseSet._screens.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal),
                                                   StringComparer.OrdinalIgnoreCase);
        foreach (var (screen, groups) in overlay._screens)
        {
            if (!result.TryGetValue(screen, out var target)) result[screen] = target = new(StringComparer.Ordinal);
            foreach (var (group, member) in groups) target[group] = member;
        }
        return new(result);
    }
}
