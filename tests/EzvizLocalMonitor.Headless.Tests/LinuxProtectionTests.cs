using System.Security.Cryptography;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class LinuxProtectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ezviz-linux-state-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, Path.Combine(_root, "model.onnx"));

    [Fact]
    public void SettingsAreProtectedWithFreshNonceAndPrivateKeyPermissions()
    {
        var key = new MasterKeyStore(Paths).InitializeForNewConfiguration();
        using var protector = new LinuxSettingsProtector(key);
        CryptographicOperations.ZeroMemory(key);
        var store = new SettingsStore(Paths, protector);
        var settings = new AppSettings { ThemeName = "Ocean", Ai = new AiSettings { ApiKey = "YOUR_API_KEY_HERE" } };
        store.Save(settings);
        var original = File.ReadAllBytes(Paths.SettingsFile);
        Assert.DoesNotContain("YOUR_API_KEY_HERE", System.Text.Encoding.UTF8.GetString(original));
        store.Save(settings);
        Assert.False(original.SequenceEqual(File.ReadAllBytes(Paths.SettingsFile)));
        Assert.Equal("Ocean", store.Load().ThemeName);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Paths.MasterKeyFile));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Paths.SettingsFile));
    }

    [Fact]
    public void TamperingAndWrongKeyFailWithoutReplacingExistingData()
    {
        var key = new MasterKeyStore(Paths).InitializeForNewConfiguration();
        using var protector = new LinuxSettingsProtector(key);
        CryptographicOperations.ZeroMemory(key);
        var store = new SettingsStore(Paths, protector);
        store.Save(new AppSettings { ThemeName = "Ocean" });
        var damaged = File.ReadAllBytes(Paths.SettingsFile);
        damaged[^1] ^= 1;
        File.WriteAllBytes(Paths.SettingsFile, damaged);
        Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal(damaged, File.ReadAllBytes(Paths.SettingsFile));
        using var wrong = new LinuxSettingsProtector(new byte[32]);
        Assert.ThrowsAny<CryptographicException>(() => wrong.Unprotect(protector.Protect(new byte[] { 1, 2, 3 })));
    }

    [Fact]
    public void MissingKeyWithExistingSettingsNeverGeneratesReplacement()
    {
        Paths.EnsureDirectories();
        File.WriteAllText(Paths.SettingsFile, "existing protected configuration");
        var keys = new MasterKeyStore(Paths);
        Assert.Throws<InvalidDataException>(() => keys.InitializeForNewConfiguration());
        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.Equal("existing protected configuration", File.ReadAllText(Paths.SettingsFile));
    }

    [Fact]
    public void UnsafeKeyPermissionsOrSymlinkAreRejected()
    {
        var keys = new MasterKeyStore(Paths);
        var key = keys.InitializeForNewConfiguration();
        CryptographicOperations.ZeroMemory(key);
        File.SetUnixFileMode(Paths.MasterKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
        File.Delete(Paths.MasterKeyFile);
        var external = Path.Combine(_root, "external-key");
        File.WriteAllBytes(external, new byte[32]);
        File.CreateSymbolicLink(Paths.MasterKeyFile, external);
        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
    }

    [Theory]
    [InlineData("EZVIZ-LINUX-SETTINGS-V2\n")]
    [InlineData("EZVIZ-LOCAL-TRANSFER-V1\n")]
    [InlineData("EZVIZ-LOCAL-BACKUP-V1\n")]
    public void WrongVersionOrPurposeIsRejectedWithoutChangingSettingsOrKey(string header)
    {
        var key = new MasterKeyStore(Paths).InitializeForNewConfiguration();
        using var protector = new LinuxSettingsProtector(key);
        CryptographicOperations.ZeroMemory(key);
        var keyBefore = File.ReadAllBytes(Paths.MasterKeyFile);
        var invalid = System.Text.Encoding.ASCII.GetBytes(header).Concat(new byte[64]).ToArray();
        File.WriteAllBytes(Paths.SettingsFile, invalid);

        Assert.Throws<InvalidDataException>(() => new SettingsStore(Paths, protector).Load());

        Assert.Equal(invalid, File.ReadAllBytes(Paths.SettingsFile));
        Assert.Equal(keyBefore, File.ReadAllBytes(Paths.MasterKeyFile));
    }

    [Fact]
    public void EveryTruncatedEnvelopePrefixIsRejected()
    {
        using var protector = new LinuxSettingsProtector(new byte[32]);
        var envelope = protector.Protect(new byte[] { 1, 2, 3 });
        for (var length = 0; length < envelope.Length; length++)
        {
            var truncated = envelope[..length];
            if (length < 52)
                Assert.Throws<InvalidDataException>(() => protector.Unprotect(truncated));
            else
                Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(truncated));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(52)]
    public void ModifiedHeaderNonceTagOrCiphertextCannotAuthenticate(int offset)
    {
        using var protector = new LinuxSettingsProtector(new byte[32]);
        var envelope = protector.Protect(new byte[] { 1, 2, 3 });
        envelope[offset] ^= 1;
        if (offset < 24)
            Assert.Throws<InvalidDataException>(() => protector.Unprotect(envelope));
        else
            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(envelope));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    public void NonAes256KeyLengthsAreRejected(int length)
    {
        Assert.Throws<ArgumentException>(() => new LinuxSettingsProtector(new byte[length]));
    }

    [Fact]
    public void ProtectorOwnsKeyCopyAndRejectsUseAfterDisposal()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var reader = new LinuxSettingsProtector(key);
        var writer = new LinuxSettingsProtector(key);
        CryptographicOperations.ZeroMemory(key);
        var plain = new byte[] { 1, 2, 3 };
        var envelope = writer.Protect(plain);
        writer.Dispose();
        writer.Dispose();

        Assert.Equal(plain, reader.Unprotect(envelope));
        Assert.Throws<ObjectDisposedException>(() => writer.Protect(plain));
        Assert.Throws<ObjectDisposedException>(() => writer.Unprotect(envelope));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
