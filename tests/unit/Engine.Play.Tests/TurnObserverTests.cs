using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Наблюдатель хода (<see cref="ITurnObserver"/>, <c>docs/ai-screen.md</c>) — точка расширения для показа хода ИИ:
/// с ним день идёт так же, как без него; ходы отрядов приходят до перестановки и непрерывны; бой, сыгранный наблюдателем
/// автобоем на генераторе партии, даёт тот же итог, что бой ядра.</summary>
public class TurnObserverTests(ITestOutputHelper output)
{
    private const string Scenario = "Power to Corrupt";
    private const int Days = 12;

    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static GameSession Session()
    {
        var looks = MapLooksLoader.Load(Path.Combine(GamePaths.Exports, Scenario + ".sg"));
        var content = SharedContent.Load(Data);
        return new GameSession(new GameState(GameInfo.FromMap(Scenario, looks), looks), content.MapArt,
                               GameBook.Load(Path.Combine(Data, "rules")));
    }

    [SkippableFact]
    public void Observer_SeesMovesAndBattles_DayUnchanged()
    {
        Skip.IfNot(File.Exists(Path.Combine(GamePaths.Exports, Scenario + ".sg")), "нет копии игры");
        var plain = Session();
        var watched = Session();
        var recorder = new Recorder(watched);
        watched.Observer = recorder;
        for (int day = 0; day < Days; day++)
        {
            var a = plain.EndTurn();
            var b = watched.EndTurn();
            recorder.Settle();
            Assert.Equal(a.Events, b.Events);
        }
        Assert.Equal(Positions(plain.Map), Positions(watched.Map));
        Assert.Equal(plain.Map.Players.Values.Select(p => p.Bank), watched.Map.Players.Values.Select(p => p.Bank));
        Assert.Equal(plain.Rng.Index, watched.Rng.Index);

        output.WriteLine($"дней {Days}: игроков-ходов {recorder.Turns}, ходов отрядов {recorder.Moves} (клеток {recorder.Cells}), " +
                         $"боёв {recorder.Battles}; без хода: {string.Join(", ", recorder.Sights.Select(p => $"{p.Key} {p.Value}"))}; " +
                         $"нарушений: старт {recorder.BadStart}, конец {recorder.BadEnd}, шаг {recorder.BadStep}");
        Assert.True(recorder.Moves > 0, "ходы отрядов есть");
        // Ход, кончающийся нападением, помечен (звук ATTK), и после такого хода — бой или вход (удар по объекту) тем же отрядом.
        Assert.True(recorder.AttackMoves > 0, "ходы с нападением помечены");
        // Начало каждого боя сообщается до самого боя (показ ABATTLE над целью).
        Assert.True(recorder.Sights.GetValueOrDefault(TurnSightKind.BattleStarted) >= recorder.Battles, "начало боя видно наблюдателю");
        output.WriteLine($"ходов с нападением {recorder.AttackMoves}");
        Assert.True(recorder.Battles > 0, "бои ИИ есть");
        Assert.True(recorder.Sights.GetValueOrDefault(TurnSightKind.Hire) > 0, "наймы героев видны наблюдателю");
        Assert.True(recorder.Sights.GetValueOrDefault(TurnSightKind.Death) > 0, "гибель отрядов в боях видна наблюдателю");
        Assert.Equal(0, recorder.BadStart);
        Assert.Equal(0, recorder.BadEnd);
        Assert.Equal(0, recorder.BadStep);
    }

    private static IEnumerable<string> Positions(MapState map) =>
        map.Stacks.Values.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => $"{s.Id} {s.X},{s.Y} {s.Inside} {s.Move}");

    /// <summary>Записывает ход, проверяет ходы и играет бои сам — автобоем на генераторе партии, как ядро.</summary>
    private sealed class Recorder(GameSession session) : ITurnObserver
    {
        private StackMove? _last;

        public int Turns, Moves, Cells, Battles, BadStart, BadEnd, BadStep, AttackMoves;
        public readonly Dictionary<TurnSightKind, int> Sights = [];

        public void Happened(TurnSight sight)
        {
            Settle();
            Sights[sight.Kind] = Sights.GetValueOrDefault(sight.Kind) + 1;
        }

        public void PlayerTurn(string playerId)
        {
            Settle();
            Turns++;
        }

        public void StackMoving(StackMove move)
        {
            Settle();
            Moves++;
            if (move.Attack) AttackMoves++;
            Cells += move.Cells.Count - 1;
            var stack = session.Map.Stacks[move.StackId];
            if (PathFinder.Start(session.Map, stack) != move.Cells[0]) BadStart++;
            for (int i = 1; i < move.Cells.Count; i++)
                if (Math.Max(Math.Abs(move.Cells[i].X - move.Cells[i - 1].X), Math.Abs(move.Cells[i].Y - move.Cells[i - 1].Y)) > 1
                    && move.Cells.Count > 2)
                    BadStep++;
            _last = move;
        }

        public IReadOnlyList<TurnRecord>? Battle(BattleState state)
        {
            Settle();
            Battles++;
            var rules = session.Book!.Rules;
            var battle = new BattleSession(state, new BattleSim(state, session.Rng, rules.Constants), rules.Constants,
                                           attackerIsPlayer: false) { Auto = true };
            battle.Resolve();
            return battle.Log;
        }

        /// <summary>Прошлый ход кончился в последней клетке (или отряд вошёл в укрепление, или его уже нет).</summary>
        public void Settle()
        {
            if (_last is not { } move) return;
            _last = null;
            if (!session.Map.Stacks.TryGetValue(move.StackId, out var stack) || stack.Inside.Length > 0) return;
            if ((stack.X, stack.Y) != move.Cells[^1]) BadEnd++;
        }
    }
}
