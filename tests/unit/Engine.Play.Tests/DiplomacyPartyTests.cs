using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Сторона дипломатии игрока (<see cref="MapPlayer.Party"/>, этап Э2, задача E): игрок отдельно от расы. Без своей стороны —
/// род расы, как у движка (все партии оригинала и харнессы); со своей — два игрока одной расы ведут дипломатию каждый за себя.</summary>
public class DiplomacyPartyTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Два ИИ «Растленной власти» — одной расы (у второго раса первого) и со своими сторонами 100 и 101: дань первого второму меняет
    /// отношение пары (100, 101), а записи пар родов рас не трогает; союз — по паре сторон; земля стороны — земля расы её игрока.</summary>
    [SkippableFact]
    public void SameRacePlayers_WithOwnParties_KeepTheirOwnRelation()
    {
        Skip.IfNot(File.Exists(Path.Combine(GamePaths.Exports, "Power to Corrupt.sg")), "нет копии игры");
        var looks = MapLooksLoader.Load(Path.Combine(GamePaths.Exports, "Power to Corrupt.sg"));
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var races = book.Economy.Races;
        var playable = looks.Map.Players.Values.Where(p => races.TryGetValue(p.RaceId, out var r) && r.Playable)
                            .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        var (a, b) = (playable[0] with { Party = 100, Bank = playable[0].Bank with { Gold = 5000 } }, playable[1] with { RaceId = playable[0].RaceId, Party = 101 });
        var players = looks.Map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        (players[a.Id], players[b.Id]) = (a, b);
        var before = looks.Map.Relations;
        var map = GameSession.With(looks.Map, players: players, relations: [.. before, new Relation(100, 101, 50u << 1)]);
        var game = new GameSession(new GameState(GameInfo.FromMap("PtC", looks), looks with { Map = map }), SharedContent.Load(Data).MapArt, book);

        Assert.Equal(100, Diplomacy.PartyOf(game.Map, races, a.Id));
        Assert.Contains(101, Diplomacy.PlayerParties(game.Map, book.Economy));
        Assert.Equal(races[a.RaceId].Terrain, Diplomacy.TerrainOfParty(game.Map, book.Economy, 101));
        Assert.False(Diplomacy.AreAllies(game.Map.Relations, 100, 101));

        long gold = book.Diplomacy.GoldPerPoint * 10;
        Assert.Null(game.Run<string?>(new GiveGoldCommand(a.Id, b.Id, gold)));
        Assert.True(Diplomacy.Current(game.Map.Relations, 100, 101) > 50);
        Assert.Equal(before, game.Map.Relations.Where(r => !r.Pairs(100, 101)).ToList());
    }
}
