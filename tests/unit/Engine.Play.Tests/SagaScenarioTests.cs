using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Sg;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Главы саг из таблиц <c>Scens\*.dbf</c> (<c>docs/day-cycle.md</c> §15): собранный поток <c>.sg</c> разбирается тем же
/// разбором, что и файлы <c>Exports</c>, все объекты на месте, земля под столицами — раса владельца (проверка кодировки плиток).</summary>
public class SagaScenarioTests
{
    private static readonly Lazy<ScensTables?> Tables = new(() => Directory.Exists(GamePaths.Scens) ? ScensTables.Load(GamePaths.Scens) : null);

    private static ScensTables Scens()
    {
        Skip.If(Tables.Value is null, $"нет {GamePaths.Scens}");
        return Tables.Value!;
    }

    /// <summary>Земля (<see cref="Tile.Terrain"/>) под клетками столиц — по расе владельца; у штатных карт одна на расу.</summary>
    private static IEnumerable<(string Race, int Terrain)> CapitalLand(MapState map) =>
        from site in map.Sites
        where site.Kind == SiteKind.Capital && map.Players.ContainsKey(site.Owner)
        from dx in Enumerable.Range(0, 5)
        from dy in Enumerable.Range(0, 5)
        select (map.Players[site.Owner].RaceId.ToUpperInvariant(), map.At(site.X + dx, site.Y + dy).Terrain);

    [SkippableFact]
    public void AllChapters_Parse_WithEveryObject()
    {
        var t = Scens();
        Assert.Equal(48, t.Scenarios.Count);
        foreach (string scen in t.Scenarios)
        {
            var file = SgFile.Load(SagaSg.Build(t, scen));
            Assert.Equal(t.Rows("SObjMap", scen).Count, file.Raw.Objects.Count);
            var data = ScenarioFile.FromSg(scen, file);
            Assert.True(data.Looks.Map.Players.Count >= 2, scen);
            Assert.Contains(data.Looks.Map.Sites, s => s.Kind == SiteKind.Capital);
        }
        // Поля потока без источника в таблицах: номер подрасы (у всех штатных карт 0).
        Assert.Equal(["MidSubRace.NUMBER"], SagaSg.Defaulted.Order());
    }

    /// <summary>Под столицами саг — земля расы владельца, та же, что у столиц штатных карт (Exports): кодировка плиток верна.</summary>
    [SkippableFact]
    public void CapitalLand_MatchesExports()
    {
        var t = Scens();
        Skip.IfNot(Directory.Exists(GamePaths.Exports), "нет Exports");
        var known = Directory.GetFiles(GamePaths.Exports, "*.sg").Take(12)
            .SelectMany(p => CapitalLand(MapLoader.Load(p))).Distinct().ToDictionary(p => p.Race, p => p.Terrain);
        Assert.NotEmpty(known);
        int cells = 0;
        foreach (string scen in t.Scenarios)
        {
            var map = MapLooksLoader.Load(scen, SgFile.Load(SagaSg.Build(t, scen))).Map;
            foreach (var (race, terrain) in CapitalLand(map).Where(p => known.ContainsKey(p.Race)))
            {
                Assert.True(known[race] == terrain, $"{scen}: столица {race} на земле {terrain}, ждали {known[race]}");
                cells++;
            }
        }
        Assert.True(cells > 48 * 25, $"проверено клеток {cells}");
    }
}
