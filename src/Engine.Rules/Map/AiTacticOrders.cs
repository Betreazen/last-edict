using LastEdict.Rules.Battle;

namespace LastEdict.Rules.Map;

/// <summary>Отряд-кандидат в защитники (<c>0x46defb</c>): действующий приказ и его цель; <paramref name="TargetOwn"/>
/// — цель-крепость принадлежит игроку (<c>+0x84</c>).</summary>
public sealed record AiDefender(string Id, StackOrder EffectiveOrder, string Target, bool TargetOwn = true);

/// <summary>Смена приказа (<c>CmdChangeOrder</c> <c>0x41f21d</c>).</summary>
public sealed record AiOrderChange(string Stack, StackOrder Order, string Target);

/// <summary>Группа отряда в очереди хода (<c>0x467c0e</c>); порядок проверок — как в перечислении.</summary>
public enum AiStackGroup { Assist, DefendCity, Summon, Noble, Rod, Other }

/// <summary>Отряд в очереди хода: <paramref name="Key"/> — сырое значение <c>CMidgardID</c> (группы сортирует
/// <c>list::sort</c> <c>0x44b3df</c> по <c>operator&lt;</c>); <paramref name="AssistTarget"/> — цель приказа
/// <c>L_ASSIST</c>.</summary>
public sealed record AiQueued(string Id, uint Key, AiStackGroup Group, string AssistTarget = "");

/// <summary>
/// Приказы ИИ между стратегией и тактикой и порядок хода отрядов. Ранг 1 по листингам, если не сказано иное.
/// </summary>
public static class AiTacticOrders
{
    /// <summary>
    /// Новый приказ смены приказа (<c>CAiChangeOrderEval</c>, слот 4 <c>0x44fcb5</c>, ранг 1): приказ <c>L_NORMAL</c> — при приказе ИИ
    /// <c>L_NORMAL</c> у знатного (<c>LUnitC</c> <c>L_NOBLE</c>, <c>[0x838198]</c>) или жезлоносца (<c>L_ROD</c>) и при приказе ИИ
    /// <c>L_STEAL</c> — приказ ИИ <c>L_ASSIST</c>, иначе ничего; <c>L_ATTACK_STACK</c> — приказ <c>L_BEZERK</c>; прочие — приказ
    /// <c>L_NORMAL</c>, у нейтрала <c>L_ROAM</c>. Цель — пусто. <c>null</c> — смены нет.
    /// </summary>
    /// <returns>Приказ и признак «это приказ ИИ» (<c>+0xb8</c>), иначе основной (<c>+0xa8</c>).</returns>
    public static (StackOrder Order, bool AiOrder)? ChangeOrder(StackOrder order, StackOrder aiOrder, bool nobleOrRod, bool ownerNeutral) =>
        order switch
        {
            StackOrder.Normal when aiOrder == StackOrder.Normal && nobleOrRod || aiOrder == StackOrder.Steal => (StackOrder.Assist, true),
            StackOrder.Normal => null,
            StackOrder.AttackStack => (StackOrder.Bezerk, false),
            _ => (ownerNeutral ? StackOrder.Roam : StackOrder.Normal, false),
        };

    /// <summary>
    /// «Угрозы» <c>0x474072</c> — на деле свои города без столицы по Чебышёву от угла столицы (углы
    /// <c>0x5e9563</c>): устойчивая сортировка по возрастанию (<c>0x4743d6</c>, сравнение <c>0x4743be</c>),
    /// затем разворот списка (<c>0x47453c</c>) — дальние первыми, равные в обратном порядке; первые два помечены
    /// (<c>index &lt; 2</c>). Контейнер — список (<c>0x474298</c> ищет перебором).
    /// </summary>
    public static IReadOnlyList<(string Id, bool Far)> CityOrder(IEnumerable<(string Id, int Distance)> cities)
    {
        ArgumentNullException.ThrowIfNull(cities);
        return [.. cities.OrderBy(c => c.Distance).Reverse().Select((c, i) => (c.Id, i < 2))];
    }

    /// <summary>
    /// <c>operational_orders</c> (<c>0x46defb</c>). Кандидаты — отряды игрока с действующим приказом
    /// <c>L_DEFEND_CITY</c>, у которых цели нет, цель чужая или своя, но не из двух дальних. Есть города — ПОСЛЕДНИЙ
    /// кандидат получает <c>L_DEFEND_CITY</c> на первый (дальний) город: сравнение идёт с константой 9999 без
    /// обновления минимума, а <c>list::remove</c> (<c>0x470d69</c>) снимает каждого проверенного, так что список
    /// пустеет на первом городе и остальные города не рассматриваются. Городов нет — все кандидаты сбрасываются
    /// в <c>L_NORMAL</c> без цели.
    /// </summary>
    public static IReadOnlyList<AiOrderChange> Operational(IReadOnlyList<(string Id, bool Far)> cities,
                                                           IEnumerable<AiDefender> stacks)
    {
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(stacks);
        var free = stacks.Where(s => s.EffectiveOrder == StackOrder.DefendCity
                                     && (s.Target.Length == 0 || !s.TargetOwn
                                         || !cities.Any(c => c.Far && Same(c.Id, s.Target))))
                         .ToList();
        if (free.Count == 0) return [];
        if (cities.Count > 0) return [new AiOrderChange(free[^1].Id, StackOrder.DefendCity, cities[0].Id)];
        return [.. free.Select(s => new AiOrderChange(s.Id, StackOrder.Normal, ""))];
    }

    /// <summary>
    /// Раздача стратегических приказов (слот 3 <c>CAiStrategic</c> <c>0x44e29e</c>, ранг 1 по листингу и журналу
    /// 20261014-replay): <paramref name="thieves"/> и <paramref name="others"/> — отряды игрока с действующим
    /// <c>L_NORMAL</c> в порядке списка <c>0x4760b0</c> (по силе), лидер-вор (<c>0x41b399</c>) — в первом, прочие без
    /// жезла (способность <c>0x837618</c>) — во втором. Допуски <c>0x44b1d7</c>: воры — доли (обычный, помощь, кража)
    /// 100/0, при <paramref name="assist"/> 50/50, при <paramref name="steal"/> обычных на 20 меньше; прочие — (обычный,
    /// защита города, помощь) 100/0, при <paramref name="defendCity"/> 67/33, при <paramref name="assist"/> обычных
    /// на 33 меньше. Раздаёт <see cref="Distribute"/>; цель — пусто.
    /// </summary>
    public static IReadOnlyList<AiOrderChange> Strategic(IReadOnlyList<string> thieves, IReadOnlyList<string> others,
                                                         bool assist, bool defendCity, bool steal)
    {
        ArgumentNullException.ThrowIfNull(thieves);
        ArgumentNullException.ThrowIfNull(others);
        int thiefNormal = (assist ? HalfShare : FullShare) - (steal ? StealCut : 0);
        int otherNormal = (defendCity ? FullShare - ThirdShare : FullShare) - (assist ? ThirdShare : 0);
        return [.. Distribute(thieves, thiefNormal, assist ? HalfShare : 0, StackOrder.Normal, StackOrder.Assist, StackOrder.Steal),
                .. Distribute(others, otherNormal, defendCity ? ThirdShare : 0, StackOrder.Normal, StackOrder.DefendCity, StackOrder.Assist)];
    }

    /// <summary>
    /// Распределение <c>0x44e518</c>: из <c>n</c> отрядов первые <c>a</c> получают <paramref name="first"/>, следующие
    /// <c>b</c> — <paramref name="second"/>, остальные — <paramref name="third"/>, по порядку списка. При <c>n</c> = 1, 2, 3 —
    /// (1, 0, 0), (1, 1, 0), (1, 1, 1) без долей; иначе <c>a = max(1, ⌊n·pA/100⌋)</c>, <c>b = max(1, ⌊n·pB/100⌋)</c>,
    /// <c>c = max(0, n − a − b)</c> (третья доля движком не читается). Приказ <c>L_NORMAL</c> не пишется — он у отряда и
    /// так действует; прочие ставятся приказом ИИ (<c>0x5e811a</c>, флаг 1).
    /// </summary>
    public static IReadOnlyList<AiOrderChange> Distribute(IReadOnlyList<string> stacks, int pctFirst, int pctSecond,
                                                          StackOrder first, StackOrder second, StackOrder third)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        int n = stacks.Count;
        var (a, b) = n switch
        {
            0 => (0, 0),
            1 => (1, 0),
            2 or 3 => (1, 1),
            _ => (Math.Max(1, n * pctFirst / FullShare), Math.Max(1, n * pctSecond / FullShare)),
        };
        var changes = new List<AiOrderChange>();
        for (int i = 0; i < n; i++)
        {
            var order = i < a ? first : i < a + b ? second : third;
            if (order != StackOrder.Normal) changes.Add(new AiOrderChange(stacks[i], order, ""));
        }
        return changes;
    }

    private const int FullShare = 100, HalfShare = 50, ThirdShare = 33, StealCut = 20;

    /// <summary>
    /// Очередь хода (<c>0x467b07</c> + <c>0x467c0e</c>): входят отряды игрока с действующим приказом не
    /// <c>L_STAND</c> (фильтрует вызывающий). Группы: <c>L_ASSIST</c>, <c>L_DEFEND_CITY</c>, лидер
    /// <c>L_SUMMON</c>, <c>L_NOBLE</c>, способность <c>L_ROD</c>, прочие; каждая сортируется по ключу, при
    /// заданном <paramref name="first"/> он переносится в начало своей группы (<c>0x468398</c>). Итог: защитники,
    /// призыв, знатные, жезлы, затем прочие — за каждым его помощники; <paramref name="first"/> ставится в самое
    /// начало очереди, его помощники — сразу за ним (каждый новый — вторым); оставшиеся помощники — в конец.
    /// Ранг 2 — порядок ключей <c>CMidgardID</c>.
    /// </summary>
    public static IReadOnlyList<string> TurnQueue(IEnumerable<AiQueued> stacks, string? first)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        var groups = stacks.GroupBy(s => s.Group)
                           .ToDictionary(g => g.Key, g => Front(g.OrderBy(s => s.Key).ToList(), first));
        List<AiQueued> Get(AiStackGroup g) => groups.TryGetValue(g, out var l) ? l : [];
        var result = new List<string>();
        foreach (var g in new[] { AiStackGroup.DefendCity, AiStackGroup.Summon, AiStackGroup.Noble, AiStackGroup.Rod })
            result.AddRange(Get(g).Select(s => s.Id));
        var assists = Get(AiStackGroup.Assist);
        foreach (var o in Get(AiStackGroup.Other))
        {
            bool isFirst = first is not null && Same(o.Id, first);
            if (isFirst) result.Insert(0, o.Id); else result.Add(o.Id);
            foreach (var a in assists.Where(a => Same(a.AssistTarget, o.Id)).ToList())
            {
                if (isFirst) result.Insert(1, a.Id); else result.Add(a.Id);
                assists.Remove(a);
            }
        }
        result.AddRange(assists.Select(a => a.Id));
        return result;
    }

    private static List<AiQueued> Front(List<AiQueued> l, string? first)
    {
        int i = first is null ? -1 : l.FindIndex(s => Same(s.Id, first));
        if (i <= 0) return l;
        var f = l[i];
        l.RemoveAt(i);
        l.Insert(0, f);
        return l;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
