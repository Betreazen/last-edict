using System.Globalization;
using LastEdict.Data.Content;
using LastEdict.Data.Input;
using LastEdict.Data.Menu;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Имя и описание постройки (<c>Gbuild.NAME_TXT</c>/<c>DESC_TXT</c>).</summary>
public sealed record BuildingText(string NameTxt, string DescTxt);

/// <summary>Вид значка постройки (флаги <c>0x4acc39</c>/<c>0x4acf0d</c>): можно строить, построена, не построена предыдущая,
/// выбран другой путь, выше потолка уровня сценария (значка нет).</summary>
public enum BuildIcon { Can, Built, NeedPrevious, OtherPath, Hidden }

/// <summary>Значок постройки в дереве: место значка и плашки подписи (точки содержимого), кадр (с подкраской вида), подпись.</summary>
/// <param name="Marked">Помечен (живая страница Студии: постройка мода).</param>
public sealed record BuildTreeItem(string Building, UiRect Icon, UiRect Plate, string Frame, string Name, BuildIcon Kind, bool Selected, bool Hovered,
                                   bool Marked = false);

/// <summary>Связь «требуемая → постройка» в дереве (точки содержимого): от низа плашки требуемой к верху значка; <see cref="Built"/> —
/// требуемая построена.</summary>
public sealed record BuildTreeLink(UiRect From, UiRect To, bool Built);

/// <summary>Вкладка ветки над деревом (точки разметки экрана).</summary>
public sealed record BuildTreeTab(string Branch, UiRect Rect, string Name, bool Open, bool Hovered);

/// <summary>
/// Окно построек столицы (<c>DLG_BUILD_STRUCTURE</c>, <c>CBuildStructInterf</c> <c>0x4aac6b</c>; Last Edict — Э6): справа — панель
/// «Управление городом» разметки оригинала (выбранная постройка, <c>0x4ab5cc</c>), слева — дерево построек данными (<see cref="BuildTree"/>:
/// ветки — содержимое, связи — требования, место — автораскладкой или из мода) в виде с зумом и сдвигом (<see cref="ZoomView"/>): колесо —
/// зум к курсору, тащение средней кнопкой или левой по пустому месту — сдвиг, вид ограничен рамкой дерева; PageUp/PageDown (курки геймпада)
/// и +/− — зум к середине. Ветки — только вкладками над деревом (кнопки круга <c>RAD_BUILDING_TYPE</c> оригинала спрятаны: в круге ровно
/// пять гнёзд, а веток — сколько в данных; решение пользователя 24.10). Рисует дерево вид Godot (<c>BuildTreeView</c>) по <see cref="Items"/>,
/// <see cref="Links"/>, <see cref="Tabs"/> и <see cref="View"/>; решения и попадания — здесь, обратным преобразованием вида.
/// <para>Правила окна — как у оригинала. Значок — постройка (<c>IconBld</c>, подкраска по виду <c>0x5b6c9d</c>). «Построить» доступна у
/// значка «можно строить»; нажатие проверяет казну (<c>X005TA0120</c>) и «раз в день» (<c>X005TA0239</c>), затем спрашивает <c>X005TA0240</c>.
/// Открывается на последней выбранной за партию ветке (<c>0x835a8c</c>), в начале — первой. Справа у постройки отрядов (<c>0x4ab5cc</c>): кто
/// в ней живёт — воин с <c>UPGRADE_B</c> = постройка (<c>X005TA0384</c>) или, если она нанимает, «Особый отряд» (<c>ENROLL_B</c>,
/// <c>X005TA0677</c>); «Тренируется: предыдущий» (<c>X005TA0383</c>), «В вашей армии» (<c>X005TA0282</c>) и, если строить можно или нужна
/// предыдущая, «Ждёт повышения» (<c>X005TA0381</c>). Выбор значка «можно строить» временно, до следующего щелчка, красит «другим путём»
/// соседние постройки той же ветки со статусом «можно» и всё, что от них растёт (<c>0x4acf0d</c>) — что станет недоступно после постройки.
/// Правая кнопка по значку — энциклопедия постройки (<see cref="BuildingCardLogic"/>, <c>0x4ac584</c>).</para>
/// </summary>
public sealed class BuildLogic : IWindowLogic, IPadTargets, IBuildTreeSource
{
    public const string Screen = "DLG_BUILD_STRUCTURE";
    private const string Group = "RAD_BUILDING_TYPE", IconArchive = "IconBld";

    private static readonly UiColor BuiltTint = new(0, 0, 196), PreviousTint = new(255, 0, 0), OtherTint = new(0, 0, 0);

    private readonly WindowContext _c;
    private readonly IReadOnlyDictionary<string, BuildingText> _texts;
    private readonly UiRequests _requests = new();

    /// <summary>Фон дерева построек (ответ пользователя 22.10): серый градиент во всю ширину слева от панели «Управление городом»; на нём
    /// вкладки веток и вид дерева.</summary>
    public const string TreeBackground = "IMG_TREE_BG";

    /// <summary>Ширина дерева в разметке 800×600 — до панели «Управление городом» (<c>IMG_FRAME_DLG</c>).</summary>
    private const int TreeWidth = 437;

    /// <summary>Тон фона дерева (сверху; к низу темнее на четверть).</summary>
    private const string TreeTone = "4A4844";   // ответ пользователя 22.10: прежний 7A7874 — слишком светлый

    /// <summary>Полоса вкладок веток над деревом: высота, поля, наибольшая ширина вкладки.</summary>
    public const int TabHeight = 26, TabGap = 4, TabPad = 6, TabMaxWidth = 150;

    /// <summary>Тащение дальше стольких точек — сдвиг вида, а не щелчок.</summary>
    private const int DragSlop = 3;

    /// <summary>Шаг зума клавишей (курок геймпада, PageUp/PageDown, +/−).</summary>
    private const double KeyZoom = 1.25;

    /// <summary>Разложенный экран последнего обновления: место фона дерева на холсте формы окна (раскладка по якорям растягивает его).</summary>
    private UiScreen? _placed;

    public BuildLogic(WindowContext context, BuildTree tree, IReadOnlyDictionary<string, BuildingText> texts)
    {
        _c = context ?? throw new ArgumentNullException(nameof(context));
        Tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

    /// <summary>Окно на дереве построек лорда игрока из содержимого <paramref name="content"/>.</summary>
    public static BuildLogic For(WindowContext context, ContentSet content, IReadOnlyDictionary<string, BuildingText> texts)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new BuildLogic(context, BuildTree.OfLord(content, context.Player.LordId), texts);
    }

    /// <summary>Имена и описания построек содержимого.</summary>
    public static IReadOnlyDictionary<string, BuildingText> LoadTexts(string rulesDir) => LoadTexts(ContentSource.Open(rulesDir));

    public static IReadOnlyDictionary<string, BuildingText> LoadTexts(ContentSet content) =>
        content.Buildings.ToDictionary(
            b => b.Key.ToUpperInvariant(), b => new BuildingText(b.NameText ?? "", b.DescriptionText ?? ""), StringComparer.OrdinalIgnoreCase);

    /// <summary>Дерево построек окна.</summary>
    public BuildTree Tree { get; }

    /// <summary>Вид на дерево открытой ветки (точки вида — от угла <see cref="TreeArea"/>).</summary>
    public ZoomView View { get; } = new();

    /// <summary>Открытая ветка — id ветки содержимого.</summary>
    public string Branch { get; private set; } = "";

    private BuildTreeBranch? Current => Tree.Branch(Branch);

    /// <summary>Номер перерисовки дерева: растёт с каждой переменой вида, ветки, выбора или наведения (вид Godot рисует заново).</summary>
    public int Revision { get; private set; }

    /// <summary>Выбранная постройка; нет — null.</summary>
    public string? Selected
    {
        get => _selected;
        private set
        {
            _selected = value;
            Shade();
        }
    }

    private string? _selected;

    /// <summary>Постройка под мышью; нет — null.</summary>
    private string? _hovered;

    /// <summary>Вкладка под мышью; нет — null.</summary>
    private string? _hoveredTab;

    /// <summary>Кнопка под мышью; нет — null.</summary>
    private string? _hoveredButton;

    /// <summary>Постройки, временно показанные «другим путём» от текущего выбора.</summary>
    private readonly HashSet<string> _shaded = new(StringComparer.OrdinalIgnoreCase);

    public string? LastRefusal { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary =>
        $"ветка={Branch} построек={Current?.Nodes.Count ?? 0} выбрано={Selected ?? "-"} " +
        $"построено={_c.Player.Buildings.Count} золото={_c.Player.Bank.Gold} значки={Icons()} отказ={(LastRefusal ?? "-").Replace(' ', '_')} " +
        $"портрет={_faceFrame ?? "-"} " +
        string.Create(CultureInfo.InvariantCulture, $"зум={View.Zoom:0.###} вид={View.X:0.#},{View.Y:0.#} вкладок={Tree.Branches.Count}");

    /// <summary>Значки открытой ветки для проверки: «постройка@x,y:вид» — середина значка в точках разметки экрана; <c>+</c> можно, <c>b</c>
    /// построена, <c>r</c> нет предыдущей, <c>k</c> другой путь (скрытые не печатаются).</summary>
    private string Icons() => Items.Count == 0 ? "-" : string.Join(",", Items.Select(i =>
    {
        var (x, y) = ScreenPoint(i.Icon.X + (i.Icon.Width / 2.0), i.Icon.Y + (i.Icon.Height / 2.0));
        return string.Create(CultureInfo.InvariantCulture, $"{i.Building}@{x:0},{y:0}:") + i.Kind switch
        {
            BuildIcon.Built => "b", BuildIcon.Can => "+", BuildIcon.NeedPrevious => "r", _ => "k",
        };
    }));

    /// <summary>Окно на холсте формы окна (<c>ui/layout.json</c>: Anchors): панели «Управление городом» оригинала нет (решение пользователя
    /// 24.10) — фон дерева во всё окно без картинки оригинала, вкладки веток сверху, справа в их полосе — золото (<c>IMG_MANA_GOLD</c>,
    /// <c>TXT_GOLD</c>) и «Закрыть» (<c>BTN_CLOSE</c>: Esc, B геймпада, граф окон — как прежде); сведения о выбранной постройке — карточкой у
    /// правого края (<see cref="Card"/>, место — <see cref="CardId"/>), её «Построить» — кнопка разметки <c>BTN_BUILD</c> (Enter, Y геймпада).
    /// Кнопки круга веток, рамка панели и прочие её элементы убраны.</summary>
    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        int w = screen.Design.Width, h = screen.Design.Height;
        UiElement? Of(string id) => screen.Elements.FirstOrDefault(e => e.Id == id);
        var elements = new List<UiElement>
        {
            new() { Id = TreeBackground, Kind = UiKind.Image, Rect = new UiRect(0, 0, w, h), Stretch = UiStretch.Width, Anchor = UiAnchor.TopLeft },
        };
        int top = TabPad + TabHeight + TabGap;
        var card = new UiRect(w - CardMargin - CardWidth, top, CardWidth, h - top - CardMargin);
        elements.Add(new UiElement { Id = CardId, Kind = UiKind.Image, Rect = card, Anchor = UiAnchor.TopRight });
        // «Назад» и «Построить» — кнопки разметки оригинала без его картинок (решение пользователя 24.10: в окне построек ничего от старого
        // интерфейса): место щелчка, Esc/Enter, граф окон и геймпад — прежние, рисует их вид дерева в теме Last Edict (Buttons).
        if (Of("BTN_CLOSE") is { } close)
            elements.Add(close with
            {
                Rect = new UiRect(w - TabPad - CloseWidth, TabPad, CloseWidth, TabHeight), Anchor = UiAnchor.TopRight, Sprites = new UiSprites(),
            });
        if (Of("BTN_BUILD") is { } build)
            elements.Add(build with
            {
                Rect = new UiRect(card.X + 16, card.Bottom - 16 - BuildHeight, card.Width - 32, BuildHeight), Anchor = UiAnchor.TopRight,
                Sprites = new UiSprites(),
            });
        return screen with { Elements = elements, Background = "" };
    }

    /// <summary>Ширина малой рамки портрета <c>BORDERUNITSMALL</c> и большой <c>BORDERUNITLARGE</c> (<see cref="LastEdict.Data.Map.MapCards"/>,
    /// 84×99 и 164×99): у большого воина место портрета — во всю ширину большой рамки (<c>0x4abb69</c>).</summary>
    private const int FaceWidth = 84, BigFaceWidth = 164;

    /// <summary>Место карточки выбранной постройки (элемент без картинки: его рисует вид дерева).</summary>
    public const string CardId = "BUILD_CARD";

    /// <summary>Ширина карточки и её отступ от краёв (точки разметки).</summary>
    public const int CardWidth = 290, CardMargin = 8;

    /// <summary>Сколько точек полосы вкладок справа занято золотом и «Назад».</summary>
    private const int TabReserve = 230;

    /// <summary>Ширина «Назад» в полосе вкладок и высота «Построить» на карточке.</summary>
    private const int CloseWidth = 96, BuildHeight = 32;

    /// <summary>Кнопка окна в теме Last Edict (место — точки разметки): подпись, доступна ли, под мышью ли.</summary>
    public sealed record BuildButton(string Id, UiRect Rect, string Label, bool Enabled, bool Hovered);

    /// <summary>Кнопки окна («Назад», «Построить» — когда есть карточка), рисует вид дерева.</summary>
    public IReadOnlyList<BuildButton> Buttons { get; private set; } = [];

    /// <summary>Золото игрока строкой («Золото: 5000») — в полосе вкладок слева от «Назад».</summary>
    public string Gold { get; private set; } = "";

    /// <summary>Место строки золота (точки разметки).</summary>
    public UiRect GoldRect => RectOf("BTN_CLOSE") is { } close ? new UiRect(close.X - 130, close.Y, 124, close.Height) : default;

    /// <summary>Кнопки по состоянию экрана: подпись — своя строка Last Edict, доступность и наведение — из состояния.</summary>
    private void CollectButtons(UiScreenState ui)
    {
        var buttons = new List<BuildButton>();
        foreach (var (id, key) in (ReadOnlySpan<(string, string)>)[("BTN_CLOSE", "LE_BUILD_BACK"), ("BTN_BUILD", "LE_BUILD_BUILD")])
        {
            if (RectOf(id) is not { } rect || ui.Screen.Elements.All(e => e.Id != id) || ui.IsHidden(id)) continue;
            buttons.Add(new BuildButton(id, rect, _c.Text(key), ui.IsEnabled(id), ui.Hovered == id));
        }
        Buttons = buttons;
    }

    /// <summary>Сведения о выбранной постройке для карточки: имя, цена, описание, портрет воина постройки (кадр; большой — шире), строки о
    /// нём (живёт, тренируется, в армии, ждут повышения) и статус. Место — <see cref="Rect"/> (точки разметки).</summary>
    public sealed record BuildCard(UiRect Rect, string Title, string Cost, string Description, string? Face, bool FaceBig,
                                   IReadOnlyList<string> UnitLines, string Status, BuildIcon Kind);

    /// <summary>Карточка выбранной постройки; ничего не выбрано — null (карточки нет).</summary>
    public BuildCard? Card { get; private set; }

    /// <summary>Место портрета на карточке (точки разметки): малый — 84×99, большой — шире (рамка 164×99).</summary>
    public static UiRect FaceRect(BuildCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        int width = card.FaceBig ? BigFaceWidth : FaceWidth;
        return new UiRect(card.Rect.X + 14, card.Rect.Y + 196, width, 99);
    }

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var remembered = Tree.Branch(_c.Memory.BuildBranch) ?? Tree.Branches.FirstOrDefault(b => b.Nodes.Count > 0) ?? Tree.Branches.FirstOrDefault();
        // Ветки — только вкладками над деревом (решение пользователя 24.10): кнопки круга оригинала спрятаны.
        foreach (string toggle in Toggles(ui)) ui.SetHidden(toggle, true);
        Show(remembered?.Id ?? "", ui);
    }

    public void Resume(UiScreenState ui) => Refresh(ui);

    /// <summary>Переключатели круга веток на экране (участники группы).</summary>
    private static IReadOnlyList<string> Toggles(UiScreenState ui) =>
        ui.Screen.Elements.FirstOrDefault(e => e.Id == Group)?.Members ?? [];

    /// <summary>Открыть ветку <paramref name="branch"/>: вид — заново.</summary>
    private void Show(string branch, UiScreenState ui)
    {
        Branch = _c.Memory.BuildBranch = branch;
        _area = null;   // вид — заново по размеру содержимого ветки
        Refresh(ui);
    }

    /// <summary>«Построить» спрашивает сама логика (окно вопроса с ценой).</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_BUILD";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiClicked { Element: "BTN_BUILD" }:
                Build();
                break;
        }
        Refresh(ui);
    }

    // ---- мышь и клавиши над деревом

    /// <summary>Нажатие левой: на пустом месте дерева — начало тащения.</summary>
    private (int X, int Y)? _press;

    private bool _dragged;

    public void Press(int x, int y, UiScreenState ui)
    {
        _press = InArea(x, y) ? (x, y) : null;
        _dragged = false;
    }

    public void Motion(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (_press is { } from && NodeAt(from.X, from.Y) is null)
        {
            if (!_dragged && Math.Abs(x - from.X) + Math.Abs(y - from.Y) > DragSlop) _dragged = true;
            if (_dragged)
            {
                if (View.Pan(x - from.X, y - from.Y)) Revision++;
                _press = (x, y);
            }
        }
        string? hovered = NodeAt(x, y)?.Building, tab = TabAt(x, y)?.Branch;
        string? button = Buttons.FirstOrDefault(b => Inside(b.Rect, x, y))?.Id;
        if (hovered == _hovered && tab == _hoveredTab && button == _hoveredButton) return;
        (_hovered, _hoveredTab, _hoveredButton) = (hovered, tab, button);
        Buttons = [.. Buttons.Select(b => b with { Hovered = b.Id == button })];
        Revision++;
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        bool dragged = _dragged;
        (_press, _dragged) = (null, false);
        if (dragged) return;
        if (TabAt(x, y) is { } tab)
        {
            if (tab.Branch != Branch) Show(tab.Branch, ui);
            return;
        }
        if (NodeAt(x, y) is not { } node) return;
        Selected = node.Building;
        Refresh(ui);
    }

    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (NodeAt(x, y) is { } node) _requests.Add(new UiOpen(BuildingCardLogic.Screen, new BuildingCardLogic(_c, node.Building, _texts)));
        else if (_faceUnit is { } unit && Card is { } card && Inside(FaceRect(card), x, y))
            _requests.Add(new UiOpen(UnitCardLogic.Screen, UnitCardLogic.OfType(_c, unit)));
    }

    /// <summary>Двойной щелчок по значку — «Построить», если кнопка доступна (<c>0x4add6c</c> → <c>0x4ab0f4</c>).</summary>
    public void DoubleClick(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (NodeAt(x, y) is not { } node) return;
        Selected = node.Building;
        Build();
        Refresh(ui);
    }

    /// <summary>Колесо над деревом — зум к курсору.</summary>
    public bool Wheel(int x, int y, int steps, UiScreenState ui)
    {
        if (!InArea(x, y) || _area is not { } area) return false;
        if (View.ZoomAt(x - area.X, y - area.Y, Math.Pow(ZoomView.WheelStep, steps))) Revision++;
        return true;
    }

    /// <summary>Тащение средней кнопкой над деревом — сдвиг.</summary>
    public bool Pan(int x, int y, int dx, int dy, UiScreenState ui)
    {
        if (!InArea(x, y)) return false;
        if (View.Pan(dx, dy)) Revision++;
        return true;
    }

    /// <summary>PageUp/PageDown (курки геймпада) и +/− — зум к середине дерева.</summary>
    public bool Key(int vk, UiScreenState ui)
    {
        double factor = vk switch
        {
            0x21 or 0x6B or 0xBB => KeyZoom,           // PageUp, + цифровой, =/+
            0x22 or 0x6D or 0xBD => 1 / KeyZoom,       // PageDown, − цифровой, −
            _ => 0,
        };
        if (factor == 0 || _area is not { } area) return false;
        if (View.ZoomAt(area.Width / 2.0, area.Height / 2.0, factor)) Revision++;
        return true;
    }

    /// <summary>Постройка, чей значок или подпись под точкой разметки; нет — null. Ищется среди собранных значков (<see cref="Items"/>: скрытые
    /// уже отброшены) — мышь зовёт это на каждое движение, а вид значка считает правила.</summary>
    private BuildTreeItem? NodeAt(int x, int y)
    {
        if (!InArea(x, y) || _area is not { } area || (Card is { } card && Inside(card.Rect, x, y))) return null;
        var (cx, cy) = View.ToContent(x - area.X, y - area.Y);
        return Items.LastOrDefault(i => Contains(i.Icon, cx, cy) || Contains(i.Plate, cx, cy));
    }

    private static bool Contains(UiRect r, double x, double y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;

    private BuildTreeTab? TabAt(int x, int y) => Tabs.FirstOrDefault(t => Inside(t.Rect, x, y));

    private bool InArea(int x, int y) => _area is { } area && Inside(area, x, y);

    private static bool Inside(UiRect r, int x, int y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;

    /// <summary>Точка содержимого → точка разметки экрана.</summary>
    private (double X, double Y) ScreenPoint(double cx, double cy)
    {
        var (vx, vy) = View.ToView(cx, cy);
        return _area is { } area ? (vx + area.X, vy + area.Y) : (vx, vy);
    }

    // ---- крестовина геймпада

    /// <summary>Приставки целей крестовины: значок постройки, вкладка ветки.</summary>
    private const string PadNode = "TREE:", PadTab = "TAB:", PadButton = "BUTTON:";

    /// <summary>Значки открытой ветки (в точках разметки — по виду) и вкладки — цели крестовины рядом с кнопками панели.</summary>
    public IReadOnlyList<UiElement> PadTargets()
    {
        if (_area is not { } area) return [];
        var targets = Tabs.Select(t => new UiElement { Id = PadTab + t.Branch, Kind = UiKind.Button, Rect = t.Rect }).ToList();
        // Кнопки без картинок экран крестовине не отдаёт (спрайт пуст) — их места даёт логика.
        targets.AddRange(Buttons.Where(b => b.Enabled).Select(b => new UiElement { Id = PadButton + b.Id, Kind = UiKind.Button, Rect = b.Rect }));
        foreach (var item in Items)
        {
            var r = View.ToView(item.Icon);
            targets.Add(new UiElement { Id = PadNode + item.Building, Kind = UiKind.Button, Rect = r with { X = r.X + area.X, Y = r.Y + area.Y } });
        }
        return targets;
    }

    /// <summary>Крестовина встала на значок — вид сдвигается, чтобы он был виден целиком.</summary>
    public void PadFocused(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!id.StartsWith(PadNode, StringComparison.Ordinal) || Items.FirstOrDefault(i => i.Building == id[PadNode.Length..]) is not { } item) return;
        View.Reveal(item.Icon with { Height = item.Plate.Bottom - item.Icon.Y });
        Revision++;
    }

    // ---- то, что рисует вид

    /// <summary>Место дерева на разложенном экране (под вкладками); до первого обновления — null.</summary>
    public UiRect? TreeArea => _area;

    private UiRect? _area;

    /// <summary>Вкладки веток над деревом (точки разметки).</summary>
    public IReadOnlyList<BuildTreeTab> Tabs { get; private set; } = [];

    /// <summary>Видимые значки открытой ветки (точки содержимого).</summary>
    public IReadOnlyList<BuildTreeItem> Items { get; private set; } = [];

    /// <summary>Связи между видимыми значками открытой ветки (точки содержимого).</summary>
    public IReadOnlyList<BuildTreeLink> Links { get; private set; } = [];

    /// <summary>Размер содержимого открытой ветки (точки содержимого).</summary>
    public (int Width, int Height) ContentSize => Current is { } b ? BuildTreeGeometry.Content(b) : (0, 0);

    /// <summary>Имя ветки: строка содержимого, нет — хвост id.</summary>
    private string BranchName(BuildTreeBranch branch) =>
        branch.NameText is { Length: > 0 } key && _c.Text(key) is { Length: > 0 } text && text != key ? text : branch.Id[(branch.Id.LastIndexOf('/') + 1)..];

    /// <summary>Вкладки веток в полосе над деревом и место самого дерева в прямоугольнике <paramref name="bg"/> (окно построек и живая страница
    /// Студии раскладываются одинаково).</summary>
    public static (IReadOnlyList<BuildTreeTab> Tabs, UiRect Area) TabsAndArea(UiRect bg, IReadOnlyList<(string Id, string Name)> branches, string open,
                                                                            string? hovered)
    {
        ArgumentNullException.ThrowIfNull(branches);
        int count = Math.Max(1, branches.Count);
        int width = Math.Max(1, Math.Min(TabMaxWidth, (bg.Width - (TabPad * 2) - (TabGap * (count - 1))) / count));
        var tabs = branches.Select((b, i) => new BuildTreeTab(b.Id, new UiRect(bg.X + TabPad + (i * (width + TabGap)), bg.Y + TabPad, width, TabHeight),
                                                            b.Name, b.Id == open, b.Id == hovered)).ToList();
        int top = TabPad + TabHeight + TabGap;
        return (tabs, new UiRect(bg.X, bg.Y + top, bg.Width, Math.Max(1, bg.Height - top)));
    }

    /// <summary>Вкладки и место дерева по фону дерева на разложенном экране; вид — заново, если место сменило размер.</summary>
    private void Place(UiScreenState ui)
    {
        if (RectOf(TreeBackground) is not { } bg) return;
        var (tabs, _) = TabsAndArea(bg with { Width = bg.Width - TabReserve }, [.. Tree.Branches.Select(b => (b.Id, BranchName(b)))], Branch,
                                    _hoveredTab);
        Tabs = tabs;
        var (_, area) = TabsAndArea(bg, [], Branch, null);
        var (cw, ch) = ContentSize;
        if (_area is not { } was) View.Reset(area.Width, area.Height, cw, ch);
        else if (was.Width != area.Width || was.Height != area.Height) View.Resize(area.Width, area.Height);
        _area = area;
        ui.SetImage(TreeBackground, string.Create(CultureInfo.InvariantCulture, $"Gradient:{bg.Width}x{bg.Height}#{TreeTone}"));
    }

    /// <summary>Значки и связи открытой ветки.</summary>
    private void Collect()
    {
        if (Current is not { } branch)
        {
            (Items, Links) = ([], []);
            return;
        }
        var items = new List<BuildTreeItem>();
        foreach (var node in branch.Nodes)
        {
            var icon = Icon(node.Building);
            if (icon == BuildIcon.Hidden) continue;
            string frame = $"{IconArchive}:{ArtNames.Of(node.Building)}";
            items.Add(new BuildTreeItem(node.Building, BuildTreeGeometry.Icon(node), BuildTreeGeometry.Plate(node), icon switch
            {
                BuildIcon.Built => UiFrameRef.Tinted(frame, BuiltTint),
                BuildIcon.NeedPrevious => UiFrameRef.Tinted(frame, PreviousTint),
                BuildIcon.OtherPath => UiFrameRef.Tinted(frame, OtherTint),
                _ => frame,
            }, Name(node.Building), icon, node.Building.Equals(Selected, StringComparison.OrdinalIgnoreCase),
            node.Building.Equals(_hovered, StringComparison.OrdinalIgnoreCase)));
        }
        var shown = items.ToDictionary(i => i.Building, StringComparer.OrdinalIgnoreCase);
        Links = [.. branch.Nodes.Where(n => n.Parent is not null && shown.ContainsKey(n.Building) && shown.ContainsKey(n.Parent!))
            .Select(n => new BuildTreeLink(shown[n.Parent!].Plate, shown[n.Building].Icon, shown[n.Parent!].Kind == BuildIcon.Built))];
        Items = items;
    }

    /// <summary>Прямоугольник элемента на разложенном экране (последнее обновление); не было — null.</summary>
    private UiRect? RectOf(string id) => _placed?.Elements.FirstOrDefault(e => e.Id == id)?.Rect;

    // ---- правила окна

    /// <summary>Воин постройки на портрете справа (<c>IMG_FACE</c>, «Для деталей — правый клик» <c>X005TA0382</c>): правая кнопка по
    /// портрету — его карточка (<see cref="UnitCardLogic.OfType"/>), пока кнопка держится; нет воина — null.</summary>
    private string? _faceUnit;

    /// <summary>Воин постройки большой — портрет в большой рамке (<see cref="BigFace"/>).</summary>
    private bool _faceBig;

    /// <summary>Кадр портрета воина постройки (для <see cref="Summary"/>); нет воина — null.</summary>
    private string? _faceFrame;

    private void Build()
    {
        if (Selected is not { } id || Icon(id) != BuildIcon.Can) return;
        var book = _c.Session.RequireBook();
        switch (Status(id))
        {
            case BuildStatus.InsufficientBank:
                _requests.Add(new UiTell(_c.Text("X005TA0120")));
                return;
            case BuildStatus.AlreadyBuiltThisDay:
                _requests.Add(new UiTell(_c.Text("X005TA0239")));
                return;
            case BuildStatus.CanBeBuilt:
                break;
            case var other:
                LastRefusal = other.ToString();
                return;
        }
        string text = UiTemplate.Fill(_c.Text("X005TA0240"), ("NAME", Name(id)), ("PRICE", book.Economy.Buildings[id].Cost.Gold));
        string player = _c.Viewer;
        _requests.Add(new UiAsk(text, () =>
        {
            var done = _c.Session.Run<BuildStatus>(new BuildCommand(player, id));
            LastRefusal = done == BuildStatus.CanBeBuilt ? null : done.ToString();
            // Построено — окно построек закрывается само (обработчик вопроса 0x4ab364: постройка, затем 0x5c8f71 в 0x4ab3d1, звук BUILDING);
            // корпус — 23 постройки человека, все 23 закрыли DLG_BUILD_STRUCTURE (20261016e-av Dungeon: BTN_YES 1198448 → ui_close 1198457).
            _requests.Add(LastRefusal is null ? new UiClose() : new UiTell(LastRefusal));
        }));
    }

    /// <summary>Полная проверка постройки (<c>getBuildingStatus</c> <c>0x5dc9e8</c>, как <see cref="Play.GameSession.Build"/>):
    /// казна, «раз в день», потолок уровня сценария.</summary>
    public BuildStatus Status(string buildingId, bool ignoreCost = false)
    {
        var map = _c.Map;
        var player = _c.Player;
        return Economy.CanBuild(map, player.Id, buildingId, _c.Session.RequireBook().Economy, ignoreCost,
                                maxUnitLevel: map.Limits?.MaxUnit is > 0 and var max ? max : int.MaxValue,
                                builtThisDay: player.ConstructionTurn == map.Turn);
    }

    /// <summary>Вид значка: деньги и «уже строили сегодня» на него не влияют; «выше потолка» и «другой путь» наследуются
    /// вниз по дереву (от предыдущих построек, <c>REQUIRED</c>).</summary>
    public BuildIcon Icon(string buildingId) => _shaded.Contains(buildingId) ? BuildIcon.OtherPath : BaseIcon(buildingId);

    /// <summary>Вид значков за одно обновление окна: правила (казна, требования, потолок) по каждой постройке — раз, а не на каждый вопрос
    /// (сбор значков, затемнение соседей, строки справа).</summary>
    private readonly Dictionary<string, BuildIcon> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Состояние партии, для которого посчитан <see cref="_icons"/>: команда даёт новое — кэш заново.</summary>
    private object? _iconsOf;

    private BuildIcon BaseIcon(string buildingId)
    {
        if (!ReferenceEquals(_iconsOf, _c.Map))
        {
            _icons.Clear();
            _iconsOf = _c.Map;
        }
        if (_icons.TryGetValue(buildingId, out var known)) return known;
        return _icons[buildingId] = ComputeIcon(buildingId);
    }

    private BuildIcon ComputeIcon(string buildingId)
    {
        var buildings = _c.Session.RequireBook().Economy.Buildings;
        if (!buildings.ContainsKey(buildingId)) return BuildIcon.Hidden;
        var own = Status(buildingId, ignoreCost: true);
        if (own == BuildStatus.AlreadyBuilt) return BuildIcon.Built;
        bool other = own == BuildStatus.SiblingUnitBuilding;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string id = buildingId; visited.Add(id) && buildings.TryGetValue(id, out var type);)
        {
            var status = Status(id, ignoreCost: true);
            if (status == BuildStatus.ExceedsMaxLevel) return BuildIcon.Hidden;
            if (status == BuildStatus.SiblingUnitBuilding) other = true;
            if (type.RequiredId.Length == 0 || type.RequiredId.Equals(id, StringComparison.OrdinalIgnoreCase)) break;
            id = type.RequiredId;
        }
        if (other) return BuildIcon.OtherPath;
        return own == BuildStatus.NoRequiredBuilding ? BuildIcon.NeedPrevious : BuildIcon.Can;
    }

    private string Name(string id) => _texts.TryGetValue(id, out var t) ? _c.Text(t.NameTxt) : id;

    /// <summary>Затемнение соседей выбранной (<c>0x4acf0d</c>): у самой постройки и у её корня — прочие постройки ветки с тем же
    /// предком и статусом «можно» вместе с поддеревьями.</summary>
    private void Shade()
    {
        _shaded.Clear();
        if (_selected is not { } s || BaseIcon(s) != BuildIcon.Can) return;
        var buildings = _c.Session.RequireBook().Economy.Buildings;
        if (!buildings.TryGetValue(s, out var selected)) return;
        string root = s;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { s };
        while (buildings.TryGetValue(root, out var t) && t.RequiredId.Length > 0 && buildings.ContainsKey(t.RequiredId) && seen.Add(t.RequiredId))
            root = t.RequiredId;
        foreach (string node in root.Equals(s, StringComparison.OrdinalIgnoreCase) ? new[] { s } : new[] { s, root })
        {
            if (!buildings.TryGetValue(node, out var n)) continue;
            foreach (var sibling in buildings.Values.Where(b => b.Branch == selected.Branch && b.Category == selected.Category
                                                                && !b.Id.Equals(node, StringComparison.OrdinalIgnoreCase)
                                                                && b.RequiredId.Equals(n.RequiredId, StringComparison.OrdinalIgnoreCase)
                                                                && BaseIcon(b.Id) == BuildIcon.Can))
                Subtree(sibling.Id);
        }

        void Subtree(string id)
        {
            if (!_shaded.Add(id)) return;
            foreach (var child in buildings.Values.Where(b => b.RequiredId.Equals(id, StringComparison.OrdinalIgnoreCase))) Subtree(child.Id);
        }
    }

    /// <summary>Строки воина постройки: живущий в ней, тренирующийся, сколько их в армии и ждут повышения; воина нет — пусто.</summary>
    private List<string> UnitLines(string id)
    {
        var lines = new List<string>();
        var book = _c.Session.RequireBook();
        string? special = _c.Rules?.EnrollBuildings.FirstOrDefault(p => p.Value.Equals(id, StringComparison.OrdinalIgnoreCase)).Key;
        string? unit = special ?? book.UpgradeBuilding.FirstOrDefault(p => p.Value.Equals(id, StringComparison.OrdinalIgnoreCase)).Key;
        if (unit is null) return lines;
        _faceUnit = unit;
        // Портрет без рамки оригинала (BORDERUNITSMALL/LARGE — старый интерфейс, решение пользователя 24.10): рамку рисует вид в теме Last
        // Edict; большой воин — место шире (0x4abb69).
        _faceBig = book.Rules.Units.TryGetValue(unit, out var sized) && !sized.SizeSmall;
        _faceFrame = _c.Faces.Small(unit);
        lines.Add($"{_c.Text(special is null ? "X005TA0384" : "X005TA0677")} {_c.UnitName(unit)}");
        if (!book.Rules.Units.TryGetValue(unit, out var type) || type.PrevId.Length == 0)
        {
            lines.Add(_c.Text("X005TA0382"));
            return lines;
        }
        string prev = type.PrevId;
        lines.Add(UiTemplate.Fill(_c.Text("X005TA0383"), ("NAME", _c.UnitName(prev))));
        var map = _c.Map;
        var ours = map.Stacks.Values.Where(s => s.Owner.Equals(_c.Viewer, StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.UnitIds)
            .Concat(map.Sites.Where(s => s.Owner.Equals(_c.Viewer, StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Guards))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(u => map.Units.GetValueOrDefault(u)).OfType<MapUnit>()
            .Where(u => u.TypeId.Equals(prev, StringComparison.OrdinalIgnoreCase)).ToList();
        lines.Add(UiTemplate.Fill(_c.Text("X005TA0282"), ("NUMBER", ours.Count)));
        if (Icon(id) is BuildIcon.Can or BuildIcon.NeedPrevious)
        {
            int urgent = ours.Count(u => book.XpNextOf(u.TypeId, u.Level) is var next and > 0 && u.Xp >= next - 1);
            lines.Add(UiTemplate.Fill(_c.Text("X005TA0381"), ("URGENT", urgent)));
        }
        lines.Add(_c.Text("X005TA0382"));
        return lines;
    }

    private void Refresh(UiScreenState ui)
    {
        Shade();   // выбор мог стать недоступным (построили, новый день)
        var player = _c.Player;
        var book = _c.Session.RequireBook();
        Gold = $"{_c.Text("LE_BUILD_GOLD")}: {player.Bank.Gold.ToString(CultureInfo.InvariantCulture)}";
        _placed = ui.Screen;
        Place(ui);
        Collect();
        Revision++;
        bool chosen = Selected is { } s && book.Economy.Buildings.ContainsKey(s);
        ui.SetEnabled("BTN_BUILD", chosen && Icon(Selected!) == BuildIcon.Can);
        ui.SetHidden("BTN_BUILD", !chosen);
        CollectButtons(ui);
        _faceUnit = null;
        _faceBig = false;
        _faceFrame = null;
        Card = null;
        if (!chosen || Selected is not { } id || RectOf(CardId) is not { } rect) return;
        var type = book.Economy.Buildings[id];
        var icon = Icon(id);
        string cost = UiTemplate.Fill(_c.Text("X005TA0055"), ("QTY", type.Cost.Gold));
        var lines = UnitLines(id);
        // Строки игры несут разметку шрифтов и цвета (MedBold; и т. п.) — карточка рисует свой шрифт, ей нужен текст без разметки.
        static string Plain(string text) => UiTextStyle.Parse(text).Text;
        Card = new BuildCard(rect, Plain(Name(id)), Plain(cost), _texts.TryGetValue(id, out var text) ? Plain(_c.Text(text.DescTxt)) : "",
            _faceFrame, _faceBig, [.. lines.Select(Plain)],
            Plain(_c.Text(icon switch
            {
                BuildIcon.Built => "X005TA0308", BuildIcon.NeedPrevious => "X005TA0310", BuildIcon.OtherPath => "X005TA0312", _ => "X005TA0307",
            })), icon);
    }

    /// <summary>Разложенный экран сменился (окно растянули): место дерева и вид — заново по новому месту фона.</summary>
    public void Relayout(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _placed = ui.Screen;
        Place(ui);
        Collect();
        Revision++;
    }
}
