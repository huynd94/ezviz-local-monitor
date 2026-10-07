using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class AppLogger : IAppLogger
{
    private readonly AppPaths _paths;
    private readonly Action<LogEntry>? _consoleSink;
    private readonly object _sync = new();
    private readonly LogRedactor _redactor = new();
    private volatile bool _loggingEnabled = true;
    private volatile bool _alertLoggingEnabled = true;

    public AppLogger(AppPaths paths, Action<LogEntry>? consoleSink = null) { _paths = paths; _consoleSink = consoleSink; }
    public void Configure(bool loggingEnabled, bool alertLoggingEnabled) { _loggingEnabled = loggingEnabled; _alertLoggingEnabled = alertLoggingEnabled; }
    public void ConfigureSecrets(AppSettings settings) => _redactor.Configure(settings);
    public string Redact(string? value) => _redactor.Redact(value);
    public void Info(LogChannel channel, string message) => Write(channel, "INFO", message);
    public void Error(LogChannel channel, string message, Exception? exception = null)
        => Write(channel, "ERROR", exception is null ? message : $"{message}; exception={exception.GetType().Name}: {exception.Message}");

    public void Write(LogChannel channel, string level, string message)
    {
        if (!_loggingEnabled || (channel == LogChannel.Alerts && !_alertLoggingEnabled)) return;
        var entry = new LogEntry(DateTimeOffset.Now, channel, level, Redact(message));
        try { _consoleSink?.Invoke(entry); } catch { }
        try
        {
            _paths.EnsureDirectories();
            var path = channel switch { LogChannel.Camera => _paths.CameraLogFile, LogChannel.Alerts => _paths.AlertsLogFile, LogChannel.Ai => _paths.AiLogFile, _ => _paths.AppLogFile };
            lock (_sync)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) File.Move(path, path + ".1", true);
                AppendPrivate(path, $"{entry.Timestamp:O}\t{level}\t{entry.Message}{Environment.NewLine}");
            }
        }
        catch { /* Logging never stops monitoring. */ }
    }

    internal static void AppendPrivate(string path, string text)
    {
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
        if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    public async Task<string> ExportDiagnosticsAsync(AppSettings settings, CancellationToken ct = default)
    {
        _paths.EnsureDirectories();
        var output = Path.Combine(_paths.Root, "diagnostics-" + Guid.NewGuid().ToString("N") + ".zip");
        var temporary = Path.Combine(_paths.Root, ".diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var path in new[] { _paths.AppLogFile, _paths.CameraLogFile, _paths.AlertsLogFile, _paths.AiLogFile, _paths.ZaloLogFile, _paths.StartupLogFile })
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(path)) continue;
                var lines = (await File.ReadAllLinesAsync(path, ct)).Select(Redact);
                await File.WriteAllLinesAsync(Path.Combine(temporary, Path.GetFileName(path)), lines, ct);
            }
            var metadata = new { exportedAt = DateTimeOffset.Now, os = Environment.OSVersion.ToString(), cameraCount = settings.Cameras.Count,
                cameras = settings.Cameras.Select(c => new { name = Redact(c.Name), enabled = c.IsEnabled, hasRtsp = !string.IsNullOrWhiteSpace(c.RtspUrl) }),
                telegramEnabled = settings.Alerts.TelegramEnabled, zaloEnabled = settings.Alerts.ZaloEnabled, aiEnabled = settings.Ai.Enabled };
            await File.WriteAllTextAsync(Path.Combine(temporary, "settings-sanitized.json"), JsonSerializer.Serialize(metadata), ct);
            ZipFile.CreateFromDirectory(temporary, output);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return output;
        }
        finally { try { Directory.Delete(temporary, true); } catch { } }
    }
}
