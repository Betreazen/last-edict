using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Тексты действий вора (имя и описание по виду действия).</summary>
public sealed record ThiefTexts(IReadOnlyDictionary<int, (string Name, string Desc)> Actions)
{
    public static ThiefTexts Load(string rulesDir) => Load(ContentSource.Open(rulesDir));

    public static ThiefTexts Load(ContentSet content) =>
        new(content.ThiefActions.GroupBy(a => (int)a.Kind)
                      .ToDictionary(g => g.Key, g => (g.First().NameText ?? "", g.First().DescriptionText ?? "")));

    /// <summary>Картинка действия в <c>Thief.ff</c>: <c>G000NA00NN</c> (190×190) или <c>_BIG</c> (250×250).</summary>
    public static string Picture(ThiefAction action, bool big) => $"gfx:Thief/G000NA{(int)action:0000}{(big ? "_BIG" : "")}";

    /// <summary>Картинка провала в окне итога — одна на все действия: <c>G000NA9999</c> с <c>_BIG</c> (<c>0x49c634</c> → <c>0x5b647e</c> →
    /// <c>0x5c5cba</c>, строка <c>0x6ed3f0</c>; успех — картинка действия, <c>0x5b63a0</c>).</summary>
    public const string FailurePicture = "gfx:Thief/G000NA9999_BIG";
}

/// <summary>
/// Открыть окно вора у цели (<c>CCmdNobleActionMsg</c> → <c>0x48bd99</c>): неподкупный лидер цели казнит вора — «нечего делать» с
/// <c>X005TA0525</c>; доступных действий нет — «нечего делать»; иначе — действия. Цель не для вора — null. Цель — невидимый отряд — тоже
/// null: нападение <c>0x440667</c> с флагом невидимости цели ветку вора пропускает (<c>0x4407c2</c>) — бой, и нападает невидимый
/// (<see cref="GameSession.StartBattle"/>). Прежде порт открывал «нечего делать» (партия 17.10, карта 8, R11).
/// </summary>
public static class ThiefWindows
{
    public static UiOpen? For(WindowContext c, string stackId, string targetId, ThiefTexts texts)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.Session.ThiefKindOf(targetId) is null || c.Session.IsInvisible(targetId)) return null;
        if (c.Session.ThiefIncorruptible(stackId, targetId))
            return new UiOpen(ThiefNoActionsLogic.Screen, new ThiefNoActionsLogic(c, stackId, targetId, hanged: true));
        return c.Session.ThiefActions(stackId, targetId).Count == 0
            ? new UiOpen(ThiefNoActionsLogic.Screen, new ThiefNoActionsLogic(c, stackId, targetId, hanged: false))
            : new UiOpen(ThiefActionsLogic.Screen, new ThiefActionsLogic(c, stackId, targetId, texts));
    }

    /// <summary>Лидер отряда — вор (<c>UNIT_CAT</c> знатный).</summary>
    public static bool IsThief(WindowContext c, string stackId) =>
        c is not null && c.Map.Stacks.TryGetValue(stackId, out var s) && c.Map.Units.TryGetValue(s.LeaderId, out var leader) && leader.Hp > 0
        && c.Session.RequireBook().CategoryOf(leader.TypeId) == LastEdict.Rules.Battle.UnitCategory.Noble;
}

/// <summary>
/// Действия вора (<c>DLG_THIEF_ACTIONS</c>, <c>CNobleActionInterf</c> <c>0x49a7ef</c>): отряд с лидером-вором дошёл до цели. В списке
/// <c>TLBOX_ACTIONS</c> — все действия окна (<see cref="Thieves.Actions"/>, <c>0x5d3a51</c>): доступные — <c>X005TA0819</c> чёрным,
/// прочие — <c>X005TA0820</c> тёмно-красным (<see cref="GameSession.ThiefActions"/>, <c>0x5d32b8</c>). Выбор (<c>0x49b39b</c>):
/// <c>TXT_DESC</c> — <c>X005TA0531</c> с вероятностью или <c>X005TA0545</c> «не доступно», картинка действия; «ОК» (и двойной щелчок) —
/// только у доступного. Не глава гильдии — <c>X005TA0538</c>. «ОК» (<c>0x49ae42</c>): кража предмета или у торговца —
/// <see cref="StealLogic"/> с предметами, заклинания или у мага — с заклинаниями; подкуп — вопрос <c>X005TA0537</c> с ценой; прочее —
/// сразу. Итог — <see cref="ThiefResultLogic"/>. «Отмена» — вопрос <c>X005TA0766</c>, очки хода всё равно тратятся.
/// </summary>
public sealed class ThiefActionsLogic(WindowContext context, string stackId, string targetId, ThiefTexts texts) : IWindowLogic
{
    public const string Screen = "DLG_THIEF_ACTIONS";
    private const string List = "TLBOX_ACTIONS";
    private readonly UiRequests _requests = new();
    private int _selected;

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Действие стирает путь вора (реестр Ин82): интерфейс движка на итог своего действия (<c>CCmdNobleResultMsg</c>, <c>CTaskSelectUnit</c>
    /// <c>0x4d23a2</c>) при категории «Разведать руины» (<c>[0x837e38]</c> — id категории <c>0x837E30</c>, <c>0x4d241e</c>; та же ветка окна
    /// итога <c>0x49ca85</c>) зовёт <c>0x4cc63b(1)</c> → <c>0x41a808</c> — успех или провал. Прочие действия (шпионаж отряда и крепости тоже)
    /// путь не трогают.</summary>
    public bool DropsRoute => Drops(Report);

    /// <inheritdoc cref="DropsRoute"/>
    public static bool Drops(ThiefReport? report) => report is { Refused: null, Action: ThiefAction.SpyRuin };

    /// <summary>Хозяин вора; вора уже нет (повешен действием, а окно ещё спрашивают о сводке) — null.</summary>
    private string? ThiefOwner => context.Map.Stacks.TryGetValue(stackId, out var thief) ? thief.Owner : null;

    private LordCategory Lord => ThiefOwner is { } owner && context.Map.Players.TryGetValue(owner, out var player)
        ? (LordCategory)Math.Max(context.Session.RequireBook().LordCategory(player.LordId), 0) : 0;

    /// <summary>Все действия окна по виду цели.</summary>
    public IReadOnlyList<ThiefAction> Shown
    {
        get
        {
            var session = context.Session;
            if (session.ThiefKindOf(targetId) is not { } kind || ThiefOwner is not { } owner) return [];
            bool playable = session.ThiefTarget(owner, targetId)?.OwnerPlayable ?? false;
            return Thieves.Actions(Lord, kind, playable);
        }
    }

    /// <summary>Доступные вору сейчас.</summary>
    public IReadOnlyList<ThiefAction> Available => context.Session.ThiefActions(stackId, targetId);

    public ThiefReport? Report { get; private set; }

    public string Summary => $"вор цель={targetId} действий={Shown.Count} доступно={Available.Count} выбрано={(_selected < Shown.Count ? Shown[_selected].ToString() : "-")}";

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or "BTN_CANCEL");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_THIEF_ACTIONS", context.Text("X005TA0241"));
        ui.SetText("TXT_WARNING", Lord == LordCategory.Diplomat ? "" : context.Text("X005TA0538"));
        var shown = Shown;
        var available = Available;
        ui.SetItems(List, [.. shown.Select(a => UiTemplate.Fill(context.Text(available.Contains(a) ? "X005TA0819" : "X005TA0820"), ("NAME", Name(a))))]);
        _selected = Math.Max(0, shown.ToList().FindIndex(available.Contains));
        if (shown.Count > 0) ui.Select(List, _selected);
        Describe(ui);
    }

    private string Name(ThiefAction a) => texts.Actions.TryGetValue((int)a, out var t) ? context.Text(t.Name) : a.ToString();

    private void Describe(UiScreenState ui)
    {
        var shown = Shown;
        if (_selected >= shown.Count)
        {
            ui.SetEnabled("BTN_OK", false);
            return;
        }
        var action = shown[_selected];
        bool can = Available.Contains(action);
        string desc = texts.Actions.TryGetValue((int)action, out var t) ? context.Text(t.Desc) : "";
        ui.SetText("TXT_DESC", can
            ? UiTemplate.Fill(context.Text("X005TA0531"), ("NAME", Name(action)), ("DESCRIPTION", desc), ("CHANCE", context.Session.ThiefChance(targetId, action)))
            : UiTemplate.Fill(context.Text("X005TA0545"), ("NAME", Name(action)), ("DESCRIPTION", desc)));
        ui.SetImage("IMG_ACTION", ThiefTexts.Picture(action, big: false));
        ui.SetEnabled("BTN_OK", can);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiListSelected { Element: List } s:
                _selected = s.Index;
                Describe(ui);
                break;
            case UiListConfirmed { Element: List } c:
                _selected = c.Index;
                Choose();
                break;
            case UiClicked { Element: "BTN_OK" }:
                Choose();
                break;
            case UiClicked { Element: "BTN_CANCEL" }:
                _requests.Add(new UiAsk(context.Text("X005TA0766"), () =>
                {
                    context.Session.Run(new ThiefCancelCommand(stackId, Empty: false));
                    _requests.Add(new UiClose());
                }));
                break;
        }
    }

    private void Choose()
    {
        var shown = Shown;
        if (_selected >= shown.Count || !Available.Contains(shown[_selected])) return;
        var action = shown[_selected];
        switch (action)
        {
            case ThiefAction.StealItem or ThiefAction.StealMerchant or ThiefAction.StealSpell or ThiefAction.StealMage:
                _requests.Add(new UiOpen(StealLogic.Screen(action), new StealLogic(context, stackId, targetId, action, pick => Act(action, pick))));
                break;
            case ThiefAction.Bribe:
                int price = ThiefOwner is { } payer ? context.Session.ThiefTarget(payer, targetId)?.BribeCost ?? 0 : 0;
                _requests.Add(new UiAsk(UiTemplate.Fill(context.Text("X005TA0537"), ("PRICE", price)), () => Act(action, null)));
                break;
            default:
                Act(action, null);
                break;
        }
    }

    /// <summary>Действие (<see cref="GameSession.Thief"/>): окно закрывается, итог — окном результата.</summary>
    private void Act(ThiefAction action, string? pick)
    {
        // Одно действие на окно. Кнопка «Да» окна кражи — она же подтверждение списка (MLBOX_LIST → BTN_YES), щелчок даёт два события
        // (UiClicked и UiListConfirmed), и до 16.10 действие шло дважды: успех, затем второй бросок — провал, вор повешен с украденным, мешок
        // на клетке гибели и два окна итога (партия 16.10, пометка 5). У движка ход вора — одно действие (CNobleActionInterf 0x49ae42).
        if (Report is not null) return;
        string name = context.Map.Stacks.TryGetValue(targetId, out var s) ? StackName(s)
            : context.Map.Sites.FirstOrDefault(f => f.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        Report = context.Session.Run<ThiefReport>(new ThiefCommand(stackId, targetId, action, pick));
        _requests.Add(new UiClose());
        if (Report.Refused is not null)
        {
            _requests.Add(new UiTell(Report.Refused));
            return;
        }
        // Итог — показу и зонду (sight ThiefResult, звук BKPOPUP) сразу по действию, а не из окна итога: окно может не открыться (мод
        // заменил разметку), а действие уже случилось (журнал 15.10, пометка 22: окно итога упало на открытии, итога в журнале нет).
        context.Announce(TurnSightKind.ThiefResult, action.ToString());
        _requests.Add(new UiOpen(ThiefResultLogic.Screen, new ThiefResultLogic(context, Report, name)));
    }

    private string StackName(MapStack stack)
    {
        var look = context.Session.Looks.Stacks.FirstOrDefault(l => l.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase));
        return look is { LeaderName.Length: > 0 } ? look.LeaderName
            : context.Map.Units.TryGetValue(stack.LeaderId, out var u) ? context.UnitName(u.TypeId) : stack.Id;
    }
}

/// <summary>
/// Что украсть (<c>DLG_STEAL_ITEM</c> <c>0x4a3f22</c>/<c>0x4a4a1f</c>, <c>DLG_STEAL_SPELL</c> <c>0x4a577f</c>): <c>TXT_CHOOSE_ITEM_STR</c> —
/// <c>X005TA0539</c>/<c>X005TA0540</c>; <c>MLBOX_LIST</c> — предметы цели одной ячейкой на тип со счётчиком (отряд — сумка, торговец — товар),
/// заклинания владельца города, неизвестные вору, кроме призывов, или товар башни мага. «Да» — выбранное вызвавшему окну, «Отмена» — назад.
/// ponytail: рамка выбора у движка жёлтая (<c>0x5c9dd9</c>) — здесь общий контур списка.
/// </summary>
public sealed class StealLogic(WindowContext context, string stackId, string targetId, ThiefAction action, Action<string> chosen) : IWindowLogic
{
    private const string List = "MLBOX_LIST";

    /// <summary>Рамка выбранной ячейки окон кражи — жёлтая <c>FFFF00</c> (<c>CImage2Outline</c>; у прочих списков — чёрная, разбор re16).</summary>
    public static readonly UiColor Mark = new(255, 255, 0);

    private readonly UiRequests _requests = new();
    private int _selected;
    private bool _picked;

    public static string Screen(ThiefAction action) =>
        action is ThiefAction.StealSpell or ThiefAction.StealMage ? "DLG_STEAL_SPELL" : "DLG_STEAL_ITEM";

    private bool Spells => action is ThiefAction.StealSpell or ThiefAction.StealMage;

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Что можно выбрать: для предметов — (тип, что передать в действие), для заклинаний — (id, id).</summary>
    public IReadOnlyList<(string Icon, string Pick, int Count)> Choices
    {
        get
        {
            var map = context.Map;
            var site = map.Sites.FirstOrDefault(s => s.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            // Вора уже нет: «Да» сделало действие, и вор повешен (партия 15.10: KeyNotFoundException в DLG_STEAL_ITEM и DLG_STEAL_SPELL —
            // сводка окна спрашивала выбор после действия).
            if (!map.Stacks.TryGetValue(stackId, out var thief) || !map.Players.TryGetValue(thief.Owner, out var me)) return [];
            switch (action)
            {
                case ThiefAction.StealItem when map.Stacks.TryGetValue(targetId, out var target):
                    return [.. ItemCells.Fold(target.Items, map).Select(c => (c.Type, c.Top, c.Ids.Count))];
                case ThiefAction.StealMerchant when site is not null:
                    return [.. site.Stock.Where(r => r.Count > 0).Select(r => (r.Item, r.Item, r.Count))];
                case ThiefAction.StealMage when site is not null:
                    return [.. site.Spells.Where(s => !me.KnownSpells.Contains(s, StringComparer.OrdinalIgnoreCase)).Select(s => (s, s, 1))];
                case ThiefAction.StealSpell when site is not null:
                    return [.. context.Session.StealableSpells(me.Id, site.Owner).Select(s => (s, s, 1))];   // 0x4b935b → 0x5cdde7: без призывов
                default:
                    return [];
            }
        }
    }

    public string Summary => $"кража выбор={Choices.Count}";

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_YES" or "BTN_CANCEL");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_CHOOSE_ITEM_STR", context.Text(Spells ? "X005TA0540" : "X005TA0539"));
        var spec = ItemCells.Spec(ui, List);
        ui.SetCards(List, [.. Choices.Select(c => new UiCard(Spells ? $"IconSpel:{c.Icon.ToUpperInvariant()}" : ItemCells.IconFrame(c.Icon, context), new UiPoint(0, 0),
            Spells ? null : UiTemplate.Fill(context.Text("X005TA0323"), ("NUM", c.Count)) + "  ",
            new UiRect(1, 1, Math.Max(0, spec.CellWidth - 2), Math.Max(0, spec.CellHeight - 2))))]);
        ui.SetMarkColor(List, Mark);
        if (Choices.Count > 0) ui.Select(List, 0);
        ui.SetEnabled("BTN_YES", Choices.Count > 0);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected { Element: List } s:
                _selected = s.Index;
                break;
            case UiListConfirmed { Element: List } c:
                _selected = c.Index;
                Pick();
                break;
            case UiClicked { Element: "BTN_YES" }:
                Pick();
                break;
            case UiClicked { Element: "BTN_CANCEL" }:
                _requests.Add(new UiClose());
                break;
        }
    }

    private void Pick()
    {
        var choices = Choices;
        if (_picked || _selected >= choices.Count) return;
        _picked = true;   // «Да» — ещё и подтверждение списка: одно нажатие приходит двумя событиями
        _requests.Add(new UiClose());
        chosen(choices[_selected].Pick);
    }
}

/// <summary>
/// Итог действия вора (<c>DLG_ACTION_RESULT</c>, <c>CNobleResultInterf</c> <c>0x49c43d</c>): только <c>BTN_YES</c>, <c>IMG_ACTION</c>,
/// <c>TXT_ACTION_RESULT</c> и <c>TXT_ACTION_RESULT_2</c> (строки элементов <c>0x79b334</c>…<c>0x79b340</c>). Заголовок — <c>X005TA0032</c>
/// «Успех!» / <c>X005TA0031</c> «Неудача!»; провал — <c>X005TA0565</c> «Ваш вор был схвачен и повешен!» (<c>0x49c80a</c>) и общая картинка
/// провала (<see cref="ThiefTexts.FailurePicture"/>); успех — строка действия (<c>0x49c8dc</c>: <c>X005TA0548</c>…<c>0566</c>) и картинка
/// действия. Других окон нет и у шпионажа: сведения о цели — правой кнопкой по ней (<c>X005TA0554</c>/<c>0548</c> так и говорят).
/// </summary>
public sealed class ThiefResultLogic(WindowContext context, ThiefReport report, string targetName) : IWindowLogic
{
    public const string Screen = "DLG_ACTION_RESULT";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"вор итог={(report.Success ? "успех" : "неудача")} действие={report.Action}";

    public bool Allows(string element, UiScreenState ui) => element != "BTN_YES";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_ACTION_RESULT", context.Text(report.Success ? "X005TA0032" : "X005TA0031"));
        ui.SetImage("IMG_ACTION", report.Success ? ThiefTexts.Picture(report.Action, big: true) : ThiefTexts.FailurePicture);
        ui.SetText("TXT_ACTION_RESULT_2", report.Success ? Success() : report.ThiefDied ? context.Text("X005TA0565") : "");
    }

    private string Success()
    {
        var kind = context.Session.ThiefKindOf(report.Target);
        string unit = context.Map.Units.TryGetValue(report.Taken, out var u) ? context.UnitName(u.TypeId) : report.Taken;
        return report.Action switch
        {
            ThiefAction.Spy => context.Text(kind == ThiefTargetKind.Stack ? "X005TA0554" : kind == ThiefTargetKind.Ruin ? "X005TA0562" : "X005TA0548"),
            ThiefAction.SpyRuin => context.Text("X005TA0562"),
            ThiefAction.StealItem => context.Text("X005TA0549"),
            ThiefAction.StealSpell => context.Text("X005TA0555"),
            ThiefAction.Misfit => context.Text("X005TA0558"),
            ThiefAction.Duel => context.Text("X005TA0552"),
            ThiefAction.PoisonStack => UiTemplate.Fill(context.Text("X005TA0563"), ("PARTYNAME", targetName)),
            ThiefAction.PoisonCity => UiTemplate.Fill(context.Text("X005TA0546"), ("CITYNAME", targetName)),
            ThiefAction.Assassinate => UiTemplate.Fill(context.Text("X005TA0557"), ("UNITNAME", unit)),
            ThiefAction.Bribe => context.Text("X005TA0556"),
            ThiefAction.StealGold => UiTemplate.Fill(context.Text("X005TA0559"), ("AMOUNT", report.Value)),
            ThiefAction.StealMerchant => context.Text("X005TA0564"),
            ThiefAction.StealMage => context.Text("X005TA0561"),
            ThiefAction.RiotCity => context.Text("X005TA0566"),
            _ => "",
        };
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_YES" }) _requests.Add(new UiClose());
    }
}

/// <summary>
/// Вору нечего делать (<c>DLG_THIEF_NOACTIONS</c>, <c>CNobleNoActionInterf</c> <c>0x49c1be</c>): неподкупный лидер цели — <c>X005TA0525</c> (вор
/// казнён), отряд — <c>X005TA0655</c>, город — <c>X005TA0526</c>, город в бунте — <c>X005TA0689</c> (снимок прогона 13.10), руина —
/// <c>X005TA0527</c>, лавки — <c>X005TA0528</c>. «ОК» — окно закрывается,
/// очки хода вора тратятся (<see cref="GameSession.ThiefCancel"/>).
/// </summary>
public sealed class ThiefNoActionsLogic(WindowContext context, string stackId, string targetId, bool hanged) : IWindowLogic
{
    public const string Screen = "DLG_THIEF_NOACTIONS";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"вор нечего цель={targetId} казнён={(hanged ? 1 : 0)}";

    public bool Allows(string element, UiScreenState ui) => element != "BTN_OK";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        string id = hanged ? "X005TA0525" : context.Session.ThiefKindOf(targetId) switch
        {
            ThiefTargetKind.Stack => "X005TA0655",
            ThiefTargetKind.Fortification when context.Map.Sites.FirstOrDefault(s => s.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                                                  is { Kind: SiteKind.Village } v && Fortifications.Rioting(v.RiotTurn) => "X005TA0689",
            ThiefTargetKind.Fortification => "X005TA0526",
            ThiefTargetKind.Ruin => "X005TA0527",
            _ => "X005TA0528",
        };
        ui.SetText("TXT_MESSAGE", context.Text(id));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: "BTN_OK" }) return;
        if (!hanged) context.Session.Run(new ThiefCancelCommand(stackId, Empty: true));
        _requests.Add(new UiClose());
    }
}
