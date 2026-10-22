using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Rng;

namespace LastEdict.Data.Tests;

/// <summary>Пересев генераторов партии (<see cref="RandomReseed"/>, Э4): зерно задаёт все потоки сразу (<see cref="RandomStreams.FromSeed"/>),
/// у каждой системы — свой. CRT — поток игры: у загрузки новый (<c>srand(1)</c>, состояние 1), у новой партии (<c>keepCrt</c>) — её шаг
/// <c>0x42af9b</c> сохраняется.</summary>
public class RandomReseedTests
{
    [Fact]
    public void Reseed_DerivesEveryStreamFromTheSeed()
    {
        var map = new LastEdict.Rules.Map.MapState
        {
            Size = 1, Tiles = new LastEdict.Rules.Map.Tile[1, 1], Stacks = new Dictionary<string, LastEdict.Rules.Map.MapStack>(),
            Units = new Dictionary<string, LastEdict.Rules.Map.MapUnit>(), Players = new Dictionary<string, LastEdict.Rules.Map.MapPlayer>(),
            Objects = new Dictionary<string, LastEdict.Rules.Map.MapObject>(), Plan = new Dictionary<(int, int), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(), Turn = 1,
        };
        var looks = new MapLooks { Map = map, TreeImage = new byte[1, 1], TreeGroup = new byte[1, 1] };
        var fresh = new GameState(new GameInfo("t", "t", [], 1, 1), looks);

        var a = fresh.Reseeded(1003);
        Assert.Equal(RngStreams.All.Count, a.Random!.Streams.Count);
        Assert.Equal(1u, a.Random.Crt);
        // То же зерно — те же потоки; у потоков разные таблицы.
        Assert.Equal(a.Random.Of(RngStream.Ai)!.Table, fresh.Reseeded(1003).Random!.Of(RngStream.Ai)!.Table);
        Assert.Equal(RngStreams.All.Count, a.Random.Streams.Values.Select(v => string.Join(",", v.Table)).Distinct().Count());
        Assert.NotEqual(a.Random.Of(RngStream.Battle)!.Table, fresh.Reseeded(1004).Random!.Of(RngStream.Battle)!.Table);

        var saved = a with { Random = a.Random with { Crt = 77 } };
        Assert.Equal(1u, saved.Reseeded(7).Random!.Crt);
        Assert.Equal(77u, saved.Reseeded(7, keepCrt: true).Random!.Crt);
    }

    /// <summary>Броски одного потока не сдвигают другой: сколько бы ни взял ИИ, бой идёт теми же числами — основа A/B баланса.</summary>
    [Fact]
    public void Streams_AreIndependent()
    {
        var battle1 = RngStreams.Derive(5, RngStream.Battle);
        var battle2 = RngStreams.Derive(5, RngStream.Battle);
        var ai = RngStreams.Derive(5, RngStream.Ai);
        for (int i = 0; i < 1000; i++) ai.Next(100);
        Assert.Equal(Enumerable.Range(0, 200).Select(_ => battle1.Next(100)), Enumerable.Range(0, 200).Select(_ => battle2.Next(100)));
        // Поток годен для бросков: значения покрывают весь диапазон и близки к равномерным.
        var counts = new int[10];
        var rng = RngStreams.Derive(5, RngStream.Loot);
        for (int i = 0; i < 100_000; i++) counts[rng.Next(10)]++;
        Assert.All(counts, c => Assert.InRange(c, 9_500, 10_500));
    }
}
