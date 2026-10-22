using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LastEdict.Data.Content;
using LastEdict.Data.Dbf;

namespace LastEdict.Data.Map;

/// <summary>Слой изо-движка (<c>ImgData/IIsoLay.dbf</c>): имя и номер. Номер — второй ключ порядка отрисовки карты
/// (<c>docs/map-render.md</c> §4).</summary>
public sealed record MapLayer(string Name, int Num);

/// <summary>След ориентира в клетках (<c>Globals/GLmark.dbf</c> <c>CX</c>, <c>CY</c>).</summary>
public sealed record LandmarkSize(string Id, int Cx, int Cy);

/// <summary>Грунты, по которым у юнита бонус хода (<c>Globals/GMabi.dbf</c>, <c>M_ABILITY</c>: 0 равнина, 1 лес,
/// 3 вода). Лидер с бонусом по всем трём рисуется на карте «летуном» (<c>docs/map-render-stacks.md</c> §1).</summary>
public sealed record MoveBonus(string Unit, IReadOnlyList<int> Grounds);

/// <summary>Юнит для экрана карты (<c>Globals/Gunits.dbf</c>): строка имени (<c>NAME_TXT</c> — id в
/// <c>data/ui/strings</c>), запас хода лидера <c>MOVE</c> и обзор <c>SCOUT</c>.</summary>
public sealed record UnitLook(string Unit, string NameTxt, int Move, int Scout);

/// <summary>Сколько картинок у объекта гор размера <paramref name="Size"/> на местности <paramref name="Terrain"/> (<c>HU</c>, <c>DW</c>,
/// <c>HE</c>, <c>UN</c>, <c>NE</c>, <c>EL</c>) — <c>Globals/GTileDBI.dbf</c>, строки <c>GROUND</c> = <c>M</c> (<c>NDX</c> — размер, <c>QTY</c> —
/// число). По нему перекладка гор бросает номер картинки (<c>0x5efd06</c> → <c>0x59ab8d</c>, <c>docs/scenario-events.md</c> §10).</summary>
public sealed record MountainImages(string Terrain, int Size, int Count);

/// <summary>Данные отрисовки карты (<c>data/map/*.json</c>, импорт <c>tools/GameImport</c>). Мод кладёт свои файлы в
/// <c>data/map</c>: запись с тем же ключом заменяет базовую, новая добавляется. Юниты и ориентиры — по id содержимого
/// (<c>base:unit/knight</c>): файлы импорта пишут id оригинала, и они переводятся при сборке набора (<see cref="LegacyIds.Base"/>).</summary>
public sealed class MapArtData
{
    public static MapArtData Empty { get; } = new([], [], [], []);

    public MapArtData(IReadOnlyList<MapLayer> layers, IReadOnlyList<LandmarkSize> landmarks,
                      IReadOnlyList<MoveBonus> moveBonus, IReadOnlyList<UnitLook>? units = null,
                      IReadOnlyList<MountainImages>? mountains = null)
    {
        // Ключи правил — id содержимого; файлы импорта и моды оригинала пишут id оригинала.
        static string Key(string id) => LegacyIds.Base.IdOf(id) ?? id;
        landmarks = [.. landmarks.Select(l => l with { Id = Key(l.Id) })];
        moveBonus = [.. moveBonus.Select(b => b with { Unit = Key(b.Unit) })];
        units = units is null ? null : [.. units.Select(u => u with { Unit = Key(u.Unit) })];
        Mountains = mountains ?? [];
        _mountains = Mountains.ToDictionary(m => (m.Terrain.ToUpperInvariant(), m.Size), m => m.Count);
        Layers = layers;
        Landmarks = landmarks;
        MoveBonuses = moveBonus;
        Units = units ?? [];
        _units = Units.ToDictionary(u => u.Unit, StringComparer.OrdinalIgnoreCase);
        _layers = layers.ToDictionary(l => l.Name, l => l.Num, StringComparer.OrdinalIgnoreCase);
        _landmarks = landmarks.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);
        _bonus = moveBonus.ToDictionary(b => b.Unit, b => b.Grounds, StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, int> _layers;
    private readonly Dictionary<string, LandmarkSize> _landmarks;
    private readonly Dictionary<string, IReadOnlyList<int>> _bonus;

    public IReadOnlyList<MapLayer> Layers { get; }
    public IReadOnlyList<LandmarkSize> Landmarks { get; }
    public IReadOnlyList<MoveBonus> MoveBonuses { get; }

    public IReadOnlyList<UnitLook> Units { get; }

    public IReadOnlyList<MountainImages> Mountains { get; }

    private readonly Dictionary<(string, int), int> _mountains;

    /// <summary>Число картинок гор местности <paramref name="terrain"/> и размера <paramref name="size"/>; нет строки — 0 (у движка
    /// тогда без броска).</summary>
    public int MountainImageCount(string terrain, int size) => _mountains.GetValueOrDefault((terrain.ToUpperInvariant(), size));

    private readonly Dictionary<string, UnitLook> _units;

    /// <summary>Юнит по типу; неизвестный — null.</summary>
    public UnitLook? Unit(string type) => _units.GetValueOrDefault(type);

    /// <summary>Номер слоя по имени; нет слоя — <see cref="KeyNotFoundException"/> с именем.</summary>
    public int Layer(string name) =>
        _layers.TryGetValue(name, out int num) ? num : throw new KeyNotFoundException($"нет слоя карты {name} в data/map/layers.json");

    /// <summary>Есть ли тип ориентира в таблице (<c>GLmark</c>).</summary>
    public bool HasLandmark(string id) => _landmarks.ContainsKey(id);

    /// <summary>След ориентира; неизвестный — 1×1.</summary>
    public (int Cx, int Cy) LandmarkFootprint(string id) =>
        _landmarks.TryGetValue(id, out var s) ? (s.Cx, s.Cy) : (1, 1);

    /// <summary>Бонус хода по равнине, лесу и воде сразу.</summary>
    public bool IsFlyer(string unit) =>
        _bonus.TryGetValue(unit, out var g) && g.Contains(0) && g.Contains(1) && g.Contains(3);

    // ---------------------------------------------------------------- импорт и файлы

    /// <summary>Из копии игры: <c>ImgData/IIsoLay.dbf</c>, <c>Globals/GLmark.dbf</c>, <c>Globals/GMabi.dbf</c>.
    /// Идентификаторы — в верхнем регистре, списки упорядочены: одни и те же таблицы дают тот же JSON.</summary>
    public static MapArtData Import(string gameDir)
    {
        var layers = DbfTable.Read(Path.Combine(gameDir, "ImgData", "IIsoLay.dbf")).Rows
            .Select(r => new MapLayer(r.GetString("ID").Trim().ToUpperInvariant(), r.GetInt("NUM")))
            .OrderBy(l => l.Num).ThenBy(l => l.Name, StringComparer.Ordinal).ToList();
        var landmarks = DbfTable.Read(Path.Combine(gameDir, "Globals", "GLmark.dbf")).Rows
            .Select(r => new LandmarkSize(r.GetString("LMARK_ID").Trim().ToUpperInvariant(), r.GetInt("CX"), r.GetInt("CY")))
            .OrderBy(l => l.Id, StringComparer.Ordinal).ToList();
        var bonus = DbfTable.Read(Path.Combine(gameDir, "Globals", "GMabi.dbf")).Rows
            .GroupBy(r => r.GetString("UNIT_ID").Trim().ToUpperInvariant())
            .Select(g => new MoveBonus(g.Key, [.. g.Select(r => r.GetInt("M_ABILITY")).Distinct().Order()]))
            .OrderBy(b => b.Unit, StringComparer.Ordinal).ToList();
        var units = DbfTable.Read(Path.Combine(gameDir, "Globals", "Gunits.dbf")).Rows
            .Select(r => new UnitLook(r.GetString("UNIT_ID").Trim().ToUpperInvariant(),
                                      r.GetString("NAME_TXT").Trim().ToUpperInvariant(), r.GetInt("MOVE"), r.GetInt("SCOUT")))
            .OrderBy(u => u.Unit, StringComparer.Ordinal).ToList();
        var mountains = DbfTable.Read(Path.Combine(gameDir, "Globals", "GTileDBI.dbf")).Rows
            .Where(r => r.GetString("GROUND").Trim().Equals("M", StringComparison.OrdinalIgnoreCase))
            .Select(r => new MountainImages(r.GetString("TERRAIN").Trim().ToUpperInvariant(), r.GetInt("NDX"), r.GetInt("QTY")))
            .OrderBy(m => m.Terrain, StringComparer.Ordinal).ThenBy(m => m.Size).ToList();
        return new MapArtData(layers, landmarks, bonus, units, mountains);
    }

    public const string LayersFile = "layers.json", LandmarksFile = "landmarks.json", MoveBonusFile = "move-bonus.json",
                        UnitsFile = "units.json", MountainsFile = "mountains.json";

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, LayersFile), ToJson(Layers));
        File.WriteAllText(Path.Combine(dir, LandmarksFile), ToJson(Landmarks));
        File.WriteAllText(Path.Combine(dir, MoveBonusFile), ToJson(MoveBonuses));
        File.WriteAllText(Path.Combine(dir, UnitsFile), ToJson(Units));
        File.WriteAllText(Path.Combine(dir, MountainsFile), ToJson(Mountains));
    }

    /// <summary>Из <paramref name="dir"/>; нет файла — пусто.</summary>
    public static MapArtData Load(string dir) => new(
        Read<MapLayer>(Path.Combine(dir, LayersFile)),
        Read<LandmarkSize>(Path.Combine(dir, LandmarksFile)),
        Read<MoveBonus>(Path.Combine(dir, MoveBonusFile)),
        Read<UnitLook>(Path.Combine(dir, UnitsFile)),
        Read<MountainImages>(Path.Combine(dir, MountainsFile)));

    /// <summary>Наложить мод: слой — по имени, ориентир и юнит — по id, картинки гор — по местности и размеру.</summary>
    public static MapArtData Merge(MapArtData baseSet, MapArtData overlay) => new(
        Overlay(baseSet.Layers, overlay.Layers, l => l.Name),
        Overlay(baseSet.Landmarks, overlay.Landmarks, l => l.Id),
        Overlay(baseSet.MoveBonuses, overlay.MoveBonuses, b => b.Unit),
        Overlay(baseSet.Units, overlay.Units, u => u.Unit),
        Overlay(baseSet.Mountains, overlay.Mountains, m => $"{m.Terrain}:{m.Size}"));

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson<T>(IReadOnlyList<T> items) =>
        JsonSerializer.Serialize(items, Json).ReplaceLineEndings("\n") + "\n";

    private static IReadOnlyList<T> Read<T>(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Json) ?? throw new FormatException($"{path}: пусто")
            : [];

    private static IReadOnlyList<T> Overlay<T>(IReadOnlyList<T> baseSet, IReadOnlyList<T> overlay, Func<T, string> key)
    {
        var over = overlay.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        var result = baseSet.Select(b => over.Remove(key(b), out var o) ? o : b).ToList();
        result.AddRange(overlay.Where(o => over.ContainsKey(key(o))));
        return result;
    }
}
