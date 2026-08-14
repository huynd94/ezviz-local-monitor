using Avalonia;

namespace EzvizLocalMonitor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
