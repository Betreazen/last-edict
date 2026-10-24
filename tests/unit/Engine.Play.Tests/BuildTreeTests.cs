using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Tests;

/// <summary>Дерево окна построек данными (Э6): ветки — содержимое, ветка постройки — по категории, ветке юнитов, требуемой; автораскладка по
/// уровню и требованию; место из мода главнее; круг требований не вешает раскладку. Вид с зумом — точка под курсором на месте, рамка.</summary>
public class BuildTreeTests
{
    [Fact]
    public void TestPack_BranchesFromContent_BuildingsByCategoryAndUnitBranch()
    {
        var content = ContentSource.Open(TestPackTests.Dir);
        var tree = BuildTree.OfLord(content, "test:lord/sun-lord");
        Assert.Equal(["test:building-branch/fighters", "test:building-branch/archers", "test:building-branch/other"],
                     tree.Branches.Select(b => b.Id));
        Assert.Equal(["test:building/sun-barracks"], tree.Branches[0].Nodes.Select(n => n.Building));
        Assert.Equal(["test:building/sun-range"], tree.Branches[1].Nodes.Select(n => n.Building));
        Assert.Equal(3, tree.Branches[2].Nodes.Count);   // гильдия, храм, башня — по категориям
        Assert.All(tree.Branches[2].Nodes, n => Assert.Equal(0, n.Row));
        Assert.Equal([0.0, 1.0, 2.0], tree.Branches[2].Nodes.Select(n => n.Column));
    }

    [Fact]
    public void Layout_RowsByLevel_ParentOverTheMiddleOfChildren_ModPlaceWins()
    {
        var content = Content(
            B("a", 2), B("b", 3, "a"), B("c", 4, "b"), B("d", 4, "b"), B("e", 3, "a"),
            B("side", 3, "a", kind: BranchKind.Sideshow),   // своей ветки нет — в ветке требуемой
            B("pinned", 2, tree: new BuildingTreePlace { Column = 7, Row = 3 }),
            B("guild", 1, category: BuildingCategory.Guild));
        var tree = BuildTree.Of(content, Mine(content));
        var fighters = tree.Branch("m:building-branch/fighters")!;
        BuildTreeNode N(string id) => fighters.Node($"m:building/{id}")!;
        Assert.Equal((0, 1, 2, 2, 1, 1), (N("a").Row, N("b").Row, N("c").Row, N("d").Row, N("e").Row, N("side").Row));
        Assert.Equal((0.0, 1.0), (N("c").Column, N("d").Column));
        Assert.Equal(0.5, N("b").Column);   // над серединой детей
        Assert.Equal(2.0, N("e").Column);
        Assert.Equal(3.0, N("side").Column);
        Assert.Equal(1.75, N("a").Column);   // над серединой первого и последнего ребёнка
        Assert.Equal("m:building/b", N("c").Parent);
        Assert.Equal((7.0, 3), (N("pinned").Column, N("pinned").Row));   // место мода главнее
        Assert.Equal(["m:building/guild"], tree.Branch("m:building-branch/other")!.Nodes.Select(n => n.Building));
        Assert.Equal(8.0, fighters.Columns);
        var (w, h) = BuildTreeGeometry.Content(fighters);
        Assert.True(w >= 8 * BuildTreeGeometry.CellWidth && h > 3 * BuildTreeGeometry.CellHeight);
        var icon = BuildTreeGeometry.Icon(N("c"));
        Assert.Equal("m:building/c", BuildTreeGeometry.At(fighters, icon.X + 1, icon.Y + 1)?.Building);
        Assert.Null(BuildTreeGeometry.At(fighters, icon.X + 1, icon.Y + 1, _ => false));   // скрытая не попадается
    }

    [Fact]
    public void Layout_RequirementCycle_BreaksIntoARoot()
    {
        var content = Content(B("x", 2, "y"), B("y", 3, "x"));
        var branch = BuildTree.Of(content, Mine(content)).Branch("m:building-branch/fighters")!;
        Assert.Equal(2, branch.Nodes.Count);
        Assert.Single(branch.Nodes, n => n.Parent is null);
    }

    [Fact]
    public void ModLayer_AddsABranchAndABuildingWithoutCode()
    {
        string root = Path.Combine(Path.GetTempPath(), "le-tree-" + Guid.NewGuid().ToString("N"));
        try
        {
            string mod = Path.Combine(root, "towers", "content");
            Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(root, "towers", "mod.json"), """{ "id": "towers", "name": "Towers" }""");
            File.WriteAllText(Path.Combine(mod, "building-branches.json"),
                """{ "buildingBranches": [ { "id": "towers:building-branch/towers", "categories": ["magic"] } ] }""");
            File.WriteAllText(Path.Combine(mod, "buildings.json"), """
                { "buildings": [
                  { "id": "towers:building/spire", "level": 2, "category": "magic", "requires": "test:building/sun-tower",
                    "cost": { "gold": 500 }, "tree": { "branch": "towers:building-branch/towers" } } ] }
                """);
            File.WriteAllText(Path.Combine(mod, "lords.json"),
                """{ "lords": [ { "id": "test:lord/sun-lord", "buildings": ["test:building/sun-barracks", "test:building/sun-range", "test:building/sun-guild", "test:building/sun-temple", "test:building/sun-tower", "towers:building/spire"] } ] }""");
            var content = ContentLayers.Load(TestPackTests.Dir, [new ModInfo("towers", "Towers", Path.Combine(root, "towers"))]);
            var tree = BuildTree.OfLord(content, "test:lord/sun-lord");
            Assert.Equal(4, tree.Branches.Count);
            var towers = tree.Branch("towers:building-branch/towers")!;
            Assert.Equal(["towers:building/spire"], towers.Nodes.Select(n => n.Building));
            Assert.Null(towers.Nodes[0].Parent);   // требуемая — в другой ветке: здесь корень
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ZoomView_KeepsThePointUnderTheCursor_AndStaysInsideTheContent()
    {
        var view = new ZoomView();
        view.Reset(400, 300, 1000, 600);
        Assert.Equal(0.5, view.Zoom, 6);   // целиком не входит, но не мельче половины
        Assert.Equal(-50, view.X, 6);   // шире вида и при наименьшем масштабе — по середине
        view.Reset(400, 300, 300, 200);
        Assert.Equal(1, view.Zoom, 6);   // меньше вида — масштаб 1, по середине
        Assert.Equal((50.0, 50.0), (view.X, view.Y));
        view.Reset(400, 300, 1200, 900);
        var (cx, cy) = view.ToContent(123, 77);
        Assert.True(view.ZoomAt(123, 77, 2));
        var (vx, vy) = view.ToView(cx, cy);
        Assert.Equal((123.0, 77.0), (Math.Round(vx, 6), Math.Round(vy, 6)));
        Assert.False(view.ZoomAt(0, 0, 100) && view.Zoom > ZoomView.MaxZoom);
        Assert.Equal(ZoomView.MaxZoom, view.Zoom, 6);
        view.Pan(1e6, 1e6);
        Assert.Equal((0.0, 0.0), (view.X, view.Y));
        view.Pan(-1e6, -1e6);
        Assert.Equal((400 - (1200 * view.Zoom), 300 - (900 * view.Zoom)), (view.X, view.Y));
        view.Reveal(new UiRect(0, 0, 10, 10));
        var (lx, ly) = view.ToView(0, 0);
        Assert.True(lx >= 0 && ly >= 0);
        view.Resize(800, 600);
        Assert.InRange(view.X, 800 - (1200 * view.Zoom), 0);
    }

    private static BuildingDef B(string name, int level, string? requires = null, BuildingCategory category = BuildingCategory.Unit,
                                 BranchKind? kind = BranchKind.Fighter, BuildingTreePlace? tree = null) =>
        new()
        {
            Id = $"m:building/{name}", Level = level, Requires = requires is null ? null : $"m:building/{requires}", Category = category,
            Branch = category == BuildingCategory.Unit ? kind : null, Tree = tree,
        };

    private static IEnumerable<string> Mine(ContentSet content) => content.Buildings.Where(b => b.Id.StartsWith("m:", StringComparison.Ordinal)).Select(b => b.Id);

    /// <summary>Содержимое test-pack с постройками и ветками теста вместо своих.</summary>
    private static ContentSet Content(params BuildingDef[] buildings)
    {
        var pack = ContentSource.Open(TestPackTests.Dir);
        var byKind = ContentSet.Kinds.ToDictionary(k => k.Type, k => pack.Of(k.Type));
        byKind[typeof(BuildingDef)] = [.. pack.Buildings, .. buildings];
        byKind[typeof(BuildingBranchDef)] =
        [
            new BuildingBranchDef { Id = "m:building-branch/fighters", Units = [BranchKind.Fighter] },
            new BuildingBranchDef { Id = "m:building-branch/other", Categories = [BuildingCategory.Guild] },
        ];
        return new ContentSet(byKind);
    }
}
