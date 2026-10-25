using System.Text.Json;
using LastEdict.Data.Game;
using LastEdict.Data.Ui;
using LastEdict.Net;

namespace LastEdict.Data.Menu;

/// <summary>Кто сидит на месте игрока сценария в лобби: этот игрок, друг (войдёт по коду) или ИИ.</summary>
public enum SeatKind { Me, Friend, Ai }

/// <summary>Место лобби: игрок сценария, его раса (id и имя), кто за него.</summary>
public sealed record LobbySeat(string Player, string RaceId, string RaceName, SeatKind Kind);

/// <summary>Вкладки лобби.</summary>
public enum LobbyTab { Create, Join, Rooms }

/// <summary>
/// Лобби «Мультиплеер» (Э7, <c>docs/network.md</c> §10) — модель окна без движка: посредник и имя, вкладки «Создать» (сценарий, места:
/// я / друг / ИИ, таймер хода, асинхронная партия), «Войти по коду», «Мои комнаты». Сетевые шаги делает окно (<c>godot/scenes/net</c>) через
/// <see cref="NetSession"/> и <see cref="AsyncRoom"/>; модель держит выбор, проверяет его и раздаёт места вошедшим друзьям.
/// </summary>
public sealed class LobbyLogic : IScreenLogic
{
    public const string Screen = "LE_LOBBY";

    public const string Close = "BTN_CLOSE";

    /// <summary>Пример адреса посредника для поля окна. Своего адреса в репозитории нет (он не публикуется): адрес — переменная
    /// <c>LE_RELAY</c> или последний введённый (<see cref="NetPrefs"/>).</summary>
    public const string RelayHint = "ws://адрес:8790/ws";

    /// <summary>Варианты таймера хода, секунды (0 — без таймера).</summary>
    public static readonly IReadOnlyList<int> Timers = [0, 60, 120, 300, 600];

    private readonly GameCatalog _catalog;
    private readonly Func<ScenarioInfo, IReadOnlyList<(string Player, string RaceId)>> _players;
    private readonly Func<string, string> _text;

    /// <param name="players">Игроки сценария (id и раса) — из файла сценария; лобби показывает только играбельные расы.</param>
    public LobbyLogic(GameCatalog catalog, Func<string, string> text, Func<ScenarioInfo, IReadOnlyList<(string Player, string RaceId)>> players)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        var playable = catalog.Races.Where(r => r.Playable).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Scenarios = [.. catalog.Scenarios.Where(s => s.RaceIds.Count(playable.Contains) >= 2).OrderBy(s => s.Name, StringComparer.CurrentCulture)];
        if (Scenarios.Count > 0) SelectScenario(0);
    }

    public LobbyTab Tab { get; set; } = LobbyTab.Create;

    public string Relay { get; set; } = Environment.GetEnvironmentVariable("LE_RELAY") ?? "";

    public string Nick { get; set; } = Environment.UserName;

    /// <summary>Посредник и имя — из своих настроек сети (последние введённые); переменная <c>LE_RELAY</c> главнее.</summary>
    public void Adopt(NetPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        if (Environment.GetEnvironmentVariable("LE_RELAY") is not { Length: > 0 } && prefs.Relay.Length > 0) Relay = prefs.Relay;
        if (prefs.Nick.Length > 0) Nick = prefs.Nick;
    }

    /// <summary>Сценарии, где играбельных рас хотя бы две.</summary>
    public IReadOnlyList<ScenarioInfo> Scenarios { get; }

    public int ScenarioIndex { get; private set; } = -1;

    public ScenarioInfo? Scenario => ScenarioIndex >= 0 && ScenarioIndex < Scenarios.Count ? Scenarios[ScenarioIndex] : null;

    public IReadOnlyList<LobbySeat> Seats { get; private set; } = [];

    public int TimerIndex { get; set; }

    public int? TurnSeconds => Timers[Math.Clamp(TimerIndex, 0, Timers.Count - 1)] is var t && t > 0 ? t : null;

    /// <summary>Асинхронная партия: сходил — закрыл игру — друг ходит позже.</summary>
    public bool Async { get; set; }

    /// <summary>Код комнаты для входа.</summary>
    public string Code { get; set; } = "";

    /// <summary>Строка состояния окна (ошибка, «ждём друзей», код комнаты).</summary>
    public string Status { get; set; } = "";

    /// <summary>Выбрать сценарий: места — игроки играбельных рас; первый — я, второй — друг, остальные — ИИ.</summary>
    public void SelectScenario(int index)
    {
        if (index < 0 || index >= Scenarios.Count) return;
        ScenarioIndex = index;
        var playable = _catalog.Races.Where(r => r.Playable).ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var seats = new List<LobbySeat>();
        foreach (var (player, race) in _players(Scenarios[index]))
        {
            if (!playable.TryGetValue(race, out var info)) continue;
            var kind = seats.Count switch { 0 => SeatKind.Me, 1 => SeatKind.Friend, _ => SeatKind.Ai };
            seats.Add(new LobbySeat(player, race, _text(info.NameText), kind));
        }
        Seats = seats;
    }

    /// <summary>Следующий вариант места: я → друг → ИИ → я. «Я» — ровно одно место: прежнее «я» становится другом.</summary>
    public void CycleSeat(int index)
    {
        if (index < 0 || index >= Seats.Count) return;
        var next = Seats[index].Kind switch { SeatKind.Me => SeatKind.Friend, SeatKind.Friend => SeatKind.Ai, _ => SeatKind.Me };
        Seats = [.. Seats.Select((s, i) => i == index ? s with { Kind = next }
                                       : next == SeatKind.Me && s.Kind == SeatKind.Me ? s with { Kind = SeatKind.Friend } : s)];
    }

    /// <summary>Моё место.</summary>
    public LobbySeat? Mine => Seats.FirstOrDefault(s => s.Kind == SeatKind.Me);

    /// <summary>Места друзей по порядку.</summary>
    public IReadOnlyList<LobbySeat> Friends => [.. Seats.Where(s => s.Kind == SeatKind.Friend)];

    /// <summary>Почему комнату не создать (null — можно): нужен сценарий, своё место и хотя бы одно место друга.</summary>
    public string? CreateRefusal() =>
        Scenario is null ? "LE_NET_NO_SCENARIO" : Mine is null ? "LE_NET_NO_ME" : Friends.Count == 0 ? "LE_NET_NO_FRIEND" : null;

    /// <summary>Люди партии при старте обычной комнаты: я и места друзей, на которые кто-то вошёл (по порядку входа); места друзей без
    /// вошедших — ИИ.</summary>
    public IReadOnlyList<string> HumansFor(int joined) => [Mine!.Player, .. Friends.Take(joined).Select(s => s.Player)];

    /// <summary>Код комнаты — 6 знаков без похожих (как у посредника); ввод — без пробелов, заглавными.</summary>
    public static string NormalizeCode(string code) => new([.. (code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    public void Open(UiScreenState ui)
    {
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}

/// <summary>Свои настройки сети игрока (<c>user://net.json</c>, не в репозитории): последний посредник и имя. Битый файл — пустые.</summary>
public sealed record NetPrefs(string Relay = "", string Nick = "")
{
    public static NetPrefs Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<NetPrefs>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    }
}

/// <summary>Своя комната: ключ места у посредника и ключ места у хоста (обычная комната — для возвращения).</summary>
public sealed record NetRoomEntry(RoomKey Key, string? Token = null, DateTime Added = default);

/// <summary>Свои комнаты — <c>user://net-rooms.json</c> (не в сохранениях): по ним меню находит асинхронные комнаты и «ваш ход», и
/// возвращает в партию. Битый файл — пустой список.</summary>
public static class NetRooms
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static IReadOnlyList<NetRoomEntry> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<NetRoomEntry>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Добавить (или заменить по коду комнаты) и записать; больше 32 — старые уходят.</summary>
    public static IReadOnlyList<NetRoomEntry> Remember(string path, NetRoomEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var rooms = Load(path).Where(r => !r.Key.Room.Equals(entry.Key.Room, StringComparison.OrdinalIgnoreCase)).Prepend(entry).Take(32).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(rooms, Json));
        File.Move(temp, path, overwrite: true);
        return rooms;
    }
}
