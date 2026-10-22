using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace D2.Data.Import;

/// <summary>Растровые шрифты игры (<c>Interf/*.mft</c>, семь однобитных растров фиксированного кегля) → BMFont <c>assets/fonts/&lt;имя&gt;.fnt</c> +
/// PNG-атлас <c>&lt;имя&gt;.png</c> (перенос <c>tools/ui-assets/import_fonts.py</c>; <c>.fnt</c> — те же байты, атлас — те же точки). Коды MFT
/// однобайтовые: 0x20…0xFF игры — cp1252, поверх кириллица 0xC0…0xFF слоя русификации (cp1251, без Ё/ё — они рисуются глифами Е и е);
/// слоя нет — шрифт без кириллицы и предупреждение. Формат MFT — <c>docs/ui-text.md</c> §2: «MFT\0», u32 версия (2), u32 длина имени, имя,
/// u32 высота ячейки, u32 межстрочный интервал, u32 ?, байт 0, затем 255 записей по кодам 1…255: u8 есть ли глиф, u32 ширина, u32 высота,
/// u32 продвижение (= ширине), u32 ?, и если глиф есть — растр 1 бит на точку, старший бит слева, строка выровнена на байт.</summary>
public static class FontsImport
{
    public const string Step = "fonts";
    public static readonly string[] Names = ["small", "normal", "medbold", "medium", "menu", "large", "vlarge"];
    private const int AtlasWidth = 256, Pad = 1;   // пустая точка между глифами: фильтр текстуры не захватит соседа

    /// <exception cref="FileNotFoundException">Нет шрифта в <c>Interf</c> игры.</exception>
    /// <exception cref="InvalidDataException">Не MFT, другая версия или ячейка русификации не та.</exception>
    public static void Run(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var missing = Names.Where(n => !File.Exists(ctx.Game("Interf", n + ".mft"))).ToList();
        if (missing.Count > 0) throw new FileNotFoundException($"нет шрифтов в {ctx.Game("Interf")}: {string.Join(", ", missing)}");
        string? l10nDir = ctx.L10n("Interf");
        if (l10nDir is null || !Directory.Exists(l10nDir))
            ctx.Say($"предупреждение: нет слоя русификации {l10nDir ?? "(не задан)"} — шрифты без кириллицы");
        string final = Path.Combine(ctx.AssetsDir, "fonts");
        ImportFiles.Recover(final);
        string built = ImportFiles.Fresh(final + ".new");
        for (int i = 0; i < Names.Length; i++)
        {
            ctx.Cancel.ThrowIfCancellationRequested();
            ctx.Report(Step, i, Names.Length);
            string name = Names[i];
            var face = Parse(ctx.Game("Interf", name + ".mft"));
            string? l10nPath = l10nDir is null ? null : Path.Combine(l10nDir, name + ".mft");
            var l10n = l10nPath is not null && File.Exists(l10nPath) ? Parse(l10nPath) : null;
            var chars = Merge(face, l10n);
            var (fnt, png) = Write(face, chars);
            string stem = face.Name.ToLowerInvariant();
            ImportFiles.WriteText(Path.Combine(built, stem + ".fnt"), fnt);
            ImportFiles.WriteBytes(Path.Combine(built, stem + ".png"), png);
            int cyrillic = chars.Keys.Count(c => c is >= 'Ѐ' and <= 'ӿ');
            ctx.Say($"{name,-8} ячейка {face.Height}+{face.LineSpacing}  глифов {chars.Count} (кириллица {cyrillic}) -> {stem}.fnt");
        }
        ImportFiles.Swap(built, final);
        ctx.Report(Step, Names.Length, Names.Length);
    }

    /// <summary>Глиф MFT: строки точек сверху вниз.</summary>
    internal sealed record Glyph(int Width, int Height, bool[][] Rows);

    internal sealed record Mft(string Name, int Height, int LineSpacing, SortedDictionary<int, Glyph> Glyphs);

    internal static Mft Parse(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 12 || !data.AsSpan(0, 4).SequenceEqual("MFT\0"u8)) throw new InvalidDataException($"{path}: не MFT");
        uint version = U32(data, 4), nameLength = U32(data, 8);
        if (version != 2) throw new InvalidDataException($"{path}: версия {version}, разобрана только 2");
        if (12 + nameLength + 13 > data.Length) throw new InvalidDataException($"{path}: заголовок обрывается");
        var nameBytes = data.AsSpan(12, (int)nameLength);
        if (nameBytes.ContainsAnyInRange((byte)0x80, (byte)0xFF)) throw new InvalidDataException($"{path}: имя шрифта не ASCII");
        string name = Encoding.ASCII.GetString(nameBytes);
        int at = 12 + (int)nameLength;
        int height = (int)U32(data, at), lineSpacing = (int)U32(data, at + 4);
        at += 13;
        var glyphs = new SortedDictionary<int, Glyph>();
        for (int code = 1; code < 256; code++)
        {
            if (at + 17 > data.Length) throw new InvalidDataException($"{path}: запись кода 0x{code:x} обрывается");
            bool present = data[at] != 0;
            int width = (int)U32(data, at + 1), glyphHeight = (int)U32(data, at + 5), advance = (int)U32(data, at + 9);
            at += 17;
            if (!present) continue;
            if (advance != width) throw new InvalidDataException($"{path}: у кода 0x{code:x} продвижение {advance} ≠ ширине {width}");
            int stride = (width + 7) / 8;
            if (at + (stride * glyphHeight) > data.Length) throw new InvalidDataException($"{path}: растр кода 0x{code:x} обрывается");
            var rows = new bool[glyphHeight][];
            for (int y = 0; y < glyphHeight; y++)
            {
                rows[y] = new bool[width];
                for (int x = 0; x < width; x++) rows[y][x] = (data[at + (y * stride) + (x / 8)] & (0x80 >> (x % 8))) != 0;
            }
            at += stride * glyphHeight;
            glyphs[code] = new Glyph(width, glyphHeight, rows);
        }
        if (at != data.Length) throw new InvalidDataException($"{path}: после 255 глифов осталось {data.Length - at} байт");
        return new Mft(name, height, lineSpacing, glyphs);
    }

    /// <summary>Глифы по символам: 0x20…0xFF игры в cp1252, поверх — кириллица 0xC0…0xFF слоя русификации в cp1251, затем Ё/ё.</summary>
    internal static SortedDictionary<char, Glyph> Merge(Mft face, Mft? l10n)
    {
        ArgumentNullException.ThrowIfNull(face);
        var chars = new SortedDictionary<char, Glyph>();
        foreach (var (code, glyph) in face.Glyphs)
            if (Cp1252(code) is { } ch) chars[ch] = glyph;
        if (l10n is null) return chars;
        if ((l10n.Height, l10n.LineSpacing) != (face.Height, face.LineSpacing))
            throw new InvalidDataException($"{face.Name}: ячейка русификации {l10n.Height}+{l10n.LineSpacing} ≠ {face.Height}+{face.LineSpacing}");
        // cp1251 0xC0…0xFF — подряд «А»…«я».
        for (int code = 0xC0; code < 0x100; code++)
            if (l10n.Glyphs.TryGetValue(code, out var glyph)) chars[(char)('А' + (code - 0xC0))] = glyph;
        foreach (var (alias, source) in new[] { ('Ё', 'Е'), ('ё', 'е') })
            if (chars.TryGetValue(source, out var glyph)) chars[alias] = glyph;
        return chars;
    }

    private static readonly Encoding Windows1252 = CodePage(1252);

    /// <summary>Символ кода cp1252; управляющие коды, DEL и пустые места кодовой страницы (0x81, 0x8D, 0x8F, 0x90, 0x9D — у Python там
    /// ошибка разбора) — <c>null</c>.</summary>
    private static char? Cp1252(int code) =>
        code < 0x20 || code == 0x7F || code is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D ? null : Windows1252.GetString([(byte)code])[0];

    private static Encoding CodePage(int page)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(page);
    }

    /// <summary>BMFont (текстовый вариант) и PNG-атлас: глиф белый, покрытие в альфе (0 или 255 — полутонов у MFT нет); глифы по порядку
    /// кодов слева направо строками, между ними <see cref="Pad"/>, высота атласа — степень двойки.</summary>
    internal static (string Fnt, byte[] Png) Write(Mft face, SortedDictionary<char, Glyph> chars)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(chars);
        var places = new Dictionary<char, (int X, int Y)>();
        int x = Pad, y = Pad, rowHeight = 0;
        foreach (var (ch, g) in chars)
        {
            if (x + g.Width + Pad > AtlasWidth) (x, y, rowHeight) = (Pad, y + rowHeight + Pad, 0);
            places[ch] = (x, y);
            x += g.Width + Pad;
            rowHeight = Math.Max(rowHeight, g.Height);
        }
        int atlasHeight = 1;
        while (atlasHeight < y + rowHeight + Pad) atlasHeight *= 2;
        var image = RgbaImage.Filled(AtlasWidth, atlasHeight, 255, 255, 255, 0);
        foreach (var (ch, g) in chars)
        {
            var (gx, gy) = places[ch];
            for (int row = 0; row < g.Height; row++)
                for (int col = 0; col < g.Width; col++)
                    if (g.Rows[row][col]) image.Pixels[((((gy + row) * AtlasWidth) + gx + col) * 4) + 3] = 255;
        }
        string stem = face.Name.ToLowerInvariant();
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"info face=\"{face.Name}\" size={face.Height} bold=0 italic=0 charset=\"\" unicode=1 stretchH=100 smooth=0 aa=1 ")
          .Append(inv, $"padding=0,0,0,0 spacing={Pad},{Pad} outline=0\n");
        // Ячейка как у MFT: строка высотой в ячейку, базовая линия — её низ (глифы стоят от верха ячейки). Межстрочный интервал MFT в шрифт
        // не входит — он в data/ui/fonts.json (Line), как и у TrueType.
        sb.Append(inv, $"common lineHeight={face.Height} base={face.Height} scaleW={AtlasWidth} scaleH={atlasHeight} pages=1 packed=0 ")
          .Append("alphaChnl=0 redChnl=4 greenChnl=4 blueChnl=4\n")
          .Append(inv, $"page id=0 file=\"{stem}.png\"\n")
          .Append(inv, $"chars count={chars.Count}\n");
        foreach (var (ch, g) in chars)
        {
            var (gx, gy) = places[ch];
            sb.Append(inv, $"char id={(int)ch} x={gx} y={gy} width={g.Width} height={g.Height} xoffset=0 yoffset=0 xadvance={g.Width} page=0 chnl=15\n");
        }
        sb.Append("kernings count=0\n");
        return (sb.ToString(), PngCodec.Encode(image));
    }

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
}
