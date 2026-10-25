using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Rng;

namespace LastEdict.Data.Play;

/// <summary>Сессия в сетевой партии (Э7, <c>docs/network.md</c>): у хоста — настоящая партия, у клиента — проекция (<c>PlayerView</c>),
/// команды которой исполняет хост.</summary>
public sealed partial class GameSession
{
    /// <summary>Клиент сети: команды верхнего уровня уходят сюда, а не в правила (<see cref="Execute"/>) — их исполняет хост, а состояние
    /// клиента меняют его дельты вида (<see cref="AdoptView"/>). То, что клиент обязан исполнить и у себя (бой на экране), он исполняет
    /// <see cref="ExecuteHere"/>. null — обычная партия.</summary>
    public Func<GameCommand, CommandResult>? Remote { get; set; }

    private bool _here;

    /// <summary>Исполнить команду правилами этой сессии мимо <see cref="Remote"/> (клиент: бой, собранный у себя).</summary>
    public CommandResult ExecuteHere(GameCommand command)
    {
        bool was = _here;
        _here = true;
        try { return Execute(command); }
        finally { _here = was; }
    }

    /// <summary>Хост: бой экрана сыгран не здесь (у клиента на копии генератора боя хоста) — <see cref="EndBattleCommand"/> играет его
    /// заново по действиям на генераторе партии, как при повторе журнала.</summary>
    public bool BattlesPlayedElsewhere { get; set; }

    /// <summary>Повтор боя, сыгранного не здесь (<see cref="BattlesPlayedElsewhere"/>), дал не тот журнал ходов, что прислал клиент. Снимает
    /// тот, кто проверяет.</summary>
    public bool ElsewhereBattleDiverged { get; set; }

    /// <summary>Клиент: новый вид партии от хоста. Состояние меняется без команды и без аудита правок мимо команд — это не правка партии, а
    /// новая проекция; бой, собранный у себя (<see cref="StartBattle"/>), остаётся.</summary>
    public void AdoptView(GameState view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _state = WithLandOwners(view, Book);
        _settled = null;
    }

    /// <summary>Поставить генераторам партии места из <paramref name="random"/>: только названные там потоки (клиенту хост даёт лишь поток
    /// боя) и, если <paramref name="crt"/>, — CRT. Ссылки на генераторы остаются прежними.</summary>
    public void RestoreRandom(RandomStreams random, bool crt = true)
    {
        ArgumentNullException.ThrowIfNull(random);
        foreach (var (stream, rng) in _rng)
            if (random.Of(stream) is { } at) rng.Restore(at.Table, at.Index);
        if (crt) Crt.Reset(random.Crt);
    }

    /// <summary>Хост: вернуть партию к снимку <paramref name="snapshot"/> (<see cref="SnapshotWithoutSummaries"/>) — команда клиента упала
    /// посреди, а отказ не должен менять ни состояния, ни генераторов. Бой, начатый командой, снимается.</summary>
    public void Restore(GameState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _state = snapshot with { Random = null };
        if (snapshot.Random is { } random) RestoreRandom(random);
        _battle = null;
        _ending = null;
        _settled = (State, RngSteps, Crt.State);
    }

    /// <summary>Места генераторов сейчас (для семени боя клиенту и отката).</summary>
    public RandomStreams RandomNow() => CurrentRandom();

    /// <summary>Бой собран (<see cref="StartBattle"/>) и ещё не кончен (<see cref="EndBattle"/>): хост доигрывает его автобоем, если срок хода
    /// клиента вышел.</summary>
    public LastEdict.Rules.Battle.BattleState? BattleInProgress => _battle?.State;

    /// <summary>Конец боя посчитан, но в карту не записан (<see cref="CommitBattle"/>).</summary>
    public bool BattleEnding => _ending is not null;

    /// <summary>Хост: бой, сыгранный клиентом (действия <paramref name="play"/>), — заново на собранном бою <paramref name="state"/> и
    /// генераторе партии; журнал ходов — итог.</summary>
    public IReadOnlyList<LastEdict.Rules.Battle.TurnRecord> ReplayBattleHere(LastEdict.Data.Battle.BattlePlay play,
                                                                             LastEdict.Rules.Battle.BattleState state) => ReplayBattle(play, state);

    /// <summary>Клиент: собрать у себя бой хода ИИ по виду — так же, как его собрал хост (<see cref="MapBattle.Build"/>, призванные — по
    /// счётчику карты, выбор призыва — CRT сессии); партия не меняется (очки хода нападающего хост уже списал).</summary>
    public LastEdict.Rules.Battle.BattleState BuildBattleHere(string attackerGroup, string defenderGroup, bool duel)
    {
        if (Book is null) throw new InvalidOperationException("партия без правил боя");
        var battle = MapBattle.Build(Map, attackerGroup, defenderGroup, Book, State.Info.Difficulty, Map.Stacks[attackerGroup].Move, duel);
        var ids = new BattleIds(Map.IdNext, [.. Map.Units.Keys], attackerGroup);
        battle.NewUnitId = ids.NewUnit;
        battle.ProcessRand = Crt;
        return battle;
    }
}
