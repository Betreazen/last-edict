using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Net;

/// <summary>С чем входит игрок: посредник, имя, набор правил, сборка игры, фабрика сессии над видом (правила и графика карты — у
/// клиента свои).</summary>
/// <param name="AutoApply">Вид меняется на потоке сети (проверки); игра — false и <see cref="NetSession.Pump"/> на своём потоке.</param>
public sealed record NetJoin(Uri Relay, string Nick, Ruleset Ruleset, string Build, Func<GameState, GameSession> SessionFactory,
                             bool AutoApply = true);

/// <summary>
/// Сетевая партия целиком (<c>docs/network.md</c> §7) — для игры и драйвера двух машин. Обычная комната: хост (<see cref="HostAsync"/>)
/// или клиент (<see cref="JoinAsync"/>) через посредника; чей ход (<see cref="MyTurn"/>); работа для рабочего потока карты
/// (<see cref="TryTakeWork"/>: у хоста — конец хода клиента, его ходы ИИ показывает карта хоста) и бои хода ИИ, которые клиент играет на
/// своём экране (<see cref="TryTakeBattle"/>). Асинхронная комната — <see cref="AsyncTurn"/>.
/// </summary>
public sealed class NetSession : IAsyncDisposable, IHostRunner, INetPlay
{
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ConcurrentQueue<BattleRequest> _battles = new();

    private NetSession(RelayLink link, string me)
    {
        Link = link;
        Me = me;
        link.Notice += OnNotice;
        link.Log += text => Log?.Invoke(text);
        link.Lost += () => Notice?.Invoke("связь с посредником потеряна — переподключаюсь");
        link.Reconnected += () => Notice?.Invoke("связь вернулась");
    }

    public RelayLink Link { get; }

    /// <summary>Игрок этой машины.</summary>
    public string Me { get; private set; }

    public NetHost? Host { get; private set; }

    public NetClient? Client { get; private set; }

    public bool IsHost => Host is not null;

    public string Room => Link.Key?.Room ?? "";

    /// <summary>Партия этой машины: у хоста — настоящая, у клиента — вид.</summary>
    public GameSession Session => Host?.Session ?? Client?.Session ?? throw new InvalidOperationException("партии ещё нет");

    /// <summary>Ход этой машины: можно водить отряды и окна.</summary>
    public bool MyTurn => Host is { } host
        ? !host.Session.Over && host.Session.ActiveHuman.Equals(Me, StringComparison.OrdinalIgnoreCase)
        : Client?.MyTurn == true;

    /// <summary>Чей ход (игрок партии).</summary>
    public string Active => Host?.Session.ActiveHuman ?? Client?.Turn?.Active ?? "";

    /// <summary>Срок хода; null — без таймера.</summary>
    public DateTimeOffset? Deadline => Host is { } host ? host.Deadline
        : Client?.Turn?.Deadline is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    public bool Over => Host?.Session.Over ?? Client?.Turn?.Over ?? false;

    /// <summary>Партия изменилась (команда другого игрока, новый вид) — с потока сети или исполнения; показ переносит на свой.</summary>
    public event Action? Changed;

    /// <summary>Сменился ход.</summary>
    public event Action? TurnChanged;

    /// <summary>Строка игроку (вошёл, отключился, вернулся, отказ).</summary>
    public event Action<string>? Notice;

    public event Action<string>? Log;

    // ---- хост

    /// <summary>Создать комнату и стать хостом партии <paramref name="session"/>: места <paramref name="seats"/> — игроки-люди для друзей.</summary>
    public static async Task<NetSession> HostAsync(Uri relay, GameSession session, string me, IReadOnlyList<string> seats, string nick,
                                                   Ruleset ruleset, string build = "", int? turnSeconds = null, string name = "",
                                                   CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(seats);
        var link = new RelayLink(relay);
        await link.ConnectAsync(cancel).ConfigureAwait(false);
        var key = await link.CreateAsync(name.Length > 0 ? name : session.State.Info.ScenarioName, nick, async: false, seat: me, cancel)
                            .ConfigureAwait(false);
        var net = new NetSession(link, me);
        session.EventBattlesOnScreen = false;   // бой события в сети — автобой у всех: журнал повторяется одной настройкой
        var host = new NetHost(session, new NetHostOptions { Room = key.Room, Ruleset = ruleset, Build = build, TurnSeconds = turnSeconds },
                               NetRelay.HostSender(link)) { Runner = net };
        NetRelay.Attach(host, link);
        foreach (string seat in seats) host.AddSeat(seat);
        host.Changed += () => net.Changed?.Invoke();
        host.TurnChanged += _ => net.TurnChanged?.Invoke();
        host.Log += text => net.Log?.Invoke(text);
        net.Host = host;
        return net;
    }

    /// <summary>Разобрать почту: у хоста — команды клиентов, у клиента — пришедшие виды (кадр игры или цикл драйвера).</summary>
    public void Pump()
    {
        Host?.Pump();
        Client?.Pump();
    }

    /// <summary>Конец хода клиента — на рабочий поток карты хоста (там ходы ИИ показываются, бой с человеком хоста — его экраном);
    /// прочее — сразу. Без показа (<see cref="HeadlessHost"/>) — всё сразу.</summary>
    void IHostRunner.Run(GameCommand command, Func<CommandResult> execute, Action done)
    {
        if (command is EndTurnCommand && !HeadlessHost)
        {
            _work.Enqueue(() =>
            {
                try { execute(); }
                finally { done(); }
            });
            return;
        }
        try { execute(); }
        finally { done(); }
    }

    /// <summary>Хост без карты (драйвер, проверки): конец хода клиента исполняется сразу на потоке почты.</summary>
    public bool HeadlessHost { get; set; }

    /// <summary>Рабочий поток карты, пока ход не этой машины: взять работу (конец хода клиента у хоста).</summary>
    public bool TryTakeWork(out Action work) => _work.TryDequeue(out work!);

    // ---- клиент

    /// <summary>Войти в комнату по коду (или вернуться по ключу <paramref name="rejoin"/>) и дождаться первого вида.</summary>
    /// <exception cref="RelayException">Комнаты нет, ключ чужой, посредник недоступен.</exception>
    /// <exception cref="NetCommandException">Хост отказал (правила, версия, мест нет) или не прислал вид.</exception>
    public static async Task<NetSession> JoinAsync(NetJoin join, string room, RoomKey? rejoin = null, string? seat = null,
                                                   string? token = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(join);
        var link = new RelayLink(join.Relay);
        await link.ConnectAsync(cancel).ConfigureAwait(false);
        if (rejoin is not null) await link.RejoinAsync(rejoin, cancel).ConfigureAwait(false);
        else await link.JoinAsync(room, join.Nick, seat ?? "", cancel).ConfigureAwait(false);
        var net = new NetSession(link, "");
        var client = new NetClient(new NetClientOptions { Ruleset = join.Ruleset, Build = join.Build, Name = join.Nick, Seat = seat, Token = token },
                                   NetRelay.ClientSender(link), join.SessionFactory) { AutoApply = join.AutoApply };
        net.Client = client;
        NetRelay.Attach(client, link);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ViewChanged += () =>
        {
            first.TrySetResult();
            net.Changed?.Invoke();
        };
        client.TurnChanged += _ => net.TurnChanged?.Invoke();
        client.Rejected += refused =>
        {
            net.Notice?.Invoke(refused.Text);
            first.TrySetException(new NetCommandException(refused.Code, refused.Text));
        };
        client.Joined += () => net.Me = client.Player ?? "";
        client.BattleRequested += request => net._battles.Enqueue(request);
        client.ChatReceived += chat => net.Notice?.Invoke($"{chat.From}: {chat.Text}");
        client.Log += text => net.Log?.Invoke(text);
        client.Connect();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        wait.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            while (!first.Task.IsCompleted)   // вид, пришедший в очередь (AutoApply = false), — разобрать здесь
            {
                wait.Token.ThrowIfCancellationRequested();
                client.Pump();
                await Task.WhenAny(first.Task, Task.Delay(20, wait.Token)).ConfigureAwait(false);
            }
            await first.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new NetCommandException(NetErrors.Lost, "хост не прислал партию — он на связи?");
        }
        net.Me = client.Player ?? "";
        return net;
    }

    /// <summary>Клиент: бой хода ИИ, который хост просит сыграть на этом экране.</summary>
    public bool TryTakeBattle(out BattleRequest request) => _battles.TryDequeue(out request!);

    public LastEdict.Rules.Battle.BattleState? BuildBattle(BattleRequest request) => Client?.BuildRequestedBattle(request);

    public void ReplyBattle(BattleRequest request, LastEdict.Data.Battle.BattlePlay? play, IReadOnlyList<LastEdict.Rules.Battle.TurnRecord> log) =>
        Client?.ReplyBattle(request, play, log);

    private void OnNotice(JsonObject notice)
    {
        string op = notice["op"]?.GetValue<string>() ?? "";
        bool host = notice["host"]?.GetValue<bool>() ?? false;
        string who = notice["nick"]?.GetValue<string>() is { Length: > 0 } nick ? nick : notice["peer"]?.GetValue<string>() ?? "";
        string? text = op switch
        {
            "peer" when host => "хост вернулся",
            "peer" => notice["back"]?.GetValue<bool>() == true ? $"{who} вернулся" : $"{who} вошёл",
            "gone" when host => "хост отключился — партия ждёт его",
            "gone" => $"{who} отключился",
            "yourTurn" => "ваш ход",
            _ => null,
        };
        if (text is not null) Notice?.Invoke(text);
    }

    public async ValueTask DisposeAsync()
    {
        Host?.Dispose();
        if (Client?.Session is { } session) session.Remote = null;
        await Link.DisposeAsync().ConfigureAwait(false);
    }
}
