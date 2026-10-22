using System.Globalization;
using D2.Data.Content;
using D2.Data.Menu;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>Имя, описание и текст эффекта заклинания (<c>Gspells.NAME_TXT</c>/<c>DESC_TXT</c>/<c>MODIF_TXT</c>).</summary>
public sealed record SpellText(string NameTxt, string DescTxt, string ModifTxt);

/// <summary>
/// Общее окон заклинаний (<c>CSpellBook</c> <c>0x49ec17</c>): исследование <c>DLG_RESEARCH_SPELL</c> и книга
/// <c>DLG_CAST_SPELL</c>. Список <c>LBOX_SPELL_LIST</c> по уровню (<c>RAD_SPELL_LEVEL</c>, выбор помнится между открытиями):
/// строка — значок <c>IconSpel</c>, имя <c>X005TA0654</c> и описание (<c>0x4a34f7</c>); справа — значок, имя, <c>X005TA0438</c>
/// уровень, <c>X005TA0136</c> тип (источник урона), <c>X005TA0472</c> эффект, стоимость по видам маны (<c>X005TA0425</c>…),
/// внизу — запас маны игрока (<c>X005TA0206</c>, <c>0x4a1c85</c>).
/// </summary>
public abstract class SpellBookLogic : IWindowLogic
{
    protected const string List = "LBOX_SPELL_LIST", Group = "RAD_SPELL_LEVEL", IconArchive = "IconSpel", Known = "Icons:ICNSY001";
    private const int IconW = 70, RowW = 290, RowH = 85;

    protected readonly WindowContext C;
    protected readonly IReadOnlyDictionary<string, SpellText> Texts;
    protected readonly UiRequests Requests = new();
    private IReadOnlyList<string> _shown = [];

    protected SpellBookLogic(WindowContext context, IReadOnlyDictionary<string, SpellText> texts)
    {
        C = context ?? throw new ArgumentNullException(nameof(context));
        Texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

    /// <summary>Тексты заклинаний содержимого.</summary>
    public static IReadOnlyDictionary<string, SpellText> LoadTexts(string rulesDir) => LoadTexts(ContentSource.Open(rulesDir));

    public static IReadOnlyDictionary<string, SpellText> LoadTexts(ContentSet content) =>
        content.Spells.ToDictionary(
            s => s.Key.ToUpperInvariant(), s => new SpellText(s.NameText ?? "", s.DescriptionText ?? "", s.ModifierText ?? ""),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Открытый уровень (1…5).</summary>
    public int Level { get; protected set; } = 1;

    /// <summary>Заклинания открытого уровня по порядку списка.</summary>
    public IReadOnlyList<string> Shown => _shown;

    /// <summary>Выбранное заклинание; нет — null.</summary>
    public string? Selected { get; private set; }

    public string? LastRefusal { get; protected set; }

    public UiRequest? TakeRequest() => Requests.Take();

    public virtual string Summary =>
        $"уровень={Level} заклинаний={_shown.Count} выбрано={Selected ?? "-"} известно={C.Player.KnownSpells.Count} " +
        $"отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    /// <summary>Заклинания уровня для списка.</summary>
    protected abstract IReadOnlyList<string> SpellsOf(int level);

    /// <summary>Уровень, на котором окно открывается.</summary>
    protected abstract int StartLevel();

    /// <summary>Уровень запомнен — хозяин памяти окна.</summary>
    protected abstract void Remember(int level);

    /// <summary>Строка вместо списка, если уровень недоступен (<c>TXT_NO_SPELL</c>); доступен — null.</summary>
    protected abstract string? Unavailable(int level);

    /// <summary>Значок «уже изучено / уже брошено» поверх строки.</summary>
    protected abstract bool Marked(string spellId);

    /// <summary>Доступна ли кнопка действия для выбранного.</summary>
    protected abstract bool CanAct(string spellId);

    /// <summary>Стоимость справа (<c>TXT_COST</c>).</summary>
    protected abstract string Cost(string spellId);

    protected abstract string ActionButton { get; }

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        Level = StartLevel();
        ui.SetGroupIndex(Group, Level - 1);
        ui.SetText("TXT_AVAILABLE", C.Text("X005TA0206"));
        Fill(ui);
        Opened(ui);
    }

    /// <summary>Досборка окна после первого заполнения.</summary>
    protected virtual void Opened(UiScreenState ui)
    {
    }

    public void Resume(UiScreenState ui) => Fill(ui);

    /// <summary>Действие делает сама логика (вопрос, прицел); у списка в графе есть чужие переходы (книга служит и окну обмена
    /// заклинаниями — <c>DLG_GIVE_ITEM_SPELL</c>), поэтому и от списка переходов нет.</summary>
    public bool Allows(string element, UiScreenState ui) => element != ActionButton && element != List;

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiGroupChanged { Element: Group } g:
                Level = g.Index + 1;
                Remember(Level);
                Selected = null;
                Fill(ui);
                return;
            case UiListSelected { Element: List } s when s.Index < _shown.Count:
                Selected = _shown[s.Index];
                break;
            case UiListConfirmed { Element: List } c when c.Index < _shown.Count:
                Selected = _shown[c.Index];
                Act(Selected);
                break;
            case UiClicked b when b.Element == ActionButton && Selected is { } chosen:
                Act(chosen);
                break;
        }
        Details(ui);
    }

    /// <summary>Правая кнопка на строке списка или на большом значке выбранного — карточка заклинания (<see cref="SpellCardLogic"/>).</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        string? spell = SiteLogicLists.At(ui, List, _shown.Count, x, y) is { } row ? _shown[row]
            : ui.Screen.Elements.Any(e => e.Id == "IMG_SPELL") && SiteLogicLists.Inside(ui, "IMG_SPELL", x, y) ? Selected : null;
        if (spell is not null) Requests.Add(new UiOpen(SpellCardLogic.Screen, new SpellCardLogic(C, spell, Texts)));
    }

    /// <summary>Нажатие кнопки действия (подтверждение списка) на выбранном.</summary>
    protected abstract void Act(string spellId);

    protected void Fill(UiScreenState ui)
    {
        string? blocked = Unavailable(Level);
        _shown = blocked is null ? SpellsOf(Level) : [];
        ui.SetText("TXT_NO_SPELL", blocked ?? (_shown.Count == 0 ? NoneText : ""));
        ui.SetCards(List, [.. _shown.Select(Row)]);
        int at = Selected is null ? -1 : _shown.ToList().FindIndex(s => s.Equals(Selected, StringComparison.OrdinalIgnoreCase));
        if (at < 0 && _shown.Count > 0) at = 0;
        Selected = at >= 0 ? _shown[at] : null;
        if (at >= 0) ui.Select(List, at);
        Mana(ui);
        Details(ui);
    }

    /// <summary>Строка пустого уровня.</summary>
    protected virtual string NoneText => "";

    private UiCard Row(string spell) => new($"{IconArchive}:{spell.ToUpperInvariant()}", new UiPoint(0, 0),
        UiTemplate.Fill(@"\vC;" + C.Text("X005TA0654"), ("NAME", Name(spell))) + @"\n" + Desc(spell),
        new UiRect(IconW + 10, 0, RowW - IconW - 10, RowH), Marked(spell) ? Known : null);

    private void Details(UiScreenState ui)
    {
        ui.SetEnabled(ActionButton, Selected is { } sel && CanAct(sel));
        if (Selected is not { } id || !C.Session.RequireBook().Spells.TryGetValue(id, out var spell))
        {
            ui.SetImage("IMG_SPELL", null);
            foreach (string t in (string[])["TXT_SPELL_INFO_2", "TXT_SPELL_STATS", "TXT_COST"]) ui.SetText(t, "");
            return;
        }
        ui.SetImage("IMG_SPELL", $"{IconArchive}:{id.ToUpperInvariant()}");
        ui.SetText("TXT_SPELL_INFO_2", Name(id));
        string stats = UiTemplate.Fill(C.Text("X005TA0438"), ("LEVEL", spell.Level));
        if (spell.DamageQty > 0 && spell.DamageSource is >= 0 and <= 7)
            stats += UiTemplate.Fill(C.Text("X005TA0136"), ("SOURCE", C.Text($"X005TA{145 + spell.DamageSource:0000}")));
        if (Texts.TryGetValue(id, out var t2) && t2.ModifTxt.Length > 0 && C.Strings.ContainsKey(t2.ModifTxt))
            stats += UiTemplate.Fill(C.Text("X005TA0472"), ("DESC", C.Text(t2.ModifTxt)));
        ui.SetText("TXT_SPELL_STATS", stats);
        ui.SetText("TXT_COST", Cost(id));
    }

    /// <summary>Запас маны игрока по видам (<c>TXT_ORANGE</c> Смерть, <c>BLANC</c> Руны, <c>JAUNE</c> Жизнь, <c>ROUGE</c>
    /// Преисподняя, <c>BLUE</c> Лес).</summary>
    protected void Mana(UiScreenState ui)
    {
        var bank = C.Player.Bank;
        ui.SetText("TXT_ORANGE", Num(bank.Death));
        ui.SetText("TXT_BLANC", Num(bank.Runic));
        ui.SetText("TXT_JAUNE", Num(bank.Life));
        ui.SetText("TXT_ROUGE", Num(bank.Infernal));
        ui.SetText("TXT_BLUE", Num(bank.Grove));
    }

    /// <summary>Мана по видам строками <c>X005TA0425</c>… — только ненулевые, без золота.</summary>
    protected string ManaLines(Bank cost)
    {
        string s = "";
        if (cost.Runic > 0) s += UiTemplate.Fill(C.Text("X005TA0425"), ("WHITEAMOUNT", cost.Runic));
        if (cost.Life > 0) s += UiTemplate.Fill(C.Text("X005TA0428"), ("YELLOWAMOUNT", cost.Life));
        if (cost.Infernal > 0) s += UiTemplate.Fill(C.Text("X005TA0426"), ("REDAMOUNT", cost.Infernal));
        if (cost.Death > 0) s += UiTemplate.Fill(C.Text("X005TA0427"), ("ORANGEAMOUNT", cost.Death));
        if (cost.Grove > 0) s += UiTemplate.Fill(C.Text("X160TA0002"), ("BLUEAMOUNT", cost.Grove));
        return s;
    }

    /// <summary>Цена словами для вопроса (<c>0x61c910</c>): золото <c>X005TA0055</c>, Руны <c>0056</c>, Смерть <c>0057</c>,
    /// Жизнь <c>0058</c>, Преисподняя <c>0059</c>, Лес <c>X160TA0001</c>. Части — через «, » (<c>0x61c910</c>, строка <c>0x6e12f4</c>), нулевые пропускаются.</summary>
    protected string PriceWords(Bank price) => PriceText.Words(C.Text, price);

    protected string Name(string spell) => Texts.TryGetValue(spell, out var t) ? C.Text(t.NameTxt) : spell;

    private string Desc(string spell) => Texts.TryGetValue(spell, out var t) ? C.Text(t.DescTxt) : "";

    protected bool IsMage => C.Session.RequireBook().LordCategory(C.Player.LordId) == Treasury.MageCategory;

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Исследование заклинаний в столице (<c>DLG_RESEARCH_SPELL</c>, <c>CSpellResInterf</c> <c>0x4a1909</c>; открывается при
/// постройке магии, иначе <c>X005TA0405</c>). Список уровня (<c>0x4a3252</c>): ещё не изученные из списка лорда
/// (<c>GspellR</c>) с уровнем не выше потолка сценария, плюс уже изученные этого уровня (с отметкой <c>ICNSY001</c>); уровень
/// выше потолка — <c>X005TA0287</c>, пятый не у мага — <c>X005TA0444</c>. «Исследовать» (<c>0x4a1ef3</c>): нет башни —
/// <c>X005TA0074</c>, сегодня уже — <c>X005TA0252</c>, мало маны — <c>X005TA0251</c>, иначе вопрос <c>X005TA0269</c>; окно
/// после исследования не закрывается, список перестраивается (<see cref="Play.GameSession.ResearchSpell"/>).
/// </summary>
public sealed class ResearchSpellLogic(WindowContext context, IReadOnlyDictionary<string, SpellText> texts)
    : SpellBookLogic(context, texts)
{
    public const string Screen = "DLG_RESEARCH_SPELL";

    /// <summary>Категория постройки магии (<c>Lbuild</c> 2 — башня магии).</summary>
    public const int MagicCategory = 2;

    protected override string ActionButton => "BTN_RESEARCH_SPELL";

    /// <summary>Есть ли у игрока постройка магии — без неё окна нет (<c>0x4af8d8</c>).</summary>
    public static bool HasMagic(WindowContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var buildings = c.Session.RequireBook().Economy.Buildings;
        return c.Player.Buildings.Any(b => buildings.TryGetValue(b, out var t) && t.Category == MagicCategory);
    }

    private int MaxLevel => C.Map.Limits?.MaxSpell is > 0 and var max ? max : 5;

    protected override IReadOnlyList<string> SpellsOf(int level)
    {
        var book = C.Session.RequireBook();
        var known = C.Player.KnownSpells.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lord = (book.LordResearch.GetValueOrDefault(C.Player.LordId) ?? []).Select(r => r.SpellId);
        return [.. lord.Concat(known).Distinct(StringComparer.OrdinalIgnoreCase)
                       .Where(s => book.Spells.TryGetValue(s, out var spell) && spell.Level == level)
                       .Order(StringComparer.OrdinalIgnoreCase)];
    }

    protected override int StartLevel() => C.Memory.ResearchLevel;

    protected override void Remember(int level) => C.Memory.ResearchLevel = level;

    protected override string? Unavailable(int level) =>
        level > MaxLevel ? C.Text("X005TA0287") : level == 5 && !IsMage ? C.Text("X005TA0444") : null;

    protected override bool Marked(string spellId) => C.Player.KnownSpells.Contains(spellId, StringComparer.OrdinalIgnoreCase);

    protected override bool CanAct(string spellId) => !Marked(spellId);

    protected override string Cost(string spellId)
    {
        var (_, price) = C.Session.ResearchCheck(C.Viewer, spellId);
        var spell = C.Session.RequireBook().Spells[spellId];
        return C.Text("X005TA0286") + ManaLines(price) + C.Text("X005TA0207") + ManaLines(spell.CastingCost);
    }

    protected override void Act(string spellId)
    {
        if (Marked(spellId)) return;
        var (refused, price) = C.Session.ResearchCheck(C.Viewer, spellId);
        string? text = !HasMagic(C) ? "X005TA0074"
            : refused == "сегодня уже исследовали" ? "X005TA0252"
            : refused == "не хватает казны" ? "X005TA0251"
            : null;
        if (text is not null) { Requests.Add(new UiTell(C.Text(text))); return; }
        if (refused is not null) { LastRefusal = refused; return; }
        string player = C.Viewer;
        Requests.Add(new UiAsk(UiTemplate.Fill(C.Text("X005TA0269"), ("NAME", Name(spellId)), ("PRICE", PriceWords(price))), () =>
        {
            LastRefusal = C.Session.Run<string?>(new ResearchSpellCommand(player, spellId));
            if (LastRefusal is not null) Requests.Add(new UiTell(LastRefusal));
        }));
    }
}
