using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Сравнение апскейла одного юнита (Р19, проба листов 23.10): слева — кадры оригинала, справа — те же анимации из апскейла, синхронно.
/// Кадры — из выгрузки <c>tools/hd/hd_layer.py</c> (плоские имена, <c>manifest.json</c>: место кадра в холсте и смешение), апскейл — из папки
/// нарезанных листов (<c>tools/hd/hd_atlas.py unpack</c>). Анимация собирается слоями, как в игре: тень (смешение наполовину), фигура,
/// второй слой атаки (сложение). Разделы: бой (атакующий и защитник: стоит, атака, удар, поза), карта (идёт и стоит в 8 направлениях),
/// портреты и картинка события.
/// <para>Запуск: <c>godot --path godot res://scenes/dev/HdCompare.tscn -- --hd &lt;выгрузка&gt; --up &lt;кадры апскейла&gt; [--unit G000UU0019]</c>;
/// <c>--hd</c> — папка с <c>original</c> (и <c>done</c>) и <c>manifest.json</c>. Клавиши: Tab — раздел, колесо — масштаб, перетаскивание —
/// сдвиг, пробел — пауза, ←/→ — кадр, +/− — скорость, F — фильтр апскейла (сглаживание / ближайший). <c>--shot &lt;png&gt;</c>
/// [<c>--section N</c>] — снимок окна через полсекунды и выход (проверка).</para>
/// </summary>
public partial class HdCompare : Node2D
{
    /// <summary>Слой: имя анимации, смешение, угол каждого кадра в холсте, плоские имена и размеры кадров.</summary>
    private sealed record Layer(string Name, int Opacity, int[] At, string[] Flats, int[] Sizes);

    private sealed record Cell(string Title, List<Layer> Layers, Rect2 Bounds, bool Still);

    private readonly List<(Cell Cell, Layer Layer, Sprite2D Left, Sprite2D Right)> _bound = [];
    private readonly Dictionary<string, Texture2D?> _original = new(StringComparer.Ordinal), _upscaled = new(StringComparer.Ordinal);
    private Dictionary<string, List<Cell>> _sections = new(StringComparer.Ordinal);
    private string[] _order = [];
    private int _section;
    private Node2D _leftRoot = null!, _rightRoot = null!;
    private Label _status = null!;
    private string _hd = "", _up = "";
    private int _frame;
    private double _clock, _fps = 12;
    private bool _paused, _smooth = true, _dragging;
    private float _zoom = 1f;
    private Vector2 _pan = new(20, 60);

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _hd = Arg(args, "--hd") ?? @"C:\Claude code projects\le-hd";
        _up = Arg(args, "--up") ?? Path.Combine(_hd, "atlas-test", "pegasus-frames");
        string unit = Arg(args, "--unit") ?? "G000UU0019";
        RenderingServer.SetDefaultClearColor(new Color(0.16f, 0.16f, 0.17f));
        _sections = Load(Path.Combine(_hd, "original", "manifest.json"), unit);
        _order = [.. _sections.Keys];
        _leftRoot = new Node2D { Name = "Original" };
        _rightRoot = new Node2D { Name = "Upscaled" };
        var leftClip = Clip(0);
        var rightClip = Clip(1);
        leftClip.AddChild(_leftRoot);
        rightClip.AddChild(_rightRoot);
        var layer = new CanvasLayer();
        AddChild(layer);
        _status = new Label { Position = new Vector2(12, 8) };
        _status.AddThemeFontSizeOverride("font_size", 18);
        layer.AddChild(_status);
        layer.AddChild(new ColorRect { Color = new Color(0.9f, 0.75f, 0.3f), Size = new Vector2(2, 4000), Position = new Vector2(Half() - 1, 0) });
        Show(int.TryParse(Arg(args, "--section"), out int first) ? Math.Clamp(first, 0, _order.Length - 1) : 0);
        if (Arg(args, "--shot") is { } shot) Shot(shot);
        GetViewport().SizeChanged += () => { foreach (var c in GetChildren().OfType<Control>()) Fit(c); };
    }

    private float Half() => GetViewportRect().Size.X / 2;

    private async void Shot(string path)
    {
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot={path}");
        GetTree().Quit();
    }

    /// <summary>Половина окна с обрезкой по краю: содержимое не залезает на соседнюю.</summary>
    private Control Clip(int side)
    {
        var clip = new Control { ClipContents = true, Name = side == 0 ? "LeftHalf" : "RightHalf", MouseFilter = Control.MouseFilterEnum.Ignore };
        clip.SetMeta("side", side);
        AddChild(clip);
        Fit(clip);
        return clip;
    }

    private void Fit(Control clip)
    {
        int side = (int)clip.GetMeta("side");
        var size = GetViewportRect().Size;
        clip.Position = new Vector2(side * size.X / 2, 0);
        clip.Size = new Vector2(size.X / 2, size.Y);
    }

    private static string? Arg(string[] args, string key)
    {
        int at = Array.IndexOf(args, key);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    // --- данные -------------------------------------------------------------------------------------------------

    /// <summary>Анимации и картинки юнита из описи выгрузки — по разделам: бой, карта, портреты.</summary>
    private static Dictionary<string, List<Cell>> Load(string manifest, string unit)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
        foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            string flat = e.GetProperty("flat").GetString()!;
            if (!flat.Contains(unit, StringComparison.Ordinal)) continue;
            string name = e.GetProperty("name").GetString()!;
            int frame = e.TryGetProperty("frame", out var f) ? f.GetInt32() : 0;
            int count = e.TryGetProperty("frames", out var fr) ? fr.GetArrayLength() : 1;
            if (!layers.TryGetValue(name, out var layer))
            {
                int[] at = e.TryGetProperty("at", out var a) ? [.. a.EnumerateArray().Select(x => x.GetInt32())] : new int[count * 2];
                layer = new Layer(name, e.TryGetProperty("opacity", out var o) ? o.GetInt32() : GfxAssets.Opaque, at, new string[count],
                                  new int[count * 2]);
                layers[name] = layer;
            }
            layer.Flats[frame] = flat;
            layer.Sizes[2 * frame] = e.GetProperty("fileW").GetInt32();
            layer.Sizes[(2 * frame) + 1] = e.GetProperty("fileH").GetInt32();
        }
        var sections = new Dictionary<string, List<Cell>>(StringComparer.Ordinal) { ["Бой"] = [], ["Карта"] = [], ["Портреты"] = [] };
        // Бой: состояние × сторона; слои — тень (S), фигура (A1), второй слой (A2).
        foreach (string state in new[] { "IDLE", "HMOV", "HHIT", "STIL" })
            foreach (char side in "AD")
            {
                var cell = layers.Values.Where(l => l.Name.StartsWith(unit + state, StringComparison.Ordinal) && l.Name.EndsWith($"{side}00", StringComparison.Ordinal))
                    .OrderBy(l => l.Name.Contains("S1", StringComparison.Ordinal) ? 0 : l.Name.Contains("A1", StringComparison.Ordinal) ? 1 : 2).ToList();
                if (cell.Count > 0) sections["Бой"].Add(Make($"{Title(state)} · {(side == 'A' ? "атакует" : "защищается")}", cell));
            }
        // Карта: идёт (MOVE + тень SMOV) и стоит (STOP + SSTO) по 8 направлениям.
        foreach (var (body, shadow, title) in new[] { ("MOVE", "SMOV", "идёт"), ("STOP", "SSTO", "стоит") })
            for (int d = 0; d < 8; d++)
            {
                var cell = new[] { $"{unit}{shadow}{d}", $"{unit}{body}{d}" }.Where(layers.ContainsKey).Select(n => layers[n]).ToList();
                if (cell.Count > 0) sections["Карта"].Add(Make($"{title} · {d}", cell));
            }
        foreach (var l in layers.Values.Where(l => l.Flats.Length == 1 && !l.Name.Contains("STIL", StringComparison.Ordinal)))
            sections["Портреты"].Add(Make(l.Name.Replace(unit, "", StringComparison.Ordinal) is { Length: > 0 } s ? s : "событие", [l]));
        return sections.Where(s => s.Value.Count > 0).ToDictionary(s => s.Key, s => s.Value);
    }

    private static string Title(string state) => state switch
    {
        "IDLE" => "стоит", "HMOV" => "атака", "HHIT" => "удар по нему", _ => "поза",
    };

    /// <summary>Клетка: слои и рамка всех их кадров в холсте.</summary>
    private static Cell Make(string title, List<Layer> layers)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var l in layers)
            for (int i = 0; i < l.Flats.Length; i++)
            {
                if (l.Sizes[2 * i] <= 1 && l.Sizes[(2 * i) + 1] <= 1) continue;   // пустой кадр (1×1) — не в рамку
                x0 = Math.Min(x0, l.At[2 * i]);
                y0 = Math.Min(y0, l.At[(2 * i) + 1]);
                x1 = Math.Max(x1, l.At[2 * i] + l.Sizes[2 * i]);
                y1 = Math.Max(y1, l.At[(2 * i) + 1] + l.Sizes[(2 * i) + 1]);
            }
        // Пустые кадры (второй слой атаки 1×1) рамку не раздвигают — она по всем непустым кадрам всех слоёв.
        if (x0 > x1) (x0, y0, x1, y1) = (0, 0, 60, 40);
        return new Cell(title, layers, new Rect2(x0, y0, Math.Max(60, x1 - x0), Math.Max(40, y1 - y0)), layers.All(l => l.Flats.Length == 1));
    }

    private Texture2D? Original(string flat)
    {
        if (_original.TryGetValue(flat, out var t)) return t;
        string? path = new[] { "original", "done" }.Select(d => Path.Combine(_hd, d, flat)).FirstOrDefault(File.Exists);
        return _original[flat] = path is null ? null : ImageTexture.CreateFromImage(Image.LoadFromFile(path));
    }

    /// <summary>Кадр апскейла — логического размера (размер оригинала), точки — полные.</summary>
    private Texture2D? Upscaled(string flat)
    {
        if (_upscaled.TryGetValue(flat, out var t)) return t;
        string path = Path.Combine(_up, flat);
        if (!File.Exists(path) || Original(flat) is not { } original) return _upscaled[flat] = null;
        var image = Image.LoadFromFile(path);
        image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        texture.SetSizeOverride((Vector2I)original.GetSize());
        return _upscaled[flat] = texture;
    }

    // --- показ --------------------------------------------------------------------------------------------------

    private void Show(int section)
    {
        _section = section;
        foreach (var root in new[] { _leftRoot, _rightRoot })
            foreach (var child in root.GetChildren()) child.QueueFree();
        _bound.Clear();
        var cells = _sections[_order[section]];
        float x = 0, y = 0, row = 0, width = (Half() / _zoom) - 40;
        foreach (var cell in cells)
        {
            var size = cell.Bounds.Size;
            if (x > 0 && x + size.X > width)
            {
                x = 0;
                y += row + 30;
                row = 0;
            }
            var holders = new[] { _leftRoot, _rightRoot }.Select(root =>
            {
                var holder = new Node2D { Position = new Vector2(x, y + 24) };
                root.AddChild(holder);
                holder.AddChild(new Label { Text = cell.Title, Position = new Vector2(0, -24), Modulate = new Color(0.85f, 0.8f, 0.7f) });
                return holder;
            }).ToArray();
            foreach (var layer in cell.Layers)
            {
                var pair = holders.Select(holder =>
                {
                    var sprite = new Sprite2D { Centered = false };
                    GfxBlending.Apply(sprite, GfxAssets.BlendOf(layer.Opacity));
                    holder.AddChild(sprite);
                    return sprite;
                }).ToArray();
                _bound.Add((cell, layer, pair[0], pair[1]));
            }
            x += size.X + 20;
            row = Math.Max(row, size.Y + 24);
        }
        Apply();
    }

    private void Apply()
    {
        foreach (var root in new[] { _leftRoot, _rightRoot })
        {
            root.Scale = new Vector2(_zoom, _zoom);
            root.Position = _pan;
        }
        foreach (var (cell, layer, left, right) in _bound)
        {
            int i = layer.Flats.Length == 1 ? 0 : _frame % layer.Flats.Length;
            string flat = layer.Flats[i];
            var at = new Vector2(layer.At[2 * i], layer.At[(2 * i) + 1]) - cell.Bounds.Position;
            left.Texture = flat is null ? null : Original(flat);
            left.Position = at;
            left.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
            right.Texture = flat is null ? null : Upscaled(flat);
            right.Position = at;
            right.TextureFilter = _smooth ? CanvasItem.TextureFilterEnum.LinearWithMipmaps : CanvasItem.TextureFilterEnum.Nearest;
        }
        int missing = _bound.Count(b => b.Right.Texture is null && b.Left.Texture is not null);
        _status.Text = $"{_order[_section]} (Tab)   слева — оригинал, справа — апскейл{(missing > 0 ? $" (нет кадров апскейла: {missing})" : "")}   " +
                       $"кадр {_frame}   {_fps:0} к/с{(_paused ? " — пауза" : "")}   масштаб {_zoom:0.0}×   фильтр апскейла: {(_smooth ? "сглаживание" : "ближайший")} (F)";
    }

    public override void _Process(double delta)
    {
        if (_paused) return;
        _clock += delta;
        if (_clock < 1 / _fps) return;
        _clock = 0;
        _frame++;
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Keycode: Key.Tab }:
                Show((_section + 1) % _order.Length);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Space }:
                _paused = !_paused;
                Apply();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Right }:
                _frame++;
                Apply();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Left }:
                _frame = Math.Max(0, _frame - 1);
                Apply();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Equal or Key.KpAdd }:
                _fps = Math.Min(60, _fps + 2);
                Apply();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Minus or Key.KpSubtract }:
                _fps = Math.Max(2, _fps - 2);
                Apply();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.F }:
                _smooth = !_smooth;
                Apply();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _zoom = Math.Min(8, _zoom * 1.15f);
                Show(_section);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _zoom = Math.Max(0.5f, _zoom / 1.15f);
                Show(_section);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _dragging = b.Pressed;
                break;
            case InputEventMouseMotion m when _dragging:
                _pan += m.Relative;
                Apply();
                break;
        }
    }
}
