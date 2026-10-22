using System.Globalization;
using D2.Data.Play;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Посох или свиток (<c>BTN_WAND_SCROLL</c> панели команд, клавиша U → <c>DLG_WAND_SCROLL</c>, <c>CWandScrollInterf</c>; проверка до
/// открытия <c>0x48de96</c> — <see cref="CanOpen"/>, иначе <c>X005TA0027</c>): <c>LBOX_SPELL_LIST</c> — предметы отряда с заклинанием
/// (<c>0x4a911e</c>); справа — значок, имя, уровень, тип и эффект заклинания, цена применения посоха; внизу — мана игрока
/// (<c>X005TA0206</c>). «Применить» (<c>BTN_CAST_SPELL</c>, доступна при непустом списке; двойной щелчок): отказы <c>0x5dbbe8</c> —
/// мана <c>X005TA0214</c>, посох сегодня уже <c>X005TA0579</c>/<c>0580</c>; заклинание на всю карту — сразу
/// (<c>CTaskCastWorldWandScroll</c>), иначе окно закрывается и карта ждёт цель, как у заклинания (<see cref="UiCast"/> с предметом).
/// </summary>
public sealed class WandScrollLogic(WindowContext context, string stackId, IReadOnlyDictionary<string, SpellText> spellTexts, StackTexts itemTexts)
    : IWindowLogic
{
    public const string Screen = "DLG_WAND_SCROLL";
    private const string List = "LBOX_SPELL_LIST";
    private const int IconW = 70, RowW = 235, RowH = 85, WholeMap = 999;
    private readonly UiRequests _requests = new();
    private int _selected;

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Можно ли открыть (<c>0x48de96</c>): отряд игрока, лидер жив и умеет применять посохи и свитки.</summary>
    public static bool CanOpen(WindowContext c, string? stackId) =>
        c is not null && stackId is not null && c.Map.Stacks.TryGetValue(stackId, out var stack)
        && stack.Owner.Equals(c.Viewer, StringComparison.OrdinalIgnoreCase)
        && c.Map.Units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0
        && c.Session.LeaderAbilitiesOf(stack).Contains(GameSession.WandScrollAbility);

    /// <summary>Предметы отряда с заклинанием — по уровню заклинания, затем по имени предмета.</summary>
    public IReadOnlyList<(string Item, Item Def, Spell Spell)> Entries
    {
        get
        {
            var book = context.Session.RequireBook();
            if (!context.Map.Stacks.TryGetValue(stackId, out var stack)) return [];
            return [.. stack.Items.Select(i => (Item: i, Def: context.Session.ItemOf(i)))
                                  .Where(p => p.Def is { SpellId.Length: > 0 } && book.Spells.ContainsKey(p.Def.SpellId))
                                  .Select(p => (p.Item, p.Def!, book.Spells[p.Def!.SpellId]))
                                  .OrderBy(e => e.Item3.Level).ThenBy(e => ItemName(e.Item2.Id), StringComparer.CurrentCulture)];
        }
    }

    public string? LastRefusal { get; private set; }

    public string Summary => $"посох предметов={Entries.Count} выбран={_selected} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_CAST_SPELL" or "BTN_CLOSE");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetHidden("BTN_TRADE_SPELL", true);
        ui.SetText("TXT_AVAILABLE", context.Text("X005TA0206"));
        var bank = context.Player.Bank;
        foreach (var (id, value) in (ReadOnlySpan<(string, long)>)[("TXT_ORANGE", bank.Death), ("TXT_BLANC", bank.Runic), ("TXT_JAUNE", bank.Life),
                                                                     ("TXT_ROUGE", bank.Infernal), ("TXT_BLUE", bank.Grove)])
            ui.SetText(id, value.ToString(CultureInfo.InvariantCulture));
        var entries = Entries;
        ui.SetCards(List, [.. entries.Select(e => new UiCard(ItemCells.IconFrame(e.Def.Id, context), new UiPoint(0, 0),
            @"\vC;\fMedBold;" + ItemName(e.Def.Id) + @"\n\fNormal;" + SpellName(e.Spell.Id), new UiRect(IconW + 10, 0, RowW - IconW - 10, RowH)))]);
        ui.SetEnabled("BTN_CAST_SPELL", entries.Count > 0);
        if (entries.Count > 0) ui.Select(List, 0);
        Details(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiListSelected { Element: List } s:
                _selected = s.Index;
                Details(ui);
                break;
            case UiListConfirmed { Element: List } c:
                _selected = c.Index;
                Cast();
                break;
            case UiClicked { Element: "BTN_CAST_SPELL" }:
                Cast();
                break;
            case UiClicked { Element: "BTN_CLOSE" }:
                _requests.Add(new UiClose());
                break;
        }
    }

    private void Details(UiScreenState ui)
    {
        var entries = Entries;
        if (_selected >= entries.Count)
        {
            ui.SetImage("IMG_SPELL", null);
            foreach (string id in (string[])["TXT_SPELL_INFO_2", "TXT_SPELL_STATS", "TXT_COST", "TXT_LABEL1"]) ui.SetText(id, "");
            return;
        }
        var (_, def, spell) = entries[_selected];
        ui.SetImage("IMG_SPELL", $"IconSpel:{spell.Id.ToUpperInvariant()}");
        ui.SetText("TXT_LABEL1", ItemName(def.Id));
        ui.SetText("TXT_SPELL_INFO_2", SpellName(spell.Id));
        string stats = UiTemplate.Fill(context.Text("X005TA0438"), ("LEVEL", spell.Level));
        if (spell.DamageQty > 0 && spell.DamageSource is >= 0 and <= 7)
            stats += UiTemplate.Fill(context.Text("X005TA0136"), ("SOURCE", context.Text($"X005TA{145 + spell.DamageSource:0000}")));
        if (spellTexts.TryGetValue(spell.Id, out var t) && t.ModifTxt.Length > 0)
            stats += UiTemplate.Fill(context.Text("X005TA0472"), ("DESC", context.Text(t.ModifTxt)));
        ui.SetText("TXT_SPELL_STATS", stats);
        ui.SetText("TXT_COST", def.Category == ItemCategory.Wand
            ? UiTemplate.Fill(context.Text("X005TA0661"), ("CASTCOST", PriceText.Words(context.Text, def.CastingCost))) : "");
    }

    private void Cast()
    {
        var entries = Entries;
        if (_selected >= entries.Count) return;
        LastRefusal = Begin(context, _requests, stackId, entries[_selected].Item);
    }

    /// <summary>
    /// Литьё предметом отряда до цели — общее у кнопки «Применить» и броска свитка или посоха на лидера (<c>CDDStackGroup</c> <c>0x4ff96b</c>,
    /// <see cref="WandScrollDrops"/>): отказы <c>0x5dbbe8</c> (мана, дневной счёт посоха) — сообщением, окно остаётся; иначе окно закрывается и
    /// заклинание на всю карту льётся сразу (<c>CTaskCastWorldWandScroll</c>), прочее ждёт цель на карте (<see cref="UiCast"/> с предметом,
    /// <c>CTaskCastWandScroll</c>; оба создаёт <c>0x40710a</c>). Возвращает отказ (null — литьё началось).
    /// </summary>
    public static string? Begin(WindowContext context, UiRequests requests, string stackId, string item)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requests);
        if (!context.Map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        // 0x5dbbe8 код 1 (предмета нет в отряде — свиток со склада укрепления на лидера гостя): у движка «[Error 2 in MidStackGroup]», литья нет.
        if (!stack.Items.Contains(item, StringComparer.OrdinalIgnoreCase)) return "предмета нет в отряде";
        if (context.Session.ItemOf(item) is not { } def || ItemUse.SpellOf(def, context.Session.RequireBook().Spells) is not { } spell)
            return "у предмета нет заклинания";
        // Отказы до цели (0x5dbbe8): мана и дневной счёт посоха — сообщением; клетку проверит прицел.
        string? refusal = context.Session.ItemCastRefusal(stackId, item, stack.X, stack.Y);
        string? message = refusal switch
        {
            GameSession.ItemCastNoMana => "X005TA0214",
            GameSession.ItemCastWandUsed => "X005TA0579",
            GameSession.ItemCastWandUsedTwice => "X005TA0580",
            _ => null,
        };
        if (message is not null)
        {
            requests.Add(new UiTell(context.Text(message)));
            return refusal;
        }
        requests.Add(new UiClose());
        if (spell.Area == WholeMap)
        {
            refusal = context.Session.Run<string?>(new CastItemCommand(stackId, item, 0, 0));
            if (refusal is not null) requests.Add(new UiTell(refusal));
            return refusal;
        }
        requests.Add(new UiCast(spell.Id, stackId, item));
        return null;
    }

    private string ItemName(string type) => itemTexts.Items.TryGetValue(type, out var n) ? context.Text(n) : type;

    private string SpellName(string spell) => spellTexts.TryGetValue(spell, out var t) ? context.Text(t.NameTxt) : spell;
}
