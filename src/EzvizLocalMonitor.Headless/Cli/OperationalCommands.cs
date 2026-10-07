using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Services;
using OpenCvSharp;

namespace EzvizLocalMonitor.Headless.Cli;

public static class OperationalCommands
{
    public const string ModelHash = "B2BC52F40E8E1C532427D5BDE3575A5D5B571B739FAB2C6DF443733ED1589CBD";

    public static async Task<int> StatusAsync(AppPaths paths, bool json, TextWriter output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var redactor = new LogRedactor();
        try { using var configuration = ProtectedConfiguration.OpenExisting(paths); redactor.Configure(configuration.Settings); }
        catch { /* Status does not require an available encryption key. */ }
        var file = new StatusFile(paths, redactor);
        var result = file.Read(DateTimeOffset.UtcNow);
        if (json)
            await output.WriteLineAsync(result.Snapshot is null ? "{\"available\":false}" : file.SerializeSafe(result.Snapshot));
        else
        {
            await output.WriteLineAsync(result.Snapshot is null ? "Daemon status unavailable." : $"Daemon: {result.Snapshot.State}; heartbeat UTC: {result.Snapshot.UpdatedAtUtc:O}; timezone: {result.Snapshot.TimeZone}; events: {result.Snapshot.EventCount}");
            if (result.Snapshot is not null)
                foreach (var camera in result.Snapshot.Cameras) await output.WriteLineAsync($"  {camera.Name}: {camera.State}; reconnects={camera.Reconnects}; {camera.Message}");
            if (!result.IsFresh) await output.WriteLineAsync(result.Failure);
        }
        return result.IsFresh ? 0 : 4;
    }

    public static async Task<int> DoctorAsync(AppPaths paths, TextWriter output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var configuration = ProtectedConfiguration.OpenExisting(paths);
        CheckNative(paths);
        await output.WriteLineAsync($"PASS: linux-x64; timezone={TimeZoneInfo.Local.Id}; model SHA-256 verified; OpenCV {Cv2.GetVersionString()}, JPEG, FFmpeg and ONNX CPU available.");
        await output.WriteLineAsync($"PASS: protected configuration; configured cameras={configuration.Settings.Cameras.Count}; no camera/network alert requests sent.");
        return 0;
    }

    public static void CheckNative(AppPaths paths)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Native monitoring requires Linux x64.");
        using (var stream = File.OpenRead(paths.ModelPath))
            if (Convert.ToHexString(SHA256.HashData(stream)) != ModelHash) throw new InvalidDataException("Model checksum mismatch.");
        using var frame = new Mat(2, 2, MatType.CV_8UC3, Scalar.Black);
        if (!Cv2.ImEncode(".jpg", frame, out _)) throw new InvalidDataException("JPEG codec unavailable.");
        if (!Regex.IsMatch(Cv2.GetBuildInformation(), @"(?im)^\s*FFMPEG:\s+YES\b")) throw new InvalidDataException("FFmpeg video I/O unavailable.");
        using var detector = new YoloPersonDetector(paths.ModelPath);
    }

    public static async Task<int> TestAlertAsync(AppPaths paths, string channel, TextWriter output, CancellationToken ct, HttpClient? client = null)
    {
        using var configuration = ProtectedConfiguration.OpenExisting(paths);
        var settings = configuration.Settings;
        var logger = new AppLogger(paths);
        logger.Configure(settings.LoggingEnabled, settings.AlertLoggingEnabled);
        logger.ConfigureSecrets(settings);
        var alerts = new AlertDispatcher(paths, logger, client);
        alerts.Diagnostics.Configure(settings.AlertLoggingEnabled);
        var result = await alerts.TestAsync(settings.Alerts, channel, ct);
        await output.WriteLineAsync(logger.Redact(result));
        return result.Contains("đã gửi", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }
}
