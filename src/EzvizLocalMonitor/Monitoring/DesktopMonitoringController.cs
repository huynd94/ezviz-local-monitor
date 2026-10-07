using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Desktop;

// Serialize whole desktop intents, not just individual runtime transitions.
public sealed class DesktopMonitoringController(MonitoringRuntime runtime)
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private bool _initialized;
    private int _shutdownRequested;

    public Task InitializeAsync(AppSettings settings, bool preview, CancellationToken ct) => CommandAsync(async () =>
    {
        if (_initialized) return;
        await runtime.StartAsync(settings, preview, ct);
        _initialized = true;
    }, ct);

    public Task StartAsync(AppSettings settings, bool preview, CancellationToken ct) => CommandAsync(async () =>
    {
        if (!_initialized)
        {
            await runtime.StartAsync(settings, preview, ct);
            _initialized = true;
        }
        else await runtime.ApplySettingsAsync(settings, ct);
        await runtime.SetManualMonitoringAsync(true, ct);
        runtime.SetPreviewEnabled(preview);
    }, ct);

    public Task ApplySettingsAsync(AppSettings settings, bool preview, CancellationToken ct) => CommandAsync(async () =>
    {
        if (!_initialized)
        {
            await runtime.StartAsync(settings, preview, ct);
            _initialized = true;
        }
        else await runtime.ApplySettingsAsync(settings, ct);
    }, ct);

    public Task PauseAsync(CancellationToken ct) => CommandAsync(async () =>
    {
        if (_initialized) await runtime.SetManualMonitoringAsync(false, ct);
    }, ct);

    public Task<ShutdownResult> ShutdownAsync(TimeSpan timeout, CancellationToken ct)
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
        // Bypass the UI gate so cancellation can interrupt startup holding it.
        return runtime.StopAsync(timeout, ct);
    }

    private async Task CommandAsync(Func<Task> operation, CancellationToken ct)
    {
        await _commands.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _shutdownRequested) != 0) throw new InvalidOperationException("Desktop monitoring is shutting down.");
            await operation();
        }
        finally { _commands.Release(); }
    }
}
