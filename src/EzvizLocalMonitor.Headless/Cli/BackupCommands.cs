using System.Security.Cryptography;
using System.Text;
using EzvizLocalMonitor.Headless.Hosting;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Cli;

public static class BackupCommands
{
    // Same Windows transfer envelope; decode raw JSON before the shared codec can normalize it.
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("EZVIZ-LOCAL-TRANSFER-V1\n");

    public static async Task ImportAsync(AppPaths paths, string path, bool passwordStdin, TextReader input, TextWriter output, bool interactive, CancellationToken ct)
    {
        RequireTransferPath(path);
        var password = await PasswordAsync(passwordStdin, input, output, interactive, false, ct);
        var settings = await ReadTransferAsync(path, password, ct);
        ct.ThrowIfCancellationRequested();
        using var stateLock = StateDirectoryLock.Acquire(paths);
        using var configuration = ProtectedConfiguration.OpenForNewOrExistingWrite(paths);
        configuration.Save(settings);
        await output.WriteLineAsync("Transfer backup imported.");
    }

    public static async Task ExportAsync(AppPaths paths, string path, bool passwordStdin, TextReader input, TextWriter output, bool interactive, CancellationToken ct)
    {
        RequireTransferPath(path);
        var destination = Path.GetFullPath(path);
        var reserved = new[] { paths.SettingsFile, paths.MasterKeyFile, paths.LockFile, paths.DatabaseFile, paths.StatusFile,
            paths.AppLogFile, paths.CameraLogFile, paths.AlertsLogFile, paths.AiLogFile, paths.ZaloLogFile, paths.StartupLogFile };
        if (reserved.Contains(destination, StringComparer.Ordinal) || destination.StartsWith(Path.GetDirectoryName(paths.MasterKeyFile)! + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ConfigurationException("Backup destination must not overwrite protected application state.");
        using var configuration = ProtectedConfiguration.OpenExisting(paths);
        var password = await PasswordAsync(passwordStdin, input, output, interactive, true, ct);
        ct.ThrowIfCancellationRequested();
        configuration.Store.ExportTransferBackup(configuration.Settings, destination, password);
        await output.WriteLineAsync("Transfer backup exported.");
    }

    private static void RequireTransferPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ConfigurationException("Transfer backup path is required.");
        if (string.Equals(Path.GetExtension(path), ".ezvizbackup", StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("DPAPI .ezvizbackup is Windows-only; export an .ezviztransfer backup on Windows instead.");
    }

    private static async Task<string> PasswordAsync(bool fromStdin, TextReader input, TextWriter output, bool interactive, bool confirm, CancellationToken ct)
    {
        if (!fromStdin && !interactive) throw new ConfigurationException("Backup requires a terminal or --password-stdin.");
        if (!fromStdin) await output.WriteAsync("Backup password: ");
        var password = await new SecretInput(input, output, !fromStdin).ReadAsync(confirm, ct);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) throw new ConfigurationException("Backup password must contain at least eight characters.");
        return password;
    }

    private static async Task<AppSettings> ReadTransferAsync(string path, string password, CancellationToken ct)
    {
        var maximum = SettingsValidator.MaximumJsonBytes + Header.Length + 44;
        using var stream = File.OpenRead(path);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, ct);
            if (count == 0) break;
            if (buffer.Length + count > maximum) throw new ConfigurationException("Transfer backup exceeds the supported size.");
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length < Header.Length + 45 || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new ConfigurationException("File is not an EZVIZ transfer backup. DPAPI backups are Windows-only.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, bytes.AsSpan(Header.Length, 16), 600_000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[bytes.Length - Header.Length - 44];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(Header.Length + 16, 12), bytes.AsSpan(Header.Length + 44), bytes.AsSpan(Header.Length + 28, 16), plain, Header);
            return SettingsValidator.Decode(plain);
        }
        catch (CryptographicException) { throw new ConfigurationException("Transfer backup cannot be decrypted; check the password and file."); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
