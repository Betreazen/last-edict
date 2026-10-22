using LastEdict.Data.Content;
using LastEdict.Data.Game;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Sg;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Свои кампании (<c>docs/day-cycle.md</c> §15): <c>Campaign\*.csg</c> (MQDB) → каталог порта (<c>d2-port-campaign</c>), окно
/// <c>DLG_NEW_CUSTOM_TOURNEMENT</c>, раса — только из заголовка, первая глава начинается.</summary>
public class CustomCampaignTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<(GameContent Content, GameBook Book)> Shared = new(() =>
        (SharedContent.Load(Data), GameBook.Load(ImportedData.PathOf("rules"))));

    /// <summary>Свои кампании копии игры с зондом (<c>game-dev\Campaign</c> соседнего репозитория).</summary>
    private static string CampaignDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        string guess = Path.Combine(dir?.Parent?.FullName ?? "", "disciples-modding", "game-dev", "Campaign");
        Skip.IfNot(Directory.Exists(guess) && Directory.GetFiles(guess, "*.csg").Length > 0, "нет своих кампаний игры (.csg)");
        return guess;
    }

    /// <summary><c>ui-camp.csg</c> (собран <c>tools/ui-maps/ui-camp</c>): «ui-camp», автор «disciples-godot ui-maps», Империя, уровень
    /// лидера 1, две главы по порядку.</summary>
    [SkippableFact]
    public void Csg_UiCamp_Header()
    {
        var csg = CsgFile.Read(File.ReadAllBytes(Path.Combine(CampaignDir(), "ui-camp.csg")));
        Assert.Equal(("ui-camp", "disciples-godot ui-maps", 0, 1, false), (csg.Name, csg.Author, csg.RaceIndex, csg.LeaderLevel, csg.Started));
        Assert.Equal(["ui-camp-1.sg", "ui-camp-2.sg"], csg.Chapters.Select(c => c.Name.ToLowerInvariant()));
    }

    /// <summary>Все кампании переводятся (главы — сценарии порта), повторный перевод ничего не трогает; окно показывает кампанию,
    /// «Загрузить» — первая глава и только раса кампании; глава начинается партией с кампанией в сведениях.</summary>
    [SkippableFact]
    public void Import_List_Menu_Start()
    {
        var (content, book) = Shared.Value;
        string src = CampaignDir(), dir = Path.Combine(Path.GetTempPath(), "d2-camp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var failed = new List<string>();
            int n = CampaignStore.Import(src, dir, content.Catalog.Races, failed);
            // Все главы разбираются, и старого MidFile тоже (версии 34, 35 и 0 — AVALANCHE, «Путь Проклятых», «Эллион», day-cycle §16);
            // кроме главы, оборванной в самом .csg (AVALANCHE, «Without Alternate») — она в списке без файла.
            Assert.Equal(Directory.GetFiles(src, "*.csg").Length, n);
            Assert.True(failed.All(f => f.Contains("Without Alternate", StringComparison.Ordinal)), string.Join("; ", failed));
            Assert.Equal(0, CampaignStore.Import(src, dir, content.Catalog.Races));
            var list = CampaignStore.List(dir);
            Assert.Equal(n, list.Count);
            Assert.Equal(1, list.Sum(c => c.Chapters.Count(ch => ch.Scenario.File.Length == 0)));
            Assert.All(list.SelectMany(c => c.Chapters).Where(ch => ch.Scenario.File.Length > 0), ch => Assert.True(File.Exists(ch.Scenario.File)));
            var camp = list.Single(c => c.Id == "ui-camp");
            Assert.Equal((true, 2, 0, "ui-camp:ui-camp-1"), (camp.Custom, camp.Chapters.Count, camp.Race, camp.Chapters[0].Scenario.Id));
            Assert.True(File.Exists(camp.Chapters[1].Scenario.File));

            var setup = new NewGameSetup();
            var catalog = content.Catalog with { Campaigns = [.. content.Catalog.Campaigns, .. list] };
            var context = new MenuContext(catalog, UiStrings.Load(ImportedData.StringsDir(), "ru"), setup, "Игрок") { Screens = content.Screens };
            var logic = MenuLogic.For("DLG_NEW_CUSTOM_TOURNEMENT", context)!;
            var ui = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_NEW_CUSTOM_TOURNEMENT"));
            ui.SetList("TLBOX_GAME_SLOT", 0, rowHeight: 20);
            logic.Open(ui);
            int row = list.ToList().FindIndex(c => c.Id == "ui-camp");
            ui.Select("TLBOX_GAME_SLOT", row);
            logic.Handle(new UiListSelected("TLBOX_GAME_SLOT", row), ui);
            Assert.Equal(("2", "1", "disciples-godot ui-maps"), (ui.TextOf("TXT_QTY"), ui.TextOf("TXT_LEVEL"), ui.TextOf("TXT_AUTHOR")));
            logic.Handle(new UiClicked("BTN_LOAD"), ui);
            Assert.Equal(new CampaignRun("ui-camp", 0, 2, Custom: true), setup.Campaign);
            Assert.Equal([0], setup.RaceLimit);

            setup.Race = 0;
            var state = NewGame.Start(ScenarioFile.Read(setup.Scenario!.File), setup, catalog, book, new Random(1));
            Assert.Equal(setup.Campaign, state.Info.Campaign);
            Assert.Equal("ui-camp:ui-camp-1", state.Info.ScenarioId);
            Assert.Equal(camp.Chapters[0].Scenario.Id, catalog.Scenario(state.Info.ScenarioId)?.Id);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
