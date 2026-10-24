namespace LastEdict.Data.Ui.Windows;

/// <summary>Что рисует вид дерева построек (<c>BuildTreeView</c>): окно построек партии (<see cref="BuildLogic"/>) и живая страница Студии
/// (<see cref="BuildTreePreview"/>). Места вкладок и дерева — в точках того, кто держит вид; значки и связи — в точках содержимого.</summary>
public interface IBuildTreeSource
{
    UiRect? TreeArea { get; }

    IReadOnlyList<BuildTreeTab> Tabs { get; }

    IReadOnlyList<BuildTreeItem> Items { get; }

    IReadOnlyList<BuildTreeLink> Links { get; }

    ZoomView View { get; }

    int Revision { get; }
}

/// <summary>Значок перенесли на живой странице: постройка и клетка, куда её бросили.</summary>
public sealed record BuildTreeDrop(string Building, double Column, int Row);

/// <summary>
/// Живая страница окна построек в Студии (Э6, пространство «Дерево построек»): то же дерево, те же вкладки, вид с зумом и та же отрисовка,
/// что у окна в игре, но без партии — все значки «можно строить», постройки мода помечены. Значок тащится левой кнопкой в другую клетку
/// (<see cref="Release"/> отдаёт <see cref="BuildTreeDrop"/> — Студия делает из него команду места), пустое место и средняя кнопка — сдвиг,
/// колесо — зум к курсору. Точки — от угла страницы.
/// </summary>
public sealed class BuildTreePreview : IBuildTreeSource
{
    private const int DragSlop = 4;

    private BuildTree? _tree;
    private Func<string, string> _name = id => id;
    private Func<string, bool> _mine = _ => false;
    private Func<string, string?> _text = _ => null;
    private UiRect _page;
    private (int X, int Y)? _press;
    private string? _pressed;
    private bool _dragging;
    private (double X, double Y) _dragAt;

    public UiRect? TreeArea { get; private set; }

    public IReadOnlyList<BuildTreeTab> Tabs { get; private set; } = [];

    public IReadOnlyList<BuildTreeItem> Items { get; private set; } = [];

    public IReadOnlyList<BuildTreeLink> Links { get; private set; } = [];

    public ZoomView View { get; } = new();

    public int Revision { get; private set; }

    /// <summary>Открытая ветка.</summary>
    public string Branch { get; private set; } = "";

    public string? Selected { get; private set; }

    private string? _hovered;

    private BuildTreeBranch? Current => _tree?.Branch(Branch);

    /// <summary>Новое дерево (правка, смена лорда): ветка и вид — прежние, если ветка есть.</summary>
    /// <param name="name">Имя постройки по id.</param>
    /// <param name="text">Строка по ключу (имена веток); нет — null.</param>
    public void Show(BuildTree? tree, Func<string, string> name, Func<string, bool> mine, Func<string, string?>? text = null)
    {
        _tree = tree;
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _mine = mine ?? throw new ArgumentNullException(nameof(mine));
        _text = text ?? (_ => null);
        bool keep = tree?.Branch(Branch) is not null;
        if (!keep) Branch = tree?.Branches.FirstOrDefault(b => b.Nodes.Count > 0)?.Id ?? tree?.Branches.FirstOrDefault()?.Id ?? "";
        Layout(resetView: !keep);
    }

    /// <summary>Размер страницы (точки окна Студии).</summary>
    public void Resize(int width, int height)
    {
        if (_page.Width == width && _page.Height == height) return;
        _page = new UiRect(0, 0, width, height);
        Layout(resetView: TreeArea is null);
    }

    public void Open(string branch)
    {
        if (_tree?.Branch(branch) is null || branch == Branch) return;
        Branch = branch;
        Layout(resetView: true);
    }

    private void Layout(bool resetView)
    {
        if (_page.Width <= 0 || _tree is null)
        {
            (Tabs, Items, Links, TreeArea) = ([], [], [], null);
            Revision++;
            return;
        }
        var (tabs, area) = BuildLogic.TabsAndArea(_page, [.. _tree.Branches.Select(b => (b.Id, BranchName(b)))], Branch, null);
        Tabs = tabs;
        var (cw, ch) = Current is { } c ? BuildTreeGeometry.Content(c) : (0, 0);
        if (resetView || TreeArea is not { } was) View.Reset(area.Width, area.Height, cw, ch);
        else
        {
            if (was.Width != area.Width || was.Height != area.Height) View.Resize(area.Width, area.Height);
            View.Refit(cw, ch);
        }
        TreeArea = area;
        Collect();
    }

    private string BranchName(BuildTreeBranch branch) =>
        branch.NameText is { Length: > 0 } key && _text(key) is { Length: > 0 } text ? text : branch.Id[(branch.Id.LastIndexOf('/') + 1)..];

    private void Collect()
    {
        if (Current is not { } branch)
        {
            (Items, Links) = ([], []);
            Revision++;
            return;
        }
        var items = new List<BuildTreeItem>();
        foreach (var node in branch.Nodes)
        {
            var icon = BuildTreeGeometry.Icon(node);
            var plate = BuildTreeGeometry.Plate(node);
            if (_dragging && node.Building == _pressed)
            {
                // Значок под курсором: середина значка — в точке тащения.
                int dx = (int)Math.Round(_dragAt.X - (icon.X + (icon.Width / 2.0))), dy = (int)Math.Round(_dragAt.Y - (icon.Y + (icon.Height / 2.0)));
                icon = icon with { X = icon.X + dx, Y = icon.Y + dy };
                plate = plate with { X = plate.X + dx, Y = plate.Y + dy };
            }
            items.Add(new BuildTreeItem(node.Building, icon, plate, $"IconBld:{ArtNames.Of(node.Building)}", _name(node.Building), BuildIcon.Can,
                                        node.Building == Selected, node.Building == _hovered, _mine(node.Building)));
        }
        var shown = items.ToDictionary(i => i.Building, StringComparer.Ordinal);
        Links = [.. branch.Nodes.Where(n => n.Parent is not null && shown.ContainsKey(n.Parent!))
            .Select(n => new BuildTreeLink(shown[n.Parent!].Plate, shown[n.Building].Icon, true))];
        Items = items;
        Revision++;
    }

    // ---- мышь (точки страницы)

    public void Press(int x, int y)
    {
        _press = (x, y);
        _pressed = NodeAt(x, y)?.Building;
        _dragging = false;
    }

    public void Motion(int x, int y, bool held)
    {
        if (held && _press is { } from)
        {
            if (!_dragging && Math.Abs(x - from.X) + Math.Abs(y - from.Y) > DragSlop) _dragging = true;
            if (_dragging && _pressed is null)
            {
                View.Pan(x - from.X, y - from.Y);
                _press = (x, y);
            }
            else if (_dragging && TreeArea is { } area) _dragAt = View.ToContent(x - area.X, y - area.Y);
        }
        string? hovered = NodeAt(x, y)?.Building;
        if (hovered != _hovered || _dragging)
        {
            _hovered = hovered;
            Collect();
        }
    }

    /// <summary>Отпускание: значок, перенесённый в другую клетку, — место для команды; щелчок по значку — выбор; по вкладке — ветка.</summary>
    public BuildTreeDrop? Release(int x, int y)
    {
        var (dragging, pressed) = (_dragging, _pressed);
        (_press, _pressed, _dragging) = (null, null, false);
        if (dragging && pressed is not null && TreeArea is { } area && Current?.Node(pressed) is { } node)
        {
            var (cx, cy) = View.ToContent(x - area.X, y - area.Y);
            var (column, row) = BuildTreeGeometry.Cell(cx, cy);
            Collect();
            return column == node.Column && row == node.Row ? null : new BuildTreeDrop(pressed, column, row);
        }
        if (dragging)
        {
            Collect();
            return null;
        }
        if (Tabs.FirstOrDefault(t => Inside(t.Rect, x, y)) is { } tab)
        {
            Selected = null;
            Open(tab.Branch);
            return null;
        }
        Selected = NodeAt(x, y)?.Building;
        Collect();
        return null;
    }

    public void Wheel(int x, int y, int steps)
    {
        if (TreeArea is not { } area || !Inside(area, x, y)) return;
        if (View.ZoomAt(x - area.X, y - area.Y, Math.Pow(ZoomView.WheelStep, steps))) Revision++;
    }

    public void Pan(int dx, int dy)
    {
        if (View.Pan(dx, dy)) Revision++;
    }

    private BuildTreeNode? NodeAt(int x, int y)
    {
        if (TreeArea is not { } area || !Inside(area, x, y) || Current is not { } branch) return null;
        var (cx, cy) = View.ToContent(x - area.X, y - area.Y);
        return BuildTreeGeometry.At(branch, cx, cy);
    }

    /// <summary>Середина значка постройки в точках страницы (для проверок и тащения без мыши); нет — null.</summary>
    public (int X, int Y)? PointOf(string building)
    {
        if (TreeArea is not { } area || Items.FirstOrDefault(i => i.Building == building) is not { } item) return null;
        var (vx, vy) = View.ToView(item.Icon.X + (item.Icon.Width / 2.0), item.Icon.Y + (item.Icon.Height / 2.0));
        return ((int)Math.Round(vx + area.X), (int)Math.Round(vy + area.Y));
    }

    /// <summary>Середина клетки в точках страницы.</summary>
    public (int X, int Y)? PointOfCell(double column, int row)
    {
        if (TreeArea is not { } area) return null;
        var icon = BuildTreeGeometry.Icon(new BuildTreeNode("", column, row, null));
        var (vx, vy) = View.ToView(icon.X + (icon.Width / 2.0), icon.Y + (icon.Height / 2.0));
        return ((int)Math.Round(vx + area.X), (int)Math.Round(vy + area.Y));
    }

    private static bool Inside(UiRect r, int x, int y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
}
