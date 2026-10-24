using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Authoring.Validation;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Дерево построек» (двухпанельный UX старой утилиты <c>ui/buildtree.py</c>): слева — граф цепочек требований
/// (<see cref="GraphEdit"/>; узел — значок, имя и юнит постройки; стрелку тянут от требуемой к постройке, разрывают — тем же жестом с
/// Ctrl), справа — живая страница окна построек (<see cref="BuildTreeView"/> по <see cref="BuildTreePreview"/> — то же, что в игре;
/// значок переносится мышью в другую клетку), снизу — панель ошибок проверки мода. Правки — методами <see cref="TreeWorkspace"/>.
/// </summary>
public partial class StudioTreeTab : VSplitContainer, IStudioTab
{
    private readonly TreeWorkspace _ws;
    private readonly Func<string, Texture2D?> _texture;
    private readonly GraphEdit _graph = new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, RightDisconnects = true, ShowArrangeButton = false,
        MinimapEnabled = false, ShowGridButtons = false,
    };
    private bool _fitted;
    private readonly Control _page = new() { ClipContents = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(480, 300) };
    private readonly ItemList _errors = new() { CustomMinimumSize = new Vector2(0, 110) };
    private readonly BuildTreeView _view;
    private readonly Dictionary<string, string> _nodeOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _buildingOf = new(StringComparer.Ordinal);
    private int _graphRevision = -1;

    public StudioTreeTab(TreeWorkspace ws, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [120] };
        _page.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(_graph);
        split.AddChild(_page);
        AddChild(split);
        AddChild(_errors);
        _view = new BuildTreeView(ws.Preview, texture);
        _page.AddChild(_view);
        _page.Resized += () => { _ws.Preview.Resize((int)_page.Size.X, (int)_page.Size.Y); _view.Sync(Vector2I.Zero); };
        _graph.Resized += () =>
        {
            if (_fitted) return;
            _graphRevision = -1;   // место графа известно — вписать его при первом показе
            Sync();
        };
        _page.GuiInput += PageInput;
        _graph.ConnectionRequest += (from, _, to, _) =>
        {
            if (_buildingOf.TryGetValue(from, out var a) && _buildingOf.TryGetValue(to, out var b)) _ws.Connect(a, b);
        };
        _graph.DisconnectionRequest += (_, _, to, _) =>
        {
            if (_buildingOf.TryGetValue(to, out var b)) _ws.Disconnect(b);
        };
    }

    public void Sync()
    {
        if (_graphRevision != _ws.Revision) RebuildGraph();
        _view.Sync(Vector2I.Zero);
        _errors.Clear();
        foreach (var item in _ws.Session.Report.Items)
        {
            int i = _errors.AddItem($"{item.Rule}  {item.Target}: {item.Message}");
            _errors.SetItemCustomFgColor(i, item.Level switch
            {
                CheckLevel.Error => new Color("#e07a5f"), CheckLevel.Warning => SettingsTheme.Gold, _ => SettingsTheme.Muted,
            });
        }
        if (_ws.Session.Report.Items.Count == 0) _errors.AddItem("ошибок нет");
    }

    /// <summary>Граф заново по узлам пространства: места — из автораскладки (ветки рядом), связи — требования.</summary>
    private void RebuildGraph()
    {
        _graphRevision = _ws.Revision;
        _graph.ClearConnections();
        foreach (var child in _graph.GetChildren().OfType<GraphNode>()) { _graph.RemoveChild(child); child.QueueFree(); }
        _nodeOf.Clear();
        _buildingOf.Clear();
        int n = 0;
        foreach (var node in _ws.Nodes)
        {
            string name = $"b{n++}";
            _nodeOf[node.Building] = name;
            _buildingOf[name] = node.Building;
            var box = new GraphNode { Name = name, Title = node.Name, PositionOffset = new Vector2((float)node.X, (float)node.Y), TooltipText = node.Building };
            box.AddThemeStyleboxOverride("panel", SettingsTheme.Plate(new Color("#1b1510"), node.Mine ? new Color("#9fd27a") : SettingsTheme.GoldSoft, small: false));
            box.AddThemeStyleboxOverride("titlebar", SettingsTheme.Plate(SettingsTheme.PrimaryBack, SettingsTheme.Gold));
            box.AddThemeStyleboxOverride("titlebar_selected", SettingsTheme.Plate(new Color("#4a3a1f"), SettingsTheme.Light));
            var row = new HBoxContainer();
            if (_texture($"IconBld:{ArtNames.Of(node.Building)}") is { } icon)
                row.AddChild(new TextureRect { Texture = icon, CustomMinimumSize = new Vector2(46, 44), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            row.AddChild(new Label { Text = node.Unit is null ? "—" : $"юнит: {node.Unit}", ThemeTypeVariation = SettingsTheme.HelpLabel });
            box.AddChild(row);
            box.SetSlot(0, true, 0, SettingsTheme.GoldSoft, true, 0, SettingsTheme.Gold);
            _graph.AddChild(box);
        }
        if (!_fitted && _ws.Nodes.Count > 0 && _graph.Size.X > 0)
        {
            // Первый показ — граф целиком: масштаб по ширине самых дальних узлов.
            double right = _ws.Nodes.Max(n => n.X) + 260, bottom = _ws.Nodes.Max(n => n.Y) + 120;
            _graph.Zoom = (float)Math.Clamp(Math.Min(_graph.Size.X / right, _graph.Size.Y / bottom), 0.6, 1.0);   // мельче не читается — дальше прокрутка
            _graph.ScrollOffset = new Vector2(-20, -20);
            _fitted = true;
        }
        foreach (var link in _ws.Links)
            if (_nodeOf.TryGetValue(link.From, out var a) && _nodeOf.TryGetValue(link.To, out var b)) _graph.ConnectNode(a, 0, b, 0);
    }

    // ---- живая страница

    private void PageInput(InputEvent e)
    {
        var preview = _ws.Preview;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } w:
                preview.Wheel((int)w.Position.X, (int)w.Position.Y, w.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when b.Pressed:
                preview.Press((int)b.Position.X, (int)b.Position.Y);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                if (preview.Release((int)b.Position.X, (int)b.Position.Y) is { } drop) _ws.Place(drop.Building, drop.Column, drop.Row);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } r:
                // Правая кнопка по значку — вернуть его к автораскладке.
                preview.Press((int)r.Position.X, (int)r.Position.Y);
                preview.Release((int)r.Position.X, (int)r.Position.Y);
                if (preview.Selected is { } selected) _ws.Unplace(selected);
                break;
            case InputEventMouseMotion m when m.ButtonMask.HasFlag(MouseButtonMask.Middle):
                preview.Pan((int)m.Relative.X, (int)m.Relative.Y);
                break;
            case InputEventMouseMotion m:
                preview.Motion((int)m.Position.X, (int)m.Position.Y, m.ButtonMask.HasFlag(MouseButtonMask.Left));
                break;
            default:
                return;
        }
        _view.Sync(Vector2I.Zero);
        _page.AcceptEvent();
    }

    /// <summary>Перенос значка без мыши (проверка): тем же путём, что мышью, — нажатие, движение, отпускание в клетке.</summary>
    public bool Drag(string building, double column, int row)
    {
        var preview = _ws.Preview;
        if (_ws.Tree?.BranchOf(building) is { } branch) preview.Open(branch.Id);   // ветка значка — на странице
        if (preview.PointOf(building) is not { } from) return false;
        // Ветка значка открыта на странице — иначе его не видно.
        if (preview.PointOfCell(column, row) is not { } to) return false;
        preview.Press(from.X, from.Y);
        preview.Motion((from.X + to.X) / 2, (from.Y + to.Y) / 2, held: true);
        preview.Motion(to.X, to.Y, held: true);
        return preview.Release(to.X, to.Y) is { } drop && _ws.Place(drop.Building, drop.Column, drop.Row);
    }

    public string Line() =>
        $"граф_узлов={_graph.GetChildren().OfType<GraphNode>().Count()} связей={_graph.GetConnectionList().Count} " +
        $"страница: ветка={_ws.Preview.Branch} значков={_ws.Preview.Items.Count} нарисовано={_view.Drawn} ошибок_в_панели={_errors.ItemCount}";
}
