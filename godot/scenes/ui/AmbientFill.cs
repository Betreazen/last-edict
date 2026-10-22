using Godot;

namespace LastEdict.Godot;

/// <summary>Заполнение полей холста шире картинки оригинала (Э5, Р13; §3.3 roadmap: «края — дорисовка или затемнение»): часть картинки,
/// растянутая на всю область, размытая и затемнённая, — под самой картинкой. Так поля вокруг поля боя 800×600 и картинки столицы 640×600
/// продолжают их цвет, а не чёрные; дорисованные края пользователь положит модом графики (Р19).</summary>
public static class AmbientFill
{
    /// <summary>Затемнение растяжки.</summary>
    public static readonly Color Dim = new(0.55f, 0.55f, 0.55f);

    /// <summary>Размытие: 5×5 выборок с шагом в несколько точек картинки.</summary>
    private const string BlurShader = """
        shader_type canvas_item;
        uniform float step_px = 4.0;

        void fragment() {
            vec2 texel = TEXTURE_PIXEL_SIZE * step_px;
            vec4 sum = vec4(0.0);
            for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                    sum += texture(TEXTURE, UV + vec2(float(x), float(y)) * texel);
            COLOR = vec4(sum.rgb / 25.0, 1.0) * COLOR;
        }
        """;

    private static ShaderMaterial? _blur;

    /// <summary>Растяжка <paramref name="region"/> картинки <paramref name="texture"/> с серединой в <paramref name="center"/>, покрывающая
    /// область <paramref name="cover"/> (с сохранением пропорций), на слое <paramref name="z"/>.</summary>
    public static Sprite2D Create(Texture2D texture, Rect2 region, Vector2 center, Vector2 cover, int z)
    {
        float scale = Mathf.Max(cover.X / region.Size.X, cover.Y / region.Size.Y);
        return new Sprite2D
        {
            Name = "Ambient", Texture = texture, RegionEnabled = true, RegionRect = region, Centered = true, Position = center,
            Scale = new Vector2(scale, scale), ZIndex = z, Modulate = Dim, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Material = _blur ??= new ShaderMaterial { Shader = new Shader { Code = BlurShader } },
        };
    }
}
