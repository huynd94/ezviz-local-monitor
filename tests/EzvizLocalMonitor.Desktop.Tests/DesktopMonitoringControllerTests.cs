using EzvizLocalMonitor.Desktop;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using OpenCvSharp;
using Xunit;

namespace EzvizLocalMonitor.Desktop.Tests;

public sealed class DesktopMonitoringControllerTests
{
    [Fact]
    public async Task StopClickedWhileStartIsPendingKeepsTheLatestIntent()
    {
        var release = Signal();
        var session = new Session(release.Task);
        var runtime = Create(session);
        var controller = new DesktopMonitoringController(runtime);
        var start = controller.StartAsync(Settings(), false, CancellationToken.None);
        await session.Starting.Task;
        var pause = controller.PauseAsync(CancellationToken.None);
        Assert.False(pause.IsCompleted);
        release.SetResult();
        await Task.WhenAll(start, pause);
        Assert.False(runtime.Snapshot.IsMonitoring);
        Assert.Equal("paused", runtime.Snapshot.State);
        Assert.True((await controller.ShutdownAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task ShutdownInterruptsStartupWithoutWaitingForDesktopCommandGate()
    {
        var release = Signal();
        var session = new Session(release.Task);
        var runtime = Create(session);
        var controller = new DesktopMonitoringController(runtime);
        var start = controller.StartAsync(Settings(), false, CancellationToken.None);
        await session.Starting.Task;
        Assert.True((await controller.ShutdownAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(runtime.Snapshot.IsMonitoring);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAsync(Settings(), false, CancellationToken.None));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AppSettings Settings() => new() { Cameras = [new() { RtspUrl = "rtsp://fixture/stream" }] };
    private static MonitoringRuntime Create(Session session) => new(() => session, TimeProvider.System, TimeZoneInfo.Utc, new Logger());
    private sealed class Logger : IAppLogger { public void Info(LogChannel c, string m) { } public void Error(LogChannel c, string m, Exception? e = null) { } }
    private sealed class Session(Task gate) : IMonitoringSession
    {
        public TaskCompletionSource Starting { get; } = Signal();
        public event Action<Guid, string>? CameraStatusChanged { add { } remove { } }
        public event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged { add { } remove { } }
        public event Action<Guid, Mat>? PreviewReady { add { } remove { } }
        public event Action<DetectionEvent>? EventRecorded { add { } remove { } }
        public async Task StartAsync(AppSettings settings, CancellationToken ct) { Starting.SetResult(); await gate.WaitAsync(ct); }
        public void SetPreviewEnabled(bool enabled) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
