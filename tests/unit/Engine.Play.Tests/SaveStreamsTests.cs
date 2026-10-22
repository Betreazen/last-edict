using System.Text.Json.Nodes;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Rng;

namespace LastEdict.Data.Tests;

/// <summary>Сохранение версии 12 — генераторы по потокам (Э4); у сохранения до 12 единый генератор становится потоком боя, прочие потоки
/// выводятся из его места, повторяемо. На своём содержимом — идёт в CI.</summary>
public class SaveStreamsTests
{
    private static GameState Start()
    {
        var data = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), "twin-fields"));
        return new GameState(GameInfo.FromMap(data.Id, data.Looks), data.Looks).Reseeded(5);
    }

    [Fact]
    public void Save_RoundTripsEveryStream()
    {
        var state = Start();
        var back = GameSave.FromJson(GameSave.ToJson(state));
        Assert.Equal(RngStreams.All.Count, back.Random!.Streams.Count);
        foreach (var s in RngStreams.All) Assert.Equal(state.Random!.Of(s)!.Table, back.Random.Of(s)!.Table);
    }

    [Fact]
    public void OldSave_SingleGenerator_BecomesBattleStream_OthersDerivedRepeatably()
    {
        var node = JsonNode.Parse(GameSave.ToJson(Start()))!;
        var table = new EngineRng(77).TableSnapshot();
        node["Version"] = 11;
        node["Random"] = new JsonObject { ["Index"] = 77, ["Table"] = new JsonArray([.. table.Select(w => (JsonNode)w)]), ["Crt"] = 9 };
        string old = node.ToJsonString();

        var a = GameSave.FromJson(old).Random!;
        var b = GameSave.FromJson(old).Random!;
        Assert.Equal(77, a.Of(RngStream.Battle)!.Index);
        Assert.Equal(table, a.Of(RngStream.Battle)!.Table);
        Assert.Equal(9u, a.Crt);
        Assert.NotEqual(table, a.Of(RngStream.Ai)!.Table.ToArray());
        foreach (var s in RngStreams.All) Assert.Equal(a.Of(s)!.Table, b.Of(s)!.Table);

        var session = new GameSession(GameSave.FromJson(old), MapArtData.Empty);
        Assert.Equal(77, session.BattleRng.Index);
    }

    [Fact]
    public void Save_WithoutAStream_IsRefused()
    {
        var node = JsonNode.Parse(GameSave.ToJson(Start()))!;
        node["Random"]!["Streams"]!.AsObject().Remove("loot");
        var damaged = GameSave.FromJson(node.ToJsonString());
        var ex = Assert.Throws<InvalidDataException>(() => new GameSession(damaged, MapArtData.Empty));
        Assert.Contains("loot", ex.Message, StringComparison.Ordinal);
    }
}
