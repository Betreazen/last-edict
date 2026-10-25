using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace LastEdict.Relay;

/// <summary>Настройки посредника (переменные окружения — <see cref="FromEnvironment"/>; секретов нет).</summary>
public sealed class RelayOptions
{
    /// <summary>Каталог комнат асинхронного режима (партия, журнал, очередь ходов); переживает перезапуск контейнера.</summary>
    public string DataDir { get; init; } = "relay-data";

    public int MaxRooms { get; init; } = 500;

    /// <summary>Наибольшее сообщение (сжатое сообщение игры, сохранение асинхронной партии).</summary>
    public int MaxMessageBytes { get; init; } = 16 << 20;

    /// <summary>Сколько держать пустую обычную комнату (все ушли) — на случай возвращения.</summary>
    public TimeSpan EmptyRoomTtl { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Сколько держать асинхронную комнату без ходов.</summary>
    public TimeSpan AsyncRoomTtl { get; init; } = TimeSpan.FromDays(60);

    /// <summary>Сколько входов и созданий комнат в минуту с одного адреса: код не перебрать, комнаты не забить.</summary>
    public int JoinsPerMinute { get; init; } = 30;

    /// <summary>Участников в комнате не больше.</summary>
    public int MaxMembers { get; init; } = 16;

    /// <summary>Кадров в секунду от соединения (среднее; запас — <see cref="FrameBurst"/>).</summary>
    public int FramesPerSecond { get; init; } = 100;

    public int FrameBurst { get; init; } = 400;

    /// <summary>Сколько байт может ждать отправки одному соединению: медленный получатель закрывается, а не копит память.</summary>
    public long MaxQueuedBytes { get; init; } = 96L << 20;

    /// <summary>Журнал асинхронной комнаты на диске не больше (дальше ходы пишутся без журнала).</summary>
    public long MaxJournalBytes { get; init; } = 64L << 20;

    public static RelayOptions FromEnvironment() => new()
    {
        DataDir = Environment.GetEnvironmentVariable("RELAY_DATA") is { Length: > 0 } dir ? dir : "relay-data",
        MaxRooms = int.TryParse(Environment.GetEnvironmentVariable("RELAY_MAX_ROOMS"), out int rooms) ? rooms : 500,
    };
}

/// <summary>
/// Комнаты посредника (<c>docs/network.md</c> §6). Соединение — WebSocket, кадры — JSON с полем <c>op</c>: <c>create</c>, <c>join</c>,
/// <c>rejoin</c>, <c>send</c>, <c>store</c>, <c>load</c>, <c>rooms</c>, <c>ping</c>; посредник отвечает <c>created</c>, <c>joined</c>, <c>msg</c>,
/// <c>peer</c>, <c>gone</c>, <c>stored</c>, <c>state</c>, <c>yourTurn</c>, <c>rooms</c>, <c>pong</c>, <c>error</c>. Поле <c>data</c> сообщения
/// игры посредник не читает. Участник комнаты — <c>peer</c> (<c>h</c> — создатель, дальше <c>p2</c>, <c>p3</c>…) с ключом для
/// возвращения (хранится только хеш ключа).
/// </summary>
public sealed class RelayHub
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Minute, int Count)> _joins = new(StringComparer.Ordinal);
    private readonly RelayOptions _options;
    private readonly ILogger _log;
    private readonly RoomStore _store;

    public RelayHub(RelayOptions options, ILogger<RelayHub> log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _store = new RoomStore(options.DataDir);
        foreach (var room in _store.LoadAll()) _rooms[room.Code] = room;
    }

    public int Rooms => _rooms.Count;

    public int Connections => _rooms.Values.Sum(r => r.Online);

    /// <summary>Вести соединение до закрытия.</summary>
    public async Task Serve(WebSocket socket, string address, CancellationToken stop)
    {
        var connection = new Connection(socket, address, _options.MaxQueuedBytes);
        var sender = connection.SendLoop(stop);
        try
        {
            while (!stop.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string? text = await Receive(socket, stop).ConfigureAwait(false);
                if (text is null) break;
                JsonObject? frame;
                try { frame = JsonNode.Parse(text) as JsonObject; }
                catch (JsonException) { frame = null; }
                if (frame is null)
                {
                    connection.Send(Error("bad", "кадр — не объект JSON"));
                    continue;
                }
                if (!connection.Admit(_options.FramesPerSecond, _options.FrameBurst))
                {
                    connection.Send(Error("rate", "слишком много сообщений — кадр пропущен"));
                    continue;
                }
                try
                {
                    Handle(connection, frame);
                }
                catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException or ArgumentException)
                {
                    connection.Send(Error("bad", "поле кадра не того вида"));
                }
            }
        }
        catch (WebSocketException)
        {
            // Соединение оборвалось — ниже уход участника.
        }
        catch (OperationCanceledException)
        {
            // Остановка сервера.
        }
        finally
        {
            Leave(connection);
            connection.Complete();
            await sender.ConfigureAwait(false);
        }
    }

    private async Task<string?> Receive(WebSocket socket, CancellationToken stop)
    {
        var buffer = new byte[64 << 10];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, stop).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
            if (message.Length > _options.MaxMessageBytes)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "слишком большое сообщение", stop).ConfigureAwait(false);
                return null;
            }
            if (result.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    private void Handle(Connection connection, JsonObject frame)
    {
        string op = frame["op"]?.GetValue<string>() ?? "";
        switch (op)
        {
            case "ping": connection.Send(new JsonObject { ["op"] = "pong" }); break;
            case "create": Create(connection, frame); break;
            case "join": Join(connection, frame); break;
            case "rejoin": Rejoin(connection, frame); break;
            case "send": Forward(connection, frame); break;
            case "store": Store(connection, frame); break;
            case "load": Load(connection); break;
            case "rooms": List(connection, frame); break;
            default: connection.Send(Error("op", $"неизвестная операция «{op}»")); break;
        }
    }

    // ---- комнаты

    private void Create(Connection connection, JsonObject frame)
    {
        if (connection.Room is not null)
        {
            connection.Send(Error("busy", "соединение уже в комнате"));
            return;
        }
        if (!Allowed(connection)) return;
        Sweep();
        if (_rooms.Count >= _options.MaxRooms)
        {
            connection.Send(Error("full", "на посреднике нет места для новой комнаты"));
            return;
        }
        string code;
        do code = NewCode(); while (_rooms.ContainsKey(code));
        var room = new Room(code, Text(frame, "name", 64), frame["async"]?.GetValue<bool>() ?? false);
        string key = NewKey();
        var member = room.Add("h", Text(frame, "nick", 32), Text(frame, "seat", 32), Hash(key));
        room.Host = "h";
        _rooms[code] = room;
        Attach(connection, room, member);
        if (room.Async) _store.Save(room);
        Logs.Created(_log, code, room.Async);
        connection.Send(new JsonObject { ["op"] = "created", ["room"] = code, ["peer"] = member.Peer, ["key"] = key, ["async"] = room.Async });
    }

    private void Join(Connection connection, JsonObject frame)
    {
        if (!Allowed(connection)) return;
        if (Find(connection, frame) is not { } room) return;
        string key = NewKey(), seat = Text(frame, "seat", 32);
        Member member;
        lock (room)
        {
            if (room.Members.Count >= _options.MaxMembers)
            {
                connection.Send(Error("full", "в комнате нет места"));
                return;
            }
            if (seat.Length > 0 && room.Members.Any(m => m.Seat == seat))
            {
                connection.Send(Error("seat", "это место уже занято — войдите своим ключом"));
                return;
            }
            member = room.Add($"p{room.Members.Count + 1}", Text(frame, "nick", 32), seat, Hash(key));
            if (room.Async) _store.Save(room);
        }
        Attach(connection, room, member);
        connection.Send(Joined(room, member, key));
        room.Send(room.Host, new JsonObject { ["op"] = "peer", ["peer"] = member.Peer, ["nick"] = member.Nick, ["seat"] = member.Seat });
        NotifyTurn(room, member);
    }

    private void Rejoin(Connection connection, JsonObject frame)
    {
        if (!Allowed(connection)) return;
        if (Find(connection, frame) is not { } room) return;
        string peer = Text(frame, "peer", 16), key = Text(frame, "key", 128);
        Member? member;
        lock (room) member = room.Members.FirstOrDefault(m => m.Peer == peer && m.KeyHash == Hash(key));
        if (member is null)
        {
            connection.Send(Error("key", "место в комнате по этому ключу не найдено"));
            return;
        }
        member.Connection?.Close();   // прежнее соединение того же места
        Attach(connection, room, member);
        connection.Send(Joined(room, member, null));
        if (member.Peer == room.Host) room.Broadcast(member.Peer, new JsonObject { ["op"] = "peer", ["peer"] = member.Peer, ["back"] = true, ["host"] = true });
        else room.Send(room.Host, new JsonObject { ["op"] = "peer", ["peer"] = member.Peer, ["nick"] = member.Nick, ["seat"] = member.Seat, ["back"] = true });
        NotifyTurn(room, member);
    }

    private static JsonObject Joined(Room room, Member member, string? key)
    {
        lock (room) return JoinedFrame(room, member, key);
    }

    private static JsonObject JoinedFrame(Room room, Member member, string? key)
    {
        var joined = new JsonObject
        {
            ["op"] = "joined", ["room"] = room.Code, ["peer"] = member.Peer, ["host"] = room.Host, ["async"] = room.Async, ["name"] = room.Name,
            ["seat"] = member.Seat,
            ["peers"] = new JsonArray([.. room.Members.Select(m => (JsonNode)new JsonObject
            {
                ["peer"] = m.Peer, ["nick"] = m.Nick, ["seat"] = m.Seat, ["online"] = m.Connection is not null,
            })]),
        };
        if (key is not null) joined["key"] = key;
        return joined;
    }

    private Room? Find(Connection connection, JsonObject frame)
    {
        if (connection.Room is not null)
        {
            connection.Send(Error("busy", "соединение уже в комнате"));
            return null;
        }
        string code = Text(frame, "room", 16).ToUpperInvariant();
        if (_rooms.TryGetValue(code, out var room)) return room;
        connection.Send(Error("room", $"комнаты {code} нет"));
        return null;
    }

    /// <summary>Входов с адреса не больше <see cref="RelayOptions.JoinsPerMinute"/> в минуту.</summary>
    private bool Allowed(Connection connection)
    {
        var minute = DateTimeOffset.UtcNow;
        minute = new DateTimeOffset(minute.Year, minute.Month, minute.Day, minute.Hour, minute.Minute, 0, TimeSpan.Zero);
        var now = _joins.AddOrUpdate(connection.Address, _ => (minute, 1), (_, old) => old.Minute == minute ? (minute, old.Count + 1) : (minute, 1));
        if (now.Count <= _options.JoinsPerMinute) return true;
        connection.Send(Error("rate", "слишком много попыток входа — подождите минуту"));
        return false;
    }

    private static void Attach(Connection connection, Room room, Member member)
    {
        lock (room)
        {
            member.Connection = connection;
            room.Touched = DateTimeOffset.UtcNow;
        }
        connection.Room = room;
        connection.Member = member;
    }

    private void Leave(Connection connection)
    {
        if (connection.Room is not { } room || connection.Member is not { } member) return;
        bool gone;
        lock (room)
        {
            gone = ReferenceEquals(member.Connection, connection);
            if (gone) member.Connection = null;
            room.Touched = DateTimeOffset.UtcNow;
        }
        if (!gone) return;
        if (member.Peer == room.Host) room.Broadcast(member.Peer, new JsonObject { ["op"] = "gone", ["peer"] = member.Peer, ["host"] = true });
        else room.Send(room.Host, new JsonObject { ["op"] = "gone", ["peer"] = member.Peer });
    }

    // ---- пересылка

    private static void Forward(Connection connection, JsonObject frame)
    {
        if (connection.Room is not { } room || connection.Member is not { } from)
        {
            connection.Send(Error("room", "сначала войдите в комнату"));
            return;
        }
        string to = Text(frame, "to", 16);
        var message = new JsonObject { ["op"] = "msg", ["from"] = from.Peer, ["data"] = frame["data"]?.DeepClone() };
        if (to == "*") room.Broadcast(from.Peer, message);
        else room.Send(to, message);   // нет на связи — пропадает: его уход хост уже знает (gone)
    }

    // ---- асинхронный режим

    /// <summary>Ход сделан: партия (сохранение целиком), журнал хода и чей ход дальше. Писать может тот, чей ход был (или создатель до
    /// первого хода), и только следующей ревизией — двое не затрут друг друга.</summary>
    private void Store(Connection connection, JsonObject frame)
    {
        if (connection.Room is not { Async: true } room || connection.Member is not { } member)
        {
            connection.Send(Error("async", "хранить партию можно только в асинхронной комнате"));
            return;
        }
        long rev = frame["rev"]?.GetValue<long>() ?? -1;
        int day = frame["day"]?.GetValue<int>() ?? 0;
        string next = Text(frame, "next", 32), hash = Text(frame, "hash", 128), state = frame["state"]?.GetValue<string>() ?? "";
        string? journal = frame["journal"]?.GetValue<string>();
        lock (room)
        {
            bool mayWrite = room.Rev == 0 ? member.Peer == room.Host : member.Seat.Length > 0 && member.Seat == room.Next;
            if (!mayWrite)
            {
                connection.Send(Error("turn", "сейчас ход не ваш — партию пишет тот, чей ход"));
                return;
            }
            if (rev != room.Rev + 1)
            {
                connection.Send(Error("conflict", $"ревизия {rev}, ожидалась {room.Rev + 1}").With("rev", room.Rev));
                return;
            }
            if (state.Length == 0)
            {
                connection.Send(Error("bad", "нет партии"));
                return;
            }
            // Сначала диск, потом подтверждение: сбой записи возвращает комнату к прежней ревизии.
            var (oldRev, oldNext, oldDay, oldHash) = (room.Rev, room.Next, room.Day, room.Hash);
            (room.Rev, room.Next, room.Day, room.Hash, room.Touched) = (rev, next, day, hash, DateTimeOffset.UtcNow);
            try
            {
                _store.Save(room, state, _store.JournalBytes(room) < _options.MaxJournalBytes ? journal : null);
            }
            catch (IOException)
            {
                (room.Rev, room.Next, room.Day, room.Hash) = (oldRev, oldNext, oldDay, oldHash);
                connection.Send(Error("storage", "партия не записана — попробуйте ещё раз"));
                return;
            }
        }
        connection.Send(new JsonObject { ["op"] = "stored", ["rev"] = rev });
        Logs.Stored(_log, room.Code, rev, next);
        List<Member> nextUp;
        lock (room) nextUp = [.. room.Members.Where(m => m.Seat == next)];
        foreach (var other in nextUp) NotifyTurn(room, other);
    }

    private void Load(Connection connection)
    {
        if (connection.Room is not { Async: true } room)
        {
            connection.Send(Error("async", "партия хранится только у асинхронной комнаты"));
            return;
        }
        JsonObject reply;
        lock (room)
            reply = new JsonObject
            {
                ["op"] = "state", ["rev"] = room.Rev, ["next"] = room.Next, ["day"] = room.Day, ["hash"] = room.Hash,
                ["state"] = room.Rev > 0 ? _store.ReadState(room) : null,
            };
        connection.Send(reply);
    }

    /// <summary>Ход участника <paramref name="member"/> — сказать ему, если он на связи.</summary>
    private static void NotifyTurn(Room room, Member member)
    {
        if (!room.Async || room.Rev == 0 || member.Seat.Length == 0 || member.Seat != room.Next) return;
        member.Connection?.Send(new JsonObject { ["op"] = "yourTurn", ["room"] = room.Code, ["day"] = room.Day, ["rev"] = room.Rev });
    }

    /// <summary>Мои комнаты (по ключам мест) — без входа: меню показывает, где мой ход.</summary>
    private void List(Connection connection, JsonObject frame)
    {
        var list = new JsonArray();
        foreach (var item in (frame["keys"] as JsonArray ?? []).OfType<JsonObject>().Take(64))
        {
            if (!_rooms.TryGetValue(Text(item, "room", 16).ToUpperInvariant(), out var room)) continue;
            string peer = Text(item, "peer", 16), hash = Hash(Text(item, "key", 128));
            lock (room)
                if (room.Members.FirstOrDefault(m => m.Peer == peer && m.KeyHash == hash) is { } member)
                    list.Add(new JsonObject
                    {
                        ["room"] = room.Code, ["name"] = room.Name, ["async"] = room.Async, ["day"] = room.Day, ["next"] = room.Next,
                        ["yourTurn"] = room.Async && room.Rev > 0 && member.Seat.Length > 0 && member.Seat == room.Next,
                        ["hostOnline"] = room.Members.FirstOrDefault(m => m.Peer == room.Host)?.Connection is not null,
                    });
        }
        connection.Send(new JsonObject { ["op"] = "rooms", ["list"] = list });
    }

    // ---- уборка

    /// <summary>Убрать пустые обычные комнаты старше срока и заброшенные асинхронные.</summary>
    public void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (address, joins) in _joins)
            if (now - joins.Minute > TimeSpan.FromMinutes(2)) _joins.TryRemove(address, out _);
        foreach (var room in _rooms.Values)
        {
            bool old;
            lock (room) old = room.Online == 0 && now - room.Touched > (room.Async ? _options.AsyncRoomTtl : _options.EmptyRoomTtl);
            if (!old || !_rooms.TryRemove(room.Code, out _)) continue;
            if (room.Async) _store.Delete(room);
            Logs.Removed(_log, room.Code);
        }
    }

    private static JsonObject Error(string code, string text) => new() { ["op"] = "error", ["code"] = code, ["text"] = text };

    private static string Text(JsonObject frame, string name, int max)
    {
        string text = frame[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        return text.Length > max ? text[..max] : text;
    }

    private static string NewCode() => string.Create(CodeLength, 0, (span, _) =>
    {
        for (int i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    });

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    internal static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}

internal static partial class Logs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "room {Room} created async={Async}")]
    public static partial void Created(ILogger log, string room, bool async);

    [LoggerMessage(Level = LogLevel.Information, Message = "room {Room} rev {Rev} next {Next}")]
    public static partial void Stored(ILogger log, string room, long rev, string next);

    [LoggerMessage(Level = LogLevel.Information, Message = "room {Room} removed")]
    public static partial void Removed(ILogger log, string room);
}

internal static class JsonObjectExtensions
{
    public static JsonObject With(this JsonObject obj, string name, JsonNode? value)
    {
        obj[name] = value;
        return obj;
    }
}

/// <summary>Участник комнаты: номер, имя, место в партии (игрок — для очереди асинхронных ходов), хеш ключа и соединение, если на связи.</summary>
internal sealed class Member(string peer, string nick, string seat, string keyHash)
{
    public string Peer { get; } = peer;
    public string Nick { get; } = nick;
    public string Seat { get; set; } = seat;
    public string KeyHash { get; } = keyHash;
    public Connection? Connection { get; set; }
}

internal sealed class Room(string code, string name, bool async)
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public bool Async { get; } = async;
    public string Host { get; set; } = "h";
    public List<Member> Members { get; } = [];
    public DateTimeOffset Touched { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Асинхронная партия: ревизия (число записанных ходов), чей ход, день, хеш состояния.</summary>
    public long Rev { get; set; }
    public string Next { get; set; } = "";
    public int Day { get; set; }
    public string Hash { get; set; } = "";

    public int Online
    {
        get
        {
            lock (this) return Members.Count(m => m.Connection is not null);
        }
    }

    public Member Add(string peer, string nick, string seat, string keyHash)
    {
        var member = new Member(peer, nick, seat, keyHash);
        Members.Add(member);
        return member;
    }

    public bool Send(string peer, JsonObject message)
    {
        Connection? to;
        lock (this) to = Members.FirstOrDefault(m => m.Peer == peer)?.Connection;
        to?.Send(message);
        return to is not null;
    }

    public void Broadcast(string from, JsonObject message)
    {
        List<Connection> to;
        lock (this) to = [.. Members.Where(m => m.Peer != from && m.Connection is not null).Select(m => m.Connection!)];
        foreach (var c in to) c.Send(message);
    }
}

/// <summary>Соединение: очередь отправки (кадры уходят по одному — WebSocket не допускает двух отправок сразу).</summary>
internal sealed class Connection(WebSocket socket, string address, long maxQueued)
{
    private readonly Channel<string> _out = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private long _queued;
    private double _tokens = double.NaN;
    private long _stamp;

    public string Address { get; } = address;
    public Room? Room { get; set; }
    public Member? Member { get; set; }

    /// <summary>В очередь отправки; ждёт больше <c>maxQueued</c> байт — получатель не читает: соединение закрывается.</summary>
    public void Send(JsonObject message)
    {
        string text = message.ToJsonString();
        if (Interlocked.Add(ref _queued, text.Length) > maxQueued)
        {
            Close();
            return;
        }
        if (!_out.Writer.TryWrite(text)) Interlocked.Add(ref _queued, -text.Length);
    }

    /// <summary>Ведро кадров: <paramref name="perSecond"/> в секунду, запас <paramref name="burst"/>.</summary>
    public bool Admit(int perSecond, int burst)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        _tokens = double.IsNaN(_tokens) ? burst
            : Math.Min(burst, _tokens + (now - _stamp) * perSecond / (double)System.Diagnostics.Stopwatch.Frequency);
        _stamp = now;
        if (_tokens < 1) return false;
        _tokens--;
        return true;
    }

    public void Complete() => _out.Writer.TryComplete();

    /// <summary>Закрыть прежнее соединение места, когда участник вернулся с другого.</summary>
    public void Close()
    {
        _out.Writer.TryComplete();
        _ = socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "вход с другого соединения", CancellationToken.None);
    }

    public async Task SendLoop(CancellationToken stop)
    {
        try
        {
            await foreach (string text in _out.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                Interlocked.Add(ref _queued, -text.Length);
                if (socket.State != WebSocketState.Open) break;
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, stop).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Соединение ушло.
        }
    }
}
