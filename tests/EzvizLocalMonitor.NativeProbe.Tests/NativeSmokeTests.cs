using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace EzvizLocalMonitor.NativeProbe.Tests;

public sealed class NativeSmokeTests
{
    private readonly ITestOutputHelper _output;
    public NativeSmokeTests(ITestOutputHelper output) => _output = output;
    [Fact]
    public void DecodeResizeAndJpegRoundTrip()
    {
        using var capture = new VideoCapture(Fixture("EZVIZ_TEST_VIDEO"), VideoCaptureAPIs.FFMPEG);
        using var frame = new Mat();
        Assert.True(capture.IsOpened());
        Assert.True(capture.Read(frame));
        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);
        using var resized = new Mat();
        Cv2.Resize(frame, resized, new Size(320, 240));
        Assert.True(Cv2.ImEncode(".jpg", resized, out var bytes));
        using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(240, decoded.Height);
    }

    [Fact]
    public void PinnedModelRunsCpuInference()
    {
        var path = Fixture("EZVIZ_TEST_MODEL");
        using (var stream = File.OpenRead(path))
            Assert.Equal("B2BC52F40E8E1C532427D5BDE3575A5D5B571B739FAB2C6DF443733ED1589CBD", Convert.ToHexString(SHA256.HashData(stream)));
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        using var session = new InferenceSession(path, options);
        var input = new DenseTensor<float>(new[] { 1, 3, 640, 640 });
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.Single(), input) });
        Assert.Equal(new[] { 1, 84, 8400 }, results.Single().AsTensor<float>().Dimensions.ToArray());
    }

    [Fact]
    public void LinuxNativeHasNoGuiOrMissingDependencies()
    {
        if (!OperatingSystem.IsLinux()) return;
        var library = Directory.GetFiles(AppContext.BaseDirectory, "libOpenCvSharpExtern.so", SearchOption.AllDirectories).Single();
        var start = new ProcessStartInfo("ldd") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(library);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        _output.WriteLine(output);
        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain("not found", output, StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { "libgtk", "libX11", "libXext", "libXrender", "libXi.", "libxcb", "libICE", "libSM.", "libwayland", "libQt" })
            Assert.DoesNotContain(name, output, StringComparison.OrdinalIgnoreCase);
    }

    private static string Fixture(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path), $"Missing fixture environment: {name}");
        Assert.True(File.Exists(path), $"Missing fixture: {name}");
        return path!;
    }
}
