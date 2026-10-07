using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public static class SettingsCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static byte[] Encode(AppSettings settings) => JsonSerializer.SerializeToUtf8Bytes(Normalize(settings), Options);
    public static AppSettings Decode(byte[] plain) => Normalize(JsonSerializer.Deserialize<AppSettings>(plain, Options)
        ?? throw new InvalidDataException("Cấu hình không chứa JSON hợp lệ."));
    public static AppSettings Clone(AppSettings settings) => Decode(JsonSerializer.SerializeToUtf8Bytes(settings, Options));

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Cameras = (settings.Cameras ?? new List<CameraDefinition>()).Where(camera => camera is not null).Take(4).ToList();
        foreach (var camera in settings.Cameras)
        {
            camera.Name ??= "Camera";
            camera.RtspUrl ??= string.Empty;
            camera.OnvifServiceUrl = string.IsNullOrWhiteSpace(camera.OnvifServiceUrl) ? null : camera.OnvifServiceUrl;
            camera.ConfidenceThreshold = Math.Clamp(camera.ConfidenceThreshold, 0.30, 0.90);
            camera.CooldownSeconds = Math.Clamp(camera.CooldownSeconds, 5, 3600);
            camera.MinPresenceSeconds = Math.Clamp(camera.MinPresenceSeconds, 0, 30);
            camera.RoiLeftPercent = Math.Clamp(camera.RoiLeftPercent, 0, 100);
            camera.RoiTopPercent = Math.Clamp(camera.RoiTopPercent, 0, 100);
            camera.RoiRightPercent = Math.Clamp(camera.RoiRightPercent, camera.RoiLeftPercent, 100);
            camera.RoiBottomPercent = Math.Clamp(camera.RoiBottomPercent, camera.RoiTopPercent, 100);
        }
        settings.Alerts ??= new AlertChannelSettings();
        settings.Ai ??= new AiSettings();
        settings.Alerts.TelegramBotToken ??= string.Empty;
        settings.Alerts.TelegramChatId ??= string.Empty;
        settings.Alerts.ZaloBotToken ??= string.Empty;
        settings.Alerts.ZaloChatId ??= string.Empty;
        settings.Alerts.ZaloImageRelayUrl ??= string.Empty;
        settings.Alerts.ZaloImageRelayApiKey ??= string.Empty;
        settings.Ai.BaseUrl ??= "https://api.openai.com/v1";
        settings.Ai.Model ??= "gpt-4o-mini";
        settings.Ai.ApiKey ??= string.Empty;
        settings.RetentionDays = Math.Clamp(settings.RetentionDays, 1, 3650);
        settings.InferenceFpsPerCamera = Math.Clamp(settings.InferenceFpsPerCamera, 1, 10);
        settings.ConfirmationsRequired = Math.Clamp(settings.ConfirmationsRequired, 1, 10);
        settings.ConfirmationWindow = Math.Clamp(settings.ConfirmationWindow, 1, 30);
        settings.DashboardLayoutMode = settings.DashboardLayoutMode is 1 or 2 or 4 ? settings.DashboardLayoutMode : 2;
        settings.PreviewFitMode = Math.Clamp(settings.PreviewFitMode, 0, 1);
        settings.DashboardViewMode = Math.Clamp(settings.DashboardViewMode, 0, 1);
        settings.PerformanceProfile = Math.Clamp(settings.PerformanceProfile, 0, 3);
        settings.IdleLockTimeoutMinutes = settings.IdleLockTimeoutMinutes is 0 or 5 or 10 or 15 or 30 or 60 ? settings.IdleLockTimeoutMinutes : 0;
        settings.MonitorSchedules = (settings.MonitorSchedules ?? new List<MonitorSchedule>()).Where(schedule => schedule is not null).ToList();
        foreach (var schedule in settings.MonitorSchedules)
        {
            schedule.Name ??= "Lịch mới";
            schedule.Days ??= "Mon-Sun";
            schedule.StartTime ??= "00:00";
            schedule.EndTime ??= "23:59";
            schedule.PerformanceProfile = Math.Clamp(schedule.PerformanceProfile, 0, 3);
        }
        settings.ThemeName ??= "Dark/Light";
        return settings;
    }
}
