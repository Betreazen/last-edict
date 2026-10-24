using LastEdict.Authoring.Maps;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Рабочие пространства Студии (Э6) без окна, на test-pack: дерево построек (связь, круг, перенос, новая постройка), содержимое по
/// схеме, баланс (арена и стенд «база против мода»), карта без потолка площади (новая, мазок, отмена, запись в мод).</summary>
public sealed class StudioWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StudioSession Session() =>
        new(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");

    [Fact]
    public void Tree_ConnectRefusesCycles_PlaceAndAddGoThroughUndo()
    {
        var session = Session();
        var tree = new TreeWorkspace(session);
        tree.SelectLord("test:lord/sun-lord");
        Assert.Equal(5, tree.Nodes.Count);
        Assert.Empty(tree.Links);
        Assert.True(tree.AddBuilding("spire", "Шпиль", "test:building/sun-tower"));
        Assert.Equal(6, tree.Nodes.Count);
        Assert.Single(tree.Links);
        Assert.Equal("Шпиль", tree.Nodes.First(n => n.Building == "towers:building/spire").Name);   // имя из несохранённого документа
        Assert.False(tree.Connect("towers:building/spire", "test:building/sun-tower"));   // круг
        Assert.Contains("кругу", session.Refusal, StringComparison.Ordinal);
        Assert.True(tree.Connect("test:building/sun-guild", "test:building/sun-temple"));
        Assert.Equal(2, tree.Links.Count);
        Assert.True(tree.Place("towers:building/spire", 3, 2));
        var spire = tree.Tree!.BranchOf("towers:building/spire")!.Node("towers:building/spire")!;
        Assert.Equal((3.0, 2), (spire.Column, spire.Row));
        session.Undo();
        Assert.NotEqual((3.0, 2), (tree.Tree!.BranchOf("towers:building/spire")!.Node("towers:building/spire")!.Column,
                                   tree.Tree.BranchOf("towers:building/spire")!.Node("towers:building/spire")!.Row));
        session.Redo();
        // Перенос мышью на живой странице — то же место.
        tree.Preview.Resize(600, 500);
        tree.Preview.Open(tree.Tree!.BranchOf("towers:building/spire")!.Id);
        var from = tree.Preview.PointOf("towers:building/spire")!.Value;
        var to = tree.Preview.PointOfCell(1, 1)!.Value;
        tree.Preview.Press(from.X, from.Y);
        tree.Preview.Motion(to.X, to.Y, held: true);
        var drop = tree.Preview.Release(to.X, to.Y);
        Assert.Equal(new BuildTreeDrop("towers:building/spire", 1, 1), drop);
        Assert.True(session.Save() > 0);
        Assert.Equal(0, session.Report.Errors);
    }

    [Fact]
    public void Content_FieldsBySchema_SetParsesByType_ResetGoesBackToBase()
    {
        var session = Session();
        var content = new ContentWorkspace(session);
        content.Open("unit");
        Assert.NotEmpty(content.Entities());
        string unit = content.Entities()[0].Id;
        content.Select(unit);
        var hp = content.Fields().First(f => f.Name == "hitPoints");
        Assert.Equal("integer", hp.Type);
        Assert.Equal("Опыт до следующего уровня.", content.Fields().First(f => f.Name == "xpForNextLevel").Description);   // описания — из схемы
        Assert.False(content.Set("hitPoints", "много"));
        Assert.True(content.Set("hitPoints", "999"));
        Assert.Equal(999, ((UnitDef)session.Report.Content!.Find(unit)!).HitPoints);
        Assert.Equal(EntityOrigin.Patched, content.Entities().First(e => e.Id == unit).Origin);
        Assert.True(content.Reset("hitPoints"));
        Assert.Equal(EntityOrigin.Base, content.Entities().First(e => e.Id == unit).Origin);
        var role = content.Fields().First(f => f.Name == "role");
        Assert.NotEmpty(role.Enum);   // перечень — выбором
        Assert.Equal("race", content.Fields().First(f => f.Name == "race").Ref);   // ссылка — id нужного вида
        Assert.True(content.Add("hero"));   // копия выбранной — новая сущность мода каталогом
        Assert.Equal(EntityOrigin.Mine, content.Entities().First(e => e.Id == "towers:unit/hero").Origin);
    }

    [Fact]
    public void Balance_ArenaAndStand_CompareBaseWithTheMod()
    {
        var session = Session();
        var balance = new BalanceWorkspace(session, Path.Combine(TestPackTests.Dir, "scenarios"));
        Assert.Contains("twin-fields", balance.Scenarios);
        var same = balance.RunArena("twin-fields", pairs: 3, seeds: 4);
        Assert.NotEmpty(same);
        Assert.All(same, r => Assert.Equal((r.BaseWins, r.BaseTurns), (r.ModWins, r.ModTurns)));   // мод ничего не меняет — то же
        // Удары нейтралов впятеро сильнее — арена (бои нейтральных отрядов карты) видит разницу.
        var content = new ContentWorkspace(session);
        content.Open("attack");
        foreach (var wild in content.Entities().Where(e => e.Id.Contains("wild", StringComparison.Ordinal)).ToList())
        {
            content.Select(wild.Id);
            if (((AttackDef)session.Report.Content!.Find(wild.Id)!).Damage is not { } damage) continue;
            content.Set("damage", (damage * 5).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var changed = balance.RunArena("twin-fields", pairs: 3, seeds: 4);
        Assert.Contains(changed, r => r.BaseWins != r.ModWins || Math.Abs(r.BaseTurns - r.ModTurns) > 1e-9);
        var stand = balance.RunStand("twin-fields", games: 2, days: 3);
        Assert.NotEmpty(stand);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(300)]
    public void Map_NewOfAnySize_StrokeUndo_SaveIntoTheMod(int size)
    {
        var session = Session();
        var map = new MapWorkspace(session);
        map.New("big", size, [new MapSeat("test:race/sun"), new MapSeat("test:race/moon")]);
        Assert.Equal(size * size, map.Tiles.Length);
        byte[] before = [.. map.Tiles];
        map.Brush = MapBrush.Water;
        map.Radius = 2;
        map.StrokeBegin();
        for (int x = 10; x < 20; x++) map.Paint(x, 12);
        map.StrokeEnd();
        Assert.True(map.Changed > 0);
        map.Undo();
        Assert.Equal(before, map.Tiles);
        map.Redo();
        Assert.Equal(MapBrush.Water, (MapBrush)((map.Tiles[(12 * size) + 15] >> 3) & 0b111));
        map.Save("Большая");
        Assert.Contains("big", map.Maps);
        var read = ScenarioFile.Read(session.Project.PathOf(Path.Combine("data", "scenarios", "big.json")));
        Assert.Equal(size, read.Looks.Map.Size);
        Assert.Equal(Ground.Water, read.Looks.Map.Tiles[15, 12].Ground);
        Assert.Equal(3, read.Looks.Map.Players.Count);   // нейтралы и двое
        // Карта играется: партия ИИ на ней идёт дни без исключений.
        var game = BalanceStand.Start(read, GameBook.Load(session.BaseContent), 1);
        for (int d = 0; d < 2; d++) game.Execute(new LastEdict.Data.Play.Commands.EndTurnCommand());
        Assert.True(game.Map.Turn >= 2);
    }

    [Fact]
    public void Map_RefusesBadIdsAndTooSmallMaps_KeepsTheOpenMap()
    {
        var session = Session();
        var map = new MapWorkspace(session);
        Assert.False(map.New("../x", 64, [new MapSeat("test:race/sun")]));
        Assert.False(map.New("tiny", 24, [.. Enumerable.Range(0, 6).Select(_ => new MapSeat("test:race/sun"))]));   // столицы налезли бы
        Assert.Contains("мала", session.Refusal, StringComparison.Ordinal);
        Assert.True(map.New("ok", 64, [new MapSeat("test:race/sun"), new MapSeat("test:race/moon")]));
        Assert.True(map.Save());
        Assert.False(map.New("ok", 64, [new MapSeat("test:race/sun")]));   // занятый id
        Assert.False(map.Open("none"));
        Assert.Equal("ok", map.Id);   // прежняя карта на месте
        File.WriteAllText(session.Project.PathOf(Path.Combine("data", "game", "scenarios.json")), "{}");
        Assert.False(map.Save());   // каталог не список — отказ до записи
    }

    [Fact]
    public void Session_IgnoresItsOwnSave_AndKeepsUnsavedEditsOnOutsideChange()
    {
        var session = Session();
        session.Do(new LastEdict.Authoring.Commands.SetField("test:building/sun-guild", "cost.gold", 700));
        session.Save();
        Assert.False(session.ReloadFromDisk());   // своя запись
        session.Do(new LastEdict.Authoring.Commands.SetField("test:building/sun-guild", "cost.gold", 800));
        File.WriteAllText(session.Project.PathOf(Path.Combine("content", "buildings.json")),
            """{ "buildings": [ { "id": "test:building/sun-guild", "cost": { "gold": 900 } } ] }""");
        Assert.False(session.ReloadFromDisk());   // в окне несохранённое — не перечитывает
        Assert.Contains("несохранённые", session.Refusal, StringComparison.Ordinal);
        Assert.True(session.ReloadFromDisk(force: true));
        Assert.Equal(900, session.Report.Content!.Buildings.First(b => b.Id == "test:building/sun-guild").Cost!["gold"]);
    }
}
