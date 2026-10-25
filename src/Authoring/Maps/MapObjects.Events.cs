using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Authoring.Maps;

/// <summary>Событие для вида: id, имя, включено, раз, шанс, число условий и действий.</summary>
public sealed record MapEventRow(string Id, string Name, bool Enabled, bool Once, int Chance, int Conditions, int Effects);

/// <summary>
/// Игроки и события карты (Студия 2, задача 5; вехи E3–E4): игрок — раса, лорд, имя, человек или ИИ, золото; новый игрок — с подрасой,
/// отношениями ко всем и туманом при записи; убрать — только без своих объектов. События — виды условий и действий берутся из атрибутов
/// <see cref="JsonDerivedTypeAttribute"/> записей правил (<see cref="EventCondition"/>, <see cref="EventEffect"/>): новый вид появляется в
/// Студии без кода; новое условие или действие — запись по умолчанию (строки пусты, числа 0), поля правятся значениями JSON. Области
/// (<c>Zones</c>) — для условий «в области».
/// </summary>
public sealed partial class MapObjects
{
    private JsonObject PlayersNode => Section("Players");

    /// <summary>Добавить игрока: раса, лорд (нет — первый лорд расы), имя; отношения ко всем — нейтральные, подраса — своя. Возвращает id.</summary>
    public string AddPlayer(string race, string? lord = null, string? name = null, bool human = false)
    {
        if (content.Find(race) is not RaceDef raceDef) throw new InvalidOperationException($"расы «{race}» нет в содержимом");
        string? lordId = lord ?? content.Lords.FirstOrDefault(l => l.Race == race)?.Id;
        if (lordId is not null && content.Find(lordId) is not LordDef) throw new InvalidOperationException($"лорда «{lordId}» нет");
        string id = NewPlayerId();
        // Сторона дипломатии: у игроков без своей — род расы (как у правил, Diplomacy.PartyOf); новому — число, не занятое никем.
        var parties = PlayersNode.Select(p => p.Value as JsonObject).OfType<JsonObject>().Select(PartyOf).OfType<int>().ToList();
        int party = Math.Max(19, parties.DefaultIfEmpty(0).Max()) + 1;
        var bank = Zero();
        bank["Gold"] = raceDef.Neutral == true ? 0 : 600;
        PlayersNode[id] = new JsonObject
        {
            ["Id"] = id, ["RaceId"] = race, ["LordId"] = lordId ?? "", ["Name"] = name ?? race[(race.LastIndexOf('/') + 1)..], ["Face"] = 0, ["IsHuman"] = human,
            ["Bank"] = bank, ["FogId"] = $"{Prefix}FG{id[^4..]}", ["BuildingsId"] = $"{Prefix}PB{id[^4..]}", ["Buildings"] = new JsonArray(),
            ["SpellBank"] = Zero(), ["QtyBreaks"] = 0, ["Attitude"] = 1, ["CapturedBy"] = "", ["ConstructionTurn"] = 0, ["ResearchTurn"] = 0,
            ["KnownSpells"] = new JsonArray(), ["Spied"] = new JsonArray(), ["Party"] = party,
        };
        var relations = List(MapNode, "Relations");
        foreach (int other in parties.Distinct())
            relations.Add(new JsonObject { ["Race1"] = other, ["Race2"] = party, ["Raw"] = 0 });
        _ = SubraceOf(id);
        return id;
    }

    /// <summary>Сторона игрока: своя (<c>Party</c>) или род расы; расы нет в содержимом — null.</summary>
    private int? PartyOf(JsonObject player) =>
        player["Party"]?.GetValue<int>() ?? (content.Find(Str(player, "RaceId")) is RaceDef race ? (int)race.Kind : null);

    private string NewPlayerId()
    {
        var existing = PlayersNode.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; ; i++)
            if (existing.Add($"{Prefix}PL{i:x4}")) return $"{Prefix}PL{i:x4}";
    }

    /// <summary>Игрок в виде JSON (для формы); нет — null.</summary>
    public JsonObject? PlayerOf(string id) => PlayersNode[id] as JsonObject;

    /// <summary>Поле игрока: раса, лорд, имя, человек, золото.</summary>
    public void SetPlayer(string id, string? race = null, string? lord = null, string? name = null, bool? human = null, int? gold = null)
    {
        if (PlayersNode[id] is not JsonObject p) throw new InvalidOperationException($"игрока «{id}» нет");
        string raceId = race ?? Str(p, "RaceId");
        if (content.Find(raceId) is not RaceDef raceDef) throw new InvalidOperationException($"расы «{raceId}» нет");
        if (lord is not null && content.Find(lord) is not LordDef) throw new InvalidOperationException($"лорда «{lord}» нет");
        if (race is not null && race != Str(p, "RaceId"))
        {
            // Сторона, своя до смены, сохраняется (отношения не путаются); флаг отрядов — по новой расе; лорд — расы.
            p["Party"] ??= PartyOf(p);
            p["RaceId"] = race;
            if (root["Events"]?["SubraceOwners"] is JsonObject owners)
                foreach (var sub in owners.Where(o => o.Value?.GetValue<string>() == id).Select(o => o.Key)) Section("Subraces")[sub] = SubraceCode(raceDef);
            if (lord is null) p["LordId"] = content.Lords.FirstOrDefault(l => l.Race == race)?.Id ?? "";
        }
        if (lord is not null)
        {
            if (content.Find(lord) is LordDef { Race: { } lordRace } && lordRace != raceId)
                throw new InvalidOperationException($"лорд «{lord}» — другой расы");
            p["LordId"] = lord;
        }
        if (name is not null) p["Name"] = name;
        if (human is { } h) p["IsHuman"] = h;
        if (gold is { } g) ((JsonObject)p["Bank"]!)["Gold"] = Math.Clamp(g, 0, 9999);
    }

    /// <summary>Убрать игрока: только без столицы, городов и отрядов (сначала их убрать или отдать другому).</summary>
    public void RemovePlayer(string id)
    {
        if (!PlayersNode.ContainsKey(id)) throw new InvalidOperationException($"игрока «{id}» нет");
        if (All().FirstOrDefault(o => o.Owner == id) is { } owned)
            throw new InvalidOperationException($"у игрока есть объекты ({owned.Kind} {owned.Id}) — сначала их убрать или отдать другому");
        if (EventsUsing(id) is { Count: > 0 } events)
            throw new InvalidOperationException($"на игрока ссылаются события ({string.Join(", ", events)}) — сначала поправить их");
        int? party = PartyOf((JsonObject)PlayersNode[id]!);
        PlayersNode.Remove(id);
        // Отношения стороны — только если она больше ничья (у двоих одной расы без своих сторон сторона общая).
        bool shared = PlayersNode.Select(p => p.Value as JsonObject).OfType<JsonObject>().Any(p => PartyOf(p) == party);
        var relations = List(MapNode, "Relations");
        if (!shared)
            foreach (var r in relations.OfType<JsonObject>().Where(r => r["Race1"]?.GetValue<int>() == party || r["Race2"]?.GetValue<int>() == party).ToList())
                relations.Remove(r);
        if (root["Events"]?["SubraceOwners"] is JsonObject owners)
            foreach (var sub in owners.Where(o => o.Value?.GetValue<string>() == id).Select(o => o.Key).ToList())
            {
                owners.Remove(sub);
                Section("Subraces").Remove(sub);
                (LooksNode["Banners"] as JsonObject)?.Remove(sub);
            }
    }

    // ---- события

    private JsonObject EventsNode => root["Events"] as JsonObject ?? (JsonObject)(root["Events"] = new JsonObject
    {
        ["Events"] = new JsonArray(), ["Zones"] = new JsonObject(), ["Variables"] = new JsonObject(), ["Templates"] = new JsonObject(),
        ["SubraceOwners"] = new JsonObject(),
    });

    private JsonArray EventList => List(EventsNode, "Events");

    /// <summary>События для чтения (без создания раздела событий).</summary>
    private IEnumerable<JsonObject> EventNodes => (root["Events"]?["Events"] as JsonArray)?.OfType<JsonObject>() ?? [];

    public IReadOnlyList<MapEventRow> Events() =>
        [.. EventNodes.Select(e => new MapEventRow(Str(e, "Id"), Str(e, "Name"), e["Enabled"]?.GetValue<bool>() ?? true,
            e["OccurOnce"]?.GetValue<bool>() ?? true, e["Chance"]?.GetValue<int>() ?? 100, (e["Conditions"] as JsonArray)?.Count ?? 0,
            (e["Effects"] as JsonArray)?.Count ?? 0))];

    public JsonObject? EventOf(string id) => EventNodes.FirstOrDefault(e => Str(e, "Id") == id);

    /// <summary>Новое событие: включено, один раз, шанс 100, для всех рас (шесть флагов — да: иначе событие не работает ни для кого), порядок —
    /// после всех; без условий и действий. Возвращает id.</summary>
    public string AddEvent(string name)
    {
        string id = NewEventId();
        EventList.Add(new JsonObject
        {
            ["Id"] = id, ["Enabled"] = true, ["OccurOnce"] = true, ["Chance"] = 100,
            ["Order"] = EventNodes.Select(e => e["Order"]?.GetValue<int>() ?? 0).DefaultIfEmpty(-1).Max() + 1,
            ["Races"] = new JsonArray(true, true, true, true, true, true), ["Conditions"] = new JsonArray(), ["Effects"] = new JsonArray(),
            ["Name"] = name,
        });
        return id;
    }

    private string NewEventId()
    {
        var existing = EventList.OfType<JsonObject>().Select(e => Str(e, "Id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; ; i++)
            if (existing.Add($"{Prefix}EV{i:x4}")) return $"{Prefix}EV{i:x4}";
    }

    public void RemoveEvent(string id)
    {
        var e = EventOf(id) ?? throw new InvalidOperationException($"события «{id}» нет");
        EventList.Remove(e);
    }

    /// <summary>Поле события: Name, Enabled, OccurOnce, Chance, Order, RaceIds, PlayerIds (значение JSON; null — убрать поле).</summary>
    public void SetEvent(string id, string field, JsonNode? value)
    {
        var e = EventOf(id) ?? throw new InvalidOperationException($"события «{id}» нет");
        if (field is not ("Name" or "Enabled" or "OccurOnce" or "Chance" or "Order" or "RaceIds" or "TriggerRaceIds" or "PlayerIds"))
            throw new InvalidOperationException($"поле события {field} так не правится");
        if (value is null && field is "RaceIds" or "TriggerRaceIds" or "PlayerIds")
        {
            e.Remove(field);
            return;
        }
        bool ok = field switch
        {
            "Name" => value is JsonValue v && v.GetValueKind() == JsonValueKind.String,
            "Enabled" or "OccurOnce" => value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
            "Chance" or "Order" => value is JsonValue v && v.TryGetValue<int>(out _),
            _ => value is JsonArray a && a.All(i => i is JsonValue s && s.GetValueKind() == JsonValueKind.String),
        };
        if (!ok) throw new InvalidOperationException($"поле события {field}: значение не того вида");
        if (field == "PlayerIds" && value is JsonArray players && players.Select(i => i!.GetValue<string>()).FirstOrDefault(i => !PlayersNode.ContainsKey(i)) is { } stranger)
            throw new InvalidOperationException($"игрока «{stranger}» на карте нет");
        e[field] = value!.DeepClone();
    }

    /// <summary>Виды условий и действий событий — из атрибутов записей правил (без «неизвестного»).</summary>
    public static IReadOnlyList<string> ConditionKinds => KindsOf(typeof(EventCondition));

    public static IReadOnlyList<string> EffectKinds => KindsOf(typeof(EventEffect));

    private static IReadOnlyList<string> KindsOf(Type root) =>
        [.. root.GetCustomAttributes<JsonDerivedTypeAttribute>().Select(a => a.TypeDiscriminator as string).OfType<string>().Where(k => k != "Unknown")];

    /// <summary>Новое условие (<paramref name="effect"/> — действие) вида <paramref name="kind"/>: запись по умолчанию. Номер в списке.</summary>
    public int AddPart(string eventId, string kind, bool effect)
    {
        var e = EventOf(eventId) ?? throw new InvalidOperationException($"события «{eventId}» нет");
        var root = effect ? typeof(EventEffect) : typeof(EventCondition);
        var type = root.GetCustomAttributes<JsonDerivedTypeAttribute>().FirstOrDefault(a => a.TypeDiscriminator as string == kind)?.DerivedType
                   ?? throw new InvalidOperationException($"вида «{kind}» нет");
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        object?[] args = [.. ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : Default(p.ParameterType))];
        var node = JsonSerializer.SerializeToNode(ctor.Invoke(args), root, SaveJson.Options)!;
        var list = List(e, effect ? "Effects" : "Conditions");
        list.Add(node);
        return list.Count - 1;
    }

    private static object? Default(Type t) =>
        t == typeof(string) ? "" : t == typeof(bool) ? false : t == typeof(int) ? 0
        : t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? Array.CreateInstance(t.GetGenericArguments()[0], 0)
        : t.IsValueType ? Activator.CreateInstance(t) : null;

    /// <summary>Поле условия или действия по номеру (значение JSON). Вид, категория и имя — не правятся.</summary>
    public void SetPart(string eventId, int index, bool effect, string field, JsonNode? value)
    {
        var part = Part(eventId, index, effect);
        if (field is "Kind" or "Category" or "Name") throw new InvalidOperationException($"поле {field} задаёт вид — оно не правится");
        if (!part.TryGetPropertyValue(field, out var was)) throw new InvalidOperationException($"у «{Str(part, "Kind")}» нет поля {field}");
        // Значение — того же вида, что у поля (число, текст, флаг, список): иначе сценарий не соберётся, а правка уже была бы в истории.
        var kind = was?.GetValueKind();
        bool same = (kind, value?.GetValueKind()) switch
        {
            (null, _) => true,
            (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => true,
            (JsonValueKind.Number, JsonValueKind.Number) => value is JsonValue n && n.TryGetValue<int>(out _),
            var (a, b) => a == b,
        };
        if (!same) throw new InvalidOperationException($"поле {field}: ждали {kind}, а дано {value?.GetValueKind().ToString() ?? "пусто"}");
        part[field] = value?.DeepClone();
    }

    public void RemovePart(string eventId, int index, bool effect)
    {
        var part = Part(eventId, index, effect);
        List(EventOf(eventId)!, effect ? "Effects" : "Conditions").Remove(part);
    }

    private JsonObject Part(string eventId, int index, bool effect)
    {
        var e = EventOf(eventId) ?? throw new InvalidOperationException($"события «{eventId}» нет");
        var list = List(e, effect ? "Effects" : "Conditions");
        return index >= 0 && index < list.Count && list[index] is JsonObject o ? o : throw new InvalidOperationException($"нет {(effect ? "действия" : "условия")} №{index}");
    }

    /// <summary>Область для условий «в области»: центр и радиус (0…3). Возвращает id.</summary>
    public string AddZone(int x, int y, int radius)
    {
        if (x < 0 || y < 0 || x >= MapSide || y >= MapSide) throw new InvalidOperationException($"клетка {x},{y} вне карты");
        var zones = EventsNode["Zones"] as JsonObject ?? (JsonObject)(EventsNode["Zones"] = new JsonObject());
        string id = "";
        for (int i = 0; id.Length == 0 || zones.ContainsKey(id); i++) id = $"{Prefix}LO{i:x4}";
        zones[id] = new JsonObject { ["Id"] = id, ["X"] = x, ["Y"] = y, ["Radius"] = Math.Clamp(radius, 0, 3) };
        return id;
    }

    public IReadOnlyList<(string Id, int X, int Y, int Radius)> Zones() =>
        EventsNode["Zones"] is JsonObject zones
            ? [.. zones.Select(z => z.Value as JsonObject).OfType<JsonObject>().Select(z => (Str(z, "Id"), z["X"]!.GetValue<int>(), z["Y"]!.GetValue<int>(), z["Radius"]!.GetValue<int>()))]
            : [];
}
