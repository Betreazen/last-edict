using System;
using System.Collections.Generic;
using Godot;

namespace LastEdict.Godot;

/// <summary>Подпись на процессоре — в картинку сборки без окна (<c>--png --text</c>): снимка вьюпорта в headless нет
/// (<c>docs/godot-notes.md</c>), а сверять текст со снимками оригинала надо числом. Вёрстка — та же, что у <see cref="Label"/>: строки
/// режет и формирует TextServer (<see cref="TextParagraph"/>: переносы движка уже в тексте, по словам — как <c>AutowrapMode.Word</c>), каждая строка
/// встаёт на свой подъём, между строками — <see cref="LabelSettings.LineSpacing"/>, выравнивание по ширине и высоте места — как у Label
/// (целые точки). Глифы берутся из кэша шрифта (растр TextServer — те же точки, что идут в текстуру видеокарты) и кладутся
/// без масштаба, в точках разметки 800×600: это вид при масштабе экрана 1.
///
/// <para>Обрезка по месту — прямоугольником <c>clip</c> (подпись выше места, <c>UiScreenView.ClipToBox</c>). Не повторяет: прокрутку
/// (<see cref="Label.LinesSkipped"/>), обводку и тень.</para></summary>
public static class UiTextRaster
{
    /// <summary>Как <c>AutowrapMode.Word</c>: подписи окон переносит сам порт (<c>UiScreenView.Wrapped</c>, правило движка) и отдаёт их
    /// без переноса Godot; по словам (без разрыва слова) — только текст с табуляцией.</summary>
    private const TextServer.LineBreakFlag WordSmart = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound
        | TextServer.LineBreakFlag.TrimStartEdgeSpaces | TextServer.LineBreakFlag.TrimEndEdgeSpaces;

    /// <summary>Нарисовать подпись в <paramref name="canvas"/>, левый верхний угол места — <paramref name="at"/>. Возвращает число точек,
    /// в которые лёг глиф (покрытие больше нуля).</summary>
    public static long Draw(Image canvas, Label label, Vector2I at, Rect2I? clip = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(label);
        if (label.LabelSettings is not { Font: { } font } settings || string.IsNullOrEmpty(label.Text)) return 0;
        var paragraph = new TextParagraph
        {
            Width = label.AutowrapMode == TextServer.AutowrapMode.Off ? -1 : label.Size.X,
            BreakFlags = WordSmart,
        };
        paragraph.AddString(label.Text, font, settings.FontSize);
        int lines = paragraph.GetLineCount();
        float spacing = settings.LineSpacing;
        float total = -spacing;
        for (int i = 0; i < lines; i++) total += paragraph.GetLineSize(i).Y + spacing;

        float y = label.VerticalAlignment switch
        {
            VerticalAlignment.Center => (int)((label.Size.Y - total) / 2),
            VerticalAlignment.Bottom => label.Size.Y - total,
            _ => 0,
        };
        long ink = 0;
        var pages = new Dictionary<(Rid, int, int), Image>();
        for (int i = 0; i < lines; i++)
        {
            Vector2 size = paragraph.GetLineSize(i);
            float x = label.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (int)((label.Size.X - size.X) / 2),
                HorizontalAlignment.Right => label.Size.X - size.X,
                _ => 0,
            };
            float ascent = paragraph.GetLineAscent(i);
            ink += Line(canvas, paragraph.GetLineRid(i), new Vector2(at.X + x, at.Y + y + ascent), settings.FontColor, pages, clip);
            y += size.Y + spacing;
        }
        return ink;
    }

    /// <summary>Глифы одной строки от пера на базовой линии.</summary>
    private static long Line(Image canvas, Rid line, Vector2 pen, Color color, Dictionary<(Rid, int, int), Image> pages, Rect2I? clip)
    {
        TextServer ts = TextServerManager.GetPrimaryInterface();
        long ink = 0;
        foreach (var glyph in ts.ShapedTextGetGlyphs(line))
        {
            var rid = glyph["font_rid"].AsRid();
            int index = glyph["index"].AsInt32(), repeat = Math.Max(1, glyph["repeat"].AsInt32());
            float advance = glyph["advance"].AsSingle();
            Vector2 offset = glyph["offset"].AsVector2();
            for (int r = 0; r < repeat; r++)
            {
                if (rid.IsValid && index != 0)
                    ink += Glyph(canvas, ts, rid, new Vector2I(glyph["font_size"].AsInt32(), 0), index, pen + offset, color, pages, clip);
                pen.X += advance;
            }
        }
        return ink;
    }

    private static long Glyph(Image canvas, TextServer ts, Rid rid, Vector2I size, int index, Vector2 pen, Color color,
                              Dictionary<(Rid, int, int), Image> pages, Rect2I? clip)
    {
        ts.FontRenderGlyph(rid, size, index);   // динамический шрифт растеризует глиф в кэш только по требованию
        int texture = (int)ts.FontGetGlyphTextureIdx(rid, size, index);
        if (texture < 0) return 0;              // пробел: глифа в текстуре нет
        if (!pages.TryGetValue((rid, size.X, texture), out var page))
        {
            page = ts.FontGetTextureImage(rid, size, texture);
            if (page is null) return 0;
            if (page.IsCompressed()) page.Decompress();
            pages[(rid, size.X, texture)] = page;
        }
        Rect2 uv = ts.FontGetGlyphUVRect(rid, size, index);
        Vector2 at = (pen + ts.FontGetGlyphOffset(rid, size, index)).Floor();
        bool alpha = page.GetFormat() is not (Image.Format.L8 or Image.Format.R8);   // полутоновый кэш без альфы — покрытие в яркости
        long ink = 0;
        int w = (int)uv.Size.X, h = (int)uv.Size.Y, u0 = (int)uv.Position.X, v0 = (int)uv.Position.Y;
        for (int gy = 0; gy < h; gy++)
            for (int gx = 0; gx < w; gx++)
            {
                int cx = (int)at.X + gx, cy = (int)at.Y + gy;
                if (cx < 0 || cy < 0 || cx >= canvas.GetWidth() || cy >= canvas.GetHeight() || (clip is { } c && !c.HasPoint(new Vector2I(cx, cy)))) continue;
                Color texel = page.GetPixel(u0 + gx, v0 + gy);
                float cover = (alpha ? texel.A : texel.R) * color.A;
                if (cover <= 0) continue;
                Color under = canvas.GetPixel(cx, cy);
                canvas.SetPixel(cx, cy, new Color(Mix(under.R, color.R, cover), Mix(under.G, color.G, cover), Mix(under.B, color.B, cover),
                                                  Math.Max(under.A, cover)));
                ink++;
            }
        return ink;
    }

    private static float Mix(float under, float over, float cover) => under + ((over - under) * cover);
}
