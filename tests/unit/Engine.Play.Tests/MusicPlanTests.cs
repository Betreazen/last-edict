using D2.Data.Sound;

namespace D2.Data.Tests;

/// <summary>Выбор музыки (<see cref="MusicPlan"/>, <see cref="MusicRotation"/>; разбор <c>CMidMusic</c>): все дорожки плана есть в описи
/// <c>Music</c>; карта чередует списки и не повторяет дорожку; столица — 1→2→3 по кругу; бой — по кругу со случайной.</summary>
public class MusicPlanTests
{
    private static string Sound => ImportedData.PathOf("sound");

    [SkippableFact]
    public void Plan_TracksExistInManifest()
    {
        var plan = MusicPlan.Load(Path.Combine(AppContext.BaseDirectory, "data", "music", "playlists.json"));
        var sounds = SoundAssets.Load(Sound);
        var all = plan.Menu.Concat(plan.Briefing).Concat(plan.Credits).Concat(plan.Battle).Concat(plan.Map.SelectMany(l => l))
                      .Concat(plan.Capital.Values.SelectMany(l => l)).ToList();
        Assert.Equal(4 + 22 + 15 + 3, all.Count);
        Assert.All(all, t => Assert.NotNull(sounds.Find("Music", t)));
    }

    [Fact]
    public void Rotation_MapAlternates_CapitalCycles_BattleCycles()
    {
        var plan = MusicPlan.Load(Path.Combine(AppContext.BaseDirectory, "data", "music", "playlists.json"));
        var rotation = new MusicRotation(plan, new Random(5));
        var map = Enumerable.Range(0, 8).Select(_ => rotation.NextMap()!).ToList();
        for (int i = 1; i < map.Count; i++)
        {
            Assert.NotEqual(map[i - 1].StartsWith("AMB", StringComparison.Ordinal), map[i].StartsWith("AMB", StringComparison.Ordinal));
            Assert.NotEqual(map[i - 1], map[i]);
        }
        Assert.Equal(["DWRFTRK1", "DWRFTRK2", "HUMNTRK3", "HUMNTRK1"],
                     new[] { rotation.NextCapital(3)!, rotation.NextCapital(3)!, rotation.NextCapital(0)!, rotation.NextCapital(0)! });
        Assert.Null(rotation.NextCapital(4));   // нейтралы — темы нет
        string first = rotation.NextBattle()!;
        int at = plan.Battle.ToList().IndexOf(first);
        Assert.Equal(plan.Battle[(at + 1) % 4], rotation.NextBattle());
    }
}
