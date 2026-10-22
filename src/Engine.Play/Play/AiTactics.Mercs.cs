using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>
/// Наёмники ИИ (<c>HireMercs</c>, тип 0xb; <c>docs/ai-tactic-candidates.md</c> §13): построитель, проверка «есть кого нанять»
/// и решение ИИ в лагере. Ранг 1 по листингам 25.09, если не сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// <c>HireMercs</c>: построитель <c>0x458cf8</c> (оценщик <c>CAiHireMercsActionEval</c>, vtable <c>0x6d0dfc</c>). Кэша мест у
    /// него нет — все лагеря карты (<c>0x4599d7</c>: площадки по порядку объектов, <c>dynamic_cast</c> к <c>0x798298</c>):
    /// <list type="bullet">
    /// <item>свободных клеток отряда (<c>0x5f4485</c>) нет и <c>SUGG_LVL</c> ниже 10 — нет;</item>
    /// <item>столица хозяина взята (слот <c>0x18</c> обёртки) — нет; при <c>SUGG_LVL ≥ 10</c> — отряд слабее половины сильнейшего
    /// отряда человека (<see cref="TrainerWanted"/>, <c>0x4692bf</c>), иначе нет;</item>
    /// <item>лагерь с непустым ростером (<c>+0x78</c>, <c>0x6012c5</c>), где есть кого нанять по кошельку тактики
    /// (<see cref="MercsAffordable"/>, <c>0x458e6c</c>): точка — вход (<c>0x601181</c>), счёт <c>0x4592cc</c>
    /// (<see cref="AiTacticEval.HireMercs"/>: <c>I_H_MERCS</c> + влияние, дальше 20 — NoScore), NoScore — нет.</item>
    /// </list>
    /// Действие одно — <c>CAiHireMercsAction</c> (<c>0x459466</c>, vtable <c>0x6d0ea4</c>): перемещение к входу (режим 1, N = 0) и
    /// найм, когда путь заходит в лагерь (<see cref="HireAtMercs"/>). В журналах human и legions кандидатов 0xb нет ни одного.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> HireMercs(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        int free = session.FreeCells(stack);
        bool high = session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel;
        if (!high && free == 0) yield break;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        if (high && !TrainerWanted(session, turn, stack)) yield break;
        var self = Position(map, stack);
        foreach (var site in map.Sites.Where(s => s.Kind == SiteKind.Mercenaries && s.Roster.Count > 0))
        {
            if (!MercsAffordable(session, turn, stack, site, free)) continue;
            var at = SiteEntrance(site);
            double score = AiTacticEval.HireMercs(turn.Weights, AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at), free,
                                                  turn.CapturedByOther(stack.Id, site.Id), high,
                                                  turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day));
            if (score == AiTactic.NoScore) continue;
            yield return (new AiTacticOption(AiActionType.HireMercs, stack.Id, site.Id, at.X, at.Y), score);
        }
    }

    /// <summary>
    /// Есть кого нанять (<c>0x458e6c</c>): предложение ростера, чью цену (слот <c>0x54</c> реализации — <see cref="GameBook.HirePrice"/>
    /// уровня предложения, через <see cref="SiteTrade.AiPrice"/>, <c>0x61eedd</c>) кошелёк тактики покрывает. При <c>SUGG_LVL ≥ 10</c>
    /// и полном отряде вместо этого — есть ли кем заменить слабейшего (<see cref="CanReplaceWeakest"/>, <c>0x458f8a</c>).
    /// </summary>
    public static bool MercsAffordable(GameSession session, AiTacticTurn turn, MapStack stack, MapSite site, int free)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(site);
        bool high = session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel;
        if (high && free == 0) return site.Roster.Count > 0 && CanReplaceWeakest(session, turn.Purse, site, stack);
        return site.Roster.Any(o => MercPrice(session, o) is { } price && !Economy.NotEnough(turn.Purse, price));
    }

    /// <summary>
    /// Слабейший воин отряда (<c>0x4590ba</c>): не лидер (<c>+0x28</c>), по порядку группы; вес — опыт за убийство
    /// (<c>XP_KILLED</c> реализации его уровня, слот <c>0x3c</c>), у большого — половина (<c>cdq; sub; sar 1</c>); меньший вес
    /// сменяет прежнего только строго. Второе — сырой <c>XP_KILLED</c> слабейшего, с ним сравнивают предложения.
    /// </summary>
    internal static (string Unit, int XpKilled)? Weakest(GameSession session, MapStack stack)
    {
        var book = session.Book!;
        (string Unit, int XpKilled)? best = null;
        int bestWeight = 0;
        foreach (string id in stack.UnitIds)
        {
            if (Same(id, stack.LeaderId) || !session.Map.Units.TryGetValue(id, out var unit)
                || book.Grown(unit.TypeId, unit.Level) is not { } grown) continue;
            int xp = grown.Type.XpKilled;
            int weight = grown.Type.SizeSmall ? xp : xp / 2;
            if (best is not null && weight >= bestWeight) continue;
            best = (id, xp);
            bestWeight = weight;
        }
        return best;
    }

    /// <summary>Опыт за убийство воина предложения (реализация его уровня, слот <c>0x3c</c>); нет в правилах — −1.</summary>
    private static int OfferXpKilled(GameSession session, MercenaryOffer offer) =>
        session.Book!.Grown(offer.UnitType, offer.Level)?.Type.XpKilled ?? -1;

    /// <summary>Есть ли кем заменить слабейшего (<c>0x458f8a</c>): у предложения опыт за убийство строго больше, чем у
    /// слабейшего (<see cref="Weakest"/>), и цену (<see cref="MercPrice"/>) кошелёк покрывает.</summary>
    private static bool CanReplaceWeakest(GameSession session, Bank purse, MapSite site, MapStack stack) =>
        Weakest(session, stack) is { } weak
        && site.Roster.Any(o => OfferXpKilled(session, o) > weak.XpKilled
                                && MercPrice(session, o) is { } price && !Economy.NotEnough(purse, price));

    /// <summary>Цена предложения для ИИ: <see cref="GameBook.HirePrice"/> уровня предложения и чит <see cref="SiteTrade.AiPrice"/>.</summary>
    private static Bank? MercPrice(GameSession session, MercenaryOffer offer) =>
        session.Book!.HirePrice(offer.UnitType, offer.Level) is { } price
            ? SiteTrade.AiPrice(price, session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel, session.State.Info.Difficulty)
            : null;

    /// <summary>
    /// Решение ИИ в лагере (колбэк посещения <c>0x459504</c> → <c>0x459570</c>): копия ростера (<c>0x459b54</c>) одна на всё
    /// посещение; если у отряда есть свободные клетки (<c>0x5f4485</c>, считается один раз), то по клеткам 0…5 — у каждой пустой
    /// (<c>0x5f4537</c>) попытка найма (<see cref="TryHireAt"/>, <c>0x4597bf</c>). Если никого не наняли, а <c>SUGG_LVL ≥ 10</c>
    /// (<c>0x61eb31</c>), — замена слабейшего: пока слабейший (<see cref="Weakest"/>) не из нанятых здесь же, он увольняется
    /// (<c>0x41f059</c> → <see cref="GameSession.DismissUnit"/>) и по пустым клеткам 0…5 идут попытки с порогом его опыта за
    /// убийство; первая неудачная кончает замену (уволенный так и уходит — движок его не возвращает).
    /// Возвращает, нанят ли кто-нибудь: статус действия — 1, иначе 2 (<c>+0x58</c>).
    /// </summary>
    public static bool HireAtMercs(GameSession session, AiTacticTurn? turn, IRng rng, string stackId, string siteId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rng);
        var map = session.Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)
            || map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Mercenaries }) return false;
        var roster = map.Sites.First(s => Same(s.Id, siteId)).Roster.ToList();
        var purse = turn?.Purse ?? map.Players[stack.Owner].Bank;
        bool high = session.State.Info.SuggestedLevel >= AiStackUnits.HighSuggestedLevel;
        bool hired = false;
        if (session.FreeCells(stack) > 0)
            for (int cell = 0; cell < UnitCells.CellCount; cell++)
                if (EmptyCell(session, stackId, cell) && TryHireAt(session, turn, rng, stackId, siteId, roster, cell, ref purse, high, 0))
                    hired = true;
        if (hired || !high) return hired;
        var fresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (Weakest(session, session.Map.Stacks[stackId]) is { } weak && !fresh.Contains(weak.Unit))
        {
            if (session.DismissUnit(stackId, weak.Unit) != 0) break;
            bool failed = false;
            for (int cell = 0; cell < UnitCells.CellCount && !failed; cell++)
            {
                if (!EmptyCell(session, stackId, cell)) continue;
                if (!TryHireAt(session, turn, rng, stackId, siteId, roster, cell, ref purse, high, weak.XpKilled))
                {
                    failed = true;
                    continue;
                }
                hired = true;
                var now = session.Map.Stacks[stackId];
                fresh.Add(UnitCells.Of(now.UnitIds, now.Slots).Cells[cell]);
            }
            if (failed) break;
        }
        return hired;
    }

    private static bool EmptyCell(GameSession session, string stackId, int cell)
    {
        var now = session.Map.Stacks[stackId];
        return UnitCells.Of(now.UnitIds, now.Slots).Cells[cell].Length == 0;
    }

    /// <summary>
    /// Попытка найма в клетку (<c>0x4597bf</c>): копия ростера перемешивается на месте (<c>0x459b1c</c>: i = 1…n − 1, обмен с
    /// R250(i)); берётся первое предложение, чью цену кошелёк тактики покрывает, а при <c>SUGG_LVL ≥ 10</c> ещё: опыт за
    /// убийство не меньше <paramref name="minXp"/>, большой — только в чётную клетку с пустой соседней, проверка найма у
    /// наёмника (<see cref="MercHireable"/>, <c>0x5e00c6</c>) — иначе следующее предложение. Место
    /// (<see cref="GameSession.PlaceRefusal"/>, <c>0x5e78f7</c>) не годно — попытка кончена без найма; годно — команда найма
    /// (<c>0x41e650</c> → <see cref="GameSession.HireMercenary"/>) и цена из кошелька (<c>0x586578</c>) — <b>и когда сервер
    /// отказал</b> (копия не знает, что уникальный уже нанят). Вернёт, нанял ли сервер.
    /// </summary>
    private static bool TryHireAt(GameSession session, AiTacticTurn? turn, IRng rng, string stackId, string siteId,
                                  List<MercenaryOffer> roster, int cell, ref Bank purse, bool high, int minXp)
    {
        for (int i = 1; i < roster.Count; i++)
        {
            int j = rng.Next(i);
            (roster[i], roster[j]) = (roster[j], roster[i]);
        }
        foreach (var offer in roster)
        {
            if (MercPrice(session, offer) is not { } price) continue;
            if (high)
            {
                if (OfferXpKilled(session, offer) < minXp) continue;
                var now = session.Map.Stacks[stackId];
                if (session.Book!.Grown(offer.UnitType, offer.Level) is { Type.SizeSmall: false }
                    && (!Formation.IsFront(cell) || UnitCells.Of(now.UnitIds, now.Slots).Cells[Formation.Pair(cell)].Length > 0)) continue;
            }
            if (Economy.NotEnough(purse, price)) continue;
            if (high && !MercHireable(session, stackId, siteId, offer)) continue;
            if (session.PlaceRefusal(stackId, offer.UnitType, cell) is not null) return false;
            bool ok = session.HireMercenary(stackId, siteId, offer.UnitType, cell, ai: true) is null;
            purse = Treasury.Pay(purse, price);
            turn?.Spend(price);
            return ok;
        }
        return false;
    }

    /// <summary>Проверка найма у наёмника (<c>merc_check</c>, <c>0x5e00c6</c>; ранг 2 — по сути, не построчно): предложение ещё
    /// в ростере (копия посещения этого не знает), и сервер взял бы его — место в отряде по лидерству и казна хозяина.</summary>
    private static bool MercHireable(GameSession session, string stackId, string siteId, MercenaryOffer offer)
    {
        var map = session.Map;
        var stack = map.Stacks[stackId];
        if (map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { } site
            || !site.Roster.Any(o => Same(o.UnitType, offer.UnitType))
            || session.MercenaryPrice(stack, offer) is not { } price) return false;
        var aiPrice = SiteTrade.AiPrice(price, true, session.State.Info.Difficulty);
        bool small = session.Book!.Grown(offer.UnitType, offer.Level)?.Type.SizeSmall ?? true;
        return session.FreeCells(stack) >= (small ? 1 : 2) && !Economy.NotEnough(map.Players[stack.Owner].Bank, aiPrice);
    }
}
