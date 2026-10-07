using System.Diagnostics;
using System.Text;

namespace EzvizLocalMonitor.Services;

public sealed class WindowsStartupService(IAppLogger logger)
{
    private const string TaskName = "EZVIZ Local Monitor";

    public bool Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (!enabled)
            {
                var deleted = RunSchtasks("/Delete", "/TN", TaskName, "/F");
                logger.Info(LogChannel.App, $"startup task disabled; success={deleted}");
                return deleted;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                logger.Error(LogChannel.App, $"startup task skipped; executable missing; path={executable}");
                return false;
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
            logger.Info(LogChannel.App, $"startup task configured; success={created}; taskName={TaskName}; executable={executable}; trigger=ONLOGON+10s; interactive=true");
            return created;
        }
        catch (Exception ex)
        {
            logger.Error(LogChannel.App, "startup task configuration failed", ex);
            return false;
        }
    }

    private bool RunSchtasks(params string[] arguments)
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
            logger.Error(LogChannel.App, $"schtasks failed; exitCode={process.ExitCode}; output={output.Trim()}; error={error.Trim()}");
            return false;
        }
        return true;
    }
}

public sealed class WatchdogService : IDisposable
{
    private readonly string _stopMarker;
    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;
    public WatchdogService(AppPaths paths, IAppLogger logger) { _paths = paths; _logger = logger; _stopMarker = Path.Combine(paths.Root, "watchdog.stop"); }
    private Process? _watchdogProcess;

    public void Start(bool restartInTray = false)
    {
        if (!OperatingSystem.IsWindows() || _watchdogProcess is { HasExited: false }) return;
        _paths.EnsureDirectories();
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
            _logger.Info(LogChannel.App, "external watchdog started");
        }
    }

    public void Stop()
    {
        _paths.EnsureDirectories();
        try { File.WriteAllText(_stopMarker, DateTimeOffset.Now.ToString("O")); } catch { }
        try
        {
            if (_watchdogProcess is { HasExited: false }) _watchdogProcess.Kill(entireProcessTree: true);
        }
        catch { }
        _watchdogProcess?.Dispose();
        _watchdogProcess = null;
    }

    public static void RunExternal(int parentPid, bool restartInTray, AppPaths paths, IAppLogger logger)
    {
        paths.EnsureDirectories();
        var marker = Path.Combine(paths.Root, "watchdog.stop");
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
                logger.Info(LogChannel.App, "external watchdog restarted parent after unexpected exit");
            }
        }
        catch (Exception ex)
        {
            logger.Error(LogChannel.App, "external watchdog failed", ex);
        }
    }

    public void Dispose() => Stop();
}
