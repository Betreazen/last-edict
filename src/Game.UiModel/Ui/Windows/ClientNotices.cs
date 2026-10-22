using D2.Data.Game;
using D2.Data.Menu;
using D2.Data.Play;
using D2.Rules.Map;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Команды сервера клиенту, у которых в порте нет своего окна в ядре (<c>docs/audit-20261013.md</c>, «Осталось» п. 4): сборщик подписан на
/// ход партии (<see cref="GameSession.Watch"/>) и копит то, что увидит человек у ПК.
/// <list type="bullet">
/// <item><c>CCmdStackActionMsg</c> (команда 7, клиент <c>0x491722</c>, только в свой ход — флаг клиента <c>+0x28</c>, <c>0x406020</c>):
/// действие 1 — новому хозяину выделить отряд (<c>CTaskSelectUnit</c>, если отряд его и вне укрепления, <c>0x5cdd8c</c>), шлют его
/// <c>CEffectCreateStack</c> (<c>0x436c9a</c>) и <c>CEffectChangeOwner</c> (<c>0x4366e2</c>); действие 2 — прежнему хозяину снять выделение,
/// если выделен этот отряд (<c>0x48d8f3</c>), шлёт <c>CEffectChangeOwner</c>. Действие 0 — окно отряда после «Меню героя»
/// (<see cref="GameSession.RequestPaperdoll"/>).</item>
/// <item><c>CCmdDiscoverSpyMsg</c>, <c>CCmdStackIllusionMsg</c> — окна сообщения (<see cref="Text"/>).</item>
/// <item><c>CCmdCaptureResourcesMsg</c> — окно взятых ресурсов (<see cref="CaptureResourcesLogic"/>).</item>
/// </list>
/// </summary>
public sealed class NoticeWatch(Func<string, bool> human) : ITurnObserver
{
    private readonly List<TurnSight> _pending = [];

    /// <summary>Идёт ход ИИ (от его начала до хода человека): выделения клиент не делает (флаг клиента <c>+0x28</c> снят, <c>0x406020</c>).
    /// Ранг 2: события начала хода человека идут до <see cref="HumanTurn"/> — их выделения тоже не копятся.</summary>
    private volatile bool _aiTurn;

    public void PlayerTurn(string playerId)
    {
        _aiTurn = true;
        lock (_pending) _pending.RemoveAll(s => IsSelection(s.Kind));
    }

    public void HumanTurn(string playerId) => _aiTurn = false;

    public void StackMoving(StackMove move)
    {
    }

    public IReadOnlyList<D2.Rules.Battle.TurnRecord>? Battle(D2.Rules.Battle.BattleState state) => null;

    public void Happened(TurnSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        if (!Kept(sight.Kind) || (_aiTurn && IsSelection(sight.Kind))) return;
        // Смена хозяина — и прежнему хозяину (снять выделение), поэтому без проверки получателя.
        if (sight.Kind != TurnSightKind.OwnerChanged && !human(sight.PlayerId)) return;
        lock (_pending) _pending.Add(sight);
    }

    /// <summary>Есть ли сообщения (окна) для <paramref name="viewer"/>.</summary>
    public bool HasMessages(string viewer)
    {
        lock (_pending) return _pending.Any(s => IsMessage(s.Kind) && Same(s.PlayerId, viewer));
    }

    /// <summary>Забрать по порядку прихода то, что видит <paramref name="viewer"/>: его сообщения и выделения, все смены хозяина;
    /// <paramref name="messagesOnly"/> — только окна (ход ИИ: выделение — дело своего хода).</summary>
    public IReadOnlyList<TurnSight> Take(string viewer, bool messagesOnly = false)
    {
        lock (_pending)
        {
            bool Mine(TurnSight s) => messagesOnly ? IsMessage(s.Kind) && Same(s.PlayerId, viewer)
                : Same(s.PlayerId, viewer) || s.Kind == TurnSightKind.OwnerChanged;
            var mine = _pending.Where(Mine).ToList();
            _pending.RemoveAll(Mine);
            return mine;
        }
    }

    private static bool Kept(TurnSightKind kind) =>
        IsSelection(kind) || kind is TurnSightKind.ResourcesCaptured or TurnSightKind.Looted || IsMessage(kind);

    private static bool IsSelection(TurnSightKind kind) => kind is TurnSightKind.Created or TurnSightKind.OwnerChanged;

    /// <summary>Окно сообщения: раскрытый шпион, гибель иллюзии, несостоявшийся захват деревни, сообщение ИИ (<c>DLG_CHAT_AI</c>,
    /// <see cref="ChatLogic.Of"/>).</summary>
    public static bool IsMessage(TurnSightKind kind) =>
        kind is TurnSightKind.SpyCaught or TurnSightKind.SpyLost or TurnSightKind.IllusionKilled or TurnSightKind.CaptureRefused
            or TurnSightKind.AiChat;

    /// <summary>Шпион раскрыт — разведанному (<c>X005TA0536</c>).</summary>
    public const string SpyCaughtText = "X005TA0536";

    /// <summary>Шпион раскрыт — его хозяину (<c>X005TA0605</c>).</summary>
    public const string SpyLostText = "X005TA0605";

    /// <summary>Гибель иллюзии (<c>X005TA0408</c>, <c>CTaskIllusion</c> <c>0x4ce7bd</c>).</summary>
    public const string IllusionText = "X005TA0408";

    /// <summary>Захват деревни призванным или вором не состоялся (<c>X005TA0458</c>, <c>0x48b415</c> → <c>0x4ced8e</c>).</summary>
    public const string CaptureRefusedText = "X005TA0458";

    /// <summary>Руина разграблена — «Вы победили!» (<c>X005TA0042</c>), с золотом — ещё строка «Вы нашли N» (<c>X005TA0044</c>).</summary>
    public const string RuinWonText = "X005TA0042", RuinGoldText = "X005TA0044";

    /// <summary>Звук окна разграбленной руины (<c>0x4cea43</c> → <c>0x5b03ce(3)</c>; журнал 20261016g seq 564459).</summary>
    public const string RuinSound = "ENTRRUIN";

    /// <summary>
    /// Текст окна разграбленной руины (<c>CCmdLootRuinMsg</c>: клиент <c>0x489787</c> — только грабителю, если это игрок клиента;
    /// задача <c>0x4cea43</c>): <c>X005TA0042</c> «Вы победили!»; если золота больше 0 (<c>0x586a2e(0)</c>) — перевод строки и
    /// <c>X005TA0044</c> с <c>%CURRENCY%</c> = найденное словами (<c>0x61c910</c>, <see cref="PriceText.Words"/>). Журнал 20261016g-port-synth,
    /// карта 8 RL: <c>ui_msgbox</c> seq 564453 «Вы победили!⏎\hC;Вы нашли 300 золота» (⏎ — символ перевода строки) — после
    /// <c>LOOTRUIN</c>, до окна предмета.
    /// </summary>
    public static string RuinLoot(Bank found, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string won = text(RuinWonText);
        return found.Gold > 0
            ? won + "\n" + text(RuinGoldText).Replace("%CURRENCY%", PriceText.Words(text, found), StringComparison.Ordinal)
            : won;
    }

    /// <summary>Текст окна сообщения: шпион — <c>%PLAYER%</c> имя другого игрока, <c>%RACE%</c> название его расы (<c>0x489f16</c>);
    /// иллюзия — <c>%KILLER%</c> и <c>%ILLUSION%</c> подписи лидеров: собственное имя, иначе имя типа (<paramref name="typeName"/>,
    /// <c>0x4ce918</c> → <c>0x5e97dc</c>). Не сообщение — null.</summary>
    public static string? Text(TurnSight sight, MapState map, GameCatalog catalog, Func<string, string> text, Func<string, string> typeName)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typeName);
        switch (sight.Kind)
        {
            case TurnSightKind.SpyCaught or TurnSightKind.SpyLost:
            {
                var other = map.Players.GetValueOrDefault(sight.Id);
                string race = other is not null && catalog.Races.FirstOrDefault(r => Same(r.Id, other.RaceId)) is { } r ? text(r.NameText) : "";
                return text(sight.Kind == TurnSightKind.SpyCaught ? SpyCaughtText : SpyLostText)
                    .Replace("%PLAYER%", other?.Name.Trim() ?? "", StringComparison.Ordinal).Replace("%RACE%", race, StringComparison.Ordinal);
            }
            case TurnSightKind.IllusionKilled:
            {
                string Caption(int at) => sight.Ids.Count < at + 2 ? "" : sight.Ids[at].Length > 0 ? sight.Ids[at] : typeName(sight.Ids[at + 1]);
                return text(IllusionText).Replace("%KILLER%", Caption(0), StringComparison.Ordinal)
                                         .Replace("%ILLUSION%", Caption(2), StringComparison.Ordinal);
            }
            case TurnSightKind.CaptureRefused:
                return text(CaptureRefusedText);
            default:
                return null;
        }
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Окно сообщения клиента (<c>DLG_MESSAGE_BOX</c>, <c>TXT_INFO</c>) для <see cref="NoticeWatch"/> — как окно сообщения графа,
/// но своего вида: ход ИИ на экране ждёт его, как попапа события. <paramref name="sound"/> — звук открытия окна (<c>Midgard</c>), если у
/// задачи движка он есть (разграбленная руина — <c>ENTRRUIN</c>). Кнопка одна — «ОК»: все эти окна движок открывает с <c>yesNo = 0</c>
/// (корпус: <c>X005TA0458</c>, <c>X005TA0536</c>, <c>X005TA0605</c>, руина <c>0x4cec07</c> — <c>yes_no=false</c>; снимок оригинала 19.10 —
/// одна печать, у порта были «Да» и «Нет»).</summary>
public sealed class NoticeLogic(string text, string? sound = null) : IScreenLogic
{
    public string Text => text;

    public string? Sound => sound;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Screen.Elements.Any(e => e.Id == "TXT_INFO")) ui.SetText("TXT_INFO", text);
        if (ui.Screen.Elements.Any(e => e.Id == "BTN_OK")) MessageBoxLogic.Buttons(ui, question: false);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}

/// <summary>
/// Разметка <c>DLG_BEGIN_TURN</c> (<c>CBeginTurnInterf</c> <c>0x4b9b9d</c>, <c>CCaptureResourcesInterf</c> <c>0x4929ae</c>): заголовок
/// <c>TXT_TURN_NUMBER</c>, <c>TXT_INCOME</c> — <c>X005TA0051</c> «Доход:» (<c>0x4b9d4f</c>, <c>0x492a6e</c>), шесть чисел казны
/// (<c>0x4b9d96</c>, <c>0x492ae8</c>: золото, мана Смерти — <c>ORANGE</c>, Ада — <c>ROUGE</c>, Жизни — <c>JAUNE</c>, Рун — <c>BLANC</c>, Рощи —
/// <c>BLUE</c>), <c>sprintf("%d", short)</c>. <c>TXT_OBJECTIVE</c> не трогается. Закрывает <c>BTN_OK</c>.
/// </summary>
public static class IncomeScroll
{
    public const string Screen = "DLG_BEGIN_TURN";

    public const string IncomeText = "X005TA0051";

    public static void Fill(UiScreenState ui, string title, Bank bank, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        ui.SetText("TXT_TURN_NUMBER", title);
        ui.SetText("TXT_INCOME", text(IncomeText));
        ui.SetText("TXT_INCOME_GOLD", Num(bank.Gold));
        ui.SetText("TXT_INCOME_ORANGE", Num(bank.Death));
        ui.SetText("TXT_INCOME_ROUGE", Num(bank.Infernal));
        ui.SetText("TXT_INCOME_JAUNE", Num(bank.Life));
        ui.SetText("TXT_INCOME_BLANC", Num(bank.Runic));
        ui.SetText("TXT_INCOME_BLUE", Num(bank.Grove));
    }

    /// <summary><c>sprintf("%d", short)</c> (<c>0x586a2e</c> отдаёт <c>short</c>).</summary>
    private static string Num(long value) => ((short)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Окно взятых ресурсов (<c>CCaptureResourcesInterf</c>, конструктор <c>0x4929ae</c>; команда <c>CCmdCaptureResourcesMsg</c> захватчику
/// столицы, <c>0x434d61</c>): разметка <c>DLG_BEGIN_TURN</c> (указатель <c>0x79a324</c>), <c>TXT_TURN_NUMBER</c> — <c>X005TA0776</c> «Вы
/// захватили ресурсы», числа — казна погибшего до захвата (<see cref="IncomeScroll"/>). Закрывает <c>BTN_OK</c> (<c>0x492c80</c>).
/// </summary>
public sealed class CaptureResourcesLogic(Bank bank, Func<string, string> text) : IScreenLogic
{
    public const string Screen = IncomeScroll.Screen;

    public const string TitleText = "X005TA0776";

    public const string IncomeText = IncomeScroll.IncomeText;

    public void Open(UiScreenState ui) => IncomeScroll.Fill(ui, text(TitleText), bank, text);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}

/// <summary>
/// Окно начала хода (<c>CBeginTurnInterf</c>, конструктор <c>0x4b9b9d</c> с сообщением хода): сервер в начале хода человека
/// (<c>CEffectPlayerTurn</c> <c>0x43ab41</c>) считает доход (<c>0x5da55f</c>), кладёт его в казну и шлёт <c>CCmdTurnInfoMsg</c> с «загрузка» 0 и
/// «показать» 1 (<c>0x478102</c>: <c>+0x14</c> = 0, <c>+0x15</c> = 1); клиент (<c>0x48a269</c> → <c>CTaskBeginTurn</c> <c>0x4d881a</c>) — это
/// окно: <c>TXT_TURN_NUMBER</c> — <c>X005TA0047</c> «Начинается N день» (<c>%NUMBER%</c> — день, <c>0x59d5b3</c>), «Доход:» и доход хода
/// (<see cref="IncomeScroll"/>), звук <c>BOTREPRT</c>. «ОК» снимает окно и зовёт <c>0x4d8bd0</c> — в день 1 окно имени героя
/// (<c>DLG_GETINFO_BOX</c>), потом очередь команд (попапы событий начала хода). Загрузка — вместо окна сообщение «Повтор миссии»
/// (<c>+0x14</c> = 1). Журнал 20261016g-port-synth, карта 9: <c>DLG_BEGIN_TURN</c> 3207023 → щелчок <c>BTN_OK</c> → <c>DLG_GETINFO_BOX</c> 3207029
/// → <c>DLG_EVENT_POPUP</c> 3207048; дни 2–5 — каждый раз после хода ИИ.
/// </summary>
public sealed class BeginTurnLogic(int day, Bank income, Func<string, string> text) : IScreenLogic
{
    public const string Screen = IncomeScroll.Screen;

    public const string TitleText = "X005TA0047";

    public int Day => day;

    public Bank Income => income;

    public void Open(UiScreenState ui) =>
        IncomeScroll.Fill(ui, text(TitleText).Replace("%NUMBER%", day.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                                       StringComparison.Ordinal), income, text);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}
