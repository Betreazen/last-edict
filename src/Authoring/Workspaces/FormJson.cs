using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Значения форм Студии в JSON слоя мода: перечень — строкой camelCase, цена — словарь валют без нулей (порядок валют оригинала),
/// пустое поле — снять (у своей сущности поле убирается, у правки чужой — null слоя снимает поле базы).</summary>
public static class FormJson
{
    public static JsonValue Enum<T>(T value) where T : struct, System.Enum =>
        JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

    /// <summary>Цена узлом JSON; бесплатно — null.</summary>
    public static JsonObject? Cost(IReadOnlyDictionary<string, long> cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        var order = Prices.Currencies.ToList();
        var node = new JsonObject();
        foreach (var (currency, amount) in cost.Where(c => c.Value != 0).OrderBy(c => order.IndexOf(c.Key) is var i and >= 0 ? i : int.MaxValue))
            node[currency] = amount;
        return node.Count == 0 ? null : node;
    }

    public static bool SameCost(IReadOnlyDictionary<string, long> a, IReadOnlyDictionary<string, long> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Where(p => p.Value != 0).OrderBy(p => p.Key, StringComparer.Ordinal)
                .SequenceEqual(b.Where(p => p.Value != 0).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    /// <summary>Правка поля формы: значение или (null) снятие поля.</summary>
    public static SetField Field(StudioSession session, string id, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(session);
        return value is null && session.IsMine(id) ? new SetField(id, path, null, Unset: true) : new SetField(id, path, value);
    }
}
