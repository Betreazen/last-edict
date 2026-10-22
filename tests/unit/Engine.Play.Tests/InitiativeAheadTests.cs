using LastEdict.Data.Battle;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Инициатива на раунд вперёд (Р4, Р17): случайная часть раунда N+1 брошена в начале раунда N, порядок следующего раунда
/// (<see cref="BattleSession.UpcomingNext"/>) — по характеристикам на этот миг. Полоса на два раунда не врёт: следующий раунд, предсказанный
/// после последнего хода раунда, идёт ровно так. На своём содержимом (<c>content/test-pack</c>) — идёт в CI.</summary>
public class InitiativeAheadTests(ITestOutputHelper output)
{
    private static GameBook Book() => GameBook.Load(TestPackTests.Dir);

    /// <summary>Бои test-pack пар отрядов обеих карт автобоем по шагам: после последнего хода каждого раунда предсказание следующего
    /// раунда совпадает с его настоящей очередью — ровно, по местам.</summary>
    [Theory]
    [InlineData("twin-fields")]
    [InlineData("long-march")]
    public void NextRound_PredictedAfterLastTurn_IsTheRealQueue(string map)
    {
        var book = Book();
        var looks = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), map)).Looks;
        var stacks = looks.Map.Stacks.Values.Where(s => s.Inside.Length == 0 && s.UnitIds.Count > 0 && s.LeaderAlive)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        int rounds = 0, battles = 0;
        for (int i = 0; i + 1 < stacks.Count; i += 2)
        {
            var state = MapBattle.Build(looks.Map, stacks[i].Id, stacks[i + 1].Id, book, difficulty: 1, attackerMove: 0);
            var session = new BattleSession(state, new BattleSim(state, RngStreams.Derive((ulong)i, RngStream.Battle), book.Rules.Constants),
                                            book.Rules.Constants) { HoldTurnStart = true };
            battles++;
            for (int guard = 0; guard < 500 && !session.Finished; guard++)
            {
                if (session.TurnStartPending)
                {
                    int round = session.Round;
                    var predicted = Ids(session.UpcomingNext);
                    bool roundEnds = session.Upcoming.Count == 0;
                    session.StartTurn();
                    if (roundEnds && session.Round == round + 1 && !session.Finished && predicted.Length > 0)
                    {
                        Assert.Equal(predicted, Ids(session.Upcoming));
                        rounds++;
                    }
                    continue;
                }
                session.AiStep();
            }
            Assert.True(session.Finished, $"бой {stacks[i].Id} — {stacks[i + 1].Id} не кончился");
        }
        output.WriteLine($"{map}: боёв {battles}, раундов сверено {rounds}");
        Assert.True(rounds >= battles);
    }

    /// <summary>Замедление сразу двигает юнита в полосе следующего раунда — без бросков: случайная часть уже брошена и не меняется.</summary>
    [Fact]
    public void Slowdown_MovesUnitInNextRound_WithoutRolls()
    {
        var c = Book().Rules.Constants;
        var units = new[] { Unit("A", true, 0, 80), Unit("B", false, 0, 60), Unit("C", true, 1, 20) };
        var state = new BattleState { Units = [.. units], AttackerGroup = "G1", DefenderGroup = "G2" };
        var rng = new CountingRng(RngStreams.Derive(3, RngStream.Battle));
        var sim = new BattleSim(state, rng, c);
        Assert.Equal("A,B,C", string.Join(",", sim.BeginRound(1).Select(u => u.Id)));
        int calls = rng.Calls;
        Assert.Equal("A,B,C", string.Join(",", sim.NextRound().Select(u => u.Id)));

        units[0].LowerInitiativeLevel = 1;   // −40 % у test-pack: (80 + бросок) · 0,6 — меньше 60, больше 20 + бросок
        Assert.Equal("B,A,C", string.Join(",", sim.NextRound().Select(u => u.Id)));
        Assert.Equal(calls, rng.Calls);

        // Раунд 2 строится по тем же броскам и тем же характеристикам — так, как показывала полоса; конечное замедление гаснет.
        Assert.Equal("B,A,C", string.Join(",", sim.BeginRound(2).Select(u => u.Id)));
        Assert.Equal(0, units[0].LowerInitiativeLevel);
    }

    private static string Ids(IEnumerable<QueueEntry> entries) => string.Join(",", entries.Select(e => e.Unit.Id));

    private static BattleUnit Unit(string id, bool attacker, int slot, int initiative)
    {
        var attack = new AttackType("test:attack/" + id, initiative, AttackClass.Damage, 100, 2, 10, 0, false, 1);
        return new BattleUnit
        {
            Id = id, Attacker = attacker, Slot = slot, Hp = 100, HpMax = 100, Attack = attack,
            Type = new UnitType("test:unit/" + id, 1, 100, 0, attack.Id, "", false, true, 10, 100),
        };
    }
}
