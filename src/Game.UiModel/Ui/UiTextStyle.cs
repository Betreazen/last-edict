using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace D2.Data.Ui;

/// <summary>Выравнивание по горизонтали. В старой разметке — <c>\hL;</c>, <c>\hC;</c>, <c>\hR;</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiHAlign { Left, Center, Right }

/// <summary>Выравнивание по вертикали. В старой разметке — <c>\vT;</c>, <c>\vC;</c>, <c>\vB;</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiVAlign { Top, Center, Bottom }

/// <summary>Цвет текста как три байта. Своя запись, а не <c>System.Drawing.Color</c> и не godot-овский
/// <c>Color</c>: <c>D2.Data</c> не знает ни о движке, ни о рисовании.</summary>
public readonly record struct UiColor(byte R, byte G, byte B)
{
    /// <summary>Обратно в запись старого движка: <c>000;000;000</c> — три числа по три цифры.</summary>
    public override string ToString() => $"{R:000};{G:000};{B:000}";
}

/// <summary>Разобранное оформление текста: выравнивание, ключ шрифта, цвет, сам текст и список
/// неопознанных директив.
///
/// <para>Разбирает разметку старого движка. Она встречается в двух местах, и грамматика у них одна:
/// в <see cref="UiElement.Style"/> (только оформление, текста нет) и внутри самих строк из
/// <c>TApp.dbf</c> (<c>"\c000;000;000;\hC;\fLarge;Неудача!"</c> — оформление и текст вместе,
/// 338 строк из 3284). Поэтому <see cref="Parse(string?, UiTextStyle)"/> принимает основу: сначала
/// разбирается оформление элемента, потом поверх него — разметка строки.</para>
///
/// <para>Грамматика выведена пересчётом всех значений в <c>data/ui/screens.json</c> (46 разных строк
/// оформления) и всех строк <c>TApp.dbf</c> + <c>Tglobal.dbf</c> (полный алфавит директив ниже).
/// Директива — обратная косая, буква, дальше аргументы через <c>;</c>:
/// <list type="table">
/// <item><term><c>\hL; \hC; \hR;</c></term><description>выравнивание по горизонтали</description></item>
/// <item><term><c>\vT; \vC; \vB;</c></term><description>выравнивание по вертикали</description></item>
/// <item><term><c>\fLarge;</c></term><description>ключ шрифта; регистр не значим</description></item>
/// <item><term><c>\c000;000;000;</c></term><description>цвет текста, три аргумента</description></item>
/// <item><term><c>\n</c>, <c>\t</c></term><description>перевод строки и табуляция, аргументов нет
/// и <c>;</c> за ними не идёт</description></item>
/// <item><term><c>\s110;</c></term><description>место табуляции: <c>\t</c> переводит к точке N от левого края (<see cref="Tab"/>)</description></item>
/// <item><term><c>\p110;</c></term><description>левое поле следующих строк абзаца (<see cref="Indent"/>)</description></item>
/// <item><term><c>\mL0;</c>, <c>\mR0;</c></term><description>новая строка, если текущая начата, и левое (правое) поле</description></item>
/// <item><term><c>\o000;000;000;</c></term>
/// <description>обводка — встречается только в строках, порт её пока не применяет и складывает в <see cref="Unknown"/></description></item>
/// </list>
/// Неопознанное не выбрасывается молча: нераспознанная директива и нераспознанное значение известной
/// директивы попадают в <see cref="Unknown"/> как есть, а проверка <c>tools/ui-render-check.py</c>
/// падает, если список непуст.</para></summary>
public sealed record UiTextStyle
{
    public UiHAlign H { get; init; } = UiHAlign.Left;
    public UiVAlign V { get; init; } = UiVAlign.Top;

    /// <summary>Ключ шрифта как в разметке (<c>Large</c>, <c>medbold</c>). Пусто — шрифт по умолчанию;
    /// в настоящее начертание и кегль ключ превращает <see cref="UiFonts"/>.</summary>
    public string Font { get; init; } = "";

    /// <summary>Цвет; <c>null</c> — цвет по умолчанию (<see cref="UiFonts.Color"/>).</summary>
    public UiColor? Color { get; init; }

    /// <summary>Цвет подсветки фона (<c>…;</c>): так в игре выделена выбранная строка списка — подсветкой фона,
    /// со слов пользователя (19.09.2026); встречается только в оформлении выбранной строки списков.</summary>
    public UiColor? Background { get; init; }

    /// <summary>Место табуляции в точках от левого края подписи (<c>\s110;</c>); 0 — не задано. Разбор движка (<c>0x529e4b</c>: вид
    /// метки 7 по таблице букв <c>0x6e0eb0</c> «cbhvnfstmp-o») пишет число в общую переменную разбора, а каждый <c>\t</c> (вид 8; знак
    /// табуляции в тексте — тоже, <c>0x529498</c>) уносит его с собой — переход к этой точке. Так карточка воина ставит значения столбцом
    /// (<c>X005TA0423</c>: «Уровень:», табуляция, «1» — значение с точки 110).</summary>
    public int Tab { get; init; }

    /// <summary>Место каждой табуляции текста по порядку — значение <see cref="Tab"/> в момент табуляции (в одной строке их может быть
    /// несколько: <c>X005TA0425</c> «\s60;\t…\s90;\t…»).</summary>
    public IReadOnlyList<int> Tabs { get; init; } = [];

    /// <summary>Левое поле следующих строк абзаца в точках (<c>\p110;</c> — вид 10, поле <c>+0x18</c>; <c>\mL0;</c> — вид 9); 0 — от
    /// края. Порт его разбирает, но перенос с полем подпись сцены пока не делает — строки переносятся от края.</summary>
    public int Indent { get; init; }

    /// <summary>Текст без разметки. Для <see cref="UiElement.Style"/> всегда пуст: там только оформление.</summary>
    public string Text { get; init; } = "";

    /// <summary>Директивы, которые порт не понял или не применяет, — как они записаны в исходнике.
    /// Повторы убраны, порядок сохранён.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = [];

    /// <summary>Значения по умолчанию старого движка: влево, вверх, шрифт и цвет экрана.</summary>
    public static readonly UiTextStyle Default = new();

    public static UiTextStyle Parse(string? markup) => Parse(markup, Default);

    /// <summary>Разбор поверх основы: всё, чего в разметке нет, берётся из <paramref name="basis"/>.
    /// <see cref="Text"/> и <see cref="Unknown"/> основы не наследуются — они относятся к своему исходнику.</summary>
    public static UiTextStyle Parse(string? markup, UiTextStyle basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var style = basis with { Text = "", Unknown = [], Tabs = [] };
        if (string.IsNullOrEmpty(markup)) return style;

        var text = new StringBuilder();
        var unknown = new List<string>();
        var tabs = new List<int>();
        int i = 0;
        while (i < markup.Length)
        {
            int slash = markup.IndexOf('\\', i);
            int end = slash < 0 ? markup.Length : slash;
            for (int k = i; k < end; k++)
                if (markup[k] == '\t') tabs.Add(style.Tab);   // знак табуляции в тексте — та же табуляция (0x529498)
            text.Append(markup, i, end - i);
            if (slash < 0) break;
            int before = text.Length;
            i = Directive(markup, slash, ref style, text, unknown);
            if (text.Length == before + 1 && text[^1] == '\t') tabs.Add(style.Tab);
        }

        return style with
        {
            Text = text.ToString(),
            Unknown = unknown.Distinct(StringComparer.Ordinal).ToList(),
            Tabs = tabs,
        };
    }

    /// <summary>Места табуляции первой строки, где она есть, расстояниями: у первой — от левого края, у следующих — от предыдущей (так их
    /// понимает подпись сцены: <c>Label.TabStops</c> Godot отсчитывает каждое место от конца предыдущей табуляции). Строка
    /// <c>X005TA0425</c> «\s60;\t120 \s90;\tМана Жизни» — [60, 30]. Нет мест (табуляция без <c>\s</c>) или места не по возрастанию —
    /// сколько есть до сбоя; ничего — пусто.</summary>
    public IReadOnlyList<int> TabStops()
    {
        int seen = 0;
        foreach (string line in Text.Split('\n'))
        {
            int count = line.Count(ch => ch == '\t');
            if (count == 0) continue;
            var places = Tabs.Skip(seen).Take(count).ToList();
            if (places.Count == 0 || places[0] <= 0) return [];
            var stops = new List<int> { places[0] };
            for (int k = 1; k < places.Count && places[k] > places[k - 1]; k++) stops.Add(places[k] - places[k - 1]);
            return stops;
        }
        return [];
    }

    /// <summary>Одна директива, начиная с обратной косой на <paramref name="at"/>. Возвращает место, с которого
    /// продолжать. Недописанная директива (нет буквы или не хватает <c>;</c>) уходит в неопознанные целиком:
    /// гадать, где она кончается, не на чем.</summary>
    private static int Directive(string markup, int at, ref UiTextStyle style, StringBuilder text,
                                 List<string> unknown)
    {
        if (at + 1 >= markup.Length) { unknown.Add(markup[at..]); return markup.Length; }
        char code = char.ToLowerInvariant(markup[at + 1]);

        // Без аргументов и без «;» — просто escape-последовательность внутри текста.
        if (code is 'n' or 't')
        {
            text.Append(code == 'n' ? '\n' : '\t');
            return at + 2;
        }

        // Цветовые директивы берут три аргумента (R;G;B), остальные — один.
        int arity = code is 'c' or 'o' or 'b' ? 3 : 1;
        var args = new string[arity];
        int p = at + 2;
        for (int a = 0; a < arity; a++)
        {
            int semi = markup.IndexOf(';', p);
            if (semi < 0) { unknown.Add(markup[at..]); return markup.Length; }
            args[a] = markup[p..semi];
            p = semi + 1;
        }

        string source = markup[at..p];
        switch (code)
        {
            case 'h' when Align(args[0], "LCR") is { } h: style = style with { H = (UiHAlign)h }; break;
            case 'v' when Align(args[0], "TCB") is { } v: style = style with { V = (UiVAlign)v }; break;
            case 'f' when args[0].Length > 0: style = style with { Font = args[0] }; break;
            case 'c' when Colour(args) is { } c: style = style with { Color = c }; break;
            case 'b' when Colour(args) is { } bg: style = style with { Background = bg }; break;
            case 's' when Number(args[0]) is { } tab: style = style with { Tab = tab }; break;
            case 'p' when Number(args[0]) is { } indent: style = style with { Indent = indent }; break;
            // \m (0x529f07): строка уже начата (последнее — не перевод строки) — перевод строки, затем поле: L — левое, R — правое
            // (0x52d5ea; правое порт не хранит). Так X005TA0423 ставит «Стойкость» под «Иммунитет» без \n.
            case 'm' when args[0].Length > 1 && "LlRr".Contains(args[0][0], StringComparison.Ordinal) && Number(args[0][1..]) is { } margin:
                if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
                if (args[0][0] is 'L' or 'l') style = style with { Indent = margin };
                break;
            default: unknown.Add(source); break;
        }
        return p;
    }

    private static int? Number(string arg) =>
        int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;

    private static int? Align(string arg, string letters)
    {
        if (arg.Length != 1) return null;
        int index = letters.IndexOf(char.ToUpperInvariant(arg[0]));
        return index < 0 ? null : index;
    }

    private static UiColor? Colour(string[] args)
    {
        var bytes = new byte[3];
        for (int i = 0; i < 3; i++)
            if (!byte.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
                return null;
        return new UiColor(bytes[0], bytes[1], bytes[2]);
    }
}
