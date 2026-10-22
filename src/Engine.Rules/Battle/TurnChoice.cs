namespace LastEdict.Rules.Battle;

/// <summary>Решение хода, принятое не политикой ИИ, — человеком (в С2 — из журнала оригинала): <see cref="BattleSim.Run"/> исполняет
/// его вместо <see cref="BattleAi.Choose"/> и бросков решения ИИ не тратит — движок в ход человека их не делает (журнал: нажатие
/// <c>BTN_DEFEND</c> или щелчок по цели, затем удар без <c>rng</c> выбора). <see cref="Target"/> у удара по одному — цель, у
/// площадного не читается; <see cref="SummonSlot"/> — позиция призыва (−1 — не призыв).</summary>
public sealed record TurnChoice(TurnChoiceKind Kind, BattleUnit? Target = null, int ItemIndex = -1, int SummonSlot = -1);

/// <summary>Решение одного действия в <see cref="BattleSim.Choices"/>: раунд, кто, что.</summary>
public sealed record ChoiceNote(int Round, string Unit, TurnChoiceKind Kind);

/// <summary>Вид решения хода (<see cref="TurnChoice"/>).</summary>
public enum TurnChoiceKind { Attack, Defend, Wait, Retreat, UseItem }
