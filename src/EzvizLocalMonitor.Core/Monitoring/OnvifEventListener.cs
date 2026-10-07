using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

public sealed record OnvifEvent(string Topic, bool IsMotion, bool IsHuman, string Source);

public sealed class OnvifEventListener : IAsyncDisposable
{
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace WsAddressing = "http://www.w3.org/2005/08/addressing";
    private static readonly XNamespace Wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private static readonly XNamespace Wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private static readonly XNamespace Tt = "http://www.onvif.org/ver10/schema";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CameraDefinition _camera;
    private readonly string _password;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private string? _eventServiceUrl;
    private string? _subscriptionUrl;
    private readonly object _lifecycle = new();
    private Task<bool>? _startTask;
    private Task? _disposeTask;
    private bool _disposed;

    public event Action<CameraDefinition, OnvifEvent>? EventReceived;
    public event Action<CameraDefinition, string>? StatusChanged;
    public event Action<CameraDefinition>? FallbackRequested;

    public OnvifEventListener(CameraDefinition camera, string password)
    {
        _camera = camera;
        _password = password;
    }

    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startTask is not null) return _startTask;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _stop.Token;
            return _startTask = Task.Run(() => StartCoreAsync(token));
        }
    }

    private async Task<bool> StartCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_camera.OnvifServiceUrl)) return false;
        try
        {
            _eventServiceUrl = await DiscoverEventServiceAsync(_camera.OnvifServiceUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(_eventServiceUrl))
            {
                StatusChanged?.Invoke(_camera, "ONVIF không có Events; fallback YOLO cục bộ");
                return false;
            }

            _subscriptionUrl = await CreatePullPointSubscriptionAsync(_eventServiceUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(_subscriptionUrl))
            {
                StatusChanged?.Invoke(_camera, "ONVIF không tạo được PullPoint; fallback YOLO cục bộ");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _loop = Task.Run(() => PullLoopAsync(cancellationToken), cancellationToken);
            StatusChanged?.Invoke(_camera, "ONVIF Events đang hoạt động");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(_camera, $"ONVIF thất bại; fallback YOLO: {SafeMessage(ex)}");
            return false;
        }
    }

    private async Task PullLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !string.IsNullOrWhiteSpace(_subscriptionUrl))
        {
            try
            {
                var response = await SoapRequestAsync(_subscriptionUrl!, $"""
                    <tev:PullMessages xmlns:tev="http://www.onvif.org/ver10/events/wsdl">
                      <tev:Timeout>PT50S</tev:Timeout><tev:MessageLimit>10</tev:MessageLimit>
                    </tev:PullMessages>
                    """, cancellationToken);
                foreach (var notification in ParseNotifications(response))
                    EventReceived?.Invoke(_camera, notification);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(_camera, "ONVIF mất kết nối; fallback YOLO: " + SafeMessage(ex));
                FallbackRequested?.Invoke(_camera);
                break;
            }
        }
    }

    private async Task<string?> DiscoverEventServiceAsync(string deviceServiceUrl, CancellationToken cancellationToken)
    {
        var response = await SoapRequestAsync(deviceServiceUrl, $"""
            <tds:GetCapabilities xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
              <tds:Category>Events</tds:Category>
            </tds:GetCapabilities>
            """, cancellationToken);
        var document = XDocument.Parse(response);
        var eventsElement = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Events");
        return eventsElement?.Descendants().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value.Trim();
    }

    private async Task<string?> CreatePullPointSubscriptionAsync(string eventServiceUrl, CancellationToken cancellationToken)
    {
        var response = await SoapRequestAsync(eventServiceUrl, $"""
            <tev:CreatePullPointSubscription xmlns:tev="http://www.onvif.org/ver10/events/wsdl">
              <tev:InitialTerminationTime>PT10M</tev:InitialTerminationTime>
            </tev:CreatePullPointSubscription>
            """, cancellationToken);
        var document = XDocument.Parse(response);
        return document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Address")?.Value.Trim();
    }

    private async Task<string> SoapRequestAsync(string url, string body, CancellationToken cancellationToken)
    {
        var messageId = "urn:uuid:" + Guid.NewGuid();
        var nonce = RandomNumberGenerator.GetBytes(16);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var digestInput = nonce.Concat(Encoding.UTF8.GetBytes(created)).Concat(Encoding.UTF8.GetBytes(_password)).ToArray();
        var digest = Convert.ToBase64String(SHA1.HashData(digestInput));
        var nonceBase64 = Convert.ToBase64String(nonce);
        var envelope = $"""
            <s:Envelope xmlns:s="{Soap}" xmlns:a="{WsAddressing}">
              <s:Header>
                <a:Action s:mustUnderstand="1">{ResolveAction(body)}</a:Action>
                <a:MessageID>{messageId}</a:MessageID><a:To s:mustUnderstand="1">{EscapeXml(url)}</a:To>
                <wsse:Security s:mustUnderstand="1" xmlns:wsse="{Wsse}" xmlns:wsu="{Wsu}">
                  <wsse:UsernameToken><wsse:Username>admin</wsse:Username>
                    <wsse:Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</wsse:Password>
                    <wsse:Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{nonceBase64}</wsse:Nonce>
                    <wsu:Created>{created}</wsu:Created>
                  </wsse:UsernameToken>
                </wsse:Security>
              </s:Header><s:Body>{body}</s:Body>
            </s:Envelope>
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/soap+xml"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}");
        if (text.Contains("Fault", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("ONVIF SOAP Fault");
        return text;
    }

    private static IEnumerable<OnvifEvent> ParseNotifications(string xml)
    {
        var document = XDocument.Parse(xml);
        foreach (var message in document.Descendants().Where(x => x.Name.LocalName == "NotificationMessage"))
        {
            var topic = message.Descendants().FirstOrDefault(x => x.Name.LocalName == "Topic")?.Value.Trim() ?? string.Empty;
            var values = string.Join(" ", message.Descendants().Where(x => x.Name.LocalName == "SimpleItem")
                .Select(x => $"{x.Attribute("Name")?.Value}={x.Attribute("Value")?.Value}"));
            var all = (topic + " " + values).ToLowerInvariant();
            var isHuman = all.Contains("human") || all.Contains("person") || all.Contains("people");
            var isMotion = isHuman || all.Contains("motion") || all.Contains("fielddetection") || all.Contains("region");
            if (isMotion) yield return new OnvifEvent(topic, true, isHuman, values);
        }
    }

    private static string ResolveAction(string body) => body.Contains("PullMessages", StringComparison.Ordinal)
        ? "http://www.onvif.org/ver10/events/wsdl/PullPointSubscription/PullMessages"
        : body.Contains("CreatePullPointSubscription", StringComparison.Ordinal)
            ? "http://www.onvif.org/ver10/events/wsdl/EventPortType/CreatePullPointSubscription"
            : "http://www.onvif.org/ver10/device/wsdl/GetCapabilities";

    private static string EscapeXml(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    private static string SafeMessage(Exception ex) => ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _stop?.Cancel();
        if (_startTask is not null) { try { await _startTask; } catch (OperationCanceledException) { } }
        if (_loop is not null) { try { await _loop; } catch (OperationCanceledException) { } }
        _stop?.Dispose();
        _httpClient.Dispose();
    }
}
