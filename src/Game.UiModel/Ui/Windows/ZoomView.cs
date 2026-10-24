namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Вид на содержимое с масштабом и сдвигом (Э6, окно построек): точка вида = точка содержимого × <see cref="Zoom"/> + (<see cref="X"/>, <see cref="Y"/>).
/// Зум колесом — к точке под курсором (она остаётся на месте), в пределах <see cref="MinZoom"/>…<see cref="MaxZoom"/>; тащение — сдвиг; после
/// каждого шага вид ограничен рамкой содержимого: меньше вида — по середине, больше — без пустых полос у края. Попадание считается здесь же
/// обратным преобразованием (<see cref="ToContent"/>) — проверки без движка. Единицы — точки окна (логические, как у разметки экрана).
/// </summary>
public sealed class ZoomView
{
    /// <summary>Наибольший масштаб: HD-значки ×4 при окне ~×1,8 от 800×600 ещё не мылятся.</summary>
    public const double MaxZoom = 2.5;

    /// <summary>Шаг колеса — во столько раз за щелчок.</summary>
    public const double WheelStep = 1.15;

    public double Width { get; private set; }
    public double Height { get; private set; }
    public double ContentWidth { get; private set; }
    public double ContentHeight { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double X { get; private set; }
    public double Y { get; private set; }

    /// <summary>Наименьший масштаб: всё содержимое в виде, но не мельче половины и не крупнее 1.</summary>
    public double MinZoom => Math.Clamp(Fit, 0.5, 1);

    /// <summary>Масштаб, при котором содержимое целиком входит в вид.</summary>
    public double Fit => ContentWidth <= 0 || ContentHeight <= 0 || Width <= 0 || Height <= 0 ? 1
        : Math.Min(Width / ContentWidth, Height / ContentHeight);

    /// <summary>Новый вид и содержимое: масштаб — чтобы содержимое вошло целиком (не крупнее 1), по середине сверху.</summary>
    public void Reset(double width, double height, double contentWidth, double contentHeight)
    {
        (Width, Height, ContentWidth, ContentHeight) = (width, height, contentWidth, contentHeight);
        Zoom = Math.Clamp(Fit, MinZoom, 1);
        X = (width - (contentWidth * Zoom)) / 2;
        Y = 0;
        Clamp();
    }

    /// <summary>Вид сменил размер (окно растянули): масштаб и точка содержимого в середине вида — прежние, насколько пускают пределы.</summary>
    public void Resize(double width, double height)
    {
        var (cx, cy) = ToContent(Width / 2, Height / 2);
        (Width, Height) = (width, height);
        Zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);
        X = (width / 2) - (cx * Zoom);
        Y = (height / 2) - (cy * Zoom);
        Clamp();
    }

    /// <summary>Масштаб × <paramref name="factor"/> к точке вида (<paramref name="vx"/>, <paramref name="vy"/>): точка содержимого под ней
    /// остаётся на месте (если пускает рамка). true — масштаб изменился.</summary>
    public bool ZoomAt(double vx, double vy, double factor)
    {
        double zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(zoom - Zoom) < 1e-9) return false;
        var (cx, cy) = ToContent(vx, vy);
        Zoom = zoom;
        X = vx - (cx * zoom);
        Y = vy - (cy * zoom);
        Clamp();
        return true;
    }

    /// <summary>Сдвиг на (<paramref name="dx"/>, <paramref name="dy"/>) точек вида (тащение). true — вид сдвинулся.</summary>
    public bool Pan(double dx, double dy)
    {
        var (x, y) = (X, Y);
        X += dx;
        Y += dy;
        Clamp();
        return Math.Abs(x - X) > 1e-9 || Math.Abs(y - Y) > 1e-9;
    }

    /// <summary>Сдвинуть вид так, чтобы прямоугольник содержимого был виден целиком (с полем <paramref name="margin"/> точек вида).</summary>
    public void Reveal(UiRect rect, double margin = 12)
    {
        double left = (rect.X * Zoom) + X, right = (rect.Right * Zoom) + X, top = (rect.Y * Zoom) + Y, bottom = (rect.Bottom * Zoom) + Y;
        double dx = left < margin ? margin - left : right > Width - margin ? Width - margin - right : 0;
        double dy = top < margin ? margin - top : bottom > Height - margin ? Height - margin - bottom : 0;
        Pan(dx, dy);
    }

    public (double X, double Y) ToContent(double vx, double vy) => ((vx - X) / Zoom, (vy - Y) / Zoom);

    public (double X, double Y) ToView(double cx, double cy) => ((cx * Zoom) + X, (cy * Zoom) + Y);

    /// <summary>Прямоугольник содержимого в точках вида.</summary>
    public UiRect ToView(UiRect rect)
    {
        var (x, y) = ToView(rect.X, rect.Y);
        return new UiRect((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(rect.Width * Zoom), (int)Math.Round(rect.Height * Zoom));
    }

    private void Clamp()
    {
        X = Axis(X, Width, ContentWidth * Zoom);
        Y = Axis(Y, Height, ContentHeight * Zoom);
    }

    /// <summary>Сдвиг по оси: содержимое уже вида — по середине, шире — край содержимого не отходит от края вида.</summary>
    private static double Axis(double offset, double view, double content) =>
        content <= view ? (view - content) / 2 : Math.Clamp(offset, view - content, 0);
}
