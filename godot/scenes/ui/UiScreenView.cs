using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Первый настоящий экран порта: берёт <see cref="UiScreen"/> из <see cref="GameData.Content"/>
/// (базовые <c>data/ui</c> плюс наложенные моды) и рисует его кадрами из <c>assets/ui</c>, а надписи — строками
/// из <c>data/ui/strings.&lt;язык&gt;.json</c> шрифтами из <c>data/ui/fonts.json</c> (тоже база плюс моды: <see cref="GameData.Strings"/>,
/// <see cref="GameData.Fonts"/>). Разметка своя, данными, — форматов старого движка сцена не знает вовсе. Строки, шрифты, кадры и их
/// текстуры общие на весь запуск (<see cref="UiArt"/>): новое окно их не грузит заново.
///
/// С окном (<c>--show</c>) экран строится узлами: кадр — <c>Sprite2D</c>, надпись — <c>Label</c> с разобранным
/// оформлением (выравнивание, шрифт, цвет из <see cref="UiTextStyle"/>). Без окна
/// (<c>--headless -- --screen DLG_X --png out.png</c>) те же кадры складываются в <c>Image</c> на стороне
/// процессора и пишутся в PNG: в headless у Godot пустой драйвер отрисовки и снимок вьюпорта там не берётся,
/// так что проверка считает не экран, а собранную картинку. Надписи в эту картинку не попадают — текст считается
/// числами в той же строке итога.
///
/// Печатает одну строку: экран, сколько кадров нарисовано, сколько пропущено (нет в описи графики), доля
/// оставшихся маджентовых точек (обязана быть 0), доля непрозрачных (0 — картинка пустая, значит не собралось),
/// сколько элементов ссылается на строку локализации, скольким строка нашлась, скольким нет (и каким именно),
/// сколько надписей нарисовано и какие директивы оформления остались непонятыми.
/// Выход 1, если кадра не нашлось или маджента осталась; за строки и оформление отвечает
/// <c>tools/ui-render-check.py</c> — у него список заведомо отсутствующих идентификаторов.
///
/// Отдельный режим <c>--ink</c> — проверка шрифта: пишет образец (по умолчанию русский) подписью в картинку на процессоре и печатает
/// число закрашенных точек пустой подписи и образца. Нужен, потому что снимка экрана в headless нет, а покрытие кириллицы проверять
/// чем-то надо. С <c>--png … --text</c> подписи экрана ложатся в картинку сборки тем же способом (<see cref="UiTextRaster"/>).
///
/// Элементы живые: состояние экрана ведёт <see cref="UiScreenState"/> (обычный C#, проверяется
/// <c>dotnet test</c>), сцена только передаёт ему ввод и меняет кадры — см. <c>UiScreenView.Input.cs</c>.</summary>
public partial class UiScreenView : Node2D
{
    /// <summary>Кадр к отрисовке: чей он, картинка и её левый верхний угол в координатах <c>Design</c>.</summary>
    private readonly record struct Frame(string Id, Image Image, Vector2I At, bool Additive = false);

    /// <summary>Надпись к отрисовке: элемент, разобранное оформление и готовый текст.</summary>
    private readonly record struct Caption(UiElement Element, UiTextStyle Style, string Text);

    /// <summary>Итог по тексту экрана: сколько элементов ссылается на строку, скольким она нашлась,
    /// каким не нашлась, сколько надписей рисуется и какие директивы остались непонятыми.</summary>
    private readonly record struct TextStats(int Ids, int Resolved, IReadOnlyList<string> Missing,
                                             int Drawn, IReadOnlyList<string> Unknown);

    private Node2D? _root;
    private Vector2 _design = new(800, 600);
    private UiFonts _fonts = new();
    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    /// <summary>Экран, который показывает хозяин (<see cref="UiFlowView"/>); задаётся до добавления в дерево.
    /// <c>null</c> — сцена запущена сама и берёт экран из аргументов.</summary>
    public string? Hosted { get; set; }

    /// <summary>Место экрана-панели на экране хозяина: левый верхний угол в точках хозяина размером
    /// <see cref="SlotHost"/> (так игра вкладывает <c>DLG_ISO_PAL</c> и <c>DLG_STRATEGIC</c> в места <c>DLG_ISO_VIEW</c>).
    /// null — экран вписывается во всё окно сам.</summary>
    public Vector2? SlotAt { get; set; }

    /// <summary>Размер экрана хозяина, в который вписывается панель (<see cref="SlotAt"/>).</summary>
    public Vector2 SlotHost { get; set; } = new(800, 600);

    /// <summary>Язык строк; хозяин передаёт свой.</summary>
    public string Language { get; set; } = GameData.Language;

    /// <summary>События элементов экрана — хозяин переводит их в переходы по графу.</summary>
    /// <summary>Все события одного нажатия — разом и по порядку: хозяину нужно знать, где кончается нажатие
    /// (после смены экрана остаток нажатия к новому экрану не относится).</summary>
    public event Action<IReadOnlyList<UiEvent>>? Fired;

    public UiScreenState State => _state;

    /// <summary>Под экраном, который показывает хозяин: кадры, надписи, начальные значения — без проверок
    /// и без выхода. Пропущенные кадры печатаются, но экран всё равно показывается.</summary>
    private void HostedReady(string id)
    {
        if (Load(id, Language, null) is not { } loaded) return;
        if (loaded.Skipped.Count > 0) GD.PrintErr($"{id}: нет кадров: {string.Join(", ", loaded.Skipped)}");
        OpenFailed = !Guarded(() => Logic?.Open(_state), "открытие");
        Refresh();
    }

    private sealed record Loaded(UiScreen Screen, List<Frame> Frames, List<string> Skipped, TextStats Text);

    /// <summary>Экран из <see cref="GameData.Content"/>: состояние с начальными значениями, кадры, надписи, узлы.
    /// <c>null</c> — экран не нашёлся или данные не читаются (причина напечатана, процесс завершается с кодом 1).</summary>
    private Loaded? Load(string id, string language, string? items, string? state = null)
    {
        UiScreen screen;
        IReadOnlyDictionary<string, UiAsset> assets;
        try
        {
            // Экраны и кадры — из GameData: базовый набор data/ плюс наложенные моды (godot/autoload/GameData.cs).
            var content = GameData.Content;
            var found = content.Screens.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (found is null) { Fail($"нет экрана {id} (в наборе {content.Screens.Count})"); return null; }
            // Окно игры может достроить экран своими элементами (ячейки групп юнитов — их нет в разметке).
            screen = Logic?.Shape(found) ?? found;
            assets = content.AssetsByName;
            _fonts = GameData.Fonts;
            _strings = GameData.Strings(language);
            _state = new UiScreenState(screen);
            _state.Apply(content.Defaults.For(screen.Id));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException
                                      or System.Text.Json.JsonException or KeyNotFoundException)
        {
            Fail($"{e.GetType().Name}: {e.Message}");
            return null;
        }

        _assets = assets;
        if (!Configure(screen, items)) { GetTree().Quit(1); return null; }
        if (state is not null && !ApplyShotState(state)) return null;
        var frames = new List<Frame>();
        var skipped = new List<string>();
        Collect(screen, frames, skipped);

        var captions = new List<Caption>();
        TextStats text = CollectText(screen, captions);

        _design = new Vector2(screen.Design.Width, screen.Design.Height);
        BuildNodes(screen, frames);
        return new Loaded(screen, frames, skipped, text);
    }

    public override void _Ready()
    {
        if (Hosted is { } hosted) { HostedReady(hosted); return; }

        string[] args = OS.GetCmdlineUserArgs();
        if (args.Contains("--ink")) { InkCheck(args); return; }
        if (args.Contains("--lines")) { LinesCheck(); return; }
        if (args.Contains("--tabs")) { TabsCheck(); return; }

        string id = ArgValue(args, "--screen") ?? "DLG_ACTION_RESULT";
        string? png = ArgValue(args, "--png");
        string language = GameData.Language;

        if (Load(id, language, ArgValue(args, "--items"), ArgValue(args, "--state")) is not { } loaded) return;
        var (screen, frames, skipped, text) = (loaded.Screen, loaded.Frames, loaded.Skipped, loaded.Text);

        Image canvas = Compose(screen, frames);
        if (args.Contains("--text")) DrawTexts(canvas);
        if (png is not null) canvas.SavePng(ResolvePath(png));
        var (magenta, opaque) = Shares(canvas);

        // Точка, а не запятая: строку разбирает tools/ui-render-check.py, и русская локаль ему не нужна.
        string Num(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);
        GD.Print($"screen={screen.Id} drawn={frames.Count} skipped={skipped.Count} " +
                 $"magenta={Num(magenta, "F6")} opaque={Num(opaque, "F4")} lang={language} " +
                 $"textids={text.Ids} strings={text.Resolved} nostring={text.Ids - text.Resolved} " +
                 $"captions={text.Drawn} missing={List(text.Missing)} unknown={List(text.Unknown)}");
        if (skipped.Count > 0) GD.PrintErr($"нет кадров: {string.Join(", ", skipped)}");

        if (skipped.Count > 0 || magenta > 0) { GetTree().Quit(1); return; }
        string? input = ArgValue(args, "--input");
        ManualTime |= input is not null;   // сценарий ввода ведёт время сам (шаг «wait N»)
        if (input is not null && !RunInput(input)) { GetTree().Quit(1); return; }
        if (!args.Contains("--show")) GetTree().Quit();
    }

    /// <summary>Состояние со снимка оригинала (<c>--state</c>, <c>tools/pixel-states</c>): флажки, счётчики, спрятанные кнопки, подложка —
    /// для попиксельной сверки; кладётся после <see cref="Configure"/>, который счётчики сбрасывает.</summary>
    private bool ApplyShotState(string path)
    {
        try
        {
            UiShotState.Load(ResolvePath(path)).ApplyTo(_state);
            return true;
        }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            Fail($"--state {path}: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>Два слоя на общем холсте наибольшего размера, каждый по центру (сдвиг — (холст − слой) / 2 с отбрасыванием к нулю, как
    /// <c>0x5b6782</c>), верхний — по альфе. Нет любого слоя — null: рамка без портрета или портрет без рамки — уже не то, что у движка.</summary>
    private static Image? Layered(Image? under, Image? over)
    {
        if (under is null || over is null) return null;
        int w = Math.Max(under.GetWidth(), over.GetWidth()), h = Math.Max(under.GetHeight(), over.GetHeight());
        var canvas = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        foreach (var layer in (Image[])[under, over])
        {
            var src = layer.GetFormat() == Image.Format.Rgba8 ? layer : Converted(layer);
            canvas.BlendRect(src, new Rect2I(Vector2I.Zero, src.GetSize()), new Vector2I((w - src.GetWidth()) / 2, (h - src.GetHeight()) / 2));
        }
        return canvas;
    }

    /// <summary>Копия картинки в RGBA8 (для вырезки из кадра другого формата).</summary>
    private static Image Converted(Image source)
    {
        var copy = (Image)source.Duplicate();
        copy.Convert(Image.Format.Rgba8);
        return copy;
    }

    /// <summary>Список для разбора скриптом: без пробелов, пусто — прочерк.</summary>
    private static string List(IReadOnlyList<string> items) => items.Count == 0 ? "-" : string.Join(",", items);

    /// <summary>Сцена, запущенная сама, — проверка: ошибка завершает процесс с кодом 1. Экран у хозяина — часть
    /// игры: ошибка печатается, экран остаётся пустым, а игра идёт дальше.</summary>
    private void Fail(string message)
    {
        GD.PrintErr($"UiScreenView{(Hosted is null ? "" : $" {Hosted}")}: {message}");
        if (Hosted is null) GetTree().Quit(1);
    }

    /// <summary>Что рисовать: фон экрана и по кадру на элемент — тот, что <see cref="UiScreenState.Frame"/>
    /// даёт в начальном состоянии (картинка, обычная кнопка, отмеченный первый участник группы). Элемент без кадра —
    /// не пропуск, а просто нечего рисовать (подпись, список, поле ввода); пропуск — это ссылка на кадр, которого
    /// нет в описи или на диске.</summary>
    private void Collect(UiScreen screen, List<Frame> frames, List<string> skipped)
    {
        Vector2I origin = Origin(screen);
        Add(UnderlayId, _state.Underlay, origin);
        Add("BACKGROUND", _state.Background, origin);
        foreach (var element in screen.Elements)
            Add(element.Id, _state.Frame(element.Id), new Vector2I(element.Rect.X, element.Rect.Y), element);

        void Add(string id, string? frame, Vector2I at, UiElement? element = null)
        {
            if (string.IsNullOrEmpty(frame)) return;
            Image? image = FrameImage(frame, out string? problem);
            if (image is null)
            {
                skipped.Add($"{id}:{frame}{problem}");
                return;
            }
            if (element is { Kind: UiKind.Image })
            {
                var (dx, dy) = UiImagePlacement.Offset(_state.ImageAlign(id), element.Rect.Width, element.Rect.Height,
                                                       image.GetWidth(), image.GetHeight());
                at += new Vector2I(dx, dy);
            }
            frames.Add(new Frame(id, image, at - origin, frame[0] == UiFrameRef.Add));
        }
    }

    /// <summary>Картинка кадра по имени из описи, по одному разу на имя за запуск (<see cref="UiArt"/>). <paramref name="problem"/> — пояснение
    /// к пропуску (нет файла на диске), запомненное вместе с картинкой; у кадра, которого нет в описи, пояснения нет.</summary>
    private Image? FrameImage(string frame, out string? problem)
    {
        if (UiArt.TryImage(frame, out var cached, out problem)) return cached;
        problem = null;
        Image? image = null;
        // Кадр из слоёв (UiFrameRef.Layer): верхний по центру поверх нижнего на холсте наибольшего размера — портрет в рамке карточек.
        if (UiFrameRef.Layers(frame) is { } layers)
        {
            image = Layered(FrameImage(layers.Under, out string? under), FrameImage(layers.Over, out string? over));
            problem = image is null ? under ?? over ?? $" (нет слоя {frame})" : null;
            return UiArt.Remember(frame, image, problem);
        }
        // Кадр со сложением (UiFrameRef.Add) — та же картинка; смешение делает спрайт или сборка без окна.
        if (frame.Length > 1 && frame[0] == UiFrameRef.Add)
        {
            image = FrameImage(frame[1..], out problem);
            return UiArt.Remember(frame, image, problem);
        }
        // Вырезка (UiFrameRef.Crop): часть кадра, за краем — прозрачно.
        if (UiFrameRef.Crop(frame) is { } crop)
        {
            var (whole, region) = crop;
            if (FrameImage(whole, out problem) is { } full)
            {
                image = Image.CreateEmpty(region.Width, region.Height, false, Image.Format.Rgba8);
                var src = new Rect2I(region.X, region.Y, region.Width, region.Height).Intersection(new Rect2I(Vector2I.Zero, full.GetSize()));
                if (src.HasArea())
                    image.BlitRect(full.GetFormat() == Image.Format.Rgba8 ? full : Converted(full), src,
                                   src.Position - new Vector2I(region.X, region.Y));
            }
            return UiArt.Remember(frame, image, problem);
        }
        // Зеркальный кадр (UiFrameRef.Mirror) — копия обычного, отражённая слева направо.
        if (frame.Length > 1 && frame[^1] == UiFrameRef.Mirror)
        {
            if (FrameImage(frame[..^1], out problem) is { } plain)
            {
                image = (Image)plain.Duplicate();
                image.FlipX();
            }
            return UiArt.Remember(frame, image, problem);
        }
        // Кадр графики мира (gfx:Архив/Кадр) — картинка события из Events и т. п.: прозрачность уже запечена.
        if (frame.StartsWith("gfx:", StringComparison.Ordinal) && frame[4..].Split('/', 2) is [var archive, var name])
        {
            if (GameData.Content.Gfx.Image(archive, name) is { } info && LastEdict.Data.Gfx.GfxAssets.PathOf(info, GameData.GfxRoot) is { } world
                && File.Exists(world))
                image = GfxTextures.Canvas(world);
            else problem = $" (нет {frame} в описи графики или на диске)";
            return UiArt.Remember(frame, image, problem);
        }
        if (GfxImage(frame) is { } gfx)
        {
            problem = gfx.Problem;
            return UiArt.Remember(frame, gfx.Image, problem);
        }
        // Анимация: экран ссылается на неё целиком, кадры лежат под «анимация/номер» — берём первый.
        var assets = GameData.Content.AssetsByName;
        if (assets.TryGetValue(frame, out var asset) || assets.TryGetValue($"{frame}/000", out asset))
        {
            string path = UiAssets.PathOf(asset, GameData.UiAssetsRoot);
            if (File.Exists(path)) image = Keyed(path);
            else problem = $" (нет {path}; python tools/ui-assets/build_ui_assets.py)";
        }
        return UiArt.Remember(frame, image, problem);
    }

    /// <summary>Кадр из графики игры: имя вида <c>архив:картинка</c> (<c>Faces:G000UU0001FACE</c> — портрет, <c>City:ALD0</c>)
    /// берётся из описи <c>data/gfx</c>, прозрачность в этих PNG уже запечена. <c>null</c> — имя не такого вида.</summary>
    private static (Image? Image, string? Problem)? GfxImage(string frame)
    {
        if (UiFrameRef.Parse(frame) is not { } reference) return null;
        if (reference.Archive == "Outline") return (Outline(reference), null);
        var info = GameData.Content.Gfx.Image(reference.Archive, reference.Name);
        if (info is null) return (null, null);
        string path = LastEdict.Data.Gfx.GfxAssets.PathOf(info, GameData.GfxRoot);
        if (!File.Exists(path)) return (null, $" (нет {path}; python tools/gfx-assets)");
        var image = GfxTextures.Canvas(path);
        if (reference.Tint is { } tint) Tint(image, tint, reference.FromPercent);
        if (reference.Mask is { } mask && GameData.Content.Gfx.Image(reference.Archive, mask) is { } maskInfo
            && LastEdict.Data.Gfx.GfxAssets.PathOf(maskInfo, GameData.GfxRoot) is var maskPath && File.Exists(maskPath))
            Blend(image, GfxTextures.Canvas(maskPath));
        return (image, null);
    }

    /// <summary>Контур прямоугольника в 1 точку (<c>Outline:ШxВ#RRGGBB</c>): так движок рисует рамку без кадра
    /// (<c>CImage2Outline</c>, <c>0x53a2fb</c>).</summary>
    private static Image? Outline(UiFrameRef reference)
    {
        var size = reference.Name.Split('x');
        if (size.Length != 2 || !int.TryParse(size[0], out int w) || !int.TryParse(size[1], out int h) || w < 2 || h < 2) return null;
        var c = reference.Tint ?? new UiColor(225, 225, 0);
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var color = Color.Color8(c.R, c.G, c.B);
        for (int x = 0; x < w; x++) { image.SetPixel(x, 0, color); image.SetPixel(x, h - 1, color); }
        for (int y = 0; y < h; y++) { image.SetPixel(0, y, color); image.SetPixel(w - 1, y, color); }
        return image;
    }

    /// <summary>Маска поверх кадра (<see cref="UiFrameRef.Mask"/>): точки смешиваются наполовину по всей картинке, прозрачность —
    /// у кадра (павший воин: <c>MASKDEADS</c>/<c>MASKDEADL</c>, <c>0x5b1698</c>).</summary>
    private static void Blend(Image image, Image mask)
    {
        image.Convert(Image.Format.Rgba8);
        mask.Convert(Image.Format.Rgba8);
        int w = Math.Min(image.GetWidth(), mask.GetWidth()), h = Math.Min(image.GetHeight(), mask.GetHeight());
        byte[] data = image.GetData(), over = mask.GetData();
        int iw = image.GetWidth(), mw = mask.GetWidth();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = ((y * iw) + x) * 4, m = ((y * mw) + x) * 4;
                if (data[i + 3] == 0) continue;
                for (int k = 0; k < 3; k++) data[i + k] = UiFrameRef.Blend(data[i + k], over[m + k]);
            }
        image.SetData(iw, image.GetHeight(), false, Image.Format.Rgba8, data);
    }

    /// <summary>Подкраска кадра (<see cref="UiFrameRef"/>): непрозрачные точки строк ниже доли высоты — наполовину цветом.</summary>
    private static void Tint(Image image, UiColor tint, int fromPercent)
    {
        image.Convert(Image.Format.Rgba8);
        byte[] data = image.GetData();
        int width = image.GetWidth(), from = image.GetHeight() * fromPercent / 100;
        for (int i = from * width * 4; i + 3 < data.Length; i += 4)
        {
            if (data[i + 3] == 0) continue;
            data[i] = UiFrameRef.Blend(data[i], tint.R);
            data[i + 1] = UiFrameRef.Blend(data[i + 1], tint.G);
            data[i + 2] = UiFrameRef.Blend(data[i + 2], tint.B);
        }
        image.SetData(width, image.GetHeight(), false, Image.Format.Rgba8, data);
    }

    /// <summary>Что писать: строка ищется по <see cref="UiElement.TextId"/> в таблице языка, оформление —
    /// из <see cref="UiElement.Style"/>, а поверх него из самой строки (в <c>TApp.dbf</c> разметка встречается
    /// внутри текста: <c>"\c000;000;000;\hC;\fLarge;Неудача!"</c>).
    ///
    /// <para>На месте рисуются только подписи (<see cref="UiKind.Text"/>) и счётчики
    /// (<see cref="UiKind.Spin"/>): у кнопок, переключателей и картинок в том же поле лежит
    /// <b>подсказка</b>, а не надпись — её показывают при наведении, и экран ею не подписывают.
    /// В счёт строк подсказки идут наравне: искать их всё равно в той же таблице.</para>
    ///
    /// <para>Непонятые директивы не теряются: и от оформления элемента, и от разметки внутри строки они
    /// складываются в один список, туда же попадает ссылка на шрифт, которого нет в <c>fonts.json</c>.</para></summary>
    private TextStats CollectText(UiScreen screen, List<Caption> captions)
    {
        var missing = new List<string>();
        var unknown = new List<string>();
        int ids = 0, resolved = 0;

        foreach (var element in screen.Elements)
        {
            var style = UiTextStyle.Parse(element.Style);
            Note(style);

            if (string.IsNullOrEmpty(element.TextId)) continue;
            ids++;
            if (!_strings.TryGetValue(element.TextId, out string? raw)) { missing.Add(element.TextId); continue; }
            resolved++;

            if (element.Kind is not (UiKind.Text or UiKind.Spin)) continue;
            var full = UiTextStyle.Parse(raw, style);
            Note(full);
            if (full.Text.Length > 0) captions.Add(new Caption(element, full, full.Text));
        }

        return new TextStats(ids, resolved, missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                             captions.Count, unknown.Distinct(StringComparer.Ordinal).ToList());

        void Note(UiTextStyle style)
        {
            unknown.AddRange(style.Unknown);
            if (!_fonts.Knows(style.Font)) unknown.Add($"\\f{style.Font};");
        }
    }

    /// <summary>Кадр интерфейса. Пурпурный ключ игры (255,0,255) вырезан при сборке (<c>tools/ui-assets/build_ui_assets.py</c>
    /// запекает альфу, 27.09) — здесь только загрузка в RGBA. Проверка экрана по-прежнему считает оставшийся пурпур
    /// (<see cref="Shares"/>): незапечённая распаковка видна сразу.</summary>
    private static Image Keyed(string path)
    {
        Image image = Image.LoadFromFile(path);
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>Узлы экрана по порядку отрисовки: фон, картинки и кнопки, ролики, надписи и строки списков
    /// (<c>UiScreenView.Content.cs</c>). Надписи — все, а не только найденные: пустую подпись может заполнить логика.</summary>
    private void BuildNodes(UiScreen screen, IReadOnlyList<Frame> frames)
    {
        var root = new Node2D { Name = "Design" };
        AddChild(root);
        BuildSprites(root, screen, frames);
        BuildTexts(root, screen);
        BuildCards(root, screen);

        _root = root;
        RefreshContent();
        Rescale();
        GetViewport().SizeChanged += Rescale;
        _sizeHooked = true;
    }

    private bool _sizeHooked;

    /// <summary>Экран, снятый хозяином и поставленный снова (панель команд под панелью столицы), опять следит за окном.</summary>
    public override void _EnterTree()
    {
        if (_root is null || _sizeHooked) return;
        GetViewport().SizeChanged += Rescale;
        _sizeHooked = true;
        Rescale();
    }

    /// <summary>Экран, снятый хозяином, больше не следит за размером окна.</summary>
    public override void _ExitTree()
    {
        if (!_sizeHooked) return;
        GetViewport().SizeChanged -= Rescale;
        _sizeHooked = false;
    }

    /// <summary>Шрифт начертания — общий на запуск (<see cref="UiArt.Face"/>).</summary>
    private static FontFile Face(UiFont font) => UiArt.Face(font);

    /// <summary>Проверка покрытия: образец пишется подписью с оформлением экранов (<see cref="UiArt.Settings"/>) в картинку на процессоре
    /// (<see cref="UiTextRaster"/> — глифы из кэша шрифта, тот же растр, что уходит в видеокарту), и считается, сколько точек закрашено.
    /// Снимка экрана в headless нет (пустой драйвер отрисовки), а глифы растеризует TextServer на процессоре (FreeType у TrueType,
    /// атлас у растрового) — поэтому числа получаются и без окна.
    ///
    /// <para>Печатает <c>ink0</c> (пустая подпись) и <c>ink1</c> (образец). Пустой шрифт дал бы <c>ink1 == ink0</c>, а символ, которого нет в
    /// основном шрифте начертания (запасной его дорисует, но уже другим начертанием), — <c>nochar &gt; 0</c>; и то и другое — выход 1.</para></summary>
    private void InkCheck(string[] args)
    {
        string sample = ArgValue(args, "--text") ?? "Русский текст";
        UiFonts fonts;
        try
        {
            fonts = GameData.Fonts;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException)
        {
            Fail($"{e.GetType().Name}: {e.Message}");
            return;
        }

        UiFont face = fonts.Resolve(ArgValue(args, "--font"));
        char[] chars = sample.Where(c => !char.IsWhiteSpace(c)).Distinct().ToArray();
        FontFile font = UiArt.LoadFace(face);
        char[] absent = chars.Where(c => !font.HasChar(c)).ToArray();
        long ink0 = Ink(face, "");
        long ink1 = Ink(face, sample);

        GD.Print($"ink font={face.File} size={face.Size} chars={chars.Length} nochar={absent.Length} " +
                 $"ink0={ink0} ink1={ink1}");
        if (absent.Length > 0) GD.PrintErr($"нет глифов: {new string(absent)}");
        GetTree().Quit(absent.Length > 0 || ink1 <= ink0 ? 1 : 0);
    }

    /// <summary>Закрашенные точки подписи <paramref name="text"/> начертанием <paramref name="face"/> — картинка на процессоре.</summary>
    private long Ink(UiFont face, string text)
    {
        var label = new Label { Text = text, LabelSettings = UiArt.Settings(face, Colors.Black), Size = new Vector2(800, 100) };
        AddChild(label);
        long ink = UiTextRaster.Draw(Image.CreateEmpty(800, 100, false, Image.Format.Rgba8), label, Vector2I.Zero);
        label.QueueFree();
        return ink;
    }

    /// <summary>Design → окно: равномерный масштаб по меньшей стороне и центрирование, так что пропорции
    /// экрана не плывут.
    ///
    /// Якоря (<see cref="UiAnchor"/>) здесь пока не нужны — экран тянется целиком. Когда понадобятся (окно шире
    /// Design и элементы должны разъезжаться к краям, а не растягиваться вместе с фоном), точка вставки эта:
    /// масштаб оставить 1, а каждому узлу прибавить смещение края — (окно − Design), умноженное на долю якоря
    /// по каждой оси: 0 у Left/Top, 0.5 у Center, 1 у Right/Bottom.</summary>
    private void Rescale()
    {
        if (_root is null) return;
        Vector2 view = GetViewportRect().Size;
        if (SlotAt is { } at)
        {
            float h = Mathf.Min(view.X / SlotHost.X, view.Y / SlotHost.Y);
            _root.Scale = new Vector2(h, h);
            _root.Position = ((view - (SlotHost * h)) / 2f) + (at * h);
            return;
        }
        var (origin, k) = Letterbox(view, _design);
        _root.Scale = new Vector2(k, k);
        _root.Position = origin;
    }

    /// <summary>Место экрана размером <paramref name="design"/> в окне <paramref name="view"/>: масштаб по меньшей стороне и левый
    /// верхний угол при центрировании. По нему же ставится ролик смены экранов меню (<c>UiFlowView.PlaceTransition</c>): движок
    /// рисует его по центру экрана 800×600 (<c>CMenuFullScreenAnim</c>, <c>0x4da85c</c>), а не во всё окно.</summary>
    public static (Vector2 At, float Scale) Letterbox(Vector2 view, Vector2 design)
    {
        float k = Mathf.Min(view.X / design.X, view.Y / design.Y);
        return ((view - (design * k)) / 2f, k);
    }

    /// <summary>Те же кадры, сложенные в картинку размером с <c>Design</c>. Пустые места остаются прозрачными —
    /// по доле непрозрачных точек видно, что экран вообще собрался.</summary>
    private static Image Compose(UiScreen screen, IReadOnlyList<Frame> frames)
    {
        Image canvas = Image.CreateEmpty(screen.Design.Width, screen.Design.Height, false, Image.Format.Rgba8);
        foreach (var item in frames)
            if (item.Additive) AddRect(canvas, item.Image, item.At);
            else canvas.BlendRect(item.Image, new Rect2I(Vector2I.Zero, item.Image.GetSize()), item.At);
        return canvas;
    }

    /// <summary>Сложение цветов кадра с холстом (вес — альфа кадра), как материал сложения спрайта.</summary>
    private static void AddRect(Image canvas, Image image, Vector2I at)
    {
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                int cx = at.X + x, cy = at.Y + y;
                if (cx < 0 || cy < 0 || cx >= canvas.GetWidth() || cy >= canvas.GetHeight()) continue;
                Color src = image.GetPixel(x, y), dst = canvas.GetPixel(cx, cy);
                if (src.A <= 0) continue;
                canvas.SetPixel(cx, cy, new Color(Math.Min(1f, dst.R + (src.R * src.A)), Math.Min(1f, dst.G + (src.G * src.A)),
                                                  Math.Min(1f, dst.B + (src.B * src.A)), dst.A));
            }
    }

    /// <summary>Доли маджентовых и непрозрачных точек собранной картинки. Маджента — всё, что в 16-битном цвете движка (RGB565) равно
    /// ключу 0xF81F, а не только ровно 255/0/255: (252,2,252) у подложек панелей боя игра тоже не рисует (пункт 15 прогона 11.10).</summary>
    private static (double Magenta, double Opaque) Shares(Image canvas)
    {
        byte[] data = canvas.GetData();
        int total = data.Length / 4, magenta = 0, opaque = 0;
        for (int i = 0; i + 3 < data.Length; i += 4)
        {
            if (data[i + 3] != 0 && data[i] >> 3 == 31 && data[i + 1] >> 2 == 0 && data[i + 2] >> 3 == 31) magenta++;
            if (data[i + 3] != 0) opaque++;
        }
        return total == 0 ? (0, 0) : ((double)magenta / total, (double)opaque / total);
    }

    private static Vector2I Origin(UiScreen screen) => new(screen.Design.X, screen.Design.Y);

    /// <summary>Относительный путь — от каталога проекта (<c>godot/</c>), как в остальных сценах.</summary>
    private static string ResolvePath(string path) => GameData.FromProject(path);

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
