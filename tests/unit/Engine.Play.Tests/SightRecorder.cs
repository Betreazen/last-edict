using D2.Data.Play;
using D2.Rules.Battle;

namespace D2.Data.Tests;

/// <summary>Наблюдатель хода для тестов: записывает события (<see cref="TurnSight"/>), бой не играет.</summary>
internal sealed class SightRecorder : ITurnObserver
{
    public List<TurnSight> Sights { get; } = [];

    public void PlayerTurn(string playerId)
    {
    }

    public void StackMoving(StackMove move)
    {
    }

    public IReadOnlyList<TurnRecord>? Battle(BattleState state) => null;

    public void Happened(TurnSight sight) => Sights.Add(sight);
}
