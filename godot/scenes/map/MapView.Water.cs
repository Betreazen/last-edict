using System;
using System.IO;
using System.Linq;
using D2.Data.Gfx;
using Godot;

namespace D2.Godot;

/// <summary>Анимированная вода (<c>docs/ground-render.md</c> §4): под землёй — фон <c>WATERBKGND1</c> (IsoAnim, 128×128, 16 кадров)
/// плиткой на всю карту с периодом 128 по миру, кадр раз в 66 мс (медленный аниматор, <c>docs/map-render.md</c>); точки воды
/// в кусках земли прозрачны (<see cref="GroundComposer.AnimatedWater"/>). Нет картинки — вода неподвижная (<c>WA_00</c>).</summary>
public partial class MapView
{
    private const string WaterAnim = "WATERBKGND1";
    private const int WaterTile = 128;
    private const double WaterFrameSeconds = D2.Data.Map.AnimClock.SlowMs / 1000.0;

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
        _waterFrames = [.. GfxAssets.FramePaths(anim, GameData.GfxRoot).Where(File.Exists)
                                   .Select(p => (Texture2D)ImageTexture.CreateFromImage(GfxTextures.Canvas(p)))];
        if (_waterFrames.Length == 0) return;
        float left = MathF.Floor(_bounds.Position.X / WaterTile) * WaterTile, top = MathF.Floor(_bounds.Position.Y / WaterTile) * WaterTile;
        float width = MathF.Ceiling((_bounds.End.X - left) / WaterTile) * WaterTile, height = MathF.Ceiling((_bounds.End.Y - top) / WaterTile) * WaterTile;
        _water = new Sprite2D
        {
            Name = "Water", Texture = _waterFrames[0], Centered = false, Position = new Vector2(left, top),
            RegionEnabled = true, RegionRect = new Rect2(0, 0, width, height),
            TextureRepeat = TextureRepeatEnum.Enabled, TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(_water);
        GameLog.Verbose($"water кадров={_waterFrames.Length} плитка={WaterTile}");
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
