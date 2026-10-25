using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Где документ мода ссылается на сущность: сущность, путь поля через точку и значение поля (строка-ссылка или список с ней).</summary>
public sealed record ModReference(string Entity, string Path, JsonNode Value);

/// <summary>
/// Ссылки на сущность внутри слоя мода. Удалить свою сущность — значит убрать и ссылки на неё, иначе мод не наложится (висячая ссылка —
/// ошибка загрузки): из списков (постройки лорда) её вычеркнуть, поле-ссылку снять. Правка чужой сущности, ставшая такой же, как база, из
/// слоя уходит (поле снимается, а не пишется равным базе).
/// </summary>
public static class ModReferences
{
    /// <summary>Все ссылки на <paramref name="id"/> в документе (кроме самой сущности).</summary>
    public static IReadOnlyList<ModReference> To(ModDocument doc, string id)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var found = new List<ModReference>();
        foreach (var e in doc.Entities.Where(e => e.Id != id))
            Walk(e.Id, e.Node, "", id, found);
        return found;
    }

    private static void Walk(string entity, JsonObject node, string prefix, string id, List<ModReference> found)
    {
        foreach (var (key, value) in node)
        {
            if (key == "id" && prefix.Length == 0) continue;
            string path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (value is JsonObject inner) Walk(entity, inner, path, id, found);
            else if ((value is JsonArray array && array.Any(v => IsId(v, id))) || IsId(value, id))
                found.Add(new ModReference(entity, path, value!.DeepClone()));
        }
    }

    private static bool IsId(JsonNode? node, string id) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == id;

    /// <summary>Команды, которые снимают ссылки на <paramref name="id"/>: из списка — вычеркнуть (список стал как у базы — снять поле), поле —
    /// снять (у своей сущности — убрать, у правки чужой — вернуть базовое). <paramref name="replacement"/> — чем заменить поле-ссылку по
    /// пути (требование постройки — требуемой удаляемой: цепочка не рвётся); нет — снять.</summary>
    public static IReadOnlyList<IEditCommand> Detach(StudioSession session, string id, Func<ModReference, string?>? replacement = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var commands = new List<IEditCommand>();
        foreach (var r in To(session.History.Current, id))
        {
            if (r.Value is JsonArray array)
            {
                var kept = new JsonArray([.. array.Where(v => !IsId(v, id)).Select(v => v?.DeepClone())]);
                bool asBase = !session.IsMine(r.Entity) && BaseValue(session, r.Entity, r.Path) is { } b && JsonNode.DeepEquals(b, kept);
                commands.Add(asBase ? new SetField(r.Entity, r.Path, null, Unset: true) : new SetField(r.Entity, r.Path, kept));
            }
            else if (replacement?.Invoke(r) is { } to)
                commands.Add(new SetField(r.Entity, r.Path, JsonValue.Create(to)));
            else
                commands.Add(new SetField(r.Entity, r.Path, null, Unset: true));
        }
        return commands;
    }

    /// <summary>Значение поля сущности в содержимом без этого мода (база и моды под ним); нет — null.</summary>
    public static JsonNode? BaseValue(StudioSession session, string entity, string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BaseContent.Find(entity) is not { } def) return null;
        JsonNode? at = JsonSerializer.SerializeToNode(def, def.GetType(), ContentJson.Options);
        foreach (string part in path.Split('.'))
            at = at is JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
        return at;
    }
}
