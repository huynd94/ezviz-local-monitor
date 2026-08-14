using Xunit;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Tests;

public sealed class EventStoreTests : IDisposable
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"ezviz-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void EventStore_CrudAndAnalysisUpdate_Works()
    {
        var store = new EventStore(_database);
        store.Initialize();
        var item = new DetectionEvent
        {
            CameraId = Guid.NewGuid(),
            CameraName = "Test camera",
            Confidence = 0.88,
            ImagePath = "test.jpg",
            DeliveryStatus = "Chưa gửi"
        };
        item.Id = store.Add(item);
        store.UpdateDeliveryStatus(item.Id, "Telegram: đã gửi");
        store.UpdateAiAnalysis(item.Id, new AiMovementAnalysis(true, true, 0.91, "Có người", "AI xác nhận"));

        var result = Assert.Single(store.Recent());
        Assert.Equal(item.Id, result.Id);
        Assert.Equal("Telegram: đã gửi", result.DeliveryStatus);
        Assert.True(result.AiMotionDetected);
        Assert.True(result.AiPersonPresent);
        Assert.Equal("Có người", result.AiSummary);
    }

    public void Dispose()
    {
        try { File.Delete(_database); } catch { }
        try { File.Delete(_database + "-wal"); } catch { }
        try { File.Delete(_database + "-shm"); } catch { }
    }
}

public sealed class AlertQueueTests
{
    private static DetectionEvent Event(long id) => new() { Id = id, CameraId = Guid.NewGuid(), CameraName = "Test", ImagePath = "none.jpg" };

    [Fact]
    public async Task Queue_DeduplicatesSameEventWhilePending()
    {
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var queue = new AlertQueueService(async (_, _, ct) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task.WaitAsync(ct);
            return "Telegram: đã gửi";
        });
        var first = queue.EnqueueAsync(new AlertChannelSettings(), Event(7));
        var second = queue.EnqueueAsync(new AlertChannelSettings(), Event(7));
        gate.SetResult();
        Assert.Equal("Telegram: đã gửi", await first);
        Assert.Equal("Telegram: đã gửi", await second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Queue_RetriesTransientResult()
    {
        var calls = 0;
        await using var queue = new AlertQueueService((_, _, _) =>
        {
            var attempt = Interlocked.Increment(ref calls);
            return Task.FromResult(attempt < 3 ? "Telegram: lỗi timeout" : "Telegram: đã gửi");
        });
        var result = await queue.EnqueueAsync(new AlertChannelSettings(), Event(8));
        Assert.Equal("Telegram: đã gửi", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Queue_ShutdownCompletesPendingWorkSafely()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new AlertQueueService(async (_, _, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "never";
        });
        var pending = queue.EnqueueAsync(new AlertChannelSettings(), Event(9));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.DisposeAsync();
        var result = await pending;
        Assert.Contains("dừng", result, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MonitorScheduleTests
{
    [Fact]
    public void Schedule_MatchesWeekdayAndTime()
    {
        var settings = new AppSettings();
        settings.MonitorSchedules.Add(new MonitorSchedule { Days = "Weekday", StartTime = "08:00", EndTime = "18:00" });
        Assert.True(MonitorScheduleService.IsMonitoringAllowed(settings, new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.FromHours(7))));
        Assert.False(MonitorScheduleService.IsMonitoringAllowed(settings, new DateTimeOffset(2026, 8, 15, 16, 0, 0, TimeSpan.FromHours(7))));
    }
}
