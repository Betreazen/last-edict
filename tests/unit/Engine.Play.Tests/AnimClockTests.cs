using LastEdict.Data.Map;

namespace LastEdict.Data.Tests;

/// <summary>Часы анимаций карты (<see cref="AnimClock"/>, <c>CTwoSpeedAnimator</c>) и отрезок движка (<see cref="PixelLine"/>, <c>0x51f452</c>).</summary>
public class AnimClockTests
{
    /// <summary>Две группы: 66 и 33 мс (<c>0x51d871</c>, <c>0x51d929</c>); прокрутка — быстрая, ход отряда — быстрая при
    /// <c>StackSmoothScroll</c>. Тик без догона (<c>0x51e23b</c>): после долгого кадра — один шаг.</summary>
    [Fact]
    public void Groups_AndTickWithoutCatchUp()
    {
        Assert.Equal((66, 33), (AnimClock.PeriodMs(AnimGroup.Slow), AnimClock.PeriodMs(AnimGroup.Fast)));
        Assert.Equal(AnimGroup.Fast, AnimClock.SmoothScroll);
        Assert.Equal((AnimGroup.Fast, AnimGroup.Slow), (AnimClock.StackWalk(true), AnimClock.StackWalk(false)));

        var fast = new AnimTicker(AnimGroup.Fast);
        Assert.False(fast.Advance(16.7));
        Assert.True(fast.Advance(16.7));    // 33,4 ≥ 33
        Assert.True(fast.Advance(500));     // долгий кадр — один тик
        Assert.False(fast.Advance(1));      // лишнее время сброшено
        var slow = new AnimTicker(AnimGroup.Slow);
        int ticks = 0;
        for (int frame = 0; frame < 60; frame++) if (slow.Advance(1000.0 / 60)) ticks++;   // секунда при 60 кадрах
        Assert.Equal(15, ticks);            // кадр Godot 16,7 мс: тик на каждом 4-м (66,7 мс)
    }

    /// <summary>Отрезок движка: точек <c>max(|dx|, |dy|) + 1</c>, начало и конец на месте; вниз — прямой Брезенхэм, вверх — построенный от
    /// нижнего конца (у (0,0)→(32,−16) точка 7 — (7, −3), у прямого прохода от начала — (7, −4)).</summary>
    [Fact]
    public void PixelLine_AsEngine()
    {
        Assert.Equal([(0, 0), (1, 1), (2, 1), (3, 2), (4, 2)], PixelLine.Points(0, 0, 4, 2));   // ошибка 0 — шаг по y сразу
        Assert.Equal((7, -3), PixelLine.Points(0, 0, 32, -16)[7]);   // от нижнего конца; прямой проход дал бы (7, −4)
        Assert.Equal([(3, 3)], PixelLine.Points(3, 3, 3, 3));
        var up = PixelLine.Points(400, 37, 0, 0);
        Assert.Equal(401, up.Length);
        Assert.Equal((400, 37), up[0]);
        Assert.Equal((0, 0), up[^1]);
        var steep = PixelLine.Points(-5, -40, 0, 0);
        Assert.Equal(41, steep.Length);
        Assert.All(steep.Zip(steep.Skip(1)), p => Assert.Equal(1, p.Second.Y - p.First.Y));
    }
}
