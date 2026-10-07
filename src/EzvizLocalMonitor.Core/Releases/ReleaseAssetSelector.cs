using System.Text.RegularExpressions;

namespace EzvizLocalMonitor.Services;

public enum ReleaseTarget { WindowsX64, LinuxHeadlessX64 }
public sealed record ReleaseAsset(string Name, string DownloadUrl);
public sealed record ReleasePackage(ReleaseAsset Package, ReleaseAsset Checksum);

public static class ReleaseAssetSelector
{
    private static readonly Regex SafeTag = new(
        @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:[0-9A-Za-z-]+)(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ReleasePackage? Select(IReadOnlyList<ReleaseAsset> assets, string releaseTag, ReleaseTarget target)
    {
        if (releaseTag is null) return null;
        var match = SafeTag.Match(releaseTag);
        if (!match.Success) return null;
        // Semantic-version numeric prerelease identifiers cannot have leading zeroes.
        if (match.Groups[4].Success && match.Groups[4].Value.Split('.').Any(
                id => id.Length > 1 && id[0] == '0' && id.All(char.IsAsciiDigit))) return null;

        var version = releaseTag.StartsWith('v') ? releaseTag[1..] : releaseTag;
        var name = target switch
        {
            ReleaseTarget.WindowsX64 => $"EZVIZ-Local-Monitor-Windows-x64-v{version}.zip",
            ReleaseTarget.LinuxHeadlessX64 => $"EZVIZ-Local-Monitor-Linux-Headless-x64-v{version}.tar.gz",
            _ => null
        };
        if (name is null) return null;
        var hashName = name + (target == ReleaseTarget.WindowsX64 ? ".sha256" : ".sha256sum");
        var packages = assets.Where(a => a.Name == name).ToArray();
        var checksums = assets.Where(a => a.Name == hashName).ToArray();
        return packages.Length == 1 && checksums.Length == 1 &&
               IsSafeUrl(packages[0].DownloadUrl) && IsSafeUrl(checksums[0].DownloadUrl)
            ? new ReleasePackage(packages[0], checksums[0]) : null;
    }

    private static bool IsSafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
}
