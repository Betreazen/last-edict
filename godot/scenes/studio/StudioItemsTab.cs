using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui;
using LastEdict.Rules.Magic;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Предметы» (Студия 2, задача 4): слева — список (поиск; свои — светлее), «Новый» (на основе выбранного или пустой); справа —
/// форма: значок 70×85 (файл вписывается в место, HD), название, имя каталога, описание, категория, цена, модификатор надетого, зелье
/// (модификатор и лечение), заклинание, атака. Решения — в <see cref="ItemWorkspace"/>.
/// </summary>
public partial class StudioItemsTab : HSplitContainer, IStudioTab
{
    private static readonly Dictionary<ItemCategory, string> Categories = new()
    {
        [ItemCategory.Armor] = "Доспех", [ItemCategory.Jewel] = "Украшение", [ItemCategory.Weapon] = "Оружие", [ItemCategory.Banner] = "Знамя",
        [ItemCategory.PotionBoost] = "Зелье усиления", [ItemCategory.PotionHeal] = "Зелье лечения", [ItemCategory.PotionRevive] = "Зелье воскрешения",
        [ItemCategory.PotionPermanent] = "Эликсир", [ItemCategory.Scroll] = "Свиток", [ItemCategory.Wand] = "Посох", [ItemCategory.Valuable] = "Ценность",
        [ItemCategory.Orb] = "Сфера", [ItemCategory.Talisman] = "Талисман", [ItemCategory.TravelItem] = "Предмет похода", [ItemCategory.Special] = "Особый",
    };

    private readonly ItemWorkspace _ws;
    private readonly Func<string, Texture2D?> _texture;
    private readonly LineEdit _filter = new() { PlaceholderText = "поиск по имени и id" };
    private readonly ItemList _list = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0), IconScale = 0.4f };
    private readonly LineEdit _newTitle = new() { PlaceholderText = "название нового" };
    private readonly Label _head = new() { ThemeTypeVariation = SettingsTheme.GroupLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly TextureRect _icon = new()
    {
        CustomMinimumSize = new Vector2(140, 170), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
    };
    private readonly LineEdit _title = new(), _name = new();
    private readonly TextEdit _description = new() { CustomMinimumSize = new Vector2(0, 60), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly OptionButton _category = new();
    private readonly SpinBox _gold = new() { MaxValue = 999999, Step = 1, CustomArrowStep = 25 }, _heal = new() { MaxValue = 9999 };
    private readonly StudioPicker _equip = new(), _potion = new(), _spell = new(), _attack = new();
    private readonly Button _apply = new() { Text = "Применить", ThemeTypeVariation = SettingsTheme.Primary }, _rename = new() { Text = "Переименовать" },
        _remove = new() { Text = "Удалить" }, _choose = new() { Text = "Своя картинка…" };
    private readonly FileDialog _dialog = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = StudioPictures.Filters, UseNativeDialog = true,
        Title = "Значок предмета",
    };
    private int _shown = -1;
    private (string? Id, int Revision) _filled = ("-", -1);

    public StudioItemsTab(ItemWorkspace ws, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(_filter);
        left.AddChild(_list);
        left.AddChild(_newTitle);
        var create = new Button { Text = "Новый (на основе выбранного)", ThemeTypeVariation = SettingsTheme.Primary };
        left.AddChild(create);
        AddChild(left);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(form);
        AddChild(scroll);
        form.AddChild(_head);
        var top = new HBoxContainer();
        top.AddChild(_icon);
        var pictureButtons = new VBoxContainer();
        pictureButtons.AddChild(_choose);
        top.AddChild(pictureButtons);
        form.AddChild(top);
        foreach (var c in Enum.GetValues<ItemCategory>()) _category.AddItem(Categories[c]);
        form.AddChild(Grid(("Название", _title), ("Имя каталога", Row(_name, _rename)), ("Категория", _category), ("Цена, золото", _gold),
                           ("Надетый даёт", _equip), ("Зелье даёт", _potion), ("Зелье лечит", _heal), ("Заклинание", _spell), ("Атака", _attack)));
        form.AddChild(new Label { Text = "Описание", ThemeTypeVariation = SettingsTheme.HelpLabel });
        form.AddChild(_description);
        var buttons = new HBoxContainer();
        buttons.AddChild(_apply);
        buttons.AddChild(_remove);
        form.AddChild(buttons);
        AddChild(_dialog);

        _filter.TextChanged += t => _ws.Find(t);
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
        _choose.Pressed += () => _dialog.PopupCentered(new Vector2I(900, 600));
        _dialog.FileSelected += path => Picked(path);
    }

    private static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer();
        foreach (var c in items) row.AddChild(c);
        items[0].SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return row;
    }

    private static GridContainer Grid(params (string Title, Control Field)[] fields)
    {
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (title, field) in fields)
        {
            grid.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.HelpLabel });
            field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(field);
        }
        return grid;
    }

    private Texture2D? Icon(string id) => _texture($"IconItem:{ArtNames.Of(id)}");

    public void Sync()
    {
        if (_shown == _ws.Revision) return;
        _shown = _ws.Revision;
        _list.Clear();
        foreach (var row in _ws.Items())
        {
            int i = Icon(row.Id) is { } icon ? _list.AddItem(row.Name == row.Id ? row.Id : $"{row.Name}  ·  {row.Id[(row.Id.LastIndexOf('/') + 1)..]}", icon)
                                              : _list.AddItem(row.Name == row.Id ? row.Id : $"{row.Name}  ·  {row.Id[(row.Id.LastIndexOf('/') + 1)..]}");
            _list.SetItemMetadata(i, row.Id);
            if (row.Origin != EntityOrigin.Base) _list.SetItemCustomFgColor(i, row.Origin == EntityOrigin.Mine ? new Color("#cfe8b0") : SettingsTheme.Gold);
            if (row.Id == _ws.Selected) _list.Select(i);
        }
        string? id = _ws.Selected is { } s && _ws.Session.Report.Content?.Find(s) is ItemDef ? s : null;
        if (_filled == (id, _ws.Session.History.Revision)) return;
        _filled = (id, _ws.Session.History.Revision);
        Fill(id);
    }

    private void Fill(string? id)
    {
        var session = _ws.Session;
        bool own = id is not null && _ws.IsOwnFolder(id);
        var f = id is null ? new ItemForm() : _ws.FormOf(id);
        _head.Text = id is null ? "Выберите предмет слева — правка формой или «Новый»"
            : (own ? $"Свой предмет: {f.Title}" : $"Предмет базы: {f.Title} (правка — слоем мода)") + StudioMarks.Head(session.ProblemsOf(id));
        (_title.Text, _title.Editable, _description.Text, _description.Editable) = (f.Title, own, f.Description, own);
        (_name.Text, _name.Editable, _rename.Visible, _remove.Visible, _choose.Visible) =
            (id is null ? "" : id[(id.LastIndexOf('/') + 1)..], own, own, own, own);
        _category.Selected = (int)f.Category;
        (_gold.Value, _heal.Value) = (f.Value.GetValueOrDefault("gold"), f.PotionHeal ?? 0);
        string Title(string i) => $"{session.NameOf(i)} · {i[(i.LastIndexOf('/') + 1)..]}";
        var modifiers = (List<PickItem>)[new PickItem(null, "—"), .. _ws.Modifiers.Select(m => new PickItem(m.Id, m.Comment is { Length: > 0 } c ? c : Title(m.Id)))];
        _equip.SetItems(modifiers, f.EquipModifier);
        _potion.SetItems(modifiers, f.PotionModifier);
        _spell.SetItems([new PickItem(null, "—"), .. _ws.Spells.Select(sp => new PickItem(sp.Id, Title(sp.Id), _texture($"IconSpel:{ArtNames.Of(sp.Id)}")))], f.Spell);
        _attack.SetItems([new PickItem(null, "—"), .. _ws.Attacks.Select(a => new PickItem(a.Id, Title(a.Id)))], f.Attack);
        _icon.Texture = id is null ? null : _ws.IconOf(id) is { } png ? StudioPictures.Texture(png, EntityArt.Find("item", EntityFolders.IconFile)) : Icon(id);
        _apply.Disabled = id is null;
        if (id is not null)
            foreach (var (field, path) in new (Control, string)[] { (_equip, "equipModifier"), (_potion, "potionModifier"), (_spell, "spell"), (_attack, "attack") })
                StudioMarks.Mark(field, session.ProblemsOf(id, path));
    }

    private void Create()
    {
        string title = _newTitle.Text.Trim();
        if (title.Length == 0) title = _ws.Selected is { } s ? $"{_ws.Session.NameOf(s)} (новый)" : "Новый предмет";
        _ws.Create(_ws.NameFor(title), title, _ws.Selected);
        _newTitle.Text = "";
    }

    private void Apply()
    {
        if (_ws.Selected is not { } id) return;
        var was = _ws.FormOf(id);
        long gold = (long)_gold.Value;
        var value = was.Value.Where(c => c.Key != "gold").ToDictionary(c => c.Key, c => c.Value);
        if (gold > 0) value["gold"] = gold;
        _ws.Edit(id, was with
        {
            Title = _title.Text.Trim(), Description = _description.Text.Trim(), Category = (ItemCategory)Math.Max(0, _category.Selected), Value = value,
            EquipModifier = _equip.Selected, PotionModifier = _potion.Selected, PotionHeal = _heal.Value > 0 ? (int)_heal.Value : was.PotionHeal is null ? null : 0,
            Spell = _spell.Selected, Attack = _attack.Selected,
        });
    }

    /// <summary>Выбран файл значка: вписать в место 70×85 и положить командой.</summary>
    public bool Picked(string path)
    {
        if (_ws.Selected is not { } id) return false;
        var (png, problem) = StudioPictures.FromFile(path, EntityArt.Find("item", EntityFolders.IconFile)!);
        return png is { } bytes ? _ws.SetIcon(id, bytes) : _ws.Session.Refuse(problem ?? "картинка не читается");
    }

    /// <summary>Шаги проверки без окна.</summary>
    public void NewItem(string title)
    {
        _newTitle.Text = title;
        Create();
    }

    public void Fill(long? gold, string? equip)
    {
        if (gold is { } g) _gold.Value = g;
        if (equip is not null) _equip.Select(equip);
    }

    public void Press() => Apply();

    public string Line() => $"{_ws.Line()} значок={(_icon.Texture is null ? 0 : 1)}";
}
