using D2.Data.Content;
using D2.Data.Menu;
using D2.Data.Play;
using D2.Data.Ui;
using D2.Data.Ui.Windows;
using D2.Rules.Map;

namespace D2.Data.Tests;

/// <summary>Саги (<c>docs/day-cycle.md</c> §15): «Новая сага» — выбор расы выбирает кампанию, партия — её первая глава и помнит главу
/// (сохранение v6); победа в не последней главе — перенос героя и вещей в столицу следующей главы, в последней — вывоз героя.</summary>
public class CampaignTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<(GameContent Content, GameBook Book)> Shared = new(() =>
        (SharedContent.Load(Data), GameBook.Load(ImportedData.PathOf("rules"))));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedStrings = new(() => UiStrings.Load(ImportedData.StringsDir(), "ru"));
    private static IReadOnlyDictionary<string, string> Strings => LoadedStrings.Value;

    private static ScenarioData Chapter(string id)
    {
        string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), id);
        Skip.IfNot(File.Exists(path), $"нет {path}");
        return ScenarioFile.Read(path);
    }

    /// <summary>Каталог: пять саг меню (7, 7, 7, 7 и 8 глав) по порядку <c>SCEN_NO</c>; главы — не квесты.</summary>
    [SkippableFact]
    public void Catalog_HasSagas_ChaptersInOrder()
    {
        var catalog = Shared.Value.Content.Catalog;
        Assert.Equal([7, 7, 7, 7, 8], new[] { "C000CC0002", "C000CC0003", "C000CC0004", "C000CC0005", "C000CC0006" }
            .Select(id => catalog.Campaigns.Single(c => c.Id == id).Chapters.Count));
        var empire = catalog.Campaigns.Single(c => c.Id == "C000CC0002");
        Assert.Equal(["S107SC0000", "S108SC0000", "S095SC0000"], empire.Chapters.Take(3).Select(c => c.Scenario.Id));
        Assert.Equal(("BRIEFH01", 0), (empire.Chapters[0].BriefingVideo, empire.Race));
        Assert.DoesNotContain(catalog.Scenarios, s => s.Id == "S107SC0000");
        Assert.Equal("S107SC0000", catalog.Scenario("s107sc0000")?.Id);
    }

    /// <summary>«Новая сага» → выбор расы: пять рас саг, первыми эльфы — первая глава их саги; Империя, Кланы — свои саги.</summary>
    [SkippableFact]
    public void SagaMenu_RaceChoosesCampaign()
    {
        var content = Shared.Value.Content;
        var setup = new NewGameSetup { Hotseat = [new HotseatSeat(3, "", 0, 0), new HotseatSeat(2, "", 0, 1)] };   // осталось от хотсита
        var context = new MenuContext(content.Catalog, Strings, setup, "Игрок") { Screens = content.Screens };
        var single = MenuLogic.For("DLG_SINGLE_PLAYER", context)!;
        var sui = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_SINGLE_PLAYER"));
        single.Open(sui);
        single.Handle(new UiClicked("BTN_NEW_TOURNEMENT"), sui);
        Assert.True(setup.Saga);
        Assert.Empty(setup.Hotseat);

        var race = MenuLogic.For("DLG_CHOOSE_RACE", context)!;
        var ui = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_CHOOSE_RACE"));
        race.Open(ui);
        Assert.Equal(5, setup.Race);   // сага открывается на эльфах (0x4edc01)
        Assert.Equal(("S135SC0000", new CampaignRun("C000CC0006", 0, 8)), (setup.Scenario?.Id, setup.Campaign));
        var next = ui.Screen.Elements.First(e => e.TextId == "X100TA0129").Id;
        for (int i = 0; i < 5 && setup.Race != 0; i++) race.Handle(new UiClicked(next), ui);
        Assert.Equal(("S107SC0000", new CampaignRun("C000CC0002", 0, 7)), (setup.Scenario?.Id, setup.Campaign));
        for (int i = 0; i < 5 && setup.Race != 3; i++) race.Handle(new UiClicked(next), ui);
        Assert.Equal((3, "C000CC0003"), (setup.Race, setup.Campaign?.CampaignId));
        Assert.Equal(content.Catalog.Campaigns.Single(c => c.Id == "C000CC0003").Chapters[0].Scenario.Id, setup.Scenario?.Id);

        single.Open(sui);
        single.Handle(new UiClicked("BTN_NEW_SKIRMISH"), sui);
        Assert.Equal((false, (CampaignRun?)null), (setup.Saga, setup.Campaign));
    }

    /// <summary>Первая глава Империи: партия помнит сагу и главу, сохранение хранит их (формат v6); глава не последняя — вывоза нет.</summary>
    [SkippableFact]
    public void ChapterOne_StartsAndSavesCampaign()
    {
        var (content, book) = Shared.Value;
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenario("S107SC0000"), Race = 0, LordCategory = 1, Difficulty = 1,
            Campaign = new CampaignRun("C000CC0002", 0, 7),
        };
        var state = NewGame.Start(Chapter("S107SC0000"), setup, content.Catalog, book, new Random(1));
        Assert.Equal(new CampaignRun("C000CC0002", 0, 7), state.Info.Campaign);
        var back = GameSave.FromJson(GameSave.ToJson(state));
        Assert.Equal(state.Info.Campaign, back.Info.Campaign);
        var session = new GameSession(back, content.MapArt, book);
        Assert.False(session.LastChapter);
        Assert.True(new GameSession(back with { Info = back.Info with { Campaign = new CampaignRun("C000CC0002", 6, 7) } }, content.MapArt, book).LastChapter);
    }

    /// <summary>Перенос: герой главы 1 (опыт, уровень, победы, отложенные навыки; предметы сняты) становится лидером отряда столицы
    /// главы 2 с именем, выбранные вещи — на складе столицы новыми экземплярами.</summary>
    [SkippableFact]
    public void CarryOver_HeroAndItems_ToNextChapterCapital()
    {
        var (content, book) = Shared.Value;
        var first = new NewGameSetup
        {
            Scenario = content.Catalog.Scenario("S107SC0000"), Race = 0, LordCategory = 1, Difficulty = 1,
            Campaign = new CampaignRun("C000CC0002", 0, 7),
        };
        var one = new GameSession(NewGame.Start(Chapter("S107SC0000"), first, content.Catalog, book, new Random(1)), content.MapArt, book);
        string human = one.ActiveHuman;
        var capitalStack = one.Map.Stacks[one.Map.Sites.First(s => s.Kind == SiteKind.Capital && s.Owner == human).StackId];
        var items = one.CarryItems(human);
        var carry = one.CarryOverOf(capitalStack.Id, items) with { BattlesWon = 3, UpgradeCount = 1 };
        Assert.NotNull(carry.Hero);
        Assert.Equal(Math.Min(items.Count, CarryOver.MaxItems), carry.ItemTypes.Count);

        var second = new NewGameSetup
        {
            Scenario = content.Catalog.Scenario("S108SC0000"), Race = 0, LordCategory = 1, Difficulty = 1,
            Campaign = new CampaignRun("C000CC0002", 1, 7), Carry = carry with { Hero = carry.Hero! with { Xp = 77 } },
        };
        var two = new GameSession(NewGame.Start(Chapter("S108SC0000"), second, content.Catalog, book, new Random(2)), content.MapArt, book);
        string human2 = two.ActiveHuman;
        var capital = two.Map.Sites.First(s => s.Kind == SiteKind.Capital && s.Owner == human2);
        var stack = two.Map.Stacks[capital.StackId];
        var leader = two.Map.Units[stack.LeaderId];
        Assert.Equal((carry.Hero!.TypeId, carry.Hero.Level, 77), (leader.TypeId, leader.Level, leader.Xp));
        Assert.Equal((3, 1), (stack.BattlesWon, stack.UpgradeCount));
        Assert.Equal(carry.HeroName, two.Looks.Stacks.Single(s => s.Id == stack.Id).LeaderName);
        var before = Chapter("S108SC0000").Looks.Map.Sites.First(s => s.Id == capital.Id).Items;
        Assert.Equal(before, capital.Items.Take(before.Count));
        Assert.Equal(carry.ItemTypes, capital.Items.Skip(before.Count).Select(i => two.Map.ItemTypes[i]));
        Assert.Equal(new CampaignRun("C000CC0002", 1, 7), two.State.Info.Campaign);
    }

    /// <summary>Перенесённый талисман создаётся заново (<c>CVisitorCreateItem</c>) — с записью зарядов <c>TALIS_CHRG</c>, как любой новый
    /// талисман; без неё подарок ИИ и вывоз героя оценивали бы его пустым (ревью 13.10).</summary>
    [SkippableFact]
    public void CarryOver_Talisman_GetsCharges()
    {
        var (content, book) = Shared.Value;
        var talisman = book.Items.Values.First(i => i.Category == D2.Rules.Magic.ItemCategory.Talisman);
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenario("S108SC0000"), Race = 0, LordCategory = 1, Difficulty = 1,
            Campaign = new CampaignRun("C000CC0002", 1, 7), Carry = new CarryOver { ItemTypes = [talisman.Id] },
        };
        var two = new GameSession(NewGame.Start(Chapter("S108SC0000"), setup, content.Catalog, book, new Random(2)), content.MapArt, book);
        var capital = two.Map.Sites.First(s => s.Kind == SiteKind.Capital && s.Owner == two.ActiveHuman);

        string instance = capital.Items[^1];
        Assert.Equal(talisman.Id, two.Map.ItemTypes[instance], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(book.Vars.TalismanCharges, two.Map.TalismanCharges[instance]);
    }

    /// <summary>Окно переноса: «Закрыть» спрашивает по правилам движка (нет героя, мало вещей, всё выбрано); «Да» отдаёт перенос.</summary>
    [SkippableFact]
    public void CarryWindow_AsksLikeEngine()
    {
        var (content, book) = Shared.Value;
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenario("S107SC0000"), Race = 0, LordCategory = 1, Difficulty = 1,
            Campaign = new CampaignRun("C000CC0002", 0, 7),
        };
        var session = new GameSession(NewGame.Start(Chapter("S107SC0000"), setup, content.Catalog, book, new Random(1)), content.MapArt, book);
        var context = new WindowContext(session, session.ActiveHuman, Strings, UnitFaces.Load(ImportedData.PathOf("rules")));
        CarryOver? got = null;
        var logic = new CarryOverLogic(context, c => got = c);
        var ui = new UiScreenState(content.Screens.Single(s => s.Id == CarryOverLogic.Screen));
        logic.Open(ui);
        string Asked()
        {
            logic.Handle(new UiClicked("BTN_CLOSE"), ui);
            return Assert.IsType<UiAsk>(logic.TakeRequest()).Text;
        }
        Assert.Equal(Strings["X005TA0299"], Asked());
        void Drag((int X, int Y) from, (int X, int Y) to)
        {
            logic.Press(from.X, from.Y, ui);
            logic.Release(to.X, to.Y, ui);
        }
        (int, int) Center(string id) => ui.Screen.Elements.Single(e => e.Id == id).Rect is var r ? (r.X + 10, r.Y + 10) : default;
        logic.Handle(new UiListConfirmed("LBOX_LEADER", 0), ui);                // двойного щелчка у окна нет
        Assert.Equal("", logic.Hero);
        Drag(Center("LBOX_LEADER"), Center("IMG_LEADER_KEEP"));                   // герой — перетаскиванием в слот
        Assert.NotEqual("", logic.Hero);
        Drag(Center("IMG_LEADER_KEEP"), Center("LBOX_ITEMS"));                    // обратно
        Assert.Equal("", logic.Hero);
        Drag(Center("LBOX_LEADER"), Center("IMG_LEADER_KEEP"));
        Assert.Contains("0", Asked(), StringComparison.Ordinal);                  // X005TA0298: выбрано 0
        int items = ui.Cards("LBOX_ITEMS").Count;
        for (int i = 0; i < Math.Min(items, 2); i++) Drag(Center("LBOX_ITEMS"), Center("LBOX_ITEMS_KEEP"));
        if (items >= 2) Assert.Contains("2", Asked(), StringComparison.Ordinal);   // X005TA0297: только 2
        if (items >= 1)
        {
            Drag(Center("LBOX_ITEMS_KEEP"), Center("LBOX_ITEMS"));                // вещь обратно
            Assert.Equal(Math.Min(items, 2) - 1, logic.Kept.Count);
        }
        logic.Handle(new UiClicked("BTN_CLOSE"), ui);
        var question = Assert.IsType<UiAsk>(logic.TakeRequest());
        question.Yes!();
        Assert.NotNull(got);
        Assert.Equal(logic.Kept.Count, got!.ItemTypes.Count);
        Assert.IsType<UiClose>(logic.TakeRequest());
    }
}
