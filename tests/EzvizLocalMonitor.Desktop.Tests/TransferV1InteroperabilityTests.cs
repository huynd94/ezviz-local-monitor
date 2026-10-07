using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Desktop.Tests;

public sealed class TransferV1InteroperabilityTests : IDisposable
{
    // Independent Python cryptography 42.0.5 AESGCM/hashlib vector:
    // password fixture-only-password; salt 00..0f; nonce 10..1b;
    // PBKDF2-HMAC-SHA256, 600000 iterations, 32-byte key, header as AAD.
    // Plain JSON: {"ThemeName":"Ocean","WatchdogEnabled":true,"RetentionDays":27,"DashboardLayoutMode":4}
    private const string Fixture = "RVpWSVotTE9DQUwtVFJBTlNGRVItVjEKAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaG7LLrJVoh/NljW6/msmddyvFTWdj1RJKRr0KVGNFCBqTwo1fyB1mYOhp1bMO6f+gxil058QQmLT3wLYcEpzs+aCpJL/TJaOiTQf6J5AOI2rDDQZqR03MBIjZFQL194qoa6caoqIu5LE=";
    private const string Password = "fixture-only-password";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ezviz-transfer-vector-" + Guid.NewGuid().ToString("N"));
    private SettingsStore Store => new(new AppPaths(_root, Path.Combine(_root, "unused.onnx")), new RejectingProtector());
    public TransferV1InteroperabilityTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void ImportsIndependentV1VectorWithoutUsingLocalProtector()
    {
        var file = Path.Combine(_root, "independent.ezviztransfer");
        var original = Convert.FromBase64String(Fixture);
        File.WriteAllBytes(file, original);
        var settings = Store.ImportTransferBackup(file, Password);
        Assert.Equal("Ocean", settings.ThemeName);
        Assert.True(settings.WatchdogEnabled);
        Assert.Equal(27, settings.RetentionDays);
        Assert.Equal(4, settings.DashboardLayoutMode);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void ExportIsReadableByIndependentV1Decoder()
    {
        var file = Path.Combine(_root, "new.ezviztransfer");
        Store.ExportTransferBackup(new AppSettings { ThemeName = "Ocean", WatchdogEnabled = true, RetentionDays = 27, DashboardLayoutMode = 4 }, file, Password);
        var bytes = File.ReadAllBytes(file);
        var header = Encoding.ASCII.GetBytes("EZVIZ-LOCAL-TRANSFER-V1\n");
        Assert.Equal(header, bytes[..24]);
        // Literal wire offsets, independent of SettingsStore and SettingsCodec.
        var key = Rfc2898DeriveBytes.Pbkdf2(Password, bytes.AsSpan(24, 16), 600000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[bytes.Length - 68];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(40, 12), bytes.AsSpan(68), bytes.AsSpan(52, 16), plain, header);
            using var json = JsonDocument.Parse(plain);
            Assert.Equal("Ocean", json.RootElement.GetProperty("ThemeName").GetString());
            Assert.True(json.RootElement.GetProperty("WatchdogEnabled").GetBoolean());
            Assert.Equal(27, json.RootElement.GetProperty("RetentionDays").GetInt32());
            Assert.Equal(4, json.RootElement.GetProperty("DashboardLayoutMode").GetInt32());
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    [Theory]
    [InlineData(-1)] // Wrong password, valid envelope.
    [InlineData(24)] // Salt.
    [InlineData(40)] // Nonce.
    [InlineData(52)] // Authentication tag.
    [InlineData(68)] // Ciphertext.
    public void AuthenticationFailureDoesNotModifyInputOrLocalState(int tamperOffset)
    {
        var file = Path.Combine(_root, "tampered.ezviztransfer");
        var bytes = Convert.FromBase64String(Fixture);
        if (tamperOffset >= 0) bytes[tamperOffset] ^= 1;
        File.WriteAllBytes(file, bytes);
        Assert.Throws<InvalidOperationException>(() => Store.ImportTransferBackup(file, tamperOffset < 0 ? "wrong-fixture-password" : Password));
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void FailedTransferReplacementPreservesOriginalAndCleansTemporaryFile()
    {
        var file = Path.Combine(_root, "locked.ezviztransfer");
        var original = Convert.FromBase64String(Fixture);
        File.WriteAllBytes(file, original);
        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Assert.Throws<IOException>(() => Store.ExportTransferBackup(new AppSettings(), file, Password));
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    private sealed class RejectingProtector : ISettingsProtector
    {
        public byte[] Protect(byte[] plain) => throw new InvalidOperationException("Transfer must not use local encryption.");
        public byte[] Unprotect(byte[] bytes) => throw new InvalidOperationException("Transfer must not use local encryption.");
    }
}
