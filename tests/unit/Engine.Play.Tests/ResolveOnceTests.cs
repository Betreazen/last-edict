using LastEdict.Data.Play;

namespace LastEdict.Data.Tests;

/// <summary>Бой ИИ, который ведёт экран, при уходе вида доигрывается автобоем ровно один раз (<c>MapView.AiBattle</c>): главный поток
/// (<c>CancelAi</c>) и рабочий (крючок <c>Battle</c> после отмены) могут прийти одновременно — второй ждёт итог первого.</summary>
public class ResolveOnceTests
{
    [Fact]
    public void TwoThreadsRunTheResolverOnce()
    {
        var once = new ResolveOnce<object>();
        int calls = 0;
        using var start = new Barrier(2);
        object? first = null, second = null;
        object Make()
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(50);   // автобой идёт долго — второй поток успевает прийти
            return new object();
        }
        var a = new Thread(() => { start.SignalAndWait(); first = once.Run(Make); });
        var b = new Thread(() => { start.SignalAndWait(); second = once.Run(Make); });
        a.Start();
        b.Start();
        a.Join();
        b.Join();

        Assert.Equal(1, calls);
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void ScreenResultWinsOverLaterResolve()
    {
        var once = new ResolveOnce<string>();
        Assert.True(once.TrySet("экран"));

        Assert.Equal("экран", once.Run(() => "автобой"));
        Assert.False(once.TrySet("ещё раз"));
        Assert.Equal("экран", once.Value);
    }

    [Fact]
    public void DeclinedBattleStaysNull()
    {
        var once = new ResolveOnce<string>();
        Assert.Null(once.Value);
        Assert.False(once.Done);

        Assert.True(once.TrySet(null));   // экран занят — ядро сыграет автобой само

        Assert.True(once.Done);
        Assert.Null(once.Run(() => "автобой"));
    }
}
