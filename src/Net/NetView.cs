using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Play;

namespace LastEdict.Net;

/// <summary>
/// Вид игрока на проводе: проекция (<see cref="PlayerView"/>) сохранением порта (<see cref="GameSave"/>, время записи постоянное) и счётчики
/// id карты (<c>MapState.IdNext</c>). Счётчики в сохранение не пишутся — чтение считает их по объектам, а у проекции объектов меньше; от них
/// зависят id призванных в бою, и бой клиента без них разошёлся бы с повтором хоста.
/// </summary>
public static class NetView
{
    /// <summary>Узел вида для проекции <paramref name="view"/>; счётчики — партии хоста <paramref name="idNext"/>.</summary>
    public static JsonNode Build(GameState view, IReadOnlyDictionary<string, int> idNext)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(idNext);
        var state = JsonNode.Parse(GameSave.ToJson(view, "", DateTime.UnixEpoch))!;
        var counters = new JsonObject();
        foreach (var (kind, next) in idNext.OrderBy(p => p.Key, StringComparer.Ordinal)) counters[kind] = next;
        return new JsonObject { ["state"] = state, ["idNext"] = counters };
    }

    /// <summary>Вид игрока <paramref name="viewer"/> партии хоста <paramref name="full"/>.</summary>
    public static JsonNode Of(GameState full, string viewer)
    {
        ArgumentNullException.ThrowIfNull(full);
        return Build(PlayerView.Of(full, viewer), full.Looks.Map.IdNext);
    }

    /// <summary>Партия-проекция из узла вида.</summary>
    /// <exception cref="InvalidDataException">Узел — не вид.</exception>
    public static GameState Read(JsonNode view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var stateNode = view["state"] ?? throw new InvalidDataException("в виде нет партии");
        var state = GameSave.FromJson(stateNode.ToJsonString());
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (view["idNext"] is JsonObject idNext)
            foreach (var (kind, next) in idNext)
                counters[kind] = next?.GetValue<int>() ?? 0;
        var map = GameSession.With(state.Looks.Map, idNext: counters);
        return state with { Looks = state.Looks with { Map = map } };
    }
}

/// <summary>Значение команды (<see cref="Data.Play.Commands.CommandResult.Value"/>) на проводе: тип и JSON. Типы — только из сборок игры
/// и простые значения .NET: чужой тип из сообщения не создаётся.</summary>
public static class ValueCodec
{
    public static JsonNode? Encode(object? value)
    {
        if (value is null) return null;
        var type = value.GetType();
        return new JsonObject
        {
            ["t"] = type.AssemblyQualifiedName,
            ["v"] = JsonSerializer.SerializeToNode(value, type, NetJson.Options),
        };
    }

    /// <exception cref="InvalidDataException">Тип не из сборок игры или значение не читается.</exception>
    public static object? Decode(JsonNode? node)
    {
        if (node is null) return null;
        string name = node["t"]?.GetValue<string>() ?? throw new InvalidDataException("значение без типа");
        var type = Type.GetType(name, throwOnError: false) ?? throw new InvalidDataException($"тип значения не найден: {name}");
        if (!Allowed(type)) throw new InvalidDataException($"тип значения не из игры: {type.FullName}");
        try
        {
            return node["v"].Deserialize(type, NetJson.Options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"значение {type.Name} не читается: {e.Message}", e);
        }
    }

    private static bool Allowed(Type type)
    {
        if (type.IsArray) return Allowed(type.GetElementType()!);
        if (type.IsGenericType && !type.GetGenericArguments().All(Allowed)) return false;
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        string assembly = definition.Assembly.GetName().Name ?? "";
        return assembly is "Engine.Rules" or "Engine.Play" or "Engine.Content" or "Net"
               || (assembly == "System.Private.CoreLib"
                   && (definition.IsPrimitive || definition.IsEnum || definition == typeof(string) || definition == typeof(decimal)
                       || definition.Namespace is "System.Collections.Generic" || definition.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal)
                       || definition == typeof(Nullable<>)));
    }
}
