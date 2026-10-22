namespace D2.Data.Map;

/// <summary>
/// Отрезок между двумя точками экрана по точкам, как у движка (<c>0x51f452</c> → <c>0x51f4fe</c>): точек <c>max(|dx|, |dy|) + 1</c>, обе
/// конечные включены, нулевая — начало. Брезенхэм всегда идёт сверху вниз (от меньшего <c>y</c>): у отрезка, ведущего вверх, точки строятся от
/// конца и кладутся с последней (<c>0x51f522</c>), поэтому у таких отрезков точки не те, что у прямого прохода от начала. Главная ось —
/// <c>x</c>, если <c>|dx| &gt; |dy|</c> (<c>0x51f56b</c>), иначе <c>y</c> (<c>0x51f5eb</c>); ошибка — <c>2·d_малой − d_главной</c>, шаг по малой
/// оси при ошибке ≥ 0.
/// </summary>
public static class PixelLine
{
    /// <summary>Точки отрезка от (<paramref name="x0"/>, <paramref name="y0"/>) до (<paramref name="x1"/>, <paramref name="y1"/>).</summary>
    public static (int X, int Y)[] Points(int x0, int y0, int x1, int y1)
    {
        int n = Math.Max(Math.Abs(x0 - x1), Math.Abs(y0 - y1)) + 1;
        var points = new (int X, int Y)[n];
        int at = 0, dir = 1;
        int x = x0, y = y0, ex = x1, ey = y1;
        if (y0 > y1)
        {
            (at, dir) = (n - 1, -1);
            (x, y, ex, ey) = (x1, y1, x0, y0);
        }
        int dx = ex - x, dy = ey - y, step = 1;
        if (dx <= 0) (dx, step) = (-dx, -1);
        points[at] = (x, y);
        at += dir;
        if (dx > dy)
        {
            int error = (2 * dy) - dx;
            for (int i = 0; i < dx; i++, at += dir)
            {
                if (error >= 0) { y++; error += (2 * dy) - (2 * dx); }
                else error += 2 * dy;
                x += step;
                points[at] = (x, y);
            }
        }
        else
        {
            int error = (2 * dx) - dy;
            for (int i = 0; i < dy; i++, at += dir)
            {
                if (error >= 0) { x += step; error += (2 * dx) - (2 * dy); }
                else error += 2 * dx;
                y++;
                points[at] = (x, y);
            }
        }
        return points;
    }
}
