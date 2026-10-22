using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Map;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Расы и игроки (Р6, Р15, Э4) на своём содержимом: карта <c>six-crowns</c> test-pack — шесть игроков трёх рас, по двое одной расы со
/// своими сторонами, три команды по двое. Идёт в CI.</summary>
public class TeamsTests(ITestOutputHelper output)
{
    private const string Map = "six-crowns";

    private static (GameSession Session, GameBook Book) Start(uint seed = 1)
    {
        var book = GameBook.Load(TestPackTests.Dir);
        var data = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), Map));
        var state = new GameState(GameInfo.FromMap(data.Id, data.Looks), data.Looks)
        {
            Events = data.Events, EventProgress = EventProgress.From(data.Events),
        }.Reseeded(seed);
        return (new GameSession(state, MapArtData.Empty, book), book);
    }

    /// <summary>Команда — вечный союз: 20 дней ИИ, союзники ни разу не бьются друг с другом и остаются союзниками; игроки одной расы из
    /// разных команд — соперники (ИИ на них нападает).</summary>
    [Fact]
    public void Teammates_StayAllied_AndNeverFight()
    {
        var (session, book) = Start();
        var battles = new List<BattleOutcome>();
        for (int day = 0; day < 20; day++)
            battles.AddRange(session.Execute(new EndTurnCommand()).Events.OfType<BattleFinished>().Select(b => b.Outcome));
        var map = session.Map;
        Assert.DoesNotContain(battles, b => Teams.SameTeam(map, b.AttackerOwner, b.DefenderOwner));
        foreach (var a in map.Players.Values.Where(p => p.Team is not null))
            foreach (var b in map.Players.Values.Where(p => p.Id != a.Id && Teams.SameTeam(a, p)))
                Assert.True(Diplomacy.AreAllies(map.Relations, a.Party!.Value, b.Party!.Value), $"{a.Id} и {b.Id} — не союзники");
        int rivals = battles.Count(b => map.Players.TryGetValue(b.AttackerOwner, out var x) && map.Players.TryGetValue(b.DefenderOwner, out var y)
                                        && x.RaceId == y.RaceId && book.Economy.Races[x.RaceId].Playable);
        output.WriteLine($"боёв {battles.Count}, между игроками одной расы {rivals}");
        Assert.NotEmpty(battles);
    }

    /// <summary>Победа — команде: соперники захватчика — игроки вне его команды; победил один — победила вся команда, даже павший.</summary>
    [Fact]
    public void Victory_IsShared_AndCountsOnlyRivals()
    {
        var (session, book) = Start();
        var map = session.Map;
        Assert.Equal(4, CapitalCapture.LiveOpponents(map, "S903PL0001", book.Economy));   // 6 игроков: сам и союзник не в счёте
        var won = new GameSession(session.State with { Info = session.State.Info with { Winner = "S903PL0001" } }, MapArtData.Empty, book);
        Assert.True(won.Won("S903PL0001"));
        Assert.True(won.Won("S903PL0005"));    // союзник по команде north
        Assert.False(won.Won("S903PL0004"));   // та же раса, другая команда
        Assert.Equal("союз команды не разорвать", session.BreakAllianceBy("S903PL0001", "S903PL0005"));
    }

    /// <summary>Поле дипломатии при шести игроках: сам и по три соперника на странице, стрелки листают по кругу, смена страницы снимает выбор.</summary>
    [Fact]
    public void Diplomacy_PagesRivalsByThree()
    {
        var (session, _) = Start();
        var context = new LastEdict.Data.Ui.Windows.WindowContext(session, "S903PL0001", new Dictionary<string, string>(),
                                                                   new LastEdict.Data.Ui.Windows.UnitFaces(new Dictionary<string, string>()));
        var model = new LastEdict.Data.Ui.Windows.DiplomacyModel(context, new Dictionary<string, string>());
        Assert.Equal((5, 2), (model.Others.Count, model.Pages));
        Assert.Equal(["S903PL0001", "S903PL0002", "S903PL0003", "S903PL0004"], model.Players.Select(p => p.Id));
        var field = new LastEdict.Data.Ui.Windows.DiplomacyFieldLogic(model);
        // Разметка поля — те же id, что у DLG_ISO_DIPLO (своя, без данных игры).
        LastEdict.Data.Ui.UiElement El(string id, LastEdict.Data.Ui.UiKind kind) => new() { Id = id, Kind = kind, Rect = new(0, 0, 30, 30) };
        List<LastEdict.Data.Ui.UiElement> elements = [El("TXT_PLAYER_CUR", LastEdict.Data.Ui.UiKind.Text), El("TXT_PLAYER_SEL", LastEdict.Data.Ui.UiKind.Text)];
        for (int i = 1; i <= 4; i++) elements.AddRange([El($"IMG_RACE{i}", LastEdict.Data.Ui.UiKind.Image), El($"TXT_RACE{i}", LastEdict.Data.Ui.UiKind.Text)]);
        for (int i = 1; i <= 3; i++)
            elements.AddRange([El($"IMG_METER{i}", LastEdict.Data.Ui.UiKind.Image), El($"IMG_TENDENCY{i}_L", LastEdict.Data.Ui.UiKind.Image),
                               El($"IMG_TENDENCY{i}_R", LastEdict.Data.Ui.UiKind.Image), El($"IMG_TENDENCYA{i}", LastEdict.Data.Ui.UiKind.Image)]);
        var screen = field.Shape(new LastEdict.Data.Ui.UiScreen { Id = LastEdict.Data.Ui.Windows.DiplomacyFieldLogic.Screen, Elements = elements });
        Assert.Contains(screen.Elements, e => e.Id == LastEdict.Data.Ui.Windows.DiplomacyFieldLogic.NextPage);
        var ui = new LastEdict.Data.Ui.UiScreenState(screen);
        model.Selected = 2;
        field.Handle(new LastEdict.Data.Ui.UiClicked(LastEdict.Data.Ui.Windows.DiplomacyFieldLogic.NextPage), ui);
        Assert.Equal((1, 0), (model.Page, model.Selected));
        Assert.Equal(["S903PL0001", "S903PL0005", "S903PL0006"], model.Players.Select(p => p.Id));
        field.Handle(new LastEdict.Data.Ui.UiClicked(LastEdict.Data.Ui.Windows.DiplomacyFieldLogic.NextPage), ui);
        Assert.Equal(0, model.Page);
    }

    /// <summary>Новая партия по карте без сторон (как карта оригинала, где у двоих одна раса): второй и дальше игрок своего рода получает
    /// свою сторону (с <see cref="NewGame.FirstOwnParty"/>), у каждой пары сторон — запись отношений.</summary>
    [Fact]
    public void Sides_SplitPlayersOfOneRace_AndEveryPairHasRelations()
    {
        var (session, book) = Start();
        var bare = GameSession.With(session.Map, players: session.Map.Players.ToDictionary(p => p.Key, p => p.Value with { Party = null, Team = null }),
                                    relations: []);
        var sided = NewGame.Sides(bare, book);
        var parties = sided.Players.Values.Where(p => book.Economy.Races[p.RaceId].Playable)
                           .ToDictionary(p => p.Id, p => Diplomacy.PartyOf(p, book.Economy.Races)!.Value);
        output.WriteLine(string.Join(", ", parties.Select(p => $"{p.Key}:{p.Value}")));
        Assert.Equal(parties.Count, parties.Values.Distinct().Count());
        Assert.Equal([0, 1, 2, 10, 11, 12], parties.Values.Order());
        var relations = Teams.Bound(sided, book.Economy);
        Assert.Equal(6 * 5 / 2, relations.Count);
        Assert.All(relations, r => Assert.Equal(Teams.FreshRelation, r.Current));
    }
}
