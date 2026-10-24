using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Validation;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Tests;

/// <summary>Ядро Студии (Э6): мод — каталог, «папка = сущность» с автоматическими id строк, команды правки с отменой, проверка поверх базы,
/// плагины «исполняют код». База — content/test-pack (без данных игры).</summary>
public sealed class AuthoringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-authoring-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ModProject NewMod(string id = "towers") => ModProject.Create(Path.Combine(_root, id), id, "Towers");

    [Fact]
    public void FolderEntity_LoadsAsLayer_WithAutomaticStringIds()
    {
        var project = NewMod();
        var history = new EditHistory(ModDocument.Load(project));
        history.Do(new AddEntity("building", "spire", new JsonObject
        {
            ["level"] = 2, ["category"] = "magic", ["requires"] = "test:building/sun-tower", ["cost"] = new JsonObject { ["gold"] = 500 },
        }));
        history.Do(new SetText("towers:building/spire", "ru", "name", "Шпиль"));
        history.Do(new SetText("towers:building/spire", "en", "name", "Spire"));
        history.Do(new SetField("test:lord/sun-lord", "buildings", new JsonArray("test:building/sun-tower", "towers:building/spire")));
        var (written, _) = history.Current.Save(project);
        Assert.Equal(4, written);   // entity.json, strings.ru, strings.en, lords.json
        Assert.True(File.Exists(Path.Combine(project.Dir, "content", "buildings", "spire", "entity.json")));

        var content = ContentLayers.Load(TestPackTests.Dir, [project.Info]);
        var spire = Assert.IsType<BuildingDef>(content.Find("towers:building/spire"));
        Assert.Equal("towers:building/spire#name", spire.NameText);   // автоматический ключ строки
        Assert.Null(spire.DescriptionText);   // строки описания нет — поля нет
        Assert.Equal("Шпиль", EntityFolders.Strings(project.Info, "ru")["towers:building/spire#name"]);
        Assert.Equal("Spire", EntityFolders.Strings(project.Info, "de")["towers:building/spire#name"]);   // нет языка — английский
        var tree = BuildTree.OfLord(content, "test:lord/sun-lord");
        Assert.Equal("test:building/sun-tower", tree.BranchOf("towers:building/spire")!.Node("towers:building/spire")!.Parent);

        // Чтение мода с диска — тот же документ.
        Assert.Equal(history.Current.Fingerprint(), ModDocument.Load(ModProject.Open(project.Dir)).Fingerprint());
    }

    [Fact]
    public void Commands_UndoAll_GivesTheStartBack_RedoAll_GivesTheResult()
    {
        var project = NewMod();
        var start = ModDocument.Load(project);
        var history = new EditHistory(start);
        var random = new Random(7);
        string[] fields = ["hitPoints", "xpKilled", "level", "tree.column", "tree.row"];
        var ids = new List<string>();
        for (int i = 0; i < 200; i++)
        {
            int roll = random.Next(10);
            if (roll == 0 || ids.Count == 0)
            {
                string name = $"b{i}";
                history.Do(new AddEntity("building", name, new JsonObject { ["level"] = 1 + random.Next(4), ["category"] = "guild" }, Folder: random.Next(2) == 0));
                ids.Add($"towers:building/{name}");
            }
            else if (roll == 1 && ids.Count > 1)
            {
                string id = ids[random.Next(ids.Count)];
                history.Do(new RemoveEntity(id));
                ids.Remove(id);
            }
            else history.Do(new SetField(ids[random.Next(ids.Count)], fields[random.Next(fields.Length)], random.Next(100)));
        }
        var end = history.Current;
        while (history.Undo()) { }
        Assert.Equal(start.Fingerprint(), history.Current.Fingerprint());
        while (history.Redo()) { }
        Assert.Equal(end.Fingerprint(), history.Current.Fingerprint());
        Assert.True(history.Dirty);
        // Сценарий команд из журнала (d2mod apply) — те же правки.
        var replay = new EditHistory(start);
        foreach (var c in EditCommands.ParseScript(new JsonArray([.. ReplayLog(start, end)]).ToJsonString())) replay.Do(c);
        Assert.Equal(end.Fingerprint(), replay.Current.Fingerprint());
    }

    /// <summary>Журнал «с нуля»: по команде добавления на сущность итога (команды сериализуются и читаются обратно).</summary>
    private static IEnumerable<JsonNode> ReplayLog(ModDocument start, ModDocument end)
    {
        _ = start;
        foreach (var e in end.Entities)
        {
            var body = (JsonObject)e.Node.DeepClone();
            body.Remove("id");
            yield return new AddEntity(e.Kind, e.Name, body, e.Source == EntitySource.Folder).ToJson();
        }
    }

    [Fact]
    public void PatchOfABaseEntity_GoesIntoTheModLayer_AndLeavesWhenEmpty()
    {
        var project = NewMod();
        var history = new EditHistory(ModDocument.Load(project));
        history.Do(new SetField("test:building/sun-guild", "cost.gold", 777));
        var patch = history.Current.Find("test:building/sun-guild");
        Assert.Equal(EntitySource.File, patch!.Source);
        history.Current.Save(project);
        var content = ContentLayers.Load(TestPackTests.Dir, [project.Info]);
        Assert.Equal(777, content.Buildings.First(b => b.Id == "test:building/sun-guild").Cost!["gold"]);
        history.Do(new SetField("test:building/sun-guild", "cost", null, Unset: true));
        Assert.Null(history.Current.Find("test:building/sun-guild"));   // правки не осталось — из слоя ушла
        var (_, deleted) = history.Current.Save(project);
        Assert.Equal(1, deleted);   // buildings.json мода больше не нужен
    }

    [Fact]
    public void Project_WritesOnlyInsideTheMod_AndChecksTheId()
    {
        var project = NewMod();
        Assert.Throws<UnauthorizedAccessException>(() => project.PathOf("../outside.json"));
        Assert.Throws<UnauthorizedAccessException>(() => project.Write(Path.Combine(_root, "x.json"), "{}"));
        Assert.Throws<ArgumentException>(() => ModProject.Create(Path.Combine(_root, "bad"), "Bad Id"));
        Assert.Throws<InvalidOperationException>(() => new AddEntity("building", "Шпиль", []).Apply(ModDocument.Load(project)));
    }

    [Fact]
    public void Check_FindsCycles_MissingLords_AndMarksCode()
    {
        var project = NewMod();
        var history = new EditHistory(ModDocument.Load(project));
        history.Do(new AddEntity("building", "a", new JsonObject { ["level"] = 1, ["category"] = "guild", ["requires"] = "towers:building/b" }));
        history.Do(new AddEntity("building", "b", new JsonObject { ["level"] = 1, ["category"] = "guild", ["requires"] = "towers:building/a" }));
        var report = ModCheck.Run(history.Current, project, TestPackTests.Dir);
        Assert.Single(report.Items, i => i.Rule == "MOD010");   // один круг — одна находка
        Assert.Equal(2, report.Items.Count(i => i.Rule == "MOD011"));   // обе — не у лорда
        Assert.Equal(1, report.Errors);

        // Слой, который игра не загрузит (ссылка в пустоту), — MOD001 с текстом загрузки.
        history.Do(new SetField("towers:building/a", "requires", "towers:building/none"));
        Assert.Contains(ModCheck.Run(history.Current, project, TestPackTests.Dir).Items, i => i.Rule == "MOD001");

        Directory.CreateDirectory(Path.Combine(project.Dir, "plugins"));
        File.WriteAllBytes(Path.Combine(project.Dir, "plugins", "Towers.dll"), [0]);
        var withCode = ModProject.Open(project.Dir);
        Assert.True(withCode.ExecutesCode);
        Assert.Contains(ModCheck.Run(ModDocument.Empty("towers"), withCode, TestPackTests.Dir).Items, i => i.Rule == "MOD090");
        Assert.Contains("не сборка .NET", ModPlugins.Load([withCode.Info]).Single().Text, StringComparison.Ordinal);   // пропуск, не обрыв
    }

    [Fact]
    public void Watcher_GivesABatchAfterQuiet_AndClassifiesByPlace()
    {
        var project = NewMod();
        var now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        using var watcher = new ContentWatcher([project.Dir], TimeSpan.FromMilliseconds(400), () => now);
        watcher.Note(Path.Combine(project.Dir, "content", "buildings", "spire", "entity.json"));
        watcher.Note(Path.Combine(project.Dir, "data", "ui", "strings.ru.json"));
        watcher.Note(Path.Combine(project.Dir, "content", "buildings.json.tmp"));   // временный файл записи — не в счёт
        Assert.Null(watcher.Take());   // ещё пишут
        now = now.AddMilliseconds(500);
        var batch = watcher.Take()!;
        Assert.Equal(2, batch.Files.Count);
        Assert.Equal(ContentChange.Rules | ContentChange.Screens, batch.Changes);
        Assert.Equal(1, batch.Revision);
        Assert.Null(watcher.Take());
        Assert.Equal(ContentChange.Art, watcher.Classify(Path.Combine(project.Dir, "data", "gfx", "IconBld.json")));
        Assert.Equal(ContentChange.Code, watcher.Classify(Path.Combine(project.Dir, "plugins", "x.dll")));
    }

    [Fact]
    public void Plugin_RegistersItsTypes()
    {
        TestPlugin.Calls = 0;
        int types = ModPlugins.Register(typeof(TestPlugin).Assembly, new ModInfo("t", "t", _root));
        Assert.Equal(1, types);
        Assert.Equal(1, TestPlugin.Calls);
    }
}

/// <summary>Точка входа плагина для <see cref="AuthoringTests.Plugin_RegistersItsTypes"/>.</summary>
public sealed class TestPlugin : IModPlugin
{
    public static int Calls { get; set; }

    public void Register(ModInfo mod) => Calls++;
}
