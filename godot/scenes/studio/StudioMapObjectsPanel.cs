using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Maps;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Объекты карты во вкладке «Карта» (Студия 2, задача 5): что ставит инструмент «объект» (хозяин, юниты, предметы, золото, ресурс кристалла,
/// ориентир, заклинания мага) и свойства выбранного объекта (имя, описание и золото места, состав и хозяин отряда, приказ) с «Применить»,
/// «Убрать». Решения — в <see cref="MapWorkspace"/> и <see cref="MapObjects"/>.
/// </summary>
public partial class StudioMapObjectsPanel : ScrollContainer
{
    public static readonly (string Title, MapObjectKind Kind)[] Kinds =
    [
        ("Столица", MapObjectKind.Capital), ("Город", MapObjectKind.City), ("Руина", MapObjectKind.Ruin), ("Лавка", MapObjectKind.Merchant),
        ("Башня мага", MapObjectKind.Mage), ("Наёмники", MapObjectKind.Mercenaries), ("Тренер", MapObjectKind.Trainer),
        ("Кристалл", MapObjectKind.Crystal), ("Мешок", MapObjectKind.Bag), ("Ориентир", MapObjectKind.Landmark), ("Отряд", MapObjectKind.Stack),
    ];

    private static readonly string[] Resources = ["золото", "мана Преисподней", "мана Жизни", "мана Смерти", "мана Рун", "мана Рощи"];

    private readonly MapWorkspace _ws;
    private readonly Func<string, Texture2D?> _texture;
    private readonly OptionButton _owner = new() { FitToLongestItem = false }, _resource = new();
    private readonly StudioPicker _unit = new(), _item = new(), _landmark = new(), _spell = new();
    private readonly Label _units = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart }, _items = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart },
        _spells = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly SpinBox _gold = new() { MaxValue = 99999, Step = 1, CustomArrowStep = 50 }, _order = new() { MinValue = 0, MaxValue = 20 };
    private readonly Label _selected = new() { ThemeTypeVariation = SettingsTheme.GroupLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly LineEdit _name = new() { PlaceholderText = "имя места" };
    private readonly TextEdit _description = new() { CustomMinimumSize = new Vector2(0, 50), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly Label _problems = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = SettingsTheme.HelpLabel };
    private readonly List<string> _unitIds = [], _itemIds = [], _spellIds = [];
    private List<string> _playerIds = [];
    private int _shown = -1;

    public StudioMapObjectsPanel(MapWorkspace ws, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        CustomMinimumSize = new Vector2(340, 0);
        HorizontalScrollMode = ScrollMode.Disabled;
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 5);
        AddChild(box);
        foreach (string r in Resources) _resource.AddItem(r);
        box.AddChild(Title("Что ставит объект"));
        box.AddChild(Labeled("Хозяин", _owner));
        box.AddChild(Labeled("Юниты (отряд — лидер первым; охрана; наёмники)", Row(_unit, Button("+", () => Add(_unitIds, _unit.Selected)), Button("×", () => Clear(_unitIds)))));
        box.AddChild(_units);
        box.AddChild(Labeled("Предметы (мешок, лавка, награда руины)", Row(_item, Button("+", () => Add(_itemIds, _item.Selected)), Button("×", () => Clear(_itemIds)))));
        box.AddChild(_items);
        box.AddChild(Labeled("Заклинания (маг)", Row(_spell, Button("+", () => Add(_spellIds, _spell.Selected)), Button("×", () => Clear(_spellIds)))));
        box.AddChild(_spells);
        box.AddChild(Labeled("Золото (руина, мешок)", _gold));
        box.AddChild(Labeled("Ресурс кристалла", _resource));
        box.AddChild(Labeled("Ориентир", _landmark));
        box.AddChild(Title("Выбранный объект"));
        box.AddChild(_selected);
        box.AddChild(Labeled("Имя места", _name));
        box.AddChild(Labeled("Описание", _description));
        box.AddChild(Labeled("Приказ отряда (номер движка)", _order));
        var buttons = new HBoxContainer();
        buttons.AddChild(Button("Применить", Apply, primary: true));
        buttons.AddChild(Button("Состав = юниты выше", () => { if (_ws.Selected is { } id && _unitIds.Count > 0) _ws.SetStack(id, units: [.. _unitIds]); }));
        buttons.AddChild(Button("Убрать", () => { if (_ws.Selected is { } id) _ws.Remove(id); }));
        box.AddChild(buttons);
        box.AddChild(Title("Проверка карты"));
        box.AddChild(Button("Проверить", ShowProblems));
        box.AddChild(_problems);
    }

    private static Label Title(string text) => new() { Text = text, ThemeTypeVariation = SettingsTheme.GroupLabel };

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

    private static VBoxContainer Labeled(string title, Control field)
    {
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.HelpLabel });
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(field);
        return box;
    }

    private void Add(List<string> list, string? id)
    {
        if (id is not null) list.Add(id);
        ShowLists();
    }

    private void Clear(List<string> list)
    {
        list.Clear();
        ShowLists();
    }

    private void ShowLists()
    {
        string Names(List<string> ids) => ids.Count == 0 ? "—" : string.Join(", ", ids.Select(i => _ws.Session.NameOf(i)));
        (_units.Text, _items.Text, _spells.Text) = (Names(_unitIds), Names(_itemIds), Names(_spellIds));
    }

    /// <summary>Что ставит объект сейчас.</summary>
    public MapPlacement Placement() => new()
    {
        Owner = _playerIds.ElementAtOrDefault(_owner.Selected), Units = [.. _unitIds], Items = [.. _itemIds], Spells = [.. _spellIds],
        Gold = (int)_gold.Value, Resource = Math.Max(0, _resource.Selected), Landmark = _landmark.Selected,
    };

    public void Sync()
    {
        if (_shown == _ws.Revision) return;
        _shown = _ws.Revision;
        var content = _ws.Session.Report.Content ?? _ws.Session.BaseContent;
        if (_unit.Selected is null && _item.Selected is null)
        {
            string Title(string id) => $"{_ws.Session.NameOf(id)} · {id[(id.LastIndexOf('/') + 1)..]}";
            _unit.SetItems([.. content.Units.Select(u => new PickItem(u.Id, Title(u.Id), _texture($"Faces:{ArtNames.Of(u.Id)}FACE")))], content.Units.FirstOrDefault()?.Id);
            _item.SetItems([.. content.Items.Select(i => new PickItem(i.Id, Title(i.Id), _texture($"IconItem:{ArtNames.Of(i.Id)}")))], content.Items.FirstOrDefault()?.Id);
            _spell.SetItems([.. content.Spells.Select(s => new PickItem(s.Id, Title(s.Id), _texture($"IconSpel:{ArtNames.Of(s.Id)}")))], content.Spells.FirstOrDefault()?.Id);
            _landmark.SetItems([.. content.Of<LandmarkDef>().Select(l => new PickItem(l.Id, $"{Title(l.Id)} ({l.Width}×{l.Height})"))], content.Of<LandmarkDef>().FirstOrDefault()?.Id);
        }
        var players = _ws.Reader?.Players() ?? [];
        if (!players.Select(p => p.Id).SequenceEqual(_playerIds))
        {
            int was = _owner.Selected;
            _owner.Clear();
            _playerIds = [.. players.Select(p => p.Id)];
            foreach (var (id, race, name) in players) _owner.AddItem($"{name} · {_ws.Session.NameOf(race)}");
            _owner.Selected = Math.Clamp(was, 0, Math.Max(0, _playerIds.Count - 1));
        }
        var selected = _ws.Selected is { } s ? _ws.Objects.FirstOrDefault(o => o.Id == s) : null;
        _selected.Text = selected is null ? "ничего не выбрано (инструмент «Выбор» — щелчок по объекту)" : $"{Kinds.FirstOrDefault(k => k.Kind == selected.Kind).Title}: {selected.Title} · {selected.Id} @{selected.X},{selected.Y}";
        var site = selected is null ? null : _ws.Reader?.SiteOf(selected.Id);
        _name.Text = site?["Name"]?.GetValue<string>() ?? "";
        _description.Text = site?["Description"]?.GetValue<string>() ?? "";
        (_name.Editable, _description.Editable) = (site is not null, site is not null);
        var stack = selected is null ? null : _ws.Reader?.StackOf(selected.Id);
        _order.Value = stack?["Order"]?.GetValue<int>() ?? 0;
        _order.Editable = stack is not null;
        if (stack is not null && _ws.Reader is { } reader) _units.Text = "в отряде: " + string.Join(", ", reader.UnitTypes(selected!.Id).Select(t => _ws.Session.NameOf(t)));
        else ShowLists();
    }

    private void Apply()
    {
        if (_ws.Selected is not { } id) return;
        var site = _ws.Reader?.SiteOf(id);
        if (site is not null && _name.Text != (site["Name"]?.GetValue<string>() ?? "")) _ws.SetSite(id, "Name", JsonValue.Create(_name.Text));
        if (site is not null && _description.Text != (site["Description"]?.GetValue<string>() ?? "")) _ws.SetSite(id, "Description", JsonValue.Create(_description.Text));
        if (_ws.Reader?.StackOf(id) is { } stack && (int)_order.Value != (stack["Order"]?.GetValue<int>() ?? 0)) _ws.SetStack(id, order: (int)_order.Value);
    }

    private void ShowProblems()
    {
        var problems = _ws.Check();
        _problems.Text = problems.Count == 0 ? "нарушений нет" : $"нарушений {problems.Count}:\n" + string.Join("\n", problems.Take(12));
    }

    /// <summary>Шаги проверки без окна: юниты и предметы размещения.</summary>
    public void Set(IEnumerable<string>? units, IEnumerable<string>? items, string? owner)
    {
        _unitIds.Clear();
        _unitIds.AddRange(units ?? []);
        _itemIds.Clear();
        _itemIds.AddRange(items ?? []);
        if (owner is not null) _owner.Selected = Math.Max(0, _playerIds.IndexOf(owner));
        ShowLists();
    }
}
