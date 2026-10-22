namespace D2.Rules.Map;

/// <summary>
/// Вид объекта карты — по классу записи <c>.sg</c>, а не по двум буквам id: класс <c>SI</c>
/// покрывает сразу четыре вида (лавка, наёмники, наставник, башня мага), и различить их можно
/// только типом записи. Ранг 1: имена классов — <c>src/Engine.Play/Sg/Sg.g.cs</c>
/// (<c>MidSiteMerchant</c>, <c>MidSiteMercs</c>, <c>MidSiteTrainer</c>, <c>MidSiteMage</c>).
/// </summary>
public enum SiteKind
{
    Capital,
    Village,
    Ruin,
    Bag,
    Crystal,
    Merchant,
    Mercenaries,
    Trainer,
    Mage,

    /// <summary>Жезл (<c>CMidRod</c>). Не посещается: постановка и снос — отдельные команды
    /// (<see cref="Rods"/>), а земля растёт от него как от источника (<see cref="LandSource"/>).</summary>
    Rod,

    /// <summary>Всё остальное, что стоит на карте (надгробие, ландшафт, область).
    /// Посещение такого объекта <see cref="Sites.Visit"/> не моделирует и бросает исключение.</summary>
    Other,
}

/// <summary>
/// Объект карты в том объёме, в каком его нужно знать посещению. Это НЕ замена
/// <see cref="MapObject"/> (там только координата и тип жилы): руине нужны добыча, предмет и
/// отметка о разграблении, мешку — предметы, городу — владелец и гарнизон. Всё — прямая
/// проекция полей <c>.sg</c>, ранг 1; собирает <c>D2.Data.Map.SiteLoader</c>.
/// </summary>
public sealed record MapSite
{
    public required string Id { get; init; }
    public required SiteKind Kind { get; init; }

    /// <summary>Имя класса записи <c>.sg</c> (<c>MidRuin</c>, <c>MidSiteMage</c>, …) — попадает
    /// в текст отказа, чтобы «не поддержан» называл сам объект, а не только его вид.</summary>
    public required string RawKind { get; init; }

    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Имя объекта из редактора сценария — текст, а не id строки: город <c>City.NAME_TXT</c>, площадка
    /// <c>Site.TXT_TITLE</c>, руина <c>MidRuin.TITLE</c>. У прочих видов пусто.</summary>
    public string Name { get; init; } = "";

    /// <summary>Описание объекта: <c>City.DESC_TXT</c>, <c>Site.TXT_DESC</c>, <c>MidRuin.DESC</c>.</summary>
    public string Description { get; init; } = "";

    /// <summary>Владелец города (<c>City.OWNER</c>); у прочих видов пусто.</summary>
    public string Owner { get; init; } = "";

    /// <summary>Добыча руины (<c>MidRuin.CASH</c>).</summary>
    public Bank Cash { get; init; } = Bank.Zero;

    /// <summary>Предмет руины (<c>MidRuin.ITEM</c>), пусто — предмета нет.</summary>
    public string ItemId { get; init; } = "";

    /// <summary>
    /// Игрок, уже разграбивший руину (<c>MidRuin.LOOTER</c>). Ранг 2: это id ИГРОКА, а не отряда —
    /// тулсет читает его как игрока (<c>tools/d2probe/mss32/src/bindings/ruinview.cpp:57</c>
    /// <c>getPlayer(objectMap, &amp;ruin-&gt;looterId)</c>, поле <c>CMidRuin.looterId</c>,
    /// <c>midruin.h:46</c>). Непусто — добыча уже забрана, и забрана навсегда, кем бы то ни было.
    /// </summary>
    public string LooterId { get; init; } = "";

    /// <summary>Предметы мешка (<c>MidBag.ITEM_ID</c>) или склад города.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Охрана руины или гарнизон города — id юнитов отряда объекта
    /// (<c>Group.UNIT_0..5</c>, пустые ссылки выброшены).</summary>
    public IReadOnlyList<string> Guards { get; init; } = [];

    /// <summary>Клетки построения охраны или гарнизона (<c>Group.POS_0..5</c>): клетка → индекс в
    /// <see cref="Guards"/>, <c>-1</c> — пусто; то же устройство, что <see cref="MapStack.Slots"/>. Ранг 1 — поле.</summary>
    public IReadOnlyList<int> GuardSlots { get; init; } = [];

    /// <summary>Тип ресурса жилы, <c>Lres.dbf</c> (см. <see cref="MapObject.Resource"/>).</summary>
    public int Resource { get; init; }

    /// <summary>Кто посещал объект последним (<c>Site.VISITER</c>, <c>MidRuin.LOOTER</c> для руин
    /// отдельным полем). Пусто — не посещали.</summary>
    public string VisitorId { get; init; } = "";

    /// <summary>Объекта на карте больше нет (мешок после подбора). Ставит
    /// <see cref="Sites.AfterVisit"/>; сам файл такого поля не имеет — исчезнувший мешок движок
    /// из сценария удаляет.</summary>
    public bool Gone { get; init; }

    /// <summary>Отряд-визитёр внутри укрепления (<c>City.STACK</c>; пусто — нет). Гарнизон — это
    /// <see cref="Guards"/>, другое поле (план Ф7 §1.4, cities §1.5). Ранг 1.</summary>
    public string StackId { get; init; } = "";

    /// <summary>Подраса владельца укрепления (<c>City.SUBRACE</c>); при захвате деревни меняется вместе с
    /// <see cref="Owner"/> (план Ф7 §1.4).</summary>
    public string Subrace { get; init; } = "";

    /// <summary>Уровень деревни (<c>SIZE</c>, 1…5); у столицы и прочих видов null. Ранг 1.</summary>
    public int? Size { get; init; }

    /// <summary>Ход последнего роста деревни (<c>GROWTH_T</c>): рост не чаще раза в день (план Ф7 §1.4).</summary>
    public int? GrowthTurn { get; init; }

    /// <summary>Ход окончания бунта деревни (<c>RIOT_T</c>).</summary>
    public int? RiotTurn { get; init; }

    /// <summary>Прибавка регенерации деревни (<c>REGEN_B</c>, план Ф7 §1.4).</summary>
    public int? RegenBonus { get; init; }

    /// <summary>Прибавка защиты деревни (<c>PROTECT_B</c>; в файле строковая запись). Ранг 1 — поле,
    /// единица — ранг 2 (cities §1.2).</summary>
    public string ProtectBonus { get; init; } = "";

    /// <summary>Мораль деревни (<c>MORALE</c>) — сопротивление вору (план Ф7 §1.7).</summary>
    public int? Morale { get; init; }

    /// <summary>Расы прежних хозяев деревни (<c>P_O_UN</c>, <c>P_O_HE</c>, <c>P_O_HU</c>, <c>P_O_DW</c>, <c>P_O_EL</c>; в памяти — набор
    /// категорий рас <c>CMidVillage +0xa4</c>, проверка <c>0x5fe50d</c>): <c>RACE_TYPE</c> (0 Империя, 1 Орды, 2 Легионы, 3 Кланы,
    /// 5 Эльфы). Пополняет захват (<c>CVisitorChangePreviousOwner</c>, <c>0x433e2e</c>), читает он же: <c>OCCUPY</c> — только расе, у
    /// которой деревни ещё не было. Нет ни одной — null (в файлы порта не пишется: в ванильных сценариях флагов нет).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<int>? PreviousOwners { get; init; }

    /// <summary>Посетители объекта: <c>VISITER</c> списка сайта или <c>RUIN_ID/VISITER</c> руины, пустые
    /// выброшены. Запрещает ли список повторный вход — открыто (план Ф7 §3 п. 10).</summary>
    public IReadOnlyList<string> Visitors { get; init; } = [];

    /// <summary>Склад торговца (<c>MidSiteMerchant</c>).</summary>
    public IReadOnlyList<MerchantStock> Stock { get; init; } = [];

    /// <summary>Какие категории торговец берёт в продажу; у прочих видов null.</summary>
    public MerchantBuys? Buys { get; init; }

    /// <summary>Торговец миссии (<c>MidSiteMerchant.MISSION</c>, <c>CMidSiteMerchant +0x88</c>, чтение <c>0x601542</c>):
    /// к такому ИИ не ходит (<c>0x46af1f</c>).</summary>
    public bool Mission { get; init; }

    /// <summary>Ростер наёмника (<c>MidSiteMercs</c>).</summary>
    public IReadOnlyList<MercenaryOffer> Roster { get; init; } = [];

    /// <summary>Заклинания башни мага (<c>MidSiteMage.SPELL_ID</c>); купленное уходит из списка (sites §2.2).</summary>
    public IReadOnlyList<string> Spells { get; init; } = [];

    /// <summary>Приоритет редактора для ИИ (<c>AIPRIORITY</c> крепости, руины, мешка, кристалла и площадки — их
    /// <c>IAiPriority +4</c>, 0…6; 3 — без поправки): поправка счёта тактики к действию с этой целью
    /// (<see cref="AiTacticScore.EditorPriority"/>, <c>0x466d5c</c>). У жезла <c>IAiPriority</c> нет — 3. Ранг 1 — поле.</summary>
    public int AiPriority { get; init; } = AiTacticScore.DefaultPriority;
}

/// <summary>
/// Что дало посещение. <see cref="Blocked"/> непусто — посещения не случилось, и там сказано
/// почему (охрана, уже разграблено, свой город); остальные поля тогда пусты.
/// </summary>
/// <param name="UnitsHired">Кого наняли у наёмников. Всегда пусто: вход в сайт ничего не даёт, найм —
/// отдельное действие <see cref="SiteTrade.HireUnit"/> (задача 3 Ф7). Поле объявлено интерфейсом
/// задачи 8 плана Ф5.</param>
/// <param name="XpGained">Опыт за посещение. Всегда 0: опыт за руину даёт бой с её охраной (ядро
/// боя Ф4), опыт у тренера — покупка <see cref="SiteTrade.Train"/>.</param>
/// <param name="Captured">Укрепление взято. Для деревни это смена владельца, для столицы — гибель
/// её игрока: владелец остаётся прежним, а последствия идут по игроку
/// (<see cref="CapitalCapture.Apply"/>, план Ф7 §1.9).</param>
public sealed record VisitResult(
    Bank Gained,
    IReadOnlyList<string> ItemsGained,
    IReadOnlyList<string> UnitsHired,
    int XpGained,
    string? Blocked,
    bool Captured = false)
{
    /// <summary>Посещение состоялось и ничего не принесло.</summary>
    public static readonly VisitResult Nothing = new(Bank.Zero, [], [], 0, null);

    /// <summary>Посещения не было; <paramref name="why"/> — причина словами.</summary>
    public static VisitResult Blocks(string why) => new(Bank.Zero, [], [], 0, why);
}

/// <summary>
/// Посещение объекта карты — подмножество, нужное контрольной карте С4 (руина, мешок, жила,
/// деревня и столица, лавка, наёмники, наставник). Всё, чего в этом подмножестве нет, бросает
/// <see cref="NotSupportedException"/> с именем класса записи: приём Ф4 — молчаливая заглушка
/// испортила бы сверку.
///
/// <para><b>Контакт устанавливает вызывающий.</b> Здесь не проверяется, что отряд стоит на
/// объекте: клетку объекта знает <see cref="MapState.Plan"/>, а вход в город и руину движок
/// считает по <see cref="Entrance"/>. Задача 8 — что посещение даёт, а не когда оно случается.</para>
/// </summary>
public static class Sites
{
    /// <summary>
    /// Что даст посещение объекта. Состояние объекта после посещения — <see cref="AfterVisit"/>:
    /// эти две функции всегда идут парой, и обе чистые.
    /// </summary>
    /// <param name="rules">Справочники экономики. Подмножеству С4 они пока не нужны (ни одна цена
    /// здесь не считается — см. <see cref="VisitResult.UnitsHired"/>), но параметр объявлен
    /// интерфейсом задачи 8 плана Ф5 и понадобится найму у наёмников; проверяется на null,
    /// чтобы отсутствие справочников вскрылось сразу, а не на первой покупке.</param>
    /// <param name="battleWon">Добыча боя у руины: победитель грабит её и с павшим лидером (<c>CBattleResultRuin</c> <c>0x41b290</c> →
    /// <c>0x41b381</c> → <c>0x44133a</c> → <c>CEffectLootRuin</c> <c>0x439019</c>, лидера никто не смотрит). Журнал 20260919-human:
    /// <c>S073KC0002</c> бьёт охрану <c>S073RU0006</c>, лидер <c>S073UN01fa</c> пал (hp 0), трофей <c>G000IG0003</c> (<c>loot_ruin</c>)
    /// и 100 золота — тому же отряду.</param>
    /// <exception cref="NotSupportedException">Вид объекта в подмножество С4 не входит.</exception>
    public static VisitResult Visit(MapState map, MapStack stack, MapSite site, EconomyRules rules, bool battleWon = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(rules);

        if (site.Gone)
            return VisitResult.Blocks($"объекта {site.Id} на карте больше нет");

        // Павший лидер (LEADR_ALIV = 0): посещать можно только мешок. Ранг 1 по сохранениям ai-q55 (083–090):
        // B подобрал S143BG000f (086 → 087), а у лавки и руины — 0 site_visit и 0 battle_start; сам отказ
        // (нет курсора) — ранг 2, со слов игрока. Выход из города не запрещён (085: A вышел из столицы) —
        // это ход, а не посещение, и сюда не попадает.
        if (!stack.LeaderAlive && site.Kind != SiteKind.Bag && !(battleWon && site.Kind == SiteKind.Ruin))
            return VisitResult.Blocks($"лидер отряда {stack.Id} пал: из объектов доступен только мешок");

        switch (site.Kind)
        {
            case SiteKind.Ruin:
                // Охрана есть — добычу решает бой, а бой это Ф4 и задача 9; посещения не было.
                if (site.Guards.Count > 0)
                    return VisitResult.Blocks($"руину {site.Id} стережёт отряд из {site.Guards.Count} " +
                                              "юнитов: добыча достаётся победителю боя");
                if (site.LooterId.Length > 0)
                    return VisitResult.Blocks($"руина {site.Id} уже разграблена ({site.LooterId})");
                return new VisitResult(site.Cash, Items(site.ItemId), [], 0, null);

            case SiteKind.Bag:
                return new VisitResult(Bank.Zero, site.Items, [], 0, null);

            // Жила ресурс за посещение не даёт вовсе: она входит в ДНЕВНОЙ доход владельца земли
            // (Economy.DailyIncome, ранг 1 — computePlayerDailyIncome 0x5da55f перебирает
            // CMidCrystal и берёт GVars.CRYSTAL_P), а землю переводит жезл CMidRod. Отряд, стоящий
            // на жиле, не меняет ничего — поэтому это отказ с причиной, а не «дали ману».
            case SiteKind.Crystal:
                return VisitResult.Blocks(
                    $"жила {site.Id} платит дневным доходом владельца земли (Economy.DailyIncome), " +
                    "а не за посещение");

            // Жезл не посещают: у него две СВОИ команды (Rods.Place, Rods.Break), и снос — не шаг
            // на клетку (отряд не двигается, run2 §2.2). Сама клетка жезла к тому же непроходима
            // (Movement.BlockedByObject, класс RD), так что «встать на жезл» невозможно.
            case SiteKind.Rod:
                return VisitResult.Blocks(
                    $"жезл {site.Id} не посещается: постановка — Rods.Place, снос — Rods.Break " +
                    "(отдельная команда, отряд не двигается)");

            case SiteKind.Capital:
            case SiteKind.Village:
                if (site.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase))
                    return VisitResult.Blocks($"город {site.Id} уже принадлежит {stack.Owner}");
                if (site.Guards.Count > 0)
                    return VisitResult.Blocks($"город {site.Id} защищает гарнизон из " +
                                              $"{site.Guards.Count} юнитов: вход решается боем");
                // Взятая столица — гибель её игрока, и решает её CapitalCapture.Apply: там и ветка
                // победы, и переход казны, и снос жезлов. Здесь только «укрепление взято».
                return VisitResult.Nothing with { Captured = true };

            // Посещение фиксируется (AfterVisit ставит VISITER); покупка, продажа, найм, обучение и
            // заклинание — отдельные действия SiteTrade (задача 3 Ф7), сам вход ничего не даёт (sites §2.8).
            case SiteKind.Merchant:
            case SiteKind.Mercenaries:
            case SiteKind.Trainer:
            case SiteKind.Mage:
                return VisitResult.Nothing;

            default:
                throw new NotSupportedException(
                    $"посещение объекта {site.RawKind} ({site.Kind}, id {site.Id}) не реализовано: " +
                    "в подмножество контрольной карты С4 этот вид не входит (задача 8 плана Ф5)");
        }
    }

    /// <summary>
    /// Объект после посещения — новый экземпляр, исходный не меняется. Отказное посещение
    /// (<see cref="VisitResult.Blocked"/> непусто) объект не трогает.
    /// </summary>
    public static MapSite AfterVisit(MapSite site, MapStack stack, VisitResult result)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Blocked is not null)
            return site;

        return site.Kind switch
        {
            // Разграблённая руина остаётся на карте пустой: LOOTER — игрок (см. MapSite.LooterId).
            SiteKind.Ruin => site with
            {
                Cash = Bank.Zero, ItemId = "", LooterId = stack.Owner, VisitorId = stack.Id,
            },
            // Подобранный мешок движок из сценария убирает — отсюда Gone.
            SiteKind.Bag => site with { Items = [], VisitorId = stack.Id, Gone = true },
            // Захват ДЕРЕВНИ: OWNER, SUBRACE, гарнизон и визитёр — Fortifications.Capture (задача 5 Ф7);
            // раньше здесь менялся только OWNER, и гарнизон побеждённого оставался в записи.
            SiteKind.Village => result.Captured
                ? Fortifications.Capture(site, stack) with { VisitorId = stack.Id }
                : site,
            // Взятие СТОЛИЦЫ — не смена владельца, а гибель игрока: OWNER и SUBRACE остаются
            // погибшему, стирается только гарнизон, а помета CAPT_BY, приказ L_BEZERK его отрядам,
            // снос его жезлов и переход казны идут по игроку, а не по объекту (CapitalCapture.Apply,
            // план Ф7 §1.9, задача 14). Раньше сюда попадал Fortifications.Capture и менял OWNER
            // столицы — этого движок не делает (в CEffectCaptureCapital::apply смены владельца нет).
            SiteKind.Capital => result.Captured ? site with { Guards = [] } : site,
            // Лавка, маг, наёмники, тренер: игрок — в список посетивших (CEffectVisit → 0x5d7664: по игроку, повтор не дублирует;
            // его читает L_VISIT_SITE). Прежде список в партии не пополнялся вовсе.
            _ => site with
            {
                VisitorId = stack.Id,
                Visitors = site.Visitors.Contains(stack.Owner, StringComparer.OrdinalIgnoreCase) ? site.Visitors : [.. site.Visitors, stack.Owner],
            },
        };
    }

    /// <summary>
    /// Клетка входа в город или руину — <b>правый нижний угол</b> занимаемого прямоугольника:
    /// <c>позиция + размер − 1</c> по обеим осям.
    /// <para><b>Ранг 1</b>, прочитано в дизассемблере <c>Discipl2.exe</c> (Gog, 4 474 880):
    /// <c>getFortOrRuinEntrance</c> <c>0x4cd253</c> ищет в клетке объект типа <c>0x1d</c> (крепость)
    /// и <c>0x29</c> (руина) и зовёт <c>0x5e94eb</c> либо <c>0x5fdf70</c>; обе передают в
    /// <c>0x5e9563</c> / <c>0x5fdfa4</c> (тела совпадают инструкция в инструкцию) пару
    /// <c>объект+0xc</c> и <c>объект+0x14</c>, а те возвращают
    /// <c>(a.x + b.x − 1, a.y + b.y − 1)</c>. По раскладке <c>IMapElement</c> тулсета
    /// (<c>mss32/include/mapelement.h:33-38</c>: <c>vftable, position, sizeX, sizeY</c>, всего 20
    /// байт, внутри <c>CMidRuin</c> со смещением 8) <c>+0xc</c> — позиция, <c>+0x14</c> — размер.</para>
    /// <para>Сам размер объекта в <see cref="MapState"/> не лежит (в <c>.sg</c> он есть только в
    /// плане занятости), поэтому функция берёт его параметром и ничего не угадывает.</para>
    /// </summary>
    public static (int X, int Y) Entrance(int x, int y, int sizeX, int sizeY) =>
        (x + sizeX - 1, y + sizeY - 1);

    private static IReadOnlyList<string> Items(string id) => id.Length == 0 ? [] : [id];
}
