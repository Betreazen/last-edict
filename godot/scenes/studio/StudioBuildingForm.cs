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
/// Форма постройки в «Дереве построек» (Студия 2): новая — название (имя каталога — само, по-латински), описание, картинка (файл → значок
/// места, HD), требуемая, уровень, цена, категория, ветка юнитов и ветка окна, «у всех лордов расы»; выбранная — те же поля, «Применить» —
/// одна команда, у своей ещё имя каталога и «Удалить». Решения — в <see cref="TreeWorkspace"/>, здесь узлы и ввод.
/// </summary>
public partial class StudioBuildingForm : ScrollContainer
{
    private static readonly (string Id, string Title)[] Currencies =
        [("gold", "Золото"), ("infernal", "Мана Преисподней"), ("life", "Мана Жизни"), ("death", "Мана Смерти"), ("runic", "Мана Рун"), ("grove", "Мана Рощи")];

    private static readonly Dictionary<BuildingCategory, string> CategoryTitles = new()
    {
        [BuildingCategory.Guild] = "Гильдия", [BuildingCategory.Heal] = "Лечение", [BuildingCategory.Magic] = "Магия", [BuildingCategory.Unit] = "Найм",
    };

    private static readonly Dictionary<BranchKind, string> BranchTitles = new()
    {
        [BranchKind.Fighter] = "Воины", [BranchKind.Archer] = "Стрелки", [BranchKind.Mage] = "Маги", [BranchKind.Special] = "Особые",
        [BranchKind.Sideshow] = "Поддержка", [BranchKind.Hero] = "Герои", [BranchKind.Noble] = "Знать", [BranchKind.Summon] = "Призванные",
    };

    private readonly TreeWorkspace _ws;
    private readonly Func<string, Texture2D?> _texture;
    private readonly Label _head = new() { ThemeTypeVariation = SettingsTheme.GroupLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly TextureRect _picture = new()
    {
        CustomMinimumSize = new Vector2(154, 148), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
    };
    private readonly Label _pictureNote = new() { ThemeTypeVariation = SettingsTheme.HelpLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _choose = new() { Text = "Картинка…" }, _clearPicture = new() { Text = "Убрать" };
    private readonly LineEdit _title = new() { PlaceholderText = "Башня мага" };
    private readonly LineEdit _name = new() { PlaceholderText = "имя каталога (латиница)" };
    private readonly Button _rename = new() { Text = "Переименовать" };
    private readonly TextEdit _description = new() { CustomMinimumSize = new Vector2(0, 70), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly OptionButton _requires = new() { FitToLongestItem = false };
    private readonly SpinBox _level = new() { MinValue = 1, MaxValue = 20 };
    private readonly Dictionary<string, SpinBox> _cost = [];
    private readonly OptionButton _category = new(), _unitBranch = new(), _treeBranch = new();
    private readonly CheckBox _raceLords = new() { Text = "у всех лордов расы", ButtonPressed = true };
    private readonly Button _new = new() { Text = "Новая постройка" }, _apply = new() { Text = "Создать", ThemeTypeVariation = SettingsTheme.Primary },
        _remove = new() { Text = "Удалить" };
    private readonly FileDialog _dialog = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = StudioPictures.Filters,
        UseNativeDialog = true, Title = "Картинка постройки",
    };

    private List<string?> _requireIds = [];
    private List<string?> _treeIds = [];
    private ImmutableArray<byte>? _pendingIcon;   // картинка новой постройки до «Создать»
    private (string? Id, int Edits, string Lord) _shown = ("-", -1, "");
    private readonly Dictionary<Texture2D, Texture2D> _small = [];
    private bool _nameTouched;

    public StudioBuildingForm(TreeWorkspace ws, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        CustomMinimumSize = new Vector2(380, 0);
        HorizontalScrollMode = ScrollMode.Disabled;
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        var top = new HBoxContainer();
        top.AddChild(_head);
        _head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(_new);
        box.AddChild(top);
        var pictureRow = new HBoxContainer();
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", SettingsTheme.Plate(new Color("#1b1510"), SettingsTheme.GoldSoft, small: true));
        frame.AddChild(_picture);
        pictureRow.AddChild(frame);
        var pictureButtons = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pictureButtons.AddChild(_choose);
        pictureButtons.AddChild(_clearPicture);
        pictureButtons.AddChild(_pictureNote);
        pictureRow.AddChild(pictureButtons);
        box.AddChild(pictureRow);
        box.AddChild(Labeled("Название", _title));
        var nameRow = new HBoxContainer();
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameRow.AddChild(_name);
        nameRow.AddChild(_rename);
        box.AddChild(Labeled("Имя каталога", nameRow));
        box.AddChild(Labeled("Описание", _description));
        box.AddChild(Labeled("Требует", _requires));
        box.AddChild(Labeled("Уровень", _level));
        var cost = new GridContainer { Columns = 2 };
        foreach (var (id, title) in Currencies)
        {
            cost.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.HelpLabel });
            var spin = new SpinBox { MinValue = 0, MaxValue = 100000, Step = 25, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _cost[id] = spin;
            cost.AddChild(spin);
        }
        box.AddChild(Labeled("Цена", cost));
        foreach (var c in Enum.GetValues<BuildingCategory>()) _category.AddItem(CategoryTitles[c]);
        box.AddChild(Labeled("Категория", _category));
        _unitBranch.AddItem("—");
        foreach (var b in Enum.GetValues<BranchKind>()) _unitBranch.AddItem(BranchTitles[b]);
        box.AddChild(Labeled("Ветка юнитов (найм)", _unitBranch));
        box.AddChild(Labeled("Вкладка окна построек", _treeBranch));
        box.AddChild(_raceLords);
        var buttons = new HBoxContainer();
        buttons.AddChild(_apply);
        buttons.AddChild(_remove);
        box.AddChild(buttons);
        AddChild(_dialog);

        _new.Pressed += () => { _ws.Select(null); Refill(force: true); };
        _choose.Pressed += () => _dialog.PopupCentered(new Vector2I(900, 600));
        _dialog.FileSelected += path => Picked(path);
        _clearPicture.Pressed += () =>
        {
            if (Editing is { } id) _ws.SetIcon(id, null);
            else (_pendingIcon, _picture.Texture) = (null, null);
        };
        _title.TextChanged += t =>
        {
            if (Editing is null && !_nameTouched) _name.Text = _ws.NameFor(t);
        };
        _name.TextChanged += _ => _nameTouched = true;
        _rename.Pressed += () =>
        {
            if (Editing is { } id) _ws.RenameBuilding(id, _name.Text.Trim());
        };
        _requires.ItemSelected += i =>
        {
            // Новая под другой требуемой — уровень и ветки по ней (поля можно поправить).
            if (Editing is not null || _requireIds[(int)i] is not { } parent) return;
            var form = _ws.NewForm(parent);
            (_level.Value, _category.Selected) = (form.Level, (int)form.Category);
            _unitBranch.Selected = form.UnitBranch is { } ub ? (int)ub + 1 : 0;
        };
        _apply.Pressed += Apply;
        _remove.Pressed += () =>
        {
            if (Editing is { } id) _ws.RemoveBuilding(id);
        };
    }

    /// <summary>Открытая в форме постройка; null — форма новой.</summary>
    private string? Editing => _ws.Selected is { } id && _ws.Session.Report.Content?.Find(id) is BuildingDef ? id : null;

    private static VBoxContainer Labeled(string title, Control field)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(new Label { Text = title, ThemeTypeVariation = SettingsTheme.HelpLabel });
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(field);
        return box;
    }

    /// <summary>Перенести в поля выбранную (или новую) постройку, когда сменился выбор или документ.</summary>
    public void Sync() => Refill(force: false);

    private void Refill(bool force)
    {
        // Поля перезаполняются по смене выбора, правке документа (история) или лорда — не по отказу команды: отказ не стирает набранное.
        string? id = Editing;
        var shown = (id, _ws.Session.History.Revision, _ws.Lord);
        if (!force && shown == _shown) return;
        bool newForm = id is null && (force || _shown.Id is not null);
        _shown = shown;
        bool own = id is not null && _ws.IsOwnFolder(id);
        var form = id is null ? (newForm ? _ws.NewForm(null) : CurrentForm()) : _ws.FormOf(id);
        _head.Text = id is null ? "Новая постройка" : own ? $"Своя постройка: {_ws.Session.NameOf(id)}" : $"Постройка базы: {_ws.Session.NameOf(id)} (правка — слоем мода)";
        _title.Text = form.Title;
        _title.Editable = id is null || own;
        _description.Text = form.Description;
        _description.Editable = id is null || own;
        if (id is null && newForm) (_name.Text, _nameTouched, _pendingIcon) = ("", false, null);
        else if (id is not null) (_name.Text, _nameTouched) = (id[(id.LastIndexOf('/') + 1)..], false);
        _name.Editable = id is null || own;
        _rename.Visible = own;
        _remove.Visible = own;
        _raceLords.Visible = id is null;
        _apply.Text = id is null ? "Создать" : "Применить";
        _choose.Disabled = id is not null && !own;
        _clearPicture.Disabled = _choose.Disabled;
        // Требуемая — постройки дерева открытого лорда, кроме самой (и её потомков не отсекаем: круг — отказ ядра с текстом).
        _requires.Clear();
        _requireIds = [null];
        _requires.AddItem("— (корень ветки)");
        foreach (var n in _ws.Nodes.Where(n => n.Building != id))
        {
            if (Small($"IconBld:{ArtNames.Of(n.Building)}") is { } icon) _requires.AddIconItem(icon, n.Name);
            else _requires.AddItem(n.Name);
            _requireIds.Add(n.Building);
        }
        _requires.Selected = Math.Max(0, _requireIds.IndexOf(form.Requires));
        _level.Value = form.Level;
        foreach (var (currency, spin) in _cost) spin.Value = form.Cost.GetValueOrDefault(currency);
        _category.Selected = (int)form.Category;
        _unitBranch.Selected = form.UnitBranch is { } ub ? (int)ub + 1 : 0;
        _treeBranch.Clear();
        _treeIds = [null];
        _treeBranch.AddItem("— (по категории или требуемой)");
        foreach (var b in _ws.TreeBranches)
        {
            _treeBranch.AddItem(b.NameText is { } key && _ws.Session.Text(key) is { Length: > 0 } t ? t : b.Id);
            _treeIds.Add(b.Id);
        }
        _treeBranch.Selected = Math.Max(0, _treeIds.IndexOf(form.TreeBranch));
        var png = id is null ? _pendingIcon : _ws.IconOf(id);
        _picture.Texture = png is { } bytes ? StudioPictures.Texture(bytes) : id is not null ? _texture($"IconBld:{ArtNames.Of(id)}") : null;
        _pictureNote.Text = Note(png);
    }

    /// <summary>Значок списка «Требует» (26×25) — уменьшенный кадр, один раз на кадр.</summary>
    private Texture2D? Small(string frame)
    {
        // Ключ — сама текстура кадра: своя картинка после замены — другая текстура, уменьшается заново.
        if (_texture(frame) is not { } full) return null;
        if (_small.TryGetValue(full, out var cached)) return cached;
        var image = full.GetImage();
        image.Resize(26, 25, Image.Interpolation.Bilinear);
        return _small[full] = ImageTexture.CreateFromImage(image);
    }

    private static string Note(ImmutableArray<byte>? png)
    {
        var slot = EntityArt.Find("building", EntityFolders.IconFile)!;
        if (png is not { } bytes) return $"значок {slot.W}×{slot.H}; любой файл впишется сам (крупный — HD)";
        var (w, h) = EntityArt.PngSize(bytes.AsSpan());
        return EntityArt.ScaleOf(slot, w, h) is { } k ? $"{w}×{h}" + (k > 1 ? $" — HD ×{k}" : "") : $"{w}×{h} — не размер места";
    }

    /// <summary>Поля формы сейчас.</summary>
    private BuildingForm CurrentForm() => new()
    {
        Title = _title.Text.Trim(), Description = _description.Text.Trim(),
        Requires = _requireIds.ElementAtOrDefault(_requires.Selected), Level = (int)_level.Value,
        Cost = _cost.Where(c => c.Value.Value > 0).ToDictionary(c => c.Key, c => (long)c.Value.Value),
        Category = (BuildingCategory)Math.Max(0, _category.Selected),
        UnitBranch = _unitBranch.Selected > 0 ? (BranchKind)(_unitBranch.Selected - 1) : null,
        TreeBranch = _treeIds.ElementAtOrDefault(_treeBranch.Selected),
    };

    private void Apply()
    {
        var form = CurrentForm();
        if (Editing is { } id) _ws.EditBuilding(id, form);
        else
        {
            string name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : _ws.NameFor(form.Title);
            _ws.CreateBuilding(name, form, _pendingIcon, _raceLords.ButtonPressed);
        }
    }

    /// <summary>Выбран файл картинки: вписать в место; у открытой постройки — сразу командой, у новой — до «Создать».</summary>
    public bool Picked(string path)
    {
        var (png, problem) = StudioPictures.FromFile(path, EntityArt.Find("building", EntityFolders.IconFile)!);
        if (png is not { } bytes) return _ws.Session.Refuse(problem ?? "картинка не читается");
        if (Editing is { } id) return _ws.SetIcon(id, bytes);
        _pendingIcon = bytes;
        _picture.Texture = StudioPictures.Texture(bytes);
        _pictureNote.Text = Note(bytes);
        return true;
    }

    /// <summary>Заполнить форму без мыши (проверка): название, требуемая, уровень, золото.</summary>
    public void Fill(string title, string? requires, int? level, long? gold)
    {
        _title.Text = title;
        if (Editing is null && !_nameTouched) _name.Text = _ws.NameFor(title);
        if (requires is not null)
        {
            _requires.Selected = Math.Max(0, _requireIds.IndexOf(requires));
            if (Editing is null && _ws.NewForm(requires) is var f) (_level.Value, _category.Selected) = (f.Level, (int)f.Category);
        }
        if (level is { } l) _level.Value = l;
        if (gold is { } g) _cost["gold"].Value = g;
    }

    /// <summary>«Создать» / «Применить» без мыши.</summary>
    public void Press() => Apply();

    public string Line() =>
        $"форма: {(Editing is { } id ? $"постройка={id}" : "новая")} имя={(_name.Text.Length > 0 ? _name.Text : "-")} картинка={(_picture.Texture is null ? 0 : 1)} " +
        $"требует={_requireIds.ElementAtOrDefault(_requires.Selected) ?? "-"} уровень={(int)_level.Value}";
}
