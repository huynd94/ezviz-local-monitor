using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed class LanCameraDiscovery
{
    private const int RtspPort = 554;
    private static readonly IPEndPoint WsDiscoveryEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);

    public async Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = new ConcurrentDictionary<string, DiscoveredCamera>(StringComparer.OrdinalIgnoreCase);

        progress?.Report("Đang tìm thiết bị ONVIF trong LAN...");
        foreach (var found in await ProbeOnvifAsync(cancellationToken))
            candidates.TryAdd(found.IpAddress, found);

        var subnets = GetPrivateIpv4Subnets().Distinct().ToList();
        if (subnets.Count == 0)
            throw new InvalidOperationException("Không tìm thấy giao diện IPv4 riêng đang hoạt động. Hãy kết nối PC vào cùng Wi-Fi/LAN với camera.");

        progress?.Report($"Đang kiểm tra RTSP trên {subnets.Count} mạng cục bộ...");
        await ScanRtspAsync(subnets, candidates, cancellationToken);

        return candidates.Values.OrderBy(x => x.IpAddress, new IpAddressComparer()).ToList();
    }

    private static async Task<IReadOnlyList<DiscoveredCamera>> ProbeOnvifAsync(CancellationToken cancellationToken)
    {
        var found = new ConcurrentDictionary<string, DiscoveredCamera>(StringComparer.OrdinalIgnoreCase);
        var probe = $"""
<?xml version="1.0" encoding="UTF-8"?>
<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery">
  <e:Header><w:MessageID>uuid:{Guid.NewGuid()}</w:MessageID><w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To><w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header>
  <e:Body><d:Probe /></e:Body>
</e:Envelope>
""";

        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        await udp.SendAsync(Encoding.UTF8.GetBytes(probe), WsDiscoveryEndpoint, cancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(remaining);
            try
            {
                var result = await udp.ReceiveAsync(receiveTimeout.Token);
                var text = Encoding.UTF8.GetString(result.Buffer);
                var xaddrMatch = Regex.Match(text, "<[^>]*XAddrs[^>]*>\\s*(?<xaddr>[^<\\s]+)", RegexOptions.IgnoreCase);
                var xaddr = xaddrMatch.Success ? xaddrMatch.Groups["xaddr"].Value : null;
                var ip = GetIpFromUri(xaddr) ?? result.RemoteEndPoint.Address.ToString();
                if (!IPAddress.TryParse(ip, out var address) || !IsPrivateIpv4(address)) continue;
                found.TryAdd(ip, new DiscoveredCamera
                {
                    IpAddress = ip,
                    RtspPort = RtspPort,
                    DiscoveryMethod = "ONVIF WS-Discovery",
                    OnvifServiceUrl = xaddr
                });
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
        }
        return found.Values.ToList();
    }

    private static async Task ScanRtspAsync(IEnumerable<(IPAddress Address, IPAddress Mask)> subnets,
        ConcurrentDictionary<string, DiscoveredCamera> candidates, CancellationToken cancellationToken)
    {
        using var throttle = new SemaphoreSlim(48);
        var tasks = new List<Task>();
        foreach (var subnet in subnets)
        {
            var baseBytes = subnet.Address.GetAddressBytes();
            for (var lastOctet = 1; lastOctet < 255; lastOctet++)
            {
                var bytes = (byte[])baseBytes.Clone();
                bytes[3] = (byte)lastOctet;
                var candidate = new IPAddress(bytes);
                if (candidate.Equals(subnet.Address)) continue;
                tasks.Add(ProbeRtspPortAsync(candidate, throttle, candidates, cancellationToken));
            }
        }
        await Task.WhenAll(tasks);
    }

    private static async Task ProbeRtspPortAsync(IPAddress ip, SemaphoreSlim throttle,
        ConcurrentDictionary<string, DiscoveredCamera> candidates, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(ip, RtspPort, timeout.Token);
            candidates.AddOrUpdate(ip.ToString(),
                _ => new DiscoveredCamera { IpAddress = ip.ToString(), RtspPort = RtspPort, DiscoveryMethod = "RTSP TCP scan" },
                (_, existing) => existing);
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        finally { throttle.Release(); }
    }

    private static IEnumerable<(IPAddress Address, IPAddress Mask)> GetPrivateIpv4Subnets()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                                 x.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel))
        {
            var properties = network.GetIPProperties();
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask is null || !IsPrivateIpv4(unicast.Address)) continue;
                var address = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var networkAddress = new byte[4];
                for (var i = 0; i < 4; i++) networkAddress[i] = (byte)(address[i] & mask[i]);
                // The fallback scanner intentionally supports common /24 local networks only.
                if (mask[0] == 255 && mask[1] == 255 && mask[2] == 255 && mask[3] == 0)
                    yield return (new IPAddress(networkAddress), unicast.IPv4Mask);
            }
        }
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31;
    }

    private static string? GetIpFromUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : null;

    private sealed class IpAddressComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (IPAddress.TryParse(x, out var first) && IPAddress.TryParse(y, out var second))
                return StructuralComparisons.StructuralComparer.Compare(first.GetAddressBytes(), second.GetAddressBytes());
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}
