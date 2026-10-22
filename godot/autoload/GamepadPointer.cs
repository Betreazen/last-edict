using System;
using System.Linq;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Указатель геймпада (Э5, Р12; Steam Deck): у игры всё делается мышью — карта, бой, перетаскивание, — поэтому геймпад водит саму мышь.
/// Левый стик двигает указатель (действия <c>pointer.left/right/up/down</c>, скорость растёт с отклонением), A — щелчок левой
/// (<c>pointer.click</c>: нажатие и отпускание вслед за кнопкой, так работает и перетаскивание), X — правой (<c>pointer.alt</c>: карточки).
/// Крестовина на окнах переводит указатель на соседнюю кнопку (<see cref="MoveTo"/> из <c>UiScreenView</c>, <c>UiNavigation</c>), а в окнах на
/// <c>Control</c> — к элементу с фокусом. События мыши идут настоящим путём — через корневой вьюпорт (как у сценариев проверок), с меткой
/// <see cref="DeviceId"/>; в окне ОС указатель ещё и переносится (<c>WarpMouse</c>), чтобы курсор игры стоял, где щёлкают.
/// </summary>
public partial class GamepadPointer : Node
{
    /// <summary>Устройство событий мыши от геймпада: по нему их отличают от настоящей мыши.</summary>
    public const int DeviceId = 0x7E57;

    public const string Left = "pointer.left", Right = "pointer.right", Up = "pointer.up", Down = "pointer.down",
        Click = "pointer.click", Alt = "pointer.alt";

    private static readonly StringName LeftName = Left, RightName = Right, UpName = Up, DownName = Down;

    /// <summary>Скорость при полном отклонении — доля высоты вьюпорта в секунду.</summary>
    private const float Speed = 1.25f;

    public static GamepadPointer? Current { get; private set; }

    /// <summary>Точка указателя в координатах корневого вьюпорта.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Последний ввод — геймпад (подсказки кнопок, шаг к фокусу окна); клавиатура или мышь — нет. Пока не геймпад, указатель и
    /// подсказки кадр не тратят: их <c>_Process</c> выключен.</summary>
    public static bool PadActive { get; private set; }

    private void SetPad(bool active)
    {
        if (active == PadActive) return;
        PadActive = active;
        SetProcess(active);
        if (active && PadHints.Current is null) AddChild(new PadHints { Name = "PadHints" });   // слой — с первого нажатия геймпада
        PadHints.Current?.Activate(active);
    }

    private Control? _focus;

    /// <summary>Куда указатель перенесён в окне ОС (<c>WarpMouse</c>): ОС присылает эхо этого переноса настоящим движением мыши в целых
    /// точках — оно не должно сбрасывать дробную точку указателя (тонкая наводка стиком — меньше точки за кадр).</summary>
    private Vector2? _warped;

    /// <summary>Сдвиг настоящей мыши, после которого играют мышью (тачпад Steam Deck), а не геймпадом.</summary>
    private const float MouseMoved = 3f;

    /// <summary>Кнопки мыши, зажатые геймпадом.</summary>
    private static readonly System.Collections.Generic.HashSet<MouseButton> Held = [];

    /// <summary>Кнопку мыши держит геймпад (у <c>Input</c> её состояния нет: события идут мимо него).</summary>
    public static bool Holding(MouseButton button) => Held.Contains(button);

    public override void _EnterTree() => Current = this;

    public override void _ExitTree()
    {
        ReleaseAll();
        Input.JoyConnectionChanged -= JoyChanged;
        UiScale.Changed -= AnimatedCursor.Refresh;
        if (Current == this) Current = null;
    }

    /// <summary>Окно потеряло фокус — зажатые геймпадом кнопки мыши отпускаются (отпускание кнопки геймпада могло уйти мимо окна).</summary>
    public override void _Notification(int what)
    {
        if (what is (int)NotificationWMWindowFocusOut or (int)NotificationApplicationFocusOut) ReleaseAll();
    }

    private void ReleaseAll()
    {
        foreach (var button in Held.ToArray()) Press(button, false);
    }

    /// <summary>Геймпад подключили или сняли: зажатое отпускается, «крупный интерфейс: сам» смотрит заново (Steam Deck).</summary>
    private void JoyChanged(long device, bool connected)
    {
        ReleaseAll();
        UiScale.Recheck();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(false);
        UiScale.Changed += AnimatedCursor.Refresh;
        Input.JoyConnectionChanged += JoyChanged;
        Position = GetViewport().GetVisibleRect().Size / 2;
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventJoypadButton:
            case InputEventJoypadMotion motion when Math.Abs(motion.AxisValue) >= InputBindings.PointerDeadzone:
                SetPad(true);
                break;
            case InputEventKey { Pressed: true }:
            case InputEventMouseButton { Pressed: true } mouse when mouse.Device != DeviceId:
                SetPad(false);
                break;
        }
        if (@event is InputEventMouseMotion real && real.Device != DeviceId)
        {
            if (_warped is { } warped && real.Position.DistanceTo(warped) < 1.5f) return;   // эхо своего переноса
            _warped = null;
            Position = real.Position;
            if (real.Relative.Length() >= MouseMoved) SetPad(false);
        }
        if (@event is not (InputEventJoypadButton or InputEventJoypadMotion) || !InputBindings.Has(Click)) return;
        foreach (var (action, button) in new[] { (Click, MouseButton.Left), (Alt, MouseButton.Right) })
        {
            if (@event.IsActionPressed(action)) Press(button, true);
            else if (@event.IsActionReleased(action)) Press(button, false);
            else continue;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!InputBindings.Has(Left)) return;
        // Геймпад шлёт события и окну в фоне — системный курсор угоняется только у окна с фокусом.
        if (DisplayServer.GetName() != "headless" && !GetWindow().HasFocus()) return;
        var stick = Input.GetVector(LeftName, RightName, UpName, DownName);
        if (stick != Vector2.Zero) Move(stick, delta);
        FollowFocus();
    }

    /// <summary>Сдвиг стиком за <paramref name="seconds"/>: скорость — квадрат отклонения (точно у центра, быстро у края).</summary>
    public void Move(Vector2 stick, double seconds)
    {
        float height = GetViewport().GetVisibleRect().Size.Y;
        MoveTo(Position + (stick * stick.Length() * Speed * height * (float)seconds));
    }

    /// <summary>Указатель — в точку корневого вьюпорта (в пределах окна).</summary>
    public void MoveTo(Vector2 point)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var to = new Vector2(Math.Clamp(point.X, 0, size.X - 1), Math.Clamp(point.Y, 0, size.Y - 1));
        var relative = to - Position;
        Position = to;
        Push(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = relative, Device = DeviceId });
        if (DisplayServer.GetName() == "headless") return;
        _warped = to;
        GetViewport().WarpMouse(to);
    }

    private void Press(MouseButton button, bool pressed)
    {
        if (pressed) Held.Add(button);
        else Held.Remove(button);
        Push(new InputEventMouseButton
        {
            Position = Position, GlobalPosition = Position, ButtonIndex = button, Pressed = pressed, Device = DeviceId,
            ButtonMask = pressed ? (button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
        });
    }

    /// <summary>Событие мыши — сразу, тем же путём, что сценарии проверок (порядок шагов сохраняется).</summary>
    private void Push(InputEvent @event) => GetViewport().PushInput(@event, true);

    /// <summary>Окно на <c>Control</c> (настройки) сменило фокус крестовиной — указатель к нему: A щёлкает то, что выделено.</summary>
    private void FollowFocus()
    {
        var stage = ScreenStage.Current;   // экраны в буфере показа — фокус там; карта без буфера (проверки) — в корне
        var owner = (stage?.Stage ?? GetViewport()).GuiGetFocusOwner();
        if (owner == _focus) return;
        _focus = owner;   // фокус, поставленный мышью, указатель не тянет: только смена фокуса, пока играют геймпадом
        if (!PadActive || owner is null || !owner.IsVisibleInTree()) return;
        var middle = owner.GetGlobalRect().GetCenter();
        MoveTo(stage is null ? middle : stage.ToRoot * middle);
    }

    /// <summary>Строка проверки.</summary>
    public string Line() => FormattableString.Invariant($"pointer {Position.X:0},{Position.Y:0} геймпад={(PadActive ? 1 : 0)} крупный={(UiScale.Large ? 1 : 0)}");
}
