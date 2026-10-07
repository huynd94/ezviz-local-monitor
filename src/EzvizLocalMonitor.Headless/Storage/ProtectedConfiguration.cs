using System.Security.Cryptography;
using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor.Headless.Storage;

public sealed class ProtectedConfiguration : IDisposable
{
    private readonly LinuxSettingsProtector protector;
    private readonly AppPaths paths;
    private bool uncommittedKey;
    public SettingsStore Store { get; }
    public AppSettings Settings { get; private set; }

    private ProtectedConfiguration(AppPaths paths, byte[] key, bool newKey)
    {
        this.paths = paths;
        try { protector = new LinuxSettingsProtector(key); }
        finally { CryptographicOperations.ZeroMemory(key); }
        Store = new SettingsStore(paths, protector);
        Settings = new AppSettings();
        uncommittedKey = newKey;
    }

    public static ProtectedConfiguration OpenExisting(AppPaths paths)
    {
        ProtectedConfiguration? configuration = null;
        try
        {
            RequireSettingsFile(paths, required: true);
            configuration = new(paths, new MasterKeyStore(paths).ReadExisting(), false);
            var bytes = File.ReadAllBytes(paths.SettingsFile);
            var plain = configuration.protector.Unprotect(bytes);
            try { configuration.Settings = SettingsValidator.Decode(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            return configuration;
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException)
        {
            configuration?.Dispose();
            throw new ConfigurationException("Protected configuration or master key is invalid; restore the original state.");
        }
        catch { configuration?.Dispose(); throw; }
    }

    public static ProtectedConfiguration OpenForNewOrExistingWrite(AppPaths paths)
    {
        RequireSettingsFile(paths, required: false);
        if (File.Exists(paths.SettingsFile)) return OpenExisting(paths);
        var newKey = !File.Exists(paths.MasterKeyFile);
        try { return new(paths, new MasterKeyStore(paths).InitializeForNewConfiguration(), newKey); }
        catch (InvalidDataException) { throw new ConfigurationException("Master key or state directory is invalid; restore the original state."); }
    }

    public void Save(AppSettings settings)
    {
        SettingsValidator.Validate(settings);
        Store.Save(settings);
        uncommittedKey = false;
        Settings = settings;
    }

    private static void RequireSettingsFile(AppPaths paths, bool required)
    {
        var file = new FileInfo(paths.SettingsFile);
        if (file.LinkTarget is not null || Directory.Exists(paths.SettingsFile)) throw new ConfigurationException("Settings must be a regular file, not a symbolic link or directory.");
        if (!file.Exists)
        {
            if (required) throw new ConfigurationException("Configuration is missing; run configure or import a transfer backup.");
            return;
        }
        if (file.Length > SettingsValidator.MaximumJsonBytes + 128) throw new ConfigurationException("Protected configuration exceeds the supported size.");
        if (OperatingSystem.IsLinux() && File.GetUnixFileMode(file.FullName) != (UnixFileMode)384) throw new ConfigurationException("Settings permissions must be 0600.");
    }

    public void Dispose()
    {
        protector.Dispose();
        if (uncommittedKey) { File.Delete(paths.MasterKeyFile); uncommittedKey = false; }
    }
}
