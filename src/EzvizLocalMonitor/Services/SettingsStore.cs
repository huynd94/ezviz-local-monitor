using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public static class DataPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EZVIZ Local Monitor");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.protected");
    public static readonly string DatabaseFile = Path.Combine(Root, "events.db");
    public static readonly string EventImages = Path.Combine(Root, "Events");
    public static readonly string ZaloLogFile = Path.Combine(Root, "zalo-send.log");
    public static readonly string AppLogFile = Path.Combine(Root, "app.log");
    public static readonly string CameraLogFile = Path.Combine(Root, "camera.log");
    public static readonly string AlertsLogFile = Path.Combine(Root, "alerts.log");
    public static readonly string AiLogFile = Path.Combine(Root, "ai.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(EventImages);
    }
}

public sealed class SettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1");
    private static readonly byte[] BackupEntropy = Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-backup-v1");
    private static readonly byte[] BackupHeader = Encoding.UTF8.GetBytes("EZVIZ-LOCAL-BACKUP-V1\n");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        DataPaths.EnsureCreated();
        if (!File.Exists(DataPaths.SettingsFile)) return new AppSettings();

        try
        {
            var encrypted = File.ReadAllBytes(DataPaths.SettingsFile);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions) ?? new AppSettings());
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Không thể mở cấu hình đã mã hóa. Hãy cấu hình lại ứng dụng bằng đúng tài khoản Windows đã tạo cấu hình.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Cấu hình cục bộ không đúng định dạng JSON.", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        DataPaths.EnsureCreated();
        var plain = JsonSerializer.SerializeToUtf8Bytes(Normalize(settings), JsonOptions);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        var temporary = DataPaths.SettingsFile + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, DataPaths.SettingsFile, true);
    }

    public void ExportBackup(AppSettings settings, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Đường dẫn file backup không được trống.", nameof(filePath));
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var plain = JsonSerializer.SerializeToUtf8Bytes(Normalize(settings), JsonOptions);
        var encrypted = ProtectedData.Protect(plain, BackupEntropy, DataProtectionScope.CurrentUser);
        var temporary = filePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(BackupHeader);
                stream.Write(encrypted);
                stream.Flush(true);
            }
            File.Move(temporary, filePath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public AppSettings ImportBackup(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Đường dẫn file backup không được trống.", nameof(filePath));
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length <= BackupHeader.Length || !bytes.AsSpan(0, BackupHeader.Length).SequenceEqual(BackupHeader))
            throw new InvalidDataException("File backup không đúng định dạng hoặc không được tạo bởi EZVIZ Local Monitor.");

        try
        {
            var encrypted = bytes.AsSpan(BackupHeader.Length).ToArray();
            var plain = ProtectedData.Unprotect(encrypted, BackupEntropy, DataProtectionScope.CurrentUser);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(plain, JsonOptions)
                ?? throw new InvalidDataException("Backup không chứa cấu hình hợp lệ."));
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Không thể giải mã backup. Hãy nhập file được tạo bởi đúng tài khoản Windows đã xuất cấu hình.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Backup không chứa cấu hình JSON hợp lệ.", ex);
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Cameras = (settings.Cameras ?? new List<CameraDefinition>())
            .Where(camera => camera is not null)
            .Take(4)
            .ToList();
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
        settings.MonitorSchedules = (settings.MonitorSchedules ?? new List<MonitorSchedule>())
            .Where(schedule => schedule is not null)
            .ToList();
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
