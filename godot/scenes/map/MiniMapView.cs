using System;
using System.Collections.Generic;
using System.IO;
using D2.Data.Gfx;
using D2.Data.Map;
using Godot;

namespace D2.Godot;

/// <summary>Мини-карта в окне <c>IMG_PALMAP</c> экрана <c>DLG_ISO_PAL</c>: растр <see cref="MiniMap"/> сдвинут так,
/// что клетка в центре вида приходится на центр окна, поверх — белая рамка вида постоянного размера
/// (<c>docs/map-screen.md</c> §1). Щелчок центрирует вид на клетке.</summary>
public partial class MiniMapView : SubViewportContainer
{
    /// <summary>Рамка вида при ширине экрана 640/800 (<c>0x5cb1a2</c>).</summary>
    private static readonly Vector2 FrameSize = new(40, 34);

    private readonly Dictionary<string, RgbaImage?> _art = new(StringComparer.OrdinalIgnoreCase);
    private SubViewport _viewport = null!;
    private Sprite2D _raster = null!;
    private Line2D _frame = null!;
    private MiniMap _mini = null!;
    private Vector2 _window;

    /// <summary>Фон окна под растром.</summary>
    public static readonly Color Backdrop = Colors.Black;

    /// <summary>Куда смотрит вид: клетка в центре окна. Ставится сценой.</summary>
    public (int X, int Y) Centre { get; set; }

    /// <summary>Щелчок по мини-карте: клетка, на которую центрировать вид.</summary>
    public event Action<(int X, int Y)>? Picked;

    /// <summary>Точка окна (в его координатах) занята кнопкой панели <c>DLG_ISO_PAL</c>: в разметке кнопки идут после <c>IMG_PALMAP</c>
    /// и лежат над ним (<c>TOG_BANNERS</c> 0,63 и <c>BTN_RES_PAL</c> 0,166 заходят на окно 4,62–154,194). Такой щелчок мини-карта не
    /// берёт — он уходит экрану панели (фильтр мыши <see cref="Control.MouseFilterEnum.Pass"/>). Прежде окно ловило всё (<c>Stop</c>),
    /// и угловые кнопки не нажимались.</summary>
    public Func<Vector2, bool>? Covered { get; set; }

    public void Build(MapLooks looks, GfxAssets gfx, string root, Func<int, int, bool>? hidden, Vector2 window)
    {
        ArgumentNullException.ThrowIfNull(looks);
        _window = window;
        _mini = new MiniMap(looks, name => Art(gfx, root, name), hidden);
        Stretch = false;
        Size = window;
        _viewport ??= Create(window);
        var image = _mini.Render();
        var texture = ImageTexture.CreateFromImage(
            Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels));
        _raster.Texture = texture;
        Place();
    }

    /// <summary>Сдвиг растра и рамки под текущий центр вида.</summary>
    public void Place()
    {
        if (_raster is null) return;
        var (px, py) = _mini.PointOf(Centre.X, Centre.Y);
        _raster.Position = new Vector2((_window.X / 2) - px, (_window.Y / 2) - py);
        var half = FrameSize / 2;
        var centre = _window / 2;
        _frame.Points = [centre - half, new Vector2(centre.X + half.X, centre.Y - half.Y),
                         centre + half, new Vector2(centre.X - half.X, centre.Y + half.Y), centre - half];
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        if (Covered?.Invoke(click.Position) == true) return;   // кнопка панели над окном — щелчок ей
        if (!click.Pressed)
        {
            AcceptEvent();
            return;
        }
        var local = click.Position - _raster.Position;
        var cell = _mini.CellAt((int)local.X, (int)local.Y);
        if (cell.X >= 0 && cell.Y >= 0 && cell.X < _mini.Size && cell.Y < _mini.Size) Picked?.Invoke(cell);
        AcceptEvent();
    }

    private SubViewport Create(Vector2 window)
    {
        var viewport = new SubViewport { Size = new Vector2I((int)window.X, (int)window.Y), TransparentBg = true };
        AddChild(viewport);
        // Вне карты и в пропусках растра — чёрное, как у движка (снимки 28.09: столица, дипломатия, карта), а не фон панели.
        viewport.AddChild(new ColorRect { Color = Backdrop, Size = window, MouseFilter = MouseFilterEnum.Ignore });
        _raster = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest };
        viewport.AddChild(_raster);
        _frame = new Line2D { Width = 1, DefaultColor = Colors.White };
        viewport.AddChild(_frame);
        MouseFilter = MouseFilterEnum.Pass;
        return viewport;
    }

    private RgbaImage? Art(GfxAssets gfx, string root, string name)
    {
        if (_art.TryGetValue(name, out var cached)) return cached;
        RgbaImage? result = null;
        if (gfx.Image("PalMap", name) is { } info && GfxAssets.PathOf(info, root) is { } path && File.Exists(path))
        {
            var image = GfxTextures.Canvas(path);
            image.Convert(Image.Format.Rgba8);
            result = new RgbaImage(image.GetWidth(), image.GetHeight(), image.GetData());
        }
        return _art[name] = result;
    }
}
