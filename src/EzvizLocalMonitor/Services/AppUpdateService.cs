using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EzvizLocalMonitor.Services;

public sealed record AppUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string ReleaseUrl,
    string? PackageUrl,
    string? ChecksumUrl)
{
    public bool IsNewer => LatestVersion > CurrentVersion;
}

/// <summary>
/// Checks the public GitHub release channel without requiring credentials.
/// Installation remains delegated to the existing Windows updater GUI.
/// </summary>
public sealed class AppUpdateService
{
    public const string Repository = "huyavm/ezviz-local-monitor-releases";
    private const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return null;

        using var client = new HttpClient { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EZVIZ-Local-Monitor", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (release is null || release.Draft || release.Prerelease) return null;

        var latest = ParseVersion(release.TagName);
        if (latest is null) return null;

        var package = release.Assets?.FirstOrDefault(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets?.FirstOrDefault(x => x.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));
        return new AppUpdateInfo(CurrentVersion, latest, release.TagName, release.HtmlUrl ?? string.Empty, package?.BrowserDownloadUrl, checksum?.BrowserDownloadUrl);
    }

    public static Version CurrentVersion => ParseVersion(
        typeof(AppUpdateService).Assembly.GetName().Version?.ToString() ?? "0.0.0") ?? new Version(0, 0, 0);

    public static string InstalledDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string UpdaterScriptPath => Path.Combine(InstalledDirectory, "scripts", "Update-EzvizLocalMonitor.ps1");

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim().TrimStart('v', 'V');
        var separator = clean.IndexOfAny(new[] { '-', '+' });
        if (separator >= 0) clean = clean[..separator];
        return Version.TryParse(clean, out var version) ? version : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

public enum UpdatePromptChoice
{
    Later,
    Update,
    OpenRelease
}
