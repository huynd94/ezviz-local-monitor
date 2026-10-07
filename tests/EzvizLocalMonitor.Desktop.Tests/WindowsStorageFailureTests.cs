using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Desktop.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsStorageFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ezviz-storage-tests-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, Path.Combine(_root, "unused.onnx"));
    private static byte[] Header => Encoding.UTF8.GetBytes("EZVIZ-LOCAL-BACKUP-V1\n");

    public WindowsStorageFailureTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void ExportBackupDoesNotRequireWritableApplicationState()
    {
        // A backup destination is independent of the application state directory,
        // as in the shipped Windows exporter. A file blocks directory creation.
        var blockedState = Path.Combine(_root, "blocked-state");
        File.WriteAllText(blockedState, "original state blocker");
        var backups = new WindowsBackupService(new AppPaths(blockedState, Path.Combine(_root, "unused.onnx")));
        var destination = Path.Combine(_root, "export", "settings.ezvizbackup");

        backups.ExportBackup(new AppSettings { ThemeName = "Ocean" }, destination);

        Assert.Equal("Ocean", backups.ImportBackup(destination).ThemeName);
        Assert.Equal("original state blocker", File.ReadAllText(blockedState));
    }

    [Fact]
    public void SettingsExportCanBeDecodedByOriginalDpapiContract()
    {
        var store = new SettingsStore(Paths, new WindowsSettingsProtector());
        store.Save(new AppSettings { ThemeName = "Ocean", WatchdogEnabled = true, RetentionDays = 27 });
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(Paths.SettingsFile),
            Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1"), DataProtectionScope.CurrentUser);
        try
        {
            using var json = JsonDocument.Parse(plain);
            Assert.Equal("Ocean", json.RootElement.GetProperty("ThemeName").GetString());
            Assert.True(json.RootElement.GetProperty("WatchdogEnabled").GetBoolean());
            Assert.Equal(27, json.RootElement.GetProperty("RetentionDays").GetInt32());
            Assert.Equal("Ocean", store.Load().ThemeName);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("EZVIZ-LOCAL-BACKUP-V1\n")]
    [InlineData("EZVIZ-LOCAL-BACKUP-V2\ninvalid")]
    [InlineData("EZVIZ-LOCAL-TRANSFER-V1\ninvalid")]
    public void InvalidBackupHeaderIsRejectedWithoutChangingInput(string content)
    {
        var file = Path.Combine(_root, "invalid.ezvizbackup");
        var original = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(file, original);
        Assert.Throws<InvalidDataException>(() => new WindowsBackupService(Paths).ImportBackup(file));
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidDpapiPayloadIsRejectedWithoutChangingBackup(bool wrongPurpose)
    {
        var file = Path.Combine(_root, "unreadable.ezvizbackup");
        var payload = wrongPurpose
            ? ProtectedData.Protect(Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes("wrong-purpose"), DataProtectionScope.CurrentUser)
            : new byte[] { 1, 2, 3, 4 };
        var original = Header.Concat(payload).ToArray();
        File.WriteAllBytes(file, original);
        var error = Assert.Throws<InvalidOperationException>(() => new WindowsBackupService(Paths).ImportBackup(file));
        Assert.IsType<CryptographicException>(error.InnerException);
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public void DecryptedInvalidConfigurationIsRejectedWithoutChangingBackup(string json)
    {
        var file = Path.Combine(_root, "bad-json.ezvizbackup");
        var original = Header.Concat(ProtectedData.Protect(Encoding.UTF8.GetBytes(json),
            Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-backup-v1"), DataProtectionScope.CurrentUser)).ToArray();
        File.WriteAllBytes(file, original);
        Assert.Throws<InvalidDataException>(() => new WindowsBackupService(Paths).ImportBackup(file));
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void BackupRejectsBlankPathsBeforeWriting(string? path)
    {
        var backups = new WindowsBackupService(Paths);
        Assert.ThrowsAny<ArgumentException>(() => backups.ExportBackup(new AppSettings(), path!));
        Assert.ThrowsAny<ArgumentException>(() => backups.ImportBackup(path!));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void FailedBackupReplacementPreservesOriginalAndRemovesTemporaryFile()
    {
        var file = Path.Combine(_root, "locked.ezvizbackup");
        var backups = new WindowsBackupService(Paths);
        backups.ExportBackup(new AppSettings { ThemeName = "Ocean" }, file);
        var original = File.ReadAllBytes(file);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<UnauthorizedAccessException>(() => backups.ExportBackup(new AppSettings { ThemeName = "Light" }, file));
            Assert.Equal(original, File.ReadAllBytes(file));
            Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
        }
        Assert.Equal("Ocean", backups.ImportBackup(file).ThemeName);
    }

    [Fact]
    public void FailedSettingsReplacementPreservesOriginalAndZerosPlaintext()
    {
        var protector = new ObservingWindowsProtector();
        var store = new SettingsStore(Paths, protector);
        store.Save(new AppSettings { ThemeName = "Ocean" });
        Assert.All(protector.LastPlain!, value => Assert.Equal((byte)0, value));
        var original = File.ReadAllBytes(Paths.SettingsFile);
        using (var locked = new FileStream(Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<UnauthorizedAccessException>(() => store.Save(new AppSettings { ThemeName = "Light" }));
            Assert.Equal(original, File.ReadAllBytes(Paths.SettingsFile));
            Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
            Assert.All(protector.LastPlain!, value => Assert.Equal((byte)0, value));
        }
        Assert.Equal("Ocean", store.Load().ThemeName);
        Assert.All(protector.LastPlain!, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void SettingsDecodeFailureZerosDecryptedBufferAndPreservesFile()
    {
        var original = ProtectedData.Protect(Encoding.UTF8.GetBytes("not json"),
            Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1"), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Paths.SettingsFile, original);
        var protector = new ObservingWindowsProtector();
        Assert.Throws<InvalidDataException>(() => new SettingsStore(Paths, protector).Load());
        Assert.NotEmpty(protector.LastPlain!);
        Assert.All(protector.LastPlain!, value => Assert.Equal((byte)0, value));
        Assert.Equal(original, File.ReadAllBytes(Paths.SettingsFile));
    }

    [Fact]
    public void CorruptSettingsAreNotSilentlyReset()
    {
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(Paths.SettingsFile, original);
        var error = Assert.Throws<InvalidOperationException>(() => new SettingsStore(Paths, new WindowsSettingsProtector()).Load());
        Assert.IsType<CryptographicException>(error.InnerException);
        Assert.Equal(original, File.ReadAllBytes(Paths.SettingsFile));
    }

    // Observe actual DPAPI buffer ownership, without replacing encryption/decryption.
    private sealed class ObservingWindowsProtector : ISettingsProtector
    {
        private readonly WindowsSettingsProtector _inner = new();
        public byte[]? LastPlain { get; private set; }
        public byte[] Protect(byte[] plain) { LastPlain = plain; return _inner.Protect(plain); }
        public byte[] Unprotect(byte[] bytes) { LastPlain = _inner.Unprotect(bytes); return LastPlain; }
    }
}
