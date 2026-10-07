using System.Diagnostics;
using System.Text.Json;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Services;
using Xunit;
using Xunit.Abstractions;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class DaemonProcessTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    public DaemonProcessTests(ITestOutputHelper output) => _output = output;
    private readonly string _root = Path.Combine("/tmp", "ezviz-daemon-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, Path.Combine(_root, "unused.onnx"), Path.Combine(_root, "logs"));

    [Fact]
    public async Task IdleDaemonPublishesStatusKeepsLockAndStopsCleanlyOnSigterm()
    {
        Assert.Equal(0, (await Command(["configure", "--stdin", "--data-dir", _root], "{\"Cameras\":[]}")).Code);
        using var daemon = Start(["run", "--data-dir", _root]);
        var errors = daemon.StandardError.ReadToEndAsync();
        var stdout = daemon.StandardOutput.ReadToEndAsync();
        try
        {
            var ready = await Ready(daemon);
            Assert.Equal("idle", ready.State);
            Assert.Equal(daemon.Id, ready.Process.Pid);
            var status = await Command(["status", "--json", "--data-dir", _root]);
            Assert.Equal(0, status.Code);
            using (var json = JsonDocument.Parse(status.Output)) Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(3, (await Command(["run", "--data-dir", _root])).Code);
            Assert.Equal(3, (await Command(["configure", "--stdin", "--data-dir", _root], "{\"Cameras\":[]}")).Code);
            await Terminate(daemon);
            Assert.Equal(0, daemon.ExitCode);
            var final = new StatusFile(Paths).Read(DateTimeOffset.UtcNow);
            Assert.False(final.IsFresh);
            Assert.Equal("stopped", final.Snapshot!.State);
            using var released = StateDirectoryLock.Acquire(Paths);
        }
        finally
        {
            if (!daemon.HasExited) { daemon.Kill(); await daemon.WaitForExitAsync(); }
            await Task.WhenAll(errors, stdout);
            _output.WriteLine(await errors);
        }
    }

    [Fact]
    public async Task RestartPublishesANewInstanceAndOutsideScheduleIsHealthy()
    {
        var config = "{\"Cameras\":[],\"MonitorSchedules\":[{\"IsEnabled\":false}]}";
        Assert.Equal(0, (await Command(["configure", "--stdin", "--data-dir", _root], config)).Code);
        string? previous = null;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            using var daemon = Start(["run", "--data-dir", _root]);
            var stderr = daemon.StandardError.ReadToEndAsync();
            var stdout = daemon.StandardOutput.ReadToEndAsync();
            try
            {
                var ready = await Ready(daemon);
                Assert.Equal("outside-schedule", ready.State);
                Assert.NotEqual(previous, ready.InstanceId);
                previous = ready.InstanceId;
                await Terminate(daemon);
                Assert.Equal(0, daemon.ExitCode);
            }
            finally
            {
                if (!daemon.HasExited) { daemon.Kill(); await daemon.WaitForExitAsync(); }
                await Task.WhenAll(stderr, stdout);
            }
        }
    }

    [Fact]
    public async Task MissingKeyOrConfigurationReturnsExitTwoWithoutReplacement()
    {
        Assert.Equal(2, (await Command(["run", "--data-dir", _root])).Code);
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
        Assert.Equal(0, (await Command(["configure", "--stdin", "--data-dir", _root], "{\"Cameras\":[]}")).Code);
        var original = File.ReadAllBytes(Paths.SettingsFile);
        File.Delete(Paths.MasterKeyFile);
        Assert.Equal(2, (await Command(["run", "--data-dir", _root])).Code);
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.Equal(original, File.ReadAllBytes(Paths.SettingsFile));
    }

    private async Task<StatusSnapshot> Ready(Process process)
    {
        var deadline = Stopwatch.StartNew();
        StatusReadResult? last = null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (process.HasExited) throw new InvalidOperationException("Daemon exited before readiness: " + process.ExitCode);
            var result = new StatusFile(Paths).Read(DateTimeOffset.UtcNow);
            last = result;
            if (result.IsFresh && result.Snapshot!.Process.Pid == process.Id) return result.Snapshot;
            await Task.Delay(25);
        }
        throw new TimeoutException($"No fresh daemon readiness snapshot. Last={last?.Failure}; state={last?.Snapshot?.State}; pid={last?.Snapshot?.Process.Pid}; start={last?.Snapshot?.Process.StartedAtUtc:O}; actual={process.StartTime.ToUniversalTime():O}");
    }

    private static async Task Terminate(Process process)
    {
        var start = new ProcessStartInfo("kill") { UseShellExecute = false };
        start.ArgumentList.Add("-TERM"); start.ArgumentList.Add(process.Id.ToString());
        using var signal = Process.Start(start)!;
        await signal.WaitForExitAsync();
        Assert.Equal(0, signal.ExitCode);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
    }

    private static Process Start(string[] args)
    {
        var executable = Environment.GetEnvironmentVariable("EZVIZ_HEADLESS_EXE");
        Assert.True(!string.IsNullOrWhiteSpace(executable) && File.Exists(executable), "Set EZVIZ_HEADLESS_EXE to the built Linux executable.");
        var start = new ProcessStartInfo(executable!) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Remove("DISPLAY"); start.Environment.Remove("WAYLAND_DISPLAY");
        return Process.Start(start)!;
    }

    private static async Task<(int Code, string Output, string Error)> Command(string[] args, string? stdin = null)
    {
        using var process = Start(args);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (stdin is not null) await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return (process.ExitCode, await stdout, await stderr);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
