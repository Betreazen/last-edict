using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Юниты» (Студия 2, задача 2): слева — список юнитов (раса, поиск; свои — светлее), посередине — форма выбранного
/// (характеристики, атака, найм, повышение, графика), справа — картинки (лицо, портрет, картина: свой файл или графика основы).
/// «Новый на основе выбранного» — копия со своей атакой. Решения — в <see cref="UnitWorkspace"/>.
/// </summary>
public partial class StudioUnitsTab : HSplitContainer, IStudioTab
{
    private static readonly Dictionary<UnitRole, string> Roles = new()
    {
        [UnitRole.Soldier] = "Солдат", [UnitRole.Leader] = "Лидер", [UnitRole.Noble] = "Вор", [UnitRole.Summon] = "Призванный",
        [UnitRole.Illusion] = "Иллюзия", [UnitRole.NeutralLeader] = "Лидер нейтралов", [UnitRole.NeutralSoldier] = "Солдат нейтралов",
        [UnitRole.Guardian] = "Страж",
    };

    private static readonly Dictionary<BranchKind, string> Branches = new()
    {
        [BranchKind.Fighter] = "Воины", [BranchKind.Archer] = "Стрелки", [BranchKind.Mage] = "Маги", [BranchKind.Special] = "Особые",
        [BranchKind.Sideshow] = "Поддержка", [BranchKind.Hero] = "Герои", [BranchKind.Noble] = "Знать", [BranchKind.Summon] = "Призванные",
    };

    private readonly UnitWorkspace _ws;
    private readonly Func<string, Texture2D?> _texture;
    private readonly OptionButton _raceFilter = new();
    private readonly LineEdit _filter = new() { PlaceholderText = "поиск по имени и id" };
    private readonly ItemList _list = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0) };
    private readonly LineEdit _newTitle = new() { PlaceholderText = "название нового", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button _create = new() { Text = "Новый на основе выбранного", ThemeTypeVariation = SettingsTheme.Primary };
    private readonly Label _head = new() { ThemeTypeVariation = SettingsTheme.GroupLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly LineEdit _title = new(), _name = new();
    private readonly TextEdit _description = new() { CustomMinimumSize = new Vector2(0, 60), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly OptionButton _role = new(), _branch = new();
    // Ссылки — выбором с поиском и значком (юнитов сотни): раса, постройки найма и повышения, из кого вырастает, чья графика.
    private readonly StudioPicker _race = new(), _hireBuilding = new(), _previous = new(), _upgradeBuilding = new(), _graphics = new();
    private readonly SpinBox _level = Spin(1, 99), _hp = Spin(1, 9999), _armor = Spin(0, 100), _regen = Spin(0, 100), _xpKill = Spin(0, 99999),
        _xpNext = Spin(0, 99999), _leadership = Spin(0, 6), _move = Spin(0, 100), _gold = Spin(0, 99999);
    private readonly CheckBox _small = new() { Text = "одна клетка (малый)" }, _hireable = new() { Text = "в списке найма расы" };
    private readonly Button _apply = new() { Text = "Применить", ThemeTypeVariation = SettingsTheme.Primary }, _rename = new() { Text = "Переименовать" },
        _remove = new() { Text = "Удалить" };
    private readonly HBoxContainer _pictures = new();
    private readonly StudioUnitCombat _combat;
    private readonly TabContainer _pages = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly FileDialog _dialog = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = StudioPictures.Filters, UseNativeDialog = true,
    };
    private string _pickingFile = "";
    private List<string?> _raceIds = [];
    private (int Revision, List<PickItem> Units, List<PickItem> Buildings, List<PickItem> Races)? _picks;
    private int _shown = -1;
    private (string? Id, int Revision) _filled = ("-", -1);   // форма перезаполняется по выбору и правке, а не по фильтру списка

    public StudioUnitsTab(UnitWorkspace ws, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(_raceFilter);
        left.AddChild(_filter);
        left.AddChild(_list);
        var newRow = new HBoxContainer();
        newRow.AddChild(_newTitle);
        left.AddChild(newRow);
        left.AddChild(_create);
        AddChild(left);

        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(form);
        AddChild(scroll);

        form.AddChild(_head);
        form.AddChild(_pictures);   // лицо, портрет и картина — строкой над полями
        form.AddChild(Grid(("Название", _title), ("Имя каталога", Row(_name, _rename))));
        form.AddChild(Labeled("Описание", _description));
        // Поля — вкладками: «Основное» (кто, характеристики, найм, повышение, графика) и «Бой и ауры» (атака, иммунитеты, ауры).
        var main = new VBoxContainer { Name = "Основное" };
        _pages.AddChild(main);
        form.AddChild(_pages);
        main.AddChild(Group("Кто", ("Роль", _role), ("Раса", _race), ("Ветка", _branch), ("Уровень", _level)));
        main.AddChild(Group("Характеристики", ("Жизнь", _hp), ("Броня", _armor), ("Регенерация, %", _regen), ("Лидерство", _leadership),
                            ("Опыт за убийство", _xpKill), ("Опыт до уровня", _xpNext), ("Ход (лидер)", _move), ("Размер", _small)));
        main.AddChild(Group("Найм", ("Найм", _hireable), ("Постройка найма", _hireBuilding), ("Цена, золото", _gold)));
        main.AddChild(Group("Повышение", ("Вырастает из", _previous), ("Постройка повышения", _upgradeBuilding)));
        main.AddChild(Group("Графика", ("Анимации и значок — как у", _graphics)));
        var buttons = new HBoxContainer();
        buttons.AddChild(_apply);
        buttons.AddChild(_remove);
        main.AddChild(buttons);
        _combat = new StudioUnitCombat(ws) { Name = "Бой и ауры" };
        _pages.AddChild(_combat);   // бой юнита: атака, иммунитеты, ауры — свои кнопки «Применить»
        AddChild(_dialog);

        // Списки юнитов и построек длинные: ширина поля — по месту, а не по самому длинному пункту.
        foreach (var pick in new[] { _role, _branch, _raceFilter }) pick.FitToLongestItem = false;
        foreach (var r in Enum.GetValues<UnitRole>()) _role.AddItem(Roles[r]);
        foreach (var b in Enum.GetValues<BranchKind>()) _branch.AddItem(Branches[b]);
        _raceFilter.ItemSelected += i => _ws.Find(_raceIds.ElementAtOrDefault((int)i) ?? "", _filter.Text);
        _filter.TextChanged += t => _ws.Find(_raceIds.ElementAtOrDefault(_raceFilter.Selected) ?? "", t);
        _list.ItemSelected += i => _ws.Select(_list.GetItemMetadata((int)i).AsString());
        _create.Pressed += Create;
        _apply.Pressed += Apply;
        _rename.Pressed += () =>
        {
            if (_ws.Selected is { } id) _ws.Rename(id, _name.Text.Trim());
        };
        _remove.Pressed += () =>
        {
            if (_ws.Selected is { } id) _ws.Remove(id);
        };
        _dialog.FileSelected += path => Picked(_pickingFile, path);
    }

    /// <summary>Лицо юнита в списке: своё (из документа) или основы (графика юнита мода — его <c>baseUnit</c>).</summary>
    private Texture2D? Face(string id) =>
        _texture($"Faces:{ArtNames.Of(id)}FACE")
        ?? (_ws.Session.Report.Content?.Find(id) is UnitDef { BaseUnit: { } b } ? _texture($"Faces:{ArtNames.Of(b)}FACE") : null);

    private static SpinBox Spin(int min, int max) => new() { MinValue = min, MaxValue = max, SizeFlagsHorizontal = SizeFlags.ExpandFill };

    private static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer();
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

    private static VBoxContainer Group(string title, params (string Title, Control Field)[] fields)
    {
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.GroupLabel });
        box.AddChild(Grid(fields));
        return box;
    }

    public void Sync()
    {
        if (_shown == _ws.Revision) return;
        _shown = _ws.Revision;
        var session = _ws.Session;
        // Фильтр рас и список.
        if (_raceIds.Count != _ws.Races.Count + 1)
        {
            _raceFilter.Clear();
            _raceIds = [null, .. _ws.Races.Select(r => r.Id)];
            _raceFilter.AddItem("все расы");
            foreach (var r in _ws.Races) _raceFilter.AddItem(session.NameOf(r.Id));
        }
        _list.Clear();
        foreach (var row in _ws.Units())
        {
            int i = _list.AddItem(row.Name == row.Id ? row.Id : $"{row.Name}  ·  {row.Id[(row.Id.LastIndexOf('/') + 1)..]}");
            _list.SetItemMetadata(i, row.Id);
            if (Face(row.Id) is { } face) _list.SetItemIcon(i, face);
            if (row.Origin != EntityOrigin.Base) _list.SetItemCustomFgColor(i, row.Origin == EntityOrigin.Mine ? new Color("#cfe8b0") : SettingsTheme.Gold);
            if (row.Id == _ws.Selected) _list.Select(i);
        }
        _list.IconScale = 0.4f;
        // Форма — когда сменился выбор или документ.
        string? id = _ws.Selected is { } s && session.Report.Content?.Find(s) is UnitDef ? s : null;
        _create.Disabled = id is null;
        // По выбору и правке документа (история), а не по отказу команды: отказ не стирает набранное.
        if (_filled == (id, session.History.Revision)) return;
        _filled = (id, session.History.Revision);
        Fill(id);
    }

    private void Fill(string? id)
    {
        var session = _ws.Session;
        bool own = id is not null && _ws.IsOwnFolder(id);
        var f = id is null ? new UnitForm() : _ws.FormOf(id);
        _head.Text = id is null ? "Выберите юнит слева — правка формой или «Новый на основе выбранного»"
            : own ? $"Свой юнит: {f.Title}" : $"Юнит базы: {f.Title} (правка — слоем мода)";
        (_title.Text, _title.Editable, _description.Text, _description.Editable) = (f.Title, own, f.Description, own);
        (_name.Text, _name.Editable, _rename.Visible, _remove.Visible) = (id is null ? "" : id[(id.LastIndexOf('/') + 1)..], own, own, own);
        _role.Selected = Array.IndexOf(Enum.GetValues<UnitRole>(), f.Role);
        _branch.Selected = (int)f.Branch;
        var (units, buildings, races) = Picks();
        _race.SetItems(races, f.Race);
        (_level.Value, _hp.Value, _armor.Value, _regen.Value) = (f.Level, f.HitPoints, f.Armor, f.Regeneration);
        (_xpKill.Value, _xpNext.Value, _leadership.Value, _move.Value) = (f.XpForKill, f.XpForNextLevel, f.Leadership ?? 0, f.Move ?? 0);
        _small.ButtonPressed = f.Small;
        _hireable.ButtonPressed = f.Hireable;
        _gold.Value = f.HireCost.GetValueOrDefault("gold");
        _hireBuilding.SetItems([new PickItem(null, "— (без постройки)"), .. buildings], f.HireBuilding);
        _upgradeBuilding.SetItems([new PickItem(null, "— (без постройки)"), .. buildings], f.UpgradeBuilding);
        _previous.SetItems([new PickItem(null, "— (нанимается сразу)"), .. units.Where(u => u.Id != id)], f.PreviousLevel);
        _graphics.SetItems([new PickItem(null, "— (своя графика)"), .. units.Where(u => u.Id != id)], f.GraphicsFrom);
        _apply.Disabled = id is null;
        Pictures(id, own);
        _combat.ShowUnit(id);
        // Ошибки проверки — у полей-ссылок и в заголовке.
        if (id is null) return;
        foreach (var (field, path) in new (Control, string)[] { (_race, "race"), (_hireBuilding, "hireBuilding"), (_previous, "previousLevel"),
                                                                 (_upgradeBuilding, "upgradeBuilding"), (_graphics, "baseUnit") })
            StudioMarks.Mark(field, session.ProblemsOf(id, path));
        _head.Text += StudioMarks.Head(session.ProblemsOf(id));
    }

    /// <summary>Пункты выбора ссылок (юниты с лицом, постройки со значком, расы) — раз на правку документа.</summary>
    private (List<PickItem> Units, List<PickItem> Buildings, List<PickItem> Races) Picks()
    {
        var session = _ws.Session;
        if (_picks is { } p && p.Revision == session.History.Revision) return (p.Units, p.Buildings, p.Races);
        string Title(string id) => $"{session.NameOf(id)} · {id[(id.LastIndexOf('/') + 1)..]}";
        var units = (session.Report.Content ?? session.BaseContent).Units.Select(u => new PickItem(u.Id, Title(u.Id), Face(u.Id))).ToList();
        var buildings = _ws.Buildings.Select(b => new PickItem(b.Id, Title(b.Id), _texture($"IconBld:{ArtNames.Of(b.Id)}"))).ToList();
        var races = _ws.Races.Select(r => new PickItem(r.Id, Title(r.Id))).ToList();
        _picks = (session.History.Revision, units, buildings, races);
        return (units, buildings, races);
    }

    /// <summary>Картинки юнита: свой файл (из документа) или графика основы; у своего — «Выбрать…» и «Убрать».</summary>
    private void Pictures(string? id, bool own)
    {
        foreach (var child in _pictures.GetChildren()) { _pictures.RemoveChild(child); child.QueueFree(); }
        if (id is null) return;
        foreach (var slot in EntityArt.Of("unit"))
        {
            var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            cell.AddChild(new Label { Text = $"{slot.Title} — {slot.W}×{slot.H}", ThemeTypeVariation = SettingsTheme.HelpLabel });
            var png = _ws.PictureOf(id, slot.File);
            string baseArt = ArtNames.Of(_ws.FormOf(id).GraphicsFrom ?? id) + slot.Suffix;
            var texture = png is { } bytes ? StudioPictures.Texture(bytes, slot) : _texture($"{slot.ArchiveName}:{baseArt}");
            float scale = Math.Min(1f, 142f / slot.H);
            var row = new HBoxContainer();
            row.AddChild(new TextureRect
            {
                Texture = texture, CustomMinimumSize = new Vector2(slot.W * scale, slot.H * scale), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var buttons = new VBoxContainer();
            row.AddChild(buttons);
            if (own)
            {
                string file = slot.File;
                var choose = new Button { Text = png is null ? "Своя картинка…" : "Заменить…" };
                choose.Pressed += () => { _pickingFile = file; _dialog.Title = slot.Title; _dialog.PopupCentered(new Vector2I(900, 600)); };
                buttons.AddChild(choose);
                if (png is not null)
                {
                    var clear = new Button { Text = "Убрать (графика основы)" };
                    clear.Pressed += () => _ws.SetPicture(id, file, null);
                    buttons.AddChild(clear);
                }
            }
            buttons.AddChild(new Label { Text = png is null ? "графика основы" : "своя", ThemeTypeVariation = SettingsTheme.HelpLabel });
            cell.AddChild(row);
            _pictures.AddChild(cell);
        }
    }

    /// <summary>Выбран файл картинки: вписать в место и положить командой.</summary>
    public bool Picked(string file, string path)
    {
        if (_ws.Selected is not { } id || EntityArt.Find("unit", file) is not { } slot) return false;
        var (png, problem) = StudioPictures.FromFile(path, slot);
        return png is { } bytes ? _ws.SetPicture(id, file, bytes) : _ws.Session.Refuse(problem ?? "картинка не читается");
    }

    private void Create()
    {
        if (_ws.Selected is not { } source) return;
        string title = _newTitle.Text.Trim();
        if (title.Length == 0) title = $"{_ws.Session.NameOf(source)} (новый)";
        _ws.CreateFrom(source, _ws.NameFor(title), title);
        _newTitle.Text = "";
    }

    /// <summary>Поля формы сейчас (по выбранному юниту).</summary>
    private UnitForm Current(string id)
    {
        var was = _ws.FormOf(id);
        long gold = (long)_gold.Value;
        var cost = was.HireCost.Where(c => c.Key != "gold").ToDictionary(c => c.Key, c => c.Value);
        if (gold > 0) cost["gold"] = gold;
        return was with
        {
            Title = _title.Text.Trim(), Description = _description.Text.Trim(), Role = Enum.GetValues<UnitRole>()[Math.Max(0, _role.Selected)],
            Race = _race.Selected, Branch = (BranchKind)Math.Max(0, _branch.Selected), Level = (int)_level.Value,
            HitPoints = (int)_hp.Value, Armor = (int)_armor.Value, Regeneration = (int)_regen.Value, XpForKill = (int)_xpKill.Value,
            XpForNextLevel = (int)_xpNext.Value, Leadership = was.Leadership is null && _leadership.Value == 0 ? null : (int)_leadership.Value,
            Move = was.Move is null && _move.Value == 0 ? null : (int)_move.Value, Small = _small.ButtonPressed,
            Hireable = _hireable.ButtonPressed, HireBuilding = _hireBuilding.Selected, HireCost = cost,
            PreviousLevel = _previous.Selected, UpgradeBuilding = _upgradeBuilding.Selected, GraphicsFrom = _graphics.Selected,
        };
    }

    private void Apply()
    {
        if (_ws.Selected is { } id) _ws.Edit(id, Current(id));
    }

    /// <summary>Шаги проверки без окна: числа формы и «Применить».</summary>
    public void Fill(int? hp, bool? hireable, string? title)
    {
        if (hp is { } h) _hp.Value = h;
        if (hireable is { } on) _hireable.ButtonPressed = on;
        if (title is not null) _title.Text = title;
    }

    public void Press() => Apply();

    public void NewTitle(string title) => _newTitle.Text = title;

    public void PressCreate() => Create();

    public StudioUnitCombat Combat => _combat;

    /// <summary>Вкладка формы: 0 — «Основное», 1 — «Бой и ауры».</summary>
    public void Page(int index) => _pages.CurrentTab = index;

    public string Line() => $"{_ws.Line()} {_combat.Line()} картинок={_pictures.GetChildCount()} форма_жизнь={(int)_hp.Value}";
}
