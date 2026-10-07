using System.Runtime.InteropServices;

namespace EzvizLocalMonitor.Headless.Hosting;

internal static class LinuxUser
{
    public static bool IsRoot => OperatingSystem.IsLinux() && GetEuid() == 0;
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();
}
