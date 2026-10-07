using System.Security.Cryptography;
using System.Text;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Storage;

public sealed class LinuxSettingsProtector : ISettingsProtector, IDisposable
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("EZVIZ-LINUX-SETTINGS-V1\n");
    private readonly byte[] _key;
    private bool _disposed;

    public LinuxSettingsProtector(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("AES-256 key must contain 32 bytes.", nameof(key));
        _key = (byte[])key.Clone();
    }

    public byte[] Protect(byte[] plain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Header);
        return [.. Header, .. nonce, .. tag, .. cipher];
    }

    public byte[] Unprotect(byte[] protectedBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (protectedBytes.Length < Header.Length + 28 || !protectedBytes.AsSpan(0, Header.Length).SequenceEqual(Header))
            throw new InvalidDataException("Unsupported or truncated Linux settings envelope.");
        var offset = Header.Length;
        var plain = new byte[protectedBytes.Length - offset - 28];
        try
        {
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(protectedBytes.AsSpan(offset, 12), protectedBytes.AsSpan(offset + 28), protectedBytes.AsSpan(offset + 12, 16), plain, Header);
            return plain;
        }
        catch { CryptographicOperations.ZeroMemory(plain); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
