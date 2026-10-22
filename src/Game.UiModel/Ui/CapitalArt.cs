using System.Text.Json;

namespace D2.Data.Ui;

/// <summary>Слой картинки столицы: кадр или анимация архива <c>Capital</c> в точке (<see cref="X"/>, <see cref="Y"/>)
/// экрана 640×600; <see cref="Building"/> — постройка, без которой слоя нет (пусто — слой есть всегда);
/// <see cref="Hotspot"/> — место постройки под мышью.</summary>
public sealed record CapitalLayer(int Z, string Image, int X, int Y, string? Building = null, UiRect? Hotspot = null);

/// <summary>Слои столицы расы (<c>Grace.RACE_TYPE</c>) по порядку наложения.</summary>
public sealed record CapitalRace(string Race, int RaceType, IReadOnlyList<CapitalLayer> Layers);

/// <summary>
/// Картинка столицы (<c>data/ui/capital.json</c>, импорт <c>tools/ui-assets/import_capital.py</c> из <c>capital.dat</c>
/// игры): стопка слоёв по номеру, слой постройки виден, когда она построена.
/// ponytail: ранг 2 — порядок «меньший номер ниже» и x, y — по полям файла, отрисовку движка не разбирали.
/// </summary>
public sealed class CapitalArt(IReadOnlyList<CapitalRace> races)
{
    public const string Archive = "Capital";

    private sealed record File(IReadOnlyList<CapitalRace> Races);

    public IReadOnlyList<CapitalRace> Races { get; } = races;

    public static CapitalArt Load(string path) =>
        new(JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path))?.Races
            ?? throw new FormatException($"{path}: нет рас"));

    /// <summary>Видимые слои столицы расы <paramref name="raceType"/> при постройках <paramref name="buildings"/>, снизу
    /// вверх. Нет такой расы — пусто.</summary>
    public IReadOnlyList<CapitalLayer> Visible(int raceType, IEnumerable<string> buildings)
    {
        var built = buildings.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var race = Races.FirstOrDefault(r => r.RaceType == raceType);
        return race is null ? [] : [.. race.Layers.Where(l => l.Building is null || built.Contains(l.Building)).OrderBy(l => l.Z)];
    }
}
