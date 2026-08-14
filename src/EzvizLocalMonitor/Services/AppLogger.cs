using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public enum LogChannel
{
    App,
    Camera,
    Alerts,
    Ai
}

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly Dictionary<LogChannel, string> Files = new()
    {
        [LogChannel.App] = DataPaths.AppLogFile,
        [LogChannel.Camera] = DataPaths.CameraLogFile,
        [LogChannel.Alerts] = DataPaths.AlertsLogFile,
        [LogChannel.Ai] = DataPaths.AiLogFile
    };
    private const long MaxBytes = 4 * 1024 * 1024;

    public static void Info(LogChannel channel, string message) => Write(channel, "INFO", message);
    public static void Error(LogChannel channel, string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}; exception={exception.GetType().Name}: {exception.Message}";
        Write(channel, "ERROR", detail);
    }

    public static void Write(LogChannel channel, string level, string message)
    {
        try
        {
            DataPaths.EnsureCreated();
            var path = Files[channel];
            var line = $"{DateTimeOffset.Now:O}\t{level}\t{Redact(message)}{Environment.NewLine}";
            lock (Sync)
            {
                RotateIfNeeded(path);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging is best effort and must never affect monitoring.
        }
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        var text = value.Replace("\r", " ").Replace("\n", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(?i)(token|api[_-]?key|authorization|password|mã xác thực|verification code)\\s*[:=]\\s*[^; ,]+", "$1=<redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(?<![\\w.])(?:10\\.(?:\\d{1,3}\\.){2}\\d{1,3}|192\\.168\\.(?:\\d{1,3}\\.)?\\d{1,3}|172\\.(?:1[6-9]|2\\d|3[0-1])\\.(?:\\d{1,3}\\.)\\d{1,3})(?::\\d+)?", "<lan-ip>");
        return text.Length > 1000 ? text[..1000] + "…" : text;
    }

    public static async Task<string> ExportDiagnosticsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        DataPaths.EnsureCreated();
        var output = Path.Combine(DataPaths.Root, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var temp = Path.Combine(DataPaths.Root, ".diagnostics-work");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var pair in Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = pair.Value;
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(temp, Path.GetFileName(source)), true);
            }
            foreach (var source in new[] { StartupDiagnostics.LogFilePath, DataPaths.ZaloLogFile })
            {
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(temp, Path.GetFileName(source)), true);
            }

            var sanitized = new
            {
                exportedAt = DateTimeOffset.Now,
                app = "EZVIZ Local Monitor",
                os = Environment.OSVersion.VersionString,
                runtime = Environment.Version.ToString(),
                processorCount = Environment.ProcessorCount,
                cameraCount = settings.Cameras.Count,
                cameras = settings.Cameras.Select((camera, index) => new
                {
                    index = index + 1,
                    name = Redact(camera.Name),
                    enabled = camera.IsEnabled,
                    confidence = camera.ConfidenceThreshold,
                    cooldownSeconds = camera.CooldownSeconds,
                    minPresenceSeconds = camera.MinPresenceSeconds,
                    hasRtsp = !string.IsNullOrWhiteSpace(camera.RtspUrl),
                    hasOnvif = !string.IsNullOrWhiteSpace(camera.OnvifServiceUrl)
                }),
                telegramEnabled = settings.Alerts.TelegramEnabled,
                zaloEnabled = settings.Alerts.ZaloEnabled,
                aiEnabled = settings.Ai.Enabled,
                aiModel = Redact(settings.Ai.Model),
                aiTimeoutSeconds = settings.Ai.TimeoutSeconds
            };
            await File.WriteAllTextAsync(Path.Combine(temp, "settings-sanitized.json"), JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(temp, "README.txt"), "Gói chẩn đoán đã loại bỏ token, API key, Chat ID, IP LAN và mã xác thực thiết bị. Không gửi file này nếu chưa kiểm tra nội dung.", cancellationToken);

            if (File.Exists(output)) File.Delete(output);
            ZipFile.CreateFromDirectory(temp, output, CompressionLevel.Fastest, false);
            return output;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= MaxBytes) return;
            var archive = path + ".1";
            if (File.Exists(archive)) File.Delete(archive);
            File.Move(path, archive);
        }
        catch { }
    }
}
