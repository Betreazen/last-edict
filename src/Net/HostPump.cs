namespace LastEdict.Net;

/// <summary>Хост без показа (проверки, <c>tools/net-duo.py</c> без окна): свой поток зовёт <see cref="NetHost.Pump"/> раз в
/// <paramref name="every"/>. В игре почту разбирает кадр сцены.</summary>
public sealed class HostPump : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    public HostPump(NetHost host, TimeSpan? every = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        var period = every ?? TimeSpan.FromMilliseconds(5);
        _thread = new Thread(() =>
        {
            while (!_stop.IsCancellationRequested)
            {
                host.Pump();
                _stop.Token.WaitHandle.WaitOne(period);
            }
        }) { IsBackground = true, Name = "net-host" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(5));
        _stop.Dispose();
    }
}
