using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>
/// Трасса решений ИИ (<see cref="GameSession.AiTrace"/>) — приёмник для зонда порта: по ней распределения выборов порта сравниваются с
/// журналом зонда оригинала (<c>docs/corpus-schema.md</c>: <c>ai_tactic_pick</c>, <c>ai_strategic_step</c>, <c>ai_turn</c>). Решений не
/// меняет; не поставлена — ни вызовов, ни затрат. Зовётся с потока хода ИИ.
/// </summary>
public interface IAiTrace
{
    /// <summary>Тактика выбрала действие отряда (<see cref="AiTactics.Choose"/>): действие, его счёт после проверки пути и сколько
    /// кандидатов было в списке (после поправок).</summary>
    void Picked(string playerId, int day, AiTacticOption action, double score, int candidates);

    /// <summary>Стратегия исполнила действие (<c>CAiStrategic::step</c>, успешный <c>execute</c>): проход и действие; <paramref name="number"/>
    /// — номер шага в ходе (с 1).</summary>
    void Stepped(string playerId, int day, AiExecuted step, int number);

    /// <summary>Ход игрока ИИ кончился (стратегия, тактика, оперативный слой): длительность в мс, шагов стратегии и выборов тактики.</summary>
    void TurnEnded(string playerId, int day, long ms, int steps, int picks);
}
