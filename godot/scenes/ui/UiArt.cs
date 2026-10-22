using System;
using System.Collections.Generic;
using System.IO;
using D2.Data.Ui;
using Godot;

namespace D2.Godot;

/// <summary>Общий на весь запуск кэш того, из чего собираются экраны разметки (<see cref="UiScreenView"/>): картинки кадров по имени,
/// их текстуры, шрифты по файлу и <see cref="LabelSettings"/> по (шрифт, кегль, цвет). Раньше каждый экран — и каждая карточка по правой
/// кнопке — заново грузил PNG своих кадров и <see cref="FontFile"/>, а у каждой подписи на каждом обновлении был свой новый
/// <see cref="LabelSettings"/> (у нового шрифта и кэш глифов новый — растеризация заново). Строки и набор шрифтов кэширует
/// <see cref="GameData"/> (<see cref="GameData.Strings"/>, <see cref="GameData.Fonts"/>), здесь — только объекты движка.
///
/// <para>Источники: скилл gdscript-knowledge, ui-input.md «Производительность UI» (не пересоздавать то, что не изменилось) и «Текст:
/// Label, LabelSettings, RichTextLabel» (один ресурс <see cref="LabelSettings"/> на много подписей); resources-import.md «Шрифты» (первое
/// появление глифа растеризуется на лету — новый <see cref="FontFile"/> на окно растеризует всё заново) и «Жизненный цикл ресурса и
/// кэш» (кэш по пути: второй экземпляр не грузится).</para>
///
/// <para>Ключ кадра — его имя из разметки: все входы разбора (опись <see cref="GameData.Content"/>, корни графики, слои модов) за запуск
/// не меняются. Картинки и шрифты общие: потребители их не меняют (подкраска, маска, вырезка и зеркало делаются над свежей копией до
/// того, как она попала сюда); общий <see cref="LabelSettings"/> тоже только читается. Сверх <see cref="ImageLimitBytes"/> картинки и
/// текстуры забываются без <c>Dispose</c> — кто ещё держит их (спрайт живого экрана), держит дальше. Все вызовы — из главного потока.</para></summary>
public static class UiArt
{
    /// <summary>Предел кэша картинок по байтам; дальше картинки и текстуры начинаются заново (портреты карточек копятся за партию).</summary>
    public const long ImageLimitBytes = 256L << 20;

    private static readonly Dictionary<string, (Image? Image, string? Problem)> _images = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Image, ImageTexture> _textures = new();
    private static readonly Dictionary<string, FontFile> _faces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string File, int Size, int Line, Color Color), LabelSettings> _settings = new();
    private static long _bytes;

    /// <summary>Забыть картинки и текстуры (шрифты остаются) — замеры «с пустым кэшем» (<c>godot/bench</c>). Без <c>Dispose</c>: кто ещё
    /// держит картинку, держит её дальше.</summary>
    public static void ForgetImages()
    {
        _images.Clear();
        _textures.Clear();
        _bytes = 0;
    }

    /// <summary>Кадров в кэше — для проверок и замеров.</summary>
    public static int Images => _images.Count;

    /// <summary>Картинка кадра, если она уже разбиралась (в том числе «нет такой» — <c>null</c>), и пояснение к пропуску.</summary>
    public static bool TryImage(string frame, out Image? image, out string? problem)
    {
        bool found = _images.TryGetValue(frame, out var entry);
        (image, problem) = entry;
        return found;
    }

    /// <summary>Запомнить разобранную картинку кадра (или её отсутствие с пояснением) и вернуть её.</summary>
    public static Image? Remember(string frame, Image? image, string? problem)
    {
        long size = image?.GetDataSize() ?? 0;
        if (_bytes + size > ImageLimitBytes)
        {
            _images.Clear();     // без Dispose: картинки и текстуры у живых экранов остаются, остальные заберёт сборщик мусора
            _textures.Clear();
            _bytes = 0;
        }
        _images[frame] = (image, problem);
        _bytes += size;
        return image;
    }

    /// <summary>Текстура картинки, одна на картинку.</summary>
    public static ImageTexture Texture(Image image)
    {
        if (!_textures.TryGetValue(image, out var texture))
            _textures[image] = texture = ImageTexture.CreateFromImage(image);
        return texture;
    }

    /// <summary>Шрифт начертания, один <see cref="FontFile"/> на файл за запуск (с ним живёт и его кэш глифов).</summary>
    public static FontFile Face(UiFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (_faces.TryGetValue(font.File, out var cached)) return cached;
        return _faces[font.File] = LoadFace(font);
    }

    /// <summary>Свежий шрифт с диска, мимо кэша (проверка покрытия <c>--ink</c> считает растеризацию с пустого кэша глифов).
    /// <c>FontFile.LoadBitmapFont</c>/<c>LoadDynamicFont</c> берут обычный путь и не проходят импорт ресурсов Godot: шрифты лежат в данных
    /// и графике порта, а не в проекте движка. Нет файла основного — шрифтом становится запасной (<see cref="UiFont.Fallback"/>, с сообщением:
    /// графика не собрана). В цепочку <c>Fallbacks</c> растрового шрифта запасной не ставится: высоту и подъём шрифта Godot берёт наибольшие
    /// по всей цепочке (замер <c>--lines</c>: у Normal высота 18 вместо 13, подъём 14 вместо 13 — строки съехали бы с мест оригинала). Символ,
    /// которого нет ни в cp1252, ни в кириллице игры, дорисует системный шрифт (<c>allow_system_fallback</c>, по умолчанию включён).</summary>
    public static FontFile LoadFace(UiFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        string path = FontPath(font.File);
        if (!File.Exists(path) && font.Fallback is { } spare)
        {
            GD.PrintErr($"шрифт {font.File}: нет файла {path} — рисуется запасным {spare} (python tools/ui-assets/import_fonts.py)");
            return LoadFile(spare);
        }
        return LoadFile(font.File);
    }

    /// <summary>Растровый шрифт (BMFont) — по расширению.</summary>
    public static bool IsBitmap(string file) =>
        file.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".font", StringComparison.OrdinalIgnoreCase);

    /// <summary>Путь к файлу шрифта: <c>assets/…</c> — от корня графики (<see cref="GameData.AssetsRoot"/>, переменная <c>D2_ASSETS_DIR</c>),
    /// иначе — от корня данных (у мода путь уже полный).</summary>
    public static string FontPath(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        const string assets = "assets/";
        string normal = file.Replace(Path.DirectorySeparatorChar, '/');
        return normal.StartsWith(assets, StringComparison.OrdinalIgnoreCase) && GameData.AssetsRoot.Length > 0
            ? Path.GetFullPath(Path.Combine(GameData.AssetsRoot, normal[assets.Length..]))
            : GameData.FromRoot(file);
    }

    private static FontFile LoadFile(string file)
    {
        var font = new FontFile();
        string path = FontPath(file);
        if (IsBitmap(file))
        {
            Error bitmap = font.LoadBitmapFont(path);
            if (bitmap != Error.Ok) throw new IOException($"шрифт {file}: {bitmap}");
            // Однобитный растр игры: точки глифа — на целые точки, без сдвига на доли (иначе фильтр текстуры размажет край).
            font.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            return font;
        }
        Error error = font.LoadDynamicFont(path);
        if (error != Error.Ok) throw new IOException($"шрифт {file}: {error}");
        return font;
    }

    /// <summary>Оформление подписи (шрифт, кегль, цвет, шаг строки), один ресурс на сочетание. Только для чтения: правка общего ресурса
    /// сменила бы вид всех подписей с тем же сочетанием.</summary>
    public static LabelSettings Settings(UiFont font, Color color)
    {
        ArgumentNullException.ThrowIfNull(font);
        var key = (font.File, font.Size, font.Line, color);
        if (_settings.TryGetValue(key, out var cached)) return cached;
        var face = Face(font);
        return _settings[key] = new LabelSettings
        {
            Font = face, FontSize = font.Size, FontColor = color, LineSpacing = LineSpacing(face, font),
        };
    }

    private static readonly Dictionary<(string File, int Size, Color Color, Color Outline), LabelSettings> _outlined = new();

    /// <summary>Подпись с обводкой в точку, как у движка (надписи на портретах боя, <c>X008TA0028</c>: белым с чёрной обводкой): текст
    /// ещё восемь раз цветом обводки со сдвигом на точку по восьми направлениям — стопка теней <see cref="LabelSettings"/>. Обводку
    /// FreeType (<c>outline_size</c>) растровый шрифт не умеет вовсе, а тени рисуются тем же глифом. Один ресурс на сочетание.</summary>
    public static LabelSettings Outlined(UiFont font, Color color, Color outline)
    {
        ArgumentNullException.ThrowIfNull(font);
        var key = (font.File, font.Size, color, outline);
        if (_outlined.TryGetValue(key, out var cached)) return cached;
        var settings = new LabelSettings { Font = Face(font), FontSize = font.Size, FontColor = color };
        int i = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                settings.AddStackedShadow();
                settings.SetStackedShadowOffset(i, new Vector2(dx, dy));
                settings.SetStackedShadowColor(i, outline);
                settings.SetStackedShadowOutlineSize(i, 0);
                i++;
            }
        return _outlined[key] = settings;
    }

    /// <summary>Шаг строки начертания в точках разметки: шаг оригинала (<see cref="UiFont.Line"/>), без него — высота шрифта.</summary>
    public static int LineStep(UiFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return font.Line > 0 ? font.Line : (int)Mathf.Ceil(Face(font).GetHeight(font.Size));
    }

    /// <summary>Подпись — в место <paramref name="box"/> (координаты родителя) по вертикальному правилу движка (<see cref="UiTextBlock"/>,
    /// <c>0x52ad04</c>), а не Godot: подпись выравнивается по верху, а верх блока строк считает порт. Блок выше места — от верха, и подпись
    /// вытягивается на все строки (движок рисует их все в картинку текста, а её режет место элемента — режет вызывающий: окна —
    /// <c>UiScreenView.ClipToBox</c>). Звать после того, как подписи заданы текст и оформление и она стоит в дереве
    /// (иначе Godot не свёрстывает строки и <see cref="Label.GetLineCount"/> отвечает 1).
    ///
    /// <para>Размер ставится каждый раз заново: подпись, созданная без оформления, вырастает по шрифту темы (16, высота 23) — Godot не даёт
    /// элементу быть ниже своего наименьшего размера и обратно его не уменьшает. Так было с плашками построек (16 точек): блок стоял
    /// по центру 23 точек, на 5 ниже — подписи на нижней кромке (снимок 43 прогона 3 от 11.10).</para></summary>
    public static int Place(Label label, Rect2 box, UiVAlign v, int step)
    {
        ArgumentNullException.ThrowIfNull(label);
        label.VerticalAlignment = VerticalAlignment.Top;
        label.Size = new Vector2(box.Size.X, box.Size.Y);   // ширина — до счёта строк: перенос по ширине места
        int lines = label.Text.Length == 0 ? 0 : label.GetLineCount();
        int top = UiTextBlock.Top((int)box.Size.Y, lines, step, v);
        label.Position = box.Position + new Vector2(0, top);
        label.Size = new Vector2(box.Size.X, Math.Max(box.Size.Y - top, UiTextBlock.Height(lines, step)));
        return top;
    }

    /// <summary>Межстрочный интервал подписи, при котором шаг строки — как у растрового шрифта оригинала (<see cref="UiFont.Line"/>):
    /// Godot ставит строки через высоту шрифта (подъём + спуск) плюс интервал. У растрового шрифта игры высота — ячейка MFT, интервал —
    /// его межстрочный (у Normal 17 − 13 = 4); у EB Garamond высота — 1,305 кегля, интервал отрицательный. Шаг не задан — интервал 0.</summary>
    public static float LineSpacing(Font face, UiFont font)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(font);
        return font.Line > 0 ? font.Line - face.GetHeight(font.Size) : 0f;
    }
}
