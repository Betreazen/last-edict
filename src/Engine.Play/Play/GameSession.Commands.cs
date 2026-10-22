using D2.Data.Battle;
using D2.Data.Play.Commands;
using D2.Rules.Battle;

namespace D2.Data.Play;

public sealed partial class GameSession
{
    /// <summary>Журнал команд (<see cref="CommandJournal"/>): задан — <see cref="Execute"/> пишет в него каждую команду с хешем состояния
    /// после неё. Хеш — сериализация всей партии, поэтому журнал для повторов, баг-репортов и сети, а не для каждого кадра.</summary>
    public CommandJournal? Journal
    {
        get => _journal;
        set
        {
            _journal = value;
            _settled = (State, Rng.Index, Crt.State);   // правка мимо команд до первой команды журнала — тоже Outside
        }
    }

    private CommandJournal? _journal;

    private int _executing;

    /// <summary>Партия меняется вне <see cref="Execute"/> (аудит перевода интерфейса на команды, <c>docs/game-commands.md</c> §3): зовётся
    /// до записи нового состояния, стек вызова показывает место. Без подписчика — ничего.</summary>
    public event Action? ChangedOutsideCommands;

    /// <summary>Отпечаток партии после прошлой команды верхнего уровня: состояние (запись неизменяемая — новое состояние, новая ссылка) и
    /// места потоков случайных чисел. Не совпал перед следующей — партия менялась мимо команд.</summary>
    private (GameState State, int Rng, uint Crt)? _settled;

    /// <summary>Записчик команды верхнего уровня, пока она идёт: вложенные команды пишутся в её решения.</summary>
    private EventRecorder? _outerRecorder;

    /// <summary>Идёт повтор журнала (<see cref="CommandJournal.Replay"/>): то, что в живой игре сделал экран вне команд (бой человека по
    /// шагам на генераторе партии), команда повторяет сама (<see cref="EndBattleCommand"/>).</summary>
    internal bool Replaying { get; set; }

    /// <summary>Основной наблюдатель без подписчиков (<see cref="Observer"/> отдаёт их вместе).</summary>
    internal ITurnObserver? MainObserver
    {
        get => _observer;
        set => _observer = value;
    }

    /// <summary>
    /// Исполнить команду (<see cref="GameCommand"/>): значение метода сессии, отчёт, события хода и решения снаружи
    /// (<see cref="CommandResult"/>). Показ получает события как прежде — через наблюдателя хода; записчик стоит перед ним. Команда, поданная,
    /// пока другая ждёт (ответ на предложение из окна, пока ход ИИ стоит на вызове наблюдателя), в журнал отдельно не пишется — она решение
    /// внешней (<see cref="NestedCommand"/>).
    /// <para>Сессия однопоточна по командам: команда во время команды допустима только пока та стоит на вызове наблюдателя (ход ИИ на
    /// рабочем потоке сцены ждёт ответа, главный поток подаёт ответ) — иначе <see cref="InvalidOperationException"/>. Поля счёта не
    /// volatile: их видимость между потоками держит рукопожатие заявок сцены (рабочий ждёт на нём, <c>MapView.AiTurn</c>).</para>
    /// <para>С журналом команда сверяет отпечаток партии (состояние и места генераторов) с отпечатком после прошлой команды: правка партии мимо команд
    /// отмечается в записи (<see cref="JournalEntry.Outside"/>).</para>
    /// </summary>
    public CommandResult Execute(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var outer = _outerRecorder;
        if (_executing > 0 && outer?.InCall != true)
            throw new InvalidOperationException($"команда {command.GetType().Name} подана во время другой не на вызове наблюдателя");
        bool outside = _executing == 0 && _settled is { } settled
                       && (!ReferenceEquals(settled.State, State)
                           || !command.FollowsScreenDraws && (settled.Rng != Rng.Index || settled.Crt != Crt.State));
        var saved = _observer;
        var recorder = new EventRecorder(saved);
        _observer = recorder;
        if (_executing++ == 0) _outerRecorder = recorder;
        try
        {
            var report = new List<string>();
            var value = command.Run(this, report);
            var result = new CommandResult(value, report, recorder.Events, recorder.Answers);
            if (_executing == 1)
            {
                Journal?.Add(command, StateHash.Of(this), recorder.Answers, outside);
                _settled = (State, Rng.Index, Crt.State);
            }
            else outer!.Nested(command);
            return result;
        }
        finally
        {
            _executing--;
            _outerRecorder = outer;
            _observer = saved;
        }
    }

    /// <summary>Команда со значением нужного типа (<see cref="Execute"/>); строки отчёта — в <paramref name="report"/>. Так интерфейс зовёт
    /// правила: всё, что меняет партию, — командой (<c>docs/game-commands.md</c> §3).</summary>
    public T Run<T>(GameCommand command, List<string>? report = null)
    {
        var result = Execute(command);
        report?.AddRange(result.Report);
        return (T)result.Value!;
    }

    /// <summary>Команда без значения (<see cref="Run{T}"/>).</summary>
    public void Run(GameCommand command, List<string>? report = null)
    {
        var result = Execute(command);
        report?.AddRange(result.Report);
    }

    /// <summary>Повтор боя, сыгранного по шагам (<see cref="BattlePlay"/>), на состоянии <paramref name="state"/> и генераторе партии: сначала
    /// числа <c>rand()</c> экрана (<see cref="BattlePlay.CrtDraws"/>), затем действия. Журнал ходов — тот, что дал экран.</summary>
    internal IReadOnlyList<TurnRecord> ReplayBattle(BattlePlay play, BattleState state)
    {
        if (Book is null) throw new InvalidOperationException("повтор боя без правил");
        for (int i = 0; i < play.CrtDraws; i++) Crt.Next();
        return play.Replay(state, Rng, Book.Rules.Constants).Log;
    }

    /// <summary>Повтор боя на экране (<see cref="ReplayBattle(BattlePlay, BattleState)"/>) — на собранном бою (<see cref="StartBattle"/>).</summary>
    internal IReadOnlyList<TurnRecord> ReplayBattle(BattlePlay play) =>
        ReplayBattle(play, _battle?.State ?? throw new InvalidOperationException("боя нет"));
}
