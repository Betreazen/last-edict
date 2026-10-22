using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Data.Play;

/// <summary>
/// Знатный (вор) ИИ из карты — <c>NobleStack</c>, <c>NobleCity</c>, <c>NobleMerchant</c> (<c>docs/ai-tactic-candidates.md</c> §12):
/// построители без кэша ближайших целей и решение ИИ у цели (<see cref="ThiefVisit"/>). Ранг 1 по листингам 24.09, если не
/// сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// <c>NobleStack</c>: построитель <c>0x45da58</c> (vtable <c>CAiNobleStack</c> <c>0x6d14fc</c>). Есть ли игрок, которого
    /// ещё можно разведать, — не свой, не нейтрал, не в разведанных (<c>0x5e46c2</c>, <see cref="MapPlayer.Spied"/>); нет — приказ
    /// потерял смысл (<see cref="SelfChangeOrder"/> при действующем <c>L_NORMAL</c>). Иначе по <b>всем</b> отрядам карты
    /// (<c>0x419aac</c>, по id): вид расы лидера (<see cref="LeaderRaceType"/>) не свой, подраса не своя; точка — позиция отряда
    /// (<c>0x5ed9b2</c>); оценщик <c>CAiNobleStackActionEval</c> (<c>0x6d150c</c>, слот <c>0x10</c> <c>0x45dd9b</c>): счёт
    /// <c>0x45de91</c> (<see cref="AiTacticEval.NobleStack"/>), NoScore — нет; дальше клетки — перемещение (<c>0x45a915</c>, тип
    /// 0xe, режим 1, N = 1), вплотную — <c>CAiNobleStackAction</c> (<c>0x45e2d8</c>, vtable <c>0x6d15ac</c>, режим 0;
    /// <see cref="AiTacticOption.Direct"/>).
    /// ponytail: цель плана тактики (<c>0x473049</c>, «лидер-призыв из плана») не разобрана — ложь.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> NobleStacks(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (!EnemyExists(session, stack.Owner))
        {
            if (OrderOf(stack).Order == StackOrder.Normal && SelfChangeOrder(session, stack) is { } change) yield return change;
            yield break;
        }
        var book = session.Book!;
        var self = Position(map, stack);
        int ownRace = LeaderRaceType(session, stack);
        bool waterOnly = session.WaterOnly(stack);
        foreach (var s in map.Stacks.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (LeaderRaceType(session, s) == ownRace && SameParty(session, stack.Owner, s.Owner) || Same(s.Subrace, stack.Subrace)) continue;
            if (waterOnly && !session.IsWater(s.X, s.Y)) continue;   // 0x45dcfa
            var at = Position(map, s);
            string leaderType = map.Units.TryGetValue(s.LeaderId, out var leader) ? leader.TypeId : "";
            var actions = session.ThiefActions(stack.Id, s.Id);
            var facts = new AiNobleStackFacts(
                Invisible: session.Invisible(s.Id), Inside: s.Inside.Length > 0, Neutral: NeutralOwner(session, s.Owner),
                IsMoveTarget: false, SummonLeader: book.CategoryOf(leaderType) == UnitCategory.Summon,
                Allied: Allied(session, stack.Owner, s.Owner),
                Human: map.Players.TryGetValue(s.Owner, out var owner) && owner.IsHuman,
                HasSpyRiotSteal: SpyRiotSteal(actions),
                OnlyDuelWithRod: actions is [ThiefAction.Duel] && session.LeaderAbilitiesOf(s).Contains(Rods.LeaderAbilityRod),
                Influence: AiTacticScore.Influence(turn.Grid, at.X, at.Y), Distance: Cheb(self, at),
                StatusToday: turn.Memo.Status(stack.Id, s.Id, at.X, at.Y, turn.Day),
                StatusYesterday: turn.Memo.Status(stack.Id, s.Id, at.X, at.Y, turn.Day - 1));
            double score = AiTacticEval.NobleStack(turn.Weights, facts);
            if (score == AiTactic.NoScore) continue;
            yield return (new AiTacticOption(AiActionType.NobleStack, stack.Id, s.Id, at.X, at.Y, Direct: Cheb(self, at) <= 1), score);
        }
    }

    /// <summary>
    /// <c>NobleCity</c>: построитель <c>0x45cbeb</c> — по всем крепостям (<c>0x41983f</c>, по id), кроме деревни с бунтом
    /// (<c>0x5e9582</c>), своей подрасы (<c>+0x88</c>) и столицы игрока со взятой столицей (<c>0x5e4e73</c>); точка — вход
    /// (<c>0x5e9563</c>). Счёт <c>0x45cea1</c> (<see cref="AiTacticEval.NobleCity"/>): список действий — против визитёра
    /// (<c>+0x8c</c>), если он есть, иначе против крепости. Дальше клетки — перемещение (тип 0xd, режим 1, N = 0), вплотную —
    /// <c>CAiNobleCityAction</c> (<c>0x45d1aa</c>, vtable <c>0x6d1404</c>).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> NobleCities(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        var self = Position(map, stack);
        foreach (var fort in Forts(map).OrderBy(f => f.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!FortUsable(map, fort) || Same(fort.Subrace, stack.Subrace)) continue;
            var at = Entrance(fort);
            string listTarget = fort.StackId.Length > 0 && map.Stacks.ContainsKey(fort.StackId) ? fort.StackId : fort.Id;
            double score = AiTacticEval.NobleCity(turn.Weights, Allied(session, stack.Owner, fort.Owner), NeutralOwner(session, fort.Owner),
                                                  SpyRiotSteal(session.ThiefActions(stack.Id, listTarget)),
                                                  AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                                  turn.Memo.Status(stack.Id, fort.Id, at.X, at.Y, turn.Day),
                                                  turn.Memo.Status(stack.Id, fort.Id, at.X, at.Y, turn.Day - 1));
            if (score == AiTactic.NoScore) continue;
            yield return (new AiTacticOption(AiActionType.NobleCity, stack.Id, fort.Id, at.X, at.Y, Direct: Cheb(self, at) <= 1), score);
        }
    }

    /// <summary>
    /// <c>NobleMerchant</c>: построитель <c>0x45d250</c>, две ветки (оценщик <c>CAiNobleMerchantActionEval</c> <c>0x6d143c</c>,
    /// слот <c>0x10</c> <c>0x45d50c</c>):
    /// <list type="bullet">
    /// <item>в сумке есть предметы — передача своим сильнейшим (<c>CAiTactic +0x51fc</c>, <see cref="AiTacticTurn.Forces"/>) вне
    /// крепостей (себя движок не исключает — только <c>+0x94</c>; в корпусе вор среди сильнейших не бывал: лишних передач у порта
    /// нет): точка — клетка отряда, счёт <c>0x45d63e</c> (<see cref="AiTacticEval.NobleMerchantGive"/>), действие
    /// <c>CAiGiveItemsAction</c> (<c>0x45717f</c>, тип 0x22 — в журнале с <c>ret 0x45d637</c>);</item>
    /// <item>сумка пуста — все торговцы карты (<c>0x4599d7</c>, миссия тоже): точка — вход, счёт <c>0x45d7b9</c>
    /// (<see cref="AiTacticEval.NobleMerchantTrade"/>: склад пуст или действий вора нет — NoScore), действие
    /// <c>CAiNobleMerchantAction</c> (<c>0x45d9ab</c>, тип 0x21, режим 1, N = 0; исполнение — перемещение <c>0x45abd8</c>).
    /// Товара нет ни у одного торговца (<c>0x601e55</c>) — смена приказа (<see cref="SelfChangeOrder"/>, <c>0x45d3a0</c>).</item>
    /// </list>
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> NobleMerchants(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        var self = Position(map, stack);
        if (stack.Items.Count > 0)
        {
            foreach (string id in turn.Forces.Own)
            {
                if (!map.Stacks.TryGetValue(id, out var s) || s.Inside.Length > 0) continue;
                var at = (s.X, s.Y);
                double give = AiTacticEval.NobleMerchantGive(turn.Weights, AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                                             turn.Memo.Status(stack.Id, s.Id, at.X, at.Y, turn.Day),
                                                             turn.Memo.Status(stack.Id, s.Id, at.X, at.Y, turn.Day - 1));
                if (give == AiTactic.NoScore) continue;
                yield return (new AiTacticOption(AiActionType.GiveItems, stack.Id, s.Id, at.X, at.Y), give);
            }
            yield break;
        }
        var merchants = map.Sites.Where(s => s.Kind == SiteKind.Merchant).OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();
        // Товара нет ни у одного торговца (0x45d362: 0x601e55 — сумма 0x6012c5 по лавкам) — смена приказа. Карта 22 (20261019b),
        // день 9: Легионы выкупили лавку S143SI0000, вор Кланов S143KC0017 — ChangeOrder на себя (111,44), затем помощник.
        if (merchants.Sum(m => m.Stock.Count(r => r.Count > 0)) == 0)
        {
            if (SelfChangeOrder(session, stack) is { } change) yield return change;
            yield break;
        }
        foreach (var site in merchants)
        {
            var at = SiteEntrance(site);
            bool hasActions = site.Stock.Any(r => r.Count > 0) && session.ThiefActions(stack.Id, site.Id).Count > 0;
            double score = AiTacticEval.NobleMerchantTrade(turn.Weights, hasActions, AiTacticScore.Influence(turn.Grid, at.X, at.Y),
                                                           Cheb(self, at), turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day),
                                                           turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day - 1));
            if (score == AiTactic.NoScore) continue;
            yield return (new AiTacticOption(AiActionType.NobleMerchant, stack.Id, site.Id, at.X, at.Y), score);
        }
    }

    /// <summary>Шпионаж, мятеж или кража предмета в списке — без них <c>NobleStack</c> и <c>NobleCity</c> дают NoScore
    /// (<c>0x45e08f</c>…).</summary>
    private static bool SpyRiotSteal(IReadOnlyList<ThiefAction> actions) =>
        actions.Any(a => a is ThiefAction.Spy or ThiefAction.RiotCity or ThiefAction.StealItem);

    /// <summary>Есть кого разведать (<c>0x45da83</c>…): игрок не свой, не нейтрал (<c>0x5e5358</c>) и не в разведанных
    /// (<c>0x5e46c2</c> — поиск в <c>CMidPlayer +0x54</c>).</summary>
    private static bool EnemyExists(GameSession session, string playerId)
    {
        var map = session.Map;
        var spied = map.Players.TryGetValue(playerId, out var me) ? me.Spied : [];
        return map.Players.Keys.Any(p => !Same(p, playerId) && !NeutralOwner(session, p) && !spied.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Решение ИИ у цели вора — колбэк свитка (<c>0x45ae96</c>, слот 23 второй таблицы действия-перемещения): у лидера
    /// отряда-цели <c>L_INCORRUPTIBLE</c> — вор гибнет до свитка; список пуст — отмена с двойным списанием; иначе выбор
    /// <see cref="ThiefChoice"/>, нет выбора — отмена (<c>0x41e771</c>), есть — действие (<c>0x41e6e6</c> →
    /// <see cref="GameSession.Thief"/>). Крепость с визитёром — действие против визитёра (ранг 3: так считает список и
    /// <c>NobleCity</c>). Возвращает отчёт действия или null — отменено.
    /// </summary>
    public static ThiefReport? ThiefVisit(GameSession session, IRng rng, string stackId, string targetId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rng);
        var map = session.Map;
        if (!map.Stacks.ContainsKey(stackId)) return null;
        targetId = ThiefTargetOf(session, targetId);
        if (session.ThiefIncorruptible(stackId, targetId)) return new ThiefReport(ThiefAction.Spy, targetId, null, ThiefDied: true);
        var available = session.ThiefActions(stackId, targetId);
        if (available.Count == 0)
        {
            session.ThiefCancel(stackId, empty: true);
            return null;
        }
        // Показ (сессия Г): вор у цели — ANOBLE/SNOBLE, как у движка при непустом списке действий (0x4f2eea). Модель не меняется.
        if (session.Observer is { } observer)
        {
            var at = map.Stacks.TryGetValue(targetId, out var victim) ? (victim.X, victim.Y)
                : PathFinder.EntranceOf(map, targetId) ?? (map.Stacks[stackId].X, map.Stacks[stackId].Y);
            observer.Happened(new TurnSight(TurnSightKind.NobleAction, map.Stacks[stackId].Owner, at.Item1, at.Item2, stackId));
        }
        if (ThiefChoice(session, rng, stackId, targetId) is not { } choice)
        {
            session.ThiefCancel(stackId, empty: false);
            return null;
        }
        return session.Thief(stackId, targetId, choice.Action, rng, choice.Pick);
    }

    /// <summary>Цель действия вора: крепость с визитёром — визитёр, иначе сама цель.</summary>
    public static string ThiefTargetOf(GameSession session, string targetId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        return map.Sites.FirstOrDefault(f => Same(f.Id, targetId)) is { Kind: SiteKind.Capital or SiteKind.Village, StackId.Length: > 0 } fort
               && map.Stacks.ContainsKey(fort.StackId)
            ? fort.StackId : targetId;
    }

    /// <summary>
    /// Выбор ИИ без исполнения (<c>0x45b98a</c>, <see cref="AiThief.Choose"/>): действие и что взять — экземпляр сумки цели
    /// (кража предмета, бросок R250), самый дорогой товар (<see cref="DearestStock"/>). Броски — на <paramref name="rng"/>
    /// (перемешивание доступного: n − 1). null — ИИ отменяет свиток.
    /// </summary>
    public static (ThiefAction Action, string? Pick)? ThiefChoice(GameSession session, IRng rng, string stackId, string targetId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rng);
        var map = session.Map;
        if (!map.Stacks.TryGetValue(stackId, out var thief)) return null;
        var available = session.ThiefActions(stackId, targetId);
        bool assist = OrderOf(thief).Order == StackOrder.Assist;
        bool diplomat = map.Players.TryGetValue(thief.Owner, out var player) && session.Book!.LordCategory(player.LordId) == (int)LordCategory.Diplomat;
        if (AiThief.Choose(assist, diplomat, available, ThiefFacts(session, thief, targetId), rng) is not { } choice) return null;
        string? pick = choice.Action switch
        {
            ThiefAction.StealItem when map.Stacks.TryGetValue(targetId, out var victim) && choice.ItemIndex >= 0
                                       && choice.ItemIndex < victim.Items.Count => victim.Items[choice.ItemIndex],
            ThiefAction.StealMerchant => map.Sites.FirstOrDefault(s => Same(s.Id, targetId)) is { } shop ? DearestStock(session, shop) : null,
            _ => null,
        };
        return (choice.Action, pick);
    }

    /// <summary>Признаки цели для выбора ИИ (<see cref="AiThiefTarget"/>).</summary>
    private static AiThiefTarget ThiefFacts(GameSession session, MapStack thief, string targetId)
    {
        var map = session.Map;
        var kind = session.ThiefKindOf(targetId) ?? ThiefTargetKind.Ruin;
        var state = session.ThiefTarget(thief.Owner, targetId);
        var gold = map.Players.TryGetValue(thief.Owner, out var p) ? p.Bank.Gold : 0;
        var victim = map.Stacks.GetValueOrDefault(targetId);
        var site = map.Sites.FirstOrDefault(s => Same(s.Id, targetId));
        return new AiThiefTarget
        {
            Kind = kind,
            DuelAllowed = victim is not null && !session.LeaderAbilitiesOf(victim).Contains(Rods.LeaderAbilityRod),
            HasGroup = victim is not null || kind == ThiefTargetKind.Fortification,
            UnitCount = victim?.UnitIds.Count ?? site?.Guards.Count ?? 0,
            ItemCount = victim?.Items.Count ?? 0,
            BribeAffordable = state is not null && state.BribeCost < gold,
            SpellToSteal = state?.HasSpellToSteal ?? false,
            MerchantStocked = site is { Kind: SiteKind.Merchant } && site.Stock.Any(r => r.Count > 0),
            MisfitUseful = MisfitUseful(session, victim?.UnitIds ?? (IReadOnlyList<string>?)site?.Guards ?? []),
            DuelWeakLeader = victim is not null && DuelWeakLeader(session, victim.LeaderId),
            OwnerNeutral = site is not null && NeutralOwner(session, site.Owner),
        };
    }

    /// <summary>Первая атака юнита его уровня (<c>IUsSoldier +0x48</c>); нет в правилах — null.</summary>
    private static AttackType? FirstAttack(GameSession session, MapUnit unit) =>
        session.Book!.Grown(unit.TypeId, unit.Level)?.Type.AttackId is { Length: > 0 } id
            ? session.Book.Rules.Attacks.GetValueOrDefault(id) : null;

    /// <summary><c>0x45c1e5</c>: в группе не меньше двух юнитов (<c>0x5f4441</c>, павшие в счёт), и у <b>малого</b> из них (<c>0x5e9830</c> —
    /// <c>CMidUnit::getSizeSmall</c>; здоровье не смотрится) первая атака дальности «все» или «любой» (<c>[0x8380c8]</c> — id <c>L_ALL</c>,
    /// объект <c>0x8380c0</c>; <c>[0x8380d8]</c> — <c>L_ANY</c>, объект <c>0x8380d0</c>: инициализация <c>0x591840</c>/<c>0x591857</c> по строкам
    /// <c>0x7acccc</c>/<c>0x7accd0</c>). Ранг 1 — листинг и журнал карты 23 (день 58): гарнизон <c>S143FT000b</c> — G000UU0052 (малый, «соседний»),
    /// G000UU0055 (большой, «любой»), G000UU0057 (большой) — у движка отравление города (<c>noble_action</c> 6, seq 9596962), не подложный приказ;
    /// прежде порт не смотрел размер и брал Misfit (реестр И156).</summary>
    private static bool MisfitUseful(GameSession session, IReadOnlyList<string> group) =>
        group.Count >= 2 && group.Any(id => session.Map.Units.TryGetValue(id, out var u)
                                            && session.Book!.Grown(u.TypeId, u.Level)?.Type is { SizeSmall: true }
                                            && FirstAttack(session, u) is { Reach: AttackType.ReachAll or ReachAny });

    private const int ReachAny = 2;

    /// <summary>Классы атак «поддержки», против которых вору дуэль выгодна (<c>0x45c350</c>…).</summary>
    private static readonly HashSet<AttackClass> DuelSupportClasses =
    [
        AttackClass.Heal, AttackClass.BoostDamage, AttackClass.LowerDamage, AttackClass.LowerInitiative, AttackClass.Revive,
        AttackClass.Cure, AttackClass.BestowWards, AttackClass.Shatter, AttackClass.GiveAttack,
    ];

    /// <summary><c>0x45c2b3</c>: здоровье лидера меньше 100 (<c>0x479bd6</c>), а первая атака — урон «всем» меньше 40 или класса
    /// поддержки.</summary>
    private static bool DuelWeakLeader(GameSession session, string leaderId)
    {
        if (!session.Map.Units.TryGetValue(leaderId, out var leader) || leader.Hp >= DuelLeaderHp
            || FirstAttack(session, leader) is not { } attack) return false;
        if (attack.Reach == AttackType.ReachAll && attack.Class == AttackClass.Damage && attack.QtyDamage < DuelWeakDamage) return true;
        return DuelSupportClasses.Contains(attack.Class);
    }

    private const int DuelLeaderHp = 100;     // 0x45c2e1: cmp eax, 0x64
    private const int DuelWeakDamage = 40;    // 0x45c347: cmp eax, 0x28

    /// <summary>
    /// Что ИИ крадёт у торговца (<c>0x45bf99</c>): склад по порядку (<c>0x602b82</c>), лучший — первый, дальше экземпляр
    /// заменяет лучший, если цены лучшего на него не хватает (<c>0x5869e6</c> — «не хватает» хоть в одной валюте): самый дорогой,
    /// из равных — первый. Журнал legions: у всех 9 краж — самый дорогой товар склада (3000, затем 1000, 450…). Склад пуст — null.
    /// </summary>
    public static string? DearestStock(GameSession session, MapSite site)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(site);
        var items = session.Book!.Items;
        string? best = null;
        Bank bestValue = Bank.Zero;
        foreach (var row in site.Stock.Where(r => r.Count > 0))
        {
            var value = items.TryGetValue(row.Item, out var item) ? Treasury.ItemBuyPrice(item) : Bank.Zero;
            if (best is null || Economy.NotEnough(bestValue, value))
                (best, bestValue) = (row.Item, value);
        }
        return best;
    }
}
