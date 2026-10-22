using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Ход партии без Godot (<see cref="GameSession"/>): конец дня и перемещение отряда.</summary>
public class GameSessionTests(ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static GameSession Session(string scenario)
    {
        var looks = MapLooksLoader.Load(Path.Combine(GamePaths.Exports, scenario + ".sg"));
        var content = SharedContent.Load(Data);
        return new GameSession(new GameState(GameInfo.FromMap(scenario, looks), looks), content.MapArt,
                               GameBook.Load(Path.Combine(Data, "rules")));
    }

    /// <summary>Конец дня: каждый играбельный игрок (здесь все — ИИ, людей в файле сценария нет) получает дневной
    /// доход, со второго дня его земля растёт, отрядам возвращается полный запас, день растёт на единицу.</summary>
    [SkippableFact]
    public void EndTurn_Income_Land_Moves_Day()
    {
        Skip.IfNot(File.Exists(Path.Combine(GamePaths.Exports, "Power to Corrupt.sg")), "нет копии игры");
        var session = Session("Power to Corrupt");
        var before = session.Map;
        var dwarf = before.Players.Values.First(p => p.RaceId.Equals("G000RR0001", StringComparison.OrdinalIgnoreCase));
        int ownBefore = Own(before, 2);

        session.EndTurn();                       // день 0 → 1 и 1 → 2: в первый день земля ещё не растёт
        session.EndTurn();
        before = session.Map;
        dwarf = before.Players[dwarf.Id];
        ownBefore = Own(before, 2);
        var report = session.EndTurn();
        var after = session.Map;
        output.WriteLine($"день {before.Turn} → {after.Turn}: {string.Join("; ", report.Events)}; земля Кланов {ownBefore} → {Own(after, 2)}");

        Assert.Equal(before.Turn + 1, after.Turn);
        Assert.Equal(report.Day, after.Turn);
        var gained = after.Players[dwarf.Id].Bank - dwarf.Bank;
        Assert.True(gained.Gold > 0, "золото столицы пришло");
        Assert.True(Own(after, 2) > ownBefore, "земля Кланов выросла");
        Assert.True(Own(after, 2) - ownBefore <= 10 + (9 * 29), "рост не больше дневной квоты столицы и квадратов жил");
        // Люди начинают день с полным запасом; ИИ свой запас уже потратил тактикой в своём ходу.
        var humans = session.State.Info.Humans.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in after.Stacks.Values.Where(s => humans.Contains(s.Owner)))
            Assert.Equal(session.DayAllowance(stack), stack.Move);
        foreach (var stack in after.Stacks.Values.Where(s => s.Owner == dwarf.Id))
            Assert.InRange(stack.Move, 0, session.DayAllowance(stack));
        var neutral = after.Players.Values.First(p => p.RaceId.EndsWith("RR0004", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before.Players[neutral.Id].Bank, neutral.Bank);
    }

    /// <summary>Партия с победителем кончилась: «Конец хода» не проводит ходов ИИ и не начинает новый день; итог — победа
    /// победителя и поражение остальных.</summary>
    [SkippableFact]
    public void EndTurn_AfterVictory_DoesNothing()
    {
        Skip.IfNot(File.Exists(Path.Combine(GamePaths.Exports, "Power to Corrupt.sg")), "нет копии игры");
        var session = Session("Power to Corrupt");
        Assert.False(session.Over);
        string winner = session.Map.Players.Keys.First();
        string other = session.Map.Players.Keys.Last();
        Assert.Null(session.Won(winner));
        var ended = new GameSession(session.State with { Info = session.State.Info with { Winner = winner } }, session.Art, session.Book);
        int day = ended.Map.Turn;
        var report = ended.EndTurn();
        Assert.True(ended.Over);
        Assert.Equal(day, ended.Map.Turn);
        Assert.Empty(report.Events);
        Assert.True(ended.Won(winner));
        Assert.False(ended.Won(other));
    }

    /// <summary>Своих клеток террейна <paramref name="terrain"/>.</summary>
    private static int Own(MapState map, int terrain)
    {
        int n = 0;
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
                if (map.Tiles[x, y].Terrain == terrain && map.Tiles[x, y].Ground is Ground.Plain or Ground.Forest) n++;
        return n;
    }
}
