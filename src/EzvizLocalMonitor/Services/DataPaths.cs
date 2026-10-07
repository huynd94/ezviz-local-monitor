namespace EzvizLocalMonitor.Services;

// Desktop-only paths; shared services receive AppPaths explicitly.
public static class DataPaths
{
    public static readonly AppPaths Paths = WindowsAppPaths.Create(AppContext.BaseDirectory);
    public static string Root => Paths.Root;
    public static string SettingsFile => Paths.SettingsFile;
    public static string DatabaseFile => Paths.DatabaseFile;
    public static string EventImages => Paths.EventImages;
    public static string ZaloLogFile => Paths.ZaloLogFile;
    public static string AppLogFile => Paths.AppLogFile;
    public static string CameraLogFile => Paths.CameraLogFile;
    public static string AlertsLogFile => Paths.AlertsLogFile;
    public static string AiLogFile => Paths.AiLogFile;
    public static void EnsureCreated() => Paths.EnsureDirectories();
}
