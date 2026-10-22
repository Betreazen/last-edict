using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Содержимое экрана, которое задаёт логика (<see cref="IScreenLogic"/>) через <see cref="UiScreenState"/>:
/// фон, кадры картинок, тексты подписей и поля ввода, строки текстовых списков с подсветкой выбранной, прокрутка
/// текста и ролики. Узлы строятся один раз при открытии экрана, а после каждого шага меняется только то, что
/// изменилось в состоянии (<see cref="UiScreenState.Revision"/>).</summary>
public partial class UiScreenView
{
    private sealed record TextSlot(Label Label, UiTextStyle Style, string Markup, bool Edit);

    private sealed record Row(ColorRect Back, Label Text);

    private readonly Dictionary<string, TextSlot> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Row>> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (VideoStreamPlayer Player, int Run)> _players = new(StringComparer.Ordinal);

    private Sprite2D? _backgroundSprite;
    private string _backgroundFrame = "";
    private Sprite2D? _underlaySprite;
    private string? _underlayFrame;

    /// <summary>Кадр подложки в списке кадров (<see cref="UiScreenState.Underlay"/>).</summary>
    private const string UnderlayId = "UNDERLAY";
    private Node2D? _videoLayer;
    private int _revision = -1;

    /// <summary>Логика экрана; хозяин задаёт её до добавления в дерево. <c>null</c> — экран как в разметке.</summary>
    public IScreenLogic? Logic { get; set; }

    /// <summary>Вызов логики экрана: её ошибка (данные мода не сошлись с разметкой) печатается с именем экрана, а
    /// игра идёт дальше — необработанное исключение Godot не роняет, оно тихо оставило бы экран недорисованным. false — логика упала.</summary>
    private bool Guarded(Action call, string what)
    {
        try
        {
            call();
            return true;
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            GD.PrintErr($"{_state.Screen.Id}: логика экрана ({what}) — {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>Логика окна упала при открытии (разметка мода без элемента, который она заполняет): окно недособрано, и хозяин его
    /// снимает — иначе модальное окно без кнопки держит игру (прогон 15.10, пометка 22: <c>DLG_ACTION_RESULT</c> примера мода).</summary>
    public bool OpenFailed { get; private set; }

    /// <summary>Спрайт на каждую картинку, кнопку и переключатель — и у тех, у кого кадра пока нет: логика может
    /// задать его позже (значки рас сценария). Пустой спрайт скрыт.</summary>
    private void BuildSprites(Node2D root, UiScreen screen, IReadOnlyList<Frame> frames)
    {
        var byId = frames.ToDictionary(f => f.Id, StringComparer.Ordinal);
        // Frame — структура: GetValueOrDefault дал бы пустой кадр вместо «нет фона» (у DLG_BATTLE_A фона нет).
        Frame? background = byId.TryGetValue("BACKGROUND", out var bg) ? bg : null;
        Frame? underlay = byId.TryGetValue(UnderlayId, out var under) ? under : null;
        _underlaySprite = Sprite(root, UnderlayId, underlay, Vector2I.Zero);
        _underlayFrame = _state.Underlay;
        _backgroundSprite = Sprite(root, "BACKGROUND", background, Vector2I.Zero);
        _backgroundFrame = screen.Background;
        Vector2I origin = Origin(screen);
        var pictures = screen.Elements.Where(e => e.Kind is UiKind.Image or UiKind.Button or UiKind.Toggle).ToList();
        void Build(UiElement e)
        {
            bool drawn = byId.TryGetValue(e.Id, out var frame);
            var sprite = Sprite(root, e.Id, drawn ? frame : null, new Vector2I(e.Rect.X, e.Rect.Y) - origin);
            sprite.SetMeta(FrameMeta, drawn ? _state.Frame(e.Id) ?? "" : "");
            Blend(sprite, drawn ? _state.Frame(e.Id) ?? "" : "");
            _sprites[e.Id] = sprite;
        }
        foreach (var e in pictures.Where(e => e.OverVideo != true)) Build(e);
        _videoLayer = new Node2D { Name = "Videos" };
        root.AddChild(_videoLayer);
        foreach (var e in pictures.Where(e => e.OverVideo == true)) Build(e);   // рамка ролика — поверх роликов (UiElement.OverVideo)
    }

    private static CanvasItemMaterial? _additive;

    /// <summary>Смешение спрайта по имени кадра: <see cref="UiFrameRef.Add"/> в начале — сложение цветов, иначе обычное.</summary>
    private static void Blend(Sprite2D sprite, string frame) =>
        sprite.Material = frame.Length > 1 && frame[0] == UiFrameRef.Add
            ? _additive ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
            : null;

    private Sprite2D Sprite(Node2D root, string id, Frame? frame, Vector2I at)
    {
        var sprite = new Sprite2D
        {
            Name = id,
            Texture = frame is { } f ? Texture(f.Image) : null,
            Position = frame is { } g ? g.At : at,
            Centered = false,
            Visible = frame is not null,
        };
        root.AddChild(sprite);
        return sprite;
    }

    /// <summary>Подпись на каждый текст, счётчик и поле ввода (пустая, если строки нет: её может задать логика) и
    /// строки текстовых списков — по одной на видимую строку, с подложкой для выбранной.</summary>
    private void BuildTexts(Node2D root, UiScreen screen)
    {
        Vector2I origin = Origin(screen);
        foreach (var e in screen.Elements.Where(e => e.Kind is UiKind.Text or UiKind.Spin or UiKind.Edit))
        {
            string markup = e.Kind != UiKind.Edit && e.TextId is { } id && _strings.TryGetValue(id, out var s) ? s : "";
            var label = NewLabel(e.Id, e.Rect, origin);
            if (e.Kind == UiKind.Edit) _wrapping.Remove(label);   // поле ввода — одна строка
            root.AddChild(label);
            _texts[e.Id] = new TextSlot(label, UiTextStyle.Parse(e.Style), markup, e.Kind == UiKind.Edit);
        }

        foreach (var e in screen.Elements.Where(e => e.Kind == UiKind.TextList))
        {
            int height = _state.RowHeight(e.Id);
            if (height <= 0) continue;
            var rows = new List<Row>();
            for (int r = 0; r < e.Rect.Height / height; r++)
            {
                var rect = new UiRect(e.Rect.X, e.Rect.Y + (r * height), e.Rect.Width, height);
                var back = new ColorRect
                {
                    Name = $"{e.Id}_back{r}", Position = new Vector2(rect.X - origin.X, rect.Y - origin.Y),
                    Size = new Vector2(rect.Width, rect.Height), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                var text = NewLabel($"{e.Id}_row{r}", rect, origin);
                _wrapping.Remove(text);   // строка списка — без переноса
                root.AddChild(back);
                root.AddChild(text);
                rows.Add(new Row(back, text));
            }
            _rows[e.Id] = rows;
        }
    }

    /// <summary>Подпись в месте элемента. Место запоминается (<see cref="_boxes"/>): по нему подпись ставится по вертикали правилом движка
    /// (<see cref="UiArt.Place"/>), а сама подпись меняет положение и высоту.</summary>
    private Label NewLabel(string name, UiRect rect, Vector2I origin)
    {
        var label = new Label
        {
            Name = name,
            Position = new Vector2(rect.X - origin.X, rect.Y - origin.Y),
            Size = new Vector2(rect.Width, rect.Height),
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
        };
        _boxes[label] = new Rect2(label.Position, rect.Width, rect.Height);
        _wrapping.Add(label);
        return label;
    }

    /// <summary>Видимые непустые подписи элементов одной строкой на каждую: <c>label экран/элемент место=x,y,ш×в верх=N строк=L
    /// выс=H</c> — верх блока строк от верха места (правило движка, <see cref="UiArt.Place"/>) и высота подписи. Для проверок без окна.</summary>
    public IEnumerable<string> LabelLines()
    {
        foreach (var (id, slot) in _texts)
        {
            var label = slot.Label;
            if (!label.Visible || label.Text.Length == 0 || !OnRoot(label) || !_boxes.TryGetValue(label, out var box)) continue;
            bool cut = _overflow.ContainsKey(label);
            yield return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"label {_state.Screen.Id}/{id} место={box.Position.X:0},{box.Position.Y:0},{box.Size.X:0}x{box.Size.Y:0} " +
                $"верх={TopIn(label, box):0} строк={label.GetLineCount()} выс={label.Size.Y:0}{(cut ? " срезано" : "")}");
        }
    }

    /// <summary>Спрайт элемента для проверки без окна: <c>кадр@x,y:ШxВ:N</c> — место в координатах разметки, нарисованный размер (после
    /// вписывания по ширине места, <see cref="Fit"/>) и N непрозрачных точек показанной части картинки; спрайт скрыт или пуст — «-».</summary>
    public string SpriteLine(string id)
    {
        if (!_sprites.TryGetValue(id, out var sprite) || !sprite.Visible || !sprite.HasMeta(FrameMeta)) return "-";
        string frame = (string)sprite.GetMeta(FrameMeta);
        if (frame.Length == 0 || FrameImage(frame, out _) is not { } image) return "-";
        var part = sprite.RegionEnabled ? new Rect2I((Vector2I)sprite.RegionRect.Position, (Vector2I)sprite.RegionRect.Size)
                                        : new Rect2I(Vector2I.Zero, image.GetSize());
        int opaque = 0;
        for (int y = part.Position.Y; y < part.End.Y; y++)
            for (int x = part.Position.X; x < part.End.X; x++)
                if (image.GetPixel(x, y).A > 0) opaque++;
        var at = (Vector2I)sprite.Position + Origin(_state.Screen);
        var size = (Vector2I)(part.Size * sprite.Scale);
        return $"{frame}@{at.X},{at.Y}:{size.X}x{size.Y}:{opaque}";
    }

    /// <summary>Подписи с переносом строк правилом движка (<see cref="Wrapped"/>): тексты элементов и карточек; строки списков, поле ввода и
    /// замер табуляции — без переноса.</summary>
    private readonly HashSet<Label> _wrapping = new();

    /// <summary>Текст подписи с переносами движка (<see cref="UiTextWrap"/>, <c>0x529b94</c>/<c>0x52a1ba</c>) по ширине места: строки
    /// режет порт, Godot их только выводит (<c>AutowrapMode.Off</c>). Прежде Godot (<c>WordSmart</c>) рвал слово длиннее места по буквам и
    /// не считал пробелы за словом. Текст с табуляцией (места табуляции движка правило не повторяет) переносит Godot по словам, без
    /// разрыва слов (<c>AutowrapMode.Word</c>).</summary>
    private string Wrapped(Label label, string text, Rect2 box, LabelSettings settings)
    {
        if (!_wrapping.Contains(label) || text.Length == 0) return text;
        if (text.Contains('\t', StringComparison.Ordinal) || settings.Font is not { } font)
        {
            label.AutowrapMode = TextServer.AutowrapMode.Word;
            return text;
        }
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        int size = settings.FontSize;
        // Пробелы за последним словом строки в ширину строки входят (0x52a1ba), а выводятся без них — как Godot прежде и сборка без окна
        // (UiTextRaster, TrimEndEdgeSpaces). ponytail: смещает ли движок центр строки на хвостовой пробел — снимком не сверено.
        return string.Join('\n', UiTextWrap.Lines(text, box.Size.X, chunk => font.GetStringSize(chunk, HorizontalAlignment.Left, -1, size).X)
                                           .Select(line => line.TrimEnd(' ')));
    }

    /// <summary>Место подписи (координаты <c>Design</c>): элемента разметки, строки списка, текста карточки.</summary>
    private readonly Dictionary<Label, Rect2> _boxes = new();

    /// <summary>Что последним поставлено подписи: обновление экрана идёт по всем подписям, а у большинства ничего не сменилось.</summary>
    private readonly record struct Applied(string Text, HorizontalAlignment H, UiVAlign V, LabelSettings Settings, string Tabs, Rect2 Box,
                                           int Step);

    private readonly Dictionary<Label, Applied> _applied = new();

    /// <summary>Подпись: разметка строки поверх оформления элемента — выравнивание, шрифт, цвет. Оформление — общий ресурс на сочетание
    /// (<see cref="UiArt.Settings"/>), а не новый на каждое обновление; неизменившуюся подпись не трогает вовсе (ui-input.md скилла
    /// gdscript-knowledge, «Производительность UI»: смена текста или шрифта поднимает перевёрстку).</summary>
    private void Apply(Label label, UiTextStyle style)
    {
        UiFont face = _fonts.Resolve(style.Font);
        UiColor colour = style.Color ?? _fonts.Color;
        var wanted = new Applied(
            style.Text,
            style.H switch
            {
                UiHAlign.Center => HorizontalAlignment.Center,
                UiHAlign.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            },
            style.V,
            UiArt.Settings(face, Color.Color8(colour.R, colour.G, colour.B)),
            string.Join(",", style.TabStops()),
            _boxes.TryGetValue(label, out var box) ? box : new Rect2(label.Position, label.Size),
            UiArt.LineStep(face));
        bool known = _applied.TryGetValue(label, out var last);
        if (known && last == wanted) return;
        // Оформление — раньше текста: без него Godot верстает текст шрифтом темы и растит подпись по нему (UiArt.Place).
        if (!known || !ReferenceEquals(last.Settings, wanted.Settings)) label.LabelSettings = wanted.Settings;
        string shown = Wrapped(label, wanted.Text, wanted.Box, wanted.Settings);
        if (label.Text != shown) label.Text = shown;
        if (!known || last.H != wanted.H) label.HorizontalAlignment = wanted.H;
        // Табуляция — к месту табуляции строки (UiTextStyle.TabStops, «\s110;» карточки воина); без мест знак табуляции шрифтом почти не
        // рисуется — «Уровень:1» слитно (прогон 2 пользователя 11.10, снимок 33; проверка --tabs).
        if (!known || last.Tabs != wanted.Tabs)
            label.TabStops = wanted.Tabs.Length == 0 ? [] : [.. wanted.Tabs.Split(',').Select(t => (float)int.Parse(t, System.Globalization.CultureInfo.InvariantCulture))];
        // По вертикали — правилом движка (0x52ad04): блок строк × шаг шрифта, не влез — от верха (UiTextBlock).
        UiArt.Place(label, wanted.Box, wanted.V, wanted.Step);
        ClipToBox(label, wanted.Box);
        _applied[label] = wanted;
    }

    /// <summary>Подписи, чей блок строк выше места, — в обрезающем контейнере на месте элемента (подпись → контейнер).</summary>
    private readonly Dictionary<Label, Control> _overflow = new();

    /// <summary>
    /// Текст выше места режется местом элемента по точкам, как у движка: <c>CTextBoxInterf</c> (пересборка <c>0x533862</c>) строит картинку
    /// текста шириной места и высотой не меньше всего блока строк (перенос по словам — <c>0x529b94</c>/<c>0x52a1ba</c>; не влез — от верха,
    /// <c>0x52acc4</c>), а рисует её виджет с прямоугольником места (<c>CWidgetInterf</c> <c>0x53da28</c>, <c>0x53e18b</c>): вторая строка
    /// длинного заголовка видна только верхом в месте, ниже плашки ничего нет. Прежде <see cref="UiArt.Place"/> вытягивал подпись на все
    /// строки, и вторая строка ложилась ниже рамки (партия 17.10, пометки 12–16 и 20: «S1 покупатель и продавец», «Частичная тренировка
    /// завершена» поверх портрета). Подпись переезжает в контейнер <c>ClipContents</c> размером с место, внутри — на (0, верх блока).
    /// </summary>
    private void ClipToBox(Label label, Rect2 box)
    {
        // Подпись без своего места (замер ширины табуляции, Tabs.Measure) и текст со сдвигом (свой контейнер, Offset) — не здесь.
        if (!_boxes.ContainsKey(label) || label.GetParent() is not { } parent || (parent is Control own && _clips.ContainsValue(own))) return;
        if (_overflow.TryGetValue(label, out var clip))
        {
            // Уже в контейнере (и когда текст перестал переполнять место — обрезка по месту ничего не меняет): место могло смениться
            // (подпись карточки переходит между ячейками).
            clip.Position = box.Position;
            clip.Size = box.Size;
        }
        else
        {
            // Пустая подпись без переноса Godot (AutowrapMode.Off) держит наименьшую высоту строки шрифта темы — переполнения в ней нет.
            if (label.Text.Length == 0 || label.Size.Y <= box.Size.Y + 0.5f) return;
            clip = new Control { Name = $"{label.Name}_cut", Position = box.Position, Size = box.Size, ClipContents = true,
                                 MouseFilter = Control.MouseFilterEnum.Ignore };
            parent.AddChild(clip);
            parent.MoveChild(clip, label.GetIndex());
            label.Reparent(clip, keepGlobalTransform: false);
            _overflow[label] = clip;
        }
        label.Position -= box.Position;   // Place ставит подпись в координатах места родителя — в контейнере место начинается с 0
    }

    /// <summary>Видимые подписи элементов, срезанные местом (<see cref="ClipToBox"/>), — для проверок без окна.</summary>
    public IReadOnlyList<string> CutLabels() =>
        [.. _texts.Where(t => t.Value.Label.Visible && t.Value.Label.Text.Length > 0 && _overflow.ContainsKey(t.Value.Label))
                  .Select(t => t.Key).Order(StringComparer.Ordinal)];

    /// <summary>Подпись лежит на корне экрана — прямо или в своём обрезающем контейнере (<see cref="ClipToBox"/>).</summary>
    private bool OnRoot(Label label) =>
        label.GetParent() == _root || (_overflow.TryGetValue(label, out var clip) && clip.GetParent() == _root);

    /// <summary>Верх блока строк подписи от верха её места.</summary>
    private float TopIn(Label label, Rect2 box) => _overflow.ContainsKey(label) ? label.Position.Y : label.Position.Y - box.Position.Y;

    /// <summary>Сборка без окна с подписями (<c>--png --text</c>): видимые подписи и строки списков — в картинку экрана, на процессоре
    /// (<see cref="UiTextRaster"/>), в точках разметки. По ней <c>tools/pixel-compare.py --text</c> сверяет текст со снимком оригинала.</summary>
    private void DrawTexts(Image canvas)
    {
        var labels = _texts.Values.Select(t => t.Label).Concat(_rows.Values.SelectMany(r => r.Select(row => row.Text)));
        foreach (var label in labels.Where(l => l.Visible && OnRoot(l)))
        {
            // Срезанная местом (ClipToBox) — в координатах контейнера и с обрезкой по месту.
            if (_overflow.TryGetValue(label, out var clip))
                UiTextRaster.Draw(canvas, label, (Vector2I)(clip.Position + label.Position), new Rect2I((Vector2I)clip.Position, (Vector2I)clip.Size));
            else UiTextRaster.Draw(canvas, label, (Vector2I)label.Position);
        }
    }

    /// <summary>Перерисовать содержимое, если состояние сменилось с прошлого раза.</summary>
    private void RefreshContent()
    {
        if (_root is null || _state.Revision == _revision) return;
        RefreshBackground();
        RefreshTexts();
        RefreshRows();
        RefreshCards();
        RefreshVideos();
        _revision = _state.Revision;
    }

    private void RefreshBackground()
    {
        if (_underlaySprite is not null && _state.Underlay != _underlayFrame)
        {
            _underlayFrame = _state.Underlay;
            Image? under = _underlayFrame is null ? null : FrameImage(_underlayFrame, out _);
            if (_underlayFrame is not null && under is null) GD.PrintErr($"{_state.Screen.Id}: нет кадра подложки {_underlayFrame}");
            _underlaySprite.Texture = under is null ? null : Texture(under);
            _underlaySprite.Visible = under is not null;
        }
        if (_backgroundSprite is null || _state.Background == _backgroundFrame) return;
        _backgroundFrame = _state.Background;
        Image? image = _backgroundFrame.Length == 0 ? null : FrameImage(_backgroundFrame, out _);
        if (image is null && _backgroundFrame.Length > 0) GD.PrintErr($"{_state.Screen.Id}: нет кадра фона {_backgroundFrame}");
        _backgroundSprite.Texture = image is null ? null : Texture(image);
        _backgroundSprite.Visible = image is not null;
    }

    private void RefreshTexts()
    {
        foreach (var (id, slot) in _texts)
        {
            if (slot.Edit)
            {
                // Набранное — не разметка: обратная косая в имени осталась бы буквой, а не директивой.
                string typed = _state.TextOf(id) ?? "";
                Apply(slot.Label, slot.Style with { Text = _state.Focused == id ? typed + "|" : typed });
                continue;
            }
            slot.Label.Visible = !_state.IsHidden(id);
            Apply(slot.Label, UiTextStyle.Parse(_state.TextOf(id) ?? slot.Markup, slot.Style));
            if (_state.TextOffset(id) is { } offset)
            {
                Offset(id, slot.Label, offset);
                continue;
            }
            int wanted = _state.ScrollOf(id);
            if (wanted == 0 && slot.Label.LinesSkipped == 0) continue;
            // Прокручиваемый текст — в своём месте целиком (UiArt.Place вытянул бы подпись на все строки, и прокручивать было бы нечего).
            if (_boxes.TryGetValue(slot.Label, out var box)) slot.Label.Size = box.Size - new Vector2(0, TopIn(slot.Label, box));
            int limit = Math.Max(0, slot.Label.GetLineCount() - slot.Label.GetVisibleLineCount());
            slot.Label.LinesSkipped = Math.Min(wanted, limit);
            if (wanted > limit) _state.SetScroll(id, limit);
        }
    }

    private readonly Dictionary<string, Control> _clips = new(StringComparer.Ordinal);

    /// <summary>Текст со сдвигом в точках (<see cref="UiScreenState.TextOffset"/>): подпись переезжает в обрезающий контейнер на месте
    /// элемента и сдвигается по вертикали; высота свёрстанного текста уходит в состояние (<see cref="UiScreenState.ReportTextHeight"/>).</summary>
    private void Offset(string id, Label label, int offset)
    {
        // Подпись уже срезана местом (ClipToBox) — её контейнер и есть место текста со сдвигом.
        if (!_clips.ContainsKey(id) && _overflow.Remove(label, out var cut))
        {
            _clips[id] = cut;
            label.ClipText = false;
        }
        if (!_clips.TryGetValue(id, out var clip))
        {
            clip = new Control
            {
                Name = $"{id}_clip", Position = _boxes.TryGetValue(label, out var box) ? box.Position : label.Position,
                Size = _boxes.TryGetValue(label, out var size) ? size.Size : label.Size, ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var parent = label.GetParent();
            parent.AddChild(clip);
            parent.MoveChild(clip, label.GetIndex());
            label.Reparent(clip, keepGlobalTransform: false);
            label.ClipText = false;
            _clips[id] = clip;
        }
        int lines = label.GetLineCount();
        float spacing = label.LabelSettings?.LineSpacing ?? 0;   // у шрифтов с шагом оригинала — отрицательный (UiArt.LineSpacing)
        int height = lines == 0 ? 0 : Mathf.RoundToInt((lines * label.GetLineHeight()) + ((lines - 1) * spacing));
        label.Position = new Vector2(0, offset);
        label.Size = new Vector2(clip.Size.X, Math.Max(clip.Size.Y, height));
        _state.ReportTextHeight(id, height);
    }

    private void RefreshRows()
    {
        foreach (var (id, rows) in _rows)
        {
            var spec = _state.Screen.Elements.First(e => e.Id == id).List ?? new UiList();
            var normal = UiTextStyle.Parse(spec.Style);
            var selected = UiTextStyle.Parse(spec.SelectedStyle, normal);
            var items = _state.Items(id);
            int top = _state.ListTop(id), chosen = _state.ListSelected(id);
            for (int r = 0; r < rows.Count; r++)
            {
                int index = top + r;
                bool on = index == chosen;
                // Строка списка — разметка поверх оформления списка, как надпись (у движка один разборщик текста, 0x529e4b): действия вора —
                // X005TA0819 «\c000;000;000;%NAME%», недоступные — X005TA0820 «\c128;000;000;…» (прогон 15.10, пометка 21: печаталось как есть).
                var style = index < items.Count ? UiTextStyle.Parse(items[index], on ? selected : normal) : normal with { Text = "" };
                Apply(rows[r].Text, style);
                rows[r].Back.Visible = on && style.Background is not null && index < items.Count;
                if (style.Background is { } bg) rows[r].Back.Color = Color.Color8(bg.R, bg.G, bg.B);
            }
        }
    }

    /// <summary>Непустые видимые строки текстового списка для проверок без окна: <c>текст#RRGGBB</c> через «|», пробелы — «_»; нет списка
    /// или строк — «-».</summary>
    public string RowsLine(string id) =>
        _rows.TryGetValue(id, out var rows) && rows.Where(r => r.Text.Text.Length > 0).ToList() is { Count: > 0 } shown
            ? string.Join("|", shown.Select(r => $"{r.Text.Text.Replace(' ', '_')}#{(r.Text.LabelSettings?.FontColor ?? Colors.Black).ToHtml(false)}"))
            : "-";

    /// <summary>Ролики по состоянию: новый или перезапущенный (<see cref="UiVideoRef.Run"/>) играет с начала,
    /// снятый — убирается. Разовый ролик на фоне (смена бога расы) по окончании прячется, под ним остаётся фон.
    /// Ролика нет в описи или на диске — строка в журнал, экран работает без него.</summary>
    private void RefreshVideos()
    {
        foreach (var id in _players.Keys.Where(k => !_state.Videos.ContainsKey(k)).ToList())
        {
            _players[id].Player.QueueFree();
            _players.Remove(id);
        }
        foreach (var (id, video) in _state.Videos)
        {
            if (_players.TryGetValue(id, out var running) && running.Run == video.Run) continue;
            if (running.Player is not null) running.Player.QueueFree();
            _players.Remove(id);
            if (VideoPath(video.Name) is not { } path)
            {
                GD.PrintErr($"{_state.Screen.Id}/{id}: нет ролика {video.Name} (python tools/video-import/build_videos.py)");
                if (!video.Loop && id != UiScreenState.BackgroundId) Callable.From(() => VideoEnded(id, video.Run)).CallDeferred();
                continue;
            }
            var player = NewPlayer(id, path, video.Loop);
            // Разовый ролик элемента (смена лица лорда) — логике по окончании: она ждёт его конца.
            if (!video.Loop && id != UiScreenState.BackgroundId)
            {
                int run = video.Run;
                player.Finished += () => VideoEnded(id, run);
            }
            _videoLayer!.AddChild(player);
            player.Play();
            PortProbe.Video(player, video.Name);
            _players[id] = (player, video.Run);
        }
    }

    private VideoStreamPlayer NewPlayer(string id, string path, bool loop)
    {
        Vector2I origin = Origin(_state.Screen);
        var rect = id == UiScreenState.BackgroundId
            ? new UiRect(origin.X, origin.Y, _state.Screen.Design.Width, _state.Screen.Design.Height)
            : _state.Screen.Elements.First(e => e.Id == id).Rect;
        var player = new VideoStreamPlayer
        {
            Name = $"video_{id}",
            Stream = new VideoStreamTheora { File = path },
            Position = new Vector2(rect.X - origin.X, rect.Y - origin.Y),
            Size = new Vector2(rect.Width, rect.Height),
            Expand = true,
            Loop = loop,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // Разовый ролик на фоне (смена бога) уходит, открывая фон; кадр-картинка брифинга остаётся на месте.
        if (!loop && id == UiScreenState.BackgroundId) player.Finished += () => player.Visible = false;
        return player;
    }

    /// <summary>Разовый ролик элемента доиграл — событие логике, если это всё ещё тот же запуск (<see cref="UiVideoRef.Run"/>).</summary>
    private void VideoEnded(string id, int run)
    {
        if (!IsInstanceValid(this) || _state.VideoOf(id)?.Run != run) return;
        Dispatch([new UiVideoEnded(id)]);
    }

    /// <summary>Путь к ролику по имени из описи роликов; <c>null</c> — нет в описи или файл не собран.</summary>
    private static string? VideoPath(string name)
    {
        if (!GameData.Content.VideosByName.TryGetValue(name, out var video)) return null;
        string path = UiVideos.PathOf(video, GameData.VideoRoot);
        return File.Exists(path) ? path : null;
    }
}
