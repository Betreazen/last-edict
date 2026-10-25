using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Net;

/// <summary>
/// Ход человека по сценарию — для проверок сети и драйвера двух машин (<c>tools/net-duo.py</c>): те же команды, что подают окна, со своим
/// зерном. Постройка (первая доступная), найм солдата в столице, до двух отрядов — нападение на видимого чужого рядом (бой «экраном»:
/// автобой по шагам на генераторе боя сессии, <c>endBattle</c> с действиями, <c>commitBattle</c>) или ход в случайную клетку по плану
/// (<c>planMove</c>, туман по шагам, <c>moveStack</c>), конец хода. <paramref name="run"/> исполняет команду: у хоста — сессия, у клиента —
/// она же, но через сеть.
/// </summary>
public static class ScriptedHuman
{
    /// <param name="battle">Бой экраном (игра): открыть бой отряда с целью и вернуть true — ход по сценарию встаёт до конца боя (зовут
    /// снова); null — бой «экраном» здесь же, автобоем по шагам.</param>
    /// <returns>Ход дошёл до конца (false — встал на бой экраном).</returns>
    /// <param name="hunt">Идти к видимому отряду другого человека (проверка боя людей), а не в случайную клетку.</param>
    public static bool Turn(GameSession session, string human, Func<GameCommand, CommandResult> run, Random random, bool endTurn = true,
                            Func<MapStack, MapStack, bool>? battle = null, bool hunt = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(random);
        var map = session.Map;
        var book = session.Book ?? throw new InvalidOperationException("партия без правил");
        bool Mine(string owner) => owner.Equals(human, StringComparison.OrdinalIgnoreCase);
        var capital = map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && Mine(s.Owner));
        foreach (var building in book.LordBuildings.GetValueOrDefault(map.Players[human].LordId) ?? [])
            if (run(new BuildCommand(human, building)).Value is BuildStatus.CanBeBuilt) break;
        if (capital is not null && book.Economy.Races.TryGetValue(map.Players[human].RaceId, out var race) && race.Soldiers is [var soldier, ..])
            run(new HireUnitCommand(capital.Id, soldier));
        foreach (string id in session.Map.Stacks.Values.Where(s => Mine(s.Owner) && s.Move > 0).Select(s => s.Id)
                                     .Order(StringComparer.Ordinal).Take(2).ToList())
        {
            if (session.Over || !session.Map.Stacks.TryGetValue(id, out var stack)) continue;
            var foe = Target(session, stack);
            if (hunt && foe is not null && !session.State.Info.Humans.Contains(foe.Owner, StringComparer.OrdinalIgnoreCase)) foe = null;   // охота — только на человека
            if (battle is not null && foe is not null)
            {
                if (battle(stack, foe)) return false;
                continue;
            }
            if (hunt ? foe is null || !Attack(session, stack, run) : !Attack(session, stack, run))
                Walk(session, stack, run, random, hunt ? Quarry(session, stack) : null);
        }
        if (endTurn && !session.Over) run(new EndTurnCommand());
        return true;
    }

    private static void Walk(GameSession session, MapStack stack, Func<GameCommand, CommandResult> run, Random random, (int X, int Y)? quarry = null)
    {
        var paths = (from dx in Enumerable.Range(-5, 11) from dy in Enumerable.Range(-5, 11)
                     select session.PathTo(stack, stack.X + dx, stack.Y + dy)).Where(p => p.Today.Count >= 2).ToList();
        if (paths.Count == 0) return;
        static int Distance(PathPoint p, (int X, int Y) q) => Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));
        var direct = quarry is { } target ? session.PathTo(stack, target.X, target.Y) : null;
        var path = direct is { Today.Count: >= 2 } ? direct
            : quarry is not { } aim ? paths[random.Next(paths.Count)]
            : paths.OrderBy(p => Distance(p.Today[^1], aim)).ThenBy(_ => random.Next()).First();
        var planned = (PlannedMove)run(new PlanMoveCommand(stack.Id, path.Today)).Value!;
        var today = path.Today.Take(planned.Walk).ToList();
        if (today.Count == 0) return;
        foreach (var p in today) run(new RevealFromCommand(stack.Id, p.X, p.Y));
        run(new MoveStackCommand(stack.Id, today[^1].X, today[^1].Y, 0, today[^1].Cost, planned.Fired, today));
    }

    /// <summary>Куда идти охоте: ближайший видимый отряд другого человека (вне крепостей), иначе ближайшая чужая столица (она есть в
    /// сценарии у каждого, хоть и под туманом).</summary>
    private static (int X, int Y)? Quarry(GameSession session, MapStack stack)
    {
        int Far(int x, int y) => Math.Max(Math.Abs(x - stack.X), Math.Abs(y - stack.Y));
        var human = session.Map.Stacks.Values.Where(s => !s.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase) && s.Inside.Length == 0
                                                         && session.State.Info.Humans.Contains(s.Owner, StringComparer.OrdinalIgnoreCase))
                           .OrderBy(s => Far(s.X, s.Y)).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        if (human is not null) return (human.X, human.Y);
        var capital = session.Map.Sites.Where(s => s.Kind == SiteKind.Capital && !s.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(s => Far(s.X, s.Y)).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        return capital is null ? null : (capital.X + 2, capital.Y + 2);
    }

    /// <summary>Видимый чужой отряд вплотную — бой; нет такого — false.</summary>
    private static bool Attack(GameSession session, MapStack stack, Func<GameCommand, CommandResult> run)
    {
        if (Target(session, stack) is not { } target) return false;
        var state = (BattleState)run(new StartBattleCommand(stack.Id, target.Id)).Value!;
        var rules = session.Book!.Rules;
        var battle = new BattleSession(state, new BattleSim(state, session.BattleRng, rules.Constants), rules.Constants) { Auto = true };
        battle.PlayOut();
        run(new EndBattleCommand(battle.Log, battle.Played()));
        run(new CommitBattleCommand());
        return true;
    }

    /// <summary>Видимый чужой отряд вплотную, если у отряда есть очки хода; нет — null.</summary>
    private static MapStack? Target(GameSession session, MapStack stack)
    {
        if (stack.Move <= 0) return null;
        var invisible = session.Looks.Stacks.Where(l => l.Invisible).Select(l => l.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return session.Map.Stacks.Values.Where(s => !s.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase) && s.Inside.Length == 0
                                                    && Math.Abs(s.X - stack.X) <= 1 && Math.Abs(s.Y - stack.Y) <= 1 && !invisible.Contains(s.Id))
                                        .OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Бой хода ИИ по запросу хоста — автобой «экраном» за сторону клиента и ответ хосту (клиент не собрал бой — автобой у хоста).</summary>
    public static void AnswerBattle(NetClient client, BattleRequest request)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (client.BuildRequestedBattle(request) is not { } battle || client.Session?.Book is not { } book)
        {
            client.ReplyBattle(request, null, []);
            return;
        }
        var rules = book.Rules;
        var play = new BattleSession(battle, new BattleSim(battle, client.Session.BattleRng, rules.Constants), rules.Constants,
                                     request.Side == "attacker") { Auto = true };
        play.PlayOut();
        client.ReplyBattle(request, play.Played(), play.Log);
    }
}
