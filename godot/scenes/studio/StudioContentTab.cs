using System;
using System.Linq;
using LastEdict.Authoring.Workspaces;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Содержимое» (<see cref="ContentWorkspace"/>): слева — вид и список сущностей (свои — светлее, правленые — золотом), справа —
/// поля выбранной по схеме: описание подсказкой, «база», правка (перечень и ссылка — выбором, флаг — переключателем, прочее — строкой,
/// Enter — записать), «итог» и сброс к базе.
/// </summary>
public partial class StudioContentTab : HSplitContainer, IStudioTab
{
    private readonly ContentWorkspace _ws;
    private readonly OptionButton _kind = new();
    private readonly LineEdit _filter = new() { PlaceholderText = "поиск по id и имени" };
    private readonly ItemList _list = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 0) };
    private readonly LineEdit _newName = new() { PlaceholderText = "имя новой (латиница)", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly GridContainer _fields = new() { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _head = new() { ThemeTypeVariation = SettingsTheme.GroupLabel };
    private int _shown = -1;

    public StudioContentTab(ContentWorkspace ws)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (string kind in ContentWorkspace.Kinds) _kind.AddItem(kind);
        _kind.Select(ContentWorkspace.Kinds.ToList().IndexOf(ws.Kind));
        _kind.ItemSelected += i => _ws.Open(ContentWorkspace.Kinds[(int)i]);
        _filter.TextChanged += t => _ws.Find(t);
        _list.ItemSelected += i => _ws.Select(_list.GetItemMetadata((int)i).AsString());
        var add = new Button { Text = "Новая" };
        add.Pressed += () => _ws.Add(_newName.Text.Trim());
        var remove = new Button { Text = "Убрать из мода" };
        remove.Pressed += () => _ws.Remove();
        var row = new HBoxContainer();
        row.AddChild(_newName);
        row.AddChild(add);
        left.AddChild(_kind);
        left.AddChild(_filter);
        left.AddChild(_list);
        left.AddChild(row);
        left.AddChild(remove);
        AddChild(left);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(_head);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_fields);
        right.AddChild(scroll);
        AddChild(right);
    }

    public void Sync()
    {
        if (_shown == _ws.Revision) return;
        _shown = _ws.Revision;
        _list.Clear();
        foreach (var e in _ws.Entities())
        {
            int i = _list.AddItem(e.Name == e.Id ? e.Id : $"{e.Name}  ·  {e.Id}");
            _list.SetItemMetadata(i, e.Id);
            if (e.Origin != EntityOrigin.Base) _list.SetItemCustomFgColor(i, e.Origin == EntityOrigin.Mine ? new Color("#cfe8b0") : SettingsTheme.Gold);
            if (e.Id == _ws.Selected) _list.Select(i);
        }
        foreach (var child in _fields.GetChildren()) { _fields.RemoveChild(child); child.QueueFree(); }
        _head.Text = _ws.Selected is { } id ? $"{_ws.Session.NameOf(id)} — {id}" : "выберите сущность";
        if (_ws.Selected is null) return;
        foreach (string title in new[] { "поле", "база", "мод", "итог", "" }) _fields.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.GroupLabel });
        foreach (var f in _ws.Fields())
        {
            _fields.AddChild(new Label { Text = f.Name, TooltipText = f.Description + (f.Ref is null ? "" : $"\nссылка: {f.Ref}"), MouseFilter = MouseFilterEnum.Pass });
            _fields.AddChild(new Label { Text = Short(FieldRow.Show(f.Base)), ThemeTypeVariation = SettingsTheme.HelpLabel, TooltipText = FieldRow.Show(f.Base) });
            _fields.AddChild(Editor(f));
            _fields.AddChild(new Label { Text = Short(FieldRow.Show(f.Value)), TooltipText = FieldRow.Show(f.Value) });
            var reset = new Button { Text = "↺", TooltipText = "сбросить к базе", Disabled = f.Mod is null };
            string name = f.Name;
            reset.Pressed += () => _ws.Reset(name);
            _fields.AddChild(reset);
        }
    }

    private static string Short(string text) => text.Length > 40 ? text[..39] + "…" : text;

    /// <summary>Правка поля по его типу.</summary>
    private Control Editor(FieldRow f)
    {
        string name = f.Name, current = FieldRow.Show(f.Mod ?? f.Value);
        if (f.Type == "boolean")
        {
            var check = new CheckBox { ButtonPressed = current == "true" };
            check.Toggled += on => _ws.Set(name, on ? "true" : "false");
            return check;
        }
        var choices = f.Enum.Count > 0 ? f.Enum : f.Ref is { } kind ? _ws.Choices(kind) : [];
        if (choices.Count > 0)
        {
            var pick = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            pick.AddItem("—");
            foreach (string c in choices) pick.AddItem(f.Ref is null ? c : $"{_ws.Session.NameOf(c)} · {c}");
            int at = choices.ToList().IndexOf(current);
            pick.Select(at + 1);
            pick.ItemSelected += i => _ws.Set(name, i == 0 ? "" : choices[(int)i - 1]);
            return pick;
        }
        var edit = new LineEdit { Text = current, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 0) };
        edit.TextSubmitted += t => _ws.Set(name, t);
        return edit;
    }
}
