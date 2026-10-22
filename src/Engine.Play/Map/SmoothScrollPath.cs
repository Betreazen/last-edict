namespace D2.Data.Map;

/// <summary>
/// Плавная прокрутка вида к клетке события (<c>CMidAnimSmoothScroll</c>, разбор 16.10). Задачу ставит перед анимацией команды
/// <c>0x4f4a0c(игрок, …, клетка, без_проверки_видимости, только_если_далеко, система)</c>:
/// <list type="bullet">
/// <item>без флага «без проверки» — только если видна хоть одна клетка окна рядом (<c>0x4f4ce9</c>, те же клетки, что у литья); не видна —
/// прокрутки нет и команда не анимируется;</item>
/// <item>«только если далеко» — клетка центра вида (<c>0x540b6c</c>) и цель расходятся на 2 и больше хотя бы по одной оси (<c>0x4f4a7c</c>:
/// <c>cmp eax, 2; jge</c>), иначе вид стоит;</item>
/// <item>флажок <c>SmoothScroll</c> настроек (<c>settings+0x17b</c>, <c>0x61a5c1</c>) выключен — вид встаёт на цель сразу (<c>0x4f4e48</c> →
/// <c>0x540c38</c> без сдвига); включён — строится список положений (<c>0x4f4b51</c>), и вид проходит его по одному положению за тик быстрой
/// группы (<see cref="AnimClock.SmoothScroll"/>, 33 мс; <c>0x4f4dac</c>: центр — на цель со сдвигом из головы списка, голова снимается).</item>
/// </list>
/// Список (<see cref="Offsets"/>): сдвиги центра вида от середины клетки цели по точкам отрезка (<see cref="PixelLine"/>) от нынешнего сдвига
/// до нуля; с начала отрезка — номера 0, 4, 12, 24, … (шаг растёт на 4 до 128, <c>0x4f4c91</c>…<c>0x4f4cca</c>), пока меньше половины точек, с
/// конца — симметрично (<c>0x4f4c55</c>…<c>0x4f4c77</c>): разгон и торможение, последнее положение — ровно цель. Центр вида уже в клетке цели —
/// списка нет (<c>0x4f4b76</c>).
/// </summary>
public static class SmoothScrollPath
{
    /// <summary>Прирост шага по точкам отрезка и потолок шага (<c>add 4</c>, <c>cmp 0x80</c>).</summary>
    public const int StepGrowth = 4, MaxStep = 128;

    /// <summary>Расхождение по оси, с которого прокрутка «только если далеко» идёт (<c>0x4f4a7c</c>).</summary>
    public const int FarCells = 2;

    /// <summary>Нужна ли прокрутка: при «только если далеко» — расхождение клеток не меньше <see cref="FarCells"/> по одной из осей.</summary>
    public static bool Wanted((int X, int Y) centre, (int X, int Y) target, bool onlyIfFar) =>
        !onlyIfFar || Math.Abs(centre.X - target.X) >= FarCells || Math.Abs(centre.Y - target.Y) >= FarCells;

    /// <summary>Положения центра вида — сдвиги от середины клетки цели, по одному на тик: первое — нынешний сдвиг
    /// (<paramref name="dx"/>, <paramref name="dy"/>), последнее — (0, 0).</summary>
    public static IReadOnlyList<(int X, int Y)> Offsets(int dx, int dy)
    {
        var line = PixelLine.Points(dx, dy, 0, 0);
        int n = line.Length, half = n / 2;
        var head = new List<(int X, int Y)>();
        for (int i = 0, step = 0; i < half; i += step)
        {
            head.Add(line[i]);
            if (step < MaxStep) step += StepGrowth;
        }
        var tail = new List<(int X, int Y)>();
        for (int i = n - 1, step = 0; i >= half; i -= step)
        {
            tail.Add(line[i]);
            if (step < MaxStep) step += StepGrowth;
        }
        tail.Reverse();
        return [.. head, .. tail];
    }
}
