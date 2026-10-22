using LastEdict.Rules.Map;

namespace LastEdict.Rules.Tests;

/// <summary>
/// Взятие столицы (<see cref="CapitalCapture"/>) на синтетике — без корпуса, без справочников игры
/// и без игры. Здесь проверяются калитки и обе ветки поимённо: в корпусе нет ни одного отказа
/// применения, ни сетевой партии, ни сценарного <c>L_WIN</c>, ни повторного захвата — сверять их
/// на сохранениях нечем (<see cref="CapitalCaptureHarnessTests"/> мерит то, что корпус видел).
/// </summary>
public class CapitalCaptureTests
{
    private const string Captor = "S001PL0000";
    private const string Loser = "S001PL0001";
    private const string Third = "S001PL0002";
    private const string Neutral = "S001PL0003";

    [Fact] public void Capture_WritesCaptBy_RazesRods_AndLeavesTheOwner()
    {
        var map = Map();
        var got = CapitalCapture.Apply(map, Fallen(), Captor, Rules);

        Assert.Null(got.Refused);
        Assert.False(got.Victory);
        Assert.Equal(Captor, got.CapturedBy);
        Assert.Equal(Loser, got.Capital.Owner);                 // владелец CCapital не меняется
        Assert.Empty(got.Capital.Guards);                       // гарнизон стёрт
        Assert.Equal(["S001RD0001"], got.RodsRazed);            // жезлы погибшего, и только его
        Assert.Equal(["S001KC0001"], got.Berserk);
        Assert.Equal(new Bank(1200, 0, 0, 0, 0, 0), got.Captor);
        Assert.Equal(Bank.Zero, got.Loser);
    }

    /// <summary>Обрезка 9999 после сложения — общая с <see cref="Treasury.CapitalCapture"/>; здесь
    /// она проверяется как часть захвата (в корпусе это золото 5523 + 6662 → 9999).</summary>
    [Fact] public void Capture_ClampsTheSumAtNineThousandNineHundredNinetyNine()
    {
        var map = Map(captorBank: new Bank(5523, 0, 0, 0, 0, 0), loserBank: new Bank(6662, 0, 0, 0, 0, 0));
        var got = CapitalCapture.Apply(map, Fallen(), Captor, Rules);

        Assert.Equal(new Bank(9999, 0, 0, 0, 0, 0), got.Captor);
    }

    [Fact] public void Victory_WhenTheCaptorHasExactlyOneLiveOpponent()
    {
        // На карте только захватчик и погибающий: соперник один, события победы нет.
        var map = Map(withThird: false);
        var got = CapitalCapture.Apply(map, Fallen(), Captor, Rules);

        Assert.True(got.Victory);
        Assert.Equal("", got.CapturedBy);
        Assert.Empty(got.RodsRazed);
        Assert.Empty(got.Berserk);
        Assert.Single(got.Capital.Guards);                      // гарнизон не стирается: партия кончилась
        Assert.Equal(new Bank(1000, 0, 0, 0, 0, 0), got.Captor);
        Assert.Equal(new Bank(200, 0, 0, 0, 0, 0), got.Loser);  // перехода казны нет
    }

    [Fact] public void Victory_WhenTheCapitalIsHumanAndTheGameIsSingle()
    {
        var map = Map(loserIsHuman: true);                      // соперников двое — решает первая половина
        Assert.True(CapitalCapture.Apply(map, Fallen(), Captor, Rules).Victory);
        Assert.False(CapitalCapture.Apply(map, Fallen(), Captor, Rules, multiplayer: true).Victory);
    }

    [Fact] public void Victory_BlockedByScenarioWinEventOfTheCaptorRace()
    {
        var map = Map(withThird: false);
        var win = Events(raceOfCaptor: true);
        var alien = Events(raceOfCaptor: false);

        Assert.False(CapitalCapture.Apply(map, Fallen(), Captor, Rules, win).Victory);
        Assert.True(CapitalCapture.Apply(map, Fallen(), Captor, Rules, alien).Victory);
    }

    [Fact] public void Apply_RefusesWhileDefendersAreAlive()
    {
        var map = Map();
        Assert.Contains("живых в гарнизоне 1",
                        CapitalCapture.Apply(map, Capital(), Captor, Rules).Refused);
        Assert.Contains("внутри отряд S001KC0001",
                        CapitalCapture.Apply(map, Fallen() with { StackId = "S001KC0001" }, Captor, Rules).Refused);
        Assert.Contains("не столица",
                        CapitalCapture.Apply(map, Fallen() with { Kind = SiteKind.Village }, Captor, Rules).Refused);
    }

    /// <summary>Юнита гарнизона, которого нет в карте, нельзя считать мёртвым: иначе неполные
    /// данные сами роняли бы игрока.</summary>
    [Fact] public void UnknownGuard_CountsAsAlive()
    {
        var capital = Capital() with { Guards = ["S001UN0404"] };
        Assert.Equal(1, CapitalCapture.LiveGuards(capital, Map().Units));
        Assert.False(CapitalCapture.CapitalFalls(capital, Map().Units));
    }

    [Fact] public void LiveOpponents_SkipsUnplayableAndAlreadyCaptured()
    {
        var map = Map();                                        // захватчик, погибающий, третий, нейтрал
        Assert.Equal(2, CapitalCapture.LiveOpponents(map, Captor, Rules));

        var players = new Dictionary<string, MapPlayer>(map.Players, StringComparer.OrdinalIgnoreCase)
        {
            [Third] = Player(Third, "S001RR0000", Bank.Zero, capturedBy: Captor),
        };
        Assert.Equal(1, CapitalCapture.LiveOpponents(Map(players: players), Captor, Rules));
    }

    [Fact] public void CaptBy_IsNotOverwrittenOnASecondCapture()
    {
        // Живых соперников двое (Third и четвёртый), иначе сработала бы ветка победы.
        var players = new Dictionary<string, MapPlayer>(StringComparer.OrdinalIgnoreCase)
        {
            [Captor] = Player(Captor, "S001RR0000", new Bank(1000, 0, 0, 0, 0, 0)),
            [Loser] = Player(Loser, "S001RR0001", new Bank(200, 0, 0, 0, 0, 0), capturedBy: Third),
            [Third] = Player(Third, "S001RR0002", Bank.Zero),
            ["S001PL0004"] = Player("S001PL0004", "S001RR0000", Bank.Zero),
        };
        var got = CapitalCapture.Apply(Map(players: players), Fallen(), Captor, Rules);

        Assert.Equal(Third, got.CapturedBy);
    }

    [Fact] public void GainsSpells_IsARollUnderTheThreshold()
    {
        Assert.True(CapitalCapture.GainsSpells(49, 50));
        Assert.False(CapitalCapture.GainsSpells(50, 50));
        Assert.Equal(100, CapitalCapture.SpellChanceDie);
    }

    // ------------------------------------------------------------------ синтетика

    private static readonly EconomyRules Rules = new(
        new Dictionary<string, RaceInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["S001RR0000"] = new("S001RR0000", Bank.Zero, 0, 0, "", 0, true),
            ["S001RR0001"] = new("S001RR0001", Bank.Zero, 0, 0, "", 1, true),
            ["S001RR0002"] = new("S001RR0002", Bank.Zero, 0, 0, "", 2, true),
            ["S001RR0004"] = new("S001RR0004", Bank.Zero, 0, 0, "", 4, Playable: false),
        },
        [], new Dictionary<string, BuildingType>(), new Dictionary<string, UnitHire>(),
        [0, 0, 0, 0], 50, 20, 0);

    /// <summary>Столица погибающего с ЖИВЫМ гарнизоном — до боя (<c>S001UN0002</c>, HP 50).</summary>
    private static MapSite Capital() => new()
    {
        Id = "S001FT0000", Kind = SiteKind.Capital, RawKind = "CCapital", X = 5, Y = 5,
        Owner = Loser, Subrace = "S001SR0001", Guards = ["S001UN0002"],
    };

    /// <summary>Та же столица после боя: защитник пал (<c>S001UN0001</c>, HP 0), внутри никого.</summary>
    private static MapSite Fallen() => Capital() with { Guards = ["S001UN0001"] };

    private static ScenarioEventSet Events(bool raceOfCaptor)
    {
        // Флаги расы события — шесть штук в порядке файла; раса захватчика здесь RACE_TYPE 0.
        var races = new bool[6];
        races[raceOfCaptor ? 0 : 2] = true;
        return new ScenarioEventSet(
            [new ScenarioEvent("S001EV0000", true, false, 100, 0, races, [],
                               [new UnsupportedEffect(CapitalCapture.WinEffectCategory, "L_WIN")])],
            new Dictionary<string, EventZone>(), new Dictionary<int, int>());
    }

    private static MapPlayer Player(string id, string raceId, Bank bank, bool human = false,
                                    string capturedBy = "") => new()
    {
        Id = id, RaceId = raceId, LordId = "", IsHuman = human, Bank = bank, FogId = "",
        BuildingsId = "", Buildings = [], CapturedBy = capturedBy,
    };

    private static MapStack Stack(string id, string owner) => new()
    {
        Id = id, Owner = owner, Subrace = "", LeaderId = "", LeaderAlive = true, X = 5, Y = 6,
        Move = 20, Inside = "", UnitIds = [], Slots = [],
    };

    /// <summary>Карта: захватчик, погибающий (жезл, отряд, павший юнит гарнизона), при
    /// <paramref name="withThird"/> — второй живой соперник и нейтрал.</summary>
    private static MapState Map(bool withThird = true, bool loserIsHuman = false,
                                Bank? captorBank = null, Bank? loserBank = null,
                                IReadOnlyDictionary<string, MapPlayer>? players = null)
    {
        var table = new Dictionary<string, MapPlayer>(StringComparer.OrdinalIgnoreCase)
        {
            [Captor] = Player(Captor, "S001RR0000", captorBank ?? new Bank(1000, 0, 0, 0, 0, 0)),
            [Loser] = Player(Loser, "S001RR0001", loserBank ?? new Bank(200, 0, 0, 0, 0, 0), loserIsHuman),
        };
        if (withThird)
        {
            table[Third] = Player(Third, "S001RR0002", Bank.Zero);
            table[Neutral] = Player(Neutral, "S001RR0004", Bank.Zero);
        }
        return new MapState
        {
            Size = 10, Tiles = new Tile[10, 10], Turn = 1, Players = players ?? table,
            Stacks = new Dictionary<string, MapStack>(StringComparer.OrdinalIgnoreCase)
            {
                [Loser + "s"] = Stack("S001KC0001", Loser),
                [Captor + "s"] = Stack("S001KC0000", Captor),
            },
            Units = new Dictionary<string, MapUnit>(StringComparer.OrdinalIgnoreCase)
            {
                ["S001UN0001"] = new() { Id = "S001UN0001", TypeId = "G000UU0001", Hp = 0 },
                ["S001UN0002"] = new() { Id = "S001UN0002", TypeId = "G000UU0001", Hp = 50 },
            },
            Objects = new Dictionary<string, MapObject>(),
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(),
            Rods = new Dictionary<string, MapRod>(StringComparer.OrdinalIgnoreCase)
            {
                ["S001RD0000"] = new("S001RD0000", Captor, 1, 1),
                ["S001RD0001"] = new("S001RD0001", Loser, 2, 2),
            },
        };
    }
}
