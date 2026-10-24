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
/// только задетые клетки.
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

    private static readonly (string Name, MapBrush Brush)[] Brushes =
        [("Равнина", MapBrush.Plain), ("Лес", MapBrush.Forest), ("Вода", MapBrush.Water), ("Горы", MapBrush.Mountain), ("Земля расы", MapBrush.Land)];

    public StudioMapTab(MapWorkspace ws, Action<string> play)
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
        var undo = new Button { Text = "Отменить мазок" };
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
                     new Label { Text = "Кисть", ThemeTypeVariation = SettingsTheme.GroupLabel }, _brush, _land, _radius, undo, redo, save, playMap, _info,
                 })
            left.AddChild(c);
        AddChild(left);
        _canvas = new MapCanvas(ws) { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        AddChild(_canvas);
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
        _info.Text = _ws.Id is null ? "карта не открыта" : $"{_ws.Id}: {_ws.Size}×{_ws.Size}, клеток изменено {_ws.Changed}" + (_ws.Dirty ? " *" : "");
        _canvas.Sync();
    }

    public string Line() => $"{_ws.Line()} холст={_canvas.Line()}";

    /// <summary>Карта клетка-в-точку: картинка RGBA, вид с зумом, кисть мышью.</summary>
    private sealed partial class MapCanvas(MapWorkspace ws) : Control
    {
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
            _view.Reset(Size.X, Size.Y, ws.Size, ws.Size);
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
            // Карта открыта до раскладки (размер был нулём) — вписать её целиком, когда место известно.
            if (_view.Width <= 1) _view.Reset(Size.X, Size.Y, ws.Size, ws.Size);
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
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } w:
                    _view.ZoomAt(w.Position.X, w.Position.Y, w.ButtonIndex == MouseButton.WheelUp ? 1.25 : 0.8);
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
                    break;
                case InputEventMouseMotion m when m.ButtonMask.HasFlag(MouseButtonMask.Left) && _last is { } from:
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
