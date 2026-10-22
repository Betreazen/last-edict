using System.Globalization;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>Что панели экрана карты показывают сейчас: выбранный отряд (имя лидера, его портрет и очки хода),
/// номер дня и казна игрока.</summary>
/// <param name="CanExchange">Рядом с выбранным отрядом свой отряд — кнопка обмена <c>BTN_EXCHANGE</c> доступна (<c>0x48df76</c>).</param>
/// <param name="CanNextLeader">Есть отряд для «Следующего лидера» (<c>BTN_NEXT_LEADER</c>, Tab): кнопка доступна, когда список отрядов
/// игрока не пуст (<c>0x48eb51</c> → <c>0x41a44e</c>), выбран отряд или нет.</param>
/// <param name="CanContinueMove">«Продолжить движение» (<c>BTN_CONT_MOVE</c>, клавиша M) доступна (<c>0x4cfdff</c> → <c>0x4d2346</c>): выбранный
/// отряд помнит путь, стоит не на его цели и у него есть очки хода. В конструкторе панели кнопка выключена (<c>0x48fdd3</c>).</param>
/// <param name="CanDropRod">«Поставить жезл» (<c>BTN_DROP_ROD</c>) доступна: выбран отряд (<c>0x48df25</c>: id отряда и ход игрока идёт —
/// флаг интерфейса <c>+0x28</c>, <c>CCmdTurnInfoMsg</c>/<c>CCmdEndTurnMsg</c> <c>0x4051f3</c>). Жезлоносца кнопка не смотрит — отказ даёт её
/// обработчик (<c>DropRodLogic.StartRefusal</c>). В конструкторе панели выключена (<c>0x48fd0f</c>).</param>
/// <param name="CanWandScroll">«Посох или свиток» (<c>BTN_WAND_SCROLL</c>) доступна (<c>0x48de33</c>): выбран отряд, лидер жив и умеет
/// посохи и свитки (<c>0x48de96</c>: <c>0x5e98ad</c>, <c>L_WAND_SCROLL_USE</c>), ход игрока идёт. Предметов в отряде кнопка не смотрит. В
/// конструкторе панели выключена (<c>0x48fe95</c>).</param>
/// <param name="CanGuard">«Охранять» (<c>BTN_GUARD</c>) доступна: выбран отряд и он не охраняет (<c>0x48d9bc</c> гасит при каждой сборке
/// сведений о лидере, <c>0x48dd7f</c> зажигает по флагу отряда <c>0x41a4e1</c>; у движка — в одном такте, итог — по отряду). Флага охраны
/// у порта нет (<see cref="NextLeaderRule"/>) — по одному выбору.</param>
/// <param name="Face">Портрет панели целиком — отражение, павший, доля здоровья, значки (<see cref="PanelFace"/>); null — по одному имени
/// <paramref name="LeaderFace"/>.</param>
public sealed record MapPanelState(string LeaderName, string? LeaderFace, int MoveLeft, int MoveMax, int Turn, Bank Bank, bool CanExchange = false,
                                   bool CanNextLeader = false, PanelFace? Face = null, bool CanContinueMove = false, bool CanDropRod = false,
                                   bool CanWandScroll = false, bool CanGuard = false)
{
    public static readonly MapPanelState Empty = new("", null, 0, 0, 1, Bank.Zero);
}

/// <summary>Панель команд <c>DLG_STRATEGIC</c>: портрет и имя лидера выбранного отряда, очки хода
/// («<c>X005TA0396</c>»: <c>%MOVE1% / %MOVE2%</c>, цвет <c>X005TA0397</c> чёрный или <c>X005TA0398</c> красный при
/// нуле) и номер дня голым числом (<c>docs/map-screen.md</c> §3).
///
/// <para>Портрет — <c>{база типа лидера}FACES</c>, нет её — <c>FACE</c> из <c>Faces.ff</c> (<see cref="Ui.Windows.UnitFaces.PanelName"/>,
/// <c>0x5b1e6a</c>): у лидера с <c>BASE_UNIT</c> — картинка базового типа, у большого — его малый портрет.</para></summary>
public sealed class StrategicPanelLogic(Func<MapPanelState> state, Func<string, string> text) : IScreenLogic
{
    private const string Stats = "X005TA0396", Black = "X005TA0397", Red = "X005TA0398", NameLine = "X005TA0399";

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    /// <summary>Отряд не выбран — кнопки отряда недоступны (в движке они выключены в конструкторе панели).</summary>
    public bool Allows(string element, UiScreenState ui) =>
        state().LeaderName.Length > 0 || element is "BTN_END_TURN" or "BTN_OPTIONS" or "BTN_CAPITAL" or "BTN_DIPLOMACY";

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var s = state();
        ui.SetImage("IMG_LEADER", s.LeaderFace);
        ui.SetText("TXT_LEADER_NAME", s.LeaderName.Length == 0 ? "" : text(NameLine).Replace("%NAME%", s.LeaderName));
        ui.SetText("TXT_LEADER_STATS", s.LeaderName.Length == 0 ? "" : text(Stats)
            .Replace("%COLOR%", text(s.MoveLeft == 0 ? Red : Black))
            .Replace("%MOVE1%", s.MoveLeft.ToString(CultureInfo.InvariantCulture))
            .Replace("%MOVE2%", s.MoveMax.ToString(CultureInfo.InvariantCulture)));
        ui.SetText("TXT_TURNNB", s.Turn.ToString(CultureInfo.InvariantCulture));
        ui.SetEnabled("BTN_EXCHANGE", s.CanExchange);
        ui.SetEnabled("BTN_NEXT_LEADER", s.CanNextLeader);   // 0x48ebd6: доступна, пока есть кого перебирать
        ui.SetEnabled("BTN_CONT_MOVE", s.CanContinueMove);
        ui.SetEnabled("BTN_DROP_ROD", s.CanDropRod);         // 0x48df25 (Зс56)
        ui.SetEnabled("BTN_WAND_SCROLL", s.CanWandScroll);   // 0x48de33 (Зс56)
        ui.SetEnabled("BTN_GUARD", s.CanGuard);              // 0x48d9bc/0x48dd7f (Зс56)
    }
}

/// <summary>Полоса ресурсов <c>DLG_ISO_RESPOPUP</c>: числа казны игрока по местам
/// (<c>docs/map-screen.md</c> §3).</summary>
public sealed class ResourcePanelLogic(Func<MapPanelState> state) : IScreenLogic
{
    public void Open(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var bank = state().Bank;
        ui.SetText("TXT_GOLD", Number(bank.Gold));
        ui.SetText("TXT_ROUGE", Number(bank.Infernal));
        ui.SetText("TXT_JAUNE", Number(bank.Life));
        ui.SetText("TXT_ORANGE", Number(bank.Death));
        ui.SetText("TXT_BLANC", Number(bank.Runic));
        ui.SetText("TXT_BLUE", Number(bank.Grove));
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Отряды для «Следующего лидера» (<c>BTN_NEXT_LEADER</c>, Tab; обработчик <c>0x48ebe8</c> → <c>0x41a5b6</c>, ранг 1 по листингу):
/// свои отряды, кроме стоящих в деревне с бунтом (фильтр <c>0x41a4bd</c> → <c>0x5cdd8c</c>: хозяин отряда — игрок, <c>+0x94</c> не деревня с
/// <c>+0x98</c> &gt; 0). Очков хода фильтр не смотрит — отряды без хода перебираются тоже (прежде порт брал только с очками, и Tab замолкал,
/// когда ход у всех кончался). Флаг «Охранять» (<c>BTN_GUARD</c>, <c>0x48eacc</c> → <c>0x41a58b</c>) из перебора снимает — кнопки в порте
/// нет. Порядок списка движка не разобран — здесь по id.</summary>
public static class NextLeaderRule
{
    public static IReadOnlyList<MapStack> Of(MapState map, Func<MapStack, bool> owned)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(owned);
        return map.Stacks.Values.Where(s => owned(s) && !InRiot(map, s)).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    private static bool InRiot(MapState map, MapStack stack) =>
        stack.Inside.Length > 0 && map.Sites.FirstOrDefault(s => s.Id.Equals(stack.Inside, StringComparison.OrdinalIgnoreCase)) is
            { Kind: SiteKind.Village } village && Fortifications.Rioting(village.RiotTurn);   // 0x5cdd7a: [+0x98] > 0
}
