using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public static class DataPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EZVIZ Local Monitor");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.protected");
    public static readonly string DatabaseFile = Path.Combine(Root, "events.db");
    public static readonly string EventImages = Path.Combine(Root, "Events");
    public static readonly string ZaloLogFile = Path.Combine(Root, "zalo-send.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(EventImages);
    }
}

public sealed class SettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        DataPaths.EnsureCreated();
        if (!File.Exists(DataPaths.SettingsFile)) return new AppSettings();

        try
        {
            var encrypted = File.ReadAllBytes(DataPaths.SettingsFile);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions) ?? new AppSettings();
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Không thể mở cấu hình đã mã hóa. Hãy cấu hình lại ứng dụng bằng đúng tài khoản Windows đã tạo cấu hình.");
        }
    }

    public void Save(AppSettings settings)
    {
        DataPaths.EnsureCreated();
        var plain = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        var temporary = DataPaths.SettingsFile + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, DataPaths.SettingsFile, true);
    }
}
