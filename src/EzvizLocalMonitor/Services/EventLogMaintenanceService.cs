using System.Globalization;
using System.Text;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed record CleanupResult(
    int DeletedEvents,
    int ProcessedLogFiles,
    long FreedBytes,
    int FailedFiles);

public sealed class EventLogMaintenanceService
{
    private static readonly string[] LogFiles =
    {
        DataPaths.AppLogFile,
        DataPaths.CameraLogFile,
        DataPaths.AlertsLogFile,
        DataPaths.AiLogFile,
        DataPaths.ZaloLogFile,
        StartupDiagnostics.LogFilePath
    };

    private readonly EventStore _eventStore;

    public EventLogMaintenanceService(EventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public CleanupResult Cleanup(CleanupRetentionPolicy policy)
    {
        DataPaths.EnsureCreated();
        var purge = policy == CleanupRetentionPolicy.DeleteAll
            ? _eventStore.PurgeAll()
            : _eventStore.PurgeBefore(DateTimeOffset.Now.Subtract(Retention(policy)));

        var processedLogs = 0;
        var failedFiles = 0;
        var freedBytes = purge.FreedBytes;
        foreach (var path in LogFiles.SelectMany(WithRotatedLog).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = policy == CleanupRetentionPolicy.DeleteAll
                    ? DeleteLog(path)
                    : PruneLog(path, DateTimeOffset.Now.Subtract(Retention(policy)));
                if (result.Processed) processedLogs++;
                freedBytes += result.FreedBytes;
            }
            catch
            {
                failedFiles++;
            }
        }

        if (policy == CleanupRetentionPolicy.DeleteAll)
            freedBytes += DeleteOrphanEventFiles();

        return new CleanupResult(purge.DeletedEvents, processedLogs, freedBytes, failedFiles);
    }

    private static TimeSpan Retention(CleanupRetentionPolicy policy) => policy switch
    {
        CleanupRetentionPolicy.KeepOneDay => TimeSpan.FromDays(1),
        CleanupRetentionPolicy.KeepTwoDays => TimeSpan.FromDays(2),
        CleanupRetentionPolicy.KeepOneWeek => TimeSpan.FromDays(7),
        CleanupRetentionPolicy.KeepOneMonth => TimeSpan.FromDays(30),
        _ => TimeSpan.Zero
    };

    private static IEnumerable<string> WithRotatedLog(string path)
    {
        yield return path;
        yield return path + ".1";
    }

    private static (bool Processed, long FreedBytes) DeleteLog(string path)
    {
        if (!File.Exists(path)) return (false, 0);
        var bytes = new FileInfo(path).Length;
        File.Delete(path);
        return (true, bytes);
    }

    private static (bool Processed, long FreedBytes) PruneLog(string path, DateTimeOffset cutoff)
    {
        if (!File.Exists(path)) return (false, 0);
        var originalBytes = new FileInfo(path).Length;
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var kept = lines.Where(line => TryGetTimestamp(line, out var timestamp) && timestamp >= cutoff).ToArray();
        if (kept.Length == lines.Length) return (true, 0);

        var temp = path + ".cleanup.tmp";
        File.WriteAllLines(temp, kept, new UTF8Encoding(false));
        File.Move(temp, path, true);
        var newBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        return (true, Math.Max(0, originalBytes - newBytes));
    }

    private static bool TryGetTimestamp(string line, out DateTimeOffset timestamp)
    {
        var separator = line.IndexOf('\t');
        var value = separator > 0 ? line[..separator] : string.Empty;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }

    private static long DeleteOrphanEventFiles()
    {
        if (!Directory.Exists(DataPaths.EventImages)) return 0;
        long freed = 0;
        foreach (var path in Directory.EnumerateFiles(DataPaths.EventImages, "*", SearchOption.AllDirectories))
        {
            try
            {
                freed += new FileInfo(path).Length;
                File.Delete(path);
            }
            catch { }
        }
        foreach (var directory in Directory.EnumerateDirectories(DataPaths.EventImages, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
        {
            try { Directory.Delete(directory, false); } catch { }
        }
        return freed;
    }
}
