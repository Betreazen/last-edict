using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;

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

    /// <summary>Снимки для проверок экранов (<see cref="BalanceStand.Snapshots"/>, tools/CheckSaves): день — тот, что просили; человек —
    /// один, его ход начат (отряды с запасом хода), окон нет; день раньше начала пропускается, не обрывая остальные; то же зерно — тот же
    /// снимок.</summary>
    [Fact]
    public void Snapshots_DaysWithHumanTurnBegun_RepeatBySeed()
    {
        var book = GameBook.Load(TestPackTests.Dir);
        var scenario = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), "twin-fields"));
        var start = BalanceStand.Start(scenario, book, 1).State;
        string human = start.Looks.Map.Players.Keys.Order(StringComparer.Ordinal).First(id => book.Economy.Races[start.Looks.Map.Players[id].RaceId].Playable);
        var first = BalanceStand.Snapshots(start, book, 7, [3, Days], human).ToList();
        var again = BalanceStand.Snapshots(start, book, 7, [3, Days], human).ToList();
        var later = BalanceStand.Snapshots(first[0].State, book, 7, [1, Days], human).ToList();

        Assert.Equal([3, Days], first.Select(s => s.Day));
        Assert.Equal([Days], later.Select(s => s.Day));   // день 1 — раньше начала (день 3): пропущен, остальные идут
        Assert.Equal(first.Select(s => StateHash.Of(s.State)), again.Select(s => StateHash.Of(s.State)));
        Assert.All(first, s =>
        {
            Assert.Equal(s.Day, s.State.Looks.Map.Turn);
            Assert.Equal([human], s.State.Info.Humans);
            Assert.Equal(human, s.State.ActiveHuman);
            Assert.Single(s.State.Looks.Map.Players.Values, p => p.IsHuman);
            Assert.True(s.State.Looks.Map.Players[human].IsHuman);
            Assert.Empty(s.State.Messages);
            Assert.Empty(s.State.Offers);
            Assert.All(s.State.Looks.Map.Stacks.Values.Where(st => st.Owner == human && st.LeaderAlive), st => Assert.True(st.Move > 0));
        });
    }
}
