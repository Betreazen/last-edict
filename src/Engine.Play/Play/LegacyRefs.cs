using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Content;

namespace LastEdict.Data.Play;

/// <summary>
/// Перевод ссылок на содержимое с id оригинала (<c>G000UU0002</c>) на id содержимого (<c>base:unit/knight</c>) — для сценариев и сохранений
/// версий до ключа правил по id содержимого (<see cref="ScenarioFile.Version"/> 6, <see cref="GameSave.Version"/> 11) и для разобранного
/// <c>.sg</c> при импорте. Ссылки лежат в десятках полей модели (тип юнита, модификаторы, предметы, заклинания, постройки, раса, лорд,
/// ориентиры, события), поэтому перевод — обходом JSON целиком: строка-значение или имя свойства, равные id сущности оригинала, заменяются
/// её id; составной ключ памяти ИИ (<c>4|g000bb0023</c> — вид и цель) — по частям. Прочие строки (id объектов карты <c>S143KC0001</c>, пустая ссылка <c>G000000000</c>, подрасы нейтралов вне содержимого) id
/// сущностей не совпадают и остаются как есть.
/// </summary>
public static class LegacyRefs
{
    /// <summary>Заменить ссылки в дереве <paramref name="node"/> на месте.</summary>
    public static void ToContentIds(JsonNode? node, LegacyIds ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        switch (node)
        {
            case JsonObject obj:
                if (obj.Any(p => Renamed(p.Key, ids) is not null))
                {
                    var pairs = obj.ToList();
                    obj.Clear();
                    foreach (var (key, value) in pairs) obj[Renamed(key, ids) ?? key] = value;
                }
                foreach (var (key, value) in obj.ToList())
                    if (Renamed(value, ids) is { } id) obj[key] = id;
                    else ToContentIds(value, ids);
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                    if (Renamed(arr[i], ids) is { } id) arr[i] = id;
                    else ToContentIds(arr[i], ids);
                break;
        }
    }

    /// <summary>Модель, записанная с id оригинала (разбор <c>.sg</c>), — та же модель с id содержимого.</summary>
    public static T ToContentIds<T>(T value, LegacyIds ids)
    {
        var node = JsonSerializer.SerializeToNode(value, SaveJson.Options);
        ToContentIds(node, ids);
        return node.Deserialize<T>(SaveJson.Options) ?? throw new InvalidDataException($"{typeof(T).Name}: перевод id дал пусто");
    }

    private static string? Renamed(JsonNode? node, LegacyIds ids) =>
        node is JsonValue v && v.TryGetValue(out string? text) ? Renamed(text, ids) : null;

    /// <summary>Строка с id содержимого вместо id оригинала; не ссылка — null.</summary>
    private static string? Renamed(string text, LegacyIds ids)
    {
        if (!text.Contains(KeySeparator, StringComparison.Ordinal)) return ids.IdOf(text);
        var parts = text.Split(KeySeparator);
        bool changed = false;
        for (int i = 0; i < parts.Length; i++)
            if (ids.IdOf(parts[i]) is { } id) (parts[i], changed) = (id, true);
        return changed ? string.Join(KeySeparator, parts) : null;
    }

    /// <summary>Разделитель частей составного ключа (<c>AiMemory.Memo</c>: <c>вид|цель</c>).</summary>
    private const char KeySeparator = '|';
}
