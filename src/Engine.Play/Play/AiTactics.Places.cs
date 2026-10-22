using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Data.Play;

/// <summary>
/// «Места» ИИ из карты — площадки торговца, мага, наёмников и тренера (<c>docs/ai-tactic-candidates.md</c> §10): кэш мест,
/// построитель тренера и решение ИИ внутри посещения. Ранг 1 по листингам 24.09, если не сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>Сторона пятна площадки (<c>CMidSite +0x14/+0x18</c>) — 3×3, как у руины.</summary>
    private const int SiteSize = 3;

    /// <summary>Золото в кошельке тактики, без которого тренер при <c>SUGG_LVL ≥ 10</c> не строится (<c>0x46cd34</c>: 500).</summary>
    private const int TrainerHighLevelGold = 500;

    /// <summary>Во сколько раз сильнейший отряд человека должен превосходить свой, чтобы при <c>SUGG_LVL ≥ 10</c> идти к
    /// тренеру (<c>0x4692bf</c>: <c>[0x6d2110]</c> = 0,5).</summary>
    private const double TrainerHumanRatio = 0.5;

    /// <summary>Вход площадки (<c>0x601181</c>: <c>pos + size − 1</c>) — точка действия и позиция в кэше.</summary>
    public static (int X, int Y) SiteEntrance(MapSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return AiTacticScore.AnchorCell(site.X, site.Y, SiteSize);
    }

    /// <summary>Площадка — <c>CMidSite</c>: торговец, наёмники, тренер, маг.</summary>
    private static bool IsPlace(SiteKind kind) => kind is SiteKind.Merchant or SiteKind.Mercenaries or SiteKind.Trainer or SiteKind.Mage;

    /// <summary>
    /// Кэш мест (<c>0x45efa4</c>, вектор <c>+0x2c</c> <c>CMidServerLogicData2</c>): все площадки без фильтра, расстояние —
    /// евклид от позиции отряда до входа (<c>0x45eadf</c>); радиус у чужих столиц и исключения — как у прочих векторов
    /// (<c>0x45f5f1</c>, <c>0x45f793</c>); до трёх. Построители берут из него свой класс площадки.
    /// </summary>
    public static IReadOnlyList<MapSite> PlaceTargets(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        return Nearest(map.Sites.Where(s => IsPlace(s.Kind) && !s.Gone),
                       s =>
                       {
                           var (x, y) = SiteEntrance(s);
                           return (s.Id, x, y);
                       }, Position(map, stack), turn);
    }

    /// <summary>
    /// <c>VisitTrainer</c>: построитель <c>0x46ccef</c> (оценщик <c>CAiVisitTrainerActionEval</c>, vtable <c>0x6d2674</c>):
    /// <list type="bullet">
    /// <item>столица хозяина взята (слот <c>0x18</c> обёртки) — нет;</item>
    /// <item>при <c>SUGG_LVL ≥ 10</c> (<c>0x61eb31</c>) — в кошельке тактики не меньше 500 золота (<c>0x5869e6</c>) и отряд
    /// слабее половины сильнейшего отряда человека (<see cref="TrainerWanted"/>, <c>0x4692bf</c>) — иначе нет;</item>
    /// <item>по местам кэша (<see cref="PlaceTargets"/>) класса тренера (<c>0x798e08</c>), если есть кого учить
    /// (<see cref="Trainable"/>, <c>0x46ce2d</c>): точка — вход, счёт <c>0x46d09f</c> (<see cref="AiTacticEval.VisitTrainer"/>).</item>
    /// </list>
    /// Действие одно — <c>CAiVisitTrainerAction</c> (<c>0x46d1f0</c>, vtable <c>0x6d2714</c>): перемещение к входу (режим 1, N = 0);
    /// обучение — когда путь заходит на площадку (<see cref="TrainAtSite"/>).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> VisitTrainers(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        bool high = session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel;
        if (high && (turn.Purse.Gold < TrainerHighLevelGold || !TrainerWanted(session, turn, stack))) yield break;
        var self = Position(map, stack);
        foreach (var site in PlaceTargets(session, turn, stack))
        {
            if (site.Kind != SiteKind.Trainer || !Trainable(session, turn, stack)) continue;
            var at = SiteEntrance(site);
            int today = turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day);
            yield return (new AiTacticOption(AiActionType.VisitTrainer, stack.Id, site.Id, at.X, at.Y),
                          AiTacticEval.VisitTrainer(turn.Weights, AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                                    turn.CapturedByOther(stack.Id, site.Id), high, today));
        }
    }

    /// <summary>
    /// Есть кого учить (<c>0x46ce2d</c>): юнит группы без отказа <see cref="GameSession.TrainRefusal"/> (<c>0x5e0372</c>), чью
    /// цену одного очка (<c>TRAINING_C</c>, через <c>0x61eb4a</c>) кошелёк тактики покрывает (<c>0x5869e6</c>: ни одна валюта
    /// кошелька не меньше цены).
    /// </summary>
    public static bool Trainable(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        bool high = session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel;
        foreach (string unitId in stack.UnitIds)
        {
            if (session.TrainRefusal(stack.Owner, unitId) is not null || session.TrainingRate(unitId) is not { } rate) continue;
            var (cost, _) = SiteTrade.AiTraining(rate, 1, high, session.State.Info.Difficulty);
            if (!Economy.NotEnough(turn.Purse, cost)) return true;
        }
        return false;
    }

    /// <summary>
    /// Идти ли к тренеру на высоком уровне (<c>0x4692bf</c>): первый в списке чужих сильнейших (<c>CAiTactic +0x51f8</c>,
    /// слот <c>0x1c</c>) отряд человека (<c>CMidPlayer +0x88</c>); нет такого — да; иначе своя сила «без города» против числа
    /// его юнитов меньше половины его силы «с городом» против числа своих (обе — <c>0x5dcec7</c> с полным здоровьем).
    /// </summary>
    public static bool TrainerWanted(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        var human = turn.Forces.Others
            .Select(id => map.Stacks.GetValueOrDefault(id))
            .FirstOrDefault(s => s is not null && map.Players.TryGetValue(s.Owner, out var p) && p.IsHuman);
        if (human is null) return true;
        double own = GroupStrength(session, stack, ignoreCity: true, human.UnitIds.Count, fullHp: true);
        double theirs = GroupStrength(session, human, ignoreCity: false, stack.UnitIds.Count, fullHp: true);
        return own < theirs * TrainerHumanRatio;
    }

    /// <summary>
    /// Решение ИИ у тренера (колбэк посещения <c>0x46d26c</c> → <c>0x46d2b5</c>/<c>0x46d322</c>): по юнитам группы по порядку —
    /// если учить можно (<see cref="GameSession.TrainRefusal"/>), команда обучения (<c>0x41e69e</c> → <c>0x5d7c6b</c>, флаг
    /// «от ИИ»), а цена (<c>TRAINING_C</c> × очки <c>0x5e0396</c>, через <c>0x61eb4a</c>) вычитается из кошелька тактики.
    /// Возвращает юнитов, которым ушла команда (у движка — строка <c>site_train</c> на каждого, и когда казна не оплатила ни
    /// очка). Журнал human: <c>S073KC0003</c> у <c>S073SI0000</c> — первый юнит забрал 144 из 145 золота, остальным команда
    /// ушла без оплаты.
    /// </summary>
    public static IReadOnlyList<string> TrainAtSite(GameSession session, AiTacticTurn? turn, string stackId, string siteId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.Map.Stacks.TryGetValue(stackId, out var stack)) return [];
        var sent = new List<string>();
        foreach (string unitId in stack.UnitIds)
        {
            if (session.TrainRefusal(stack.Owner, unitId) is not null) continue;
            session.Train(stackId, siteId, unitId, out var paid, ai: true);
            turn?.Spend(paid);
            sent.Add(unitId);
        }
        return sent;
    }

    // ------------------------------------------------------------------ торговец

    /// <summary>
    /// <c>VisitMerchant</c>: построитель <c>0x46aece</c> (оценщик <c>CAiVisitMerchantActionEval</c>, vtable <c>0x6d23d4</c>):
    /// столица хозяина взята — нет; по местам кэша класса торговца (<c>0x798520</c>), не миссии (<c>+0x88</c>). Оценщик
    /// (слот <c>0x10</c> <c>0x46afba</c>): сколько купить (<see cref="MerchantWants"/>, <c>0x46b0e0</c>) меньше 1 — нет; счёт
    /// <c>0x46b218</c> (<see cref="AiTacticEval.VisitMerchant"/>: купить по кошельку можно — <see cref="MerchantCanBuy"/>),
    /// NoScore — нет. Не вплотную ко входу (слот <c>0x2c</c> обёртки) — перемещение к входу (<c>0x45a915</c>, режим 1, N = 0);
    /// вплотную и с очками хода (<c>+0x98</c> ≥ 1) — посещение <c>CAiVisitMerchantAction</c> (<c>0x46b51e</c>, vtable
    /// <c>0x6d247c</c>; <see cref="AiTacticOption.Direct"/>); вплотную без очков — нет.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> VisitMerchants(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        var self = Position(map, stack);
        foreach (var site in PlaceTargets(session, turn, stack))
        {
            if (site.Kind != SiteKind.Merchant || site.Mission) continue;
            int wants = MerchantWants(session, stack);
            if (wants < 1) continue;
            var at = SiteEntrance(site);
            double score = AiTacticEval.VisitMerchant(turn.Weights, AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                                      site.Mission, MerchantCanBuy(session, turn, stack, site), wants,
                                                      turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day),
                                                      turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day - 1));
            if (score == AiTactic.NoScore) continue;
            bool adjacent = Cheb(self, at) <= 1;
            if (adjacent && stack.Move < 1) continue;
            yield return (new AiTacticOption(AiActionType.VisitMerchant, stack.Id, site.Id, at.X, at.Y, Direct: adjacent), score);
        }
    }

    /// <summary>Сколько предметов ИИ хочет купить (<c>0x46b0e0</c>): юниты группы (с павшими) минус предметы сумки; при
    /// <c>SUGG_LVL ≥ 10</c> особые предметы (<c>L_SPECIAL</c>, <c>0x46b15e</c>) не считаются — их ИИ там же пытается продать.</summary>
    public static int MerchantWants(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        int items = stack.Items.Count;
        if (session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel)
            items -= stack.Items.Count(i => session.ItemOf(i) is { Category: LastEdict.Rules.Magic.ItemCategory.Special });
        return stack.UnitIds.Count - items;
    }

    /// <summary>Купить по кошельку можно (<c>0x46b428</c>): на складе есть предмет, чью цену для отряда (<c>0x5dfd5f</c>: <c>VALUE</c>
    /// со скидкой лидера, <see cref="GameSession.ItemPrice"/>) кошелёк тактики покрывает.</summary>
    public static bool MerchantCanBuy(GameSession session, AiTacticTurn turn, MapStack stack, MapSite site)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(site);
        var items = session.Book!.Items;
        return site.Stock.Any(r => r.Count > 0 && items.TryGetValue(r.Item, out var item)
                                   && !Economy.NotEnough(turn.Purse, session.ItemPrice(stack, item)));
    }

    /// <summary>
    /// Решение ИИ у торговца (колбэк посещения <c>0x46b5bd</c>): при <c>SUGG_LVL ≥ 10</c> сперва продажа особых предметов
    /// (<c>0x46b61f</c> → <c>0x41e5c0</c>) — сервер их отвергает (<see cref="SiteTrade.SellItem"/>: <c>L_SPECIAL</c>), так что
    /// ничего не меняется; затем покупка <c>0x46b6f8</c>: все экземпляры склада по порядку (тип — столько раз, сколько штук),
    /// пока куплено не столько, сколько <see cref="MerchantWants"/> (листинг: <c>jne</c>, выход до цикла — только при 0, так что
    /// при отрицательном n покупки идут до конца склада или кошелька), и список не пуст — случайный (R250 <c>0x54851f</c>) экземпляр уходит из
    /// списка, и если его цену для отряда (<c>0x46b84b</c> → <c>0x5dfd5f</c>, со скидкой лидера) кошелёк покрывает — команда покупки
    /// (<c>0x41e575</c> → <c>0x5d76e2</c>), цена — из кошелька.
    /// Возвращает купленные типы.
    /// </summary>
    public static IReadOnlyList<string> BuyAtMerchant(GameSession session, AiTacticTurn? turn, IRng rng, string stackId, string siteId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rng);
        var map = session.Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)
            || map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Merchant } site) return [];
        var items = session.Book!.Items;
        int wants = MerchantWants(session, stack);
        var pool = site.Stock.SelectMany(r => Enumerable.Repeat(r.Item, Math.Max(r.Count, 0))).ToList();
        var bought = new List<string>();
        var purse = turn?.Purse ?? map.Players[stack.Owner].Bank;
        while (bought.Count != wants && pool.Count > 0)
        {
            int at = rng.Next(pool.Count);
            string type = pool[at];
            pool.RemoveAt(at);
            if (!items.TryGetValue(type, out var item)) continue;
            var price = session.ItemPrice(stack, item);
            if (Economy.NotEnough(purse, price)) continue;
            session.BuyItem(stackId, siteId, type);
            purse = Treasury.Pay(purse, price);
            turn?.Spend(price);
            bought.Add(type);
        }
        return bought;
    }

    // ------------------------------------------------------------------ маг

    /// <summary>
    /// <c>VisitMage</c>: построитель <c>0x46a125</c> (оценщик vtable <c>0x6d22fc</c>, слот <c>0x10</c> <c>0x46a204</c>): столица
    /// хозяина взята — нет; по местам кэша класса мага (<c>0x798c20</c>): счёт <c>0x46a31d</c> (<see cref="AiTacticEval.VisitMage"/>:
    /// нехватка заклинаний <see cref="SpellDeficit"/> не 0, купить по кошельку есть что — <see cref="MageBuyable"/>), NoScore —
    /// нет. Не вплотную — перемещение к входу; вплотную с очками хода — посещение (<c>0x46a7c9</c>, vtable <c>0x6d239c</c>).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> VisitMages(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        var self = Position(map, stack);
        foreach (var site in PlaceTargets(session, turn, stack))
        {
            if (site.Kind != SiteKind.Mage) continue;
            var at = SiteEntrance(site);
            double score = AiTacticEval.VisitMage(turn.Weights, AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                                  turn.CapturedByOther(stack.Id, site.Id),
                                                  turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day),
                                                  turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day - 1),
                                                  MageBuyable(session, turn.Purse, stack, site).Count > 0,
                                                  SpellDeficit(session, stack.Owner) != 0);
            if (score == AiTactic.NoScore) continue;
            bool adjacent = Cheb(self, at) <= 1;
            if (adjacent && stack.Move < 1) continue;
            yield return (new AiTacticOption(AiActionType.VisitMage, stack.Id, site.Id, at.X, at.Y, Direct: adjacent), score);
        }
    }

    /// <summary>Нехватка заклинаний (<c>0x46a6c6</c>): ⌊сумма известных заклинаний игроков, у которых не взята столица
    /// (<c>0x5e4e73</c>; сам игрок и нейтрал — тоже), / их число⌋ − своё число.</summary>
    public static int SpellDeficit(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var players = session.Map.Players.Values.Where(p => p.CapturedBy.Length == 0).ToList();
        int own = session.Map.Players.TryGetValue(playerId, out var me) ? me.KnownSpells.Count : 0;
        return players.Count == 0 ? -own : players.Sum(p => p.KnownSpells.Count) / players.Count - own;
    }

    /// <summary>Что ИИ купил бы у мага (<c>0x46a589</c>): заклинания башни по порядку, которые сервер продаёт отряду
    /// <paramref name="buyer"/> (<c>0x5dfee2</c>: игроку ещё не известно, казна хозяина покрывает <c>BUY_C</c> со скидкой лидера —
    /// <see cref="GameSession.SpellPrice"/>) и чей <c>BUY_C</c> без скидки (<c>0x582af3</c>) покрывает кошелёк тактики.</summary>
    public static IReadOnlyList<string> MageBuyable(GameSession session, Bank purse, MapStack buyer, MapSite site)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(site);
        var book = session.Book!;
        if (!session.Map.Players.TryGetValue(buyer.Owner, out var player)) return [];
        return [.. site.Spells.Where(id => book.Spells.TryGetValue(id, out var spell)
                                           && !player.KnownSpells.Contains(id, StringComparer.OrdinalIgnoreCase)
                                           && !Economy.NotEnough(player.Bank, session.SpellPrice(buyer, spell))
                                           && !Economy.NotEnough(purse, Treasury.SpellBuyPrice(spell)))];
    }

    /// <summary>
    /// Решение ИИ у мага (колбэк посещения <c>0x46a84a</c> → <c>0x46a89e</c>): список <see cref="MageBuyable"/> перемешивается
    /// (<c>0x4495d8</c>: i-й меняется местами с <c>rng(i)</c>, i = 1…n − 1, R250), купить надо не меньше одного и не меньше
    /// <see cref="SpellDeficit"/>; пока список не пуст и купить ещё надо — первое заклинание уходит из списка, и если кошелёк
    /// покрывает <c>BUY_C</c> (без скидки: <c>0x46a94f</c>/<c>0x46a980</c> — <c>0x582af3</c>) — команда покупки (<c>0x41e608</c> →
    /// <c>0x5d7952</c>, сервер списывает со скидкой лидера), из кошелька — <c>BUY_C</c>. Возвращает купленные.
    /// </summary>
    public static IReadOnlyList<string> BuyAtMage(GameSession session, AiTacticTurn? turn, IRng rng, string stackId, string siteId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rng);
        var map = session.Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)
            || map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Mage } site) return [];
        var purse = turn?.Purse ?? map.Players[stack.Owner].Bank;
        var list = MageBuyable(session, purse, stack, site).ToList();
        for (int i = 1; i < list.Count; i++)
        {
            int j = rng.Next(i);
            (list[i], list[j]) = (list[j], list[i]);
        }
        int wants = Math.Max(SpellDeficit(session, stack.Owner), 1);
        var bought = new List<string>();
        while (list.Count > 0 && wants > 0)
        {
            string id = list[0];
            list.RemoveAt(0);
            // Кошелёк — BUY_C без скидки (0x46a94f, списание 0x46a980), казну сервер списывает со скидкой лидера: у лидера со скидкой
            // кошелёк тактики убывает быстрее казны — как у движка.
            var price = Treasury.SpellBuyPrice(session.Book!.Spells[id]);
            if (Economy.NotEnough(purse, price)) continue;
            session.BuySpell(stackId, siteId, id);
            purse = Treasury.Pay(purse, price);
            turn?.Spend(price);
            bought.Add(id);
            wants--;
        }
        return bought;
    }
}
