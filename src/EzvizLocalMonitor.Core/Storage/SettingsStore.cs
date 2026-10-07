using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class SettingsStore
{
    private static readonly byte[] TransferHeader = Encoding.UTF8.GetBytes("EZVIZ-LOCAL-TRANSFER-V1\n");
    private readonly AppPaths _paths;
    private readonly ISettingsProtector _protector;

    public SettingsStore(AppPaths paths, ISettingsProtector protector) { _paths = paths; _protector = protector; }

    public AppSettings Load()
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.SettingsFile)) return new AppSettings();
        byte[]? plain = null;
        try
        {
            plain = _protector.Unprotect(File.ReadAllBytes(_paths.SettingsFile));
            return SettingsCodec.Decode(plain);
        }
        catch (CryptographicException ex) { throw new InvalidOperationException("Không thể mở cấu hình đã mã hóa. Kiểm tra khóa và tài khoản tạo cấu hình.", ex); }
        catch (JsonException ex) { throw new InvalidDataException("Cấu hình cục bộ không đúng định dạng JSON.", ex); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(AppSettings settings)
    {
        _paths.EnsureDirectories();
        var plain = SettingsCodec.Encode(settings);
        try { AtomicFile.Write(_paths.SettingsFile, _protector.Protect(plain)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void ExportTransferBackup(AppSettings settings, string filePath, string password)
    {
        ValidateTransferPassword(password);
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Đường dẫn file backup không được trống.", nameof(filePath));
        var plain = SettingsCodec.Encode(settings);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try
        {
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, TransferHeader);
            AtomicFile.Write(filePath, [.. TransferHeader, .. salt, .. nonce, .. tag, .. cipher]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Không thể ghi file backup. Hãy đóng file backup đang mở, chọn tên file mới hoặc kiểm tra quyền ghi thư mục đích.", ex);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public AppSettings ImportTransferBackup(string filePath, string password)
    {
        ValidateTransferPassword(password);
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Đường dẫn file backup không được trống.", nameof(filePath));
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < TransferHeader.Length + 16 + 12 + 16 + 1 || !bytes.AsSpan(0, TransferHeader.Length).SequenceEqual(TransferHeader))
            throw new InvalidDataException("File không phải backup chuyển máy EZVIZ Local Monitor v1.");
        var offset = TransferHeader.Length;
        var salt = bytes.AsSpan(offset, 16).ToArray(); offset += 16;
        var nonce = bytes.AsSpan(offset, 12).ToArray(); offset += 12;
        var tag = bytes.AsSpan(offset, 16).ToArray(); offset += 16;
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[bytes.Length - offset];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, bytes.AsSpan(offset), tag, plain, TransferHeader);
            return SettingsCodec.Decode(plain);
        }
        catch (CryptographicException) { throw new InvalidOperationException("Không thể giải mã backup chuyển máy. Hãy kiểm tra mật khẩu hoặc chọn đúng file."); }
        catch (JsonException ex) { throw new InvalidDataException("Backup chuyển máy không chứa cấu hình JSON hợp lệ.", ex); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    private static void ValidateTransferPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Mật khẩu backup chuyển máy phải có ít nhất 8 ký tự.", nameof(password));
    }
}
