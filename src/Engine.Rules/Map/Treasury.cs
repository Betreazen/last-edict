using LastEdict.Rules.Magic;

namespace LastEdict.Rules.Map;

/// <summary>
/// Цены юнита в части казны, которых нет в <see cref="UnitHire"/>. Прямая проекция
/// <c>Gunits.dbf</c>, ранг 1: смещения данных <c>TUsSoldierImpl</c> — <c>XP_NEXT +0x30</c>,
/// <c>REVIVE_C +0x48</c>, <c>HEAL_C +0x54</c>, <c>TRAINING_C +0x60</c> (разбор
/// <c>disciples-modding/.superpowers/sdd/2026-09-09-f6plus-coverage/sites.md</c> и <c>cities.md</c>).
/// <para><b>Рост уровнем.</b> Движок домножает и эти три цены на число шагов <c>GDynUpgr</c>
/// (<c>0x598dd3</c>, <c>0x598e4e</c>, <c>0x598ec9</c>, см. <see cref="Units.UnitGrowth"/>), но у всех
/// 502 строк <c>GDynUpgr</c> ванили <c>REVIVE_C</c>, <c>HEAL_C</c> и <c>TRAINING_C</c> нулевые
/// (ранг 1, пересчитано по таблице 11.09.2026) — цена базового типа и есть цена прокачанного юнита.
/// <see cref="XpNext"/> — порог БАЗОВОГО типа: у прокачанного он растёт (<c>GDynUpgr.XP_NEXT</c>),
/// выращенный порог передаёт вызывающий.</para>
/// </summary>
public sealed record UnitCosts(string UnitId, Bank Revive, Bank Heal, Bank Training, int XpNext);

/// <summary>Справочники казны сверх <see cref="EconomyRules"/>. Собирает
/// <c>LastEdict.Data.Map.EconomyLoader.LoadTreasury</c>.</summary>
/// <param name="Units">Цены юнитов по <c>UNIT_ID</c> (регистр не важен).</param>
/// <param name="RodCost"><c>GVars.ROD_COST</c> = <c>g0150</c>. Ранг 1: таблица и 7 из 7 установок
/// прогонов 1–2 Ф7 (план Ф7, §10.1, §11.1).</param>
/// <param name="LordCategory"><c>Glord.CATEGORY</c> по <c>LORD_ID</c>: <c>Llord.dbf</c>
/// 0 <c>L_MAGE</c>, 1 <c>L_WARRIOR</c>, 2 <c>L_DIPLOMAT</c>. Ранг 1 (таблица).</param>
public sealed record TreasuryRules(
    IReadOnlyDictionary<string, UnitCosts> Units,
    Bank RodCost,
    IReadOnlyDictionary<string, int> LordCategory);

/// <summary>Покупка «сколько влезет»: сколько очков (HP или опыта) куплено и за сколько.</summary>
public readonly record struct Purchase(int Points, Bank Cost);

/// <summary>
/// Казна игрока: одна операция изменения и цены всех механик, которые её двигают.
/// <para><b>Одна точка.</b> Все траты и приходы движка — покупка, продажа, найм, обучение,
/// лечение, воскрешение, постройка, рост города, жезл, дань, захват столицы, дневной доход —
/// заканчиваются визитором <c>CVisitorChangePlayerBank::apply</c> (Gog <c>0x60be8d</c>), который
/// <b>присваивает</b> казне новое значение (ранг 1: <c>after == delta</c> в 113 из 113 строк
/// <c>bank</c> с <c>mode = 0</c>, план Ф7 §12.4, §13.4). Новое значение считает вызывающий, и
/// считает его сложением <see cref="Apply"/>. Поэтому в порте механика не пишет казну сама: она
/// считает цену, а казну меняет <see cref="Apply"/>.</para>
/// <para>Сверка с корпусом — <c>TreasuryHarnessTests</c>: каждое движение казны в журналах Ф7
/// объясняется предшествующим событием-причиной и ценой отсюда.</para>
/// </summary>
public static class Treasury
{
    /// <summary><c>Llord.dbf</c>: 2 = <c>L_DIPLOMAT</c> (Guildmaster, Повелитель Воров). Ранг 1.</summary>
    public const int GuildmasterCategory = 2;

    /// <summary><c>Llord.dbf</c>: 0 = <c>L_MAGE</c> (у Империи <c>G000LR0003</c>). Ранг 1 — таблица.</summary>
    public const int MageCategory = 0;

    /// <summary><c>Lbuild.dbf</c>: 2 = башня магии (<c>Gbuild.CATEGORY</c>). Ранг 1 — таблица.</summary>
    public const int MagicTowerCategory = 2;

    /// <summary>
    /// Единственная операция изменения казны: сложение по всем шести валютам, затем обрезка каждой
    /// в [0, 9999]. Ранг 1: <c>BankApi::add</c> (Gog <c>0x586642</c>) — шесть сложений <c>int16</c>
    /// подряд, потом шесть обрезок (<c>0x586676..0x58663B</c>), <b>обрезка после сложения</b>
    /// (<c>capture-treasury.md</c> §3). Трата — сложение с отрицательной ценой, та же обрезка снизу.
    /// Дневной доход, включая тик при загрузке карты (план Ф7 §11.4), — тоже сюда:
    /// <c>Apply(казна, Economy.DailyIncome(...))</c>.
    /// </summary>
    public static Bank Apply(Bank purse, Bank change) => Economy.Clamp(purse + change);

    /// <summary>Списать цену: <see cref="Apply"/> с отрицательной ценой. Ранг 1: <c>Bank::subtract</c>
    /// (<c>0x586578</c>) — шесть вычитаний <c>int16</c>, затем та же обрезка в [0, 9999] (<c>0x5865ac</c>…,
    /// листинг capstone 16.09.2026).</summary>
    public static Bank Pay(Bank purse, Bank price) => Apply(purse, Bank.Zero - price);

    /// <summary>
    /// Дневной доход игрока ИИ делится между казной и вторым кошельком (<c>SPELL_BANK</c>, им ИИ платит за
    /// заклинания): золото идёт в казну целиком, каждая мана — <c>⌊доход/2⌋</c> в казну и остаток <c>⌈доход/2⌉</c> в
    /// кошелёк. <b>Ранг 1 по корпусу</b> (23.09, пары строк <c>income</c> ИИ: казна до и после первого начисления):
    /// золото 761/763, мана 2005/2007 (оба исключения — срез экранов <c>20260921-ui</c>, где казну двигал игрок);
    /// кошелёк — <c>ai_budget.spell_bank</c> 20 → 33 при доходе жизни 25. Где это в коде, не искали. У человека доход
    /// целиком в казну (строка <c>income</c> одна).
    /// </summary>
    public static (Bank Purse, Bank SpellBank) AiIncome(Bank purse, Bank spellBank, Bank income)
    {
        ArgumentNullException.ThrowIfNull(income);
        var toPurse = income.Map((currency, v) => currency == "gold" ? v : v / 2);   // валюта мода — как мана
        return (Apply(purse, toPurse), Apply(spellBank, income - toPurse));   // золото ушло целиком — в кошелёк 0
    }

    /// <summary>
    /// Чит ресурсов ИИ «AiCheatResource» — второй вызов дневного дохода ИИ. Ранг 1 по листингу <c>0x4734ef</c>
    /// (зовётся из <c>0x473439</c> на игрока ИИ, <c>0x4734d6</c>): <c>доход = computePlayerDailyIncome</c>
    /// (<c>0x47351c</c>, это вторая строка <c>income</c>); по каждой валюте <c>⌊доход·p/100⌋</c> (<c>0x473543</c>,
    /// <c>imul; idiv 100</c>); казна <c>− доход</c> с обрезкой (<c>0x586578</c>), <c>+ ⌊доход·p/100⌋</c> с обрезкой
    /// (<c>0x586642</c>), запись казны <c>0x41f43b</c> → <c>0x431d55</c>. <c>p</c> — <c>GAI.D_*_M</c> по сложности
    /// (<see cref="AiCheatRules"/>). Корпус: на средней (<c>p = 100</c>) — «мана доливается до дохода», 411/0;
    /// на лёгкой (<c>p = 70</c>) — <c>20260914-smoke</c> seq 52816: 595/0,48 − 195/0,97 → 400/0,0, + 136/0,67 → 536/0,67.
    /// </summary>
    public static Bank AiResourceCheat(Bank purse, Bank income, int percent) =>
        Apply(Pay(purse, income), Scale(income, percent));

    /// <summary>Доля казны по каждой валюте: <c>⌊v·percent/100⌋</c> (<c>0x5838c0</c> — <c>imul</c>, <c>idiv 100</c>).</summary>
    public static Bank Share(Bank b, int percent) => Scale(b, percent);

    private static Bank Scale(Bank b, int percent) => b.Map(v => v * percent / 100);

    /// <summary>Покупка предмета у торговца — ценность типа (<see cref="ItemValue"/>, <c>getItemValue 0x600a20</c> без экземпляра):
    /// <c>GItem.VALUE</c>, а у талисмана золото × <see cref="TalismanValueChargesDefault"/>. Ранг 1 по данным: 12 из 12 покупок корпуса
    /// под тремя лордами по номиналу; талисман — <c>20261016e-av</c>, карта 6, seq 12891924: Talisman of Restoration (G000IG9117,
    /// VALUE 400) — диалог «стоит 2000», казна 3650 → 1650 (до 17.10 порт брал 400). Это цена без скидки: скидку лидера
    /// (<c>0x5dfd5f</c>: × (100 − <c>L_LOWER_COST</c>) / 100) накладывает <see cref="SiteTrade.Discounted"/> — у покупавших лидеров корпуса
    /// её не было (план Ф7 §10.1, §16.2 приняли это за «скидки нет»; листинг — И47).</summary>
    public static Bank ItemBuyPrice(Item item) => ItemValue(item);

    /// <summary>Продажа торговцу — <c>ценность × SELL_RATIO / 100</c> по каждой валюте с усечением.
    /// Ранг 1: <c>applySellRatio</c> (<c>0x5838c0</c>), <c>GVars.SELL_RATIO = 20</c>, 7 из 7 продаж
    /// (одна съедена потолком казны 9999). Ценность — <see cref="ItemValue"/> (у талисмана она зависит от зарядов).</summary>
    /// <param name="talismanCharges">Остаток зарядов продаваемого экземпляра-талисмана (<c>CMidTalismanCharges</c>);
    /// null — не экземпляр.</param>
    public static Bank ItemSellPrice(Item item, int sellRatio, int? talismanCharges = null) =>
        Scaled(ItemValue(item, talismanCharges), sellRatio);

    /// <summary>Множитель ценности талисмана, когда предмет — не экземпляр (<c>[0x6ea178]</c> = 5 в .rdata).</summary>
    public const int TalismanValueChargesDefault = 5;

    /// <summary>Ценность предмета — <c>getItemValue 0x600a20</c>: <c>GItem.VALUE</c>, а у талисмана (категория
    /// <c>[0x8376b8]</c>) золото умножается на заряды (<c>0x600af4 imul</c>): у экземпляра <c>IM</c> (тип id 0x26) — на
    /// остаток из <c>CMidTalismanCharges</c> (<c>0x5eb9b3</c>, записи нет — 0), иначе на
    /// <see cref="TalismanValueChargesDefault"/>. Прочие валюты не трогаются. Ранг 1 по дизассемблеру и по данным:
    /// <c>20260919-human</c> seq 15347639 — Talisman of Freezing (IG9138, VALUE 600, 5 зарядов) продан за 600
    /// = 600 × 5 × 20 / 100 (диалог «за 600 золотых», строка bank 2494 → 3094).</summary>
    public static Bank ItemValue(Item item, int? talismanCharges = null) =>
        item.Category == ItemCategory.Talisman
            ? item.Value with { Gold = item.Value.Gold * (talismanCharges ?? TalismanValueChargesDefault) }
            : item.Value;

    /// <summary>Покупка заклинания в башне мага — <c>Gspells.BUY_C</c> (<c>0x582af3</c>). Ранг 1:
    /// 11 из 11 покупок, скидки лорда нет; скидку лидера отряда накладывает <see cref="SiteTrade.Discounted"/> (И47).</summary>
    public static Bank SpellBuyPrice(Spell spell) => spell.BuyCost;

    /// <summary>Изучение заклинания в столице — <c>GspellR.RESEARCH</c> строки (лорд, заклинание), у лорда
    /// <see cref="MageCategory"/> — <b>половина</b> по каждой валюте. Ранг 1 по данным (ai-q03): Маг
    /// <c>G000LR0003</c> — Lightning 100 из 200, Wrath of God 225 + 225 из 450 + 450 (2/0); Воитель — полные
    /// 200 и 450 + 450 (2/0). Цены <c>GspellR</c> у лордов одной расы одинаковы, так что скидку даёт код, а не
    /// таблица. ponytail: адрес множителя в <c>.exe</c> не прочитан; взято целочисленное деление (усечение) —
    /// отличить его от округления нечем: все 1872 цены <c>GspellR</c> ванили чётные.
    /// Если в чужих данных появится нечётная цена — нужен адрес. Пути изучения в порте нет (событие
    /// <c>spell_research</c> только читается) — подключать сюда.</summary>
    /// <param name="research"><c>GspellR.RESEARCH</c> для лорда игрока и заклинания.</param>
    /// <param name="lordCategory"><c>Glord.CATEGORY</c> лорда игрока (<see cref="TreasuryRules.LordCategory"/>).</param>
    public static Bank SpellResearchPrice(Bank research, int lordCategory) =>
        lordCategory == MageCategory ? Halved(research) : research;

    /// <summary>Воскрешение — плоский <c>REVIVE_C</c>, HP после воскрешения 1. Ранг 1:
    /// <c>reviveUnit</c> (<c>0x5D733F</c>: <c>bank − REVIVE_C</c>) и 4 из 4 воскрешений прогона 2.</summary>
    public static Bank RevivePrice(UnitCosts unit) => unit.Revive;

    /// <summary>Постройка в столице — <c>Gbuild.COST</c>. Ранг 1 по данным: платные постройки
    /// корпуса списывают ровно цену строкой <c>bank</c> прямо перед <c>build</c>; стартовые постройки
    /// (<c>id2 = G000000000</c>) и начальные постройки ИИ (<c>flag = 1</c>) казну не трогают. Код
    /// списания не найден (план Ф7 §1), скидки лорда нет.</summary>
    public static Bank BuildingPrice(BuildingType building) => building.Cost;

    /// <summary>Постройки, которые у лорда есть с начала партии даром: у <see cref="MageCategory"/> — башня
    /// магии его списка. Ранг 1 по данным (ai-q03, 1/0): при загрузке у Мага Империи <c>build</c>
    /// <c>G000BB0024</c> с <c>id2 G000000000</c> (seq 9279124) и без строки <c>bank</c>; у Воителя башни нет —
    /// он построил её за 150 на 2-м ходу (seq 9396280).
    /// ponytail: наблюдение одно — Маг Империи; для прочих рас башня берётся по той же категории
    /// (<c>Gbuild</c>: <c>bb0024/0048/0073/0098/0119</c>, по одной на расу). Списка построек лорда
    /// (<c>GbuiList.dbf</c>, <c>LORD_ID → BUILD_ID</c>) порт не читает — его подаёт вызывающий. Места старта
    /// партии в порте нет (<see cref="MapSim"/> начинает с готового <c>.sg</c>); подключать туда, где
    /// появится выбор лорда: добавить в <see cref="MapPlayer.Buildings"/> без списания.</summary>
    /// <param name="lordBuildings">Постройки из списка лорда (<c>GbuiList</c>).</param>
    public static IReadOnlyList<string> StartBuildings(int lordCategory, IEnumerable<BuildingType> lordBuildings) =>
        lordCategory == MageCategory
            ? [.. lordBuildings.Where(b => b.Category == MagicTowerCategory).Select(b => b.Id)]
            : [];

    /// <summary>Установка жезла — <c>GVars.ROD_COST</c>. Ранг 1 по данным (см.
    /// <see cref="TreasuryRules.RodCost"/>); события-причины у жезла в схеме зонда нет.</summary>
    public static Bank RodPrice(TreasuryRules rules) => rules.RodCost;

    /// <summary>
    /// Рост города размера <paramref name="size"/> на один уровень — строка <c>GcityInf</c> расы
    /// игрока с <c>SIZE = size</c>, колонка <c>GROWTH</c> (<c>getCityGrowCost</c> <c>0x5DC722</c>,
    /// размер зажат в 1…5 — <c>0x59C07B</c>). Ранг 1.
    /// <para><b>−50 % у лорда <see cref="GuildmasterCategory"/></b> — ранг 1 по данным: 75/125/250/375
    /// против таблицы 150/250/500/750 (прогон 1), 75 и 250 во второй большой партии; под Воителем —
    /// табличные 250 и 500. ponytail: множитель зашит в <c>.exe</c> и не прочитан; деление пополам с
    /// усечением сходится на всех ступенях ванили, где <c>GROWTH</c> чётный. Если в чужих данных
    /// появится нечётная цена — нужен адрес множителя.</para>
    /// </summary>
    /// <exception cref="ArgumentException">Нет строки <c>GcityInf</c> для расы и размера.</exception>
    public static Bank CityGrowthPrice(EconomyRules rules, string raceId, int size, int lordCategory)
    {
        int clamped = Math.Clamp(size, 1, 5);
        var city = rules.Cities.FirstOrDefault(c =>
                       c.RaceId.Equals(raceId, StringComparison.OrdinalIgnoreCase) && c.Size == clamped)
                   ?? throw new ArgumentException($"в GcityInf нет строки расы {raceId} размера {clamped}",
                                                  nameof(raceId));
        return lordCategory == GuildmasterCategory ? Halved(city.Growth) : city.Growth;
    }

    /// <summary>
    /// Лечение: <c>HEAL_C</c> за каждый недостающий HP, пока хватает казны. Ранг 1:
    /// <c>computeAffordableHealHp</c> (<c>0x5DE1C1</c>) — цикл «пока вылечено меньше недостающего и в
    /// кошельке хватает, вычесть <c>HEAL_C</c>»; данные прогона 2: полные 40/30/675/597/358/318/372,
    /// частичные 255 (85 HP) и «в казне 28, <c>HEAL_C</c> 3 — списано 27, 9 HP».
    /// </summary>
    public static Purchase Heal(Bank rate, int missingHp, Bank purse) =>
        Buy(rate, Math.Max(missingHp, 0), purse);

    /// <summary>
    /// Обучение у тренера: <c>TRAINING_C</c> за очко опыта до <c>XP_NEXT − 1</c> — последнее очко даёт
    /// только бой. При нехватке казны — <c>⌊казна / TRAINING_C⌋</c> очков. На потолке — ноль очков
    /// («уже прошёл курс»). Ранг 1: <c>0x5e044f</c> (<c>sites.md</c>); данные — 276, 316, 104 за 26 очков,
    /// 110 за 22 очка Рыцаря со ставкой 5, 376/296/2745/2495 во второй большой партии.
    /// </summary>
    /// <param name="xpNext">Порог уровня юнита С РОСТОМ (см. <see cref="UnitCosts.XpNext"/>).</param>
    public static Purchase Training(Bank rate, int xpNext, int xp, Bank purse) =>
        Buy(rate, Math.Max(xpNext - 1 - xp, 0), purse);

    /// <summary>Сколько единиц по цене <paramref name="rate"/> оплачивает казна — минимум по валютам,
    /// где цена ненулевая. Нулевая цена — без ограничения.</summary>
    public static int Affordable(Bank purse, Bank rate)
    {
        ArgumentNullException.ThrowIfNull(purse);
        ArgumentNullException.ThrowIfNull(rate);
        long best = int.MaxValue;
        foreach (var (currency, cost) in rate.Amounts())
            if (cost > 0) best = Math.Min(best, Math.Max(purse[currency], 0) / cost);
        return (int)best;
    }

    /// <summary>Дань: плательщик теряет <paramref name="gold"/> золота, получатель его получает. Ранг 1
    /// по данным: восемь передач корпуса — пара строк <c>bank</c> подряд, сначала получатель, затем
    /// плательщик, копейка в копейку (план Ф7 §13.1). Изменение отношений — не казна: очки дают
    /// <see cref="Diplomacy.Tribute"/> и <see cref="Diplomacy.TributeValue"/> (задача 13,
    /// <c>золото / D_GOLD</c>), а <c>CEffectGiveGold::apply</c> <c>0x4384de</c> делает и то и
    /// другое одним эффектом.</summary>
    public static (Bank Payer, Bank Recipient) Tribute(Bank payer, Bank recipient, long gold)
    {
        var amount = Economy.Currency(0, gold);
        return (Pay(payer, amount), Apply(recipient, amount));
    }

    /// <summary>
    /// Кончается ли захват столицы победой вместо захвата. Ранг 1, генератор <c>0x440EE0</c>:
    /// победа, если взята столица человека в одиночной игре (<c>0x4405CE</c>, <c>0x4025E7</c>), либо у
    /// захватчика остался ровно один живой соперник (<c>0x44120D</c>: играбельная раса, не захватчик,
    /// не захвачен — погибающий ещё в счёте) и в сценарии нет события победы (<c>0x441090</c>).
    /// </summary>
    public static bool CaptureEndsInVictory(int liveOpponents, bool scenarioHasWinEvent,
                                            bool capitalOwnerIsHuman, bool multiplayer) =>
        (capitalOwnerIsHuman && !multiplayer) || (liveOpponents == 1 && !scenarioHasWinEvent);

    /// <summary>
    /// Переход казны при захвате столицы — <c>0x434D61</c>: захватчику сумма обеих казн по всем шести
    /// валютам через <c>BankApi::add</c> (обрезка после сложения), погибшему — ноль; две строки
    /// <c>bank</c> сразу за <c>battle_end</c>, сначала захватчик. Ранг 1: код и данные — Нежить +379,
    /// Империя 5523 + 6662 → 9999, <c>f7-m22</c> +500 +333 +444 +555 +666 +777.
    /// <para>При <paramref name="victory"/> (<see cref="CaptureEndsInVictory"/>) создаётся
    /// <c>CEffectWin</c> вместо <c>CEffectCaptureCapital</c>, и перехода нет вовсе: обе казны прежние
    /// (<c>f7-m20</c> — после <c>battle_end</c> ни одной строки <c>bank</c>).</para>
    /// </summary>
    public static (Bank Winner, Bank Loser) CapitalCapture(Bank winner, Bank loser, bool victory) =>
        victory ? (winner, loser) : (Apply(winner, loser), Bank.Zero);

    private static Purchase Buy(Bank rate, int wanted, Bank purse)
    {
        int points = Math.Min(wanted, Affordable(purse, rate));
        return new Purchase(points, Times(rate, points));
    }

    private static Bank Times(Bank b, long n) => b.Map(v => v * n);

    private static Bank Halved(Bank b) => b.Map(v => v / 2);

    private static Bank Scaled(Bank b, int percent) => b.Map(v => v * percent / 100);
}
