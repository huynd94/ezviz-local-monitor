using System.Text;
using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void PathsAndAtomicWriteUseOnlyExplicitStateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ezviz-boundary-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, "model.onnx"));
            paths.EnsureDirectories();
            AtomicFile.Write(paths.SettingsFile, Encoding.UTF8.GetBytes("first"));
            Assert.Equal(Path.Combine(root, "settings.protected"), paths.SettingsFile);
            Assert.Equal(Path.Combine(root, "keys", "master.key"), paths.MasterKeyFile);
            Assert.Equal("first", File.ReadAllText(paths.SettingsFile));
            Assert.Throws<IOException>(() => AtomicFile.Write(paths.SettingsFile, Encoding.UTF8.GetBytes("second"), overwrite: false));
            Assert.Equal("first", File.ReadAllText(paths.SettingsFile));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CodecReadsLegacyJsonAndClonesWithoutSharingMutableSettings()
    {
        var settings = SettingsCodec.Decode(Encoding.UTF8.GetBytes("{\"ThemeName\":\"Ocean\",\"WatchdogEnabled\":true}"));
        Assert.Equal("Ocean", settings.ThemeName);
        Assert.True(settings.WatchdogEnabled);
        settings.Cameras.Add(new CameraDefinition { Name = "Original" });
        var clone = SettingsCodec.Clone(settings);
        clone.Cameras[0].Name = "Changed";
        Assert.Equal("Original", settings.Cameras[0].Name);
    }
}
