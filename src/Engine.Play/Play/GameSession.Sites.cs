using D2.Rules.Battle;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>
/// Услуги площадок карты, общие для человека и ИИ: обучение у тренера (сервер <c>onSiteTrainUnit</c> <c>0x5d7c6b</c>), покупки у
/// торговца и мага, найм у наёмника (<c>onSiteBuyUnit</c> <c>0x5d7aa1</c>).
/// Цены и формулы — <see cref="SiteTrade"/>; им же воспользуется окно площадки человека.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Потолок уровня юнита, не лидера (<c>0x5d8eca</c>: <c>[0x6e88d0]</c> = 99).</summary>
    private const int UnitLevelCap = 99;

    /// <summary>
    /// Почему юнит нельзя учить (<c>0x5e0372</c> = <c>0x5e023b</c> или <c>0x5e02b6</c>, ранг 1 по листингу): у хозяина нет
    /// золота (<c>≤ 0</c>, код 4); юнит не растёт — призыв, вор, знатный, иллюзия (<c>0x5d8f84</c>, код 2); хозяин нейтрал
    /// (код 6); <c>XP_NEXT</c> = 0 (код 2); опыт уже <c>XP_NEXT − 1</c> (код 3 — «курс пройден»); юнит пал (код 5); уровень на
    /// потолке — лидер на <c>MAX_LEADER</c>, прочие на 99 (<c>0x5d8eca</c>, код 2; ранг 2: у лидера движок ещё смотрит,
    /// создан ли тип следующего уровня). null — можно.
    /// </summary>
    public string? TrainRefusal(string ownerId, string unitId)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Players.TryGetValue(ownerId, out var owner)) return $"нет игрока {ownerId}";
        if (!map.Units.TryGetValue(unitId, out var unit)) return $"нет юнита {unitId}";
        if (owner.Bank.Gold <= 0) return "у хозяина нет золота";
        int? category = book.CategoryOf(unit.TypeId);
        if (category is UnitCategory.Noble or UnitCategory.Summon or UnitCategory.Illusion
            || book.LeaderCategoryOf(unit.TypeId) == ThiefLeaderCategory) return $"{unitId} не растёт опытом";
        if (AiTactics.NeutralOwner(this, ownerId)) return "хозяин — нейтрал";
        int xpNext = book.XpNextOf(unit.TypeId, unit.Level);
        if (xpNext == 0) return $"у {unitId} нет порога опыта";
        if (unit.Xp >= xpNext - 1) return "курс пройден: опыт на XP_NEXT − 1";
        if (unit.Hp <= 0) return $"{unitId} пал";
        int cap = category == UnitCategory.Leader ? map.Limits?.MaxLeader ?? int.MaxValue : UnitLevelCap;
        return unit.Level >= cap ? $"{unitId} на потолке уровня" : null;
    }

    /// <summary><c>LEADER_CAT</c> вора (<c>LleadC</c> 5, <c>0x41b399</c>).</summary>
    private const int ThiefLeaderCategory = 5;

    /// <summary>Цена одного очка обучения юнита — <c>TRAINING_C</c> (слот <c>0x60</c> реализации). ponytail: прибавки
    /// <c>GDynUpgr.TRAINING_C</c> к ней не добавляются — в ванили все 502 строки нулевые.</summary>
    public Bank? TrainingRate(string unitId) => CostsOf(unitId)?.Training;

    /// <summary>Цена воскрешения юнита — <c>REVIVE_C</c> (слот <c>0x58</c> реализации, <see cref="Treasury.RevivePrice"/>).</summary>
    public Bank? ReviveRate(string unitId) => CostsOf(unitId) is { } costs ? Treasury.RevivePrice(costs) : null;

    /// <summary>Цены типа юнита по глобальному id (порождённый тип <c>UG</c> — через разбор).</summary>
    private UnitCosts? CostsOf(string unitId)
    {
        var book = RequireBook();
        return Map.Units.TryGetValue(unitId, out var unit) && book.TryGlobal(unit.TypeId, out var id, out _)
               && book.Treasury.Units.TryGetValue(id, out var costs) ? costs : null;
    }

    /// <summary>
    /// Покупка предмета у торговца (<c>onSiteBuyItem</c> <c>0x5d76e2</c>, <see cref="SiteTrade.BuyItem"/>): цена — <see cref="ItemPrice"/>
    /// (<c>VALUE</c> со скидкой лидера), со склада уходит экземпляр, в сумку отряда — новый предмет <c>IM</c>.
    /// </summary>
    /// <returns>Причина отказа; null — куплено.</returns>
    public string? BuyItem(string stackId, string siteId, string itemType)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Merchant } site) return $"{siteId} — не торговец";
        if (!book.Items.TryGetValue(itemType, out var item)) return $"нет предмета {itemType}";
        var player = map.Players[stack.Owner];
        var trade = SiteTrade.BuyItem(site.Stock, item, player.Bank, LowerCostPercent(stack));
        if (trade.Refused is { } why) return why;
        var types = map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var ids = map.IdNext;
        string instance = NewId(ref ids, types.Keys, stackId, "IM");
        types[instance] = itemType.ToUpperInvariant();
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack with { Items = [.. stack.Items, instance] };
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = trade.Purse };
        var sites = map.Sites.Select(s => ReferenceEquals(s, site) ? s with { Stock = trade.State } : s).ToList();
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, players: players, sites: sites, itemTypes: types, idNext: ids), Looks.Stacks) };
        ChargeNewTalismans([instance]);
        Observer?.Happened(new TurnSight(TurnSightKind.Bought, player.Id, 0, 0, instance));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>
    /// Покупка заклинания в башне мага (<c>onSiteBuySpell</c> <c>0x5d7952</c>, <see cref="SiteTrade.BuySpell"/>): цена —
    /// <see cref="SpellPrice"/> (<c>BUY_C</c> со скидкой лидера, <c>0x5d7a36…0x5d7a4b</c>), заклинание уходит из списка башни и
    /// становится известным хозяину отряда.
    /// </summary>
    /// <returns>Причина отказа; null — куплено.</returns>
    public string? BuySpell(string stackId, string siteId, string spellId)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Mage } site) return $"{siteId} — не башня мага";
        if (!book.Spells.TryGetValue(spellId, out var spell)) return $"нет заклинания {spellId}";
        var player = map.Players[stack.Owner];
        var trade = SiteTrade.BuySpell(new SpellBooks(site.Spells, player.KnownSpells), spell, player.Bank, LowerCostPercent(stack));
        if (trade.Refused is { } why) return why;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = trade.Purse, KnownSpells = [.. trade.State.Known.Select(s => s.ToUpperInvariant())] };
        var sites = map.Sites.Select(s => ReferenceEquals(s, site) ? s with { Spells = trade.State.Site } : s).ToList();
        State = State with { Looks = Retold(Looks, With(map, players: players, sites: sites), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Bought, player.Id, 0, 0, spellId));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>Цена предмета для отряда (<c>0x5dfd5f</c>): <see cref="Treasury.ItemBuyPrice"/> со скидкой лидера
    /// (<see cref="SiteTrade.Discounted"/>, <see cref="LowerCostPercent"/>).</summary>
    public Bank ItemPrice(MapStack stack, D2.Rules.Magic.Item item) =>
        SiteTrade.Discounted(Treasury.ItemBuyPrice(item), LowerCostPercent(stack));

    /// <summary>Цена заклинания для отряда (<c>0x5d7952</c>, <c>0x5dfee2</c>): <c>BUY_C</c> со скидкой лидера
    /// (<see cref="SiteTrade.Discounted"/>).</summary>
    public Bank SpellPrice(MapStack stack, D2.Rules.Magic.Spell spell) =>
        SiteTrade.Discounted(Treasury.SpellBuyPrice(spell), LowerCostPercent(stack));

    /// <summary>
    /// Место для нового юнита в отряде (<c>CVisitorAddUnitToGroup::canApply</c> <c>0x6121d1</c>, у ИИ — <c>0x5e78f7</c>): клетка
    /// <paramref name="cell"/> пуста; большому — передняя клетка пустой колонки (чётная, соседняя пуста); занятые клетки с новым
    /// не больше вместимости отряда (<see cref="GroupCapacity"/>). Ранг 2: разметку группы (<c>0x6067fb</c>) и лидерство
    /// (<c>0x5f4d80</c>, слот <c>0x20</c> группы) листинг проверяет сам, здесь — те же правила, что у найма в гарнизон.
    /// </summary>
    /// <returns>Причина отказа; null — встанет.</returns>
    public string? PlaceRefusal(string stackId, string unitType, int cell)
    {
        var book = RequireBook();
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (!book.TryGlobal(unitType, out var id, out _) || !book.Rules.Units.TryGetValue(id, out var type)) return $"нет типа {unitType}";
        var cells = UnitCells.Of(stack.UnitIds, stack.Slots);
        bool big = !type.SizeSmall;
        if (cell is < 0 or >= UnitCells.CellCount || cells.Cells[cell].Length > 0) return $"клетка {cell} занята";
        if (big && (!Formation.IsFront(cell) || cells.Cells[Formation.Pair(cell)].Length > 0)) return $"{unitType} большой, колонка {cell} занята";
        return cells.Occupied + (big ? 2 : 1) > GroupCapacity(stackId) ? "в отряде нет места" : null;
    }

    /// <summary>
    /// Найм у наёмника в отряд (<c>onSiteBuyUnit</c> <c>0x5d7aa1</c>, <see cref="SiteTrade.HireUnit"/>): предложение ростера,
    /// цена — <see cref="MercenaryPrice"/>, по команде ИИ — ещё
    /// <see cref="SiteTrade.AiPrice"/> (<c>0x61eedd</c>, ранг 2: у ИИ его зовёт и проверка перед командой — та без скидки); место — клетка
    /// <paramref name="cell"/> (<see cref="PlaceRefusal"/>) или первая подходящая. Юнит приходит уровнем предложения с полным
    /// здоровьем, уникальный уходит из ростера. Им же воспользуется окно наёмника человека.
    /// </summary>
    /// <returns>Причина отказа; null — нанят.</returns>
    public string? HireMercenary(string stackId, string siteId, string unitType, int? cell = null, bool ai = false)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Mercenaries } site) return $"{siteId} — не наёмник";
        if (site.Roster.FirstOrDefault(o => Same(o.UnitType, unitType)) is not { } offer) return $"у наёмника нет {unitType}";
        if (!book.TryGlobal(offer.UnitType, out var typeId, out _) || !book.Rules.Units.TryGetValue(typeId, out var type)
            || MercenaryPrice(stack, offer) is not { } price || book.Grown(offer.UnitType, offer.Level) is not { } grown)
            return $"{unitType} нет в правилах";
        if (ai) price = SiteTrade.AiPrice(price, State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel, State.Info.Difficulty);
        var player = map.Players[stack.Owner];
        var cells = UnitCells.Of(stack.UnitIds, stack.Slots);
        bool big = !type.SizeSmall;
        var trade = SiteTrade.HireUnit(site.Roster, unitType, type.SizeSmall, SiteTrade.FreeCells(Leadership(stack), cells.Occupied),
                                       price, player.Bank);
        if (trade.Refused is { } why) return why;
        int at = cell ?? FirstFit(cells.Slots(), big);
        if (PlaceRefusal(stackId, typeId, at) is { } place) return place;

        var ids = map.IdNext;
        string id = NewId(ref ids, map.Units.Keys, stackId, "UN");
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[id] = new MapUnit { Id = id, TypeId = type.Id.ToUpperInvariant(), Level = grown.Level, Hp = grown.Type.HitPoints };
        int[] slots = cells.Slots();
        slots[at] = stack.UnitIds.Count;
        if (big) slots[at + 1] = stack.UnitIds.Count;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack with { UnitIds = [.. stack.UnitIds, id], Slots = slots };
        units = EquipOnMembership(units, stack, stacks[stackId].UnitIds) ?? units;   // знамя отряда — и нанятому (0x6147c4)
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = trade.Purse };
        var sites = map.Sites.Select(s => ReferenceEquals(s, site) ? s with { Roster = trade.State } : s).ToList();
        State = State with { Looks = Retold(Looks, With(map, units: units, stacks: stacks, players: players, sites: sites, idNext: ids), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Bought, player.Id, 0, 0, id));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>Цена воина наёмника для отряда (<c>0x5d7aa1</c>): <c>ENROLL_C</c> уровня предложения (<see cref="GameBook.HirePrice"/>) со
    /// скидкой лидера (<see cref="SiteTrade.LeaderDiscount"/>, <see cref="LowerCostPercent"/>); нет в правилах — null.</summary>
    public Bank? MercenaryPrice(MapStack stack, MercenaryOffer offer)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(offer);
        return RequireBook().HirePrice(offer.UnitType, offer.Level) is { } price
            ? SiteTrade.LeaderDiscount(price, LowerCostPercent(stack)) : null;
    }

    /// <summary>
    /// Обучение юнита отряда у тренера (<c>0x5d7c6b</c>): отказ <see cref="TrainRefusal"/>, площадка — тренер; очки и цена —
    /// <see cref="SiteTrade.Train"/> от казны хозяина (<c>0x5e0396</c>), по команде ИИ — <see cref="SiteTrade.TrainAi"/> (чит
    /// <c>0x61eb4a</c>); казна уменьшается на цену, опыт растёт на очки. Ни одного очка казна не оплатила — не ошибка у движка
    /// (<c>site_train</c> с <c>ret</c> true и без траты), здесь — причина словами.
    /// Опыт прибавляет <c>CMidUnit::addXp</c> (<c>0x5d7d8d</c> → <c>0x5e7641</c> → визитёр <c>0x6f0a84</c> → <c>0x5e9a7b</c>): у живого
    /// юнита <c>xp += очки</c>, зажато в [0, <c>XP_NEXT</c>] (слот <c>+0x38</c> типа). Человеку тренер продаёт не больше
    /// <c>XP_NEXT − 1 − опыт</c>, а чит ИИ (×150 % на сложной) доводит ровно до <c>XP_NEXT</c>: журнал 20261018b-port-synth, карта 16 —
    /// S143UN0029 15 → 95 (XP_NEXT 95), S143UN0021/0022/002e — 95, 275, 400, S143UN002a — 500.
    /// </summary>
    /// <returns>Причина отказа; null — обучен. <paramref name="paid"/> — списанное.</returns>
    public string? Train(string stackId, string siteId, string unitId, out Bank paid, bool ai = false)
    {
        paid = Bank.Zero;
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (!stack.UnitIds.Contains(unitId, StringComparer.OrdinalIgnoreCase)) return $"{unitId} не в {stackId}";
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Trainer }) return $"{siteId} — не тренер";
        if (TrainRefusal(stack.Owner, unitId) is { } why) return why;
        if (TrainingRate(unitId) is not { } rate) return $"нет цены обучения {unitId}";
        var player = map.Players[stack.Owner];
        var unit = map.Units[unitId];
        int xpNext = book.XpNextOf(unit.TypeId, unit.Level);
        var trade = ai ? SiteTrade.TrainAi(unit.Xp, xpNext, rate, player.Bank, State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel,
                                           State.Info.Difficulty)
                       : SiteTrade.Train(unit.Xp, xpNext, rate, player.Bank);
        if (trade.Refused is { } refused) return refused;
        paid = player.Bank - trade.Purse;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = unit with { Xp = trade.State };
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = trade.Purse };
        State = State with { Looks = Retold(Looks, With(map, units: units, players: players), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Trained, player.Id, 0, 0, unitId));   // зонд порта
        return null;
    }
}
