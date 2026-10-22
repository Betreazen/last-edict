using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Tests;

/// <summary>Движок событий сценария (<see cref="ScenarioEvents"/>, <c>docs/scenario-events.md</c>) на маленькой карте: два
/// игрока — Империя (<c>PL0001</c>) и Легионы (<c>PL0002</c>), нейтрал (<c>PL0000</c>).</summary>
public class ScenarioEventsTests
{
    private const string Empire = "S001PL0001", Legions = "S001PL0002", Neutral = "S001PL0000";

    private static readonly EventRules Rules = new(new Dictionary<string, RaceInfo>
    {
        ["R0"] = new("R0", Bank.Zero, 0, 0, "", 0, true),
        ["R2"] = new("R2", Bank.Zero, 0, 0, "", 2, true),
        ["R4"] = new("R4", Bank.Zero, 0, 0, "", 4, false),
    }, DiplomacyRules.Vanilla);

    // HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF
    private static readonly bool[] EmpireOnly = [true, false, false, false, false, false];
    private static readonly bool[] Both = [true, false, false, true, false, false];

    [Fact]
    public void Frequency_OnlyForTheTurnPlayer_OnDaysDivisibleByPeriod()
    {
        var set = Set(Event("S001EV0001", [new FrequencyCondition(3)], races: Both, trigger: Both));
        Assert.Empty(Fire(set, Map(turn: 2), turnPlayer: Empire));
        var fired = Fire(set, Map(turn: 3), turnPlayer: Empire);
        Assert.Equal([Empire], fired.Select(f => f.PlayerId));   // Легионы — кандидат, но ход не их
        Assert.Empty(Fire(set, Map(turn: 3), turnPlayer: ""));   // проверка без игрока хода
    }

    [Fact]
    public void DayZero_HasNoEvents() =>
        Assert.Empty(Fire(Set(Event("S001EV0001", [new FrequencyCondition(1)])), Map(turn: 0), turnPlayer: Empire));

    [Fact]
    public void Zone_TriggererStandsInSquare_NotInsideCity_OwnerAffected()
    {
        var set = Set(Event("S001EV0002", [new ZoneCondition("S001LO0001")]));   // зона (10,10) радиус 2
        Assert.Single(Fire(set, Map(stackAt: (12, 8)), triggerer: "S001KC0001"));   // угол квадрата — в зоне
        Assert.Empty(Fire(set, Map(stackAt: (13, 10)), triggerer: "S001KC0001"));
        Assert.Empty(Fire(set, Map(stackAt: (10, 10), inside: "S001FT0001"), triggerer: "S001KC0001"));
        Assert.Empty(Fire(set, Map(stackAt: (10, 10)), triggerer: ""));
        Assert.Empty(Fire(set, Map(stackAt: (10, 10), owner: Legions), triggerer: "S001KC0001"));   // HUMAN… не у Легионов
    }

    [Fact]
    public void Order_ThenId_OneEventPerCall_AllCandidates()
    {
        var a = Event("S001EV0005", [new FrequencyCondition(1)], races: Both, trigger: Both, order: 0);
        var b = Event("S001EV0003", [new FrequencyCondition(1)], races: Both, trigger: Both, order: 1);
        var c = Event("S001EV0002", [new FrequencyCondition(1)], races: Both, trigger: Both, order: 1);
        var set = Set(b, a, c);
        var executed = new HashSet<string>();
        var map = Map(turn: 1);
        var rng = new ScriptedRng(Enumerable.Repeat(0, 10));
        Assert.Equal("S001EV0005", ScenarioEvents.Next(set, EventProgress.From(set), map, Rules, Empire, "", rng, executed).Single().Event.Id);
        Assert.Equal("S001EV0002", ScenarioEvents.Next(set, EventProgress.From(set), map, Rules, Empire, "", rng, executed).Single().Event.Id);
        Assert.Equal("S001EV0003", ScenarioEvents.Next(set, EventProgress.From(set), map, Rules, Empire, "", rng, executed).Single().Event.Id);
        Assert.Empty(ScenarioEvents.Next(set, EventProgress.From(set), map, Rules, Empire, "", rng, executed));
    }

    [Fact]
    public void Chance_RollsForEveryCandidate_FiresWhenRollIsNotAbove()
    {
        var e = Event("S001EV0001", [new OwnCityCondition("S001FT0001")], races: Both, trigger: Both) with { Chance = 30 };
        var set = Set(e);
        // Деревня Империи; условие у обоих кандидатов одинаково (владелец — Империя, HUMAN есть): броски 30 и 31.
        var rng = new ScriptedRng([30, 31]);
        var fired = ScenarioEvents.Next(set, EventProgress.From(set), Map(), Rules, "", "", rng, new HashSet<string>());
        Assert.Equal([Empire], fired.Select(f => f.PlayerId));
        Assert.Equal(2, rng.Used);
        // Шанс 100 бросок тоже тратит.
        var always = new ScriptedRng([99, 99]);
        Assert.Equal(2, ScenarioEvents.Next(Set(e with { Chance = 100 }), EventProgress.From(set), Map(), Rules, "", "", always,
                                          new HashSet<string>()).Count);
    }

    [Fact]
    public void OwnCity_CapitalCountsOnlyWhenCaptured_ForTheCaptor()
    {
        var set = Set(Event("S001EV0001", [new OwnCityCondition("S001FT0000")]));
        Assert.Empty(Fire(set, Map(), turnPlayer: ""));
        Assert.Single(Fire(set, Map(capitalCapturedBy: Empire), turnPlayer: ""));
    }

    [Fact]
    public void Disabled_AndOnce()
    {
        var e = Event("S001EV0001", [new FrequencyCondition(1)]) with { OccurOnce = true };
        var set = Set(e);
        var fired = Fire(set, Map(), turnPlayer: Empire);
        Assert.True(Assert.Single(fired).DisablesItself);
        var progress = EventProgress.From(set).Enable("S001EV0001", false);
        Assert.Empty(ScenarioEvents.Next(set, progress, Map(), Rules, Empire, "", new ScriptedRng([0]), new HashSet<string>()));
    }

    [Fact]
    public void Items_StackExists_KillStack_LootRuin()
    {
        var map = Map();
        Assert.Single(Fire(Set(Event("S001EV0001", [new OwnItemCondition("G000IG0001")])), map, ""));
        Assert.Empty(Fire(Set(Event("S001EV0001", [new OwnItemCondition("G000IG0002")])), map, ""));
        Assert.Single(Fire(Set(Event("S001EV0001", [new StackExistsCondition("S001KC0001", 0)])), map, ""));
        Assert.Single(Fire(Set(Event("S001EV0001", [new StackExistsCondition("S001KC0009", 1)])), map, ""));
        Assert.Single(Fire(Set(Event("S001EV0001", [new StackExistsCondition("S001TM0001", 0)])), map, ""));   // шаблон
        var killed = map.With(destroyed: [new DestroyedStack("S001KC0009", Empire, "")]);
        Assert.Single(Fire(Set(Event("S001EV0001", [new KillStackCondition("S001KC0009")])), killed, ""));
        Assert.Empty(Fire(Set(Event("S001EV0001", [new KillStackCondition("S001KC0008")])), killed, ""));
        Assert.Empty(Fire(Set(Event("S001EV0001", [new LootRuinCondition("S001RU0001")])), map, ""));
    }

    [Fact]
    public void ModifyVariable_Operations()
    {
        var p = EventProgress.Empty with { Variables = new Dictionary<int, int> { [1] = 10 } };
        Assert.Equal(15, p.Modify(new ModifyVariableEffect(1, 0, 5)).Variables[1]);
        Assert.Equal(5, p.Modify(new ModifyVariableEffect(1, 1, 5)).Variables[1]);
        Assert.Equal(50, p.Modify(new ModifyVariableEffect(1, 2, 5)).Variables[1]);
        Assert.Equal(-3, p.Modify(new ModifyVariableEffect(1, 3, -3)).Variables[1]);
        Assert.Equal(7, p.Modify(new ModifyVariableEffect(1, 9, 7)).Variables[1]);
        Assert.Equal(EventProgress.VariableMax, p.Modify(new ModifyVariableEffect(1, 2, 999_999_999)).Variables[1]);
        // Сложение и вычитание тоже в 32 битах (0x43995c): 999 999 999 + 2 000 000 000 переполняется в отрицательное → нижний предел.
        var full = EventProgress.Empty with { Variables = new Dictionary<int, int> { [1] = 999_999_999 } };
        Assert.Equal(EventProgress.VariableMin, full.Modify(new ModifyVariableEffect(1, 0, 2_000_000_000)).Variables[1]);
        var low = EventProgress.Empty with { Variables = new Dictionary<int, int> { [1] = -99_999_999 } };
        Assert.Equal(EventProgress.VariableMax, low.Modify(new ModifyVariableEffect(1, 1, 2_100_000_000)).Variables[1]);
        var set = Set(Event("S001EV0001", [new VariableInRangeCondition([1, 10, 10, 2, 0, 0, 0])]));
        Assert.Single(ScenarioEvents.Next(set, p, Map(), Rules, "", "", new ScriptedRng([0]), new HashSet<string>()));
    }

    [Fact]
    public void Popup_Recipients()
    {
        var e = Event("S001EV0001", [], races: EmpireOnly);
        var map = Map();
        Assert.Equal([Legions], ScenarioEvents.PopupRecipients(new PopupEffect("т", ShowTo: "TRI"), e, Legions, map, Rules));
        Assert.Equal([Empire, Legions], ScenarioEvents.PopupRecipients(new PopupEffect("т", ShowTo: "ALL"), e, Legions, map, Rules));
        Assert.Equal([Empire], ScenarioEvents.PopupRecipients(new PopupEffect("т", ShowTo: "AFF"), e, Legions, map, Rules));
        Assert.Equal([Legions], ScenarioEvents.PopupRecipients(new PopupEffect("т"), e, Legions, map, Rules));   // поля нет — как TRI
        Assert.Empty(ScenarioEvents.PopupRecipients(new PopupEffect("т", ShowTo: "XYZ"), e, Legions, map, Rules));   // иное — никому
        var log = EventProgress.Empty.Logged(Empire, 3, "S001EV0001", 0).Logged(Empire, 3, "S001EV0001", 1);
        Assert.Equal([1, 2], log.Log.Select(l => l.Seq));
    }

    // ---- карта и события

    private static IReadOnlyList<EventFiring> Fire(ScenarioEventSet set, MapState map, string turnPlayer = "", string triggerer = "") =>
        ScenarioEvents.Next(set, EventProgress.From(set), map, Rules, turnPlayer, triggerer,
                            new ScriptedRng(Enumerable.Repeat(0, 10)), new HashSet<string>());

    private static ScenarioEvent Event(string id, IReadOnlyList<EventCondition> conditions, bool[]? races = null,
                                       bool[]? trigger = null, int order = 0) =>
        new(id, true, false, 100, order, races ?? EmpireOnly, conditions, [new PopupEffect("т")], "", trigger ?? EmpireOnly);

    private static ScenarioEventSet Set(params ScenarioEvent[] events) => new(
        events,
        new Dictionary<string, EventZone>(StringComparer.OrdinalIgnoreCase) { ["S001LO0001"] = new("S001LO0001", 10, 10, 2) },
        new Dictionary<int, int>());

    private static MapState Map(int turn = 1, (int X, int Y)? stackAt = null, string inside = "", string owner = Empire,
                                string capitalCapturedBy = "")
    {
        var (x, y) = stackAt ?? (5, 5);
        MapPlayer Player(string id, string race, string captured = "") => new()
        {
            Id = id, RaceId = race, LordId = "", Bank = Bank.Zero, FogId = "", BuildingsId = "", Buildings = [], CapturedBy = captured,
        };
        return new MapState
        {
            Size = 20, Tiles = new Tile[20, 20], Turn = turn, Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>
            {
                [Neutral] = Player(Neutral, "R4"), [Empire] = Player(Empire, "R0"), [Legions] = Player(Legions, "R2", capitalCapturedBy),
            },
            Stacks = new Dictionary<string, MapStack>(StringComparer.OrdinalIgnoreCase)
            {
                ["S001KC0001"] = new()
                {
                    Id = "S001KC0001", Owner = owner, Subrace = "", LeaderId = "", LeaderAlive = true, X = x, Y = y, Inside = inside,
                    UnitIds = [], Slots = [], Items = ["S001IM0001"], TemplateId = "S001TM0001",
                },
            },
            ItemTypes = new Dictionary<string, string> { ["S001IM0001"] = "G000IG0001" },
            Sites =
            [
                new MapSite { Id = "S001FT0001", Kind = SiteKind.Village, RawKind = "MidVillage", Owner = Empire },
                new MapSite { Id = "S001FT0000", Kind = SiteKind.Capital, RawKind = "Capital", Owner = Legions },
                new MapSite { Id = "S001RU0001", Kind = SiteKind.Ruin, RawKind = "MidRuin" },
            ],
            Objects = new Dictionary<string, MapObject>(), Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(),
        };
    }
}

internal static class MapStateTestExtensions
{
    public static MapState With(this MapState map, IReadOnlyList<DestroyedStack> destroyed) => new()
    {
        Size = map.Size, Tiles = map.Tiles, Stacks = map.Stacks, Units = map.Units, Players = map.Players, Objects = map.Objects,
        Plan = map.Plan, Fog = map.Fog, Turn = map.Turn, Sites = map.Sites, ItemTypes = map.ItemTypes, Destroyed = destroyed,
    };
}
