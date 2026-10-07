using System.Security.Cryptography;
using System.Text;
using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class ConfigurationBackupTests : IDisposable
{
    private readonly string root = Path.Combine("/tmp", "ezviz-backup-cli-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(Path.Combine(root, "state"), Path.Combine(root, "model.onnx"));
    private string Backup => Path.Combine(root, "backup.ezviztransfer");
    private Task Invoke(string command, AppPaths paths, string password) => command == "Import"
        ? BackupCommands.ImportAsync(paths, Backup, true, new StringReader(password), new StringWriter(), false, CancellationToken.None)
        : BackupCommands.ExportAsync(paths, Backup, true, new StringReader(password), new StringWriter(), false, CancellationToken.None);

    private void CreateTransfer()
    {
        Directory.CreateDirectory(root, (UnixFileMode)448);
        using var protector = new LinuxSettingsProtector(new byte[32]);
        new SettingsStore(Paths, protector).ExportTransferBackup(new AppSettings { ThemeName = "Ocean" }, Backup, "fixture-password");
    }

    [Fact]
    public async Task WrongPasswordDoesNotCreateProtectedState()
    {
        CreateTransfer();
        await Assert.ThrowsAsync<ConfigurationException>(() => Invoke("Import", Paths, "wrong-password\n"));
        Assert.False(File.Exists(Paths.SettingsFile));
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Fact]
    public async Task ImportExportRoundTripUsesPrivateEncryptedTransfer()
    {
        CreateTransfer();
        await Invoke("Import", Paths, "fixture-password\n");
        Assert.Equal("Ocean", ConfigurationCommands.Validate(Paths).ThemeName);
        await Invoke("Export", Paths, "second-password\nsecond-password\n");
        Assert.Equal((UnixFileMode)384, File.GetUnixFileMode(Backup));
        using var protector = new LinuxSettingsProtector(new byte[32]);
        Assert.Equal("Ocean", new SettingsStore(Paths, protector).ImportTransferBackup(Backup, "second-password").ThemeName);
        Assert.DoesNotContain("Ocean", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Backup)));
    }

    [Fact]
    public async Task MismatchedExportConfirmationPreservesExistingBackup()
    {
        CreateTransfer();
        await Invoke("Import", Paths, "fixture-password\n");
        var original = File.ReadAllBytes(Backup);
        await Assert.ThrowsAsync<ConfigurationException>(() => Invoke("Export", Paths, "new-password\ndifferent-password\n"));
        Assert.Equal(original, File.ReadAllBytes(Backup));
    }

    [Fact]
    public async Task SaveFailureDoesNotLeaveANewMasterKey()
    {
        CreateTransfer();
        Directory.CreateDirectory(Paths.Root, (UnixFileMode)448);
        File.WriteAllText(Paths.EventImages, "blocks settings directory initialization");
        await Assert.ThrowsAnyAsync<IOException>(() => Invoke("Import", Paths, "fixture-password\n"));
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short\n")]
    public async Task IncompleteOrShortPasswordIsConfigurationError(string password)
    {
        CreateTransfer();
        await Assert.ThrowsAsync<ConfigurationException>(() => Invoke("Import", Paths, password));
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Fact]
    public async Task MissingSecretConfirmationIsConfigurationError()
    {
        await Assert.ThrowsAsync<ConfigurationException>(() => new SecretInput(new StringReader("password\n"), new StringWriter(), false).ReadAsync(true, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"Cameras\":[{},{},{},{},{}]}")]
    [InlineData("{\"RetentionDays\":0}")]
    [InlineData("{\"MonitorSchedules\":[{\"StartTime\":\"25:00\"}]}")]
    public async Task AuthenticatedButInvalidRawBackupCannotBeNormalizedIntoAcceptance(string json)
    {
        Directory.CreateDirectory(root, (UnixFileMode)448);
        var header = Encoding.UTF8.GetBytes("EZVIZ-LOCAL-TRANSFER-V1\n");
        var salt = new byte[16];
        var nonce = new byte[12];
        var key = Rfc2898DeriveBytes.Pbkdf2("fixture-password", salt, 600_000, HashAlgorithmName.SHA256, 32);
        var plain = Encoding.UTF8.GetBytes(json);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, header);
        CryptographicOperations.ZeroMemory(key);
        File.WriteAllBytes(Backup, [.. header, .. salt, .. nonce, .. tag, .. cipher]);
        await Assert.ThrowsAsync<ConfigurationException>(() => Invoke("Import", Paths, "fixture-password\n"));
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Fact]
    public async Task WrongPasswordPreservesExistingKeyAndCiphertext()
    {
        CreateTransfer();
        await Invoke("Import", Paths, "fixture-password\n");
        var key = File.ReadAllBytes(Paths.MasterKeyFile);
        var cipher = File.ReadAllBytes(Paths.SettingsFile);
        await Assert.ThrowsAsync<ConfigurationException>(() => Invoke("Import", Paths, "wrong-password\n"));
        Assert.Equal(key, File.ReadAllBytes(Paths.MasterKeyFile));
        Assert.Equal(cipher, File.ReadAllBytes(Paths.SettingsFile));
    }

    [Fact]
    public async Task WindowsDpapiBackupIsExplicitlyRejected()
    {
        var exception = await Assert.ThrowsAsync<ConfigurationException>(() => BackupCommands.ImportAsync(Paths,
            Path.Combine(root, "windows.ezvizbackup"), true, new StringReader("fixture-password\n"), new StringWriter(), false, CancellationToken.None));
        Assert.Contains("Windows", exception.Message);
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Fact]
    public async Task ExportCannotReplaceMasterKey()
    {
        CreateTransfer();
        await Invoke("Import", Paths, "fixture-password\n");
        var key = File.ReadAllBytes(Paths.MasterKeyFile);
        await Assert.ThrowsAsync<ConfigurationException>(() => BackupCommands.ExportAsync(Paths, Paths.MasterKeyFile, true,
            new StringReader("fixture-password\nfixture-password\n"), new StringWriter(), false, CancellationToken.None));
        Assert.Equal(key, File.ReadAllBytes(Paths.MasterKeyFile));
    }

    [Fact]
    public async Task RedirectedInputRequiresExplicitPasswordFlag()
    {
        await Assert.ThrowsAsync<ConfigurationException>(() => BackupCommands.ImportAsync(Paths, Backup, false,
            new StringReader("fixture-password\n"), new StringWriter(), false, CancellationToken.None));
        Assert.False(Directory.Exists(Paths.Root));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
