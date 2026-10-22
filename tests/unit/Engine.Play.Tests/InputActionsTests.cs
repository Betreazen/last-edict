using LastEdict.Data.Input;
using LastEdict.Data.Map;
using LastEdict.Data.Settings;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Действия ввода (Э5, Р12; <see cref="InputActions"/>): вывод из горячих клавиш разметки по группам экранов, подписи и умолчания,
/// переназначение с конфликтами, слой мода, привязки в файле настроек и в черновике окна. Разметка — выдуманная, свои данные —
/// <c>data/ui/input.json</c>.</summary>
public class InputActionsTests
{
    private static UiElement El(string id, string? tip, params int[] hotkeys) =>
        new() { Id = id, Kind = UiKind.Button, TextId = tip, Hotkeys = hotkeys };

    private static readonly UiScreen[] Screens =
    [
        new() { Id = "DLG_PANEL", Elements = [El("BTN_END", "X100TA0066", 32), El("BTN_MENU", "X100TA0048", 115, 27), El("BTN_OK", null, 13)] },
        new() { Id = "DLG_PALETTE", Elements = [El("BTN_END", "X100TA0099", 32, 69)] },
        new() { Id = "DLG_FIGHT", Elements = [El("BTN_DEFEND", "X005TA0753", 68), El("BTN_WAIT", "X005TA0752", 87)] },
    ];

    private static readonly InputConfig Config = InputConfig.FromJson("""
        // комментарий допустим
        {
          "Groups": [
            { "Id": "map", "Label": "LE_IN_GROUP_MAP", "Screens": ["DLG_PANEL", "DLG_PALETTE"] },
            { "Id": "battle", "Label": "LE_IN_GROUP_BATTLE", "Screens": ["DLG_FIGHT"] },
          ],
          "Actions": [
            { "Id": "accept", "Label": "LE_IN_ACCEPT", "Godot": "ui_accept", "Vk": [13], "Bindings": ["key:Enter", "pad:A"] },
            { "Id": "cancel", "Label": "LE_IN_CANCEL", "Godot": "ui_cancel", "Vk": [27], "Bindings": ["key:Escape", "pad:B"] },
            { "Id": "map.quicksave", "Label": "LE_IN_QUICKSAVE", "Group": "map", "Bindings": ["key:Q"] },
          ],
          "Pad": { "map.BTN_END": ["pad:Y"], "battle.BTN_DEFEND": ["pad:X"] },
        }
        """);

    private static IReadOnlyList<InputAction> Actions => InputActions.Build(Config, Screens);

    private static InputAction Get(string id) => Actions.Single(a => a.Id == id);

    [Fact]
    public void Build_DerivesGroupActionsFromHotkeys_SkipsGeneralKeys()
    {
        Assert.Equal(["accept", "cancel", "map.quicksave", "map.BTN_END", "map.BTN_MENU", "battle.BTN_DEFEND", "battle.BTN_WAIT"],
                     Actions.Select(a => a.Id));
        var end = Get("map.BTN_END");
        Assert.Equal("X100TA0066", end.Label);              // подсказка элемента с первого экрана группы
        Assert.Equal([32, 69], end.Vk);                     // клавиши элемента со всех экранов группы
        Assert.Equal(["key:Space", "key:E", "pad:Y"], end.Defaults);
        Assert.Equal(["DLG_PANEL", "DLG_PALETTE"], end.Screens);
        Assert.Equal("LE_IN_GROUP_MAP", end.GroupLabel);
        Assert.Equal([115], Get("map.BTN_MENU").Vk);        // Esc — общее «Отмена», F4 — своё
        Assert.Equal(["key:F4"], Get("map.BTN_MENU").Defaults);
        Assert.True(Get("accept").Everywhere);
        Assert.Null(Get("accept").GroupLabel);
        Assert.Equal(["DLG_PANEL", "DLG_PALETTE"], Get("map.quicksave").Screens);
    }

    [Fact]
    public void On_GeneralAndOwnGroupOnly()
    {
        Assert.Equal(["accept", "cancel", "battle.BTN_DEFEND", "battle.BTN_WAIT"], InputActions.On(Actions, "dlg_fight").Select(a => a.Id));
        Assert.True(InputActions.Overlap(Get("accept"), Get("battle.BTN_WAIT")));
        Assert.False(InputActions.Overlap(Get("map.BTN_END"), Get("battle.BTN_WAIT")));
    }

    [Fact]
    public void Bind_ReplacesDeviceBinding_ClearsConflictOnSameScreensOnly()
    {
        // D — у «Защищаться»; ставим её «Ждать»: у «Защищаться» снимается, у действий карты (другие экраны) — нет.
        var map = InputActions.Bind(Actions, null, Get("battle.BTN_WAIT"), "key", "key:D");
        Assert.Equal(["key:D"], map["battle.BTN_WAIT"]);
        Assert.Equal(["pad:X"], map["battle.BTN_DEFEND"]);
        Assert.Equal(2, map.Count);

        // Геймпад заменяется, клавиатура остаётся.
        map = InputActions.Bind(Actions, map, Get("map.BTN_END"), "pad", "pad:Start");
        Assert.Equal(["key:Space", "key:E", "pad:Start"], map["map.BTN_END"]);

        // Общее действие пересекается со всеми: Space у «Подтвердить» снимается с «Конца хода».
        map = InputActions.Bind(Actions, map, Get("accept"), "key", "key:Space");
        Assert.Equal(["pad:A", "key:Space"], map["accept"]);
        Assert.Equal(["key:E", "pad:Start"], map["map.BTN_END"]);
    }

    [Fact]
    public void Bind_Null_Clears_BackToDefaultsIsNotStored()
    {
        var map = InputActions.Bind(Actions, null, Get("map.quicksave"), "key", null);
        Assert.Empty(map["map.quicksave"]);
        Assert.Empty(InputActions.BindingsOf(Get("map.quicksave"), map));
        map = InputActions.Bind(Actions, map, Get("map.quicksave"), "key", "key:Q");
        Assert.Empty(map);   // совпало с умолчанием

        // Умолчание «Защищаться» — [key:D, pad:X]; клавиша R, затем снова D даёт [pad:X, key:D]: порядок другой, множество то же —
        // переопределение не хранится, черновик окна не «изменён».
        var defend = Get("battle.BTN_DEFEND");
        map = InputActions.Bind(Actions, null, defend, "key", "key:R");
        map = InputActions.Bind(Actions, map, defend, "key", "key:D");
        Assert.Empty(map);
    }

    [Theory]
    [InlineData("""["key:E"]""")]                  // не объект
    [InlineData("""{ "accept": [1] }""")]          // не строки
    [InlineData("""{ "accept": "key:E" }""")]      // не список
    public void SettingsFile_BrokenBindings_KeepOtherSettings(string bindings)
    {
        string text = $$"""{ "version": 2, "audio": { "music": 10 }, "controls": { "bindings": {{bindings}} } }""";
        var parsed = SettingsFile.Parse(text);
        Assert.Equal(10, parsed.MusicVolume);
        Assert.Null(parsed.Bindings);
    }

    [Fact]
    public void Merge_ModReplacesById_AddsGroupsAndPad()
    {
        var mod = InputConfig.FromJson("""
            { "Groups": [{ "Id": "battle", "Label": "MOD_BATTLE", "Screens": ["DLG_FIGHT", "DLG_PALETTE"] }],
              "Actions": [{ "Id": "cancel", "Label": "MOD_CANCEL", "Vk": [27], "Bindings": ["key:Backspace"] }],
              "Pad": { "battle.BTN_WAIT": ["pad:Y"] } }
            """);
        var merged = InputConfig.Merge(Config, mod);
        var actions = InputActions.Build(merged, Screens);
        Assert.Equal(["key:Backspace"], actions.Single(a => a.Id == "cancel").Defaults);
        Assert.Equal(["key:W", "pad:Y"], actions.Single(a => a.Id == "battle.BTN_WAIT").Defaults);
        Assert.Contains(actions, a => a.Id == "battle.BTN_END");   // экран группы мода — новые действия без кода
        Assert.Equal(2, merged.Groups.Count);
    }

    [Fact]
    public void Names_KeysOfVirtualCodes_PadLabels()
    {
        Assert.Equal("Space", InputNames.KeyOfVk(32));
        Assert.Equal("Key1", InputNames.KeyOfVk(49));
        Assert.Equal("F4", InputNames.KeyOfVk(115));
        Assert.Equal("Tab", InputNames.KeyOfVk(9));
        Assert.Null(InputNames.KeyOfVk(255));
        Assert.Equal("RB", InputNames.PadLabel("pad:RightShoulder"));
        Assert.Equal("RT", InputNames.PadLabel("axis:TriggerRight+"));
        Assert.Equal("A", InputNames.PadLabel("pad:A"));
        Assert.Equal("key", InputActions.DeviceOf("key:A"));
        Assert.Equal("pad", InputActions.DeviceOf("axis:LeftX-"));
    }

    [Fact]
    public void KeyBindings_EqualByContent_RoundTripInSettingsFile()
    {
        var a = new KeyBindings(new Dictionary<string, IReadOnlyList<string>> { ["map.BTN_END"] = ["key:E", "pad:Y"], ["accept"] = [] });
        var b = new KeyBindings(new Dictionary<string, IReadOnlyList<string>> { ["accept"] = [], ["map.BTN_END"] = ["key:E", "pad:Y"] });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(new MapSettings(Bindings: a), new MapSettings(Bindings: b));

        string text = SettingsFile.Format(new MapSettings(Bindings: a));
        Assert.Contains("\"controls\"", text, StringComparison.Ordinal);
        Assert.Equal(a, SettingsFile.Parse(text).Bindings);
        Assert.DoesNotContain("controls", SettingsFile.Format(new MapSettings()), StringComparison.Ordinal);   // по умолчанию — раздела нет
        Assert.Null(new MapSettings(Bindings: KeyBindings.Empty).Clamped().Bindings);
    }

    [Fact]
    public void SettingsLogic_Bind_IsADraftChange_ResetGoesBack()
    {
        MapSettings? previewed = null;
        var logic = new SettingsLogic(new MapSettings(), inGame: true, s => previewed = s, null);
        logic.Bind(Actions, Get("battle.BTN_WAIT"), "key", "key:D");
        Assert.True(logic.Draft.Changed);
        Assert.Equal(2, previewed?.Bindings?.Count);
        Assert.Contains("привязок=2 ", logic.Line(), StringComparison.Ordinal);
        logic.ResetBindings();
        Assert.False(logic.Draft.Changed);
        Assert.Null(previewed?.Bindings);
    }

    [Fact]
    public void OwnInputFile_ParsesWithBindingsForEveryExplicitAction()
    {
        var config = InputConfig.Load(Path.Combine(AppContext.BaseDirectory, "data", "ui", "input.json"));
        Assert.NotNull(config);
        Assert.NotEmpty(config.Groups);
        Assert.All(config.Actions, a => Assert.NotEmpty(a.Bindings));
        Assert.All(config.Actions.Where(a => a.Group is { } g), a => Assert.Contains(config.Groups, g => g.Id == a.Group));
        string[] kinds = ["key:", "pad:", "axis:"];
        Assert.All(config.Actions.SelectMany(a => a.Bindings).Concat(config.Pad.Values.SelectMany(v => v)),
                   b => Assert.Contains(kinds, k => b.StartsWith(k, StringComparison.Ordinal)));
        Assert.All(config.Pad.Keys, id => Assert.Contains(config.Groups, g => id.StartsWith(g.Id + ".", StringComparison.Ordinal)));
    }
}
