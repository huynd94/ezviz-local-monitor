namespace EzvizLocalMonitor.Services;

public sealed class ZaloDiagnostics(AppPaths paths, IAppLogger logger)
{
    private readonly object _sync = new();
    private volatile bool _enabled = true;
    public string LogFilePath => paths.ZaloLogFile;
    public void Configure(bool enabled) => _enabled = enabled;
    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}; exception={exception.GetType().Name}: {exception.Message}");
    public static string SanitizeResponse(string? value) => new LogRedactor().Redact(value);
    public static string Mask(string? value, int visiblePrefix = 3) => string.IsNullOrWhiteSpace(value) ? "<empty>" : "***";

    private void Write(string level, string message)
    {
        if (!_enabled) return;
        var sanitized = logger.Redact(message);
        if (level == "ERROR") logger.Error(LogChannel.Alerts, sanitized); else logger.Info(LogChannel.Alerts, sanitized);
        try
        {
            paths.EnsureDirectories();
            lock (_sync)
            {
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > 2 * 1024 * 1024) File.Move(LogFilePath, LogFilePath + ".1", true);
                AppLogger.AppendPrivate(LogFilePath, $"{DateTimeOffset.Now:O}\t{level}\t{sanitized}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
