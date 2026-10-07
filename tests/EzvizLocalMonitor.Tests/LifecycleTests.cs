using EzvizLocalMonitor.Services;
using EzvizLocalMonitor.Models;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task CoordinatorDisposalJoinsInProgressOnvifStartup()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, Path.Combine(root, "model.onnx"));
        var logger = new TestLogger();
        var coordinator = new MonitorCoordinator(paths, new EventStore(paths), new AlertDispatcher(paths, logger), logger);
        var start = coordinator.StartAsync(new AppSettings
        {
            Cameras = [new() { IsEnabled = true, RtspUrl = "rtsp://admin:fixture-password@127.0.0.1/fixture",
                OnvifServiceUrl = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/onvif" }]
        });
        using var connection = await server.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var stop = coordinator.DisposeAsync().AsTask();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(start.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        coordinator.SetPreviewEnabled(false);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task FallbackStartupFailureIsNotSwallowedByTaskRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, Path.Combine(root, "missing-model.onnx"));
        var logger = new TestLogger();
        await using var coordinator = new MonitorCoordinator(paths, new EventStore(paths), new AlertDispatcher(paths, logger), logger);
        await Assert.ThrowsAsync<FileNotFoundException>(() => coordinator.StartAsync(new AppSettings
        {
            Cameras = [new() { IsEnabled = true, RtspUrl = "rtsp://127.0.0.1/fixture" }]
        }));
    }

    [Fact]
    public async Task DisposedSnapshotReaderDoesNotReopenOnWarmOrRead()
    {
        var logger = new TestLogger();
        await using var registry = new TaskRegistry(logger);
        using var reader = new RtspSnapshotReader(registry);
        reader.Dispose();
        reader.Warm(new() { RtspUrl = "rtsp://127.0.0.1/fixture" });
        Assert.True(await registry.CompleteAsync(TimeSpan.FromSeconds(5)));
        // Disposed check must happen before any access to a native destination.
        Assert.Throws<ObjectDisposedException>(() => reader.TryRead(new(), null!));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task DisposedCameraRejectsStartAndDisposesIdempotently()
    {
        // No model is needed when the camera never starts native work.
        var camera = new CameraMonitor(new(), null!, 1, 2, 3, new TestLogger());
        await camera.DisposeAsync();
        await camera.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => camera.Start());
    }

    [Fact]
    public async Task CancelledDeliveryKeepsOwnershipUntilSenderActuallyExits()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var files = new ActiveEventFiles();
        var path = Path.Combine(Path.GetTempPath(), "owned-alert-fixture.jpg");
        await using var queue = new AlertQueueService(async (_, _, ct) =>
        {
            using var registration = ct.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await release.Task;
            return "Telegram: đã gửi";
        });
        using var lease = files.Acquire(path);
        var delivery = queue.EnqueueAsync(new(), new() { Id = 1 }, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(delivery.IsCompleted);
            Assert.Null(files.TryBeginDelete(path));
            release.SetResult();
            Assert.Equal("Chưa gửi (đã dừng)", await delivery);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task QueueDisposalDrainsAcceptedAlertsWithoutCancellingSender()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new AlertQueueService(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task;
            ct.ThrowIfCancellationRequested();
            return "Telegram: đã gửi";
        });
        var delivery = queue.EnqueueAsync(new(), new() { Id = 1 });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = queue.DisposeAsync().AsTask();
        try
        {
            Assert.False(stop.IsCompleted);
            release.SetResult();
            Assert.Equal("Telegram: đã gửi", await delivery);
            await stop;
        }
        finally { release.TrySetResult(); await stop; }
    }

    [Fact]
    public async Task ListenerDisposalCancelsAndJoinsStartup()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var camera = new CameraDefinition { OnvifServiceUrl = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/onvif" };
        var listener = new OnvifEventListener(camera, "fixture-password");
        var start = listener.StartAsync();
        using var connection = await server.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var stop = listener.DisposeAsync().AsTask();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(start.IsCompleted);
        Assert.False(await start);
        await listener.DisposeAsync();
    }

    [Fact]
    public async Task DisposedListenerRejectsStartupEvenWithoutCamera()
    {
        var listener = new OnvifEventListener(new(), "fixture-password");
        await listener.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => listener.StartAsync());
    }

    [Fact]
    public async Task DisposedCoordinatorRejectsStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, Path.Combine(root, "model.onnx"));
        var logger = new TestLogger();
        var coordinator = new MonitorCoordinator(paths, new EventStore(paths), new AlertDispatcher(paths, logger), logger);
        await coordinator.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.StartAsync(new()));
    }

    [Fact]
    public async Task ShutdownClosesAdmissionAndWaitsForOwnedWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new TaskRegistry(new TestLogger());
        Assert.True(registry.TryStart(async _ => { entered.SetResult(); await release.Task; }));
        await entered.Task;
        var stop = registry.CompleteAsync(TimeSpan.FromSeconds(20));
        Assert.False(stop.IsCompleted);
        Assert.False(registry.TryStart(_ => Task.CompletedTask));
        release.SetResult();
        Assert.True(await stop);
    }

    [Fact]
    public async Task DeadlineCancelsWorkAndTaskErrorsAreObserved()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new TaskRegistry(new TestLogger());
        registry.TryStart(async ct =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { cancelled.SetResult(); }
        });
        Assert.False(await registry.CompleteAsync(TimeSpan.Zero));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await registry.CompleteAsync(TimeSpan.FromSeconds(2)));
        var logger = new TestLogger();
        var failed = new TaskRegistry(logger);
        failed.TryStart(_ => throw new InvalidOperationException("fixture failure"));
        Assert.True(await failed.CompleteAsync(TimeSpan.FromSeconds(2)));
        Assert.Single(logger.Errors);
    }

    [Fact]
    public void ActiveImageCannotBeDeletedUntilEveryLeaseEnds()
    {
        var files = new ActiveEventFiles();
        var path = Path.Combine(Path.GetTempPath(), "fixture.jpg");
        using (files.Acquire(path))
        {
            Assert.Null(files.TryBeginDelete(path));
            Assert.True(files.IsInUse(path));
        }
        using (var deletion = files.TryBeginDelete(path))
        {
            Assert.NotNull(deletion);
            Assert.Throws<IOException>(() => { using var lease = files.Acquire(path); });
        }
        Assert.False(files.IsInUse(path));
    }

    private sealed class TestLogger : IAppLogger
    {
        public List<string> Errors { get; } = [];
        public void Info(LogChannel channel, string message) { }
        public void Error(LogChannel channel, string message, Exception? exception = null) { lock (Errors) Errors.Add(message); }
    }
}
