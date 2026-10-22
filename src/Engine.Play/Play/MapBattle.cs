using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Бой с карты: из групп партии — <see cref="BattleState"/> для <c>BattleSim</c>, после боя — исход обратно в
/// карту. Сборка — по образцу <c>CorpusBattleLoader</c> (там тот же бой собирается из журнала зонда), только юниты и
/// их цепочки берутся из модели карты.
///
/// <para><b>Кто защищается</b> (ранг 1 по корпусу, 27 боёв у укреплений, <c>docs/step6-game-loop.md</c> §9): отряд в
/// поле; в укреплении — отряд-визитёр, если он есть, иначе гарнизон (<c>FT</c>); в руине — её охрана (<c>RU</c>).
/// Защитники укрепления (визитёр и гарнизон) получают броню укрепления (<see cref="Fortifications.Protection"/>,
/// в журнале <c>fort</c> 10…50), у охраны руины её нет. Бежать защитникам укрепления и руины нельзя (<c>0x5f51bf</c>).</para>
///
/// <para>Отступление с карты после бегства защитника — <c>GameSession.RetreatFled</c> (клетка прочь от нападающего).</para></summary>
public static class MapBattle
{
    /// <summary>Группа боя на карте: отряд, гарнизон укрепления или охрана руины.</summary>
    private sealed record Group(string Id, string Owner, IReadOnlyList<string> UnitIds, IReadOnlyList<int> Slots,
                                MapStack? Stack, MapSite? Fort);

    /// <summary>Бой нападающего <paramref name="attackerId"/> с группой <paramref name="defenderId"/> — отрядом
    /// (<c>KC</c>), гарнизоном (<c>FT</c>) или охраной руины (<c>RU</c>). <paramref name="attackerMove"/> — остаток
    /// очков хода нападающего уже после списания за атаку (<see cref="Movement.AfterAttack"/>): его читает бегство ИИ.
    /// <paramref name="duel"/> — дуэль вора (<c>CNobleActionResultDuel</c> → <c>CEffectBattle</c>): в бою участвуют только лидер
    /// нападающего и лидер отряда-цели, прочие юниты — <see cref="BattleUnit.Hidden"/> (бит 30; журнал human, день 20:
    /// <c>S073KC0083</c> против <c>S073KC007c</c> — в очереди двое, у остальных <c>st</c> = <c>0x40000000</c>).</summary>
    /// <exception cref="InvalidDataException">Тип юнита или модификатор не найден в правилах.</exception>
    /// <exception cref="KeyNotFoundException">Нет такой группы на карте.</exception>
    public static BattleState Build(MapState map, string attackerId, string defenderId, GameBook book,
                                    int difficulty, int attackerMove, bool duel = false)
    {
        var rules = book.Rules;
        var attacker = map.Stacks[attackerId];
        var defender = GroupOf(map, defenderId);
        int fortArmor = defender.Fort is { Kind: SiteKind.Capital or SiteKind.Village } fort
            ? Fortifications.Protection(book.Economy, rules.Modifiers ?? new Dictionary<string, Modifier>(), fort,
                                        map.Players.GetValueOrDefault(fort.Owner)?.RaceId ?? "")
            : 0;
        var units = new List<BattleUnit>();
        units.AddRange(UnitsOf(map, new Group(attacker.Id, attacker.Owner, attacker.UnitIds, attacker.Slots, attacker, null),
                               rules, isAttacker: true, fortArmor: 0));
        units.AddRange(UnitsOf(map, defender, rules, isAttacker: false, fortArmor));
        EquipLeader(units, map, attacker, book);
        if (defender.Stack is { } guest) EquipLeader(units, map, guest, book);
        if (duel)
            foreach (var u in units)
                u.Hidden = !u.Id.Equals(attacker.LeaderId, StringComparison.OrdinalIgnoreCase)
                           && !u.Id.Equals(defender.Stack?.LeaderId, StringComparison.OrdinalIgnoreCase);
        var attackerSide = SideOf(map, attacker.Owner, attacker, attackerMove);
        var defenderSide = SideOf(map, defender.Owner, defender.Stack, defender.Stack?.Move ?? 0);
        bool easy = difficulty == (int)Difficulty.Easy;
        return new BattleState
        {
            Units = units, AttackerGroup = attacker.Id, DefenderGroup = defender.Id, Round = 0, Rules = rules,
            LimitRounds = !attackerSide.IsHuman && !defenderSide.IsHuman,
            AttackersHumanOnEasy = easy && attackerSide.IsHuman,
            DefendersHumanOnEasy = easy && defenderSide.IsHuman,
            AttackersPowerShift = attackerSide.IsHuman ? 0 : Formulas.AiPowerShift((Difficulty)difficulty),
            DefendersPowerShift = defenderSide.IsHuman ? 0 : Formulas.AiPowerShift((Difficulty)difficulty),
            DefendersCanRetreat = defender.Stack is { Inside.Length: 0 },
            AttackerSide = attackerSide, DefenderSide = defenderSide,
        };
    }

    /// <summary>Боевые предметы лидера — экземпляры слотов <c>BATTLE1</c> и <c>BATTLE2</c> по порядку, известные правилам
    /// (тип в <see cref="MapState.ItemTypes"/>, предмет в <c>GItem</c>). В бою это <see cref="BattleUnit.BattleItems"/> лидера:
    /// тот же номер — тот же слот (у движка в бою два слота предводителя, их же обходит конец боя <c>0x629755</c>).</summary>
    public static IReadOnlyList<string> BattleSlots(MapState map, MapStack stack, GameBook book)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(book);
        if (stack.Equipment.Count != Equipment.SlotCount) return [];
        return [.. new[] { stack.Equipment[Equipment.SlotBattle1], stack.Equipment[Equipment.SlotBattle2] }
                   .Where(id => id.Length > 0 && map.ItemTypes.TryGetValue(id, out var type) && book.Items.ContainsKey(type))];
    }

    private static void EquipLeader(List<BattleUnit> units, MapState map, MapStack stack, GameBook book)
    {
        var leader = units.FirstOrDefault(u => u.Id.Equals(stack.LeaderId, StringComparison.OrdinalIgnoreCase));
        if (leader is null) return;
        foreach (string id in BattleSlots(map, stack, book)) leader.BattleItems.Add(book.Items[map.ItemTypes[id]]);
    }

    /// <summary>Расход боевых предметов лидеров за бой (<c>checkAndDestroyEquippedBattleItems</c> <c>0x629755</c>): по
    /// <see cref="BattleState.UsedItems"/> применённая сфера и зелье уходят из отряда (<see cref="Item.IsDestroyedAfterBattle"/>),
    /// применённый талисман теряет заряд (<see cref="Charges.TalismanAfterBattle"/>; записи нет — заряды нового,
    /// <c>TALIS_CHRG</c>) и на нуле уходит вместе с записью. Звать до <see cref="Apply"/> — у погибшего отряда предметы
    /// уходят вместе с ним.</summary>
    public static MapState SpendItems(MapState map, BattleState battle, GameBook book)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(book);
        if (battle.UsedItems.Count == 0) return map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var charges = map.TalismanCharges.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (string group in new[] { battle.AttackerGroup, battle.DefenderGroup })
        {
            if (!stacks.TryGetValue(group, out var stack)) continue;
            var slots = BattleSlots(map, stack, book);
            var used = battle.UsedItems.Where(u => u.UnitId.Equals(stack.LeaderId, StringComparison.OrdinalIgnoreCase))
                                       .Select(u => u.Index).ToHashSet();
            foreach (int i in used.Where(i => i >= 0 && i < slots.Count).Order())
            {
                string id = slots[i];
                var item = book.Items[map.ItemTypes[id]];
                bool gone = item.IsDestroyedAfterBattle(applied: true);
                if (item.Category == ItemCategory.Talisman)
                {
                    // Записи с нулём не бывает: на нуле талисман снимается вместе с записью (Charges.Destroyed).
                    int left = Charges.TalismanAfterBattle(charges.GetValueOrDefault(id, book.Vars.TalismanCharges), applied: true);
                    gone = Charges.Destroyed(left);
                    if (gone) charges.Remove(id);
                    else charges[id] = left;
                }
                if (gone)
                    stack = stack with
                    {
                        Items = [.. stack.Items.Where(x => !x.Equals(id, StringComparison.OrdinalIgnoreCase))],
                        Equipment = [.. stack.Equipment.Select(x => x.Equals(id, StringComparison.OrdinalIgnoreCase) ? "" : x)],
                    };
            }
            stacks[group] = stack;
        }
        return GameSession.With(map, stacks: stacks, talismanCharges: charges);
    }

    /// <summary>Защитник объекта на карте: отряд — он сам; укрепление — визитёр, если есть, иначе гарнизон (пустой
    /// гарнизон — null: боя нет, объект берётся заходом); руина — её охрана (нет — null).</summary>
    public static string? DefenderOf(MapState map, string targetId)
    {
        if (map.Stacks.ContainsKey(targetId)) return targetId;
        var site = map.Sites.FirstOrDefault(s => s.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (site is null) return null;
        if (site.Kind is SiteKind.Capital or SiteKind.Village && site.StackId.Length > 0 && map.Stacks.ContainsKey(site.StackId))
            return site.StackId;
        return AliveGuards(map, site) > 0 ? site.Id : null;
    }

    private static int AliveGuards(MapState map, MapSite site) =>
        site.Guards.Count(id => map.Units.TryGetValue(id, out var u) && u.Hp > 0);

    private static Group GroupOf(MapState map, string id)
    {
        if (map.Stacks.TryGetValue(id, out var stack))
        {
            var fort = stack.Inside.Length == 0 ? null
                : map.Sites.FirstOrDefault(s => s.Id.Equals(stack.Inside, StringComparison.OrdinalIgnoreCase));
            return new Group(stack.Id, stack.Owner, stack.UnitIds, stack.Slots, stack, fort);
        }
        var site = map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                   ?? throw new KeyNotFoundException($"группы {id} нет на карте");
        return new Group(site.Id, site.Owner, site.Guards, site.GuardSlots, null, site);
    }

    /// <summary>Юниты группы в порядке <c>BattleMsgData::unitsInfo</c>: от последнего юнита группы к первому — <c>0x62136d</c>
    /// (<c>for i = group.count() − 1 (0x5f4441) … 0: addUnitToBattleMsgData(map, group, group.unitId(i) (0x5f44b9), …)</c> —
    /// <c>0x6213ba</c>), сперва нападающие (<c>0x6212aa</c>), затем защитники (<c>0x6212bd</c>). Порядок не по клеткам: им движок раздаёт
    /// броски инициативы и решает ничьи очереди (<see cref="TurnOrder"/>), от него идёт перемешивание площадных целей
    /// (<see cref="Attacks.Shuffle"/>). Ранг 1 — листинг и журнал карты 23 (<c>20261020-port-synth</c>): 96 начал боя из 96 —
    /// <c>battle_start</c> по убыванию индекса юнита в группе; прежде порт ставил юнитов по клеткам, и в 91 бою ИИ ↔ ИИ очередь и цели
    /// расходились с первого раунда (бои на R250 движка давали иное здоровье, StayInside дней 32–57, проигрыш KC0007 дня 58).
    /// Крупный юнит стоит в двух клетках одной колонки, место в бою — первая из них. Павший в прошлом бою выходит в бой павшим —
    /// лежит костями на своём месте, его можно воскресить (вторая атака Патриарха, зелье жизни), а призвать на его клетку — можно
    /// (<see cref="LifeAttacks.Summon"/>). Ранг 1 по корпусу: <c>battle_start</c> оригинала несёт павших с <c>hp 0</c> и статусом 3
    /// (мёртв) — 586 начал боя в корпусе, например <c>20260909-arena1/map-001</c> seq 124189 (<c>S143UN0011</c>, <c>S143UN0010</c>);
    /// прежде порт их в бой не брал, и их клетки были пустыми (партия 15.10, пометки 3 и 12).</summary>
    private static IEnumerable<BattleUnit> UnitsOf(MapState map, Group group, GameRules rules, bool isAttacker, int fortArmor)
    {
        for (int index = group.UnitIds.Count - 1; index >= 0; index--)
        {
            int cell = FirstCell(group.Slots, index);
            if (cell < 0 || !map.Units.TryGetValue(group.UnitIds[index], out var unit)) continue;
            var fighter = Unit(unit, cell, rules, isAttacker, index);
            fighter.FortificationArmor = fortArmor;
            yield return fighter;
        }
    }

    /// <summary>Первая клетка построения (0..5), где стоит юнит <paramref name="index"/> группы; нет — −1.</summary>
    private static int FirstCell(IReadOnlyList<int> slots, int index)
    {
        for (int cell = 0; cell < slots.Count; cell++)
            if (slots[cell] == index) return cell;
        return -1;
    }

    /// <summary>Юнит карты как боевой: реализация, выросшая уровнем, и своя цепочка модификаторов.</summary>
    public static BattleUnit Unit(MapUnit unit, int cell, GameRules rules, bool isAttacker, int groupIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(rules);
        // Выросший уровнем юнит движок держит порождённой реализацией (G000UG…): у неё выросли и тип, и атаки.
        string implId = unit.TypeId;
        if (rules.Units.TryGetValue(unit.TypeId, out var baseType) && unit.Level > baseType.Level && rules.Generator is { } g
            && g.GeneratedId(unit.TypeId, unit.Level) is { Length: > 0 } generated)
            implId = generated;
        var impl = rules.ImplOf(implId) ?? rules.ImplOf(unit.TypeId)
                   ?? throw new InvalidDataException($"юнит {unit.Id}: тип {unit.TypeId} не найден в правилах");
        if (impl.Attack is not { } attack) throw new InvalidDataException($"юнит {unit.Id}: у типа {unit.TypeId} нет атаки");
        var chain = Chain(unit, rules);
        // Потолок здоровья — с цепочкой (предметы и способности лидера прибавляют HP), как у движка.
        int hpMax = EffectiveStats.Of(impl.Type, chain).HitPoints;
        return new BattleUnit
        {
            Id = unit.Id, Type = impl.Type, Attack = attack, Attack2 = impl.Attack2, Attacker = isAttacker, Slot = cell,
            GroupIndex = groupIndex, HpMax = hpMax, Hp = Math.Min(unit.Hp, hpMax), Xp = unit.Xp, Modifiers = chain,
        };
    }

    /// <summary>Своя цепочка модификаторов юнита (<see cref="MapUnit.Modifiers"/>) в порядке хранения.</summary>
    private static IReadOnlyList<Modifier> Chain(MapUnit unit, GameRules rules)
    {
        var ids = unit.Modifiers.Where(id => id.Length > 0 && !id.Equals("G000000000", StringComparison.OrdinalIgnoreCase)).ToList();
        if (ids.Count == 0) return [];
        if (rules.Modifiers is null) throw new InvalidDataException("правила собраны без таблицы модификаторов");
        return [.. ids.Select(id => rules.Modifiers.TryGetValue(id, out var m) ? m
                    : throw new InvalidDataException($"модификатор {id} юнита {unit.Id} не найден в правилах"))];
    }

    private static BattleSide SideOf(MapState map, string ownerId, MapStack? stack, int movement)
    {
        var owner = map.Players.GetValueOrDefault(ownerId);
        return new BattleSide
        {
            Known = owner is not null, IsStack = stack is not null, IsHuman = owner?.IsHuman ?? false,
            PlayableRace = owner is not null && !owner.RaceId.EndsWith("RR0004", StringComparison.OrdinalIgnoreCase),
            Order = (StackOrder)(stack?.Order ?? 0), OrderTarget = stack?.OrderTarget ?? "",
            AiOrder = (StackOrder)(stack?.AiOrder ?? 0), Movement = movement,
        };
    }

    /// <summary>Будет ли в конце боя «Меню героя» (<c>BTN_PAPERDOLL</c>, <c>battleEnd</c> <c>0x637581</c>): клиент играет за нападающих
    /// (<c>CTaskBattle</c> <c>+0x2c</c>: нападающая группа — отряд, id типа 0x23, этого игрока, <c>0x4d77b8…0x4d7842</c>;
    /// <paramref name="ownAttackerStack"/>) и у нападающих остался живой юнит (<c>0x637803</c>). Иначе — только «Закрыть».</summary>
    public static bool PaperdollOffered(BattleState state, bool ownAttackerStack)
    {
        ArgumentNullException.ThrowIfNull(state);
        return ownAttackerStack && state.Units.Any(u => u.Attacker && u.Hp > 0);
    }

    /// <summary>Нападающая группа — отряд игрока <paramref name="viewer"/> (null — вид редактора: свой).</summary>
    public static bool OwnsAttackerStack(MapState map, string attackerGroup, string? viewer)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Stacks.TryGetValue(attackerGroup, out var stack)
               && (viewer is null || stack.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Место боя для фона (<c>docs/battle-render.md</c>): укрепление — <c>CITY</c> (экран боя подменяет на
    /// <c>CAP_{раса владельца}</c> у столицы, <c>0x62d807</c>), руина — <c>RUIN</c>, в поле — вода <c>WATER</c>, иначе
    /// земля расы по клетке защитника (<c>Lterrain</c> 1…6). Ранг 3 для поля: где движок считает индекс, не найдено.</summary>
    public static string PlaceOf(MapState map, string defenderId)
    {
        var group = GroupOf(map, defenderId);
        if (group.Fort is { Kind: SiteKind.Capital } capital)
            return "CAP_" + RaceArt(map.Players.GetValueOrDefault(capital.Owner)?.RaceId ?? "");
        if (group.Fort is { Kind: SiteKind.Village }) return "CITY";
        if (group.Fort is { Kind: SiteKind.Ruin }) return "RUIN";
        var (x, y) = group.Stack is { } s ? (s.X, s.Y) : (0, 0);
        if (!map.Contains(x, y)) return "NEUTRAL";
        var tile = map.At(x, y);
        if (tile.Ground == Ground.Water) return "WATER";
        return tile.Terrain switch
        {
            1 => "HUMAN", 2 => "DWARF", 3 => "HERETIC", 4 => "UNDEAD", 6 => "ELF", _ => "NEUTRAL",
        };
    }

    /// <summary>Раса в именах фонов столицы (<c>CAP_HUMAN</c>…): по id расы в <c>Grace</c>.</summary>
    private static string RaceArt(string raceId) => raceId.ToUpperInvariant() switch
    {
        "G000RR0000" => "HUMAN", "G000RR0001" => "DWARF", "G000RR0002" => "HERETIC", "G000RR0003" => "UNDEAD",
        "G000RR0005" => "ELF", _ => "HUMAN",
    };

    /// <summary>Исход боя в карту: здоровье и опыт юнитов, павшие остаются в группе с нулём (их можно воскресить),
    /// призванные в бою исчезают, отряд без живых снимается с карты вместе с юнитами (визитёр — и из укрепления),
    /// у гарнизона и охраны без живых павшие удаляются. Жив ли лидер — по его юниту.</summary>
    public static MapState Apply(MapState map, BattleState battle)
    {
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var fighter in battle.Units)
        {
            if (!units.TryGetValue(fighter.Id, out var unit)) continue;   // призванный в бою — в карте его нет
            units[fighter.Id] = unit with { Hp = Math.Max(0, fighter.Hp), Xp = fighter.Xp };
        }
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        var sites = map.Sites.ToList();
        foreach (string group in new[] { battle.AttackerGroup, battle.DefenderGroup })
        {
            if (stacks.TryGetValue(group, out var stack))
            {
                bool anyAlive = stack.UnitIds.Any(id => units.TryGetValue(id, out var u) && u.Hp > 0);
                if (!anyAlive)
                {
                    stacks.Remove(group);
                    foreach (string id in stack.UnitIds) units.Remove(id);
                    if (plan.TryGetValue((stack.X, stack.Y), out var ids))
                        plan[(stack.X, stack.Y)] = [.. ids.Where(i => !i.Equals(group, StringComparison.OrdinalIgnoreCase))];
                    int at = sites.FindIndex(s => s.StackId.Equals(group, StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) sites[at] = sites[at] with { StackId = "" };
                    continue;
                }
                bool leaderAlive = units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0;
                if (leaderAlive != stack.LeaderAlive) stacks[group] = stack with { LeaderAlive = leaderAlive };
                continue;
            }
            int index = sites.FindIndex(s => s.Id.Equals(group, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            var site = sites[index];
            if (site.Guards.Any(id => units.TryGetValue(id, out var u) && u.Hp > 0)) continue;
            foreach (string id in site.Guards) units.Remove(id);
            sites[index] = site with { Guards = [], GuardSlots = [] };
        }
        return GameSession.With(map, stacks: stacks, units: units, plan: plan, sites: sites);
    }
}
