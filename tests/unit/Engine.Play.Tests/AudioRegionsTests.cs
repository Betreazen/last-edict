using LastEdict.Data.Map;
using LastEdict.Data.Sound;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Фон мест (<see cref="AudioRegions"/>): какие объекты звучат и как — ближайшие N не дальше 768 точек, громкость и панорама
/// по формулам движка.</summary>
public class AudioRegionsTests
{
    private const int Size = 48;

    private static MapLooks Looks(params FortLook[] forts)
    {
        var map = new MapState
        {
            Size = Size, Tiles = new Tile[Size, Size], Stacks = new Dictionary<string, MapStack>(), Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(), Objects = new Dictionary<string, MapObject>(),
            Plan = new Dictionary<(int, int), IReadOnlyList<string>>(), Fog = new Dictionary<string, bool[,]>(), Turn = 1,
        };
        return new MapLooks
        {
            Map = map, TreeImage = new byte[Size, Size], TreeGroup = new byte[Size, Size], Forts = forts,
            Sites = [new SiteLook("m0", 20, 20, "MERH", 0), new SiteLook("m1", 30, 30, "MERH", 1), new SiteLook("t", 5, 5, "MAGE", 2)],
            Crystals = [new CrystalLook("g", 10, 12, 0), new CrystalLook("r", 11, 12, 1)],
            Landmarks = [new LandmarkLook("l1", 12, 10, "base:landmark/fall-3x3"), new LandmarkLook("l2", 40, 40, "base:landmark/talder-s-altar")],
            Ruins = [new RuinLook("r1", 30, 4, false, 0), new RuinLook("r2", 33, 4, true, 1)],
        };
    }

    [Fact]
    public void Sources_ByKindRaceAndLandmark()
    {
        var looks = Looks(new FortLook("c", true, 2, 2, "p1", 0, false, ""), new FortLook("v", false, 8, 8, "p2", 1, false, ""),
                          new FortLook("c2", true, 40, 2, "p3", 0, false, ""));
        var sources = AudioRegions.Sources(looks, id => id == "p1" ? 5 : -1, _ => (2, 3));
        // Руины — RUIN, одно имя у всех, и у разграбленной (0x50851a), площадь 3×3.
        Assert.Equal(["CAPEL", "CITY", "CAPDW", "WINDMILL", "MAGICTW", "RUIN", "RUIN", "GOLDMINE", "WATRFALL"], sources.Select(s => s.Name));
        Assert.Equal((30, 4, 3, 3), (sources[5].X, sources[5].Y, sources[5].W, sources[5].H));
        Assert.Equal((5, 5), (sources[0].W, sources[0].H));
        Assert.Equal((2, 3), (sources[^1].W, sources[^1].H));
        Assert.Equal((12 * 64 + 64, 10 * 32 + 48), sources[^1].Centre);
    }

    [Fact]
    public void Nearest_CountReachVolumeAndPan()
    {
        var sources = new[]
        {
            new AudioSource("A", 10, 10, 1, 1), new AudioSource("B", 12, 10, 1, 1), new AudioSource("C", 10, 13, 1, 1),
            new AudioSource("FAR", 30, 10, 1, 1),
        };
        var voices = AudioRegions.Nearest(sources, 10, 10, 2, Size);
        Assert.Equal(["A", "C"], voices.Select(v => v.Source.Name));   // C — 96 точек, B — 128
        Assert.Equal((100, 64), (voices[0].Volume, voices[0].Pan));
        Assert.Equal(100 - (96 * 100 / 769), voices[1].Volume);
        Assert.Equal(64 - (3 * 63 / 12), voices[1].Pan);   // столбец на 3 левее — сдвиг влево
        Assert.DoesNotContain(AudioRegions.Nearest(sources, 10, 10, 9, Size), v => v.Source.Name == "FAR");   // 1280 точек
        Assert.Empty(AudioRegions.Nearest(sources, 10, 10, 0, Size));
    }

    /// <summary>Источник без звука в архиве (руина <c>RUIN</c>: в <c>AudioRgn.wdb</c> только <c>RUIN1..4</c>) молчит и места ближайшего не
    /// занимает (<c>0x509282</c>): звучат два ближайших из тех, чей звук есть (C — 96 точек, B — 128; руина — 71).</summary>
    [Fact]
    public void Nearest_SkipsSourcesWithoutSound()
    {
        var sources = new[]
        {
            new AudioSource(AudioRegions.Ruin, 10, 10, 3, 3), new AudioSource("B", 12, 10, 1, 1), new AudioSource("C", 10, 13, 1, 1),
        };
        var all = new HashSet<string> { "B", "C", "RUIN1", "RUIN2", "RUIN3", "RUIN4" };
        Assert.Equal(["C", "B"], AudioRegions.Nearest(sources, 10, 10, 2, Size, all.Contains).Select(v => v.Source.Name));
        Assert.Equal([AudioRegions.Ruin, "C"], AudioRegions.Nearest(sources, 10, 10, 2, Size).Select(v => v.Source.Name));
    }

    /// <summary>Опись архива <c>AudioRgn</c>: звука <c>RUIN</c> нет, есть <c>RUIN1..4</c>, <c>AIRWARD</c>, <c>GOLDMINE2</c> — их имён в
    /// <c>Discipl2.exe</c> нет (у движка не играют).</summary>
    [SkippableFact]
    public void Archive_HasNoRuinSound()
    {
        var assets = SoundAssets.Load(ImportedData.PathOf("sound"));
        Assert.Null(assets.Find(AudioRegions.Archive, AudioRegions.Ruin));
        foreach (string unused in (string[])["RUIN1", "RUIN2", "RUIN3", "RUIN4", "AIRWARD", "GOLDMINE2"])
            Assert.NotNull(assets.Find(AudioRegions.Archive, unused));
    }
}
