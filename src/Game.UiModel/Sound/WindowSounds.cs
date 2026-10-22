using D2.Data.Play;
using D2.Rules.Magic;

namespace D2.Data.Sound;

/// <summary>
/// Звуки окон карты (<c>Midgard</c>, <c>docs/ai-screen.md</c> §12). Движок, ранг 1: <c>OPENINTR</c>/<c>CLOSINTR</c> звучат при создании и
/// закрытии окон <c>CDiplomacyInterf</c>, <c>CExchangeInterf</c>, <c>CManageStkInterf</c>, <c>CPickUpDropInterf</c>,
/// <c>CBuildStructInterf</c>, <c>CCityStackInterf</c>, <c>CTradeItemInterf</c>, <c>CLoadGameInterf</c>, <c>CSaveGameInterf</c>,
/// <c>COptionInterf</c>; <c>OPENBOOK</c>/<c>CLOSBOOK</c> — книги заклинаний и исследования; <c>BOTREPRT</c> — окно начала хода;
/// <c>AICHAT</c> — речь ИИ; <c>ENTRSITE</c> — вход в лавку, башню мага, лагерь наёмников (у наставника звука нет); <c>BKPOPUP</c> —
/// попапы (кража, повышение героя, найм героя, сведения, предложение, дань, передача предметов). Какой разметке <c>DLG_*</c> отвечает
/// класс — ранг 2, по имени. Звука щелчка кнопки у движка нет. Действия в окнах (<see cref="OfAction"/>) — по уведомлениям модели.
/// </summary>
public static class WindowSounds
{
    public const string Archive = "Midgard";

    private static readonly HashSet<string> Interfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "DLG_DIPLOMACY", "DLG_EXCHANGE", "DLG_MANAGE_STACK", "DLG_PICKUP_DROP", "DLG_BUILD_STRUCTURE", "DLG_CITY_STACK",
        "DLG_TRADE_ITEM", "DLG_LOAD_GAME", "DLG_SAVE_GAME", "DLG_OPTIONS", "DLG_CARRY_ITEM_LEADER",
    };

    private static readonly HashSet<string> Sites = new(StringComparer.OrdinalIgnoreCase) { "DLG_MERCHANT", "DLG_MAGE", "DLG_MERCENARIES" };

    private static readonly HashSet<string> Popups = new(StringComparer.OrdinalIgnoreCase)
    {
        "DLG_STEAL_ITEM", "DLG_STEAL_SPELL", "DLG_UPGRADE_LEADER", "DLG_HIRE_LEADER_2", "DLG_GETINFO_BOX", "DLG_OFFER",
        "DLG_GIVE_RESOURCE", "DLG_GIVE_ITEM_SPELL",
    };

    private static readonly HashSet<string> Books = new(StringComparer.OrdinalIgnoreCase)
    {
        "DLG_CAST_SPELL", "DLG_RESEARCH_SPELL", "DLG_WAND_SCROLL",
    };

    /// <summary>Звук открытия окна; нет — null.</summary>
    public static string? Open(string screen) =>
        Interfaces.Contains(screen) ? "OPENINTR"
        : Books.Contains(screen) ? "OPENBOOK"
        : Sites.Contains(screen) ? "ENTRSITE"
        : Popups.Contains(screen) ? "BKPOPUP"
        : screen.Equals("DLG_BEGIN_TURN", StringComparison.OrdinalIgnoreCase) ? "BOTREPRT"
        : screen.Equals("DLG_CHAT_AI", StringComparison.OrdinalIgnoreCase) ? "AICHAT"
        : null;

    /// <summary>Звук окна полученных предметов и заклинаний (<c>DLG_ITEM</c>, сообщения 44/43): <c>TAKEBAG</c> — один раз на сообщение, сразу
    /// за первым окном, и у пустого мешка («Ничего не найдено»): <c>CTaskGiveItem</c> <c>0x4d95f5</c> и <c>CTaskGiveSpell</c> <c>0x4d9aa9</c>
    /// зовут звук 0x1d без условия после показа (<c>0x4d964b</c>; разбор 28.09, ранг 1). Следующие окна того же сообщения беззвучны.</summary>
    public static string? OfGiven(bool first) => first ? "TAKEBAG" : null;

    /// <summary>Звук действия в окне по уведомлению модели (<see cref="TurnSightKind"/>): постройка — <c>BUILDING</c>
    /// (<c>CBuildStructBranch</c>), покупка предмета и заклинания, найм — <c>BUYITEM</c>, лечение и воскрешение в городе — <c>PHEAL</c>,
    /// <c>PREVIVE</c>, исследование — <c>SPELLDIS</c>, дань — <c>GIVEGOLD</c>, захват укрепления — <c>OCCUPY</c>; прочее — null.</summary>
    public static string? OfAction(TurnSightKind kind) => kind switch
    {
        TurnSightKind.Built => "BUILDING",
        TurnSightKind.Bought => "BUYITEM",
        TurnSightKind.Healed => "PHEAL",
        TurnSightKind.Revived => "PREVIVE",
        TurnSightKind.Researched => "SPELLDIS",
        TurnSightKind.GoldGiven => "GIVEGOLD",
        TurnSightKind.Capture => "OCCUPY",
        _ => null,
    };

    /// <summary>Звук уведомления с объектом (разбор 27.09, ранг 1 по листингу; громкость — по умолчанию): прежние действия —
    /// <see cref="OfAction(TurnSightKind)"/>; зелье вне боя (<c>0x4ff396</c>) по категории предмета — усиление <c>PBOOST</c>, лечение
    /// <c>PHEAL</c>, воскрешение <c>PREVIVE</c>, постоянное <c>USEITEM</c>; ответ на союз предложившему (сообщение 18, <c>0x4d5429</c>) —
    /// <c>ALLIANCE</c>/<c>REFALLIA</c>, разрыв (<c>0x4850c4</c>, сообщение 19) — <c>BRKALLIA</c>; ответ на сделку предложившему (сообщение 16,
    /// <c>0x48b53b</c>): предмет — <c>TRADITEM</c>/<c>REFTRITM</c>, заклинание — <c>TRADSPEL</c>/<c>REFTRSPE</c>; итог вора (сообщение 14,
    /// <c>DLG_ACTION_RESULT</c>) — <c>BKPOPUP</c>; рост деревни (<c>CMidAnim2CityGrow</c>, сообщение 31) — <c>CITYGROW</c>; растекание
    /// земли (<c>CMidAnim2Terrain</c>) — <c>OCCUPY</c>; разграбление руины — <c>LOOTRUIN</c>; действие знатного — <c>SNOBLE</c>.</summary>
    public static string? OfSight(TurnSight sight, GameBook? book)
    {
        ArgumentNullException.ThrowIfNull(sight);
        return sight.Kind switch
        {
            TurnSightKind.PotionDrunk => book?.Items.TryGetValue(sight.Id, out var item) == true ? OfPotion(item.Category) : null,
            TurnSightKind.AllianceMade => "ALLIANCE",
            TurnSightKind.AllianceRefused => "REFALLIA",
            TurnSightKind.AllianceBroken => "BRKALLIA",
            TurnSightKind.Traded => IsSpell(sight.Id, book) ? "TRADSPEL" : "TRADITEM",
            TurnSightKind.TradeRefused => IsSpell(sight.Id, book) ? "REFTRSPE" : "REFTRITM",
            TurnSightKind.ThiefResult => "BKPOPUP",
            TurnSightKind.CityGrown => "CITYGROW",
            TurnSightKind.LandSpread => "OCCUPY",
            TurnSightKind.Looted => "LOOTRUIN",
            TurnSightKind.NobleAction => "SNOBLE",
            TurnSightKind.Capture when !sight.Occupy => null,   // деревня уже бывала у этой расы — OCCUPY не рассылается (0x434080)
            _ => OfAction(sight.Kind),
        };
    }

    /// <summary>Зелье, выпитое вне боя, по категории (<c>ITEM_CAT</c> 4–7); прочее сюда не доходит (<c>0x4ffad5</c>).</summary>
    public static string? OfPotion(ItemCategory category) => category switch
    {
        ItemCategory.PotionBoost => "PBOOST",
        ItemCategory.PotionHeal => "PHEAL",
        ItemCategory.PotionRevive => "PREVIVE",
        ItemCategory.PotionPermanent => "USEITEM",
        _ => null,
    };

    private static bool IsSpell(string id, GameBook? book) => book?.Spells.ContainsKey(id) == true;

    /// <summary>Действие только для звука окна (ход ИИ его не показывает).</summary>
    public static bool IsWindowAction(TurnSightKind kind) =>
        kind is TurnSightKind.Built or TurnSightKind.Bought or TurnSightKind.Healed or TurnSightKind.Revived
            or TurnSightKind.Researched or TurnSightKind.GoldGiven or TurnSightKind.PotionDrunk or TurnSightKind.AllianceMade
            or TurnSightKind.AllianceRefused or TurnSightKind.AllianceBroken or TurnSightKind.Traded or TurnSightKind.TradeRefused
            or TurnSightKind.ThiefResult or TurnSightKind.Dismissed or TurnSightKind.BagDropped or TurnSightKind.Sold or TurnSightKind.Trained;

    /// <summary>Звук нажатия элемента (разбор 27.09, ранг 1): только смена лица лорда — <c>BTN_FACE</c> экрана выбора лорда
    /// (<c>CMenuLord</c>, <c>DLG_CHOOSE_LORD</c>, <c>0x4e6945</c>) — <c>CHNGFACE</c>. Поле <c>[+0x44]</c> — ролик смены лица: пока он
    /// идёт, нажатие не принимается и беззвучно (разбор 28.09, ранг 1) — звук просит логика окна (<c>ChooseLordLogic</c>,
    /// <see cref="D2.Data.Ui.Windows.UiSound"/>). Прочие кнопки беззвучны.</summary>
    public static string? OfClick(string screen, string element) =>
        screen.Equals("DLG_CHOOSE_LORD", StringComparison.OrdinalIgnoreCase) && element.Equals("BTN_FACE", StringComparison.OrdinalIgnoreCase)
            ? "CHNGFACE" : null;

    /// <summary>Звук закрытия окна; нет — null.</summary>
    public static string? Close(string screen) =>
        Interfaces.Contains(screen) ? "CLOSINTR" : Books.Contains(screen) ? "CLOSBOOK" : null;
}
