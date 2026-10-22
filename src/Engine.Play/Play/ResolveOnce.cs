namespace LastEdict.Data.Play;

/// <summary>Итог, который получают один раз из двух потоков (бой ИИ на экране, <c>MapView.AiBattle</c>): либо его отдаёт экран
/// (<see cref="TrySet"/>), либо его считает первый пришедший к <see cref="Run"/> — второй ждёт и берёт тот же итог. Без этого при уходе
/// вида главный и рабочий потоки доигрывали бы один бой автобоем одновременно.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001",
    Justification = "ManualResetEventSlim без WaitHandle не держит неуправляемых ресурсов; объект живёт один бой")]
public sealed class ResolveOnce<T> where T : class
{
    private int _claimed;
    private readonly ManualResetEventSlim _ready = new(false);
    private T? _value;

    /// <summary>Итог уже есть (может быть null — «экран не сыграл, пусть ядро»).</summary>
    public bool Done => _ready.IsSet;

    /// <summary>Итог, если он уже есть; иначе null.</summary>
    public T? Value => _ready.IsSet ? _value : null;

    /// <summary>Отдать итог снаружи. Итог уже был или его считают — false, отданное не принимается.</summary>
    public bool TrySet(T? value)
    {
        if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return false;
        _value = value;
        _ready.Set();
        return true;
    }

    /// <summary>Итог: первый вызов считает его <paramref name="make"/>, остальные ждут и получают тот же. Исключение
    /// <paramref name="make"/> уходит вызвавшему, ждущие получают null.</summary>
    public T? Run(Func<T?> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
        {
            _ready.Wait();
            return _value;
        }
        try
        {
            _value = make();
        }
        finally
        {
            _ready.Set();
        }
        return _value;
    }
}
