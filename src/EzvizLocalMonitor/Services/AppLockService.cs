using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EzvizLocalMonitor.Services;

public sealed class AppLockService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-app-lock-v1");
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 180_000;
    private readonly AppPaths _paths;
    private readonly string _filePath;
    public AppLockService(AppPaths paths) { _paths = paths; _filePath = Path.Combine(paths.Root, "app-lock.protected"); }

    public bool IsEnabled => File.Exists(_filePath);

    public AppLockMode GetMode()
    {
        try
        {
            var data = Read();
            return data.Mode == (int)AppLockMode.Pin ? AppLockMode.Pin : AppLockMode.Password;
        }
        catch
        {
            return AppLockMode.Password;
        }
    }

    public void Set(AppLockMode mode, string secret)
    {
        Validate(mode, secret);
        _paths.EnsureDirectories();
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        var data = new StoredLock((int)mode, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        var plain = JsonSerializer.SerializeToUtf8Bytes(data);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, _filePath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(protectedBytes);
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public bool Verify(string secret)
    {
        if (!IsEnabled || string.IsNullOrEmpty(secret)) return false;
        try
        {
            var data = Read();
            var salt = Convert.FromBase64String(data.Salt);
            var expected = Convert.FromBase64String(data.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            var valid = CryptographicOperations.FixedTimeEquals(actual, expected);
            CryptographicOperations.ZeroMemory(actual);
            return valid;
        }
        catch
        {
            return false;
        }
    }

    public void Disable()
    {
        try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
    }

    public static void Validate(AppLockMode mode, string secret)
    {
        AppLockPolicy.Validate(mode, secret);
    }

    private StoredLock Read()
    {
        var encrypted = File.ReadAllBytes(_filePath);
        var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<StoredLock>(plain)
                ?? throw new InvalidDataException("File khóa ứng dụng không hợp lệ.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private sealed record StoredLock(int Mode, string Salt, string Hash);
}
