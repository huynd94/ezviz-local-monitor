using EzvizLocalMonitor.Models;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Tests;

public sealed class LogRedactorTests
{
    [Fact]
    public void MasksCredentialsInUrlsAndUnlabelledKnownTokens()
    {
        var redactor = new LogRedactor();
        redactor.Configure(new AppSettings { Ai = new AiSettings { ApiKey = "YOUR_API_KEY_HERE" } });
        var output = redactor.Redact("rtsp://admin:YOUR_RTSP_PASSWORD@192.168.1.20/stream https://api.telegram.org/botYOUR_TELEGRAM_TOKEN/sendMessage failure YOUR_API_KEY_HERE");
        Assert.DoesNotContain("YOUR_RTSP_PASSWORD", output);
        Assert.DoesNotContain("YOUR_TELEGRAM_TOKEN", output);
        Assert.DoesNotContain("YOUR_API_KEY_HERE", output);
        Assert.DoesNotContain("192.168.1.20", output);
    }

    [Fact]
    public void IndependentLoggersNeverWriteToAnotherStateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ezviz-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var one = new AppPaths(Path.Combine(root, "one"), Path.Combine(root, "model.onnx"));
            var two = new AppPaths(Path.Combine(root, "two"), Path.Combine(root, "model.onnx"));
            new AppLogger(one).Info(LogChannel.App, "first marker");
            new AppLogger(two).Info(LogChannel.App, "second marker");
            Assert.Contains("first marker", File.ReadAllText(one.AppLogFile));
            Assert.DoesNotContain("second marker", File.ReadAllText(one.AppLogFile));
            Assert.DoesNotContain("first marker", File.ReadAllText(two.AppLogFile));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
