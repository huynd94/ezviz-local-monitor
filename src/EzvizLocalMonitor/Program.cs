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
            WatchdogService.RunExternal(parentPid);
            return;
        }

        StartupDiagnostics.Install();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Write("Program.Main", ex);
            StartupDiagnostics.ShowWindowsError(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
