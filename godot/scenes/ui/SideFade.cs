using Godot;

namespace LastEdict.Godot;

/// <summary>Поля по бокам картинки или окна уже холста (ответ пользователя 22.10, вместо растяжки картинки <see cref="AmbientFill"/>):
/// горизонтальный градиент симметрично от середины — серый вплотную к картинке, к краю холста чёрный. Под картинкой градиента не видно,
/// он лежит слоем ниже.</summary>
public static class SideFade
{
    /// <summary>Серый у края картинки — тон рамок интерфейса.</summary>
    public static readonly Color Near = new(0.27f, 0.26f, 0.24f);

    public static readonly Color Far = Colors.Black;

    /// <summary>Градиент на область <paramref name="area"/>, светлый у краёв <paramref name="inner"/> (в тех же координатах), на слое
    /// <paramref name="z"/>. Картинка не уже области — null.</summary>
    public static Sprite2D? Create(Rect2 area, Rect2 inner, int z)
    {
        if (area.Size.X <= inner.Size.X + 1 || area.Size.X <= 0 || area.Size.Y <= 0) return null;
        float left = Mathf.Clamp((inner.Position.X - area.Position.X) / area.Size.X, 0f, 1f);
        float right = Mathf.Clamp((inner.End.X - area.Position.X) / area.Size.X, left, 1f);
        var gradient = new Gradient
        {
            Offsets = [0f, left, right, 1f],
            Colors = [Far, Near, Near, Far],
        };
        var texture = new GradientTexture2D { Gradient = gradient, Width = 256, Height = 1, FillFrom = Vector2.Zero, FillTo = Vector2.Right };
        return new Sprite2D
        {
            Name = "SideFade", Texture = texture, Centered = false, Position = area.Position, ZIndex = z,
            Scale = new Vector2(area.Size.X / texture.Width, area.Size.Y / texture.Height),
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
    }
}
