using System.Collections.Generic;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Экран на холсте формы окна (Э5, Р13; <see cref="UiLayout"/>, <c>ui/layout.json</c>). Экран <see cref="UiFit.Anchors"/> раскладывается
/// под холст: элементы держатся своих краёв и растягиваются, фон — по центру; состояние (<see cref="UiScreenState.Place"/>), кадры, надписи и
/// попадания щелчков работают с разложенным экраном. Окно сменило форму — экран раскладывается заново и узлы строятся заново (смена формы
/// окна редка, а у узлов много мест, считанных из прямоугольников). Экран <see cref="UiFit.Letterbox"/> — как прежде, разметка по центру.</summary>
public partial class UiScreenView
{
    /// <summary>Экран после логики (<see cref="IScreenLogic.Shape"/>), до раскладки: от него раскладывается каждый холст.</summary>
    private UiScreen? _shaped;

    /// <summary>Холст экрана-панели в точках хозяина (место хозяина растянуто: место карты <c>IMG_MAIN</c> — для <c>DLG_ISO</c>); null —
    /// разметка самого экрана. Экран без места (<see cref="SlotAt"/>) берёт холст по форме окна.</summary>
    public Vector2I? SlotSize { get; set; }

    /// <summary>Хозяин сменил место или холст (<see cref="SlotAt"/>, <see cref="SlotHost"/>, <see cref="SlotSize"/>) — разложить и вписать заново.</summary>
    public void Fit() => Rescale();

    /// <summary>Холст экрана по нынешнему окну или месту хозяина.</summary>
    private Vector2I CanvasFor(UiScreen shaped)
    {
        if (shaped.Fit == UiFit.Letterbox) return new Vector2I(shaped.Design.Width, shaped.Design.Height);
        if (SlotAt is not null) return SlotSize ?? new Vector2I(shaped.Design.Width, shaped.Design.Height);
        Vector2 view = GetViewportRect().Size;
        var (w, h) = UiLayout.Canvas(shaped, view.X, view.Y);
        return new Vector2I(w, h);
    }

    /// <summary>Разложить экран под нынешний холст (при загрузке, до состояния).</summary>
    private UiScreen PlaceOnCanvas(UiScreen shaped)
    {
        _shaped = shaped;
        var canvas = CanvasFor(shaped);
        return UiLayout.Place(shaped, canvas.X, canvas.Y);
    }

    /// <summary>Сдвиг фона и подложки на разложенном экране: доля прироста холста по якорю фона (<see cref="UiScreen.BackgroundAnchor"/>).</summary>
    private static Vector2I BackgroundShift(UiScreen placed, UiScreen? shaped)
    {
        if (shaped is null) return Vector2I.Zero;
        var (fx, fy) = UiLayout.Share(shaped.BackgroundAnchor ?? UiAnchor.Center);
        return new Vector2I((int)((placed.Design.Width - shaped.Design.Width) * fx), (int)((placed.Design.Height - shaped.Design.Height) * fy));
    }

    /// <summary>Паспарту экрана <see cref="UiFit.Letterbox"/> во всё окно (<see cref="UiScreen.Border"/>, <c>ui/layout.json</c>): кадр по центру
    /// разметки под экраном, точка в точку с ней (дыра <c>BORDER_1280</c> — ровно 800×600); всё, что за краем кадра, — чёрное. Панели и окна
    /// в местах хозяина (<see cref="SlotAt"/>) и окна поверх сцены (не <see cref="Framed"/>) паспарту не рисуют.</summary>
    private void BuildBorder(Node2D root, UiScreen screen)
    {
        if (!(Framed || Hosted is null) || SlotAt is not null || screen.Fit != UiFit.Letterbox || string.IsNullOrEmpty(screen.Border)) return;
        if (FrameImage(screen.Border, out _) is not { } image)
        {
            GD.PrintErr($"{screen.Id}: нет кадра паспарту {screen.Border}");
            return;
        }
        var size = ImageScales.Logical(image);
        root.AddChild(new Sprite2D
        {
            Name = "Border", Texture = Texture(image), Centered = false, TextureFilter = ImageScales.Filter(Texture(image), TextureFilterEnum.ParentNode),
            Position = new Vector2((screen.Design.Width - size.X) / 2, (screen.Design.Height - size.Y) / 2),
        });
    }

    /// <summary>Холст сменился (<see cref="Rescale"/>) — разложить заново и построить узлы. true — перестроено.</summary>
    private bool Relayout()
    {
        if (_shaped is not { Fit: UiFit.Anchors } shaped || _root is null) return false;
        var canvas = CanvasFor(shaped);
        var now = _state.Screen.Design;
        if (canvas.X == now.Width && canvas.Y == now.Height) return false;
        var placed = UiLayout.Place(shaped, canvas.X, canvas.Y);
        _state.Place(placed);
        _design = new Vector2(placed.Design.Width, placed.Design.Height);
        ClearNodes();
        var frames = new List<Frame>();
        var skipped = new List<string>();
        Collect(placed, frames, skipped);
        BuildNodes(placed, frames);
        Refresh();
        return true;
    }

    /// <summary>Строка проверки вписывания: политика, холст, масштаб и угол экрана в окне, паспарту.</summary>
    private string FitLine()
    {
        var design = _state.Screen.Design;
        string border = _root?.GetNodeOrNull<Sprite2D>("Border") is { } b ? $"{_state.Screen.Border}@{b.Position.X},{b.Position.Y}" : "-";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"fit screen={_state.Screen.Id} политика={_state.Screen.Fit} холст={design.Width}x{design.Height} " +
            $"масштаб={_root?.Scale.X ?? 0:0.###} угол={_root?.Position.X ?? 0:0.#},{_root?.Position.Y ?? 0:0.#} паспарту={border}");
    }

    private Sprite2D? _slotAmbient;

    /// <summary>Сдвиг экрана <see cref="UiFit.Letterbox"/> в месте хозяина больше его разметки (<see cref="SlotSize"/>: поле дипломатии
    /// 640×600 в месте карты на холсте шире 800) — по середине места.</summary>
    private Vector2 SlotInset()
    {
        if (_shaped is not { Fit: UiFit.Letterbox } shaped || SlotSize is not { } slot) return Vector2.Zero;
        return new Vector2(Mathf.Max(0, (slot.X - shaped.Design.Width) / 2), Mathf.Max(0, (slot.Y - shaped.Design.Height) / 2));
    }

    /// <summary>Окно во весь экран оригинала (800×600) над сценой: бока холста хозяина закрывает градиент (<see cref="SideFade"/>) — карта и
    /// её панели за окном не видны (гарнизон, войско, торговцы; ответ пользователя 22.10).</summary>
    public bool Backdrop { get; set; }

    /// <summary>Поля вокруг экрана: по середине места (<see cref="SlotInset"/>) — до краёв места, окно во весь экран (<see cref="Backdrop"/>)
    /// — до краёв холста хозяина; градиент <see cref="SideFade"/>, под экраном.</summary>
    private void SlotAmbient()
    {
        if (_slotAmbient is not null && IsInstanceValid(_slotAmbient)) _slotAmbient.QueueFree();
        _slotAmbient = null;
        if (_root is null || SlotAt is not { } at) return;
        var inset = SlotInset();
        Rect2? area = inset != Vector2.Zero && SlotSize is { } slot ? new Rect2(-inset, slot)
            : Backdrop ? new Rect2(-at, SlotHost) : null;
        if (area is not { } a || SideFade.Create(a, new Rect2(Vector2.Zero, _design), 0) is not { } fade) return;
        _slotAmbient = fade;
        _root.AddChild(_slotAmbient);
        _root.MoveChild(_slotAmbient, 0);
    }

    /// <summary>Снять узлы экрана и всё, что на них ссылается.</summary>
    private void ClearNodes()
    {
        if (_root is not null)
        {
            RemoveChild(_root);
            _root.QueueFree();
            _root = null;
        }
        _texts.Clear();
        _rows.Clear();
        _players.Clear();
        _sprites.Clear();
        _cardSlots.Clear();
        _imageless.Clear();
        _wrapping.Clear();
        _boxes.Clear();
        _applied.Clear();
        _overflow.Clear();
        _clips.Clear();
        _tipNode = null;
        _videoLayer = null;
        _backgroundSprite = null;
        _underlaySprite = null;
        _revision = -1;
    }
}
