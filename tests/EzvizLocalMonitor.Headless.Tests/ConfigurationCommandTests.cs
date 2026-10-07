using System.Text.Json;
using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Headless.Storage;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class ConfigurationCommandTests : IDisposable
{
    private readonly string root = Path.Combine("/tmp", "ezviz-config-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(root, Path.Combine(root, "model.onnx"));
    private Task Configure(string json) => ConfigurationCommands.ConfigureAsync(Paths, true, new StringReader(json), new StringWriter(), false, CancellationToken.None);

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"RetentionDays\":0}")]
    [InlineData("{\"Cameras\":[{},{},{},{},{}]}")]
    [InlineData("{\"Cameras\":[{\"RtspUrl\":\"https://camera.invalid\"}]}")]
    [InlineData("{\"MonitorSchedules\":[{\"Days\":\"garbage\"}]}")]
    [InlineData("{\"Alerts\":{\"TelegramEnabled\":true}}")]
    public async Task InvalidRawInputDoesNotCreateKeyOrSettings(string json)
    {
        await Assert.ThrowsAsync<ConfigurationException>(() => Configure(json));
        Assert.False(File.Exists(Paths.MasterKeyFile));
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Fact]
    public async Task OversizedJsonDoesNotCreateKey()
    {
        await Assert.ThrowsAsync<ConfigurationException>(() => Configure("{\"ThemeName\":\"" + new string('x', 1024 * 1024) + "\"}"));
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Fact]
    public async Task ValidSettingsSurviveReopenAndStoredRangesAreNotRuntimeClamped()
    {
        await Configure(JsonSerializer.Serialize(new AppSettings { InferenceFpsPerCamera = 10, ConfirmationWindow = 30, ConfirmationsRequired = 10 }));
        var key = new MasterKeyStore(Paths).ReadExisting();
        using var protector = new LinuxSettingsProtector(key);
        var settings = new SettingsStore(Paths, protector).Load();
        Assert.Equal(10, settings.InferenceFpsPerCamera);
        Assert.Equal(30, settings.ConfirmationWindow);
        Assert.Equal(10, settings.ConfirmationsRequired);
        Assert.Equal((UnixFileMode)384, File.GetUnixFileMode(Paths.SettingsFile));
    }

    [Fact]
    public async Task CorruptExistingStateIsNotOverwritten()
    {
        await Configure("{}");
        var key = File.ReadAllBytes(Paths.MasterKeyFile);
        File.WriteAllBytes(Paths.SettingsFile, [1, 2, 3]);
        await Assert.ThrowsAsync<ConfigurationException>(() => Configure("{}"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Paths.SettingsFile));
        Assert.Equal(key, File.ReadAllBytes(Paths.MasterKeyFile));
    }

    [Fact]
    public void OversizedInMemoryConfigurationIsRejectedBeforeKeyCreation()
    {
        Assert.Throws<ConfigurationException>(() => SettingsValidator.Validate(new AppSettings { ThemeName = new string('x', 1024 * 1024) }));
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Fact]
    public void FailedFirstSaveRollsBackOnlyTheNewKey()
    {
        using (var configuration = ProtectedConfiguration.OpenForNewOrExistingWrite(Paths))
        {
            Directory.CreateDirectory(Paths.SettingsFile);
            Assert.ThrowsAny<IOException>(() => configuration.Save(new AppSettings()));
        }
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Fact]
    public async Task LostKeyCannotBeRegeneratedByConfigure()
    {
        await Configure("{}");
        var original = File.ReadAllBytes(Paths.SettingsFile);
        File.Delete(Paths.MasterKeyFile);
        await Assert.ThrowsAsync<ConfigurationException>(() => Configure("{}"));
        Assert.Equal(original, File.ReadAllBytes(Paths.SettingsFile));
        Assert.False(File.Exists(Paths.MasterKeyFile));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteCameraNumbersAreRejected(double confidence)
    {
        Assert.Throws<ConfigurationException>(() => SettingsValidator.Validate(new AppSettings
        {
            Cameras = [new CameraDefinition { RtspUrl = "rtsp://camera.invalid/stream", ConfidenceThreshold = confidence }]
        }));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
