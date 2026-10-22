using D2.Data.Map;
using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>
/// Заклинание на карте (<c>docs/ai-spells.md</c> §3): проверка <c>0x5dbd75</c> и литьё — команда сервера <c>0x443600</c>
/// (результат по категории <c>0x443828</c>, <c>CEffectSpell</c> → <c>0x5dc2d6</c> оплата и запись, постэффекты урона,
/// лечения, призыва). Общее для окна заклинаний человека и ИИ.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Лорд-маг бросает одно заклинание дважды за день (<c>0x5dba61</c>, <c>[0x837760]</c>).</summary>
    private const int CastsPerDay = 1, CastsPerDayMage = 2;

    /// <summary>Площадь «вся карта» (<c>AREA</c> 999, <c>0x582b5a</c>).</summary>
    private const int WholeMap = 999;

    /// <summary>
    /// Почему заклинание нельзя бросить в клетку (<c>0x5dbd75</c>, шестой аргумент 0); null — можно. Порядок проверок —
    /// как у движка: игрок-нейтрал — можно всегда (<c>0x5e5358</c>); карта и туман (<c>0x5dab48</c>: клетка на карте и
    /// видна, развеивание — только под туман); знание, раз в день (магу — дважды) и казна (<c>0x5dba61</c>, при
    /// <paramref name="useSpellBank"/> — вместе со вторым кошельком); цель (<see cref="TargetRefusal"/>, <c>0x5db2cb</c>).
    /// </summary>
    public string? SpellRefusal(string playerId, string spellId, int x, int y, bool useSpellBank)
    {
        var book = RequireBook();
        if (AiTactics.NeutralOwner(this, playerId)) return null;
        if (!book.Spells.TryGetValue(spellId, out var spell)) return "нет такого заклинания";
        var player = Map.Players[playerId];
        if (spell.Area != WholeMap)
        {
            if (!Map.Contains(x, y)) return "клетка вне карты";
            bool fogged = Map.Fog.TryGetValue(player.FogId, out var fog) && fog[x, y];
            if (spell.Category == SpellCategory.Unfog ? !fogged : fogged)
                return spell.Category == SpellCategory.Unfog ? "клетка не под туманом" : "клетка под туманом";
        }
        if (!player.KnownSpells.Contains(spellId, StringComparer.OrdinalIgnoreCase)) return "не изучено";
        int today = Map.SpellCasts.Count(c => Same(c.PlayerId, playerId) && Same(c.Origin, spellId) && SpellCast.Live(c.TurnStop, Map.Turn));
        if (today >= (book.LordCategory(player.LordId) == Treasury.MageCategory ? CastsPerDayMage : CastsPerDay))
            return "уже брошено сегодня";
        if (!SpellCast.CanPay(player.Bank, player.SpellBank, spell, useSpellBank)) return "не хватает казны";
        return TargetRefusal(playerId, spell, x, y);
    }

    /// <summary>
    /// Цель (<c>0x5db2cb</c>): у площади (не 1 и не вся карта) центр не на крепости; туман и развеивание — любая клетка;
    /// призыв — клетка годна для отряда (<see cref="Placeable"/>); у шести категорий «на отряд» — хоть один отряд области
    /// (<see cref="Targets"/>) проходит <see cref="StackRefusal"/>, иначе первый отказ; нет отрядов — «нет цели».
    /// Смена земли — есть клетка, где она что-то меняет (<see cref="TerrainCells"/>, <c>0x5db4fc</c>; иначе отказ 5); снос жезла —
    /// в клетке жезл (<c>0x5db847</c>), не свой (<see cref="Rods.SpellRefusal"/>). ponytail: у смены земли без таких клеток движок
    /// ещё пропускает заклинание с флагом <c>CHNGTERCAT</c> при клетке своей земли (флаг ложен во всей ванили); обереги (в ванили
    /// заклинаний нет) цель не проверяют.
    /// </summary>
    private string? TargetRefusal(string playerId, Spell spell, int x, int y)
    {
        if (spell.Area > 1 && spell.Area != WholeMap
            && spell.Category is not (SpellCategory.Fog or SpellCategory.Unfog or SpellCategory.ChangeTerrain)
            && Map.Sites.Any(f => f.Kind is SiteKind.Capital or SiteKind.Village && Covers(f, x, y)))
            return "в центре площади крепость";
        if (spell.Category is SpellCategory.Fog or SpellCategory.Unfog) return null;
        if (spell.Category == SpellCategory.Summon) return Placeable(x, y, spell.SummonUnitId) ? null : "сюда нельзя призвать";
        if (spell.Category == SpellCategory.ChangeTerrain) return TerrainCells(spell, x, y).Count > 0 ? null : "земля здесь не меняется";
        if (spell.Category == SpellCategory.RemoveRod) return RodAt(x, y) is { } rod ? Rods.SpellRefusal(rod, playerId) : "нет жезла";
        // Обереги (0x5db2cb, категория [0x837808]): в клетке отряд заклинателя — можно, иначе отказ 6; площадь не смотрится.
        if (spell.Category == SpellCategory.GiveWards) return StackAt(x, y) is { } own && Same(own.Owner, playerId) ? null : "нет цели";
        if (!OnStack(spell.Category)) return null;
        string? first = null;
        foreach (var (stack, _) in Targets(playerId, spell, x, y, check: true))
        {
            if (StackRefusal(playerId, spell, stack) is not { } why) return null;
            first ??= why;
        }
        return first ?? "нет цели";
    }

    /// <summary>
    /// Годится ли отряд целью (<c>0x5dac42</c>). Вся карта — всегда. Невидимый: чужой — «нет цели», свой — отказ. Удар и
    /// ослабление — не свой и не союзник; лечение, усиление, ход, невидимость — свой (усиление — и союзник). Отряд в
    /// крепости — нет. Удар — хоть один живой юнит без иммунитета к источнику; лечение — есть кого лечить; ход — запас не
    /// полон; невидимость — ещё видим, лидер не иллюзия; усиление и ослабление — на лидере нет действующего литья этого заклинания (код 4, <see cref="CastOnLeader"/>), ослабление
    /// брони — у отряда есть броня (<see cref="ArmorOf"/>, код 5).
    /// </summary>
    private string? StackRefusal(string playerId, Spell spell, MapStack stack)
    {
        if (spell.Area == WholeMap) return null;
        bool own = Same(stack.Owner, playerId);
        if (Invisible(stack.Id)) return own ? "свой невидимый отряд" : "нет цели";
        bool ally = !own && AiTactics.Allied(this, playerId, stack.Owner);
        if (spell.Category is SpellCategory.Attack or SpellCategory.Lower)
        {
            if (own) return "свой отряд";
            if (ally) return "союзник";
        }
        else if (!own && !(spell.Category == SpellCategory.Boost && ally)) return "чужой отряд";
        if (stack.Inside.Length > 0) return "отряд в крепости";
        var living = stack.UnitIds.Select(id => Map.Units.GetValueOrDefault(id)).OfType<MapUnit>().Where(u => u.Hp > 0).ToList();
        return spell.Category switch
        {
            SpellCategory.Attack when !living.Any(u => !Immune(u, spell.DamageSource)) => "все иммунны",
            SpellCategory.Heal when living.Sum(u => HpMax(u) - u.Hp) <= 0 => "лечить некого",
            SpellCategory.RestoreMove when stack.Move >= DayAllowance(stack) => "запас хода полон",
            SpellCategory.Invisibility when IsIllusion(stack.LeaderId) => "лидер — иллюзия",
            SpellCategory.Boost or SpellCategory.Lower when CastOnLeader(stack.LeaderId, spell) => "уже наложено",
            SpellCategory.Lower when LowersArmor(spell) && ArmorOf(stack) == 0 => "брони нет",
            _ => null,
        };
    }

    /// <summary>Код 4 (<c>0x5db0b2</c>): в <c>CMidSpellEffects</c> карты (<c>0x5eb31c</c>, объект типа <c>0x1c</c>) есть запись «лидер — это
    /// заклинание — его модификатор» (поиск <c>0x5eae5e</c> → <c>0x5eb616</c>: <c>UNIT_ID</c>, <c>ORIGIN_ID</c>, <c>MODIF_ID</c>). Модификатор в
    /// цепочке юнита без такой записи (положен редактором, предметом, другим заклинанием) отказа не даёт: карта 19, полигон C3 — у лидера
    /// <c>S143KC0008</c> <c>g000um0021</c> из сценария, движок считает «Силу» годной (<c>attack_spell_pick</c> max 3, у порта было 2).</summary>
    private bool CastOnLeader(string leaderId, Spell spell) =>
        spell.ModifierId.Length > 0
        && Map.SpellEffects.Any(e => Same(e.UnitId, leaderId) && Same(e.OriginId, spell.Id) && Same(e.ModifierId, spell.ModifierId));

    /// <summary>Модификатор ослабления с элементом брони (<c>0x5db12a</c>: модификатор заклинания <c>0x5a5bd4</c> → <c>0x57f707</c>, слот
    /// <c>+0x1c</c> с флагом <c>0x400</c> — <c>ModifierElementTypeFlag::Armor</c>): только у такого ослабления есть «без эффекта».</summary>
    private bool LowersArmor(Spell spell) =>
        spell.ModifierId.Length > 0 && RequireBook().Rules.Modifiers?.GetValueOrDefault(spell.ModifierId) is { } modifier
        && modifier.Elements.Any(e => e.Type == ModifierElementType.Armor);

    /// <summary>Сумма брони юнитов отряда (<c>0x5db216</c>: по всем юнитам группы <c>+0x30</c>, и павшим, — <c>IUsSoldier::getArmor</c>, слот
    /// <c>+0x28</c>, с цепочкой модификаторов). Ноль — ослаблению брони нечего снижать: отказ 5, <c>X005TA0671</c> «не возымеет никакого
    /// эффекта» (клиент <c>0x4d35a5</c>). Журнал <c>20261016e-av</c>, карта 7: «Адские муки» (<c>G000UM0045</c>, броня −50) по Гному
    /// <c>S143KC0007</c> с бронёй 0 — отказ (seq 13976861), маны не берёт.</summary>
    private int ArmorOf(MapStack stack)
    {
        var rules = RequireBook().Rules;
        int total = 0;
        foreach (string id in stack.UnitIds)
        {
            if (!Map.Units.TryGetValue(id, out var unit)) continue;
            var soldier = MapBattle.Unit(unit, 0, rules, isAttacker: false);
            total += EffectiveStats.Of(soldier.Type, soldier.Modifiers).Armor;
        }
        return total;
    }

    /// <summary>
    /// Бросить заклинание (<c>0x443600</c>): проверка <see cref="SpellRefusal"/>; у играбельного игрока — оплата
    /// (<see cref="SpellCast.Pay(Bank, Bank, Spell, bool)"/>) и запись каста до завтра (<see cref="SpellCast.TurnStop"/>);
    /// затем эффект категории по отрядам области (<see cref="Targets"/>), сила — <c>SPLPWR</c> по расстоянию от центра:
    /// урон — живым юнитам без иммунитета к источнику (<c>L_ONCE</c> на карте защищает как <c>L_ALWAYS</c>, оберег не
    /// тратится), без брони и случайности, опыта нет, отряд с лидером-иллюзией не задевается, отряд без живых снимается;
    /// лечение — живым до наибольшего здоровья; призыв — новый отряд заклинателя (<see cref="Summon"/>); усиление и ослабление —
    /// модификатор со сроком (<see cref="ModifySpell"/>), ход — запас (<see cref="RestoreMoveSpell"/>), невидимость
    /// (<see cref="HideSpell"/>), развеивание тумана (<see cref="UnfogSpell"/>).
    /// Туман — <see cref="FogSpell"/>, смена земли — <see cref="TerrainSpell"/>, снос жезла — <see cref="RodSpell"/>, обереги —
    /// <see cref="WardSpell"/> (в ванили таких заклинаний нет, только моды).
    /// Возвращает отказ или null.
    /// </summary>
    /// <param name="summonTarget">Отряд, на который призванный получает приказ <c>L_ATTACK_STACK</c>; пусто — <c>L_NORMAL</c>.</param>
    public string? CastSpell(string playerId, string spellId, int x, int y, bool useSpellBank, string? summonTarget = null) =>
        Cast(playerId, spellId, x, y, useSpellBank, summonTarget, command: false).Refusal;

    /// <summary>
    /// Команда литья без предварительной проверки — как её разбирает сервер <c>0x443600</c> (ранг 1 по листингу, 28.09): цель
    /// проверяется только у категорий «на отряд» и прочих, кроме призыва, тумана, развеивания, сноса жезла и смены земли
    /// (<c>0x443745</c>; не прошла — ничего); полная проверка <see cref="SpellRefusal"/> стоит лишь в <c>CEffectSpell::apply</c>
    /// (<c>0x43e4a7</c> → <c>0x5dc2d6</c> → <c>0x5dbd75</c>, шестой аргумент 1) и решает только оплату и запись: результат <c>0x5dc2d6</c>
    /// там не читается (<c>0x43e575</c>), apply всегда возвращает 1, а эффект результата кладётся в список отдельно, слотом +4
    /// (у смены земли <c>CSpellResultChangeTerrain</c>, vtable <c>0x6cdb4c</c>: <c>0x41cdd5</c> → <c>CEffectChangeTerrain</c>
    /// <c>0x43591d</c>, apply <c>0x4359a7</c> казну не смотрит). Так ИИ у своего жезла (<c>0x46f1d1</c>) меняет землю и без казны.
    /// </summary>
    /// <returns><c>Refusal</c> — отказ цели (эффекта нет); <c>Unpaid</c> — отказ проверки, при котором эффект лёг без оплаты и записи.</returns>
    public (string? Refusal, string? Unpaid) CastCommand(string playerId, string spellId, int x, int y, bool useSpellBank) =>
        Cast(playerId, spellId, x, y, useSpellBank, summonTarget: null, command: true);

    private (string? Refusal, string? Unpaid) Cast(string playerId, string spellId, int x, int y, bool useSpellBank, string? summonTarget, bool command)
    {
        var book = RequireBook();
        if (!book.Spells.TryGetValue(spellId, out var spell)) return ("нет такого заклинания", null);
        // Призыв проверяется и ставится в клетке постановки (0x443600: pos' = результат +0xc).
        if (spell.Category == SpellCategory.Summon)
        {
            if (SummonCell(x, y, spell.SummonUnitId) is not { } cell) return ("некуда призвать", null);
            (x, y) = cell;
        }
        string? why = SpellRefusal(playerId, spellId, x, y, useSpellBank);
        if (why is not null && !command) return (why, null);
        if (why is not null && spell.Category is not (SpellCategory.Summon or SpellCategory.Fog or SpellCategory.Unfog
                                                      or SpellCategory.RemoveRod or SpellCategory.ChangeTerrain)
            && TargetRefusal(playerId, spell, x, y) is { } target) return (target, null);
        var map = Map;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var casts = map.SpellCasts;
        if (why is null && !AiTactics.NeutralOwner(this, playerId))
        {
            var (bank, spellBank) = SpellCast.Pay(players[playerId].Bank, players[playerId].SpellBank, spell, useSpellBank);
            players[playerId] = players[playerId] with { Bank = bank, SpellBank = spellBank };
            casts = [.. casts, new CastRecord(playerId, spell.Id, SpellCast.TurnStop(map.Turn))];
        }
        State = State with { Looks = Retold(Looks, With(map, players: players, spellCasts: casts), Looks.Stacks) };
        Observer?.Happened(new TurnSight(TurnSightKind.Spell, playerId, x, y, spell.Id));
        ApplySpell(playerId, spell, x, y, summonTarget);
        return (null, why);
    }

    /// <summary>Эффект заклинания в клетке — общий для литья (<see cref="Cast"/>) и свитка/посоха (<see cref="CastItem"/>): у движка оба
    /// идут через одну фабрику результатов (<c>0x443600</c> и <c>0x443b76</c> → <c>0x443828</c>).</summary>
    private void ApplySpell(string playerId, Spell spell, int x, int y, string? summonTarget)
    {
        switch (spell.Category)
        {
            case SpellCategory.Attack or SpellCategory.Heal:
                Strike(playerId, spell, x, y);
                break;
            case SpellCategory.Summon:
                Summon(playerId, spell.SummonUnitId, (x, y), summonTarget);
                break;
            case SpellCategory.Boost or SpellCategory.Lower:
                ModifySpell(playerId, spell, x, y);
                break;
            case SpellCategory.RestoreMove:
                RestoreMoveSpell(playerId, spell, x, y);
                break;
            case SpellCategory.Invisibility:
                HideSpell(playerId, spell, x, y);
                break;
            case SpellCategory.Unfog:
                UnfogSpell(playerId, spell, x, y);
                break;
            case SpellCategory.Fog:
                FogSpell(playerId, spell, x, y);
                break;
            case SpellCategory.ChangeTerrain:
                TerrainSpell(spell, x, y);
                break;
            case SpellCategory.RemoveRod:
                RodSpell(x, y);
                break;
            case SpellCategory.GiveWards:
                WardSpell(playerId, spell, x, y);
                break;
        }
    }

    /// <summary>Отряд в клетке (<c>0x5f54e2</c> с типом id 0x23) — первый отряд плана клетки.</summary>
    private MapStack? StackAt(int x, int y)
    {
        if (!Map.Plan.TryGetValue((x, y), out var ids)) return null;
        foreach (string id in ids)
            if (Map.Stacks.TryGetValue(id, out var stack)) return stack;
        return null;
    }

    /// <summary>Урон или лечение по отрядам области (<c>0x41c05c</c>, <c>0x41cbd1</c>).</summary>
    private void Strike(string playerId, Spell spell, int x, int y)
    {
        var book = RequireBook();
        var map = Map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var hit = new List<string>();
        foreach (var (stack, dist) in Targets(playerId, spell, x, y, check: false).ToList())
        {
            int power = book.SpellPower[Math.Min(dist, book.SpellPower.Count - 1)];
            if (spell.Category == SpellCategory.Attack && IsIllusion(stack.LeaderId)) continue;
            hit.Add(stack.Id);
            foreach (string id in stack.UnitIds)
            {
                if (!units.TryGetValue(id, out var u) || u.Hp <= 0) continue;
                if (spell.Category == SpellCategory.Attack)
                {
                    if (Immune(u, spell.DamageSource)) continue;
                    units[id] = u with { Hp = Math.Max(0, u.Hp - spell.DamageQty * power / 100) };
                }
                else units[id] = u with { Hp = Math.Min(HpMax(u), u.Hp + spell.HealQty * power / 100) };
            }
        }
        map = With(map, units: units);
        // Погибшие от заклинания (CSpellResultAttack 0x41c1e7): убийцы нет — вещи мешком или в склад крепости (GameSession.Loot.cs).
        var bags = new List<D2.Data.Map.BagLook>();
        foreach (string id in hit)
        {
            if (!map.Stacks.TryGetValue(id, out var dead) || dead.UnitIds.Any(u => map.Units.TryGetValue(u, out var mu) && mu.Hp > 0)) continue;
            var loot = Loot(map, dead, "");
            map = loot.Map;
            if (loot.Bag is { } bag) bags.Add(bag);
        }
        map = WithoutDead(map, hit);
        State = State with
        {
            Looks = Retold(Looks, map, [.. Looks.Stacks.Where(s => map.Stacks.ContainsKey(s.Id))]) with { Bags = [.. Looks.Bags, .. bags] },
        };
    }

    /// <summary>
    /// Призыв (<c>0x41c259</c>/<c>0x41c323</c>): новый отряд заклинателя с юнитом <paramref name="unitType"/> в клетке
    /// постановки <paramref name="at"/> (<see cref="SummonCell"/>), с полным запасом хода; приказ — <c>L_ATTACK_STACK</c> на <paramref name="target"/>, если он
    /// задан, иначе <c>L_NORMAL</c> (у нейтрала — <c>L_BEZERK</c>). Юнит создан сегодня (<see cref="MapUnit.Creation"/> — день карты):
    /// в начале хода заклинателя через <c>LIFE_TIME</c> дней отряд уходит (<see cref="ExpireSummons"/>). Клетки нет — ничего.
    /// </summary>
    internal void Summon(string playerId, string unitType, (int X, int Y) at, string? target)
    {
        var book = RequireBook();
        var map = Map;
        if (!book.Rules.Units.TryGetValue(unitType, out var type) || book.Grown(unitType, type.Level) is not { } grown) return;
        var ids = map.IdNext;
        string unitId = NewId(ref ids, map.Units.Keys, playerId, "UN");
        string stackId = NewId(ref ids, map.Stacks.Keys, playerId, "KC");
        int[] slots = LeaderSlots(grown.Type, book.Rules);   // CVisitorCreateStack → 0x60cffa, как у найма лидера
        string subrace = map.Sites.FirstOrDefault(f => f.Kind == SiteKind.Capital && Same(f.Owner, playerId))?.Subrace
                         ?? map.Stacks.Values.FirstOrDefault(s => Same(s.Owner, playerId))?.Subrace ?? "";
        bool neutral = AiTactics.NeutralOwner(this, playerId);
        var order = target is { Length: > 0 } ? StackOrder.AttackStack : neutral ? StackOrder.Bezerk : StackOrder.Normal;
        var stack = new MapStack
        {
            Id = stackId, Owner = playerId, Subrace = subrace, LeaderId = unitId, LeaderAlive = true, X = at.X, Y = at.Y,
            Inside = "", UnitIds = [unitId], Slots = slots, Order = (int)order, OrderTarget = target ?? "", AiOrder = (int)StackOrder.Normal,
        };
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = new MapUnit
        {
            Id = unitId, TypeId = type.Id.ToUpperInvariant(), Level = grown.Level, Hp = grown.Type.HitPoints, Creation = map.Turn,
        };
        stack = stack with { Move = Allowance(With(map, units: units), stack) };
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack;
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[at] = [.. plan.GetValueOrDefault(at) ?? [], stackId];
        var look = new StackLook(stackId, at.X, at.Y, playerId, subrace, type.Id.ToUpperInvariant(), LeaderAlive: true, AnyAlive: true,
                                 Facing: 4, Inside: "", Invisible: false);
        State = State with { Looks = Retold(Looks, With(map, units: units, stacks: stacks, plan: plan, idNext: ids), [.. Looks.Stacks, look]) };
        // Обзор призванного — сразу (0x41c506: у игрока не нейтральной расы, 0x5e5358, эффект тумана 0x4382a8 в клетке постановки
        // с обзором лидера-призыва, 0x41c53f → 0x438314 → 0x5d5230). Карта 12, E03d (t01-034): 32 клетки у (14,38) открыты до следующего хода.
        // Маска меняется на месте, как у Reveal: зовут на живой сессии (литьё, эффект события).
        if (!neutral) RevealAround(Map, Map.Stacks[stackId], at.X, at.Y);
    }

    /// <summary>
    /// Уход призванных в начале хода владельца (<c>0x443ca2</c> → <c>0x444631</c>, ранг 1 по листингу): <c>0x5d3dab</c> собирает отряды
    /// игрока, чей лидер — призыв (реализация с <c>IUsSummon</c>, <c>0x431d8c</c>: её строит загрузчик <c>Gunits</c> <c>0x5889f3</c> ровно
    /// для <c>UNIT_CAT</c> <c>L_SUMMON</c> — <c>0x5a1aa9</c>, читает <c>LIFE_TIME</c> — и <c>L_ILLUSION</c> — <c>0x5a192e</c>, её наследник; ранг 1),
    /// а у юнита первой клетки группы день создания (<c>CMidUnit +8</c>) плюс <c>LIFE_TIME</c> (слот 2 <c>IUsSummon</c>) не больше текущего
    /// дня. Защиты от <c>LIFE_TIME</c> 0 у движка нет: такой призыв (в <c>Gunits</c> — <c>g000uu8051</c>) уходит в ближайшее начало хода
    /// хозяина, так и здесь. Каждый гибнет без убийцы (<c>0x4413bc</c> → <c>CEffectKillStack</c> <c>0x4388e4</c>, применение <c>0x43895c</c>:
    /// запись в уничтоженные <c>0x5e6f63</c>, по ней <c>L_KILL_STACK</c>; могилы у призыва нет — <c>0x5f9042</c> только без <c>IUsSummon</c>).
    /// Записи заклинаний на ушедших юнитах остаются до срока и снимаются <see cref="ExpireEffects"/> (юнита нет — запись уходит).
    /// Эффект ставится после регенерации (<c>CEffectRegenUnits</c>), поэтому в журнале у отряда ещё есть <c>ap_restore</c> начала хода.
    /// Следствие для сохранений порта до 13.10 (<see cref="MapUnit.Creation"/> не писался, читается 0): их призванные уходят в ближайшее
    /// начало хода хозяина — у призывов (<c>LIFE_TIME</c> 1) это и так следующий день, иллюзия (3) уйдёт раньше срока. 20261014-replay: Ent KC0094 с карты (создание 0, <c>LIFE_TIME</c> 1) —
    /// <c>ap_restore</c> в начале хода Эльфов дня 1 (r2a seq 240630, r2b 2045422) и больше ни одной строки; призыв Нежити KC00a5
    /// (<c>G000SS0031</c>, день 8, создание 8) — <c>ap_restore</c> дня 9 (seq 2282826) и исчез. Прежде порт держал призванных вечно и
    /// строил им тактику.
    /// </summary>
    internal MapState ExpireSummons(MapState map, string playerId)
    {
        var book = Book!;
        var gone = map.Stacks.Values
            .Where(s => Same(s.Owner, playerId) && map.Units.TryGetValue(s.LeaderId, out var leader)
                        && book.CategoryOf(leader.TypeId) is UnitCategory.Summon or UnitCategory.Illusion
                        && map.Units.TryGetValue(s.UnitIds.FirstOrDefault() ?? s.LeaderId, out var first)
                        && first.Creation + book.LifeTimeOf(first.TypeId) <= map.Turn)
            .Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (gone.Count == 0) return map;
        var dead = gone.SelectMany(id => map.Stacks[id].UnitIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (cell, ids) in map.Plan)
            if (ids.Any(gone.Contains)) plan[cell] = [.. ids.Where(id => !gone.Contains(id))];
        var after = With(map,
            stacks: map.Stacks.Where(p => !gone.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            units: map.Units.Where(p => !dead.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            sites: [.. map.Sites.Select(s => gone.Contains(s.StackId) ? s with { StackId = "" } : s)],
            plan: plan);
        return WithDestroyed(after, [.. map.Destroyed, .. gone.Select(id => new DestroyedStack(id, "", map.Stacks[id].TemplateId))]);
    }

    /// <summary>
    /// Клетка для призванного отряда (<c>0x5edded</c>, ранг 1). Три прохода; границы квадрата копятся (прибавляется номер
    /// прохода к уже расширенным), поэтому кольца — радиусов 0, 1 и 3. Клетки кольца кладутся в список по x, внутри по y
    /// (<c>push_back</c> <c>0x524c10</c>); при двух и больше последняя переносится в начало (<c>0x5edf3f</c> —
    /// <c>push_front</c> её копии, <c>0x5edf64</c> — <c>pop_back</c>), затем проверяются С КОНЦА (<c>back</c>
    /// <c>0x43a220</c>, неудача — <c>pop_back</c>) до первой годной (<see cref="Placeable"/>). Бросков нет. Нет — null.
    /// </summary>
    /// <param name="unitType">Тип призываемого: «только по воде» ставится только на воду; пусто — без этой проверки.</param>
    public (int X, int Y)? SummonCell(int x, int y, string unitType = "")
    {
        int reach = 0;
        for (int pass = 0; pass < SummonPasses; pass++)
        {
            reach += pass;
            var ring = new List<(int X, int Y)>();
            for (int cx = x - reach; cx <= x + reach; cx++)
                for (int cy = y - reach; cy <= y + reach; cy++)
                    if (Math.Max(Math.Abs(cx - x), Math.Abs(cy - y)) == reach) ring.Add((cx, cy));
            if (ring.Count > 1) { ring.Insert(0, ring[^1]); ring.RemoveAt(ring.Count - 1); }
            for (int i = ring.Count - 1; i >= 0; i--)
                if (Placeable(ring[i].X, ring[i].Y, unitType)) return ring[i];
        }
        return null;
    }

    /// <summary>Проходов поиска клетки призыва (<c>cmp esi, 3</c> у <c>0x5eded5</c>).</summary>
    private const int SummonPasses = 3;

    /// <summary>
    /// Годна ли клетка для нового отряда (<c>0x5e6db2</c> → <c>CVisitorCreateStack</c>, vtable <c>0x6f0824</c>, слот
    /// <c>0x60ceaf</c>; ранг 1): цена хода в неё <c>computeMovementCost</c> (<c>0x6027f3</c>) без своего отряда
    /// (<c>stackId</c> пуст, маски <c>a6 = a7 = 0</c>, живой лидер, бонусы всех трёх грунтов,
    /// <c>forbidWaterOnlyOnLand</c> = 1) не 0: клетка на карте, не гора, нет крепости, ориентира, места, руины, жезла и
    /// кристалла (<see cref="Movement.Cost"/>), нет мешка и никакого отряда. Вода и дорога годятся. Призванный «только по
    /// воде» (слот <c>0x70</c> реализации — <see cref="GameBook.WaterOnlyOf"/>) ставится ещё только на воду (<c>0x60cf85</c> →
    /// <c>0x5e6453</c>).
    /// </summary>
    private bool Placeable(int x, int y, string unitType = "")
    {
        if (Movement.Cost(Map, x, y, SummonPlacing, MovementRules.Vanilla) == Movement.Forbidden) return false;
        if (unitType.Length > 0 && RequireBook().WaterOnlyOf(unitType) && !IsWater(x, y)) return false;
        return !(Map.Plan.TryGetValue((x, y), out var ids)
                 && ids.Any(id => id.Length >= 6 && id.Substring(4, 2).ToUpperInvariant() is "BG" or "KC"));
    }

    /// <summary>Флаги <c>computeMovementCost</c> у <c>0x60cee7</c>: живой лидер, бонусы равнины, леса и воды.</summary>
    private static readonly MoveOptions SummonPlacing = new(LeaderAlive: true, PlainsBonus: true, ForestBonus: true,
                                                            WaterBonus: true, WaterOnly: false, ForbidWaterOnLand: true);

    /// <summary>
    /// Отряды области с расстоянием от центра (<c>0x41bd90</c>, <c>0x5eda0b</c>): одна клетка — отряд в ней; площадь —
    /// квадрат со стороной <c>AREA</c> без стоящих в крепости, все хозяева; вся карта — для литья отряды заклинателя
    /// (и в крепостях), для проверки — любые.
    /// </summary>
    private IEnumerable<(MapStack Stack, int Dist)> Targets(string playerId, Spell spell, int x, int y, bool check)
    {
        if (spell.Area == 1)
        {
            if (Map.Plan.TryGetValue((x, y), out var ids))
                foreach (string id in ids)
                    if (Map.Stacks.TryGetValue(id, out var s)) { yield return (s, 0); yield break; }
            yield break;
        }
        if (spell.Area == WholeMap)
        {
            foreach (var s in Map.Stacks.Values.Where(s => check || Same(s.Owner, playerId)).OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
                yield return (s, 0);
            yield break;
        }
        int r = spell.AreaRadius;
        foreach (var s in Map.Stacks.Values.Where(s => s.Inside.Length == 0 && Math.Abs(s.X - x) <= r && Math.Abs(s.Y - y) <= r)
                                           .OrderBy(s => s.Y).ThenBy(s => s.X))
            yield return (s, Math.Max(Math.Abs(s.X - x), Math.Abs(s.Y - y)));
    }

    private static bool OnStack(SpellCategory c) => c is SpellCategory.Attack or SpellCategory.Lower or SpellCategory.Heal
        or SpellCategory.Boost or SpellCategory.RestoreMove or SpellCategory.Invisibility;

    /// <summary>Иммунитет юнита к источнику урона заклинания (<c>IUsSoldier +0x44</c>): не <c>L_NOTIMMUNE</c>.</summary>
    private bool Immune(MapUnit unit, int source) =>
        Immunity.LevelsFor(MapBattle.Unit(unit, 0, RequireBook().Rules, isAttacker: false), source, AttackClass.Damage).BySource
        != ImmunityLevel.NotImmune;

    private bool IsIllusion(string unitId) =>
        Map.Units.TryGetValue(unitId, out var u) && RequireBook().CategoryOf(u.TypeId) == UnitCategory.Illusion;

    /// <summary>Отряд невидим (<c>INVISIBLE</c>, <c>+0x9b</c>): есть облик с этим id (без регистра) и флагом. Ход ИИ спрашивает
    /// про каждого кандидата в цели каждого отряда, поэтому id невидимых собраны во множество на текущий список обликов: список
    /// не меняется на месте, всякая правка ставит новый (<see cref="Retold"/>, <c>with</c>), и смена ссылки пересобирает множество.</summary>
    internal bool Invisible(string stackId)
    {
        var stacks = Looks.Stacks;
        var known = _invisible;
        if (known is null || !ReferenceEquals(known.Of, stacks))
            _invisible = known = new InvisibleIds(stacks, stacks.Where(s => s.Invisible).Select(s => s.Id)
                                                                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        return known.Ids.Contains(stackId);
    }

    /// <summary>Id невидимых отрядов списка обликов <paramref name="Of"/> (сравнение как у <see cref="Same"/>).</summary>
    private sealed record InvisibleIds(IReadOnlyList<StackLook> Of, HashSet<string> Ids);

    private InvisibleIds? _invisible;

    private static bool Covers(MapSite fort, int x, int y)
    {
        int size = fort.Kind == SiteKind.Capital ? 5 : 4;
        return x >= fort.X && y >= fort.Y && x < fort.X + size && y < fort.Y + size;
    }

    /// <summary>Отряды из <paramref name="ids"/> без живых юнитов снимаются с карты (как после боя).</summary>
    private static MapState WithoutDead(MapState map, IEnumerable<string> ids)
    {
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        var sites = map.Sites.ToList();
        bool changed = false;
        foreach (string group in ids)
        {
            if (!stacks.TryGetValue(group, out var stack)) continue;
            if (stack.UnitIds.Any(id => units.TryGetValue(id, out var u) && u.Hp > 0))
            {
                bool leaderAlive = units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0;
                if (leaderAlive != stack.LeaderAlive) { stacks[group] = stack with { LeaderAlive = leaderAlive }; changed = true; }
                continue;
            }
            changed = true;
            stacks.Remove(group);
            foreach (string id in stack.UnitIds) units.Remove(id);
            if (plan.TryGetValue((stack.X, stack.Y), out var cell)) plan[(stack.X, stack.Y)] = [.. cell.Where(i => !Same(i, group))];
            int at = sites.FindIndex(s => Same(s.StackId, group));
            if (at >= 0) sites[at] = sites[at] with { StackId = "" };
        }
        return changed ? With(map, stacks: stacks, units: units, plan: plan, sites: sites) : map;
    }
}
