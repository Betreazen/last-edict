using System.Runtime.CompilerServices;

namespace LastEdict.Rules.Map;

/// <summary>
/// Клетки объекта по занятости карты (<see cref="MapState.Plan"/>) — указатель id → клетки, собранный один раз на экземпляр
/// плана. Ход ИИ спрашивает вход объекта (<see cref="PathFinder.EntranceOf"/>) и пятно (<see cref="LandModel.PlanCells"/>) на
/// каждого кандидата, а прежде каждый вызов шёл по всем клеткам плана с регистронезависимым сравнением. План не меняется на месте:
/// всякая правка карты собирает новый словарь (копия <c>ToDictionary</c>, затем <c>With(…, plan:)</c>), так что указатель по
/// ссылке на словарь всегда соответствует содержимому.
/// </summary>
internal static class PlanIndex
{
    private static readonly ConditionalWeakTable<IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>>,
                                                  Dictionary<string, (int X, int Y)[]>> Built = new();

    /// <summary>Клетки, в списке которых есть <paramref name="objectId"/> (без регистра), по возрастанию (X, затем Y) — то же,
    /// что <c>Plan.Where(p =&gt; p.Value.Contains(objectId, OrdinalIgnoreCase)).Select(p =&gt; p.Key).Order()</c>.</summary>
    public static IReadOnlyList<(int X, int Y)> CellsOf(MapState map, string objectId)
    {
        // null в указатель не входит (ключ словаря); такой запрос (вне типов) — прежним проходом.
        if (objectId is null)
            return [.. map.Plan.Where(p => p.Value.Contains(objectId, StringComparer.OrdinalIgnoreCase)).Select(p => p.Key).Order()];
        return Built.GetValue(map.Plan, Build).TryGetValue(objectId, out var cells) ? cells : [];
    }

    private static Dictionary<string, (int X, int Y)[]> Build(IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> plan)
    {
        var lists = new Dictionary<string, List<(int X, int Y)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cell, ids) in plan)
            foreach (string id in ids)
            {
                if (id is null) continue;
                if (!lists.TryGetValue(id, out var list)) lists[id] = list = [];
                // Один id дважды в клетке (хоть в разном регистре) — клетка одна, как у Where.
                if (list.Count == 0 || list[^1] != cell) list.Add(cell);
            }
        return lists.ToDictionary(p => p.Key, p => p.Value.Order().ToArray(), StringComparer.OrdinalIgnoreCase);
    }
}
