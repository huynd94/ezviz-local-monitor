using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using EzvizLocalMonitor.Services;
using OpenCvSharp;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class NativeRuntimeTests
{
    [Fact]
    public void ProductionDetectorRunsWithoutGui()
    {
        var model = Environment.GetEnvironmentVariable("EZVIZ_TEST_MODEL");
        Assert.False(string.IsNullOrWhiteSpace(model));
        using var detector = new YoloPersonDetector(model);
        using var frame = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.Black);
        var people = detector.Detect(frame, 0.99);
        Assert.All(people, person => { Assert.InRange(person.Left, 0, 639); Assert.InRange(person.Top, 0, 479); });
    }

    [Fact]
    public async Task SilentRtspPeerDoesNotBlockNativeOpenIndefinitely()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var watch = Stopwatch.StartNew();
            var opening = Task.Run(() => { using var capture = RtspCapture.Open($"rtsp://127.0.0.1:{port}/fixture", 500); return capture.IsOpened(); });
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await opening.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.InRange(watch.ElapsedMilliseconds, 100, 2500);
        }
        finally { listener.Stop(); }
    }
}
