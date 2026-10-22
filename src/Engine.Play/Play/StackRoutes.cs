using LastEdict.Data.Map;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>
/// Пути, которые интерфейс помнит за каждым отрядом (<c>docs/map-path.md</c> §4, «Путь через конец хода»). У движка это список клиента
/// (<c>0x4074c0</c>: <c>[+0x10] + 0x34</c>; поиск по id отряда <c>0x41a509</c>/<c>0x41a96d</c>), запись на отряд: id, флаг «Охранять»
/// (<c>+4</c>, <c>0x41a58b</c>), клетка щелчка (<c>+8</c>, −1 — пути нет) и точки (<c>+0x10</c>).
/// <list type="bullet">
/// <item>щелчок, показавший путь, кладёт его отряду, прежний заменяется (<c>0x4d1209</c> → <c>0x41a799</c>);</item>
/// <item>ход отряда срезает пройденное начало (<c>CCmdMoveStackMsg</c> <c>0x49237f</c> → <c>0x41a84d</c>);</item>
/// <item>начало хода путей не трогает — сбрасывает только флаги «Охранять» (<c>CCmdBeginTurnMsg</c> <c>0x4911af</c> → <c>0x41a3f4</c>);</item>
/// <item>выбор отряда показывает его путь снова или стирает, если на остатке отряд, жезл или мешок (<c>0x4cf9cc</c> → <c>0x4cfead</c>,
/// <c>0x41a808</c>); выбранному отряду путь пересоздаётся и в начале дня — с новым запасом (<c>0x4d2089</c> → <c>0x4cc3b3</c>);</item>
/// <item>команды особых задач (жезл, обмен) путь стирают (<c>0x4cdede</c>, <c>0x4ce0b1</c> → <c>0x41a808</c>).</item>
/// </list>
/// В <c>.sg</c> путей нет: новая или загруженная партия их не помнит.
/// </summary>
public sealed class StackRoutes
{
    /// <summary>Путь отряда: точки (как были показаны), сколько из них пройдено, цель щелчка.</summary>
    public sealed record Route(StackPath Path, int Walked, (int X, int Y) Target);

    private readonly Dictionary<string, Route> _routes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Сколько отрядов помнят путь.</summary>
    public int Count => _routes.Count;

    /// <summary>Отряд помнит путь.</summary>
    public bool Has(string stackId) => _routes.ContainsKey(stackId);

    public void Clear() => _routes.Clear();

    /// <summary>Щелчок показал путь <paramref name="path"/> к цели <paramref name="target"/> — он и есть путь отряда (<c>0x41a799</c>).</summary>
    public void Remember(string stackId, StackPath path, (int X, int Y) target)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Points.Count == 0) _routes.Remove(stackId);
        else _routes[stackId] = new Route(path, 0, target);
    }

    /// <summary>Отряд прошёл <paramref name="walked"/> точек пути <paramref name="path"/> к цели <paramref name="target"/> (<c>0x41a84d</c>). Без
    /// цели (ход к жезлу) путь стирается (<c>0x41a808</c>).</summary>
    public void Walked(string stackId, StackPath path, int walked, (int X, int Y)? target)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (target is { } aim && path.Points.Count > 0) _routes[stackId] = new Route(path, walked, aim);
        else _routes.Remove(stackId);
    }

    public void Forget(string stackId) => _routes.Remove(stackId);

    /// <summary>Стереть пути отрядов, которых больше нет (погиб, слит; у движка запись уходит вместе с отрядом из списка клиента).</summary>
    public void Prune(Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        foreach (string id in _routes.Keys.Where(id => !exists(id)).ToList()) _routes.Remove(id);
    }

    /// <summary>Путь отряда — снова на карту (<c>0x4cfead</c>): отряд стоит там, где его оставил ход, не в крепости и не на цели, а на остатке
    /// нет отряда, жезла или мешка (<see cref="GameSession.ShownPathKept"/>). Остаток — с новой клетки и по сегодняшнему запасу
    /// (<see cref="GameSession.KeptPath"/>). Иначе путь стирается и ответ — null.</summary>
    public (StackPath Path, (int X, int Y) Target)? Show(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        if (!_routes.TryGetValue(stack.Id, out var route)) return null;
        if (route.Walked >= route.Path.Points.Count
            || (stack.X, stack.Y) != (route.Path.Points[route.Walked].X, route.Path.Points[route.Walked].Y)
            || !session.ShownPathKept(stack, route.Path, route.Walked, route.Target))
        {
            _routes.Remove(stack.Id);
            return null;
        }
        return (session.KeptPath(stack, route.Path, route.Walked), route.Target);
    }

    /// <summary>«Продолжить движение» доступна (<c>0x4cfdff</c>, кнопку ставит <c>0x4d2346</c>): отряд помнит путь, стоит не на клетке его цели и
    /// у него есть очки хода.</summary>
    public bool CanContinue(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return _routes.TryGetValue(stack.Id, out var route) && (stack.X, stack.Y) != route.Target && stack.Move > 0;
    }
}
