using LastEdict.Data.Ui;

namespace LastEdict.Data.Input;

/// <summary>Сторона шага крестовины.</summary>
public enum NavDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Шаг фокуса по экрану разметки крестовиной (Э5, Р12): у экранов оригинала фокуса нет, кнопки ждут мышь — геймпад переводит указатель
/// на соседний элемент в нужную сторону (<c>GamepadPointer</c>), и нажатие A щёлкает под ним. Соседний — ближайший по ходу с штрафом
/// за уход вбок (ход + 4 × поперёк: кнопка прямо в стороне лучше близкой наискосок) среди лежащих в конусе 45° от стороны; в конусе никого —
/// среди всех, что дальше по ходу. Элементы — кнопки и переключатели, которые сейчас можно
/// нажать (<paramref name="usable"/>). Без начального элемента — кнопка «подтвердить» экрана (горячая клавиша Enter), иначе первая
/// сверху-слева.
/// </summary>
/// <summary>Логика окна, у которой есть цели крестовины вне разметки (значки дерева построек в виде с зумом): прямоугольники — в точках разметки
/// экрана, как у элементов; шаг на цель зовёт <see cref="PadFocused"/> (вид сдвигается, чтобы цель была видна), затем цель спрашивается заново.</summary>
public interface IPadTargets
{
    IReadOnlyList<UiElement> PadTargets();

    void PadFocused(string id);
}

public static class UiNavigation
{
    private const int VkEnter = 13;

    public static bool Targetable(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Kind is UiKind.Button or UiKind.Toggle && element.Rect.Width > 0 && element.Rect.Height > 0;
    }

    /// <summary>Элемент, куда шагнуть из <paramref name="from"/> (null — фокуса ещё нет) в сторону <paramref name="direction"/>; некуда — null
    /// (фокус остаётся).</summary>
    public static string? Next(UiScreen screen, Func<string, bool> usable, string? from, NavDirection direction)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(usable);
        var targets = screen.Elements.Where(e => Targetable(e) && usable(e.Id)).ToList();
        if (targets.Count == 0) return null;
        var origin = from is null ? null : screen.Elements.FirstOrDefault(e => e.Id == from);
        if (origin is null)
            return (targets.FirstOrDefault(e => e.Hotkeys.Contains(VkEnter))
                    ?? targets.OrderBy(e => e.Rect.Y).ThenBy(e => e.Rect.X).First()).Id;
        var (ox, oy) = Center(origin.Rect);
        var (dx, dy) = direction switch
        {
            NavDirection.Up => (0, -1),
            NavDirection.Down => (0, 1),
            NavDirection.Left => (-1, 0),
            _ => (1, 0),
        };
        var ahead = targets.Where(t => t.Id != origin.Id)
            .Select(e =>
            {
                var (cx, cy) = Center(e.Rect);
                double vx = cx - ox, vy = cy - oy;
                return (e.Id, Along: (vx * dx) + (vy * dy), Across: Math.Abs((vx * dy) - (vy * dx)));
            })
            .Where(c => c.Along > 0.5)
            .ToList();
        var cone = ahead.Where(c => c.Across <= c.Along).ToList();
        return (cone.Count > 0 ? cone : ahead).OrderBy(c => c.Along + (4 * c.Across)).Select(c => c.Id).FirstOrDefault();
    }

    public static (double X, double Y) Center(UiRect rect) => (rect.X + (rect.Width / 2.0), rect.Y + (rect.Height / 2.0));
}
