using System.Text.Json.Nodes;
using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Net;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>Протокол сети без сети (Э7, <c>docs/network.md</c>): дельты вида, проекция по туману без секретов, хост и клиент в памяти против
/// той же партии локально — тот же хеш, тот же вид. Идёт в CI на test-pack.</summary>
public class NetProtocolTests
{
    // ---- дельты вида

    [Fact]
    public void ViewPatch_RandomEdits_ApplyBackToTheSameDocument()
    {
        var random = new Random(5);
        for (int round = 0; round < 300; round++)
        {
            var from = RandomNode(random, 4);
            var to = Mutate(from?.DeepClone(), random, 4);
            var ops = ViewPatch.Diff(from, to);
            var applied = ViewPatch.Apply(from?.DeepClone(), ops);
            Assert.Equal(ViewPatch.CanonicalJson(to), ViewPatch.CanonicalJson(applied));
            var wire = System.Text.Json.JsonSerializer.Serialize(ops, NetJson.Options);
            var read = System.Text.Json.JsonSerializer.Deserialize<List<PatchOp>>(wire, NetJson.Options)!;
            Assert.Equal(ViewPatch.CanonicalJson(to), ViewPatch.CanonicalJson(ViewPatch.Apply(from?.DeepClone(), read)));
        }
    }

    [Fact]
    public void ViewPatch_ArrayInsertAndRemoval_IsOneSplice()
    {
        var from = JsonNode.Parse("""{"s":[{"id":1},{"id":2},{"id":3},{"id":4}]}""");
        var to = JsonNode.Parse("""{"s":[{"id":1},{"id":3},{"id":9},{"id":4}]}""");
        var ops = ViewPatch.Diff(from, to);
        Assert.Equal(2, ops.Count);   // одна длина — по элементам: два поменялись
        var shorter = JsonNode.Parse("""{"s":[{"id":1},{"id":4}]}""");
        var splice = Assert.Single(ViewPatch.Diff(from, shorter));
        Assert.Equal((PatchOp.Op.Splice, 1, 2), (splice.Kind, splice.At, splice.Count));
        Assert.Throws<InvalidDataException>(() => ViewPatch.Apply(JsonNode.Parse("""{"x":1}"""), ops));
    }

    private static JsonNode? RandomNode(Random r, int depth) => r.Next(depth <= 0 ? 3 : 6) switch
    {
        0 => JsonValue.Create(r.Next(5)),
        1 => JsonValue.Create("v" + r.Next(4)),
        2 => null,
        3 or 4 => new JsonObject(Enumerable.Range(0, r.Next(4)).Select(i => KeyValuePair.Create("k" + r.Next(6) + "/~" + i, RandomNode(r, depth - 1)))
                                                       .DistinctBy(p => p.Key)),
        _ => new JsonArray([.. Enumerable.Range(0, r.Next(5)).Select(_ => RandomNode(r, depth - 1))]),
    };

    private static JsonNode? Mutate(JsonNode? node, Random r, int depth)
    {
        if (r.Next(5) == 0) return RandomNode(r, depth);
        switch (node)
        {
            case JsonObject o:
                foreach (string key in o.Select(p => p.Key).ToList())
                    switch (r.Next(4))
                    {
                        case 0: o.Remove(key); break;
                        case 1: o[key] = Mutate(o[key]?.DeepClone(), r, depth - 1); break;
                    }
                if (r.Next(3) == 0) o["n" + r.Next(9)] = RandomNode(r, depth - 1);
                return o;
            case JsonArray a:
                if (a.Count > 0 && r.Next(3) == 0) a.RemoveAt(r.Next(a.Count));
                if (r.Next(3) == 0) a.Insert(r.Next(a.Count + 1), RandomNode(r, depth - 1));
                for (int i = 0; i < a.Count; i++)
                    if (r.Next(4) == 0) a[i] = Mutate(a[i]?.DeepClone(), r, depth - 1);
                return a;
            default:
                return node;
        }
    }

    // ---- проекция и секреты

    [Fact]
    public void PlayerView_HidesWhatTheViewerDoesNotKnow()
    {
        var state = NetTestGame.Start();
        var session = new GameSession(state, MapArtData.Empty, NetTestGame.Book);
        for (int day = 0; day < 3; day++) session.Execute(new EndTurnCommand());   // ИИ походил, у людей есть что скрывать
        var full = session.SnapshotWithoutSummaries();
        string me = NetTestGame.ClientPlayer;
        var map = full.Looks.Map;
        var fog = map.Fog[map.Players[me].FogId];
        var view = PlayerView.Of(full, me);
        var shown = view.Looks.Map;

        var hiddenStacks = map.Stacks.Values.Where(s => !s.Owner.Equals(me, StringComparison.OrdinalIgnoreCase) && fog[s.X, s.Y]).ToList();
        Assert.NotEmpty(hiddenStacks);
        Assert.All(hiddenStacks, s =>
        {
            Assert.False(shown.Stacks.ContainsKey(s.Id));
            Assert.DoesNotContain(view.Looks.Stacks, l => l.Id == s.Id);
            Assert.All(s.UnitIds.Where(u => u.Length > 0), u => Assert.False(shown.Units.ContainsKey(u)));
            Assert.DoesNotContain(shown.Plan.Values, ids => ids.Contains(s.Id, StringComparer.OrdinalIgnoreCase));
        });
        Assert.All(map.Stacks.Values.Where(s => s.Owner.Equals(me, StringComparison.OrdinalIgnoreCase)), s => Assert.Equal(s, shown.Stacks[s.Id]));
        foreach (var (id, player) in shown.Players.Where(p => !p.Key.Equals(me, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Equal(Bank.Zero, player.Bank);
            Assert.Equal(Bank.Zero, player.SpellBank);
            Assert.Empty(player.KnownSpells);
            Assert.Empty(player.Buildings);
            Assert.All(Cells(shown.Fog[player.FogId]), hidden => Assert.True(hidden));
        }
        Assert.Equal(map.Players[me], shown.Players[me]);
        Assert.Contains(map.Players.Values, p => p.Id != me && p.Bank != Bank.Zero);
        Assert.Null(view.Random);
        Assert.Empty(view.Ai);
        Assert.Empty(view.Tactic);
        Assert.All(shown.Sites.Where(s => fog[s.X, s.Y]), s => Assert.Empty(s.Guards));

        // Проекция — та же партия для показа: читается, пишется и даёт тот же вид.
        var node = NetView.Of(full, me);
        var read = NetView.Read(node);
        Assert.Equal(ViewPatch.Hash(node), ViewPatch.Hash(NetView.Build(read, read.Looks.Map.IdNext)));
        Assert.Equal(map.IdNext.OrderBy(p => p.Key), read.Looks.Map.IdNext.OrderBy(p => p.Key));
        string text = node.ToJsonString();
        Assert.All(hiddenStacks, s => Assert.DoesNotContain(s.Id, text, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<bool> Cells(bool[,] mask) => mask.Cast<bool>();

    // ---- набор правил

    [Fact]
    public void Ruleset_CountsRulesFiles_NotTextsOrPictures()
    {
        string dir = Path.Combine(Path.GetTempPath(), "le-ruleset-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "content", "buildings", "spire"));
            File.WriteAllText(Path.Combine(dir, "mod.json"), """{"id": "towers", "name": "Towers"}""");
            File.WriteAllText(Path.Combine(dir, "content", "buildings", "spire", "entity.json"), """{"level": 2}""");
            File.WriteAllText(Path.Combine(dir, "content", "buildings", "spire", "strings.ru.json"), """{"name": "Шпиль"}""");
            File.WriteAllBytes(Path.Combine(dir, "content", "buildings", "spire", "icon.png"), [1, 2, 3]);
            var mod = new ModInfo("towers", "Towers", dir);
            var before = RulesetHash.Of(TestPackTests.Dir, [mod]);
            Assert.Equal(["content/buildings/spire/entity.json", "mod.json"], RulesetHash.ModFiles(mod).Select(f => f.Name));

            // Тексты, значки и форматирование JSON правил не меняют.
            File.WriteAllText(Path.Combine(dir, "content", "buildings", "spire", "strings.ru.json"), """{"name": "Башня"}""");
            File.WriteAllBytes(Path.Combine(dir, "content", "buildings", "spire", "icon.png"), [9, 9]);
            File.WriteAllText(Path.Combine(dir, "content", "buildings", "spire", "entity.json"), "{\r\n  \"level\" : 2\r\n}");
            Assert.Equal(before.Hash, RulesetHash.Of(TestPackTests.Dir, [mod]).Hash);

            // Правило — меняет, и отказ называет мод.
            File.WriteAllText(Path.Combine(dir, "content", "buildings", "spire", "entity.json"), """{"level": 3}""");
            var after = RulesetHash.Of(TestPackTests.Dir, [mod]);
            Assert.NotEqual(before.Hash, after.Hash);
            Assert.Equal(before.Base, after.Base);
            Assert.Contains(RulesetHash.Differences(before, after), l => l.Contains("towers", StringComparison.Ordinal) && l.Contains("другой версии", StringComparison.Ordinal));
            var bare = RulesetHash.Of(TestPackTests.Dir, []);
            Assert.Contains(RulesetHash.Differences(after, bare), l => l.StartsWith("нет мода", StringComparison.Ordinal));
            Assert.Contains(RulesetHash.Differences(bare, after), l => l.StartsWith("лишний мод", StringComparison.Ordinal));
            Assert.Empty(RulesetHash.Differences(after, RulesetHash.Of(TestPackTests.Dir, [mod])));
            Assert.Equal(64, after.Hash.Length);

            // Плагин — входит.
            Directory.CreateDirectory(Path.Combine(dir, "plugins"));
            File.WriteAllBytes(Path.Combine(dir, "plugins", "x.dll"), [7]);
            var coded = new ModInfo("towers", "Towers", dir) { Plugins = ModInfo.PluginsIn(dir) };
            Assert.NotEqual(after.Hash, RulesetHash.Of(TestPackTests.Dir, [coded]).Hash);
            Assert.True(RulesetHash.Of(coded).ExecutesCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- хост и клиент

    /// <summary>
    /// Партия двух людей через хост и клиента в памяти: люди ходят по очереди (постройка, найм, ход отряда, нападение — бой на «экране»
    /// клиента с генератором хоста), между ними ходит ИИ (его нападения на клиента — бой у клиента по запросу хоста). После хода каждого
    /// человека вид клиента = проекция партии хоста; журнал хоста, прочитанный из строк, повторяется локально на свежей сессии с тем же хешем
    /// после каждой команды, и вид клиента = проекция повтора.
    /// </summary>
    [Fact]
    public void HostAndClient_PlayTheSameGameAsLocally()
    {
        var start = NetTestGame.Start();
        var game = Network(start, out var hub, out var client);
        int battlesAsked = 0, clientBattles = 0;
        client.BattleRequested += request =>
        {
            battlesAsked++;
            var battle = client.BuildRequestedBattle(request);
            Assert.NotNull(battle);
            var rules = client.Session!.Book!.Rules;
            var play = new BattleSession(battle!, new BattleSim(battle!, client.Session.BattleRng, rules.Constants), rules.Constants,
                                         request.Side == "attacker") { Auto = true };
            play.PlayOut();
            client.ReplyBattle(request, play.Played(), play.Log);
        };
        var random = new Random(3);
        for (int day = 0; day < 8 && !game.Over; day++)
        {
            Assert.Equal(NetTestGame.HostPlayer, game.ActiveHuman);
            NetTestGame.HumanTurn(game, NetTestGame.HostPlayer, c => game.Execute(c), random);
            Assert.True(client.MyTurn || game.Over);
            if (game.Over) break;
            AssertSameView(client, game);
            NetTestGame.HumanTurn(client.Session!, NetTestGame.ClientPlayer, c =>
            {
                if (c is StartBattleCommand) clientBattles++;
                return client.Session!.Execute(c);
            }, random);
            AssertSameView(client, game);
        }
        Assert.True(game.Journal!.Entries.Count > 40, $"команд {game.Journal.Entries.Count}");
        Assert.Contains(game.Journal.Entries, e => e.Command is EndBattleCommand { Play: not null });
        Assert.True(clientBattles > 0, $"боёв клиента {clientBattles}, запросов боя {battlesAsked}");

        // Журнал хоста — то, что считала сеть: повтор с начала даёт тот же хеш после каждой команды.
        var replay = new GameSession(NetTestGame.Copy(start), MapArtData.Empty, NetTestGame.Book);
        CommandJournal.FromJsonLines(game.Journal.ToJsonLines()).Replay(replay);
        Assert.Equal(StateHash.Of(game), StateHash.Of(replay));
        Assert.Equal(ViewPatch.Hash(NetView.Of(replay.SnapshotWithoutSummaries(), NetTestGame.ClientPlayer)), client.ViewHash);
    }

    /// <summary>Бой ИИ с отрядом клиента (в ходе ИИ — наблюдатель хоста, <see cref="NetHost.RemoteBattle"/>): клиент собирает тот же бой
    /// по виду (отпечаток), играет его на генераторе боя хоста и отвечает действиями; хост повторяет их — партия та же, что при повторе
    /// тех же действий локально.</summary>
    [Fact]
    public void AiBattle_AgainstTheClient_IsPlayedOnTheClient()
    {
        var start = NetTestGame.Copy(NetTestGame.Start());
        var startMap = start.Looks.Map;
        var sight = startMap.Fog[startMap.Players[NetTestGame.ClientPlayer].FogId];
        for (int x = 0; x < startMap.Size; x++)
            for (int y = 0; y < startMap.Size; y++)
                sight[x, y] = false;   // клиент видит нападающего — как когда тот подошёл вплотную
        var game = Network(start, out var hub, out var client);
        BattleRequest? asked = null;
        client.BattleRequested += request =>
        {
            asked = request;
            var battle = client.BuildRequestedBattle(request)!;
            var rules = client.Session!.Book!.Rules;
            var play = new BattleSession(battle, new BattleSim(battle, client.Session.BattleRng, rules.Constants), rules.Constants, false) { Auto = true };
            play.PlayOut();
            client.ReplyBattle(request, play.Played(), play.Log);
        };
        var mine = game.Map.Stacks.Values.First(s => s.Owner == NetTestGame.ClientPlayer);
        var foe = game.Map.Stacks.Values.First(s => s.Owner != NetTestGame.ClientPlayer && !game.State.Info.Humans.Contains(s.Owner)
                                                    && s.Inside.Length == 0);
        var copy = NetTestGame.Copy(game.SnapshotWithoutSummaries());
        var state = game.StartBattle(foe.Id, mine.Id);
        var decision = game.Observer!.DecideBattle(state);
        Assert.NotNull(asked);
        Assert.Equal("defender", asked!.Side);
        Assert.NotNull(decision?.Play);
        game.FinishBattle(decision!.Log);
        hub.Host!.Broadcast();

        var local = new GameSession(copy, MapArtData.Empty, NetTestGame.Book);
        var again = local.StartBattle(foe.Id, mine.Id);
        local.FinishBattle(local.ReplayBattleHere(decision.Play!, again));
        Assert.Equal(StateHash.Of(local), StateHash.Of(game));
        AssertSameView(client, game);
    }

    /// <summary>Обрыв связи посреди партии: хост ходит дальше, клиент возвращается по ключу места — получает недостающее (дельтами или
    /// снимком), и вид снова тот же; испорченная дельта (хеш не сошёлся) — resync, а не догадка.</summary>
    [Fact]
    public void Reconnect_AndBrokenDelta_RestoreTheView()
    {
        var game = Network(NetTestGame.Start(), out var hub, out var client);
        var random = new Random(1);
        hub.Drop("peer-1");
        NetTestGame.HumanTurn(game, NetTestGame.HostPlayer, c => game.Execute(c), random);   // клиент не слышит
        Assert.Throws<NetCommandException>(() => client.Session!.Execute(new EndTurnCommand()));   // и не может ходить
        hub.Restore("peer-1");
        client.Connect();
        AssertSameView(client, game);
        Assert.True(client.MyTurn);

        // Новое соединение (перезапуск игры клиента): ключ места тот же, вида нет — снимок.
        var again = new NetClient(new NetClientOptions { Ruleset = NetTestGame.Ruleset, Token = client.Token }, hub.FromClient("peer-2"),
                                  view => new GameSession(view, MapArtData.Empty, NetTestGame.Book));
        hub.Attach("peer-2", again);
        again.Connect();
        Assert.Equal(NetTestGame.ClientPlayer, again.Player);
        AssertSameView(again, game);

        // Испорченная дельта: клиент просит снимок и получает тот же вид.
        long seq = again.Seq;
        again.Receive(new ViewDelta(again.Epoch!, seq, seq + 1, [new PatchOp(PatchOp.Op.Set, "/state/Day", JsonValue.Create(999))], "0"));
        AssertSameView(again, game);
        Assert.True(again.Seq > seq);
    }

    /// <summary>Повтор command_id — прежний ответ без второго исполнения; команда не в свой ход или над чужим отрядом — отказ; другие
    /// правила — отказ входа с текстом.</summary>
    [Fact]
    public void Host_RefusesWhatItMust()
    {
        var game = Network(NetTestGame.Start(), out var hub, out var client);
        var probe = new NetClient(new NetClientOptions { Ruleset = NetTestGame.Ruleset, Token = client.Token }, hub.FromClient("peer-3"),
                                  view => new GameSession(view, MapArtData.Empty, NetTestGame.Book));
        hub.Attach("peer-3", probe);
        probe.Connect();
        // Не свой ход.
        var refused = Assert.Throws<NetCommandException>(() => probe.Session!.Execute(new EndTurnCommand()));
        Assert.Equal(NetErrors.WrongTurn, refused.Code);
        game.Execute(new EndTurnCommand());   // ход хоста кончен — ходит клиент
        Assert.True(probe.MyTurn);
        // Чужой отряд.
        var foreign = game.Map.Stacks.Values.First(s => s.Owner == NetTestGame.HostPlayer);
        var notYours = Assert.Throws<NetCommandException>(() => probe.Session!.Execute(new RenameLeaderCommand(foreign.Id, "x")));
        Assert.Equal(NetErrors.NotYours, notYours.Code);
        Assert.DoesNotContain(foreign.Id, notYours.Message, StringComparison.OrdinalIgnoreCase);
        // Повтор command_id: одна запись журнала.
        var own = probe.Session!.Map.Stacks.Values.First(s => s.Owner == NetTestGame.ClientPlayer);
        int entries = game.Journal!.Entries.Count;
        var request = new CommandRequest("same-1", probe.Seq, probe.Turn!.Token!, new RenameLeaderCommand(own.Id, "Первый"));
        hub.FromClient("peer-3")(request);
        hub.FromClient("peer-3")(request with { ExpectedViewSeq = probe.Seq });
        Assert.Equal(entries + 1, game.Journal.Entries.Count);
        // Другие правила.
        var other = new Ruleset("f" + new string('0', 63), "x", []);
        var stranger = new NetClient(new NetClientOptions { Ruleset = other }, hub.FromClient("peer-4"),
                                     view => new GameSession(view, MapArtData.Empty, NetTestGame.Book));
        hub.Attach("peer-4", stranger);
        stranger.Connect();
        Assert.Equal(NetErrors.RulesetMismatch, stranger.Refusal?.Code);
        Assert.Contains("база содержимого", stranger.Refusal!.Text, StringComparison.Ordinal);
    }

    /// <summary>Таймер хода: срок клиента вышел (с запасом), а конца хода нет — хост заканчивает ход за него, и ход снова у хоста.</summary>
    [Fact]
    public void TurnTimer_EndsAnIdleClientTurn()
    {
        var now = DateTimeOffset.UnixEpoch;
        var game = Network(NetTestGame.Start(), out var hub, out var client, turnSeconds: 60, clock: () => now);
        game.Execute(new EndTurnCommand());
        Assert.True(client.MyTurn);
        Assert.NotNull(client.Turn!.Deadline);
        int day = game.Map.Turn;
        now = now.AddSeconds(65);
        hub.Host!.Pump();
        Assert.True(client.MyTurn);   // запас ещё не вышел
        now = now.AddSeconds(10);
        hub.Host.Pump();
        Assert.Equal(NetTestGame.HostPlayer, game.ActiveHuman);
        Assert.Equal(day + 1, game.Map.Turn);
        Assert.False(client.MyTurn);
        AssertSameView(client, game);
    }

    private static void AssertSameView(NetClient client, GameSession host) =>
        Assert.Equal(ViewPatch.Hash(NetView.Of(host.SnapshotWithoutSummaries(), NetTestGame.ClientPlayer)), client.ViewHash);

    /// <summary>Хост над партией <paramref name="start"/> с журналом и один клиент на месте второго человека.</summary>
    internal static GameSession Network(GameState start, out NetHub hub, out NetClient client, int? turnSeconds = null,
                                        Func<DateTimeOffset>? clock = null)
    {
        var game = new GameSession(NetTestGame.Copy(start), MapArtData.Empty, NetTestGame.Book);
        game.Journal = CommandJournal.Start(game);
        hub = new NetHub();
        var h = hub;
        var host = new NetHost(game, new NetHostOptions
        {
            Room = "TEST", Ruleset = NetTestGame.Ruleset, TurnSeconds = turnSeconds, Clock = clock ?? (() => DateTimeOffset.UtcNow),
        }, h.ToClient);
        hub.Host = host;
        host.AddSeat(NetTestGame.ClientPlayer);
        client = new NetClient(new NetClientOptions { Ruleset = NetTestGame.Ruleset, Name = "друг" }, h.FromClient("peer-1"),
                               view => new GameSession(view, MapArtData.Empty, NetTestGame.Book));
        hub.Attach("peer-1", client);
        client.Connect();
        Assert.Equal(NetTestGame.ClientPlayer, client.Player);
        Assert.NotNull(client.Session);
        return game;
    }
}
