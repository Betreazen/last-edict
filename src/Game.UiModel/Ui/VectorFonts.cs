namespace LastEdict.Data.Ui;

/// <summary>
/// Векторный текст (ответ пользователя 24.10: «чёткость при любом разрешении»): растровые шрифты игры (<c>.fnt</c>, точки на кегль
/// 12…19) при дробном масштабе окна мылятся, векторный шрифт рисуется в точках окна. Гарнитура — PT Serif (ParaType, SIL OFL 1.1,
/// <c>data/fonts/OFL-PTSerif.txt</c>): книжная антиква с кириллицей, по метрикам близкая к шрифтам игры. Кегль подобран по высоте
/// заглавной (у PT Serif — 0,70 кегля) к высоте заглавной растрового шрифта, ширина строки при этом в пределах 0…10 % от растровой
/// (замер «Hamburgefonts» 24.10); шаг строки (<see cref="UiFont.Line"/>) остаётся оригинальным — строки на своих местах.
/// </summary>
public static class VectorFonts
{
    public const string Regular = "data/fonts/PTSerif-Regular.ttf", Bold = "data/fonts/PTSerif-Bold.ttf", Italic = "data/fonts/PTSerif-Italic.ttf";

    /// <summary>Начертание игры → файл и кегль вектора (заглавная растрового: small 8, normal/medbold/menu 9, medium/large 10, vlarge 14;
    /// medbold — по ширине: при 13 полужирный на 10 % шире).</summary>
    private static readonly IReadOnlyDictionary<string, (string File, int Size)> Faces =
        new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["small"] = (Regular, 11), ["normal"] = (Regular, 13), ["medium"] = (Regular, 14), ["medbold"] = (Bold, 12),
            ["menu"] = (Bold, 13), ["large"] = (Bold, 14), ["vlarge"] = (Italic, 20),
        };

    /// <summary>Файл векторного начертания (для сглаживания MSDF у движка).</summary>
    public static bool IsVector(string file) => file is Regular or Bold or Italic;

    /// <summary>Набор с векторными начертаниями вместо растровых шрифтов игры (импорт, <c>assets/fonts/*.fnt</c>); начертания модов (путь мода
    /// полный, <see cref="UiFonts.Merge"/>) и незнакомые ключи — как есть.</summary>
    public static UiFonts Apply(UiFonts fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        var faces = new Dictionary<string, UiFont>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, face) in fonts.Faces)
        {
            bool bitmap = face.File.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                && (face.File.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase) || face.File.EndsWith(".font", StringComparison.OrdinalIgnoreCase));
            faces[key] = bitmap && Faces.TryGetValue(key, out var vector)
                ? face with { File = vector.File, Size = vector.Size, Fallback = null }
                : face;
        }
        return fonts with { Faces = faces };
    }
}
