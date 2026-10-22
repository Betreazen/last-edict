namespace LastEdict.Data.Map;

/// <summary>
/// Шаг идущего отряда по ключевым кадрам, как у задачи движения движка (<c>docs/map-render-stacks.md</c> §6, разбор 11.10):
/// <list type="bullet">
/// <item>шаг между соседними клетками — отрезок Брезенхэма между их точками на экране (<c>0x51f452</c>): <c>(±32, ±16)</c>, <c>(0, ±32)</c>
/// или <c>(±64, 0)</c>; ключевые кадры — точки отрезка с номерами <c>0, s, 2s, …</c> меньше его длины (<c>0x4f5d8c</c>…<c>0x4f5df4</c>),
/// конец шага — первый кадр следующего;</item>
/// <item><c>s</c> = 14 / 30 / 52 при <c>PlayerSpeed</c> (свой отряд) или <c>OpponentSpeed</c> = 1 / 2 / 3 (<c>0x4f5d2b</c>), пополам
/// (с отбрасыванием дробной части, <c>cdq; sub; sar 1</c>) при <c>StackSmoothScroll</c> (<c>0x4f5d59</c>; в поставляемом <c>Disciple.ini</c> — 1);</item>
/// <item>кадр задачи — один ключевой кадр (<c>0x4f61e2</c>): отряд встаёт в точку кадра, вид центрируется на ней (<c>0x540c38</c> в
/// <c>0x4f6539</c>, а по концу пути — на последней клетке, <c>0x4f6671</c>), на первом кадре шага своего отряда открывается обзор вокруг
/// клетки, куда шаг ведёт (<c>0x4f6486</c> → <c>0x4f6856</c> → <c>0x5d52fe</c>; флаг задачи — владелец отряда = игрок клиента, <c>0x4f5b41</c>).</item>
/// </list>
/// Период кадра задачи — тик её группы часов (<see cref="AnimClock.StackWalk"/>, <c>0x4f5e7c</c>): 33 мс при <c>StackSmoothScroll</c>, иначе
/// 66 мс; кадр — один ключевой кадр за тик, без догона. Скорость в точках за секунду от флажка не зависит (7 точек за 33 мс или 14 за 66),
/// меняется только плавность. Проверка по журналу <c>20260919-human</c> (ini драйвера: <c>PlayerSpeed</c> 3, <c>StackSmoothScroll</c> 0 —
/// <c>s</c> = 52, медленная группа): шаг по клетке с 32 точками по главной оси — 1 кадр, 62–78 мс между строками <c>stack_move</c> клиента
/// (229 шагов), с 64 точками — 2 кадра, 125–141 мс (120 шагов); часы журнала идут квантами 15,6 мс. Прежнее «13 мс на кадр» (11.10) считало
/// ini поставки (<c>s</c> = 7) и расходилось с кодом движка.
/// </summary>
public static class StackWalk
{
    /// <summary>Мс на ключевой кадр: тик группы хода (<see cref="AnimClock.StackWalk"/>) — 33 мс при <c>StackSmoothScroll</c>, иначе 66.</summary>
    public static int KeyframeMs(bool smooth = SmoothScroll) => AnimClock.PeriodMs(AnimClock.StackWalk(smooth));

    /// <summary>Точек между кадрами при скорости 1 без плавной прокрутки.</summary>
    public const int BasePixels = 14;

    /// <summary><c>StackSmoothScroll</c> поставляемого ini: шаг кадра пополам (флажок окна настроек — <c>MapSettings.StackSmoothScroll</c>).</summary>
    public const bool SmoothScroll = true;

    /// <summary>Точек между ключевыми кадрами при множителе скорости (<c>MapSettings.SpeedFactor</c>: 1, 30/14, 52/14) и плавной
    /// прокрутке за отрядом (<paramref name="smooth"/>, по умолчанию — как в поставляемом ini).</summary>
    public static int PixelsPerKeyframe(float speedFactor, bool smooth = SmoothScroll)
    {
        int s = (int)Math.Round(BasePixels * Math.Max(0.01f, speedFactor));
        if (smooth) s /= 2;
        return Math.Max(1, s);
    }

    /// <summary>Ключевые кадры шага на экранный сдвиг <paramref name="dx"/>, <paramref name="dy"/> (из клетки в соседнюю): точки отрезка
    /// движка (<see cref="PixelLine"/>, <c>0x51f452</c>) с номерами 0, s, 2s, … меньше длины; у нулевого сдвига — один кадр (0, 0).</summary>
    public static IReadOnlyList<(int X, int Y)> Keyframes(int dx, int dy, int pixelsPerKeyframe)
    {
        int s = Math.Max(1, pixelsPerKeyframe);
        int n = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (n == 0) return [(0, 0)];
        var line = PixelLine.Points(0, 0, dx, dy);
        var frames = new List<(int X, int Y)>((n + s - 1) / s);
        for (int i = 0; i < n; i += s) frames.Add(line[i]);
        return frames;
    }
}
