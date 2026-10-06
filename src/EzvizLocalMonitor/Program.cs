using Avalonia;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[1], out var parentPid))
        {
            var restartInTray = args.Any(x => string.Equals(x, "--restart-in-tray", StringComparison.OrdinalIgnoreCase));
            WatchdogService.RunExternal(parentPid, restartInTray);
            return;
        }

        // All normal launches start in the tray; --background remains accepted.
        StartupDiagnostics.Install();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Write("Program.Main", ex);
            Environment.ExitCode = 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
