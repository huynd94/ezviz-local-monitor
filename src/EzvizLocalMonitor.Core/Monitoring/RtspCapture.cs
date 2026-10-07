using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public static class RtspCapture
{
    public static VideoCapture Open(string url, int timeoutMilliseconds = 5000) => new(url, VideoCaptureAPIs.FFMPEG,
        new[] { (int)VideoCaptureProperties.OpenTimeoutMsec, timeoutMilliseconds, (int)VideoCaptureProperties.ReadTimeoutMsec, timeoutMilliseconds });
}
