namespace LastEdict.Rules.Map;

/// <summary>
/// Действие вора — <c>Laction.dbf</c> (<c>LNobleActionCat</c>). Ранг 1: таблица плюс порядок
/// регистрации категорий в <c>LNobleActionCatTable::ctor</c> <c>0x58E786</c> (пары «имя из массива
/// <c>.data 0x7AC98C</c> → глобальная категория»): <c>0x837E50</c> Poison Party, <c>0x837E40</c>
/// Infiltrate Spy, <c>0x837E60</c> Steal Item, <c>0x837EC0</c> Assassinate, <c>0x837EE0</c>
/// Counterfeit Orders, <c>0x837EF8</c> Duel, <c>0x837E90</c> Poison City, <c>0x837E70</c> Steal
/// Spell, <c>0x837E80</c> Bribe Populace, <c>0x837F08</c> Steal Gold, <c>0x837ED0</c> Riot,
/// <c>0x837EB0</c> Steal Item (торговец), <c>0x837EA0</c> Steal Spell (башня мага), <c>0x837E30</c>
/// Visit Ruin. Подписи — <c>Gaction.NAME_TXT</c> → <c>Tglobal</c> <c>x000tg0100…0113</c>.
/// </summary>
public enum ThiefAction
{
    PoisonStack = 0,
    Spy = 1,
    StealItem = 2,
    Assassinate = 3,

    /// <summary>Counterfeit Orders. Применимо и к отряду, и к укреплению (<c>0x5D3C0C</c> и
    /// <c>0x5D3C43</c>), но только у лорда-Дипломата.</summary>
    Misfit = 4,

    Duel = 5,
    PoisonCity = 6,
    StealSpell = 7,
    Bribe = 8,
    StealGold = 9,
    StealMerchant = 10,
    StealMage = 11,
    SpyRuin = 12,
    RiotCity = 13,
}

/// <summary>Категория лорда игрока — <c>Llord.dbf</c>: 0 <c>L_MAGE</c>, 1 <c>L_WARRIOR</c>,
/// 2 <c>L_DIPLOMAT</c> (он же «Повелитель воров», «глава гильдии»). Ранг 1 — таблица;
/// в <c>.sg</c> лежит <c>Glord.CATEGORY</c> лорда игрока (<see cref="TreasuryRules.LordCategory"/>).</summary>
public enum LordCategory
{
    Mage = 0,
    Warrior = 1,
    Diplomat = 2,
}

/// <summary>
/// Вид цели вора — то, что <c>getPossibleNobleActions</c> <c>0x5D3A51</c> различает по типу id
/// объекта: <c>0x23</c> отряд, <c>0x1D</c> укрепление, <c>0x29</c> руина, <c>0x28</c> объект-лавка
/// (дальше — по категории объекта: торговец <c>[0x838248]</c> или башня мага <c>[0x838260]</c>).
/// Ранг 1 — дизассемблирование.
/// </summary>
public enum ThiefTargetKind
{
    Stack,
    Fortification,
    Ruin,
    Merchant,
    MageTower,
}

/// <summary>Один бросок вора: порог, оба кубика и итог. <see cref="Average"/> — то самое
/// <c>(d1 + d2) &gt;&gt; 1</c>, с которым сравнивается порог.</summary>
public readonly record struct ThiefRoll(int Chance, int Dice1, int Dice2)
{
    /// <summary><c>shr esi, 1</c> в <c>0x4430B3</c> — сдвиг, а не деление с округлением.</summary>
    public int Average => (Dice1 + Dice2) >> 1;

    /// <summary><c>setle</c> <c>0x4430C1</c>: успех при «среднее НЕ БОЛЬШЕ порога».</summary>
    public bool Success => Average <= Chance;
}

/// <summary>Таблицы вора: <c>Gaction.CHANCE</c> по категории действия и <c>GVars.MORALE_1…6</c>.</summary>
/// <param name="ActionChance">Категория действия (<see cref="ThiefAction"/>) → <c>CHANCE</c>.</param>
/// <param name="Morale">Мораль укрепления по уровню, индекс 0 — <c>MORALE_1</c>. Шесть значений на
/// пять уровней деревни: шестое — столица (<see cref="Thieves.CapitalMoraleIndex"/>).</param>
public sealed record ThiefRules(IReadOnlyDictionary<int, int> ActionChance, IReadOnlyList<int> Morale)
{
    /// <summary>Ванильные значения — на случай отсутствия <c>Globals</c> (синтетика тестов):
    /// <c>Gaction.CHANCE</c> 90/100/80/70/90/100/90/80/100/80/60/60/90/100 и
    /// <c>MORALE_1…6</c> = 10…60.</summary>
    public static readonly ThiefRules Vanilla = new(
        new Dictionary<int, int>
        {
            [0] = 90, [1] = 100, [2] = 80, [3] = 70, [4] = 90, [5] = 100, [6] = 90,
            [7] = 80, [8] = 100, [9] = 80, [10] = 60, [11] = 60, [12] = 90, [13] = 100,
        },
        [10, 20, 30, 40, 50, 60]);

    // Величины GVars.dbf исходов (ранг 1: таблица и чтение кода, отчёт C-thieves.md). Умолчания — ванильные.

    /// <summary><c>POISON_S</c> — % текущего HP за Poison Party (<c>0x41D36B</c>).</summary>
    public int PoisonStack { get; init; } = 20;

    /// <summary><c>POISON_C</c> — % текущего HP за Poison City (<c>0x41D3AE</c>).</summary>
    public int PoisonCity { get; init; } = 25;

    /// <summary><c>BRIBE</c> (<c>+0x5C</c>) — множитель цены подкупа (<c>0x5D3CA7</c>).</summary>
    public int Bribe { get; init; } = 3;

    /// <summary><c>STEAL_RACE</c> (<c>+0x60</c>) — % казны играбельного владельца, потолок кражи.</summary>
    public int StealRace { get; init; } = 25;

    /// <summary><c>STEAL_NEUT</c> (<c>+0x64</c>) — то же для нейтрала; для городов недостижимо (Steal Gold
    /// нейтралу не предлагается).</summary>
    public int StealNeut { get; init; } = 250;

    /// <summary><c>RIOT_MIN</c> / <c>RIOT_MAX</c> — границы длительности мятежа
    /// (<see cref="Fortifications.RiotEnd"/>).</summary>
    public int RiotMin { get; init; } = 1;

    /// <inheritdoc cref="RiotMin"/>
    public int RiotMax { get; init; } = 4;

    /// <summary><c>RIOT_DMG</c> — % текущего HP в момент конца мятежа (<see cref="Regeneration.RiotHit"/>).</summary>
    public int RiotDamage { get; init; } = 20;

    /// <summary><c>CHANCE</c> действия.</summary>
    /// <exception cref="ArgumentException">Категории нет в <c>Gaction.dbf</c>.</exception>
    public int Chance(ThiefAction action) =>
        ActionChance.TryGetValue((int)action, out int chance)
            ? chance
            : throw new ArgumentException($"в Gaction.dbf нет строки CATEGORY {(int)action}", nameof(action));
}

/// <summary>Признаки цели, которые читает фильтр доступности <c>0x5D32B8</c> (<see cref="Thieves.Available"/>).
/// Считает их вызывающий по состоянию карты; каждое поле значимо только для своего вида цели.</summary>
public sealed record ThiefTargetState(ThiefTargetKind Kind)
{
    /// <summary>Раса владельца играбельна (не нейтрал, <c>0x5E5358</c>).</summary>
    public bool OwnerPlayable { get; init; }

    /// <summary>Владелец уже разведан игроком-вором — есть в его множестве <c>CMidPlayer +0x54</c> (<c>SPY_N</c> сохранения;
    /// поиск <c>0x5E46C2</c>, разбор 24.09 — до этого читалось как «союзник»). Шпионажа тогда нет.</summary>
    public bool OwnerSpied { get; init; }

    /// <summary>Хозяева вора и цели в союзе (<c>0x5F7F5A</c> → <c>0x5F7EBD</c>): против отряда и крепости союзника
    /// действий нет вовсе.</summary>
    public bool OwnerAllied { get; init; }

    /// <summary>В отряде (у укрепления — в гарнизоне) есть юнит с HP &gt; 1 (<c>0x5D371F</c>).</summary>
    public bool HasUnitAboveOneHp { get; init; }

    /// <summary>У отряда есть лидер (<c>0x5E98AD</c>).</summary>
    public bool HasLeader { get; init; }

    /// <summary>Юнитов в отряде, у укрепления — в гарнизоне (<c>0x5F50FD</c>).</summary>
    public int UnitCount { get; init; }

    /// <summary>Предметов у отряда (<c>+0x64</c>).</summary>
    public int ItemCount { get; init; }

    /// <summary>Мятеж поднять можно (<c>0x5D377D</c>: укрепление не бунтует и проходит <c>0x5E89E3</c>, не
    /// разобрана). У отряда — про укрепление, в котором он стоит; вне укрепления — false.</summary>
    public bool RiotPossible { get; init; }

    /// <summary>Укрепление бунтует — <see cref="Fortifications.Rioting"/>.</summary>
    public bool Rioting { get; init; }

    /// <summary>В укреплении стоит отряд-визитёр (<c>+0x8C</c>).</summary>
    public bool HasVisitor { get; init; }

    /// <summary>Укрепление — деревня, не столица.</summary>
    public bool IsVillage { get; init; }

    /// <summary>У владельца есть заклинание, которое можно украсть (<c>0x5CDDE7</c>).</summary>
    public bool HasSpellToSteal { get; init; }

    /// <summary>Цена подкупа — <see cref="Thieves.BribeCost"/>.</summary>
    public int BribeCost { get; init; }

    /// <summary>Руина уже посещена игроком (<c>0x5FDA67</c>).</summary>
    public bool RuinVisited { get; init; }

    /// <summary>У лавки (торговец, башня мага) есть товар.</summary>
    public bool Stocked { get; init; }
}

/// <summary>
/// Воры — план Ф7 §1.7, задача 12. Всё ранг 1, кроме оговорённого.
///
/// <para><b>Вор — это <c>Gunits.LEADER_CAT = 5</c></b> (<c>LleadC</c> <c>L_NOBLE</c>; в ванили пять
/// типов), но <b>список действий гейтит лорд ИГРОКА, а не вор</b>: <c>getPossibleNobleActions</c>
/// <c>0x5D3A51</c> читает <c>getPlayerLordCategory</c> <c>0x5E471A</c> и при совпадении с
/// <c>[0x837778]</c> (<c>L_DIPLOMAT</c>) добавляет действия главы гильдии отдельной функцией
/// <c>0x5D3BEB</c>. Гипотеза «роль — свойство типа юнита» опровергнута (П§12.5).</para>
///
/// <para><b>Порог.</b> <c>0x5D31A1</c>: <c>chance = Gaction.CHANCE</c>; при <c>CHANCE &lt; 100</c>
/// (<c>cmp eax, 0x64; jge</c> в <c>0x5D31CA</c>) вычитается сопротивление цели — у отряда
/// <c>NEGOTIATE</c> его лидера (ветка типа <c>0x23</c>), у укрепления <c>MORALE_[SIZE]</c> (ветка
/// <c>0x1D</c>; записи деревни нет — <c>ecx = 6</c> в <c>0x5D3279</c>, то есть столица берёт
/// <c>MORALE_6</c>). Итог обрезается снизу нулём (<c>0x5D329B</c>).</para>
///
/// <para><b>Бросок.</b> <c>0x44309E…0x4430C1</c>: два вызова <c>rand(100)</c> <c>0x54851F</c>
/// <b>всегда</b>, сумма, <c>shr 1</c>, <c>setle</c> против порога. Отбраковки при пороге 100 нет:
/// оба броска делаются и там.</para>
///
/// <para><b>Гибель.</b> Провал — вор гибнет всегда и безусловно (<c>0x44314B</c> →
/// <c>0x4431B8</c> → <c>CEffectKillStack</c>, ни второй вероятности, ни проверки способности).
/// Против лидера со способностью <c>L_INCORRUPTIBLE</c> (<c>LleadA</c> 3) вор гибнет до диалога и
/// без броска (<c>0x442E85</c> → <c>0x442F00</c>).</para>
///
/// <para><b>Цена.</b> <c>0x5CE239</c>: <c>fild MOVE; fmul 0.5; ceil</c> — <c>ceil(MOVE / 2)</c> от
/// ПОЛНОГО запаса лидера, не от остатка. Списывается через <c>CEffectChangeAP</c> →
/// <c>CVisitorChangeStackMoveAllowance</c> с полом нуля (<c>0x5ECFD5</c>). Отмена стоит столько же
/// и до броска не доходит (<c>onNobleActionCancelMsg</c> <c>0x42312F</c> → <c>0x443506</c>;
/// <see cref="Cancel"/>, ai-q42: <c>thief_cancel_cost13</c> 35/0). Пустой список списывает дважды —
/// <c>thief_no_actions</c> и закрытие свитка (<see cref="NoActions"/>, 15/0: 25 → 12 → 0). При
/// выполнении списание идёт ПОСЛЕ броска и только при успехе (<c>0x443181</c>); Дуэль обработчик
/// пропускает (<c>0x44316D</c> сравнивает категорию с <c>[0x837F00]</c> — полем id глобальной
/// категории Дуэли <c>0x837EF8</c>), но платит тем же числом её результат <c>0x41D892</c>
/// (<see cref="Charged"/>). Дуэль переходит в бой.</para>
/// </summary>
public static class Thieves
{
    /// <summary><c>LleadC</c> 5 <c>L_NOBLE</c> — то же число, что
    /// <see cref="Fortifications.ThiefLeaderCategory"/>; дом константы один.</summary>
    public const int LeaderCategory = Fortifications.ThiefLeaderCategory;

    /// <summary><c>LleadA</c> 3 <c>L_INCORRUPTIBLE</c> — «неподкупный» лидер цели.</summary>
    public const int IncorruptibleAbility = 3;

    /// <summary>Индекс <c>MORALE_6</c> в <see cref="ThiefRules.Morale"/>: столица.
    /// Ранг 1 — <c>push 6; pop ecx</c> в <c>0x5D3279</c> при отсутствующей записи деревни.</summary>
    public const int CapitalMoraleIndex = 6;

    /// <summary>Граница «вычитать ли сопротивление» и диапазон кубика — один и тот же литерал
    /// <c>0x64</c> (<c>cmp eax, 0x64</c> <c>0x5D31CA</c>, <c>push 0x64</c> <c>0x44309E</c>).</summary>
    public const int Hundred = 100;

    /// <summary>
    /// Список действий вора по <b>категории лорда игрока</b> и виду цели — <c>0x5D3A51</c> плюс
    /// добавка главы гильдии <c>0x5D3BEB</c>. Порядок — по номеру действия (<c>Laction</c>): так окно движка показывает список на всех
    /// снимках прогона 13.10 (отряд 0, 1, 2, 3, 4, 5; город 1, 3, 4, 6, 7, 8, 9, 13), добавка Гильдмастера не идёт первой.
    /// <i>Было иначе:</i> «порядок добавления» — добавка впереди.
    /// <para><b>Это список окна, не доступность</b>: экран <c>0x49AC24</c> рисует его рядом с множеством
    /// <see cref="Available"/>; с журналом список окна сходится на 11 из 53 <c>noble_choose</c>
    /// (ai-q43, C-thieves.md).</para>
    /// </summary>
    /// <param name="targetPlayable">Раса цели играбельна (<c>0x5E5358</c> над <c>Grace.PLAYABLE</c>,
    /// <c>stack+0x8C</c> / <c>fort+0x84</c>). У нейтрала Spy из списка выпадает.</param>
    public static IReadOnlyList<ThiefAction> Actions(LordCategory lord, ThiefTargetKind kind,
                                                     bool targetPlayable)
    {
        var list = new List<ThiefAction>();
        if (lord == LordCategory.Diplomat) list.AddRange(GuildmasterExtras(kind));
        switch (kind)
        {
            case ThiefTargetKind.Stack:
                if (targetPlayable) list.Add(ThiefAction.Spy);
                list.Add(ThiefAction.PoisonStack);
                list.Add(ThiefAction.Duel);
                break;
            case ThiefTargetKind.Fortification:
                if (targetPlayable) list.Add(ThiefAction.Spy);
                list.Add(ThiefAction.PoisonCity);
                break;
            case ThiefTargetKind.Ruin: list.Add(ThiefAction.SpyRuin); break;
            case ThiefTargetKind.Merchant: list.Add(ThiefAction.StealMerchant); break;
            case ThiefTargetKind.MageTower: list.Add(ThiefAction.StealMage); break;
        }
        return [.. list.OrderBy(a => (int)a)];
    }

    /// <summary>Добавка лорда-Дипломата (<c>0x5D3BEB</c>): по отряду — Counterfeit Orders, Steal
    /// Item, Assassinate; по укреплению — Counterfeit Orders, Assassinate, Steal Spell, Steal Gold,
    /// Bribe Populace, Riot. Руине и лавкам глава гильдии не добавляет ничего (<c>0x5D3CA0</c>),
    /// и играбельность расы здесь не проверяется вовсе — Counterfeit Orders у него есть и против
    /// нейтрала (П§16.1). В окне; <b>доступен</b> он против нейтрала только по отряду (<see cref="Available"/>).</summary>
    private static IReadOnlyList<ThiefAction> GuildmasterExtras(ThiefTargetKind kind) => kind switch
    {
        ThiefTargetKind.Stack => [ThiefAction.Misfit, ThiefAction.StealItem, ThiefAction.Assassinate],
        ThiefTargetKind.Fortification =>
        [
            ThiefAction.Misfit, ThiefAction.Assassinate, ThiefAction.StealSpell,
            ThiefAction.StealGold, ThiefAction.Bribe, ThiefAction.RiotCity,
        ],
        _ => [],
    };

    /// <summary>
    /// Действия, <b>реально доступные</b> вору, — фильтр <c>0x5D32B8</c> (зовёт <c>0x442F41</c>). Ранг 1:
    /// дизассемблер (перечитан 17.09) и журнал синтетических карт — 53/0 <c>noble_choose</c> (C-thieves.md).
    /// Порядок — по возрастанию номера действия: фильтр кладёт их во множество <c>Set&lt;LNobleActionCat&gt;</c> (дерево, ключ — номер
    /// категории), и выбор ИИ (<c>0x45b98a</c>) перемешивает вектор, собранный обходом множества (<c>0x45bc14</c> begin,
    /// <c>0x45bc25</c> end); <c>noble_choose</c> зонда (обход того же дерева) — всегда по возрастанию (215 строк корпуса). До 19.10
    /// порт отдавал порядок добавления: у крепости [PoisonCity, Spy] вместо [Spy, PoisonCity], и перемешивание <c>rng(1)</c> = 0 давало
    /// ИИ шпионаж вместо отравления (карта 22, день 1: вор Легионов <c>S143KC0005</c> у деревни Нежити — у движка отравление дважды).
    /// <list type="bullet">
    /// <item><b>Вход</b>: отряд или укрепление союзника (<c>0x5F7F5A</c>) — пусто.</item>
    /// <item><b>Отряд</b> <c>0x5D35CB</c>: Poison Party — есть юнит с HP &gt; 1 (<c>0x5D371F</c>); Spy — владелец
    /// играбелен (<c>0x5E5358</c>) и ещё не разведан (<c>0x5E46C2</c>); Duel — есть лидер (<c>0x5E98AD</c>). Гильдмастер: Assassinate
    /// всегда; Counterfeit — юнитов больше одного (<c>0x5F50FD</c>); Steal Item — есть предметы (<c>+0x64</c>);
    /// Riot — отряд внутри укрепления (<c>+0x94</c>), где можно поднять мятеж (<c>0x5D377D</c>).</item>
    /// <item><b>Укрепление</b> <c>0x5D3856</c>: бунтует (<c>0x5D37E8</c>) — пусто. Гильдмастер: Riot при
    /// <c>0x5D377D</c>. Визитёр (<c>+0x8C</c>) — дальше ничего. Poison City — гарнизон непуст и в нём юнит с
    /// HP &gt; 1. Играбельный владелец: Spy, если не разведан; у Гильдмастера Steal Gold, Steal Spell (есть что
    /// украсть, <c>0x5CDDE7</c>), при непустом гарнизоне Assassinate (только деревня — приведение к
    /// <c>0x791A98</c>) и Counterfeit. Нейтрал: только Bribe у Гильдмастера при 0 &lt; цена ≤ золото
    /// (<c>0x5D3CA7</c>, казна <c>+0x70</c>).</item>
    /// <item><b>Руина</b> <c>0x5D3579</c> — не посещена игроком (<c>0x5FDA67</c>); <b>лавки</b> <c>0x5D33EE</c> —
    /// есть товар.</item>
    /// </list>
    /// Признаки цели считает вызывающий (<see cref="ThiefTargetState"/>).
    /// </summary>
    /// <param name="playerGold">Золото игрока-вора — для Bribe.</param>
    public static IReadOnlyList<ThiefAction> Available(LordCategory lord, ThiefTargetState target, int playerGold)
    {
        ArgumentNullException.ThrowIfNull(target);
        bool guild = lord == LordCategory.Diplomat;
        var list = new List<ThiefAction>();
        if (target.OwnerAllied && target.Kind is ThiefTargetKind.Stack or ThiefTargetKind.Fortification) return list;
        void AddIf(bool condition, ThiefAction action) { if (condition) list.Add(action); }
        switch (target.Kind)
        {
            case ThiefTargetKind.Stack:
                AddIf(target.HasUnitAboveOneHp, ThiefAction.PoisonStack);
                AddIf(target.OwnerPlayable && !target.OwnerSpied, ThiefAction.Spy);
                AddIf(target.HasLeader, ThiefAction.Duel);
                AddIf(guild, ThiefAction.Assassinate);
                AddIf(guild && target.UnitCount > 1, ThiefAction.Misfit);
                AddIf(guild && target.ItemCount > 0, ThiefAction.StealItem);
                AddIf(guild && target.RiotPossible, ThiefAction.RiotCity);
                break;
            case ThiefTargetKind.Fortification:
                if (target.Rioting) break;
                AddIf(guild && target.RiotPossible, ThiefAction.RiotCity);
                if (target.HasVisitor) break;
                bool garrison = target.UnitCount > 0;
                AddIf(garrison && target.HasUnitAboveOneHp, ThiefAction.PoisonCity);
                if (!target.OwnerPlayable)
                {
                    AddIf(guild && target.BribeCost > 0 && target.BribeCost <= playerGold, ThiefAction.Bribe);
                    break;
                }
                AddIf(!target.OwnerSpied, ThiefAction.Spy);
                if (!guild) break;
                list.Add(ThiefAction.StealGold);
                AddIf(target.HasSpellToSteal, ThiefAction.StealSpell);
                AddIf(garrison && target.IsVillage, ThiefAction.Assassinate);
                AddIf(garrison, ThiefAction.Misfit);
                break;
            case ThiefTargetKind.Ruin: AddIf(!target.RuinVisited, ThiefAction.SpyRuin); break;
            case ThiefTargetKind.Merchant: AddIf(target.Stocked, ThiefAction.StealMerchant); break;
            case ThiefTargetKind.MageTower: AddIf(target.Stocked, ThiefAction.StealMage); break;
        }
        list.Sort();
        return list;
    }

    /// <summary>Доступно ли действие этому лорду против этой цели — по <see cref="Available"/>.</summary>
    public static bool Allowed(LordCategory lord, ThiefTargetState target, int playerGold, ThiefAction action) =>
        Available(lord, target, playerGold).Contains(action);

    /// <summary>Сколько раз списывается цена при пустом списке: <c>thief_no_actions</c> и затем закрытие
    /// свитка <c>thief_cancel</c> (ai-q42 <c>empty_list_then_cancel</c> 15/0, ai-q22: 23 → 10 → 0).</summary>
    public const int NoActionsCharges = 2;

    /// <summary>Цена отмены — та же <c>ceil(MOVE / 2)</c> от полного запаса (<c>ap_effect cause
    /// thief_cancel</c> delta 13 при MOVE 25, 35/0).</summary>
    public static int CancelCost(int moveAllowance) => MoveCost(moveAllowance);

    /// <summary>Остаток после отмены действия (броска нет, генератор не тратится).</summary>
    public static int Cancel(int moveAllowance, int moveLeft) => Spend(moveLeft, CancelCost(moveAllowance));

    /// <summary>Остаток после пустого списка и закрытия свитка — два списания по <see cref="CancelCost"/>,
    /// каждое с полом нуля: 25 → 12 → 0.</summary>
    public static int NoActions(int moveAllowance, int moveLeft) =>
        Cancel(moveAllowance, Cancel(moveAllowance, moveLeft));

    /// <summary>
    /// Порог действия: <c>CHANCE</c>, а при <c>CHANCE &lt; 100</c> минус сопротивление цели, с полом
    /// нуля. Это ровно то число, которое игра показывает в окне вора процентами.
    /// </summary>
    /// <param name="resistance"><see cref="StackResistance"/> или <see cref="CityResistance"/>.</param>
    public static int Chance(ThiefRules rules, ThiefAction action, int resistance)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int chance = rules.Chance(action);
        return chance >= Hundred ? chance : Math.Max(chance - resistance, 0);
    }

    /// <summary>Сопротивление отряда — <c>NEGOTIATE</c> его лидера (выращенный по уровню:
    /// <c>GDynUpgr.NEGOTIATE</c> прибавляет по 5, <see cref="Units.UnitGrowth.Of"/>).</summary>
    public static int StackResistance(int leaderNegotiate) => leaderNegotiate;

    /// <summary>Сопротивление укрепления — <c>MORALE_[SIZE]</c>; у столицы записи деревни нет и
    /// берётся <c>MORALE_6</c>.</summary>
    /// <param name="villageSize"><c>City.SIZE</c> деревни (1…5) или null для столицы.</param>
    /// <exception cref="ArgumentException">В <c>GVars</c> нет столбца нужного уровня.</exception>
    public static int CityResistance(ThiefRules rules, int? villageSize)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int index = villageSize ?? CapitalMoraleIndex;
        if (index < 1 || index > rules.Morale.Count)
            throw new ArgumentException($"в GVars нет MORALE_{index}", nameof(villageSize));
        return rules.Morale[index - 1];
    }

    /// <summary>Сопротивление укрепления по записи <c>.sg</c>: столица — <c>MORALE_6</c>,
    /// деревня — по её <c>SIZE</c>. Имя отличается от <see cref="CityResistance(ThiefRules, int?)"/>
    /// нарочно: перегрузка по <c>int?</c> и по записи делала вызов с литералом <c>null</c>
    /// неоднозначным.</summary>
    public static int FortResistance(ThiefRules rules, MapSite fort)
    {
        ArgumentNullException.ThrowIfNull(fort);
        return CityResistance(rules, fort.Kind == SiteKind.Capital ? null : fort.Size ?? 1);
    }

    /// <summary>
    /// Бросок: два <c>rand(100)</c> <b>всегда</b>, даже при пороге 100 — генератор тратит ровно два
    /// шага на каждое действие (сверка <c>thief_two_draws</c>: 28 строк корпуса, <c>rng_after −
    /// rng_before = 2</c> во всех).
    /// </summary>
    public static ThiefRoll Roll(Rng.IRng rng, int chance)
    {
        ArgumentNullException.ThrowIfNull(rng);
        int first = rng.Next(Hundred);
        int second = rng.Next(Hundred);
        return new ThiefRoll(chance, first, second);
    }

    /// <summary>Гибнет ли вор ещё до диалога: у лидера цели есть <c>L_INCORRUPTIBLE</c>
    /// (<c>0x442E85</c>). Тогда броска нет вовсе, и генератор не тратится.</summary>
    public static bool DiesBeforeDialog(IEnumerable<int> targetLeaderAbilities)
    {
        ArgumentNullException.ThrowIfNull(targetLeaderAbilities);
        return targetLeaderAbilities.Contains(IncorruptibleAbility);
    }

    /// <summary>Цена действия в очках хода — <c>ceil(MOVE / 2)</c> от полного запаса лидера.
    /// <para>Дом величины один на всю карту: задача 26 Ф7 нашла, что ту же функцию движка
    /// (<c>0x5CE239</c>) зовёт и атака отряда, поэтому число живёт в
    /// <see cref="Movement.HalfAllowance"/>, а здесь остаётся имя действия вора.</para></summary>
    public static int MoveCost(int moveAllowance) => Movement.HalfAllowance(moveAllowance);

    /// <summary>
    /// Списывает ли действие очки хода — <b>любое успешное, Дуэль тоже; провал — нет</b>. Ранг 1, листинг
    /// обработчика действия вора <c>0x443130</c>–<c>0x4431c9</c> (capstone, 16.09.2026):
    /// <list type="bullet">
    /// <item><c>0x44314b</c>: флаг успеха <c>[ebp+0x20]</c> = 0 → <c>0x4431b8</c> (<c>0x4413bc</c>) — ветка провала без
    /// <c>CEffectChangeAP</c>;</item>
    /// <item>успех: результат действия строит свои эффекты (<c>0x44315e</c>, слот <c>+0xc</c>), затем обработчик
    /// сам списывает <c>ceil(MOVE/2)</c> (<c>0x443181</c> → <c>CEffectChangeAP</c> <c>0x4431a0</c>) — кроме Дуэли
    /// (<c>0x44316d</c>, категория <c>[0x837f00]</c>);</item>
    /// <item>Дуэль платит тем же числом из своего результата <c>CNobleActionResultDuel</c> <c>0x41d892</c>:
    /// <c>0x41d8b6</c> <c>0x5ce239</c> → <c>0x41d8c5</c> <c>CEffectChangeAP</c>, следом <c>CEffectBattle</c>.</item>
    /// </list>
    /// Журнал: <c>20260915-passive</c> <c>ap_effect</c> seq 5309788 (<c>cause duel</c>, delta 13 = ceil(25/2)),
    /// <c>ap_apply</c> seq 5309792 (7 → 0), T08 п. 56. Прежнее «Дуэль не списывает» смотрело только на <c>0x44316d</c>.
    /// </summary>
    public static bool Charged(bool success) => success;

    /// <summary>Остаток очков хода после списания — с полом нуля (<c>0x5ECFD5</c>,
    /// <see cref="Movement.Spend"/>). Тем же числом платится отмена действия.</summary>
    public static int Spend(int moveLeft, int cost) => Movement.Spend(moveLeft, cost);

    /// <summary>Урон отравления — <c>min(⌊hp × pct / 100⌋, hp − 1)</c> от ТЕКУЩЕГО HP (<c>0x41D3AE</c>;
    /// ai-q12 150 → 113 при <c>POISON_C</c> 25, <c>poison_pct_of_hp</c> 6/0). Poison City бьёт и гарнизон, и
    /// визитёра (<c>0x41D566</c>); Poison Party — та же функция с <c>POISON_S</c> (<c>0x41D36B</c>). <i>Было
    /// иначе:</i> «<c>POISON_S</c> от максимума HP» (план §1.7, ранг 2) — опровергнуто.</summary>
    public static int PoisonDamage(int hp, int percent) => Math.Max(Math.Min(hp * percent / 100, hp - 1), 0);

    /// <summary>Потолок кражи золота X = казна владельца × (<c>STEAL_RACE</c> или <c>STEAL_NEUT</c>) / 100
    /// (<c>0x41DDA5</c>; ai-q12: 1000 × 25 / 100 = 250).</summary>
    public static int StealGoldMax(ThiefRules rules, int ownerGold, bool ownerPlayable)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return ownerGold * (ownerPlayable ? rules.StealRace : rules.StealNeut) / 100;
    }

    /// <summary>Сумма кражи — <c>rand(X) + 1</c> генератором движка <c>0x54851F</c> (R250). Бросок делается
    /// и при провале действия (ai-q12: провал — <c>value</c> 18, успех — 180; <c>steal_value_in_range</c> 2/0).</summary>
    public static int StealGold(Rng.IRng rng, int max)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Next(max) + 1;
    }

    /// <summary>Цена подкупа — <c>SIZE × BRIBE × Σ</c> поля юнита (vtable <c>+0x3C</c>, по числам —
    /// <c>XP_KILLED</c>) гарнизона и визитёра (<c>0x5D3CA7</c>; ai-q12: 1 × 3 × (30 + 30) = 180, Skorlus
    /// 2 × 3 × 635 = 3810; <c>bribe_cost</c> 2/0).</summary>
    public static int BribeCost(ThiefRules rules, int size, int xpKilledSum)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return size * rules.Bribe * xpKilledSum;
    }

    /// <summary>Исход подкупа — захват укрепления вором <b>без проверки лидера</b> (<c>fort_capture</c>
    /// <c>skip_leader_check</c> true, ai-q12): гарнизон стёрт, вор — визитёр. Золото списывает
    /// <see cref="Treasury"/>; отношения подкуп не трогает.</summary>
    public static MapSite Bribe(MapSite fort, MapStack thief) => Fortifications.Capture(fort, thief);

    /// <summary>
    /// Исход одного действия вора: порог, бросок, списание очков и судьба вора. Казну, HP цели,
    /// бунт и штраф отношений эта функция не двигает — они приходят своими подсистемами
    /// (<see cref="Treasury"/>; штраф <c>D_NOBLE</c> за успешное действие по отряду или укреплению
    /// играбельной расы — <see cref="Diplomacy.NobleAction"/>, задача 13); величины — <see cref="PoisonDamage"/>,
    /// <see cref="StealGoldMax"/>/<see cref="StealGold"/>, <see cref="BribeCost"/>/<see cref="Bribe"/>,
    /// мятеж — <see cref="Fortifications.RiotEnd"/>.
    /// </summary>
    /// <param name="moveAllowance">ПОЛНЫЙ запас хода лидера-вора на день (не остаток).</param>
    /// <param name="moveLeft">Остаток очков хода вора до действия.</param>
    public static ThiefOutcome Perform(ThiefRules rules, Rng.IRng rng, ThiefAction action, int chance,
                                       int moveAllowance, int moveLeft)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var roll = Roll(rng, chance);
        int cost = Charged(roll.Success) ? MoveCost(moveAllowance) : 0;
        return new ThiefOutcome(roll, Spend(moveLeft, cost), cost, ThiefDies: !roll.Success);
    }
}

/// <summary>Что осталось после действия вора: бросок, остаток очков, списанная цена и гибель.</summary>
public readonly record struct ThiefOutcome(ThiefRoll Roll, int MoveLeft, int Cost, bool ThiefDies);
