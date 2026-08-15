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
                RunSchtasks("/Delete", "/TN", TaskName, "/F");
                AppLogger.Info(LogChannel.App, "startup task disabled");
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                AppLogger.Error(LogChannel.App, $"startup task skipped; executable missing; path={executable}");
                return;
            }

            // ArgumentList avoids nested-quote parsing problems when the install path contains spaces.
            var taskCommand = $"\"{executable}\" --background";
            var created = RunSchtasks(
                "/Create",
                "/TN", TaskName,
                "/TR", taskCommand,
                "/SC", "ONLOGON",
                "/DELAY", "0000:10",
                "/RL", "LIMITED",
                "/IT",
                "/F");
            AppLogger.Info(LogChannel.App, $"startup task configured; success={created}; executable={Path.GetFileName(executable)}; trigger=ONLOGON+10s; interactive=true");
        }
        catch (Exception ex)
        {
            AppLogger.Error(LogChannel.App, "startup task configuration failed", ex);
        }
    }

    private static bool RunSchtasks(params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info);
        if (process is null) return false;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        if (process.ExitCode != 0)
        {
            AppLogger.Error(LogChannel.App, $"schtasks failed; exitCode={process.ExitCode}; output={output.Trim()}; error={error.Trim()}");
            return false;
        }
        return true;
    }
}

public sealed class WatchdogService : IDisposable
{
    private readonly string _stopMarker = Path.Combine(DataPaths.Root, "watchdog.stop");
    private Process? _watchdogProcess;

    public void Start(bool restartInTray = false)
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
        if (restartInTray) process.StartInfo.ArgumentList.Add("--restart-in-tray");
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

    public static void RunExternal(int parentPid, bool restartInTray)
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
                var restart = new ProcessStartInfo { FileName = executable, UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
                if (restartInTray) restart.ArgumentList.Add("--background");
                Process.Start(restart);
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
