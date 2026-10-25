using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LastEdict.Net;

/// <summary>Правка вида по пути (<see cref="Path"/> — указатель JSON, RFC 6901): <see cref="Op.Set"/> — значение поля или элемента
/// (<see cref="Value"/>), <see cref="Op.Remove"/> — убрать поле, <see cref="Op.Splice"/> — в массиве с <see cref="At"/> заменить
/// <see cref="Count"/> элементов элементами <see cref="Value"/> (массив).</summary>
public sealed record PatchOp(
    [property: JsonPropertyName("o")] PatchOp.Op Kind,
    [property: JsonPropertyName("p")] string Path,
    [property: JsonPropertyName("v"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonNode? Value = null,
    [property: JsonPropertyName("i"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int At = 0,
    [property: JsonPropertyName("n"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Count = 0)
{
    public enum Op { Set, Remove, Splice }
}

/// <summary>
/// Дельта вида (<c>docs/network.md</c> §3): разность двух JSON проекции (<see cref="PlayerView"/>) — список правок по путям. Объекты
/// сравниваются по полям вглубь; массивы одной длины — по элементам, разной — одной заменой середины между общими началом и концом (отряд
/// пришёл или ушёл — правка одного места, а не всего списка). Двумерные массивы карты лежат строкой base64 и меняются целиком.
/// <para>Клиент накладывает дельту на свою копию вида и сверяет каноничный хеш (<see cref="Hash"/>) с хешем хоста: не сошлось — resync, а не
/// догадка.</para>
/// </summary>
public static class ViewPatch
{
    /// <summary>Правки, переводящие <paramref name="from"/> в <paramref name="to"/>.</summary>
    public static IReadOnlyList<PatchOp> Diff(JsonNode? from, JsonNode? to)
    {
        var ops = new List<PatchOp>();
        Diff(from, to, "", ops);
        return ops;
    }

    private static void Diff(JsonNode? a, JsonNode? b, string path, List<PatchOp> ops)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var (key, value) in oa)
                if (!ob.ContainsKey(key)) ops.Add(new PatchOp(PatchOp.Op.Remove, path + "/" + Escape(key)));
            foreach (var (key, value) in ob)
            {
                string at = path + "/" + Escape(key);
                if (!oa.TryGetPropertyValue(key, out var old)) ops.Add(new PatchOp(PatchOp.Op.Set, at, value?.DeepClone()));
                else Diff(old, value, at, ops);
            }
            return;
        }
        if (a is JsonArray xa && b is JsonArray xb)
        {
            if (xa.Count == xb.Count)
            {
                for (int i = 0; i < xa.Count; i++) Diff(xa[i], xb[i], path + "/" + i, ops);
                return;
            }
            int head = 0;
            while (head < xa.Count && head < xb.Count && JsonNode.DeepEquals(xa[head], xb[head])) head++;
            int tail = 0;
            while (tail < xa.Count - head && tail < xb.Count - head && JsonNode.DeepEquals(xa[xa.Count - 1 - tail], xb[xb.Count - 1 - tail])) tail++;
            var inserted = new JsonArray([.. xb.Skip(head).Take(xb.Count - head - tail).Select(n => n?.DeepClone())]);
            ops.Add(new PatchOp(PatchOp.Op.Splice, path, inserted, head, xa.Count - head - tail));
            return;
        }
        if (!JsonNode.DeepEquals(a, b)) ops.Add(new PatchOp(PatchOp.Op.Set, path, b?.DeepClone()));
    }

    /// <summary>Наложить правки на <paramref name="root"/> (меняет его на месте; правка корня — новый корень).</summary>
    /// <exception cref="InvalidDataException">Путь не ведёт в документ — дельта не к этому виду.</exception>
    public static JsonNode? Apply(JsonNode? root, IEnumerable<PatchOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        foreach (var op in ops)
        {
            if (op.Path.Length == 0 && op.Kind == PatchOp.Op.Set)
            {
                root = op.Value?.DeepClone();
                continue;
            }
            var (parent, last) = Locate(root, op.Path, op.Kind == PatchOp.Op.Splice);
            switch (op.Kind)
            {
                case PatchOp.Op.Splice:
                    var array = (parent ?? root) as JsonArray ?? throw Bad(op.Path, "не массив");
                    if (op.At < 0 || op.Count < 0 || (long)op.At + op.Count > array.Count) throw Bad(op.Path, "место вне массива");
                    for (int i = 0; i < op.Count; i++) array.RemoveAt(op.At);
                    var items = op.Value as JsonArray ?? throw Bad(op.Path, "вставка — не массив");
                    for (int i = 0; i < items.Count; i++) array.Insert(op.At + i, items[i]?.DeepClone());
                    break;
                case PatchOp.Op.Remove:
                    if (parent is not JsonObject obj || !obj.Remove(last)) throw Bad(op.Path, "поля нет");
                    break;
                default:
                    if (parent is JsonObject o) o[last] = op.Value?.DeepClone();
                    else if (parent is JsonArray arr && int.TryParse(last, out int i) && i >= 0 && i < arr.Count) arr[i] = op.Value?.DeepClone();
                    else throw Bad(op.Path, "некуда положить");
                    break;
            }
        }
        return root;
    }

    /// <summary>Родитель последнего шага пути и сам шаг; для <paramref name="whole"/> — сам узел пути (родитель — он).</summary>
    private static (JsonNode? Parent, string Last) Locate(JsonNode? root, string path, bool whole)
    {
        if (!path.StartsWith('/') && path.Length > 0) throw Bad(path, "путь не с «/»");
        var steps = path.Length == 0 ? [] : path[1..].Split('/').Select(Unescape).ToArray();
        int upTo = whole ? steps.Length : steps.Length - 1;
        var node = root;
        for (int i = 0; i < upTo; i++)
            node = node switch
            {
                JsonObject o when o.TryGetPropertyValue(steps[i], out var next) => next,
                JsonArray a when int.TryParse(steps[i], out int k) && k >= 0 && k < a.Count => a[k],
                _ => throw Bad(path, $"нет шага «{steps[i]}»"),
            };
        return (node, whole || steps.Length == 0 ? "" : steps[^1]);
    }

    private static InvalidDataException Bad(string path, string why) => new($"дельта вида: {path} — {why}");

    private static string Escape(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string Unescape(string step) => step.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    private static readonly JsonSerializerOptions Canonical = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Каноничная строка узла: ключи объектов по порядку, без пробелов.</summary>
    public static string CanonicalJson(JsonNode? node) => Sorted(node)?.ToJsonString(Canonical) ?? "null";

    /// <summary>SHA-256 каноничной строки (полный, 64 знака): по нему клиент проверяет, что вид после дельты тот же, что у хоста.</summary>
    public static string Hash(JsonNode? node) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(node)))).ToLowerInvariant();

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray arr => new JsonArray([.. arr.Select(Sorted)]),
        _ => node?.DeepClone(),
    };
}
