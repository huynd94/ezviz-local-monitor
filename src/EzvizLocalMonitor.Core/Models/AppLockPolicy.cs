namespace EzvizLocalMonitor.Services;

public enum AppLockMode { Password = 0, Pin = 1 }

public static class AppLockPolicy
{
    public static void Validate(AppLockMode mode, string secret)
    {
        if (mode == AppLockMode.Pin)
        {
            if (secret.Length < 4 || secret.Length > 12 || secret.Any(c => c < '0' || c > '9'))
                throw new ArgumentException("PIN phải gồm từ 4 đến 12 chữ số.", nameof(secret));
        }
        else if (secret.Length < 8) throw new ArgumentException("Mật khẩu phải có ít nhất 8 ký tự.", nameof(secret));
    }
}
