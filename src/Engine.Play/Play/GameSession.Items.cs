using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>
/// Предметы отряда на карте: выпить зелье, надеть, снять, забрать склад крепости. Общие для ИИ (подготовка отряда,
/// <see cref="AiStackItems"/>) и окна отряда человека. Правила — <see cref="ItemUse"/> и <see cref="Equipment"/>.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Навык героя «использование посохов и свитков» (<c>L_WAND_SCROLL_USE</c>, <c>LLeadA</c> 7).</summary>
    public const int WandScrollAbility = 7;

    /// <summary>Отказы литья предметом, которые окно показывает сообщением (<c>0x5dbbe8</c>): казна — <c>X005TA0214</c>, посох сегодня
    /// уже — <c>X005TA0579</c> (магу — дважды, <c>X005TA0580</c>).</summary>
    public const string ItemCastNoMana = "не хватает казны", ItemCastWandUsed = "посох уже применён сегодня",
        ItemCastWandUsedTwice = "посох уже применён дважды сегодня";

    /// <summary>Сообщить наблюдателю о полученных предметах или заклинаниях (<see cref="TurnSightKind.ItemsGiven"/>,
    /// <see cref="TurnSightKind.SpellsGiven"/>; сообщения 44/43 движка шлются только не нейтралу, <c>0x5e5358</c>). Модель не меняет.</summary>
    internal void Given(TurnSightKind kind, string playerId, int x, int y, string source, IReadOnlyList<string> ids, bool library = false)
    {
        if (Observer is not { } observer || Book is null || IsNeutral(playerId)) return;
        observer.Happened(new TurnSight(kind, playerId, x, y, source) { Ids = ids, Library = library });
    }

    /// <summary>
    /// Можно ли применить свиток или посох отряда (<c>BTN_WAND_SCROLL</c> → <c>DLG_WAND_SCROLL</c>, проверки <c>0x48de96</c> и
    /// <c>0x5dbbe8</c>): лидер жив и у него навык <see cref="WandScrollAbility"/>; предмет в отряде и несёт заклинание; у посоха — цена
    /// применения (<see cref="ItemUse.CanPay"/>) и раз в день (лорду-магу — дважды); клетка — как у заклинания (карта, туман, цель
    /// <see cref="TargetRefusal"/>). Заклинания знать не нужно, дневной счёт заклинаний не трогается.
    /// </summary>
    /// <returns>Причина отказа или null.</returns>
    public string? ItemCastRefusal(string stackId, string itemId, int x, int y)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return $"нет отряда {stackId}";
        if (!map.Units.TryGetValue(stack.LeaderId, out var leader) || leader.Hp <= 0 || !LeaderAbilitiesOf(stack).Contains(WandScrollAbility))
            return "герой не применяет посохи и свитки";
        if (!stack.Items.Contains(itemId, StringComparer.OrdinalIgnoreCase)) return "предмета нет в отряде";
        if (ItemOf(itemId) is not { } item || ItemUse.SpellOf(item, book.Spells) is not { } spell) return "у предмета нет заклинания";
        var player = map.Players[stack.Owner];
        if (item.Category == ItemCategory.Wand)
        {
            if (!ItemUse.CanPay(player.Bank, item)) return ItemCastNoMana;
            bool mage = book.LordCategory(player.LordId) == Treasury.MageCategory;
            int today = map.ItemCasts.Count(c => Same(c.PlayerId, stack.Owner) && Same(c.Origin, itemId) && SpellCast.Live(c.TurnStop, map.Turn));
            if (today >= (mage ? CastsPerDayMage : CastsPerDay)) return mage ? ItemCastWandUsedTwice : ItemCastWandUsed;
        }
        if (spell.Area != WholeMap)
        {
            if (!map.Contains(x, y)) return "клетка вне карты";
            bool fogged = map.Fog.TryGetValue(player.FogId, out var fog) && fog[x, y];
            if (spell.Category == SpellCategory.Unfog ? !fogged : fogged)
                return spell.Category == SpellCategory.Unfog ? "клетка не под туманом" : "клетка под туманом";
        }
        return TargetRefusal(stack.Owner, spell, x, y);
    }

    /// <summary>
    /// Применить свиток или посох (<c>CTaskCastWandScroll</c>/<c>CTaskCastWorldWandScroll</c>): проверки <see cref="ItemCastRefusal"/>;
    /// посох — цена применения и запись в дневной счёт посохов (<see cref="MapState.ItemCasts"/>), свиток — расходуется; эффект — как у
    /// заклинания (<see cref="CastSpell"/>: урон/лечение, призыв, усиление/ослабление, ход, невидимость, развеивание, туман).
    /// </summary>
    /// <returns>Причина отказа или null.</returns>
    public string? CastItem(string stackId, string itemId, int x, int y, string? summonTarget = null)
    {
        var book = RequireBook();
        if (ItemOf(itemId) is not { } item || ItemUse.SpellOf(item, book.Spells) is not { } spell) return "у предмета нет заклинания";
        if (spell.Category == SpellCategory.Summon)
        {
            if (SummonCell(x, y, spell.SummonUnitId) is not { } cell) return "некуда призвать";
            (x, y) = cell;
        }
        if (ItemCastRefusal(stackId, itemId, x, y) is { } why) return why;
        var map = Map;
        var stack = map.Stacks[stackId];
        string owner = stack.Owner;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var itemCasts = map.ItemCasts;
        if (item.Category == ItemCategory.Wand)
        {
            players[owner] = players[owner] with { Bank = ItemUse.Pay(players[owner].Bank, item) };
            itemCasts = [.. itemCasts, new CastRecord(owner, itemId, SpellCast.TurnStop(map.Turn))];
        }
        else stacks[stackId] = stack with { Items = [.. stack.Items.Where(i => !Same(i, itemId))] };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, players: players, itemCasts: itemCasts), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Spell, owner, x, y, spell.Id));
        ApplySpell(owner, spell, x, y, summonTarget);
        return null;
    }

    /// <summary>Новые экземпляры-талисманы получают запись зарядов <c>GVars.TALIS_CHRG</c> (<c>CMidTalismanCharges</c>): движок пишет её
    /// при создании талисмана (<c>[0x6ea178]</c> = 5, <see cref="D2.Rules.Magic.Item"/>), а без записи <c>0x5eb9b3</c> даёт 0 — порт её не
    /// создавал, и подарок ИИ (<see cref="GiftItems"/>) и вывоз героя оценивали новый талисман пустым (аудит 13.10).</summary>
    private void ChargeNewTalismans(IReadOnlyList<string> instances)
    {
        var book = RequireBook();
        var map = Map;
        var fresh = instances.Where(i => !map.TalismanCharges.ContainsKey(i) && ItemOf(i)?.Category == ItemCategory.Talisman).ToList();
        if (fresh.Count == 0) return;
        var charges = map.TalismanCharges.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (string id in fresh) charges[id] = book.Vars.TalismanCharges;
        State = State with { Looks = Retold(Looks, With(map, talismanCharges: charges), Looks.Stacks) };
    }

    /// <summary>Предмет экземпляра <c>IM</c> по его типу (<see cref="MapState.ItemTypes"/>); нет — null.</summary>
    public Item? ItemOf(string instanceId) =>
        Map.ItemTypes.TryGetValue(instanceId, out var type) && RequireBook().Items.TryGetValue(type, out var item) ? item : null;

    /// <summary>Способности лидера отряда — то, что видит <c>IUsStackLeader::hasAbility</c>: врождённые (<c>GLabi</c>) и
    /// элементы цепочки модификаторов (навыки уровня и <c>MOD_EQUIP</c> надетого).</summary>
    public IReadOnlySet<int> LeaderAbilitiesOf(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var book = RequireBook();
        if (!Map.Units.TryGetValue(stack.LeaderId, out var leader)) return new HashSet<int>();
        var chain = leader.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>();
        return D2.Rules.Units.LeaderAbilities.Innate(leader.TypeId, book.InnateAbilities)
                                        .Concat(D2.Rules.Units.LeaderAbilities.FromChain(chain)).ToHashSet();
    }

    /// <summary>Что у лидера отряда уже есть для отказа «предмет ничего не даст» (<c>0x5EC48F</c>) помимо способностей: земли с
    /// бонусом хода (<c>hasMovementBonus</c> — врождённые <c>GMabi</c> типа и элементы цепочки) и иммунитеты по источнику и классу
    /// (<c>getImmuneBy*</c> — тип выросшей реализации, <c>Gimmu</c>/<c>GimmuC</c>, плюс цепочка). Проверяемый предмет в сумке, в цепочку
    /// он ещё не лёг. Нет лидера — ничего.
    /// <para>Врождённые земли — 16.10 по журналу: <c>hasMovementBonus</c> — виртуальный вызов реализации лидера, и база отвечает по
    /// <c>GMabi</c> (летун <c>0x5c3a76</c>: равнина, лес и вода сразу), модификаторы лишь добавляют. Летуну <c>G000UU0071</c> сапоги
    /// плавания <c>G000IG1011</c> движок отказывает кодом 0xB во всех семи слотах (probe65, seq 5401949–5401969, 6955526), порт
    /// без <c>GMabi</c> пускал в слот сапог (<c>HumanChecksHarnessTests</c>).</para></summary>
    public LeaderEffects LeaderEffectsOf(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var rules = RequireBook().Rules;
        if (!Map.Units.TryGetValue(stack.LeaderId, out var leader)) return LeaderEffects.None;
        var unit = MapBattle.Unit(leader, 0, rules, isAttacker: false);
        var stats = EffectiveStats.Of(unit.Type, unit.Modifiers);
        var innate = Art.MoveBonuses.FirstOrDefault(b => b.Unit.Equals(leader.TypeId, StringComparison.OrdinalIgnoreCase))?.Grounds ?? [];
        return new LeaderEffects(stats.MoveAbilities.Concat(innate).ToHashSet(), [.. unit.Type.ImmunitiesBySource ?? [], .. stats.Immunities],
                                 [.. unit.Type.ImmunitiesByClass ?? [], .. stats.ClassImmunities]);
    }

    /// <summary>
    /// Выпить зелье на карте (команда <c>0x41f172</c>; ворота — <see cref="ItemUse.CanApplyPotion"/>, <c>0x5de495</c>).
    /// Лечебное — <see cref="ItemUse.HealAmount"/>; воскрешение — здоровье 1 (журналы: 54 из 54); усиление и постоянное —
    /// <c>MOD_POTION</c> в цепочку юнита. Вне боя зелье тратится всегда (журналы: <c>destroy</c> у всех 131); надетое —
    /// освобождает слот. Воскрешённый лидер заново надевает снаряжение, и боевые слоты уплотняются: первый пуст, второй
    /// занят — предмет второго переходит в первый (ранг 2: журнал human, дни 21, 34, 39 — после зелья воскрешения лидера
    /// из слота 2 снимаются все слоты и слот 3 надевается во 2; после лечебного, дни 1, 3, 9, — без сдвига). Отказ — его
    /// имя, иначе null.
    /// Модификатор зелья усиления — со сроком, как у заклинания (<see cref="SpellEffectRecord"/>: <c>ORIGIN_ID</c> — тип предмета,
    /// снимается в начале следующего хода хозяина).
    /// <para>Держатель <paramref name="holderId"/> — отряд или укрепление (склад), юнит — любой на карте: ни ворота <c>0x5de495</c>
    /// (<c>0x5de66a</c> — только «предмет у держателя»), ни <c>applyPotion</c> <c>0x5d70f9</c> не спрашивают, в чьей он группе
    /// (окно армии: зелье со склада укрепления — воинам отряда-гостя, журнал gaps, 5 из 5; из сумки гостя — любому воину окна).</para>
    /// </summary>
    public string? DrinkPotion(string holderId, string itemId, string unitId)
    {
        var map = Map;
        if (!map.Units.TryGetValue(unitId, out var unit) || !IsHolder(holderId)) return "нет отряда или юнита";
        if (ItemOf(itemId) is not { } item) return "нет предмета";
        int max = HpMax(unit);
        bool applied = item.PotionModifierId.Length > 0 && unit.Modifiers.Contains(item.PotionModifierId, StringComparer.OrdinalIgnoreCase);
        var gate = ItemUse.CanApplyPotion(item, Holds(holderId, itemId), unit.Hp, max, applied);
        if (gate != PotionResult.Ok) return gate.ToString();
        var drunk = item.Category switch
        {
            ItemCategory.PotionHeal => unit with { Hp = unit.Hp + ItemUse.HealAmount(item, unit.Hp, max) },
            ItemCategory.PotionRevive => unit with { Hp = 1 },
            _ => unit with { Modifiers = [.. unit.Modifiers, item.PotionModifierId.ToUpperInvariant()] },
        };
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = drunk;
        var records = map.SpellEffects;
        string owner = map.Stacks.TryGetValue(holderId, out var holder) ? holder.Owner : map.Sites.First(s => Same(s.Id, holderId)).Owner;
        if (item.Category == ItemCategory.PotionBoost && item.PotionModifierId.Length > 0)
            records = [.. records, new SpellEffectRecord(unitId, item.Id.ToUpperInvariant(), item.PotionModifierId.ToUpperInvariant(),
                                                         owner, SpellCast.TurnStop(map.Turn))];
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var sites = map.Sites.ToList();
        if (holder is not null) stacks[holderId] = WithoutItem(holder, itemId);
        else
        {
            int at = sites.FindIndex(s => Same(s.Id, holderId));
            sites[at] = sites[at] with { Items = [.. sites[at].Items.Where(i => !Same(i, itemId))] };
        }
        // Воскрешённый лидер — его отряд снова с живым лидером (LEADR_ALIV), как у воскрешения в укреплении (LeaderRevived).
        if (item.Category == ItemCategory.PotionRevive && stacks.Values.FirstOrDefault(s => Same(s.LeaderId, unitId)) is { } led)
            stacks[led.Id] = Compacted(led) with { LeaderAlive = true };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, units: units, spellEffects: records, sites: sites), Looks.Stacks) };
        return null;
    }

    /// <summary>Держатель предметов для зелья: отряд или укрепление (склад, <c>CDDFortInventoryDisplay</c>); мешок на земле — нет (у движка
    /// рамок от него нет: <c>0x500d03</c>, тип id держателя <c>0x27</c>).</summary>
    private bool IsHolder(string holderId) =>
        Map.Stacks.ContainsKey(holderId)
        || Map.Sites.Any(s => Same(s.Id, holderId) && s.Kind is SiteKind.Capital or SiteKind.Village && !s.Gone);

    /// <summary>Лежит ли предмет у держателя (<c>0x5de66a</c>): у отряда — сумка и слоты (<see cref="Equipment.Carried"/>), у укрепления —
    /// склад.</summary>
    public bool Holds(string holderId, string itemId) =>
        Map.Stacks.TryGetValue(holderId, out var stack) ? Equipment.Carried(stack, itemId)
        : IsHolder(holderId) && ItemsOf(holderId) is { } items && items.Contains(itemId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Надеть предмет из сумки в слот (команда <c>0x41f0fe</c>; проверка <see cref="Equipment.Check"/> = <c>0x5df6b7</c>).
    /// Занятый слот сначала освобождается (<see cref="UnequipItem"/>). <c>MOD_EQUIP</c> ложится носителям
    /// (<see cref="Equipment.Bearers"/>). Возвращает отказ проверки (<see cref="EquipRefusal.Ok"/> — надето). «Предмет ничего не
    /// даст» — по способностям, бонусу хода и иммунитетам лидера (<see cref="LeaderEffectsOf"/>).
    /// </summary>
    public EquipRefusal EquipItem(string stackId, string itemId, int slot)
    {
        var book = RequireBook();
        var refusal = EquipCheck(stackId, itemId, slot);
        if (refusal != EquipRefusal.Ok) return refusal;
        var stack = Map.Stacks[stackId];
        var item = ItemOf(itemId)!;
        bool alive = Map.Units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0;
        if (Equipment.Slots(stack)[slot].Length > 0) UnequipItem(stackId, slot);
        stack = Map.Stacks[stackId];
        var outcome = Equipment.Equip(stack, item, itemId, slot, LeaderAbilitiesOf(stack), alive, book.Rules.Modifiers,
                                      has: LeaderEffectsOf(stack));
        ApplyEquip(stack with { Equipment = outcome.Slots }, outcome, add: true);
        return outcome.Refusal;
    }

    /// <summary>Проверка надевания предмета в слот лидера отряда (<see cref="Equipment.Check"/> = <c>0x5df6b7</c>; способности, бонус хода и
    /// иммунитеты лидера) — та же, что у <see cref="EquipItem"/> и рамок слотов куклы.</summary>
    /// <param name="checkEffect">Пятый аргумент <c>0x5df6b7</c>: ложь — отказ «ничего не даст» (0xB) не проверяется (журнал
    /// <c>equip_check</c>, поле <c>check_effect</c>).</param>
    public EquipRefusal EquipCheck(string stackId, string itemId, int slot, bool checkEffect = true)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack) || ItemOf(itemId) is not { } item) return EquipRefusal.NotEquippable;
        bool alive = Map.Units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0;
        return Equipment.Check(item, slot, LeaderAbilitiesOf(stack), alive, checkEffect ? RequireBook().Rules.Modifiers : null,
                               LeaderEffectsOf(stack));
    }

    /// <summary>Снять предмет из слота (<c>unEquipItem 0x5e746d</c>): слот пустеет, <c>MOD_EQUIP</c> уходит у носителей;
    /// предмет остаётся в сумке. Пустой слот — ничего.</summary>
    public void UnequipItem(string stackId, int slot)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return;
        string id = Equipment.Slots(stack)[slot];
        if (id.Length == 0 || ItemOf(id) is not { } item) return;
        var outcome = Equipment.Unequip(stack, slot, item);
        ApplyEquip(stack with { Equipment = outcome.Slots }, outcome, add: false);
    }

    /// <summary>Отряд в крепости забирает весь её склад (<c>0x45193d</c> → команда обмена <c>0x41f0c4</c>): список
    /// собирается множеством id (<c>0x524c35</c> → <c>0x525c9c</c>, вставка с парой-результатом), поэтому предметы идут в
    /// конец сумки по возрастанию id (ранг 2: журнал legions, день 6 — <c>S073IM0008</c> раньше <c>S073IM0099</c>); склад
    /// пустеет. Не в крепости или склад пуст — ничего.</summary>
    public void TakeFortItems(string stackId)
    {
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack) || stack.Inside.Length == 0) return;
        var sites = map.Sites.ToList();
        int i = sites.FindIndex(s => Same(s.Id, stack.Inside) && s.Kind is SiteKind.Capital or SiteKind.Village);
        if (i < 0 || sites[i].Items.Count == 0) return;
        var fort = sites[i];
        sites[i] = fort with { Items = [] };
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack with { Items = [.. stack.Items, .. fort.Items.Order(StringComparer.OrdinalIgnoreCase)] };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, sites: sites), Looks.Stacks) };
    }

    /// <summary>Отряд в крепости сдаёт всю сумку на её склад — у вора и жезлоносца (<c>0x451a43</c>: та же команда обмена
    /// <c>0x41f0c4</c>, что у <see cref="TakeFortItems"/>, с переставленными «откуда» и «куда»). Сервер (<c>0x5d752e</c>) по
    /// каждому предмету: надетый сначала снимается (<c>0x5e746d</c>), затем переносится (<c>0x5e73cb</c>); список — по
    /// возрастанию id (<c>0x524c35</c>). Ранг 2: в сохранениях корпуса у вора и жезлоносца в крепости сумка пуста всегда
    /// (110 случаев).</summary>
    public void GiveFortItems(string stackId)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack) || stack.Inside.Length == 0 || stack.Items.Count == 0) return;
        if (!Map.Sites.Any(s => Same(s.Id, stack.Inside) && s.Kind is SiteKind.Capital or SiteKind.Village)) return;
        var items = stack.Items.Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string item in items)
            for (int slot = 0; slot < Equipment.SlotCount; slot++)
                if (Same(Equipment.Slots(Map.Stacks[stackId])[slot], item)) UnequipItem(stackId, slot);
        var map = Map;
        stack = map.Stacks[stackId];
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack with { Items = [] };
        var sites = map.Sites.Select(s => Same(s.Id, stack.Inside) ? s with { Items = [.. s.Items, .. items] } : s).ToList();
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, sites: sites), Looks.Stacks) };
    }

    /// <summary>Отряд отдаёт всю сумку другому отряду — команда обмена <c>0x41f0c4</c> (передача предметов ИИ <c>0x4571f0</c>): как у
    /// <see cref="GiveFortItems"/>, надетое сначала снимается, список — по возрастанию id, в конец сумки получателя.</summary>
    public void GiveItems(string fromStackId, string toStackId)
    {
        if (!Map.Stacks.TryGetValue(fromStackId, out var from) || !Map.Stacks.ContainsKey(toStackId) || from.Items.Count == 0) return;
        var items = from.Items.Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string item in items)
            for (int slot = 0; slot < Equipment.SlotCount; slot++)
                if (Same(Equipment.Slots(Map.Stacks[fromStackId])[slot], item)) UnequipItem(fromStackId, slot);
        var map = Map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[fromStackId] = stacks[fromStackId] with { Items = [] };
        stacks[toStackId] = stacks[toStackId] with { Items = [.. stacks[toStackId].Items, .. items] };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks), Looks.Stacks) };
    }

    /// <summary>
    /// Состав отряда поменялся: <c>MOD_EQUIP</c> надетых предметов — вошедшему юниту, у ушедшего — снять. Движок: у
    /// <c>CMidUnitGroupStack</c> (vftable <c>0x6f0d64</c>) слот 6 — добавить юнит (<c>0x6147c4</c> → <c>0x5ed732</c>), слот 7 — убрать
    /// (<c>0x61483b</c> → <c>0x5ed766</c>); оба по каждому надетому зовут <c>0x5ecb47</c>: знамя (категория <c>[0x837678]</c>) — любому
    /// юниту, прочее — только лидеру (<c>+0x28</c> отряда), как <see cref="Equipment.Bearers"/> при надевании. Ранг 1 по листингу;
    /// корпус legions, день 38: <c>S073UN01e9</c> из гарнизона столицы в <c>KC0000</c> со знаменем <c>IG1002</c> — у движка броня 15
    /// (<c>UM2102</c>), своя сила B +2,75294 = 15,6 · (60/51 − 1).
    /// </summary>
    /// <returns>Юниты карты с поправленными цепочками; ничего не поменялось — null (<see cref="With"/> оставит прежние).</returns>
    private Dictionary<string, MapUnit>? EquipOnMembership(IReadOnlyDictionary<string, MapUnit> allUnits, MapStack stack,
                                                         IReadOnlyList<string> nowUnits)
    {
        var worn = Equipment.Slots(stack).Where(id => id.Length > 0).Select(ItemOf).OfType<Item>()
                            .Where(i => i.EquipModifierId.Length > 0).ToList();
        if (worn.Count == 0) return null;
        var joined = nowUnits.Except(stack.UnitIds, StringComparer.OrdinalIgnoreCase).ToList();
        var left = stack.UnitIds.Except(nowUnits, StringComparer.OrdinalIgnoreCase).ToList();
        if (joined.Count + left.Count == 0) return null;
        var units = allUnits.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var (unitId, add) in joined.Select(u => (u, true)).Concat(left.Select(u => (u, false))))
        {
            if (!units.TryGetValue(unitId, out var unit)) continue;
            var mods = unit.Modifiers.ToList();
            foreach (var item in worn.Where(i => i.Category == ItemCategory.Banner || Same(unitId, stack.LeaderId)))
                if (add) mods.Add(item.EquipModifierId.ToUpperInvariant());
                else if (mods.FindIndex(m => Same(m, item.EquipModifierId)) is int at and >= 0) mods.RemoveAt(at);
            if (mods.Count == unit.Modifiers.Count && mods.SequenceEqual(unit.Modifiers)) continue;
            units[unitId] = unit with { Modifiers = mods };
            changed = true;
        }
        return changed ? units : null;
    }

    private void ApplyEquip(MapStack stack, EquipOutcome outcome, bool add)
    {
        var map = Map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (outcome.ModifierId.Length > 0)
            foreach (string bearer in outcome.Bearers)
            {
                if (!units.TryGetValue(bearer, out var u)) continue;
                var mods = u.Modifiers.ToList();
                if (add) mods.Add(outcome.ModifierId.ToUpperInvariant());
                else if (mods.FindIndex(m => Same(m, outcome.ModifierId)) is int at and >= 0) mods.RemoveAt(at);
                units[bearer] = u with { Modifiers = mods };
            }
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack;
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, units: units), Looks.Stacks) };
    }

    /// <summary>Отряд без экземпляра: из сумки и из слота снаряжения, если он надет.</summary>
    private static MapStack WithoutItem(MapStack stack, string itemId) => stack with
    {
        Items = [.. stack.Items.Where(i => !Same(i, itemId))],
        Equipment = [.. stack.Equipment.Select(i => Same(i, itemId) ? "" : i)],
    };

    /// <summary>Боевые слоты без дыры: первый пуст, второй занят — второй переходит в первый.</summary>
    private static MapStack Compacted(MapStack stack)
    {
        if (stack.Equipment.Count != Equipment.SlotCount || stack.Equipment[Equipment.SlotBattle1].Length > 0
            || stack.Equipment[Equipment.SlotBattle2].Length == 0) return stack;
        var slots = stack.Equipment.ToList();
        (slots[Equipment.SlotBattle1], slots[Equipment.SlotBattle2]) = (slots[Equipment.SlotBattle2], "");
        return stack with { Equipment = slots };
    }
}
