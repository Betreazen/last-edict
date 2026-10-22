using D2.Data.Content;
using D2.Data.Gfx;
using D2.Data.Map;
using D2.Data.Play;
using D2.Rules.Map;

namespace D2.Data.Tests;

/// <summary>Круг цели сценария на карте (<see cref="MapScene"/>, слой <c>SYM_OBJECTIVE</c>; набор — <see cref="GameSession.Objectives"/>
/// сессии В): у каждой цели зрителя — <c>OBJECTIVE{размер следа}</c> с якорем самого объекта, сложением (<c>opacity</c> 300); без набора и
/// у зрителя без целей кругов нет.</summary>
public class MapObjectiveCircleTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private const string Empire = "S135PL0001", Neutral = "S135PL0000";

    [SkippableFact]
    public void Mercantile_EmpireSeesCirclesOnObjectiveVillages()
    {
        string path = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Mercantile");
        Skip.IfNot(File.Exists(path), $"нет {path}");
        var content = SharedContent.Load(Data);
        Skip.If(content.Gfx.Anim("IsoCmon", "OBJECTIVE4") is null && content.Gfx.Image("IsoCmon", "OBJECTIVE4") is null, "нет описи графики");
        var data = ScenarioFile.Read(path);
        var state = new GameState(new GameInfo("Mercantile", "Mercantile", [Empire], 1), data.Looks)
        {
            Events = data.Events, EventProgress = EventProgress.From(data.Events),
        };
        var session = new GameSession(state, content.MapArt, GameBook.Load(Path.Combine(Data, "rules")));
        var ids = session.Objectives(Empire);
        Assert.NotEmpty(ids);

        var scene = MapScene.Build(session.Looks, content.MapArt, content.Gfx, objectives: new MapObjectives(ids, data.Events.Zones));
        var circles = scene.Sprites.Where(s => s.Name.StartsWith("OBJECTIVE", StringComparison.Ordinal)).ToList();
        var forts = session.Looks.Forts.Where(f => ids.Contains(f.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.Equal(ids.Count, circles.Count);
        int layer = content.MapArt.Layer("SYM_OBJECTIVE");
        foreach (var fort in forts)
        {
            int size = fort.Capital ? 5 : 4;
            var (ax, ay, _) = MapScene.Anchor(fort.X, fort.Y, size, size);
            var circle = Assert.Single(circles, c => c.Source == $"цель {fort.Id}");
            Assert.Equal(($"OBJECTIVE{size}", layer, ax, ay), (circle.Name, circle.Layer, circle.AnchorX, circle.AnchorY));
            Assert.Equal(GfxBlend.Add, GfxAssets.BlendOf(content.Gfx.Anim("IsoCmon", circle.Name)?.Opacity
                                                        ?? content.Gfx.Image("IsoCmon", circle.Name)!.Opacity));
        }

        Assert.Empty(session.Objectives(Neutral));
        Assert.DoesNotContain(MapScene.Build(session.Looks, content.MapArt, content.Gfx).Sprites, s => s.Name.StartsWith("OBJECTIVE", StringComparison.Ordinal));
    }
}
