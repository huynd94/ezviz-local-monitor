using System.Runtime.InteropServices;
using System.Text;

namespace EzvizLocalMonitor;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogFilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "EZVIZ Local Monitor", "startup-crash.log");
        }
    }

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                var text = new StringBuilder()
                    .AppendLine($"[{DateTimeOffset.Now:O}] {source}")
                    .AppendLine($"OS={Environment.OSVersion}")
                    .AppendLine($"Runtime={Environment.Version}")
                    .AppendLine(exception?.ToString() ?? "Không có chi tiết exception.")
                    .AppendLine(new string('-', 80))
                    .ToString();
                File.AppendAllText(LogFilePath, text, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostic logging must never become a second crash.
        }
    }

    public static void ShowWindowsError(Exception exception)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                MessageBox(IntPtr.Zero, $"EZVIZ Local Monitor không thể khởi động.\n\nLog: {LogFilePath}\n\n{exception.Message}", "EZVIZ Local Monitor", 0x10);
        }
        catch
        {
            // There may be no desktop available; the log remains the source of truth.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
