using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Очередь ходов раунда для полосы очереди (<see cref="BattleSession.Upcoming"/>, <see cref="BattleSession.QueueChanged"/>,
/// <c>docs/own-version-roadmap.md</c> §3.3): кто ходит сейчас и дальше, пометки «ходит», «пропустит», «ждёт», и событие при каждом изменении.
/// Главное — полоса не врёт: следующий ходящий — следующий непропускающий в прежней полосе.</summary>
public class BattleSessionQueueTests(ITestOutputHelper output)
{
    private static readonly AttackType Strike = new("g000aa0019", 90, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Middle = new("g000aa0022", 70, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Slow = new("g000aa0020", 50, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Late = new("g000aa0023", 30, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Give = new("g000aa0161", 60, AttackClass.GiveAttack, 100, 2, 0, 0, false, 1);

    private static BattleUnit Unit(string id, bool attacker, int slot, AttackType attack, int hp = 1000) => new()
    {
        Id = id, Attacker = attacker, Slot = slot, Hp = hp, HpMax = hp, Attack = attack,
        Type = new UnitType("g000uu0019", 1, hp, 0, attack.Id, "g000000000", false, true, 10, 100),
    };

    private static BattleState State(params BattleUnit[] units) => new() { Units = [.. units], AttackerGroup = "A", DefenderGroup = "D" };

    private static BattleSession Session(BattleState state, bool hold = false)
    {
        var rules = RulesLoader.Load(ImportedData.PathOf("rules"));
        return new BattleSession(state, new BattleSim(state, new EngineRng(), rules.Constants), rules.Constants) { HoldTurnStart = hold };
    }

    private static string Ids(IEnumerable<QueueEntry> entries) => string.Join(",", entries.Select(e => e.Unit.Id));

    /// <summary>Кто в этом шаге пропустил ход в его начале: «Пас»/«Защита» (паралич или окаменение держится) или снятие паралича и окаменения
    /// (ход занят и тогда, <c>BattleSim.BeginTurn</c>).</summary>
    private static List<string> SkippedAtStart(BattleSession session, int shows, int notes) =>
    [
        .. session.TurnShows.Skip(shows).Where(n => n.AtTurnStart).Select(n => n.UnitId),
        .. session.State.Notes.Skip(notes).Where(n => n.Kind is BattleNoteKind.Unparalyzed or BattleNoteKind.Unpetrified).Select(n => n.UnitId),
    ];

    private static string Marks(IEnumerable<QueueEntry> entries) =>
        string.Join(",", entries.Select(e => $"{e.Unit.Id}:{e.Mark switch { QueueMark.Current => "C", QueueMark.Skips => "S", QueueMark.Waits => "W", _ => "N" }}"));

    /// <summary>Открытие боя: первым — ходящий с пометкой «ходит», дальше по инициативе (90, 70, 50, 30 при броске до ±10 % — порядок не
    /// меняется), все — «сходит».</summary>
    [SkippableFact]
    public void Upcoming_StartsWithCurrent_InInitiativeOrder()
    {
        var session = Session(State(Unit("P", true, 0, Strike), Unit("E1", false, 0, Middle), Unit("E2", false, 1, Slow), Unit("Q", true, 1, Late)));
        Assert.Equal("P:C,E1:N,E2:N,Q:N", Marks(session.Upcoming));
        Assert.Same(session.Current, session.Upcoming[0].Unit);
    }

    /// <summary>«Ждать»: ждущий уходит в конец очереди раунда с пометкой «ждёт»; событие очереди зовётся.</summary>
    [SkippableFact]
    public void Wait_MovesUnitToEnd_MarkedWaiting()
    {
        var session = Session(State(Unit("P", true, 0, Strike), Unit("E1", false, 0, Middle), Unit("E2", false, 1, Slow)));
        int changes = 0;
        session.QueueChanged += () => changes++;
        Assert.Equal("P", session.Current?.Id);   // раунд 1 — ждать уже можно
        Assert.True(session.Wait());

        Assert.Equal(1, changes);
        Assert.Equal("P:W", Marks(session.Upcoming.TakeLast(1)));
        Assert.Equal(session.Current, session.Upcoming[0].Unit);
        // Ждущий сходит последним в раунде — и в полосе ходящим, а не «ждёт».
        while (session.Upcoming.Count > 1) session.AiStep();
        Assert.Equal("P:C", Marks(session.Upcoming));
    }

    /// <summary>Дар атаки (<c>GiveAttack</c>, <see cref="BattleSim.InsertExtraTurns"/>): союзник получает ход сразу за дарителем — в полосе он
    /// первым («ходит»), хвост прежней полосы не меняется; второй его ход — на своём прежнем месте.</summary>
    [SkippableFact]
    public void GiveAttack_InsertsExtraTurnRightAfterGiver()
    {
        var giver = Unit("G", true, 0, Give);
        var ally = Unit("A", true, 1, Late);
        var session = Session(State(giver, ally, Unit("E1", false, 0, Middle), Unit("E2", false, 1, Slow)));
        while (!ReferenceEquals(session.Current, giver)) session.AiStep();
        var before = session.Upcoming;
        Assert.Contains(before, e => ReferenceEquals(e.Unit, ally));
        int changes = 0;
        session.QueueChanged += () => changes++;

        session.Attack(ally);

        var after = session.Upcoming;
        output.WriteLine($"до {Marks(before)} после {Marks(after)}");
        Assert.True(changes >= 1);
        Assert.Equal("A:C", Marks(after.Take(1)));
        Assert.Equal(Ids(before.Skip(1)), Ids(after.Skip(1)));
        Assert.Equal(2, after.Count(e => ReferenceEquals(e.Unit, ally)));
    }

    /// <summary>Гибель: убитый исчезает из полосы сразу после удара, событие зовётся.</summary>
    [SkippableFact]
    public void Death_RemovesUnitFromStrip()
    {
        var player = Unit("P", true, 0, Strike);
        var victim = Unit("E1", false, 0, Middle, hp: 5);
        var session = Session(State(player, victim, Unit("E2", false, 1, Slow)));
        Assert.Equal("P:C,E1:N,E2:N", Marks(session.Upcoming));
        var seen = new List<string>();
        session.QueueChanged += () => seen.Add(Marks(session.Upcoming));

        session.Attack(victim);

        Assert.False(victim.Alive);
        Assert.Equal("E2:C", Marks(session.Upcoming));
        Assert.Equal(["E2:C"], seen);
    }

    /// <summary>Паралич и окаменение: стоят в очереди с пометкой «пропустит» и правда пропускают ход; короткие снимаются в этом же начале хода
    /// (ход занят и тогда) — в следующем раунде пометки «пропустит» нет.</summary>
    [SkippableFact]
    public void ParalyzedAndPetrified_MarkedSkipping_AndReallySkip()
    {
        var player = Unit("P", true, 0, Strike);
        var stunned = Unit("E1", false, 0, Middle);
        var stone = Unit("E2", false, 1, Slow);
        stunned.Paralyzed = true;
        stone.Petrified = true;
        var session = Session(State(player, stunned, stone, Unit("Q", true, 1, Late)));
        Assert.Equal("P:C,E1:S,E2:S,Q:N", Marks(session.Upcoming));

        session.Defend();

        Assert.Equal("Q:C", Marks(session.Upcoming));
        Assert.Equal(["E1", "E2"], SkippedAtStart(session, 0, 0).Order());
        session.Defend();   // Q — конец раунда 1
        Assert.Equal(2, session.Round);
        Assert.DoesNotContain(session.Upcoming, e => e.Mark == QueueMark.Skips);
    }

    /// <summary>Ход лекарей после победы: полоса — только лекарь победителя («ходит»), после лечения бой кончен — полоса пуста.</summary>
    [SkippableFact]
    public void Healers_AfterVictory_StripHasOnlyHealer()
    {
        var fighter = Unit("F", true, 0, Strike);
        fighter.Hp = 500;
        var healer = Unit("H", true, 1, new AttackType("g000aa0011", 10, AttackClass.Heal, 100, 2, 0, 20, false, 1));
        var enemy = Unit("E", false, 0, Slow, hp: 5);
        var session = Session(State(fighter, healer, enemy));
        var seen = new List<string>();
        session.QueueChanged += () => seen.Add(Marks(session.Upcoming));

        session.Attack(enemy);

        Assert.True(session.AfterBattle);
        Assert.Equal("H:C", Marks(session.Upcoming));
        Assert.Equal("H:C", seen[^1]);
        session.Attack(fighter);
        Assert.True(session.Finished);
        Assert.Empty(session.Upcoming);
        Assert.Equal("", seen[^1]);
    }

    /// <summary>Событие зовётся только при изменении: чтение команд и целей, переключение автобоя полосу не трогают.</summary>
    [SkippableFact]
    public void QueueChanged_NotRaised_WithoutChange()
    {
        var session = Session(State(Unit("P", true, 0, Strike), Unit("E", false, 0, Slow)));
        int changes = 0;
        session.QueueChanged += () => changes++;
        _ = session.Allowed;
        _ = session.Targets();
        _ = session.Upcoming;
        session.SetAuto(true, on: true);
        session.SetAuto(true, on: false);
        Assert.Equal(0, changes);
        Assert.True(session.Wait());
        Assert.Equal(1, changes);
    }

    /// <summary>Бой из восьми юнитов с ядами, параличом и окаменением на старте, доигранный ИИ, с удержанием начала хода и без. На каждом ходу:
    /// ходящий — первый в полосе; пока ход показывается (удержание), полоса — хвост прежней без павших; следующий ходивший — первый в хвосте
    /// прежней полосы, кто на поле и не пропустил ход, а все обойдённые до него с пометкой «пропустит» правда пропустили («Пас»/«Защита» в
    /// начале хода); новый раунд — только когда в прежней полосе сходить было некому. Подписчик события всегда видит то же, что модель.</summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Upcoming_PredictsWhoActsNext_ThroughWholeBattle(bool hold)
    {
        var units = new[]
        {
            Unit("A1", true, 0, Strike, 120), Unit("A2", true, 1, Middle, 140), Unit("A3", true, 2, Slow, 130), Unit("A4", true, 3, Late, 150),
            Unit("D1", false, 0, Strike, 130), Unit("D2", false, 1, Middle, 120), Unit("D3", false, 2, Slow, 150), Unit("D4", false, 3, Late, 140),
        };
        units[2].Paralyzed = true;
        units[2].DisableLong = true;   // длинный паралич — держится броском несколько раундов
        units[5].Petrified = true;
        units[6].Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 7, AppliedRound = 1, Long = true });
        units[1].Dots.Add(new DotEffect { Class = AttackClass.Frostbite, Damage = 5, AppliedRound = 1, Long = true });
        var session = Session(State(units), hold);
        session.Auto = true;
        string seen = Marks(session.Upcoming);
        session.QueueChanged += () => seen = Marks(session.Upcoming);

        int turns = 0, predicted = 0, skipped = 0, rounds = 0;
        for (int guard = 0; guard < 2000 && !session.Finished; guard++)
        {
            session.StartTurn();
            if (session.Finished) break;
            var before = session.Upcoming;
            Assert.Same(session.Current, before[0].Unit);
            Assert.Equal(QueueMark.Current, before[0].Mark);
            int round = session.Round, shows = session.TurnShows.Count, notes = session.State.Notes.Count;

            session.AiStep();
            turns++;
            Assert.Equal(seen, Marks(session.Upcoming));
            if (hold && !session.Finished)
                Assert.Equal(Ids(before.Skip(1).Where(e => e.Unit.OnField)), Ids(session.Upcoming));
            session.StartTurn();
            Assert.Equal(seen, Marks(session.Upcoming));
            if (session.Finished || session.Winner is not null) break;

            // Обход хвоста прежней полосы: павшие и ушедшие до своего черёда (удар, яд в начале хода) не ходят; пропустившие ход в его начале —
            // ровно те, у кого была пометка «пропустит» (парализующих атак в бою нет: новых пропусков посреди раунда не бывает).
            var startSkips = SkippedAtStart(session, shows, notes);
            var rest = before.Skip(1).ToList();
            int next = -1;
            for (int i = 0; i < rest.Count && next < 0; i++)
            {
                if (!rest[i].Unit.OnField) continue;
                bool passed = startSkips.Remove(rest[i].Unit.Id);
                Assert.True(passed == (rest[i].Mark == QueueMark.Skips), $"полоса {Marks(before)}: {rest[i].Unit.Id} пропустил={passed}");
                if (passed) skipped++;
                else next = i;
            }
            if (session.Round != round)
            {
                Assert.True(next < 0, $"раунд {round} кончился, а в полосе {Marks(before)} ещё ходил бы {(next < 0 ? "-" : rest[next].Unit.Id)}");
                rounds++;
                continue;
            }
            Assert.True(next >= 0, $"полоса {Marks(before)}: раунд {round} идёт, а ходить в ней некому");
            Assert.Same(rest[next].Unit, session.Current);
            predicted++;
        }
        output.WriteLine($"удержание={hold} ходов={turns} предсказано={predicted} пропусков={skipped} раундов={rounds} победитель={session.Winner ?? "-"}");
        Assert.True(session.Finished);
        Assert.True(predicted >= turns - rounds - 1);
        Assert.True(skipped >= 2);
        Assert.True(rounds >= 2);
    }
}
