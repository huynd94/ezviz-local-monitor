using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Desktop.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsProtectionTests
{
    [Fact]
    public void ReadsLegacySettingsAndBackupWithoutChangingEntropy()
    {
        var root = Path.Combine(Path.GetTempPath(), "ezviz-windows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, "model.onnx"));
            var plain = Encoding.UTF8.GetBytes("{\"ThemeName\":\"Ocean\"}");
            File.WriteAllBytes(paths.SettingsFile, ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1"), DataProtectionScope.CurrentUser));
            var store = new SettingsStore(paths, new WindowsSettingsProtector());
            Assert.Equal("Ocean", store.Load().ThemeName);
            var backupPath = Path.Combine(root, "old.ezvizbackup");
            var encrypted = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-backup-v1"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(backupPath, Encoding.UTF8.GetBytes("EZVIZ-LOCAL-BACKUP-V1\n").Concat(encrypted).ToArray());
            var backups = new WindowsBackupService(paths);
            Assert.Equal("Ocean", backups.ImportBackup(backupPath).ThemeName);
            backups.ExportBackup(store.Load(), backupPath);
            var raw = File.ReadAllBytes(backupPath);
            var header = Encoding.UTF8.GetBytes("EZVIZ-LOCAL-BACKUP-V1\n");
            var decoded = ProtectedData.Unprotect(raw[header.Length..], Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-backup-v1"), DataProtectionScope.CurrentUser);
            Assert.Equal("Ocean", SettingsCodec.Decode(decoded).ThemeName);
        }
        finally { Directory.Delete(root, true); }
    }
}
