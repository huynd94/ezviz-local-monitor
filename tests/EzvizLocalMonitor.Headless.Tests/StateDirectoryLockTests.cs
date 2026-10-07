using System.Diagnostics;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class StateDirectoryLockTests : IDisposable
{
    private readonly string _root = Path.Combine("/tmp", "ezviz-lock-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, "/tmp/model.onnx");

    [Fact]
    public void LockCreatesOnlyPrivateRootAndLockAndDoesNotUnlinkOnDispose()
    {
        using (StateDirectoryLock.Acquire(Paths))
        {
            Assert.Equal((UnixFileMode)448, File.GetUnixFileMode(_root));
            Assert.Equal((UnixFileMode)384, File.GetUnixFileMode(Paths.LockFile));
            Assert.Equal(new[] { Paths.LockFile }, Directory.GetFileSystemEntries(_root));
        }
        Assert.True(File.Exists(Paths.LockFile));
        using var again = StateDirectoryLock.Acquire(Paths);
    }

    [Fact]
    public void IndependentOpenInSameProcessCannotAcquireHeldLock()
    {
        using var first = StateDirectoryLock.Acquire(Paths);
        Assert.Throws<StateInUseException>(() => StateDirectoryLock.Acquire(Paths));
    }

    [Fact]
    public async Task RealProcessLockContentionReleaseAndCrashRecovery()
    {
        using var child = StartChild();
        try
        {
            Assert.Equal("LOCKED", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.Throws<StateInUseException>(() => StateDirectoryLock.Acquire(Paths));
            await child.StandardInput.WriteLineAsync("release");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, child.ExitCode);
            using (StateDirectoryLock.Acquire(Paths)) { }
            using var crashed = StartChild();
            try
            {
                Assert.Equal("LOCKED", await crashed.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
                crashed.Kill();
                await crashed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                using var recovered = StateDirectoryLock.Acquire(Paths);
            }
            finally { if (!crashed.HasExited) crashed.Kill(); }
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    private Process StartChild()
    {
        var dll = Environment.GetEnvironmentVariable("EZVIZ_TEST_CHILD_PATH");
        Assert.True(!string.IsNullOrWhiteSpace(dll) && File.Exists(dll), "Build TestChild and set EZVIZ_TEST_CHILD_PATH before running lock tests.");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.ArgumentList.Add(dll!);
        start.ArgumentList.Add("hold-lock");
        start.ArgumentList.Add(_root);
        return Process.Start(start)!;
    }

    [Theory]
    [InlineData("root", 493)]
    [InlineData("root", 1472)]
    [InlineData("lock", 420)]
    [InlineData("lock", 256)]
    public void UnsafeModesAreRejectedWithoutRepair(string target, int mode)
    {
        Directory.CreateDirectory(_root, (UnixFileMode)448);
        File.WriteAllText(Paths.LockFile, "unchanged");
        File.SetUnixFileMode(Paths.LockFile, (UnixFileMode)384);
        var path = target == "root" ? _root : Paths.LockFile;
        File.SetUnixFileMode(path, (UnixFileMode)mode);
        Assert.ThrowsAny<IOException>(() => StateDirectoryLock.Acquire(Paths));
        Assert.Equal((UnixFileMode)mode, File.GetUnixFileMode(path));
        Assert.Equal("unchanged", File.ReadAllText(Paths.LockFile));
    }

    [Theory]
    [InlineData("root", false)]
    [InlineData("root", true)]
    [InlineData("lock", false)]
    [InlineData("lock", true)]
    public void ExistingAndDanglingSymlinksAreRejected(string target, bool dangling)
    {
        var outer = _root + "-outer";
        Directory.CreateDirectory(outer, (UnixFileMode)448);
        try
        {
            var destination = Path.Combine(outer, "destination");
            if (target == "root")
            {
                if (!dangling) Directory.CreateDirectory(destination, (UnixFileMode)448);
                Directory.CreateSymbolicLink(_root, destination);
            }
            else
            {
                Directory.CreateDirectory(_root, (UnixFileMode)448);
                if (!dangling) File.WriteAllText(destination, "sentinel");
                File.CreateSymbolicLink(Paths.LockFile, destination);
            }
            Assert.ThrowsAny<IOException>(() => StateDirectoryLock.Acquire(Paths));
            Assert.False(File.Exists(Paths.MasterKeyFile));
            Assert.False(File.Exists(Paths.SettingsFile));
            if (target == "lock" && !dangling) Assert.Equal("sentinel", File.ReadAllText(destination));
        }
        finally
        {
            if (target == "root") File.Delete(_root);
            Directory.Delete(outer, true);
        }
    }

    [Fact]
    public void DirectoryAtLockPathIsIoErrorNotContention()
    {
        Directory.CreateDirectory(Paths.LockFile, (UnixFileMode)448);
        File.SetUnixFileMode(_root, (UnixFileMode)448);
        var error = Assert.ThrowsAny<IOException>(() => StateDirectoryLock.Acquire(Paths));
        Assert.IsNotType<StateInUseException>(error);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
