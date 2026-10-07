using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class WindowsBackupService
{
    public WindowsBackupService(AppPaths paths) => ArgumentNullException.ThrowIfNull(paths);

    private static readonly byte[] Header = Encoding.UTF8.GetBytes("EZVIZ-LOCAL-BACKUP-V1\n");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-backup-v1");
    public void ExportBackup(AppSettings settings, string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var plain = SettingsCodec.Encode(settings);
        try { AtomicFile.Write(path, [.. Header, .. ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)]); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public AppSettings ImportBackup(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length <= Header.Length || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new InvalidDataException("File backup không đúng định dạng hoặc không được tạo bởi EZVIZ Local Monitor.");
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(bytes[Header.Length..], Entropy, DataProtectionScope.CurrentUser);
            return SettingsCodec.Decode(plain);
        }
        catch (CryptographicException ex) { throw new InvalidOperationException("Không thể giải mã backup. Hãy nhập file được tạo bởi đúng tài khoản Windows đã xuất cấu hình.", ex); }
        catch (JsonException ex) { throw new InvalidDataException("Backup không chứa cấu hình JSON hợp lệ.", ex); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
}
