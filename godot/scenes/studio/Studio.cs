using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Maps;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Студия Last Edict (Э6, roadmap §3.4, <c>docs/tools/studio.md</c>): одно приложение на ядре <c>LastEdict.Authoring</c>, рабочие
/// пространства — вкладками («Дерево построек», «Содержимое», «Баланс»). Открывает каталог мода (<c>--open</c>), правит его командами ядра
/// (отмена, повтор), пишет только в мод; «Играть» запускает игру отдельным процессом с этим модом и <c>--watch</c> — сохранённая правка
/// видна в идущей партии без перезапуска. Мод с C#-плагином помечен «исполняет код». Решений здесь нет — только узлы, ввод и вызовы
/// пространств; без окна: <c>--check --script &lt;шаги.json&gt;</c> печатает строку пространства после каждого шага.
/// </summary>
public partial class Studio : Control
{
    private StudioSession? _session;
    private TreeWorkspace? _tree;
    private ContentWorkspace? _content;
    private BalanceWorkspace? _balance;
    private MapWorkspace? _mapWs;
    private UnitWorkspace? _units;
    private ItemWorkspace? _items;
    private LordWorkspace? _lords;
    private ModWorkspace? _modWs;
    private ContentWatcher? _watcher;

    private readonly Label _title = new() { ThemeTypeVariation = SettingsTheme.Title };
    private readonly Label _status = new() { ThemeTypeVariation = SettingsTheme.HelpLabel, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly OptionButton _lord = new() { CustomMinimumSize = new Vector2(260, 0) };
    private readonly OptionButton _scenario = new() { CustomMinimumSize = new Vector2(220, 0) };
    private readonly Button _undo = new() { Text = "Отменить" }, _redo = new() { Text = "Повторить" }, _save = new() { Text = "Сохранить" },
        _play = new() { Text = "Играть", ThemeTypeVariation = SettingsTheme.Primary };
    private readonly TabContainer _tabs = new() { SizeFlagsVertical = SizeFlags.ExpandFill };

    private StudioTreeTab? _treeTab;
    private StudioContentTab? _contentTab;
    private StudioBalanceTab? _balanceTab;
    private StudioMapTab? _mapTab;
    private StudioUnitsTab? _unitsTab;
    private StudioItemsTab? _itemsTab;
    private StudioLordsTab? _lordsTab;
    private StudioModTab? _modTab;
    private int _shown = -1, _contentShown = -1, _unitsShown = -1, _itemsShown = -1, _lordsShown = -1;

    public override void _Ready()
    {
        Theme = SettingsTheme.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        string[] args = OS.GetCmdlineUserArgs();
        string? dir = Arg(args, "--open");
        if (dir is null)
        {
            GD.PrintErr("Studio: нужен --open <каталог мода> (новый мод — d2mod new <каталог> --id <id>)");
            GetTree().Quit(2);
            return;
        }
        try
        {
            var project = ModProject.Open(GameData.FromProject(dir));
            _session = new StudioSession(project, GameData.RulesDir, key => GameData.Strings(GameData.Language).GetValueOrDefault(key),
                                         [.. GameData.Mods.Where(m => !m.Dir.Equals(project.Dir, StringComparison.OrdinalIgnoreCase))], GameData.Language);
            _tree = new TreeWorkspace(_session);
            _content = new ContentWorkspace(_session);
            _balance = new BalanceWorkspace(_session, Arg(args, "--scenarios") is { } scenarios ? GameData.FromProject(scenarios) : GameData.ScenariosDir);
            _mapWs = new MapWorkspace(_session);
            _units = new UnitWorkspace(_session);
            _items = new ItemWorkspace(_session);
            _lords = new LordWorkspace(_session);
            _modWs = new ModWorkspace(_session);
            if (Arg(args, "--lord") is { } lord) _tree.SelectLord(lord);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Studio: {e.Message}");
            GetTree().Quit(1);
            return;
        }
        Build();
        Sync();
        if (args.Contains("--check")) Callable.From(() => RunCheck(Arg(args, "--script"), Arg(args, "--shot"))).CallDeferred();
        else
        {
            _watcher = new ContentWatcher([_session.Project.Dir]);
            SetProcess(true);
        }
    }

    private void Build()
    {
        var back = new Panel { ThemeTypeVariation = SettingsTheme.Inner };
        back.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = SettingsTheme.Field });
        AddChild(back);
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        root.AddChild(Margin(bar, 8));
        bar.AddChild(_title);
        bar.AddChild(new Label { Text = "Лорд:", ThemeTypeVariation = SettingsTheme.GroupLabel });
        bar.AddChild(_lord);
        bar.AddChild(_undo);
        bar.AddChild(_redo);
        bar.AddChild(_save);
        bar.AddChild(_scenario);
        bar.AddChild(_play);
        root.AddChild(Margin(_tabs, 8));
        root.AddChild(Margin(_status, 8));

        _treeTab = new StudioTreeTab(_tree!, FrameTexture) { Name = "Дерево построек" };
        _contentTab = new StudioContentTab(_content!) { Name = "Содержимое" };
        _balanceTab = new StudioBalanceTab(_balance!) { Name = "Баланс" };
        _tabs.AddChild(_treeTab);
        _unitsTab = new StudioUnitsTab(_units!, FrameTexture) { Name = "Юниты" };
        _tabs.AddChild(_unitsTab);
        _itemsTab = new StudioItemsTab(_items!, FrameTexture) { Name = "Предметы" };
        _tabs.AddChild(_itemsTab);
        _lordsTab = new StudioLordsTab(_lords!, FrameTexture) { Name = "Лорды" };
        _tabs.AddChild(_lordsTab);
        _tabs.AddChild(_contentTab);
        _tabs.AddChild(_balanceTab);
        _mapTab = new StudioMapTab(_mapWs!, PlayMap, FrameTexture) { Name = "Карта" };
        _tabs.AddChild(_mapTab);
        _modTab = new StudioModTab(_modWs!, Status) { Name = "Мод" };
        _tabs.AddChild(_modTab);

        _lord.ItemSelected += i => { _tree!.SelectLord(_tree.Lords[(int)i].Id); Sync(); };
        _undo.Pressed += () => { _session!.Undo(); Sync(); };
        _redo.Pressed += () => { _session!.Redo(); Sync(); };
        _save.Pressed += () => { Status($"записано файлов: {_session!.Save()}"); Sync(); };
        _play.Pressed += Play;
        foreach (var s in GameData.Catalog.Scenarios) _scenario.AddItem(s.Name.Length > 0 ? s.Name : s.Id);
    }

    private static MarginContainer Margin(Control inner, int m)
    {
        var box = new MarginContainer { SizeFlagsVertical = inner.SizeFlagsVertical };
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) box.AddThemeConstantOverride(side, m);
        box.AddChild(inner);
        return box;
    }

    /// <summary>Перенести состояние пространства в окно: заголовок, лорды, кнопки, вкладки.</summary>
    private void Sync()
    {
        var session = _session!;
        var tree = _tree!;
        _title.Text = $"Студия — {session.Project.Info.Name}" + (session.Project.ExecutesCode ? "  ⚠ исполняет код" : "") + (session.History.Dirty ? " *" : "");
        _lord.Clear();
        foreach (var l in tree.Lords) _lord.AddItem(session.NameOf(l.Id));
        int at = tree.Lords.ToList().FindIndex(l => l.Id == tree.Lord);
        if (at >= 0) _lord.Select(at);
        _undo.Disabled = !session.History.CanUndo;
        _undo.TooltipText = session.History.UndoName ?? "";
        _redo.Disabled = !session.History.CanRedo;
        _redo.TooltipText = session.History.RedoName ?? "";
        _save.Disabled = !session.History.Dirty;
        if (session.Refusal is { } refusal) Status(refusal);
        foreach (var tab in _tabs.GetChildren().OfType<IStudioTab>()) tab.Sync();
        _shown = session.Revision + tree.Revision;
    }

    private void Status(string text) => _status.Text = text;

    public override void _Process(double delta)
    {
        if (_session is null || _tree is null || _content is null) return;
        if (_watcher?.Take() is not null && _session.ReloadFromDisk()) Status("мод изменён снаружи — перечитан");
        if (_session.Revision + _tree.Revision != _shown || _content.Revision != _contentShown || _units?.Revision != _unitsShown || _items?.Revision != _itemsShown || _lords?.Revision != _lordsShown) Sync();
        _contentShown = _content.Revision;
        _unitsShown = _units?.Revision ?? 0;
        _itemsShown = _items?.Revision ?? 0;
        _lordsShown = _lords?.Revision ?? 0;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, CtrlPressed: true } key || _session is null) return;
        bool undo = key.Keycode == Key.Z && !key.ShiftPressed, redo = key.Keycode == Key.Y || (key.Keycode == Key.Z && key.ShiftPressed);
        if (_tabs.GetCurrentTabControl() == _mapTab && _mapWs is { } map)
        {
            // На вкладке «Карта» — отмена мазков и запись карты (у карты своя история).
            if (undo) map.Undo();
            else if (redo) map.Redo();
            else if (key.Keycode == Key.S) Status(map.Save() ? "карта записана" : _session.Refusal ?? "");
            else return;
            _mapTab!.Sync();
        }
        else if (undo) _session.Undo();
        else if (redo) _session.Redo();
        else if (key.Keycode == Key.S) Status($"записано файлов: {_session.Save()}");
        else return;
        Sync();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Игра отдельным процессом с этим модом и горячей перезагрузкой (<c>--watch</c>): падение партии не роняет Студию.</summary>
    private void Play()
    {
        var ws = _session!;
        if (ws.History.Dirty) Status($"записано файлов: {ws.Save()}");
        var scenarios = GameData.Catalog.Scenarios;
        Launch(_scenario.Selected >= 0 && _scenario.Selected < scenarios.Count ? scenarios[_scenario.Selected].Id : null);
    }

    /// <summary>Сыграть на карте мода (вкладка «Карта»).</summary>
    private void PlayMap(string id) => Launch(id);

    /// <summary>Файл доверенных сборок плагинов (хеши SHA-256): мод с кодом запускается с <c>--allow-plugins</c> только после подтверждения.</summary>
    private const string TrustFile = "user://studio-trusted-plugins.txt";

    private static string[] PluginHashes(LastEdict.Data.Content.ModInfo mod) =>
        [.. mod.Plugins.Select(p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))))];

    private void Launch(string? scenario)
    {
        var ws = _session!;
        if (ws.Project.ExecutesCode)
        {
            var hashes = PluginHashes(ws.Project.Info);
            string trust = ProjectSettings.GlobalizePath(TrustFile);
            var trusted = File.Exists(trust) ? File.ReadAllLines(trust).ToHashSet(StringComparer.Ordinal) : [];
            if (!hashes.All(trusted.Contains))
            {
                var ask = new ConfirmationDialog
                {
                    Title = "Мод исполняет код",
                    DialogText = $"Мод «{ws.Project.Info.Name}» несёт C#-плагин ({string.Join(", ", ws.Project.Info.Plugins.Select(Path.GetFileName))}).\n" +
                                 "Игра выполнит его код без песочницы. Запускать только доверенный мод. Запустить?",
                    OkButtonText = "Запустить", CancelButtonText = "Отмена",
                };
                ask.Confirmed += () =>
                {
                    File.AppendAllLines(trust, hashes.Where(h => !trusted.Contains(h)));
                    Launch(scenario);
                };
                AddChild(ask);
                ask.PopupCentered();
                return;
            }
        }
        var args = new List<string> { "--path", ProjectSettings.GlobalizePath("res://"), "res://scenes/map/MapScreen.tscn", "--", "--mods", ws.Project.Dir, "--watch" };
        if (ws.Project.ExecutesCode) args.Add("--allow-plugins");   // подтверждено выше (хеши сборок — в доверенных)
        if (scenario is not null) args.AddRange(["--scenario", scenario]);
        int pid = OS.CreateProcess(OS.GetExecutablePath(), [.. args]);
        Status(pid > 0 ? $"игра запущена (процесс {pid}) с модом {ws.Project.Id} — сохранённые правки она подхватит сама" : "игра не запустилась");
    }

    /// <summary>Текстура кадра интерфейса или графики игры (значки построек, в том числе модов); картинка своей сущности — из документа
    /// (несохранённая видна сразу).</summary>
    private Texture2D? FrameTexture(string frame) =>
        _session?.Picture(frame) is { } own ? StudioPictures.Texture(own.Png, own.Slot) : UiScreenView.TextureOf(frame);

    // ---- проверка без окна

    private async void RunCheck(string? script, string? shot)
    {
        var ws = _tree!;
        var session = _session!;
        GD.Print(ws.Line());
        if (script is not null)
        {
            var steps = JsonNode.Parse(File.ReadAllText(GameData.FromProject(script))) as JsonArray ?? [];
            foreach (var step in steps.OfType<JsonObject>())
            {
                string op = step["op"]?.GetValue<string>() ?? "";
                bool ok = op switch
                {
                    "lord" => Do(() => ws.SelectLord(step["id"]!.GetValue<string>())),
                    "connect" => ws.Connect(step["from"]!.GetValue<string>(), step["to"]!.GetValue<string>()),
                    "disconnect" => ws.Disconnect(step["to"]!.GetValue<string>()),
                    "drag" => _treeTab!.Drag(step["building"]!.GetValue<string>(), step["column"]!.GetValue<double>(), step["row"]!.GetValue<int>()),
                    "mod-version" => _modWs!.SetField("version", step["version"]!.GetValue<string>()),
                    "mod-pack" => Do(() => GD.Print($"mod-pack {_modTab!.Pack()}")),
                    "lord-select" => Do(() => _lords!.Select(step["id"]?.GetValue<string>())),
                    "lord-new" => Do(() => _lordsTab!.NewLord(step["title"]?.GetValue<string>() ?? "")),
                    "lord-research" => Do(() => _lordsTab!.AddResearch(step["spell"]!.GetValue<string>(), step["gold"]?.GetValue<long>() ?? 100)),
                    "item-select" => Do(() => _items!.Select(step["id"]?.GetValue<string>())),
                    "item-new" => Do(() => _itemsTab!.NewItem(step["title"]?.GetValue<string>() ?? "")),
                    "item-form" => Do(() => _itemsTab!.Fill(step["gold"]?.GetValue<long>(), step["equip"]?.GetValue<string>())),
                    "item-apply" => Do(() => _itemsTab!.Press()),
                    "item-picture" => _itemsTab!.Picked(GameData.FromProject(step["path"]!.GetValue<string>())),
                    "unit-page" => Do(() => _unitsTab!.Page(step["index"]!.GetValue<int>())),
                    "unit-select" => Do(() => _units!.Select(step["id"]?.GetValue<string>())),
                    "unit-new" => Do(() => { _unitsTab!.NewTitle(step["title"]?.GetValue<string>() ?? ""); _unitsTab.PressCreate(); }),
                    "unit-form" => Do(() => _unitsTab!.Fill(step["hp"]?.GetValue<int>(), step["hireable"]?.GetValue<bool>(), step["title"]?.GetValue<string>())),
                    "unit-attack" => Do(() => { _unitsTab!.Combat.Check(step["effect"]?.GetValue<string>(), step["damage"]?.GetValue<int>(), null, null, null); _unitsTab.Combat.PressAttack(); }),
                    "unit-aura" => Do(() => { _unitsTab!.Combat.Check(null, null, step["modifier"]!.GetValue<string>(), step["reach"]?.GetValue<string>(), null); _unitsTab.Combat.PressAuras(); }),
                    "unit-immune" => Do(() => { _unitsTab!.Combat.Check(null, null, null, null, step["source"]!.GetValue<string>()); _unitsTab.Combat.PressImmunities(); }),
                    "unit-apply" => Do(() => _unitsTab!.Press()),
                    "unit-picture" => _unitsTab!.Picked(step["file"]!.GetValue<string>(), GameData.FromProject(step["path"]!.GetValue<string>())),
                    "unit-rename" => _units!.Rename(step["id"]!.GetValue<string>(), step["name"]!.GetValue<string>()),
                    "unit-remove" => _units!.Remove(step["id"]!.GetValue<string>()),
                    "building-select" => Do(() => ws.Select(step["id"]?.GetValue<string>())),
                    "building-form" => Do(() => _treeTab!.Form.Fill(step["title"]?.GetValue<string>() ?? "", step["requires"]?.GetValue<string>(),
                        step["level"]?.GetValue<int>(), step["gold"]?.GetValue<long>())),
                    "building-picture" => _treeTab!.Form.Picked(GameData.FromProject(step["path"]!.GetValue<string>())),
                    "building-apply" => Do(() => _treeTab!.Form.Press()),
                    "building-rename" => ws.RenameBuilding(step["id"]!.GetValue<string>(), step["name"]!.GetValue<string>()),
                    "building-remove" => ws.RemoveBuilding(step["id"]!.GetValue<string>()),
                    "add-building" => ws.AddBuilding(step["name"]!.GetValue<string>(), step["title"]?.GetValue<string>() ?? "", step["requires"]?.GetValue<string>()),
                    "undo" => session.Undo(),
                    "redo" => session.Redo(),
                    "save" => session.Save() >= 0,
                    "reload" => session.ReloadFromDisk(),
                    "content-open" => Do(() => _content!.Open(step["kind"]!.GetValue<string>())),
                    "content-select" => Do(() => _content!.Select(step["id"]!.GetValue<string>())),
                    "content-set" => _content!.Set(step["field"]!.GetValue<string>(), step["text"]!.GetValue<string>()),
                    "content-reset" => _content!.Reset(step["field"]!.GetValue<string>()),
                    "content-add" => _content!.Add(step["name"]!.GetValue<string>()),
                    "arena" or "stand" => true,   // прогон — в рабочем потоке, ждём ниже
                    "tab" => Do(() => _tabs.CurrentTab = step["index"]!.GetValue<int>()),
                    "map-new" => Do(() => _mapTab!.New(step["id"]!.GetValue<string>(), step["size"]!.GetValue<int>(), step["seats"]?.GetValue<int>() ?? 4)),
                    "map-stroke" => Do(() => { _mapWs!.Brush = Enum.Parse<MapBrush>(step["brush"]?.GetValue<string>() ?? "Forest"); _mapWs.Radius = step["radius"]?.GetValue<int>() ?? 1; _mapTab!.Stroke(step["x0"]!.GetValue<int>(), step["y0"]!.GetValue<int>(), step["x1"]!.GetValue<int>(), step["y1"]!.GetValue<int>()); }),
                    "map-undo" => Do(() => { _mapWs!.Undo(); _mapTab!.Sync(); }),
                    "map-save" => Do(() => _mapWs!.Save()),
                    "map-place" => _mapTab!.PlaceAt(step["kind"]!.GetValue<string>(), step["x"]!.GetValue<int>(), step["y"]!.GetValue<int>(),
                        (step["units"] as JsonArray)?.Select(u => u!.GetValue<string>()), (step["items"] as JsonArray)?.Select(i => i!.GetValue<string>()),
                        step["owner"]?.GetValue<string>()),
                    "map-select" => _mapTab!.SelectAt(step["x"]!.GetValue<int>(), step["y"]!.GetValue<int>()),
                    "map-move" => _mapTab!.MoveSelected(step["x"]!.GetValue<int>(), step["y"]!.GetValue<int>()),
                    "map-remove" => _mapWs!.Selected is { } removing && _mapWs.Remove(removing),
                    "map-player" => _mapWs!.Change("игрок", m => m.AddPlayer(step["race"]!.GetValue<string>(), human: step["human"]?.GetValue<bool>() ?? false)),
                    "map-event" => _mapWs!.Change("событие", m =>
                    {
                        string ev = m.AddEvent(step["name"]?.GetValue<string>() ?? "");
                        m.AddPart(ev, step["condition"]?.GetValue<string>() ?? "Frequency", effect: false);
                        int e = m.AddPart(ev, step["effect"]?.GetValue<string>() ?? "Popup", effect: true);
                        if (step["text"] is { } text) m.SetPart(ev, e, true, "Text", text.DeepClone());
                    }),
                    "map-check" => Do(() => GD.Print($"map-check нарушений={_mapWs!.Check().Count} {string.Join(" | ", _mapWs.Check().Take(3))}")),
                    "map-open" => Do(() => _mapTab!.Open(step["id"]!.GetValue<string>())),
                    _ => session.Do(EditCommands.Parse(step)),
                };
                if (op is "arena" or "stand") await _balanceTab!.Run(step["scenario"]!.GetValue<string>(), arena: op == "arena");
                Sync();
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"step {op} ok={(ok ? 1 : 0)} {ws.Line()} {_treeTab!.Line()} {_content!.Line()} {_balance!.Line()} {_mapTab!.Line()} {_unitsTab!.Line()} {_itemsTab!.Line()} {_lordsTab!.Line()} {_modTab!.Line()}");
            }
        }
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (shot is not null) GetViewport().GetTexture().GetImage().SavePng(GameData.FromProject(shot));
        GD.Print($"studio done {ws.Line()} {_treeTab!.Line()}" + (shot is null ? "" : $" shot={shot}"));
        GetTree().Quit(0);
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }

    private static string? Arg(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}

/// <summary>Вкладка Студии, которая перерисовывается по ревизии пространства.</summary>
public interface IStudioTab
{
    void Sync();
}
