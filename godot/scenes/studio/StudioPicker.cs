using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LastEdict.Godot;

/// <summary>Пункт выбора ссылки: id (null — «нет»), подпись и значок.</summary>
public sealed record PickItem(string? Id, string Title, Texture2D? Icon = null);

/// <summary>
/// Выбор ссылки в формах Студии (Студия 2, задача 4): кнопка с именем и значком выбранного; щелчок — окно со строкой поиска (по имени и id, без
/// учёта регистра) и списком со значками; Enter или двойной щелчок — выбор. Вместо длинного выпадающего списка на сотни юнитов и построек.
/// </summary>
public partial class StudioPicker : Button
{
    private readonly PopupPanel _popup = new();
    private readonly LineEdit _search = new() { PlaceholderText = "поиск по имени и id" };
    private readonly ItemList _list = new() { CustomMinimumSize = new Vector2(460, 360), IconScale = 0.4f };
    private IReadOnlyList<PickItem> _items = [];

    public StudioPicker()
    {
        Alignment = HorizontalAlignment.Left;
        ClipText = true;
        ExpandIcon = false;
        AddThemeConstantOverride("icon_max_width", 28);   // значок строки — маленький, кнопка не растёт под лицо 70×85
        var box = new VBoxContainer();
        box.AddChild(_search);
        box.AddChild(_list);
        _popup.AddChild(box);
        AddChild(_popup);
        Pressed += Open;
        _search.TextChanged += _ => Fill();
        _search.TextSubmitted += _ => Choose(_list.GetSelectedItems() is { Length: > 0 } s ? s[0] : 0);
        _list.ItemActivated += i => Choose((int)i);
    }

    /// <summary>Выбранный id (null — «нет»).</summary>
    public string? Selected { get; private set; }

    /// <summary>Выбор сменился рукой.</summary>
    public event Action<string?>? Picked;

    /// <summary>Пункты и выбранный (без события).</summary>
    public void SetItems(IReadOnlyList<PickItem> items, string? selected)
    {
        _items = items ?? [];
        Select(selected);
    }

    /// <summary>Выбрать без события (форма заполняется).</summary>
    public void Select(string? id)
    {
        Selected = id;
        var item = _items.FirstOrDefault(i => i.Id == id) ?? _items.FirstOrDefault(i => i.Id is null);
        Text = item?.Title ?? id ?? "—";
        Icon = item?.Icon;
        TooltipText = id ?? "";
    }

    private void Open()
    {
        _search.Text = "";
        Fill();
        _popup.PopupCentered();
        _search.GrabFocus();
    }

    private void Fill()
    {
        _list.Clear();
        string q = _search.Text.Trim();
        foreach (var item in _items.Where(i => q.Length == 0 || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                               || (i.Id?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            int at = item.Icon is { } icon ? _list.AddItem(item.Title, icon) : _list.AddItem(item.Title);
            _list.SetItemMetadata(at, item.Id ?? "");
            if (item.Id == Selected) _list.Select(at);
        }
        // С поиском — верхний найденный (Enter берёт его), без поиска — выбранный.
        if (_list.ItemCount > 0 && (q.Length > 0 || _list.GetSelectedItems().Length == 0)) _list.Select(0);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _list.ItemCount) return;
        string id = _list.GetItemMetadata(index).AsString();
        Select(id.Length == 0 ? null : id);
        _popup.Hide();
        Picked?.Invoke(Selected);
    }

    /// <summary>Выбрать по id рукой (проверка без окна): как Enter в окне.</summary>
    public void Pick(string? id)
    {
        Select(id);
        Picked?.Invoke(Selected);
    }
}
