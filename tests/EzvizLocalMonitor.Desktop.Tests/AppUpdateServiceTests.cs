using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Desktop.Tests;

public sealed class AppUpdateServiceTests
{
    private const string Package = "EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip";
    private static object Asset(string name, string url) => new { name, browser_download_url = url };
    private static string Release(object[] assets, bool draft = false, bool prerelease = false, string tag = "v1.9.0") =>
        JsonSerializer.Serialize(new { tag_name = tag, html_url = "https://example.test/release", draft, prerelease, assets });

    [Fact]
    public async Task RealReleaseJsonSelectsExactWindowsPairAfterUnrelatedZipAndHash()
    {
        var json = Release([
            Asset("unrelated.zip", "https://example.test/wrong-zip"),
            Asset("unrelated.sha256", "https://example.test/wrong-hash"),
            Asset("EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz.sha256sum", "https://example.test/linux-hash"),
            Asset(Package + ".sha256", "https://example.test/windows-hash"),
            Asset(Package, "https://example.test/windows-zip")]);
        var update = await CheckJsonAsync(json);
        Assert.NotNull(update);
        Assert.Equal("https://example.test/windows-zip", update.PackageUrl);
        Assert.Equal("https://example.test/windows-hash", update.ChecksumUrl);
        Assert.Equal(new Version(1, 9, 0), update.LatestVersion);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-package")]
    [InlineData("duplicate-checksum")]
    [InlineData("http-checksum")]
    [InlineData("wrong-platform")]
    public async Task UnusablePairDoesNotOfferUpdate(string scenario)
    {
        var assets = new List<object> { Asset(Package, "https://example.test/windows-zip") };
        if (scenario != "missing") assets.Add(Asset(Package + ".sha256", scenario == "http-checksum" ? "http://example.test/hash" : "https://example.test/hash"));
        if (scenario == "duplicate-package") assets.Add(Asset(Package, "https://example.test/duplicate"));
        if (scenario == "duplicate-checksum") assets.Add(Asset(Package + ".sha256", "https://example.test/duplicate"));
        if (scenario == "wrong-platform") assets = [Asset("linux.tar.gz", "https://example.test/linux"), Asset("linux.tar.gz.sha256sum", "https://example.test/linux-hash")];
        Assert.Null(await CheckJsonAsync(Release(assets.ToArray())));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StableChannelIgnoresDraftAndPrerelease(bool draft, bool prerelease)
    {
        Assert.Null(await CheckJsonAsync(Release([Asset(Package, "https://example.test/zip"), Asset(Package + ".sha256", "https://example.test/hash")], draft, prerelease)));
    }

    [Fact]
    public async Task MissingChecksumCannotStartPreparation()
    {
        var update = new AppUpdateInfo(new Version(1, 8, 6), new Version(1, 9, 0), "v1.9.0", "", "https://example.test/not-contacted", null);
        Assert.Null(await new AppUpdateService().PrepareUpdaterAsync(update));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("mismatch")]
    public async Task BadChecksumRejectsDownloadedZipBeforeExtraction(string kind)
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("Update-EzvizLocalMonitor.ps1").Open());
            writer.Write("# never extracted on bad hash");
        }
        var hash = kind == "invalid" ? "not-a-sha256" : new string('0', 64);
        using var server = new LocalServer(path => path == "/zip" ? archive.ToArray() : Encoding.UTF8.GetBytes(hash));
        var update = new AppUpdateInfo(new Version(1, 8, 6), new Version(1, 9, 0), "v1.9.0", "", server.Url + "zip", server.Url + "hash");
        await Assert.ThrowsAsync<InvalidDataException>(() => new AppUpdateService().PrepareUpdaterAsync(update));
    }

    private static async Task<AppUpdateInfo?> CheckJsonAsync(string json)
    {
        using var server = new LocalServer(_ => Encoding.UTF8.GetBytes(json));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // Private HTTP seam preserves the shipped public API and runs real HTTP + JSON parsing.
        var method = typeof(AppUpdateService).GetMethod("CheckReleaseAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<AppUpdateInfo?>)method.Invoke(null, [client, server.Url, CancellationToken.None])!;
    }

    private sealed class LocalServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Task serving;
        public string Url { get; }

        public LocalServer(Func<string, byte[]> response)
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Url = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(Url);
            listener.Start();
            serving = Task.Run(async () =>
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var context = await listener.GetContextAsync();
                        var bytes = response(context.Request.Url!.AbsolutePath);
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes);
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
            });
        }

        public void Dispose()
        {
            listener.Close();
            serving.GetAwaiter().GetResult();
        }
    }
}
