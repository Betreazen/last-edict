using System.Text.Json.Nodes;
using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Battle;

namespace LastEdict.Net;

/// <summary>Настройки клиента.</summary>
public sealed class NetClientOptions
{
    public required LastEdict.Data.Content.Ruleset Ruleset { get; init; }
    public string Build { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Ключ места от прошлого входа (возвращение); null — новый вход.</summary>
    public string? Token { get; init; }

    /// <summary>Желаемое место (игрок партии); null — любое свободное.</summary>
    public string? Seat { get; init; }

    /// <summary>Сколько ждать ответа хоста на команду (у конца хода — ходы ИИ на хосте).</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan EndTurnTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>Хост не ответил или отказал — команда не исполнена. Окно показывает текст; вид восстанавливается resync.</summary>
public sealed class NetCommandException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Клиент сетевой партии (<c>docs/network.md</c> §4): держит вид игрока (<see cref="NetView"/>) и сессию над ним; команды сессии
/// (<see cref="GameSession.Remote"/>) уходят хосту, ответ ждётся, вид меняют дельты хоста (каждая — к точному номеру, хеш сверяется;
/// иначе — resync). Бои, которые клиент играет на своём экране, собираются и у него: <c>startBattle</c>, <c>endBattle</c> исполняются здесь
/// же, генератор боя — копия генератора хоста (<see cref="Receipt.Seed"/>); хост повторяет бой по действиям и сверяет журнал.
/// <para>Сообщения принимает <see cref="Receive"/> с потока сети; вид меняется под замком, события зовутся там же — показ переносит их на
/// свой поток.</para>
/// </summary>
public sealed class NetClient
{
    private readonly NetClientOptions _options;
    private readonly Action<NetMessage> _send;
    private readonly Func<GameState, GameSession> _sessionFactory;
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<Receipt>> _waiting = new(StringComparer.Ordinal);
    private readonly string _instance = Guid.NewGuid().ToString("N")[..8];
    private JsonNode? _view;
    private int _serial;
    private bool _resyncing;

    /// <param name="sessionFactory">Сессия над первым видом (правила, графика карты — у клиента свои).</param>
    public NetClient(NetClientOptions options, Action<NetMessage> send, Func<GameState, GameSession> sessionFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    /// <summary>Игрок клиента (после <see cref="Welcome"/>).</summary>
    public string? Player { get; private set; }

    /// <summary>Ключ места для возвращения.</summary>
    public string? Token { get; private set; }

    public string? Room { get; private set; }

    public string? Epoch { get; private set; }

    public long Seq { get; private set; }

    public TurnInfo? Turn { get; private set; }

    /// <summary>Сессия над видом; null — вида ещё нет.</summary>
    public GameSession? Session { get; private set; }

    /// <summary>Отказ входа (<see cref="Refused"/>), если был.</summary>
    public Refused? Refusal { get; private set; }

    /// <summary>Сейчас ход этого клиента.</summary>
    public bool MyTurn => Turn is { Token: not null, Over: false } t && t.Active.Equals(Player, StringComparison.OrdinalIgnoreCase);

    /// <summary>Хеш текущего вида (для проверок).</summary>
    public string? ViewHash
    {
        get
        {
            lock (_gate) return _view is null ? null : ViewPatch.Hash(_view);
        }
    }

    /// <summary>Узел текущего вида (копия, для проверок).</summary>
    public JsonNode? View
    {
        get
        {
            lock (_gate) return _view?.DeepClone();
        }
    }

    public event Action? Joined;
    public event Action? ViewChanged;
    public event Action<TurnInfo>? TurnChanged;
    public event Action<Refused>? Rejected;
    public event Action<BattleRequest>? BattleRequested;
    public event Action<Chat>? ChatReceived;
    public event Action<string>? Log;

    /// <summary>Войти (или вернуться): <see cref="Hello"/> с правилами и последним видом.</summary>
    public void Connect()
    {
        string? epoch;
        long seq;
        lock (_gate) (epoch, seq) = (Epoch, Seq);
        _send(new Hello(NetJson.ProtocolVersion, _options.Build, _options.Ruleset, _options.Name, Token ?? _options.Token, _options.Seat, epoch, seq));
    }

    /// <summary>Связь есть: вход принят хостом и не было обрыва.</summary>
    public bool Online { get; private set; }

    /// <summary>Связь пропала: ждущие команды получают отказ «нет связи» (окно покажет; после возвращения вид придёт заново).</summary>
    public void Disconnected()
    {
        lock (_gate)
        {
            Online = false;
            foreach (var waiting in _waiting.Values) waiting.TrySetResult(new Receipt("", NetErrors.Lost, "связь с хостом потеряна"));
            _waiting.Clear();
        }
    }

    public void Receive(NetMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message)
        {
            case Welcome welcome:
                lock (_gate) (Player, Token, Room, Online) = (welcome.Player, welcome.Token, welcome.Room, true);
                Joined?.Invoke();
                break;
            case Refused refused:
                Refusal = refused;
                Rejected?.Invoke(refused);
                break;
            case ViewSnapshot snapshot: Apply(snapshot); break;
            case ViewDelta delta: Apply(delta); break;
            case TurnInfo turn:
                lock (_gate) Turn = turn;
                TurnChanged?.Invoke(turn);
                break;
            case Receipt receipt:
                TaskCompletionSource<Receipt>? waiting;
                lock (_gate)
                    if (_waiting.Remove(receipt.CommandId, out waiting)) { }
                waiting?.TrySetResult(receipt);
                break;
            case BattleRequest request: BattleRequested?.Invoke(request); break;
            case Chat chat: ChatReceived?.Invoke(chat); break;
            default: Log?.Invoke($"net: от хоста — {message.GetType().Name}, не для клиента"); break;
        }
    }

    private void Apply(ViewSnapshot snapshot)
    {
        bool fresh;
        lock (_gate)
        {
            if (ViewPatch.Hash(snapshot.View) != snapshot.Hash)
            {
                Log?.Invoke("net: снимок вида с чужим хешем — прошу заново");
                Resync("хеш снимка не сошёлся");
                return;
            }
            _view = snapshot.View.DeepClone();
            (Epoch, Seq) = (snapshot.Epoch, snapshot.Seq);
            _resyncing = false;
            fresh = Session is null;
            Adopt();
        }
        if (fresh) Log?.Invoke($"net: вид получен, день {Session!.Map.Turn}");
        ViewChanged?.Invoke();
    }

    private void Apply(ViewDelta delta)
    {
        lock (_gate)
        {
            if (_resyncing) return;
            if (_view is null || delta.Epoch != Epoch || delta.BaseSeq != Seq)
            {
                Resync($"дельта {delta.BaseSeq}→{delta.Seq} к виду {Seq}");
                return;
            }
            try
            {
                _view = ViewPatch.Apply(_view, delta.Ops);
            }
            catch (InvalidDataException e)
            {
                Resync(e.Message);
                return;
            }
            if (ViewPatch.Hash(_view) != delta.Hash)
            {
                Resync("хеш вида после дельты не сошёлся");
                return;
            }
            Seq = delta.Seq;
            Adopt();
        }
        ViewChanged?.Invoke();
    }

    /// <summary>Вид → сессия: первая — фабрикой, дальше — та же сессия с новым состоянием (бой у себя остаётся).</summary>
    private void Adopt()
    {
        var state = NetView.Read(_view!);
        if (Session is null)
        {
            Session = _sessionFactory(state);
            Session.Remote = Run;
        }
        else Session.AdoptView(state);
    }

    /// <summary>Попросить у хоста вид целиком (дельта не легла, хеш не сошёлся, отказ «вид устарел»).</summary>
    public void Resync(string reason)
    {
        lock (_gate)
        {
            if (_resyncing) return;
            _resyncing = true;
        }
        Log?.Invoke($"net: resync — {reason}");
        _send(new ResyncRequest(reason));
    }

    /// <summary>Команда сессии клиента (<see cref="GameSession.Remote"/>): хосту и ждать ответа. Бой своего экрана — сначала здесь
    /// (<see cref="GameSession.ExecuteHere"/>), значение — своё; генератор боя — с хоста. Отказ или нет связи —
    /// <see cref="NetCommandException"/>.</summary>
    public CommandResult Run(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var session = Session ?? throw new NetCommandException(NetErrors.Lost, "вида партии ещё нет");
        var before = NetHost.Mirrored(command) ? session.SnapshotWithoutSummaries() : null;
        CommandResult? local = before is not null ? session.ExecuteHere(command) : null;
        var receipt = Ask(command);
        if (!receipt.Ok)
        {
            if (before is not null) session.Restore(before);   // бой, собранный у себя, — снят: у хоста его нет
            if (receipt.Error is NetErrors.StaleView or NetErrors.WrongTurn) Resync($"отказ {receipt.Error}");
            throw new NetCommandException(receipt.Error!, receipt.Text ?? receipt.Error!);
        }
        if (receipt.Seed is { } seed) session.RestoreRandom(seed);
        if (local is not null) return local;
        return new CommandResult(ValueCodec.Decode(receipt.Value), receipt.Report ?? [], [], []);
    }

    private Receipt Ask(GameCommand command)
    {
        string id;
        long seq;
        string token;
        var waiting = new TaskCompletionSource<Receipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (!Online) return new Receipt("", NetErrors.Lost, "нет связи с хостом");
            id = $"{_instance}:{++_serial}";
            seq = Seq;
            token = Turn?.Token ?? "";
            _waiting[id] = waiting;
        }
        _send(new CommandRequest(id, seq, token, command));
        var timeout = command is EndTurnCommand ? _options.EndTurnTimeout : _options.CommandTimeout;
        if (!waiting.Task.Wait(timeout))
        {
            lock (_gate) _waiting.Remove(id);
            return new Receipt(id, NetErrors.Lost, "хост не ответил");
        }
        return waiting.Task.Result;
    }

    /// <summary>Бой хода ИИ по запросу хоста — собрать у себя (<see cref="GameSession.BuildBattleHere"/>) и поставить генератор боя хоста;
    /// отпечаток не сошёлся ни как бой, ни как дуэль — null (вид неполон: resync).</summary>
    public BattleState? BuildRequestedBattle(BattleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = Session ?? throw new InvalidOperationException("вида партии ещё нет");
        foreach (bool duel in new[] { false, true })
        {
            var battle = session.BuildBattleHere(request.AttackerGroup, request.DefenderGroup, duel);
            if (BattleFingerprint.Of(battle) != request.Fingerprint) continue;
            session.RestoreRandom(request.Seed);
            return battle;
        }
        Resync($"бой {request.Battle} у клиента собирается не так, как у хоста");
        return null;
    }

    /// <summary>Ответ на бой хода ИИ: действия по шагам и журнал.</summary>
    public void ReplyBattle(BattleRequest request, BattlePlay? play, IReadOnlyList<TurnRecord> log)
    {
        ArgumentNullException.ThrowIfNull(request);
        _send(new BattleReply(request.Battle, play, log));
    }

    public void Say(string text) => _send(new Chat(Player ?? "", text));
}
