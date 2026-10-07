using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Services;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class OperationalCommandTests : IDisposable
{
    private readonly string _root = Path.Combine("/tmp", "ezviz-operations-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths(string model) => new(_root, model, Path.Combine(_root, "logs"));

    [Fact]
    public async Task DoctorChecksRealNativeLibrariesAndMissingModelFails()
    {
        var model = Environment.GetEnvironmentVariable("EZVIZ_TEST_MODEL")!;
        await ConfigurationCommands.ConfigureAsync(Paths(model), true, new StringReader("{\"Cameras\":[]}"), new StringWriter(), false, CancellationToken.None);
        var output = new StringWriter();
        Assert.Equal(0, await OperationalCommands.DoctorAsync(Paths(model), output, CancellationToken.None));
        Assert.Contains("ONNX CPU", output.ToString());
        await Assert.ThrowsAsync<FileNotFoundException>(() => OperationalCommands.DoctorAsync(Paths(Path.Combine(_root, "missing.onnx")), output, CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitAlertTestSendsExpectedPayloadOnlyToLocalReceiver()
    {
        var paths = Paths("/tmp/unused.onnx");
        await ConfigurationCommands.ConfigureAsync(paths, true,
            new StringReader("{\"Alerts\":{\"TelegramEnabled\":true,\"TelegramBotToken\":\"YOUR_TOKEN_HERE\",\"TelegramChatId\":\"YOUR_CHAT_ID_HERE\"}}"),
            new StringWriter(), false, CancellationToken.None);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var receive = ReceiveAsync(listener);
            using var client = new HttpClient(new Forwarder(port)) { Timeout = TimeSpan.FromSeconds(5) };
            var output = new StringWriter();
            Assert.Equal(0, await OperationalCommands.TestAlertAsync(paths, "telegram", output, CancellationToken.None, client));
            var (request, payload) = await receive.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.StartsWith("POST /botYOUR_TOKEN_HERE/sendMessage", request);
            using var json = JsonDocument.Parse(payload);
            Assert.Equal("YOUR_CHAT_ID_HERE", json.RootElement.GetProperty("chat_id").GetString());
            Assert.Contains("EZVIZ Local Monitor", json.RootElement.GetProperty("text").GetString());
            Assert.DoesNotContain("YOUR_TOKEN_HERE", output.ToString());
            Assert.DoesNotContain("YOUR_CHAT_ID_HERE", output.ToString());
        }
        finally { listener.Stop(); }
    }

    private static async Task<(string Request, string Payload)> ReceiveAsync(TcpListener listener)
    {
        using var peer = await listener.AcceptTcpClientAsync();
        await using var stream = peer.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var request = (await reader.ReadLineAsync())!;
        var length = 0;
        while (await reader.ReadLineAsync() is string line && line.Length != 0)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim());
        var body = new char[length];
        Assert.Equal(length, await reader.ReadBlockAsync(body, 0, length));
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 11\r\nConnection: close\r\n\r\n{\"ok\":true}");
        await stream.WriteAsync(response);
        return (request, new string(body));
    }

    private sealed class Forwarder(int port) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri = new Uri($"http://127.0.0.1:{port}{request.RequestUri!.AbsolutePath}");
            return base.SendAsync(request, ct);
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
