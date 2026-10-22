using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Проверка дипломатии ИИ по одной цели — входы и шансы в тех же величинах, что пишет зонд
/// (<c>ai_offer_check</c>, <c>ai_break_check</c>, <c>ai_gift_check</c>). <see cref="Draws"/> — шагов R250 за оценку.</summary>
public sealed record AiDiploCheck(string Player, string Target, int Turn, int Relation, bool Ally, int OwnForts,
                                  int OtherForts, int Share, int? OfferChance, int? BreakChance, int? GiftThreshold,
                                  AiDiploDecision Decision, int Draws);

// Дипломатия партии: решения ИИ (список 8 стратегии, AiEconomy.Diplomacy), их исполнение и ответ человека на
// предложение союза. Отношения меняет мутатор Diplomacy (сверен в Ф7); броски — общий R250 партии.
public sealed partial class GameSession
{
    /// <summary>Журнал проверок дипломатии ИИ (<see cref="AiDiploCheck"/>); <c>null</c> — не писать. Для харнессов и
    /// отладки: по нему сверяются входы решений с журналом зонда.</summary>
    public List<AiDiploCheck>? DiplomacyChecks { get; set; }

    /// <summary>Игроки, с которыми ИИ ведёт дипломатию (<c>0x44857e</c>): все, кроме него самого, нейтралов и пустых
    /// (<c>0x5e5358</c>), в порядке id. Игрок, чья столица взята (<see cref="MapPlayer.CapturedBy"/>), выпадает: в партии
    /// human после взятия столицы <c>S073PL000c</c> в день 61 его нет в списках с дня 62 (18 проверок из 18).</summary>
    internal IReadOnlyList<string> DiplomacyTargets(string playerId) =>
        [.. Map.Players.Values.Where(p => !p.Id.Equals(playerId, StringComparison.OrdinalIgnoreCase) && p.CapturedBy.Length == 0
                                          && PartyOf(p.Id) is not null)
               .Select(p => p.Id).Order(StringComparer.Ordinal)];

    /// <summary>Решение ИИ по цели (<see cref="AiDiplomacy.Evaluate"/>) на R250 партии. Отношение — пара рас
    /// (<c>0x5f788f</c>, записи нет — 0); «сильнее» — больше укреплений (<c>0x4489f6</c>); доля равнины — цели.</summary>
    internal AiDiploDecision DiplomacyDecision(string playerId, string targetId)
    {
        var book = Book!;
        var map = Map;
        int own = PartyOf(playerId)!.Value, other = PartyOf(targetId)!.Value;
        var pair = map.Relations.FirstOrDefault(r => r.Pairs(own, other));
        int ownForts = FortCount(map, playerId), otherForts = FortCount(map, targetId);
        int share = AiDiplomacy.PlainLandShare(map, other, book.Economy);
        var (breakTurn, breakChance) = book.AllianceBreak;
        int steps = AiRng.Steps;
        var decision = AiDiplomacy.Evaluate(AiRng, book.Diplomacy, pair, map.Turn, ownForts, otherForts, share, breakTurn, breakChance,
                                            kind => PickGift(playerId, targetId, kind, pair.Current));
        DiplomacyChecks?.Add(new AiDiploCheck(
            playerId, targetId, map.Turn, pair.Current, pair.Ally, ownForts, otherForts, share,
            pair.Ally ? null : AiDiplomacy.OfferChance(book.Diplomacy, pair.Current, map.Turn, share),
            pair.Ally ? AiDiplomacy.BreakChance(pair, map.Turn, ownForts, otherForts, breakTurn, breakChance) : null,
            pair.Current > book.Diplomacy.Neutral && decision.Action is not (AiDiploAction.Break or AiDiploAction.Offer)
                ? pair.Current * 20 / 100 : null,
            decision, AiRng.Steps - steps));
        return decision;
    }

    /// <summary>
    /// Исполнение действия дипломатии ИИ (<c>vft[3]</c> действий <c>CAiDiplo*</c>, <c>J-map-ai-notes.md</c>), порядок бросков —
    /// по корпусу (<c>ai_exec</c> → <c>ai_chat</c> → ответ цели):
    /// ненависть — только сообщение; разрыв — <see cref="Diplomacy.BreakAlliance"/>, сообщение разорвавшего, затем реакция
    /// цели-ИИ; предложение — сообщение, команда предложения, затем ответ цели: ИИ отвечает сразу
    /// (<see cref="AiDiplomacy.AnswerAlliance"/>, <c>0x474b37</c>), человеку предложение ждёт в <see cref="GameState.Offers"/>.
    /// </summary>
    internal bool ExecuteDiplomacy(string playerId, string targetId, AiDiploAction action, List<string>? report)
    {
        switch (action)
        {
            case AiDiploAction.Hate:
                Chat(playerId, targetId, AiMessage.Hate, report);
                return true;
            case AiDiploAction.Break:
                if (!BreakAlliance(playerId, targetId)) return false;
                report?.Add($"{playerId} разрывает союз с {targetId}");
                Chat(playerId, targetId, AiMessage.AiBreakAlliance, report);
                if (!IsHuman(targetId)) Chat(targetId, playerId, AiMessage.PlayerBreakAlliance, report);
                return true;
            case AiDiploAction.Offer:
                Chat(playerId, targetId, AiMessage.AiOffersAlliance, report);
                report?.Add($"{playerId} предлагает союз {targetId}");
                if (IsHuman(targetId))
                {
                    OfferToHuman(new AllianceOffer(playerId, targetId, Map.Turn));
                    return true;
                }
                int rel = Diplomacy.Current(Map.Relations, PartyOf(playerId)!.Value, PartyOf(targetId)!.Value);
                bool accept = AiDiplomacy.AnswerAlliance(AiRng, rel, AiDiplomacy.PlainLandShare(Map, PartyOf(playerId)!.Value, Book!.Economy));
                SettleAlliance(playerId, targetId, accept, report);
                return true;
            case AiDiploAction.Gift:
                return TakeGift(playerId, targetId) is { } gift && ExecuteGift(playerId, targetId, gift, report);
            default:
                return false;
        }
    }

    /// <summary>Ответ человека на предложение союза от ИИ (<c>AcceptAlliance 0x41eceb</c>): согласие — союз
    /// (<see cref="Diplomacy.MakeAlliance"/>), отказ — <c>D_REF_ALLY</c>; предлагавший ИИ отвечает сообщением. Вернёт
    /// причину отказа или <c>null</c>.</summary>
    public string? AnswerAllianceOffer(string fromAi, string humanId, bool accept, List<string>? report = null)
    {
        var offer = State.Offers.FirstOrDefault(o => o.IsAlliance && o.From.Equals(fromAi, StringComparison.OrdinalIgnoreCase)
                                                    && o.To.Equals(humanId, StringComparison.OrdinalIgnoreCase));
        if (offer is null) return $"предложения союза от {fromAi} игроку {humanId} нет";
        State = State with { Offers = [.. State.Offers.Where(o => o != offer)] };
        // ponytail: что движок делает с предложением игрока, потерявшего столицу, не проверено — здесь оно снимается.
        if (!DiplomacyTargets(humanId).Contains(offer.From, StringComparer.OrdinalIgnoreCase) || PartyOf(humanId) is null)
            return $"{offer.From} больше не ведёт дипломатию (столица взята) — предложение снято";
        SettleAlliance(fromAi, humanId, accept, report);
        return null;
    }

    /// <summary>Итог предложения: союз или отказ, затем сообщение предлагавшего (корпус: <c>alliance_change</c> →
    /// <c>ai_chat</c> <c>PLAYER_ACCEPTS_ALL</c>/<c>PLAYER_REFUSED_ALL</c>). Стороны мутатора — по строкам <c>relation</c>
    /// (5 союзов, 1 отказ): у союза <c>a</c> — предлагавший, у отказа — отказавший; от этого зависит третья сторона.</summary>
    private void SettleAlliance(string proposer, string target, bool accept, List<string>? report)
    {
        var book = Book!;
        var map = Map;
        int p = PartyOf(proposer)!.Value, t = PartyOf(target)!.Value;
        var races = Diplomacy.PlayerParties(map, book.Economy);
        bool bothHuman = IsHuman(proposer) && IsHuman(target);
        var change = accept
            ? Diplomacy.MakeAlliance(map.Relations, races, book.Diplomacy, p, t, map.Turn, bothHuman)
            : Diplomacy.RefuseAlliance(map.Relations, races, book.Diplomacy, t, p, bothHuman);
        var players = accept ? SpyEachOther(map.Players, proposer, target, spied: true) : null;
        State = State with { Looks = Retold(Looks, With(map, players: players, relations: change.Relations), Looks.Stacks) };
        report?.Add($"{target} {(accept ? "принимает" : "отклоняет")} союз с {proposer}");
        if (!IsHuman(proposer))
            Chat(proposer, target, accept ? AiMessage.PlayerAcceptsAlliance : AiMessage.PlayerRefusedAlliance, report);
    }

    private bool BreakAlliance(string playerId, string targetId)
    {
        var book = Book!;
        var map = Map;
        int a = PartyOf(playerId)!.Value, b = PartyOf(targetId)!.Value;
        if (!Diplomacy.AreAllies(map.Relations, a, b)) return false;
        var player = map.Players[playerId];
        var broken = Diplomacy.BreakAlliance(map.Relations, Diplomacy.PlayerParties(map, book.Economy), book.Diplomacy, a, b,
                                             map.Turn, player.QtyBreaks);
        var players = SpyEachOther(map.Players, playerId, targetId, spied: false);
        players[playerId] = players[playerId] with { QtyBreaks = broken.QtyBreaks };
        State = State with { Looks = Retold(Looks, With(map, players: players, relations: broken.Change.Relations), Looks.Stacks) };
        return true;
    }

    /// <summary>
    /// Взаимная разведка при смене союза (<c>CEffectChangeAlliance</c>): союз (<c>0x5d91af</c>) — обоим «разведан» (<c>0x5d7dbc</c> в обе
    /// стороны: нет записи — <c>CVisitorPlayerAddSpy</c>), разрыв (<c>0x5d91fd</c>) — запись снимается у обоих (<c>CVisitorPlayerDelSpy</c>,
    /// <c>0x5e6aae</c> в обе стороны, <c>0x5d9264</c>/<c>0x5d926e</c>). Журнал 20261019b-port-synth, карта 22, день 10: Кланы рвут союз с
    /// Нежитью — в начале хода Нежити один бросок раскрытия шпиона (<c>spy_discover</c>, за Легионов), а вор Нежити против отряда Кланов
    /// видит шпионаж (<c>noble_choose</c> [0 1 5]); порт до 19.10 держал Кланов разведанными после разрыва, и вор шёл к отряду человека.
    /// </summary>
    private static Dictionary<string, MapPlayer> SpyEachOther(IReadOnlyDictionary<string, MapPlayer> current, string a, string b, bool spied)
    {
        var players = current.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        void Set(string who, string whom)
        {
            if (!players.TryGetValue(who, out var p)) return;
            bool has = p.Spied.Contains(whom, StringComparer.OrdinalIgnoreCase);
            if (spied && !has) players[who] = p with { Spied = [.. p.Spied, whom] };
            else if (!spied && has) players[who] = p with { Spied = [.. p.Spied.Where(x => !x.Equals(whom, StringComparison.OrdinalIgnoreCase))] };
        }
        Set(a, b);
        Set(b, a);
        return players;
    }

    /// <summary>Отношения за нападение (<see cref="Diplomacy.Attack"/>, обёртка <c>0x5d96fa</c>) — в начале боя, до первого
    /// раунда (в журнале <c>relation</c> с причиной <c>attack</c> стоит прямо перед <c>battle_start</c>). Цель — отряд или
    /// укрепление; у руины владельца нет, а бой с нейтралами отношений не двигает. Нападение нейтралов на игрока мутатор
    /// зовёт (строки <c>relation</c> с расой 4: 8, 18 и 28 на столько же боёв в трёх партиях), поэтому раса нападающего
    /// берётся любая.</summary>
    private void AttackRelations(string attackerOwner, string targetId)
    {
        var book = Book!;
        var map = Map;
        var (owner, kind) = map.Stacks.TryGetValue(targetId, out var stack)
            ? (stack.Owner, BattleTarget.Stack)
            : map.Sites.FirstOrDefault(s => s.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase)) is { Kind: SiteKind.Capital or SiteKind.Village } fort
                ? (fort.Owner, BattleTarget.Fortification)
                : ("", BattleTarget.Other);
        if (PartyOf(owner) is not { } defender
            || !map.Players.TryGetValue(attackerOwner, out var attacker)
            || !book.Economy.Races.TryGetValue(attacker.RaceId, out var race)) return;
        var change = Diplomacy.Attack(map.Relations, Diplomacy.PlayerParties(map, book.Economy), book.Diplomacy, attacker.Party ?? race.RaceType,
                                      defender, kind, targetPlayable: true);
        State = State with { Looks = Retold(Looks, With(map, relations: change.Relations), Looks.Stacks) };
    }

    /// <summary>Сообщение ИИ (<see cref="AiDiplomacy.Chat"/>): бросок — по числу строк <c>TAiMsg</c> расы отправителя. Человеку —
    /// окно <c>DLG_CHAT_AI</c> (<see cref="TurnSightKind.AiChat"/>, <c>CCmdChatMsg</c> → <c>0x4b9747</c>).</summary>
    private void Chat(string from, string to, AiMessage category, List<string>? report)
    {
        var messages = Book!.AiMessages(Map.Players[from].RaceId, category);
        int n = AiDiplomacy.Chat(AiRng, messages.Count);
        if (messages.Count == 0) return;
        report?.Add($"{from} → {to}: {messages[n]}");
        if (IsHuman(to)) Observer?.Happened(new TurnSight(TurnSightKind.AiChat, to, -1, -1, from) { Text = messages[n] });
    }

    private bool IsHuman(string playerId) => State.Info.Humans.Contains(playerId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Сторона дипломатии игрока играбельной расы (<see cref="Diplomacy.PartyOf(MapPlayer, IReadOnlyDictionary{string, RaceInfo})"/>:
    /// у движка — <c>Grace.RACE_TYPE</c>); нейтралы и неизвестная раса — <c>null</c>.</summary>
    private int? PartyOf(string playerId) =>
        Map.Players.TryGetValue(playerId, out var p) && Book!.Economy.Races.TryGetValue(p.RaceId, out var race) && race.Playable
            ? Diplomacy.PartyOf(p, Book.Economy.Races) : null;

    /// <summary>Текст отказа щелчка по союзнику: «Перед тем, как атаковать союзника, расторгните альянс с ним.» (<c>Tglobal</c>).</summary>
    public const string AllyRefusalText = "X005TA0303";

    /// <summary>
    /// Щелчок выбранным отрядом <paramref name="stack"/>, нацеленный в клетку <paramref name="aim"/> (<see cref="ClickAim"/>), упирается в
    /// союзника — у движка путь не строится, а всплывает <see cref="AllyRefusalText"/>. Показ пути по щелчку <c>0x4d1590</c> (после
    /// неподтверждённого <c>0x4cc8c2</c>) по типу объекта прицела зовёт <c>0x4d0ca4(…, показать 1)</c>: видимый чужой отряд (<c>0x4d1808</c>:
    /// невидимый — как пустая клетка), чужая крепость (<c>0x4d1886</c>) и чужой жезл (<c>0x4d1683</c>) — хозяин союзник игрока
    /// (<c>0x5f7f5a</c> → <c>0x5f7ebd</c>) → окно <c>0x4d0da1</c> и выход без пути (<c>0x4d1945</c>); показанный прежде путь не меняется.
    /// Курсор над ними — <c>ALLIANCE</c> (<c>0x4d0aa9</c> → <c>0x4d0c12</c>, <see cref="MapCursors"/>). Журнал <c>20261018-port-synth</c>, карта 13,
    /// AL: щелчок по пустой (13,67) над Эльфами (14,68) — seq 814794 <c>ret 0</c>, окно 814829, пути нет; по самим Эльфам — 816021/816065;
    /// день 2 — 908343/908377. Порт до 18.10 прокладывал путь к союзнику и окна не показывал (слова пользователя).
    /// </summary>
    public bool AllyRefuses(MapStack stack, (int X, int Y) aim)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (Book is null || !Map.Plan.TryGetValue(aim, out var ids)) return false;
        string? owner = null;
        foreach (string id in ids)
        {
            // Отряд в крепости не в счёт — решает хозяин крепости; невидимый — как пустая клетка (0x4d180f → 0x4d192f).
            if (Map.Stacks.TryGetValue(id, out var other))
            {
                if (other.Inside.Length > 0 || Invisible(other.Id)) continue;
                owner = other.Owner;
                break;
            }
            if (Map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { Kind: SiteKind.Capital or SiteKind.Village } fort)
                owner ??= fort.Owner;
            else if (Map.Rods.TryGetValue(id, out var rod))
                owner ??= rod.Owner;
        }
        return owner is not null && !owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase) && AiTactics.Allied(this, stack.Owner, owner);
    }

    /// <summary>Укрепления игрока — столица и деревни (<c>CFortification</c>, владелец <c>+0x84</c>).</summary>
    private static int FortCount(MapState map, string playerId) =>
        map.Sites.Count(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
}
