using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Authoring.Maps;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Карта» (<see cref="MapWorkspace"/>, Р6, Р16): слева — карты мода, новая карта (сторона любая, игроки), кисть (грунт или
/// земля расы, радиус), отмена, запись и «Играть»; справа — карта клетка-в-точку с зумом к курсору и сдвигом средней кнопкой (тот же
/// <see cref="ZoomView"/>, что у окна построек; на 1024² видна целиком), кисть — левой кнопкой, мазок — одна команда отмены. Перекрашиваются
/// только задетые клетки. Инструмент (Студия 2): кисть, объект палитры (ставится щелчком в клетку — левый верхний угол) или выбор (щелчок —
/// объект, тащение — перенос, Delete — убрать); объекты — метками на карте, справа — что ставить и свойства выбранного
/// (<see cref="StudioMapObjectsPanel"/>). Отмена — общая для мазков и объектов.
/// </summary>
public partial class StudioMapTab : HSplitContainer, IStudioTab
{
    private readonly MapWorkspace _ws;
    private readonly Action<string> _play;
    private readonly OptionButton _maps = new() { CustomMinimumSize = new Vector2(220, 0) };
    private readonly LineEdit _id = new() { Text = "new-map", PlaceholderText = "id карты (латиница)" };
    private readonly SpinBox _size = new() { MinValue = 24, MaxValue = MapWorkspace.MaxNewSide, Value = 256, Prefix = "сторона" };
    private readonly SpinBox _seats = new() { MinValue = 1, MaxValue = 16, Value = 4, Prefix = "игроков" };
    private readonly OptionButton _brush = new();
    private readonly OptionButton _land = new();
    private readonly SpinBox _radius = new() { MinValue = 0, MaxValue = 3, Value = 1, Prefix = "радиус" };
    private readonly Label _info = new() { ThemeTypeVariation = SettingsTheme.HelpLabel, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly MapCanvas _canvas;
    private readonly OptionButton _tool = new();
    private readonly StudioMapObjectsPanel _panel;
    private readonly StudioMapScenarioPanel _scenario;

    /// <summary>Инструмент холста: null — кисть, вид объекта — поставить, <see cref="SelectTool"/> — выбор и перенос.</summary>
    private MapObjectKind? _placing;
    private bool _selecting;

    private static readonly (string Name, MapBrush Brush)[] Brushes =
        [("Равнина", MapBrush.Plain), ("Лес", MapBrush.Forest), ("Вода", MapBrush.Water), ("Горы", MapBrush.Mountain), ("Земля расы", MapBrush.Land)];

    public StudioMapTab(MapWorkspace ws, Action<string> play, Func<string, Texture2D?> texture)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _play = play ?? throw new ArgumentNullException(nameof(play));
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        left.AddThemeConstantOverride("separation", 6);
        var open = new Button { Text = "Открыть" };
        open.Pressed += () => { if (_maps.Selected >= 0) Open(_maps.GetItemText(_maps.Selected)); };
        var create = new Button { Text = "Новая карта", ThemeTypeVariation = SettingsTheme.Primary };
        create.Pressed += () => New(_id.Text.Trim(), (int)_size.Value, (int)_seats.Value);
        foreach (var (name, _) in Brushes) _brush.AddItem(name);
        _brush.Select(1);
        _brush.ItemSelected += i => _ws.Brush = Brushes[(int)i].Brush;
        foreach (var kind in Enum.GetValues<TerrainKind>()) _land.AddItem(kind.ToString());
        _land.ItemSelected += i => _ws.Land = Enum.GetValues<TerrainKind>()[(int)i];
        _radius.ValueChanged += v => _ws.Radius = (int)v;
        _tool.AddItem("Кисть");
        _tool.AddItem("Выбор и перенос");
        foreach (var (title, _) in StudioMapObjectsPanel.Kinds) _tool.AddItem($"Объект: {title}");
        _tool.ItemSelected += i => Tool((int)i);
        var undo = new Button { Text = "Отменить" };
        undo.Pressed += () => { _ws.Undo(); _canvas!.Sync(); };
        var redo = new Button { Text = "Повторить" };
        redo.Pressed += () => { _ws.Redo(); _canvas!.Sync(); };
        var save = new Button { Text = "Записать в мод" };
        save.Pressed += () => { _ws.Save(); Sync(); };   // отказ — строкой статуса Студии (Session.Refusal)
        var playMap = new Button { Text = "Играть на карте" };
        playMap.Pressed += () => { if (_ws.Id is { } id) { _ws.Save(); _play(id); } };
        foreach (var c in new Control[]
                 {
                     new Label { Text = "Карты мода", ThemeTypeVariation = SettingsTheme.GroupLabel }, _maps, open,
                     new Label { Text = "Новая", ThemeTypeVariation = SettingsTheme.GroupLabel }, _id, _size, _seats, create,
                     new Label { Text = "Инструмент", ThemeTypeVariation = SettingsTheme.GroupLabel }, _tool,
                     new Label { Text = "Кисть", ThemeTypeVariation = SettingsTheme.GroupLabel }, _brush, _land, _radius, undo, redo, save, playMap, _info,
                 })
            left.AddChild(c);
        AddChild(left);
        _canvas = new MapCanvas(ws, this) { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        _panel = new StudioMapObjectsPanel(ws, texture) { Name = "Объект" };
        // Справа — вкладки «Объект» (что ставить, свойства выбранного), «Игроки», «События».
        _scenario = new StudioMapScenarioPanel(ws) { CustomMinimumSize = new Vector2(360, 0) };
        _scenario.AddChild(_panel);
        _scenario.MoveChild(_panel, 0);
        _scenario.CurrentTab = 0;
        var right = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(_canvas);
        right.AddChild(_scenario);
        AddChild(right);
    }

    /// <summary>Инструмент по пункту списка: 0 — кисть, 1 — выбор, дальше — объекты палитры.</summary>
    public void Tool(int index)
    {
        _tool.Selected = index;
        _selecting = index == 1;
        _placing = index >= 2 ? StudioMapObjectsPanel.Kinds[index - 2].Kind : null;
    }

    /// <summary>Щелчок по клетке инструментом (не кистью): поставить объект или выбрать. Возвращает «сделано».</summary>
    private bool Click(int x, int y)
    {
        if (_placing is { } kind)
        {
            bool done = _ws.Place(kind, x, y, _panel.Placement());
            Sync();
            return done;
        }
        if (_selecting)
        {
            _ws.Select(_ws.Reader?.At(x, y)?.Id);
            Sync();
            return true;
        }
        return false;
    }

    /// <summary>Перенос выбранного объекта в клетку (тащение инструментом «Выбор»).</summary>
    private void Drop(int x, int y)
    {
        if (_ws.Selected is { } id && _ws.Objects.FirstOrDefault(o => o.Id == id) is { } o && (o.X, o.Y) != (x, y)) _ws.Move(id, x, y);
        Sync();
    }

    /// <summary>Шаги проверки без окна: поставить объект (инструмент, размещение, щелчок), выбрать, перенести, убрать.</summary>
    public bool PlaceAt(string kind, int x, int y, IEnumerable<string>? units, IEnumerable<string>? items, string? owner)
    {
        Tool(2 + Array.FindIndex(StudioMapObjectsPanel.Kinds, k => k.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase)));
        _panel.Set(units, items, owner);
        return Click(x, y);
    }

    public bool SelectAt(int x, int y)
    {
        Tool(1);
        Click(x, y);
        return _ws.Selected is not null;
    }

    public bool MoveSelected(int x, int y)
    {
        Drop(x, y);
        return true;
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Delete } && _ws.Selected is { } id && IsVisibleInTree())
        {
            _ws.Remove(id);
            Sync();
            GetViewport().SetInputAsHandled();
        }
    }

    public void New(string id, int size, int seats)
    {
        var races = (_ws.Session.Report.Content ?? _ws.Session.BaseContent).Races.Where(r => r.Playable == true && r.Neutral != true).Select(r => r.Id).ToList();
        if (races.Count == 0) return;
        if (!_ws.New(id, size, [.. Enumerable.Range(0, seats).Select(i => new MapSeat(races[i % races.Count]))])) return;
        _canvas.Reset();
        Sync();
    }

    public void Open(string id)
    {
        if (!_ws.Open(id)) return;
        _canvas.Reset();
        Sync();
    }

    /// <summary>Мазок без мыши (проверка): кисть от клетки к клетке, одна команда.</summary>
    public void Stroke(int x0, int y0, int x1, int y1)
    {
        _ws.StrokeBegin();
        foreach (var (x, y) in MapCanvas.Line(x0, y0, x1, y1)) _ws.Paint(x, y);
        _ws.StrokeEnd();
        _canvas.Sync();
    }

    public void Sync()
    {
        var maps = _ws.Maps;
        if (_maps.ItemCount != maps.Count)
        {
            _maps.Clear();
            foreach (string m in maps) _maps.AddItem(m);
        }
        _info.Text = _ws.Id is null ? "карта не открыта" : $"{_ws.Id}: {_ws.Size}×{_ws.Size}, клеток изменено {_ws.Changed}, объектов {_ws.Objects.Count}" + (_ws.Dirty ? " *" : "");
        _canvas.Sync();
        _canvas.QueueRedraw();   // метки объектов
        _panel.Sync();
        _scenario.Sync();
    }

    public string Line() => $"{_ws.Line()} холст={_canvas.Line()} {_scenario.Line()}";

    /// <summary>Карта клетка-в-точку: картинка RGBA, вид с зумом, кисть мышью.</summary>
    private sealed partial class MapCanvas(MapWorkspace ws, StudioMapTab tab) : Control
    {
        /// <summary>Цвет метки объекта по виду.</summary>
        private static Color Mark(MapObjectKind kind) => kind switch
        {
            MapObjectKind.Capital => new Color("#ffd24a"), MapObjectKind.City => new Color("#e8b45a"), MapObjectKind.Ruin => new Color("#b07cff"),
            MapObjectKind.Merchant or MapObjectKind.Mage or MapObjectKind.Mercenaries or MapObjectKind.Trainer => new Color("#4ad0ff"),
            MapObjectKind.Crystal => new Color("#7cffb0"), MapObjectKind.Bag => new Color("#ffffff"), MapObjectKind.Landmark => new Color("#9c8f7a"),
            _ => new Color("#ff5a4a"),
        };

        private (int X, int Y)? _drag;

        /// <summary>Карта целиком в место холста: и мельче, и крупнее (маленькая карта — крупнее 1:1; <see cref="ZoomView.Reset"/> крупнее
        /// 1:1 не даёт — так нужно окну построек).</summary>
        private void Fit()
        {
            _view.Reset(Size.X, Size.Y, ws.Size, ws.Size);
            double fit = Math.Min(Size.X, Size.Y) / Math.Max(1.0, ws.Size);
            if (fit > _view.Zoom) _view.ZoomAt(Size.X / 2, Size.Y / 2, fit / _view.Zoom);
        }

        /// <summary>Вид вписан и не тронут (зум, сдвиг) — при смене места вписывается снова.</summary>
        private bool _fitted;

        private Image? _image;
        private ImageTexture? _texture;
        private readonly ZoomView _view = new() { Least = 0.02, Most = 24 };
        private (int X, int Y)? _last;
        private int _painted;

        public void Reset()
        {
            if (ws.Size == 0) return;
            _image = Image.CreateEmpty(ws.Size, ws.Size, false, Image.Format.Rgba8);
            var bytes = new byte[ws.Size * ws.Size * 4];
            for (int i = 0; i < ws.Tiles.Length; i++) Put(bytes, i, MapWorkspace.Rgba(ws.Tiles[i]));
            _image.SetData(ws.Size, ws.Size, false, Image.Format.Rgba8, bytes);
            _texture = ImageTexture.CreateFromImage(_image);
            ws.TakeDirty();
            Fit();
            _fitted = true;
            QueueRedraw();
        }

        private static void Put(byte[] bytes, int i, uint rgba)
        {
            bytes[(i * 4) + 0] = (byte)(rgba >> 24);
            bytes[(i * 4) + 1] = (byte)(rgba >> 16);
            bytes[(i * 4) + 2] = (byte)(rgba >> 8);
            bytes[(i * 4) + 3] = (byte)rgba;
        }

        /// <summary>Перекрасить задетые клетки.</summary>
        public void Sync()
        {
            if (_image is null || _texture is null) return;
            var dirty = ws.TakeDirty();
            if (dirty.Count == 0) return;
            foreach (int i in dirty)
            {
                uint c = MapWorkspace.Rgba(ws.Tiles[i]);
                _image.SetPixel(i % ws.Size, i / ws.Size, Color.Color8((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8)));
            }
            _painted += dirty.Count;
            _upload = true;   // текстура — раз за кадр (_Process), а не на каждое движение мыши: на 1024² это 4 МБ
            QueueRedraw();
        }

        private bool _upload;

        public override void _Process(double delta)
        {
            if (!_upload || _image is null || _texture is null) return;
            _texture.Update(_image);
            _upload = false;
        }

        public override void _Notification(int what)
        {
            if (what != NotificationResized || ws.Size == 0) return;
            // Карта открыта до раскладки (размер был нулём) или её ещё не двигали — вписать целиком в новое место.
            if (_view.Width <= 1 || _fitted) Fit();
            else _view.Resize(Size.X, Size.Y);
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("#0d0a07"));
            if (_texture is null) return;
            TextureFilter = TextureFilterEnum.Nearest;
            DrawSetTransform(new Vector2((float)_view.X, (float)_view.Y), 0, new Vector2((float)_view.Zoom, (float)_view.Zoom));
            DrawTexture(_texture, Vector2.Zero);
            // Метки объектов: рамка по клеткам объекта, отряд — кружок; выбранный — толще и светлее.
            float line = (float)Math.Max(0.15, 1.5 / _view.Zoom);
            foreach (var o in ws.Objects)
            {
                bool picked = o.Id == ws.Selected;
                var color = picked ? Colors.White : Mark(o.Kind);
                if (o.Kind == MapObjectKind.Stack) DrawCircle(new Vector2(o.X + 0.5f, o.Y + 0.5f), 0.45f, color);
                else DrawRect(new Rect2(o.X, o.Y, o.W, o.H), color, false, picked ? line * 2 : line);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } w:
                    _view.ZoomAt(w.Position.X, w.Position.Y, w.ButtonIndex == MouseButton.WheelUp ? 1.25 : 0.8);
                    _fitted = false;
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when b.Pressed && (tab._placing is not null || tab._selecting):
                    var at = Cell(b.Position);
                    tab.Click(at.X, at.Y);
                    _drag = tab._selecting && ws.Selected is not null ? at : null;
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when !b.Pressed && _drag is not null:
                    var drop = Cell(b.Position);
                    if (drop != _drag && ws.Objects.FirstOrDefault(o => o.Id == ws.Selected) is { } moving)
                        tab.Drop(moving.X + drop.X - _drag.Value.X, moving.Y + drop.Y - _drag.Value.Y);
                    _drag = null;
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when b.Pressed:
                    ws.StrokeBegin();
                    _last = Cell(b.Position);
                    ws.Paint(_last.Value.X, _last.Value.Y);
                    Sync();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left }:
                    ws.StrokeEnd();
                    _last = null;
                    break;
                case InputEventMouseMotion m when m.ButtonMask.HasFlag(MouseButtonMask.Middle):
                    _view.Pan(m.Relative.X, m.Relative.Y);
                    _fitted = false;
                    break;
                case InputEventMouseMotion m when m.ButtonMask.HasFlag(MouseButtonMask.Left) && _last is { } from && tab._placing is null && !tab._selecting:
                    var to = Cell(m.Position);
                    foreach (var (x, y) in Line(from.X, from.Y, to.X, to.Y)) ws.Paint(x, y);
                    _last = to;
                    Sync();
                    break;
                default:
                    return;
            }
            QueueRedraw();
            AcceptEvent();
        }

        private (int X, int Y) Cell(Vector2 at)
        {
            var (cx, cy) = _view.ToContent(at.X, at.Y);
            return ((int)Math.Floor(cx), (int)Math.Floor(cy));
        }

        /// <summary>Клетки отрезка (Брезенхем): мазок не рвётся при быстрой мыши.</summary>
        public static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                yield return (x0, y0);
                if (x0 == x1 && y0 == y1) yield break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public string Line() => System.FormattableString.Invariant($"зум={_view.Zoom:0.###} перекрашено={_painted} текстура={(_texture is null ? 0 : ws.Size)}");
    }
}
