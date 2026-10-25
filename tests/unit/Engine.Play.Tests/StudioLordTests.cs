using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Лорды мода в Студии (Студия 2, задача 4) без окна, на test-pack: новый на основе существующего (постройки и исследования — его),
/// правка формой (вид, исследования), удаление.</summary>
public sealed class StudioLordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-l-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NewLord_CopiesBuildingsAndResearch_FormEditsResearch()
    {
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");
        var lords = new LordWorkspace(session);
        Assert.True(lords.CreateFrom("test:lord/sun-lord", lords.NameFor("Жрец Солнца"), "Жрец Солнца"));
        const string id = "towers:lord/zhrets-solntsa";
        var source = (LordDef)session.Report.Content!.Find("test:lord/sun-lord")!;
        var made = (LordDef)session.Report.Content.Find(id)!;
        Assert.Equal(source.Buildings, made.Buildings);
        Assert.Equal(source.Research.Count, made.Research.Count);
        var form = lords.FormOf(id);
        Assert.True(lords.Edit(id, form with
        {
            Category = LordCategory.Mage,
            Research = [.. form.Research.Take(1), new SpellResearch { Spell = form.Research[1].Spell, Cost = new Dictionary<string, long> { ["gold"] = 42 } }],
        }));
        made = (LordDef)session.Report.Content!.Find(id)!;
        Assert.Equal((LordCategory.Mage, 42L), (made.Category, made.Research[1].Cost!["gold"]));
        Assert.False(lords.Edit(id, form with { Research = [new SpellResearch { Spell = "towers:spell/none" }] }));
        Assert.Equal(0, session.Report.Errors);
        Assert.NotNull(GameBook.Load(session.Report.Content!));
        Assert.True(lords.Remove(id));
        Assert.Empty(session.History.Current.Entities);
    }
}
