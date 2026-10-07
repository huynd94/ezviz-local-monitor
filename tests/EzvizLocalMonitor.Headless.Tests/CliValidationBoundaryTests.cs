using EzvizLocalMonitor.Headless.Cli;
using EzvizLocalMonitor.Models;
using Xunit;

namespace EzvizLocalMonitor.Headless.Tests;

public sealed class CliValidationBoundaryTests
{
    [Fact]
    public void DisabledDraftCameraCanRemainUnconfigured()
        => SettingsValidator.Validate(new AppSettings { Cameras = [new() { IsEnabled = false, RtspUrl = "" }] });

    [Fact]
    public void EmptyOrDuplicateCameraIdentityIsRejectedBeforeSaving()
    {
        var camera = new CameraDefinition { RtspUrl = "rtsp://fixture/stream", Id = Guid.Empty };
        Assert.Throws<ConfigurationException>(() => SettingsValidator.Validate(new AppSettings { Cameras = [camera] }));
        var id = Guid.NewGuid();
        Assert.Throws<ConfigurationException>(() => SettingsValidator.Validate(new AppSettings { Cameras = [
            new() { Id = id, RtspUrl = "rtsp://fixture/one" }, new() { Id = id, RtspUrl = "rtsp://fixture/two" }] }));
    }

    [Fact]
    public void EnabledImageRelayRequiresHttps()
        => Assert.Throws<ConfigurationException>(() => SettingsValidator.Validate(new AppSettings
        { Alerts = new AlertChannelSettings { ZaloImageRelayEnabled = true, ZaloImageRelayUrl = "http://fixture/upload" } }));
}
