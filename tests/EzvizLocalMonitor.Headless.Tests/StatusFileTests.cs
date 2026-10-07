using System.Text.Json;
using System.Text.Json.Nodes;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class StatusFileTests : IDisposable
{
    private readonly string _root = Path.Combine("/tmp", "ezviz-status-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, "/tmp/model.onnx");
    private static StatusSnapshot Snapshot(DateTimeOffset now) => new(1, "instance-1", ProcessIdentity.Capture(), now,
        "UTC", "1.0", "running", true, [new(Guid.NewGuid(), "Front", CameraConnectionState.Degraded, "offline", 2)], 12);

    [Fact]
    public void MissingStatusReadDoesNotCreateStateOrKey()
    {
        var result = new StatusFile(Paths).Read(DateTimeOffset.UtcNow);
        Assert.False(result.IsFresh);
        Assert.Null(result.Snapshot);
        Assert.NotNull(result.Failure);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("idle", true)]
    [InlineData("outside-schedule", true)]
    [InlineData("stopping", false)]
    [InlineData("stopped", false)]
    [InlineData("faulted", false)]
    public void DaemonFreshnessIsIndependentOfOfflineCamera(string state, bool healthy)
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        var now = DateTimeOffset.UtcNow;
        var status = new StatusFile(Paths);
        status.Write(Snapshot(now) with { State = state });
        var read = status.Read(now);
        Assert.Equal(healthy, read.IsFresh);
        Assert.NotNull(read.Snapshot);
        Assert.Equal(state, read.Snapshot.State);
        Assert.Equal(CameraConnectionState.Degraded, Assert.Single(read.Snapshot.Cameras).State);
        Assert.Equal((UnixFileMode)384, File.GetUnixFileMode(Paths.StatusFile));
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-1, false)]
    public void HeartbeatHasThirtySecondBoundaryAndRejectsFuture(int age, bool fresh)
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        var now = DateTimeOffset.UtcNow;
        var status = new StatusFile(Paths);
        status.Write(Snapshot(now.AddSeconds(-age)));
        Assert.Equal(fresh, status.Read(now).IsFresh);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("instanceId", "\"\"")]
    [InlineData("state", "\"unknown\"")]
    [InlineData("cameras", "null")]
    [InlineData("cameras", "[null]")]
    [InlineData("process", "null")]
    [InlineData("updatedAtUtc", "\"0001-01-01T00:00:00+00:00\"")]
    [InlineData("eventCount", "-1")]
    [InlineData("timeZone", "null")]
    public void MalformedSnapshotCannotBeReportedFresh(string field, string value)
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        var now = DateTimeOffset.UtcNow;
        var status = new StatusFile(Paths);
        var json = JsonNode.Parse(status.SerializeSafe(Snapshot(now)))!.AsObject();
        json[field] = JsonNode.Parse(value);
        AtomicFile.Write(Paths.StatusFile, System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), (UnixFileMode)384);
        var result = status.Read(now);
        Assert.False(result.IsFresh);
        Assert.Null(result.Snapshot);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("null")]
    public void InvalidJsonIsSafeFailure(string text)
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        AtomicFile.Write(Paths.StatusFile, System.Text.Encoding.UTF8.GetBytes(text), (UnixFileMode)384);
        Assert.False(new StatusFile(Paths).Read(DateTimeOffset.UtcNow).IsFresh);
    }

    [Fact]
    public void ReusedPidWithWrongStartTimeIsNotLive()
    {
        var identity = ProcessIdentity.Capture();
        Assert.True(ProcessIdentity.IsAlive(identity));
        Assert.False(ProcessIdentity.IsAlive(identity with { StartedAtUtc = identity.StartedAtUtc.AddSeconds(-1) }));
        Assert.False(ProcessIdentity.IsAlive(identity with { Pid = int.MaxValue }));
        Assert.False(ProcessIdentity.IsAlive(identity with { Pid = 0 }));
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        var now = DateTimeOffset.UtcNow;
        var status = new StatusFile(Paths);
        status.Write(Snapshot(now) with { Process = identity with { StartedAtUtc = identity.StartedAtUtc.AddSeconds(-1) } });
        Assert.False(status.Read(now).IsFresh);
    }

    [Fact]
    public async Task ExitedRealProcessCannotLeaveFreshStatus()
    {
        using var process = System.Diagnostics.Process.Start("sleep", "30")!;
        try
        {
            var identity = ProcessIdentity.Capture(process.Id);
            Assert.True(ProcessIdentity.IsAlive(identity));
            Directory.CreateDirectory(_root, (UnixFileMode)448);
            var now = DateTimeOffset.UtcNow;
            var status = new StatusFile(Paths);
            status.Write(Snapshot(now) with { Process = identity });
            Assert.True(status.Read(now).IsFresh);
            process.Kill();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(ProcessIdentity.IsAlive(identity));
            Assert.False(status.Read(now).IsFresh);
        }
        finally { if (!process.HasExited) process.Kill(); }
    }

    [Fact]
    public void WriteAndTamperedReadRedactAllExportedStringsAndDropUnknownFields()
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        var redactor = new LogRedactor();
        redactor.Configure(new AppSettings { Ai = new() { ApiKey = "known-secret-value" } });
        var status = new StatusFile(Paths, redactor);
        var now = DateTimeOffset.UtcNow;
        var dangerous = "rtsp://user:pass@example.test/live https://api.telegram.org/bot123:secret/send known-secret-value";
        var snapshot = Snapshot(now) with { Cameras = [new(Guid.NewGuid(), dangerous, CameraConnectionState.Failed, dangerous, 0)], Version = dangerous, TimeZone = dangerous, InstanceId = dangerous };
        status.Write(snapshot);
        AssertSafe(File.ReadAllText(Paths.StatusFile));
        var tampered = JsonNode.Parse(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        tampered["settings"] = dangerous;
        AtomicFile.Write(Paths.StatusFile, System.Text.Encoding.UTF8.GetBytes(tampered.ToJsonString()), (UnixFileMode)384);
        var read = status.Read(now);
        Assert.NotNull(read.Snapshot);
        var exported = status.SerializeSafe(read.Snapshot);
        AssertSafe(exported);
        Assert.DoesNotContain("settings", exported);
        AssertSafe(read.Snapshot.Cameras[0].Name);
        AssertSafe(read.Snapshot.Cameras[0].Message);
    }

    private static void AssertSafe(string value)
    {
        Assert.DoesNotContain("user:pass", value);
        Assert.DoesNotContain("123:secret", value);
        Assert.DoesNotContain("known-secret-value", value);
    }

    [Fact]
    public void StatusSymlinkAndUnsafeModesAreRejectedWithoutReadingTarget()
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        var target = Path.Combine(_root, "target");
        File.WriteAllText(target, "sentinel");
        File.CreateSymbolicLink(Paths.StatusFile, target);
        var status = new StatusFile(Paths);
        Assert.False(status.Read(DateTimeOffset.UtcNow).IsFresh);
        Assert.ThrowsAny<IOException>(() => status.Write(Snapshot(DateTimeOffset.UtcNow)));
        Assert.Equal("sentinel", File.ReadAllText(target));
        File.Delete(Paths.StatusFile);
        status.Write(Snapshot(DateTimeOffset.UtcNow));
        File.SetUnixFileMode(Paths.StatusFile, (UnixFileMode)420);
        Assert.False(status.Read(DateTimeOffset.UtcNow).IsFresh);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
