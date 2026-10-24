using System;
using System.Linq;
using LastEdict.Data.Map;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Показ экранов игры в окне (прогон 3 пользователя 11.10, пункт 1): все экраны — меню, карта, бой — рисуются во внеэкранный буфер
/// <see cref="Stage"/>, а буфер ложится в окно одной картинкой (<see cref="ScreenFit"/>). По умолчанию «чёткий» (sharp bilinear по
/// смыслу): в буфере экраны получают целый масштаб n (ближайший сосед, как и прежде, — сверки точек при k = 1 не меняются), буфер
/// сжимается в окно шейдером усреднения по площади (<see cref="AreaShader"/>). Однобитные линии растровых шрифтов и графики — одной
/// толщины (у каждой линии в точку разметки вес ровно k точек окна, <c>tools/stroke-check.py</c>), размыты только края; при прежнем
/// дробном масштабе 1,2 ближайшим соседом они были то в одну, то в две точки.
///
/// <para>Почему не растяжение окна Godot: режим <c>viewport</c> корневого окна масштабирует картинку на экран ближайшим соседом
/// (проверено на 4.7.2: точки снимка экрана при сжатии 1600→1280 — только 0 и 255), а фильтра или шейдера на этот шаг у Godot нет.
/// Поэтому буфер — свой <see cref="SubViewport"/>, а показ — <see cref="Sprite2D"/> с его текстурой.</para>
///
/// <para>Ввод: корневой вьюпорт отдаёт событие (уже в своих координатах) в <see cref="_UnhandledInput"/>; оно переводится в координаты
/// буфера обратным преобразованием показа и уходит в буфер (<see cref="Viewport.PushInput"/>). Обработал его кто-то в буфере — событие
/// помечается обработанным и в корне. Узел ввода хозяина (<see cref="Hub"/>) стоит в буфере первым: его <c>_Input</c> и
/// <c>_UnhandledInput</c> зовутся после экранов, как прежде у хозяина экранов.</para>
///
/// <para>Корневой вьюпорт в режимах «чёткий» и «целый» — без растяжения (точка вьюпорта = точка окна); в режиме «как прежде» растяжение
/// <c>canvas_items</c> из project.godot остаётся как было, а буфер равен вьюпорту.</para>
/// </summary>
public partial class ScreenStage : Node
{
    /// <summary>Режим вписывания; задаётся до добавления в дерево.</summary>
    public ScreenScaling Mode { get; set; } = ScreenScaling.Sharp;

    /// <summary>Буфер экранов: сюда хозяин кладёт экраны (их <c>GetViewport()</c> — он).</summary>
    public SubViewport Stage { get; } = new() { Name = "Buffer" };

    /// <summary>Узел ввода хозяина внутри буфера (первый ребёнок — его обработчики идут после экранов).</summary>
    public StageHub Hub { get; } = new() { Name = "Hub" };

    /// <summary>Действующий показ (один на запуск) — по нему сценарий ввода экрана переводит точку буфера в точку окна.</summary>
    public static ScreenStage? Current { get; private set; }

    private readonly Sprite2D _view = new() { Name = "View", Centered = false };
    private ScreenFit _fit;

    /// <summary>Сжатие буфера в окно — усреднением по площади точки окна (box filter): каждая точка окна берёт точки буфера, которые она
    /// накрывает, с весом их доли (при сжатии n/k &lt; 2 — до 3×3 выборок точным <c>texelFetch</c>). Билинейная выборка при сжатии 0,6
    /// даёт штриху в точку растра вес то 1,0, то 1,33 (модель в отчёте прогона 3), площадь — ровно 1,2 у каждого: «все линии одной
    /// толщины, смягчение только на границах». Масштаб 1 — ровно одна выборка своей точки (точка в точку).</summary>
    private const string AreaShader = """
        shader_type canvas_item;
        render_mode blend_disabled;

        void fragment() {
            vec2 size = vec2(textureSize(TEXTURE, 0));
            vec2 t = UV * size;
            vec2 w = clamp(fwidth(t), vec2(1.0), vec2(4.0));
            vec2 t0 = t - w * 0.5;
            vec2 t1 = t + w * 0.5;
            ivec2 lo = ivec2(floor(t0 + 1e-4));
            ivec2 hi = ivec2(ceil(t1 - 1e-4)) - 1;
            ivec2 last = ivec2(size) - 1;
            vec4 sum = vec4(0.0);
            for (int y = lo.y; y <= hi.y; y++) {
                float wy = min(t1.y, float(y + 1)) - max(t0.y, float(y));
                for (int x = lo.x; x <= hi.x; x++) {
                    float wx = min(t1.x, float(x + 1)) - max(t0.x, float(x));
                    sum += texelFetch(TEXTURE, clamp(ivec2(x, y), ivec2(0), last), 0) * (wx * wy);
                }
            }
            COLOR = vec4(sum.rgb / (w.x * w.y), 1.0);
        }
        """;

    private static ShaderMaterial? _area;

    /// <summary>Резкость готового кадра окна — CAS (contrast adaptive sharpening, AMD FidelityFX, упрощённо: крест 3×3): вес соседей
    /// отрицательный и тем слабее, чем ближе точка к пределам яркости, — детали резче без ореолов на контрастных краях. Читает кадр окна
    /// (<c>hint_screen_texture</c>) поверх показа, поэтому одинакова в любом режиме вписывания.</summary>
    private const string SharpenShader = """
        shader_type canvas_item;
        render_mode blend_disabled;
        uniform sampler2D frame : hint_screen_texture, filter_nearest;
        uniform float sharpness : hint_range(0.0, 1.0) = 0.4;

        void fragment() {
            vec2 px = SCREEN_PIXEL_SIZE;
            vec3 c = texture(frame, SCREEN_UV).rgb;
            vec3 n = texture(frame, SCREEN_UV + vec2(0.0, -px.y)).rgb;
            vec3 s = texture(frame, SCREEN_UV + vec2(0.0, px.y)).rgb;
            vec3 w = texture(frame, SCREEN_UV + vec2(-px.x, 0.0)).rgb;
            vec3 e = texture(frame, SCREEN_UV + vec2(px.x, 0.0)).rgb;
            vec3 lo = min(c, min(min(n, s), min(w, e)));
            vec3 hi = max(c, max(max(n, s), max(w, e)));
            vec3 amp = sqrt(clamp(min(lo, 1.0 - hi) / max(hi, vec3(1e-4)), 0.0, 1.0));
            vec3 k = amp * (-1.0 / mix(8.0, 5.0, sharpness));
            COLOR = vec4(clamp((c + (n + s + w + e) * k) / (1.0 + 4.0 * k), 0.0, 1.0), 1.0);
        }
        """;

    private CanvasLayer? _sharpen;

    /// <summary>Резкость кадра (<see cref="MapSettings.Sharpness"/>, проценты): 0 — прохода нет (ничего не стоит).</summary>
    public void SetSharpness(int percent)
    {
        if (percent <= 0)
        {
            _sharpen?.QueueFree();
            _sharpen = null;
            return;
        }
        if (_sharpen is null)
        {
            var rect = new ColorRect
            {
                Name = "Sharpen", MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = new ShaderMaterial { Shader = new Shader { Code = SharpenShader } },
            };
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _sharpen = new CanvasLayer { Name = "SharpenLayer", Layer = 120 };
            _sharpen.AddChild(rect);
            AddChild(_sharpen);
        }
        ((ShaderMaterial)((ColorRect)_sharpen.GetChild(0)).Material).SetShaderParameter("sharpness", Math.Min(percent, 100) / 100f);
    }

    /// <summary>Режим, которым экраны на деле ложатся в окно: HD без HD-слоя — чёткий (<see cref="ScreenScaling.Hd"/>).</summary>
    private ScreenScaling Effective => Mode == ScreenScaling.Hd && !GameData.HasHd ? ScreenScaling.Sharp : Mode;

    public ScreenFit Fit => _fit;

    public override void _Ready()
    {
        var root = GetTree().Root;
        // Корень без растяжения: точка вьюпорта = точка окна, экраны масштабирует буфер. Прежний режим без окна (проверки headless) оставляет
        // растяжение проекта — числа его строк в точках вьюпорта 1280×720.
        // Разрешение полного экрана (настройки) — кадр корня в нём (DisplaySettings.RootScale).
        if (DisplaySettings.RenderSize is not null) DisplaySettings.RootScale(root, nearest: false);
        else if (Mode != ScreenScaling.Nearest || DisplayServer.GetName() != "headless") root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        // Буфер — с настройками корня, которыми рисовались экраны (фильтр Nearest проекта: у SubViewport по умолчанию Linear).
        Stage.CanvasItemDefaultTextureFilter = root.CanvasItemDefaultTextureFilter;
        Stage.CanvasItemDefaultTextureRepeat = root.CanvasItemDefaultTextureRepeat;
        Stage.Snap2DTransformsToPixel = root.Snap2DTransformsToPixel;
        Stage.Snap2DVerticesToPixel = root.Snap2DVerticesToPixel;
        Stage.GuiEmbedSubwindows = root.GuiEmbedSubwindows;
        Stage.Msaa2D = root.Msaa2D;
        // Резкость: из настроек, в снимках — ключ --sharpen <проценты>.
        SetSharpness(int.TryParse(MapSource.Arg(OS.GetCmdlineUserArgs(), "--sharpen"), out int sharpen) ? sharpen : DisplaySettings.Sharpness);
        Stage.TransparentBg = false;
        Stage.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Stage.AddChild(Hub);
        AddChild(Stage);
        AddChild(_view);
        _view.Texture = Stage.GetTexture();
        Refit();
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Contains("--stage-pattern")) AddPattern();
        _stats = args.Contains("--stage-stats");
        if (_stats)
        {
            RenderingServer.ViewportSetMeasureRenderTime(Stage.GetViewportRid(), true);
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        }
    }

    /// <summary>Замер кадра (<c>--stage-stats</c>): по выходе — время кадра и время видеокарты на буфер и на окно (показ) за последние
    /// <see cref="StatFrames"/> кадров. Для честного числа — с <c>--disable-vsync --max-fps 0</c>.</summary>
    private bool _stats;

    private const int StatFrames = 300;
    private readonly System.Collections.Generic.Queue<(double Ms, double Stage, double Root)> _frames = new();

    private void Measure(double delta)
    {
        _frames.Enqueue((delta * 1000, RenderingServer.ViewportGetMeasuredRenderTimeGpu(Stage.GetViewportRid()),
                         RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid())));
        while (_frames.Count > StatFrames) _frames.Dequeue();
    }

    private void PrintStats()
    {
        if (!_stats || _frames.Count == 0) return;
        double[] ms = _frames.Select(f => f.Ms).OrderBy(v => v).ToArray();
        double mean = ms.Average(), p95 = ms[(int)(ms.Length * 0.95) - (ms.Length >= 20 ? 1 : 0)];
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"stage stats {Describe()} кадров={ms.Length} кадр_мс={mean:0.00} p95_мс={p95:0.00} fps={1000 / mean:0} " +
            $"gpu_буфер_мс={_frames.Average(f => f.Stage):0.000} gpu_окно_мс={_frames.Average(f => f.Root):0.000}"));
    }

    /// <summary>Проверочная картинка поверх экранов (<c>--stage-pattern</c>, только для <c>tools/stroke-check.py</c>): белый экран 800×600 с
    /// чёрными линиями в одну точку разметки — вертикальными через 7 точек (x = 10, 17, …) в верхней половине и горизонтальными в нижней.
    /// Ставится в буфер так же, как экраны (вписывание по меньшей стороне, ближайший сосед); в снимке окна вес каждой линии — сумма
    /// затемнения поперёк неё — должен быть одинаковым.</summary>
    private void AddPattern()
    {
        var image = Image.CreateEmpty(ScreenFit.DesignWidth, ScreenFit.DesignHeight, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        for (int x = 10; x < ScreenFit.DesignWidth - 10; x += 7)
            image.FillRect(new Rect2I(x, 10, 1, 280), Colors.Black);
        for (int y = 310; y < ScreenFit.DesignHeight - 10; y += 7)
            image.FillRect(new Rect2I(10, y, ScreenFit.DesignWidth - 20, 1), Colors.Black);
        var pattern = new Sprite2D { Name = "Pattern", Centered = false, Texture = ImageTexture.CreateFromImage(image) };
        var layer = new CanvasLayer { Name = "PatternLayer", Layer = 100 };
        layer.AddChild(pattern);
        Stage.AddChild(layer);
        void Place()
        {
            var (at, k) = UiScreenView.Letterbox(Stage.GetVisibleRect().Size, new Vector2(ScreenFit.DesignWidth, ScreenFit.DesignHeight));
            pattern.Position = at;
            pattern.Scale = new Vector2(k, k);
        }
        Place();
        Stage.SizeChanged += Place;
    }

    public override void _EnterTree()
    {
        Current = this;
        GetViewport().SizeChanged += Refit;
    }

    public override void _ExitTree()
    {
        PrintStats();
        GetViewport().SizeChanged -= Refit;
        if (Current == this) Current = null;
    }

    /// <summary>Сменить режим вписывания на ходу (окно настроек, «Применить»): растяжение корня и раскладка показа заново.</summary>
    public void SetMode(ScreenScaling mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        RefreshRootScale();
    }

    /// <summary>Растяжение корня по вписыванию и разрешению полного экрана (<see cref="DisplaySettings.RootScale"/>), раскладка заново.</summary>
    public void RefreshRootScale()
    {
        DisplaySettings.RootScale(GetTree().Root, Mode == ScreenScaling.Nearest);
        Refit();
    }

    /// <summary>Раскладка по размеру корневого вьюпорта: размер буфера, масштаб, угол и фильтр картинки показа.</summary>
    private void Refit()
    {
        if (!IsNodeReady()) return;
        Vector2 size = WindowSize() ?? GetViewport().GetVisibleRect().Size;
        _fit = ScreenFit.For(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y), Effective);
        Stage.Size = new Vector2I(_fit.StageWidth, _fit.StageHeight);
        _view.Position = new Vector2(_fit.X, _fit.Y);
        _view.Scale = new Vector2(_fit.Scale, _fit.Scale);
        _view.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        _view.Material = _fit.Filtered ? _area ??= new ShaderMaterial { Shader = new Shader { Code = AreaShader } } : null;
    }

    /// <summary>Размер окна для проверки без окна (<see cref="GameData.HeadlessWindow"/>: <c>--window-size</c>, по умолчанию 1280×720): у headless
    /// окно 64×64, а раскладку и перевод щелчков надо проверить на настоящем размере. Перевод точек от этого не зависит — он весь в
    /// <see cref="ToRoot"/>.</summary>
    private static Vector2? WindowSize() => GameData.HeadlessWindow is { } window ? new Vector2(window.X, window.Y) : null;

    /// <summary>Точка буфера → точка корневого вьюпорта.</summary>
    public Transform2D ToRoot => new(0f, new Vector2(_fit.Scale, _fit.Scale), 0f, new Vector2(_fit.X, _fit.Y));

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsNodeReady()) return;
        Stage.PushInput(@event.XformedBy(ToRoot.AffineInverse()), true);
        if (Stage.IsInputHandled()) GetViewport().SetInputAsHandled();
    }

    /// <summary>Событие в координатах буфера — настоящим путём, через корневой вьюпорт и обратный перевод показа (сценарии проверок).</summary>
    public void PushFromStage(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!IsNodeReady()) return;
        GetViewport().PushInput(@event.XformedBy(ToRoot), true);
    }

    private string? _shot;
    private double _shotAt;
    private double _clock;

    /// <summary>Снимок окна с окном (<c>--stage-shot файл.png</c>, <c>--stage-shot-ms</c> — через сколько, по умолчанию 3000): картинка корневого
    /// вьюпорта — ровно точки окна (в режимах «чёткий» и «целый» корень без растяжения) — и выход. Для сверки толщины штрихов
    /// (<c>tools/stroke-check.py</c>).</summary>
    public override void _Process(double delta)
    {
        if (_stats) Measure(delta);
        if (_shot is null)
        {
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--stage-shot");
            if (at < 0 || at + 1 >= args.Length)
            {
                if (!_stats) SetProcess(false);
                return;
            }
            _shot = args[at + 1];
            int ms = Array.IndexOf(args, "--stage-shot-ms");
            _shotAt = ms >= 0 && ms + 1 < args.Length && int.TryParse(args[ms + 1], out int v) ? v / 1000.0 : 3.0;
        }
        if ((_clock += delta) < _shotAt) return;
        GetViewport().GetTexture().GetImage().SavePng(GameData.FromProject(_shot));
        GD.Print($"stage shot={_shot} {Describe()}");
        GetTree().Quit(0);
        _shotAt = double.MaxValue;
    }

    /// <summary>Строка проверки: режим, размер буфера, масштаб показа и фильтр.</summary>
    public string Describe() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"показ={Mode.ToString().ToLowerInvariant()} буфер={_fit.StageWidth}x{_fit.StageHeight} масштаб={_fit.Scale:0.####} " +
        $"угол={_fit.X},{_fit.Y} фильтр={(_fit.Filtered ? "area" : "nearest")}");
}

/// <summary>Узел ввода хозяина экранов внутри буфера (<see cref="ScreenStage.Hub"/>): <c>_Input</c> и <c>_UnhandledInput</c> — его
/// обработчикам.</summary>
public partial class StageHub : Node
{
    public Action<InputEvent>? OnInput { get; set; }
    public Action<InputEvent>? OnUnhandled { get; set; }

    public override void _Input(InputEvent @event) => OnInput?.Invoke(@event);

    public override void _UnhandledInput(InputEvent @event) => OnUnhandled?.Invoke(@event);
}
