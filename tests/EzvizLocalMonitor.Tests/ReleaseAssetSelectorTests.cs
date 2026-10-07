using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class ReleaseAssetSelectorTests
{
    private const string Windows = "EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip";
    private const string Linux = "EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz";
    private static ReleaseAsset Asset(string name, string url = "https://example.test/download") => new(name, url);
    private static ReleaseAsset[] Assets() => [Asset(Linux + ".sha256sum", "https://example.test/linux-hash"), Asset(Linux), Asset(Windows + ".sha256", "https://example.test/win-hash"), Asset(Windows)];

    [Theory]
    [InlineData(ReleaseTarget.WindowsX64, Windows, "https://example.test/win-hash")]
    [InlineData(ReleaseTarget.LinuxHeadlessX64, Linux, "https://example.test/linux-hash")]
    public void SelectsExactPairRegardlessOfAssetOrder(ReleaseTarget target, string name, string hashUrl)
    {
        foreach (var assets in new[] { Assets(), Assets().Reverse().ToArray() })
        {
            var pair = ReleaseAssetSelector.Select(assets, "v1.9.0", target);
            Assert.NotNull(pair);
            Assert.Equal(name, pair.Package.Name);
            Assert.Equal(hashUrl, pair.Checksum.DownloadUrl);
        }
    }

    [Theory]
    [InlineData(ReleaseTarget.WindowsX64, Windows, ".sha256")]
    [InlineData(ReleaseTarget.LinuxHeadlessX64, Linux, ".sha256sum")]
    public void RejectsMissingAndDuplicateMembers(ReleaseTarget target, string name, string suffix)
    {
        Assert.Null(ReleaseAssetSelector.Select([Asset(name)], "v1.9.0", target));
        Assert.Null(ReleaseAssetSelector.Select([Asset(name + suffix)], "v1.9.0", target));
        Assert.Null(ReleaseAssetSelector.Select([Asset(name), Asset(name), Asset(name + suffix)], "v1.9.0", target));
        Assert.Null(ReleaseAssetSelector.Select([Asset(name), Asset(name + suffix), Asset(name + suffix)], "v1.9.0", target));
    }

    [Theory]
    [InlineData("1.9.0")]
    [InlineData("v1.9.0")]
    [InlineData("v1.9.0-rc.1")]
    [InlineData("1.9.0-beta-2")]
    public void PreservesFullSafeVersionInPackageName(string tag)
    {
        var name = "EZVIZ-Local-Monitor-Windows-x64-v" + (tag.StartsWith('v') ? tag[1..] : tag) + ".zip";
        var pair = ReleaseAssetSelector.Select([Asset(name), Asset(name + ".sha256")], tag, ReleaseTarget.WindowsX64);
        Assert.NotNull(pair);
        Assert.Equal(name, pair.Package.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.9")]
    [InlineData("vv1.9.0")]
    [InlineData("v01.9.0")]
    [InlineData("v1.9.0+build")]
    [InlineData("v1.9.0-../escape")]
    [InlineData("v1.9.0-..\\escape")]
    [InlineData("v1.9.0-rc..1")]
    [InlineData("v1.9.0-01")]
    [InlineData("v1.9.0-rc_1")]
    [InlineData(" v1.9.0")]
    [InlineData("v1.9.0\n")]
    public void RejectsUnsafeOrNonSemanticTags(string tag)
    {
        var name = "EZVIZ-Local-Monitor-Windows-x64-v" + tag.TrimStart('v') + ".zip";
        Assert.Null(ReleaseAssetSelector.Select([Asset(name), Asset(name + ".sha256")], tag, ReleaseTarget.WindowsX64));
    }

    [Theory]
    [InlineData("http://example.test/file")]
    [InlineData("file:///tmp/file")]
    [InlineData("/relative")]
    [InlineData("https://user:password@example.test/file")]
    [InlineData("")]
    public void RejectsUnsafeDownloadUrlOnEitherMember(string url)
    {
        Assert.Null(ReleaseAssetSelector.Select([Asset(Windows, url), Asset(Windows + ".sha256")], "v1.9.0", ReleaseTarget.WindowsX64));
        Assert.Null(ReleaseAssetSelector.Select([Asset(Windows), Asset(Windows + ".sha256", url)], "v1.9.0", ReleaseTarget.WindowsX64));
    }

    [Theory]
    [InlineData("EZVIZ-Local-Monitor-Windows-arm64-v1.9.0.zip")]
    [InlineData("../EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip")]
    [InlineData("folder\\EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip")]
    [InlineData("EZVIZ-Local-Monitor-Windows-x64-v1.8.6.zip")]
    public void RejectsWrongArchitecturePathAndVersion(string name)
    {
        Assert.Null(ReleaseAssetSelector.Select([Asset(name), Asset(name + ".sha256")], "v1.9.0", ReleaseTarget.WindowsX64));
    }

    [Fact]
    public void DoesNotUseAnotherPlatformOrLegacyLinuxHashSuffix()
    {
        Assert.Null(ReleaseAssetSelector.Select(Assets().Where(a => a.Name.StartsWith("EZVIZ-Local-Monitor-Linux")).ToArray(), "v1.9.0", ReleaseTarget.WindowsX64));
        Assert.Null(ReleaseAssetSelector.Select([Asset(Linux), Asset(Linux + ".sha256")], "v1.9.0", ReleaseTarget.LinuxHeadlessX64));
        Assert.Null(ReleaseAssetSelector.Select(Assets(), "v1.9.0", (ReleaseTarget)99));
    }

    [Fact]
    public void Legacy186FirstSuffixAlgorithmStillFindsOnlyWindowsAssets()
    {
        // Isolated compatibility fixture for the shipped v1.8.6 algorithm.
        var assets = Assets();
        Assert.Equal(Windows, assets.First(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Name);
        Assert.Equal(Windows + ".sha256", assets.First(a => a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)).Name);
        Assert.Single(assets, a => a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));
    }
}
