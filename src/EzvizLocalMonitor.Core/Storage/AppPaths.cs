namespace EzvizLocalMonitor.Services;

public sealed class AppPaths
{
    public AppPaths(string stateRoot, string modelPath, string? logRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        Root = Path.GetFullPath(stateRoot);
        ModelPath = Path.GetFullPath(modelPath);
        LogsRoot = Path.GetFullPath(logRoot ?? Root);
    }

    public string Root { get; }
    public string ModelPath { get; }
    public string LogsRoot { get; }
    public string SettingsFile => Path.Combine(Root, "settings.protected");
    public string MasterKeyFile => Path.Combine(Root, "keys", "master.key");
    public string DatabaseFile => Path.Combine(Root, "events.db");
    public string EventImages => Path.Combine(Root, "Events");
    public string StatusFile => Path.Combine(Root, "status.json");
    public string LockFile => Path.Combine(Root, "daemon.lock");
    public string AppLogFile => Path.Combine(LogsRoot, "app.log");
    public string CameraLogFile => Path.Combine(LogsRoot, "camera.log");
    public string AlertsLogFile => Path.Combine(LogsRoot, "alerts.log");
    public string AiLogFile => Path.Combine(LogsRoot, "ai.log");
    public string ZaloLogFile => Path.Combine(LogsRoot, "zalo-send.log");
    public string StartupLogFile => Path.Combine(LogsRoot, "startup-crash.log");

    public void EnsureDirectories()
    {
        foreach (var path in new[] { Root, EventImages, LogsRoot })
        {
            if (OperatingSystem.IsLinux())
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else
                Directory.CreateDirectory(path);
        }
    }
}
