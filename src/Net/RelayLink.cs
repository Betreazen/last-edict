using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace LastEdict.Net;

/// <summary>Место в комнате посредника: код, участник и ключ для возвращения (<c>docs/network.md</c> §6). Хранит игра
/// (<c>user://net-rooms.json</c>): по нему меню находит свои асинхронные комнаты и возвращает в партию.</summary>
public sealed record RoomKey(string Room, string Peer, string Key, bool Async = false, string Name = "", string Seat = "");

/// <summary>Ошибка посредника (<c>op: error</c>): код и текст.</summary>
public sealed class RelayException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Соединение с посредником (<c>tools/Relay</c>): WebSocket, кадры JSON с полем <c>op</c>. Сообщения игры (<see cref="NetMessage"/>) идут в
/// поле <c>data</c> строкой; длинные (снимок вида, партия асинхронной комнаты) — сжатыми Brotli в base64 с приставкой <c>z:</c>. Обрыв —
/// переподключение с возвращением на своё место по ключу (<see cref="Reconnected"/>), пока не <see cref="DisposeAsync"/>.
/// </summary>
public sealed class RelayLink : IAsyncDisposable
{
    private const int CompressFrom = 2048;
    private readonly Uri _url;
    private readonly Channel<JsonObject> _replies = Channel.CreateUnbounded<JsonObject>();
    private Channel<byte[]> _out = NewOutbox();
    private readonly CancellationTokenSource _stop = new();
    private ClientWebSocket? _socket;
    private Task? _reader;
    private bool _reconnecting;

    public RelayLink(Uri url) => _url = url ?? throw new ArgumentNullException(nameof(url));

    /// <summary>Своё место в комнате (после <see cref="CreateAsync"/>, <see cref="JoinAsync"/>, <see cref="RejoinAsync"/>).</summary>
    public RoomKey? Key { get; private set; }

    /// <summary>Участник-хост комнаты.</summary>
    public string Host { get; private set; } = "h";

    /// <summary>Участники комнаты при входе (номер, имя, место, на связи).</summary>
    public JsonArray? Peers { get; private set; }

    /// <summary>Переподключаться после обрыва (игра — да; проверка обрыва — нет).</summary>
    public bool AutoReconnect { get; set; } = true;

    public long BytesSent { get; private set; }

    public long BytesReceived { get; private set; }

    /// <summary>Сообщение игры от участника.</summary>
    public event Action<string, NetMessage>? Message;

    /// <summary>Кадр посредника не ответ на запрос: <c>peer</c> (вошёл или вернулся), <c>gone</c> (ушёл), <c>yourTurn</c>.</summary>
    public event Action<JsonObject>? Notice;

    /// <summary>Связь с посредником потеряна (дальше — переподключение, если <see cref="AutoReconnect"/>).</summary>
    public event Action? Lost;

    /// <summary>Связь вернулась, место — прежнее.</summary>
    public event Action? Reconnected;

    public event Action<string>? Log;

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(_url, cancel).ConfigureAwait(false);
        _socket = socket;
        _out = NewOutbox();
        var outbox = _out;
        _ = Task.Run(() => SendLoop(socket, outbox), CancellationToken.None);
        _reader = Task.Run(() => ReadLoop(socket), CancellationToken.None);
    }

    // ---- комнаты

    public async Task<RoomKey> CreateAsync(string name, string nick, bool async, string seat = "", CancellationToken cancel = default)
    {
        var reply = await Request(new JsonObject { ["op"] = "create", ["name"] = name, ["nick"] = nick, ["async"] = async, ["seat"] = seat },
                                  "created", cancel).ConfigureAwait(false);
        Host = "h";
        Key = new RoomKey(reply["room"]!.GetValue<string>(), reply["peer"]!.GetValue<string>(), reply["key"]!.GetValue<string>(), async, name, seat);
        return Key;
    }

    public async Task<RoomKey> JoinAsync(string room, string nick, string seat = "", CancellationToken cancel = default)
    {
        var reply = await Request(new JsonObject { ["op"] = "join", ["room"] = room, ["nick"] = nick, ["seat"] = seat }, "joined", cancel)
                              .ConfigureAwait(false);
        return Joined(reply, reply["key"]!.GetValue<string>());
    }

    public async Task<RoomKey> RejoinAsync(RoomKey key, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var reply = await Request(new JsonObject { ["op"] = "rejoin", ["room"] = key.Room, ["peer"] = key.Peer, ["key"] = key.Key, ["seat"] = key.Seat },
                                  "joined", cancel).ConfigureAwait(false);
        return Joined(reply, key.Key);
    }

    private RoomKey Joined(JsonObject reply, string key)
    {
        Host = reply["host"]?.GetValue<string>() ?? "h";
        Peers = reply["peers"]?.DeepClone() as JsonArray;
        Key = new RoomKey(reply["room"]!.GetValue<string>(), reply["peer"]!.GetValue<string>(), key, reply["async"]?.GetValue<bool>() ?? false,
                          reply["name"]?.GetValue<string>() ?? "", reply["seat"]?.GetValue<string>() ?? "");
        return Key;
    }

    /// <summary>Мои комнаты по ключам (без входа): асинхронные — чей ход, день.</summary>
    public async Task<JsonArray> RoomsAsync(IEnumerable<RoomKey> keys, CancellationToken cancel = default)
    {
        var list = new JsonArray([.. keys.Select(k => (JsonNode)new JsonObject { ["room"] = k.Room, ["peer"] = k.Peer, ["key"] = k.Key })]);
        var reply = await Request(new JsonObject { ["op"] = "rooms", ["keys"] = list }, "rooms", cancel).ConfigureAwait(false);
        return reply["list"] as JsonArray ?? [];
    }

    // ---- асинхронный режим

    /// <summary>Записать партию асинхронной комнаты: ревизия <paramref name="rev"/> (на 1 больше прежней), чей ход дальше, журнал хода.</summary>
    public async Task StoreAsync(long rev, string next, int day, string hash, string state, string journal, CancellationToken cancel = default) =>
        await Request(new JsonObject
        {
            ["op"] = "store", ["rev"] = rev, ["next"] = next, ["day"] = day, ["hash"] = hash, ["state"] = Pack(state), ["journal"] = journal,
        }, "stored", cancel).ConfigureAwait(false);

    /// <summary>Партия асинхронной комнаты: ревизия, чей ход, день, хеш и сохранение (null — ещё не записана).</summary>
    public async Task<(long Rev, string Next, int Day, string Hash, string? State)> LoadAsync(CancellationToken cancel = default)
    {
        var reply = await Request(new JsonObject { ["op"] = "load" }, "state", cancel).ConfigureAwait(false);
        string? state = reply["state"]?.GetValue<string>();
        return (reply["rev"]?.GetValue<long>() ?? 0, reply["next"]?.GetValue<string>() ?? "", reply["day"]?.GetValue<int>() ?? 0,
                reply["hash"]?.GetValue<string>() ?? "", state is null ? null : Unpack(state));
    }

    // ---- сообщения игры

    /// <summary>Сообщение игры участнику <paramref name="to"/> (<c>h</c> — хосту, <c>*</c> — всем).</summary>
    public Task SendAsync(string to, NetMessage message) =>
        Send(new JsonObject { ["op"] = "send", ["to"] = to, ["data"] = Pack(NetJson.Write(message)) });

    /// <summary>То же без ожидания (с потока правил хоста): кадры уходят строго по порядку; без связи — пропадают (вид восстановит
    /// возвращение), исключения наружу не идут.</summary>
    public void Post(string to, NetMessage message)
    {
        try
        {
            Enqueue(new JsonObject { ["op"] = "send", ["to"] = to, ["data"] = Pack(NetJson.Write(message)) });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log?.Invoke($"relay: {message.GetType().Name} не отправлено: {e.Message}");
        }
    }

    private static string Pack(string text)
    {
        if (text.Length < CompressFrom) return text;
        using var buffer = new MemoryStream();
        using (var brotli = new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true)) brotli.Write(Encoding.UTF8.GetBytes(text));
        return "z:" + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Больше этого сообщение после распаковки не бывает (снимок вида 1024² — десятки МБ): сжатая «бомба» дальше не читается.</summary>
    internal const int MaxUnpacked = 96 << 20;

    private static string Unpack(string data)
    {
        if (!data.StartsWith("z:", StringComparison.Ordinal)) return data;
        using var input = new MemoryStream(Convert.FromBase64String(data[2..]));
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxUnpacked) throw new InvalidDataException("сообщение после распаковки слишком большое");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    // ---- провод

    private async Task<JsonObject> Request(JsonObject frame, string expect, CancellationToken cancel)
    {
        while (_replies.Reader.TryRead(out _)) { }   // старые ответы — не к этому запросу
        await Send(frame).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel, _stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var reply = await _replies.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            string op = reply["op"]?.GetValue<string>() ?? "";
            if (op == expect) return reply;
            if (op == "error") throw new RelayException(reply["code"]?.GetValue<string>() ?? "", reply["text"]?.GetValue<string>() ?? "");
        }
    }

    /// <summary>Кадр — в очередь отправки соединения (один писатель: порядок кадров — порядок вызовов); нет связи — исключение.</summary>
    private Task Send(JsonObject frame)
    {
        if (_socket is not { State: WebSocketState.Open }) throw new RelayException("offline", "нет связи с посредником");
        Enqueue(frame);
        return Task.CompletedTask;
    }

    /// <summary>Кадр — в очередь; нет связи — пропадает.</summary>
    private void Enqueue(JsonObject frame)
    {
        if (_socket is { State: WebSocketState.Open }) _out.Writer.TryWrite(Encoding.UTF8.GetBytes(frame.ToJsonString()));
    }

    private static Channel<byte[]> NewOutbox() => Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    private async Task SendLoop(ClientWebSocket socket, Channel<byte[]> outbox)
    {
        try
        {
            await foreach (byte[] bytes in outbox.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false);
                BytesSent += bytes.Length;
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // Соединение ушло — читатель заметит обрыв.
        }
    }

    private async Task ReadLoop(ClientWebSocket socket)
    {
        var buffer = new byte[64 << 10];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                BytesReceived += message.Length;
                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                try
                {
                    Dispatch(text);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Сбой обработчика (игра, сессия) не глушит соединение: кадр пропущен, следующие читаются.
                    Log?.Invoke($"relay: кадр не обработан: {e.GetType().Name}: {e.Message}");
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Обрыв — ниже.
        }
        _out.Writer.TryComplete();   // неотправленное пропадает: после возвращения вид придёт заново
        if (_stop.IsCancellationRequested || !ReferenceEquals(socket, _socket)) return;
        Lost?.Invoke();
        if (AutoReconnect) _ = Task.Run(Reconnect);
    }

    private void Dispatch(string text)
    {
        JsonObject frame;
        try
        {
            frame = JsonNode.Parse(text)!.AsObject();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            Log?.Invoke("relay: кадр не читается");
            return;
        }
        switch (frame["op"]?.GetValue<string>())
        {
            case "msg":
                NetMessage message;
                try
                {
                    message = NetJson.Read(Unpack(frame["data"]?.GetValue<string>() ?? ""));
                }
                catch (Exception e) when (e is InvalidDataException or FormatException or InvalidOperationException or JsonException)
                {
                    Log?.Invoke($"relay: сообщение от {frame["from"]} не читается: {e.Message}");
                    return;
                }
                Message?.Invoke(frame["from"]?.GetValue<string>() ?? "", message);
                break;
            case "peer" or "gone" or "yourTurn":
                Notice?.Invoke(frame);
                break;
            default:
                _replies.Writer.TryWrite(frame);
                break;
        }
    }

    /// <summary>Переподключение со своим местом: 1, 2, 4… до 15 с между попытками.</summary>
    private async Task Reconnect()
    {
        if (_reconnecting || Key is not { } key) return;
        _reconnecting = true;
        try
        {
            var delay = TimeSpan.FromSeconds(1);
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
                    await ConnectAsync(_stop.Token).ConfigureAwait(false);
                    await RejoinAsync(key, _stop.Token).ConfigureAwait(false);
                    Log?.Invoke($"relay: связь вернулась, комната {key.Room}");
                    Reconnected?.Invoke();
                    return;
                }
                catch (Exception e) when (e is WebSocketException or RelayException or OperationCanceledException or HttpRequestException)
                {
                    if (e is RelayException { Code: "key" or "room" }) return;   // места больше нет — возвращаться некуда
                    delay = TimeSpan.FromSeconds(Math.Min(15, delay.TotalSeconds * 2));
                }
            }
        }
        finally
        {
            _reconnecting = false;
        }
    }

    /// <summary>Оборвать соединение (проверка обрыва): без закрытия по протоколу, как пропавшая сеть.</summary>
    public void Abort() => _socket?.Abort();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_socket is { State: WebSocketState.Open } socket)
        {
            try
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", close.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException)
            {
                // Уже закрыто.
            }
        }
        _socket?.Dispose();
        if (_reader is not null) await _reader.ConfigureAwait(false);
        _stop.Dispose();
    }
}

/// <summary>Хост комнаты поверх посредника: сообщения участников — в <see cref="NetHost.Receive"/>, ответы хоста — им; ушёл участник — его
/// место ждёт.</summary>
public static class NetRelay
{
    /// <summary>Связать хоста с соединением; вызвать до первого <see cref="NetHost.Pump"/>.</summary>
    public static void Attach(NetHost host, RelayLink link)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(link);
        link.Message += host.Receive;
        link.Notice += notice =>
        {
            if (notice["op"]?.GetValue<string>() == "gone" && notice["peer"]?.GetValue<string>() is { } peer) host.Disconnected(peer);
        };
    }

    /// <summary>Отправка хоста через соединение.</summary>
    public static Action<string, NetMessage> HostSender(RelayLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return link.Post;
    }

    /// <summary>Клиент поверх посредника: его сообщения — хосту; обрыв — отказ ждущим командам; возвращение (своё или хоста) — снова
    /// <see cref="NetClient.Connect"/> с последним видом.</summary>
    public static void Attach(NetClient client, RelayLink link)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(link);
        link.Message += (from, message) =>
        {
            if (from == link.Host) client.Receive(message);
        };
        link.Lost += client.Disconnected;
        link.Reconnected += client.Connect;
        link.Notice += notice =>
        {
            bool host = notice["host"]?.GetValue<bool>() ?? false;
            switch (notice["op"]?.GetValue<string>())
            {
                case "gone" when host: client.Disconnected(); break;
                case "peer" when host: client.Connect(); break;
            }
        };
    }

    public static Action<NetMessage> ClientSender(RelayLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return message => link.Post(link.Host, message);
    }
}
