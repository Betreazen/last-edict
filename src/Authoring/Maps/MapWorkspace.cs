using System.Text.Json.Nodes;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Diagnostics;
using LastEdict.Data.Play;

namespace LastEdict.Authoring.Maps;

/// <summary>Кисть редактора карт: грунт (равнина, лес, вода, горы) или земля расы (владелец клетки).</summary>
public enum MapBrush { Plain = 0, Forest = 1, Water = 3, Mountain = 4, Land = 8 }

/// <summary>Правка карты в истории: мазок кисти или правка объектов.</summary>
public interface IMapEdit
{
    string Name { get; }
}

/// <summary>Мазок кисти — одна команда отмены: клетки и их байты до и после.</summary>
public sealed record MapStroke(IReadOnlyList<(int Index, byte Before, byte After)> Cells) : IMapEdit
{
    public string Name => $"мазок ({Cells.Count} клеток)";
}

/// <summary>Правка объектов — одна команда отмены: объектная часть сценария до и после (без клеток и тумана — они большие и правкой
/// объектов не меняются).</summary>
public sealed record MapObjectEdit(string Name, JsonObject Before, JsonObject After) : IMapEdit;

/// <summary>
/// Рабочее пространство Студии «Карта» (Э6, Р6, Р16; объекты — Студия 2): карта мода любой стороны (квадрат, без потолка площади) — новая
/// (<see cref="MapFactory"/>) или из <c>data/scenarios</c> мода; кисть грунта и земли расы радиусом 0…3 (1/3/5/7 клеток, как у ScenEdit),
/// мазок за время зажатой кнопки — одна команда отмены (меняются только задетые клетки: на 1024² отмена не копирует карту); объекты палитры
/// (<see cref="MapObjects"/>: столица, город, руина, лавки, кристалл, мешок, ориентир, отряд) — поставить, сдвинуть, убрать, свойства; одна
/// история с мазками. Запись — в <c>data/scenarios/&lt;id&gt;.json</c> мода и карточка в <c>data/game/scenarios.json</c> мода. Вид — картинка
/// карты клетка-в-точку (<see cref="Rgba"/>), окно Студии обновляет только задетые клетки (<see cref="TakeDirty"/>) и рисует объекты метками.
/// </summary>
public sealed class MapWorkspace(StudioSession session)
{
    private readonly Stack<IMapEdit> _undo = new(), _redo = new();
    private readonly Dictionary<int, byte> _strokeBefore = [];
    private readonly HashSet<int> _dirty = [];
    private JsonObject? _doc;
    private IReadOnlyList<MapObject>? _objects;

    public StudioSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

    public string? Id { get; private set; }

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

    /// <summary>Выбранный объект (свойства — в окне); нет — null.</summary>
    public string? Selected { get; private set; }

    private ContentSet Content => Session.Report.Content ?? Session.BaseContent;

    /// <summary>Объекты открытой карты.</summary>
    public IReadOnlyList<MapObject> Objects => _objects ??= _doc is null ? [] : new MapObjects(_doc, Content).All();

    /// <summary>Объекты карты для чтения свойств (место, отряд, игроки); карта не открыта — null.</summary>
    public MapObjects? Reader => _doc is null ? null : new MapObjects(_doc, Content);

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

    /// <summary>Наибольшая сторона новой карты в Студии: сборка карты идёт в окне, а 2048² — уже ~4 млн клеток и десятки МБ сценария.</summary>
    public const int MaxNewSide = 2048;

    /// <summary>Новая карта стороны <paramref name="size"/> на содержимом мода (с несохранёнными правками). Неверный id, занятый id, карта мала
    /// для игроков — отказ сессии (<see cref="StudioSession.Refusal"/>), прежняя карта остаётся.</summary>
    public bool New(string id, int size, IReadOnlyList<MapSeat> seats, int seed = 1)
    {
        if (!ValidId(id)) return Session.Refuse($"id карты «{id}» — только строчные латинские буквы, цифры и «-»");
        if (Maps.Contains(id, StringComparer.OrdinalIgnoreCase)) return Session.Refuse($"карта «{id}» в моде уже есть — откройте её или возьмите другой id");
        if (size > MaxNewSide) return Session.Refuse($"сторона {size} — больше {MaxNewSide} новая карта в Студии не собирается");
        try
        {
            Load(MapFactory.Create(Content, new NewMapOptions(id, size, seats, seed)));
        }
        catch (ArgumentException e)
        {
            return Session.Refuse(e.Message);
        }
        Dirty = true;
        return true;
    }

    /// <summary>Открыть карту мода; нет или не читается — отказ сессии, прежняя карта остаётся.</summary>
    public bool Open(string id)
    {
        if (!ValidId(id)) return Session.Refuse($"id карты «{id}» — только строчные латинские буквы, цифры и «-»");
        try
        {
            Load(ScenarioFile.Read(Session.Project.PathOf(Path.Combine("data", "scenarios", id + ScenarioFile.Extension))));
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return Session.Refuse($"карта «{id}» не открылась — {e.Message}");
        }
    }

    private static bool ValidId(string id) => id.Length > 0 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private void Load(ScenarioData scenario)
    {
        Id = scenario.Id;
        _doc = JsonNode.Parse(ScenarioFile.ToJson(scenario)) as JsonObject ?? throw new InvalidDataException("сценарий — не объект JSON");
        var map = scenario.Looks.Map;
        Size = map.Size;
        Tiles = new byte[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++) Tiles[(y * Size) + x] = map.Tiles[x, y].Raw;
        _undo.Clear();
        _redo.Clear();
        _dirty.Clear();
        _strokeBefore.Clear();
        _objects = null;
        Selected = null;
        Changed = 0;
        Dirty = false;
        Revision++;
    }

    // ---- кисть

    /// <summary>Начало мазка; незакрытый прежний (кнопку отпустили вне холста) закрывается — его клетки не теряются из отмены.</summary>
    public void StrokeBegin() => StrokeEnd();

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
                Dirty = true;
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

    public bool Undo()
    {
        StrokeEnd();
        return Swap(_undo, _redo, back: true);
    }

    public bool Redo()
    {
        StrokeEnd();
        return Swap(_redo, _undo, back: false);
    }

    private bool Swap(Stack<IMapEdit> from, Stack<IMapEdit> to, bool back)
    {
        if (from.Count == 0) return false;
        var edit = from.Pop();
        switch (edit)
        {
            case MapStroke stroke:
                foreach (var (i, before, after) in stroke.Cells)
                {
                    Tiles[i] = back ? before : after;
                    _dirty.Add(i);
                }
                break;
            case MapObjectEdit objects:
                Restore(back ? objects.Before : objects.After);
                break;
        }
        to.Push(edit);
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

    // ---- объекты

    /// <summary>Части сценария, которые меняет правка объектов (снимок для отмены): всё, кроме клеток, тумана и картинок деревьев.</summary>
    private static readonly string[] MapParts = ["Stacks", "Units", "Objects", "Plan", "Sites", "Players", "ItemTypes", "Subraces", "Relations"];

    private static readonly string[] LookParts = ["Forts", "Ruins", "Sites", "Crystals", "Bags", "Landmarks", "Stacks", "Banners"];

    private JsonObject Snapshot()
    {
        var looks = (JsonObject)_doc!["Looks"]!;
        var map = (JsonObject)looks["Map"]!;
        return new JsonObject
        {
            ["Map"] = new JsonObject(MapParts.Select(k => KeyValuePair.Create(k, map[k]?.DeepClone()))),
            ["Looks"] = new JsonObject(LookParts.Select(k => KeyValuePair.Create(k, looks[k]?.DeepClone()))),
            ["Events"] = _doc["Events"]?.DeepClone(),
        };
    }

    private void Restore(JsonObject snapshot)
    {
        var looks = (JsonObject)_doc!["Looks"]!;
        var map = (JsonObject)looks["Map"]!;
        foreach (var (k, v) in (JsonObject)snapshot["Map"]!) map[k] = v?.DeepClone();
        foreach (var (k, v) in (JsonObject)snapshot["Looks"]!) looks[k] = v?.DeepClone();
        _doc["Events"] = snapshot["Events"]?.DeepClone();
        _objects = null;
        if (Selected is { } s && !Objects.Any(o => o.Id == s)) Selected = null;
    }

    /// <summary>Правка объектов одной командой отмены; отказ (<see cref="InvalidOperationException"/>) — карта прежняя, текст — в сессии.</summary>
    private bool Edit(string name, Action<MapObjects> change)
    {
        if (_doc is null) return Session.Refuse("карта не открыта — новая или из мода");
        StrokeEnd();
        var before = Snapshot();
        try
        {
            change(new MapObjects(_doc, Content));
        }
        catch (InvalidOperationException e)
        {
            Restore(before);
            return Session.Refuse(e.Message);
        }
        _undo.Push(new MapObjectEdit(name, before, Snapshot()));
        _redo.Clear();
        _objects = null;
        Dirty = true;
        Revision++;
        return true;
    }

    /// <summary>Правка карты одной командой отмены: игроки, события, области (<see cref="MapObjects"/>); отказ — текст в сессии.</summary>
    public bool Change(string name, Action<MapObjects> change) => Edit(name, change);

    public void Select(string? id)
    {
        Selected = id;
        Revision++;
    }

    /// <summary>Поставить объект палитры; он становится выбранным.</summary>
    public bool Place(MapObjectKind kind, int x, int y, MapPlacement placement)
    {
        string? id = null;
        if (!Edit($"поставить {kind} в {x},{y}", m => id = m.Place(kind, x, y, placement))) return false;
        Select(id);
        return true;
    }

    public bool Remove(string id)
    {
        if (!Edit($"убрать {id}", m => m.Remove(id))) return false;
        if (Selected == id) Select(null);
        return true;
    }

    public bool Move(string id, int x, int y) => Edit($"сдвинуть {id} в {x},{y}", m => m.Move(id, x, y));

    /// <summary>Поле места (имя, описание, золото, ресурс, заклинания мага, товар…).</summary>
    public bool SetSite(string id, string field, JsonNode? value) => Edit($"{field} у {id}", m => m.SetSite(id, field, value));

    /// <summary>Отряд: состав, приказ, хозяин.</summary>
    public bool SetStack(string id, IReadOnlyList<string>? units = null, int? order = null, string? owner = null) =>
        Edit($"отряд {id}", m => m.SetStack(id, units, order, owner));

    /// <summary>Нарушения инвариантов карты (<see cref="MapInvariants"/>): сценарий собирается из документа и проверяется по правилам мода.
    /// Не читается — одна строка с причиной.</summary>
    public IReadOnlyList<string> Check()
    {
        if (_doc is null) return [];
        try
        {
            var scenario = ScenarioFile.FromJson(Json());
            return [.. MapInvariants.Check(scenario.Looks.Map, GameBook.Load(Content)).Select(v => v.ToString())];
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            return [$"карта не собирается: {e.Message}"];
        }
    }

    /// <summary>Сценарий JSON для записи: клетки — из <see cref="Tiles"/>, туман — у каждого игрока карты (новым — пустой).</summary>
    private string Json()
    {
        var map = (JsonObject)_doc!["Looks"]!["Map"]!;
        ((JsonObject)map[nameof(Tiles)]!)["data"] = Convert.ToBase64String(Tiles);
        var fog = map["Fog"] as JsonObject ?? (JsonObject)(map["Fog"] = new JsonObject());
        foreach (var player in ((JsonObject)map["Players"]!).Select(p => p.Value).OfType<JsonObject>())
            if (player["FogId"]?.GetValue<string>() is { Length: > 0 } fogId && !fog.ContainsKey(fogId))
                fog[fogId] = new JsonObject { ["w"] = Size, ["h"] = Size, ["data"] = Convert.ToBase64String(new byte[Size * Size]) };
        return _doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }

    // ---- запись

    /// <summary>Записать карту в мод: сценарий и карточка в каталоге мода. Байт файла сценария — как у <see cref="ScenarioFile"/>.</summary>
    public bool Save(string? name = null)
    {
        try
        {
            SaveCore(name);
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Session.Refuse(e.Message);
        }
    }

    private void SaveCore(string? name)
    {
        if (_doc is null || Id is null) return;
        StrokeEnd();
        // Каталог мода — сначала разобрать и проверить: битый или не список — отказ до записи карты (ничего не записано наполовину).
        string catalog = Path.Combine("data", "game", "scenarios.json");
        JsonArray cards;
        if (File.Exists(Session.Project.PathOf(catalog)))
        {
            try
            {
                cards = JsonNode.Parse(File.ReadAllText(Session.Project.PathOf(catalog))) as JsonArray
                        ?? throw new InvalidDataException($"{catalog} мода — не список карточек: карта не записана");
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new InvalidDataException($"{catalog} мода не читается — {e.Message}: карта не записана", e);
            }
        }
        else cards = [];
        string json = Json();
        var scenario = ScenarioFile.FromJson(json);   // документ обязан читаться игрой — иначе отказ до записи
        Session.Project.Write(Path.Combine("data", "scenarios", Id + ScenarioFile.Extension), ScenarioFile.ToJson(scenario));   // вид файла — как у ScenarioFile
        var card = cards.OfType<JsonObject>().FirstOrDefault(c => c[nameof(ScenarioData.Id)] is JsonValue v && v.TryGetValue<string>(out var id) && id == Id);
        if (card is null) cards.Add(card = new JsonObject { [nameof(ScenarioData.Id)] = Id });
        card["File"] = Id + ScenarioFile.Extension;
        card["Name"] = name ?? card["Name"]?.GetValue<string>() ?? Id;
        card["MapSize"] = Size;
        card["RaceIds"] = new JsonArray([.. scenario.Looks.Map.Players.Values.Select(p => (JsonNode)JsonValue.Create(p.RaceId)!)]);
        Session.Project.Write(catalog, cards.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        Dirty = false;
        Revision++;
    }

    public string Line() =>
        $"map: id={Id ?? "-"} size={Size} cells={Size * Size} changed={Changed} undo={_undo.Count} redo={_redo.Count} dirty={(Dirty ? 1 : 0)} " +
        $"objects={Objects.Count} selected={Selected ?? "-"}";
}
