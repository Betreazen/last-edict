using LastEdict.Authoring.Maps;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Объекты карты в Студии (Студия 2, задача 5; вехи E2–E3) без окна, на test-pack: палитра целиком (столица, город, руина, лавки,
/// кристалл, мешок, ориентир, отряд), инварианты карты, отмена объектов вместе с мазками, запись и чтение, партия ИИ на карте с объектами.</summary>
public sealed class StudioMapObjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-m-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private (StudioSession Session, MapWorkspace Map) Open()
    {
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");
        var map = new MapWorkspace(session);
        Assert.True(map.New("objects", 64, [new MapSeat("test:race/sun"), new MapSeat("test:race/moon")], seed: 3));
        return (session, map);
    }

    /// <summary>Свободная клетка под квадрат <paramref name="side"/> (с полем в клетку).</summary>
    private static (int X, int Y) Free(MapWorkspace map, int side)
    {
        for (int y = 2; y < map.Size - side - 2; y++)
            for (int x = 2; x < map.Size - side - 2; x++)
                if (map.Reader!.Blocked(x - 1, y - 1, side + 2, side + 2) is null) return (x, y);
        throw new InvalidOperationException("места нет");
    }

    [Fact]
    public void Palette_PlacesEveryKind_MapStaysValid_UndoAndSaveRoundTrip()
    {
        var (session, map) = Open();
        int start = map.Objects.Count;
        Assert.Empty(map.Check());
        string sun = map.Reader!.Players().First(p => p.Race == "test:race/sun").Id;
        var placements = new (MapObjectKind Kind, MapPlacement P)[]
        {
            (MapObjectKind.City, new MapPlacement { Units = ["test:unit/sun-footman"], Name = "Городок" }),
            (MapObjectKind.Ruin, new MapPlacement { Units = ["test:unit/moon-footman", "test:unit/moon-bowman"], Items = ["test:item/gem"], Gold = 300 }),
            (MapObjectKind.Merchant, new MapPlacement { Items = ["test:item/healing-draught", "test:item/iron-charm"] }),
            (MapObjectKind.Mage, new MapPlacement { Spells = ["test:spell/sun-bolt"] }),
            (MapObjectKind.Mercenaries, new MapPlacement { Units = ["test:unit/sun-bowman"] }),
            (MapObjectKind.Trainer, new MapPlacement()),
            (MapObjectKind.Crystal, new MapPlacement { Resource = 2 }),
            (MapObjectKind.Bag, new MapPlacement { Items = ["test:item/gem", "test:item/iron-draught"], Gold = 50 }),
            (MapObjectKind.Landmark, new MapPlacement { Landmark = "test:landmark/stone" }),
            (MapObjectKind.Stack, new MapPlacement { Owner = sun, Units = ["test:unit/sun-captain", "test:unit/sun-footman", "test:unit/sun-bowman"] }),
        };
        string placed = "";
        foreach (var (kind, p) in placements)
        {
            var (x, y) = Free(map, MapObjects.SideOf(kind));
            Assert.True(map.Place(kind, x, y, p), $"{kind}: {session.Refusal}");
            Assert.Equal(kind, map.Objects.Single(o => o.Id == map.Selected).Kind);
            if (kind == MapObjectKind.Stack) placed = map.Selected!;
        }
        Assert.Equal(start + placements.Length, map.Objects.Count);
        Assert.Empty(map.Check());
        // Занятая клетка — отказ, карта прежняя.
        var city = map.Objects.First(o => o.Kind == MapObjectKind.City && o.Title == "Городок");
        Assert.False(map.Place(MapObjectKind.Crystal, city.X + 1, city.Y + 1, new MapPlacement()));
        Assert.Contains("занята", session.Refusal, StringComparison.Ordinal);
        // Сдвиг, свойства, состав отряда — по команде; отмена всего — карта как была, повтор — та же.
        var stack = map.Objects.Single(o => o.Id == placed);
        var (nx, ny) = Free(map, 1);
        Assert.True(map.Move(stack.Id, nx, ny));
        Assert.True(map.SetStack(stack.Id, units: ["test:unit/sun-ranger", "test:unit/sun-healer"], order: 1));
        Assert.Equal(["test:unit/sun-ranger", "test:unit/sun-healer"], map.Reader!.UnitTypes(stack.Id));
        Assert.True(map.SetSite(city.Id, "Name", System.Text.Json.Nodes.JsonValue.Create("Солнечный")));
        map.Brush = MapBrush.Forest;
        map.StrokeBegin();
        map.Paint(30, 30);
        map.StrokeEnd();
        Assert.Empty(map.Check());
        int edits = placements.Length + 4;
        var end = map.Objects;
        for (int i = 0; i < edits; i++) Assert.True(map.Undo());
        Assert.Equal(start, map.Objects.Count);
        for (int i = 0; i < edits; i++) Assert.True(map.Redo());
        Assert.Equal(end, map.Objects);
        // Убрать отряд — с его юнитами.
        Assert.True(map.Remove(stack.Id));
        Assert.Empty(map.Check());
        // Запись и чтение — те же объекты; партия ИИ на карте идёт.
        Assert.True(map.Save("Объекты"));
        var before = map.Objects;
        Assert.True(map.Open("objects"));
        Assert.Equal(before.OrderBy(o => o.Id).ToList(), map.Objects.OrderBy(o => o.Id).ToList());
        var read = ScenarioFile.Read(session.Project.PathOf(Path.Combine("data", "scenarios", "objects.json")));
        var game = BalanceStand.Start(read, GameBook.Load(session.BaseContent), 1);
        for (int d = 0; d < 3; d++) game.Execute(new LastEdict.Data.Play.Commands.EndTurnCommand());
        Assert.True(game.Map.Turn >= 3);
    }

    [Fact]
    public void Capital_NeedsAnOwnerWithoutOne_StackNeedsUnits()
    {
        var (session, map) = Open();
        string sun = map.Reader!.Players().First(p => p.Race == "test:race/sun").Id;
        var (x, y) = Free(map, 5);
        Assert.False(map.Place(MapObjectKind.Capital, x, y, new MapPlacement { Owner = sun }));   // у солнца столица есть
        Assert.Contains("уже есть", session.Refusal, StringComparison.Ordinal);
        Assert.False(map.Place(MapObjectKind.Stack, x, y, new MapPlacement { Owner = sun }));
        Assert.False(map.Place(MapObjectKind.Bag, x, y, new MapPlacement { Items = ["towers:item/none"] }));
        Assert.False(map.CanUndo);   // отказы в историю не идут
    }
}
