using LastEdict.Data.Play;

namespace LastEdict.Data.Tests;

/// <summary>Стенд симуляции баланса (<see cref="BalanceStand"/>, Э4): партия с тем же зерном повторяется бит в бит — и по одной, и параллельно
/// с другими; строка на каждого играбельного игрока; в нерешённой партии победитель один или никого. На своём содержимом — идёт в CI.</summary>
public class BalanceStandTests
{
    private const int Days = 6;

    [Fact]
    public void Games_RepeatBySeed_SerialOrParallel()
    {
        var book = GameBook.Load(TestPackTests.Dir);
        var scenario = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), "twin-fields"));
        uint[] seeds = [1, 2, 3, 4];
        var serial = seeds.Select(s => BalanceStand.Play(scenario, book, s, Days)).ToList();
        var parallel = new IReadOnlyList<BalanceRow>[seeds.Length];
        Parallel.For(0, seeds.Length, i => parallel[i] = BalanceStand.Play(scenario, book, seeds[i], Days));
        for (int i = 0; i < seeds.Length; i++) Assert.Equal(serial[i], parallel[i]);
        Assert.NotEqual(serial[0], serial[1]);   // зерно меняет партию
        Assert.All(serial, rows =>
        {
            Assert.Equal(2, rows.Count);
            Assert.Single(rows, r => r.First);
            Assert.True(rows.Count(r => r.Won) <= 1);
            Assert.All(rows, r => Assert.InRange(r.Score, 0, int.MaxValue));
        });
    }
}
