using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Поле боя на холсте шире или выше 800×600 (Э5, Р13): поле и юниты держатся середины (<see cref="Margin"/> — сдвиг на половину
/// прироста). Фон места 950×600 нарисован только там, где его видно в оригинале (экран 0…800; за панелями ниже верхней полосы — чёрное),
/// поэтому слои места обрезаются по экрану оригинала, а поле прироста заполняет размытая и затемнённая растяжка середины фона (между
/// панелями) — §3.3 roadmap: «края — дорисовка или затемнение»; дорисованные края пользователь положит модом графики (Р19).</summary>
public partial class BattleView
{
    /// <summary>Середина фона, которая видна между панелями при любом месте (x поля 174…627): из неё — растяжка краёв.</summary>
    private const int ClearLeft = 174, ClearRight = 627;

    private Vector2I _margin;
    private readonly List<Sprite2D> _placeNodes = new();
    private Sprite2D? _ambient;

    /// <summary>Поле прироста холста с каждой стороны, в точках поля: поле сдвигается на него, края заполняются растяжкой фона.</summary>
    public Vector2I Margin
    {
        get => _margin;
        set
        {
            if (value == _margin) return;
            _margin = value;
            Position = value;
            Edges();
        }
    }

    /// <summary>Фон места последней сборки (<see cref="BattleScene.PlaceLayers"/>: слой <c>*_BG</c>).</summary>
    private BattleSprite? _background;

    /// <summary>Растяжек краёв последней сборкой (0 или 1) — для проверок.</summary>
    public int EdgesShown => _ambient is null ? 0 : 1;

    /// <summary>Обрезка слоёв места и растяжка краёв по нынешнему <see cref="Margin"/>.</summary>
    private void Edges()
    {
        _ambient?.QueueFree();
        _ambient = null;
        if (_scene?.Layout is { Wide: true } layout)
        {
            WideEdges(layout.Width);
            return;
        }
        bool wide = _margin != Vector2I.Zero;
        foreach (var node in _placeNodes)
        {
            // Слой места — с x фона (BattleLayout.BackgroundX): экран оригинала 0…800 — его столбцы −x…−x+800.
            int from = -(_background?.X ?? 0);
            node.RegionEnabled = wide;
            node.RegionRect = new Rect2(from, 0, BattleScene.CanvasWidth, BattleScene.CanvasHeight);
            node.Position = new Vector2(wide ? 0 : -from, node.Position.Y);
        }
        if (!wide || _layer is null || _background is not { } bg || Frames(bg) is not [var texture, ..]) return;
        var clear = new Rect2(ClearLeft - bg.X, 0, ClearRight - ClearLeft, BattleScene.CanvasHeight);
        var canvas = new Vector2(BattleScene.CanvasWidth + (2 * _margin.X), BattleScene.CanvasHeight + (2 * _margin.Y));
        _ambient = AmbientFill.Create(texture, clear, new Vector2(BattleScene.CanvasWidth / 2f, BattleScene.CanvasHeight / 2f), canvas, bg.Layer - 1);
        _layer.AddChild(_ambient);
    }

    /// <summary>Нарисованная при любом месте полоса фона (x картинки 150…800): ниже неба остальное в картинках чёрное — его закрывали
    /// панели оригинала.</summary>
    private const int PaintedLeft = 150, PaintedRight = 800;

    /// <summary>Чёрное картинки фона — прозрачное: под ним видна растяжка.</summary>
    private const string KeyBlackShader = """
        shader_type canvas_item;

        void fragment() {
            vec4 c = texture(TEXTURE, UV);
            COLOR.a *= smoothstep(0.004, 0.03, max(c.r, max(c.g, c.b)));   // мягко: сжатие HD и фильтр дают «почти чёрный» край
        }
        """;

    private static ShaderMaterial? _keyBlack;

    /// <summary>Широкий бой (<see cref="WideBattle"/>): фон места целиком, по середине холста, его чёрное (за панелями оригинала) прозрачно;
    /// под ним — растяжка нарисованной полосы на весь холст с полями прироста.</summary>
    private void WideEdges(int width)
    {
        foreach (var node in _placeNodes)
        {
            node.RegionEnabled = false;
            node.Position = new Vector2(_background?.X ?? 0, node.Position.Y);
            node.Material = _keyBlack ??= new ShaderMaterial { Shader = new Shader { Code = KeyBlackShader } };
        }
        if (_layer is null || _background is not { } bg || Frames(bg) is not [var texture, ..]) return;
        var clear = new Rect2(PaintedLeft, 0, PaintedRight - PaintedLeft, BattleScene.CanvasHeight);
        var canvas = new Vector2(width + (2 * _margin.X), BattleScene.CanvasHeight + (2 * _margin.Y));
        _ambient = AmbientFill.Create(texture, clear, new Vector2(width / 2f, BattleScene.CanvasHeight / 2f), canvas, bg.Layer - 1);
        _layer.AddChild(_ambient);
    }

    /// <summary>Запомнить фон и узлы слоёв места сборки (источник «фон», <see cref="BattleScene.PlaceLayers"/>) и поставить края (из
    /// <see cref="Build"/>).</summary>
    private void PlaceEdges(IReadOnlyList<BattleSprite> layers)
    {
        _ambient = null;   // прежняя растяжка снята вместе со слоем
        _placeNodes.Clear();
        _background = layers.FirstOrDefault(l => l.Name.EndsWith("_BG", System.StringComparison.OrdinalIgnoreCase));
        if (_background is not null && _byUnit.TryGetValue(_background.Source, out var nodes)) _placeNodes.AddRange(nodes.OfType<Sprite2D>());
        Edges();
    }
}
