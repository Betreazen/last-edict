using D2.Data.Gfx;
using Godot;

namespace D2.Godot;

/// <summary>Смешение кадра карты по его <c>opacity</c> (<see cref="GfxBlend"/>, <c>docs/ai-screen.md</c> §13): половина — прозрачность 0,5
/// (та же формула <c>⌊d/2⌋+⌊s/2⌋</c> с точностью до округления 565), сложение и вычитание — материал с режимом смешения, 0 — скрыт.</summary>
public static class GfxBlending
{
    private const float HalfAlpha = 0.5f;

    private static CanvasItemMaterial? _add, _subtract;

    private static CanvasItemMaterial AddMaterial =>
        _add ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    private static CanvasItemMaterial SubtractMaterial =>
        _subtract ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Sub };

    /// <summary>Настроить картинку под смешение.</summary>
    public static void Apply(CanvasItem item, GfxBlend blend)
    {
        item.Modulate = blend == GfxBlend.Half ? new Color(1, 1, 1, HalfAlpha) : Colors.White;
        item.Material = blend switch
        {
            GfxBlend.Add => AddMaterial,
            GfxBlend.Subtract => SubtractMaterial,
            _ => null,
        };
        item.Visible = blend != GfxBlend.Hidden;
    }

    /// <summary>Смешение картинки сцены карты: по описи; тень без поля в описи — половина (тени отрядов — 128).</summary>
    public static GfxBlend Of(MapSprite s, GfxAssets gfx)
    {
        var blend = gfx.BlendOf(s.Archive, s.Name, s.Anim);
        return s.Shadow && blend == GfxBlend.Opaque ? GfxBlend.Half : blend;
    }
}
