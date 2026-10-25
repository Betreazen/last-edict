using System.Text.Json.Nodes;
using LastEdict.Data.Battle;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Net;
using LastEdict.Relay;
using LastEdict.Rules.Battle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LastEdict.Data.Tests;

/// <summary>Посредник (Э7, <c>tools/Relay</c>) в этом же процессе на localhost и игроки без Godot — настоящий WebSocket: комнаты по коду,
/// пересылка, возвращение по ключу, партия хоста и клиента через посредника с обрывом связи, хранение асинхронной партии с перезапуском
/// посредника. Идёт в CI.</summary>
public sealed class RelayTests : IAsyncLifetime
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "le-relay-" + Guid.NewGuid().ToString("N")[..8]);
    private WebApplication? _app;
    private Uri _url = new("ws://127.0.0.1/ws");
    private readonly List<RelayLink> _links = [];

    public async Task InitializeAsync() => await StartRelay();

    private async Task StartRelay()
    {
        _app = RelayServer.Create(["--urls", "http://127.0.0.1:0"], new RelayOptions { DataDir = _data, JoinsPerMinute = 1000 },
                                  b => b.Logging.ClearProviders());
        await _app.StartAsync();
        string http = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _url = new Uri(http.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws");
    }

    private async Task StopRelay()
    {
        await _app!.StopAsync();
        await _app.DisposeAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var link in _links) await link.DisposeAsync();
        await StopRelay();
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }

    private async Task<RelayLink> Link()
    {
        var link = new RelayLink(_url);
        _links.Add(link);
        await link.ConnectAsync();
        return link;
    }

    private static async Task<T> Within<T>(TaskCompletionSource<T> wait, int seconds = 10) =>
        await wait.Task.WaitAsync(TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task Room_ByCode_ForwardsAndTakesBackByKey()
    {
        var host = await Link();
        var key = await host.CreateAsync("Друзья", "хост", async: false);
        Assert.Matches("^[A-Z2-9]{6}$", key.Room);
        var guest = await Link();
        var peerSeen = new TaskCompletionSource<JsonObject>();
        host.Notice += n => { if (n["op"]?.GetValue<string>() == "peer") peerSeen.TrySetResult(n); };
        var joined = await guest.JoinAsync(key.Room.ToLowerInvariant(), "гость");
        Assert.Equal("p2", (await Within(peerSeen))["peer"]!.GetValue<string>());

        var got = new TaskCompletionSource<(string, NetMessage)>();
        host.Message += (from, m) => got.TrySetResult((from, m));
        await guest.SendAsync("h", new Chat("", new string('я', 5000)));   // длинное — сжатым
        var (from, message) = await Within(got);
        Assert.Equal("p2", from);
        Assert.Equal(5000, Assert.IsType<Chat>(message).Text.Length);

        // Гость пропал и вернулся по ключу — тот же участник; чужой ключ и чужая комната — отказ.
        var gone = new TaskCompletionSource<JsonObject>();
        host.Notice += n => { if (n["op"]?.GetValue<string>() == "gone") gone.TrySetResult(n); };
        guest.AutoReconnect = false;
        guest.Abort();
        Assert.Equal("p2", (await Within(gone))["peer"]!.GetValue<string>());
        var back = await Link();
        Assert.Equal("p2", (await back.RejoinAsync(joined)).Peer);
        var thief = await Link();
        Assert.Equal("key", (await Assert.ThrowsAsync<RelayException>(() => thief.RejoinAsync(joined with { Key = "00" }))).Code);
        Assert.Equal("room", (await Assert.ThrowsAsync<RelayException>(() => thief.JoinAsync("ZZZZZZ", "x"))).Code);
        // Место занято — второй с тем же местом не входит (а сам выбрать место при возвращении нельзя).
        var taker = await Link();
        await taker.JoinAsync(key.Room, "a", "S1");
        var twin = await Link();
        Assert.Equal("seat", (await Assert.ThrowsAsync<RelayException>(() => twin.JoinAsync(key.Room, "b", "S1"))).Code);
    }

    /// <summary>Партия хоста и клиента через посредника: ходы людей и ИИ, вид клиента = проекция хоста; клиент теряет связь посреди
    /// своего хода, переподключается сам и получает вид заново; журнал хоста повторяется с тем же хешем.</summary>
    [Fact]
    public async Task Game_OverRelay_SurvivesAConnectionLoss()
    {
        var start = NetTestGame.Start();
        var game = new GameSession(NetTestGame.Copy(start), MapArtData.Empty, NetTestGame.Book);
        game.Journal = CommandJournal.Start(game);
        var hostLink = await Link();
        var key = await hostLink.CreateAsync("Партия", "хост", async: false);
        var host = new NetHost(game, new NetHostOptions { Room = key.Room, Ruleset = NetTestGame.Ruleset }, NetRelay.HostSender(hostLink));
        NetRelay.Attach(host, hostLink);
        host.AddSeat(NetTestGame.ClientPlayer);
        using var pump = new HostPump(host);

        var clientLink = await Link();
        await clientLink.JoinAsync(key.Room, "друг", NetTestGame.ClientPlayer);
        var client = new NetClient(new NetClientOptions { Ruleset = NetTestGame.Ruleset, Name = "друг" }, NetRelay.ClientSender(clientLink),
                                   view => new GameSession(view, MapArtData.Empty, NetTestGame.Book));
        NetRelay.Attach(client, clientLink);
        client.BattleRequested += request => ScriptedHuman.AnswerBattle(client, request);
        var joined = new TaskCompletionSource();
        client.ViewChanged += () => joined.TrySetResult();
        client.Connect();
        await joined.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var random = new Random(4);
        for (int day = 0; day < 4 && !game.Over; day++)
        {
            NetTestGame.HumanTurn(game, NetTestGame.HostPlayer, c => game.Execute(c), random);
            await Until(() => client.MyTurn && SameView(client, game));
            if (day == 2)
            {
                // Обрыв посреди своего хода: команда не проходит, связь возвращается сама, вид — снова тот же.
                var back = new TaskCompletionSource();
                clientLink.Reconnected += () => back.TrySetResult();
                clientLink.Abort();
                await Until(() => !client.Online);
                Assert.Throws<NetCommandException>(() => client.Session!.Execute(new EndTurnCommand()));
                await back.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Until(() => client.Online && client.MyTurn && SameView(client, game));
            }
            NetTestGame.HumanTurn(client.Session!, NetTestGame.ClientPlayer, c => client.Session!.Execute(c), random);
            await Until(() => SameView(client, game));
        }
        Assert.True(clientLink.BytesReceived > 0 && hostLink.BytesSent > 0);

        var replay = new GameSession(NetTestGame.Copy(start), MapArtData.Empty, NetTestGame.Book);
        CommandJournal.FromJsonLines(game.Journal.ToJsonLines()).Replay(replay);
        Assert.Equal(StateHash.Of(game), StateHash.Of(replay));
    }

    private static bool SameView(NetClient client, GameSession host) =>
        client.ViewHash == ViewPatch.Hash(NetView.Of(host.SnapshotWithoutSummaries(), NetTestGame.ClientPlayer));

    private static async Task Until(Func<bool> condition, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("условие не наступило");
            await Task.Delay(10);
        }
    }

    /// <summary>Асинхронная комната: партию пишет тот, чей ход, следующей ревизией; следующему — «ваш ход» (на связи — сразу, иначе — в
    /// списке комнат); партия переживает перезапуск посредника.</summary>
    [Fact]
    public async Task AsyncRoom_StoresTheGame_AndTheTurnQueue()
    {
        var first = await Link();
        var mine = await first.CreateAsync("Долгая", "первый", async: true, seat: "A");
        await first.StoreAsync(1, "B", 1, "hash1", "{\"state\":1}", "{\"line\":1}");
        var second = await Link();
        var turn = new TaskCompletionSource<JsonObject>();
        second.Notice += n => { if (n["op"]?.GetValue<string>() == "yourTurn") turn.TrySetResult(n); };
        var theirs = await second.JoinAsync(mine.Room, "второй", "B");
        Assert.Equal(1, (await Within(turn))["rev"]!.GetValue<long>());
        Assert.Equal("turn", (await Assert.ThrowsAsync<RelayException>(() => first.StoreAsync(2, "A", 1, "h", "x", ""))).Code);
        Assert.Equal("conflict", (await Assert.ThrowsAsync<RelayException>(() => second.StoreAsync(5, "A", 2, "h", "x", ""))).Code);
        string big = "{\"state\":\"" + new string('x', 200_000) + "\"}";
        await second.StoreAsync(2, "A", 2, "hash2", big, "{\"line\":2}");

        // Перезапуск посредника: комната, партия и очередь — с диска.
        foreach (var link in _links) link.AutoReconnect = false;
        await StopRelay();
        await StartRelay();
        var later = await Link();
        var list = await later.RoomsAsync([mine, theirs]);
        Assert.Equal(2, list.Count);
        Assert.True(list.OfType<JsonObject>().Single(r => r["room"]!.GetValue<string>() == mine.Room && r["yourTurn"]!.GetValue<bool>()) is not null);
        await later.RejoinAsync(mine);
        var stored = await later.LoadAsync();
        Assert.Equal((2L, "A", 2, "hash2"), (stored.Rev, stored.Next, stored.Day, stored.Hash));
        Assert.Equal(big, stored.State);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(_data, mine.Room + ".journal.jsonl")).Length);
    }
}
