using LastEdict.Rules.Map;

namespace LastEdict.Rules.Tests;

/// <summary>Расы и игроки событий сценария (Last Edict): шесть флагов оригинала знают только расы с флагом (<see cref="RaceInfo.EventFlag"/>);
/// событие называет расу мода по id содержимого (<see cref="ScenarioEvent.RaceIds"/>) и различает двоих одной расы по игрокам
/// (<see cref="ScenarioEvent.PlayerIds"/>).</summary>
public class EventRacesTests
{
    private const string First = "S001PL0001", Second = "S001PL0002", Embers = "S001PL0003";

    // Империя (флаг 0), раса мода «Угли» без флага событий; двое игроков Империи — на сторонах 0 и 10.
    private static readonly RaceInfo Empire = new("base:race/empire", Bank.Zero, 0, 0, "", 0, true) { EventFlag = 0 };
    private static readonly RaceInfo ModRace = new("embers:race/embers", Bank.Zero, 0, 0, "", 0, true) { EventFlag = -1 };

    private static readonly EventRules Rules = new(new Dictionary<string, RaceInfo>
    {
        [Empire.Id] = Empire, [ModRace.Id] = ModRace,
    }, DiplomacyRules.Vanilla);

    // HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF
    private static readonly bool[] HumanFlag = [true, false, false, false, false, false];

    private static ScenarioEvent Event(IReadOnlyList<EventEffect>? effects = null) =>
        new("S001EV0001", true, false, 100, 0, HumanFlag, [], effects ?? []);

    private static MapPlayer Player(string id, string race, int? party = null) => new()
    {
        Id = id, RaceId = race, LordId = "", Bank = Bank.Zero, FogId = "", BuildingsId = "", Buildings = [], Party = party,
    };

    private static MapState Map() => new()
    {
        Size = 4, Tiles = new Tile[4, 4], Turn = 1, Units = new Dictionary<string, MapUnit>(),
        Players = new Dictionary<string, MapPlayer>
        {
            [First] = Player(First, Empire.Id), [Second] = Player(Second, Empire.Id, party: 10), [Embers] = Player(Embers, ModRace.Id),
        },
        Stacks = new Dictionary<string, MapStack>(), Objects = new Dictionary<string, MapObject>(),
        Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(), Fog = new Dictionary<string, bool[,]>(),
    };

    [Fact]
    public void ModRace_WithoutAFlag_IsNotAHumanForTheOriginalFlags_ButIsNamedById()
    {
        Assert.True(ScenarioEvents.ForRace(Event(), Empire));
        Assert.False(ScenarioEvents.ForRace(Event(), ModRace));
        var byId = Event() with { RaceIds = [ModRace.Id] };
        Assert.True(ScenarioEvents.ForRace(byId, ModRace));
        Assert.False(ScenarioEvents.ForRace(byId, Empire));   // id рас главнее флагов
    }

    [Fact]
    public void PlayerIds_TellTwoPlayersOfTheSameRaceApart()
    {
        var map = Map();
        var onlySecond = Event() with { PlayerIds = [Second] };
        Assert.False(ScenarioEvents.ForPlayer(onlySecond, map.Players[First], Empire));
        Assert.True(ScenarioEvents.ForPlayer(onlySecond, map.Players[Second], Empire));
        Assert.Equal([Second], ScenarioEvents.Candidates(onlySecond, map, Rules, null).Select(p => p.Id));
        Assert.Equal([First, Second], ScenarioEvents.Candidates(Event(), map, Rules, null).Select(p => p.Id));
        Assert.Equal([Embers], ScenarioEvents.Candidates(Event() with { TriggerRaceIds = [ModRace.Id] }, map, Rules, null).Select(p => p.Id));
        // Событие с id рас и флагами VER… (как у сценария из оригинала) берёт кандидатов из id.
        var withFlags = Event() with { RaceIds = [ModRace.Id] };
        withFlags = withFlags with { TriggerRaces = HumanFlag };
        Assert.Equal([Embers], ScenarioEvents.Candidates(withFlags, map, Rules, null).Select(p => p.Id));
    }

    /// <summary>Событие победы при взятии столицы — и у второго игрока той же расы (сторона 10): по флагу рода прежде его не находило.</summary>
    [Fact]
    public void WinEvent_IsFound_ForTheSecondPlayerOfTheSameRace()
    {
        var map = Map();
        var events = new ScenarioEventSet([Event([new WinEffect(true, "")])], new Dictionary<string, EventZone>(), new Dictionary<int, int>());
        Assert.True(CapitalCapture.HasWinEvent(events, map.Players[Second], Empire));
        Assert.False(CapitalCapture.HasWinEvent(events, map.Players[Embers], ModRace));
    }
}
