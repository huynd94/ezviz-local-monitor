using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class CliDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine("/tmp", "ezviz-cli-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvalidJsonReturnsConfigurationErrorWithoutCreatingKeyOrSettings()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CommandDispatcher.ExecuteAsync(["configure", "--stdin", "--data-dir", _root],
            new StringReader("not-json"), stdout, stderr, CancellationToken.None);
        Assert.Equal(2, code);
        Assert.False(Directory.Exists(_root));
        Assert.DoesNotContain("not-json", stderr.ToString());
    }

    [Fact]
    public async Task ConfigureThenValidateAndBackupRoundTripUseProtectedState()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Assert.Equal(0, await CommandDispatcher.ExecuteAsync(["configure", "--stdin", "--data-dir", _root],
            new StringReader("{\"ThemeName\":\"Ocean\",\"Cameras\":[]}"), stdout, stderr, CancellationToken.None));
        Assert.Equal(0, await CommandDispatcher.ExecuteAsync(["config", "validate", "--data-dir", _root],
            new StringReader(""), stdout, stderr, CancellationToken.None));
        var backup = Path.Combine(_root, "portable.ezviztransfer");
        Assert.Equal(0, await CommandDispatcher.ExecuteAsync(["backup", "export", backup, "--password-stdin", "--data-dir", _root],
            new StringReader("TEST_PASSWORD_ONLY\nTEST_PASSWORD_ONLY\n"), stdout, stderr, CancellationToken.None));
        Assert.True(File.Exists(backup));
        Assert.Equal(0, await CommandDispatcher.ExecuteAsync(["backup", "import", backup, "--password-stdin", "--data-dir", _root],
            new StringReader("TEST_PASSWORD_ONLY\n"), stdout, stderr, CancellationToken.None));
        Assert.DoesNotContain("TEST_PASSWORD_ONLY", stdout.ToString() + stderr.ToString());
    }

    [Fact]
    public async Task SecretArgumentIsRejectedWithoutEchoingItsValue()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(2, await CommandDispatcher.ExecuteAsync(["backup", "export", "fixture.ezviztransfer", "--password", "YOUR_SECRET_HERE"],
            new StringReader(""), output, error, CancellationToken.None));
        Assert.DoesNotContain("YOUR_SECRET_HERE", output.ToString() + error.ToString());
    }

    [Fact]
    public async Task VersionAndMissingStatusDoNotInitializeProtectedConfiguration()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, await CommandDispatcher.ExecuteAsync(["version"], new StringReader(""), output, error, CancellationToken.None));
        Assert.Contains("ezviz-headless", output.ToString());
        Assert.Equal(4, await CommandDispatcher.ExecuteAsync(["status", "--json", "--data-dir", _root], new StringReader(""), output, error, CancellationToken.None));
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
