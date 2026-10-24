using System.Text.Json.Nodes;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;

namespace LastEdict.Authoring.Maps;

/// <summary>Кисть редактора карт: грунт (равнина, лес, вода, горы) или земля расы (владелец клетки).</summary>
public enum MapBrush { Plain = 0, Forest = 1, Water = 3, Mountain = 4, Land = 8 }

/// <summary>Мазок кисти — одна команда отмены: клетки и их байты до и после.</summary>
public sealed record MapStroke(IReadOnlyList<(int Index, byte Before, byte After)> Cells);

/// <summary>
/// Рабочее пространство Студии «Карта» (Э6, Р6, Р16): карта мода любой стороны (квадрат, без потолка площади) — новая
/// (<see cref="MapFactory"/>) или из <c>data/scenarios</c> мода; кисть грунта и земли расы радиусом 0…3 (1/3/5/7 клеток, как у ScenEdit),
/// мазок за время зажатой кнопки — одна команда отмены (меняются только задетые клетки: на 1024² отмена не копирует карту); запись — в
/// <c>data/scenarios/&lt;id&gt;.json</c> мода и карточка в <c>data/game/scenarios.json</c> мода (игра видит карту в списке сценариев).
/// Вид — картинка карты клетка-в-точку (<see cref="Rgba"/>), окно Студии обновляет только задетые клетки (<see cref="TakeDirty"/>).
/// </summary>
public sealed class MapWorkspace(StudioSession session)
{
    private readonly Stack<MapStroke> _undo = new(), _redo = new();
    private readonly List<(int Index, byte Before, byte After)> _stroke = [];
    private readonly Dictionary<int, byte> _strokeBefore = [];
    private readonly HashSet<int> _dirty = [];
    private ScenarioData? _scenario;

    public StudioSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

    public string? Id => _scenario?.Id;

    public int Size { get; private set; }

    /// <summary>Клетки карты (индекс <c>y·N + x</c>): биты 0–2 — земля расы, 3–5 — грунт.</summary>
    public byte[] Tiles { get; private set; } = [];

    public MapBrush Brush { get; set; } = MapBrush.Forest;

    /// <summary>Земля кисти «земля расы» (<see cref="TerrainKind"/>).</summary>
    public TerrainKind Land { get; set; } = TerrainKind.Human;

    /// <summary>Радиус кисти 0…3: стороны 1, 3, 5, 7 клеток.</summary>
    public int Radius { get; set; } = 1;

    public int Revision { get; private set; }

    public bool Dirty { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Клеток, изменённых за всё время правки (для строки проверки).</summary>
    public int Changed { get; private set; }

    /// <summary>Карты мода (<c>data/scenarios</c>).</summary>
    public IReadOnlyList<string> Maps
    {
        get
        {
            string dir = Path.Combine(Session.Project.Dir, "data", "scenarios");
            return Directory.Exists(dir)
                ? [.. Directory.GetFiles(dir, "*" + ScenarioFile.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)]
                : [];
        }
    }

    /// <summary>Новая карта стороны <paramref name="size"/> на содержимом мода (с несохранёнными правками).</summary>
    public void New(string id, int size, IReadOnlyList<MapSeat> seats, int seed = 1)
    {
        var content = Session.Report.Content ?? Session.BaseContent;
        Load(MapFactory.Create(content, new NewMapOptions(id, size, seats, seed)));
        Dirty = true;
    }

    public void Open(string id) => Load(ScenarioFile.Read(Session.Project.PathOf(Path.Combine("data", "scenarios", id + ScenarioFile.Extension))));

    private void Load(ScenarioData scenario)
    {
        _scenario = scenario;
        var map = scenario.Looks.Map;
        Size = map.Size;
        Tiles = new byte[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++) Tiles[(y * Size) + x] = map.Tiles[x, y].Raw;
        _undo.Clear();
        _redo.Clear();
        _dirty.Clear();
        Changed = 0;
        Dirty = false;
        Revision++;
    }

    // ---- кисть

    public void StrokeBegin()
    {
        _stroke.Clear();
        _strokeBefore.Clear();
    }

    /// <summary>Мазок в клетке: квадрат радиуса кисти.</summary>
    public void Paint(int x, int y)
    {
        if (Size == 0) return;
        for (int dy = -Radius; dy <= Radius; dy++)
            for (int dx = -Radius; dx <= Radius; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (cx < 0 || cy < 0 || cx >= Size || cy >= Size) continue;
                int i = (cy * Size) + cx;
                byte was = Tiles[i];
                byte now = Brush == MapBrush.Land ? (byte)((was & ~0b111) | (int)Land) : (byte)((was & 0b111) | ((int)Brush << 3));
                if (now == was) continue;
                _strokeBefore.TryAdd(i, was);
                Tiles[i] = now;
                _dirty.Add(i);
            }
        Revision++;
    }

    /// <summary>Конец мазка: задетые клетки — одна команда отмены.</summary>
    public void StrokeEnd()
    {
        if (_strokeBefore.Count == 0) return;
        var stroke = new MapStroke([.. _strokeBefore.Select(p => (p.Key, p.Value, Tiles[p.Key]))]);
        _undo.Push(stroke);
        _redo.Clear();
        Changed += stroke.Cells.Count;
        Dirty = true;
        _strokeBefore.Clear();
        Revision++;
    }

    public bool Undo() => Swap(_undo, _redo, back: true);

    public bool Redo() => Swap(_redo, _undo, back: false);

    private bool Swap(Stack<MapStroke> from, Stack<MapStroke> to, bool back)
    {
        if (from.Count == 0) return false;
        var stroke = from.Pop();
        foreach (var (i, before, after) in stroke.Cells)
        {
            Tiles[i] = back ? before : after;
            _dirty.Add(i);
        }
        to.Push(stroke);
        Dirty = true;
        Revision++;
        return true;
    }

    /// <summary>Клетки, изменённые с прошлого вызова (окно перекрашивает только их).</summary>
    public IReadOnlyList<int> TakeDirty()
    {
        var cells = _dirty.ToList();
        _dirty.Clear();
        return cells;
    }

    /// <summary>Цвет клетки для вида: грунт (лес темнее, вода синяя, горы серые), земля расы — оттенок равнины.</summary>
    public static uint Rgba(byte tile)
    {
        int land = tile & 0b111, ground = (tile >> 3) & 0b111;
        (byte r, byte g, byte b) = ground switch
        {
            1 => ((byte)40, (byte)86, (byte)38),
            3 => ((byte)36, (byte)72, (byte)128),
            4 => ((byte)112, (byte)108, (byte)102),
            _ => land switch
            {
                1 => ((byte)150, (byte)160, (byte)96), 2 => ((byte)170, (byte)170, (byte)180), 3 => ((byte)140, (byte)82, (byte)60),
                4 => ((byte)92, (byte)84, (byte)110), 6 => ((byte)110, (byte)150, (byte)80), _ => ((byte)132, (byte)128, (byte)84),
            },
        };
        return (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);
    }

    /// <summary>Записать карту в мод: сценарий и карточка в каталоге мода. Байт файла сценария — как у <see cref="ScenarioFile"/>.</summary>
    public void Save(string? name = null)
    {
        if (_scenario is not { } scenario) return;
        var root = JsonNode.Parse(ScenarioFile.ToJson(scenario))!;
        root[nameof(ScenarioData.Looks)]!["Map"]![nameof(Tiles)]!["data"] = Convert.ToBase64String(Tiles);
        Session.Project.Write(Path.Combine("data", "scenarios", scenario.Id + ScenarioFile.Extension), root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        string catalog = Path.Combine("data", "game", "scenarios.json");
        var cards = File.Exists(Session.Project.PathOf(catalog)) ? JsonNode.Parse(File.ReadAllText(Session.Project.PathOf(catalog))) as JsonArray ?? [] : [];
        var card = cards.OfType<JsonObject>().FirstOrDefault(c => c[nameof(ScenarioData.Id)]?.GetValue<string>() == scenario.Id);
        if (card is null) cards.Add(card = new JsonObject { [nameof(ScenarioData.Id)] = scenario.Id });
        card["File"] = scenario.Id + ScenarioFile.Extension;
        card["Name"] = name ?? card["Name"]?.GetValue<string>() ?? scenario.Id;
        card["MapSize"] = Size;
        card["RaceIds"] = new JsonArray([.. scenario.Looks.Map.Players.Values.Select(p => (JsonNode)JsonValue.Create(p.RaceId)!)]);
        Session.Project.Write(catalog, cards.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        Dirty = false;
        Revision++;
    }

    public string Line() =>
        $"map: id={Id ?? "-"} size={Size} cells={Size * Size} changed={Changed} undo={_undo.Count} redo={_redo.Count} dirty={(Dirty ? 1 : 0)}";
}
