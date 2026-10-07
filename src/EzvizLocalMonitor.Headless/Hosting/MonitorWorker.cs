using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Microsoft.Extensions.Hosting;

namespace EzvizLocalMonitor.Headless.Hosting;

public sealed class MonitorWorker(MonitoringRuntime runtime, AppSettings settings, StatusFile status,
    CompletedEventRetention retention, IAppLogger logger, DaemonExitState exit, IHostApplicationLifetime lifetime) : BackgroundService
{
    private readonly object _statusSync = new();
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly ProcessIdentity _identity = ProcessIdentity.Capture();
    private bool _starting = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var background = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        runtime.SnapshotChanged += OnSnapshot;
        runtime.Faulted += OnFault;
        WriteStatus();
        var heartbeat = HeartbeatAsync(background.Token);
        var cleanup = RetentionAsync(background.Token);
        try
        {
            await runtime.StartAsync(settings, false, stoppingToken);
            _starting = false;
            WriteStatus();
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.Error(LogChannel.App, "daemon startup/runtime failed", ex); exit.Fail(1); lifetime.StopApplication(); }
        finally
        {
            _starting = false;
            background.Cancel();
            var result = await runtime.StopAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
            if (!result.Completed) exit.Fail(1);
            try { await Task.WhenAll(heartbeat, cleanup); } catch (OperationCanceledException) { }
            WriteStatus();
            runtime.SnapshotChanged -= OnSnapshot;
            runtime.Faulted -= OnFault;
        }
    }

    private void OnSnapshot(RuntimeSnapshot snapshot) => WriteStatus();
    private void OnFault(Exception ex) { exit.Fail(1); lifetime.StopApplication(); }

    private void WriteStatus()
    {
        try
        {
            lock (_statusSync)
            {
                var current = runtime.Snapshot;
                var cameras = runtime.CameraStates;
                var views = settings.Cameras.Select(camera =>
                {
                    cameras.TryGetValue(camera.Id, out var state);
                    return new CameraStatusView(camera.Id, camera.Name, state?.State ?? CameraConnectionState.Stopped, state?.Message ?? "Not monitoring", state?.ReconnectCount ?? 0);
                }).ToArray();
                status.Write(new StatusSnapshot(1, _instance, _identity, DateTimeOffset.UtcNow, TimeZoneInfo.Local.Id, CommandDispatcher.Version,
                    _starting && current.State == "stopped" ? "starting" : current.State, current.ScheduleAllowed, views, current.EventCount));
            }
        }
        catch (Exception ex) { logger.Error(LogChannel.App, "daemon status write failed", ex); exit.Fail(1); lifetime.StopApplication(); }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct)) WriteStatus();
    }

    private async Task RetentionAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await runtime.RunMaintenanceAsync(token => retention.RunAsync(DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays), token), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.Error(LogChannel.App, "scheduled retention failed", ex); }
        }
    }
}
