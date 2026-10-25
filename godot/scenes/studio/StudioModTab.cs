using System;
using System.Linq;
using LastEdict.Authoring.Validation;
using LastEdict.Authoring.Workspaces;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Мод» (Студия 2, задача 6): описание мода (имя, версия, описание), порядок — «этот мод главнее …» для каждого мода рядом и
/// мода игры, конфликты слоёв (поле правят несколько модов — кто действует), «Упаковать» (архив с описью в <c>.studio/pack</c> мода).
/// Решения — в <see cref="ModWorkspace"/>.
/// </summary>
public partial class StudioModTab : ScrollContainer, IStudioTab
{
    private readonly ModWorkspace _ws;
    private readonly Action<string> _status;
    private readonly LineEdit _name = new(), _version = new() { PlaceholderText = "1.0.0" };
    private readonly TextEdit _description = new() { CustomMinimumSize = new Vector2(0, 60), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly VBoxContainer _order = new();
    private readonly ItemList _conflicts = new() { CustomMinimumSize = new Vector2(0, 220) };
    private int _shown = -1;

    public StudioModTab(ModWorkspace ws, Action<string> status)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        HorizontalScrollMode = ScrollMode.Disabled;
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        var apply = new Button { Text = "Применить", ThemeTypeVariation = SettingsTheme.Primary };
        apply.Pressed += Apply;
        var check = new Button { Text = "Найти конфликты" };
        check.Pressed += ShowConflicts;
        var pack = new Button { Text = "Упаковать мод", ThemeTypeVariation = SettingsTheme.Primary };
        pack.Pressed += () => _status(_ws.Pack());
        foreach (var c in new Control[]
                 {
                     new Label { Text = "Описание мода (mod.json)", ThemeTypeVariation = SettingsTheme.GroupLabel },
                     Help("Имя"), _name, Help("Версия"), _version, Help("Описание"), _description, apply,
                     new Label { Text = "Порядок: кто главнее", ThemeTypeVariation = SettingsTheme.GroupLabel },
                     Help("Отмечено — этот мод накладывается после того и его правки действуют (loadAfter). Моды — рядом с этим и моды игры."), _order,
                     new Label { Text = "Конфликты слоёв", ThemeTypeVariation = SettingsTheme.GroupLabel }, check, _conflicts,
                     new Label { Text = "Упаковка", ThemeTypeVariation = SettingsTheme.GroupLabel },
                     Help("Мод проверяется; архив <id>-<версия>.zip с описью файлов и хешами — в .studio/pack мода."), pack,
                 })
            box.AddChild(c);
    }

    private static Label Help(string text) => new() { Text = text, ThemeTypeVariation = SettingsTheme.HelpLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };

    public void Sync()
    {
        if (_shown == _ws.Session.History.Revision) return;
        _shown = _ws.Session.History.Revision;
        (_name.Text, _version.Text, _description.Text) = (_ws.Field("name"), _ws.Field("version"), _ws.Field("description"));
        foreach (var child in _order.GetChildren()) { _order.RemoveChild(child); child.QueueFree(); }
        foreach (var other in _ws.Others())
        {
            var check = new CheckBox { Text = $"главнее «{other.Name}» ({other.Id})", ButtonPressed = _ws.IsAbove(other.Id) };
            string id = other.Id;
            check.Toggled += on => _ws.SetAbove(id, on);
            _order.AddChild(check);
        }
        if (_order.GetChildCount() == 0) _order.AddChild(Help("модов рядом нет"));
    }

    private void Apply()
    {
        if (_name.Text.Trim() != _ws.Field("name")) _ws.SetField("name", _name.Text.Trim());
        if (_version.Text.Trim() != _ws.Field("version")) _ws.SetField("version", _version.Text.Trim());
        if (_description.Text.Trim() != _ws.Field("description")) _ws.SetField("description", _description.Text.Trim());
    }

    private void ShowConflicts()
    {
        _conflicts.Clear();
        var list = _ws.Conflicts();
        foreach (var c in list)
            _conflicts.AddItem($"{_ws.Session.NameOf(c.Entity)} · {c.Field}: {string.Join(" → ", c.Mods)} — действует {c.Winner}");
        if (list.Count == 0) _conflicts.AddItem("конфликтов нет");
    }

    /// <summary>Шаги проверки без окна.</summary>
    public string Pack() => _ws.Pack();

    public string Line() => $"мод: версия={(_version.Text.Length > 0 ? _version.Text : "-")} рядом={_ws.Others().Count} конфликтов={_conflicts.ItemCount}";
}
