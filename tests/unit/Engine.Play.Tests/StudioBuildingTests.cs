using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Tests;

/// <summary>Постройки мода в Студии (Студия 2, задача 1) без окна, на test-pack: форма постройки со значком, правка, переименование и
/// удаление своей — одной командой отмены; картинка в документе (отмена, запись, удаление файла); значок крупнее места — HD.</summary>
public sealed class StudioBuildingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-b-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StudioSession Session() =>
        new(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");

    private const string Lord = "test:lord/sun-lord", Tower = "test:building/sun-tower";

    [Fact]
    public void CreateBuilding_ByForm_WithIcon_JoinsTheRaceLords_InOneUndoStep()
    {
        var session = Session();
        var tree = new TreeWorkspace(session);
        tree.SelectLord(Lord);
        string name = tree.NameFor("Башня мага");
        Assert.Equal("bashnya-maga", name);
        var form = tree.NewForm(Tower) with { Title = "Башня мага", Description = "Учит магов", Cost = new Dictionary<string, long> { ["gold"] = 900 } };
        var parent = (BuildingDef)session.Report.Content!.Find(Tower)!;
        Assert.Equal(parent.Level + 1, form.Level);
        Assert.True(tree.CreateBuilding(name, form, TestPng.Make(77 * 2, 74 * 2)));
        const string id = "towers:building/bashnya-maga";
        var made = (BuildingDef)session.Report.Content!.Find(id)!;
        Assert.Equal((Tower, parent.Level + 1, 900L), (made.Requires, made.Level, made.Cost!["gold"]));
        Assert.Equal("Башня мага", session.NameOf(id));
        Assert.Equal("Учит магов", tree.FormOf(id).Description);
        var race = ((LordDef)session.Report.Content.Find(Lord)!).Race;
        Assert.All(session.Report.Content.Lords.Where(l => l.Race == race), l => Assert.Contains(id, l.Buildings));
        Assert.Contains(tree.Nodes, n => n.Building == id && n.Mine);
        Assert.Equal(0, session.Report.Errors);
        Assert.NotNull(tree.IconOf(id));
        // Одна команда: отмена — ни постройки, ни лордов в слое мода.
        session.Undo();
        Assert.Empty(session.History.Current.Entities);
        session.Redo();
        // Запись: каталог сущности с картинкой; игра видит значок — HD ×2 логического размера места.
        session.Save();
        string dir = Path.Combine(session.Project.ContentDir, "buildings", "bashnya-maga");
        Assert.True(File.Exists(Path.Combine(dir, "icon.png")));
        var icon = GameContent.EntityIcons(ModProject.Open(session.Project.Dir).Info).Image("IconBld", "TOWERS:BUILDING/BASHNYA-MAGA");
        Assert.NotNull(icon);
        Assert.Equal((77, 74, (int?)2), (icon!.W, icon.H, icon.Scale));
    }

    [Fact]
    public void EditRenameRemove_KeepTheModLoadable_AndTheChainWhole()
    {
        var session = Session();
        var tree = new TreeWorkspace(session);
        tree.SelectLord(Lord);
        Assert.True(tree.CreateBuilding("spire", tree.NewForm(Tower) with { Title = "Шпиль" }, TestPng.Make(77, 74)));
        Assert.True(tree.CreateBuilding("top", tree.NewForm("towers:building/spire") with { Title = "Вершина" }));
        // Правка формой: уровень и название; круг — отказ.
        Assert.True(tree.EditBuilding("towers:building/spire", tree.FormOf("towers:building/spire") with { Level = 4, Title = "Высокий шпиль" }));
        Assert.Equal(4, ((BuildingDef)session.Report.Content!.Find("towers:building/spire")!).Level);
        Assert.Equal("Высокий шпиль", session.NameOf("towers:building/spire"));
        Assert.False(tree.EditBuilding(Tower, tree.FormOf(Tower) with { Requires = "towers:building/top" }));
        Assert.Contains("кругу", session.Refusal, StringComparison.Ordinal);
        // Правка чужой постройки формой — поля слоем мода.
        Assert.True(tree.EditBuilding(Tower, tree.FormOf(Tower) with { Cost = new Dictionary<string, long> { ["gold"] = 1 } }));
        Assert.Equal(1L, ((BuildingDef)session.Report.Content!.Find(Tower)!).Cost!["gold"]);
        // Переименование: ссылки мода (требование «Вершины», списки лордов) — на новый id, картинка — с ней.
        Assert.True(tree.RenameBuilding("towers:building/spire", "high-spire"));
        Assert.Null(session.History.Current.Find("towers:building/spire"));
        Assert.Equal("towers:building/high-spire", ((BuildingDef)session.Report.Content!.Find("towers:building/top")!).Requires);
        Assert.Contains("towers:building/high-spire", ((LordDef)session.Report.Content.Find(Lord)!).Buildings);
        Assert.NotNull(tree.IconOf("towers:building/high-spire"));
        Assert.Equal(0, session.Report.Errors);
        // Удаление: «Вершина» требует теперь требуемую удалённой (башню), лорды — без неё; мод грузится.
        Assert.True(tree.RemoveBuilding("towers:building/high-spire"));
        Assert.Equal(Tower, ((BuildingDef)session.Report.Content!.Find("towers:building/top")!).Requires);
        Assert.DoesNotContain("towers:building/high-spire", ((LordDef)session.Report.Content.Find(Lord)!).Buildings);
        Assert.Equal(0, session.Report.Errors);
        Assert.False(tree.RemoveBuilding(Tower));   // чужую — нельзя
        // Удаление последней своей — правки лордов, ставшие как у базы, уходят из слоя.
        Assert.True(tree.RemoveBuilding("towers:building/top"));
        Assert.DoesNotContain(session.History.Current.Entities, e => e.Kind == "lord");
        session.Save();
        Assert.False(Directory.Exists(Path.Combine(session.Project.ContentDir, "buildings", "spire")));
        Assert.False(Directory.Exists(Path.Combine(session.Project.ContentDir, "buildings", "high-spire")));
    }

    [Fact]
    public void PicturesAndRenames_UndoAll_GivesTheStartBack_ReplayGivesTheResult()
    {
        var project = ModProject.Create(Path.Combine(_root, "towers"), "towers");
        var start = ModDocument.Load(project);
        var history = new EditHistory(start);
        var random = new Random(11);
        var ids = new List<string>();
        var log = new JsonArray();
        void Do(IEditCommand c)
        {
            history.Do(c);
            log.Add(c.ToJson());
        }
        for (int i = 0; i < 200; i++)
        {
            int roll = random.Next(6);
            if (roll == 0 || ids.Count == 0)
            {
                Do(new AddEntity("building", $"b{i}", new JsonObject { ["level"] = 1, ["category"] = "guild" }));
                ids.Add($"towers:building/b{i}");
            }
            else if (roll == 1)
            {
                int at = random.Next(ids.Count);
                Do(new RenameEntity(ids[at], $"r{i}"));
                ids[at] = $"towers:building/r{i}";
            }
            else if (roll == 2) Do(new SetFile(ids[random.Next(ids.Count)], "icon.png", random.Next(4) == 0 ? null : TestPng.Make(77, 74, (byte)i)));
            else if (roll == 3 && ids.Count > 1) Do(new SetField(ids[random.Next(ids.Count)], "requires", JsonValue.Create(ids[random.Next(ids.Count)])));
            else Do(new SetText(ids[random.Next(ids.Count)], "ru", "name", $"имя {i}"));
        }
        var end = history.Current;
        while (history.Undo()) { }
        Assert.Equal(start.Fingerprint(), history.Current.Fingerprint());
        while (history.Redo()) { }
        Assert.Equal(end.Fingerprint(), history.Current.Fingerprint());
        // Журнал (картинки — base64) повторяет те же правки.
        var replay = new EditHistory(start);
        foreach (var c in EditCommands.ParseScript(log.ToJsonString())) replay.Do(c);
        Assert.Equal(end.Fingerprint(), replay.Current.Fingerprint());
        // Запись и чтение с диска — тот же документ (картинки байт в байт).
        end.Save(project);
        Assert.Equal(end.Fingerprint(), ModDocument.Load(project).Fingerprint());
    }

    [Fact]
    public void SetFile_RefusesNotPng_UnknownSlots_AndBaseEntities()
    {
        var doc = new AddEntity("building", "spire", new JsonObject { ["level"] = 1 }).Apply(ModDocument.Empty("towers"));
        Assert.Throws<InvalidOperationException>(() => new SetFile("towers:building/spire", "icon.png", ImmutableArray.Create<byte>(1, 2, 3)).Apply(doc));
        Assert.Throws<InvalidOperationException>(() => new SetFile("towers:building/spire", "face.png", TestPng.Make(70, 85)).Apply(doc));
        Assert.Throws<InvalidOperationException>(() => new SetFile(Tower, "icon.png", TestPng.Make(77, 74)).Apply(doc));
        Assert.Throws<InvalidOperationException>(() => new RenameEntity("towers:building/spire", "Шпиль").Apply(doc));
        Assert.Equal((154, 148, 2), EntityArt.Fit(EntityArt.Find("building", "icon.png")!, 200, 160));
        Assert.Equal((77, 74, 1), EntityArt.Fit(EntityArt.Find("building", "icon.png")!, 30, 30));
        Assert.Null(EntityArt.ScaleOf(EntityArt.Find("building", "icon.png")!, 100, 74));
    }
}

/// <summary>Настоящий PNG заданного размера (RGBA, сплошная заливка) — картинки модов в тестах без графики игры.</summary>
internal static class TestPng
{
    public static ImmutableArray<byte> Make(int w, int h, byte shade = 128)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        (ihdr[8], ihdr[9]) = (8, 6);
        Chunk(png, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (w * 4)];
            for (int x = 0; x < w; x++) (row[1 + (x * 4)], row[2 + (x * 4)], row[3 + (x * 4)], row[4 + (x * 4)]) = (shade, 64, 32, 255);
            for (int y = 0; y < h; y++) z.Write(row);
        }
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return [.. png.ToArray()];
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        byte[] body = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        s.Write(body);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, Crc(body));
        s.Write(len);
    }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}
