using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Рискованное в отложенном начале хода (<see cref="BattleSession.HoldTurnStart"/>, экран боя): конец боя ударом и тиком, ходы лекарей,
/// быстрый бой и автобой, «Ждать» и «Отступить» между ходами, тики разных видов — всё как без удержания, только начало следующего хода — по
/// <see cref="BattleSession.StartTurn"/>.</summary>
public class BattleSessionHoldTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly AttackType Strike = new("g000aa0019", 90, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Slow = new("g000aa0020", 50, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Middle = new("g000aa0022", 70, AttackClass.Damage, 100, 2, 10, 0, false, 1);
    private static readonly AttackType Heal = new("g000aa0021", 40, AttackClass.Heal, 100, 2, 0, 50, false, 1);

    private static BattleUnit Unit(string id, bool attacker, int slot, AttackType attack, bool twice = false) => new()
    {
        Id = id, Attacker = attacker, Slot = slot, Hp = 1000, HpMax = 1000, Attack = attack,
        Type = new UnitType("g000uu0019", 1, 1000, 0, attack.Id, "g000000000", twice, true, 10, 100),
    };

    private static BattleSession Session(BattleState state, bool hold)
    {
        var rules = RulesLoader.Load(ImportedData.PathOf("rules"));
        return new BattleSession(state, new BattleSim(state, new EngineRng(), rules.Constants), rules.Constants) { HoldTurnStart = hold };
    }

    private static BattleState State(params BattleUnit[] units) => new() { Units = [.. units], AttackerGroup = "A", DefenderGroup = "D" };

    /// <summary>Удар, добивший последнего врага, при удержании: ход кончился, бой ещё не кончен (<see cref="BattleSession.Finished"/> ложь,
    /// победитель уже есть); <see cref="BattleSession.StartTurn"/> — конец боя (лекарей нет) или ход лекаря победителя (<c>0x626d49</c>).
    /// Без удержания — то же сразу.</summary>
    [SkippableTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void LastEnemyKilled_EndsOrHealersAfterStartTurn(bool hold, bool healer)
    {
        var player = Unit("P", attacker: true, slot: 0, Strike);
        player.Hp = 500;   // ранен — лекарю есть цель
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        enemy.Hp = 5;
        var medic = Unit("H", attacker: true, slot: 1, Heal);
        var state = healer ? State(player, enemy, medic) : State(player, enemy);
        var session = Session(state, hold);
        Assert.Same(player, session.Current);   // инициатива 90 против 50 и 40 — человек первым

        session.Attack(enemy);
        Assert.False(enemy.Alive);
        Assert.Equal("A", session.Winner);
        if (hold)
        {
            Assert.True(session.TurnStartPending);
            Assert.False(session.Finished);
            Assert.Null(session.Current);
            session.StartTurn();
        }
        Assert.False(session.TurnStartPending);
        if (healer)
        {
            Assert.Same(medic, session.Current);
            Assert.True(session.AfterBattle);
            Assert.False(session.Finished);
            Assert.Contains(BattleCommand.Attack, session.Allowed);   // ход лекаря — только лечение (и «Автобой», «Быстрый бой»)
            Assert.DoesNotContain(BattleCommand.Defend, session.Allowed);
            session.Attack(player);
            Assert.Equal(550, player.Hp);
            session.StartTurn();
        }
        Assert.True(session.Finished);
        Assert.Null(session.Current);
        Assert.Empty(session.Allowed);
    }

    /// <summary>Бой из четырёх юнитов с ядом, морозом, ожогом и двойной атакой — новое состояние на каждый вызов.</summary>
    private static BattleState Skirmish()
    {
        var units = new[]
        {
            Unit("P1", attacker: true, slot: 0, Strike), Unit("P2", attacker: true, slot: 1, Slow, twice: true),
            Unit("E1", attacker: false, slot: 0, Slow), Unit("E2", attacker: false, slot: 1, Middle),
        };
        foreach (var unit in units) unit.Hp = 60;
        units[2].Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 3, AppliedRound = 1, Long = true });
        units[3].Dots.Add(new DotEffect { Class = AttackClass.Frostbite, Damage = 4, AppliedRound = 1, Long = true });
        units[1].Dots.Add(new DotEffect { Class = AttackClass.Blister, Damage = 2, AppliedRound = 1, Long = true });
        return State(units);
    }

    private static string Trace(BattleSession s) =>
        string.Join(";", s.Log.Select(r => $"{r.Round}:{r.Unit}>{r.Target}={r.TargetHpAfter}"))
        + "|" + string.Join(";", s.Sim.DotLog.Select(t => $"{t.Round}:{t.Unit}:{t.Class}={t.HpAfter}"))
        + $"|раунд={s.Round}|победитель={s.Winner ?? "-"}|конец={s.Finished}";

    /// <summary>Быстрый бой, автобой и доигрывание с удержанием — тот же бой, что без него (удержание сдвигает только миг начала хода), и
    /// отложенный ход на ходу человека не тратит лишний проход: тот же итог и при малом потолке действий.</summary>
    [SkippableTheory]
    [InlineData("resolve", BattleSession.MaxActions)]
    [InlineData("auto", BattleSession.MaxActions)]
    [InlineData("playout", 5)]
    [InlineData("playout", 9)]
    public void HeldSession_PlaysOutLikeUnheld(string how, int maxActions)
    {
        string Play(bool hold)
        {
            var session = Session(Skirmish(), hold);
            Assert.Equal("P1", session.Current?.Id);   // инициатива 90 — первым человек
            Assert.True(session.Defend());   // ход человека кончен — дальше отложенный ход (при удержании)
            switch (how)
            {
                case "resolve": session.Resolve(maxActions); break;
                case "auto": session.Auto = true; session.PlayOut(maxActions); break;
                default: session.PlayOut(maxActions); break;
            }
            session.StartTurn();   // потолок мог прийтись на отложенное начало хода — его досчитать, как без удержания
            return Trace(session);
        }
        string held = Play(hold: true);
        Assert.Equal(Play(hold: false), held);
        if (maxActions == BattleSession.MaxActions) Assert.EndsWith("конец=True", held, StringComparison.Ordinal);
    }

    /// <summary>Шаг за шагом, как экран (<see cref="BattleSession.AiStep"/> за ИИ, «Защищаться» за человека), с удержанием — тот же бой, что без
    /// него; между ходами — только «Автобой» и «Быстрый бой», команды хода не принимаются.</summary>
    [SkippableFact]
    public void HeldSession_StepByStep_LikeUnheld()
    {
        string Play(bool hold)
        {
            var session = Session(Skirmish(), hold);
            for (int guard = 0; guard < 400 && !session.Finished; guard++)
            {
                if (session.TurnStartPending)
                {
                    Assert.Equal([BattleCommand.Auto, BattleCommand.Resolve], session.Allowed.Order());
                    Assert.False(session.Defend());
                    Assert.False(session.Retreat());
                    Assert.False(session.Wait());
                    Assert.Empty(session.Attack(session.State.Units[2]));
                    session.StartTurn();
                    continue;
                }
                if (session.WaitsPlayer && session.Allowed.Contains(BattleCommand.Defend)) session.Defend();
                else if (session.WaitsPlayer) session.Skip();
                else session.AiStep();
            }
            return Trace(session);
        }
        string held = Play(hold: true);
        Assert.Equal(Play(hold: false), held);
        Assert.EndsWith("конец=True", held, StringComparison.Ordinal);
    }

    /// <summary>«Ждать» и «Отступить» при удержании кончают ход, как удар: следующий — по <see cref="BattleSession.StartTurn"/>; пока он не
    /// начат, вторая команда не принимается. «Отступить» ждавшего — бит 7 (<see cref="BattleUnit.RetreatingAfterWait"/>): раунд 2 он
    /// пропускает и уходит в свой ход раунда 3, как у движка (журнал 20261016-port-synth, бой WR).</summary>
    [SkippableFact]
    public void WaitAndRetreat_UnderHold_DeferTheNextTurn()
    {
        var player = Unit("P", attacker: true, slot: 0, Strike);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        var session = Session(State(player, enemy), hold: true);
        Assert.Same(player, session.Current);

        Assert.True(session.Wait());
        Assert.True(session.TurnStartPending);
        Assert.False(session.Wait());
        Assert.False(session.Retreat());
        session.StartTurn();
        Assert.Same(enemy, session.Current);
        session.AiStep();
        Assert.True(session.TurnStartPending);
        session.StartTurn();
        Assert.Same(player, session.Current);   // ждавший — в конце этого же раунда
        Assert.Equal(1, session.Round);

        Assert.True(session.Retreat());
        Assert.True(player.Retreating);
        Assert.True(player.RetreatingAfterWait);   // «Бежать» в отложенный ход — бит 7 (0x65a01b)
        Assert.True(session.TurnStartPending);
        Assert.Null(session.Current);
        Assert.False(session.Finished);
        session.StartTurn();   // раунд 2: ждавший беглец пропущен построением очереди, остаётся на поле
        Assert.Equal(2, session.Round);
        Assert.Same(enemy, session.Current);
        Assert.False(player.Retreated);
        Assert.True(player.OnField);
        session.AiStep();
        session.StartTurn();   // раунд 3: беглец уходит в начале своего хода — сторона пала, бой кончен
        Assert.True(player.Retreated);
        Assert.Equal("D", session.Winner);
        Assert.True(session.Finished);
    }

    /// <summary>Тики разных видов (яд, мороз, ожог — <see cref="Attacks.TickDots"/> по классу) и снятие паралича (<see cref="BattleNoteKind.Unparalyzed"/>)
    /// следующих юнитов при удержании — только по <see cref="BattleSession.StartTurn"/>.</summary>
    [SkippableFact]
    public void TickKindsAndNotes_WaitForStartTurn()
    {
        var player = Unit("P", attacker: true, slot: 0, Strike);
        var enemy = Unit("E", attacker: false, slot: 0, Slow);
        var stone = Unit("S", attacker: false, slot: 1, Middle);
        stone.Paralyzed = true;
        enemy.Dots.Add(new DotEffect { Class = AttackClass.Blister, Damage = 3, AppliedRound = 1, Long = true });
        enemy.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 5, AppliedRound = 1, Long = true });
        enemy.Dots.Add(new DotEffect { Class = AttackClass.Frostbite, Damage = 7, AppliedRound = 1, Long = true });
        var state = State(player, enemy, stone);
        var session = Session(state, hold: true);
        Assert.Same(player, session.Current);   // 90, затем окаменевший 70, затем отравленный 50

        session.Attack(enemy);
        Assert.Empty(session.Sim.DotLog);
        Assert.Empty(state.Notes);
        int hp = enemy.Hp;
        session.StartTurn();   // паралич снят — ход занят; следом начало хода отравленного
        Assert.Equal(BattleNoteKind.Unparalyzed, Assert.Single(state.Notes).Kind);
        Assert.Equal(new HashSet<AttackClass> { AttackClass.Poison, AttackClass.Frostbite, AttackClass.Blister }, session.Sim.DotLog.Select(t => t.Class).ToHashSet());
        Assert.Equal(3, session.Sim.DotLog.Count);
        Assert.All(session.Sim.DotLog, t => Assert.Equal("E", t.Unit));
        Assert.Equal(hp - 15, enemy.Hp);
        Assert.Same(enemy, session.Current);
    }

    /// <summary>Тик, добивший последнего врага в начале его хода: удар человека кончает ход, бой не кончен; <see cref="BattleSession.StartTurn"/>
    /// — тик, смерть, конец боя (сторона пала в начале хода, <c>0x627ffc</c>).</summary>
    [SkippableFact]
    public void Tick_KillingLastEnemy_EndsBattleOnStartTurn()
    {
        var last = Unit("L", attacker: false, slot: 0, Slow);
        last.Hp = 40;
        last.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 100, AppliedRound = 1, Long = true });
        var session = Session(State(Unit("Q", attacker: true, slot: 0, Strike), last), hold: true);
        Assert.Equal("Q", session.Current?.Id);

        session.Attack(last);
        Assert.True(last.Alive);
        Assert.False(session.Finished);
        Assert.Null(session.Winner);
        session.StartTurn();
        Assert.False(last.Alive);
        Assert.Equal("A", session.Winner);
        Assert.True(session.Finished);
        Assert.Null(session.Current);
    }

    /// <summary>Эльфы на настоящих таблицах (<see cref="GameBook.IsElf"/>: <c>Gunits.RACE_ID</c> → <c>Grace.RACE_TYPE</c> 5): Лесная Дева — да
    /// (у неё вторая атака «лечение» без картинки), Аббатиса Империи и неизвестный тип — нет.</summary>
    [SkippableFact]
    public void IsElf_OnRealTables()
    {
        var book = GameBook.Load(ImportedData.PathOf("rules"));
        Assert.True(book.IsElf("G000UU8033"));
        Assert.True(book.IsElf("g000uu8033"));
        Assert.False(book.IsElf("G000UU0017"));
        Assert.False(book.IsElf("G000UU9999"));
        Assert.False(book.IsElf(""));
    }
}
