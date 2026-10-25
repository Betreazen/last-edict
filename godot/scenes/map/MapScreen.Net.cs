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
    public NetSession? Net { get; set; }

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
        GD.Print($"net {(net.IsHost ? "host" : "client")} room={net.Room} me={net.Me} day={net.Session.Map.Turn} active={net.Active}");
        Callable.From(OnNetTurn).CallDeferred();
    }

    /// <summary>Каждый кадр: почта сети, свой срок хода, ход по сценарию.</summary>
    private void StepNet()
    {
        if (Net is not { } net) return;
        net.Pump();
        if (_netFinished) return;   // итог напечатан — партия стоит до выхода
        if (_netDays is { } last && net.Session.Map.Turn > last && (!net.IsHost || net.MyTurn || net.Over))
        {
            FinishNet(net);   // каждый кадр: смена хода могла прийти раньше дельты нового дня
            return;
        }
        if (_netScript is not null && (_netBeat += 1) % 600 == 0)
            GD.Print($"net beat day={net.Session.Map.Turn} active={net.Active} mine={(net.MyTurn ? 1 : 0)} ai={(_map.AiRunning ? 1 : 0)} " +
                     $"online={(net.Client?.Online ?? true ? 1 : 0)} seq={net.Client?.Seq ?? 0}");
        if (_netScript is not null && _map.AiRunning) DismissPopups();   // реплики ИИ и попапы в ходе ИИ — «Закрыть», как в проверке
        if (!net.MyTurn || _map.AiRunning || _map.Walking || net.Client is { Online: false }) return;
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
            if (net.Session.Map.Turn > days && (!net.IsHost || net.MyTurn || net.Over))
            {
                FinishNet(net);
                return;
            }
        }
        if (!net.MyTurn && !net.Over && !_map.AiRunning) _map.WaitOthers();
    }

    /// <summary>Ход по сценарию: команды хода (<see cref="ScriptedHuman.Turn"/>) без конца хода, затем «Конец хода» панели — ходы ИИ и
    /// ожидание чужого хода идут, как у человека.</summary>
    private void ScriptTurn(NetSession net)
    {
        _netScriptRunning = true;
        try
        {
            var session = _map.Session;
            if (_netDropAt is { } dropDay && session.Map.Turn >= dropDay)
            {
                _netDropAt = null;
                GD.Print($"net drop day={session.Map.Turn}");
                net.Link.Abort();
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
    private void FinishNet(NetSession net)
    {
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
    private static NetSession? LaunchNet(string[] args)
    {
        string? hosted = MapSource.Arg(args, "--net-host"), joined = MapSource.Arg(args, "--net-join");
        if (hosted is null && joined is null) return null;
        var relay = new Uri(MapSource.Arg(args, "--net-relay") ?? System.Environment.GetEnvironmentVariable("LE_RELAY") ?? "ws://127.0.0.1:8790/ws");
        string nick = MapSource.Arg(args, "--net-nick") ?? System.Environment.MachineName;
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
