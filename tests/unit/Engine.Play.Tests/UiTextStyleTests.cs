using System.Text.RegularExpressions;
using D2.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>Разбор разметки оформления. Главная часть — перечисление всех значений, которые реально встречаются
/// в <c>data/ui/screens.json</c>: сколько их, какие и во что каждое разбирается. Числа сверены пересчётом самого
/// файла.</summary>
public class UiTextStyleTests(ITestOutputHelper output)
{
    private static string ScreensJson => ImportedData.PathOf("ui/screens.json");

    /// <summary>Все разные строки оформления набора экранов, по убыванию частоты.</summary>
    private static IReadOnlyList<(string Style, int Count)> RealStyles =>
        UiScreens.Load(ScreensJson).SelectMany(s => s.Elements)
                 .Select(e => e.Style).Where(s => !string.IsNullOrEmpty(s))
                 .GroupBy(s => s!, StringComparer.Ordinal)
                 .Select(g => (g.Key, g.Count())).OrderByDescending(p => p.Item2).ToList();

    // --- перечисление настоящих значений ---

    /// <summary>В наборе экранов 46 разных строк оформления на 429 элементов, и все они разбираются
    /// без остатка: непонятая директива попала бы в <see cref="UiTextStyle.Unknown"/>. Текста в них нет —
    /// это только оформление.</summary>
    [SkippableFact]
    public void RealStyles_AllParse_WithoutUnknownDirectives()
    {
        var styles = RealStyles;
        output.WriteLine($"разных строк оформления {styles.Count}, элементов с оформлением {styles.Sum(p => p.Count)}");
        foreach (var (style, count) in styles)
        {
            var parsed = UiTextStyle.Parse(style);
            output.WriteLine($"{count,4}  {style,-40} -> {parsed.H}/{parsed.V} " +
                             $"шрифт «{parsed.Font}» цвет {(parsed.Color?.ToString() ?? "по умолчанию")}");
            Assert.True(parsed.Unknown.Count == 0, $"{style}: не разобрано {string.Join(", ", parsed.Unknown)}");
            Assert.Equal("", parsed.Text);
        }

        Assert.Equal(46, styles.Count);
        Assert.Equal(429, styles.Sum(p => p.Count));
    }

    /// <summary>Каждая настоящая строка разбирается ровно в то, что в ней написано: выравнивание, шрифт и цвет
    /// сверяются с текстом самой строки, а не с заранее выписанным ответом. Так проверка покрывает все 46
    /// значений сразу и не устареет, если в наборе появится новое.</summary>
    [SkippableFact]
    public void RealStyles_MatchTheirOwnText()
    {
        foreach (var (style, _) in RealStyles)
        {
            var parsed = UiTextStyle.Parse(style);

            Assert.Equal(Single(style, UiHAlign.Left, ("\\hL;", UiHAlign.Left), ("\\hC;", UiHAlign.Center),
                                ("\\hR;", UiHAlign.Right)), parsed.H);
            Assert.Equal(Single(style, UiVAlign.Top, ("\\vT;", UiVAlign.Top), ("\\vC;", UiVAlign.Center),
                                ("\\vB;", UiVAlign.Bottom)), parsed.V);

            var font = Regex.Match(style, @"\\f([^;]*);", RegexOptions.IgnoreCase);
            Assert.Equal(font.Success ? font.Groups[1].Value : "", parsed.Font);

            var colour = Regex.Match(style, @"\\c(\d{3});(\d{3});(\d{3});", RegexOptions.IgnoreCase);
            Assert.Equal(colour.Success
                             ? new UiColor(byte.Parse(colour.Groups[1].Value), byte.Parse(colour.Groups[2].Value),
                                           byte.Parse(colour.Groups[3].Value))
                             : null,
                         parsed.Color);
        }
    }

    /// <summary>Какие вообще значения директив встречаются в наборе: это и есть перечень, который проверка
    /// обязана покрывать. Список печатается — если в данных появится новое значение, оно тут же видно.</summary>
    [SkippableFact]
    public void RealStyles_UseOnlyKnownDirectiveValues()
    {
        var all = string.Join(" ", RealStyles.Select(p => p.Style));
        var seen = Regex.Matches(all, @"\\(.)").Select(m => char.ToLowerInvariant(m.Groups[1].Value[0]))
                        .Distinct().Order().ToList();
        output.WriteLine($"директивы: {string.Join(", ", seen)}");

        var fonts = Regex.Matches(all, @"\\f([^;]*);", RegexOptions.IgnoreCase)
                         .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)
                         .OrderBy(f => f, StringComparer.Ordinal).ToList();
        output.WriteLine($"шрифты: {string.Join(", ", fonts)}");

        Assert.Equal(['c', 'f', 'h', 'v'], seen);
        // Регистр в разметке гуляет — в порту он не значим.
        Assert.Equal(["Large", "Medbold", "Menu", "Small", "VLarge", "large", "medbold", "menu", "vlarge"], fonts);
        Assert.Equal(5, fonts.Select(f => f.ToLowerInvariant()).Distinct().Count());
    }

    // --- отдельные правила ---

    /// <summary><c>\b</c> — подсветка фона: так в оформлении выбранной строки списков (<c>TLBOX</c>) задана
    /// выделенная строка.</summary>
    [Fact]
    public void Background_FromListSelectionStyle()
    {
        var style = UiTextStyle.Parse(@"\hC;\b255;255;255;");
        Assert.Equal(new UiColor(255, 255, 255), style.Background);
        Assert.Equal(UiHAlign.Center, style.H);
        Assert.Empty(style.Unknown);
    }

    /// <summary>Пусто и мусор: без разметки — значения по умолчанию (влево, вверх, шрифт и цвет экрана).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_GivesDefaults(string? style)
    {
        var parsed = UiTextStyle.Parse(style);
        Assert.Equal(UiHAlign.Left, parsed.H);
        Assert.Equal(UiVAlign.Top, parsed.V);
        Assert.Equal("", parsed.Font);
        Assert.Null(parsed.Color);
        Assert.Empty(parsed.Unknown);
    }

    /// <summary>Все значения выравнивания, которые понимает порт, включая <c>R</c> и <c>B</c>: в наборе экранов
    /// <c>\hR;</c> не встречается, но грамматика у обеих осей одна, и разбирать её надо целиком.</summary>
    [Theory]
    [InlineData("\\hL;\\vT;", UiHAlign.Left, UiVAlign.Top)]
    [InlineData("\\hC;\\vC;", UiHAlign.Center, UiVAlign.Center)]
    [InlineData("\\hR;\\vB;", UiHAlign.Right, UiVAlign.Bottom)]
    [InlineData("\\hc;\\vb;", UiHAlign.Center, UiVAlign.Bottom)]
    public void Alignment_IsCaseInsensitive(string style, UiHAlign h, UiVAlign v)
    {
        var parsed = UiTextStyle.Parse(style);
        Assert.Equal(h, parsed.H);
        Assert.Equal(v, parsed.V);
        Assert.Empty(parsed.Unknown);
    }

    /// <summary>Цвет — три аргумента через точку с запятой, по три цифры. Все три значения из набора экранов.</summary>
    [Theory]
    [InlineData("\\c000;000;000;", 0, 0, 0)]
    [InlineData("\\c255;255;255;", 255, 255, 255)]
    [InlineData("\\c064;064;064;", 64, 64, 64)]
    public void Colour_ReadsThreeArguments(string style, byte r, byte g, byte b)
    {
        var parsed = UiTextStyle.Parse(style);
        Assert.Equal(new UiColor(r, g, b), parsed.Color);
        Assert.Empty(parsed.Unknown);
        Assert.Equal(style.Trim('\\', 'c', ';'), parsed.Color!.Value.ToString());
    }

    /// <summary>Порядок директив не важен, последняя выигрывает.</summary>
    [Fact]
    public void LastDirectiveWins_AndOrderDoesNotMatter()
    {
        // Списки внутри записи сравниваются по ссылке, поэтому сверяются разобранные поля.
        var a = UiTextStyle.Parse("\\hC;\\vC;\\fLarge;");
        var b = UiTextStyle.Parse("\\fLarge;\\vC;\\hC;");
        Assert.Equal((a.H, a.V, a.Font, a.Color, a.Text), (b.H, b.V, b.Font, b.Color, b.Text));
        Assert.Equal(UiHAlign.Right, UiTextStyle.Parse("\\hC;\\hR;").H);
    }

    /// <summary>Неизвестная директива не пропадает молча, а попадает в <see cref="UiTextStyle.Unknown"/> как есть.
    /// <c>\o</c> — обводка: встречается в строках <c>TApp.dbf</c>, порт её пока не применяет; у <c>\s</c>, <c>\p</c>, <c>\m</c> — только
    /// значение не числом. Цветовые берут три аргумента, остальные — один, иначе разбор поехал бы дальше по тексту.</summary>
    [Theory]
    [InlineData("\\o000;000;000;Текст", "\\o000;000;000;", "Текст")]
    [InlineData("\\sX;Текст", "\\sX;", "Текст")]
    [InlineData("\\p-1;Текст", "\\p-1;", "Текст")]
    [InlineData("\\mX3;Текст", "\\mX3;", "Текст")]
    [InlineData("\\hX;Текст", "\\hX;", "Текст")]
    [InlineData("\\cАБВ;000;000;Текст", "\\cАБВ;000;000;", "Текст")]
    public void UnknownDirective_IsReportedNotDropped(string markup, string reported, string text)
    {
        var parsed = UiTextStyle.Parse(markup);
        Assert.Equal([reported], parsed.Unknown);
        Assert.Equal(text, parsed.Text);
    }

    /// <summary>Табуляция и поля строк игры (разбор движка <c>0x529e4b</c>, буквы <c>0x6e0eb0</c> «cbhvnfstmp-o»): <c>\s110;</c> — место
    /// табуляции, <c>\p110;</c> — левое поле, <c>\mL0;</c> — перевод строки, если строка начата, и левое поле (<c>0x529f07</c>), — так
    /// <c>X005TA0423</c> ставит «Стойкость» под «Иммунитет» без <c>\n</c> (прогон 2 пользователя 11.10, снимок 33: слиплись).</summary>
    [Fact]
    public void TabStopAndMargins_FromGameStrings()
    {
        var parsed = UiTextStyle.Parse("\\s110;\\fmedbold;Уровень:\\t\\fnormal;1\\n\\fMedbold;Иммунитет:\\t\\fNormal;\\p110;Нет\\mL0;\\fMedbold;Стойкость:\\t\\fNormal;\\p110;Нет");
        Assert.Empty(parsed.Unknown);
        Assert.Equal(110, parsed.Tab);
        Assert.Equal(110, parsed.Indent);
        Assert.Equal("Уровень:\t1\nИммунитет:\tНет\nСтойкость:\tНет", parsed.Text);
        // \m в начале текста или сразу за переводом строки новой строки не добавляет; правое поле левое не трогает.
        Assert.Equal("А\nБ", UiTextStyle.Parse("\\mL0;А\\n\\mL5;Б").Text);
        Assert.Equal(5, UiTextStyle.Parse("\\mL5;А\\mR9;Б").Indent);
        Assert.Equal(0, UiTextStyle.Parse("Без табуляции").Tab);
        // Места табуляции подписи: у каждой табуляции — своё (X005TA0425 «\s60;\t120 \s90;\tМана»), расстояниями от предыдущей.
        Assert.Equal([110, 110, 110], parsed.Tabs);
        Assert.Equal([110], parsed.TabStops());
        var mana = UiTextStyle.Parse("\\s60;\\t120 \\s90;\\tМана Жизни\\n");
        Assert.Equal([60, 90], mana.Tabs);
        Assert.Equal([60, 30], mana.TabStops());
        Assert.Empty(UiTextStyle.Parse("Тип:\\tброня").TabStops());   // табуляция без \s — мест нет
    }

    /// <summary>Недописанная директива (нет буквы или не хватает «;») тоже сообщается — целиком и до конца
    /// строки: гадать, где она кончается, не на чем.</summary>
    [Theory]
    [InlineData("Текст\\", "\\")]
    [InlineData("Текст\\hC", "\\hC")]
    [InlineData("Текст\\c000;000", "\\c000;000")]
    public void Truncated_IsReportedWhole(string markup, string reported)
    {
        var parsed = UiTextStyle.Parse(markup);
        Assert.Equal([reported], parsed.Unknown);
        Assert.Equal("Текст", parsed.Text);
    }

    /// <summary>Строки самой игры несут ту же разметку внутри текста, и у части из них есть <c>\n</c> и <c>\t</c> —
    /// без аргумента и без «;». Они превращаются в перевод строки и табуляцию, а не в директиву.</summary>
    [Fact]
    public void NewlineAndTab_AreCharactersNotDirectives()
    {
        var parsed = UiTextStyle.Parse("\\n____\\n\\nОтвергнуто!");
        Assert.Equal("\n____\n\nОтвергнуто!", parsed.Text);
        Assert.Empty(parsed.Unknown);
        Assert.Equal("Тип:\tброня", UiTextStyle.Parse("\\hL;Тип:\\tброня").Text);
    }

    /// <summary>Разметка строки кладётся поверх оформления элемента: что в строке задано — берётся из строки,
    /// остальное остаётся от элемента. Настоящий пример — <c>X005TA0031</c> из русской таблицы.</summary>
    [Fact]
    public void StringMarkup_OverridesElementStyle()
    {
        var element = UiTextStyle.Parse("\\hL;\\vC;\\fMenu;");
        var full = UiTextStyle.Parse("\\c000;000;000;\\hC;\\fLarge;Неудача!", element);

        Assert.Equal("Неудача!", full.Text);
        Assert.Equal(UiHAlign.Center, full.H);        // из строки
        Assert.Equal(UiVAlign.Center, full.V);        // осталось от элемента
        Assert.Equal("Large", full.Font);             // из строки
        Assert.Equal(new UiColor(0, 0, 0), full.Color);
        Assert.Empty(full.Unknown);
        // Текст и неопознанное от основы не наследуются: они про свой исходник.
        Assert.Equal("", element.Text);
    }

    /// <summary>Какое из значений присутствует в строке; ни одного — значение по умолчанию. Двух сразу
    /// в настоящих данных не бывает, и проверка на этом настаивает.</summary>
    private static T Single<T>(string style, T fallback, params (string Marker, T Value)[] options)
    {
        var hits = options.Where(o => style.Contains(o.Marker, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(hits.Count <= 1, $"{style}: сразу несколько значений одной директивы");
        return hits.Count == 1 ? hits[0].Value : fallback;
    }
}
