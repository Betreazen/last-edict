using LastEdict.Data.Menu;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Начать прицел заклинанием <see cref="SpellId"/> на карте (<c>CTaskCastSpell</c> <c>0x4d2745</c>).</summary>
public sealed record UiCast(string SpellId, string? StackId = null, string? ItemId = null) : UiRequest;

/// <summary>
/// Книга заклинаний на карте (<c>DLG_CAST_SPELL</c>, <c>CSpellCastInterf</c> <c>0x4a030b</c>; из <c>DLG_STRATEGIC.BTN_CAST_SPELL</c>,
/// ничего не изучено — <c>X005TA0406</c>). Список — изученные заклинания уровня (пустой уровень — <c>X005TA0430</c>); открывается на
/// запомненном уровне, иначе на первом, где есть заклинания (<c>0x49fdf3</c>); отметка <c>ICNSY001</c> — нельзя бросить не из-за
/// маны (уже брошено сегодня). «Сотворить» (<c>0x4a0581</c>): сегодня уже — <c>X005TA0445</c> (магу — <c>X005TA0446</c>), мало маны —
/// <c>X005TA0215</c>, лорда нет в <c>Glord</c> — <c>X005TA0213</c> — сообщение поверх книги, книга остаётся (<c>CMidTaskMsgBox</c> в своём диспетчере задач книги); иначе книга
/// закрывается и: заклинание на всю карту — вопрос <c>X005TA0702</c> и литьё, прочее — прицел на карте
/// (<see cref="UiCast"/>; ответ пользователя 25.09: курсор-прицел, щелчок по карте без вопроса, квадрат области под курсором).
/// <paramref name="browseOnly"/> — книга известных заклинаний у мага (<c>CSpellKnownInterf</c> <c>0x4a1807</c>): «Сотворить» спрятана
/// (<c>0x50b795</c>), литья нет.
/// </summary>
public sealed class CastSpellLogic(WindowContext context, IReadOnlyDictionary<string, SpellText> texts, bool browseOnly = false)
    : SpellBookLogic(context, texts)
{
    public const string Screen = "DLG_CAST_SPELL";

    /// <summary>Площадь «вся карта» (<c>AREA</c> 999, <c>0x582b5a</c>).</summary>
    public const int WholeMap = 999;

    protected override string ActionButton => "BTN_CAST_SPELL";

    protected override string NoneText => C.Text("X005TA0430");

    /// <summary>Изучено ли хоть что-то — без этого книга не открывается (<c>0x597fbb</c>).</summary>
    public static bool AnyKnown(WindowContext c) => c?.Player.KnownSpells.Count > 0;

    protected override IReadOnlyList<string> SpellsOf(int level)
    {
        var spells = C.Session.RequireBook().Spells;
        return [.. C.Player.KnownSpells.Where(s => spells.TryGetValue(s, out var spell) && spell.Level == level)
                                       .Order(StringComparer.OrdinalIgnoreCase)];
    }

    protected override int StartLevel()
    {
        if (C.Memory.CastLevel > 0) return C.Memory.CastLevel;
        for (int level = 1; level <= 5; level++)
            if (SpellsOf(level).Count > 0) return level;
        return 1;
    }

    protected override void Remember(int level) => C.Memory.CastLevel = level;

    protected override string? Unavailable(int level) => null;

    protected override bool Marked(string spellId) => CastsToday(spellId) >= CastsPerDay;

    protected override bool CanAct(string spellId) => !browseOnly && C.Player.KnownSpells.Contains(spellId, StringComparer.OrdinalIgnoreCase);

    protected override void Opened(UiScreenState ui)
    {
        if (browseOnly) ui.SetHidden(ActionButton, true);
        ui.SetHidden("BTN_TRADE_SPELL", true);   // кнопка продажи — только у продажи из дипломатии (CTradeSpellInterf 0x4b9107)
    }

    protected override string Cost(string spellId) =>
        C.Text("X005TA0207") + ManaLines(C.Session.RequireBook().Spells[spellId].CastingCost);

    /// <summary>Бросков за день: лорду-магу два (<c>0x5dba61</c>, <c>[0x837760]</c>), прочим одно.</summary>
    private int CastsPerDay => IsMage ? 2 : 1;

    private int CastsToday(string spellId) =>
        C.Map.SpellCasts.Count(c => c.PlayerId.Equals(C.Viewer, StringComparison.OrdinalIgnoreCase)
                                    && c.Origin.Equals(spellId, StringComparison.OrdinalIgnoreCase) && SpellCast.Live(c.TurnStop, C.Map.Turn));

    protected override void Act(string spellId)
    {
        if (!CanAct(spellId) || !C.Session.RequireBook().Spells.TryGetValue(spellId, out var spell)) return;
        // Цена литья берётся по лорду (0x582afa): лорда игрока нет в Glord — «недостаточно могущественны» (код 0xa, 0x5dba61).
        if (C.Session.RequireBook().LordCategory(C.Player.LordId) < 0) { Requests.Add(new UiTell(C.Text("X005TA0213"))); return; }
        if (Marked(spellId)) { Requests.Add(new UiTell(C.Text(IsMage ? "X005TA0446" : "X005TA0445"))); return; }
        if (!SpellCast.CanPay(C.Player.Bank, spell)) { Requests.Add(new UiTell(C.Text("X005TA0215"))); return; }
        Requests.Add(new UiClose());
        if (spell.Area != WholeMap)
        {
            Requests.Add(new UiCast(spellId));
            return;
        }
        string player = C.Viewer;
        Requests.Add(new UiAsk(UiTemplate.Fill(C.Text("X005TA0702"), ("SPELLNAME", Name(spellId))), () =>
        {
            LastRefusal = C.Session.Run<string?>(new CastSpellCommand(player, spellId, 0, 0, UseSpellBank: false));
            if (LastRefusal is not null) Requests.Add(new UiTell(LastRefusal));
        }));
    }

    /// <summary>Сообщение отказа цели при щелчке (<c>0x5dbd75</c>, разбор кода клиентом <c>0x4d355f</c>): иммунитет (3) <c>X005TA0670</c>, без
    /// эффекта (5) <c>X005TA0671</c>, союзник (15) <c>X005TA0672</c>, невидимый (16) <c>X005TA0673</c>; прочее — молча (null): нет цели, туман,
    /// вне карты, лидер-иллюзия (2) и уже наложенный модификатор (4) — у них ветки сообщения нет.</summary>
    public static string? TargetMessage(string refusal) => refusal switch
    {
        "все иммунны" => "X005TA0670",
        "лечить некого" or "запас хода полон" or "брони нет" => "X005TA0671",
        "союзник" => "X005TA0672",
        "свой невидимый отряд" => "X005TA0673",
        _ => null,
    };
}

/// <summary>Панель прицела (<c>DLG_STRAT_SPELL</c>, <c>0x490061</c>) на месте панели команд: значок, имя <c>X005TA0617</c>,
/// запас маны; «Закрыть» (<c>BTN_CLOSE</c>, Esc) — отмена прицела (<c>0x48f87f</c>).</summary>
public sealed class StratSpellLogic(WindowContext context, string spellId, IReadOnlyDictionary<string, SpellText> texts) : IScreenLogic
{
    public const string Screen = "DLG_STRAT_SPELL";

    public string SpellId => spellId;

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetImage("IMG_SPELL_ICON", $"IconSpel:{ArtNames.Of(spellId)}");
        string name = texts.TryGetValue(spellId, out var t) ? context.Text(t.NameTxt) : spellId;
        ui.SetText("TXT_SPELL_INFO", UiTemplate.Fill(context.Text("X005TA0617"), ("NAME", name)));
        var bank = context.Player.Bank;
        ui.SetText("TXT_ORANGE", $"{bank.Death}");
        ui.SetText("TXT_BLANC", $"{bank.Runic}");
        ui.SetText("TXT_JAUNE", $"{bank.Life}");
        ui.SetText("TXT_ROUGE", $"{bank.Infernal}");
        ui.SetText("TXT_BLUE", $"{bank.Grove}");
    }
}
