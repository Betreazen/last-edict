using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Game;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Tests;

/// <summary>Вид расы (Э5, <see cref="RaceLook"/>): расы меню — из содержимого (<see cref="MenuRaces"/>), раса мода — в круге выбора расы
/// рядом с расой оригинала того же рода, портреты лорда и значки мини-карты — по виду расы, а не по роду.</summary>
public class RaceLookTests
{
    private static ContentSet Pack => ContentSource.Open(TestPackTests.Dir);

    /// <summary>Тест-пак (свои расы без id оригинала): каждая раса — в каталоге меню со своими лордами, вид по роду; у рас тест-пака по
    /// одному лорду — выбрать их в меню нельзя (окну выбора лорда нужны все три типа), нейтралы неиграбельны и вне круга.</summary>
    [Fact]
    public void MenuRaces_FromContent_PackRaces()
    {
        var races = MenuRaces.From(Pack);
        Assert.Equal(["test:race/sun", "test:race/moon", "test:race/ember", "test:race/wild"], races.Select(r => r.Id));
        var sun = races.Single(r => r.Id == "test:race/sun");
        // Раса мода без своего вида: графика — расы оригинала рода, а места в круге и саги нет (не занимает их у Империи).
        Assert.Equal(("HU", null, null, false, false), (sun.Code, sun.ChoiceOrder, sun.Saga, sun.Original, sun.Playable));
        Assert.Equal(["test:lord/sun-lord"], sun.Lords.Select(l => l.Id));
        var wild = races.Single(r => r.Id == "test:race/wild");
        Assert.False(wild.Playable);
        Assert.Null(wild.ChoiceOrder);
    }

    /// <summary>Свой вид расы главнее рода; нет своего — вид рода (раса оригинала этого рода).</summary>
    [Fact]
    public void Look_OwnOverridesKind()
    {
        var plain = new RaceDef { Id = "mod:race/ash", Kind = RaceKind.Heretic };
        var byKind = RaceLooks.Of(plain);
        Assert.Equal(("HE", "G000RR0002", null, null), (byKind.Code, byKind.Portraits, byKind.ChoiceOrder, byKind.Saga));
        var own = plain with { Look = new RaceLook { Code = "AS", Art = "ASH", Faces = 2, ChoiceOrder = 9 } };
        Assert.Equal(("AS", 9, "G000RR0002"), (RaceLooks.Of(own).Code, RaceLooks.Of(own).ChoiceOrder, RaceLooks.Of(own).Portraits));
        Assert.Equal("HE", RaceLooks.Of(plain with { Look = new RaceLook { Art = "ASH" } }).Code);   // пустой код — по роду
        var original = new RaceDef { Id = "base:race/legions", LegacyId = "g000rr0002", Kind = RaceKind.Heretic };
        Assert.Equal((4, "C000CC0004"), (RaceLooks.Of(original).ChoiceOrder, RaceLooks.Of(original).Saga));
        Assert.Equal(("NE", 1), (RaceLooks.Legacy(RaceKind.Neutral, null).Code, RaceLooks.Legacy(RaceKind.Neutral, null).Faces));
    }

    [Fact]
    public void LordFaces_ByLook()
    {
        var look = new RaceLook { Code = "AS", Portraits = "MOD_ASH", Faces = 2 };
        Assert.Equal("Lords:LORDASL02", LordFaces.Large(look, 3));   // лиц два — третье становится последним
        Assert.Equal("Lords:LORDASS01", LordFaces.Small(look, 0));
        Assert.Equal("Faces:MOD_ASHLORD2", LordFaces.Round(look, 1));
        Assert.Equal("Lords:LORDNEL01", LordFaces.Large(RaceLooks.Legacy(RaceKind.Neutral, null), 2));   // у нейтралов лицо одно
    }

    /// <summary>Книга партии отдаёт вид расы содержимого; расы нет — нейтральный.</summary>
    [Fact]
    public void GameBook_LookOf()
    {
        var book = GameBook.Load(TestPackTests.Dir);
        Assert.Equal("UN", book.LookOf("test:race/moon").Code);
        Assert.Equal("NE", book.LookOf("nobody:race/none").Code);
    }

    /// <summary>Раса игрока меню не выбрана (пусто) или её нет — первая играбельная по кругу (Империя), а не ошибка посреди меню.</summary>
    [SkippableFact]
    public void PlayerRace_DefaultsToFirstOfRing()
    {
        var catalog = SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data")).Catalog;
        Assert.Equal(BaseRaces.Empire, catalog.PlayerRace("").Id);
        Assert.Equal(BaseRaces.Empire, catalog.PlayerRace("nobody:race/none").Id);
        Assert.Equal(BaseRaces.Elves, catalog.PlayerRace(BaseRaces.Elves).Id);
    }

    /// <summary>Играбельная раса мода без места в круге — в конце круга выбора, а не вне его (сценарий только с ней — не пустой выбор).</summary>
    [SkippableFact]
    public void ChooseRace_PlayableWithoutOrder_EndsTheRing()
    {
        var content = SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var ash = content.Catalog.Race(BaseRaces.Legions) with { Id = "mod:race/ash", Code = "AS", ChoiceOrder = null, Original = false };
        var catalog = content.Catalog.WithRaces([.. content.Catalog.Races, ash]);
        var setup = new NewGameSetup { RaceLimit = ["mod:race/ash"] };
        var ui = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_CHOOSE_RACE"));
        MenuLogic.For("DLG_CHOOSE_RACE", new MenuContext(catalog, new Dictionary<string, string>(), setup, "Игрок"))!.Open(ui);
        Assert.Equal("mod:race/ash", setup.RaceId);
        Assert.Equal("DLG_CHOOSE_RACE_GODAS_BG", ui.Underlay);
    }

    /// <summary>Каталог с расой мода того же рода, что и Легионы (вид мода: свой код, место в круге после рас оригинала): в круге выбора
    /// без сценария она — после Легионов, перед Империей; ролика смены бога к ней и от неё нет, а между расами оригинала — есть.</summary>
    [SkippableFact]
    public void ChooseRace_ModRaceInRing_NoGodVideo()
    {
        var content = SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var legions = content.Catalog.Race(BaseRaces.Legions);
        var ash = legions with { Id = "mod:race/ash", Code = "AS", ChoiceOrder = 9, Original = false, Saga = null, ChoiceWater = null };
        var catalog = content.Catalog.WithRaces([.. content.Catalog.Races, ash]);
        var setup = new NewGameSetup();
        var screen = content.Screens.Single(s => s.Id == "DLG_CHOOSE_RACE");
        var ui = new UiScreenState(screen);
        var logic = MenuLogic.For("DLG_CHOOSE_RACE", new MenuContext(catalog, new Dictionary<string, string>(), setup, "Игрок"))!;
        logic.Open(ui);
        Assert.Equal(BaseRaces.Empire, setup.RaceId);
        var next = screen.Elements.Single(e => e.TextId == "X100TA0129").Id;
        var seen = new List<(string Race, string? Video)>();
        for (int i = 0; i < 6; i++)
        {
            logic.Handle(new UiClicked(next), ui);
            seen.Add((setup.RaceId, ui.VideoOf(UiScreenState.BackgroundId)?.Name));
        }
        Assert.Equal([(BaseRaces.Undead, "GOD_HU2UN"), (BaseRaces.Elves, "GOD_UN2EL"), (BaseRaces.Clans, "GOD_EL2DW"),
                      (BaseRaces.Legions, "GOD_DW2HE"), ("mod:race/ash", null), (BaseRaces.Empire, null)], seen);
        Assert.Equal("DLG_CHOOSE_RACE_GODHU_BG", ui.Underlay);
    }
}
