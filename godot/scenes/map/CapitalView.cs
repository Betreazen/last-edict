using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Картинка столицы на месте карты (640×600, <c>CCityDisplay</c>/<c>CCity2DImage</c>): слои <see cref="CapitalArt"/> снизу вверх — кадры и
/// анимации архива <c>Capital</c>. Анимации идут непрерывно, кадр в 66 мс (пауза <c>capital.dat</c> в поставке нулевая,
/// <c>0x56b906</c>); кадры грузятся по мере показа и остаются в памяти. Мышь над прямоугольником постройки (границы включительно,
/// <c>0x569dab</c>; сверху — верхний слой) — <see cref="Hovered"/>; подсветки на картинке нет. Нажатие левой в любом месте картинки —
/// <see cref="Clicked"/> (<c>0x4af9dc</c>/<c>0x4afad5</c>: <c>WM_LBUTTONDOWN</c> — окно построек); отпускание кнопки, открывшей столицу с
/// карты, сюда не считается. Новая постройка мигает: 4 шага по 200 мс — скрыть, показать, скрыть,
/// показать (<c>0x4ae978</c>).
/// </summary>
public partial class CapitalView : Node2D
{
    private const double FrameMs = 66, BlinkMs = 200;
    private const int BlinkSteps = 4;
    private static readonly Vector2 Size = new(640, 600);

    /// <summary>Размер картинки столицы (точки разметки).</summary>
    public static Vector2 PictureSize => Size;

    private readonly List<(CapitalLayer Layer, Sprite2D Sprite, IReadOnlyList<string> Frames)> _layers = [];
    private readonly Dictionary<string, Texture2D> _cache = [];
    private HashSet<string>? _buildings;
    private double _frameClock, _blinkClock;
    private int _frame, _blinkStep = BlinkSteps;
    private string? _blinking;

    /// <summary>Сколько слоёв нарисовано и сколько картинок не нашлось — для строки проверки.</summary>
    public (int Drawn, int Missing) Counts { get; private set; }

    /// <summary>Сколько слоёв анимировано.</summary>
    public int Animated => _layers.Count(l => l.Frames.Count > 1);

    /// <summary>Постройка под мышью; нет — null.</summary>
    public string? Hover { get; private set; }

    /// <summary>Принимает ли картинка мышь (окон поверх нет).</summary>
    public Func<bool> Active { get; set; } = () => true;

    public event Action<string?>? Hovered;

    public event Action? Clicked;

    /// <summary>Правая кнопка по постройке — её энциклопедия (<c>0x4afa89</c>).</summary>
    public event Action<string>? RightClicked;

    private Vector2 _area = Size;
    private Sprite2D? _ambient;

    /// <summary>Место картинки на экране карты (место карты <c>IMG_MAIN</c> на холсте формы окна, Э5): картинка 640×600 — по его середине
    /// (позицию ставит хозяин), поля вокруг — растяжкой самой картинки (<see cref="AmbientFill"/>).</summary>
    public Vector2 Area
    {
        get => _area;
        set
        {
            if (value == _area) return;
            _area = value;
            Ambient();
        }
    }

    /// <summary>Поля вокруг картинки: растяжка нижнего слоя (сама картинка столицы) на всё место; место — размер картинки — полей нет.</summary>
    private void Ambient()
    {
        _ambient?.QueueFree();
        _ambient = null;
        if ((_area.X <= Size.X && _area.Y <= Size.Y) || _layers.Count == 0 || _layers[0].Sprite.Texture is not { } texture) return;
        _ambient = AmbientFill.Create(texture, new Rect2(Vector2.Zero, texture.GetSize()), Size / 2f, _area, -1);
        AddChild(_ambient);
        MoveChild(_ambient, 0);
    }

    /// <summary>Слои заново; постройка, которой не было при прошлом показе, мигает.</summary>
    public void Show(IReadOnlyList<CapitalLayer> layers, GfxAssets gfx, string root)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(gfx);
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _layers.Clear();
        int drawn = 0, missing = 0;
        foreach (var layer in layers)
        {
            IReadOnlyList<string> frames = gfx.Image(CapitalArt.Archive, layer.Image) is { } image ? [GfxAssets.PathOf(image, root)]
                : gfx.Anim(CapitalArt.Archive, layer.Image) is { } anim ? GfxAssets.FramePaths(anim, root) : [];
            if (frames.Count == 0 || !File.Exists(frames[0]))
            {
                missing++;
                continue;
            }
            var sprite = new Sprite2D
            {
                Name = $"L{layer.Z}_{layer.Image}", Centered = false, Position = new Vector2(layer.X, layer.Y),
                Texture = Frame(frames[_frame % frames.Count]),
            };
            AddChild(sprite);
            _layers.Add((layer, sprite, frames));
            drawn++;
        }
        Counts = (drawn, missing);
        _ambient = null;   // снята вместе со слоями
        Ambient();
        var buildings = layers.Where(l => l.Building is not null).Select(l => l.Building!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_buildings is not null && buildings.FirstOrDefault(b => !_buildings.Contains(b)) is { } added)
        {
            _blinking = added;
            _blinkStep = 0;
            _blinkClock = 0;
        }
        _buildings = buildings;
    }

    /// <summary>Постройка в точке картинки (верхний слой с прямоугольником, границы включительно); нет — null.</summary>
    public string? BuildingAt(Vector2 point)
    {
        for (int i = _layers.Count - 1; i >= 0; i--)
            if (_layers[i].Layer is { Building: { } building, Hotspot: { } r }
                && point.X >= r.X && point.Y >= r.Y && point.X <= r.X + r.Width && point.Y <= r.Y + r.Height)
                return building;
        return null;
    }

    /// <summary>Щелчок по картинке без мыши (проверка сцены).</summary>
    public void Click() => Clicked?.Invoke();

    /// <summary>Наведение на точку картинки без мыши (проверка сцены).</summary>
    public void HoverAt(Vector2? point)
    {
        _manual = true;   // дальше мышь наведение не меняет — только для проверки сцены, до нового вида
        SetHover(point is { } p ? BuildingAt(p) : null);
    }

    private bool _manual;

    public override void _Process(double delta)
    {
        _frameClock += delta * 1000;
        if (_frameClock >= FrameMs)
        {
            _frameClock %= FrameMs;
            _frame++;
            foreach (var (_, sprite, frames) in _layers)
                if (frames.Count > 1) sprite.Texture = Frame(frames[_frame % frames.Count]);
        }
        if (_blinkStep < BlinkSteps)
        {
            _blinkClock += delta * 1000;
            while (_blinkClock >= BlinkMs && _blinkStep < BlinkSteps)
            {
                _blinkClock -= BlinkMs;
                _blinkStep++;
                foreach (var (layer, sprite, _) in _layers)
                    if (string.Equals(layer.Building, _blinking, StringComparison.OrdinalIgnoreCase)) sprite.Visible = _blinkStep % 2 == 0;
            }
        }
        if (!Active() || _manual) return;
        var local = GetLocalMousePosition();
        SetHover(local.X >= 0 && local.Y >= 0 && local.X < Size.X && local.Y < Size.Y ? BuildingAt(local) : null);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active() || @event is not InputEventMouseButton button) return;
        var local = GetLocalMousePosition();
        if (local.X < 0 || local.Y < 0 || local.X >= Size.X || local.Y >= Size.Y) return;
        if (button is { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            Clicked?.Invoke();
        }
        else if (button is { ButtonIndex: MouseButton.Right, Pressed: true } && BuildingAt(local) is { } building)
        {
            GetViewport().SetInputAsHandled();
            RightClicked?.Invoke(building);
        }
    }

    /// <summary>Правая кнопка по точке картинки без мыши (проверка сцены).</summary>
    public void RightClickAt(Vector2 point)
    {
        if (BuildingAt(point) is { } building) RightClicked?.Invoke(building);
    }

    private void SetHover(string? building)
    {
        if (string.Equals(building, Hover, StringComparison.OrdinalIgnoreCase)) return;
        Hover = building;
        Hovered?.Invoke(building);
    }

    private Texture2D Frame(string path)
    {
        if (!_cache.TryGetValue(path, out var texture))
            _cache[path] = texture = ImageTexture.CreateFromImage(GfxTextures.Canvas(path));
        return texture;
    }
}
