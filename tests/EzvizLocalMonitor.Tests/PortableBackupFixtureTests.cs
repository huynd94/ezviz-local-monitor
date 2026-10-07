using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class PortableBackupFixtureTests : IDisposable
{
    // Independent Python AESGCM/PBKDF2 vector, also exercised by Windows storage tests.
    private const string Vector = "RVpWSVotTE9DQUwtVFJBTlNGRVItVjEKAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaG7LLrJVoh/NljW6/msmddyvFTWdj1RJKRr0KVGNFCBqTwo1fyB1mYOhp1bMO6f+gxil058QQmLT3wLYcEpzs+aCpJL/TJaOiTQf6J5AOI2rDDQZqR03MBIjZFQL194qoa6caoqIu5LE=";
    private const string Password = "fixture-only-password";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ezviz-portable-" + Guid.NewGuid().ToString("N"));
    private SettingsStore Store => new(new AppPaths(_root, Path.Combine(_root, "unused.onnx")), new RejectingProtector());
    public PortableBackupFixtureTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ImportMatchesIndependentLegacyVectorOnEachPlatform()
    {
        var file = Path.Combine(_root, "legacy.ezviztransfer");
        File.WriteAllBytes(file, Convert.FromBase64String(Vector));
        var settings = Store.ImportTransferBackup(file, Password);
        Assert.Equal("Ocean", settings.ThemeName);
        Assert.True(settings.WatchdogEnabled);
        Assert.Equal(27, settings.RetentionDays);
        Assert.Equal(4, settings.DashboardLayoutMode);
    }

    [Fact]
    public void ExportMatchesIndependentDecoderWithoutPlatformProtector()
    {
        var file = Path.Combine(_root, "export.ezviztransfer");
        Store.ExportTransferBackup(new AppSettings { ThemeName = "Ocean", RetentionDays = 27 }, file, Password);
        var bytes = File.ReadAllBytes(file);
        var header = Encoding.ASCII.GetBytes("EZVIZ-LOCAL-TRANSFER-V1\n");
        Assert.Equal(header, bytes[..24]);
        var key = Rfc2898DeriveBytes.Pbkdf2(Password, bytes.AsSpan(24, 16), 600000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[bytes.Length - 68];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(40, 12), bytes.AsSpan(68), bytes.AsSpan(52, 16), plain, header);
            using var json = JsonDocument.Parse(plain);
            Assert.Equal("Ocean", json.RootElement.GetProperty("ThemeName").GetString());
            Assert.Equal(27, json.RootElement.GetProperty("RetentionDays").GetInt32());
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public void Dispose() => Directory.Delete(_root, true);
    private sealed class RejectingProtector : ISettingsProtector
    {
        public byte[] Protect(byte[] bytes) => throw new InvalidOperationException("Transfer must not use platform settings protection.");
        public byte[] Unprotect(byte[] bytes) => throw new InvalidOperationException("Transfer must not use platform settings protection.");
    }
}
