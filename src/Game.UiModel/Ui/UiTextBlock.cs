namespace D2.Data.Ui;

/// <summary>
/// Положение блока строк подписи по вертикали в её месте — правило движка, а не Godot (разбор 11.10.2026, прогон 3, пункт 2).
///
/// <para>Вывод текста в прямоугольник (<c>0x52acc4</c>): высота текста <c>H</c> — сумма высот строк (<c>0x52a36e</c>), выравнивание
/// <c>\v</c> — выход разбора разметки (<c>0x52a011</c> → <c>0x52d1d0</c>: <c>T</c> = 0, <c>C</c> = 1, <c>B</c> = 2). Дальше
/// (<c>0x52ad0b…0x52ad3e</c>): если <c>H</c> больше высоты места — выравнивание сбрасывается в верх; низ — верх = низ места − <c>H</c>;
/// середина — верх += (низ − <c>H</c> − верх) / 2 (деление с отбрасыванием дроби).</para>
///
/// <para>Высота строки — шаг шрифта: ячейка MFT плюс межстрочный, у последней строки тоже (<see cref="UiFont.Line"/>, у Normal 13 + 4 = 17).
/// Сверено со снимками оригинала 28.09: «Скорость:\n20 / 20» в месте 85 точек — верх ячейки на 25 (2 × 17 = 34, (85 − 34) / 2), имя героя
/// в плашке 18 — на 0, название города (Large, 14 + 4) в месте 24 — на 3. Godot считает блок без последнего межстрочного
/// (13 + 4 + 13 = 30 → 27) и не сбрасывает выравнивание, когда текст выше места.</para>
/// </summary>
public static class UiTextBlock
{
    /// <summary>Высота блока: строки × шаг строки.</summary>
    public static int Height(int lines, int lineStep) => Math.Max(0, lines) * lineStep;

    /// <summary>Верх первой строки (верх её ячейки) от верха места высотой <paramref name="box"/>.</summary>
    public static int Top(int box, int lines, int lineStep, UiVAlign v)
    {
        int block = Height(lines, lineStep);
        if (block > box) return 0;
        return v switch
        {
            UiVAlign.Center => (box - block) / 2,
            UiVAlign.Bottom => box - block,
            _ => 0,
        };
    }
}
