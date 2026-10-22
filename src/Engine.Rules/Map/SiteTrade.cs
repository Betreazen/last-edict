using D2.Rules.Magic;

namespace D2.Rules.Map;

/// <summary>Итог торговой операции у сайта: новое состояние того, что она меняет, и казна.
/// <see cref="Refused"/> непусто — операции не было, состояние и казна прежние.</summary>
public sealed record Trade<T>(T State, Bank Purse, string? Refused = null)
{
    public bool Done => Refused is null;
}

/// <summary>Заклинания башни мага и книга игрока после покупки.</summary>
public sealed record SpellBooks(IReadOnlyList<string> Site, IReadOnlyList<string> Known);

/// <summary>
/// Торговля у сайтов карты — план Ф7 §1.1–1.2, задача 3: торговец, башня мага, наёмник, тренер. Сам вход в сайт
/// (<see cref="Sites.Visit"/>) ничего не даёт — каждая покупка отдельное действие. Функции чистые: цена — из
/// <see cref="Treasury"/>, казну меняет <see cref="Treasury.Pay"/>/<see cref="Treasury.Apply"/>.
/// <para>Адреса серверных обработчиков (ранг 1, sites §2): <c>onSiteBuyItem</c> <c>0x5d76e2</c>,
/// <c>onSiteSellItem</c> <c>0x5d77f5</c>, <c>onSiteBuySpell</c> <c>0x5d7952</c>, <c>onSiteBuyUnit</c>
/// <c>0x5d7aa1</c>, <c>onSiteTrainUnit</c> <c>0x5d7c6b</c>. Скидка лидера (<c>getLowerCost</c>, слот <c>+0x24</c> лидера
/// <c>CMidStack +0x28</c>, процент <c>L_LOWER_COST</c>) — у покупки предмета и заклинания (<see cref="Discounted"/>, каждая валюта) и у
/// найма наёмника (<see cref="LeaderDiscount"/>, только золото); ранг 1 по листингу. В корпусе у покупавших и нанимавших лидеров скидки не
/// было — покупки шли по номиналу (план §10.1, §16.2), это с листингом не спорит.</para>
/// </summary>
public static class SiteTrade
{
    /// <summary>Покупка предмета: <c>GItem.VALUE</c> по номиналу, со склада уходит один экземпляр
    /// (<c>CVisitorMerchantDelItem</c> <c>0x612fc2</c>), покупка кладётся в сумку отряда — во всех 14 покупках
    /// корпуса <c>slot = −1</c>. Ранг 1 на цену и склад; строка склада на нуле убирается — ранг 2 (сверяет
    /// харнесс <c>site_buy_item</c>).</summary>
    /// <param name="lowerCostPercent">Скидка лидера покупающего отряда (<see cref="Discounted"/>).</param>
    public static Trade<IReadOnlyList<MerchantStock>> BuyItem(IReadOnlyList<MerchantStock> stock, Item item, Bank purse, int lowerCostPercent = 0)
    {
        var row = stock.FirstOrDefault(s => Same(s.Item, item.Id) && s.Count > 0);
        if (row is null) return new(stock, purse, $"у торговца нет {item.Id}");
        var price = Discounted(Treasury.ItemBuyPrice(item), lowerCostPercent);
        if (Treasury.Affordable(purse, price) < 1) return new(stock, purse, $"казны не хватает на {item.Id}");
        var left = stock.Select(s => ReferenceEquals(s, row) ? s with { Count = s.Count - 1 } : s)
                        .Where(s => s.Count > 0).ToList();
        return new(left, Treasury.Pay(purse, price));
    }

    /// <summary>Продажа: <c>VALUE × SELL_RATIO / 100</c> в казну (излишек над 9999 теряется), <b>склад торговца не
    /// меняется</b> — проданный предмет исчезает. Ранг 1 на цену (7 из 7) и на склад: во всех 7 продажах второй
    /// большой партии в следующем сохранении проданных типов на складе нет (харнесс <c>site_sell_item</c>).
    /// <i>Было иначе:</i> «проданное на склад добавляется» (план Ф7 §1.2 по sites §2.1 — по наличию
    /// <c>CVisitorMerchantAddItem</c> в exe; его вызывает не продажа).
    /// <para><b>Отказ — только <see cref="ItemCategory.Special"/></b> (L_SPECIAL, 14). Ранг 1 по дизассемблеру:
    /// серверная проверка <c>0x5dfe1a</c> отвечает по категории предмета сама (<c>[0x837718]</c> → true,
    /// <c>[0x837658]</c> → false), остальное отдаёт визитору <c>0x5e7c51</c> (vftable <c>0x6f0b84</c>), а тот —
    /// только в <c>canApply</c> <c>0x612e7f</c> (режим 0): объект — торговец, тип предмета есть, категория ≠
    /// <c>[0x837718]</c>. <b>Флаги торговца <c>BUY_*</c> на этом пути не читаются</b> — поэтому их нет и в
    /// параметрах. Ранг 1 по данным (ai-q20): при <c>BUY_WEAPON=0</c> Runic Blade (L_WEAPON) продан за 400 —
    /// <c>sell_check</c> true у <c>0x4998e5</c>, <c>0x4fe8c8</c> и серверного <c>0x5d7816</c>; Totem (L_SPECIAL) —
    /// false, продажи нет; проверено на категориях 2/5/10/14.</para></summary>
    public static Trade<IReadOnlyList<MerchantStock>> SellItem(IReadOnlyList<MerchantStock> stock, Item item,
                                                               int sellRatio, Bank purse, int? talismanCharges = null) =>
        item.Category == ItemCategory.Special
            ? new(stock, purse, $"{item.Id} — L_SPECIAL: торговец такие предметы не покупает")
            : new(stock, Treasury.Apply(purse, Treasury.ItemSellPrice(item, sellRatio, talismanCharges)));

    /// <summary>Покупка заклинания: <c>Gspells.BUY_C</c>, заклинание уходит из списка башни
    /// (<c>CVisitorMageDelSpell</c> <c>0x612e1a</c>) и добавляется игроку (<c>CVisitorPlayerAddKnownSpell</c>).
    /// Отказ, если у башни его нет или игрок его уже знает (<c>X005TA0511</c>). Ранг 1.</summary>
    /// <param name="lowerCostPercent">Скидка лидера покупающего отряда (<see cref="Discounted"/>).</param>
    public static Trade<SpellBooks> BuySpell(SpellBooks books, Spell spell, Bank purse, int lowerCostPercent = 0)
    {
        if (!books.Site.Contains(spell.Id, StringComparer.OrdinalIgnoreCase))
            return new(books, purse, $"у башни нет {spell.Id}");
        if (books.Known.Contains(spell.Id, StringComparer.OrdinalIgnoreCase))
            return new(books, purse, $"{spell.Id} уже известно игроку");
        var price = Discounted(Treasury.SpellBuyPrice(spell), lowerCostPercent);
        if (Treasury.Affordable(purse, price) < 1) return new(books, purse, $"казны не хватает на {spell.Id}");
        var site = books.Site.Where(s => !Same(s, spell.Id)).ToList();
        return new(new SpellBooks(site, [.. books.Known, spell.Id]), Treasury.Pay(purse, price));
    }

    /// <summary>Найм у наёмника. Цена <paramref name="price"/> — <c>Economy.HireCost(hire, level, …)</c> с
    /// уровнем предложения, а не <c>ENROLL_C</c> по номиналу: ранг 1, ai-q53 3/0 — Wolf <c>G000UU5039</c>
    /// 4-го уровня за 390 = 300 + 3·30 (в журнале тип порождён как <c>G000UG8165</c>), Barbarian 850, Orc 300;
    /// формула без уровня — 2/1. Уникальный (<c>UNIT_UNIQ</c>) уходит из ростера (<c>CVisitorMercDelUnit</c>
    /// <c>0x61309a</c>) <b>перестановкой</b>: на его место встаёт последний элемент — ранг 1, ai-q53 1/0
    /// ([5040 uniq, 5039, 5013] → [5013, 5039]). Неуникальный остаётся — ранг 1, 2/0.
    /// <para><b>Место в отряде</b> (<c>0x498291</c>, ранг 1, ai-q20b): <paramref name="free"/> — <see cref="FreeCells"/>.
    /// Порядок: пустой ростер — <c>X005TA0514</c>; свободно 0 — <c>X005TA0247</c> (seq 699012, 709729); большой
    /// (<paramref name="sizeSmall"/> = false, две клетки) при свободной одной — <c>X005TA0501</c>. Свиток
    /// <c>X005TA0502</c> («только большие») — уведомление при входе, не отказ операции.</para>
    /// ponytail: большой при свободных ≥ 2 берётся без проверки расположения клеток (передний/задний ряд) — в
    /// корпусе такого отказа нет; появится — проверять раскладку отряда, а не число.</summary>
    /// <param name="sizeSmall"><c>Gunits.SIZE_SMALL</c> нанимаемого.</param>
    /// <param name="free">Свободные клетки отряда (<see cref="FreeCells"/>).</param>
    public static Trade<IReadOnlyList<MercenaryOffer>> HireUnit(IReadOnlyList<MercenaryOffer> roster, string unitType,
                                                                bool sizeSmall, int free, Bank price, Bank purse)
    {
        if (roster.Count == 0) return new(roster, purse, "X005TA0514: наёмнику нечего предложить");
        if (free <= 0) return new(roster, purse, "X005TA0247: не хватает свободного места в отряде");
        var offer = roster.FirstOrDefault(o => Same(o.UnitType, unitType));
        if (offer is null) return new(roster, purse, $"у наёмника нет {unitType}");
        if (!sizeSmall && free < 2) return new(roster, purse, $"X005TA0501: {unitType} большой, место — на малого");
        if (Treasury.Affordable(purse, price) < 1) return new(roster, purse, $"казны не хватает на {unitType}");
        return new(offer.Unique ? SwapRemove(roster, offer) : roster, Treasury.Pay(purse, price));
    }

    /// <summary>Скидка найма лидера отряда (<c>onSiteBuyUnit</c> <c>0x5d7aa1</c>, ранг 1 по листингу): у лидера (<c>CMidStack +0x28</c>)
    /// <c>IUsStackLeader::getLowerCost</c> (слот <c>+0x24</c>, процент <c>L_LOWER_COST</c>), и золото цены (<c>0x586a2e</c>/<c>0x586a6f</c>
    /// с валютой 0) становится <c>золото × (100 − скидка) / 100</c> с отбросом дробной части (<c>idiv</c>); прочие валюты не трогаются.
    /// Чит ИИ (<see cref="AiPrice"/>, <c>0x61eedd</c>) идёт уже после скидки. Процент движок не зажимает — как и здесь: сумма
    /// <c>L_LOWER_COST</c> больше 100 (модовые предметы) дала бы отрицательную цену и у него.</summary>
    public static Bank LeaderDiscount(Bank cost, int lowerCostPercent)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return lowerCostPercent == 0 ? cost : cost with { Gold = cost.Gold * (100 - lowerCostPercent) / 100 };
    }

    /// <summary>Цена покупки у торговца и мага со скидкой лидера (И47, ранг 1 по листингу): каждая валюта
    /// <c>× (100 − скидка) / 100</c> с отбросом дробной части (<c>0x5868d6</c>, <c>idiv</c>). Так считают цену предмета
    /// <c>0x5dfd5f</c> (её берут проверка сервера <c>0x5dfb98</c>, проверка окна <c>0x5df9ea</c>, вопрос покупки <c>0x50595a</c> и ИИ
    /// у торговца и в храме <c>0x46b428</c>/<c>0x46b84b</c>/<c>0x46cb5e</c>), списание и проверка заклинания (<c>0x5d7a36…0x5d7a4b</c>,
    /// <c>0x5dfee2</c>), строка списка башни (<c>0x4fcfab</c>, только золото — его там и пишут).</summary>
    public static Bank Discounted(Bank cost, int lowerCostPercent)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return lowerCostPercent == 0 ? cost : Treasury.Share(cost, 100 - lowerCostPercent);
    }

    /// <summary>Потолок клеток отряда: базовая группа 6 (<c>0x5f444b</c>), гарнизон 6 (<c>0x617b8e</c>).</summary>
    public const int MaxCells = UnitCells.CellCount;

    /// <summary>Свободные клетки для найма: <c>max(0, min(лидерство, 6) − занятые)</c> (<c>0x498291</c>,
    /// <c>0x5f4485</c>). Ранг 1 на формулу; потолок 6 — ранг 2: вместимость отряда берётся из кеша <c>0x6144ce</c>
    /// (<c>+0x2c</c> или <c>[+0x30]→vf 0x24</c>, недочитано), а по данным KC002f лидерство 7 (4 + 3 навыка) при 6
    /// занятых дало отказ. Лидерство — с навыками (<see cref="D2.Rules.Units.EffectiveStats.OfStack"/>).</summary>
    /// <param name="occupied">Занятые клетки (большой юнит — две).</param>
    public static int FreeCells(int leadership, int occupied) => Math.Max(0, Math.Min(leadership, MaxCells) - occupied);

    /// <summary>Удаление перестановкой: последний элемент встаёт на место удалённого, список короче на один.</summary>
    private static List<MercenaryOffer> SwapRemove(IReadOnlyList<MercenaryOffer> roster, MercenaryOffer offer)
    {
        var left = roster.ToList();
        int at = left.FindIndex(o => ReferenceEquals(o, offer));
        left[at] = left[^1];
        left.RemoveAt(left.Count - 1);
        return left;
    }

    /// <summary>Обучение у тренера: <c>xp = min(⌊казна / TRAINING_C⌋, XP_NEXT − 1 − опыт)</c>, цена
    /// <c>TRAINING_C × xp</c> (<see cref="Treasury.Training"/>, <c>0x5e0396</c>). Уровень тренер не даёт никогда,
    /// ограничения «раз в ход» нет. На потолке — отказ «курс пройден» (<c>X005TA0499</c>). Ранг 1: прогон 4 —
    /// 26 (казна 105), 69, 26 → 79, Рыцарь 22 по ставке 5; вторая большая партия 94/74/549/499.</summary>
    /// <param name="xpNext">Порог уровня с ростом.</param>
    public static Trade<int> Train(int xp, int xpNext, Bank rate, Bank purse)
    {
        var bought = Treasury.Training(rate, xpNext, xp, purse);
        return bought.Points == 0
            ? new(xp, purse, xp >= xpNext - 1 ? "курс пройден: опыт на XP_NEXT − 1" : "казны не хватает ни на одно очко")
            : new(xp + bought.Points, Treasury.Pay(purse, bought.Cost));
    }

    /// <summary>Обучение по команде ИИ целиком (<c>0x5d7c6b</c> с флагом «от ИИ»): очки и цена — как у <see cref="Train"/>, затем чит
    /// <see cref="AiTraining"/>, а опыт прибавляет <c>CMidUnit::addXp</c> (<c>0x5e9a7b</c>) с зажимом в <c>XP_NEXT</c> — чит доводит ровно до
    /// порога (журнал 20261018b-port-synth, карта 16, сложная, <c>SUGG_LVL</c> 10: S143UN0029 15 → 95 при XP_NEXT 95; S143UN002e 48 → 319 за
    /// 362 золота из 725 — 181 очко × 150 % по цене × 50 %).</summary>
    /// <param name="highLevel"><c>SUGG_LVL</c> ≥ 10.</param>
    /// <param name="difficulty">Сложность игры (<c>Ldiff</c>).</param>
    public static Trade<int> TrainAi(int xp, int xpNext, Bank rate, Bank purse, bool highLevel, int difficulty)
    {
        var bought = Treasury.Training(rate, xpNext, xp, purse);
        if (bought.Points == 0) return Train(xp, xpNext, rate, purse);   // отказ с причиной; «курс пройден» движок проверяет до чита (0x5e0372)
        var (cost, points) = AiTraining(bought.Cost, bought.Points, highLevel, difficulty);
        return new(Math.Min(xp + points, xpNext), Treasury.Pay(purse, cost));
    }

    /// <summary>
    /// Обучение по команде ИИ (<c>0x61eb4a</c>; сервер <c>0x5d7c6b</c> зовёт его при флаге «от ИИ», построитель тренера и
    /// решение ИИ у тренера — всегда): при <c>SUGG_LVL ≥ 10</c> и сложности выше лёгкой цена урезается до 75 / 50 / 25 %
    /// (средняя / сложная / очень сложная, <c>0x61ee17</c>: <c>⌊v·p/100⌋</c> по валютам), а очки растут до 125 / 150 / 200 %;
    /// валюта с ненулевой ценой не дешевле 1. Иначе — как есть. Ранг 1 по листингу; соответствие сложностей константам
    /// <c>0x8379c0</c>/<c>0x8379b0</c>/<c>0x8379a0</c> — ранг 2 (порядок, как у <see cref="AiCheatRules"/>). В корпусе
    /// (<c>SUGG_LVL</c> ниже 10) не встречается.
    /// </summary>
    /// <param name="difficulty">Сложность игры (<c>Ldiff</c>: 0 — лёгкая … 3 — очень сложная).</param>
    public static (Bank Cost, int Points) AiTraining(Bank cost, int points, bool highLevel, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(cost);
        if (points == 0 || !highLevel || difficulty is < 1 or > 3) return (cost, points);
        int gain = difficulty switch { 1 => 125, 2 => 150, _ => 200 };
        return (AiPrice(cost, highLevel, difficulty), points * gain / 100);
    }

    /// <summary>
    /// Цена для ИИ (<c>0x61eedd</c>; наёмник — <c>0x458e6c</c>, <c>0x4597bf</c>; обучение — внутри <c>0x61eb4a</c>): при
    /// <c>SUGG_LVL ≥ 10</c> и сложности выше лёгкой — 75 / 50 / 25 % по валютам (средняя / сложная / очень сложная), валюта с
    /// ненулевой ценой не дешевле 1; иначе как есть. Ранг 1 по листингу, соответствие сложностей — ранг 2 (как у
    /// <see cref="AiTraining"/>).
    /// </summary>
    public static Bank AiPrice(Bank cost, bool highLevel, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(cost);
        if (!highLevel || difficulty is < 1 or > 3) return cost;
        int pct = difficulty switch { 1 => 75, 2 => 50, _ => 25 };
        var cut = Treasury.Share(cost, pct);
        static long AtLeastOne(long v, long was) => was > 0 ? Math.Max(v, 1) : v;
        return cut.Zip(cost, AtLeastOne);
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
