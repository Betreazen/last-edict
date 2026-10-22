namespace LastEdict.Data.Ui;

/// <summary>Как сейчас выглядит кнопка или переключатель — те же четыре состояния, что у <c>ButtonState</c>
/// движка (реверс тулсета, <c>tools/d2probe/mss32/include/button.h</c>).</summary>
public enum UiLook { Normal, Hover, Pressed, Disabled }

/// <summary>Что произошло на экране. Смысл событий — как у обработчиков движка: щелчок кнопки
/// (<c>onClickedFunctor</c>), переключатель (<c>CToggleButton</c>), выбор в группе (<c>onButtonPressed</c>),
/// смена значения счётчика (<c>onOptionChanged</c>), выбор и подтверждение в списке
/// (<c>onSelectionConfirmed</c>).</summary>
public abstract record UiEvent(string Element);

public sealed record UiClicked(string Element) : UiEvent(Element);

public sealed record UiToggled(string Element, bool Checked) : UiEvent(Element);

public sealed record UiGroupChanged(string Element, int Index, string Member) : UiEvent(Element);

public sealed record UiSpinChanged(string Element, int Option) : UiEvent(Element);

public sealed record UiListSelected(string Element, int Index) : UiEvent(Element);

public sealed record UiListConfirmed(string Element, int Index) : UiEvent(Element);

public sealed record UiEdited(string Element, string Text) : UiEvent(Element);

/// <summary>Разовый ролик в прямоугольнике элемента доиграл (или его нет на диске) — логике, которая ждёт конца (смена лица лорда).</summary>
public sealed record UiVideoEnded(string Element) : UiEvent(Element);

/// <summary>Ролик в прямоугольнике элемента (или на фоне экрана): имя из описи роликов, крутить ли по кругу и номер
/// запуска — тот же ролик, заданный заново, получает новый номер, и сцена проигрывает его с начала.</summary>
public sealed record UiVideoRef(string Name, bool Loop, int Run);

/// <summary>Пункт списка картинок: кадр (<see cref="UiScreenState.SetImage"/> — имя кадра интерфейса или
/// <c>архив:имя</c> графики игры, как <c>Faces:G000UU0001FACE</c>) в точке <see cref="ImageAt"/> ячейки и подпись
/// (разметка <see cref="UiTextStyle"/>) в прямоугольнике <see cref="TextAt"/> ячейки; <see cref="Overlay"/> — второй кадр
/// поверх первого в той же точке (значок «уже изучено» <c>ICNSY001</c> над заклинанием); <see cref="Dimmed"/> — картинка затемнена
/// вдвое (режим картинки 0xd: чёрная заливка с альфой 0x80, <c>out = src·127/255</c> — герой чужой расы в окне импорта).</summary>
public sealed record UiCard(string? Image, UiPoint ImageAt, string? Text, UiRect TextAt, string? Overlay = null, bool Dimmed = false);

/// <summary>Точка в координатах экрана или ячейки.</summary>
public readonly record struct UiPoint(int X, int Y);

/// <summary>Живое состояние одного экрана без движка: что под мышью, что нажато, что отключено и отмечено,
/// значения счётчиков и выбор в списках. Сцена передаёт сюда ввод в координатах <see cref="UiScreen.Design"/>
/// и получает события и кадры; сама она ничего не решает, поэтому поведение проверяется <c>dotnet test</c>.
///
/// <para>Модель — из реверса тулсета (<c>tools/d2probe/mss32/include</c>): у кнопки четыре состояния и щелчок,
/// у переключателя «отмечен», у группы — номер выбранного, у счётчика — список значений и выбранное, у списка —
/// выбранный пункт, число пунктов и подтверждение. Чего в заголовках нет, подтверждено пользователем по игре
/// (19.09.2026): щелчок срабатывает при отпускании над той же кнопкой; стрелки списка двигают выбор, а не только
/// прокрутку. По обычаю Windows и ещё не сверено: строка списка выбирается при нажатии, двойной щелчок
/// подтверждает; горячая клавиша жмёт кнопку сразу. В группе по умолчанию отмечен первый участник — а где игра
/// ставит другой (сложность на <c>DLG_CHOOSE_LORD</c> — средняя), это задаёт логика экрана через
/// <see cref="SetGroupIndex"/>.</para>
/// <para>Автоповтор (<see cref="UiElement.Autorepeat"/>, <c>CButtonInterf</c> <c>0x531d22</c>…<c>0x531e76</c>): нажатие щелчка не даёт, пока
/// кнопка зажата и под мышью — щелчок каждые <see cref="AutorepeatMs"/> (<see cref="Hold"/>), и ещё один при отпускании над ней.
/// Стрелки у края гаснут сами (<see cref="IsEnabled"/>): у счётчика (<c>0x539de4</c>) — по выбранному значению, у списка
/// (<c>0x535e3c</c>) — по выбранному пункту (вверх — не в первом ряду, вниз — не в последнем, влево/вправо — по колонке); список без
/// выбора (сумки окон, выбора у них нет) листается прокруткой, и его стрелки гаснут по ней. Ответ пользователя 26.09: «листает и
/// сереет».</para></summary>
public sealed class UiScreenState
{
    private sealed class ListState
    {
        public int Count;
        public int Selected = -1;
        public int Top;
        public int RowHeight;
    }

    private sealed class SpinState
    {
        public int Options;
        public int Selected;
    }

    private readonly UiScreen _screen;
    private readonly Dictionary<string, UiElement> _byId;
    private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiElement> _groupOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ListState> _lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpinState> _spins = new(StringComparer.Ordinal);

    private string? _hover;
    private string? _captured;
    private int _held;

    /// <summary>Период автоповтора кнопки, мс (<c>0xFA</c>, одно число на всё; задержки до первого повтора нет).</summary>
    public const int AutorepeatMs = 250;

    /// <summary>Стрелки списков и счётчиков: кнопка → чья и какая.</summary>
    private readonly Dictionary<string, (string Owner, string Role)> _arrows = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiSprites> _sprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiVideoRef> _videos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<UiCard>> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _scroll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiImageAlign> _aligns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _videoRuns = new(StringComparer.Ordinal);
    private string? _background;
    private string? _underlay;
    private string? _focus;

    /// <summary>Имя, под которым в <see cref="SetVideo"/> и <see cref="VideoOf"/> идёт фон экрана.</summary>
    public const string BackgroundId = "BACKGROUND";

    /// <summary>Сколько символов принимает поле ввода: игра даёт ввести 15 символов имени
    /// (<c>default_player_name</c> в заголовке сценария, реверс формата <c>.sg</c>).</summary>
    public const int EditLimit = 15;

    public UiScreenState(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _screen = screen;
        _byId = screen.Elements.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var group in screen.Elements.Where(e => e.Kind == UiKind.RadioGroup && e.Members.Count > 0))
        {
            foreach (var member in group.Members) _groupOf[member] = group;
            _checked.Add(group.Members[0]);
        }
        foreach (var e in screen.Elements)
        {
            if (e.Kind is UiKind.List or UiKind.TextList) _lists[e.Id] = new ListState();
            if (e.Kind == UiKind.Spin) _spins[e.Id] = new SpinState();
            if (e.List is { } l && _lists.ContainsKey(e.Id))
                foreach (var (button, role) in (ReadOnlySpan<(string?, string)>)[(l.Up, "up"), (l.Down, "down"), (l.Left, "left"),
                                                                                (l.Right, "right"), (l.PageUp, "up"), (l.PageDown, "down")])
                    if (!string.IsNullOrEmpty(button)) _arrows.TryAdd(button, (e.Id, role));
            if (e.Spin is { } s && _spins.ContainsKey(e.Id))
            {
                if (!string.IsNullOrEmpty(s.Increment)) _arrows.TryAdd(s.Increment, (e.Id, "inc"));
                if (!string.IsNullOrEmpty(s.Decrement)) _arrows.TryAdd(s.Decrement, (e.Id, "dec"));
            }
        }
    }

    public UiScreen Screen => _screen;

    /// <summary>Элемент под мышью (кнопка, переключатель, список, поле ввода); <c>null</c> — ни одного.</summary>
    public string? Hovered => _hover;

    /// <summary>Поле ввода, в которое идут символы; <c>null</c> — ни одного.</summary>
    public string? Focused => _focus;

    /// <summary>Поставить ввод в поле (окно имени героя открывается с курсором в поле); <c>null</c> — снять.</summary>
    public void SetFocus(string? id)
    {
        if (id is not null && Get(id).Kind != UiKind.Edit) throw new ArgumentException($"{_screen.Id}/{id}: не поле ввода", nameof(id));
        _focus = id;
        Revision++;
    }

    /// <summary>Растёт с каждой сменой содержимого (текст, картинка, фон, ролик, пункты и выбор в списке): по нему
    /// сцена понимает, что пора перерисовать надписи и ролики.</summary>
    public int Revision { get; private set; }

    /// <summary>Фон экрана: заданный логикой или из разметки.</summary>
    public string Background => _background ?? _screen.Background;

    /// <summary>Подложка под фоном: фон разметки с прозрачностью (рамка <c>DLG_CHOOSE_LORD_BG1TRANS</c>, <c>DLG_CHOOSE_RACE_BG_CHOOSE_RACE</c>)
    /// рисуется поверх неё; <c>null</c> — подложки нет.</summary>
    public string? Underlay => _underlay;

    // --- то, что задаёт логика экрана ---

    /// <summary>Включить или отключить элемент; у группы — всех её участников.</summary>
    public void SetEnabled(string id, bool enabled)
    {
        var e = Get(id);
        foreach (var target in e.Kind == UiKind.RadioGroup ? e.Members : [id])
            if (enabled) _disabled.Remove(target);
            else _disabled.Add(target);
    }

    /// <summary>Доступен ли элемент; группа — если доступны все её участники.</summary>
    public bool IsEnabled(string id)
    {
        var e = Get(id);
        return e.Kind == UiKind.RadioGroup ? e.Members.All(m => !Off(m)) : !Off(id);
    }

    /// <summary>Элемент выключен: логикой или как стрелка у края своего списка или счётчика.</summary>
    private bool Off(string id) => _disabled.Contains(id) || AtEdge(id);

    /// <summary>Стрелка у края (правила движка — в описании класса).</summary>
    private bool AtEdge(string id)
    {
        if (!_arrows.TryGetValue(id, out var arrow)) return false;
        if (_spins.TryGetValue(arrow.Owner, out var spin))
            return arrow.Role == "inc" ? spin.Selected >= spin.Options - 1 : spin.Selected <= 0;
        var list = _lists[arrow.Owner];
        var (columns, rows) = Grid(_byId[arrow.Owner], list);
        int n = list.Count, sel = list.Selected;
        if (sel < 0)
            return arrow.Role is "up" or "left" ? list.Top <= 0 : list.Top + (columns * rows) >= n;
        return arrow.Role switch
        {
            "up" => sel <= columns - 1,
            "down" => sel >= n - ((n - 1) % columns) - 1,
            "left" => sel % columns == 0,
            _ => sel % columns >= columns - 1 || sel >= n - 1,
        };
    }

    /// <summary>Спрятать элемент или показать снова (<c>hideControl</c> движка, <c>0x50b795</c>): спрятанный не рисуется,
    /// мышь и горячие клавиши его не находят. Окно сообщения так оставляет «Да/Нет» или одно «ОК».</summary>
    public void SetHidden(string id, bool hidden)
    {
        Get(id);
        if (hidden) _hidden.Add(id);
        else _hidden.Remove(id);
        Revision++;
    }

    public bool IsHidden(string id) => _hidden.Contains(Get(id).Id);

    public void SetChecked(string id, bool value)
    {
        Get(id);
        if (value) _checked.Add(id);
        else _checked.Remove(id);
    }

    public bool IsChecked(string id) => _checked.Contains(Get(id).Id);

    /// <summary>Номер отмеченного участника группы; -1 — ни одного.</summary>
    public int GroupIndex(string id)
    {
        var members = Get(id).Members;
        for (int i = 0; i < members.Count; i++)
            if (_checked.Contains(members[i])) return i;
        return -1;
    }

    public void SetGroupIndex(string id, int index)
    {
        var members = Get(id).Members;
        for (int i = 0; i < members.Count; i++)
            if (i == index) _checked.Add(members[i]);
            else _checked.Remove(members[i]);
    }

    /// <summary>Начальные значения экрана (<see cref="UiDefaults.For"/>): группа → отмеченный участник.</summary>
    /// <exception cref="KeyNotFoundException">Нет такой группы на экране или такого участника в группе.</exception>
    public void Apply(IReadOnlyDictionary<string, string> defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        foreach (var (group, member) in defaults)
        {
            var e = Get(group);
            int index = e.Kind == UiKind.RadioGroup ? e.Members.ToList().IndexOf(member) : -1;
            if (index < 0) throw new KeyNotFoundException($"{_screen.Id}/{group}: нет участника {member}");
            SetGroupIndex(group, index);
        }
    }

    /// <summary>Сколько значений у счётчика и какое выбрано.</summary>
    public void SetSpin(string id, int options, int selected)
    {
        var spin = Spin(id);
        spin.Options = Math.Max(0, options);
        spin.Selected = Math.Clamp(selected, 0, Math.Max(0, spin.Options - 1));
    }

    public int SpinOption(string id) => Spin(id).Selected;

    /// <summary>Сколько пунктов в списке. <paramref name="rowHeight"/> — высота строки текстового списка
    /// (её задаёт шрифт); у списка картинок она есть в разметке. Выбор и прокрутка сбрасываются.</summary>
    public void SetList(string id, int count, int rowHeight = 0)
    {
        var list = List(id);
        list.Count = Math.Max(0, count);
        list.RowHeight = rowHeight;
        list.Selected = -1;
        list.Top = 0;
    }

    public int ListSelected(string id) => List(id).Selected;

    /// <summary>Выбрать пункт списка логикой экрана (с прокруткой к нему); событий не даёт.</summary>
    public void Select(string id, int index)
    {
        var state = List(id);
        if (state.Count > 0) Select(Get(id), state, index, []);
    }

    // --- содержимое, которое задаёт логика экрана ---

    /// <summary>Текст подписи или поля ввода вместо строки разметки; <c>null</c> — снова из разметки.</summary>
    public void SetText(string id, string? text)
    {
        Get(id);
        if (text is null) _texts.Remove(id);
        else _texts[id] = text;
        Revision++;
    }

    public string? TextOf(string id) => _texts.GetValueOrDefault(Get(id).Id);

    /// <summary>Сколько строк текста подписи прокручено вверх (не меньше нуля; предел по числу строк знает только
    /// сцена, которая текст раскладывает, — она и не даёт уйти дальше последней строки).</summary>
    public void SetScroll(string id, int lines)
    {
        _scroll[Get(id).Id] = Math.Max(0, lines);
        Revision++;
    }

    public int ScrollOf(string id) => _scroll.GetValueOrDefault(Get(id).Id);

    private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal), _heights = new(StringComparer.Ordinal);

    /// <summary>Сдвиг текста в точках (прокрутка «Задания», <c>CScrollTextBoxInterf</c>): верх текста — на <paramref name="offset"/>
    /// ниже верха рамки элемента (отрицательный — выше), что вне рамки — обрезано. Задан — построчная прокрутка не действует.</summary>
    public void SetTextOffset(string id, int offset)
    {
        _offsets[Get(id).Id] = offset;
        Revision++;
    }

    public int? TextOffset(string id) => _offsets.TryGetValue(Get(id).Id, out int v) ? v : null;

    /// <summary>Высота свёрстанного текста в точках — сообщает сцена (логике нужна для конца прокрутки); 0 — ещё не свёрстан.
    /// Не поднимает <see cref="Revision"/>: это отчёт сцены, а не смена содержимого.</summary>
    public void ReportTextHeight(string id, int height) => _heights[Get(id).Id] = Math.Max(0, height);

    public int TextHeight(string id) => _heights.GetValueOrDefault(Get(id).Id);

    /// <summary>Кадр картинки вместо кадра разметки; <c>null</c> — снова из разметки. <paramref name="align"/> — как картинка ложится
    /// в место элемента (у движка — флаги <c>setImage</c> <c>0x530e29</c>, их задаёт код окна при каждой смене кадра).</summary>
    public void SetImage(string id, string? frame, UiImageAlign align = UiImageAlign.Fit)
    {
        Get(id);
        if (frame is null) _images.Remove(id);
        else _images[id] = frame;
        if (frame is null || align == UiImageAlign.Fit) _aligns.Remove(id);
        else _aligns[id] = align;
        Revision++;
    }

    /// <summary>Как картинка элемента ложится в его место (<see cref="SetImage"/>).</summary>
    public UiImageAlign ImageAlign(string id) => _aligns.GetValueOrDefault(Get(id).Id);

    /// <summary>Кадры кнопки или переключателя вместо кадров разметки (картинка на кнопке меняется по выбору: лицо лорда в
    /// лобби); <c>null</c> — снова из разметки.</summary>
    public void SetSprites(string id, UiSprites? sprites)
    {
        Get(id);
        if (sprites is null) _sprites.Remove(id);
        else _sprites[id] = sprites;
        Revision++;
    }

    public void SetBackground(string? frame)
    {
        _background = frame;
        Revision++;
    }

    /// <summary>Картинка под фоном экрана (<see cref="Underlay"/>): фон разметки остаётся сверху.</summary>
    public void SetUnderlay(string? frame)
    {
        _underlay = frame;
        Revision++;
    }

    /// <summary>Ролик в прямоугольнике элемента или на фоне (<see cref="BackgroundId"/>); <c>null</c> — убрать.</summary>
    public void SetVideo(string id, string? name, bool loop = false)
    {
        if (id != BackgroundId) Get(id);
        // Номер запуска растёт и через снятие: иначе «снять и тут же задать снова» дало бы тот же номер,
        // и сцена сочла бы ролик уже идущим.
        int run = _videoRuns[id] = _videoRuns.GetValueOrDefault(id) + 1;
        if (name is null) _videos.Remove(id);
        else _videos[id] = new UiVideoRef(name, loop, run);
        Revision++;
    }

    public UiVideoRef? VideoOf(string id) => _videos.GetValueOrDefault(id);

    /// <summary>Все ролики экрана: элемент (или <see cref="BackgroundId"/>) → ролик.</summary>
    public IReadOnlyDictionary<string, UiVideoRef> Videos => _videos;

    /// <summary>Пункты списка: их число и тексты (у текстового списка). Выбор и прокрутка сбрасываются.</summary>
    public void SetItems(string id, IReadOnlyList<string> items, int rowHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        SetList(id, items.Count, rowHeight > 0 ? rowHeight : List(id).RowHeight);
        _items[id] = items;
        Revision++;
    }

    public IReadOnlyList<string> Items(string id) => _items.TryGetValue(Get(id).Id, out var items) ? items : [];

    /// <summary>Пункты списка картинок — карточки (портрет и подпись в ячейке списка). Число пунктов — число карточек; прокрутка и
    /// выбор остаются, подрезанные по новому числу: окна перерисовывают сумку после каждого события, и щелчка стрелки тоже.</summary>
    public void SetCards(string id, IReadOnlyList<UiCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var state = List(id);
        int top = state.Top, selected = state.Selected;
        SetList(id, cards.Count, state.RowHeight);
        var (columns, rows) = Grid(Get(id), state);
        state.Top = Math.Clamp(top, 0, Math.Max(0, ((state.Count - 1) / columns - rows + 1) * columns));
        state.Selected = selected < state.Count ? selected : -1;
        _cards[id] = cards;
        Revision++;
    }

    public IReadOnlyList<UiCard> Cards(string id) => _cards.TryGetValue(Get(id).Id, out var cards) ? cards : [];

    private readonly Dictionary<string, UiColor> _marks = new(StringComparer.Ordinal);

    /// <summary>Цвет рамки выбранной карточки списка (<c>CImage2Outline</c>): задан — рамка у выбранной ячейки всегда, этим цветом
    /// (окна кражи — жёлтая <c>FFFF00</c>); не задан — чёрная и только у ячейки с подписью.</summary>
    public void SetMarkColor(string id, UiColor color)
    {
        List(id);
        _marks[id] = color;
        Revision++;
    }

    public UiColor? MarkColor(string id) => _marks.TryGetValue(Get(id).Id, out var c) ? c : null;

    /// <summary>Высота строки списка (у текстового — от шрифта, у списка картинок — из разметки).</summary>
    public int RowHeight(string id) => Cell(Get(id), List(id)).H;

    /// <summary>Символ, набранный на клавиатуре, — в поле ввода с фокусом. Управляющие символы и лишние сверх
    /// <see cref="EditLimit"/> не принимаются.</summary>
    public IReadOnlyList<UiEvent> Type(char c)
    {
        if (_focus is null || char.IsControl(c)) return [];
        string text = TextOf(_focus) ?? "";
        if (text.Length >= EditLimit) return [];
        return Edited(_focus, text + c);
    }

    private List<UiEvent> Edited(string id, string text)
    {
        _texts[id] = text;
        Revision++;
        return [new UiEdited(id, text)];
    }

    /// <summary>Номер первого видимого пункта.</summary>
    public int ListTop(string id) => List(id).Top;

    // --- ввод ---

    public IReadOnlyList<UiEvent> Move(int x, int y)
    {
        _hover = Hit(x, y)?.Id;
        return [];
    }

    public IReadOnlyList<UiEvent> Press(int x, int y, bool doubleClick = false)
    {
        var hit = Hit(x, y);
        _hover = hit?.Id;
        if (hit is null) return [];
        if (hit.Kind == UiKind.Edit)
        {
            _focus = hit.Id;
            Revision++;
            return [];
        }
        if (hit.Kind is UiKind.Button or UiKind.Toggle)
        {
            _captured = hit.Id;
            _held = 0;
            return [];
        }
        return PressList(hit, x, y, doubleClick);
    }

    public IReadOnlyList<UiEvent> Release(int x, int y)
    {
        _hover = Hit(x, y)?.Id;
        string? captured = _captured;
        _captured = null;
        return captured is not null && captured == _hover ? Activate(_byId[captured]) : [];
    }

    /// <summary>Время, пока кнопка зажата (<paramref name="elapsedMs"/> с прошлого вызова): у кнопки с автоповтором, зажатой и под мышью,
    /// — щелчок на каждые <see cref="AutorepeatMs"/> (тик таймера <c>0x531e15</c>); у стрелки, погасшей у края, — ничего.</summary>
    public IReadOnlyList<UiEvent> Hold(int elapsedMs)
    {
        if (_captured is not { } id || !_byId[id].Autorepeat || _hover != id) return [];
        _held += Math.Max(0, elapsedMs);
        var events = new List<UiEvent>();
        while (_held >= AutorepeatMs && !Off(id))
        {
            _held -= AutorepeatMs;
            events.AddRange(Activate(_byId[id]));
        }
        return events;
    }

    /// <summary>Подсказка под точкой: верхний видимый элемент с подсказкой (<see cref="UiElement.Tooltip"/>) — его id и строка;
    /// над элементом без подсказки или вне элементов — null.</summary>
    public (string Element, string Text)? TooltipAt(int x, int y)
    {
        for (int i = _screen.Elements.Count - 1; i >= 0; i--)
        {
            var e = _screen.Elements[i];
            if (e.Kind is UiKind.Text or UiKind.RadioGroup || _hidden.Contains(e.Id)) continue;
            if (x < e.Rect.X || x >= e.Rect.Right || y < e.Rect.Y || y >= e.Rect.Bottom) continue;
            if (e.Kind == UiKind.Image && e.Tooltip is null) continue;   // картинка без подсказки не заслоняет нижние
            return e.Tooltip is { } tip ? (e.Id, tip) : null;
        }
        return null;
    }

    /// <summary>Нажатие клавиши: <paramref name="virtualKey"/> — виртуальный код Windows, как в
    /// <see cref="UiElement.Hotkeys"/>. Жмётся первый доступный элемент с этой клавишей.</summary>
    public IReadOnlyList<UiEvent> Key(int virtualKey)
    {
        const int backspace = 8;
        if (virtualKey == backspace && _focus is not null)
        {
            string text = TextOf(_focus) ?? "";
            return text.Length == 0 ? [] : Edited(_focus, text[..^1]);
        }
        var target = _screen.Elements.FirstOrDefault(e =>
            e.Kind is UiKind.Button or UiKind.Toggle && e.Hotkeys.Contains(virtualKey) && !Off(e.Id)
            && !_hidden.Contains(e.Id));
        return target is null ? [] : Activate(target);
    }

    // --- что рисовать ---

    public UiLook Look(string id)
    {
        Get(id);
        if (Off(id)) return UiLook.Disabled;
        if (_captured is not null) return _captured == id && _hover == id ? UiLook.Pressed : UiLook.Normal;
        return _hover == id ? UiLook.Hover : UiLook.Normal;
    }

    /// <summary>Кадр элемента в текущем состоянии; пустой кадр состояния заменяется обычным. У включённого
    /// переключателя своего «запрета» нет — берётся выключенный. <c>null</c> — рисовать нечего.</summary>
    public string? Frame(string id)
    {
        var e = Get(id);
        if (_hidden.Contains(id)) return null;
        if (e.Kind == UiKind.Image) return _images.GetValueOrDefault(id) ?? Empty(e.Image);
        var sprites = _sprites.GetValueOrDefault(id) ?? e.Sprites;
        if (sprites is null) return null;
        var look = Look(id);
        var set = e.Kind == UiKind.Toggle && _checked.Contains(id) && look != UiLook.Disabled && e.Checked is not null
            ? e.Checked
            : sprites;
        string pick = look switch
        {
            UiLook.Hover => set.Hover,
            UiLook.Pressed => set.Pressed,
            UiLook.Disabled => set.Disabled,
            _ => set.Normal,
        };
        return Empty(pick) ?? Empty(set.Normal);
    }

    /// <summary>Элемент, который поймает мышь в точке разметки (кнопка, переключатель, список, поле), или null — для хозяина, у которого
    /// поверх экрана лежит свой узел (окно мини-карты под кнопками палитры).</summary>
    public string? ElementAt(int x, int y) => Hit(x, y)?.Id;

    // --- внутреннее ---

    /// <summary>Верхний элемент под точкой — последний в списке экрана: элементы рисуются по порядку.
    /// Отключённые мышь не ловят; подписи и картинки — тоже, иначе фон во весь экран закрыл бы всё.</summary>
    private UiElement? Hit(int x, int y)
    {
        for (int i = _screen.Elements.Count - 1; i >= 0; i--)
        {
            var e = _screen.Elements[i];
            if (e.Kind is not (UiKind.Button or UiKind.Toggle or UiKind.List or UiKind.TextList or UiKind.Edit)) continue;
            if (Off(e.Id) || _hidden.Contains(e.Id)) continue;
            if (x >= e.Rect.X && x < e.Rect.Right && y >= e.Rect.Y && y < e.Rect.Bottom) return e;
        }
        return null;
    }

    private List<UiEvent> Activate(UiElement e)
    {
        if (e.Kind == UiKind.Toggle) return ActivateToggle(e);

        var events = new List<UiEvent> { new UiClicked(e.Id) };
        foreach (var owner in _screen.Elements)
        {
            if (owner.List is { } l && _lists.ContainsKey(owner.Id)) LinkedList(owner, l, e.Id, events);
            if (owner.Spin is { } s && _spins.ContainsKey(owner.Id)) LinkedSpin(owner, s, e.Id, events);
        }
        return events;
    }

    private List<UiEvent> ActivateToggle(UiElement e)
    {
        if (_groupOf.TryGetValue(e.Id, out var group))
        {
            int index = group.Members.ToList().IndexOf(e.Id);
            if (GroupIndex(group.Id) == index) return [];
            SetGroupIndex(group.Id, index);
            return [new UiGroupChanged(group.Id, index, e.Id)];
        }
        bool value = !_checked.Contains(e.Id);
        SetChecked(e.Id, value);
        return [new UiToggled(e.Id, value)];
    }

    private void LinkedSpin(UiElement owner, UiSpin spin, string button, List<UiEvent> events)
    {
        int step = button == spin.Increment ? 1 : button == spin.Decrement ? -1 : 0;
        if (step == 0) return;
        var state = _spins[owner.Id];
        int next = Math.Clamp(state.Selected + step, 0, Math.Max(0, state.Options - 1));
        if (next == state.Selected) return;
        state.Selected = next;
        events.Add(new UiSpinChanged(owner.Id, next));
    }

    private void LinkedList(UiElement owner, UiList spec, string button, List<UiEvent> events)
    {
        var state = _lists[owner.Id];
        // Кнопка подтверждения списка — одно нажатие, одно событие: только UiClicked, без UiListConfirmed. У движка у кнопки один
        // обработчик (onClickedFunctor), а логики окон ловили оба события и действовали дважды (партия 16.10, пометка 5: кража у торговца
        // дважды — успех, второй бросок, вор повешен). UiListConfirmed — двойной щелчок по строке.
        if (button == spec.Confirm) return;

        var (columns, rows) = Grid(owner, state);
        int step = button == spec.Up ? -columns : button == spec.Down ? columns
                 : button == spec.Left ? -1 : button == spec.Right ? 1
                 : button == spec.PageUp ? -columns * rows : button == spec.PageDown ? columns * rows : 0;
        if (step == 0 || state.Count == 0) return;
        if (state.Selected < 0)
        {
            // Без выбора — прокрутка: верхний ряд сдвигается на шаг, не дальше последнего ряда.
            int lastTop = Math.Max(0, ((state.Count - 1) / columns - rows + 1) * columns);
            state.Top = Math.Clamp(state.Top + (step / columns * columns), 0, lastTop);
            Revision++;
            return;
        }
        Select(owner, state, state.Selected + step, events);
    }

    private IReadOnlyList<UiEvent> PressList(UiElement list, int x, int y, bool doubleClick)
    {
        var state = _lists[list.Id];
        int index = RowAt(list, state, x - list.Rect.X, y - list.Rect.Y);
        if (index < 0) return [];
        var events = new List<UiEvent>();
        Select(list, state, index, events);
        if (doubleClick) events.Add(new UiListConfirmed(list.Id, state.Selected));
        return events;
    }

    /// <summary>Выбрать пункт (с обрезкой по краям) и прокрутить так, чтобы он был виден.</summary>
    private void Select(UiElement list, ListState state, int index, List<UiEvent> events)
    {
        index = Math.Clamp(index, 0, state.Count - 1);
        var (columns, rows) = Grid(list, state);
        int row = index / columns;
        if (index < state.Top) state.Top = row * columns;
        else if (index >= state.Top + (columns * rows)) state.Top = (row - rows + 1) * columns;
        Revision++;
        if (index == state.Selected) return;
        state.Selected = index;
        events.Add(new UiListSelected(list.Id, index));
    }

    private int RowAt(UiElement list, ListState state, int lx, int ly)
    {
        if (lx < 0 || ly < 0 || lx >= list.Rect.Width || ly >= list.Rect.Height) return -1;
        var (cellW, cellH, gapX, gapY) = Cell(list, state);
        if (cellW <= 0 || cellH <= 0) return -1;
        int col = lx / (cellW + gapX), row = ly / (cellH + gapY);
        if (lx - (col * (cellW + gapX)) >= cellW || ly - (row * (cellH + gapY)) >= cellH) return -1;
        var (columns, rows) = Grid(list, state);
        if (col >= columns || row >= rows) return -1;
        int index = state.Top + (row * columns) + col;
        return index < state.Count ? index : -1;
    }

    private static (int W, int H, int GapX, int GapY) Cell(UiElement list, ListState state)
    {
        var spec = list.List ?? new UiList();
        int w = spec.CellWidth > 0 ? spec.CellWidth : list.Rect.Width;
        int h = state.RowHeight > 0 ? state.RowHeight : spec.CellHeight;
        return (w, h, spec.GapX, spec.GapY);
    }

    /// <summary>Сколько ячеек помещается по ширине и высоте; не меньше одной, чтобы шаги не обнулились.</summary>
    private static (int Columns, int Rows) Grid(UiElement list, ListState state)
    {
        var (w, h, gapX, gapY) = Cell(list, state);
        int columns = w > 0 ? (list.Rect.Width + gapX) / (w + gapX) : 1;
        int rows = h > 0 ? (list.Rect.Height + gapY) / (h + gapY) : 1;
        return (Math.Max(1, columns), Math.Max(1, rows));
    }

    private UiElement Get(string id) =>
        _byId.TryGetValue(id, out var e) ? e : throw new KeyNotFoundException($"{_screen.Id}: нет элемента {id}");

    private ListState List(string id) =>
        _lists.TryGetValue(Get(id).Id, out var s) ? s : throw new KeyNotFoundException($"{_screen.Id}/{id}: не список");

    private SpinState Spin(string id) =>
        _spins.TryGetValue(Get(id).Id, out var s) ? s : throw new KeyNotFoundException($"{_screen.Id}/{id}: не счётчик");

    private static string? Empty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
