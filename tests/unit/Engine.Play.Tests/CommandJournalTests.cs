using System.Text.Json;
using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Команды партии (этап Э2, задача A, <c>docs/game-commands.md</c>): каждая команда переживает запись в JSON; партия, сыгранная
/// командами, повторяется по журналу с тем же хешем состояния после каждой команды — ходы ИИ и бои автобоем детерминированы, журнал —
/// баг-репорт и основа реплеев и сети.</summary>
public class CommandJournalTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Новая партия «Растленная власть» за Кланы, как из меню (<see cref="NewGame.Begin"/>), но бои событий — автобоем.</summary>
    private static GameSession NewGame()
    {
        ImportedData.Require("scenarios", "rules/Gunits.json", "map", "game");
        string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "Power to Corrupt");
        var content = SharedContent.Load(Data);
        var setup = new LastEdict.Data.Menu.NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Race = 3, LordCategory = 1, Difficulty = 1,
        };
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var state = LastEdict.Data.Play.NewGame.Start(ScenarioFile.Read(path), setup, content.Catalog, book, new Random(1));
        return new GameSession(state, content.MapArt, book);
    }

    /// <summary>Партия командами: постройка, найм, ход отряда человека (план с проверкой событий, туман по шагам, приход) и три конца хода
    /// с ходами ИИ — затем повтор журнала (прочитанного из строк JSON) на новой сессии с того же начала: хеш после каждой команды тот же.</summary>
    [SkippableFact]
    public void Game_PlayedByCommands_ReplaysToTheSameHashes()
    {
        var game = NewGame();
        game.Journal = new CommandJournal(StateHash.Of(game));
        string human = game.State.Info.Humans[0];
        var capital = game.Map.Sites.First(s => s.Kind == SiteKind.Capital && s.Owner.Equals(human, StringComparison.OrdinalIgnoreCase));
        foreach (var building in game.Book!.LordBuildings.GetValueOrDefault(game.Map.Players[human].LordId) ?? [])
            if (game.Execute(new BuildCommand(human, building)).Value is BuildStatus.CanBeBuilt) break;
        string soldier = game.Book.Economy.Races[game.Map.Players[human].RaceId].Soldiers[0];
        game.Execute(new HireUnitCommand(capital.Id, soldier));
        var stack = game.Map.Stacks.Values.First(s => s.Owner.Equals(human, StringComparison.OrdinalIgnoreCase));
        var path = (from dx in Enumerable.Range(-4, 9) from dy in Enumerable.Range(-4, 9)
                    select game.PathTo(stack, stack.X + dx, stack.Y + dy)).First(p => p.Today.Count >= 2);
        var planned = (PlannedMove)game.Execute(new PlanMoveCommand(stack.Id, path.Today)).Value!;
        var today = path.Today.Take(planned.Walk).ToList();
        Assert.NotEmpty(today);
        foreach (var p in today) game.Execute(new RevealFromCommand(stack.Id, p.X, p.Y));
        game.Execute(new MoveStackCommand(stack.Id, today[^1].X, today[^1].Y, 0, today[^1].Cost, planned.Fired, today));
        for (int day = 0; day < 3; day++)
        {
            var result = game.Execute(new EndTurnCommand());
            Assert.Contains(result.Events, e => e is PlayerTurnStarted);
        }
        Assert.True(game.Journal.Entries.Count >= 8);
        Assert.All(game.Journal.Entries, e => Assert.Null(e.Answers));

        var replay = NewGame();
        CommandJournal.FromJsonLines(game.Journal.ToJsonLines()).Replay(replay);
        Assert.Equal(StateHash.Of(game), StateHash.Of(replay));
    }

    /// <summary>
    /// Решения снаружи команды (шаг A2): бои ИИ играет «экран» — по шагам, со своими решениями за сторону защитника (случайными, со своим
    /// зерном), на генераторе партии и с двумя числами <c>rand()</c> фона, — а в начале хода одного ИИ человек дарит ему золото командой,
    /// поданной, пока ход ждёт. Журнал пишет бои действиями и подарок — вложенной командой; повтор без наблюдателя даёт те же хеши.
    /// </summary>
    [SkippableFact]
    public void Answers_FromOutside_ReplayWithoutTheObserver()
    {
        Skip.IfNot(File.Exists(Path.Combine(GamePaths.Exports, "Power to Corrupt.sg")), "нет копии игры");
        var game = AiGame();
        var screen = new ScreenLike(game, seed: 7);
        game.Observer = screen;
        game.Journal = new CommandJournal(StateHash.Of(game));
        for (int day = 0; day < 4; day++) game.Execute(new EndTurnCommand());
        Assert.True(screen.Battles > 0, "бои ИИ сыграны экраном");
        Assert.True(screen.Gifts > 0, "подарок подан вложенной командой");
        var answers = game.Journal.Entries.SelectMany(e => e.Answers ?? []).ToList();
        Assert.Equal(screen.Battles, answers.OfType<BattleAnswer>().Count(a => a.Play is { Actions.Count: > 0 }));
        Assert.Equal(screen.Gifts, answers.OfType<NestedCommand>().Count());

        var replay = AiGame();
        CommandJournal.FromJsonLines(game.Journal.ToJsonLines()).Replay(replay);
        Assert.Equal(StateHash.Of(game), StateHash.Of(replay));
        Assert.Equal(game.AiRng.Index, replay.AiRng.Index);

        // Без решений журнала (автобой ядра, подарка нет) партия другая: повтор её ловит.
        var plain = AiGame();
        var stripped = new CommandJournal(game.Journal.StartHash);
        foreach (var e in game.Journal.Entries) stripped.Add(e.Command, e.Hash);
        Assert.Throws<InvalidDataException>(() => stripped.Replay(plain));
    }

    /// <summary>Партия без человека (все — ИИ), как в <see cref="TurnObserverTests"/>: наблюдатель получает каждый бой.</summary>
    private static GameSession AiGame()
    {
        var looks = MapLooksLoader.Load(Path.Combine(GamePaths.Exports, "Power to Corrupt.sg"));
        return new GameSession(new GameState(GameInfo.FromMap("Power to Corrupt", looks), looks), SharedContent.Load(Data).MapArt,
                               GameBook.Load(Path.Combine(Data, "rules")));
    }

    /// <summary>Наблюдатель как сцена: бой — экраном по шагам (отложенное начало хода, решения за защитника), подарок — командой изнутри хода.</summary>
    private sealed class ScreenLike(GameSession session, int seed) : ITurnObserver
    {
        private readonly Random _choice = new(seed);

        public int Battles, Gifts;

        public void PlayerTurn(string playerId)
        {
            if (Gifts > 0) return;
            int gold = session.Book!.Diplomacy.GoldPerPoint;
            var giver = session.Map.Players.Keys.FirstOrDefault(p => !p.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                                                     && session.Map.Players[p].Bank.Gold >= gold);
            if (giver is null) return;
            var result = session.Execute(new GiveGoldCommand(giver, playerId, gold));
            if (result.Value is null) Gifts++;
        }

        public void StackMoving(StackMove move)
        {
        }

        public IReadOnlyList<TurnRecord>? Battle(BattleState state) => throw new InvalidOperationException("ядро спрашивает DecideBattle");

        public BattleDecision? DecideBattle(BattleState state)
        {
            session.Crt.Next();   // фон экрана (MapScreen.BackgroundDraw)
            session.Crt.Next();
            var rules = session.Book!.Rules;
            var battle = new BattleSession(state, new BattleSim(state, session.BattleRng, rules.Constants), rules.Constants, attackerIsPlayer: false)
            {
                HoldTurnStart = true,
            };
            for (int steps = 0; !battle.Finished && steps < BattleSession.MaxActions; steps++)
            {
                if (battle.TurnStartPending) battle.StartTurn();
                else if (!battle.WaitsPlayer) battle.AiStep();
                else Decide(battle);
            }
            if (!battle.Finished) battle.PlayOut();
            Battles++;
            return new BattleDecision(battle.Log, battle.Played(crtDraws: 2));
        }

        /// <summary>Ход человека: удар по случайной цели, ждать, защита, пропуск; изредка автобой до конца.</summary>
        private void Decide(BattleSession battle)
        {
            var allowed = battle.Allowed;
            int roll = _choice.Next(100);
            if (roll < 3) battle.Auto = true;
            else if (roll < 15 && allowed.Contains(BattleCommand.Wait)) battle.Wait();
            else if (roll < 25 && allowed.Contains(BattleCommand.Defend)) battle.Defend();
            else if (battle.SummonSlots() is { Count: > 0 } slots) battle.SummonAt(slots[_choice.Next(slots.Count)]);
            else if (battle.Targets() is { Count: > 0 } targets) battle.Attack(targets[_choice.Next(targets.Count)]);
            else battle.Skip();
        }
    }

    /// <summary>Каждое действие боя переживает JSON (и ни одно не забыто).</summary>
    [Fact]
    public void EveryBattleAction_RoundTripsThroughJson()
    {
        BattleInput[] all =
        [
            new StartTurnAction(), new AttackAction("U1"), new AttackAction(null), new SummonAction(2), new DefendAction(), new RetreatAction(),
            new SkipAction(), new WaitAction(), new AiDecideAction(), new UseItemAtAction(1, 3), new UseItemOnAction(0, "U2"),
            new UseItemAction(1, "U3"), new ResolveAction(100), new PlayOutAction(BattleSession.MaxActions), new SetAutoAction(true, false),
            new SetAutoAction(null, true),
        ];
        var derived = typeof(BattleInput).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(BattleInput)) && !t.IsAbstract).ToHashSet();
        Assert.Equal(derived, all.Select(c => c.GetType()).ToHashSet());
        var play = new BattlePlay(new BattleSetup(PlayerIsAttacker: false, HoldTurnStart: true), all, CrtDraws: 2);
        string json = JsonSerializer.Serialize(play, CommandJournal.Json);
        var back = JsonSerializer.Deserialize<BattlePlay>(json, CommandJournal.Json)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, CommandJournal.Json));
        Assert.Equal(all, back.Actions);
        // Журнал до вырезки «горячего стула» (Р33) с полем BothHuman читается: поле пропускается.
        string old = json.Replace("\"HoldTurnStart\"", "\"BothHuman\":true,\"HoldTurnStart\"", StringComparison.Ordinal);
        Assert.NotEqual(json, old);
        Assert.Equal(play.Setup, JsonSerializer.Deserialize<BattlePlay>(old, CommandJournal.Json)!.Setup);
    }

    /// <summary>Повтор ловит расхождение: иное начало, иная команда или правка партии мимо команд — ошибка с номером команды.</summary>
    [SkippableFact]
    public void Replay_RefusesADifferentStartOrADivergingCommand()
    {
        var game = NewGame();
        game.Journal = new CommandJournal(StateHash.Of(game));
        game.Execute(new EndTurnCommand());

        var other = NewGame();
        other.Execute(new WinCommand(other.State.Info.Humans[0]));
        Assert.Contains("начальное состояние", Assert.Throws<InvalidDataException>(() => game.Journal.Replay(other)).Message, StringComparison.Ordinal);

        var forged = new CommandJournal(game.Journal.StartHash);
        forged.Add(new EndTurnCommand(), "0000");
        Assert.Contains("команда 0", Assert.Throws<InvalidDataException>(() => forged.Replay(NewGame())).Message, StringComparison.Ordinal);

        // Правка партии мимо команд (прямой вызов метода сессии) видна в журнале у следующей команды, и повтор на ней отказывает.
        string human = game.State.Info.Humans[0];
        Assert.Contains(game.Map.Players.Keys.Where(p => p != human), p => game.GiveGold(human, p, game.Book!.Diplomacy.GoldPerPoint) is null);
        game.Execute(new EndTurnCommand());
        Assert.True(game.Journal.Entries[^1].Outside);
        Assert.Contains("мимо команд", Assert.Throws<InvalidDataException>(() => game.Journal.Replay(NewGame())).Message, StringComparison.Ordinal);
    }

    /// <summary>Каждая команда переживает строку JSON журнала (имя — <c>"$cmd"</c>).</summary>
    [Fact]
    public void EveryCommand_RoundTripsThroughJson()
    {
        PathPoint[] points = [new(1, 2, 3, 0)];
        FiringRef[] fired = [new("E1", "P1", "S1", CutOnly: true)];
        GameCommand[] all =
        [
            new PlanMoveCommand("S1", points), new RevealFromCommand("S1", 1, 2), new MoveStackCommand("S1", 1, 2, 3, 4, fired, points, true),
            new ActInPlaceCommand("S1", true, 2), new LeftFortCommand("S1"), new LeaveFortCommand("S1"), new EndTurnCommand(),
            new BuildCommand("P1", "B1"), new HireUnitCommand("F1", "U1", 2), new HireLeaderCommand("F1", "L1", "Имя"),
            new CastSpellCommand("P1", "SP", 1, 2, true, "T"), new CastItemCommand("S1", "I1", 1, 2), new ResearchSpellCommand("P1", "SP"),
            new GrowCityCommand("V1"), new DismissUnitCommand("G1", "U1"), new HealUnitCommand("F1", "U1"), new ReviveUnitCommand("F1", "U1"), new SwapUnitsCommand("G1", 0, "G2", 1),
            new MoveInGroupCommand("G1", 0, 1), new EquipItemCommand("S1", "I1", 3), new UnequipItemCommand("S1", 3),
            new DrinkPotionCommand("S1", "I1", "U1"), new ExchangeItemCommand("S1", "S2", "I1"), new DropBagCommand("S1", 1, 2),
            new CloseBagCommand("B1"), new TakeFortItemsCommand("S1"), new GiveFortItemsCommand("S1"), new GiveItemsCommand("S1", "S2"),
            new ChooseSkillCommand("S1", "M1"), new RenameLeaderCommand("S1", "Имя"), new ForceDynLevelCommand("U1"),
            new RequestPaperdollCommand("S1"), new PlaceRodCommand("S1", 1, 2), new BreakRodCommand("S1", "R1"),
            new BuyItemCommand("S1", "X1", "I1"), new BuySpellCommand("S1", "X1", "SP"), new SellItemCommand("S1", "X1", "I1"),
            new HireMercenaryCommand("S1", "X1", "U1", 1), new TrainCommand("S1", "X1", "U1"), new ThiefCommand("S1", "T1", ThiefAction.Spy, "x"),
            new ThiefCancelCommand("S1", true), new GiveGoldCommand("P1", "P2", 100), new BreakAllianceCommand("P1", "P2"),
            new ProposeAllianceCommand("P1", "P2"), new AnswerAllianceCommand("P1", "P2", true), new AnswerTradeCommand("P1", "P2", false),
            new AnswerExchangeCommand("P1", "P2", true), new ProposeTradeCommand("P1", "P2", AiGiftKind.Spell, "SP", 10, "F1"),
            new ProposeExchangeMapCommand("P1", "P2"), new EnterCommand("S1", "X1", false, true), new StartBattleCommand("S1", "S2", true),
            new EndBattleCommand([new TurnRecord(1, "U1", "U2", false, 10, 5)],
                                 new BattlePlay(new BattleSetup(false, HoldTurnStart: true), [new AttackAction("U2"), new WaitAction()], 2)), new CommitBattleCommand(), new NextEventBattleCommand(),
            new TakeMessagesCommand("P1"), new WinCommand("P1"), new CheatExperienceCommand("P1", true),
        ];
        var derived = typeof(GameCommand).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GameCommand)) && !t.IsAbstract).ToHashSet();
        Assert.Equal(derived, all.Select(c => c.GetType()).ToHashSet());   // ни одна команда не забыта
        foreach (var command in all)
        {
            string json = JsonSerializer.Serialize<GameCommand>(command, CommandJournal.Json);
            var back = JsonSerializer.Deserialize<GameCommand>(json, CommandJournal.Json)!;
            Assert.Equal(command.GetType(), back.GetType());
            Assert.Equal(json, JsonSerializer.Serialize(back, CommandJournal.Json));
        }
    }
}
