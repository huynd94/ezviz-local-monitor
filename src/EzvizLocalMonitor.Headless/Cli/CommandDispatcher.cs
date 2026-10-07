using System.Reflection;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Cli;

public static class CommandDispatcher
{
    public static string Version => typeof(CommandDispatcher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CommandDispatcher).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static async Task<int> ExecuteAsync(string[] args, TextReader input, TextWriter output, TextWriter error, CancellationToken ct)
    {
        try
        {
            var request = CommandRequest.Parse(args);
            if (request.Command == "help") { await output.WriteLineAsync(Help); return 0; }
            if (request.Command == "version") { await output.WriteLineAsync($"ezviz-headless {Version} linux-x64"); return 0; }
            if (!OperatingSystem.IsLinux()) throw new ConfigurationException("Headless commands require Linux.");
            if (LinuxUser.IsRoot) throw new ConfigurationException("Run headless commands as the service user, not root.");
            var root = request.Get("--data-dir") ?? "/var/lib/ezviz-local-monitor";
            var paths = new AppPaths(root, Path.Combine(AppContext.BaseDirectory, "Models", "yolov8n.onnx"), Path.Combine(Path.GetFullPath(root), "logs"));
            var interactive = ReferenceEquals(input, Console.In) && !Console.IsInputRedirected;
            switch (request.Command)
            {
                case "configure": await ConfigurationCommands.ConfigureAsync(paths, request.Has("--stdin"), input, output, interactive, ct); return 0;
                case "config validate": ConfigurationCommands.Validate(paths); await output.WriteLineAsync("Configuration is valid; no cameras or alerts were contacted."); return 0;
                case "backup import": await BackupCommands.ImportAsync(paths, request.Path!, request.Has("--password-stdin"), input, output, interactive, ct); return 0;
                case "backup export": await BackupCommands.ExportAsync(paths, request.Path!, request.Has("--password-stdin"), input, output, interactive, ct); return 0;
                case "status": return await OperationalCommands.StatusAsync(paths, request.Has("--json"), output, ct);
                case "doctor": return await OperationalCommands.DoctorAsync(paths, output, ct);
                case "alerts test": return await OperationalCommands.TestAlertAsync(paths, request.Get("--channel")!, output, ct);
                case "run": return await RunCommand.ExecuteAsync(paths, ct);
                default: throw new ConfigurationException("Unsupported command.");
            }
        }
        catch (StateInUseException) { await error.WriteLineAsync("State is in use. Stop ezviz-local-monitor before changing its configuration or starting another daemon."); return 3; }
        catch (ConfigurationException ex) { await error.WriteLineAsync(new LogRedactor().Redact(ex.Message)); return 2; }
        catch (ArgumentException) { await error.WriteLineAsync("Invalid command, path or configuration argument."); return 2; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { await error.WriteLineAsync("Command cancelled."); return 1; }
        catch (Exception ex) { await error.WriteLineAsync($"Operation failed ({ex.GetType().Name}); check permissions, state and native dependencies."); return 1; }
    }

    private const string Help = """
        ezviz-headless: Ubuntu 24.04 x64 monitoring via SSH
          configure [--stdin] [--data-dir PATH]
          config validate [--data-dir PATH]
          backup import PATH [--password-stdin] [--data-dir PATH]
          backup export PATH [--password-stdin] [--data-dir PATH]
          status [--json] [--data-dir PATH]
          doctor [--data-dir PATH]
          alerts test --channel telegram|zalo [--data-dir PATH]
          run [--data-dir PATH]
          version
        Run as the service user. Secret values are read from hidden prompts or stdin.
        Configure --stdin reads AppSettings JSON. Backup export stdin reads password
        and confirmation on two lines; import reads one password line.
        """;
}
