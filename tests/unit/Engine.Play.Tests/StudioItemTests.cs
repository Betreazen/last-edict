using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Tests;

/// <summary>Предметы мода в Студии (Студия 2, задача 4) без окна, на test-pack: новый на основе существующего, правка формой (правила
/// видят модификатор и цену), значок 70×85 в архиве IconItem (HD), удаление со ссылками.</summary>
public sealed class StudioItemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-i-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NewItem_ByForm_WithIcon_RulesSeeIt()
    {
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");
        var items = new ItemWorkspace(session);
        string source = session.Report.Content!.Items.First(i => i.Category == ItemCategory.Jewel).Id;
        Assert.True(items.Create(items.NameFor("Кольцо стража"), "Кольцо стража", source, TestPng.Make(140, 170)));
        const string id = "towers:item/koltso-strazha";
        Assert.Equal(id, items.Selected);
        Assert.True(items.Edit(id, items.FormOf(id) with
        {
            Category = ItemCategory.Jewel, EquipModifier = "test:modifier/charm", Value = new Dictionary<string, long> { ["gold"] = 777 },
            Description = "Бережёт хозяина",
        }));
        var item = (ItemDef)session.Report.Content!.Find(id)!;
        Assert.Equal(("test:modifier/charm", 777L), (item.EquipModifier, item.Value!["gold"]));
        Assert.Equal("Кольцо стража", session.NameOf(id));
        Assert.Equal(0, session.Report.Errors);
        var book = GameBook.Load(session.Report.Content!);
        Assert.Equal("test:modifier/charm", book.Items[id].EquipModifierId);
        session.Save();
        var icon = GameContent.EntityIcons(ModProject.Open(session.Project.Dir).Info).Image("IconItem", "TOWERS:ITEM/KOLTSO-STRAZHA");
        Assert.Equal((70, 85, (int?)2), (icon!.W, icon.H, icon.Scale));
        Assert.True(items.Remove(id));
        Assert.Empty(session.History.Current.Entities);
    }
}
