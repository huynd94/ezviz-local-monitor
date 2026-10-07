using System.Collections.Concurrent;
using System.Threading.Channels;
using EzvizLocalMonitor.Models;

namespace EzvizLocalMonitor.Services;

/// <summary>
/// Hàng đợi cảnh báo trong bộ nhớ. Mỗi EventId chỉ có một work item đang chờ;
/// retry được giới hạn để không tạo vòng lặp gửi vô hạn.
/// </summary>
public sealed class AlertQueueService : IAsyncDisposable
{
    private sealed record WorkItem(AlertChannelSettings Settings, DetectionEvent Event, TaskCompletionSource<string> Completion, CancellationToken Cancellation);

    private readonly Func<AlertChannelSettings, DetectionEvent, CancellationToken, Task<string>> _sender;
    private readonly Channel<WorkItem> _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<long, TaskCompletionSource<string>> _pending = new();
    private readonly ConcurrentDictionary<long, string> _completed = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly object _sync = new();
    private Task? _disposeTask;
    private bool _accepting = true;

    public AlertQueueService(AlertDispatcher dispatcher)
        : this((settings, item, ct) => dispatcher.SendAsync(settings, item, ct)) { }

    public AlertQueueService(Func<AlertChannelSettings, DetectionEvent, CancellationToken, Task<string>> sender)
    {
        _sender = sender;
        _worker = Task.Run(WorkerAsync);
    }

    public Task<string> EnqueueAsync(AlertChannelSettings settings, DetectionEvent item, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_accepting) return Task.FromResult("Chưa gửi (hàng đợi đã dừng)");
            if (_completed.TryGetValue(item.Id, out var completed)) return Task.FromResult(completed);
            if (_pending.TryGetValue(item.Id, out var existing)) return existing.Task;

            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[item.Id] = completion;

            var work = new WorkItem(settings, item, completion, cancellationToken);
            if (!_channel.Writer.TryWrite(work))
            {
                _pending.TryRemove(item.Id, out _);
                completion.TrySetResult("Cảnh báo: hàng đợi đầy, sự kiện chưa được gửi");
            }
            return completion.Task;
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var work in _channel.Reader.ReadAllAsync(_stop.Token))
            {
                var result = await SendWithRetryAsync(work);
                lock (_sync)
                {
                    _completed[work.Event.Id] = result;
                    work.Completion.TrySetResult(result);
                    _pending.TryRemove(work.Event.Id, out _);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            FinishPending("Chưa gửi (hàng đợi đã dừng)");
        }
        catch (Exception ex)
        {
            FinishPending($"Chưa gửi (worker lỗi: {Safe(ex)})");
        }
        finally
        {
            lock (_sync)
            {
                _accepting = false;
                _channel.Writer.TryComplete();
            }
        }
    }

    private async Task<string> SendWithRetryAsync(WorkItem work)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, work.Cancellation);
        var ct = stop.Token;
        string result = "Cảnh báo: chưa gửi";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (ct.IsCancellationRequested) return "Chưa gửi (đã dừng)";
            try
            {
                result = await _sender(work.Settings, work.Event, ct);
                ct.ThrowIfCancellationRequested();
                if (!NeedsRetry(result) || attempt == 3) return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return "Chưa gửi (đã dừng)";
            }
            catch (Exception ex)
            {
                result = $"Cảnh báo: lần gửi {attempt} lỗi ({Safe(ex)})";
                if (attempt == 3) return result;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return "Chưa gửi (đã dừng)"; }
        }
        return result;
    }

    private static bool NeedsRetry(string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return true;

        // AlertDispatcher trả về nhiều thao tác trong cùng một chuỗi, ví dụ:
        // "Telegram: đã gửi + Telegram ảnh: lỗi ... | Zalo: đã gửi + Zalo ảnh: chưa gửi...".
        // Chỉ retry khi có thao tác thất bại. AlertDispatcher có idempotency theo
        // EventId + thao tác nên phần đã thành công sẽ không gửi lại qua mạng.
        var operations = result.Split(new[] { " | ", " + " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return operations.Any(NeedsRetryPart);
    }

    private static bool NeedsRetryPart(string part)
    {
        var separator = part.IndexOf(':');
        var operationResult = separator >= 0 ? part[(separator + 1)..].Trim() : part.Trim();

        // Đây là trạng thái hợp lệ khi Zalo chỉ được cấu hình gửi văn bản còn
        // relay ảnh HTTPS chưa bật/được đồng ý; không được coi là lỗi mạng.
        if (operationResult.Contains("ảnh: chưa gửi", StringComparison.OrdinalIgnoreCase) ||
            operationResult.Contains("API yêu cầu URL HTTPS công khai", StringComparison.OrdinalIgnoreCase) ||
            operationResult.Contains("relay chưa bật", StringComparison.OrdinalIgnoreCase)) return false;

        return operationResult.Contains("lỗi", StringComparison.OrdinalIgnoreCase) ||
               operationResult.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               operationResult.Contains("hủy", StringComparison.OrdinalIgnoreCase) ||
               operationResult.Contains("chưa gửi", StringComparison.OrdinalIgnoreCase) ||
               operationResult.Contains("chưa xác nhận", StringComparison.OrdinalIgnoreCase);
    }

    private static string Safe(Exception ex)
    {
        var text = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        return text.Length > 100 ? text[..100] : text;
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _accepting = false;
            return new ValueTask(_disposeTask ??= DrainAsync());
        }
    }

    private void FinishPending(string status)
    {
        lock (_sync)
        {
            _accepting = false;
            _channel.Writer.TryComplete();
            foreach (var pair in _pending) pair.Value.TrySetResult(status);
            _pending.Clear();
        }
    }

    private async Task DrainAsync()
    {
        _channel.Writer.TryComplete();
        try { await _worker.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { _stop.Cancel(); }
        // Cancellation does not prove that a sender has released its image/HTTP work.
        await _worker;
        _stop.Dispose();
    }
}
