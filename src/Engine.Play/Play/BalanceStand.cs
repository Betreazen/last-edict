using LastEdict.Data.Map;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Итог игрока в партии стенда (<see cref="BalanceStand"/>): строка JSONL — одна партия глазами одного игрока, как ждёт
/// <c>design_metrics.py</c> скилла game-design (<c>--rate won --by race</c>).</summary>
/// <param name="Decided">Партию кончил захват последней столицы (<see cref="GameSession.Over"/>), а не предел дней.</param>
/// <param name="First">Ходит первым в дне (<see cref="GameSession.DayOrder"/>) — для «преимущества первого хода».</param>
/// <param name="Won">Победа: в решённой партии — победа команды (<see cref="GameSession.Won"/>), иначе — наибольший счёт
/// (<see cref="BalanceStand.Score"/>; ничья — никто).</param>
/// <param name="LedEarly">Вёл по счёту на четверти партии (день <c>Days / 4</c>, не раньше первого) — для «снежного кома».</param>
public sealed record BalanceRow(string Scenario, uint Seed, int Days, bool Decided, string Player, string Race, string? Team, int Order,
                                bool First, bool Won, bool Alive, int Sites, int Score, long Gold, bool LedEarly);

/// <summary>
/// Стенд симуляции баланса (Э4, <c>docs/own-version-roadmap.md</c> §3.2, Т7): партия без людей — все игроки ИИ — с зерном партии
/// (<see cref="RandomReseed.Reseeded"/>, все потоки генераторов от него) до победы или предела дней. Без Godot и без графики, поэтому
/// партии идут параллельно по зёрнам (<c>tools/BalanceStand</c>). Счёт игрока (<see cref="Score"/>) — объекты и сила армий: по нему
/// судится нерешённая партия и «кто ведёт».
/// </summary>
public static class BalanceStand
{
    /// <summary>Очков за город или столицу игрока.</summary>
    public const int SitePoints = 10;

    /// <summary>Сыграть партию: сценарий <paramref name="scenario"/>, правила <paramref name="book"/>, зерно <paramref name="seed"/>, не больше
    /// <paramref name="maxDays"/> дней. Люди сценария — тоже ИИ.</summary>
    public static IReadOnlyList<BalanceRow> Play(ScenarioData scenario, GameBook book, uint seed, int maxDays)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(book);
        var map = scenario.Looks.Map;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value with { IsHuman = false }, StringComparer.OrdinalIgnoreCase);
        var looks = GameSession.Retold(scenario.Looks, GameSession.With(map, players: players), scenario.Looks.Stacks);
        var state = new GameState(GameInfo.FromMap(scenario.Id, looks), looks)
        {
            Events = scenario.Events, EventProgress = EventProgress.From(scenario.Events),
        }.Reseeded(seed);
        var session = new GameSession(state, MapArtData.Empty, book);
        var playable = Playable(session.Map, book);
        var order = GameSession.DayOrder(session.Map, [], book).Where(playable.Contains).ToList();
        int early = Math.Max(1, maxDays / 4), days = 0;
        IReadOnlySet<string> leaders = new HashSet<string>();
        while (days < maxDays && !session.Over)
        {
            session.Execute(new EndTurnCommand());
            days++;
            if (days == early) leaders = Leaders(session.Map, book, playable);
        }
        var end = session.Map;
        var top = session.Over ? new HashSet<string>() : Leaders(end, book, playable);
        return [.. order.Select(id =>
        {
            var p = end.Players[id];
            return new BalanceRow(scenario.Id, seed, days, session.Over, id, p.RaceId, p.Team, order.IndexOf(id), order.Count > 0 && order[0] == id,
                                  session.Over ? session.Won(id) == true : top.Contains(id), p.CapturedBy.Length == 0,
                                  end.Sites.Count(s => Owns(s, id)), Score(end, id), p.Bank.Gold, leaders.Contains(id));
        })];
    }

    /// <summary>Счёт игрока: <see cref="SitePoints"/> за каждый его город и столицу (не взятую) плюс сумма уровней его юнитов в отрядах на
    /// карте — земля и армия, без казны.</summary>
    public static int Score(MapState map, string playerId)
    {
        ArgumentNullException.ThrowIfNull(map);
        int sites = map.Sites.Count(s => Owns(s, playerId));
        int army = map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                      .SelectMany(s => s.UnitIds).Sum(u => map.Units.TryGetValue(u, out var unit) && unit.Hp > 0 ? unit.Level : 0);
        return SitePoints * sites + army;
    }

    private static bool Owns(MapSite site, string playerId) =>
        site.Kind is SiteKind.Capital or SiteKind.Village && site.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Playable(MapState map, GameBook book) =>
        map.Players.Values.Where(p => book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Playable).Select(p => p.Id)
           .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Игроки с наибольшим счётом среди живых; ничья — никто (у снежного кома лидер один).</summary>
    private static HashSet<string> Leaders(MapState map, GameBook book, IReadOnlySet<string> playable)
    {
        var scores = playable.Where(id => map.Players[id].CapturedBy.Length == 0).Select(id => (Id: id, Score: Score(map, id))).ToList();
        if (scores.Count == 0) return [];
        int best = scores.Max(s => s.Score);
        var top = scores.Where(s => s.Score == best).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return top.Count == 1 ? top : [];
    }
}
