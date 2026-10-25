using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Maps;
using LastEdict.Data.Content.Schema;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Игроки и события карты во вкладке «Карта» (Студия 2, задача 5): игроки — раса, лорд, имя, человек, золото, новый и убрать; события — список,
/// новое, поля (имя, включено, раз, шанс, для каких игроков), условия и действия строками: вид и поля значениями (число, флаг, текст, список —
/// JSON), «+ условие», «+ действие» любого вида из правил, области для условий «в области». Каждая кнопка — одна команда <see cref="MapWorkspace"/>.
/// </summary>
public partial class StudioMapScenarioPanel : TabContainer
{
    private readonly MapWorkspace _ws;
    // Игроки
    private readonly ItemList _players = new() { CustomMinimumSize = new Vector2(0, 120) };
    private readonly LineEdit _playerName = new();
    private readonly StudioPicker _race = new(), _lord = new(), _newRace = new();
    private readonly CheckBox _human = new() { Text = "человек" };
    private readonly SpinBox _gold = new() { MaxValue = 9999, Step = 1, CustomArrowStep = 100 };
    // События
    private readonly ItemList _events = new() { CustomMinimumSize = new Vector2(0, 120) };
    private readonly LineEdit _eventName = new() { PlaceholderText = "имя нового события" };
    private readonly CheckBox _enabled = new() { Text = "включено" }, _once = new() { Text = "один раз" };
    private readonly SpinBox _chance = new() { MinValue = 0, MaxValue = 100 };
    private readonly VBoxContainer _who = new(), _conditions = new(), _effects = new();
    private readonly OptionButton _conditionKind = new(), _effectKind = new();
    private readonly SpinBox _zoneX = new() { MaxValue = 9999, Prefix = "x" }, _zoneY = new() { MaxValue = 9999, Prefix = "y" }, _zoneR = new() { MaxValue = 3, Prefix = "r" };
    private readonly Label _zones = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = SettingsTheme.HelpLabel };
    private int _shown = -1;
    private string? _player, _event;

    public StudioMapScenarioPanel(MapWorkspace ws)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        AddChild(PlayersPage());
        AddChild(EventsPage());
        _players.ItemSelected += i => { _player = _players.GetItemMetadata((int)i).AsString(); ShowPlayer(); };
        _events.ItemSelected += i => { _event = _events.GetItemMetadata((int)i).AsString(); ShowEvent(); };
        foreach (string k in LastEdict.Authoring.Maps.MapObjects.ConditionKinds) _conditionKind.AddItem(k);
        foreach (string k in LastEdict.Authoring.Maps.MapObjects.EffectKinds) _effectKind.AddItem(k);
    }

    private static Button Button(string text, Action pressed, bool primary = false)
    {
        var b = new Button { Text = text, ThemeTypeVariation = primary ? SettingsTheme.Primary : "" };
        b.Pressed += pressed;
        return b;
    }

    private static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var c in items) row.AddChild(c);
        items[0].SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return row;
    }

    private static Label Help(string text) => new() { Text = text, ThemeTypeVariation = SettingsTheme.HelpLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };

    private ScrollContainer Page(string name, params Control[] items)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 5);
        foreach (var c in items) box.AddChild(c);
        scroll.AddChild(box);
        return scroll;
    }

    private ScrollContainer PlayersPage() => Page("Игроки",
        _players,
        Help("Имя"), _playerName, Help("Раса"), _race, Help("Лорд"), _lord, _human, Help("Золото"), _gold,
        Row(Button("Применить", ApplyPlayer, primary: true), Button("Убрать", () => { if (_player is { } id) _ws.Change("убрать игрока", m => m.RemovePlayer(id)); })),
        Help("Новый игрок (столица — палитрой «Объект: Столица» с хозяином)"), Row(_newRace, Button("+ игрок", AddPlayer)));

    private ScrollContainer EventsPage() => Page("События",
        _events, Row(_eventName, Button("+ событие", () => { string? id = null; if (_ws.Change("событие", m => id = m.AddEvent(_eventName.Text.Trim()))) _event = id; })),
        Row(_enabled, _once), Help("Шанс, %"), _chance, Help("Для игроков (никого — для всех)"), _who,
        new Label { Text = "Условия", ThemeTypeVariation = SettingsTheme.GroupLabel }, _conditions, Row(_conditionKind, Button("+ условие", () => AddPart(false))),
        new Label { Text = "Действия", ThemeTypeVariation = SettingsTheme.GroupLabel }, _effects, Row(_effectKind, Button("+ действие", () => AddPart(true))),
        Row(Button("Применить", ApplyEvent, primary: true), Button("Убрать событие", () => { if (_event is { } id) _ws.Change("убрать событие", m => m.RemoveEvent(id)); })),
        new Label { Text = "Области (для условий «в области»)", ThemeTypeVariation = SettingsTheme.GroupLabel },
        Row(_zoneX, _zoneY, _zoneR, Button("+ область", () => _ws.Change("область", m => m.AddZone((int)_zoneX.Value, (int)_zoneY.Value, (int)_zoneR.Value)))),
        _zones);

    public void Sync()
    {
        if (_shown == _ws.Revision) return;
        _shown = _ws.Revision;
        var content = _ws.Session.Report.Content ?? _ws.Session.BaseContent;
        if (_race.Selected is null)
        {
            var races = content.Races.Select(r => new PickItem(r.Id, $"{_ws.Session.NameOf(r.Id)} · {r.Id[(r.Id.LastIndexOf('/') + 1)..]}")).ToList();
            _race.SetItems(races, races.FirstOrDefault()?.Id);
            _newRace.SetItems(races, races.FirstOrDefault()?.Id);
            _lord.SetItems([.. content.Lords.Select(l => new PickItem(l.Id, $"{_ws.Session.NameOf(l.Id)} · {l.Id[(l.Id.LastIndexOf('/') + 1)..]}"))],
                           content.Lords.FirstOrDefault()?.Id);
        }
        _players.Clear();
        foreach (var (id, race, name) in _ws.Reader?.Players() ?? [])
        {
            int i = _players.AddItem($"{name} · {_ws.Session.NameOf(race)} · {id}");
            _players.SetItemMetadata(i, id);
            if (id == _player) _players.Select(i);
        }
        _events.Clear();
        foreach (var e in _ws.Reader?.Events() ?? [])
        {
            int i = _events.AddItem($"{(e.Name.Length > 0 ? e.Name : e.Id)} · условий {e.Conditions}, действий {e.Effects}" + (e.Enabled ? "" : " (выкл.)"));
            _events.SetItemMetadata(i, e.Id);
            if (e.Id == _event) _events.Select(i);
        }
        _zones.Text = string.Join("; ", (_ws.Reader?.Zones() ?? []).Select(z => $"{z.Id} @{z.X},{z.Y} r{z.Radius}"));
        ShowPlayer();
        ShowEvent();
    }

    private void ShowPlayer()
    {
        var p = _player is null ? null : _ws.Reader?.PlayerOf(_player);
        _playerName.Text = p?["Name"]?.GetValue<string>() ?? "";
        _race.Select(p?["RaceId"]?.GetValue<string>());
        _lord.Select(p?["LordId"]?.GetValue<string>());
        _human.ButtonPressed = p?["IsHuman"]?.GetValue<bool>() ?? false;
        _gold.Value = p?["Bank"]?["Gold"]?.GetValue<int>() ?? 0;
    }

    private void ApplyPlayer()
    {
        if (_player is not { } id) return;
        _ws.Change("игрок", m => m.SetPlayer(id, race: _race.Selected, lord: _lord.Selected, name: _playerName.Text.Trim(), human: _human.ButtonPressed,
                                            gold: (int)_gold.Value));
    }

    private void AddPlayer()
    {
        if (_newRace.Selected is not { } race) return;
        string? id = null;
        if (_ws.Change("новый игрок", m => id = m.AddPlayer(race))) _player = id;
    }

    // ---- события

    private void ShowEvent()
    {
        var e = _event is null ? null : _ws.Reader?.EventOf(_event);
        _eventName.PlaceholderText = e is null ? "имя нового события" : "имя нового события (выбрано: " + (e["Name"]?.GetValue<string>() ?? "") + ")";
        _enabled.ButtonPressed = e?["Enabled"]?.GetValue<bool>() ?? true;
        _once.ButtonPressed = e?["OccurOnce"]?.GetValue<bool>() ?? true;
        _chance.Value = e?["Chance"]?.GetValue<int>() ?? 100;
        Clear(_who);
        var chosen = (e?["PlayerIds"] as JsonArray)?.Select(v => v?.GetValue<string>()).ToHashSet() ?? [];
        foreach (var (id, _, name) in _ws.Reader?.Players() ?? [])
            _who.AddChild(new CheckBox { Text = $"{name} · {id}", ButtonPressed = chosen.Contains(id), TooltipText = id });
        Parts(_conditions, e?["Conditions"] as JsonArray, effect: false);
        Parts(_effects, e?["Effects"] as JsonArray, effect: true);
    }

    private static void Clear(Node box)
    {
        foreach (var child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); }
    }

    /// <summary>Строки условий или действий: вид и поля (кроме вида, категории и имени) — правка значением.</summary>
    private void Parts(VBoxContainer box, JsonArray? parts, bool effect)
    {
        Clear(box);
        for (int i = 0; i < (parts?.Count ?? 0); i++)
        {
            if (parts![i] is not JsonObject part) continue;
            var row = new VBoxContainer();
            int index = i;
            row.AddChild(Row(new Label { Text = $"№{i} {part["Kind"]?.GetValue<string>()}", ThemeTypeVariation = SettingsTheme.HelpLabel },
                             Button("×", () => { if (_event is { } id) _ws.Change("убрать", m => m.RemovePart(id, index, effect)); })));
            var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var (field, value) in part.Where(p => p.Key is not ("Kind" or "Category" or "Name")))
            {
                grid.AddChild(new Label { Text = field, ThemeTypeVariation = SettingsTheme.HelpLabel });
                var edit = new LineEdit { Text = Show(value), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = field };
                edit.SetMeta("field", field);
                edit.SetMeta("kind", value?.GetValueKind().ToString() ?? "Null");
                grid.AddChild(edit);
            }
            row.AddChild(grid);
            row.SetMeta("index", index);
            box.AddChild(row);
        }
    }

    private static string Show(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => value.ToJsonString(),
    };

    /// <summary>Значение поля из текста по виду прежнего значения: строка как есть, число, флаг, список и объект — JSON.</summary>
    private static JsonNode? Parse(string text, string kind) => kind switch
    {
        nameof(JsonValueKind.String) => JsonValue.Create(text),
        nameof(JsonValueKind.Number) => JsonValue.Create(int.Parse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture)),
        nameof(JsonValueKind.True) or nameof(JsonValueKind.False) => JsonValue.Create(text.Trim() is "true" or "1" or "да"),
        _ => text.Trim().Length == 0 ? null : JsonNode.Parse(text),
    };

    private void AddPart(bool effect)
    {
        if (_event is not { } id) return;
        var pick = effect ? _effectKind : _conditionKind;
        string kind = pick.GetItemText(Math.Max(0, pick.Selected));
        _ws.Change($"+ {kind}", m => m.AddPart(id, kind, effect));
    }

    /// <summary>Поля события и всех его строк — одной командой.</summary>
    private void ApplyEvent()
    {
        if (_event is not { } id) return;
        var players = _who.GetChildren().OfType<CheckBox>().Where(c => c.ButtonPressed).Select(c => (JsonNode)JsonValue.Create(c.TooltipText)!).ToArray();
        var edits = new List<(int Index, bool Effect, string Field, JsonNode? Value)>();
        try
        {
            foreach (var (box, effect) in new[] { (_conditions, false), (_effects, true) })
                foreach (var row in box.GetChildren().OfType<VBoxContainer>())
                    foreach (var edit in row.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>())
                        edits.Add((row.GetMeta("index").AsInt32(), effect, edit.GetMeta("field").AsString(), Parse(edit.Text, edit.GetMeta("kind").AsString())));
        }
        catch (Exception e) when (e is FormatException or JsonException or OverflowException)
        {
            _ws.Session.Refuse($"значение поля не читается: {e.Message}");
            return;
        }
        _ws.Change("событие", m =>
        {
            m.SetEvent(id, "Enabled", JsonValue.Create(_enabled.ButtonPressed));
            m.SetEvent(id, "OccurOnce", JsonValue.Create(_once.ButtonPressed));
            m.SetEvent(id, "Chance", JsonValue.Create((int)_chance.Value));
            m.SetEvent(id, "PlayerIds", players.Length == 0 ? null : new JsonArray(players));
            foreach (var (index, effect, field, value) in edits) m.SetPart(id, index, effect, field, value);
        });
    }

    public string Line() => $"сценарий: игроков={_players.ItemCount} событий={_events.ItemCount}";
}
