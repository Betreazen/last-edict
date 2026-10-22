using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

// «Подарок» ИИ и сделки (docs/ai-diplomacy.md §4–5): кандидаты выбора (AiGift), предложение сделки цели, ответ ИИ-цели или
// ожидание ответа человека, принятие сделки (CEffectAcceptTrade 0x433805), подарок золотом (GiveGold 0x41eafb) и реакция
// ИИ-получателя на него (0x474dd2); сделки и обмен картами от человека (окно дипломатии).

/// <summary>Итог предложения человека: ИИ-цель ответила сразу (<see cref="Accepted"/>), ждёт ответа человека (оба пусты) или
/// предложение не ушло (<see cref="Refusal"/>).</summary>
public readonly record struct ProposalResult(bool? Accepted, string? Refusal);

public sealed partial class GameSession
{
    /// <summary>Выбранный в последней оценке подарок по паре (ИИ, цель): оценка бросает и выбирает, исполнение того же
    /// прохода берёт выбранное.</summary>
    private readonly Dictionary<(string Player, string Target), AiTradeOffer> _gifts = [];

    /// <summary>Выбор подарка вида <paramref name="kind"/> на R250 партии (<see cref="AiGift"/>).</summary>
    private AiTradeOffer? PickGift(string playerId, string targetId, AiGiftKind kind, int relation)
    {
        var offer = kind switch
        {
            AiGiftKind.Item => AiGift.PickItem(Rng, GiftItems(playerId), relation),
            AiGiftKind.Spell => AiGift.PickSpell(Rng, GiftSpells(playerId, targetId), relation),
            _ => AiGift.Gold(Diplomacy.AreAllies(Map.Relations, PartyOf(playerId)!.Value, PartyOf(targetId)!.Value),
                             relation, (int)Map.Players[playerId].Bank.Gold),
        };
        if (offer is null) _gifts.Remove((playerId, targetId));
        else _gifts[(playerId, targetId)] = offer;
        return offer;
    }

    /// <summary>Предметы для подарка (<c>0x448c51</c>): склады своих крепостей в порядке объектов карты (итератор
    /// <c>0x41983f</c>, как у найма), кроме деревень в бунте (<c>0x5e9582</c>: деревня и <c>+0x98</c> ≠ 0); ценность —
    /// <see cref="Treasury.ItemValue"/> (у талисмана без записи зарядов — 0, <c>0x5eb9b3</c>).</summary>
    internal IReadOnlyList<AiGiftItem> GiftItems(string playerId)
    {
        var map = Map;
        var items = Book!.Items;
        var list = new List<AiGiftItem>();
        foreach (var fort in map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village
                                                  && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                                  && !(s.Kind == SiteKind.Village && Fortifications.Rioting(s.RiotTurn))))
            foreach (string item in fort.Items)
                if (map.ItemTypes.TryGetValue(item, out var type) && items.TryGetValue(type, out var def))
                    list.Add(new AiGiftItem(fort.Id, item, (int)Treasury.ItemValue(def, map.TalismanCharges.GetValueOrDefault(item)).Gold));
        return list;
    }

    /// <summary>Заклинания для подарка (<c>0x5cdde7</c>): известные ИИ, кроме призыва, которых не знает цель, — по id.</summary>
    internal IReadOnlyList<AiGiftSpell> GiftSpells(string playerId, string targetId)
    {
        var spells = Book!.Spells;
        var known = new HashSet<string>(Map.Players[targetId].KnownSpells, StringComparer.OrdinalIgnoreCase);
        return [.. Map.Players[playerId].KnownSpells
                   .Where(id => spells.TryGetValue(id, out var s) && s.Category != SpellCategory.Summon && !known.Contains(id))
                   .Select(id => id.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal)
                   .Select(id => new AiGiftSpell(id, spells[id].Level, (int)spells[id].BuyCost.Gold))];
    }

    /// <summary>Подарок, выбранный оценкой этого прохода (<see cref="PickGift"/>), — снимается при исполнении.</summary>
    private AiTradeOffer? TakeGift(string playerId, string targetId) =>
        _gifts.Remove((playerId, targetId), out var offer) ? offer : null;

    /// <summary>Исполнение подарка: сделка (<see cref="ProposeTrade"/>) или золото.</summary>
    internal bool ExecuteGift(string playerId, string targetId, AiTradeOffer offer, List<string>? report)
    {
        if (offer.Kind != AiGiftKind.Gold)
        {
            ProposeTrade(playerId, targetId, offer, report);
            return true;
        }
        // GiveGold 0x41eafb (CEffectGiveGold: казна и отношения за дань, реакция ИИ-получателя), затем сообщение
        // L_AI_GIVE_GOLD (0x838040). ponytail: реакция до сообщения — как синхронный ответ на союз (ранг 2, золота от ИИ
        // в корпусе нет).
        report?.Add($"{playerId} дарит {targetId} {offer.Gold} золота");
        PayTribute(playerId, targetId, offer.Gold);
        ReactToGold(targetId, playerId, report);
        Chat(playerId, targetId, AiMessage.AiGiveGold, report);
        return true;
    }

    /// <summary>Предложение сделки (<c>ProposeTrade 0x41eb74</c>): ИИ-цель отвечает сразу (<see cref="AiDiplomacy.AnswerTrade"/>,
    /// <c>0x474841</c>), человеку предложение ждёт в <see cref="GameState.Offers"/> до его хода.</summary>
    /// <returns>Ответ ИИ-цели; <c>null</c> — ждёт ответа человека.</returns>
    private bool? ProposeTrade(string proposer, string target, AiTradeOffer offer, List<string>? report)
    {
        report?.Add($"{proposer} предлагает {target} {offer.Object} за {offer.Gold} золота");
        if (IsHuman(target))
        {
            OfferToHuman(new AllianceOffer(proposer, target, Map.Turn, offer));
            return null;
        }
        var payer = Map.Players[target];
        bool known = offer.Kind == AiGiftKind.Spell && payer.KnownSpells.Contains(offer.Object, StringComparer.OrdinalIgnoreCase);
        bool accept = AiDiplomacy.AnswerTrade(Rng, offer.Gold, TradePrice(offer), (int)payer.Bank.Gold, known);
        return SettleTrade(proposer, target, offer, accept, report);
    }

    /// <summary>
    /// Сделка от человека (окно дипломатии: предмет — <c>DLG_TRADE_ITEM</c>, склад выбранного города; заклинание —
    /// <c>DLG_GIVE_ITEM_SPELL</c>): тот же <c>ProposeTrade</c>, что у ИИ. ИИ-цель отвечает сразу броском <c>0x474841</c> и ничего
    /// не пишет (реплику пишет только ИИ-предлагавший, <c>0x474aee</c>); человеку предложение ждёт его хода, ответ —
    /// <see cref="AnswerTradeOffer"/>. Предмет — экземпляр в складе своей крепости <paramref name="fort"/>, заклинание — известное
    /// игроку, кроме призыва; <paramref name="gold"/> — цена, которую платит цель.
    /// </summary>
    public ProposalResult ProposeTradeBy(string playerId, string targetId, AiGiftKind kind, string thing, int gold,
                                         string? fort = null, List<string>? report = null)
    {
        if (kind == AiGiftKind.Gold) return new(null, "золото передаётся данью");
        if (playerId.Equals(targetId, StringComparison.OrdinalIgnoreCase)) return new(null, "сделка с собой");
        if (PartyOf(playerId) is null || PartyOf(targetId) is null) return new(null, "не ведёт дипломатию");
        if (gold < 0) return new(null, "цена меньше нуля");
        if (State.Offers.Any(o => o.Trade is not null && o.From.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                  && o.To.Equals(targetId, StringComparison.OrdinalIgnoreCase)))
            return new(null, "прежнее предложение ещё ждёт ответа");
        var map = Map;
        if (kind == AiGiftKind.Spell)
        {
            if (!map.Players[playerId].KnownSpells.Contains(thing, StringComparer.OrdinalIgnoreCase)
                || !Book!.Spells.TryGetValue(thing, out var spell))
                return new(null, "заклинание не известно");
            if (spell.Category == SpellCategory.Summon) return new(null, "призыв не продаётся");
            return new(ProposeTrade(playerId, targetId, new AiTradeOffer(kind, thing.ToUpperInvariant(), "", gold), report), null);
        }
        if (fort is null || !map.Sites.Any(s => s.Id.Equals(fort, StringComparison.OrdinalIgnoreCase)
                                               && s.Kind is SiteKind.Capital or SiteKind.Village
                                               && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                               && s.Items.Contains(thing, StringComparer.OrdinalIgnoreCase)))
            return new(null, "предмета нет в складе крепости");
        return new(ProposeTrade(playerId, targetId, new AiTradeOffer(kind, thing, fort, gold), report), null);
    }

    /// <summary>
    /// Обмен картами от человека (<c>0x40740b</c>; мир проверяет окно дипломатии, <c>0x4842ed</c>): ИИ-цель отвечает сразу
    /// (<see cref="AiDiplomacy.AnswerExchangeMap"/>, <c>0x474d40</c>), человеку предложение ждёт его хода
    /// (<see cref="AnswerExchangeOffer"/>). Реплики ИИ нет.
    /// </summary>
    public ProposalResult ProposeExchangeMapBy(string playerId, string targetId, List<string>? report = null)
    {
        if (playerId.Equals(targetId, StringComparison.OrdinalIgnoreCase)) return new(null, "обмен с собой");
        if (PartyOf(playerId) is null || PartyOf(targetId) is null) return new(null, "не ведёт дипломатию");
        report?.Add($"{playerId} предлагает {targetId} обмен картами");
        if (IsHuman(targetId))
        {
            if (!State.Offers.Any(o => o.Exchange && o.From.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                       && o.To.Equals(targetId, StringComparison.OrdinalIgnoreCase)))
                OfferToHuman(new AllianceOffer(playerId, targetId, Map.Turn, Exchange: true));
            return new(null, null);
        }
        bool accept = AiDiplomacy.AnswerExchangeMap(Rng);
        SettleExchange(playerId, targetId, accept, report);
        return new(accept, null);
    }

    /// <summary>Предложение человеку — в <see cref="GameState.Offers"/>; от ИИ — сразу вопрос наблюдателю
    /// (<see cref="ITurnObserver.Offered"/>: у движка ответ человека приходит в ход ИИ). От человека («горячий стул») — гонец сцены.</summary>
    private void OfferToHuman(AllianceOffer offer)
    {
        State = State with { Offers = [.. State.Offers, offer] };
        if (!IsHuman(offer.From)) Observer?.Offered(offer);
    }

    /// <summary>Ответ человека на обмен картами от человека (<c>0x41ee50</c>). Вернёт причину отказа или <c>null</c>.</summary>
    public string? AnswerExchangeOffer(string fromId, string humanId, bool accept, List<string>? report = null)
    {
        var offer = State.Offers.FirstOrDefault(o => o.Exchange && o.From.Equals(fromId, StringComparison.OrdinalIgnoreCase)
                                                    && o.To.Equals(humanId, StringComparison.OrdinalIgnoreCase));
        if (offer is null) return $"предложения обмена картами от {fromId} игроку {humanId} нет";
        State = State with { Offers = [.. State.Offers.Where(o => o != offer)] };
        if (PartyOf(fromId) is null || PartyOf(humanId) is null) return $"{fromId} больше не ведёт дипломатию — предложение снято";
        SettleExchange(fromId, humanId, accept, report);
        return null;
    }

    /// <summary>Итог обмена картами (эффект <c>0x438010</c>): сначала — и при отказе тоже — предлагавшему запоминается день обмена с
    /// целью (<see cref="MapPlayer.MapExchanges"/>, <c>0x43803f</c> → <c>CVisitorPlayerSetLastExchangeMapTurn</c>); затем при согласии
    /// (<c>0x438044</c>) клетка, открытая у одного из двух, открывается и у другого (<c>0x5d927b</c>).</summary>
    private void SettleExchange(string proposer, string target, bool accept, List<string>? report)
    {
        report?.Add($"{target} {(accept ? "принимает" : "отклоняет")} обмен картами с {proposer}");
        var before = Map;
        if (before.Players.TryGetValue(proposer, out var self))
        {
            var exchanges = (self.MapExchanges ?? new Dictionary<string, int>()).Where(p => !Same(p.Key, target)).ToDictionary(StringComparer.OrdinalIgnoreCase);
            exchanges[target] = before.Turn;
            var players = before.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            players[proposer] = self with { MapExchanges = exchanges };
            State = State with { Looks = Retold(Looks, With(before, players: players), Looks.Stacks) };
        }
        if (!accept) return;
        var map = Map;
        string a = map.Players[proposer].FogId, b = map.Players[target].FogId;
        if (!map.Fog.TryGetValue(a, out var fa) || !map.Fog.TryGetValue(b, out var fb)) return;
        var merged = (bool[,])fa.Clone();
        for (int x = 0; x < merged.GetLength(0); x++)
            for (int y = 0; y < merged.GetLength(1); y++)
                merged[x, y] = fa[x, y] && fb[x, y];
        var fog = map.Fog.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        fog[a] = merged;
        fog[b] = (bool[,])merged.Clone();
        State = State with { Looks = Retold(Looks, With(map, fog: fog), Looks.Stacks) };
    }

    /// <summary>Реакция ИИ-получателя на дань (<c>0x474dd2</c>): сообщение дарителю — <c>PLAYER_GIVE_GOLD</c>, при вечной войне
    /// пары — <c>PLAYER_GIVE_GOLD_N</c>. Корпус (<c>react_474dd2</c>): 10 даней человека, 10 сообщений категории 5.</summary>
    private void ReactToGold(string recipient, string giver, List<string>? report)
    {
        if (IsHuman(recipient) || PartyOf(recipient) is not { } a || PartyOf(giver) is not { } b) return;
        bool eternal = Diplomacy.AtEternalWar(Map.Relations, b, a);
        Chat(recipient, giver, eternal ? AiMessage.PlayerGiveGoldN : AiMessage.PlayerGiveGold, report);
    }

    /// <summary>Ответ человека на сделку от ИИ (<c>AcceptTrade 0x41ebf3</c>). Вернёт причину отказа или <c>null</c>; согласие,
    /// которое не может пройти (нет золота, предмета), предложение не снимает — остаётся отказаться.</summary>
    public string? AnswerTradeOffer(string fromAi, string humanId, bool accept, List<string>? report = null)
    {
        var offer = State.Offers.FirstOrDefault(o => o.Trade is not null && o.From.Equals(fromAi, StringComparison.OrdinalIgnoreCase)
                                                    && o.To.Equals(humanId, StringComparison.OrdinalIgnoreCase));
        if (offer is null) return $"предложения сделки от {fromAi} игроку {humanId} нет";
        bool gone = !Map.Players.ContainsKey(offer.From) || PartyOf(offer.From) is null;
        if (accept && !gone && TradeRefusal(humanId, offer.From, offer.Trade!) is { } why) return why;
        State = State with { Offers = [.. State.Offers.Where(o => o != offer)] };
        if (gone) return $"{offer.From} больше нет — предложение снято";
        SettleTrade(offer.From, humanId, offer.Trade!, accept, report);
        return null;
    }

    /// <summary>Цена предмета или заклинания в ответе цели (<c>0x474841</c>): заклинание — <see cref="AiDiplomacy.SpellTradePrice"/>,
    /// предмет — <see cref="Treasury.ItemValue"/>.</summary>
    private int TradePrice(AiTradeOffer offer) => offer.Kind == AiGiftKind.Spell
        ? Book!.Spells[offer.Object] is var s ? AiDiplomacy.SpellTradePrice((int)s.BuyCost.Gold, s.Level) : 0
        : ItemTradeValue(offer.Object);

    private int ItemTradeValue(string item) =>
        Map.ItemTypes.TryGetValue(item, out var type) && Book!.Items.TryGetValue(type, out var def)
            ? (int)Treasury.ItemValue(def, Map.TalismanCharges.GetValueOrDefault(item)).Gold
            : 0;

    /// <summary>Почему сделка не может пройти (<c>0x5da985</c>: у плательщика хватает золота; заклинание плательщику ещё не
    /// известно — предложение могло устареть, пока ждало ответа человека; предмет ещё в складе крепости предлагавшего, у плательщика
    /// есть столица, куда он ляжет).</summary>
    private string? TradeRefusal(string payerId, string proposer, AiTradeOffer offer)
    {
        var map = Map;
        if (map.Players[payerId].Bank.Gold < offer.Gold) return "не хватает золота";
        if (offer.Kind == AiGiftKind.Spell)
            return map.Players[payerId].KnownSpells.Contains(offer.Object, StringComparer.OrdinalIgnoreCase) ? "заклинание уже известно" : null;
        if (!map.Sites.Any(s => s.Id.Equals(offer.Container, StringComparison.OrdinalIgnoreCase)
                                && s.Owner.Equals(proposer, StringComparison.OrdinalIgnoreCase)
                                && s.Items.Contains(offer.Object, StringComparer.OrdinalIgnoreCase)))
            return "предмета больше нет";
        return CapitalOf(payerId) is null ? "нет столицы, куда положить предмет" : null;
    }

    /// <summary>
    /// Итог сделки (<c>CEffectAcceptTrade 0x433805</c>, строки <c>trade_accept</c>): при согласии золото — предлагавшему, затем
    /// с плательщика (две строки <c>bank</c> в этом порядке); предмет — из склада крепости предлагавшего в склад столицы
    /// плательщика (<c>0x5d90d7</c> → <c>0x5e9450</c>, <c>0x5e73cb</c>), заклинание — плательщику в известные (<c>0x5d9134</c>);
    /// отношения за обмен (<see cref="Diplomacy.TradeValue"/>, <c>a</c> — предлагавший): у заклинания цена — золото
    /// <c>BUY_C</c> без уровня (probe65 seq 2947307: 200 − 300 → 0), у предмета — ценность. Затем предлагавший ИИ отвечает
    /// сообщением <c>TRADE_ACCEPTED</c>/<c>TRADE_REFUSED</c>.
    /// </summary>
    private bool SettleTrade(string proposer, string payerId, AiTradeOffer offer, bool accept, List<string>? report)
    {
        if (accept && TradeRefusal(payerId, proposer, offer) is null)
        {
            var book = Book!;
            var map = Map;
            var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            var (payerBank, proposerBank) = Treasury.Tribute(players[payerId].Bank, players[proposer].Bank, offer.Gold);
            players[proposer] = WithBank(players[proposer], proposerBank);
            players[payerId] = WithBank(players[payerId], payerBank);
            var sites = map.Sites;
            int basePrice;
            if (offer.Kind == AiGiftKind.Spell)
            {
                var payer = players[payerId];
                players[payerId] = payer with { KnownSpells = [.. payer.KnownSpells, offer.Object.ToUpperInvariant()] };
                basePrice = (int)book.Spells[offer.Object].BuyCost.Gold;
            }
            else
            {
                basePrice = ItemTradeValue(offer.Object);
                string capital = CapitalOf(payerId)!.Id;
                sites = [.. sites.Select(s =>
                    s.Id.Equals(offer.Container, StringComparison.OrdinalIgnoreCase)
                        ? s with { Items = [.. s.Items.Where(i => !i.Equals(offer.Object, StringComparison.OrdinalIgnoreCase))] }
                        : s.Id.Equals(capital, StringComparison.OrdinalIgnoreCase) ? s with { Items = [.. s.Items, offer.Object] } : s)];
            }
            var change = Diplomacy.Change(map.Relations, Diplomacy.PlayerParties(map, book.Economy), book.Diplomacy,
                                          PartyOf(proposer)!.Value, PartyOf(payerId)!.Value,
                                          Diplomacy.TradeValue(book.Diplomacy, basePrice, offer.Gold), false);
            State = State with { Looks = Retold(Looks, With(map, players: players, sites: sites, relations: change.Relations), Looks.Stacks) };
        }
        else accept = false;
        report?.Add($"{payerId} {(accept ? "принимает" : "отклоняет")} сделку {proposer}: {offer.Object} за {offer.Gold}");
        if (!IsHuman(proposer)) Chat(proposer, payerId, accept ? AiMessage.TradeAccepted : AiMessage.TradeRefused, report);
        return accept;
    }

    /// <summary>Дань золотом (<c>CEffectGiveGold::apply 0x4384de</c>): казна (<see cref="Treasury.Tribute"/>) и отношения
    /// (<see cref="Diplomacy.Tribute"/>).</summary>
    private void PayTribute(string payerId, string recipientId, int gold)
    {
        var book = Book!;
        var map = Map;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var (payerBank, recipientBank) = Treasury.Tribute(players[payerId].Bank, players[recipientId].Bank, gold);
        players[payerId] = WithBank(players[payerId], payerBank);
        players[recipientId] = WithBank(players[recipientId], recipientBank);
        var change = Diplomacy.Tribute(map.Relations, Diplomacy.PlayerParties(map, book.Economy), book.Diplomacy,
                                       PartyOf(payerId)!.Value, PartyOf(recipientId)!.Value, gold);
        State = State with { Looks = Retold(Looks, With(map, players: players, relations: change.Relations), Looks.Stacks) };
    }

    private MapSite? CapitalOf(string playerId) =>
        Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
}
