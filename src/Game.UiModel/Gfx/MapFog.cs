namespace LastEdict.Data.Gfx;

/// <summary>Туман войны на карте (<c>docs/map-render-stacks.md</c> §4): картинка скрытой клетки по её соседям,
/// рамка за краем карты и правило, прячется ли картинка объекта. <c>hidden(x, y)</c> — клетка скрыта для игрока;
/// клетки за картой считаются скрытыми.</summary>
public static class MapFog
{
    /// <summary>Соседи в порядке движка (<c>0x6f1eb8</c>); первый — старший бит маски.</summary>
    private static readonly (int Dx, int Dy)[] Neighbours =
        [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    /// <summary>Маска соседей → номер картинки <c>FOG{nn}</c> (заполнение <c>0x60a021</c>, приложение A).</summary>
    private static readonly byte[] Table =
    [
        1, 54, 1, 58, 55, 7, 63, 7, 1, 54, 1, 58, 59, 33, 67, 33,
        52, 2, 52, 43, 6, 11, 29, 11, 60, 47, 60, 51, 6, 11, 29, 11,
        1, 54, 1, 58, 55, 7, 63, 7, 1, 54, 1, 58, 59, 33, 67, 33,
        56, 41, 56, 37, 30, 27, 15, 27, 64, 68, 64, 77, 30, 27, 15, 27,
        53, 5, 53, 32, 3, 10, 42, 10, 53, 5, 53, 32, 40, 26, 71, 26,
        4, 9, 4, 21, 8, 0, 24, 0, 34, 25, 34, 17, 8, 0, 24, 0,
        61, 35, 61, 13, 44, 22, 38, 22, 61, 35, 61, 13, 48, 18, 76, 18,
        4, 9, 4, 21, 8, 0, 24, 0, 34, 25, 34, 17, 8, 0, 24, 0,
        1, 62, 1, 66, 55, 28, 63, 28, 1, 62, 1, 66, 59, 14, 67, 14,
        52, 45, 52, 70, 6, 23, 29, 23, 60, 39, 60, 73, 6, 23, 29, 23,
        1, 62, 1, 66, 55, 28, 63, 28, 1, 62, 1, 66, 59, 14, 67, 14,
        56, 49, 56, 75, 30, 19, 15, 19, 64, 79, 64, 80, 30, 19, 15, 19,
        57, 5, 57, 32, 46, 10, 50, 10, 57, 5, 57, 32, 36, 26, 78, 26,
        31, 9, 31, 21, 20, 0, 16, 0, 12, 25, 12, 17, 20, 0, 16, 0,
        65, 35, 65, 13, 69, 22, 74, 22, 65, 35, 65, 13, 72, 18, 81, 18,
        31, 9, 31, 21, 20, 0, 16, 0, 12, 25, 12, 17, 20, 0, 16, 0,
    ];

    /// <summary>Рамка края: индекс <c>cx + 3·cy</c> (0 — край 0, 1 — внутри, 2 — край N−1) → <c>t</c> картинки
    /// <c>FOG{82+t}00</c> (<c>0x621156</c>); у внутренней клетки рамки нет.</summary>
    private static readonly int[] BorderSlots = [0, 1, 2, 7, -1, 3, 6, 5, 4];

    /// <summary>Маска соседей: бит = сосед за картой или тоже скрыт; <c>(−1,−1)</c> — старший (<c>0x620fcb</c>).</summary>
    public static int Mask(Func<int, int, bool> hidden, int x, int y, int size)
    {
        int mask = 0;
        foreach (var (dx, dy) in Neighbours)
        {
            int nx = x + dx, ny = y + dy;
            bool set = nx < 0 || ny < 0 || nx >= size || ny >= size || hidden(nx, ny);
            mask = (mask << 1) | (set ? 1 : 0);
        }
        return mask;
    }

    /// <summary>Имя картинки тумана скрытой клетки: <c>FOG{nn:02}{vv:02}</c>; при <c>nn = 0</c> вариант
    /// <paramref name="variant"/> (у движка <c>rand() % 5</c>), иначе 0.</summary>
    public static string Name(int mask, int variant) =>
        Table[mask] is var nn && nn == 0 ? $"FOG00{variant % 5:00}" : $"FOG{Table[mask]:00}00";

    /// <summary>Рамка за краем карты у скрытой краевой клетки: <c>FOG{82+t}00</c>, у внутренней — null.</summary>
    public static string? Border(int x, int y, int size)
    {
        int cx = x == 0 ? 0 : x == size - 1 ? 2 : 1, cy = y == 0 ? 0 : y == size - 1 ? 2 : 1;
        int t = BorderSlots[cx + (3 * cy)];
        return t < 0 ? null : $"FOG{82 + t}00";
    }

    /// <summary>Прячется ли картинка высотой <paramref name="imageHeight"/> точек у элемента <c>sx×sy</c> с якорем
    /// <c>(ax, ay)</c>: да, только если скрыты все клетки прямоугольника вокруг (<c>0x5bdd52</c>, <c>0x5bdea6</c>),
    /// обрезанного по карте.</summary>
    public static bool Hides(Func<int, int, bool> hidden, int ax, int ay, int sx, int sy, int imageHeight, int size)
    {
        int k = (imageHeight + 31) / 32;
        int x0 = Math.Max(0, ax - (k / 2) - 1), x1 = Math.Min(size - 1, ax + (sx / 2) + 1);
        int y0 = Math.Max(0, ay - (k / 2) - 1), y1 = Math.Min(size - 1, ay + (sy / 2) + 1 - (sx % 2 == 0 ? 1 : 0));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (!hidden(x, y)) return false;
        return true;
    }
}
