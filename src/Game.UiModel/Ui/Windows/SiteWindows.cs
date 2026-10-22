using LastEdict.Data.Menu;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Общее окон площадок (открывает команда сервера <c>StackVisit</c> → <c>0x48b218</c> по категории площадки, у человека — когда
/// отряд подошёл вплотную или щёлкнул по ней, стоя рядом; ответ пользователя 25.09): портрет и имя героя, золото, воины отряда
/// в ячейках от первой ячейки разметки (<see cref="UnitGroupLayout"/>, как у гостя), карточка воина по правой кнопке, имя площадки
/// из сценария как есть (<c>Site.TXT_TITLE</c>, <c>0x600d83</c>; описание окна не показывают). Покупка и продажа — только
/// перетаскиванием с вопросом (двойного щелчка в списках нет).
/// <para>Группа отряда — та же <c>CDDStackGroup</c>, что у гостя окна армии и в окне отряда (конструктор <c>0x4ff166</c> с теми же
/// параметрами у мага <c>0x495ffb</c>, наёмников <c>0x498c31</c>, торговца <c>0x499fcf</c>, тренера <c>0x4a6860</c>): воина можно
/// перетащить на другую позицию — перестановка <see cref="Play.GameSession.MoveInGroup"/> (<c>0x501469</c>), рамки — <see cref="DropFrames.Unit"/>
/// (журнал human, день 30: окно наёмников, <c>swap_check</c> шести позиций и перенос 2 → 0).</para>
/// </summary>
public abstract class SiteLogic : IWindowLogic
{
    public const string Group = "S";

    protected readonly WindowContext C;
    protected readonly UiRequests Requests = new();
    private readonly string _anchor;
    private readonly string _nameText;
    protected UiRect First;

    protected SiteLogic(WindowContext context, string stackId, string siteId, string anchor, string nameText)
    {
        C = context ?? throw new ArgumentNullException(nameof(context));
        StackId = stackId;
        SiteId = siteId;
        _anchor = anchor;
        _nameText = nameText;
    }

    public string StackId { get; }

    public string SiteId { get; }

    public string? LastRefusal { get; protected set; }

    protected MapStack Stack => C.Map.Stacks[StackId];

    protected MapSite Site => C.Map.Sites.First(s => s.Id.Equals(SiteId, StringComparison.OrdinalIgnoreCase));

    protected long Gold => C.Map.Players[Stack.Owner].Bank.Gold;

    /// <summary>Имя площадки из сценария; нет — пусто.</summary>
    public string Title => C.Session.Map.Sites.FirstOrDefault(s => s.Id.Equals(SiteId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";

    /// <summary>Куда пишется имя площадки.</summary>
    protected virtual string TitleElement => "TXT_LABEL1";

    /// <summary>Видны ли герой и его отряд (у торговца на их месте бывает кукла).</summary>
    protected virtual bool GroupShown => true;

    public virtual bool Allows(string element, UiScreenState ui) => true;

    public UiRequest? TakeRequest() => Requests.Take();

    public virtual string Summary
    {
        get
        {
            var cells = C.Session.GroupCells(StackId);
            string group = cells is null ? "------" : new string([.. cells.Cells.Select(id => id.Length == 0 ? '.' : 'x')]);
            return $"площадка={SiteId} имя={(Title.Length > 0 ? Title.Replace(' ', '_') : "-")} S={group} золото={Gold} " +
                   $"отказ={(LastRefusal ?? "-").Replace(' ', '_')}";
        }
    }

    public virtual UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        First = screen.Elements.First(e => e.Id == _anchor).Rect;
        return UnitGroupLayout.Expand(screen, _anchor, Group, leftSide: true);
    }

    public abstract void Open(UiScreenState ui);

    public virtual void Resume(UiScreenState ui) => Refresh(ui);

    public abstract void Handle(UiEvent e, UiScreenState ui);

    /// <summary>Герой, золото, воины.</summary>
    protected virtual void Refresh(UiScreenState ui)
    {
        var stack = Stack;
        string type = C.Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        ui.SetImage("IMG_LEADER", GroupShown && type.Length > 0 ? C.Faces.Large(type) : null);
        ui.SetText(_nameText, GroupShown ? LeaderName : "");
        ui.SetText(TitleElement, Title);
        ui.SetText("TXT_GOLD", $"{Gold}");
        UnitCellsView.Render(ui, Group, GroupShown ? C.Session.GroupCells(StackId) : null, C, stack.Owner);
        Frames(ui);
    }

    /// <summary>Рамки допустимости группы (<see cref="DropFrames"/>) по <see cref="FrameState"/>.</summary>
    protected void Frames(UiScreenState ui)
    {
        var cells = GroupShown ? C.Session.GroupCells(StackId) : null;
        UnitCellsView.Frames(ui, Group, cells, cells is null ? null : FrameState(cells));
    }

    /// <summary>Состояние позиций группы, пока что-то тащат или нажат инструмент; null — рамок нет. Здесь — тащимый воин.</summary>
    protected virtual Func<int, DropFrame>? FrameState(UnitCells cells) =>
        _unitDrag is { } from ? p => DropFrames.Unit(C.Session, StackId, from, StackId, p) : null;

    /// <summary>Позиция тащимого воина отряда; не тащат — null.</summary>
    private int? _unitDrag;

    /// <summary>Нажатие на воина — начало перетаскивания (<c>CDDStackGroup</c>); true — нажали на воина.</summary>
    protected bool PressUnit(int x, int y)
    {
        _unitDrag = CellAt(x, y) is { } p && C.Session.GroupCells(StackId) is { } cells && cells.Cells[p].Length > 0 ? p : null;
        return _unitDrag is not null;
    }

    /// <summary>Бросок тащимого воина на позицию своей группы — перестановка (большой переносит колонку, <see cref="Play.GameSession.Within"/>);
    /// true — тащили воина.</summary>
    protected bool ReleaseUnit(int x, int y)
    {
        if (_unitDrag is not { } from) return false;
        _unitDrag = null;
        if (CellAt(x, y) is { } to && to != from) LastRefusal = C.Session.Run<string?>(new MoveInGroupCommand(StackId, from, to));
        return true;
    }

    /// <summary>Тащат воина: примет ли место — любая позиция группы (обмен с собой — рамка жёлтая, <see cref="DropFrames.Unit"/>).</summary>
    public virtual bool? DropAccepts(int x, int y, UiScreenState ui) => _unitDrag is null ? null : CellAt(x, y) is not null;

    /// <summary>Кадр тащимого воина — его портрет.</summary>
    public virtual string? DragImage =>
        _unitDrag is { } p && C.Session.GroupCells(StackId) is { } cells ? ItemCells.Face(cells.Cells[p], C) : null;

    protected string LeaderName
    {
        get
        {
            var look = C.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(StackId, StringComparison.OrdinalIgnoreCase));
            string type = C.Map.Units.TryGetValue(Stack.LeaderId, out var leader) ? leader.TypeId : "";
            return look is { LeaderName.Length: > 0 } ? look.LeaderName : C.UnitName(type);
        }
    }

    /// <summary>Нажатие: воин отряда — начало перетаскивания, рамки (<c>0x56c58a</c>).</summary>
    public virtual void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        PressUnit(x, y);
        Frames(ui);
    }

    /// <summary>Отпускание: тащили воина — перестановка, рамки снимаются.</summary>
    public virtual void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ReleaseUnit(x, y)) Refresh(ui);
    }

    /// <summary>Двойной щелчок (после <see cref="Press"/>): у площадок движка его нет; свой — у торговца (<see cref="MerchantLogic.DoubleClick"/>).</summary>
    public virtual void DoubleClick(int x, int y, UiScreenState ui)
    {
    }

    public virtual void RightPress(int x, int y, UiScreenState ui)
    {
        if (CellAt(x, y) is { } p && UnitCards.For(C, StackId, p) is { } open) Requests.Add(open);
    }

    /// <summary>Позиция воина под точкой (клетка большого — его позиция); вне ячеек — null.</summary>
    protected int? CellAt(int x, int y)
    {
        if (!GroupShown || UnitGroupLayout.PositionAt(First, leftSide: true, x, y) is not { } p) return null;
        return C.Session.GroupCells(StackId) is { } cells && cells.Cells[p].Length > 0 ? cells.PositionOf(cells.Cells[p]) : p;
    }

    /// <summary>Окно закрывается с сообщением (площадке нечего предложить).</summary>
    protected void CloseWith(string textId)
    {
        Requests.Add(new UiClose());
        Requests.Add(new UiTell(C.Text(textId)));
    }

    /// <summary>Пункт списка картинок под точкой (номер среди пунктов): ячейки по разметке и прокрутка.</summary>
    protected static int? ListAt(UiScreenState ui, string id, int count, int x, int y) => SiteLogicLists.At(ui, id, count, x, y);

    protected static bool Inside(UiScreenState ui, string id, int x, int y) => SiteLogicLists.Inside(ui, id, x, y);
}

/// <summary>
/// Башня мага (<c>DLG_MAGE</c>, <c>CSiteMageInterf</c> <c>0x4956ed</c>): справа заклинания мага, ещё не известные игроку
/// (<c>LBOX_MAGE_INV</c>: значок, имя, <c>X005TA0249</c> цена), слева — купленные за этот визит (<c>LBOX_SPELL</c>). Покупка —
/// перетащить в левый список: не хватает золота — <c>X005TA0577</c>, иначе вопрос <c>X005TA0108</c>. У мага ничего нет —
/// <c>X005TA0825</c>, всё уже известно — <c>X005TA0511</c>, окно закрывается. Имя башни — <c>TXT_MAGE_NAME</c> (<c>0x495827</c>),
/// <c>TXT_LABEL2</c> — <c>X005TA0105</c> с именем расы игрока (<c>0x495e3a</c>). «Известные заклинания» (<c>BTN_KNOWN_SPELL</c>,
/// <c>0x495b6e</c>, доступна всегда) — книга <c>DLG_CAST_SPELL</c> без «Сотворить» (<c>CSpellKnownInterf</c> <c>0x4a1807</c>).
/// </summary>
public sealed class MageLogic(WindowContext context, string stackId, string siteId, IReadOnlyDictionary<string, SpellText> texts,
                              RulesTexts? siteTexts = null)
    : SiteLogic(context, stackId, siteId, "IMG_RSLOT01", "TXT_LDR_NAME")
{
    public const string Screen = "DLG_MAGE";
    private const string Offer = "LBOX_MAGE_INV", Bought = "LBOX_SPELL";
    private readonly List<string> _bought = [];
    private int? _drag;

    public IReadOnlyList<string> Offered =>
        [.. Site.Spells.Where(s => !C.Map.Players[Stack.Owner].KnownSpells.Contains(s, StringComparer.OrdinalIgnoreCase))];

    public override string Summary => base.Summary + $" предлагает={Offered.Count} куплено={_bought.Count}";

    /// <summary>Правая кнопка: товар — карточка заклинания с ценой покупки со скидкой героя (<c>getLowerCost</c>), купленное — без неё;
    /// воин — его карточка.</summary>
    public override void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (SiteLogicLists.At(ui, Offer, Offered.Count, x, y) is { } o)
            Requests.Add(new UiOpen(SpellCardLogic.Screen, new SpellCardLogic(C, Offered[o], texts, buy: true,
                                                                            lowerCostPercent: C.Session.LowerCostPercent(Stack))));
        else if (SiteLogicLists.At(ui, Bought, _bought.Count, x, y) is { } b)
            Requests.Add(new UiOpen(SpellCardLogic.Screen, new SpellCardLogic(C, _bought[b], texts)));
        else base.RightPress(x, y, ui);
    }

    protected override string TitleElement => "TXT_MAGE_NAME";

    /// <summary>Книга известных открывается логикой, а не переходом графа; списки — только перетаскиванием (двойного щелчка у них движок не
    /// ставит, <c>0x53616c</c>; переход графа на <c>LBOX_MAGE_INV</c> — вопрос <c>X005TA0108</c>, записанный по перетаскиванию, без подстановки).</summary>
    public override bool Allows(string element, UiScreenState ui) => element is not ("BTN_KNOWN_SPELL" or Offer or Bought);

    public override void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_LABEL1", C.Text("X005TA0107"));
        string race = C.Map.Players[Stack.Owner].RaceId;
        ui.SetText("TXT_LABEL2", UiTemplate.Fill(C.Text("X005TA0105"),
            ("NAME", siteTexts?.RaceNames.TryGetValue(race, out var name) == true ? C.Text(name) : race)));
        Refresh(ui);
        if (Site.Spells.Count == 0) CloseWith("X005TA0825");
        else if (Offered.Count == 0) CloseWith("X005TA0511");
    }

    public override void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_KNOWN_SPELL" })
            Requests.Add(new UiOpen(CastSpellLogic.Screen, new CastSpellLogic(C, texts, browseOnly: true)));
        Refresh(ui);
    }

    protected override void Refresh(UiScreenState ui)
    {
        base.Refresh(ui);
        var spells = C.Session.RequireBook().Spells;
        // Строка башни — золото BUY_C со скидкой героя (0x4fcfab: × (100 − getLowerCost) / 100).
        ui.SetCards(Offer, [.. Offered.Select(s => Row(s, spells.TryGetValue(s, out var sp) ? C.Session.SpellPrice(Stack, sp).Gold : 0, withPrice: true))]);
        ui.SetCards(Bought, [.. _bought.Select(s => Row(s, 0, withPrice: false))]);
    }

    /// <summary>Строка списка (ячейка 270×80): значок и текст в рамке с <c>x = 0x62</c> на всю высоту (<see cref="RowTextX"/>). Товар мага
    /// (<c>0x4fcf00</c>): <c>\vC;</c> + имя (<c>0x5a5b5c</c>, без жирного) + перевод строки + <c>X005TA0249</c> с ценой + перевод строки +
    /// <c>X005TA0495</c> «Щелкните правой кнопкой для получения информации.»; купленное (<c>0x4fd81d</c>): <c>\hC;\vC;</c> + имя. Снимки
    /// оригинала 19.10 (башня LUTE): у товара три строки, купленное — по центру; у порта были имя жирным, цена и рамка с x = 80.</summary>
    private UiCard Row(string spell, long price, bool withPrice) => new($"IconSpel:{spell.ToUpperInvariant()}", new UiPoint(0, 0),
        withPrice
            ? @"\vC;" + Name(spell) + @"\n" + UiTemplate.Fill(C.Text("X005TA0249"), ("PRICE", price)) + @"\n" + C.Text("X005TA0495")
            : @"\hC;\vC;" + Name(spell),
        new UiRect(RowTextX, 0, RowWidth - RowTextX, RowHeight));

    /// <summary>Левый край текста строки (<c>mov [ebp-0x24], 0x62</c>: <c>0x4fd07c</c>, <c>0x4fd8b5</c>); ширина — ячейки без него
    /// (<c>0x4fd06d</c>, <c>0x5340d6</c>), ячейка <c>LBOX_MAGE_INV</c>/<c>LBOX_SPELL</c> — 270×80.</summary>
    private const int RowTextX = 0x62, RowWidth = 270, RowHeight = 80;

    public override void Press(int x, int y, UiScreenState ui)
    {
        _drag = ListAt(ui, Offer, Offered.Count, x, y);
        if (_drag is null) base.Press(x, y, ui);   // воин отряда
    }

    public override void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var drag = _drag;
        _drag = null;
        if (drag is null) base.Release(x, y, ui);
        if (drag is not { } d || !Inside(ui, Bought, x, y)) return;
        string spell = Offered[d];
        if (!C.Session.RequireBook().Spells.TryGetValue(spell, out var sp)) return;
        if (Economy.NotEnough(C.Map.Players[Stack.Owner].Bank, C.Session.SpellPrice(Stack, sp))) { Requests.Add(new UiTell(C.Text("X005TA0577"))); return; }
        string stack = StackId, site = SiteId;
        Requests.Add(new UiAsk(UiTemplate.Fill(C.Text("X005TA0108"), ("NAME", Name(spell))), () =>
        {
            LastRefusal = C.Session.Run<string?>(new BuySpellCommand(stack, site, spell));
            if (LastRefusal is null) _bought.Add(spell);
            else Requests.Add(new UiTell(C.Text("X005TA0577")));
        }));
    }

    /// <summary>Тащат заклинание мага — примет только левый список купленных (<c>CDDMageInventory</c>); иначе — как у площадки (воин).</summary>
    public override bool? DropAccepts(int x, int y, UiScreenState ui) =>
        _drag is not null ? Inside(ui, Bought, x, y) : base.DropAccepts(x, y, ui);

    /// <summary>Кадр тащимого заклинания — его значок <c>IconSpel</c> (как в строке списка); воин — портрет.</summary>
    public override string? DragImage =>
        _drag is { } d ? d < Offered.Count ? $"IconSpel:{Offered[d].ToUpperInvariant()}" : null : base.DragImage;

    private string Name(string spell) => texts.TryGetValue(spell, out var t) ? C.Text(t.NameTxt) : spell;
}

/// <summary>
/// Тренер (<c>DLG_TRAINING_CAMP</c>, <c>CSiteTrainingCampInterf</c> <c>0x4a5ec8</c>): <c>TOG_TRAINING</c> — инструмент
/// (<c>X005TA0371</c>, без него <c>X005TA0498</c>), щелчок по воину — отказ (<c>X005TA0678</c> нет золота, <c>X005TA0618</c> потолок,
/// <c>X110TA0017</c> курс пройден, <c>X005TA0859</c> мёртв) или вопрос (<c>X005TA0377</c> полное, <c>X005TA0392</c> частичное);
/// после — окно <c>DLG_TRAINING_CONFIRM</c> (<see cref="TrainingDoneLogic"/>). Инструмент отжимается после щелчка. <c>TXT_STACK_INFO</c> —
/// <c>X005TA0370</c> с именем героя (<c>0x4a66c7</c>).
/// </summary>
public sealed class TrainerLogic(WindowContext context, string stackId, string siteId)
    : SiteLogic(context, stackId, siteId, "IMG_RSLOT01", "TXT_LDR_NAME")
{
    public const string Screen = "DLG_TRAINING_CAMP";

    public bool Tool { get; private set; }

    public override void Open(UiScreenState ui) => Refresh(ui);

    public override void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (e is UiToggled { Element: "TOG_TRAINING" } t) Tool = t.Checked;
        Refresh(ui);
    }

    protected override void Refresh(UiScreenState ui)
    {
        base.Refresh(ui);
        ui.SetChecked("TOG_TRAINING", Tool);
        ui.SetText("TXT_TRAINER_INFO", C.Text(Tool ? "X005TA0371" : "X005TA0498"));
        ui.SetText("TXT_STACK_INFO", UiTemplate.Fill(C.Text("X005TA0370"), ("NAME", LeaderName)));
    }

    /// <summary>Инструмент обучения — рамки: учить можно — жёлтая, нельзя — красная (<see cref="DropFrames.Train"/>); без него — тащимый воин.</summary>
    protected override Func<int, DropFrame>? FrameState(UnitCells cells) =>
        Tool ? p => DropFrames.Train(C, cells, p, Stack.Owner) : base.FrameState(cells);

    /// <summary>Инструмент нажат — воина не тащат (инструмент сам — перетаскивание, <c>CMidDropAction</c>).</summary>
    public override void Press(int x, int y, UiScreenState ui)
    {
        if (!Tool) base.Press(x, y, ui);
    }

    public override void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!Tool)
        {
            base.Release(x, y, ui);
            return;
        }
        if (CellAt(x, y) is not { } p || C.Session.GroupCells(StackId) is not { } cells || cells.Cells[p].Length == 0) return;
        Tool = false;
        Train(cells.Cells[p]);
        Refresh(ui);
    }

    private void Train(string unitId)
    {
        string? why = C.Session.TrainRefusal(Stack.Owner, unitId);
        string? text = why switch
        {
            null => null,
            "у хозяина нет золота" => "X005TA0678",
            var w when w.StartsWith("курс пройден", StringComparison.Ordinal) => "X110TA0017",
            var w when w.EndsWith("пал", StringComparison.Ordinal) => "X005TA0859",
            _ => "X005TA0618",
        };
        if (text is not null) { LastRefusal = why; Requests.Add(new UiTell(C.Text(text))); return; }
        var unit = C.Map.Units[unitId];
        int next = C.Session.RequireBook().XpNextOf(unit.TypeId, unit.Level);
        if (C.Session.TrainingRate(unitId) is not { } rate) { LastRefusal = "нет цены обучения"; Requests.Add(new UiTell(C.Text("X005TA0678"))); return; }
        var bought = Treasury.Training(rate, next, unit.Xp, C.Map.Players[Stack.Owner].Bank);
        if (bought.Points == 0) { LastRefusal = "казны не хватает ни на одно очко"; Requests.Add(new UiTell(C.Text("X005TA0678"))); return; }
        bool full = bought.Points >= next - 1 - unit.Xp;
        string ask = full
            ? UiTemplate.Fill(C.Text("X005TA0377"), ("PRICE", bought.Cost.Gold))
            : UiTemplate.Fill(C.Text("X005TA0392"), ("PRICE", bought.Cost.Gold), ("XP", bought.Points));
        string stack = StackId, site = SiteId;
        Requests.Add(new UiAsk(ask, () =>
        {
            LastRefusal = C.Session.Run<(string?, Bank)>(new TrainCommand(stack, site, unitId)).Item1;
            if (LastRefusal is not null) { Requests.Add(new UiTell(C.Text("X005TA0678"))); return; }
            Requests.Add(new UiOpen(TrainingDoneLogic.Screen, new TrainingDoneLogic(C, unitId, full)));
        }));
    }
}

/// <summary>Итог обучения (<c>DLG_TRAINING_CONFIRM</c>, <c>CPostTrainingMsgBox</c> <c>0x49e298</c>): <c>X005TA0453</c> или
/// <c>X005TA0662</c> (частичная), <c>X005TA0454</c> имя и опыт, портрет; «ОК» закрывает.</summary>
public sealed class TrainingDoneLogic(WindowContext context, string unitId, bool full) : IWindowLogic
{
    public const string Screen = "DLG_TRAINING_CONFIRM";
    private readonly UiRequests _requests = new();
    private string _face = "";

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"обучен={unitId} полностью={(full ? 1 : 0)} лицо={_face}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var unit = context.Map.Units[unitId];
        ui.SetText("TXT_TITLE", context.Text(full ? "X005TA0453" : "X005TA0662"));
        ui.SetText("TXT_XP_INFO", UiTemplate.Fill(context.Text("X005TA0454"), ("NAME", context.UnitName(unit.TypeId)),
            ("XPVALUE1", unit.Xp), ("XPVALUE2", context.Session.RequireBook().XpNextOf(unit.TypeId, unit.Level))));
        _face = Face(context, unit);
        ui.SetImage("IMG_UNIT_FACE", _face, UiImageAlign.Center);
    }

    /// <summary>Портрет в рамке (<c>0x49e5cb</c> → <c>0x5b0bdd</c> лицо <c>{база}FACE</c> без доли здоровья; <c>0x5e9830</c> малый — рамка
    /// <c>0x5b680b</c> <c>BORDERUNITSMALL</c> 84×99, большой — <c>0x5b6870</c> <c>BORDERUNITLARGE</c> 164×99). Место <c>IMG_UNIT_FACE</c> (84×99)
    /// движок раздвигает по ширине картинки с центром на месте (<c>0x49e606…0x49e671</c>: x = левый + (ширина места − ширина картинки)/2,
    /// высота прежняя) — то же, что <see cref="UiImageAlign.Center"/> при высоте рамки, равной месту. Прежде порт клал голый широкий
    /// <c>FACE</c> большого юнита, ужатый в место (пометка 32 партии 17.10: Горгулья «полоской» 100×56).</summary>
    public static string Face(WindowContext context, LastEdict.Rules.Map.MapUnit unit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(unit);
        bool big = context.Session.RequireBook().Rules.Units.TryGetValue(unit.TypeId, out var type) && !type.SizeSmall;
        return UiFrameRef.Layered(big ? LastEdict.Data.Map.MapCards.LargeBorder : LastEdict.Data.Map.MapCards.SmallBorder, context.Faces.Small(unit.TypeId));
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_OK";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_OK" }) _requests.Add(new UiClose());
    }
}

/// <summary>
/// Наёмники (<c>DLG_MERCENARIES</c>, <c>CSiteMercenaryInterf</c> <c>0x497e49</c>): ростер (<c>LBOX_UNIT_LIST</c>, в строке только портрет
/// <c>FACE</c>, <c>0x503516</c>), взятый воин (<c>0x498886</c>) — <c>X005TA0489</c> с именем и описанием типа и цена <c>X005TA0490</c>,
/// отпущенный (<c>0x498ab5</c>) — снова <c>X005TA0492</c>; <c>TXT_STACK_INFO</c> — <c>X005TA0127</c> с лидерством. При открытии:
/// места нет — <c>X005TA0247</c>, одно место и в ростере большие — <c>X005TA0501</c>/<c>0502</c>, ростер пуст — <c>X005TA0514</c> и
/// окно закрывается. Найм — перетащить воина на ячейку отряда (встаёт туда): нет золота — <c>X005TA0590</c>, иначе вопрос
/// <c>X005TA0129</c>. <c>TOG_DISMISS</c> — отпуск, как в окне армии.
/// Цена — <c>ENROLL_C × (100 − скидка героя) / 100</c> (<see cref="Play.GameSession.MercenaryPrice"/>); её же списывает
/// <see cref="Play.GameSession.HireMercenary"/>.
/// </summary>
public sealed class MercenaryLogic(WindowContext context, string stackId, string siteId, RulesTexts? siteTexts = null)
    : SiteLogic(context, stackId, siteId, "IMG_RSLOT01", "TXT_LEADER_NAME")
{
    public const string Screen = "DLG_MERCENARIES";
    private const string List = "LBOX_UNIT_LIST";
    private int? _drag;

    public bool Dismissing { get; private set; }

    public IReadOnlyList<MercenaryOffer> Roster => Site.Roster;

    public override string Summary => base.Summary + $" ростер={Roster.Count}";

    public override void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_DISPLAY1", C.Text("X005TA0492"));
        Refresh(ui);
        if (Roster.Count == 0) { CloseWith("X005TA0514"); return; }
        int free = C.Session.FreeCells(Stack);
        var book = C.Session.RequireBook();
        bool big = Roster.Any(o => book.Rules.Units.TryGetValue(o.UnitType, out var t) && !t.SizeSmall);
        bool small = Roster.Any(o => book.Rules.Units.TryGetValue(o.UnitType, out var t) && t.SizeSmall);
        if (free <= 0) Requests.Add(new UiTell(C.Text("X005TA0247")));
        else if (free == 1 && big) Requests.Add(new UiTell(C.Text(small ? "X005TA0501" : "X005TA0502")));
    }

    public override void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (e is UiToggled { Element: "TOG_DISMISS" } t) Dismissing = t.Checked;
        Refresh(ui);
    }

    protected override void Refresh(UiScreenState ui)
    {
        base.Refresh(ui);
        ui.SetChecked("TOG_DISMISS", Dismissing);
        var cells = C.Session.GroupCells(StackId);
        string lead = UiTemplate.Fill(C.Text("X005TA0575"), ("USED", (cells?.Occupied ?? 1) - 1), ("AVAIL", C.Session.Leadership(Stack) - 1));
        ui.SetText("TXT_STACK_INFO", UiTemplate.Fill(C.Text("X005TA0127"), ("NAME", LeaderName), ("LEADERSHIP", lead)));
        ui.SetCards(List, [.. Roster.Select(Row)]);
    }

    /// <summary>
    /// Строка ростера (<c>CDDMercenaryInvDisplay</c>, рисование строки <c>0x503e47</c>): портрет типа (<c>0x62010e</c> → <c>0x5b0bdd</c>) в
    /// левом верхнем углу ячейки — в левой рамке подложки (большой — на обе), и подпись <c>0x61fe1e</c> с флагами «имя, ОЗ, цена»
    /// (<c>X005TA0435</c> имя, <c>X005TA0436</c> ОЗ типа «N/N», <c>X005TA0613</c> цена найма со скидкой героя, <c>× (100 − скидка) / 100</c>) в
    /// месте от точки (14, 115) до правого нижнего угла ячейки без точки — свиток пергамента под рамками. Прежде портрет стоял по
    /// середине ячейки (снимок пользователя 17.10: «портреты поползли», «текста нет»).
    /// </summary>
    private UiCard Row(MercenaryOffer offer)
    {
        const int TextX = 0xe, TextY = 0x73;
        // ОЗ — того экземпляра типа, что в ростере (выросший наёмник — с ростом уровня, GDynUpgr).
        int hp = C.Session.RequireBook().Grown(offer.UnitType, offer.Level)?.Type.HitPoints ?? 0;
        string text = UiTemplate.Fill(C.Text("X005TA0435"), ("NAME", C.UnitName(offer.UnitType)))
                      + UiTemplate.Fill(C.Text("X005TA0436"), ("HPVALUE1", hp), ("HPVALUE2", hp))
                      + UiTemplate.Fill(C.Text("X005TA0613"), ("COST_HIRING", Price(offer)));
        return new UiCard(C.Faces.Small(offer.UnitType), new UiPoint(0, 0), text,
                          new UiRect(TextX, TextY, RowWidth - 1 - TextX, RowHeight - 1 - TextY));
    }

    /// <summary>Ячейка ростера по разметке (<c>LBOX_UNIT_LIST</c> 266×195: одна строка на страницу).</summary>
    private const int RowWidth = 266, RowHeight = 195;

    /// <summary>Тащат наёмника из ростера — примет позиция отряда с рамкой (<see cref="DropFrames.Mercenary"/>); иначе — как у площадки.</summary>
    public override bool? DropAccepts(int x, int y, UiScreenState ui) =>
        _drag is { } d && d < Roster.Count ? DropAccepts(d, x, y) : base.DropAccepts(x, y, ui);

    private bool DropAccepts(int d, int x, int y)
    {
        if (CellAt(x, y) is not { } cell || C.Session.GroupCells(StackId) is not { } cells) return false;
        bool big = C.Session.RequireBook().Rules.Units.TryGetValue(Roster[d].UnitType, out var type) && !type.SizeSmall;
        return DropFrames.Mercenary(big, C.Session.FreeCells(Stack), cells, cell) != DropFrame.None;
    }

    /// <summary>Кадр тащимого наёмника — его портрет (<c>CDDMercenaryInventory</c>); воин отряда — его портрет.</summary>
    public override string? DragImage =>
        _drag is { } d ? d < Roster.Count ? C.Faces.Small(Roster[d].UnitType) : null : base.DragImage;

    private long Price(MercenaryOffer offer) =>
        C.Session.MercenaryPrice(Stack, offer)?.Gold ?? 0;

    /// <summary>Рамки: инструмент отпуска (<see cref="DropFrames.Dismiss"/>) или наёмник из списка (<see cref="DropFrames.Mercenary"/>).</summary>
    protected override Func<int, DropFrame>? FrameState(UnitCells cells)
    {
        if (Dismissing) return p => DropFrames.Dismiss(C, StackId, cells, p, garrison: false);
        if (_drag is not { } d || d >= Roster.Count) return base.FrameState(cells);
        bool big = C.Session.RequireBook().Rules.Units.TryGetValue(Roster[d].UnitType, out var type) && !type.SizeSmall;
        int free = C.Session.FreeCells(Stack);
        return p => DropFrames.Mercenary(big, free, cells, p);
    }

    public override void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _drag = Dismissing ? null : ListAt(ui, List, Roster.Count, x, y);
        if (!Dismissing && _drag is null) PressUnit(x, y);   // воин отряда
        Frames(ui);   // начало перетаскивания — рамки у мест броска (0x56c58a)
        if (_drag is not { } d) return;
        string type = Roster[d].UnitType;
        ui.SetText("TXT_DISPLAY1", UiTemplate.Fill(C.Text("X005TA0489"), ("NAME", C.UnitName(type)),
            ("DESC", siteTexts?.UnitDescs.TryGetValue(type, out var desc) == true ? C.Text(desc) : "")));
        ui.SetText("TXT_PRICE", UiTemplate.Fill(C.Text("X005TA0490"), ("PRICE", Price(Roster[d]))));
    }

    public override void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ReleaseUnit(x, y))
        {
            Refresh(ui);
            return;
        }
        var drag = _drag;
        _drag = null;
        Frames(ui);   // бросок — рамки снимаются (0x56c606)
        ui.SetText("TXT_DISPLAY1", C.Text("X005TA0492"));
        ui.SetText("TXT_PRICE", "");
        if (Dismissing)
        {
            if (CellAt(x, y) is { } p && C.Session.GroupCells(StackId) is { } cells && cells.Cells[p].Length > 0)
                LastRefusal = UnitOrders.Dismiss(C, Requests, StackId, cells.Cells[p], r => LastRefusal = r);
            Dismissing = false;
            Refresh(ui);
            return;
        }
        // Бросок — только при рамке не «нет» (0x56eace), как говорит курсор (DropAccepts): занятая клетка или нет места — ничего.
        if (drag is not { } d || d >= Roster.Count || !DropAccepts(d, x, y) || CellAt(x, y) is not { } cell) return;
        var offer = Roster[d];
        if (Gold < Price(offer)) { Requests.Add(new UiTell(C.Text("X005TA0590"))); return; }
        bool big = C.Session.RequireBook().Rules.Units.TryGetValue(offer.UnitType, out var type) && !type.SizeSmall;
        int at = big ? Formation.ColumnFront(cell) : cell;
        string stack = StackId, site = SiteId, unit = offer.UnitType;
        Requests.Add(new UiAsk(UiTemplate.Fill(C.Text("X005TA0129"), ("NAME", C.UnitName(unit))), () =>
        {
            LastRefusal = C.Session.Run<string?>(new HireMercenaryCommand(stack, site, unit, at));
            if (LastRefusal is not null) Requests.Add(new UiTell(C.Text("X005TA0247")));
        }));
    }
}
