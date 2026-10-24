using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Map;

/// <summary>Вид подарка ИИ — второй бросок <c>rng(3)</c> в <c>0x448bda</c>: 0 предмет (<c>0x448c51</c>),
/// 1 заклинание (<c>0x448e84</c>), 2 золото (<c>0x448fc6</c>). Ранг 1 по листингу.</summary>
public enum AiGiftKind
{
    Item = 0,
    Spell = 1,
    Gold = 2,
}

/// <summary>
/// Дипломатия ИИ на карте: ответы на предложения игрока (<c>0x474fde</c>) и собственные решения
/// <c>CAiDiplomacyEval::evaluate</c> (<c>0x4486f5</c>). Разбор — <c>docs/synthetic-maps/analysis/J-map-ai.md</c>
/// и <c>J-map-ai-notes.md</c> (disciples-modding). Все броски — <c>rng(100)</c> обёртки R250 <c>0x54851f</c>.
/// Бросок чата (<c>0x4733a4</c>), который сопровождает каждое действие, сюда не входит.
/// </summary>
public static class AiDiplomacy
{
    private const int MinChance = 5;               // max(5, …) в 0x474c21 и 0x448ae2
    private const int SharePenalty = 10;           // rel − 10 − s
    private const int FirstDayOfferRelation = 90;  // 0x448aad: rel ≥ 90 в день 1 → 100
    private const int ExchangeMapChance = 30;      // 0x474d40
    private const int GiftPercent = 20;            // 0x448bda: rel·20/100
    private const int GiftKinds = 3;
    private const double TradeHalf = 50.0;         // 0x6d2858 = 50f, 0x6d285c = −50f

    /// <summary>
    /// Доля равнины расы, % (<c>0x474c5b</c>, ранг 1): <c>trunc(100 · своих / всех)</c>, где считаются
    /// ТОЛЬКО клетки грунта <c>L_PLAIN</c> (обход <c>0x5e5d50</c> → <c>0x5f33c9</c>); лес не входит — в отличие
    /// от экономики. Своя клетка — террейн расы (<see cref="EconomyRules.TerrainOf"/>; без правил — по роду, <see cref="LegacyRaces.Terrain"/>);
    /// при общей расе (<see cref="LandOwnership"/>) — клетка, чей хозяин на этой стороне.
    /// </summary>
    /// <param name="raceType">Сторона дипломатии игрока (<see cref="Diplomacy.PartyOf(MapPlayer, IReadOnlyDictionary{string, RaceInfo})"/>; у движка —
    /// <c>LRaceCategory</c>, 0 Империя … 5 эльфы).</param>
    public static int PlainLandShare(MapState map, int raceType, EconomyRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        int own = rules is null ? LegacyRaces.Terrain(raceType) : Diplomacy.TerrainOfParty(map, rules, raceType), total = 0, mine = 0;
        var shared = rules is not null && LandOwnership.Tracked(map) ? LandOwnership.SharedTerrains(map, rules) : null;
        var partyBySlot = shared is null ? [] : LandOwnership.PartyBySlot(map, rules!);
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
            {
                var tile = map.Tiles[x, y];
                if (tile.Ground != Ground.Plain) continue;
                total++;
                if (tile.Terrain != own) continue;
                if (shared is null || !shared.ContainsKey(own)
                    || (map.LandOwners![x, y] is var slot && slot > 0 && slot < partyBySlot.Length && partyBySlot[slot] == raceType)) mine++;
            }
        // ponytail: пустая равнина даёт 0 без деления; в движке это деление double на 0 (не встречалось).
        return total == 0 ? 0 : (int)(100.0 * mine / total);
    }

    /// <summary>Шанс согласия ИИ на союз (<c>0x474c21</c>, ранг 1): <c>max(5, rel − 10 − s)</c>; порога 66 в
    /// ответе нет. Корпус q45: 56 %.</summary>
    /// <param name="relation">Текущее отношение ИИ к предлагающему (<c>0x5f788f</c>).</param>
    /// <param name="proposerShare"><see cref="PlainLandShare"/> предлагающего.</param>
    public static int AnswerAllianceChance(int relation, int proposerShare) =>
        Math.Max(MinChance, relation - SharePenalty - proposerShare);

    /// <summary>Ответ ИИ на союз (<c>0x474b37</c>, ранг 1): один бросок, <c>rng(100) &lt; шанс</c>; итог
    /// уходит в <c>AcceptAlliance 0x41eceb</c>.</summary>
    public static bool AnswerAlliance(IRng rng, int relation, int proposerShare)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Next(100) < AnswerAllianceChance(relation, proposerShare);
    }

    /// <summary>Цена заклинания в сделке (<c>0x474841</c>, ранг 1): золото <c>BUY_C</c> (<c>0x582af3</c>)
    /// · (<c>LEVEL</c> + 1) (<c>0x535361</c>). Цена предмета — <c>0x600a20</c> (<see cref="Treasury.ItemValue"/>).</summary>
    public static int SpellTradePrice(int buyGold, int level) => buyGold * (level + 1);

    /// <summary>
    /// Шанс согласия на сделку (<c>0x4749c8</c>, ранг 1): при <c>g &lt; p</c> — <c>50 − trunc(50g/p − 50)</c>,
    /// иначе <c>50 + trunc(50p/g − 50)</c>. Корпус q46: 97/93/60/83.
    /// </summary>
    /// <param name="gold">Доплата золотом со стороны ИИ.</param>
    /// <param name="price">Цена предмета или заклинания.</param>
    public static int TradeChance(int gold, int price)
    {
        if (gold < price) return (int)TradeHalf - (int)(TradeHalf * gold / price - TradeHalf);
        // ponytail: g = p = 0 в движке — 0/0 на x87; не встречалось, отдаём середину (ранг 3).
        if (gold == 0) return (int)TradeHalf;
        return (int)TradeHalf + (int)(TradeHalf * price / gold - TradeHalf);
    }

    /// <summary>Ответ ИИ на сделку (<c>0x474841</c>, ранг 1). Отказ без броска, если доплата больше золота
    /// ИИ или ИИ уже знает предлагаемое заклинание; иначе <c>rng(100) &lt; </c><see cref="TradeChance"/>.</summary>
    /// <param name="aiGold">Золото в казне ИИ.</param>
    /// <param name="known">Предлагается заклинание, которое ИИ уже знает (<c>player+0x44</c>).</param>
    public static bool AnswerTrade(IRng rng, int gold, int price, int aiGold, bool known)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (gold > aiGold || known) return false;
        return rng.Next(100) < TradeChance(gold, price);
    }

    /// <summary>Ответ ИИ на обмен картами (<c>0x474d40</c>, ранг 1): <c>rng(100) &lt; 30</c>; итог — <c>0x41ee50</c>.</summary>
    public static bool AnswerExchangeMap(IRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Next(100) < ExchangeMapChance;
    }

    /// <summary>
    /// Шанс собственного предложения союза (<c>0x448aad</c>, формула <c>0x448ae2</c>; ранг 1):
    /// <c>null</c> — при <c>rel ≤ D_NEUTRAL</c> броска нет; <c>rel ≥ 90</c> в день 1 — 100; иначе
    /// <c>max(5, rel − 10 − s)</c>. Корпус: предложения 3/0 (все rel 95, день 1).
    /// </summary>
    /// <param name="relation">Отношение другого игрока к ИИ.</param>
    /// <param name="turn">День (<c>CScenarioInfo.CUR_TURN</c>, <see cref="MapState.Turn"/>).</param>
    /// <param name="share"><see cref="PlainLandShare"/> другого игрока.</param>
    public static int? OfferChance(DiplomacyRules rules, int relation, int turn, int share)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (relation <= rules.Neutral) return null;
        return relation >= FirstDayOfferRelation && turn == 1 ? 100 : AnswerAllianceChance(relation, share);
    }

    /// <summary>Решение предложить союз (<c>0x448aad</c>): бросок тратится всегда, когда есть
    /// <see cref="OfferChance"/>, даже при шансе 100.</summary>
    public static bool ShouldOffer(IRng rng, DiplomacyRules rules, int relation, int turn, int share)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return OfferChance(rules, relation, turn, share) is { } chance && rng.Next(100) < chance;
    }

    /// <summary>
    /// Шанс разрыва союза (<c>0x448897</c>, ранг 1). <c>null</c> — броска нет: бит 31 пары или
    /// <c>d = день − ход союза ≤ T</c>. «Сильнее» — у ИИ больше крепостей (<c>CFortification</c>, владелец
    /// <c>+0x84</c>); <c>T = D_BKA_TURN</c>, у сильного <c>T/2</c>; шанс <c>min(d − T, D_BKA_CHNC)</c>, у сильного
    /// ×2. Хук <c>ai_break_check</c>, корпус 20260917-synth q09: 117/117 (шанс/<c>null</c>, бросок ⇔ шанс,
    /// <c>ret == roll &lt; шанс</c>); из 38 бросков сработал один — seq 11505827 (PL4→PL3, день 16, d = 16,
    /// шанс 5, бросок 2). Долгая жизнь союзов объясняется не формулой, а тем, что решение исполняется лишь на
    /// проходе без экономики (<see cref="RunPasses"/>).
    /// </summary>
    /// <param name="breakTurn"><c>GVars.D_BKA_TURN</c> (<c>+0xec</c>).</param>
    /// <param name="breakChance"><c>GVars.D_BKA_CHNC</c> (<c>+0xe8</c>).</param>
    public static int? BreakChance(Relation pair, int turn, int ownForts, int otherForts,
                                   int breakTurn = 10, int breakChance = 5)
    {
        if (pair.AiCannotBreak) return null;
        bool strong = ownForts > otherForts;
        int threshold = strong ? breakTurn / 2 : breakTurn;
        int days = turn - pair.AllianceTurn;
        if (days <= threshold) return null;
        int chance = Math.Min(days - threshold, breakChance);
        return strong ? chance * 2 : chance;
    }

    /// <summary>Решение разорвать союз (<c>0x448897</c>, ранг 1 — см. <see cref="BreakChance"/>):
    /// <c>rng(100) &lt; шанс</c>, бросок только при наличии шанса.</summary>
    public static bool ShouldBreak(IRng rng, Relation pair, int turn, int ownForts, int otherForts,
                                   int breakTurn = 10, int breakChance = 5)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return BreakChance(pair, turn, ownForts, otherForts, breakTurn, breakChance) is { } chance
               && rng.Next(100) < chance;
    }

    /// <summary>
    /// Первые два броска подарка (<c>0x448bda</c>, ранг 1): при <c>rel &gt; D_NEUTRAL</c> —
    /// <c>rng(100) &lt; rel·20/100</c>, затем вид <c>rng(3)</c>. <c>null</c> — подарка нет. «Подарок» — это
    /// предложение сделки: предмет или заклинание за золото (<c>CAiDiploItem</c>/<c>CAiDiploSpell</c> →
    /// <c>ProposeTrade</c>, цель отвечает <see cref="AnswerTrade"/>) или золото — только не союзнику (<see cref="AiGift"/>).
    /// Выбор предмета и заклинания бросает ещё — перемешивание кандидатов (<see cref="AiGift.Shuffle{T}"/>). Корпус
    /// <c>ai_gift_check</c>: 424 вызова.
    /// </summary>
    public static AiGiftKind? GiftRoll(IRng rng, DiplomacyRules rules, int relation)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rules);
        if (relation <= rules.Neutral) return null;
        if (rng.Next(100) >= relation * GiftPercent / 100) return null;
        return (AiGiftKind)rng.Next(GiftKinds);
    }

    /// <summary>
    /// Сообщение ИИ (<c>0x4733a4</c>, ранг 1 по листингу): номер — <c>rng(N)</c>, где N — число строк <c>TAiMsg</c>
    /// расы отправителя в категории; <c>rng(0)</c> броска не тратит. Чат сопровождает каждое действие дипломатии и
    /// ответы на него, поэтому сдвигает общий R250: ненависть, предложение (до команды), разрыв (после), ответ
    /// предлагающего на согласие и отказ, реакция на разрыв (корпус <c>ai_chat</c>, 75 строк, сдвиг 1 у каждой).
    /// </summary>
    /// <param name="messages">Строк <c>TAiMsg</c> расы отправителя в категории.</param>
    public static int Chat(IRng rng, int messages)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Next(messages);
    }

    /// <summary>
    /// Решение по одной цели (<c>CAiDiplomacyEval::evaluate 0x4486f5</c>, ранг 1 по листингу; ветки сверены
    /// хуками 20260917-synth). Союзник — <see cref="ShouldBreak"/>, успех — разрыв без проверки подарка.
    /// Не союзник — <see cref="ShouldOffer"/>, успех — предложение без подарка; иначе в день 1 — ненависть
    /// (<c>CAiDiploHatePlayer</c>). Затем при <c>rel &gt; D_NEUTRAL</c> — <see cref="GiftRoll"/> и сразу выбор подарка
    /// выпавшего вида (<paramref name="pickGift"/>, его броски — в той же оценке); выбора нет — нет и действия.
    /// </summary>
    /// <param name="pair">Пара ИИ и цели; <c>rel</c> — <see cref="Relation.Current"/>.</param>
    /// <param name="ownForts">Крепости ИИ (для «сильнее»).</param>
    /// <param name="otherShare"><see cref="PlainLandShare"/> цели.</param>
    /// <param name="pickGift">Выбор подарка по виду (<see cref="AiGift"/>); <c>null</c> — только броски вида, действие
    /// «подарок» без содержимого.</param>
    public static AiDiploDecision Evaluate(IRng rng, DiplomacyRules rules, Relation pair, int turn,
                                           int ownForts, int otherForts, int otherShare,
                                           int breakTurn = 10, int breakChance = 5,
                                           Func<AiGiftKind, AiTradeOffer?>? pickGift = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rules);
        var main = AiDiploAction.None;
        if (pair.Ally)
        {
            if (ShouldBreak(rng, pair, turn, ownForts, otherForts, breakTurn, breakChance))
                return new(AiDiploAction.Break, null);
        }
        else if (ShouldOffer(rng, rules, pair.Current, turn, otherShare))
            return new(AiDiploAction.Offer, null);
        else if (turn == 1)
            main = AiDiploAction.Hate;
        var gift = GiftRoll(rng, rules, pair.Current);
        var offer = gift is { } kind && pickGift is not null ? pickGift(kind) : null;
        bool giving = gift is not null && (pickGift is null || offer is not null);
        return new(main == AiDiploAction.None && giving ? AiDiploAction.Gift : main, gift, offer);
    }

    /// <summary>
    /// Проходы стратегии одного хода (<c>CAiStrategic::step 0x44e7e4</c>, сброс <c>0x44e777</c>; ранг 2 —
    /// run17 q09): на каждом проходе дипломатия оценивается для всех ещё не обслуженных целей в порядке id
    /// (броски тратятся всегда), а исполняется только на проходе без экономического действия — одно действие
    /// за проход, после чего цель выпадает до конца дня. Проходы идут, пока есть что исполнять.
    /// Корпус: предложение PL2→PL4 — <c>ret=True</c> шесть проходов (9191916…9191979), исполнено на седьмом (9191986).
    /// </summary>
    /// <param name="targets">Другие не-нейтральные игроки.</param>
    /// <param name="economicByPass">Было ли экономическое действие на проходе i (за концом списка — нет).</param>
    /// <param name="evaluate">Оценка по цели, обычно <see cref="Evaluate"/>.</param>
    /// <returns>Исполненные действия дипломатии с номером прохода (с 1).</returns>
    // ponytail: выбор экономического шага не переносится — флаги проходов даёт вызывающий (журнал); в партии проходы
    // ведёт AiStrategic.RunTurn. Из нескольких решений берётся первое по id — так и у движка (ранг 1): действия идут
    // в список в конец (0x44dd87 → 0x44df02) со счётом-константой 1000 (0x6cf580 у всех пяти действий 0x4486f5),
    // исполняется begin (0x43f962 → 0x44ea89), счёт выбор не решает. Ненависть и подарок одной цели у движка — два
    // действия, исполняется ненависть, подарок снимает память дня цели: одно действие равносильно. Харнесс —
    // AiDiplomacyHarnessTests.Execution_FirstAddedAction (158 из 158 исполнений).
    public static IReadOnlyList<AiDiploExecuted> RunPasses(IEnumerable<string> targets,
        IReadOnlyList<bool> economicByPass, Func<string, AiDiploDecision> evaluate)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(economicByPass);
        ArgumentNullException.ThrowIfNull(evaluate);
        var pending = targets.Order(StringComparer.Ordinal).ToList();
        var done = new List<AiDiploExecuted>();
        for (int pass = 1; ; pass++)
        {
            AiDiploExecuted? first = null;
            foreach (var target in pending)
            {
                var decision = evaluate(target);
                if (first is null && decision.Action != AiDiploAction.None)
                    first = new AiDiploExecuted(pass, target, decision);
            }
            bool economic = pass <= economicByPass.Count && economicByPass[pass - 1];
            if (economic) continue;
            if (first is null) return done;
            done.Add(first);
            pending.Remove(first.Target);
        }
    }
}

/// <summary>Категория сообщения ИИ — <c>LAiMsg</c> (<c>TAiMsg.MSG_CAT</c>); кто пишет — в корпусе <c>ai_chat</c>.</summary>
public enum AiMessage
{
    /// <summary>Предлагавший ИИ — тому, кто согласился на союз.</summary>
    PlayerAcceptsAlliance = 0,
    /// <summary>Предлагавший ИИ — тому, кто отказал.</summary>
    PlayerRefusedAlliance = 1,
    /// <summary>ИИ, разорвавший союз, — бывшему союзнику.</summary>
    AiBreakAlliance = 2,
    /// <summary>ИИ, с которым разорвали союз, — разорвавшему (реакция <c>0x474db7</c>, в корпусе <c>react_474d40</c>).</summary>
    PlayerBreakAlliance = 3,
    /// <summary>ИИ, предлагающий союз, — до команды предложения.</summary>
    AiOffersAlliance = 4,
    /// <summary>ИИ, получивший дань, — дарителю, если у пары нет вечной войны (реакция <c>0x474dd2</c>, корпус
    /// <c>react_474dd2</c>: 10 из 10).</summary>
    PlayerGiveGold = 5,
    AiGiveGold = 6,
    TradeAccepted = 7,
    TradeRefused = 8,
    /// <summary>Ненависть дня 1 (<c>CAiDiploHatePlayer</c> — только это сообщение).</summary>
    Hate = 9,
    NoGold = 10,
    /// <summary>То же при вечной войне пары (<c>0x474dd2</c>, ранг 2: в корпусе не встречалось).</summary>
    PlayerGiveGoldN = 11,
}

/// <summary>Действие дипломатии ИИ из <see cref="AiDiplomacy.Evaluate"/>: <c>CAiDiploBreakAlliance</c>,
/// <c>CAiDiploOfferAlliance</c>, <c>CAiDiploHatePlayer</c>, подарок (<c>0x448bda</c>).</summary>
public enum AiDiploAction
{
    None,
    Break,
    Offer,
    Hate,
    Gift,
}

/// <summary>Решение по цели. <paramref name="Gift"/> — выпавший вид подарка; при <see cref="AiDiploAction.Hate"/>
/// подарок идёт вторым действием. Выбранный вид ещё не значит, что дарить есть что: <paramref name="Offer"/> — что
/// выбрано (<see cref="AiGift"/>), <c>null</c> — нечего.</summary>
public readonly record struct AiDiploDecision(AiDiploAction Action, AiGiftKind? Gift, AiTradeOffer? Offer = null);

/// <summary>Исполненное действие дипломатии: проход (с 1), цель, решение.</summary>
public sealed record AiDiploExecuted(int Pass, string Target, AiDiploDecision Decision);
