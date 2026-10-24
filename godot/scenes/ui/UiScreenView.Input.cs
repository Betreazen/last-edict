using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Живые элементы экрана: ввод из Godot переводится в координаты <c>Design</c> и отдаётся
/// <see cref="UiScreenState"/>, а кадры спрайтов после каждого шага берутся из него же. Решений здесь нет —
/// только перевод координат и клавиш и смена текстур.
///
/// <para>Проверка без окна — <c>--input "шаг; шаг; …"</c>. Шаги: <c>move</c>, <c>press</c>, <c>release</c>,
/// <c>click</c> (нажать и отпустить), <c>double</c> (двойной щелчок), <c>wheelup</c>/<c>wheeldown</c> (щелчок колеса) — с точкой <c>x,y</c> в координатах
/// <c>Design</c> или именем элемента (берётся его середина); <c>key N</c> — виртуальный код Windows. События
/// идут настоящим путём: <c>Viewport.PushInput</c> → <see cref="_UnhandledInput"/>, так что проверяется и перевод
/// координат через масштаб окна. На каждый шаг печатается строка <c>input …</c>: что под мышью, какие события
/// и какие спрайты сменили кадр (по метке кадра на самом узле, а не по состоянию).</para></summary>
public partial class UiScreenView
{
    private const string FrameMeta = "frame";

    private readonly Dictionary<string, Sprite2D> _sprites = new(StringComparer.Ordinal);
    private readonly List<UiEvent> _events = new();

    private UiScreenState _state = new(new UiScreen { Id = "-" });
    private IReadOnlyDictionary<string, UiAsset> _assets = new Dictionary<string, UiAsset>();

    /// <summary>Коды стрелок Windows — колесо мыши (<see cref="_UnhandledInput"/>).</summary>
    private const int VkUp = 38, VkDown = 40;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_root is null) return;
        if (Navigate(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton) HideTip();   // любое нажатие и отпускание прячет подсказку (0x53cfd3)
        IReadOnlyList<UiEvent> events = @event switch
        {
            InputEventMouseMotion motion => Pointer(motion, (x, y) =>
            {
                TipMove(x, y);
                DragCursorAt(x, y);
                if (Logic is LastEdict.Data.Ui.Windows.IWindowLogic w)
                {
                    if (_middle is { } from && motion.ButtonMask.HasFlag(MouseButtonMask.Middle))
                    {
                        Guarded(() => w.Pan(from.X, from.Y, x - from.X, y - from.Y, _state), $"pan {x - from.X},{y - from.Y}");
                        _middle = new Vector2I(x, y);
                    }
                    Guarded(() => w.Motion(x, y, _state), $"motion {x},{y}");
                }
                return _state.Move(x, y);
            }),
            InputEventMouseButton { ButtonIndex: MouseButton.Left } button => Pointer(button, (x, y) =>
                button.Pressed ? _state.Press(x, y, button.DoubleClick) : _state.Release(x, y)),
            // Поле ввода с фокусом забирает печатные символы — иначе буква нажала бы кнопку с этой горячей клавишей.
            InputEventKey { Pressed: true } key when _state.Focused is not null && key.Unicode >= ' ' =>
                _state.Type((char)key.Unicode),
            // Клавиши и геймпад — через действия ввода (InputBindings, data/ui/input.json): переназначенная клавиша жмёт код оригинала.
            InputEventKey { Pressed: true, Echo: false } or InputEventJoypadButton or InputEventJoypadMotion =>
                [.. InputBindings.VirtualKeys(@event, _state.Screen, VirtualKey).SelectMany(KeyPressed)],
            // Колесо мыши у движка — стрелки: WndProc 0x5626a3 переводит WM_MOUSEWHEEL в нажатие VK_UP (вперёд) или VK_DOWN (назад) и шлёт
            // его тем же путём, что WM_KEYDOWN (0x562993). Списки сами клавиш не берут (CListBoxInterf, слот клавиатуры — общий 0x5305ac),
            // листают кнопки со стрелками в горячих клавишах (38/40: книга заклинаний, сохранения, сценарии, журнал, наёмники…).
            // Окно с зумом (дерево построек) съедает колесо над своим видом раньше стрелок.
            InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel => Pointer(wheel, (x, y) =>
                WindowWheel(x, y, wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1) ? [] : KeyPressed(wheel.ButtonIndex == MouseButton.WheelUp ? VkUp : VkDown)),
            // Средняя кнопка — тащение вида окна (дерево построек, IWindowLogic.Pan): начало — здесь, сдвиг — с движением мыши.
            InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle => Pointer(middle, (x, y) =>
            {
                _middle = middle.Pressed ? new Vector2I(x, y) : null;
                return [];
            }),
            _ => [],
        };
        bool pointed = Dispatch(events, @event is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mouse
                                        ? () => Point(mouse) : null,
                                hotkey: @event is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion
                                        or InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown });
        if (pointed) Pointed?.Invoke();
    }

    /// <summary>События элементов — логике и хозяину, как у ввода (и у щелчков автоповтора); <paramref name="point"/> — нажатие мыши логике
    /// окна между логикой и перерисовкой; <paramref name="hotkey"/> — события от клавиши (поле <c>hotkey</c> строки <c>ui_click</c> зонда).
    /// true — логика окна получила нажатие.</summary>
    private bool Dispatch(IReadOnlyList<UiEvent> events, Func<bool>? point = null, bool hotkey = false)
    {
        _events.AddRange(events);
        TraceClicks(events, hotkey);
        if (Hosted is null) foreach (var e in events) GD.Print($"ui {Describe(e)}");
        foreach (var e in events) Guarded(() => Logic?.Handle(e, _state), Describe(e));
        bool pointed = point?.Invoke() ?? false;
        Refresh();
        if (events.Count > 0) Fired?.Invoke(events);
        return pointed;
    }

    /// <summary>Точка нажатия средней кнопки (или последняя точка её тащения); не зажата — null.</summary>
    private Vector2I? _middle;

    /// <summary>Колесо — логике окна (<see cref="LastEdict.Data.Ui.Windows.IWindowLogic.Wheel"/>); true — съела.</summary>
    private bool WindowWheel(int x, int y, int steps)
    {
        bool eaten = false;
        if (Logic is LastEdict.Data.Ui.Windows.IWindowLogic window) Guarded(() => eaten = window.Wheel(x, y, steps, _state), $"wheel {x},{y} {steps}");
        return eaten;
    }

    /// <summary>Клавиша — сначала логике окна (<see cref="LastEdict.Data.Ui.Windows.IWindowLogic.Key"/>; клавиша без кода — 0), не съела —
    /// горячим клавишам кнопок. Поле ввода с фокусом логике окна клавиш не отдаёт: стирание и прочие — полю.</summary>
    private IReadOnlyList<UiEvent> KeyPressed(int? vk)
    {
        bool eaten = false;
        if (_state.Focused is null && Logic is LastEdict.Data.Ui.Windows.IWindowLogic window)
            Guarded(() => eaten = window.Key(vk ?? 0, _state), $"key {vk ?? 0}");
        return eaten || vk is not int code ? [] : _state.Key(code);
    }

    /// <summary>Курсор при тащении в окне сменился (<c>NODRAGDROP</c> — бросить сюда нельзя; null — обычный): хозяин ставит его в общий
    /// курсор.</summary>
    public event Action<string?>? DragCursorChanged;

    /// <summary>Курсор тащения сейчас (для строки проверки): <c>NODRAGDROP</c> или null.</summary>
    public string? DragCursor { get; private set; }

    /// <summary>Картинка тащимого на курсоре (<see cref="LastEdict.Data.Ui.Windows.IWindowLogic.DragImage"/>); не тащат — null.</summary>
    public Image? DragImage { get; private set; }

    /// <summary>Кадр картинки тащимого (ключ кэша курсора); не тащат — пусто.</summary>
    public string DragImageKey { get; private set; } = "";

    private void DragCursorAt(int x, int y)
    {
        var window = Logic as LastEdict.Data.Ui.Windows.IWindowLogic;
        bool? accepts = window?.DropAccepts(x, y, _state);
        string? cursor = accepts == false ? "NODRAGDROP" : accepts is null ? window?.ToolCursor(x, y, _state) : null;
        string key = accepts is null ? "" : window!.DragImage ?? "";
        if (cursor == DragCursor && key == DragImageKey) return;
        DragCursor = cursor;
        DragImageKey = key;
        DragImage = key.Length > 0 && FrameImage(key, out _) is { } drag ? ImageScales.ToLogical(drag) : null;   // HD — логический значок
        DragCursorChanged?.Invoke(cursor);
    }

    /// <summary>Логика окна отработала нажатие или отпускание мыши вне элементов разметки (перетаскивание юнитов):
    /// хозяин забирает её просьбы.</summary>
    public event Action? Pointed;

    /// <summary>Левая кнопка отпущена в точке разметки — хозяину (портрет героя панели команд открывает окно отряда).</summary>
    public event Action<int, int>? Released;

    /// <summary>Нажатие и отпускание левой кнопки — логике окна (<see cref="LastEdict.Data.Ui.Windows.IWindowLogic"/>), в
    /// координатах разметки; отпускание — ещё и хозяину (<see cref="Released"/>). <c>false</c> — логика не окна.</summary>
    private bool Point(InputEventMouseButton button)
    {
        var local = (InputEventMouse)_root!.MakeInputLocal(button);
        Vector2I at = new Vector2I(Mathf.FloorToInt(local.Position.X), Mathf.FloorToInt(local.Position.Y)) + Origin(_state.Screen);
        bool right = button.ButtonIndex == MouseButton.Right;
        if (!button.Pressed && !right) Released?.Invoke(at.X, at.Y);
        if (Logic is not LastEdict.Data.Ui.Windows.IWindowLogic window) return false;
        if (right && button.Pressed) Guarded(() => window.RightPress(at.X, at.Y, _state), $"rpress {at.X},{at.Y}");
        else if (right) Guarded(() => window.RightRelease(at.X, at.Y, _state), $"rrelease {at.X},{at.Y}");
        else if (button.Pressed)
        {
            Guarded(() => window.Press(at.X, at.Y, _state), $"press {at.X},{at.Y}");
            if (button.DoubleClick) Guarded(() => window.DoubleClick(at.X, at.Y, _state), $"double {at.X},{at.Y}");
            DragCursorAt(at.X, at.Y);   // нажатие начало тащение — курсор с картинкой тащимого сразу (0x56e59a → 0x56c58a)
        }
        else
        {
            Guarded(() => window.Release(at.X, at.Y, _state), $"release {at.X},{at.Y}");
            DragCursorAt(at.X, at.Y);   // бросили — курсор снова обычный
        }
        return true;
    }

    /// <summary>Точка события в координатах <c>Design</c>: обратное преобразование узла экрана (масштаб
    /// и сдвиг из <see cref="Rescale"/>) плюс начало экрана.</summary>
    private IReadOnlyList<UiEvent> Pointer(InputEventMouse mouse, Func<int, int, IReadOnlyList<UiEvent>> handle)
    {
        var local = (InputEventMouse)_root!.MakeInputLocal(mouse);
        Vector2I at = new Vector2I(Mathf.FloorToInt(local.Position.X), Mathf.FloorToInt(local.Position.Y))
                      + Origin(_state.Screen);
        return handle(at.X, at.Y);
    }

    /// <summary>Перерисовать экран под изменившееся состояние: хозяин зовёт после того, как его логика поменяла
    /// содержимое (панели экрана карты — выбранный отряд, день, казна).</summary>
    public void Sync() => Refresh();

    /// <summary>Кадры, которых при последнем обновлении элемента не нашлось (элемент оставил прежнюю картинку), — для строки проверки окна.</summary>
    public IReadOnlyCollection<string> MissingFrames => [.. _missing.Select(p => $"{p.Key}:{p.Value}")];

    private readonly Dictionary<string, string> _missing = [];

    /// <summary>Размер собранной картинки кадра «ШxВ» (для строки проверки окна); кадра нет — «-».</summary>
    public string FrameSize(string frame) =>
        FrameImage(frame, out _) is { } image ? $"{ImageScales.Logical(image).X}x{ImageScales.Logical(image).Y}" : "-";

    /// <summary>Видимые рамки допустимости перетаскивания (кадры <c>DRAGDROP_*</c>) для строки проверки: «элемент:кадр@x,y:ШxВ» — место
    /// спрайта в координатах разметки и размер его картинки, по узлам, а не по состоянию.</summary>
    public IReadOnlyList<string> DropFrameSprites() => [.. _sprites
        .Where(p => p.Value.Visible && ((string)p.Value.GetMeta(FrameMeta)).StartsWith("DRAGDROP_", StringComparison.Ordinal))
        .Select(p =>
        {
            var at = (Vector2I)p.Value.Position + Origin(_state.Screen);
            var size = p.Value.Texture?.GetSize() ?? Vector2.Zero;
            string frame = ((string)p.Value.GetMeta(FrameMeta))["DRAGDROP_".Length..];
            return $"{p.Key}:{frame}@{at.X},{at.Y}:{(int)size.X}x{(int)size.Y}";
        })];

    /// <summary>Картинки элементов, видимая часть которых выходит за место элемента (для строки проверки): «элемент:ШxВ>шxв» — показанный
    /// размер против места. Пусто — все в своих местах.</summary>
    public IReadOnlyList<string> ImagesOutside() => [.. _sprites
        .Where(p => p.Value.Visible && p.Value.Texture is not null)
        .Select(p => (p.Key, Sprite: p.Value, Element: _state.Screen.Elements.FirstOrDefault(e => e.Id == p.Key)))
        .Where(t => t.Element is { Kind: UiKind.Image })
        .Select(t =>
        {
            var size = (t.Sprite.RegionEnabled ? t.Sprite.RegionRect.Size : t.Sprite.Texture!.GetSize()) * t.Sprite.Scale;
            var at = t.Sprite.Position + (Vector2)Origin(_state.Screen);
            var r = t.Element!.Rect;
            bool outside = at.X < r.X || at.Y < r.Y || at.X + size.X > r.X + r.Width + 0.5f || at.Y + size.Y > r.Y + r.Height + 0.5f;
            return outside ? $"{t.Key}:{(int)size.X}x{(int)size.Y}>{r.Width}x{r.Height}" : null;
        })
        .OfType<string>()];

    /// <summary>Кадры спрайтов по текущему состоянию. Кадр, которого нет в описи, оставляет прежний — пропуски
    /// уже названы в строке итога экрана.</summary>
    private void Refresh()
    {
        SyncTree();   // дерево окна построек: раскладка логики по новому холсту — до кадров (фон дерева меняет размер)
        foreach (var (id, sprite) in _sprites)
        {
            string frame = _state.Frame(id) ?? "";
            if ((string)sprite.GetMeta(FrameMeta) == frame) continue;
            _missing.Remove(id);
            if (frame.Length == 0)
            {
                sprite.Visible = false;
                sprite.SetMeta(FrameMeta, "");
                continue;
            }
            Image? image = FrameImage(frame, out _);
            if (image is null)
            {
                _missing[id] = frame;
                continue;
            }
            Show(sprite, image);
            Blend(sprite, frame);
            Fit(sprite, id, image);
            sprite.Visible = true;
            sprite.SetMeta(FrameMeta, frame);
        }
        RefreshContent();
        TraceEnabled();
    }

    /// <summary>Картинка шире своего элемента вписывается по ширине, сверху, а то, что ниже прямоугольника,
    /// обрезается: так игра показывает значки рас сценария — картинку выбора рас 174×202 в месте 45×73 (сверено
    /// пользователем по снимкам 19.09.2026). Картинка в свой размер или меньше рисуется как есть. Картинка с выравниванием
    /// по центру (<see cref="UiImageAlign.Center"/>) — без масштаба, по центру места; с обрезкой (<see cref="UiImageAlign.Clip"/>) — без
    /// масштаба от угла, лишнее за местом не видно.</summary>
    private void Fit(Sprite2D sprite, string id, Image image)
    {
        var element = _state.Screen.Elements.First(e => e.Id == id);
        var rect = element.Rect;
        var align = element.Kind == UiKind.Image ? _state.ImageAlign(id) : UiImageAlign.Fit;
        // Размеры — логические (HD: текстура того же логического размера, ImageScales), места и области — в точках разметки.
        var size = ImageScales.Logical(image);
        var (iw, ih) = (size.X, size.Y);
        var (dx, dy) = UiImagePlacement.Offset(align, rect.Width, rect.Height, iw, ih);
        sprite.Position = new Vector2(rect.X + dx, rect.Y + dy) - (Vector2)Origin(_state.Screen);
        if (align == UiImageAlign.Clip && rect.Width > 0 && rect.Height > 0)
        {
            sprite.Scale = Vector2.One;
            sprite.RegionEnabled = iw > rect.Width || ih > rect.Height;
            sprite.RegionRect = new Rect2(0, 0, Math.Min(iw, rect.Width), Math.Min(ih, rect.Height));
            return;
        }
        if (align != UiImageAlign.Fit || rect.Width <= 0 || iw <= rect.Width)
        {
            sprite.Scale = Vector2.One;
            sprite.RegionEnabled = false;
            return;
        }
        float k = rect.Width / (float)iw;
        sprite.Scale = new Vector2(k, k);
        float visible = Math.Min(ih, rect.Height / k);
        sprite.RegionEnabled = true;
        sprite.RegionRect = new Rect2(0, 0, iw, visible);
    }

    /// <summary>Текстура кадра — общая на запуск (<see cref="UiArt.Texture"/>).</summary>
    private static ImageTexture Texture(Image image) => UiArt.Texture(image);

    /// <summary>Текстура кадра на спрайт и фильтр по ней: HD — сглаживание с mip-уровнями, пиксельная — от родителя («ближайший»).</summary>
    private static void Show(Sprite2D sprite, Image? image)
    {
        sprite.Texture = image is null ? null : Texture(image);
        sprite.TextureFilter = ImageScales.Filter(sprite.Texture, CanvasItem.TextureFilterEnum.ParentNode);
    }

    /// <summary>Начальное устройство элементов: высота строки текстовых списков — шаг строки их шрифта (в разметке её
    /// нет), число пунктов — из <c>--items СПИСОК=12,СЧЁТЧИК=5</c> (пока пункты не даёт логика экрана, иначе
    /// список пуст и проверять в нём нечего). <c>false</c> — имя не список и не счётчик.</summary>
    private bool Configure(UiScreen screen, string? items)
    {
        var counts = (items ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .ToDictionary(p => p[0].Trim(), p => p.Length > 1 && int.TryParse(p[1], out int n) ? n : 0);

        foreach (var e in screen.Elements)
        {
            counts.Remove(e.Id, out int count);
            if (e.Kind == UiKind.Spin) _state.SetSpin(e.Id, count, 0);
            if (e.Kind is not (UiKind.List or UiKind.TextList)) continue;
            int row = 0;
            if (e.Kind == UiKind.TextList)
            {
                UiFont face = _fonts.Resolve(UiTextStyle.Parse(e.List?.Style).Font);
                // Шаг строки шрифта (как у многострочных подписей, UiFont.Line), а не его высота: у растрового шрифта игры высота —
                // голая ячейка (у Normal 13), и строки списка с выносными стояли бы впритык. Высоту строки списка в движке не разбирали.
                row = face.Line > 0 ? face.Line : Mathf.CeilToInt(Face(face).GetHeight(face.Size));
            }
            _state.SetList(e.Id, count, row);
        }
        if (counts.Count == 0) return true;
        GD.PrintErr($"UiScreenView: --items — нет списков или счётчиков {string.Join(", ", counts.Keys)}");
        return false;
    }

    /// <summary>Прогон шагов <c>--input</c>. <c>false</c> — шаг не разобран (печатается, что именно).</summary>
    private bool RunInput(string script)
    {
        var steps = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < steps.Length; i++)
        {
            var before = _sprites.ToDictionary(p => p.Key, p => (string)p.Value.GetMeta(FrameMeta));
            _events.Clear();
            if (!Step(steps[i], out string at))
            {
                GD.PrintErr($"UiScreenView: шаг «{steps[i]}» не разобран");
                return false;
            }
            var changed = _sprites.Where(p => (string)p.Value.GetMeta(FrameMeta) != before[p.Key])
                                  .Select(p => $"{p.Key}:{p.Value.GetMeta(FrameMeta)}");
            GD.Print($"input step={i + 1} do={steps[i].Split(' ')[0]} at={at} hover={_state.Hovered ?? "-"} " +
                     $"events={List(_events.Select(Describe).ToList())} changed={List(changed.ToList())} " +
                     $"tip={(TooltipShown is { Length: > 0 } tip ? tip.Replace(' ', '_') : "-")}");
        }
        return true;
    }

    /// <summary>Один шаг сценария ввода (см. описание класса); хозяин гоняет им свой сценарий через верхний экран.</summary>
    public bool Step(string step, out string at)
    {
        at = "-";
        string[] parts = step.Split(' ', 2, StringSplitOptions.TrimEntries);
        string verb = parts[0];
        string arg = parts.Length > 1 ? parts[1] : "";
        if (_overlay is not null && _overlay.Step(verb, arg)) return true;   // окно поверх экрана (UiScreenView.Overlay.cs)

        if (verb == "type")
        {
            foreach (char c in arg) Push(new InputEventKey { Unicode = c, Pressed = true });
            return true;
        }

        if (verb == "wait")
        {
            // Время без мыши: автоповтор зажатой кнопки и отсчёт подсказки («wait 300»).
            if (!int.TryParse(arg, out int ms)) return false;
            Advance(ms);
            return true;
        }

        if (verb == "key")
        {
            if (!int.TryParse(arg, out int vk) || KeyOf(vk) is not Key key) return false;
            Push(new InputEventKey { Keycode = key, Pressed = true });
            Push(new InputEventKey { Keycode = key, Pressed = false });
            return true;
        }

        if (verb == "stick")
        {
            // Левый стик: «stick 1,0 500» — отклонение и сколько мс держать; указатель геймпада сдвигается, как за это время в игре.
            string[] stick = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] xy = stick.Length == 2 ? stick[0].Split(',') : [];
            if (xy.Length != 2 || !float.TryParse(xy[0], System.Globalization.CultureInfo.InvariantCulture, out float sx)
                || !float.TryParse(xy[1], System.Globalization.CultureInfo.InvariantCulture, out float sy)
                || !int.TryParse(stick[1], out int held) || GamepadPointer.Current is not { } pointer) return false;
            pointer.Move(new Vector2(sx, sy), held / 1000.0);
            at = pointer.Line();
            return true;
        }

        if (verb == "pad")
        {
            // Кнопка геймпада по имени JoyButton Godot («pad Y»): нажатие и отпускание — через действия ввода (InputBindings).
            if (!Enum.TryParse(arg, ignoreCase: true, out JoyButton button)) return false;
            Push(new InputEventJoypadButton { ButtonIndex = button, Pressed = true });
            Push(new InputEventJoypadButton { ButtonIndex = button, Pressed = false });
            return true;
        }

        if (verb == "mdrag")
        {
            // Тащение средней кнопкой: «mdrag x,y dx,dy» — от точки разметки на смещение (нажатие, движение, отпускание).
            string[] drag = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (drag.Length != 2 || Point(drag[0]) is not Vector2I start || Point(drag[1]) is not Vector2I by) return false;
            Vector2 a = ViewPoint(start), b = ViewPoint(start + by);
            Push(new InputEventMouseButton { Position = a, ButtonIndex = MouseButton.Middle, Pressed = true, ButtonMask = MouseButtonMask.Middle });
            Push(new InputEventMouseMotion { Position = b, ButtonMask = MouseButtonMask.Middle });
            Push(new InputEventMouseButton { Position = b, ButtonIndex = MouseButton.Middle, Pressed = false });
            at = $"{start.X},{start.Y}";
            return true;
        }

        if (Point(arg) is not Vector2I design) return false;
        at = $"{design.X},{design.Y}";
        // Точка Design → координаты вьюпорта тем же преобразованием, каким рисуется экран.
        Vector2 view = _root!.GetGlobalTransformWithCanvas() * (Vector2)(design - Origin(_state.Screen));
        view += new Vector2(0.5f, 0.5f) * _root.Scale; // середина точки, чтобы обратный перевод не съехал вниз
        switch (verb)
        {
            case "move": Push(new InputEventMouseMotion { Position = view }); break;
            case "press": Push(Button(view, true, false)); break;
            case "release": Push(Button(view, false, false)); break;
            case "click": Push(Button(view, true, false)); Push(Button(view, false, false)); break;
            case "double": Push(Button(view, true, true)); Push(Button(view, false, false)); break;
            case "rpress": Push(new InputEventMouseButton { Position = view, ButtonIndex = MouseButton.Right, Pressed = true }); break;
            case "rrelease": Push(new InputEventMouseButton { Position = view, ButtonIndex = MouseButton.Right, Pressed = false }); break;
            case "wheelup" or "wheeldown":   // щелчок колеса над точкой: нажатие и отпускание, как шлёт Godot
                var wheel = verb == "wheelup" ? MouseButton.WheelUp : MouseButton.WheelDown;
                Push(new InputEventMouseButton { Position = view, ButtonIndex = wheel, Pressed = true, Factor = 1 });
                Push(new InputEventMouseButton { Position = view, ButtonIndex = wheel, Pressed = false, Factor = 1 });
                break;
            default: return false;
        }
        return true;
    }

    /// <summary>Событие сценария — настоящим путём: экран в буфере показа (<see cref="ScreenStage"/>) получает его через корневой вьюпорт
    /// и обратный перевод показа (так проверяется и перевод точек окна при чётком и целом масштабе), экран сам по себе — через свой вьюпорт.</summary>
    private void Push(InputEvent @event)
    {
        if (ScreenStage.Current is { } stage && stage.Stage == GetViewport()) stage.PushFromStage(@event);
        else GetViewport().PushInput(@event, true);
    }

    /// <summary>Точка разметки → координаты вьюпорта экрана (середина точки).</summary>
    private Vector2 ViewPoint(Vector2I design) =>
        (_root!.GetGlobalTransformWithCanvas() * (Vector2)(design - Origin(_state.Screen))) + (new Vector2(0.5f, 0.5f) * _root.Scale);

    private static InputEventMouseButton Button(Vector2 at, bool pressed, bool doubleClick) => new()
    {
        Position = at, ButtonIndex = MouseButton.Left, Pressed = pressed, DoubleClick = doubleClick,
    };

    /// <summary>Точка шага: <c>x,y</c> или имя элемента (середина его прямоугольника).</summary>
    private Vector2I? Point(string arg)
    {
        var xy = arg.Split(',');
        if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) return new(x, y);
        var e = _state.Screen.Elements.FirstOrDefault(el => el.Id == arg);
        return e is null ? null : new Vector2I(e.Rect.X + (e.Rect.Width / 2), e.Rect.Y + (e.Rect.Height / 2));
    }

    public static string Describe(UiEvent e) => e switch
    {
        UiClicked c => $"Clicked:{c.Element}",
        UiToggled t => $"Toggled:{t.Element}={(t.Checked ? 1 : 0)}",
        UiGroupChanged g => $"Group:{g.Element}={g.Index}:{g.Member}",
        UiSpinChanged s => $"Spin:{s.Element}={s.Option}",
        UiListSelected l => $"Selected:{l.Element}={l.Index}",
        UiListConfirmed l => $"Confirmed:{l.Element}={l.Index}",
        UiEdited t => $"Edited:{t.Element}={t.Text.Replace(' ', '_')}",
        _ => e.ToString(),
    };

    // --- клавиши: Godot ↔ виртуальные коды Windows (как в UiElement.Hotkeys) ---

    private static readonly (Key Key, int Vk)[] Special =
    [
        (Key.Backspace, 8), (Key.Tab, 9), (Key.Enter, 13), (Key.KpEnter, 13), (Key.Shift, 16), (Key.Ctrl, 17), (Key.Alt, 18),
        (Key.Pause, 19), (Key.Capslock, 20), (Key.Escape, 27), (Key.Space, 32),
        (Key.Pageup, 33), (Key.Pagedown, 34), (Key.End, 35), (Key.Home, 36), (Key.Left, 37), (Key.Up, 38),
        (Key.Right, 39), (Key.Down, 40), (Key.Insert, 45), (Key.Delete, 46),
    ];

    /// <summary>Буквы и цифры у Godot и Windows совпадают (ASCII), остальное — по таблице; F1–F12 — подряд.</summary>
    private static int? VirtualKey(Key key)
    {
        long code = (long)key;
        if (code is >= 'A' and <= 'Z' or >= '0' and <= '9') return (int)code;
        if (key is >= Key.F1 and <= Key.F12) return 112 + (int)(key - Key.F1);
        foreach (var (k, vk) in Special)
            if (k == key) return vk;
        return null;
    }

    private static Key? KeyOf(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return (Key)vk;
        if (vk is >= 112 and <= 123) return Key.F1 + (vk - 112);
        foreach (var (k, v) in Special)
            if (v == vk) return k;
        return null;
    }
}
