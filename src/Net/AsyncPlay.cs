using System.Collections.Concurrent;
using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Battle;

namespace LastEdict.Net;

/// <summary>
/// Асинхронная партия для показа (<c>docs/network.md</c> §8): своя партия целиком, свой ход — у себя (ходы ИИ после него — тоже у себя). Ход
/// кончился (ход ушёл другому человеку или партия кончилась) — рабочий поток карты записывает его посреднику с журналом хода
/// (<see cref="AsyncRoom.StoreAsync"/>); игру можно закрыть. Посредник сообщил «ваш ход» (или опрос раз в <see cref="PollEvery"/>) — партия
/// берётся заново и подменяет сессию на месте (<see cref="GameSession.Replace"/>), ход — снова свой.
/// </summary>
public sealed class AsyncPlay : INetPlay, IAsyncDisposable
{
    private readonly AsyncRoom _room;
    private readonly ConcurrentQueue<Action> _work = new();
    private bool _playing;
    private int _loading;
    private DateTime _polled = DateTime.UtcNow;

    private AsyncPlay(AsyncRoom room, GameSession session, string me)
    {
        _room = room;
        Session = session;
        Me = me;
        room.YourTurn += _ => Fetch();
        room.Link.Lost += () => Notice?.Invoke("связь с посредником потеряна — переподключаюсь");
        room.Link.Reconnected += () => Notice?.Invoke("связь вернулась");
    }

    /// <summary>Как часто спрашивать посредника, не мой ли ход (на случай, если «ваш ход» не дошёл).</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(20);

    public AsyncRoom RoomLink => _room;

    public string Me { get; }

    public GameSession Session { get; }

    public bool MyTurn => !Session.Over && _playing && Session.ActiveHuman.Equals(Me, StringComparison.OrdinalIgnoreCase);

    public string Active => Session.ActiveHuman;

    public bool Over => Session.Over;

    public DateTimeOffset? Deadline => null;

    public string Room => _room.Key.Room;

    /// <summary>Ход записан посреднику: ревизия, хеш, журнал хода (драйвер собирает журналы для повтора).</summary>
    public event Action<long, string, string>? Stored;

    public event Action? Changed;

    public event Action? TurnChanged;

    public event Action<string>? Notice;

    public event Action<string>? Log;

    /// <summary>Открыть партию комнаты: взять у посредника, сессия — фабрикой; ход свой — журнал хода с этого мига.</summary>
    public static async Task<AsyncPlay> OpenAsync(AsyncRoom room, Func<GameState, GameSession> factory, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(factory);
        var stored = await room.LoadAsync(cancel).ConfigureAwait(false);
        var state = stored.State ?? throw new InvalidDataException($"у комнаты {room.Key.Room} ещё нет партии");
        var play = new AsyncPlay(room, factory(state), room.Key.Seat);
        play.Session.EventBattlesOnScreen = false;
        if (room.Mine(stored)) play.BeginTurn();
        return play;
    }

    private void BeginTurn()
    {
        Session.Journal = CommandJournal.Start(Session);
        _playing = true;
        Log?.Invoke($"async: ход {Me}, день {Session.Map.Turn}, ревизия {_room.Rev}");
    }

    public bool TryTakeWork(out Action work)
    {
        if (_playing && !MyTurn)
        {
            _playing = false;   // ход сделан: записать (один раз)
            _work.Enqueue(Store);
        }
        return _work.TryDequeue(out work!);
    }

    /// <summary>Записать ход (рабочий поток карты).</summary>
    private void Store()
    {
        var journal = Session.Journal;
        try
        {
            _room.StoreAsync(Session, journal).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is RelayException or OperationCanceledException or InvalidOperationException)
        {
            Notice?.Invoke($"ход не записан: {e.Message} — пробую снова");
            _playing = true;   // следующая попытка — тем же журналом
            Thread.Sleep(2000);
            return;
        }
        string hash = StateHash.Of(GameSave.FromJson(GameSave.ToJson(Session.SnapshotWithoutSummaries(), "", DateTime.UnixEpoch)));
        Stored?.Invoke(_room.Rev, hash, journal?.ToJsonLines() ?? "");
        Notice?.Invoke(Session.Over ? "партия кончилась — записано" : $"ход передан: {Session.ActiveHuman} — игру можно закрыть");
        Log?.Invoke($"async: записано ревизия {_room.Rev} день {Session.Map.Turn} дальше {Session.ActiveHuman}");
        TurnChanged?.Invoke();
    }

    /// <summary>Посредник сказал «ваш ход» — взять партию (в фоне), подменить сессию на рабочем потоке карты.</summary>
    private void Fetch()
    {
        if (_playing || Interlocked.Exchange(ref _loading, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var stored = await _room.LoadAsync().ConfigureAwait(false);
                if (!_room.Mine(stored) || stored.State is not { } state) return;
                _work.Enqueue(() =>
                {
                    Session.Replace(state);
                    BeginTurn();
                    Changed?.Invoke();
                    TurnChanged?.Invoke();
                    Notice?.Invoke("ваш ход");
                });
            }
            catch (Exception e) when (e is RelayException or InvalidDataException or OperationCanceledException)
            {
                Log?.Invoke($"async: партия не взята: {e.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _loading, 0);
            }
        });
    }

    /// <summary>Кадр: не свой ход — изредка спросить посредника.</summary>
    public void Pump()
    {
        if (_playing || DateTime.UtcNow - _polled < PollEvery) return;
        _polled = DateTime.UtcNow;
        Fetch();
    }

    public bool TryTakeBattle(out BattleRequest request)
    {
        request = null!;
        return false;
    }

    public BattleKit? BuildBattle(BattleRequest request) => null;

    public void SendBattleStep(BattleInput input)
    {
    }

    public event Action<BattleInput>? BattleStepReceived { add { } remove { } }

    public void ReplyBattle(BattleRequest request, BattlePlay? play, IReadOnlyList<TurnRecord> log)
    {
    }

    public ValueTask DisposeAsync() => _room.DisposeAsync();
}
