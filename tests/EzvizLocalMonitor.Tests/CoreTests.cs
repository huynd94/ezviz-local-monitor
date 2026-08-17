using Xunit;
using System.Text.Json;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Tests;

public sealed class SettingsSerializationTests
{
    [Fact]
    public void AppSettings_SerializesAndRestoresAllConfigurationSections()
    {
        var cameraId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var settings = new AppSettings
        {
            Cameras = new List<CameraDefinition>
            {
                new()
                {
                    Id = cameraId,
                    Name = "Cửa trước",
                    RtspUrl = "rtsp://admin:masked@192.168.1.20:554/ch1/main",
                    OnvifServiceUrl = "http://192.168.1.20/onvif",
                    IsEnabled = true,
                    ConfidenceThreshold = 0.72,
                    CooldownSeconds = 45,
                    MinPresenceSeconds = 1.5,
                    RoiLeftPercent = 5,
                    RoiTopPercent = 10,
                    RoiRightPercent = 95,
                    RoiBottomPercent = 90
                }
            },
            Alerts = new AlertChannelSettings
            {
                TelegramEnabled = true,
                TelegramBotToken = "telegram-token",
                TelegramChatId = "telegram-chat",
                ZaloEnabled = true,
                ZaloBotToken = "zalo-token",
                ZaloChatId = "zalo-chat",
                ZaloImageRelayEnabled = true,
                AllowImageRelayOutsideLan = true,
                ZaloImageRelayUrl = "https://relay.example/upload",
                ZaloImageRelayApiKey = "relay-key"
            },
            Ai = new AiSettings
            {
                Enabled = true,
                BaseUrl = "https://ai.example/v1",
                Model = "vision-model",
                ApiKey = "ai-key",
                TimeoutSeconds = 35,
                RequireConfirmationBeforeAlert = true
            },
            RetentionDays = 21,
            InferenceFpsPerCamera = 3,
            ConfirmationsRequired = 2,
            ConfirmationWindow = 4,
            StartWithWindows = true,
            DashboardLayoutMode = 4,
            PreviewFitMode = 1,
            DashboardViewMode = 1,
            HasCompletedOnboarding = true,
            PerformanceProfile = 2,
            DarkTheme = true,
            ThemeName = "Midnight",
            MonitorSchedules = new List<MonitorSchedule>
            {
                new() { Id = scheduleId, Name = "Ban đêm", Days = "Mon-Fri", StartTime = "22:00", EndTime = "06:00", PerformanceProfile = 3 }
            },
            WatchdogEnabled = true,
            LoggingEnabled = false,
            AlertLoggingEnabled = false,
            AutoUpdateEnabled = false,
            IdleLockTimeoutMinutes = 15
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(cameraId, restored!.Cameras.Single().Id);
        Assert.Equal(settings.Cameras[0].Name, restored.Cameras[0].Name);
        Assert.Equal(settings.Cameras[0].RtspUrl, restored.Cameras[0].RtspUrl);
        Assert.Equal(settings.Cameras[0].ConfidenceThreshold, restored.Cameras[0].ConfidenceThreshold);
        Assert.Equal(settings.Cameras[0].MinPresenceSeconds, restored.Cameras[0].MinPresenceSeconds);
        Assert.Equal(settings.Alerts.TelegramBotToken, restored.Alerts.TelegramBotToken);
        Assert.Equal(settings.Alerts.ZaloImageRelayUrl, restored.Alerts.ZaloImageRelayUrl);
        Assert.Equal(settings.Ai.Model, restored.Ai.Model);
        Assert.Equal(settings.Ai.RequireConfirmationBeforeAlert, restored.Ai.RequireConfirmationBeforeAlert);
        Assert.Equal(settings.MonitorSchedules.Single().Id, restored.MonitorSchedules.Single().Id);
        Assert.Equal(settings.MonitorSchedules.Single().StartTime, restored.MonitorSchedules.Single().StartTime);
        Assert.Equal(settings.ThemeName, restored.ThemeName);
        Assert.Equal(settings.WatchdogEnabled, restored.WatchdogEnabled);
        Assert.Equal(settings.LoggingEnabled, restored.LoggingEnabled);
        Assert.Equal(settings.AlertLoggingEnabled, restored.AlertLoggingEnabled);
        Assert.Equal(settings.AutoUpdateEnabled, restored.AutoUpdateEnabled);
        Assert.Equal(settings.IdleLockTimeoutMinutes, restored.IdleLockTimeoutMinutes);
    }
}

public sealed class AppLockPolicyTests
{
    [Fact]
    public void PasswordRequiresEightCharacters()
    {
        AppLockService.Validate(AppLockMode.Password, "safe-pass");
        Assert.Throws<ArgumentException>(() => AppLockService.Validate(AppLockMode.Password, "short"));
    }

    [Fact]
    public void PinRequiresDigitsAndFourToTwelveCharacters()
    {
        AppLockService.Validate(AppLockMode.Pin, "1234");
        Assert.Throws<ArgumentException>(() => AppLockService.Validate(AppLockMode.Pin, "12ab"));
        Assert.Throws<ArgumentException>(() => AppLockService.Validate(AppLockMode.Pin, "123"));
    }
}

public sealed class TransferBackupTests
{
    [Fact]
    public void TransferBackup_RoundTripWorks_AndWrongPasswordFails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ezviz-transfer-{Guid.NewGuid():N}.ezviztransfer");
        try
        {
            var store = new SettingsStore();
            var settings = new AppSettings
            {
                Cameras = new List<CameraDefinition> { new() { Name = "Camera chuyển máy", RtspUrl = "rtsp://local/stream" } },
                Alerts = new AlertChannelSettings { TelegramBotToken = "secret-token", TelegramChatId = "chat-id" },
                Ai = new AiSettings { ApiKey = "secret-ai-key", Model = "vision-model" },
                StartWithWindows = true,
                WatchdogEnabled = true,
                LoggingEnabled = false,
                AutoUpdateEnabled = false
            };

            store.ExportTransferBackup(settings, path, "correct-horse-battery");
            store.ExportTransferBackup(settings, path, "correct-horse-battery");
            var raw = File.ReadAllBytes(path);
            Assert.DoesNotContain("secret-token", System.Text.Encoding.UTF8.GetString(raw));
            var restored = store.ImportTransferBackup(path, "correct-horse-battery");

            Assert.Equal(settings.Cameras[0].Name, restored.Cameras[0].Name);
            Assert.Equal(settings.Alerts.TelegramBotToken, restored.Alerts.TelegramBotToken);
            Assert.Equal(settings.Ai.ApiKey, restored.Ai.ApiKey);
            Assert.True(restored.StartWithWindows);
            Assert.False(restored.AutoUpdateEnabled);
            Assert.Throws<InvalidOperationException>(() => store.ImportTransferBackup(path, "wrong-password"));
            if (OperatingSystem.IsWindows())
            {
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.Throws<IOException>(() => store.ExportTransferBackup(settings, path, "correct-horse-battery"));
            }
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}

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

    [Fact]
    public void EventStore_QueryAndPurgeBefore_RemovesOldEventAndImageOnly()
    {
        var store = new EventStore(_database);
        store.Initialize();
        var root = Path.Combine(Path.GetTempPath(), $"ezviz-event-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var oldImage = Path.Combine(root, "old.jpg");
        var oldBefore = Path.Combine(root, "old_before.jpg");
        var newImage = Path.Combine(root, "new.jpg");
        File.WriteAllBytes(oldImage, new byte[32]);
        File.WriteAllBytes(oldBefore, new byte[16]);
        File.WriteAllBytes(newImage, new byte[24]);
        var now = DateTimeOffset.Now;
        var old = new DetectionEvent { CameraId = Guid.NewGuid(), CameraName = "Old", DetectedAt = now.AddDays(-3), ImagePath = oldImage, DeliveryStatus = "Chưa gửi" };
        var recent = new DetectionEvent { CameraId = Guid.NewGuid(), CameraName = "Recent", DetectedAt = now.AddHours(-2), ImagePath = newImage, DeliveryStatus = "Chưa gửi" };
        old.Id = store.Add(old);
        recent.Id = store.Add(recent);

        Assert.Single(store.Query(now.AddDays(-1), null, 50));
        var result = store.PurgeBefore(now.AddDays(-1));

        Assert.Equal(1, result.DeletedEvents);
        Assert.False(File.Exists(oldImage));
        Assert.False(File.Exists(oldBefore));
        Assert.True(File.Exists(newImage));
        Assert.Single(store.Recent(50));
        try { Directory.Delete(root, true); } catch { }
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
    public async Task Queue_DoesNotRedispatchCompletedEvent()
    {
        var calls = 0;
        await using var queue = new AlertQueueService((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("Telegram: đã gửi");
        });

        Assert.Equal("Telegram: đã gửi", await queue.EnqueueAsync(new AlertChannelSettings(), Event(10)));
        Assert.Equal("Telegram: đã gửi", await queue.EnqueueAsync(new AlertChannelSettings(), Event(10)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Queue_RetriesOnlyMixedFailureWithoutAcceptingDuplicateSuccesses()
    {
        var calls = 0;
        await using var queue = new AlertQueueService((_, _, _) =>
        {
            var attempt = Interlocked.Increment(ref calls);
            return Task.FromResult(attempt == 1
                ? "Telegram text: đã gửi + Telegram photo: lỗi timeout | Zalo text: đã gửi + Zalo photo: chưa gửi; API yêu cầu URL HTTPS công khai"
                : "Telegram text: đã gửi (idempotent) + Telegram photo: đã gửi | Zalo text: đã gửi (idempotent) + Zalo photo: chưa gửi; API yêu cầu URL HTTPS công khai");
        });

        var result = await queue.EnqueueAsync(new AlertChannelSettings(), Event(11));
        Assert.Contains("đã gửi", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, calls);
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
    public async Task Queue_RetriesTelegramPhotoTimeoutWithoutTreatingTextAsFailed()
    {
        var calls = 0;
        await using var queue = new AlertQueueService((_, _, _) =>
        {
            var attempt = Interlocked.Increment(ref calls);
            return Task.FromResult(attempt == 1
                ? "Telegram text: đã gửi + Telegram ảnh: lỗi timeout upload (45 giây)"
                : "Telegram text: đã gửi (idempotent) + Telegram ảnh: đã gửi");
        });

        var result = await queue.EnqueueAsync(new AlertChannelSettings(), Event(12));

        Assert.Contains("Telegram ảnh: đã gửi", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, calls);
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

public sealed class AlertMessagePolicyTests
{
    [Fact]
    public void CaptionForPhoto_ReturnsEmpty_WhenTextSucceeded()
    {
        var result = AlertMessagePolicy.CaptionForPhotoAfterText("caption text", "Telegram: đã gửi");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CaptionForPhoto_ReturnsCaption_WhenTextFailed()
    {
        var result = AlertMessagePolicy.CaptionForPhotoAfterText("caption text", "Telegram: lỗi timeout");
        Assert.Equal("caption text", result);
    }

    [Fact]
    public void CaptionForAlert_IncludesAiSummary_WhenAnalysisCompleted()
    {
        var item = new DetectionEvent
        {
            CameraName = "EZVIZ 192.168.2.2",
            Confidence = 0.84,
            DetectionSource = "YOLO cục bộ",
            IsHumanDetection = true,
            DetectedAt = new DateTimeOffset(2026, 8, 15, 15, 39, 14, TimeSpan.FromHours(7)),
            AiSummary = "Có người đứng cạnh xe máy, cảnh gần như không đổi."
        };

        var caption = AlertMessagePolicy.CaptionForAlert(item);

        Assert.Contains("AI: Có người đứng cạnh xe máy, cảnh gần như không đổi.", caption);
        Assert.Equal(1, caption.Split("AI: ", StringSplitOptions.None).Length - 1);
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
