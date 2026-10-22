using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Цели сценария для круга <c>SYM_OBJECTIVE</c> (<see cref="GameSession.Objectives"/>, <c>docs/scenario-events.md</c> §8):
/// события победы расы зрителя, id их условий, отряд в крепости — крепость, тип предмета — держатели.</summary>
public class ScenarioObjectivesTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<(GameContent Content, GameBook Book)> Shared = new(() =>
        (SharedContent.Load(Data), GameBook.Load(Path.Combine(Data, "rules"))));

    private static readonly bool[] Empire = [true, false, false, false, false, false];

    private const string Zone = "S135LO9000";

    private const string EmpirePlayer = "S135PL0001", ElfPlayer = "S135PL0002", Neutral = "S135PL0000";

    /// <summary>Mercantile со своими событиями (<paramref name="events"/>) или событиями сценария (null); событие
    /// <paramref name="disabled"/> выключено.</summary>
    private static GameSession Mercantile(ScenarioEvent[]? events = null, string disabled = "", int turn = 0, int difficulty = 1)
    {
        string path = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Mercantile");
        Skip.IfNot(File.Exists(path), $"нет {path}");
        var data = ScenarioFile.Read(path);
        // В Mercantile областей нет — своя, для условий с областью.
        var zones = new Dictionary<string, EventZone>(data.Events.Zones, StringComparer.OrdinalIgnoreCase) { [Zone] = new(Zone, 10, 10, 1) };
        var set = events is null ? data.Events : data.Events with { Events = events, Zones = zones };
        var looks = turn > 0 ? data.Looks with { Map = GameSession.With(data.Looks.Map, turn: turn) } : data.Looks;
        var state = new GameState(new GameInfo("Mercantile", "Mercantile", [EmpirePlayer], difficulty), looks)
        {
            Events = set, EventProgress = EventProgress.From(set).Enable(disabled, disabled.Length == 0),
        };
        return new GameSession(state, Shared.Value.Content.MapArt, Shared.Value.Book);
    }

    private static ScenarioEvent Goal(string id, EventCondition condition, bool win = true, string winner = EmpirePlayer,
                                      bool enabled = true, bool[]? races = null) =>
        new(id, enabled, false, 100, 0, races ?? Empire, [condition], [new PopupEffect("x"), new WinEffect(win, winner)]);

    /// <summary>Данные сценария: у Империи и Эльфов победа — владеть тремя деревнями (<c>EV001a</c>, <c>EV000e</c>, ещё условия
    /// переменных — без id); у нейтрала флага расы нет — целей нет.</summary>
    [SkippableFact]
    public void Mercantile_Villages_ForEachRace()
    {
        var session = Mercantile();
        string[] villages = ["S135FT0001", "S135FT0003", "S135FT0002"];

        Assert.Equal(villages, session.Objectives(EmpirePlayer));
        Assert.Equal(villages, session.Objectives(ElfPlayer));
        Assert.Empty(session.Objectives(Neutral));
        Assert.Empty(session.Objectives(""));
    }

    /// <summary>Отбор событий (<c>0x5f1d36</c>): последний эффект — «Победа» победителя расы зрителя; выключенное и чужой расы — нет;
    /// победа чужого — не цель; нет ни одного — события «победителя нет» (<c>WIN_SCEN</c> 0).</summary>
    [SkippableFact]
    public void Goals_WinOfOwnRace_ElseNoWinnerEvents()
    {
        var session = Mercantile([
            Goal("S135EV9000", new OwnCityCondition("S135FT0004"), enabled: false),
            Goal("S135EV9001", new OwnCityCondition("S135FT0001"), winner: ElfPlayer),
            Goal("S135EV9002", new OwnCityCondition("S135FT0002"), races: [false, false, false, false, false, true]),
            Goal("S135EV9003", new LootRuinCondition("S135FT0003"), win: false)]);

        Assert.Equal(["S135FT0003"], session.Objectives(EmpirePlayer));

        session = Mercantile([Goal("S135EV9000", new OwnCityCondition("S135FT0004")),
                              Goal("S135EV9003", new OwnCityCondition("S135FT0003"), win: false)]);
        Assert.Equal(["S135FT0004"], session.Objectives(EmpirePlayer));
    }

    /// <summary>Разбор id (<c>0x5f2042</c>): отряд в крепости — крепость, на карте — он сам; тип предмета — крепости со складом,
    /// торговец, отряды и мешки с ним (<c>0x5f21ce</c>); область — она сама; игрок условия отношений и шаблон — не объекты карты.</summary>
    [SkippableFact]
    public void Ids_StackInFort_ItemHolders_Zone()
    {
        var session = Mercantile([new ScenarioEvent("S135EV9000", true, false, 100, 0, Empire,
            [new StackExistsCondition("S135KC0021", 0), new LeaderToZoneCondition("S135KC000d", Zone),
             new OwnItemCondition("G000IG0001"), new AllianceCondition(ElfPlayer, EmpirePlayer)],
            [new WinEffect(true, EmpirePlayer)])]);

        var got = session.Objectives(EmpirePlayer);

        string[] want = ["S135FT0001", "S135KC000d", Zone, "S135FT0000", "S135FT0002", "S135FT0004", "S135SI0000", "S135KC0015",
                         "S135BG0007", "S135BG000b"];
        Assert.Equal(want.Order(), got.Order());
    }

    /// <summary>Множество целей тактики ИИ (<c>0x4662d7</c> → <c>0x5f1e06</c> с флагом 0): отряд, прямо названный в условии, остаётся
    /// отрядом и в крепости (<c>0x5f213f</c>); держатели предмета — как у круга (отряд в крепости — крепость, <c>0x5f25ab</c>); фильтра
    /// рисования нет — игроки условия отношений тоже в множестве.</summary>
    [SkippableFact]
    public void AiTargets_StackStaysStack_NoDrawFilter()
    {
        var session = Mercantile([new ScenarioEvent("S135EV9000", true, false, 100, 0, Empire,
            [new StackExistsCondition("S135KC0021", 0), new LeaderToZoneCondition("S135KC000d", Zone),
             new OwnItemCondition("G000IG0001"), new AllianceCondition(ElfPlayer, EmpirePlayer)],
            [new WinEffect(true, EmpirePlayer)])]);
        var book = Shared.Value.Book;

        var got = ScenarioObjectives.AiTargets(session.Map, session.State.Events, session.State.EventProgress,
                                               new EventRules(book.Economy.Races, book.Diplomacy), EmpirePlayer);

        string[] want = ["S135KC0021", "S135KC000d", Zone, "S135FT0000", "S135FT0002", "S135FT0004", "S135SI0000", "S135KC0015",
                         "S135BG0007", "S135BG000b", ElfPlayer, EmpirePlayer];
        Assert.Equal(want.Order(StringComparer.OrdinalIgnoreCase), got.Order(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Вход в тактику (<see cref="AiTactics.WinTargets"/>): до порога сложности партии множество пусто (средняя — 50 дней,
    /// трудная — 40); с порога — цели событий победы (Mercantile: три деревни).</summary>
    [SkippableTheory]
    [InlineData(49, 1, 0)]
    [InlineData(50, 1, 3)]
    [InlineData(39, 2, 0)]
    [InlineData(40, 2, 3)]
    public void WinTargets_FromDifficultyDay(int turn, int difficulty, int count)
    {
        var session = Mercantile(turn: turn, difficulty: difficulty);

        var got = AiTactics.WinTargets(session, EmpirePlayer);

        Assert.Equal(count, got.Count);
        if (count > 0) Assert.Equal(["S135FT0001", "S135FT0002", "S135FT0003"], got.Order(StringComparer.Ordinal));
    }

    /// <summary>Набор не хранится, а считается по состоянию: событие выключено эффектом — его цели пропадают.</summary>
    [SkippableFact]
    public void Objectives_FollowEventState()
    {
        var session = Mercantile(disabled: "S135EV001a");

        Assert.Empty(session.Objectives(EmpirePlayer));
        Assert.Equal(3, session.Objectives(ElfPlayer).Count);
    }
}
