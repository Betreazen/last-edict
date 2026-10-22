using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;

namespace LastEdict.Data.Tests;

/// <summary>«Горячий стул» (<c>docs/day-cycle.md</c> §5): люди ходят по очереди в одном дне (<c>NextPlayer 0x420929</c>),
/// начало хода человека — в момент его хода, день растёт после ИИ.</summary>
public class HotseatTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Кланы (<c>PL000a</c>, хозяин) и Легионы (<c>PL000b</c>) — люди: ходят Кланы, потом ИИ, чья раса по ключу очереди раньше
    /// Легионов, потом Легионы того же дня (их доход — в начале их хода), потом остальные ИИ и день +1 — снова Кланы. Множителя стартовой казны нет (одиночная игра — только одиночная).</summary>
    [SkippableFact]
    public void TwoHumans_TakeTurnsInOneDay()
    {
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var data = ScenarioFile.Read(scenario);
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Difficulty = 1,
            Hotseat = [new HotseatSeat(3, "", 0, 0), new HotseatSeat(2, "", 0, 1)],
        };
        var state = NewGame.Start(data, setup, content.Catalog, book, new Random(1));
        Assert.Equal(["S073PL000A", "S073PL000B"], state.Info.Humans.Select(h => h.ToUpperInvariant()));
        var session = new GameSession(state, content.MapArt, book);
        Assert.True(session.Hotseat);
        Assert.Equal("S073PL000A", session.ActiveHuman.ToUpperInvariant());
        var legionsBank = session.Map.Players["S073PL000b"].Bank;
        // Ни множителя, ни дохода до своего хода; SPELL_BANK файла влит при занятии места (CVisitorChangePlayerType, у каждого человека).
        var file = data.Looks.Map.Players["S073PL000b"];
        Assert.Equal(LastEdict.Rules.Map.Economy.Clamp(file.Bank + file.SpellBank), legionsBank);

        // Очередь дня (0x42b8e8, 28.09): хозяин (Кланы), затем все по ключу расы — ИИ с ключом меньше, чем у Легионов, ходит между людьми.
        var order = GameSession.DayOrder(session.Map, state.Info.Humans, book);
        var beforeLegions = order.Skip(1).TakeWhile(id => !id.Equals("S073PL000b", StringComparison.OrdinalIgnoreCase)).ToList();
        var report = session.EndTurn();
        Assert.Equal(1, session.Map.Turn);
        Assert.Equal("S073PL000B", session.ActiveHuman.ToUpperInvariant());
        Assert.True(session.Map.Players["S073PL000b"].Bank.Gold > legionsBank.Gold, "доход Легионов — в начале их хода");
        Assert.Equal(beforeLegions.Select(id => $"ход {id}"),
                     report.Events.Where(e => e.StartsWith("ход ", StringComparison.Ordinal)));   // ходили только ИИ раньше Легионов

        report = session.EndTurn();
        Assert.Equal(2, session.Map.Turn);
        Assert.Equal("S073PL000A", session.ActiveHuman.ToUpperInvariant());
        var afterLegions = order.SkipWhile(id => !id.Equals("S073PL000b", StringComparison.OrdinalIgnoreCase)).Skip(1).ToList();
        Assert.Equal(afterLegions.Select(id => $"ход {id}"), report.Events.Where(e => e.StartsWith("ход ", StringComparison.Ordinal)));
        Assert.NotEmpty(beforeLegions);   // Power to Corrupt: ИИ S073PL0009 (ключ расы меньше, чем у Легионов) ходит между людьми

        var back = GameSave.FromJson(GameSave.ToJson(session.Snapshot() with { ActiveHuman = "S073PL000b" }));
        Assert.Equal("S073PL000b", back.ActiveHuman);
    }

    /// <summary>Попап «всем» в начале хода Легионов: окно — только Легионам (местный игрок, <c>0x489536</c>), у Кланов, которые не за
    /// ПК, попап пропадает, но запись в журнале заданий у обоих (<c>CEffectPopup</c> <c>0x43b290</c> пишет её до отправки).</summary>
    [SkippableFact]
    public void Popup_ToHumanNotSeated_Lost_QuestLogKept()
    {
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var data = ScenarioFile.Read(scenario);
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Difficulty = 1,
            Hotseat = [new HotseatSeat(3, "", 0, 0), new HotseatSeat(2, "", 0, 1)],
        };
        bool[] heretic = [false, false, false, true, false, false];   // HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF
        var legions = new LastEdict.Rules.Map.ScenarioEvent("S999EV0000", true, false, 100, -1, heretic,
            [new LastEdict.Rules.Map.FrequencyCondition(1)], [new LastEdict.Rules.Map.PopupEffect("всем", ShowTo: "ALL")], "проверка", heretic);
        var state = NewGame.Start(data with { Events = data.Events with { Events = [legions] } }, setup, content.Catalog, book, new Random(1));
        var session = new GameSession(state, content.MapArt, book);
        var sights = new SightRecorder();
        session.Observer = sights;
        session.EndTurn();   // ход Легионов
        Assert.Equal("S073PL000B", session.ActiveHuman.ToUpperInvariant());
        // Точка показа «сразу» (и в ходе ИИ) — только тому, кому попап лёг.
        var shown = Assert.Single(sights.Sights, s => s.Kind == TurnSightKind.EventPopup);
        Assert.Equal(("S073PL000B", "S999EV0000"), (shown.PlayerId.ToUpperInvariant(), shown.Id));
        Assert.Empty(session.TakeMessages("S073PL000a"));
        Assert.Single(session.TakeMessages("S073PL000b"));
        var logged = session.State.EventProgress.Log.Where(l => l.EventId == "S999EV0000").Select(l => l.PlayerId.ToUpperInvariant()).ToList();
        Assert.Contains("S073PL000A", logged);
        Assert.Contains("S073PL000B", logged);
    }

    /// <summary>Выбывание в хотсите (<c>docs/day-cycle.md</c> §13, <c>0x440ee0</c>): Кланы берут пустую столицу Легионов — партия
    /// идёт дальше (у захватчика есть другие живые соперники), Легионы выбыли (<c>CAPT_BY</c>), казна у захватчика; ход Легионов
    /// в тот же день всё равно наступает (<c>NextPlayer</c> выбывших не пропускает), доход у них нулевой.</summary>
    [SkippableFact]
    public void CapturedHuman_GameGoesOn_TurnStillComes()
    {
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Difficulty = 1,
            Hotseat = [new HotseatSeat(3, "", 0, 0), new HotseatSeat(2, "", 0, 1)],
        };
        var state = NewGame.Start(ScenarioFile.Read(scenario), setup, content.Catalog, book, new Random(1));
        var map = state.Looks.Map;
        var legions = map.Sites.Single(s => s.Kind == LastEdict.Rules.Map.SiteKind.Capital && s.Owner.Equals("S073PL000b", StringComparison.OrdinalIgnoreCase));
        var dwarf = map.Stacks.Values.First(s => s.Owner.Equals("S073PL000a", StringComparison.OrdinalIgnoreCase));
        // Столица Легионов без защитников, отряд Кланов у её входа.
        var sites = map.Sites.Select(s => s.Id == legions.Id ? s with { Guards = [], GuardSlots = [-1, -1, -1, -1, -1, -1], StackId = "" } : s).ToList();
        var stacks = map.Stacks.Where(p => !p.Key.Equals(legions.StackId, StringComparison.OrdinalIgnoreCase))
                               .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[dwarf.Id] = dwarf with { X = legions.X + 3, Y = legions.Y + 3, Inside = "", Move = 20 };
        var plan = map.Plan.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value.Where(id => !id.Equals(dwarf.Id, StringComparison.OrdinalIgnoreCase)
                                                                                                          && !id.Equals(legions.StackId, StringComparison.OrdinalIgnoreCase))]);
        plan[(legions.X + 3, legions.Y + 3)] = [dwarf.Id];
        var session = new GameSession(state with { Looks = GameSession.Retold(state.Looks, GameSession.With(map, stacks: stacks, sites: sites, plan: plan), state.Looks.Stacks) },
                                      content.MapArt, book);
        long legionsGold = session.Map.Players["S073PL000b"].Bank.Gold;
        var legionsBank = session.Map.Players["S073PL000b"].Bank;
        var notices = new LastEdict.Data.Ui.Windows.NoticeWatch(_ => true);
        session.Watch(notices);

        var visit = session.Enter(dwarf.Id, legions.Id);

        Assert.True(visit.Captured, visit.Refused);
        // Окно взятых ресурсов захватчику (CCmdCaptureResourcesMsg, 0x434d61): казна погибшего до захвата.
        var taken = Assert.Single(notices.Take("S073PL000a"), n => n.Kind == TurnSightKind.ResourcesCaptured);
        Assert.Equal(("S073PL000B", legionsBank), (taken.Id.ToUpperInvariant(), taken.Bank));
        Assert.False(session.Over);
        Assert.Equal("S073PL000A", session.Map.Players["S073PL000b"].CapturedBy.ToUpperInvariant());
        Assert.Equal(0, session.Map.Players["S073PL000b"].Bank.Gold);
        Assert.True(session.Map.Players["S073PL000a"].Bank.Gold >= legionsGold);
        session.EndTurn();
        Assert.Equal("S073PL000B", session.ActiveHuman.ToUpperInvariant());
        Assert.Equal(0, session.Map.Players["S073PL000b"].Bank.Gold);
    }

    /// <summary>Окно передачи хода: «%NAME%, %RACE% … Начинается Ваш ход!» с именем и расой игрока и портрет лобби по расе и
    /// лицу.</summary>
    [SkippableFact]
    public void TurnPopup_NameRaceFace()
    {
        ImportedData.Require("ui/strings.ru.json");   // русский текст окна — только в импорте с русификацией
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var strings = LastEdict.Data.Ui.UiStrings.Load(Path.Combine(Data, "ui"), "ru");
        var player = ScenarioFile.Read(scenario).Looks.Map.Players["S073PL000a"] with { Name = "Гимли", Face = 2 };
        var ui = new LastEdict.Data.Ui.UiScreenState(content.Screens.Single(s => s.Id == "DLG_HOTSEAT_POPUP"));
        new HotseatTurnLogic(player, content.Catalog, id => strings.GetValueOrDefault(id) ?? id).Open(ui);
        string text = ui.TextOf("TXT_TEXT")!;
        Assert.Contains("Гимли", text);
        Assert.Contains("Начинается Ваш ход", text);
        Assert.DoesNotContain("%RACE%", text);
        Assert.Equal("DLG_HOTSEAT_LOBBY_RACE_FACEDW3N", ui.Frame("IMG_FACE"));
    }

    /// <summary>Окно «горячего стула» в режимах 1 и 2 (<c>0x4bdef3</c>, <c>0x4bdfae</c>): предложение — имя и раса предлагающего,
    /// <c>%TARGETNAME%</c> — имя адресата; нападение — имя и раса защитника, <c>%TARGETNAME%</c> — имя его группы; портрет — всегда того,
    /// кого зовут.</summary>
    [SkippableFact]
    public void CallPopups_MessageAndDefend()
    {
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var strings = LastEdict.Data.Ui.UiStrings.Load(Path.Combine(Data, "ui"), "en");
        var players = ScenarioFile.Read(scenario).Looks.Map.Players;
        var clans = players["S073PL000a"] with { Name = "Gimli", Face = 2 };
        var legions = players["S073PL000b"] with { Name = "Bethrezen", Face = 0 };
        string legionsFace = $"DLG_HOTSEAT_LOBBY_RACE_FACE{content.Catalog.Races.Single(r => r.Id.Equals(legions.RaceId, StringComparison.OrdinalIgnoreCase)).Code}1N";
        string Text(string id) => strings.GetValueOrDefault(id) ?? id;
        var ui = new LastEdict.Data.Ui.UiScreenState(content.Screens.Single(s => s.Id == "DLG_HOTSEAT_POPUP"));

        new HotseatTurnLogic(legions, content.Catalog, Text, HotseatCall.Message, sender: clans).Open(ui);
        string message = ui.TextOf("TXT_TEXT")!;
        Assert.Contains("from \\fMedBold;Gimli", message);
        Assert.Contains("message for  \\fMedBold;Bethrezen", message);
        Assert.Equal(legionsFace, ui.Frame("IMG_FACE"));

        new HotseatTurnLogic(legions, content.Catalog, Text, HotseatCall.Defend, target: "Tower").Open(ui);
        string defend = ui.TextOf("TXT_TEXT")!;
        Assert.Contains("Tower is under attack!", defend);
        Assert.Contains("Bethrezen", defend);
        Assert.DoesNotContain("%", defend);
        Assert.Equal(legionsFace, ui.Frame("IMG_FACE"));
    }

    /// <summary>Кого звать к бою (<c>CTaskBattle</c> <c>0x4d79c8</c>): защитник — человек партии «горячего стула» (отряд — имя лидера,
    /// крепость — её имя), в том числе тот, кто сейчас ходит; ИИ и одиночная игра — никого. Обе стороны — люди: каждый ведёт свою.</summary>
    [SkippableFact]
    public void Defender_HumanOfHotseat_BothSidesHuman()
    {
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var data = ScenarioFile.Read(scenario);
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Difficulty = 1,
            Hotseat = [new HotseatSeat(3, "Gimli", 0, 0), new HotseatSeat(2, "Bethrezen", 0, 1)],
        };
        var session = new GameSession(NewGame.Start(data, setup, content.Catalog, book, new Random(1)), content.MapArt, book);
        var map = session.Map;
        var clansStack = map.Stacks.Values.First(s => s.Owner.Equals("S073PL000a", StringComparison.OrdinalIgnoreCase));
        var legionsStack = map.Stacks.Values.First(s => s.Owner.Equals("S073PL000b", StringComparison.OrdinalIgnoreCase));
        var aiStack = map.Stacks.Values.First(s => s.Owner.Equals("S073PL0009", StringComparison.OrdinalIgnoreCase));
        var legionsCapital = map.Sites.Single(s => s.Kind == LastEdict.Rules.Map.SiteKind.Capital && s.Owner.Equals("S073PL000b", StringComparison.OrdinalIgnoreCase));
        string leaderName = session.Looks.Stacks.Single(s => s.Id == legionsStack.Id).LeaderName;

        var call = HotseatCalls.Defender(session, legionsStack.Id, id => id);
        Assert.Equal("S073PL000b", call?.Player.Id, ignoreCase: true);
        if (leaderName.Length > 0) Assert.Equal(leaderName, call?.Target);
        else Assert.False(string.IsNullOrEmpty(call?.Target));
        Assert.Equal(legionsCapital.Name, HotseatCalls.Defender(session, legionsCapital.Id, id => id)?.Target);
        Assert.Equal("S073PL000a", HotseatCalls.Defender(session, clansStack.Id, id => id)?.Player.Id, ignoreCase: true);
        Assert.Null(HotseatCalls.Defender(session, aiStack.Id, id => id));
        Assert.True(HotseatCalls.BothHuman(session, clansStack.Id, legionsStack.Id));
        Assert.True(HotseatCalls.BothHuman(session, clansStack.Id, legionsCapital.Id));
        Assert.False(HotseatCalls.BothHuman(session, clansStack.Id, aiStack.Id));

        var single = new GameSession(session.State with { Info = session.State.Info with { Humans = ["S073PL000a"] } }, content.MapArt, book);
        Assert.Null(HotseatCalls.Defender(single, legionsStack.Id, id => id));

        // Бой двух людей: команду ждёт юнит любой стороны; без флага — только сторона нападающего.
        var state = session.StartBattle(clansStack.Id, legionsStack.Id);
        var both = new LastEdict.Data.Battle.BattleSession(state, new LastEdict.Rules.Battle.BattleSim(state, new LastEdict.Rules.Rng.EngineRng(), book.Rules.Constants),
                                                    book.Rules.Constants) { BothHuman = true };
        var sides = new HashSet<bool>();
        for (int i = 0; i < 40 && !both.Finished; i++)
        {
            Assert.True(both.WaitsPlayer);
            sides.Add(both.Current!.Attacker);
            both.Defend();
        }
        Assert.Equal([false, true], sides.Order());
        var one = new LastEdict.Data.Battle.BattleSession(state, new LastEdict.Rules.Battle.BattleSim(state, new LastEdict.Rules.Rng.EngineRng(), book.Rules.Constants),
                                                   book.Rules.Constants);
        Assert.False(one.IsPlayers(state.Units.First(u => !u.Attacker)));
        Assert.True(both.IsPlayers(state.Units.First(u => !u.Attacker)));
    }

    /// <summary>Окно имени героя первого хода (<c>CChangeFirstLeaderNameInterf</c>, <c>docs/day-cycle.md</c> §17) — каждому человеку:
    /// по умолчанию имя его расы, ещё не носимое на карте; кнопка переименовывает героя столицы, пустое имя — отказ; имя едет в сохранение.</summary>
    [SkippableFact]
    public void FirstTurn_LeaderName_EachHuman_Renames()
    {
        string scenario = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(scenario), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Difficulty = 1,
            Hotseat = [new HotseatSeat(3, "", 0, 0), new HotseatSeat(2, "", 0, 1)],
        };
        var session = new GameSession(NewGame.Start(ScenarioFile.Read(scenario), setup, content.Catalog, book, new Random(1)), content.MapArt, book);
        foreach (string human in session.State.Info.Humans)
        {
            var hero = session.FirstLeader(human);
            Assert.NotNull(hero);
            var (stackId, _, name) = hero.Value;
            Assert.Contains(name, book.LeaderNames[session.Map.Players[human].RaceId].Select(n => n.Trim()));
            Assert.Single(session.Looks.Stacks, s => s.LeaderName.Trim() == name);
            Assert.Equal("пустое имя", session.RenameLeader(stackId, "  "));
            Assert.Null(session.RenameLeader(stackId, "  Герой " + human + " "));
            Assert.Equal("Герой " + human, session.FirstLeader(human)!.Value.Name);
        }
        Assert.Equal("нет отряда", session.RenameLeader("нет-такого", "Имя"));
        var back = GameSave.FromJson(GameSave.ToJson(session.Snapshot()));
        string first = session.State.Info.Humans[0];
        Assert.Contains(back.Looks.Stacks, s => s.LeaderName == "Герой " + first);
    }
}
