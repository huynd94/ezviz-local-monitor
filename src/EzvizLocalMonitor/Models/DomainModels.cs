namespace EzvizLocalMonitor.Models;

public sealed class CameraDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Camera mới";
    public string RtspUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.55;
    public int CooldownSeconds { get; set; } = 30;
    public int RoiLeftPercent { get; set; } = 0;
    public int RoiTopPercent { get; set; } = 0;
    public int RoiRightPercent { get; set; } = 100;
    public int RoiBottomPercent { get; set; } = 100;

    public override string ToString() => Name;
}

public sealed class AlertChannelSettings
{
    public bool TelegramEnabled { get; set; }
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public bool ZaloEnabled { get; set; }
    public string ZaloBotToken { get; set; } = string.Empty;
    public string ZaloChatId { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public List<CameraDefinition> Cameras { get; set; } = new();
    public AlertChannelSettings Alerts { get; set; } = new();
    public int RetentionDays { get; set; } = 14;
    public int InferenceFpsPerCamera { get; set; } = 1;
    public int ConfirmationsRequired { get; set; } = 2;
    public int ConfirmationWindow { get; set; } = 3;
    public bool StartWithWindows { get; set; }
}

public sealed record PersonDetection(double Confidence, int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

public sealed class DiscoveredCamera
{
    public string IpAddress { get; init; } = string.Empty;
    public int RtspPort { get; init; } = 554;
    public string DiscoveryMethod { get; init; } = string.Empty;
    public string? OnvifServiceUrl { get; init; }

    public override string ToString()
    {
        var detail = string.IsNullOrWhiteSpace(OnvifServiceUrl) ? "Ứng viên RTSP" : "ONVIF";
        return $"{IpAddress}:{RtspPort} — {detail} ({DiscoveryMethod})";
    }
}

public sealed class DetectionEvent
{
    public long Id { get; set; }
    public Guid CameraId { get; set; }
    public string CameraName { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.Now;
    public double Confidence { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = "Chưa gửi";
}
