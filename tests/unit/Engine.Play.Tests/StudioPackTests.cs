using System.IO.Compression;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Packaging;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Validation;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Tests;

/// <summary>Мод целиком (Студия 2, задача 6) на test-pack: конфликты слоёв (одно поле правят два мода — кто главнее), порядок наложения —
/// <c>mod.json</c> командой с отменой; упаковка — проверка, архив с описью и хешами, без <c>.studio</c>; мод с ошибкой не пакуется.</summary>
public sealed class StudioPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-p-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string Footman = "test:unit/sun-footman";

    [Fact]
    public void Conflict_SameFieldInTwoMods_OrderByLoadAfter_IsAnUndoableCommand()
    {
        // Чужой мод правит жизнь пехотинца.
        var other = ModProject.Create(Path.Combine(_root, "giants"), "giants");
        var otherDoc = new SetField(Footman, "hitPoints", JsonValue.Create(300)).Apply(ModDocument.Load(other));
        otherDoc.Save(other);
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null,
                                        [ModProject.Open(other.Dir).Info]);
        Assert.True(session.Do(new SetField(Footman, "hitPoints", JsonValue.Create(50))));
        var conflict = Assert.Single(LayerConflicts.Of(session.Report.Layers, "towers"));
        Assert.Equal((Footman, "hitPoints"), (conflict.Entity, conflict.Field));
        Assert.Contains(session.Report.Items, i => i.Rule == "MOD020" && i.Target == Footman);
        // Этот мод главнее — по порядку (после giants), его 50; «уступить» — снять loadAfter и поставить giants после себя нельзя,
        // но отмена возвращает прежний порядок.
        Assert.True(session.Do(new SetManifest("loadAfter", new JsonArray("giants"))));
        Assert.Equal(50, ((UnitDef)session.Report.Content!.Find(Footman)!).HitPoints);
        Assert.Equal("towers", LayerConflicts.Of(session.Report.Layers, "towers").Single().Winner);
        Assert.False(session.Do(new SetManifest("loadAfter", new JsonArray("towers"))));   // после себя — нельзя
        Assert.False(session.Do(new SetManifest("id", JsonValue.Create("x"))));              // id не правится
        session.Save();
        Assert.Contains("giants", File.ReadAllText(Path.Combine(session.Project.Dir, "mod.json")), StringComparison.Ordinal);
        session.Undo();
        session.Save();
        Assert.DoesNotContain("loadAfter", File.ReadAllText(Path.Combine(session.Project.Dir, "mod.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_ChecksTheMod_WritesFilesWithHashes_SkipsStudioFolder()
    {
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null);
        var tree = new TreeWorkspace(session);
        tree.SelectLord("test:lord/sun-lord");
        Assert.True(tree.CreateBuilding("spire", tree.NewForm("test:building/sun-tower") with { Title = "Шпиль" }, TestPng.Make(77, 74)));
        Assert.True(session.Do(new SetManifest("version", JsonValue.Create("1.2.0"))));
        session.Save();
        Directory.CreateDirectory(Path.Combine(session.Project.Dir, ".studio"));
        File.WriteAllText(Path.Combine(session.Project.Dir, ".studio", "waiting"), "-");
        var result = ModPack.Pack(session.Project, TestPackTests.Dir);
        Assert.EndsWith(Path.Combine(".studio", "pack", "towers-1.2.0.zip"), result.Archive, StringComparison.Ordinal);
        Assert.Empty(ModPack.Verify(result.Archive));
        using (var zip = ZipFile.OpenRead(result.Archive))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("mod.json", names);
            Assert.Contains("content/buildings/spire/icon.png", names);
            Assert.Contains(ModPack.ManifestFile, names);
            Assert.DoesNotContain(names, n => n.StartsWith(".studio", StringComparison.Ordinal));
        }
        // Хеш правил — тот же, что у сетевой игры (Э7): с ним хосту и гостю сверяют набор модов.
        var info = ModProject.Open(session.Project.Dir).Info;
        Assert.Equal(LastEdict.Data.Content.RulesetHash.Of(info).Hash, result.RulesetHash);
        using (var zip = ZipFile.OpenRead(result.Archive))
        using (var reader = new StreamReader(zip.GetEntry(ModPack.ManifestFile)!.Open()))
            Assert.Equal(result.RulesetHash, JsonNode.Parse(reader.ReadToEnd())!["rulesetHash"]!.GetValue<string>());
        // Картинка сущности хеш не меняет (графика — не правила), поле в entity.json — меняет.
        string folder = Path.Combine(session.Project.Dir, "content", "buildings", "spire");
        File.AppendAllText(Path.Combine(folder, "icon.png"), "-");
        Assert.Equal(result.RulesetHash, LastEdict.Data.Content.RulesetHash.Of(info).Hash);
        string entity = Path.Combine(folder, "entity.json"), was = File.ReadAllText(entity);
        var changed = JsonNode.Parse(was)!.AsObject();
        changed["level"] = 9;
        File.WriteAllText(entity, changed.ToJsonString());
        Assert.NotEqual(result.RulesetHash, LastEdict.Data.Content.RulesetHash.Of(info).Hash);
        File.WriteAllText(entity, was);
        session.Save();
        // Тот же мод — тот же архив байт в байт; версия с путём — отказ команды.
        var bytes = File.ReadAllBytes(result.Archive);
        Assert.Equal(bytes, File.ReadAllBytes(ModPack.Pack(session.Project, TestPackTests.Dir).Archive));
        Assert.False(session.Do(new SetManifest("version", JsonValue.Create("x/../../evil"))));
        Assert.False(session.Do(new SetManifest("name", JsonValue.Create(5))));
        // Мод с ошибкой (висячая ссылка) не упаковывается; архив внутри мода вне .studio — отказ.
        Assert.True(session.Do(new SetField("towers:building/spire", "requires", JsonValue.Create("towers:building/ghost"))));
        session.Save();
        Assert.Throws<InvalidOperationException>(() => ModPack.Pack(session.Project, TestPackTests.Dir));
        session.Undo();
        session.Save();
        Assert.Throws<InvalidOperationException>(() => ModPack.Pack(session.Project, TestPackTests.Dir, outDir: Path.Combine(session.Project.Dir, "content")));
    }

    [Fact]
    public void Pack_ModWithoutRules_HasNoRulesetHash()
    {
        // Мод только с mod.json (графика, музыка) правил партии не меняет — хеша нет, как у сетевой игры (RulesetHash.HasRules).
        var project = ModProject.Create(Path.Combine(_root, "look"), "look");
        Assert.Null(ModPack.Pack(project, TestPackTests.Dir).RulesetHash);
    }
}
