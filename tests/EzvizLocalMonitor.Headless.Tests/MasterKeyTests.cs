using System.Security.Cryptography;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class MasterKeyTests : IDisposable
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly string _root = Path.Combine("/tmp", "ezviz-master-key-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root, Path.Combine(_root, "model.onnx"));
    private string KeyDirectory => Path.GetDirectoryName(Paths.MasterKeyFile)!;

    [Fact]
    public void NewKeyHasRequiredLengthAndPrivateModes()
    {
        var key = new MasterKeyStore(Paths).InitializeForNewConfiguration();
        try
        {
            Assert.Equal(32, key.Length);
            Assert.Equal(key, File.ReadAllBytes(Paths.MasterKeyFile));
            Assert.Equal(DirectoryMode, File.GetUnixFileMode(_root));
            Assert.Equal(DirectoryMode, File.GetUnixFileMode(KeyDirectory));
            Assert.Equal(FileMode, File.GetUnixFileMode(Paths.MasterKeyFile));
            Assert.False(File.Exists(Paths.SettingsFile));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public void ExistingKeyBeforeFirstSettingsIsReusedAndDecryptsAfterRestart()
    {
        CreatePrivateDirectories();
        var original = RandomNumberGenerator.GetBytes(32);
        WriteKey(original);
        var keys = new MasterKeyStore(Paths);
        var reused = keys.InitializeForNewConfiguration();
        try
        {
            Assert.Equal(original, reused);
            Assert.Equal(original, File.ReadAllBytes(Paths.MasterKeyFile));
            using (var protector = new LinuxSettingsProtector(reused))
                new SettingsStore(Paths, protector).Save(new AppSettings { ThemeName = "Ocean" });
            var read = keys.ReadExisting();
            try
            {
                using var protector = new LinuxSettingsProtector(read);
                Assert.Equal("Ocean", new SettingsStore(Paths, protector).Load().ThemeName);
            }
            finally { CryptographicOperations.ZeroMemory(read); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            CryptographicOperations.ZeroMemory(reused);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void InvalidExistingKeyLengthIsRejectedWithoutReplacement(int length)
    {
        CreatePrivateDirectories();
        var invalid = new byte[length];
        WriteKey(invalid);
        var keys = new MasterKeyStore(Paths);

        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
        Assert.Throws<InvalidDataException>(() => keys.InitializeForNewConfiguration());
        Assert.Equal(invalid, File.ReadAllBytes(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Theory]
    [InlineData("root", 493)] // 0755
    [InlineData("root", 448 + 1024)] // 02700
    [InlineData("keys", 488)] // 0750
    [InlineData("keys", 448 + 512)] // 01700
    [InlineData("key", 420)] // 0644
    [InlineData("key", 256)] // 0400
    public void UnsafeExistingModesAreRejectedWithoutRepairOrReplacement(string target, int mode)
    {
        CreatePrivateDirectories();
        var original = new byte[32];
        WriteKey(original);
        var path = target switch { "root" => _root, "keys" => KeyDirectory, _ => Paths.MasterKeyFile };
        File.SetUnixFileMode(path, (UnixFileMode)mode);
        var keys = new MasterKeyStore(Paths);

        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
        Assert.Throws<InvalidDataException>(() => keys.InitializeForNewConfiguration());
        Assert.Equal((UnixFileMode)mode, File.GetUnixFileMode(path));
        Assert.Equal(original, File.ReadAllBytes(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("keys")]
    public void InitializationRejectsUnsafeDirectoriesBeforeCreatingOtherState(string target)
    {
        Directory.CreateDirectory(_root, DirectoryMode);
        var path = target == "root" ? _root : Directory.CreateDirectory(KeyDirectory, DirectoryMode).FullName;
        File.SetUnixFileMode(path, DirectoryMode | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Assert.Throws<InvalidDataException>(() => new MasterKeyStore(Paths).InitializeForNewConfiguration());

        Assert.DoesNotContain(Directory.GetFileSystemEntries(_root), entry => entry != KeyDirectory);
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.Equal(DirectoryMode | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData("root", false)]
    [InlineData("root", true)]
    [InlineData("keys", false)]
    [InlineData("keys", true)]
    [InlineData("key", false)]
    [InlineData("key", true)]
    public void ExistingAndDanglingSymlinksAreRejectedWithoutChangingTargets(string target, bool dangling)
    {
        // Both the link and its destination stay inside this test's owned /tmp root.
        Directory.CreateDirectory(_root, DirectoryMode);
        var destination = Path.Combine(_root, "destination");
        var state = Path.Combine(_root, "state");
        var paths = new AppPaths(state, Path.Combine(_root, "model.onnx"));
        if (target != "root") Directory.CreateDirectory(state, DirectoryMode);
        if (target == "key") Directory.CreateDirectory(Path.GetDirectoryName(paths.MasterKeyFile)!, DirectoryMode);
        if (!dangling)
        {
            if (target == "key") File.WriteAllBytes(destination, new byte[32]);
            else Directory.CreateDirectory(destination, DirectoryMode);
        }
        var link = target switch { "root" => state, "keys" => Path.GetDirectoryName(paths.MasterKeyFile)!, _ => paths.MasterKeyFile };
        if (target == "key") File.CreateSymbolicLink(link, destination);
        else Directory.CreateSymbolicLink(link, destination);
        var keys = new MasterKeyStore(paths);

        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
        Assert.Throws<InvalidDataException>(() => keys.InitializeForNewConfiguration());

        FileSystemInfo info = target == "key" ? new FileInfo(link) : new DirectoryInfo(link);
        Assert.Equal(destination, info.LinkTarget);
        if (dangling) Assert.False(Path.Exists(destination));
        else if (target == "key") Assert.Equal(new byte[32], File.ReadAllBytes(destination));
        else Assert.Empty(Directory.GetFileSystemEntries(destination));
    }

    [Fact]
    public void MissingKeyAfterSettingsWereSavedDoesNotRegenerateOrMutateSettings()
    {
        var keys = new MasterKeyStore(Paths);
        var key = keys.InitializeForNewConfiguration();
        using var protector = new LinuxSettingsProtector(key);
        CryptographicOperations.ZeroMemory(key);
        new SettingsStore(Paths, protector).Save(new AppSettings { ThemeName = "Ocean" });
        var original = File.ReadAllBytes(Paths.SettingsFile);
        File.Delete(Paths.MasterKeyFile);

        Assert.Throws<InvalidDataException>(() => keys.ReadExisting());
        Assert.Throws<InvalidDataException>(() => keys.InitializeForNewConfiguration());
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.Empty(Directory.GetFileSystemEntries(KeyDirectory));
        Assert.Equal(original, File.ReadAllBytes(Paths.SettingsFile));
    }

    [Fact]
    public void ReadingMissingStateDoesNotCreateDirectoriesOrKey()
    {
        Assert.Throws<InvalidDataException>(() => new MasterKeyStore(Paths).ReadExisting());
        Assert.False(Directory.Exists(_root));
    }

    private void CreatePrivateDirectories()
    {
        Directory.CreateDirectory(_root, DirectoryMode);
        Directory.CreateDirectory(KeyDirectory, DirectoryMode);
    }

    private void WriteKey(byte[] key)
    {
        File.WriteAllBytes(Paths.MasterKeyFile, key);
        File.SetUnixFileMode(Paths.MasterKeyFile, FileMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
