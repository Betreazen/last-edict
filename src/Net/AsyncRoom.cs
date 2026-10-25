using System.Text.Json.Nodes;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Net;

/// <summary>Партия асинхронной комнаты у посредника: ревизия, чей ход, день, хеш и сама партия (null — ещё не записана).</summary>
public sealed record AsyncState(long Rev, string Next, int Day, string Hash, GameState? State);

/// <summary>
/// Асинхронная партия (Р22, <c>docs/network.md</c> §8): сходил — закрыл игру — друг ходит позже. Партию целиком хранит посредник; тот,
/// чей ход, берёт её (<see cref="LoadAsync"/>, хеш сверяется), ходит у себя — его машина на этот ход хост, ходы ИИ после него идут тоже у
/// него, — и записывает следующей ревизией с журналом своего хода и тем, чей ход дальше (<see cref="StoreAsync"/>). Посредник сообщает
/// следующему «ваш ход» (на связи — сразу, иначе — в списке комнат меню). Партия ходящего — полная (между друзьями допустимо, §3.5
/// roadmap); показ — по его туману.
/// </summary>
public sealed class AsyncRoom : IAsyncDisposable
{
    private AsyncRoom(RelayLink link)
    {
        Link = link;
        link.Notice += notice =>
        {
            if (notice["op"]?.GetValue<string>() == "yourTurn") YourTurn?.Invoke(notice["day"]?.GetValue<int>() ?? 0);
        };
    }

    public RelayLink Link { get; }

    public RoomKey Key => Link.Key ?? throw new InvalidOperationException("комнаты нет");

    /// <summary>Последняя ревизия, которую эта машина прочла или записала.</summary>
    public long Rev { get; private set; }

    /// <summary>Посредник сообщил: ход этой машины (день).</summary>
    public event Action<int>? YourTurn;

    /// <summary>Новая асинхронная комната с партией <paramref name="start"/> (ревизия 1); <paramref name="me"/> — место создателя.</summary>
    public static async Task<AsyncRoom> CreateAsync(Uri relay, GameSession start, string me, string nick, string name = "",
                                                    CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        var link = new RelayLink(relay);
        await link.ConnectAsync(cancel).ConfigureAwait(false);
        await link.CreateAsync(name.Length > 0 ? name : start.State.Info.ScenarioName, nick, async: true, seat: me, cancel).ConfigureAwait(false);
        var room = new AsyncRoom(link);
        await room.StoreAsync(start, null, cancel).ConfigureAwait(false);
        return room;
    }

    /// <summary>Войти в асинхронную комнату по коду на место <paramref name="seat"/> (игрок партии) или вернуться по ключу.</summary>
    public static async Task<AsyncRoom> OpenAsync(Uri relay, RoomKey? key, string room = "", string seat = "", string nick = "",
                                                  CancellationToken cancel = default)
    {
        var link = new RelayLink(relay);
        await link.ConnectAsync(cancel).ConfigureAwait(false);
        if (key is not null) await link.RejoinAsync(key, cancel).ConfigureAwait(false);
        else await link.JoinAsync(room, nick, seat, cancel).ConfigureAwait(false);
        return new AsyncRoom(link);
    }

    /// <summary>Партия у посредника; хеш записанной партии сверяется с прочитанной (порча — <see cref="InvalidDataException"/>).</summary>
    public async Task<AsyncState> LoadAsync(CancellationToken cancel = default)
    {
        var (rev, next, day, hash, text) = await Link.LoadAsync(cancel).ConfigureAwait(false);
        Rev = rev;
        if (text is null) return new AsyncState(rev, next, day, hash, null);
        var state = GameSave.FromJson(text);
        if (hash.Length > 0 && StateHash.Of(state) != hash)
            throw new InvalidDataException($"партия комнаты {Key.Room} повреждена: хеш {StateHash.Of(state)}, записан {hash}");
        return new AsyncState(rev, next, day, hash, state);
    }

    /// <summary>Ход сделан: партия сессии, журнал хода (null — начало партии) и чей ход дальше — следующей ревизией.</summary>
    public async Task StoreAsync(GameSession session, CommandJournal? turn, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var snapshot = session.SnapshotWithoutSummaries();
        string next = session.Over ? "" : session.ActiveHuman;
        await Link.StoreAsync(Rev + 1, next, session.Map.Turn, StateHash.Of(snapshot), GameSave.ToJson(snapshot, "", DateTime.UnixEpoch),
                              turn?.ToJsonLines() ?? "", cancel).ConfigureAwait(false);
        Rev++;
    }

    /// <summary>Ход ли места этой машины в записанной партии.</summary>
    public bool Mine(AsyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Next.Length > 0 && state.Next.Equals(Key.Seat, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Мои асинхронные комнаты по ключам: где мой ход.</summary>
    public static async Task<JsonArray> ListAsync(Uri relay, IEnumerable<RoomKey> keys, CancellationToken cancel = default)
    {
        await using var link = new RelayLink(relay) { AutoReconnect = false };
        await link.ConnectAsync(cancel).ConfigureAwait(false);
        return await link.RoomsAsync(keys, cancel).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => Link.DisposeAsync();
}
