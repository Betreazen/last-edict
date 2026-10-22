using LastEdict.Rules.Units;
namespace LastEdict.Rules.Map;

/// <summary>Ряд, куда перестановка ИИ ставит юнита (<c>0x5d59b3</c>): большой — колонка переднего ряда, малый с ближней
/// атакой класса «урон» (<c>0x5d59e4</c>: досягаемость <c>0x8380b8</c>, класс <c>0x837428</c>) — передний ряд, прочие —
/// задний.</summary>
public enum ArrangeLine { Big = 0, Melee = 1, Back = 2 }

/// <summary>Роль юнита в очереди перестановки (<c>0x5d5a34</c>): без атаки с уроном (<c>0x5ddf70</c>) — поддержка; иначе
/// по ветке <c>Gunits.BRANCH</c> — побочная (<c>L_SIDESHOW</c>), лучник (<c>L_ARCHER</c>), прочие.</summary>
public enum ArrangeRole { Sideshow = 0, Archer = 1, Support = 2, Other = 3 }

/// <summary>Юнит в списке перестановки (<c>0x5d58dd</c>): сила <c>0x5dd06c</c>, ряд, роль.</summary>
public sealed record ArrangeUnit(string Id, double Score, ArrangeLine Line, ArrangeRole Role)
{
    public bool Big => Line == ArrangeLine.Big;
}

/// <summary>Группа юнитов по клеткам: порядок юнитов (<c>UNIT_0..5</c>) и шесть клеток — id юнита или пусто; большой
/// юнит стоит в обеих клетках колонки; строение — <see cref="Formation"/>.</summary>
public sealed record UnitCells(IReadOnlyList<string> Units, IReadOnlyList<string> Cells)
{
    public const int CellCount = Formation.Cells;

    /// <summary>Из представления сохранения: клетка → индекс юнита (<see cref="MapStack.Slots"/>).</summary>
    public static UnitCells Of(IReadOnlyList<string> units, IReadOnlyList<int> slots)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(slots);
        var cells = new string[CellCount];
        for (int c = 0; c < CellCount; c++)
            cells[c] = c < slots.Count && slots[c] >= 0 && slots[c] < units.Count ? units[slots[c]] : "";
        return new UnitCells([.. units], cells);
    }

    /// <summary>Клетка → индекс юнита в <see cref="Units"/>, <c>-1</c> — пусто.</summary>
    public int[] Slots() => [.. Cells.Select(id => id.Length == 0 ? -1 : IndexOf(Units, id))];

    /// <summary>Позиция юнита — первая его клетка (<c>0x5f455f</c>); нет — <c>-1</c>.</summary>
    public int PositionOf(string id) => IndexOf(Cells, id);

    /// <summary>Занятые клетки (большой — две).</summary>
    public int Occupied => Cells.Count(c => c.Length > 0);

    private static int IndexOf(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}

/// <summary>
/// Перестановка группы на сервере (<c>docs/ai-items.md</c> §6): расстановка одной группы <c>0x5d57de</c> и очередь
/// переноса из гарнизона <c>0x5d5d73</c>. Ранг 1 — листинг; сверка с журналом — <c>AiStackItemsHarnessTests</c>.
/// </summary>
public static class GroupArrange
{
    /// <summary>Список по убыванию силы (<c>0x5d58dd</c>: вставка перед первым не сильнее — <c>0x5d6711</c>, нижняя граница
    /// по <c>a &gt; b</c>). При равной силе позже вставленный идёт раньше — отсюда «пинг-понг» обменов одинаковых юнитов
    /// между отрядом и гарнизоном на каждом выборе (журнал human, день 35: 19 обменов).</summary>
    public static List<ArrangeUnit> Sorted(IEnumerable<ArrangeUnit> inGroupOrder)
    {
        ArgumentNullException.ThrowIfNull(inGroupOrder);
        var list = new List<ArrangeUnit>();
        foreach (var u in inGroupOrder)
        {
            int at = list.FindIndex(e => !(e.Score > u.Score));
            list.Insert(at < 0 ? list.Count : at, u);
        }
        return list;
    }

    /// <summary>Первый подходящий, кроме стоящего первым, — в начало (<c>0x5d5fd8</c> по ряду, <c>0x5d6095</c> по роли:
    /// совпадение засчитывается, только если узел не первый, <c>cmp eax, [[L+4]]</c>): сила = сила первого + 1
    /// (<c>fsub [0x6ede98]</c> = −1), затем устойчивая сортировка по убыванию (<c>0x5d65ab</c>). Нет такого — false.
    /// Журнал: Легионы, день 1, <c>S073KC0003</c> — последний вынос «прочих» поднимает большого над бойцом, вынесенным
    /// первым; жезлоносцы (лидерство 2) берут мага, а не единственного бойца.</summary>
    public static bool ToFront(List<ArrangeUnit> list, Func<ArrangeUnit, bool> match)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(match);
        int i = list.Count < 2 ? -1 : list.FindIndex(1, u => match(u));
        if (i < 0) return false;
        list[i] = list[i] with { Score = list[0].Score + 1 };
        var sorted = list.OrderByDescending(u => u.Score).ToList();
        list.Clear();
        list.AddRange(sorted);
        return true;
    }

    /// <summary>
    /// Очередь переноса (<c>0x5d5d73</c>): юниты отряда без лидера, затем гарнизона (<see cref="Sorted"/>); вперёд —
    /// первый малый ближнего боя (нет — первый большой), затем лучник, затем при приказе «помощь» или «защита города»
    /// (<c>L_ASSIST</c>, <c>L_DEFEND_CITY</c>) побочная ветка, иначе «прочие». Каждый вынос ставит юнита перед всеми,
    /// поэтому порядок начала: последний вынесенный, лучник, боец.
    /// </summary>
    public static List<ArrangeUnit> Queue(IEnumerable<ArrangeUnit> inGroupOrder, bool assistOrDefend)
    {
        var list = Sorted(inGroupOrder);
        if (!ToFront(list, u => u.Line == ArrangeLine.Melee)) ToFront(list, u => u.Line == ArrangeLine.Big);
        ToFront(list, u => u.Role == ArrangeRole.Archer);
        ToFront(list, u => u.Role == (assistOrDefend ? ArrangeRole.Sideshow : ArrangeRole.Other));
        return list;
    }

    /// <summary>
    /// Расстановка группы (<c>0x5d57de</c>) — новые клетки. По списку <see cref="Sorted"/>: (1) большие — в первую годную
    /// клетку переднего ряда (не нашлось — остаётся без клетки); (2) малые ближнего боя — в первую годную переднего ряда,
    /// не нашлось — ждут шага 3; (3) остальные по порядку — в первую годную заднего ряда, иначе переднего. Годность —
    /// <see cref="Fits"/>. С <paramref name="center"/> — центровка <see cref="Center"/> (<c>0x606a7b</c>).
    /// </summary>
    public static string[] Layout(IEnumerable<ArrangeUnit> inGroupOrder, bool center)
    {
        var list = Sorted(inGroupOrder);
        var cells = Enumerable.Repeat("", UnitCells.CellCount).ToArray();
        foreach (var u in list.Where(u => u.Line == ArrangeLine.Big).ToList())
        {
            if (First(cells, u, Formation.Front) is int c and >= 0) Place(cells, u, c);
            list.Remove(u);
        }
        foreach (var u in list.Where(u => u.Line == ArrangeLine.Melee).ToList())
            if (First(cells, u, Formation.Front) is int c and >= 0)
            {
                Place(cells, u, c);
                list.Remove(u);
            }
        foreach (var u in list)
        {
            int c = First(cells, u, Formation.Back);
            if (c < 0) c = First(cells, u, Formation.Front);
            if (c >= 0) Place(cells, u, c);
        }
        if (center) Center(cells);
        return cells;
    }

    /// <summary>Годна ли клетка (<c>0x6067fb</c>): большому — вторая клетка колонки пуста и клетка передняя; клетка пуста.</summary>
    public static bool Fits(IReadOnlyList<string> cells, bool big, int cell)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (big && (cells[Formation.Pair(cell)].Length > 0 || !Formation.IsFront(cell))) return false;
        return cells[cell].Length == 0;
    }

    /// <summary>
    /// Центровка (<c>0x606a7b</c>). Передний ряд: один занятый — в клетку 2; два — в клетки 0 и 4 (по порядку клеток).
    /// Затем задний: один — в 3; два — в 1 и 5. Перенос — обмен клеток (<c>0x606be0</c>); большой переносит и вторую
    /// клетку колонки. Считаются клетки, поэтому большой занимает место в обоих рядах.
    /// </summary>
    public static void Center(string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        Row(cells, Formation.Front, 2);
        Row(cells, Formation.Back, 3);
    }

    private static void Row(string[] cells, IReadOnlyList<int> row, int middle)
    {
        var taken = row.Where(c => cells[c].Length > 0).ToList();
        if (taken.Count == 1)
        {
            if (taken[0] != middle) Move(cells, taken[0], middle);
            return;
        }
        if (taken.Count != 2) return;
        int target = row[0];
        foreach (int c in row)
        {
            if (cells[c].Length == 0) continue;
            if (c != target) Move(cells, c, target);
            target += 4;
            if (target >= UnitCells.CellCount) break;
        }
    }

    private static void Move(string[] cells, int from, int to)
    {
        string id = cells[from];
        bool big = id.Length > 0 && cells[Formation.Pair(from)] == id;
        (cells[from], cells[to]) = (cells[to], cells[from]);
        if (big) (cells[Formation.Pair(from)], cells[Formation.Pair(to)]) = (cells[Formation.Pair(to)], cells[Formation.Pair(from)]);
    }

    private static int First(string[] cells, ArrangeUnit u, IReadOnlyList<int> row)
    {
        foreach (int c in row)
            if (Fits(cells, u.Big, c)) return c;
        return -1;
    }

    private static void Place(string[] cells, ArrangeUnit u, int cell)
    {
        cells[cell] = u.Id;
        if (u.Big) cells[Formation.Pair(cell)] = u.Id;
    }

    /// <summary>
    /// Обмен клеток двух групп (<c>CVisitorSwapUnitPosition</c>, <c>0x5d5642</c>): юнит клетки <paramref name="fromPos"/>
    /// меняется с содержимым клетки <paramref name="toPos"/>. Если в обмене большой юнит, меняются колонки целиком
    /// (обе клетки; юнит второй клетки уходит вместе с ним — журнал human, день 1: Myrmidon в отряд, два малых в гарнизон):
    /// обе позиции сводятся к передним клеткам своих колонок (<c>0x6116ba</c>: юнит позиции большой — обе позиции, не передние
    /// (<c>0x6114c6</c>), уменьшаются на 1), так что клетка заднего ряда отказа не даёт (журнал human t=265422: малый с задней клетки 5
    /// на большого в колонке 0 — <c>swap_check</c> 0). Ушедшие из группы юниты выбывают из её списка, пришедшие дописываются в конец
    /// (сохранения: <c>S073KC0000</c> дни 23, 31). Null — обмен невозможен (клетка пуста). Вместимость и «лидер/страж не уходит»
    /// проверяет вызывающий.
    /// </summary>
    // ponytail: ранг 2 — canApply 0x61111f (снятие vt+0x24, добавление vt+0x20, клетки 0x611744) не расписан до конца;
    // модель «колонка целиком» сверена по журналу и сохранениям.
    public static (UnitCells From, UnitCells To, IReadOnlyList<string> Left, IReadOnlyList<string> Came)? Swap(
        UnitCells from, int fromPos, UnitCells to, int toPos)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        string u = from.Cells[fromPos], o = to.Cells[toPos];
        if (u.Length == 0) return null;
        bool column = IsBig(from, u) || (o.Length > 0 && IsBig(to, o));
        if (column) (fromPos, toPos) = (Formation.ColumnFront(fromPos), Formation.ColumnFront(toPos));
        int[] fc = column ? [fromPos, Formation.Pair(fromPos)] : [fromPos];
        int[] tc = column ? [toPos, Formation.Pair(toPos)] : [toPos];
        var fromCells = from.Cells.ToArray();
        var toCells = to.Cells.ToArray();
        var left = fc.Select(c => fromCells[c]).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var came = tc.Select(c => toCells[c]).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < fc.Length; i++) (fromCells[fc[i]], toCells[tc[i]]) = (toCells[tc[i]], fromCells[fc[i]]);
        var fromUnits = from.Units.Where(id => !left.Contains(id, StringComparer.OrdinalIgnoreCase)).Concat(came).ToList();
        var toUnits = to.Units.Where(id => !came.Contains(id, StringComparer.OrdinalIgnoreCase)).Concat(left).ToList();
        return (new UnitCells(fromUnits, fromCells), new UnitCells(toUnits, toCells), left, came);
    }

    private static bool IsBig(UnitCells g, string id)
    {
        int p = g.PositionOf(id);
        return p >= 0 && string.Equals(g.Cells[Formation.Pair(p)], id, StringComparison.OrdinalIgnoreCase);
    }
}
