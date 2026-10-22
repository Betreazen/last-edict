namespace D2.Data.Map;

/// <summary>
/// Анимация смены тумана (<c>CMidAnim2ChangeFog</c>, разбор 16.10). Команду <c>CCmdChangeFogMsg</c> шлёт эффект <c>CEffectFog</c>
/// (<c>0x438314</c>: события <c>L_UNFOG</c> и <c>L_CREATE_STACK</c>, развеивание и туман заклинанием, призыв, смена владельца) с клеткой центра,
/// прямоугольником <c>(x, y)…(x + 1, y + 1)</c> (<c>0x438462</c>) и полем <c>r</c> — тем же радиусом, что меняет модель (<c>0x5d5230</c>).
/// Клиент перед анимацией прокручивает вид к клетке (<c>0x4f351d</c>: без проверки видимости, только если далеко), затем:
/// <list type="bullet">
/// <item>создание (<c>0x4f1a29</c>): прямоугольник шире на <c>r</c> со всех сторон и зажат в карту (<c>[0, размер]</c>); сторона
/// <c>s = право − лево</c>, центр <c>((лево + право) &gt;&gt; 1, (верх + низ) &gt;&gt; 1)</c>, кольцо 1;</item>
/// <item>шаг — на каждом тике медленной группы (66 мс, <see cref="AnimClock"/>; <c>0x4f1b63</c>): клетки периметра квадрата «кольцо × кольцо» от
/// угла <c>(cx, cy)</c>, обход по строкам, у кольца больше 1 угловые клетки сдвинуты на клетку внутрь по диагонали; каждой клетке в карте
/// ставится новый туман (<c>0x5f3bdc</c>) и перерисовываются она и соседи (<c>0x5ac56a</c>); затем <c>cx−1, cy−1</c>, кольцо + 2;</item>
/// <item>конец — на шаге, где <c>кольцо + 2 &gt; s</c> (<c>0x4f1b9f</c>): колец <c>⌊(s + 1) / 2⌋</c>, а у незажатого квадрата <c>r + 1</c> —
/// центр, затем кольца радиусов 1…r; угловые клетки последнего кольца не затронуты (форма эффекта — квадрат без углов, <c>0x5d4dfc</c>).</item>
/// </list>
/// Туман меняется по клеткам сразу, без прозрачности: от центра наружу, кольцо за тик.
/// </summary>
public sealed class FogReveal
{
    private readonly int _size, _ringsCount;
    private readonly int _cx, _cy;

    /// <param name="x">Клетка центра (<c>CEffectFog</c>, поле клетки).</param>
    /// <param name="y">Клетка центра.</param>
    /// <param name="radius">Радиус эффекта (<c>[effect+0x14]</c>): тот, на который модель открыла или закрыла квадрат.</param>
    /// <param name="mapSize">Сторона карты.</param>
    public FogReveal(int x, int y, int radius, int mapSize)
    {
        int left = Math.Max(0, x - radius), top = Math.Max(0, y - radius);
        int right = Math.Min(mapSize, x + 1 + radius), bottom = Math.Min(mapSize, y + 1 + radius);
        (X, Y, Radius) = (x, y, radius);
        _size = right - left;
        _cx = (left + right) >> 1;
        _cy = (top + bottom) >> 1;
        int rings = 0;
        for (int ring = 1; ; ring += 2)
        {
            rings++;
            if (ring + 2 > _size) break;
        }
        _ringsCount = rings;
        MapSize = mapSize;
    }

    public int X { get; }

    public int Y { get; }

    public int Radius { get; }

    public int MapSize { get; }

    /// <summary>Сколько шагов (тиков 66 мс) у анимации.</summary>
    public int Steps => _ringsCount;

    /// <summary>Клетки шага <paramref name="step"/> (0 — центр) в порядке движка; клетки за картой отброшены (<c>0x5f38de</c>).</summary>
    public IReadOnlyList<(int X, int Y)> Cells(int step)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(step, Steps);
        int ring = 1 + (2 * step), cx = _cx - step, cy = _cy - step;
        var cells = new List<(int X, int Y)>();
        for (int row = 0; row < ring; row++)
            for (int col = 0; col < ring; col++)
            {
                if (row != 0 && row != ring - 1 && col != 0 && col != ring - 1) continue;
                int px = cx + col, py = cy + row;
                if (ring > 1)
                {
                    // Углы — на клетку внутрь по диагонали (0x4f1c10…0x4f1c49).
                    bool left = col == 0, right = col == ring - 1, topRow = row == 0, bottomRow = row == ring - 1;
                    if ((left || right) && (topRow || bottomRow))
                    {
                        px += left ? 1 : -1;
                        py += topRow ? 1 : -1;
                    }
                }
                if (px >= 0 && py >= 0 && px < MapSize && py < MapSize) cells.Add((px, py));
            }
        return cells;
    }
}
