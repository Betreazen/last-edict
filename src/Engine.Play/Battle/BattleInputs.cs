using System.Text.Json.Serialization;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;

namespace LastEdict.Data.Battle;

/// <summary>
/// Действие боя по шагам (<see cref="BattleSession"/>) одной записью-данными: экран зовёт методы сессии, сессия пишет каждый внешний
/// вызов в <see cref="BattleSession.Actions"/>, и бой повторяется на том же состоянии и генераторе (<see cref="BattlePlay.Replay"/>) — так
/// бой человека на экране входит в журнал команд партии (<c>docs/game-commands.md</c>). Юниты — по id. Записывается и отклонённое
/// действие: повтор зовёт тот же метод и получает тот же отказ.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$act")]
[JsonDerivedType(typeof(StartTurnAction), "startTurn")]
[JsonDerivedType(typeof(AttackAction), "attack")]
[JsonDerivedType(typeof(SummonAction), "summon")]
[JsonDerivedType(typeof(DefendAction), "defend")]
[JsonDerivedType(typeof(RetreatAction), "retreat")]
[JsonDerivedType(typeof(SkipAction), "skip")]
[JsonDerivedType(typeof(WaitAction), "wait")]
[JsonDerivedType(typeof(AiDecideAction), "aiDecide")]
[JsonDerivedType(typeof(UseItemAtAction), "useItemAt")]
[JsonDerivedType(typeof(UseItemOnAction), "useItemOn")]
[JsonDerivedType(typeof(UseItemAction), "useItem")]
[JsonDerivedType(typeof(ResolveAction), "resolve")]
[JsonDerivedType(typeof(PlayOutAction), "playOut")]
[JsonDerivedType(typeof(SetAutoAction), "setAuto")]
public abstract record BattleInput;

public sealed record StartTurnAction : BattleInput;

/// <summary>Удар ходящего; <see cref="Target"/> null — атака «по всем».</summary>
public sealed record AttackAction(string? Target) : BattleInput;

public sealed record SummonAction(int Slot) : BattleInput;

public sealed record DefendAction : BattleInput;

public sealed record RetreatAction : BattleInput;

public sealed record SkipAction : BattleInput;

public sealed record WaitAction : BattleInput;

public sealed record AiDecideAction : BattleInput;

public sealed record UseItemAtAction(int Index, int Slot) : BattleInput;

public sealed record UseItemOnAction(int Index, string Target) : BattleInput;

public sealed record UseItemAction(int Index, string Target) : BattleInput;

public sealed record ResolveAction(int MaxActions) : BattleInput;

public sealed record PlayOutAction(int MaxActions) : BattleInput;

/// <summary>Автобой стороны (<see cref="Attacker"/>) или обеих (null).</summary>
public sealed record SetAutoAction(bool? Attacker, bool On) : BattleInput;

/// <summary>Как создана сессия боя: чья сторона — игрока, бой двух людей, отложенное начало хода (экран).</summary>
public sealed record BattleSetup(bool PlayerIsAttacker, bool BothHuman = false, bool HoldTurnStart = false);

/// <summary>
/// Бой, сыгранный по шагам: устройство сессии, её действия по порядку и сколько чисел <c>rand()</c> процесса (<c>GameSession.Crt</c>)
/// экран взял при открытии (вариант фона). Повтор на том же состоянии боя и генераторе партии даёт тот же журнал ходов.
/// </summary>
public sealed record BattlePlay(BattleSetup Setup, IReadOnlyList<BattleInput> Actions, int CrtDraws = 0)
{
    /// <summary>Сыграть заново: новая сессия на <paramref name="state"/> (состоянии из <c>GameSession.StartBattle</c>, ещё не тронутом
    /// боем) и <paramref name="rng"/>, затем действия по порядку. Числа <c>rand()</c> экрана берёт вызывающий — у него поток процесса.</summary>
    public BattleSession Replay(BattleState state, IRng rng, RulesConstants constants)
    {
        var session = new BattleSession(state, new BattleSim(state, rng, constants), constants, Setup.PlayerIsAttacker)
        {
            BothHuman = Setup.BothHuman, HoldTurnStart = Setup.HoldTurnStart,
        };
        foreach (var action in Actions) session.Apply(action);
        return session;
    }
}
