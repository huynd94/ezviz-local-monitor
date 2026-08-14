using System.Diagnostics;
using System.Text;

namespace EzvizLocalMonitor.Services;

public static class WindowsStartupService
{
    private const string TaskName = "EZVIZ Local Monitor";

    public static void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (!enabled)
            {
                RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            var command = $"/Create /TN \"{TaskName}\" /TR \"\\\"{executable}\"\" /SC ONLOGON /RL LIMITED /F";
            RunSchtasks(command);
            AppLogger.Info(LogChannel.App, "startup task configured without registry");
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "startup task configuration failed", ex);
        }
    }

    private static void RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        });
        process?.WaitForExit(5000);
    }
}

public sealed class WatchdogService : IDisposable
{
    private readonly string _stopMarker = Path.Combine(DataPaths.Root, "watchdog.stop");
    private Process? _watchdogProcess;

    public void Start()
    {
        if (!OperatingSystem.IsWindows() || _watchdogProcess is { HasExited: false }) return;
        DataPaths.EnsureCreated();
        try { File.Delete(_stopMarker); } catch { }
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("--watchdog");
        process.StartInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        if (process.Start())
        {
            _watchdogProcess = process;
            AppLogger.Info(LogChannel.App, "external watchdog started");
        }
    }

    public void Stop()
    {
        DataPaths.EnsureCreated();
        try { File.WriteAllText(_stopMarker, DateTimeOffset.Now.ToString("O")); } catch { }
        try
        {
            if (_watchdogProcess is { HasExited: false }) _watchdogProcess.Kill(entireProcessTree: true);
        }
        catch { }
        _watchdogProcess?.Dispose();
        _watchdogProcess = null;
    }

    public static void RunExternal(int parentPid)
    {
        DataPaths.EnsureCreated();
        var marker = Path.Combine(DataPaths.Root, "watchdog.stop");
        try
        {
            while (true)
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));
                if (File.Exists(marker)) return;
                try
                {
                    using var parent = Process.GetProcessById(parentPid);
                    if (parent.HasExited) break;
                }
                catch (ArgumentException) { break; }
            }

            if (File.Exists(marker)) return;
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                Process.Start(new ProcessStartInfo { FileName = executable, UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
                AppLogger.Info(LogChannel.App, "external watchdog restarted parent after unexpected exit");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "external watchdog failed", ex);
        }
    }

    public void Dispose() => Stop();
}
