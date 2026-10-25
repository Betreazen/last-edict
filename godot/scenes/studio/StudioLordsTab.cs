using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Лорды» (Студия 2, задача 4): слева — лорды (свои — светлее), «Новый на основе выбранного»; справа — форма: название, имя
/// каталога, описание, раса, вид лорда, исследования (строки: заклинание со значком, валюта, цена; «+»). Постройки лорда — во вкладке «Дерево
/// построек». Решения — в <see cref="LordWorkspace"/>.
/// </summary>
public partial class StudioLordsTab : HSplitContainer, IStudioTab
{
    private static readonly (LordCategory Kind, string Title)[] Kinds = [(LordCategory.Mage, "Маг"), (LordCategory.Warrior, "Воин"), (LordCategory.Diplomat, "Дипломат")];
    private static readonly (string Id, string Title)[] Currencies =
        [("gold", "золото"), ("infernal", "мана Преисподней"), ("life", "мана Жизни"), ("death", "мана Смерти"), ("runic", "мана Рун"), ("grove", "мана Рощи")];

    private readonly LordWorkspace _ws;
    private readonly Func<string, Texture2D?> _texture;
    private readonly ItemList _list = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0) };
    private readonly LineEdit _newTitle = new() { PlaceholderText = "название нового" };
    private readonly Label _head = new() { ThemeTypeVariation = SettingsTheme.GroupLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly LineEdit _title = new(), _name = new();
    private readonly TextEdit _description = new() { CustomMinimumSize = new Vector2(0, 60), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly StudioPicker _race = new();
    private readonly OptionButton _kind = new();
    private readonly VBoxContainer _research = new();
    private readonly Button _apply = new() { Text = "Применить", ThemeTypeVariation = SettingsTheme.Primary }, _rename = new() { Text = "Переименовать" },
        _remove = new() { Text = "Удалить" };
    private List<PickItem> _spellItems = [];
    private int _shown = -1;
    private (string? Id, int Revision) _filled = ("-", -1);

    public StudioLordsTab(LordWorkspace ws, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(_list);
        left.AddChild(_newTitle);
        var create = new Button { Text = "Новый на основе выбранного", ThemeTypeVariation = SettingsTheme.Primary };
        left.AddChild(create);
        AddChild(left);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(form);
        AddChild(scroll);
        foreach (var (_, t) in Kinds) _kind.AddItem(t);
        form.AddChild(_head);
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (title, field) in new (string, Control)[] { ("Название", _title), ("Имя каталога", Row(_name, _rename)), ("Раса", _race), ("Вид лорда", _kind) })
        {
            grid.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.HelpLabel });
            field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(field);
        }
        form.AddChild(grid);
        form.AddChild(new Label { Text = "Описание", ThemeTypeVariation = SettingsTheme.HelpLabel });
        form.AddChild(_description);
        form.AddChild(new Label { Text = "Исследования (заклинания по порядку, цена)", ThemeTypeVariation = SettingsTheme.GroupLabel });
        form.AddChild(_research);
        var add = new Button { Text = "+ заклинание" };
        add.Pressed += () => AddRow(null, "life", 100);
        form.AddChild(add);
        var buttons = new HBoxContainer();
        buttons.AddChild(_apply);
        buttons.AddChild(_remove);
        form.AddChild(buttons);

        _list.ItemSelected += i => _ws.Select(_list.GetItemMetadata((int)i).AsString());
        create.Pressed += Create;
        _apply.Pressed += Apply;
        _rename.Pressed += () =>
        {
            if (_ws.Selected is { } id) _ws.Rename(id, _name.Text.Trim());
        };
        _remove.Pressed += () =>
        {
            if (_ws.Selected is { } id) _ws.Remove(id);
        };
    }

    private static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var c in items) row.AddChild(c);
        items[0].SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return row;
    }

    public void Sync()
    {
        if (_shown == _ws.Revision) return;
        _shown = _ws.Revision;
        _list.Clear();
        foreach (var row in _ws.Lords())
        {
            int i = _list.AddItem(row.Name == row.Id ? row.Id : $"{row.Name}  ·  {row.Id[(row.Id.LastIndexOf('/') + 1)..]}");
            _list.SetItemMetadata(i, row.Id);
            if (row.Origin != EntityOrigin.Base) _list.SetItemCustomFgColor(i, row.Origin == EntityOrigin.Mine ? new Color("#cfe8b0") : SettingsTheme.Gold);
            if (row.Id == _ws.Selected) _list.Select(i);
        }
        string? id = _ws.Selected is { } s && _ws.Session.Report.Content?.Find(s) is LordDef ? s : null;
        if (_filled == (id, _ws.Session.History.Revision)) return;
        _filled = (id, _ws.Session.History.Revision);
        Fill(id);
    }

    private void Fill(string? id)
    {
        var session = _ws.Session;
        bool own = id is not null && _ws.IsOwnFolder(id);
        var f = id is null ? new LordForm() : _ws.FormOf(id);
        _head.Text = id is null ? "Выберите лорда слева" : (own ? $"Свой лорд: {f.Title}" : $"Лорд базы: {f.Title} (правка — слоем мода)")
                                                          + StudioMarks.Head(session.ProblemsOf(id));
        (_title.Text, _title.Editable, _description.Text, _description.Editable) = (f.Title, own, f.Description, own);
        (_name.Text, _name.Editable, _rename.Visible, _remove.Visible) = (id is null ? "" : id[(id.LastIndexOf('/') + 1)..], own, own, own);
        _race.SetItems([.. _ws.Races.Select(r => new PickItem(r.Id, $"{session.NameOf(r.Id)} · {r.Id[(r.Id.LastIndexOf('/') + 1)..]}"))], f.Race);
        _kind.Selected = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == f.Category));
        _spellItems = [.. _ws.Spells.Select(s => new PickItem(s.Id, $"{session.NameOf(s.Id)} · {s.Id[(s.Id.LastIndexOf('/') + 1)..]}",
                                                              _texture($"IconSpel:{ArtNames.Of(s.Id)}")))];
        foreach (var child in _research.GetChildren()) { _research.RemoveChild(child); child.QueueFree(); }
        foreach (var r in f.Research)
        {
            var (currency, amount) = r.Cost?.FirstOrDefault(c => c.Value != 0) is { Key: not null } c ? (c.Key, c.Value) : ("gold", 0L);
            AddRow(r.Spell, currency, amount);
        }
        _apply.Disabled = id is null;
        if (id is not null) StudioMarks.Mark(_research, session.ProblemsOf(id, "research"));
    }

    private void AddRow(string? spell, string currency, long amount)
    {
        var pick = new StudioPicker();
        pick.SetItems(_spellItems, spell);
        var money = new OptionButton();
        foreach (var (_, t) in Currencies) money.AddItem(t);
        money.Selected = Math.Max(0, Array.FindIndex(Currencies, c => c.Id == currency));
        var spin = new SpinBox { MaxValue = 99999, Step = 1, CustomArrowStep = 25, Value = amount };
        var row = Row(pick, money, spin);
        var drop = new Button { Text = "×" };
        drop.Pressed += () => { _research.RemoveChild(row); row.QueueFree(); };
        row.AddChild(drop);
        _research.AddChild(row);
    }

    /// <summary>Исследования по строкам.</summary>
    private List<SpellResearch> Research() =>
        [.. _research.GetChildren().OfType<HBoxContainer>()
            .Select(row => (Pick: row.GetChildren().OfType<StudioPicker>().First(), Money: row.GetChildren().OfType<OptionButton>().First(),
                            Spin: row.GetChildren().OfType<SpinBox>().First()))
            .Where(r => r.Pick.Selected is not null)
            .Select(r => new SpellResearch
            {
                Spell = r.Pick.Selected!,
                Cost = r.Spin.Value > 0 ? new Dictionary<string, long> { [Currencies[Math.Max(0, r.Money.Selected)].Id] = (long)r.Spin.Value } : null,
            })];

    private void Create()
    {
        if (_ws.Selected is not { } source) return;
        string title = _newTitle.Text.Trim();
        if (title.Length == 0) title = $"{_ws.Session.NameOf(source)} (новый)";
        if (_ws.CreateFrom(source, _ws.NameFor(title), title)) _newTitle.Text = "";
    }

    private void Apply()
    {
        if (_ws.Selected is not { } id) return;
        if (_ws.Edit(id, _ws.FormOf(id) with
        {
            Title = _title.Text.Trim(), Description = _description.Text.Trim(), Race = _race.Selected, Category = Kinds[Math.Max(0, _kind.Selected)].Kind,
            Research = Research(),
        })) _filled = (_filled.Id, _ws.Session.History.Revision);   // своя правка — форма уже такая
    }

    /// <summary>Шаги проверки без окна.</summary>
    public void NewLord(string title)
    {
        _newTitle.Text = title;
        Create();
    }

    public void AddResearch(string spell, long amount)
    {
        AddRow(spell, "gold", amount);
        Apply();
    }

    public string Line() => $"{_ws.Line()} исследований={Research().Count}";
}
