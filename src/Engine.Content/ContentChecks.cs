using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using D2.Data.Content.Schema;

namespace D2.Data.Content;

/// <summary>
/// Целостность набора содержимого — то, чего не видит схема JSON одной сущности:
/// <list type="bullet">
/// <item>ключ правил (<see cref="ContentDef.Key"/>) уникален внутри вида без учёта регистра — словари правил регистронезависимы, и второй
/// ключ молча перекрыл бы первый (прежние загрузчики на повторе <c>GDynUpgr</c>, <c>Gmodif</c>, <c>Gspells</c>… падали);</item>
/// <item>ссылка (<see cref="RefAttribute"/>) ведёт к существующей сущности нужного вида (или к объекту оригинала вне содержимого —
/// <c>legacy:…</c>);</item>
/// <item>отношение ИИ — одна сущность на отношение; списки глобальных величин фиксированной длины — той длины, которую ждут правила
/// (иначе бой упал бы на индексе, а не загрузка с именем поля).</item>
/// </list>
/// Сообщение называет сущность, поле и, если известно, откуда она пришла (мод и файл).
/// </summary>
internal static class ContentChecks
{
    public static void Validate(ContentSet set, Func<string, string?>? origin)
    {
        string Where(ContentDef def) => origin?.Invoke(def.Id) is { } from ? $"«{def.Id}» ({from})" : $"«{def.Id}»";
        foreach (var kind in ContentSet.Kinds)
        {
            var keys = new Dictionary<string, ContentDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in set.Of(kind.Type))
            {
                if (!keys.TryAdd(def.Key, def))
                    throw new InvalidDataException($"{Where(def)} и {Where(keys[def.Key])}: один ключ правил «{def.Key}» (legacyId или id)");
                foreach (var (path, refKind, value) in References(def))
                {
                    if (value.StartsWith(ContentSet.LegacyPrefix, StringComparison.Ordinal)) continue;
                    var target = set.Find(value) ?? throw new InvalidDataException(
                        $"{Where(def)}: {path} → «{value}» — нет такой сущности" + (origin?.Invoke(value) is { } gone ? $" ({gone})" : ""));
                    string actual = ContentSet.KindOf(target.GetType()).Name;
                    if (actual != refKind)
                        throw new InvalidDataException($"{Where(def)}: {path} → «{value}» — это {actual}, а нужен {refKind}");
                }
            }
        }
        var attitudes = new Dictionary<AiAttitude, AiAttitudeDef>();
        foreach (var ai in set.AiAttitudes)
            if (!attitudes.TryAdd(ai.Attitude, ai))
                throw new InvalidDataException($"{Where(ai)} и {Where(attitudes[ai.Attitude])}: одно отношение ИИ {ai.Attitude}");
        foreach (var ai in set.AiAttitudes)
        {
            Length(ai, "cheats.resourcePercent", ai.Cheats.ResourcePercent, 4, Where);
            Length(ai, "cheats.xpPercent", ai.Cheats.XpPercent, 4, Where);
            Length(ai, "spells.levelChance", ai.Spells.LevelChance, 3, Where);
            Length(ai, "spells.levelPower", ai.Spells.LevelPower, 4, Where);
            Length(ai, "spells.criticalWeight", ai.Spells.CriticalWeight, 4, Where);
            Length(ai, "spells.criticalChance", ai.Spells.CriticalChance, 4, Where);
        }
        var g = set.Globals;
        Length(g, "battle.boostDamagePercent", g.Battle.BoostDamagePercent, 4, Where);
        Length(g, "battle.lowerDamagePercent", g.Battle.LowerDamagePercent, 2, Where);
        Length(g, "thieves.morale", g.Thieves.Morale, D2.Rules.Map.Thieves.CapitalMoraleIndex, Where);
        Length(g, "land.cities", g.Land.Cities, 5, Where);
        Length(g, "difficulty.startBankPercent", g.Difficulty.StartBankPercent, 4, Where);
        Length(g, "difficulty.incomePercent", g.Difficulty.IncomePercent, 4, Where);
        Length(g, "ai.cityArmor", g.Ai.CityArmor, 5, Where);
        Length(g, "spells.areaPowerPercent", g.Spells.AreaPowerPercent, 5, Where);
    }

    private static void Length(ContentDef def, string field, IReadOnlyList<int> list, int expected, Func<ContentDef, string> where)
    {
        if (list.Count != expected)
            throw new InvalidDataException($"{where(def)}: {field} — {list.Count} значений, а правила ждут {expected}");
    }

    /// <summary>Все ссылки сущности: путь поля, вид цели, значение (включая вложенные группы и элементы списков).</summary>
    private static IEnumerable<(string Path, string Kind, string Value)> References(object record, string prefix = "")
    {
        foreach (var (property, refKind, nested) in Shape(record.GetType()))
        {
            object? value = property.GetValue(record);
            if (value is null) continue;
            string path = prefix + JsonName(property.Name);
            if (refKind is not null)
            {
                if (value is string one) yield return (path, refKind, one);
                else if (value is IEnumerable<string> many)
                    foreach (var (item, i) in many.Select((v, i) => (v, i))) yield return ($"{path}[{i}]", refKind, item);
            }
            else if (nested && value is IEnumerable items and not string)
            {
                int i = 0;
                foreach (var item in items)
                {
                    if (item is not null)
                        foreach (var r in References(item, $"{path}[{i}].")) yield return r;
                    i++;
                }
            }
            else if (nested)
                foreach (var r in References(value, path + ".")) yield return r;
        }
    }

    private static string JsonName(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>Свойства записи: ссылки и вложенные записи схемы (группы, списки записей).</summary>
    private static IReadOnlyList<(PropertyInfo Property, string? Ref, bool Nested)> Shape(Type type) =>
        Shapes.GetOrAdd(type, static t => [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => (Property: p, Ref: p.GetCustomAttribute<RefAttribute>()?.Kind, Nested: IsSchema(Element(p.PropertyType))))
            .Where(p => p.Ref is not null || p.Nested)]);

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(PropertyInfo, string?, bool)>> Shapes = new();

    private static Type Element(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : type;

    private static bool IsSchema(Type type) => type.IsClass && type.Namespace == typeof(ContentDef).Namespace && !typeof(ContentDef).IsAssignableFrom(type);
}
