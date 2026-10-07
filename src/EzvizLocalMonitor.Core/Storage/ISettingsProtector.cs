namespace EzvizLocalMonitor.Services;

public interface ISettingsProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] protectedBytes);
}
