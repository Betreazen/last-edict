namespace LastEdict.Data.Ui;

/// <summary>
/// Перенос строк текста в месте шириной <c>W</c> — правило движка, а не Godot (<c>CTextBoxInterf</c>, сборка картинки <c>0x533862</c> →
/// <c>0x52a2fd</c>).
///
/// <para>Куски (<c>0x529b94</c>): абзац режется перед каждым непробельным знаком, которому предшествует пробел, — кусок «слово + пробелы
/// за ним» (пробелы в начале абзаца — свой кусок перед первым словом). Строка (<c>0x52a1ba</c>): ширина куска не больше <c>W</c>; кусок встаёт в строку,
/// пока сумма ширин её кусков с ним строго меньше <c>W</c> (<c>0x52a2a5</c>: «≥ ширины» — в следующую), — пробелы за словом тоже
/// считаются. Кусок, который не влез и первым, всё равно занимает строку один (<c>0x52a2f0</c>: кусков 0 → 1): длинное слово по буквам
/// не рвётся, а выходит за место (картинка текста шириной места его срезает). Перевод строки — обязательный конец строки
/// (<c>0x52a2d3</c>).</para>
///
/// <para>Godot (<c>AutowrapMode.WordSmart</c>) рвал слово длиннее места по буквам и не считал хвостовые пробелы (сборка 17.10, `r17b/uifix`).
/// Табуляцию (места табуляции, <c>0x52a254</c>) правило не разбирает — такой текст переносит Godot.</para>
/// </summary>
public static class UiTextWrap
{
    /// <summary>Строки текста: <paramref name="measure"/> — ширина куска шрифтом подписи в точках разметки.</summary>
    public static IReadOnlyList<string> Lines(string text, float width, Func<string, float> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            float used = 0;
            foreach (string chunk in Chunks(paragraph))
            {
                float w = Math.Min(measure(chunk), width);
                if (line.Length > 0 && used + w >= width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    used = 0;
                }
                line.Append(chunk);
                used += w;
                // Первый кусок не влез и один (0x52a28e → 0x52a2f0): строка — он один, следующий — с новой строки.
                if (used >= width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    used = 0;
                }
            }
            if (line.Length > 0 || paragraph.Length == 0) lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>Текст с переносами (<see cref="Lines"/>) через <c>\n</c>.</summary>
    public static string Wrap(string text, float width, Func<string, float> measure) => string.Join('\n', Lines(text, width, measure));

    /// <summary>Куски абзаца «слово + пробелы» (<c>0x529b94</c>).</summary>
    public static IEnumerable<string> Chunks(string paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        int start = 0;
        for (int i = 1; i < paragraph.Length; i++)
        {
            if (paragraph[i] == ' ' || paragraph[i - 1] != ' ') continue;
            yield return paragraph[start..i];
            start = i;
        }
        if (start < paragraph.Length) yield return paragraph[start..];
    }
}
