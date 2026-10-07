using System.Security.Cryptography;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Storage;

public sealed class MasterKeyStore(AppPaths paths)
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public byte[] InitializeForNewConfiguration()
    {
        RequireLinux();
        if (File.Exists(paths.SettingsFile)) throw new InvalidDataException("Existing settings require their original master key; a replacement key will not be generated.");
        RejectLink(paths.Root, directory: true);
        Directory.CreateDirectory(paths.Root, PrivateDirectory);
        RequirePrivateDirectory(paths.Root);
        var directory = Path.GetDirectoryName(paths.MasterKeyFile)!;
        RejectLink(directory, directory: true);
        Directory.CreateDirectory(directory, PrivateDirectory);
        RequirePrivateDirectory(directory);
        paths.EnsureDirectories();
        if (File.Exists(paths.MasterKeyFile) || new FileInfo(paths.MasterKeyFile).LinkTarget is not null) return ReadExisting();
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            AtomicFile.Write(paths.MasterKeyFile, key, PrivateFile, overwrite: false);
            return key;
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    public byte[] ReadExisting()
    {
        RequireLinux();
        RequirePrivateDirectory(paths.Root);
        RequirePrivateDirectory(Path.GetDirectoryName(paths.MasterKeyFile)!);
        RejectLink(paths.MasterKeyFile, directory: false);
        if (!File.Exists(paths.MasterKeyFile)) throw new InvalidDataException("Master key is missing. Restore the original key or import a transfer backup into a new state directory.");
        if (File.GetUnixFileMode(paths.MasterKeyFile) != PrivateFile) throw new InvalidDataException("Master key permissions must be 0600.");
        var key = File.ReadAllBytes(paths.MasterKeyFile);
        if (key.Length == 32) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new InvalidDataException("Master key must contain exactly 32 bytes.");
    }

    public static void RequirePrivateDirectory(string path)
    {
        RequireLinux();
        RejectLink(path, directory: true);
        if (!Directory.Exists(path) || File.GetUnixFileMode(path) != PrivateDirectory)
            throw new InvalidDataException("State and key directories must exist with permissions 0700 on a Linux filesystem.");
    }

    private static void RejectLink(string path, bool directory)
    {
        FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null) throw new InvalidDataException("Symbolic links are not permitted for the protected state root or master key.");
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux key storage requires Linux.");
    }
}
