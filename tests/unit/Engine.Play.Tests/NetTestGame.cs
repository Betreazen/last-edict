using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Net;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Партия сети на своём содержимом (test-pack, идёт в CI): <c>six-crowns</c>, два человека — хост и клиент, остальные ИИ; хост и
/// клиенты соединены в памяти, каждое сообщение проходит запись и чтение JSON протокола (<see cref="NetJson"/>).</summary>
internal static class NetTestGame
{
    public const string Map = "six-crowns";
    public const string HostPlayer = "S903PL0001", ClientPlayer = "S903PL0002";

    public static GameBook Book => _book ??= GameBook.Load(TestPackTests.Dir);

    private static GameBook? _book;

    /// <summary>Начало партии: люди <paramref name="humans"/>, туман по обзору, ход первого человека; зерно <paramref name="seed"/>.</summary>
    public static GameState Start(uint seed = 7, params string[] humans)
    {
        if (humans.Length == 0) humans = [HostPlayer, ClientPlayer];
        var data = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), Map));
        var map = data.Looks.Map;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value with { IsHuman = humans.Contains(p.Key) }, StringComparer.OrdinalIgnoreCase);
        var looks = GameSession.Retold(data.Looks, GameSession.With(map, players: players), data.Looks.Stacks);
        var info = new GameInfo(data.Id, data.Id, [.. humans.Order(StringComparer.Ordinal)], 1, 1);
        var state = new GameState(info, looks)
        {
            Events = data.Events, EventProgress = EventProgress.From(data.Events), ActiveHuman = info.Humans[0],
        }.Reseeded(seed);
        var session = new GameSession(state, MapArtData.Empty, Book);
        foreach (var player in session.Map.Players.Keys) session.ResetFog(player);
        session.BeginFirstDay(info.Humans[0]);
        return session.SnapshotWithoutSummaries();
    }

    /// <summary>Независимая копия партии: клетки и туман — изменяемые массивы, их правит ход (обзор), и две сессии над одним состоянием
    /// делили бы их.</summary>
    public static GameState Copy(GameState state) => GameSave.FromJson(GameSave.ToJson(state, "", DateTime.UnixEpoch));

    public static Ruleset Ruleset { get; } = RulesetHash.Of(TestPackTests.Dir, []);

    /// <summary>Ход человека командами (как окна интерфейса): постройка, найм, ход отряда с планом и туманом по шагам, нападение на
    /// видимого врага рядом (бой на «экране» — автобой по шагам на генераторе сессии), конец хода. <paramref name="run"/> — исполнить
    /// команду (хост — сессия, клиент — через сеть).</summary>
    public static void HumanTurn(GameSession session, string human, Func<GameCommand, CommandResult> run, Random random)
    {
        var map = session.Map;
        var capital = map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(human, StringComparison.OrdinalIgnoreCase));
        foreach (var building in session.Book!.LordBuildings.GetValueOrDefault(map.Players[human].LordId) ?? [])
            if (run(new BuildCommand(human, building)).Value is BuildStatus.CanBeBuilt) break;
        if (capital is not null && session.Book.Economy.Races[map.Players[human].RaceId].Soldiers is [var soldier, ..])
            run(new HireUnitCommand(capital.Id, soldier));
        foreach (var stack in session.Map.Stacks.Values.Where(s => s.Owner.Equals(human, StringComparison.OrdinalIgnoreCase) && s.Move > 0)
                                     .OrderBy(s => s.Id, StringComparer.Ordinal).Take(2).ToList())
        {
            if (!session.Map.Stacks.TryGetValue(stack.Id, out var now)) continue;
            if (Attack(session, now, run)) continue;
            Walk(session, now, run, random);
        }
        run(new EndTurnCommand());
    }

    private static void Walk(GameSession session, MapStack stack, Func<GameCommand, CommandResult> run, Random random)
    {
        var paths = (from dx in Enumerable.Range(-5, 11) from dy in Enumerable.Range(-5, 11)
                     select session.PathTo(stack, stack.X + dx, stack.Y + dy)).Where(p => p.Today.Count >= 2).ToList();
        if (paths.Count == 0) return;
        var path = paths[random.Next(paths.Count)];
        var planned = (PlannedMove)run(new PlanMoveCommand(stack.Id, path.Today)).Value!;
        var today = path.Today.Take(planned.Walk).ToList();
        if (today.Count == 0) return;
        foreach (var p in today) run(new RevealFromCommand(stack.Id, p.X, p.Y));
        run(new MoveStackCommand(stack.Id, today[^1].X, today[^1].Y, 0, today[^1].Cost, planned.Fired, today));
    }

    /// <summary>Видимый чужой отряд вплотную — бой: <c>startBattle</c>, автобой «экраном», <c>endBattle</c> с действиями, <c>commitBattle</c>.</summary>
    private static bool Attack(GameSession session, MapStack stack, Func<GameCommand, CommandResult> run)
    {
        var target = session.Map.Stacks.Values.FirstOrDefault(s => !s.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase)
                                                                   && s.Inside.Length == 0 && Math.Abs(s.X - stack.X) <= 1
                                                                   && Math.Abs(s.Y - stack.Y) <= 1 && !session.Invisible(s.Id));
        if (target is null || stack.Move <= 0) return false;
        var state = (BattleState)run(new StartBattleCommand(stack.Id, target.Id)).Value!;
        var rules = session.Book!.Rules;
        var battle = new BattleSession(state, new BattleSim(state, session.BattleRng, rules.Constants), rules.Constants) { Auto = true };
        battle.PlayOut();
        run(new EndBattleCommand(battle.Log, battle.Played()));
        run(new CommitBattleCommand());
        return true;
    }
}

/// <summary>Хост и клиенты в памяти: сообщение — запись и чтение JSON; хост разбирает почту сразу (<see cref="NetHost.Pump"/>).</summary>
internal sealed class NetHub
{
    private readonly Dictionary<string, NetClient> _clients = new(StringComparer.Ordinal);
    private int _depth;

    public NetHost? Host { get; set; }

    public long Bytes { get; private set; }

    public int Messages { get; private set; }

    public bool Connected(string peer) => !_down.Contains(peer);

    private readonly HashSet<string> _down = new(StringComparer.Ordinal);

    public void Attach(string peer, NetClient client) => _clients[peer] = client;

    public void ToClient(string peer, NetMessage message)
    {
        string json = NetJson.Write(message);
        Bytes += json.Length;
        Messages++;
        if (_down.Contains(peer) || !_clients.TryGetValue(peer, out var client)) return;
        client.Receive(NetJson.Read(json));
    }

    public Action<NetMessage> FromClient(string peer) => message =>
    {
        string json = NetJson.Write(message);
        Bytes += json.Length;
        Messages++;
        if (_down.Contains(peer)) return;
        Host!.Receive(peer, NetJson.Read(json));
        if (_depth > 0) return;
        _depth++;
        try { Host.Pump(); }
        finally { _depth--; }
    };

    /// <summary>Обрыв: сообщения клиенту и от него пропадают, хост узнаёт об уходе.</summary>
    public void Drop(string peer)
    {
        _down.Add(peer);
        Host!.Disconnected(peer);
        _clients[peer].Disconnected();
    }

    public void Restore(string peer) => _down.Remove(peer);
}
