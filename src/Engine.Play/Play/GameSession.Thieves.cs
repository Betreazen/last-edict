using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Rng;

namespace D2.Data.Play;

/// <summary>Итог действия вора: бросок, успех, списанные очки хода, гибель вора, величина (отравление — процент, кража
/// золота — сумма) и что взято (экземпляр предмета, заклинание). <see cref="Refused"/> непусто — действия не было.</summary>
public sealed record ThiefReport(ThiefAction Action, string Target, string? Refused, ThiefRoll Roll = default,
                                 bool Success = false, int Cost = 0, bool ThiefDied = false, int Value = 0, string Taken = "");

/// <summary>
/// Действие вора на карте — общее для человека (окно вора) и ИИ (<see cref="AiTactics.ThiefVisit"/>). Правила — <see cref="Thieves"/>
/// (Ф7 задача 12, <c>docs/ai-tactic-candidates.md</c> §12): доступность <c>0x5d32b8</c> по лорду игрока-вора, порог
/// <c>CHANCE</c> минус сопротивление цели, два броска R250 всегда, провал — вор гибнет, успех — <c>ceil(MOVE/2)</c> очков хода
/// и <c>D_NOBLE</c> к отношениям с играбельным владельцем отряда или крепости.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Правила вора из <c>data/rules</c> (<c>Gaction</c>, <c>GVars</c>).</summary>
    private ThiefRules ThiefRules => RequireBook().Thieves;

    /// <summary>Вид цели вора по объекту: отряд, крепость, руина, торговец, башня мага; прочее — null.</summary>
    public ThiefTargetKind? ThiefKindOf(string targetId)
    {
        var map = Map;
        if (map.Stacks.ContainsKey(targetId)) return ThiefTargetKind.Stack;
        return map.Sites.FirstOrDefault(s => Same(s.Id, targetId))?.Kind switch
        {
            SiteKind.Capital or SiteKind.Village => ThiefTargetKind.Fortification,
            SiteKind.Ruin => ThiefTargetKind.Ruin,
            SiteKind.Merchant => ThiefTargetKind.Merchant,
            SiteKind.Mage => ThiefTargetKind.MageTower,
            _ => null,
        };
    }

    /// <summary>Живых юнитов группы — <c>0x5f50fd</c> → <c>0x5f509a</c>: обход группы (<c>0x5f4441</c>/<c>0x5f44b9</c>) со счётом тех, у кого
    /// здоровье больше нуля (<c>0x5e98ad</c>: <c>cmp [unit+0x20], 0; setg</c>). Им фильтр действий вора решает «подложный приказ» у отряда
    /// (<c>0x5d36a4</c>, больше одного) и «гарнизон непуст» у укрепления (<c>0x5d38e2</c>). Ранг 1 по листингу; в корпусе случая, где счёт
    /// с павшими и без них дал бы иной список (<c>noble_choose</c>), нет — прежде порт считал и павших (реестр И150).</summary>
    private int AliveCount(IEnumerable<string> unitIds) => unitIds.Count(u => Map.Units.TryGetValue(u, out var unit) && unit.Hp > 0);

    /// <summary>
    /// Признаки цели для <see cref="Thieves.Available"/> глазами игрока <paramref name="playerId"/>. Ранг 2: мятеж возможен —
    /// деревня, которая не бунтует (<c>0x5e89e3</c> не разобрана); «есть что украсть из заклинаний» — <see cref="StealableSpells"/>
    /// не пуст. null — у цели нет действий вора.
    /// </summary>
    public ThiefTargetState? ThiefTarget(string playerId, string targetId)
    {
        var map = Map;
        if (ThiefKindOf(targetId) is not { } kind) return null;
        if (kind == ThiefTargetKind.Stack)
        {
            var s = map.Stacks[targetId];
            var fort = s.Inside.Length > 0 ? map.Sites.FirstOrDefault(f => Same(f.Id, s.Inside)) : null;
            return new ThiefTargetState(kind)
            {
                OwnerPlayable = !AiTactics.NeutralOwner(this, s.Owner),
                OwnerSpied = Spied(playerId, s.Owner),
                OwnerAllied = AiTactics.Allied(this, playerId, s.Owner),
                HasUnitAboveOneHp = s.UnitIds.Any(u => map.Units.TryGetValue(u, out var unit) && unit.Hp > 1),
                HasLeader = s.LeaderAlive,
                UnitCount = AliveCount(s.UnitIds),
                ItemCount = s.Items.Count,
                RiotPossible = fort is { Kind: SiteKind.Village } && !Fortifications.Rioting(fort.RiotTurn),
            };
        }
        var site = map.Sites.First(f => Same(f.Id, targetId));
        switch (kind)
        {
            case ThiefTargetKind.Fortification:
            {
                bool rioting = site.Kind == SiteKind.Village && Fortifications.Rioting(site.RiotTurn);
                var thief = map.Players.GetValueOrDefault(playerId);
                var owner = map.Players.GetValueOrDefault(site.Owner);
                return new ThiefTargetState(kind)
                {
                    OwnerPlayable = !AiTactics.NeutralOwner(this, site.Owner),
                    OwnerSpied = Spied(playerId, site.Owner),
                    OwnerAllied = AiTactics.Allied(this, playerId, site.Owner),
                    Rioting = rioting,
                    RiotPossible = site.Kind == SiteKind.Village && !rioting,
                    HasVisitor = site.StackId.Length > 0,
                    UnitCount = AliveCount(site.Guards),
                    HasUnitAboveOneHp = site.Guards.Any(u => map.Units.TryGetValue(u, out var unit) && unit.Hp > 1),
                    IsVillage = site.Kind == SiteKind.Village,
                    HasSpellToSteal = thief is not null && StealableSpells(playerId, site.Owner).Count > 0,
                    BribeCost = BribeCost(site),
                };
            }
            case ThiefTargetKind.Ruin:
                // 0x5d35a6: осмотр — пока игрока нет среди посетивших руину (0x5fda67: поиск в наборе +0x84; +0x90/+0x98 — конец набора, а
                // не разграбивший — тот в +0x80). Прежде порт смотрел разграбившего, и вор мог осматривать одну руину без конца.
                // Разграбленная руина — как и прежде, без осмотра: флаг [ebp+0x14] 0x5d35a6 не разобран, а карточка у неё — X005TA0186 (ранг 2).
                return new ThiefTargetState(kind)
                {
                    RuinVisited = site.LooterId.Length > 0 || site.Visitors.Contains(playerId, StringComparer.OrdinalIgnoreCase),
                };
            case ThiefTargetKind.Merchant:
                return new ThiefTargetState(kind) { Stocked = site.Stock.Any(r => r.Count > 0) };
            default:
                return new ThiefTargetState(kind) { Stocked = site.Spells.Count > 0 };
        }
    }

    /// <summary>Игрок <paramref name="owner"/> уже разведан игроком <paramref name="playerId"/> (<see cref="MapPlayer.Spied"/>).</summary>
    private bool Spied(string playerId, string owner) =>
        Map.Players.TryGetValue(playerId, out var p) && p.Spied.Contains(owner, StringComparer.OrdinalIgnoreCase);

    /// <summary>Цена подкупа (<c>0x5d3ca7</c>, <see cref="Thieves.BribeCost"/>): уровень деревни × <c>BRIBE</c> × Σ <c>XP_KILLED</c>
    /// гарнизона и визитёра.</summary>
    private int BribeCost(MapSite fort)
    {
        var map = Map;
        var rules = RequireBook().Rules;
        var ids = fort.Guards.Concat(map.Stacks.TryGetValue(fort.StackId, out var guest) ? guest.UnitIds : []);
        int sum = ids.Where(map.Units.ContainsKey).Sum(id => MapBattle.Unit(map.Units[id], 0, rules, isAttacker: false).Type.XpKilled);
        return Thieves.BribeCost(ThiefRules, fort.Size ?? 1, sum);
    }

    /// <summary>Действия вора отряда <paramref name="stackId"/> против цели — <see cref="Thieves.Available"/> по лорду его игрока
    /// (Дипломат — глава гильдии). Против невидимого отряда действий нет: нападение <c>0x440667</c> берёт флаг невидимости цели на момент
    /// команды и с ним вместо выбора вора ведёт в бой (<c>0x4407c2</c>).</summary>
    public IReadOnlyList<ThiefAction> ThiefActions(string stackId, string targetId)
    {
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var thief) || Invisible(targetId)
            || ThiefTarget(thief.Owner, targetId) is not { } target) return [];
        var player = map.Players[thief.Owner];
        return Thieves.Available(LordOf(player), target, (int)Math.Min(player.Bank.Gold, int.MaxValue));
    }

    private LordCategory LordOf(MapPlayer player) => (LordCategory)Math.Max(RequireBook().LordCategory(player.LordId), 0);

    /// <summary>Порог действия (<see cref="Thieves.Chance"/>): сопротивление отряда — <c>NEGOTIATE</c> его лидера, крепости —
    /// мораль уровня; у руины и лавок сопротивления нет.</summary>
    public int ThiefChance(string targetId, ThiefAction action)
    {
        var map = Map;
        int resistance = 0;
        if (map.Stacks.TryGetValue(targetId, out var s) && map.Units.TryGetValue(s.LeaderId, out var leader))
            resistance = Thieves.StackResistance(RequireBook().NegotiateOf(leader.TypeId, leader.Level));
        else if (map.Sites.FirstOrDefault(f => Same(f.Id, targetId)) is { Kind: SiteKind.Capital or SiteKind.Village } fort)
            resistance = Thieves.FortResistance(ThiefRules, fort);
        return Thieves.Chance(ThiefRules, action, resistance);
    }

    /// <summary>
    /// Действие вора (<c>0x443130</c>): проверка доступности, порог и бросок (<see cref="Thieves.Perform"/>), провал — вор гибнет
    /// (<c>CEffectKillStack</c>), успех — очки хода, отношения и исход действия:
    /// <list type="bullet">
    /// <item>отравление отряда и города — ⌊% текущего здоровья⌋ (<c>POISON_S</c>/<c>POISON_C</c>), город — гарнизон и визитёр;</item>
    /// <item>дуэль — бой вора с лидером цели, прочие юниты в бою не участвуют (<see cref="StartBattle"/> с дуэлью);</item>
    /// <item>кража у торговца — экземпляр склада (<paramref name="pick"/> — тип; иначе самый дорогой, как выбирает ИИ);
    /// кража предмета — экземпляр сумки цели; кража заклинания — заклинание владельца;</item>
    /// <item>кража золота — сумма <c>rand(X) + 1</c> бросается и при провале; подкуп — золото и захват крепости вором;
    /// мятеж — <c>RIOT_T</c> по MSVCRT <c>rand</c>;</item>
    /// <item>шпионаж — только знание (окно);</item>
    /// <item>убийство (<see cref="Assassinate"/>), подложный приказ (<see cref="Misfit"/>), кража у мага (<see cref="StealFromMage"/>).</item>
    /// </list>
    /// </summary>
    public ThiefReport Thief(string stackId, string targetId, ThiefAction action, IRng rng, string? pick = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var thief)) return new ThiefReport(action, targetId, $"нет отряда {stackId}");
        if (!ThiefActions(stackId, targetId).Contains(action)) return new ThiefReport(action, targetId, $"{action} против {targetId} недоступно");
        Unhide(stackId);   // вор у цели (0x4406bf, 0x440a53, 0x440b59); цель-отряд здесь видима — невидимой действий нет (ThiefActions)
        var kind = ThiefKindOf(targetId)!.Value;
        int chance = ThiefChance(targetId, action);
        var outcome = Thieves.Perform(ThiefRules, rng, action, chance, DayAllowance(thief), thief.Move);
        int value = 0;
        if (action == ThiefAction.StealGold)
        {
            var fort = map.Sites.First(f => Same(f.Id, targetId));
            var owner = map.Players[fort.Owner];
            value = Thieves.StealGold(rng, Thieves.StealGoldMax(ThiefRules, (int)Math.Min(owner.Bank.Gold, int.MaxValue), !AiTactics.NeutralOwner(this, owner.Id)));
        }
        if (outcome.ThiefDies)
        {
            KillStack(stackId);
            RunActionEvents(stackId);   // 0x41e6e6: проверка событий после действия вора (погибший — без триггерера)
            return new ThiefReport(action, targetId, null, outcome.Roll, false, 0, true, value);
        }
        SetMove(stackId, outcome.MoveLeft);
        NobleRelations(thief.Owner, targetId, kind);
        string taken = Apply(thief, targetId, action, ref value, pick, rng);
        RunActionEvents(stackId);   // 0x41e6e6
        return new ThiefReport(action, targetId, null, outcome.Roll, true, outcome.Cost, false, value, taken);
    }

    /// <summary>Вор подошёл к отряду, у лидера которого <c>L_INCORRUPTIBLE</c> (<see cref="Thieves.DiesBeforeDialog"/>,
    /// <c>0x442e85</c>): гибнет до свитка и без броска. Убийца — отряд-цель (<c>CEffectKillStack</c> <c>0x442f00</c>): вещи вора — в конец
    /// его сумки, сообщение 44 — его хозяину. true — погиб. Это первый шаг вора у цели (ход ИИ, окно человека): до него невидимый вор
    /// показывается (<c>0x4406bf</c>, <c>0x440a53</c>, <c>0x440b59</c>). Цель-отряд невидима — выбора вора нет, будет бой (<c>0x4407c2</c>,
    /// <see cref="ThiefActions"/>): гибели до свитка нет, а цель покажет <see cref="StartBattle"/>.</summary>
    public bool ThiefIncorruptible(string stackId, string targetId)
    {
        Unhide(stackId);
        if (Invisible(targetId)) return false;
        if (!Map.Stacks.TryGetValue(targetId, out var target) || !Thieves.DiesBeforeDialog(LeaderAbilitiesOf(target))) return false;
        KillStack(stackId, targetId);
        return true;
    }

    /// <summary>Отмена свитка (<c>0x443506</c>): списание <c>ceil(MOVE/2)</c>; пустой список — ещё раз (<see cref="Thieves.NoActions"/>).</summary>
    public void ThiefCancel(string stackId, bool empty)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var thief)) return;
        Unhide(stackId);
        int allowance = DayAllowance(thief);
        SetMove(stackId, empty ? Thieves.NoActions(allowance, thief.Move) : Thieves.Cancel(allowance, thief.Move));
        RunActionEvents(stackId);   // 0x41e771
    }

    /// <summary>Исход успешного действия; возвращает взятое (экземпляр, заклинание).</summary>
    private string Apply(MapStack thief, string targetId, ThiefAction action, ref int value, string? pick, IRng rng)
    {
        var rules = ThiefRules;
        switch (action)
        {
            case ThiefAction.Spy:
                // Успешный шпионаж добавляет владельца цели в разведанные (journal: noble_action.id — игрок цели).
                MarkSpied(thief.Owner, Map.Stacks.TryGetValue(targetId, out var spiedStack) ? spiedStack.Owner
                                       : Map.Sites.First(f => Same(f.Id, targetId)).Owner);
                return "";
            case ThiefAction.PoisonStack:
                value = rules.PoisonStack;
                Poison(Map.Stacks[targetId].UnitIds, rules.PoisonStack);
                return "";
            case ThiefAction.PoisonCity:
            {
                value = rules.PoisonCity;
                var fort = Map.Sites.First(f => Same(f.Id, targetId));
                Poison([.. fort.Guards, .. Map.Stacks.TryGetValue(fort.StackId, out var guest) ? guest.UnitIds : []], rules.PoisonCity);
                return "";
            }
            case ThiefAction.Duel:
                if (Map.Stacks.ContainsKey(targetId)) AiBattle(thief.Id, targetId, duel: true);
                return targetId;
            case ThiefAction.StealMerchant:
                return StealFromMerchant(thief.Id, targetId, pick);
            case ThiefAction.StealItem:
                return StealItem(thief.Id, targetId, pick);
            case ThiefAction.StealSpell:
                return StealSpell(thief.Owner, targetId, pick);
            case ThiefAction.StealGold:
                MoveGold(Map.Sites.First(f => Same(f.Id, targetId)).Owner, thief.Owner, value);
                return "";
            case ThiefAction.Bribe:
                BribeFort(thief.Id, targetId);
                return "";
            case ThiefAction.RiotCity:
                Riot(targetId);
                return "";
            case ThiefAction.Assassinate:
                return Assassinate(targetId, thief.Id);
            case ThiefAction.Misfit:
                Misfit(targetId, rng);
                return "";
            case ThiefAction.StealMage:
                return StealFromMage(thief.Owner, targetId, pick);
            case ThiefAction.SpyRuin:
                SpyRuin(thief.Owner, targetId);
                return "";
            default:
                return "";
        }
    }

    /// <summary>Осмотр руины (<c>CEffectVisitRuin</c>, применение <c>0x43f5fa</c> → <c>CVisitorRuinAddVisiter</c> <c>0x613164</c>; REA 13.10): игрок вора
    /// — в список посетивших руину, без повторов, как у площадок (<c>0x5d7664</c>). Этот список и решает доступность осмотра (<c>0x5fda67</c>:
    /// игрок в наборе <c>+0x84</c>, <c>0x5d35a6</c>) и карточку руины по правой кнопке: охрана вместо «еще не исследованы» (<c>0x5796c0</c>,
    /// <see cref="D2.Data.Map.MapCards"/>).</summary>
    private void SpyRuin(string playerId, string ruinId)
    {
        var map = WithRuinVisitor(Map, ruinId, playerId);
        if (!ReferenceEquals(map, Map)) State = State with { Looks = Retold(Looks, map, Looks.Stacks) };
    }

    /// <summary>Игрок — в посетившие руину <paramref name="ruinId"/> (<c>CVisitorRuinAddVisiter</c> <c>0x613164</c>: набор <c>+0x84</c>, без
    /// повторов). Его пополняют осмотр вором (<c>CNobleActionResultSpyRuin</c>) и бой у руины (<c>CBattleResultRuin</c> <c>0x41b290</c>) — других
    /// создателей <c>CEffectVisitRuin</c> нет (<c>0x41b331</c>, <c>0x41e049</c>). Не руина или уже там — та же карта.</summary>
    private static MapState WithRuinVisitor(MapState map, string ruinId, string playerId)
    {
        if (map.Sites.FirstOrDefault(s => Same(s.Id, ruinId)) is not { Kind: SiteKind.Ruin } ruin
            || ruin.Visitors.Contains(playerId, StringComparer.OrdinalIgnoreCase)) return map;
        return With(map, sites: [.. map.Sites.Select(s => ReferenceEquals(s, ruin) ? s with { Visitors = [.. s.Visitors, playerId] } : s)]);
    }

    /// <summary>Отношения за успешное действие вора (<see cref="Diplomacy.NobleAction"/>): отряд или крепость играбельной расы.</summary>
    private void NobleRelations(string thiefOwner, string targetId, ThiefTargetKind kind)
    {
        var book = RequireBook();
        var map = Map;
        string owner = map.Stacks.TryGetValue(targetId, out var s) ? s.Owner
            : map.Sites.FirstOrDefault(f => Same(f.Id, targetId))?.Owner ?? "";
        if (PartyOf(owner) is not { } targetRace || PartyOf(thiefOwner) is not { } thiefRace) return;
        var change = Diplomacy.NobleAction(map.Relations, Diplomacy.PlayerParties(map, book.Economy), book.Diplomacy, thiefRace,
                                           targetRace, kind, !AiTactics.NeutralOwner(this, owner), success: true);
        State = State with { Looks = Retold(Looks, With(map, relations: change.Relations), Looks.Stacks) };
    }

    private void MarkSpied(string playerId, string owner)
    {
        var map = Map;
        if (!map.Players.TryGetValue(playerId, out var me) || me.Spied.Contains(owner, StringComparer.OrdinalIgnoreCase)) return;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[playerId] = me with { Spied = [.. me.Spied, owner] };
        State = State with { Looks = Retold(Looks, With(map, players: players), Looks.Stacks) };
    }

    private void Poison(IEnumerable<string> unitIds, int percent)
    {
        var map = Map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (string id in unitIds)
            if (units.TryGetValue(id, out var u) && u.Hp > 0) units[id] = u with { Hp = u.Hp - Thieves.PoisonDamage(u.Hp, percent) };
        State = State with { Looks = Retold(Looks, With(map, units: units), Looks.Stacks) };
    }

    /// <summary>Кража у торговца: экземпляр склада уходит (<c>CVisitorMerchantDelItem</c>), в сумку вора — новый предмет.</summary>
    private string StealFromMerchant(string thiefId, string siteId, string? pick)
    {
        var map = Map;
        var site = map.Sites.First(s => Same(s.Id, siteId));
        string type = pick ?? AiTactics.DearestStock(this, site) ?? "";
        var row = site.Stock.FirstOrDefault(r => Same(r.Item, type) && r.Count > 0);
        if (row is null) return "";
        var sites = map.Sites.Select(s => ReferenceEquals(s, site)
            ? s with { Stock = [.. s.Stock.Select(r => ReferenceEquals(r, row) ? r with { Count = r.Count - 1 } : r).Where(r => r.Count > 0)] }
            : s).ToList();
        var types = map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var ids = map.IdNext;
        string instance = NewId(ref ids, types.Keys, thiefId, "IM");
        types[instance] = type.ToUpperInvariant();
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[thiefId] = stacks[thiefId] with { Items = [.. stacks[thiefId].Items, instance] };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, sites: sites, itemTypes: types, idNext: ids), Looks.Stacks) };
        ChargeNewTalismans([instance]);
        var thief = stacks[thiefId];
        Given(TurnSightKind.ItemsGiven, thief.Owner, thief.X, thief.Y, siteId, [instance]);   // окно DLG_ITEM (0x41df26)
        return type.ToUpperInvariant();
    }

    /// <summary>Кража предмета: экземпляр сумки цели (<paramref name="pick"/> — id; иначе первый) — надетый снимается — в сумку вора.</summary>
    private string StealItem(string thiefId, string targetId, string? pick)
    {
        var target = Map.Stacks[targetId];
        string item = pick ?? target.Items.FirstOrDefault() ?? "";
        if (!target.Items.Contains(item, StringComparer.OrdinalIgnoreCase)) return "";
        for (int slot = 0; slot < D2.Rules.Magic.Equipment.SlotCount; slot++)
            if (Same(D2.Rules.Magic.Equipment.Slots(Map.Stacks[targetId])[slot], item)) UnequipItem(targetId, slot);
        var map = Map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[targetId] = stacks[targetId] with { Items = [.. stacks[targetId].Items.Where(i => !Same(i, item))] };
        stacks[thiefId] = stacks[thiefId] with { Items = [.. stacks[thiefId].Items, item] };
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks), Looks.Stacks) };
        return item;
    }

    /// <summary>
    /// Что можно украсть из заклинаний (<c>0x5cdde7</c>, ранг 1 по листингу): известные игроку <paramref name="ownerId"/>, кроме призывов
    /// (<c>0x5cdf6f</c>: категория не <c>L_SUMMON</c>) и тех, что знает вор <paramref name="thiefOwner"/>. Один список у признака «есть
    /// что украсть» (<c>0x45c0f1</c>, <c>0x5d397e</c>), окна выбора (<c>0x4b935b</c>) и самой кражи (<c>0x41d786</c>: выбранного нет в
    /// списке — кражи нет). Порядок — как у владельца (у движка — множество, порядок не разобран).
    /// </summary>
    public IReadOnlyList<string> StealableSpells(string thiefOwner, string ownerId)
    {
        var map = Map;
        if (Book is not { } book || !map.Players.TryGetValue(ownerId, out var owner) || !map.Players.TryGetValue(thiefOwner, out var me)) return [];
        var spells = book.Spells;
        return [.. owner.KnownSpells.Where(s => !(spells.TryGetValue(s, out var spell) && spell.Category == SpellCategory.Summon)
                                                && !me.KnownSpells.Contains(s, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>Кража заклинания: игрок-вор узнаёт заклинание владельца крепости из <see cref="StealableSpells"/>
    /// (<paramref name="pick"/>; иначе первое).</summary>
    private string StealSpell(string thiefOwner, string fortId, string? pick)
    {
        var map = Map;
        var me = map.Players[thiefOwner];
        var stealable = StealableSpells(thiefOwner, map.Sites.First(f => Same(f.Id, fortId)).Owner);
        string? spell = pick ?? stealable.FirstOrDefault();
        if (spell is null || !stealable.Contains(spell, StringComparer.OrdinalIgnoreCase)) return "";
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[me.Id] = me with { KnownSpells = [.. me.KnownSpells, spell.ToUpperInvariant()] };
        State = State with { Looks = Retold(Looks, With(map, players: players), Looks.Stacks) };
        var fort = map.Sites.First(f => Same(f.Id, fortId));
        Given(TurnSightKind.SpellsGiven, me.Id, fort.X, fort.Y, fortId, [spell.ToUpperInvariant()]);   // окно DLG_ITEM (0x41d7e0)
        return spell.ToUpperInvariant();
    }

    /// <summary>
    /// Убийство (результат <c>0x41d926</c>, эффект <c>0x41dae2</c>; ранг 1 по листингу): в группе цели (отряд или гарнизон
    /// <c>0x5f4f87</c>) жертва — живой не-лидер с наименьшим опытом за убийство (<c>XP_KILLED</c>, слот <c>0x3c</c>; равные — первый по
    /// порядку группы), нет такого — лидер. У отряда с единственным живым — гибель отряда (<c>0x4413bc</c>), иначе жертве — урон во
    /// всё её здоровье (<c>CEffectChangeHP</c> <c>0x43624a</c>). Гарнизон крепости, оставшийся без живых (<c>0x5f50fd</c> = 0), уходит в
    /// генератор захвата <c>0x440ee0</c> с отрядом вора: деревня — <c>CEffectCapture</c> и <c>CEffectEnterCity</c> (вор берёт её и
    /// входит), столица — взятие столицы; в порте это вход отряда (<see cref="Enter"/>). Возвращает жертву.
    /// ponytail: крепость с визитёром <see cref="Enter"/> не берёт (у движка генератор зовётся и тогда — ранг 3, в корпусе убийств
    /// последнего стража нет).
    /// </summary>
    internal string Assassinate(string targetId, string thiefStackId = "")
    {
        var map = Map;
        var book = RequireBook();
        bool isStack = map.Stacks.TryGetValue(targetId, out var stack);
        IReadOnlyList<string> group = isStack ? stack!.UnitIds
            : map.Sites.FirstOrDefault(f => Same(f.Id, targetId))?.Guards ?? [];
        string leader = isStack ? stack!.LeaderId : "";
        var alive = group.Where(id => map.Units.TryGetValue(id, out var u) && u.Hp > 0).ToList();
        if (alive.Count == 0) return "";
        if (isStack && alive.Count == 1)
        {
            KillStack(targetId, thiefStackId);   // 0x41dae2 → 0x41db0d: вещи — вору
            return alive[0];
        }
        string? victim = null;
        int best = 0;
        foreach (string id in alive.Where(id => !Same(id, leader)))
        {
            var u = map.Units[id];
            int xp = book.Grown(u.TypeId, u.Level)?.Type.XpKilled ?? 0;
            if (victim is not null && xp >= best) continue;
            (victim, best) = (id, xp);
        }
        victim ??= leader;
        if (!map.Units.TryGetValue(victim, out var dead)) return "";
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[victim] = dead with { Hp = 0 };
        var sites = map.Sites;
        bool emptied = !isStack && alive.Count == 1 && Map.Stacks.ContainsKey(thiefStackId);
        if (emptied)
        {
            // Гарнизон без живых снимается целиком, как после боя (MapBattle), затем захват и вход.
            foreach (string id in group) units.Remove(id);
            sites = [.. sites.Select(s => Same(s.Id, targetId) ? s with { Guards = [], GuardSlots = [] } : s)];
        }
        State = State with { Looks = Retold(Looks, With(map, units: units, sites: sites), Looks.Stacks) };
        // Вход — часть той же команды вора: события проверит одна проверка действия (RunActionEvents, 0x41e6e6), а не вход.
        if (emptied) Enter(thiefStackId, targetId, events: false);
        return victim;
    }

    /// <summary>
    /// Подложный приказ (результат <c>0x41d815</c>, <c>CEffectMisfit</c> <c>0x4392e2</c>; ранг 1 по листингу): позиции 0…5 перемешиваются
    /// (<c>0x43949e</c>: для i = 1…5 обмен с R250(i)), и юнит группы с номером i переставляется в клетку i-й позиции (<c>0x5e7554</c>, по
    /// очереди, с обменом с тем, кто там стоит; ранг 2 — как перенос внутри группы <see cref="Within"/>: с большим юнитом колонками,
    /// перенос в свою же колонку пропускается).
    /// </summary>
    internal void Misfit(string groupId, IRng rng)
    {
        if (GroupCells(groupId) is not { } cells) return;
        var positions = Enumerable.Range(0, UnitCells.CellCount).ToList();
        AiGift.Shuffle(rng, positions);
        for (int i = 0; i < cells.Units.Count; i++)
        {
            int from = cells.PositionOf(cells.Units[i]);
            if (from >= 0 && from != positions[i] && Within(cells, from, positions[i]) is { } moved) cells = moved;
        }
        SetCells(groupId, cells);
    }

    /// <summary>Кража у мага (результат <c>0x41df4d</c>: <c>CEffectStealMage</c> <c>0x43e9de</c> — заклинание уходит из лавки мага, затем
    /// <c>CEffectGiveSpell</c> — игроку-вору). Заклинание — <paramref name="pick"/> (выбор окна), иначе первое в лавке, которого вор не
    /// знает. ИИ этого действия не выбирает (<see cref="AiThief"/>). Ранг 2.</summary>
    internal string StealFromMage(string thiefOwner, string siteId, string? pick)
    {
        var map = Map;
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { } mage) return "";
        var me = map.Players[thiefOwner];
        string? spell = pick ?? mage.Spells.FirstOrDefault(s => !me.KnownSpells.Contains(s, StringComparer.OrdinalIgnoreCase));
        if (spell is null || !mage.Spells.Contains(spell, StringComparer.OrdinalIgnoreCase)
            || me.KnownSpells.Contains(spell, StringComparer.OrdinalIgnoreCase)) return "";
        var sites = map.Sites.Select(s => ReferenceEquals(s, mage) ? s with { Spells = [.. s.Spells.Where(x => !Same(x, spell))] } : s).ToList();
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[me.Id] = me with { KnownSpells = [.. me.KnownSpells, spell.ToUpperInvariant()] };
        State = State with { Looks = Retold(Looks, With(map, players: players, sites: sites), Looks.Stacks) };
        Given(TurnSightKind.SpellsGiven, me.Id, mage.X, mage.Y, siteId, [spell.ToUpperInvariant()]);   // окно DLG_ITEM (0x41dfd6)
        return spell.ToUpperInvariant();
    }

    private void MoveGold(string fromPlayer, string toPlayer, int amount)
    {
        var map = Map;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var gold = new Bank(amount, 0, 0, 0, 0, 0);
        players[fromPlayer] = players[fromPlayer] with { Bank = Treasury.Pay(players[fromPlayer].Bank, gold) };
        players[toPlayer] = players[toPlayer] with { Bank = Treasury.Apply(players[toPlayer].Bank, gold) };
        State = State with { Looks = Retold(Looks, With(map, players: players), Looks.Stacks) };
    }

    /// <summary>Подкуп (<see cref="Thieves.Bribe"/>): цена — золотом игрока-вора, гарнизон и визитёр стёрты, крепость — вора, вор
    /// в ней визитёром.</summary>
    private void BribeFort(string thiefId, string fortId)
    {
        var map = Map;
        var fort = map.Sites.First(s => Same(s.Id, fortId));
        int cost = BribeCost(fort);
        var thief = map.Stacks[thiefId];
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[thief.Owner] = players[thief.Owner] with { Bank = Treasury.Pay(players[thief.Owner].Bank, new Bank(cost, 0, 0, 0, 0, 0)) };
        if (fort.StackId.Length > 0) KillStack(fort.StackId, thiefId);   // 0x41dbe4 → 0x41dc59: вещи гостя — вору
        map = Map;
        var units = map.Units.Where(p => !fort.Guards.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                             .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var captured = Thieves.Bribe(fort, map.Stacks[thiefId]);
        var sites = map.Sites.Select(s => Same(s.Id, fortId) ? captured : s).ToList();
        var next = With(map, units: units, players: players, sites: sites);
        // Подкуп — тот же CEffectCapture (флаг +0x14): прежние хозяева деревни и сброс чужих «охранять город» (GameSession.Capture.cs).
        // Ранг 2: OCCUPY подкупа порт не озвучивает — уведомления о захвате у подкупа нет.
        if (fort.Kind == SiteKind.Village) (next, _) = VillageCaptured(next, fort, captured);
        captured = next.Sites.First(s => Same(s.Id, fortId));
        var looks = Retold(Looks, next, Looks.Stacks);
        looks = looks with { Forts = FortLooks(looks.Forts, looks.Map) };
        State = State with { Looks = Inside(looks, looks.Map, looks.Map.Stacks[thiefId], captured) };
    }

    /// <summary>Мятеж: <c>RIOT_T</c> = ход + <c>RIOT_MIN</c> + 1 + MSVCRT <c>rand() % (RIOT_MAX − RIOT_MIN)</c>
    /// (<see cref="Fortifications.RiotEnd"/>).</summary>
    private void Riot(string fortId)
    {
        var map = Map;
        int end = Fortifications.RiotEnd(ThiefRules, map.Turn, new CrtRng(Crt));
        var sites = map.Sites.Select(s => Same(s.Id, fortId) ? s with { RiotTurn = end } : s).ToList();
        State = State with { Looks = Retold(Looks, With(map, sites: sites), Looks.Stacks) };
    }

    private void SetMove(string stackId, int left)
    {
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stacks[stackId] with { Move = left };
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
    }

    /// <summary>Отряд гибнет целиком (<c>CEffectKillStack</c>): вещи — убийце <paramref name="killer"/> (отряд вора; у вора, погибшего о
    /// неподкупного, — отряд-цель), без убийцы — в склад крепости, где стоял, иначе мешком (<see cref="Loot"/>); затем снимается с карты и
    /// из крепости, юниты — тоже. Вор, погибший на деле (провал броска, любая цель), убийцы не имеет (<c>0x4431b8</c> → <c>0x4413bc</c>).
    /// <para>Погибший — в уничтоженные (<c>CMidStackDestroyed</c>, как у боя): по нему <c>L_KILL_STACK</c>, а ИИ на
    /// <c>CCmdStackDestroyedMsg</c> собирает списки сил заново (<see cref="AiTactics.StackDestroyed"/>; журнал 20261016d, seq 732750 →
    /// 732752: после провала вора <c>S143KC0042</c> строки <c>ai_strength_units</c> <c>0x476393</c>, «главным» стал нанятый
    /// <c>S143KC0045</c>). Убийца в записи у вора, погибшего на деле, — нейтральный игрок: сохранения корпуса, 31 проваленное дело
    /// девяти каталогов (цель — отряд, крепость или лавка; 20261016-port-synth не в счёт — там id двух карт совпадают). У прочих
    /// гибелей (последний юнит под ударом вора, гость подкупленной крепости, неподкупный лидер) — хозяин отряда-убийцы, как у боя
    /// (ранг 2: в корпусе таких записей не нашлось).</para></summary>
    private void KillStack(string stackId, string killer = "")
    {
        string killerPlayer = Map.Stacks.TryGetValue(killer, out var by) ? by.Owner : Neutral();
        LootNow(stackId, killer);
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return;
        var stacks = map.Stacks.Where(p => !Same(p.Key, stackId)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var units = map.Units.Where(p => !stack.UnitIds.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                             .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var sites = map.Sites.Select(s => Same(s.StackId, stackId) ? s with { StackId = "" } : s).ToList();
        var after = With(map, stacks: stacks, units: units, sites: sites, plan: PlanWithout(map, stackId));
        after = WithDestroyed(after, [.. map.Destroyed, new DestroyedStack(stack.Id, killerPlayer, stack.TemplateId)]);
        State = State with { Looks = Retold(Looks, after, [.. Looks.Stacks.Where(s => !Same(s.Id, stackId))]) };
    }

    /// <summary>MSVCRT <c>rand() % n</c> поверх потока CRT партии (<c>Next(0)</c> = 0 без броска).</summary>
    private sealed class CrtRng(D2.Rules.Rng.CrtRand crt) : IRng
    {
        public int Next(int maxValue) => maxValue == 0 ? 0 : crt.Next() % maxValue;
    }
}
