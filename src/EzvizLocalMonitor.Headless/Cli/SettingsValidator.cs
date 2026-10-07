using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Headless.Cli;

public static class SettingsValidator
{
    public const int MaximumJsonBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static AppSettings Decode(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaximumJsonBytes) throw new ConfigurationException("Configuration JSON exceeds 1 MiB.");
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new ConfigurationException("Configuration must be a JSON object.");
            Validate(settings);
            return settings;
        }
        catch (JsonException) { throw new ConfigurationException("Configuration JSON is invalid."); }
    }

    public static void Validate(AppSettings settings)
    {
        Require(settings is not null, "Configuration is required.");
        Require(settings!.Cameras is not null && settings.Cameras.Count <= 4, "Configuration allows at most four cameras.");
        Require(settings.Alerts is not null && settings.Ai is not null && settings.MonitorSchedules is not null, "Configuration sections cannot be null.");
        var identities = new HashSet<Guid>();
        foreach (var camera in settings.Cameras!)
        {
            Require(camera is not null, "Camera cannot be null.");
            Require(!string.IsNullOrWhiteSpace(camera!.Name), "Camera name is required.");
            Require(camera.Id != Guid.Empty && identities.Add(camera.Id), "Camera IDs must be non-empty and unique.");
            if (camera.IsEnabled || !string.IsNullOrWhiteSpace(camera.RtspUrl)) UriField(camera.RtspUrl, "Camera RTSP URL", "rtsp", "rtsps");
            if (!string.IsNullOrWhiteSpace(camera.OnvifServiceUrl)) UriField(camera.OnvifServiceUrl, "ONVIF URL", "http", "https");
            Range(camera.ConfidenceThreshold, .30, .90, "Camera confidence");
            Range(camera.CooldownSeconds, 5, 3600, "Camera cooldown");
            Range(camera.MinPresenceSeconds, 0, 30, "Camera presence");
            Range(camera.RoiLeftPercent, 0, 100, "ROI left"); Range(camera.RoiTopPercent, 0, 100, "ROI top");
            Range(camera.RoiRightPercent, camera.RoiLeftPercent, 100, "ROI right");
            Range(camera.RoiBottomPercent, camera.RoiTopPercent, 100, "ROI bottom");
        }
        Range(settings.RetentionDays, 1, 3650, "Retention days");
        Range(settings.InferenceFpsPerCamera, 1, 10, "Inference FPS");
        Range(settings.ConfirmationsRequired, 1, 10, "Required confirmations");
        Range(settings.ConfirmationWindow, 1, 30, "Confirmation window");
        Range(settings.PerformanceProfile, 0, 3, "Performance profile");
        var alerts = settings.Alerts!;
        if (alerts.TelegramEnabled) Credentials(alerts.TelegramBotToken, alerts.TelegramChatId, "Telegram");
        if (alerts.ZaloEnabled) Credentials(alerts.ZaloBotToken, alerts.ZaloChatId, "Zalo");
        if (alerts.ZaloImageRelayEnabled) UriField(alerts.ZaloImageRelayUrl, "Image relay URL", "https");
        var ai = settings.Ai!;
        Range(ai.TimeoutSeconds, 5, 90, "AI timeout");
        if (ai.Enabled)
        {
            UriField(ai.BaseUrl, "AI base URL", "http", "https");
            Credentials(ai.ApiKey, ai.Model, "AI");
        }
        foreach (var schedule in settings.MonitorSchedules!)
        {
            Require(schedule is not null, "Schedule cannot be null.");
            Require(!string.IsNullOrWhiteSpace(schedule!.Name), "Schedule name is required.");
            Require(ValidDays(schedule.Days), "Schedule Days is invalid; use Mon-Sun, weekdays, weekends, or comma-separated day names.");
            Time(schedule.StartTime); Time(schedule.EndTime);
            Range(schedule.PerformanceProfile, 0, 3, "Schedule profile");
        }
        // The indented representation is what SettingsStore actually persists/exports.
        var encoded = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
        try { Require(encoded.Length <= MaximumJsonBytes, "Configuration JSON exceeds 1 MiB."); }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }

    private static bool ValidDays(string? days)
    {
        if (string.IsNullOrWhiteSpace(days)) return false;
        var value = days.Replace(" ", "").ToLowerInvariant();
        if (value is "all" or "mon-sun" or "everyday" or "weekday" or "weekdays" or "weekend" or "weekends") return true;
        return value.Split(',').All(day => day is "mon" or "tue" or "wed" or "thu" or "fri" or "sat" or "sun");
    }
    private static void Time(string? value) => Require(value?.Length == 5 && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "Schedule time must be HH:mm.");
    private static void Credentials(string? secret, string? target, string field) => Require(!string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(target), field + " credentials and target are required when enabled.");
    private static void UriField(string? value, string field, params string[] schemes) => Require(Uri.TryCreate(value, UriKind.Absolute, out var uri) && schemes.Contains(uri.Scheme) && !string.IsNullOrWhiteSpace(uri.Host), field + " has an invalid protocol or host.");
    private static void Range(double value, double min, double max, string field) => Require(double.IsFinite(value) && value >= min && value <= max, field + " is outside its supported range.");
    private static void Require(bool condition, string message) { if (!condition) throw new ConfigurationException(message); }
}
