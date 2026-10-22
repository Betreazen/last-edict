namespace LastEdict.Rules.Map;

/// <summary>
/// Форма пятна обзора вокруг «глаза» (отряда, города) при радиусе <c>r</c>:
/// <c>Square</c> — max(|dx|,|dy|) ≤ r, <c>Diamond</c> — |dx|+|dy| ≤ r,
/// <c>Circle</c> — dx²+dy² ≤ r². Движковой функции «раскрыть туман» в разборе нет (единственная
/// точка тумана в тулсете — <c>CMidgardMapFogApi::getFog</c>, это чтение), поэтому форма здесь
/// параметр, а не константа; чем и насколько её сузило сохранение — в <see cref="Fog"/>.
/// </summary>
public enum FogShape
{
    Square,
    Circle,
    Diamond,

    /// <summary>Квадрат без четырёх угловых клеток (|dx| = |dy| = r). Ранг 2: единственная форма,
    /// которая на прогоне 20260907-c4 (маршрут из журнала, 48 ходов, 5 контрольных точек) даёт
    /// 0 лишних и 0 недостающих клеток; чистый квадрат оставляет ровно угловые клетки на каждом
    /// диагональном шаге лишними, круг и ромб — сотни недостающих
    /// (<c>C4HarnessTests.C4_FogGeometry_RankedByFirstCheckpoint</c>). Ранг 1 с 17.10 — листинг <c>0x5d4dfc</c>: угол, совпавший с углом
    /// карты, движок не снимает (<see cref="Fog.Reveal"/>).</summary>
    SquareNoCorners,
}

/// <summary>
/// Туман войны: маска <c>[x, y]</c>, где <c>true</c> — клетка ещё скрыта (то же соглашение, что
/// в <see cref="MapState.Fog"/>, ранг 1 — измерено по столицам в сохранении).
///
/// <para><b>Как меряется форма.</b> Журнал зонда к туману слеп, поэтому эталон — туман человека из
/// сохранения <c>game-dev/SaveGame</c> (день 2, карта 48x48, открыто 320 клеток из 2304, открытое —
/// одна связная область, 74 смены открыто/скрыто). Числа печатает
/// <c>FogTests.FogShape_And_Radius_MeasuredAgainstTheSave</c>.</para>
///
/// <para>Процент совпавших клеток формы не выбирает: маска «всё скрыто» уже даёт 86,11 %, а
/// накопленный за партию туман пересчётом по текущим позициям не воспроизводится в принципе.
/// Решает односторонний признак: туман только накапливается, значит клетка, открытая нами вокруг
/// глаза, который стоит там и сейчас, обязана быть открытой и в эталоне. Клетка «открыто у нас,
/// скрыто в эталоне» — доказанный перебор, и наибольший радиус без единой такой клетки есть верхняя
/// граница обзора: <c>Square 4, Circle 5, Diamond 5</c> при <c>SCOUT</c> лидера = 5.</para>
/// </summary>
public static class Fog
{
    /// <summary>
    /// Ранг 2 — измерено по известному маршруту (прогон 20260907-c4, 48 ходов, 5 сохранений):
    /// квадрат радиуса <c>SCOUT</c> без четырёх угловых клеток вокруг каждой пройденной клетки
    /// плюс <c>GcityInf.SCOUT</c> по размеру города от каждой клетки его пятна дают маску,
    /// совпадающую с сохранением клетка в клетку (2304/2304 на всех пяти точках). Это же
    /// объясняет и раннее наблюдение на <c>game-dev/SaveGame</c>: чистый квадрат открывал (20,20),
    /// скрытую в сохранении, — угол dx=-5 dy=5. Круг при 5 давал 179 недостающих клеток на ходе 10.
    /// </summary>
    public const FogShape DefaultShape = FogShape.SquareNoCorners;

    /// <summary>Границы обзора отряда — <c>stackScoutRange</c> (<c>.rdata 0x6e88fc</c>), см. <see cref="Radius"/>.</summary>
    public const int MinScoutRange = 1, MaxScoutRange = 8;

    /// <summary>Всё скрыто.</summary>
    public static bool[,] Empty(int size)
    {
        var fog = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                fog[x, y] = true;
        return fog;
    }

    /// <summary>Клетка в пятне — чистая геометрия, без краёв карты: исключение «угол карты» у <see cref="FogShape.SquareNoCorners"/>
    /// есть только в <see cref="Reveal"/> (<c>0x5d4dfc</c>). <c>L_UNFOG</c> события (<c>GameSession.EventEffects</c>) берёт его отсюда —
    /// у угла карты не сверено.</summary>
    public static bool InSight(int dx, int dy, int radius, FogShape shape) => shape switch
    {
        FogShape.Square => Math.Abs(dx) <= radius && Math.Abs(dy) <= radius,
        FogShape.Diamond => Math.Abs(dx) + Math.Abs(dy) <= radius,
        FogShape.Circle => dx * dx + dy * dy <= radius * radius,
        FogShape.SquareNoCorners => Math.Abs(dx) <= radius && Math.Abs(dy) <= radius
                                    && !(Math.Abs(dx) == radius && Math.Abs(dy) == radius),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Открывает пятно вокруг клетки; за края карты не выходит. <see cref="FogShape.SquareNoCorners"/> — как у движка
    /// (<see cref="RevealSquare"/>).</summary>
    public static void Reveal(bool[,] fog, int x, int y, int radius, FogShape shape)
    {
        ArgumentNullException.ThrowIfNull(fog);
        if (shape == FogShape.SquareNoCorners)
        {
            RevealSquare(fog, x, y, radius);
            return;
        }
        int w = fog.GetLength(0), h = fog.GetLength(1);
        for (int cy = Math.Max(0, y - radius); cy <= Math.Min(h - 1, y + radius); cy++)
            for (int cx = Math.Max(0, x - radius); cx <= Math.Min(w - 1, x + radius); cx++)
                if (InSight(cx - x, cy - y, radius, shape))
                    fog[cx, cy] = false;
    }

    /// <summary>
    /// Список клеток обзора движка (<c>0x5d4dfc</c>; его зовут обзор отряда <c>0x5d5168</c>, туман и развеивание эффектом <c>0x5d5230</c>):
    /// прямоугольник объекта (у клетки — 1×1, <c>0x438462</c>), расширенный на <paramref name="radius"/>, без четырёх угловых клеток —
    /// кроме угла, который сам угол карты (<c>0x5d4e8a</c>: левый верхний остаётся, если левый край = 0 и верхний = 0; так же три
    /// остальных с краем <c>size − 1</c>). Прямоугольник зажимается в карту только при радиусе ≥ <c>size / 2</c> (<c>0x5d4e2a</c>), иначе
    /// угол — геометрический, и вне карты его просто нет. Сохранение оригинала карты 12 (<c>20261017-port-synth</c> t01-028): столица Кланов
    /// 5×5 в (6,6), обзор 6 — клетка (0,0) открыта, а (16,0), (0,16), (16,16) скрыты; у трёх ИИ открыт ближний угол карты так же.
    /// </summary>
    private static void RevealSquare(bool[,] fog, int x, int y, int radius)
    {
        int size = fog.GetLength(0), h = fog.GetLength(1);   // маски квадратные (у движка один size на обе оси)
        System.Diagnostics.Debug.Assert(size == h, "маска тумана не квадратная");
        int left = x - radius, top = y - radius, right = x + 1 + radius, bottom = y + 1 + radius;   // правый и нижний — не включая
        if (radius >= size / 2)
            (left, top, right, bottom) = (Math.Max(0, left), Math.Max(0, top), Math.Min(size, right), Math.Min(size, bottom));
        int last = size - 1;
        bool Cut(int cx, int cy) =>
            (cx == left && cy == top && !(left == 0 && top == 0))
            || (cx == left && cy == bottom - 1 && !(left == 0 && bottom - 1 == last))
            || (cx == right - 1 && cy == top && !(right - 1 == last && top == 0))
            || (cx == right - 1 && cy == bottom - 1 && !(right - 1 == last && bottom - 1 == last));
        for (int cy = Math.Max(0, top); cy < Math.Min(h, bottom); cy++)
            for (int cx = Math.Max(0, left); cx < Math.Min(size, right); cx++)
                if (!Cut(cx, cy))
                    fog[cx, cy] = false;
    }

    /// <summary>
    /// Радиус обзора отряда — <c>SCOUT</c> лидера из <c>Gunits.dbf</c> (у героя
    /// <c>g000uu0019</c> = 5). Ранг 3: чей именно <c>SCOUT</c> берёт движок — лидера или максимум
    /// по отряду — данные не различают, в сохранении у единственного отряда человека лидер и есть
    /// самый зоркий (5 и 5); тест печатает обе величины и говорит, совпали ли они. Само число 5
    /// подтверждено с одной стороны: круг радиуса 5 вокруг стоящего отряда не открывает ни одной
    /// скрытой в сохранении клетки, а радиуса 6 — уже открывает.
    /// Тип лидера неизвестен или его нет в таблице — <paramref name="fallback"/>.
    /// <para><b>Навык Keen-Sight (+3 обзора) — задача 11 Ф7:</b> это элемент
    /// <c>ScoutingRange</c> цепочки <c>MidUnit.MODIF_ID</c> (<c>NUMBER</c>, а не процент), и
    /// прибавляет его <see cref="LastEdict.Rules.Units.EffectiveStats.OfStack"/>.
    /// <paramref name="modifiers"/> — справочник <c>Gmodif</c>; не подан (<c>null</c>) — радиус
    /// остаётся базовым, как до задачи 11.</para>
    /// <para><b>Потолок <c>stackScoutRange</c> {1, 8}</b> (<c>.rdata 0x6e88fc</c>) — ранг 1: в ai-q39
    /// зонд <c>scout</c> (<c>0x5d520f</c>) у героя с <c>SCOUT</c> 6 и Keen-Sight вернул 8, а не 9
    /// (<c>scout_cap_8</c> 2/0, отчёт A §6).</para>
    /// </summary>
    public static int Radius(MapStack stack, MapState map, IReadOnlyDictionary<string, int> scoutByType,
                             int fallback = 1,
                             IReadOnlyDictionary<string, Units.Modifier>? modifiers = null)
    {
        if (!map.Units.TryGetValue(stack.LeaderId, out var leader)) return fallback;
        if (!scoutByType.TryGetValue(leader.TypeId, out int scout)) return fallback;
        return Math.Clamp(Units.EffectiveStats.OfStack(Movement.Chain(leader, modifiers),
                                                       scoutingRange: scout).ScoutingRange,
                          MinScoutRange, MaxScoutRange);
    }

    /// <summary>Максимальный <c>SCOUT</c> по отряду — вторая гипотеза к <see cref="Radius"/>.</summary>
    public static int MaxRadius(MapStack stack, MapState map, IReadOnlyDictionary<string, int> scoutByType,
                                int fallback = 1)
    {
        int best = -1;
        foreach (var id in stack.UnitIds)
            if (map.Units.TryGetValue(id, out var unit) && scoutByType.TryGetValue(unit.TypeId, out var scout))
                best = Math.Max(best, scout);
        return best < 0 ? fallback : best;
    }

    /// <summary>
    /// Туман игрока, пересчитанный по текущим позициям: открыто вокруг каждого его отряда и вокруг
    /// каждого «глаза» из <paramref name="extra"/> (города — у <see cref="MapObject"/> нет владельца,
    /// поэтому их подаёт вызывающий). <paramref name="radiusOverride"/> задаёт один радиус на всех —
    /// им и меряется форма.
    ///
    /// <para>Накопления нет и быть не может: файл помнит весь маршрут, а позиции — только
    /// последний день. Сверка с сохранением показывает разницу, а не совпадение.</para>
    /// </summary>
    public static bool[,] Recompute(MapState map, string playerId, IReadOnlyDictionary<string, int> scoutByType,
                                    FogShape shape, int? radiusOverride = null,
                                    IEnumerable<(int X, int Y, int Radius)>? extra = null)
    {
        var fog = Empty(map.Size);
        foreach (var stack in map.Stacks.Values)
            if (stack.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                Reveal(fog, stack.X, stack.Y, radiusOverride ?? Radius(stack, map, scoutByType), shape);
        foreach (var (x, y, r) in extra ?? [])
            Reveal(fog, x, y, radiusOverride ?? r, shape);
        return fog;
    }

    /// <summary>
    /// Сверка двух масок. <c>Same</c> — клетки с одинаковым битом, <c>OnlyOurs</c> — открытые
    /// только у нас (лишние), <c>OnlyTheirs</c> — открытые только в эталоне (недостающие).
    /// Асимметрия не сворачивается в одно число нарочно: у неё разный смысл.
    /// </summary>
    public static (int Total, int Same, int OnlyOurs, int OnlyTheirs) Compare(bool[,] ours, bool[,] theirs)
    {
        if (ours.GetLength(0) != theirs.GetLength(0) || ours.GetLength(1) != theirs.GetLength(1))
            throw new ArgumentException(
                $"размеры масок разные: {ours.GetLength(0)}x{ours.GetLength(1)} и " +
                $"{theirs.GetLength(0)}x{theirs.GetLength(1)}");

        int total = 0, same = 0, onlyOurs = 0, onlyTheirs = 0;
        for (int y = 0; y < ours.GetLength(1); y++)
            for (int x = 0; x < ours.GetLength(0); x++)
            {
                total++;
                if (ours[x, y] == theirs[x, y]) same++;
                else if (!ours[x, y]) onlyOurs++;
                else onlyTheirs++;
            }
        return (total, same, onlyOurs, onlyTheirs);
    }
}
