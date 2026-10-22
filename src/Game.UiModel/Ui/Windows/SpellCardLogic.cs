using System.Globalization;
using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Энциклопедия заклинания (<c>DLG_R_C_SPELL</c>, <c>CEncLayoutSpell</c> <c>0x57cdca</c>, заполнение <c>0x57d066</c>), пока держится правая
/// кнопка (книга, исследование, лавка мага):
/// <list type="bullet">
/// <item><c>IMG_SPELL_ICON</c> — значок; <c>TXT_SPELL_INFO</c> — <c>X005TA0653</c>: имя, описание, область <c>X005TA0710</c> (999 — «Вся карта»
/// <c>0713</c>, 1 — «Одна цель» <c>0711</c>, иначе <c>0712</c> «N x N»), уровень <c>X005TA0438</c>, тип <c>X005TA0136</c> (источник урона,
/// нет — «Нет» <c>0473</c>), эффект <c>X005TA0472</c>; у призыва — длительность <c>X005TA0137</c> (<c>LIFE_TIME</c> призываемого).</item>
/// <item>Стоимость литья: <paramref name="hideCasting"/> (свиток) — <c>TXT_CASTING_COST</c> и мана прячутся, кроме лесной (ошибка движка
/// повторена: <c>TXT_BLUE</c>/<c>IMG_BLUE</c> нет в его таблице); иначе заголовок <c>X005TA0497</c> и числа по видам маны, если лорд игрока
/// знает цены (<c>0x582afa</c>), иначе <c>X005TA0496</c>.</item>
/// <item>Цена покупки (<paramref name="buy"/>, лавка мага) — <c>X005TA0530</c>, каждый ресурс ×(100 − скидка)/100 (<c>0x5868d6</c>).</item>
/// </list>
/// </summary>
public sealed class SpellCardLogic(WindowContext context, string spellId, IReadOnlyDictionary<string, SpellText> texts,
                                   bool hideCasting = false, bool buy = false, int lowerCostPercent = 0) : IWindowLogic
{
    public const string Screen = "DLG_R_C_SPELL";
    private const int WholeMap = 999;
    private static readonly string[] ManaTexts = ["TXT_ORANGE", "TXT_BLANC", "TXT_JAUNE", "TXT_ROUGE"];
    private static readonly string[] ManaImages = ["IMG_ORANGE", "IMG_BLANC", "IMG_JAUNE", "IMG_ROUGE"];
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"заклинание={spellId}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var book = context.Session.RequireBook();
        if (!book.Spells.TryGetValue(spellId, out var spell)) return;
        texts.TryGetValue(spellId, out var t);
        ui.SetImage("IMG_SPELL_ICON", $"IconSpel:{spellId.ToUpperInvariant()}");
        ui.SetText("TXT_SPELL_INFO", UiTemplate.Fill(context.Text("X005TA0653"),
            ("NAME", t is null ? spellId : context.Text(t.NameTxt)), ("DESC", t is null ? "" : context.Text(t.DescTxt)),
            ("AREA", UiTemplate.Fill(context.Text("X005TA0710"), ("AREA", Area(spell.Area)))), ("INFO", Info(spell, t))));
        Casting(ui, spell);
        ui.SetHidden("TXT_BUY_COST", !buy);
        if (buy) ui.SetText("TXT_BUY_COST", UiTemplate.Fill(context.Text("X005TA0530"), ("COST", PriceText.Words(context.Text, Discounted(spell.BuyCost)))));
    }

    private string Area(int area) => area switch
    {
        WholeMap => context.Text("X005TA0713"),
        1 => context.Text("X005TA0711"),
        _ => UiTemplate.Fill(context.Text("X005TA0712"), ("W", area), ("H", area)),
    };

    private string Info(Spell spell, SpellText? t)
    {
        string source = spell.DamageSource is >= 0 and <= 7 && spell.DamageQty > 0
            ? context.Text($"X005TA{145 + spell.DamageSource:0000}") : context.Text("X005TA0473");
        string info = UiTemplate.Fill(context.Text("X005TA0438"), ("LEVEL", spell.Level))
                      + UiTemplate.Fill(context.Text("X005TA0136"), ("SOURCE", source))
                      + UiTemplate.Fill(context.Text("X005TA0472"), ("DESC", t is { ModifTxt.Length: > 0 } ? context.Text(t.ModifTxt) : ""));
        if (spell.Category == SpellCategory.Summon && context.Rules?.UnitLifeTimes.TryGetValue(spell.SummonUnitId, out int life) == true)
            info += UiTemplate.Fill(context.Text("X005TA0137"), ("DURATION", life));
        return info;
    }

    private void Casting(UiScreenState ui, Spell spell)
    {
        foreach (string id in ManaTexts.Concat(ManaImages).Append("TXT_CASTING_COST")) ui.SetHidden(id, hideCasting);
        if (hideCasting) return;
        bool known = context.Session.RequireBook().LordCategory(context.Player.LordId) >= 0;
        ui.SetText("TXT_CASTING_COST", context.Text(known ? "X005TA0497" : "X005TA0496"));
        var c = spell.CastingCost;
        (string Id, long Value)[] mana = [("TXT_ORANGE", c.Death), ("TXT_BLANC", c.Runic), ("TXT_JAUNE", c.Life), ("TXT_ROUGE", c.Infernal),
                                          ("TXT_BLUE", c.Grove)];
        foreach (var (id, value) in mana) ui.SetText(id, known ? value.ToString(CultureInfo.InvariantCulture) : "");
    }

    /// <summary>Цена со скидкой героя: каждый ресурс ×(100 − процент)/100, дробь отбрасывается (<c>0x5868d6</c>).</summary>
    private Bank Discounted(Bank price) => Rules.Map.SiteTrade.Discounted(price, lowerCostPercent);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => Close();

    public void Release(int x, int y, UiScreenState ui) => Close();

    private void Close()
    {
        if (_requests.Count == 0) _requests.Add(new UiClose());
    }
}

/// <summary>Энциклопедия навыка героя (<c>DLG_R_C_LEADERABIL</c>, <c>CEncLayoutLeaderAbil</c> <c>0x57423f</c>): <c>TXT_INFO</c> —
/// <c>X005TA0911</c> с именем и описанием навыка (<see cref="HeroSkills.Describe"/>); значка в разметке нет. По правой кнопке на значке
/// навыка в окне отряда и в окне обмена.</summary>
public sealed class LeaderAbilityCardLogic(WindowContext context, string nameTxt) : IWindowLogic
{
    public const string Screen = "DLG_R_C_LEADERABIL";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"навык={nameTxt}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_INFO", UiTemplate.Fill(context.Text("X005TA0911"), ("NAME", context.Text(nameTxt)),
                                               ("DESC", HeroSkills.Describe(nameTxt) is { } desc ? context.Text(desc) : "")));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => Close();

    public void Release(int x, int y, UiScreenState ui) => Close();

    private void Close()
    {
        if (_requests.Count == 0) _requests.Add(new UiClose());
    }
}
