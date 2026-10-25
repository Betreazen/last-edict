using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using LastEdict.Data.Menu;
using LastEdict.Net;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Лобби «Мультиплеер» на <c>Control</c> (Э7, <c>docs/network.md</c> §10), в теме окон Last Edict (<see cref="SettingsTheme"/>): сверху —
/// посредник и имя, вкладки «Создать» (сценарий, места «я / друг / ИИ», таймер хода, асинхронная партия → код комнаты, вошедшие друзья,
/// «Начать»), «Войти по коду», «Мои комнаты» (асинхронные — с «ваш ход»), внизу — строка состояния и «Закрыть». Выбор держит модель
/// (<see cref="LobbyLogic"/>), сетевые шаги — здесь (<see cref="NetSession"/>, <see cref="AsyncRoom"/>, <see cref="AsyncPlay"/>); партия
/// начинается через <see cref="Started"/> (хозяин — поток меню). Продолжения после <c>await</c> — на главном потоке; окно закрыли посреди
/// подключения — шаги отменяются (<see cref="_cancel"/>), а дошедшая партия закрывается, не открываясь.
/// </summary>
public partial class LobbyWindow : Control
{
    private static readonly Rect2 Frame = new(30, 22, 740, 556);

    /// <summary>Партия сети готова — хозяин (поток меню) открывает карту.</summary>
    public static Action<INetPlay>? Started { get; set; }

    private readonly LobbyLogic _logic;
    private readonly Func<string, string> _text;
    private readonly Action<string> _emit;
    private LineEdit _relay = null!, _nick = null!, _code = null!;
    private OptionButton _scenario = null!, _timer = null!;
    private CheckBox _async = null!;
    private VBoxContainer _seats = null!, _joined = null!, _rooms = null!;
    private Label _status = null!, _room = null!;
    private Button _create = null!, _start = null!, _join = null!;
    private TabContainer _tabs = null!;
    private RelayLink? _link;
    /// <summary>Вошедшие друзья по порядку входа: соединение у посредника и ник (ники могут совпадать).</summary>
    private readonly List<(string Peer, string Nick)> _friends = [];
    private readonly CancellationTokenSource _cancel = new();
    private bool _busy, _refreshing;

    /// <summary>Окно ещё открыто: продолжение после сети может трогать узлы и открывать партию.</summary>
    private bool Alive => !_cancel.IsCancellationRequested && IsInstanceValid(this) && IsInsideTree();

    public LobbyWindow(LobbyLogic logic, Func<string, string> text, Action<string> emit)
    {
        _logic = logic;
        _text = text;
        _emit = emit;
        Name = "LobbyWindow";
        Size = new Vector2(800, 600);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = SettingsTheme.Theme;
        TextureFilter = TextureFilterEnum.Linear;
    }

    private string T(string key) => _text(key);

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.55f), Position = new Vector2(-2000, -2000), Size = Size + new Vector2(4000, 4000),
                                 MouseFilter = MouseFilterEnum.Stop });
        var frame = new PanelContainer { Position = Frame.Position, Size = Frame.Size };
        AddChild(frame);
        var inner = new PanelContainer { ThemeTypeVariation = SettingsTheme.Inner };
        frame.AddChild(inner);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        inner.AddChild(column);
        column.AddChild(new Label { Text = T("LE_NET_TITLE"), ThemeTypeVariation = SettingsTheme.Title, HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new TextureRect { Texture = SettingsTheme.Divider(), StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                                          CustomMinimumSize = new Vector2(0, 14) });
        _logic.Adopt(NetPrefs.Load(NetGames.PrefsPath));
        _relay = new LineEdit { Text = _logic.Relay, SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = "Relay", PlaceholderText = LobbyLogic.RelayHint };
        _relay.TextChanged += t => _logic.Relay = t.Trim();
        _nick = new LineEdit { Text = _logic.Nick, CustomMinimumSize = new Vector2(180, 0), MaxLength = 32, Name = "Nick" };
        _nick.TextChanged += t => _logic.Nick = t.Trim();
        var top = Row("LE_NET_RELAY", _relay);
        ((HBoxContainer)top.GetChild(0)).AddChild(new Label { Text = T("LE_NET_NICK"), ThemeTypeVariation = SettingsTheme.RowLabel });
        ((HBoxContainer)top.GetChild(0)).AddChild(_nick);
        column.AddChild(top);
        var tabs = _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, TabAlignment = TabBar.AlignmentMode.Center, Name = "Tabs" };
        tabs.AddChild(CreatePage());
        tabs.AddChild(JoinPage());
        tabs.AddChild(RoomsPage());
        tabs.CurrentTab = (int)_logic.Tab;
        tabs.TabChanged += index =>
        {
            _logic.Tab = (LobbyTab)(int)index;
            if (_logic.Tab == LobbyTab.Rooms) _ = Guard(RefreshRooms);
        };
        column.AddChild(tabs);
        column.AddChild(new ColorRect { Color = SettingsTheme.Gold with { A = 0.35f }, CustomMinimumSize = new Vector2(0, 1) });
        var footer = new HBoxContainer();
        _status = new Label { ThemeTypeVariation = SettingsTheme.HelpLabel, SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        footer.AddChild(_status);
        footer.AddChild(Command("LE_NET_CLOSE", Leave));
        column.AddChild(footer);
        RefreshSeats();
        Callable.From(() => _scenario.GrabFocus()).CallDeferred();
    }

    // ---- вкладки

    private Control CreatePage()
    {
        var page = Page("LE_NET_TAB_CREATE");
        _scenario = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, Name = "Scenario", FocusMode = FocusModeEnum.All };
        foreach (var s in _logic.Scenarios) _scenario.AddItem($"{s.Name} ({s.MapSize}×{s.MapSize})");
        _scenario.Selected = _logic.ScenarioIndex;
        _scenario.ItemSelected += i =>
        {
            _logic.SelectScenario((int)i);
            RefreshSeats();
        };
        page.AddChild(Row("LE_NET_SCENARIO", _scenario));
        _seats = new VBoxContainer();
        _seats.AddThemeConstantOverride("separation", 3);
        page.AddChild(_seats);
        _timer = new OptionButton { Name = "Timer", FocusMode = FocusModeEnum.All };
        foreach (int seconds in LobbyLogic.Timers) _timer.AddItem(seconds == 0 ? T("LE_NET_TIMER_OFF") : string.Format(System.Globalization.CultureInfo.InvariantCulture, T("LE_NET_TIMER_MIN"), seconds / 60));
        _timer.ItemSelected += i => _logic.TimerIndex = (int)i;
        page.AddChild(Row("LE_NET_TIMER", _timer));
        _async = new CheckBox { Name = "Async", FocusMode = FocusModeEnum.All };
        _async.Toggled += on => _logic.Async = on;
        page.AddChild(Row("LE_NET_ASYNC", _async));
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _create = Command("LE_NET_CREATE", () => _ = Guard(Create));
        _create.ThemeTypeVariation = SettingsTheme.Primary;
        _start = Command("LE_NET_START", () => _ = Guard(Begin));
        _start.Visible = false;
        _start.ThemeTypeVariation = SettingsTheme.Primary;
        actions.AddChild(_create);
        actions.AddChild(_start);
        page.AddChild(actions);
        _room = new Label { ThemeTypeVariation = SettingsTheme.Title, HorizontalAlignment = HorizontalAlignment.Center };
        page.AddChild(_room);
        _joined = new VBoxContainer();
        page.AddChild(_joined);
        return Scroll(page, "LE_NET_TAB_CREATE");
    }

    private Control JoinPage()
    {
        var page = Page("LE_NET_TAB_JOIN");
        _code = new LineEdit { CustomMinimumSize = new Vector2(160, 0), MaxLength = 8, Name = "Code", PlaceholderText = "ABC234" };
        _code.TextChanged += t => _logic.Code = LobbyLogic.NormalizeCode(t);
        _code.TextSubmitted += submitted => { if (!_busy) _ = Guard(Join); };
        page.AddChild(Row("LE_NET_CODE", _code));
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _join = Command("LE_NET_JOIN", () => _ = Guard(Join));
        _join.ThemeTypeVariation = SettingsTheme.Primary;
        actions.AddChild(_join);
        page.AddChild(actions);
        return Scroll(page, "LE_NET_TAB_JOIN");
    }

    private Control RoomsPage()
    {
        var page = Page("LE_NET_TAB_ROOMS");
        _rooms = new VBoxContainer();
        _rooms.AddThemeConstantOverride("separation", 3);
        page.AddChild(_rooms);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddChild(Command("LE_NET_REFRESH", () => _ = Guard(RefreshRooms)));
        page.AddChild(actions);
        return Scroll(page, "LE_NET_TAB_ROOMS");
    }

    private void RefreshSeats()
    {
        foreach (var child in _seats.GetChildren()) child.QueueFree();
        for (int i = 0; i < _logic.Seats.Count; i++)
        {
            int index = i;
            var seat = _logic.Seats[i];
            var kind = new Button { Text = T(seat.Kind switch { SeatKind.Me => "LE_NET_SEAT_ME", SeatKind.Friend => "LE_NET_SEAT_FRIEND", _ => "LE_NET_SEAT_AI" }),
                                    CustomMinimumSize = new Vector2(160, 26), FocusMode = FocusModeEnum.All, Disabled = _link is not null };
            kind.Pressed += () =>
            {
                _logic.CycleSeat(index);
                RefreshSeats();
            };
            _seats.AddChild(Row(seat.RaceName, kind, raw: true, alternate: i % 2 == 1));
        }
    }

    // ---- создать комнату

    private async Task Create()
    {
        if (_busy || _link is not null) return;
        if (_logic.CreateRefusal() is { } refusal)
        {
            Say(T(refusal));
            return;
        }
        _busy = true;
        try
        {
            var scenario = _logic.Scenario!;
            var me = _logic.Mine!;
            if (_logic.Async)
            {
                // Асинхронная: люди — я и все места друзей; партия — у посредника сразу, друзья входят по коду в свой ход.
                var humans = _logic.HumansFor(_logic.Friends.Count);
                var start = NetGames.NewGame(scenario.Id, humans, (uint)System.Environment.TickCount, Names(humans));
                Say(T("LE_NET_CONNECTING"));
                var room = await AsyncRoom.CreateAsync(Relay(), start, me.Player, _logic.Nick, scenario.Name, _cancel.Token);
                try
                {
                    NetRooms.Remember(NetGames.RoomsPath, new NetRoomEntry(room.Key, Added: DateTime.UtcNow));
                    if (Alive) _room.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, T("LE_NET_ROOM_CODE"), room.Key.Room);
                    await Launch(await AsyncPlay.OpenAsync(room, NetGames.ViewSession, _cancel.Token));
                }
                catch
                {
                    await room.DisposeAsync();   // партия не открылась — соединение не держать
                    throw;
                }
                return;
            }
            Say(T("LE_NET_CONNECTING"));
            _link = new RelayLink(Relay());
            await _link.ConnectAsync(_cancel.Token);
            var key = await _link.CreateAsync(scenario.Name, _logic.Nick, async: false, seat: me.Player, _cancel.Token);
            if (!Alive) return;   // окно закрыли — соединение сносит _ExitTree
            _link.Notice += notice => Callable.From(() => OnPeer(notice)).CallDeferred();
            _room.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, T("LE_NET_ROOM_CODE"), key.Room);
            _create.Visible = false;
            _start.Visible = true;
            RefreshSeats();
            RefreshJoined();
            Say(T("LE_NET_WAIT_FRIENDS"));
            GD.Print($"lobby room {key.Room} scenario={scenario.Id.Replace(' ', '_')} seats={string.Join(",", _logic.Seats.Select(s => $"{s.Player}:{s.Kind}"))}");
        }
        catch (Exception e) when (e is RelayException or System.Net.WebSockets.WebSocketException or InvalidOperationException
                                      or System.Net.Http.HttpRequestException or OperationCanceledException or UriFormatException)
        {
            Say(T("LE_NET_FAILED") + " " + e.Message);
            if (Alive) await DropLink();
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnPeer(JsonObject notice)
    {
        if (!Alive) return;
        string op = notice["op"]?.GetValue<string>() ?? "";
        string peer = notice["peer"]?.GetValue<string>() ?? "";
        string nick = notice["nick"]?.GetValue<string>() is { Length: > 0 } n ? n : peer.Length > 0 ? peer : "?";
        if (op == "peer" && notice["back"]?.GetValue<bool>() != true && _friends.All(f => f.Peer != peer)) _friends.Add((peer, nick));
        if (op == "gone") _friends.RemoveAll(f => f.Peer == peer);
        RefreshJoined();
    }

    private void RefreshJoined()
    {
        foreach (var child in _joined.GetChildren()) child.QueueFree();
        var friends = _logic.Friends;
        for (int i = 0; i < friends.Count; i++)
        {
            string who = i < _friends.Count ? _friends[i].Nick : T("LE_NET_SEAT_EMPTY");
            _joined.AddChild(new Label { Text = $"{friends[i].RaceName}: {who}", ThemeTypeVariation = SettingsTheme.RowLabel });
        }
        _start.Disabled = _friends.Count == 0;
    }

    /// <summary>«Начать»: люди — я и места друзей по порядку входа, прочие места друзей — ИИ; хост — на этой же комнате.</summary>
    private async Task Begin()
    {
        if (_busy || _link is null || _friends.Count == 0) return;
        _busy = true;
        try
        {
            var humans = _logic.HumansFor(Math.Min(_friends.Count, _logic.Friends.Count));
            var session = NetGames.NewGame(_logic.Scenario!.Id, humans, (uint)System.Environment.TickCount, Names(humans));
            var net = NetSession.HostOn(_link, session, _logic.Mine!.Player, humans.Skip(1).ToList(), NetGames.Ruleset(), turnSeconds: _logic.TurnSeconds);
            NetRooms.Remember(NetGames.RoomsPath, new NetRoomEntry(_link.Key!, Added: DateTime.UtcNow));
            _link = null;   // соединение теперь у партии
            GD.Print($"lobby start humans={string.Join(",", humans)} timer={_logic.TurnSeconds ?? 0}");
            await Launch(net);
        }
        catch (InvalidOperationException e)
        {
            Say(T("LE_NET_FAILED") + " " + e.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Имена лордов людей: моё — из поля «Имя», друзей — по их нику, если вошли.</summary>
    private Dictionary<string, string> Names(IReadOnlyList<string> humans)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_logic.Nick.Length > 0) names[humans[0]] = _logic.Nick;
        for (int i = 1; i < humans.Count && i - 1 < _friends.Count; i++) names[humans[i]] = _friends[i - 1].Nick;
        return names;
    }

    // ---- войти

    private async Task Join()
    {
        if (_busy) return;
        string code = _logic.Code;
        if (code.Length != 6)
        {
            Say(T("LE_NET_BAD_CODE"));
            return;
        }
        _busy = true;
        RelayLink? link = null;
        try
        {
            Say(T("LE_NET_CONNECTING"));
            link = new RelayLink(Relay());
            await link.ConnectAsync(_cancel.Token);
            var key = await link.JoinAsync(code, _logic.Nick, cancel: _cancel.Token);
            if (key.Async)
            {
                // Асинхронная: место выбирается по партии — первое свободное место человека.
                var room = AsyncRoom.FromLink(link);
                var stored = await room.LoadAsync(_cancel.Token);
                var free = stored.State is { } state ? AsyncRoom.FreeSeats(state, link.Peers) : [];
                if (free.Count == 0)
                {
                    Say(T("LE_NET_NO_SEAT"));
                    return;   // соединение закроет finally
                }
                await link.ClaimSeatAsync(free[0], _cancel.Token);
                NetRooms.Remember(NetGames.RoomsPath, new NetRoomEntry(link.Key!, Added: DateTime.UtcNow));
                var play = await AsyncPlay.OpenAsync(room, NetGames.ViewSession, _cancel.Token);
                link = null;
                await Launch(play);
                return;
            }
            Say(T("LE_NET_WAIT_HOST"));
            var join = new NetJoin(Relay(), _logic.Nick, NetGames.Ruleset(), "", NetGames.ViewSession, AutoApply: false);
            var net = await NetSession.JoinOn(link, join, null, null, TimeSpan.FromMinutes(30), _cancel.Token);
            NetRooms.Remember(NetGames.RoomsPath, new NetRoomEntry(link.Key!, net.Client?.Token, DateTime.UtcNow));
            link = null;
            await Launch(net);
        }
        catch (Exception e) when (e is RelayException or NetCommandException or System.Net.WebSockets.WebSocketException or InvalidOperationException
                                      or InvalidDataException or System.Net.Http.HttpRequestException or OperationCanceledException or UriFormatException)
        {
            Say(T("LE_NET_FAILED") + " " + e.Message);
        }
        finally
        {
            if (link is not null) await link.DisposeAsync();   // не дошли до партии — соединение не держать
            _busy = false;
        }
    }

    // ---- мои комнаты

    private async Task RefreshRooms()
    {
        if (_refreshing) return;   // одно обновление за раз: два подряд задвоили бы строки
        _refreshing = true;
        try
        {
            await FillRooms();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task FillRooms()
    {
        foreach (var child in _rooms.GetChildren()) child.QueueFree();
        var entries = NetRooms.Load(NetGames.RoomsPath);
        if (entries.Count == 0)
        {
            _rooms.AddChild(new Label { Text = T("LE_NET_NO_ROOMS"), ThemeTypeVariation = SettingsTheme.RowLabel });
            return;
        }
        JsonArray list;
        try
        {
            list = await AsyncRoom.ListAsync(Relay(), entries.Select(e => e.Key), _cancel.Token);
        }
        catch (Exception e) when (e is RelayException or System.Net.WebSockets.WebSocketException or OperationCanceledException or UriFormatException)
        {
            Say(T("LE_NET_FAILED") + " " + e.Message);
            return;
        }
        if (!Alive) return;
        var live = list.OfType<JsonObject>().Where(r => r["room"] is not null)
                       .ToDictionary(r => r["room"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        int row = 0;
        foreach (var entry in entries)
        {
            if (!live.TryGetValue(entry.Key.Room, out var info)) continue;   // комнаты на посреднике больше нет
            bool mine = info["yourTurn"]?.GetValue<bool>() == true;
            string text = $"{info["name"]?.GetValue<string>()} · {entry.Key.Room} · {T(entry.Key.Async ? "LE_NET_ASYNC_SHORT" : "LE_NET_SYNC_SHORT")}"
                          + (entry.Key.Async ? $" · {string.Format(System.Globalization.CultureInfo.InvariantCulture, T("LE_NET_DAY"), info["day"]?.GetValue<int>() ?? 0)}" : "")
                          + (mine ? $" · {T("LE_NET_YOUR_TURN")}" : "");
            var open = new Button { Text = T("LE_NET_OPEN"), FocusMode = FocusModeEnum.All };
            open.Pressed += () => _ = Guard(() => Open(entry));
            _rooms.AddChild(Row(text, open, raw: true, alternate: row++ % 2 == 1));
        }
        if (row == 0) _rooms.AddChild(new Label { Text = T("LE_NET_NO_ROOMS"), ThemeTypeVariation = SettingsTheme.RowLabel });
    }

    private async Task Open(NetRoomEntry entry)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            Say(T("LE_NET_CONNECTING"));
            if (entry.Key.Async)
            {
                var room = await AsyncRoom.OpenAsync(Relay(), entry.Key, cancel: _cancel.Token);
                try
                {
                    await Launch(await AsyncPlay.OpenAsync(room, NetGames.ViewSession, _cancel.Token));
                }
                catch
                {
                    await room.DisposeAsync();
                    throw;
                }
                return;
            }
            var join = new NetJoin(Relay(), _logic.Nick, NetGames.Ruleset(), "", NetGames.ViewSession, AutoApply: false);
            await Launch(await NetSession.JoinAsync(join, entry.Key.Room, rejoin: entry.Key, token: entry.Token, cancel: _cancel.Token));
        }
        catch (Exception e) when (e is RelayException or NetCommandException or System.Net.WebSockets.WebSocketException or InvalidOperationException
                                      or InvalidDataException or OperationCanceledException or UriFormatException)
        {
            Say(T("LE_NET_FAILED") + " " + e.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- общее

    /// <summary>Посредник из поля окна; удачный вход — посредник и имя запоминаются (<see cref="NetPrefs"/>).</summary>
    private Uri Relay()
    {
        if (_logic.Relay.Length == 0) throw new UriFormatException(T("LE_NET_NO_RELAY"));
        var uri = new Uri(_logic.Relay);
        try
        {
            new NetPrefs(_logic.Relay, _logic.Nick).Save(NetGames.PrefsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"lobby: настройки сети не записаны: {e.Message}");
        }
        return uri;
    }

    /// <summary>Партия готова: окно открыто — в игру; закрыли, пока шло подключение, — партию закрыть (игрок ушёл из лобби).</summary>
    private async Task Launch(INetPlay play)
    {
        if (!Alive)
        {
            GD.Print($"lobby: окно закрыто — партия {play.Room} не открывается");
            if (play is IAsyncDisposable disposable) await disposable.DisposeAsync();
            return;
        }
        GD.Print($"lobby launch room={play.Room} me={play.Me} {(play is AsyncPlay ? "async" : "sync")}");
        Started?.Invoke(play);
    }

    /// <summary>Шаг кнопки: исключение, не разобранное самим шагом, — в строку состояния и журнал, а не в ничейную задачу.</summary>
    private async Task Guard(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            GD.PrintErr($"lobby: {e.GetType().Name}: {e.Message}");
            if (Alive) Say(T("LE_NET_FAILED") + " " + e.Message);
            _busy = false;
        }
    }

    private void Say(string text)
    {
        _logic.Status = text;
        if (IsInstanceValid(_status)) _status.Text = text;
    }

    private void Leave()
    {
        _cancel.Cancel();   // идущие подключения — прочь
        _ = DropLink();
        _emit(LobbyLogic.Close);
    }

    private async Task DropLink()
    {
        if (_link is { } link)
        {
            _link = null;
            await link.DisposeAsync();
        }
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        _create.Visible = true;
        _start.Visible = false;
        _room.Text = "";
        _friends.Clear();
        RefreshSeats();
        RefreshJoined();
    }

    public override void _ExitTree()
    {
        _cancel.Cancel();
        if (_link is { } link)
        {
            _link = null;
            _ = link.DisposeAsync().AsTask();
        }
    }

    private VBoxContainer Page(string name)
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 4);
        return page;
    }

    private Control Scroll(Control page, string title)
    {
        var scroll = new ScrollContainer { Name = T(title), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(page);
        return scroll;
    }

    private Control Row(string label, Control input, bool raw = false, bool alternate = false)
    {
        var panel = new PanelContainer { ThemeTypeVariation = alternate ? SettingsTheme.RowPanelAlt : SettingsTheme.RowPanel, CustomMinimumSize = new Vector2(0, 34) };
        var line = new HBoxContainer();
        panel.AddChild(line);
        line.AddChild(new Label
        {
            Text = raw ? label : T(label), ThemeTypeVariation = SettingsTheme.RowLabel, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center, ClipText = true,
        });
        line.AddChild(input);
        return panel;
    }

    private Button Command(string label, Action action)
    {
        var button = new Button { Text = T(label), Name = label, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 30) };
        button.Pressed += action;
        return button;
    }

    /// <summary>Шаг сценария проверки: «lobby tab N» — вкладка, «lobby seat N» — следующий вариант места, «lobby scenario N» — сценарий;
    /// прочее — не окну.</summary>
    public bool Step(string verb, string arg)
    {
        if (verb != "lobby") return false;
        var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        int n = parts.Length > 1 && int.TryParse(parts[1], out int v) ? v : 0;
        switch (parts[0])
        {
            case "tab": _tabs.CurrentTab = n; break;
            case "seat": _logic.CycleSeat(n); RefreshSeats(); break;
            case "scenario": _logic.SelectScenario(n); _scenario.Selected = n; RefreshSeats(); break;
            default: return false;
        }
        return true;
    }

    /// <summary>Строка проверки окна (ui-render-check): вкладка, сценарий, места, состояние.</summary>
    public string Line() =>
        $"lobby вкладка={_logic.Tab} сценарий={_logic.Scenario?.Id.Replace(' ', '_') ?? "-"} мест={_logic.Seats.Count} " +
        $"места={string.Join(",", _logic.Seats.Select(s => s.Kind))} комната={(_room?.Text.Length > 0 ? 1 : 0)} состояние={_logic.Status.Replace(' ', '_')}";
}
