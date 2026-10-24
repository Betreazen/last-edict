using System;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Анимированная вода (<c>docs/ground-render.md</c> §4): под землёй — фон <c>WATERBKGND1</c> (IsoAnim, 128×128, 16 кадров)
/// плиткой на всю карту с периодом 128 по миру, кадр раз в 66 мс (медленный аниматор, <c>docs/map-render.md</c>); точки воды
/// в кусках земли прозрачны (<see cref="GroundComposer.AnimatedWater"/>). Нет картинки — вода неподвижная (<c>WA_00</c>).</summary>
public partial class MapView
{
    private const string WaterAnim = "WATERBKGND1";
    private const int WaterTile = 128;
    private const double WaterFrameSeconds = LastEdict.Data.Map.AnimClock.SlowMs / 1000.0;

    private Sprite2D? _water;
    private Texture2D[] _waterFrames = [];
    private double _waterClock;
    private int _waterFrame;

    /// <summary>Вода анимирована (фон нашёлся).</summary>
    public bool WaterAnimated => _waterFrames.Length > 0;

    private bool _animatedIso = true;

    /// <summary>Анимация карты (настройка <c>AnimatedIso</c>, <c>TOG_ANIMISO</c>): выключена — объекты картинками IsoStill, вода
    /// неподвижная в кусках земли (<c>0x5ac8bc</c>, <c>0x5ab292</c>). Смена — сцена и земля собираются заново.</summary>
    public bool AnimatedIso
    {
        get => _animatedIso;
        set
        {
            if (_animatedIso == value) return;
            _animatedIso = value;
            if (_water is not null) _water.Visible = value;
            if (_chunks is null) return;   // вид ещё не собран — соберётся с новым значением
            _chunks = Chunks(Map);   // новый сборщик — вода в кусках земли другая, прежние куски не годятся
            RefreshGround();
            Reload();
            GameLog.Verbose($"animiso {(value ? 1 : 0)} IsoStill={Scene.Sprites.Count(s => s.Archive == "IsoStill")} IsoAnim={Scene.Sprites.Count(s => s.Archive == "IsoAnim")}");
        }
    }

    /// <summary>Фон воды под землёй; зовётся до слоя земли.</summary>
    private void BuildWater()
    {
        if (GameData.Content.Gfx.Anim("IsoAnim", WaterAnim) is not { } anim) return;
        // Кадры — в HD слоя (текстура логического размера, точки полные): плитка повторяется своим размером.
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _waterFrames = [.. GfxAssets.FramePaths(anim, GameData.GfxRoot).Where(File.Exists).Select(WaterFrame)];
        double waterMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (_waterFrames.Length == 0) return;
        float left = MathF.Floor(_bounds.Position.X / WaterTile) * WaterTile, top = MathF.Floor(_bounds.Position.Y / WaterTile) * WaterTile;
        float width = MathF.Ceiling((_bounds.End.X - left) / WaterTile) * WaterTile, height = MathF.Ceiling((_bounds.End.Y - top) / WaterTile) * WaterTile;
        _water = new Sprite2D
        {
            Name = "Water", Texture = _waterFrames[0], Centered = false, Position = new Vector2(left, top),
            RegionEnabled = true, RegionRect = new Rect2(0, 0, width, height),
            TextureRepeat = TextureRepeatEnum.Enabled, TextureFilter = ImageScales.Filter(_waterFrames[0], TextureFilterEnum.Nearest),
        };
        AddChild(_water);
        GameLog.Verbose($"water кадров={_waterFrames.Length} плитка={WaterTile} hd={(ImageScales.Filter(_waterFrames[0], TextureFilterEnum.Nearest) == TextureFilterEnum.LinearWithMipmaps ? 1 : 0)} мс={waterMs:F0}");
    }

    /// <summary>Кадр воды. У HD-кадра апскейл тёмнит края плитки — повторённая, она ложится сеткой (как туман и дороги, которые в слой не
    /// идут). Края чинятся здесь: плитка смешивается со своей копией, сдвинутой на полплитки (<see cref="Seamless"/>), — у краёв видна
    /// середина копии, а она по кругу непрерывна, поэтому стыков нет. Кадр ×1 — как есть.</summary>
    private static Texture2D WaterFrame(string path)
    {
        var image = GfxTextures.CanvasHd(path);
        if (ImageScales.Of(image) <= 1) return ImageTexture.CreateFromImage(image);
        Seamless(ImageScales.Detach(image));   // точки меняются — текстура из них, а не из DDS файла
        return ImageScales.Texture(image);
    }

    /// <summary>Доля стороны плитки у каждого края, где она переходит в сдвинутую копию.</summary>
    private const float SeamBand = 0.2f;

    /// <summary>Плитка без шва при повторе — два прохода: по горизонтали (у левого и правого края — копия, сдвинутая на полплитки по x),
    /// затем по вертикали (у верха и низа — копия, сдвинутая по y). Шов копии каждого прохода лежит посередине плитки, где видна сама плитка;
    /// смесь двух бесшовных по x картинок бесшовна по x — второй проход первому не мешает.</summary>
    private static void Seamless(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        int w = image.GetWidth(), h = image.GetHeight();
        byte[] data = image.GetData();
        data = Blend(data, w, h, horizontal: true);
        data = Blend(data, w, h, horizontal: false);
        image.SetData(w, h, false, Image.Format.Rgba8, data);
    }

    /// <summary>Один проход: в полосе <see cref="SeamBand"/> у двух краёв оси — плавно копия, сдвинутая на половину по этой оси. Вес —
    /// заранее на каждую точку оси (0…256), смесь — целыми, строки — параллельно: 16 кадров ×4 готовятся при открытии карты.</summary>
    private static byte[] Blend(byte[] src, int w, int h, bool horizontal)
    {
        var dst = new byte[src.Length];
        int side = horizontal ? w : h;
        float band = MathF.Max(1, side * SeamBand);
        var weight = new int[side];
        for (int a = 0; a < side; a++)
        {
            float m = MathF.Min(1, MathF.Min(a, side - 1 - a) / band);
            weight[a] = (int)MathF.Round(m * m * (3 - (2 * m)) * 256);   // плавно (smoothstep)
        }
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            int row = y * w * 4, rolledRow = horizontal ? row : ((y + (h / 2)) % h) * w * 4;
            for (int x = 0; x < w; x++)
            {
                int m = weight[horizontal ? x : y], at = row + (x * 4);
                int rolled = rolledRow + ((horizontal ? (x + (w / 2)) % w : x) * 4);
                for (int c = 0; c < 4; c++) dst[at + c] = (byte)(((src[at + c] * m) + (src[rolled + c] * (256 - m)) + 128) >> 8);
            }
        });
        return dst;
    }

    private void StepWater(double delta)
    {
        if (_water is null) return;
        _waterClock += delta;
        if (_waterClock < WaterFrameSeconds) return;
        _waterClock %= WaterFrameSeconds;
        _waterFrame = (_waterFrame + 1) % _waterFrames.Length;
        _water.Texture = _waterFrames[_waterFrame];
    }
}
