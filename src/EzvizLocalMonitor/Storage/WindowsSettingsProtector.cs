using System.Security.Cryptography;
using System.Text;

namespace EzvizLocalMonitor.Services;

public sealed class WindowsSettingsProtector : ISettingsProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1");
    public byte[] Protect(byte[] plain)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI requires Windows.");
        return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
    }
    public byte[] Unprotect(byte[] protectedBytes)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI requires Windows.");
        return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
    }
}
