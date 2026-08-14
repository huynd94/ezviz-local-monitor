using System.Text;

namespace EzvizLocalMonitor.Services;

public static class ZaloDiagnostics
{
    private static readonly object Sync = new();
    private static int PendingWrites;
    private const int MaxPendingWrites = 128;
    private const long MaxLogBytes = 2 * 1024 * 1024;

    public static string LogFilePath => DataPaths.ZaloLogFile;

    public static void Info(string message) => Write("INFO", SanitizeResponse(message));

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception is null
            ? message
            : $"{message}; exception={exception.GetType().Name}: {SanitizeResponse(exception.Message)}";
        Write("ERROR", SanitizeResponse(detail));
    }

    public static string SanitizeResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "<empty>";
        var text = response.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 500 ? text : text[..500] + "…";
    }

    public static string Mask(string? value, int visiblePrefix = 3)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        var trimmed = value.Trim();
        if (trimmed.Length <= visiblePrefix) return "***";
        return trimmed[..visiblePrefix] + "***";
    }

    private static void Write(string level, string message)
    {
        AppLogger.Write(LogChannel.Alerts, level, message);
        if (Interlocked.Increment(ref PendingWrites) > MaxPendingWrites)
        {
            Interlocked.Decrement(ref PendingWrites);
            return;
        }

        var line = $"{DateTimeOffset.Now:O}\t{level}\t{SanitizeResponse(message)}{Environment.NewLine}";
        _ = Task.Run(() =>
        {
            try
            {
                DataPaths.EnsureCreated();
                lock (Sync)
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never interrupt camera monitoring, alert delivery or UI.
            }
            finally
            {
                Interlocked.Decrement(ref PendingWrites);
            }
        });
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath) || new FileInfo(LogFilePath).Length <= MaxLogBytes) return;
        var archive = LogFilePath + ".1";
        try { File.Delete(archive); } catch { }
        try { File.Move(LogFilePath, archive); } catch { }
    }
}
