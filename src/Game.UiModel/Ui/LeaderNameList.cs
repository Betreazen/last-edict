using System.Text.Json;

namespace LastEdict.Data.Ui;

/// <summary>
/// Имена героев по умолчанию на языке интерфейса (<c>data/ui/leader-names.{язык}.json</c>, импорт
/// <c>tools/ui-assets/import_leader_names.py</c> из <c>Tleader.dbf</c>): по расе — мужские и женские.
/// </summary>
public sealed class LeaderNameList
{
    private readonly Dictionary<string, (IReadOnlyList<string> Male, IReadOnlyList<string> Female)> _races;

    private LeaderNameList(Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)> races) => _races = races;

    /// <summary>Список языка <paramref name="language"/>; файла нет — пустой.</summary>
    public static LeaderNameList Load(string uiDir, string language)
    {
        var races = new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(uiDir, $"leader-names.{language}.json");
        if (!File.Exists(path)) return new(races);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var race in doc.RootElement.GetProperty("Races").EnumerateObject())
            races[race.Name] = (Names(race.Value, "Male"), Names(race.Value, "Female"));
        return new(races);

        static IReadOnlyList<string> Names(JsonElement e, string key) =>
            e.TryGetProperty(key, out var list) ? [.. list.EnumerateArray().Select(n => n.GetString() ?? "").Where(n => n.Length > 0)] : [];
    }

    /// <summary>
    /// Имя нового героя (<c>0x4b02f3</c>): имена расы того же пола; из тех, что реже всего носят лидеры на карте
    /// (<paramref name="taken"/>), — случайное (<paramref name="random"/> — неотрицательное число). Имён нет — null.
    /// </summary>
    public string? Suggest(string raceId, bool male, IEnumerable<string> taken, Func<int> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!_races.TryGetValue(raceId, out var race)) return null;
        var names = male ? race.Male : race.Female;
        if (names.Count == 0) return null;
        var counts = (taken ?? []).GroupBy(n => n.Trim()).ToDictionary(g => g.Key, g => g.Count());
        int least = names.Min(n => counts.GetValueOrDefault(n));
        var pool = names.Where(n => counts.GetValueOrDefault(n) == least).ToList();
        return pool[(random() & int.MaxValue) % pool.Count];
    }
}
