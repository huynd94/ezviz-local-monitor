using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using OpenCvSharp;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class MonitoringRuntimeTests
{
    [Fact]
    public async Task DisabledScheduleStaysStoppedAndManualOverrideCanStart()
    {
        var runtime = Runtime();
        var settings = new AppSettings { Cameras = [new() { RtspUrl = "rtsp://fixture/stream" }], MonitorSchedules = [new() { IsEnabled = false }] };
        await runtime.StartAsync(settings, false, CancellationToken.None);
        Assert.False(runtime.Snapshot.IsMonitoring);
        Assert.False(runtime.Snapshot.ScheduleAllowed);
        await runtime.SetManualMonitoringAsync(true, CancellationToken.None);
        Assert.True(runtime.Snapshot.IsMonitoring);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task EffectiveSettingsNeverMutateStoredConfiguration()
    {
        var runtime = Runtime();
        var settings = new AppSettings { Cameras = [new() { RtspUrl = "rtsp://fixture/stream" }], ConfirmationsRequired = 10, ConfirmationWindow = 30 };
        await runtime.StartAsync(settings, false, CancellationToken.None);
        Assert.True(runtime.Snapshot.IsMonitoring);
        Assert.Equal(10, settings.ConfirmationsRequired);
        Assert.Equal(30, settings.ConfirmationWindow);
        Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None)).Completed);
    }

    [Fact]
    public void OvernightScheduleUsesStartDayAndExplicitTimezone()
    {
        var settings = new AppSettings { MonitorSchedules = [new() { Days = "Mon", StartTime = "22:00", EndTime = "02:00" }] };
        Assert.True(MonitorScheduleService.IsMonitoringAllowed(settings, DateTimeOffset.Parse("2026-10-06T01:00:00Z"), TimeZoneInfo.Utc));
        Assert.False(MonitorScheduleService.IsMonitoringAllowed(settings, DateTimeOffset.Parse("2026-10-06T03:00:00Z"), TimeZoneInfo.Utc));
    }

    private static MonitoringRuntime Runtime() => new(() => new Session(), TimeProvider.System, TimeZoneInfo.Utc, new Logger());
    [Fact]
    public void ExplicitTimezoneIsUsedInsteadOfHostLocalClock()
    {
        var settings = new AppSettings { MonitorSchedules = [new() { Days = "Mon", StartTime = "22:00", EndTime = "23:00" }] };
        var instant = DateTimeOffset.Parse("2026-10-05T15:30:00Z");
        Assert.True(MonitorScheduleService.IsMonitoringAllowed(settings, instant, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")));
        Assert.False(MonitorScheduleService.IsMonitoringAllowed(settings, instant, TimeZoneInfo.Utc));
    }
    private sealed class Logger : IAppLogger { public void Info(LogChannel c, string m) { } public void Error(LogChannel c, string m, Exception? e = null) { } }
    private sealed class Session : IMonitoringSession
    {
        public event Action<Guid, string>? CameraStatusChanged;
        public event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged;
        public event Action<Guid, Mat>? PreviewReady;
        public event Action<DetectionEvent>? EventRecorded;
        public Task StartAsync(AppSettings settings, CancellationToken ct)
        {
            Assert.InRange(settings.ConfirmationsRequired, 1, settings.ConfirmationWindow);
            Assert.InRange(settings.ConfirmationWindow, 1, 5);
            return Task.CompletedTask;
        }
        public void SetPreviewEnabled(bool enabled) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
