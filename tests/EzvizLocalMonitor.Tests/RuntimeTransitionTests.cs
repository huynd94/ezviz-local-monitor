using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using OpenCvSharp;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class RuntimeTransitionTests
{
    private static AppSettings Settings() => new() { Cameras = [new() { RtspUrl = "rtsp://fixture/stream" }] };

    [Fact]
    public async Task SessionCancellationRemainsLinkedAfterStartupCompletes()
    {
        var session = new Session();
        var runtime = Create(() => session);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        Assert.False(session.StartToken.IsCancellationRequested);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
        Assert.True(session.StartToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ShutdownCancelsMaintenanceBeforeWaitingForTransitionGate()
    {
        var runtime = Create(() => new Session());
        await runtime.StartAsync(new AppSettings(), false, CancellationToken.None);
        var entered = NewSignal();
        var maintenance = runtime.RunMaintenanceAsync(async ct => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }, CancellationToken.None);
        await entered.Task;
        var result = await runtime.StopAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.True(result.Completed, result.Failure);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => maintenance);
    }

    [Fact]
    public async Task CancelledShutdownReportsIncompleteRatherThanThrowing()
    {
        var session = new Session();
        var runtime = Create(() => session);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = await runtime.StopAsync(TimeSpan.FromSeconds(2), cancelled.Token);
        Assert.False(result.Completed);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task RetryShutdownJoinsTheSameDisposalWithoutDisposingTwice()
    {
        var release = NewSignal();
        var session = new Session(disposeGate: release.Task);
        var runtime = Create(() => session);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        var first = runtime.StopAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        await session.Disposing.Task;
        Assert.False((await first).Completed);
        Assert.False(session.Disposed);
        release.SetResult();
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task FailedStartupCanBeRetriedOnlyAfterOwnedSessionIsDisposed()
    {
        var failed = new Session(failStart: true);
        var healthy = new Session();
        var sessions = new Queue<Session>([failed, healthy]);
        var runtime = Create(() => sessions.Dequeue());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(Settings(), false, CancellationToken.None));
        Assert.Equal("faulted", runtime.Snapshot.State);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        Assert.True(failed.Disposed);
        Assert.True(runtime.Snapshot.IsMonitoring);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task FaultyCameraObserverCannotBreakOtherObserversOrLeakMessageSecrets()
    {
        var session = new Session();
        var runtime = Create(() => session);
        runtime.CameraStatusChanged += (_, _) => throw new InvalidOperationException("observer fixture");
        string? received = null;
        runtime.CameraStatusChanged += (_, text) => received = text;
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        session.EmitStatus("rtsp://admin:YOUR_RTSP_PASSWORD@127.0.0.1/stream");
        Assert.NotNull(received);
        Assert.DoesNotContain("YOUR_RTSP_PASSWORD", received);
        Assert.True(runtime.Snapshot.IsMonitoring);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task UniqueEventCountIsPublishedImmediatelyAndDisposedSessionIsDetached()
    {
        var session = new Session();
        var runtime = Create(() => session);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        session.EmitEvent(new DetectionEvent { Id = 42 });
        session.EmitEvent(new DetectionEvent { Id = 42, DeliveryStatus = "Telegram: đã gửi" });
        Assert.Equal(1, runtime.Snapshot.EventCount);
        await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        session.EmitEvent(new DetectionEvent { Id = 99 });
        Assert.Equal(1, runtime.Snapshot.EventCount);
    }

    private static MonitoringRuntime Create(Func<IMonitoringSession> factory) => new(factory, TimeProvider.System, TimeZoneInfo.Utc, new Logger());
    [Fact]
    public async Task FailedRestartNeverReportsAnUnstartedSessionAsRunning()
    {
        var original = new Session();
        var failed = new Session(failStart: true);
        var recovered = new Session();
        var sessions = new Queue<Session>([original, failed, recovered]);
        var runtime = Create(() => sessions.Dequeue());
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ApplySettingsAsync(Settings(), CancellationToken.None));
        Assert.Equal("faulted", runtime.Snapshot.State);
        Assert.False(runtime.Snapshot.IsMonitoring);
        Assert.True(failed.Disposed);
        await runtime.EvaluateScheduleAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.True(runtime.Snapshot.IsMonitoring);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task ManualStopPublishesTransitionWhileDisposalIsStillBlocked()
    {
        var release = NewSignal();
        var session = new Session(disposeGate: release.Task);
        var runtime = Create(() => session);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        var pause = runtime.SetManualMonitoringAsync(false, CancellationToken.None);
        await session.Disposing.Task;
        Assert.Equal("stopping", runtime.Snapshot.State);
        Assert.False(runtime.Snapshot.IsMonitoring);
        release.SetResult();
        await pause;
        Assert.Equal("paused", runtime.Snapshot.State);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task LateSuccessfulStartupCannotResurrectRunningAfterShutdownTimeout()
    {
        var release = NewSignal();
        var session = new Session(startGate: release.Task);
        var runtime = Create(() => session);
        var startup = runtime.StartAsync(Settings(), false, CancellationToken.None);
        await session.Starting.Task;
        Assert.False((await runtime.StopAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None)).Completed);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        Assert.False(runtime.Snapshot.IsMonitoring);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task CompletedCleanupWithTimeoutDetachesSessionAndAllowsAcknowledgedRetry()
    {
        var session = new Session(failDisposeAfterCleanup: true);
        var runtime = Create(() => session);
        await runtime.StartAsync(Settings(), false, CancellationToken.None);
        Assert.False((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
        Assert.True(session.Disposed);
        session.EmitEvent(new DetectionEvent { Id = 99 });
        Assert.Equal(0, runtime.Snapshot.EventCount);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [Fact]
    public async Task ManualRecoveryAlsoResumesPeriodicSchedulingAfterTransitionFault()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-10-05T22:05:00Z"));
        var sessions = new Queue<Session>([new(), new(failStart: true), new()]);
        var runtime = new MonitoringRuntime(() => sessions.Dequeue(), clock, TimeZoneInfo.Utc, new Logger());
        var faulted = NewSignal();
        var outside = NewSignal();
        runtime.Faulted += _ => faulted.TrySetResult();
        runtime.SnapshotChanged += snapshot => { if (snapshot.State == "outside-schedule") outside.TrySetResult(); };
        var settings = Settings();
        settings.MonitorSchedules = [
            new() { Days = "Mon", StartTime = "22:00", EndTime = "22:10", PerformanceProfile = 0 },
            new() { Days = "Mon", StartTime = "22:11", EndTime = "22:20", PerformanceProfile = 2 }];
        try
        {
            await runtime.StartAsync(settings, false, CancellationToken.None);
            clock.Set(DateTimeOffset.Parse("2026-10-05T22:15:00Z"));
            await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await runtime.ApplySettingsAsync(settings, CancellationToken.None);
            Assert.True(runtime.Snapshot.IsMonitoring);
            clock.Set(DateTimeOffset.Parse("2026-10-05T23:00:00Z"));
            await outside.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(runtime.Snapshot.IsMonitoring);
        }
        finally { await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None); }
    }

    private sealed class Clock(DateTimeOffset initial) : TimeProvider
    {
        private long _ticks = initial.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Set(DateTimeOffset value) => Interlocked.Exchange(ref _ticks, value.UtcTicks);
    }
    private sealed class Logger : IAppLogger { public void Info(LogChannel c, string m) { } public void Error(LogChannel c, string m, Exception? e = null) { } }

    private sealed class Session(Task? disposeGate = null, bool failStart = false, Task? startGate = null, bool failDisposeAfterCleanup = false) : IMonitoringSession
    {
        public CancellationToken StartToken { get; private set; }
        public bool Disposed { get; private set; }
        public bool CleanupCompleted => Disposed;
        private bool _disposing;
        public TaskCompletionSource Disposing { get; } = NewSignal();
        public TaskCompletionSource Starting { get; } = NewSignal();
        public event Action<Guid, string>? CameraStatusChanged;
        public event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged { add { } remove { } }
        public event Action<Guid, Mat>? PreviewReady { add { } remove { } }
        public event Action<DetectionEvent>? EventRecorded;
        public async Task StartAsync(AppSettings settings, CancellationToken ct)
        {
            StartToken = ct;
            Starting.TrySetResult();
            if (failStart) throw new InvalidOperationException("startup fixture");
            if (startGate is not null) await startGate;
        }
        public void EmitStatus(string text) => CameraStatusChanged?.Invoke(Guid.NewGuid(), text);
        public void EmitEvent(DetectionEvent item) => EventRecorded?.Invoke(item);
        public void SetPreviewEnabled(bool enabled) { ObjectDisposedException.ThrowIf(Disposed, this); }
        public async ValueTask DisposeAsync()
        {
            if (_disposing) throw new InvalidOperationException("Double disposal detected.");
            _disposing = true;
            Disposing.TrySetResult();
            if (disposeGate is not null) await disposeGate;
            Disposed = true;
            if (failDisposeAfterCleanup) throw new TimeoutException("Cleanup completed after its deadline.");
        }
    }
}
