using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Net;

/// <summary>Настройки хоста комнаты.</summary>
public sealed class NetHostOptions
{
    public required string Room { get; init; }

    /// <summary>Набор правил хоста: входящий с другим получает отказ с текстом (<see cref="Data.Content.RulesetHash.Differences"/>).</summary>
    public required LastEdict.Data.Content.Ruleset Ruleset { get; init; }

    /// <summary>Сборка игры хоста; пусто — не сверять.</summary>
    public string Build { get; init; } = "";

    /// <summary>Таймер хода, секунды; null — без таймера.</summary>
    public int? TurnSeconds { get; init; }

    /// <summary>Сколько секунд хост ждёт сверх срока, прежде чем закончить ход клиента сам (клиент обычно заканчивает его сам в срок).</summary>
    public int GraceSeconds { get; init; } = 10;

    /// <summary>Сколько секунд ждать ответа клиента на бой в ходе ИИ; не ответил — автобой.</summary>
    public int BattleSeconds { get; init; } = 900;

    /// <summary>Сколько последних дельт вида хранить на место для переподключения.</summary>
    public int KeepDeltas { get; init; } = 64;

    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;
}

/// <summary>Кто исполняет команды на хосте. По умолчанию — сразу, на потоке <see cref="NetHost.Pump"/>; игра отдаёт конец хода сцене
/// карты (ходы ИИ на её рабочем потоке, с показом).</summary>
public interface IHostRunner
{
    /// <summary>Исполнить <paramref name="command"/> через <paramref name="execute"/> (там правила, рассылка вида и ответ) — сразу или
    /// позже на своём потоке; хост ждёт <paramref name="done"/>.</summary>
    void Run(GameCommand command, Func<CommandResult> execute, Action done);
}

/// <summary>
/// Хост комнаты (<c>docs/network.md</c> §4): настоящая партия, один последовательный обработчик команд. Людям-клиентам — их вид
/// (<see cref="PlayerView"/>) дельтами (<see cref="ViewPatch"/>), право хода (<see cref="TurnInfo"/>), бои в ходе ИИ на их экране
/// (<see cref="BattleRequest"/>). Транспорт не знает: сообщения приходят в <see cref="Receive"/> (с любого потока) и уходят через
/// <c>send(получатель, сообщение)</c>; получатель — соединение посредника.
/// <para>Порядок команды (контракт §4): место и command_id (повтор — прежний ответ), право хода и номер вида, права на объекты команды,
/// исполнение (сбой — откат партии и генераторов, <see cref="GameSession.Restore"/>), рассылка видов, ответ.</para>
/// <para>Свои команды хоста (его окна) идут через тот же <see cref="GameSession.Remote"/> — после каждой клиенты получают вид.</para>
/// </summary>
public sealed class NetHost : IDisposable
{
    private readonly GameSession _session;
    private readonly NetHostOptions _options;
    private readonly Action<string, NetMessage> _send;
    private readonly ConcurrentQueue<(string Peer, NetMessage Message)> _inbox = new();
    private readonly Dictionary<string, Seat> _seats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BattleReply>> _battles = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _busy;
    private string _active = "";
    private string _token = "";
    private int _day = -1;
    private DateTimeOffset? _deadline;
    private int _battleSerial;

    public NetHost(GameSession session, NetHostOptions options, Action<string, NetMessage> send)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _session.Remote = OwnCommand;
        _session.Observer = new HostObserver(this, _session.Observer);
    }

    public GameSession Session => _session;

    /// <summary>Исполнитель команд клиентов (<see cref="IHostRunner"/>); null — сразу.</summary>
    public IHostRunner? Runner { get; set; }

    /// <summary>Партия изменилась командой клиента — показ хоста перерисовывает карту. Зовётся на потоке исполнения.</summary>
    public event Action? Changed;

    /// <summary>Сменился ход (чей, день, срок) — показ хоста запирает или отпирает окна.</summary>
    public event Action<TurnInfo>? TurnChanged;

    /// <summary>Строка журнала хоста (отказы, расхождения боёв, переподключения).</summary>
    public event Action<string>? Log;

    /// <summary>Чей ход сейчас.</summary>
    public string Active => _session.ActiveHuman;

    /// <summary>Срок хода; null — без таймера.</summary>
    public DateTimeOffset? Deadline => _deadline;

    /// <summary>Место клиента — игрок партии. Токен — для входа и возвращения; null — выдать новый.</summary>
    public string AddSeat(string player, string? token = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(player);
        if (!_session.Map.Players.ContainsKey(player)) throw new ArgumentException($"в партии нет игрока «{player}»", nameof(player));
        lock (_gate)
        {
            var seat = new Seat(player, token ?? NewToken());
            _seats[player] = seat;
            return seat.Token;
        }
    }

    /// <summary>Места клиентов: игрок → подключён ли.</summary>
    public IReadOnlyDictionary<string, bool> Seats
    {
        get
        {
            lock (_gate) return _seats.ToDictionary(s => s.Key, s => s.Value.Peer is not null, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Игрок — клиент (не хост и не ИИ).</summary>
    public bool IsRemote(string player)
    {
        lock (_gate) return _seats.ContainsKey(player);
    }

    /// <summary>Сообщение от соединения <paramref name="peer"/> — с любого потока. Ответ клиента на бой будит ждущий ход ИИ сразу; прочее
    /// ждёт <see cref="Pump"/>.</summary>
    public void Receive(string peer, NetMessage message)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(message);
        if (message is BattleReply reply && _battles.TryGetValue(reply.Battle, out var waiting))
        {
            waiting.TrySetResult(reply);
            return;
        }
        _inbox.Enqueue((peer, message));
    }

    /// <summary>Соединение ушло: место ждёт возвращения, ход ИИ не ждёт его ответа на бой.</summary>
    public void Disconnected(string peer)
    {
        lock (_gate)
            foreach (var seat in _seats.Values.Where(s => s.Peer == peer))
            {
                seat.Peer = null;
                Log?.Invoke($"net: {seat.Player} отключился");
                foreach (var (id, waiting) in _battles)
                    if (id.StartsWith(seat.Player + ":", StringComparison.Ordinal)) waiting.TrySetCanceled();
            }
    }

    /// <summary>Разобрать накопленные сообщения и проверить срок хода. Зовёт поток хоста (кадр игры, цикл проверки); пока команда идёт
    /// (ход ИИ на рабочем потоке), сообщения ждут.</summary>
    public void Pump()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (_busy) return;
            if (_day < 0) AnnounceTurn();
            while (!_busy && _inbox.TryDequeue(out var item)) Handle(item.Peer, item.Message);
            if (!_busy) CheckDeadline();
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void Handle(string peer, NetMessage message)
    {
        switch (message)
        {
            case Hello hello: Join(peer, hello); break;
            case CommandRequest request: Command(peer, request); break;
            case ResyncRequest resync:
                if (SeatOf(peer) is { } seat)
                {
                    Log?.Invoke($"net: resync {seat.Player}: {resync.Reason}");
                    Snapshot(seat);
                    SendTurn(seat);
                }
                break;
            case Chat chat:
                if (SeatOf(peer) is { } from)
                    foreach (var s in _seats.Values.Where(s => s.Peer is not null)) _send(s.Peer!, chat with { From = from.Player });
                break;
            case BattleReply: break;   // бой уже не ждут (автобой по сроку или обрыву)
            default: Log?.Invoke($"net: от {peer} — {message.GetType().Name}, не для хоста"); break;
        }
    }

    // ---- вход

    private void Join(string peer, Hello hello)
    {
        if (hello.Protocol != NetJson.ProtocolVersion)
        {
            _send(peer, new Refused(NetErrors.UnsupportedVersion, $"другая версия протокола: у хоста {NetJson.ProtocolVersion}, у вас {hello.Protocol}"));
            return;
        }
        if (_options.Build.Length > 0 && hello.Build.Length > 0 && hello.Build != _options.Build)
        {
            _send(peer, new Refused(NetErrors.UnsupportedVersion, $"другая сборка игры: у хоста {_options.Build}, у вас {hello.Build}"));
            return;
        }
        var differences = LastEdict.Data.Content.RulesetHash.Differences(_options.Ruleset, hello.Ruleset);
        if (differences.Count > 0)
        {
            _send(peer, new Refused(NetErrors.RulesetMismatch, "правила не совпадают с хостом: " + string.Join("; ", differences)));
            return;
        }
        var seat = hello.Token is { } token ? _seats.Values.FirstOrDefault(s => s.Token == token) : null;
        if (hello.Token is not null && seat is null)
        {
            _send(peer, new Refused(NetErrors.UnknownToken, "место по этому ключу не найдено — войдите в комнату заново"));
            return;
        }
        seat ??= hello.Seat is { } wanted && _seats.TryGetValue(wanted, out var asked) && !asked.Claimed ? asked
                 : _seats.Values.FirstOrDefault(s => !s.Claimed);
        if (seat is null)
        {
            _send(peer, new Refused(NetErrors.RoomFull, "свободных мест нет"));
            return;
        }
        if (seat.Peer is { } old && old != peer) Log?.Invoke($"net: {seat.Player} вошёл с другого соединения");
        seat.Peer = peer;
        seat.Claimed = true;
        seat.Name = hello.Name;
        _send(peer, new Welcome(seat.Player, seat.Token, _options.Room));
        if (hello.Epoch == seat.Epoch && hello.Seq <= seat.Seq && seat.Deltas.FirstOrDefault()?.BaseSeq <= hello.Seq)
        {
            foreach (var delta in seat.Deltas.Where(d => d.BaseSeq >= hello.Seq)) _send(peer, delta);
            Log?.Invoke($"net: {seat.Player} вернулся, дослано дельт {seat.Seq - hello.Seq}");
        }
        else Snapshot(seat);
        SendTurn(seat);
    }

    private Seat? SeatOf(string peer) => _seats.Values.FirstOrDefault(s => s.Peer == peer);

    // ---- команды клиента

    private void Command(string peer, CommandRequest request)
    {
        if (SeatOf(peer) is not { } seat)
        {
            _send(peer, new Refused(NetErrors.UnknownToken, "сначала войдите в комнату"));
            return;
        }
        string payload = JsonSerializer.Serialize(request.Command, NetJson.Options);
        if (seat.Receipts.TryGetValue(request.CommandId, out var done))
        {
            _send(peer, done.Payload == payload ? done.Receipt
                : new Receipt(request.CommandId, NetErrors.InvalidCommand, "повтор command_id с другой командой"));
            return;
        }
        string? refusal = !seat.Player.Equals(_session.ActiveHuman, StringComparison.OrdinalIgnoreCase) || _session.Over ? NetErrors.WrongTurn
            : request.TurnToken != _token ? NetErrors.WrongTurn
            : request.ExpectedViewSeq != seat.Seq ? NetErrors.StaleView
            : CommandRights.Check(_session.State, seat.Player, request.Command) is not null ? NetErrors.NotYours
            : null;
        if (refusal is not null)
        {
            string text = refusal switch
            {
                NetErrors.StaleView => "вид устарел — нужен свежий",
                NetErrors.NotYours => CommandRights.Check(_session.State, seat.Player, request.Command)!,
                _ => "сейчас не ваш ход",
            };
            Remember(seat, request.CommandId, payload, new Receipt(request.CommandId, refusal, text));
            return;
        }
        _busy = true;
        var runner = Runner ?? Immediate.Instance;
        runner.Run(request.Command, () => Execute(seat, request, payload), () =>
        {
            lock (_gate) _busy = false;
        });
    }

    /// <summary>Исполнить команду клиента: правила, рассылка, ответ. Сбой — откат и отказ без подробностей (их — в журнал хоста).</summary>
    private CommandResult Execute(Seat seat, CommandRequest request, string payload)
    {
        lock (_gate)
        {
            var before = _session.SnapshotWithoutSummaries();
            CommandResult result;
            _session.BattlesPlayedElsewhere = true;
            _session.ElsewhereBattleDiverged = false;
            try
            {
                result = _session.ExecuteHere(request.Command);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException or InvalidDataException
                                          or NullReferenceException or IndexOutOfRangeException)
            {
                _session.Restore(before);
                Log?.Invoke($"net: команда {seat.Player} {request.Command.GetType().Name} не исполнена: {e.GetType().Name}: {e.Message}");
                Remember(seat, request.CommandId, payload,
                         new Receipt(request.CommandId, NetErrors.InvalidCommand, $"команда «{request.Command.GetType().Name}» не исполнена"));
                return new CommandResult(null, [], [], []);
            }
            finally
            {
                _session.BattlesPlayedElsewhere = false;
            }
            if (_session.ElsewhereBattleDiverged) Log?.Invoke($"net: бой {seat.Player} у хоста сыгран не так, как у клиента — итог хоста");
            Broadcast();
            var receipt = new Receipt(request.CommandId, Value: Mirrored(request.Command) ? null : SafeEncode(result.Value),
                                      Report: result.Report, Seed: request.Command is StartBattleCommand or NextEventBattleCommand ? BattleSeed() : null);
            Remember(seat, request.CommandId, payload, receipt);
            AnnounceTurn();
            Changed?.Invoke();
            return result;
        }
    }

    /// <summary>Команды, которые клиент исполняет и у себя (бой на его экране): значение — его, хост шлёт только вид и семя боя.</summary>
    internal static bool Mirrored(GameCommand command) =>
        command is StartBattleCommand or EndBattleCommand or NextEventBattleCommand;

    private JsonNode? SafeEncode(object? value)
    {
        try
        {
            return ValueCodec.Encode(value);
        }
        catch (Exception e) when (e is NotSupportedException or JsonException or InvalidOperationException)
        {
            Log?.Invoke($"net: значение {value?.GetType().Name} не пересылается: {e.Message}");
            return null;
        }
    }

    /// <summary>Места генератора боя и CRT сейчас — клиенту для боя на его экране (остальные потоки ему не нужны и не показываются).</summary>
    private RandomStreams BattleSeed()
    {
        var all = _session.RandomNow();
        string battle = RngStreams.Name(RngStream.Battle);
        return new RandomStreams(all.Streams.Where(s => s.Key.Equals(battle, StringComparison.OrdinalIgnoreCase))
                                            .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase), all.Crt);
    }

    private void Remember(Seat seat, string id, string payload, Receipt receipt)
    {
        seat.Receipts[id] = (payload, receipt);
        seat.ReceiptOrder.Enqueue(id);
        while (seat.ReceiptOrder.Count > 256) seat.Receipts.Remove(seat.ReceiptOrder.Dequeue());
        if (seat.Peer is { } peer) _send(peer, receipt);
    }

    // ---- свои команды хоста

    private CommandResult OwnCommand(GameCommand command)
    {
        lock (_gate)
        {
            var result = _session.ExecuteHere(command);
            Broadcast();
            AnnounceTurn();
            return result;
        }
    }

    // ---- вид

    /// <summary>Разослать каждому месту его вид (дельтой к прошлому). Зовётся после каждой команды и перед боем клиента в ходе ИИ.</summary>
    public void Broadcast()
    {
        lock (_gate)
        {
            if (_seats.Count == 0) return;
            var state = _session.SnapshotWithoutSummaries();
            foreach (var seat in _seats.Values)
            {
                var view = NetView.Of(state, seat.Player);
                if (seat.View is null)
                {
                    seat.View = view;
                    seat.Epoch ??= NewToken();
                    continue;
                }
                var ops = ViewPatch.Diff(seat.View, view);
                if (ops.Count == 0) continue;
                var delta = new ViewDelta(seat.Epoch!, seat.Seq, seat.Seq + 1, ops, ViewPatch.Hash(view));
                seat.View = view;
                seat.Seq++;
                seat.Deltas.Add(delta);
                if (seat.Deltas.Count > _options.KeepDeltas) seat.Deltas.RemoveAt(0);
                if (seat.Peer is { } peer) _send(peer, delta);
            }
        }
    }

    private void Snapshot(Seat seat)
    {
        var view = NetView.Of(_session.SnapshotWithoutSummaries(), seat.Player);
        seat.View = view;
        seat.Epoch = NewToken();
        seat.Seq++;
        seat.Deltas.Clear();
        if (seat.Peer is { } peer) _send(peer, new ViewSnapshot(seat.Epoch, seat.Seq, view.DeepClone(), ViewPatch.Hash(view)));
    }

    // ---- ход и таймер

    private void AnnounceTurn()
    {
        string active = _session.ActiveHuman;
        int day = _session.Map.Turn;
        if (active == _active && day == _day && !_session.Over) return;
        if (_session.Over && _token.Length == 0 && _day == day) return;
        _active = active;
        _day = day;
        _token = _session.Over ? "" : NewToken();
        _deadline = _options.TurnSeconds is { } seconds && !_session.Over ? _options.Clock().AddSeconds(seconds) : null;
        foreach (var seat in _seats.Values) SendTurn(seat);
        TurnChanged?.Invoke(TurnFor(""));
    }

    private TurnInfo TurnFor(string player) =>
        new(_active, player.Equals(_active, StringComparison.OrdinalIgnoreCase) && !_session.Over ? _token : null, _day,
            _deadline?.ToUnixTimeMilliseconds(), _session.Over);

    private void SendTurn(Seat seat)
    {
        if (seat.Peer is { } peer && _day >= 0) _send(peer, TurnFor(seat.Player));
    }

    /// <summary>Срок хода клиента вышел (с запасом), а конца хода нет — хост заканчивает его сам: открытый бой — автобоем.</summary>
    private void CheckDeadline()
    {
        if (_deadline is not { } deadline || _options.Clock() < deadline.AddSeconds(_options.GraceSeconds)) return;
        if (!_seats.ContainsKey(_session.ActiveHuman)) return;   // ход хоста: срок держит его окно
        Log?.Invoke($"net: срок хода {_session.ActiveHuman} вышел — конец хода за него");
        _deadline = null;
        _busy = true;
        var runner = Runner ?? Immediate.Instance;
        runner.Run(new EndTurnCommand(), ForceEndTurn, () =>
        {
            lock (_gate) _busy = false;
        });
    }

    private CommandResult ForceEndTurn()
    {
        lock (_gate)
        {
            if (_session.BattleInProgress is { } battle)
            {
                var rules = _session.Book!.Rules;
                var auto = new BattleSession(battle, new BattleSim(battle, _session.BattleRng, rules.Constants), rules.Constants) { Auto = true };
                auto.PlayOut();
                _session.ExecuteHere(new EndBattleCommand(auto.Log, auto.Played(0)));
            }
            if (_session.BattleEnding) _session.ExecuteHere(new CommitBattleCommand());
            var result = _session.ExecuteHere(new EndTurnCommand());
            Broadcast();
            AnnounceTurn();
            Changed?.Invoke();
            return result;
        }
    }

    // ---- бой клиента в ходе ИИ

    /// <summary>Бой, в котором сторона — клиент: показать ему бой и ждать ответа (<see cref="BattleRequest"/>); бой не его, клиента нет
    /// или он не ответил в срок — null (автобой ядром). Зовёт наблюдатель хода хоста (<see cref="HostObserver"/>, крючки сцены карты).</summary>
    public BattleDecision? RemoteBattle(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Seat? seat;
        string side;
        BattleRequest request;
        string id;
        lock (_gate)
        {
            var map = _session.Map;
            string attacker = OwnerOf(map, state.AttackerGroup), defender = OwnerOf(map, state.DefenderGroup);
            seat = _seats.GetValueOrDefault(defender) ?? _seats.GetValueOrDefault(attacker);
            if (seat?.Peer is null) return null;
            side = seat.Player.Equals(defender, StringComparison.OrdinalIgnoreCase) ? "defender" : "attacker";
            Broadcast();
            id = $"{seat.Player}:{++_battleSerial}";
            request = new BattleRequest(id, state.AttackerGroup, state.DefenderGroup, BattleFingerprint.Of(state), BattleSeed(), side);
        }
        var waiting = new TaskCompletionSource<BattleReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _battles[id] = waiting;
        try
        {
            _send(seat.Peer!, request);
            if (!waiting.Task.Wait(TimeSpan.FromSeconds(_options.BattleSeconds))) return null;
            var reply = waiting.Task.Result;
            if (reply.Play is null) return null;
            var log = _session.ReplayBattleHere(reply.Play, state);
            if (!log.SequenceEqual(reply.Log)) Log?.Invoke($"net: бой {id} у хоста сыгран не так, как у клиента — итог хоста");
            return new BattleDecision(log, reply.Play);
        }
        catch (AggregateException e) when (e.InnerException is TaskCanceledException)
        {
            return null;
        }
        finally
        {
            _battles.TryRemove(id, out _);
        }
    }

    private static string OwnerOf(MapState map, string group) =>
        map.Stacks.TryGetValue(group, out var stack) ? stack.Owner
        : map.Sites.FirstOrDefault(s => s.Id.Equals(group, StringComparison.OrdinalIgnoreCase))?.Owner ?? "";

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public void Dispose()
    {
        foreach (var waiting in _battles.Values) waiting.TrySetCanceled();
        _session.Remote = null;
        if (_session.Observer is HostObserver observer) _session.Observer = observer.Inner;
    }

    private sealed class Seat(string player, string token)
    {
        public string Player { get; } = player;
        public string Token { get; } = token;
        public string? Peer { get; set; }
        public bool Claimed { get; set; }
        public string Name { get; set; } = "";
        public string? Epoch { get; set; }
        public long Seq { get; set; }
        public JsonNode? View { get; set; }
        public List<ViewDelta> Deltas { get; } = [];
        public Dictionary<string, (string Payload, Receipt Receipt)> Receipts { get; } = new(StringComparer.Ordinal);
        public Queue<string> ReceiptOrder { get; } = new();
    }

    private sealed class Immediate : IHostRunner
    {
        public static readonly Immediate Instance = new();

        public void Run(GameCommand command, Func<CommandResult> execute, Action done)
        {
            try { execute(); }
            finally { done(); }
        }
    }

    /// <summary>Наблюдатель хода хоста: бои, где сторона — клиент, уходят ему; остальное — прежнему наблюдателю (показ хоста).</summary>
    internal sealed class HostObserver(NetHost host, ITurnObserver? inner) : ITurnObserver
    {
        public ITurnObserver? Inner { get; } = inner;

        public void PlayerTurn(string playerId) => Inner?.PlayerTurn(playerId);

        public void StackMoving(StackMove move) => Inner?.StackMoving(move);

        public IReadOnlyList<TurnRecord>? Battle(BattleState state) => DecideBattle(state)?.Log;

        public BattleDecision? DecideBattle(BattleState state) => host.RemoteBattle(state) ?? Inner?.DecideBattle(state);

        public void Happened(TurnSight sight) => Inner?.Happened(sight);

        public void Offered(AllianceOffer offer) => Inner?.Offered(offer);

        public void HumanTurn(string playerId) => Inner?.HumanTurn(playerId);

        public void BattleEnded(BattleOutcome outcome) => Inner?.BattleEnded(outcome);

        public void EventFired(EventFiring firing) => Inner?.EventFired(firing);
    }
}
