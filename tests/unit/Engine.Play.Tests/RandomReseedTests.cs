using LastEdict.Data.Map;
using LastEdict.Data.Play;

namespace LastEdict.Data.Tests;

/// <summary>Пересев генератора боя из интерфейса (<see cref="RandomReseed"/>, «Random» движка, <c>0x429dc8</c>): меняется только индекс
/// (<c>seed % 250</c>), таблица — прежняя; у новой партии таблица — исходная. CRT — поток игры: у загрузки новый (<c>srand(1)</c>,
/// состояние 1), у новой партии (<c>keepCrt</c>) — её шаг <c>0x42af9b</c> сохраняется.</summary>
public class RandomReseedTests
{
    [Fact]
    public void Reseed_ChangesOnlyIndex()
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
        Assert.Equal(1003 % 250, a.Random!.Index);
        Assert.Equal(new LastEdict.Rules.Rng.EngineRng().TableSnapshot(), a.Random.Table);
        Assert.Equal(1u, a.Random.Crt);

        var saved = a with { Random = a.Random with { Table = [.. a.Random.Table.Select(v => (ushort)(v ^ 1))], Crt = 77 } };
        var b = saved.Reseeded(7);
        Assert.Equal(7, b.Random!.Index);
        Assert.Equal(saved.Random!.Table, b.Random.Table);
        Assert.Equal(1u, b.Random.Crt);
        Assert.Equal(77u, saved.Reseeded(7, keepCrt: true).Random!.Crt);
    }
}
