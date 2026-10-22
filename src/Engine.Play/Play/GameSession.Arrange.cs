using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Действия окон человека: перестановка юнитов внутри одной группы — перетаскивание в окне армии (между группами —
/// <see cref="SwapUnits"/>), вывод героя из укрепления.</summary>
public sealed partial class GameSession
{
    /// <summary>Куда выходит отряд — сдвиги от входа укрепления по порядку (таблица <c>0x6ee7e0</c>).</summary>
    private static readonly (int Dx, int Dy)[] ExitCells = [(1, 1), (0, 1), (1, 0), (-1, 1), (1, -1)];

    /// <summary>
    /// «Вывести героя из города» (<c>BTN_EXIT_CITY</c>/<c>BTN_EXIT_CAPITAL</c> → <c>0x406e8e</c>): от входа укрепления E
    /// (правый нижний угол пятна, <see cref="PathFinder.EntranceOf"/>) перебираются клетки E+(1,1), (0,1), (1,0), (−1,1), (1,−1);
    /// отряд встаёт на первую, куда можно поставить отряд (<see cref="PathFinder.CellCost"/> — земля и занятость), и перестаёт
    /// быть гостем. Движок шлёт обычный ход по пути [E, клетка]; выход из ворот и первый шаг за ними бесплатны
    /// (<c>docs/map-path.md</c> §1), поэтому очки хода не тратятся. Ответ пользователя 25.09: отряд встаёт на клетку у входа.
    /// ponytail: ранг 2 — «есть путь от E» (поиск <c>0x5cc6da</c>) не проверяется: соседняя проходимая клетка достижима
    /// всегда; серверная проверка хода (нет очков хода) не разобрана.
    /// </summary>
    /// <returns>Причина отказа; null — отряд вышел.</returns>
    public string? LeaveFort(string stackId)
    {
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack) || stack.Inside.Length == 0) return $"{stackId} не в укреплении";
        if (PathFinder.EntranceOf(map, stack.Inside) is not { } gate) return $"у {stack.Inside} нет входа";
        var opt = MoveOptionsOf(stack);
        foreach (var (dx, dy) in ExitCells)
        {
            int x = gate.X + dx, y = gate.Y + dy;
            if (!map.Contains(x, y) || PathFinder.CellCost(map, stack, x, y, opt, MovementRules.Vanilla) == Movement.Forbidden) continue;
            MoveStack(stackId, x, y, PathFinder.Facing(new PathPoint(gate.X, gate.Y, 0, 0), new PathPoint(x, y, 0, 0)), 0);
            LeftFort(stackId);
            return null;
        }
        return "у входа нет свободной клетки";
    }

    /// <summary>
    /// Можно ли отпустить воина группы (<c>0x5df266</c>, общая для окна и сервера): 4 — воина нет в группе, 2 — страж
    /// (<c>X005TA0450</c>), 5 — лидер, а в отряде есть ещё воины, 6 — лидер вне укрепления. 0 — можно.
    /// ponytail: пробная проверка удаления из группы (код 1) и «герой нужен заданию» (<c>X005TA0883</c>) не делаются.
    /// </summary>
    public int DismissCheck(string groupId, string unitId)
    {
        if (GroupCells(groupId) is not { } cells || !cells.Units.Contains(unitId, StringComparer.OrdinalIgnoreCase)) return 4;
        if (Map.Units.TryGetValue(unitId, out var unit) && RequireBook().CategoryOf(unit.TypeId) == LastEdict.Rules.Battle.UnitCategory.Guardian) return 2;
        if (!Map.Stacks.TryGetValue(groupId, out var stack) || !Same(stack.LeaderId, unitId)) return 0;
        if (stack.UnitIds.Count > 1) return 5;
        return stack.Inside.Length == 0 ? 6 : 0;
    }

    /// <summary>
    /// Отпустить воина (<c>CStackDismissUnitMsg</c> → <c>0x5d5773</c>): воин уходит из группы вместе с опытом. Отпуск лидера
    /// (<c>CStackDismissLeaderMsg</c> → <c>0x5d6ac1</c>) распускает отряд: все его предметы, и надетые, уходят на склад
    /// укрепления, отряд снимается с карты.
    /// </summary>
    /// <returns>Код отказа <see cref="DismissCheck"/>; 0 — отпущен.</returns>
    public int DismissUnit(string groupId, string unitId)
    {
        int check = DismissCheck(groupId, unitId);
        if (check != 0) return check;
        var map = Map;
        string owner = map.Stacks.TryGetValue(groupId, out var group) ? group.Owner
            : map.Sites.FirstOrDefault(s => Same(s.Id, groupId))?.Owner ?? "";
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units.Remove(unitId);
        if (map.Stacks.TryGetValue(groupId, out var stack) && Same(stack.LeaderId, unitId))
        {
            var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            stacks.Remove(stack.Id);
            var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
            if (plan.TryGetValue((stack.X, stack.Y), out var cell)) plan[(stack.X, stack.Y)] = [.. cell.Where(i => !Same(i, stack.Id))];
            var sites = map.Sites.Select(s => Same(s.Id, stack.Inside)
                ? s with { StackId = "", Items = [.. s.Items, .. stack.Items.Where(i => !s.Items.Contains(i, StringComparer.OrdinalIgnoreCase))] }
                : s).ToList();
            var next = With(map, stacks: stacks, units: units, plan: plan, sites: sites);
            State = State with
            {
                Looks = Retold(Looks, next, [.. Looks.Stacks.Where(l => !Same(l.Id, stack.Id))]) with { Forts = FortLooks(Looks.Forts, next) },
            };
            Observer?.Happened(new TurnSight(TurnSightKind.Dismissed, owner, 0, 0, unitId));   // зонд порта
            return 0;
        }
        var cells = GroupCells(groupId)!;
        var left = new UnitCells([.. cells.Units.Where(u => !Same(u, unitId))], [.. cells.Cells.Select(c => Same(c, unitId) ? "" : c)]);
        SetCells(groupId, left);
        State = State with { Looks = Retold(Looks, With(Map, units: units), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Dismissed, owner, 0, 0, unitId));   // зонд порта
        return 0;
    }

    /// <summary>
    /// Перенести предмет между сумкой отряда и складом укрепления (<c>CStackExchangeItemMsg</c> → <c>0x5d752e</c>): надетый в
    /// отряде сначала снимается (<c>0x5e746d</c>), затем обмен <c>CVisitorExchangeItem</c> (<c>0x610bfb</c>: оба владельца есть,
    /// предмет есть в источнике и нет в приёмнике, источник ≠ приёмник). Владелец — отряд или укрепление по id.
    /// </summary>
    /// <returns>Причина отказа; null — перенесён.</returns>
    public string? ExchangeItem(string fromOwner, string toOwner, string itemId)
    {
        if (Same(fromOwner, toOwner)) return "источник и приёмник совпадают";
        if (ItemsOf(fromOwner) is not { } from || ItemsOf(toOwner) is not { } to) return "владельца нет";
        if (!from.Contains(itemId, StringComparer.OrdinalIgnoreCase)) return "предмета нет у источника";
        if (to.Contains(itemId, StringComparer.OrdinalIgnoreCase)) return "предмет уже у приёмника";
        if (Map.Stacks.TryGetValue(fromOwner, out var worn) && Equipment.Slots(worn).ToList().FindIndex(i => Same(i, itemId)) is var slot and >= 0)
            UnequipItem(fromOwner, slot);
        var map = Map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var sites = map.Sites.ToList();
        Move(fromOwner, items => [.. items.Where(i => !Same(i, itemId))]);
        Move(toOwner, items => [.. items, itemId]);
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, sites: sites), Looks.Stacks) };
        return null;

        void Move(string owner, Func<IReadOnlyList<string>, IReadOnlyList<string>> change)
        {
            if (stacks.TryGetValue(owner, out var s)) { stacks[owner] = s with { Items = change(s.Items) }; return; }
            int at = sites.FindIndex(x => Same(x.Id, owner));
            sites[at] = sites[at] with { Items = change(sites[at].Items) };
        }
    }

    /// <summary>
    /// Продажа предмета торговцу (<c>onSiteSellItem</c> <c>0x5d77f5</c>, <see cref="SiteTrade.SellItem"/>): в казну — <c>VALUE ×
    /// SELL_RATIO / 100</c>, предмет исчезает (склад торговца не меняется), особые (<c>L_SPECIAL</c>) не покупаются; надетый
    /// сначала снимается.
    /// </summary>
    /// <returns>Причина отказа; null — продано.</returns>
    public string? SellItem(string stackId, string siteId, string itemId)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Merchant } site) return $"{siteId} — не торговец";
        if (!stack.Items.Contains(itemId, StringComparer.OrdinalIgnoreCase) || ItemOf(itemId) is not { } item) return "предмета нет в сумке";
        var player = map.Players[stack.Owner];
        var trade = SiteTrade.SellItem(site.Stock, item, book.Economy.SellRatio, player.Bank,
                                       map.TalismanCharges.TryGetValue(itemId, out int charges) ? charges : null);
        if (trade.Refused is { } why) return why;
        if (Equipment.Slots(stack).ToList().FindIndex(i => Same(i, itemId)) is var slot and >= 0) UnequipItem(stackId, slot);
        map = Map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stacks[stackId] with { Items = [.. stacks[stackId].Items.Where(i => !Same(i, itemId))] };
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[player.Id] = player with { Bank = trade.Purse };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, players: players), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Sold, player.Id, site.X, site.Y, item.Id));   // зонд порта
        return null;
    }

    /// <summary>Предметы владельца: сумка отряда, склад укрепления или мешок на земле; нет такого — null.</summary>
    public IReadOnlyList<string>? ItemsOf(string owner) =>
        Map.Stacks.TryGetValue(owner, out var stack) ? stack.Items
        : Map.Sites.FirstOrDefault(s => Same(s.Id, owner)) is { Kind: SiteKind.Capital or SiteKind.Village or SiteKind.Bag, Gone: false } site
            ? site.Items : null;

    /// <summary>
    /// Годится ли клетка для «Бросить предмет» (<c>CTaskPickupDrop</c> <c>0x4cefa3</c>, проверка клиента <c>0x4cf21e</c>): рядом с отрядом
    /// (|dx|, |dy| ≤ 1); объекты клетки по очереди — мешок сразу годится (класть в него, <paramref name="bagId"/>), дорога
    /// пропускается, любой другой объект клетку отвергает; клетка без объектов — у отряда есть предметы и почва равнина, вода или лес
    /// (<c>CVisitorCreateBag::canApply</c> <c>0x610402</c>).
    /// </summary>
    /// <returns>Причина отказа или null.</returns>
    public string? DropCheck(string stackId, int x, int y, out string bagId)
    {
        bagId = "";
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (stack.Inside.Length > 0) return "отряд в укреплении";
        if (Math.Abs(x - stack.X) > 1 || Math.Abs(y - stack.Y) > 1 || (x == stack.X && y == stack.Y)) return "клетка не рядом с отрядом";
        if (!map.Contains(x, y)) return "клетка вне карты";
        foreach (string id in map.Plan.TryGetValue((x, y), out var ids) ? ids : [])
        {
            if (id.Length >= 6 && id.AsSpan(4, 2).Equals("RA", StringComparison.OrdinalIgnoreCase)) continue;   // дорога (0x22)
            if (map.Sites.FirstOrDefault(s => Same(s.Id, id)) is { Kind: SiteKind.Bag, Gone: false } bag)
            {
                bagId = bag.Id;
                return null;
            }
            return "клетка занята";
        }
        if (stack.Items.Count == 0) return "у отряда нет предметов";
        return map.At(x, y).Ground is Ground.Plain or Ground.Water or Ground.Forest ? null : $"мешок не лежит на такой земле ({map.At(x, y).Ground})";
    }

    /// <summary>
    /// Годится ли клетка для обмена (<c>BTN_EXCHANGE</c>, задача выбора <c>CExchangeInterf</c> <c>0x492cee</c>): на ней свой отряд, не этот,
    /// рядом (|dx|, |dy| ≤ 1), оба не в укреплении (<c>0x48df76</c>).
    /// </summary>
    /// <returns>Причина отказа или null; <paramref name="otherId"/> — отряд клетки.</returns>
    public string? ExchangeCheck(string stackId, int x, int y, out string otherId)
    {
        otherId = "";
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (stack.Inside.Length > 0) return "отряд в укреплении";
        if (Math.Abs(x - stack.X) > 1 || Math.Abs(y - stack.Y) > 1 || (x == stack.X && y == stack.Y)) return "клетка не рядом с отрядом";
        if (map.Stacks.Values.FirstOrDefault(s => s.X == x && s.Y == y && s.Inside.Length == 0) is not { } other) return "на клетке нет отряда";
        if (!Same(other.Owner, stack.Owner)) return "отряд чужой";
        otherId = other.Id;
        return null;
    }

    /// <summary>
    /// Обмен воинов рукой человека (<c>CStackSwapUnitMsg</c>: окна обмена и армии) — <see cref="SwapUnits"/>; правило павшего лидера
    /// (окно обмена при показе говорит <c>X005TA0212</c>) — в общем <c>TrySwap</c>, для ИИ тоже. Лидер в чужой отряд не уходит: сервер бы
    /// пустил одного (<c>0x614963</c>), но окно броска его не отдаёт (клиент <c>0x5df4fb</c>, код 3; страж — код 2) — как и здесь.
    /// </summary>
    /// <returns>Причина отказа; null — обменяно.</returns>
    public string? ExchangeUnits(string fromGroup, int fromPos, string toGroup, int toPos) => SwapUnits(fromGroup, fromPos, toGroup, toPos);

    /// <summary>Можно ли обменять воинов рукой человека (<see cref="ExchangeUnits"/>) — без обмена: курсор броска в окнах.</summary>
    /// <returns>Причина отказа; null — можно.</returns>
    public string? ExchangeRefusal(string fromGroup, int fromPos, string toGroup, int toPos) => SwapRefusal(fromGroup, fromPos, toGroup, toPos);

    private bool LeaderDead(string groupId) =>
        Map.Stacks.TryGetValue(groupId, out var stack) && (!Map.Units.TryGetValue(stack.LeaderId, out var leader) || leader.Hp <= 0);

    /// <summary>Есть ли с кем меняться: рядом свой отряд не в укреплении (кнопка <c>BTN_EXCHANGE</c> панели, <c>0x48df76</c>).</summary>
    public bool CanExchange(string stackId) =>
        Map.Stacks.TryGetValue(stackId, out var stack)
        && Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => (dx, dy)))
                     .Any(d => ExchangeCheck(stackId, stack.X + d.dx, stack.Y + d.dy, out _) is null);

    /// <summary>
    /// «Бросить предмет» (<c>CDropBagMsg</c> → <c>CEffectDropBag</c> <c>0x4374ec</c>): годная клетка (<see cref="DropCheck"/>) с мешком —
    /// он и открывается, без мешка — появляется пустой мешок (<c>MidBag</c>, <c>0x5d7022</c>); предметы в него кладёт окно мешка
    /// (<see cref="ExchangeItem"/>). ponytail: картинка нового мешка — первая.
    /// </summary>
    /// <returns>Причина отказа или null; <paramref name="bagId"/> — мешок клетки.</returns>
    public string? DropBag(string stackId, int x, int y, out string bagId)
    {
        if (DropCheck(stackId, x, y, out bagId) is { } refused) return refused;
        Unhide(stackId);   // невидимый показывается до CEffectDropBag (0x441575 → CEffectShow с 1, 0x4415c8), и у мешка клетки
        string owner = Map.Stacks[stackId].Owner;
        if (bagId.Length > 0)
        {
            Observer?.Happened(new TurnSight(TurnSightKind.BagDropped, owner, x, y, bagId));   // зонд порта
            RunActionEvents(stackId);   // DropBag 0x41f257
            return null;
        }
        var map = Map;
        var ids = map.IdNext;
        bagId = NewId(ref ids, map.Sites.Select(s => s.Id).Concat(map.Plan.Values.SelectMany(v => v)), stackId, "BG");
        var bag = new MapSite { Id = bagId, Kind = SiteKind.Bag, RawKind = "MidBag", X = x, Y = y };
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[(x, y)] = [.. plan.GetValueOrDefault((x, y)) ?? [], bagId];
        var next = With(map, sites: [.. map.Sites, bag], plan: plan, idNext: ids);
        State = State with { Looks = Retold(Looks, next, Looks.Stacks) with { Bags = [.. Looks.Bags, new LastEdict.Data.Map.BagLook(bagId, x, y, 0)] } };
        Observer?.Happened(new TurnSight(TurnSightKind.BagDropped, owner, x, y, bagId));   // зонд порта
        RunActionEvents(stackId);   // DropBag 0x41f257
        return null;
    }

    /// <summary>Мешок закрыт (<c>CCloseBagMsg</c>): пустой убирается с карты, как подобранный.</summary>
    public void CloseBag(string bagId)
    {
        var map = Map;
        if (map.Sites.FirstOrDefault(s => Same(s.Id, bagId)) is not { Kind: SiteKind.Bag } bag || bag.Items.Count > 0) return;
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        if (plan.TryGetValue((bag.X, bag.Y), out var cell)) plan[(bag.X, bag.Y)] = [.. cell.Where(i => !Same(i, bagId))];
        var next = With(map, sites: [.. map.Sites.Where(s => !Same(s.Id, bagId))], plan: plan);
        State = State with { Looks = Retold(Looks, next, Looks.Stacks) with { Bags = [.. Looks.Bags.Where(b => !Same(b.Id, bagId))] } };
    }

    /// <summary>Отряд, вышедший из укрепления (ход человека по карте, вывод героя), перестаёт быть его гостем.
    /// <see cref="MoveStack"/> снимает только отметку отряда «внутри», а гостя у укрепления оставляет.</summary>
    public void LeftFort(string stackId)
    {
        var map = Map;
        if (map.Stacks.TryGetValue(stackId, out var stack) && stack.Inside.Length > 0) return;
        var site = map.Sites.FirstOrDefault(s => s.StackId.Equals(stackId, StringComparison.OrdinalIgnoreCase));
        if (site is null) return;
        var sites = map.Sites.Select(s => ReferenceEquals(s, site) ? s with { StackId = "" } : s).ToList();
        var next = With(map, sites: sites);
        State = State with { Looks = Retold(Looks, next, Looks.Stacks) with { Forts = FortLooks(Looks.Forts, next) } };
    }

    /// <summary>
    /// Юнит клетки <paramref name="fromPos"/> меняется местами с содержимым клетки <paramref name="toPos"/> той же группы.
    /// Большой юнит (свой или на месте назначения) переносит колонку целиком: обе позиции сводятся к передним клеткам колонок, как у
    /// обмена между группами (<see cref="GroupArrange.Swap"/>, <c>CVisitorSwapUnitPosition</c> <c>0x61111f</c> → <c>0x6116ba</c>; та же
    /// группа — та же проверка, журнал human: в своей группе <c>swap_check</c> 0 у всех шести клеток). Порядок юнитов группы не меняется.
    /// </summary>
    /// <returns>Причина отказа; null — переставлено.</returns>
    public string? MoveInGroup(string groupId, int fromPos, int toPos)
    {
        if (GroupCells(groupId) is not { } cells) return "группы нет";
        if (Within(cells, fromPos, toPos) is not { } moved) return "клетки не годятся";
        SetCells(groupId, moved);
        return null;
    }

    /// <summary>Клетки после переноса внутри группы; null — переносить нечего (пусто, та же клетка или колонка).</summary>
    public static UnitCells? Within(UnitCells cells, int fromPos, int toPos)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (fromPos is < 0 or >= UnitCells.CellCount || toPos is < 0 or >= UnitCells.CellCount) return null;
        string u = cells.Cells[fromPos], o = cells.Cells[toPos];
        if (u.Length == 0 || fromPos == toPos) return null;
        bool column = Big(cells, u) || (o.Length > 0 && Big(cells, o));
        if (column) (fromPos, toPos) = (Formation.ColumnFront(fromPos), Formation.ColumnFront(toPos));
        if (fromPos == toPos) return null;
        var next = cells.Cells.ToArray();
        int[] from = column ? [fromPos, Formation.Pair(fromPos)] : [fromPos];
        int[] to = column ? [toPos, Formation.Pair(toPos)] : [toPos];
        for (int i = 0; i < from.Length; i++) (next[from[i]], next[to[i]]) = (next[to[i]], next[from[i]]);
        return cells with { Cells = next };
    }

    /// <summary>
    /// Можно ли остановить продвижение воина (<c>TOG_DYNLEVEL</c>, проверка <c>0x5de8ba</c>, у сервера та же — <c>0x612c37</c>): 1 —
    /// воина нет, 0x13 — уже остановлено (<c>X005TA0939</c>), 0x12 — герой (<c>X005TA0936</c>), 0x11 — страж (<c>X005TA0937</c>). 0 — можно.
    /// </summary>
    public int DynLevelCheck(string unitId)
    {
        if (!Map.Units.TryGetValue(unitId, out var unit)) return 1;
        if (unit.DynLevel) return 0x13;
        var book = RequireBook();
        if (book.Rules.Units.TryGetValue(unit.TypeId, out var type) && type.IsLeader) return 0x12;
        return book.CategoryOf(unit.TypeId) == LastEdict.Rules.Battle.UnitCategory.Guardian ? 0x11 : 0;
    }

    /// <summary>
    /// Остановить продвижение (<c>CForceDynLevelUnitMsg</c> → <c>CVisitorForceDynLevel</c> <c>0x612c92</c>): флаг <see cref="MapUnit.DynLevel"/>
    /// ставится навсегда (отмены нет) — воин больше не повышается до следующего вида, но уровни набирает (<c>UnitUpgrade</c>).
    /// </summary>
    /// <returns>Код отказа <see cref="DynLevelCheck"/>; 0 — остановлено.</returns>
    public int ForceDynLevel(string unitId)
    {
        int check = DynLevelCheck(unitId);
        if (check != 0) return check;
        var units = Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = units[unitId] with { DynLevel = true };
        State = State with { Looks = Retold(Looks, With(Map, units: units), Looks.Stacks) };
        return 0;
    }

    /// <summary>
    /// Нужен ли герой заданию (<c>0x500a57</c>; только окно, до вопроса об отпуске — <c>X005TA0883</c>): группа — отряд, воин — его лидер,
    /// и есть событие сценария, последний эффект которого — «Победа», а одно из условий ссылается на этот отряд (убить отряд, отряд
    /// существует, лидер в области, в городе, с предметом — <c>isIdsEqual</c> <c>0x615b31</c>/<c>0x6155dc</c>). Флаги события (включено,
    /// раз, расы) движок не смотрит. У условий с двумя id движок сверяет с группой оба поля (<c>+0x10</c> и <c>+0x14</c>) — повторено как есть,
    /// хотя второе (область, город, тип предмета) с id отряда совпасть не может.
    /// </summary>
    public bool NeededByQuest(string groupId, string unitId)
    {
        if (!Map.Stacks.TryGetValue(groupId, out var stack) || !Same(stack.LeaderId, unitId)) return false;
        return State.Events.Events.Any(e => e.Effects.Count > 0 && e.Effects[^1] is WinEffect && e.Conditions.Any(c => c switch
        {
            KillStackCondition k => Same(k.StackId, groupId),
            StackExistsCondition s => Same(s.StackId, groupId),
            LeaderToZoneCondition z => Same(z.StackId, groupId) || Same(z.LocationId, groupId),
            LeaderToCityCondition l => Same(l.StackId, groupId) || Same(l.CityId, groupId),
            LeaderOwnItemCondition o => Same(o.StackId, groupId) || Same(o.ItemType, groupId),
            _ => false,
        }));
    }

    /// <summary>
    /// Клетки прицела тумана в квадрате <paramref name="r"/> вокруг (<paramref name="x"/>, <paramref name="y"/>), лежащие в обзоре чужих
    /// (<c>true</c>; у движка такие клетки — <c>SPLAREAFOGN</c>). Ранг 2 по листингу <c>CTaskSpell</c> (<c>0x4d2f58</c>):
    /// <list type="bullet">
    /// <item>объекты собираются по клеткам квадрата, расширенного на 8 (<c>0x4d3333</c>, <c>[0x6e8900]</c> = 8, наибольший обзор): отряд —
    /// по своей клетке, укрепление — по любой клетке пятна;</item>
    /// <item>чужой — хозяин не заклинатель и не нейтрал (<c>0x5e5358</c>: раса не из пяти играбельных); союзы не смотрятся; отряд — только вне
    /// укрепления (<c>+0x94</c> пусто);</item>
    /// <item>область объекта (<c>0x4d31d1</c>) — прямоугольник «точка + размер» (<c>0x438462</c>: у отряда размер 1 × 1, <c>0x6ee9f8</c>; у
    /// укрепления — его пятно), расширенный на обзор, и перебирается <b>включая</b> правую и нижнюю границу: у отряда это квадрат
    /// <c>[x − R, x + 1 + R] × [y − R, y + 1 + R]</c> без снятых углов — на клетку шире настоящего обзора (<c>0x5d4dfc</c> идёт по
    /// полуоткрытому прямоугольнику и снимает углы). Обзор отряда — <c>SCOUT</c> лидера с цепочкой (<c>0x40a41f</c> → <c>+0x10</c>),
    /// укрепления — <c>GcityInf.SCOUT</c> его расы и размера, как в <see cref="Reveal(string)"/>.</item>
    /// </list>
    /// </summary>
    public bool[,] ForeignSight(string playerId, int x, int y, int r)
    {
        var map = Map;
        var seen = new bool[map.Size, map.Size];
        int gx0 = x - r - Fog.MaxScoutRange, gy0 = y - r - Fog.MaxScoutRange, gx1 = x + r + Fog.MaxScoutRange, gy1 = y + r + Fog.MaxScoutRange;
        bool Gathered(int cx, int cy) => map.Contains(cx, cy) && cx >= gx0 && cx <= gx1 && cy >= gy0 && cy <= gy1;
        void Mark(int x0, int y0, int x1, int y1)
        {
            for (int cx = Math.Max(0, x0); cx <= Math.Min(map.Size - 1, x1); cx++)
                for (int cy = Math.Max(0, y0); cy <= Math.Min(map.Size - 1, y1); cy++)
                    seen[cx, cy] = true;
        }
        bool Foreign(string owner) => owner.Length > 0 && !Same(owner, playerId) && map.Players.TryGetValue(owner, out var p)
                                      && Book?.Economy.Races.TryGetValue(p.RaceId, out var race) == true && !race.Neutral;
        foreach (var stack in map.Stacks.Values)
        {
            if (!Foreign(stack.Owner) || stack.Inside.Length > 0 || !Gathered(stack.X, stack.Y)) continue;
            int scout = ScoutRange(stack);
            Mark(stack.X - scout, stack.Y - scout, stack.X + 1 + scout, stack.Y + 1 + scout);
        }
        if (Book is not { } book) return seen;
        foreach (var site in map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && Foreign(s.Owner)))
        {
            var plan = LandModel.PlanCells(map, site.Id);
            if (!plan.Any(c => Gathered(c.X, c.Y)) || !map.Players.TryGetValue(site.Owner, out var owner)) continue;
            var scout = book.Economy.Cities.Where(c => Same(c.RaceId, owner.RaceId)).GroupBy(c => c.Size).ToDictionary(g => g.Key, g => g.First().Scout);
            if (scout.Count == 0) continue;
            int size = site.Kind == SiteKind.Capital ? CapitalCitySize : site.Size ?? 1;
            int radius = scout.TryGetValue(size, out int sr) ? sr : scout.Values.Min();
            Mark(plan.Min(c => c.X) - radius, plan.Min(c => c.Y) - radius, plan.Max(c => c.X) + 1 + radius, plan.Max(c => c.Y) + 1 + radius);
        }
        return seen;
    }

    /// <summary>
    /// Подарок золотом от человека (окно дипломатии «Дать ресурсы», <c>CGiveResourceMsg</c>): сумма — кратная <c>D_GOLD</c> и не больше
    /// казны (<c>0x4b5e26</c>); золото переходит получателю, отношения — как у дани (<see cref="Diplomacy.Tribute"/>: золото / <c>D_GOLD</c>).
    /// ИИ-получатель сразу отвечает сообщением (<see cref="ReactToGold"/>, <c>0x474dd2</c>).
    /// ponytail: серверная проверка <c>CGiveResourceMsg</c> не разобрана — ранг 2.
    /// </summary>
    /// <returns>Причина отказа или null.</returns>
    public string? GiveGold(string from, string to, long amount, List<string>? report = null)
    {
        var book = RequireBook();
        var map = Map;
        if (Same(from, to)) return "дарить себе нельзя";
        if (!map.Players.TryGetValue(from, out var giver) || !map.Players.TryGetValue(to, out var taker)) return "игрока нет";
        if (amount <= 0 || amount % book.Diplomacy.GoldPerPoint != 0) return $"сумма не кратна {book.Diplomacy.GoldPerPoint}";
        if (giver.Bank.Gold < amount) return "не хватает золота";
        if (PartyOf(from) is not { } a || PartyOf(to) is not { } b) return "не ведёт дипломатию";
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[from] = giver with { Bank = giver.Bank with { Gold = giver.Bank.Gold - amount } };
        players[to] = taker with { Bank = taker.Bank with { Gold = Math.Min(Economy.CurrencyMax, taker.Bank.Gold + amount) } };
        var change = Diplomacy.Tribute(map.Relations, Diplomacy.PlayerParties(map, book.Economy), book.Diplomacy, a, b, amount);
        State = State with { Looks = Retold(Looks, With(map, players: players, relations: change.Relations), Looks.Stacks) };
        report?.Add($"{from} дарит {to} {amount} золота");
        ReactToGold(to, from, report);
        Observer?.Happened(new TurnSight(TurnSightKind.GoldGiven, from, 0, 0, to));   // звук окна (сессия Г)
        return null;
    }

    /// <summary>Разрыв союза человеком (<c>CBreakAllianceMsg</c>): тот же мутатор, что у ИИ; цель-ИИ отвечает репликой.</summary>
    /// <returns>Причина отказа или null.</returns>
    public string? BreakAllianceBy(string playerId, string targetId, List<string>? report = null)
    {
        if (PartyOf(playerId) is null || PartyOf(targetId) is null) return "не ведёт дипломатию";
        if (!BreakAlliance(playerId, targetId)) return "союза нет";
        report?.Add($"{playerId} разрывает союз с {targetId}");
        if (!IsHuman(targetId)) Chat(targetId, playerId, AiMessage.PlayerBreakAlliance, report);
        return null;
    }

    /// <summary>
    /// Предложение союза человеком (<c>CProposeAllianceMsg</c>): ИИ отвечает сразу, как на предложение другого ИИ
    /// (<see cref="AiDiplomacy.AnswerAlliance"/>, <c>0x474b37</c>), человеку предложение ждёт его хода (<see cref="GameState.Offers"/>).
    /// </summary>
    /// <returns>Принят ли союз; null — ждёт ответа человека или предложение невозможно.</returns>
    public bool? ProposeAllianceBy(string playerId, string targetId, List<string>? report = null)
    {
        if (PartyOf(playerId) is not { } a || PartyOf(targetId) is not { } b) return null;
        report?.Add($"{playerId} предлагает союз {targetId}");
        if (IsHuman(targetId))
        {
            State = State with { Offers = [.. State.Offers, new AllianceOffer(playerId, targetId, Map.Turn)] };
            return null;
        }
        int rel = Diplomacy.Current(Map.Relations, a, b);
        bool accept = AiDiplomacy.AnswerAlliance(Rng, rel, AiDiplomacy.PlainLandShare(Map, a, Book!.Economy));
        SettleAlliance(playerId, targetId, accept, report);
        return accept;
    }

    private static bool Big(UnitCells cells, string id)
    {
        int p = cells.PositionOf(id);
        return p >= 0 && string.Equals(cells.Cells[Formation.Pair(p)], id, StringComparison.OrdinalIgnoreCase);
    }
}
