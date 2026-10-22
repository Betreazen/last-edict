namespace LastEdict.Data.Ui;

/// <summary>Логика экрана, которой нужно время (автопрокрутка текста): сцена зовёт <see cref="Tick"/> с прошедшими миллисекундами — в
/// игре по кадрам, в проверке шагом <c>wait N</c>.</summary>
public interface IUiTicking
{
    void Tick(int elapsedMs, UiScreenState ui);
}

/// <summary>
/// Всплывающая подсказка экрана (<c>CTooltipImpl</c> <c>0x53f40e</c>…<c>0x53ffaa</c>, один объект на весь интерфейс): мышь над элементом с
/// подсказкой (<see cref="UiScreenState.TooltipAt"/>) — отсчёт <see cref="DelayMs"/>, каждое движение над ним, пока подсказка не
/// показана, отсчёт перезапускает (<c>0x53fe48</c>); показанная и переход на другой элемент с подсказкой — новая сразу (<c>0x53f89a</c>),
/// движение внутри того же элемента ничего не меняет. Прячется: мышь ушла на место без подсказки, любое нажатие или отпускание кнопки
/// мыши (<c>0x53cfd3</c>), смена окна. Место (<c>0x53fa0b</c>, режим 6 — снизу слева от курсора): текст не больше
/// <see cref="MaxWidth"/>×<see cref="MaxHeight"/>, рамка на 4 точки шире с каждой стороны и на 1 выше и ниже, прижата к краям экрана
/// (<c>0x53fc5b</c>). Вид — чёрный текст на белом с рамкой (<c>0x61b1d1</c>). ponytail: имя шрифта и цвет рамки не разобраны.
/// </summary>
public sealed class UiTooltip
{
    public const int DelayMs = 250, Offset = 4, MaxWidth = 500, MaxHeight = 50, PadX = 4, PadY = 1;

    private int _waited;

    /// <summary>Элемент, чья подсказка ждёт или показана; null — подсказки нет.</summary>
    public string? Element { get; private set; }

    /// <summary>Строка подсказки (id строки или сам текст — как в разметке).</summary>
    public string? Text { get; private set; }

    public bool Shown { get; private set; }

    /// <summary>Мышь двинулась: над элементом <paramref name="element"/> с подсказкой <paramref name="text"/> или (null) над местом без неё.</summary>
    public void Move(string? element, string? text)
    {
        if (element is null || text is null)
        {
            Hide();
            return;
        }
        _waited = 0;
        if (element == Element) return;
        bool shown = Shown;
        (Element, Text, Shown) = (element, text, shown);
    }

    /// <summary>Прошло <paramref name="elapsedMs"/>; true — подсказка только что показалась.</summary>
    public bool Tick(int elapsedMs)
    {
        if (Element is null || Shown) return false;
        _waited += Math.Max(0, elapsedMs);
        if (_waited < DelayMs) return false;
        Shown = true;
        return true;
    }

    public void Hide()
    {
        (Element, Text, Shown, _waited) = (null, null, false, 0);
    }

    /// <summary>Рамка подсказки для текста <paramref name="textWidth"/>×<paramref name="textHeight"/> у мыши, прижатая к <paramref name="bounds"/>;
    /// текст — в ней со сдвигом (<see cref="PadX"/>, <see cref="PadY"/>).</summary>
    public static UiRect Place(int mouseX, int mouseY, int textWidth, int textHeight, UiRect bounds)
    {
        int tw = Math.Clamp(textWidth, 0, MaxWidth), th = Math.Clamp(textHeight, 0, MaxHeight);
        int w = tw + (2 * PadX), h = th + (2 * PadY);
        int x = Math.Clamp(mouseX - tw - Offset, bounds.X, Math.Max(bounds.X, bounds.Right - w));
        int y = Math.Clamp(mouseY + Offset, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - h));
        return new UiRect(x, y, w, h);
    }
}
