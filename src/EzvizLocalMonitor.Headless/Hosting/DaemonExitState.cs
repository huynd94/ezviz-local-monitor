namespace EzvizLocalMonitor.Headless.Hosting;

public sealed class DaemonExitState
{
    private int _code;
    public int Code => Volatile.Read(ref _code);
    public void Fail(int code) { if (code != 0) Interlocked.CompareExchange(ref _code, code, 0); }
}
