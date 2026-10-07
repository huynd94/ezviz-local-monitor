using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzvizLocalMonitor.Headless.Hosting;

public static class RunCommand
{
    public static async Task<int> ExecuteAsync(AppPaths paths, CancellationToken ct)
    {
        using var stateLock = StateDirectoryLock.Acquire(paths);
        using var configuration = ProtectedConfiguration.OpenExisting(paths);
        OperationalCommands.CheckNative(paths);
        var settings = configuration.Settings;
        var logger = new AppLogger(paths, entry => Console.Error.WriteLine($"{entry.Timestamp:O}\t{entry.Level}\t{entry.Message}"));
        logger.Configure(settings.LoggingEnabled, settings.AlertLoggingEnabled);
        logger.ConfigureSecrets(settings);
        var store = new EventStore(paths);
        store.Initialize();
        var alerts = new AlertDispatcher(paths, logger);
        alerts.Diagnostics.Configure(settings.AlertLoggingEnabled);
        var files = new ActiveEventFiles();
        var runtime = new MonitoringRuntime(() => new MonitorCoordinator(paths, store, alerts, logger, files), TimeProvider.System, TimeZoneInfo.Local, logger);
        var redactor = new LogRedactor();
        redactor.Configure(settings);
        var status = new StatusFile(paths, redactor);
        var retention = new CompletedEventRetention(paths, store, files, logger);
        var exit = new DaemonExitState();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, Args = [] });
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddHostedService(provider => new MonitorWorker(runtime, settings, status, retention, logger, exit,
            provider.GetRequiredService<IHostApplicationLifetime>()));
        using var host = builder.Build();
        try { await host.RunAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        return exit.Code;
    }
}
