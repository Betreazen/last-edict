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
                if (map.Reader!.Blocked(x - 1, y - 1, side + 2, side + 2) is null && Land(map, x, y, side)) return (x, y);
        throw new InvalidOperationException("места нет");
    }

    /// <summary>Под квадратом — суша (не вода и не горы).</summary>
    private static bool Land(MapWorkspace map, int x, int y, int side) =>
        Enumerable.Range(y, side).All(yy => Enumerable.Range(x, side).All(xx => ((map.Tiles[(yy * map.Size) + xx] >> 3) & 0b111) is not (3 or 4)));

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
    [Fact]
    public void PlayersAndEvents_ByCommands_KindsFromTheRules_MapStillPlays()
    {
        var (session, map) = Open();
        Assert.Equal(17, MapObjects.ConditionKinds.Count);
        Assert.Equal(24, MapObjects.EffectKinds.Count);
        // Третий игрок — раса углей: игрок, подраса, отношения ко всем; столица ему — палитрой.
        string? ember = null;
        Assert.True(map.Change("игрок", m => ember = m.AddPlayer("test:race/ember", name: "Угли", human: false)));
        Assert.True(map.Change("золото", m => m.SetPlayer(ember!, gold: 1234)));
        var (x, y) = Free(map, 5);
        Assert.True(map.Place(MapObjectKind.Capital, x, y, new MapPlacement { Owner = ember }), session.Refusal);
        Assert.False(map.Change("убрать", m => m.RemovePlayer(ember!)));   // у него столица
        // Событие: каждый ход — всплывающий текст; область и условие «отряд в области».
        string? ev = null, zone = null;
        Assert.True(map.Change("событие", m =>
        {
            ev = m.AddEvent("Приветствие");
            int c = m.AddPart(ev, "Frequency", effect: false);
            m.SetPart(ev, c, false, "Frequency", System.Text.Json.Nodes.JsonValue.Create(1));
            int e = m.AddPart(ev, "Popup", effect: true);
            m.SetPart(ev, e, true, "Text", System.Text.Json.Nodes.JsonValue.Create("Добро пожаловать"));
            zone = m.AddZone(10, 10, 2);
            int z = m.AddPart(ev, "Zone", effect: false);
            m.SetPart(ev, z, false, "LocationId", System.Text.Json.Nodes.JsonValue.Create(zone));
            m.SetEvent(ev, "PlayerIds", new System.Text.Json.Nodes.JsonArray(ember));
        }));
        Assert.False(map.Change("плохое поле", m => m.SetPart(ev!, 0, false, "Nope", null)));
        var row = map.Reader!.Events().Single(r => r.Id == ev);
        Assert.Equal((2, 1), (row.Conditions, row.Effects));
        Assert.Empty(map.Check());
        Assert.True(map.Save("События"));
        var read = ScenarioFile.Read(session.Project.PathOf(Path.Combine("data", "scenarios", "objects.json")));
        Assert.Equal(4, read.Looks.Map.Players.Count);
        Assert.Equal(1234, read.Looks.Map.Players[ember!].Bank.Gold);
        var game = BalanceStand.Start(read, GameBook.Load(session.BaseContent), 1);
        for (int d = 0; d < 2; d++) game.Execute(new LastEdict.Data.Play.Commands.EndTurnCommand());
        Assert.True(game.Map.Turn >= 2);
        // Отмена — игрок и событие уходят вместе со своими правками.
        while (map.Undo()) { }
        Assert.Equal(3, map.Reader!.Players().Count);
        Assert.Empty(map.Reader!.Events());
    }
    [Fact]
    public void ReviewFixes_EventsWorkForAll_FortMovesWithGarrison_TerrainAndReferencesChecked()
    {
        var (session, map) = Open();
        // Событие Студии — для всех рас (шесть флагов да), порядок — после всех.
        string? ev = null;
        Assert.True(map.Change("событие", m => ev = m.AddEvent("Всем")));
        Assert.All(map.Reader!.EventOf(ev!)!["Races"]!.AsArray(), f => Assert.True(f!.GetValue<bool>()));
        // Столица с гарнизоном переносится вместе с ним — план цел.
        var capital = map.Objects.First(o => o.Kind == MapObjectKind.Capital);
        Assert.Contains(map.Reader!.All(), o => o.Kind == MapObjectKind.Stack && map.Reader.StackOf(o.Id)?["Inside"]?.GetValue<string>() == capital.Id);
        var (x, y) = Free(map, 5);
        Assert.True(map.Move(capital.Id, x, y), session.Refusal);
        Assert.Empty(map.Check());
        // Вода — не под объект; на объект, на который ссылается событие, — отказ убрать.
        map.Brush = MapBrush.Water;
        map.Radius = 0;
        var (wx, wy) = Free(map, 1);
        map.StrokeBegin();
        map.Paint(wx, wy);
        map.StrokeEnd();
        Assert.False(map.Place(MapObjectKind.Bag, wx, wy, new MapPlacement()));
        Assert.Contains("вода", session.Refusal, StringComparison.Ordinal);
        string? bag = null;
        (x, y) = Free(map, 1);
        Assert.True(map.Place(MapObjectKind.Bag, x, y, new MapPlacement()));
        bag = map.Selected;
        Assert.True(map.Change("ссылка", m =>
        {
            int c = m.AddPart(ev!, "VisitSite", effect: false);
            m.SetPart(ev!, c, false, "SiteId", System.Text.Json.Nodes.JsonValue.Create(bag));
        }));
        Assert.False(map.Remove(bag!));
        Assert.Contains("событи", session.Refusal, StringComparison.Ordinal);
        // Поле условия — значение того же вида; поле события — тоже.
        Assert.False(map.Change("тип", m => m.SetPart(ev!, 0, false, "SiteId", System.Text.Json.Nodes.JsonValue.Create(5))));
        Assert.False(map.Change("тип", m => m.SetEvent(ev!, "Chance", System.Text.Json.Nodes.JsonValue.Create("много"))));
        Assert.Empty(map.Check());
    }

    [Fact]
    public void AddPlayer_OnAMapWithoutParties_GetsItsOwnSideAndRelationsToAll()
    {
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");
        Directory.CreateDirectory(Path.Combine(session.Project.Dir, "data", "scenarios"));
        File.Copy(Path.Combine(TestPackTests.Dir, "scenarios", "long-march.json"), Path.Combine(session.Project.Dir, "data", "scenarios", "long-march.json"));
        var map = new MapWorkspace(session);
        Assert.True(map.Open("long-march"), session.Refusal);
        int players = map.Reader!.Players().Count;
        string? id = null;
        Assert.True(map.Change("игрок", m => id = m.AddPlayer("test:race/ember")));
        Assert.Empty(map.Check());
        var read = ScenarioFile.FromJson(System.IO.File.ReadAllText(Path.Combine(session.Project.Dir, "data", "scenarios", "long-march.json")));
        Assert.True(map.Save());
        read = ScenarioFile.Read(session.Project.PathOf(Path.Combine("data", "scenarios", "long-march.json")));
        var book = GameBook.Load(session.BaseContent);
        var parties = read.Looks.Map.Players.Values.Select(p => LastEdict.Rules.Map.Diplomacy.PartyOf(p, book.Economy.Races)).ToList();
        Assert.Equal(players + 1, parties.Count);
        Assert.Equal(parties.Count, parties.Distinct().Count());   // новый — своя сторона, ни с кем не слит
        int mine = (int)LastEdict.Rules.Map.Diplomacy.PartyOf(read.Looks.Map.Players[id!], book.Economy.Races)!;
        Assert.Equal(players, read.Looks.Map.Relations.Count(r => r.Race1 == mine || r.Race2 == mine));
        Assert.True(map.Undo());
        Assert.Equal(players, map.Reader!.Players().Count);
    }
}
