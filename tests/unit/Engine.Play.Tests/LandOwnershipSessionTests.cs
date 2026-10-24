using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Земля по владельцу клетки (<see cref="LandOwnership"/>) в партии test-pack: на <c>six-crowns</c> по двое игроков одной расы — земля
/// делится на старте, рост и доход у каждого свои, хозяева переживают сохранение и входят в хеш; на карте с разными расами слоя нет. Идёт в CI.</summary>
public class LandOwnershipSessionTests
{
    private static GameSession Start(string map, uint seed = 1)
    {
        var book = GameBook.Load(TestPackTests.Dir);
        var data = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), map));
        var state = new GameState(GameInfo.FromMap(data.Id, data.Looks), data.Looks)
        {
            Events = data.Events, EventProgress = EventProgress.From(data.Events),
        }.Reseeded(seed);
        return new GameSession(state, MapArtData.Empty, book);
    }

    /// <summary>Своя земля игроков одной расы не пересекается, у каждого есть, и вся земля общего рода поделена.</summary>
    private static void AssertSplit(GameSession session)
    {
        var map = session.Map;
        var races = session.Book!.Economy.Races;
        var shared = LandOwnership.SharedTerrains(map, session.Book.Economy);
        Assert.NotEmpty(shared);
        foreach (var (terrain, players) in shared)
        {
            var lands = players.Select(p => LandOwnership.OwnCells(map, p, terrain)).ToList();
            Assert.All(lands, land => Assert.NotEmpty(land));
            for (int i = 0; i < lands.Count; i++)
                for (int j = i + 1; j < lands.Count; j++)
                    Assert.Empty(lands[i].Intersect(lands[j]));
            int all = 0;
            for (int x = 0; x < map.Size; x++)
                for (int y = 0; y < map.Size; y++)
                    if (map.Tiles[x, y].Terrain == terrain) all++;
            Assert.Equal(all, lands.Sum(l => l.Count));
        }
        Assert.All(map.Players.Values.Where(p => races[p.RaceId].Playable),
                   p => Assert.True(map.Sites.Where(s => s.Kind == SiteKind.Capital && s.Owner == p.Id)
                                       .All(s => LandOwnership.IsOwn(map, s.X + 2, s.Y + 2, p.Id, races[p.RaceId].Terrain))));
    }

    [Fact]
    public void SameRace_SplitsTheLand_AndKeepsItSplitThroughTwentyDays()
    {
        var session = Start("six-crowns");
        Assert.True(LandOwnership.Tracked(session.Map));
        AssertSplit(session);
        for (int day = 0; day < 20; day++) session.Execute(new EndTurnCommand());
        AssertSplit(session);
    }

    [Fact]
    public void DistinctRaces_HaveNoOwnerLayer()
    {
        var session = Start("twin-fields");
        Assert.False(LandOwnership.Tracked(session.Map));
        Assert.Null(session.Map.LandOwners);
    }

    /// <summary>Хозяева земли пишутся в сохранение, читаются без нового дележа и входят в хеш состояния (сеть сверяет его у хоста и клиента).</summary>
    [Fact]
    public void Owners_SurviveTheSave_AndAreInTheStateHash()
    {
        var session = Start("six-crowns");
        for (int day = 0; day < 5; day++) session.Execute(new EndTurnCommand());
        var state = session.SnapshotWithoutSummaries();
        var read = GameSave.FromJson(GameSave.ToJson(state));
        Assert.Equal(state.Looks.Map.LandOwnerIds, read.Looks.Map.LandOwnerIds);
        Assert.Equal(state.Looks.Map.LandOwners!.Cast<byte>(), read.Looks.Map.LandOwners!.Cast<byte>());
        Assert.Equal(StateHash.Of(state), StateHash.Of(new GameSession(read, MapArtData.Empty, session.Book)));

        var owners = (byte[,])state.Looks.Map.LandOwners!.Clone();
        var (x, y) = Enumerable.Range(0, state.Looks.Map.Size * state.Looks.Map.Size)
                               .Select(i => (X: i / state.Looks.Map.Size, Y: i % state.Looks.Map.Size)).First(c => owners[c.X, c.Y] != 0);
        owners[x, y] = (byte)(owners[x, y] == 1 ? 2 : 1);
        var changed = state with
        {
            Looks = GameSession.Retold(state.Looks, GameSession.WithLandOwners(state.Looks.Map, owners, state.Looks.Map.LandOwnerIds!),
                                       state.Looks.Stacks),
        };
        Assert.NotEqual(StateHash.Of(state), StateHash.Of(changed));
    }
}
