using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEdict.Data.Input;

/// <summary>
/// Переназначенные пользователем привязки действий ввода (<see cref="InputActions"/>): id действия → его привязки целиком
/// (<c>key:E</c>, <c>pad:Y</c>; пустой список — действие снято). Чего здесь нет — по умолчанию. Неизменяемое значение с равенством по
/// содержимому (настройки — запись, черновик окна сравнивает её с сохранённой), в файле — объект
/// <c>{ "map.BTN_END_TURN": ["key:E", "pad:Y"] }</c>.
/// </summary>
[JsonConverter(typeof(KeyBindingsConverter))]
public sealed class KeyBindings : IEquatable<KeyBindings>
{
    public static readonly KeyBindings Empty = new(new Dictionary<string, IReadOnlyList<string>>());

    private readonly SortedDictionary<string, IReadOnlyList<string>> _map = new(StringComparer.Ordinal);

    public KeyBindings(IReadOnlyDictionary<string, IReadOnlyList<string>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (id, bindings) in map)
            if (!string.IsNullOrWhiteSpace(id))
                _map[id] = Array.AsReadOnly([.. (bindings ?? []).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct()]);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Map => _map;

    public int Count => _map.Count;

    public bool Equals(KeyBindings? other) =>
        other is not null && _map.Count == other._map.Count
        && _map.All(p => other._map.TryGetValue(p.Key, out var b) && b.SequenceEqual(p.Value));

    public override bool Equals(object? obj) => Equals(obj as KeyBindings);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (id, bindings) in _map)
        {
            hash.Add(id);
            foreach (string b in bindings) hash.Add(b);
        }
        return hash.ToHashCode();
    }

    private sealed class KeyBindingsConverter : JsonConverter<KeyBindings>
    {
        public override KeyBindings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new((JsonSerializer.Deserialize<Dictionary<string, string[]>>(ref reader, options) ?? [])
                .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)(p.Value ?? [])));

        public override void Write(Utf8JsonWriter writer, KeyBindings value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value._map.ToDictionary(p => p.Key, p => p.Value.ToArray()), options);
    }
}
