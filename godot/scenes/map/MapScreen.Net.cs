using System;
using System.IO;
using System.Linq;
using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Net;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Сетевая партия на экране карты (Э7, <c>docs/network.md</c> §7): почта сети — каждый кадр (<see cref="NetSession.Pump"/>), чужой ход —
/// ожидание в режиме хода ИИ (<see cref="MapView.WaitOthers"/>), свой срок хода — конец хода сам (бой — автобоем), строки сети — в чат
/// карты. Запуск без меню — для драйвера двух машин (<c>tools/net-duo.py</c>): <c>--net-host</c> / <c>--net-join</c>, ход по сценарию
/// <c>--net-script</c>, конец — <c>--net-days</c>.
/// </summary>
public partial class MapScreen
{
    /// <summary>Сетевая партия; null — обычная.</summary>
    public INetPlay? Net { get; set; }

    /// <summary>Ход по сценарию (драйвер): зерно хода человека (<see cref="ScriptedHuman"/>); null — ходит человек.</summary>
    private Random? _netScript;

    /// <summary>Драйвер: партия кончается, когда начался день больше этого (у хоста — печать хеша и журнала, у клиента — хеша вида).</summary>
    private int? _netDays;

    private bool _netScriptRunning;

    /// <summary>Драйвер, сценарий обрыва: в этот день посреди своего хода оборвать связь с посредником (как пропавшая сеть) — дальше
    /// переподключение и resync сами.</summary>
    private int? _netDropAt;

    /// <summary>Подключить сеть к виду карты (из <see cref="_Ready"/>, после показа партии).</summary>
    private void WireNet(string[] args)
    {
        if (Net is not { } net) return;
        _map.Net = net;
        if (MapSource.Arg(args, "--net-script") is { } seed)
        {
            _netScript = new Random(int.Parse(seed, System.Globalization.CultureInfo.InvariantCulture));
            _map.AiSpeed = MapView.CheckAiSpeed;   // ходы ИИ — быстро, как в проверке; бои с человеком — автобоем экрана
        }
        if (MapSource.Arg(args, "--net-days") is { } days) _netDays = int.Parse(days, System.Globalization.CultureInfo.InvariantCulture);
        if (MapSource.Arg(args, "--net-drop-at") is { } drop) _netDropAt = int.Parse(drop, System.Globalization.CultureInfo.InvariantCulture);
        net.Changed += () => Callable.From(() => { if (IsInsideTree()) _map.NetChanged(); }).CallDeferred();
        net.TurnChanged += () => Callable.From(OnNetTurn).CallDeferred();
        net.Notice += text => Callable.From(() => Chat(text)).CallDeferred();
        net.Log += text => GD.Print(text);
        _map.AiTurnFinished += () => Callable.From(OnNetTurn).CallDeferred();
        string role = net is NetSession room ? room.IsHost ? "host" : "client" : "async";
        GD.Print($"net {role} room={net.Room} me={net.Me} day={net.Session.Map.Turn} active={net.Active}");
        Callable.From(OnNetTurn).CallDeferred();
    }

    /// <summary>Каждый кадр: почта сети, свой срок хода, ход по сценарию.</summary>
    private void StepNet()
    {
        if (Net is not { } net) return;
        net.Pump();
        if (_netFinished) return;   // итог напечатан — партия стоит до выхода
        if (_netDays is { } last && net.Session.Map.Turn > last && (net is NetSession { IsHost: false } || net.MyTurn || net.Over))
        {
            FinishNet(net);   // каждый кадр: смена хода могла прийти раньше дельты нового дня
            return;
        }
        var client = (net as NetSession)?.Client;
        if (_netScript is not null && (_netBeat += 1) % 600 == 0)
            GD.Print($"net beat day={net.Session.Map.Turn} active={net.Active} mine={(net.MyTurn ? 1 : 0)} ai={(_map.AiRunning ? 1 : 0)} " +
                     $"online={(client?.Online ?? true ? 1 : 0)} seq={client?.Seq ?? 0}");
        if (_netScript is not null && _map.AiRunning) DismissPopups();   // реплики ИИ и попапы в ходе ИИ — «Закрыть», как в проверке
        if (!net.MyTurn || _map.AiRunning || _map.Walking || client is { Online: false }) return;
        if (net.Deadline is { } deadline && DateTimeOffset.UtcNow > deadline)
        {
            GD.Print($"net срок хода вышел день={_map.Map.Turn}");
            if (_battle is not null) _battle.AutoFinish();
            else EndTurn();
            return;
        }
        if (_netScript is null || _netScriptRunning || _battle is not null) return;
        if (TurnWindowsPending)
        {
            // Окна начала хода (попапы событий, имя героя, навыки) — как шаг проверки «ok».
            DismissPopups();
            if (UpgradesPending) _ = AcceptUpgrades();
            return;
        }
        if (_overlay is null && _dialogViews.Count == 0) ScriptTurn(net);
        else if ((_netBlocked += 1) % 120 == 0)
            GD.Print($"net script ждёт: окно={_overlay?.Hosted ?? "-"} окон={_dialogViews.Count} {string.Join(",", _dialogViews.Select(v => v.Hosted))}");
    }

    private int _netBlocked, _netBeat;

    /// <summary>Сменился ход: не свой — ждать в режиме хода ИИ; свой — панель команд (её возвращает конец режима хода ИИ); драйвер — конец
    /// партии по дню.</summary>
    private void OnNetTurn()
    {
        if (Net is not { } net || !IsInsideTree()) return;
        if (_netDays is { } days)
        {
            net.Pump();   // виды до смены хода — сначала (у клиента они ждут кадра)
            if (net.Session.Map.Turn > days && (net is NetSession { IsHost: false } || net.MyTurn || net.Over))
            {
                FinishNet(net);
                return;
            }
        }
        if (!net.MyTurn && !net.Over && !_map.AiRunning) _map.WaitOthers();
    }

    /// <summary>Ход по сценарию: команды хода (<see cref="ScriptedHuman.Turn"/>) без конца хода, затем «Конец хода» панели — ходы ИИ и
    /// ожидание чужого хода идут, как у человека.</summary>
    private void ScriptTurn(INetPlay net)
    {
        _netScriptRunning = true;
        try
        {
            var session = _map.Session;
            if (_netDropAt is { } dropDay && session.Map.Turn >= dropDay)
            {
                _netDropAt = null;
                GD.Print($"net drop day={session.Map.Turn}");
                (net as NetSession)?.Link.Abort();
                return;   // ход — когда связь вернётся (StepNet снова позовёт)
            }
            ScriptedHuman.Turn(session, net.Me, c => session.Execute(c), _netScript!, endTurn: false);
            _map.NetChanged();
            GD.Print($"net script day={session.Map.Turn} me={net.Me}");
            EndTurn();
        }
        catch (NetCommandException e)
        {
            GD.PrintErr($"net script: {e.Code}: {e.Message}");
        }
        finally
        {
            _netScriptRunning = false;
        }
    }

    /// <summary>Драйвер: партия доиграна до дня — печать итога (хост — хеш партии, клиент — хеш вида) и выход; журнал хоста пишет выход
    /// сцены (<c>D2_COMMAND_JOURNAL</c>).</summary>
    private void FinishNet(INetPlay play)
    {
        if (play is not NetSession net)
        {
            GD.Print($"net-done async day={play.Session.Map.Turn} me={play.Me}");
            GetTree().Quit(0);
            return;
        }
        string result = net.IsHost
            ? $"net-done host day={net.Session.Map.Turn} hash={LastEdict.Data.Play.Commands.StateHash.Of(net.Session)} commands={net.Session.Journal?.Entries.Count ?? 0}"
            : $"net-done client day={net.Session.Map.Turn} player={net.Me} view={net.Client!.ViewHash} bytes={net.Link.BytesReceived}";
        if (_netFinished) return;
        _netFinished = true;
        GD.Print(result);
        // Хост — не сразу: последние дельты и смена хода ещё в очереди отправки, клиент должен их получить.
        if (net.IsHost) GetTree().CreateTimer(3.0).Timeout += () => GetTree().Quit(0);
        else GetTree().Quit(0);
    }

    private bool _netFinished;

    // ---- запуск без меню (драйвер)

    /// <summary>Сетевая партия из аргументов: <c>--net-host &lt;сценарий&gt; --net-seats хозяин,гость…</c> (создать комнату, код — в
    /// <c>--net-room-out</c>) или <c>--net-join &lt;код&gt; [--net-seat игрок]</c>; посредник — <c>--net-relay</c> (или <c>LE_RELAY</c>). Без
    /// этих ключей — null.</summary>
    private static INetPlay? LaunchNet(string[] args)
    {
        string? hosted = MapSource.Arg(args, "--net-host"), joined = MapSource.Arg(args, "--net-join");
        var relay = new Uri(MapSource.Arg(args, "--net-relay") ?? System.Environment.GetEnvironmentVariable("LE_RELAY") ?? "ws://127.0.0.1:8790/ws");
        string nick = MapSource.Arg(args, "--net-nick") ?? System.Environment.MachineName;
        if (LaunchAsync(args, relay, nick) is { } async) return async;
        if (hosted is null && joined is null) return null;
        var ruleset = Ruleset();
        if (hosted is not null)
        {
            var seats = (MapSource.Arg(args, "--net-seats") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (seats.Length < 2) throw new InvalidOperationException("--net-seats: хозяин и хотя бы один гость");
            uint seed = uint.TryParse(MapSource.Arg(args, "--net-seed"), out uint s) ? s : (uint)System.Environment.TickCount;
            var session = NewNetGame(hosted, seats, seed);
            int? turn = int.TryParse(MapSource.Arg(args, "--net-turn-seconds"), out int t) ? t : null;
            var net = NetSession.HostAsync(relay, session, seats[0], seats[1..], nick, ruleset, turnSeconds: turn).GetAwaiter().GetResult();
            if (MapSource.Arg(args, "--net-room-out") is { } file) File.WriteAllText(file, net.Room);
            GD.Print($"net-room {net.Room}");
            return net;
        }
        var join = new NetJoin(relay, nick, ruleset, "", view => new GameSession(view, GameData.Content.MapArt, GameData.Book), AutoApply: false);
        return NetSession.JoinAsync(join, joined!, seat: MapSource.Arg(args, "--net-seat")).GetAwaiter().GetResult();
    }

    /// <summary>Асинхронная комната без меню (драйвер): <c>--net-async-create &lt;сценарий&gt; --net-seats …</c> (создать, ключ — в
    /// <c>--net-key-out</c>), <c>--net-async-join &lt;код&gt; --net-seat &lt;игрок&gt;</c> или <c>--net-async-open &lt;файл ключа&gt;</c>. После своего хода
    /// с <c>--net-async-quit</c> игра записывает ход, журнал хода (<c>--net-journal-out</c>) и выходит — «сходил и закрыл игру».</summary>
    private static AsyncPlay? LaunchAsync(string[] args, Uri relay, string nick)
    {
        string? create = MapSource.Arg(args, "--net-async-create"), join = MapSource.Arg(args, "--net-async-join"),
                open = MapSource.Arg(args, "--net-async-open");
        if (create is null && join is null && open is null) return null;
        AsyncRoom room;
        if (create is not null)
        {
            var seats = (MapSource.Arg(args, "--net-seats") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (seats.Length < 2) throw new InvalidOperationException("--net-seats: хозяин и хотя бы один гость");
            uint seed = uint.TryParse(MapSource.Arg(args, "--net-seed"), out uint s) ? s : (uint)System.Environment.TickCount;
            var start = NewNetGame(create, seats, seed);
            room = AsyncRoom.CreateAsync(relay, start, seats[0], nick).GetAwaiter().GetResult();
            if (MapSource.Arg(args, "--net-start-out") is { } startFile)
                GameSave.Write(startFile, start.SnapshotWithoutSummaries());   // начало для повтора по ходам (драйвер)
        }
        else if (join is not null) room = AsyncRoom.OpenAsync(relay, null, join, MapSource.Arg(args, "--net-seat") ?? "", nick).GetAwaiter().GetResult();
        else room = AsyncRoom.OpenAsync(relay, System.Text.Json.JsonSerializer.Deserialize<RoomKey>(File.ReadAllText(open!))).GetAwaiter().GetResult();
        if (MapSource.Arg(args, "--net-key-out") is { } keyFile) File.WriteAllText(keyFile, System.Text.Json.JsonSerializer.Serialize(room.Key));
        GD.Print($"net-room {room.Key.Room}");
        var play = AsyncPlay.OpenAsync(room, state => new GameSession(state, GameData.Content.MapArt, GameData.Book)).GetAwaiter().GetResult();
        if (args.Contains("--net-async-quit"))
        {
            string? journalFile = MapSource.Arg(args, "--net-journal-out");
            play.Stored += (rev, hash, journal) =>
            {
                if (journalFile is not null) File.WriteAllText(journalFile, journal);
                GD.Print($"net-async stored rev={rev} hash={hash} day={play.Session.Map.Turn} next={play.Session.ActiveHuman}");
                Callable.From(() => (Engine.GetMainLoop() as SceneTree)?.Quit(0)).CallDeferred();   // сходил — закрыл игру
            };
        }
        return play;
    }

    /// <summary>Набор правил этой игры: база содержимого и включённые моды (графические — не в счёт).</summary>
    private static Ruleset Ruleset() => RulesetHash.Of(GameData.RulesDir, GameData.Mods);

    /// <summary>Новая партия сценария с людьми на местах <paramref name="seats"/> (игроки сценария), как из лобби: раса — игрока сценария,
    /// лорд — воин.</summary>
    private static GameSession NewNetGame(string scenarioId, string[] seats, uint seed)
    {
        var catalog = GameData.Catalog;
        var scenario = catalog.Scenario(scenarioId) ?? throw new InvalidOperationException($"сценария «{scenarioId}» нет");
        var data = GameData.ReadScenario(GameData.Content.ScenarioPath(scenario.File.Length > 0 ? Path.GetFileNameWithoutExtension(scenario.File) : scenario.Id));
        var players = data.Looks.Map.Players;
        var setup = new NewGameSetup
        {
            Scenario = scenario, Difficulty = 1,
            Seats = [.. seats.Select(id => players.TryGetValue(id, out var p)
                ? new PlayerSeat(p.RaceId, id, 0, 1, PlayerId: id)
                : throw new InvalidOperationException($"в сценарии нет игрока {id}"))],
        };
        var begun = LastEdict.Data.Play.NewGame.Begin(data, setup, catalog, GameData.Book, GameData.Content.MapArt, reseed: seed);
        // Партия комнаты начинается с записанного и прочитанного состояния: чтение кое-что упорядочивает (известные заклинания — как
        // движок при загрузке), и сохранение начала журнала, асинхронная комната и возвращение хоста видят ту же партию, что идёт.
        var read = GameSave.FromJson(GameSave.ToJson(begun.SnapshotWithoutSummaries(), "", DateTime.UnixEpoch));
        return new GameSession(read, GameData.Content.MapArt, GameData.Book) { EventBattlesOnScreen = false };
    }
}
