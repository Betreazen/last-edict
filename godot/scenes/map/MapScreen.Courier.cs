using System;
using System.Linq;
using D2.Data.Play;
using D2.Data.Ui;
using D2.Data.Ui.Windows;
using Godot;

namespace D2.Godot;

/// <summary>Предложения человека другому человеку «горячего стула» — сразу, ещё в ход предложившего (<see cref="HotseatCourierLogic"/>,
/// сессия Б): гонец, «ОК» — окно ответа адресата поверх карты предложившего, ответ — итог предложившему. Отложенные предложения
/// (сохранение прежнего порта) по-прежнему ждут хода адресата (<see cref="ShowOffers"/>).</summary>
public partial class MapScreen
{
    /// <summary>Есть предложение зрителя другому человеку и его гонец ещё не открыт — открыть (после смены окон).</summary>
    private void CourierOffers()
    {
        var session = _map.Session;
        if (!session.Hotseat || _leaving || _map.AiRunning || _battle is not null || _dialogsStale || Windows is not { } context
            || _map.Viewer is not { } viewer) return;
        if (_logics.Any(l => l is HotseatCourierLogic or AllianceOfferLogic or TradeOfferLogic)) return;
        var offer = session.State.Offers.FirstOrDefault(o => o.From.Equals(viewer, StringComparison.OrdinalIgnoreCase)
            && session.State.Info.Humans.Contains(o.To, StringComparer.OrdinalIgnoreCase)
            && (o.IsAlliance || o.Exchange || o.Trade is { Kind: not D2.Rules.Map.AiGiftKind.Gold }));
        if (offer is null || !session.Map.Players.TryGetValue(offer.To, out var to) || !session.Map.Players.TryGetValue(offer.From, out var from)) return;
        var raceNames = _raceNames ??= RaceNames.Load(GameData.Rules);
        UiOpen? answer = offer.Trade is null
            ? new UiOpen(AllianceOfferLogic.Screen, new AllianceOfferLogic(context, offer, raceNames))
            : TradeOffer.Of(offer, _map.Map) is { } trade ? new UiOpen(TradeOfferLogic.Screen, CourierTrade(context, trade, raceNames)) : null;
        if (answer is null) return;
        var popup = new HotseatTurnLogic(to, GameData.Catalog, Text, HotseatCall.Message, sender: from);
        GD.Print($"courier from={from.Id} to={to.Id} ответ={answer.Screen}");
        OpenWindow(HotseatCourierLogic.Screen, new HotseatCourierLogic(popup, answer));
    }

    /// <summary>Сделка человека человеку: ответ адресата (<see cref="GameSession.AnswerTradeOffer"/>), звук ответа предложившему (сессия Г),
    /// итог предложившему — <c>X005TA0302</c> и принято/отвергнуто (<see cref="TradePriceLogic.Result"/>).</summary>
    private TradeOfferLogic CourierTrade(WindowContext context, TradeOffer trade, System.Collections.Generic.IReadOnlyDictionary<string, string> raceNames) =>
        new(context, trade, raceNames, TradeThingName(trade.IsItem), yes =>
        {
            string? refused = (string?)_map.Session.Execute(new D2.Data.Play.Commands.AnswerTradeCommand(trade.From, trade.To, yes)).Value;
            if (refused is not null && yes) _map.Session.Execute(new D2.Data.Play.Commands.AnswerTradeCommand(trade.From, trade.To, false));
            bool done = yes && refused is null;
            context.Announce(done ? TurnSightKind.Traded : TurnSightKind.TradeRefused, trade.ThingId);
            string addressee = _map.Map.Players.TryGetValue(trade.To, out var p) ? p.Name : trade.To;
            GD.Print($"courier trade {trade.From}→{trade.To} принято={(done ? 1 : 0)}");
            Callable.From(() => OpenWindow(MessageBoxLogic.Screen, MessageBoxLogic.For(new UiTell(TradePriceLogic.Result(context, addressee, done))))).CallDeferred();
            _map.Reload();
            SyncPanels();
        });
}
