using LastEdict.Data;
using LastEdict.Data.Map;
using LastEdict.Rules.Map;

/// <summary>Блокирующий ландмарк — <c>GLmark.MOUNTAIN</c> (ai-q25, ранг 1, 4/0). Проверка на базе карты q25:
/// <c>Exports/Arrival of the Pilgrims.sg</c>, где Плато <c>S143MM0009</c> — отказ, Лавовая яма
/// <c>S143MM0057</c> — взятие.</summary>
public class LandmarkTests
{
    [SkippableFact] public void MountainLandmarks_FromGLmark_AndTypeFromSg()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var mountains = EconomyLoader.LoadMountainLandmarks(GamePaths.Globals);
        Assert.Contains("base:landmark/cemetery-3x3-4", mountains);          // Кладбище: T, категория 0
        Assert.DoesNotContain("base:landmark/whirlpool", mountains);    // Водоворот: F, категория 3

        var map = MapLoader.Load(Path.Combine(GamePaths.Exports, "Arrival of the Pilgrims.sg"));
        var plateau = map.Objects["S143MM0009"];
        var lava = map.Objects["S143MM0057"];
        Assert.Contains(plateau.TypeId, mountains);
        Assert.DoesNotContain(lava.TypeId, mountains);

        var blocked = LandModel.BlockedCells(map, [], mountains);
        var plateauCells = LandModel.PlanCells(map, plateau.Id);
        Assert.NotEmpty(plateauCells);
        Assert.All(plateauCells, c => Assert.Contains(c, blocked));
        Assert.All(LandModel.PlanCells(map, lava.Id), c => Assert.DoesNotContain(c, blocked));
    }
}
