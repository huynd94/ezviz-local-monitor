namespace EzvizLocalMonitor.Services;

public sealed class TaskRegistry(IAppLogger logger) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<long, Task> _tasks = [];
    private readonly CancellationTokenSource _stop = new();
    private long _nextId;
    private bool _accepting = true;

    public bool TryStart(Func<CancellationToken, Task> work)
    {
        long id;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (_sync)
        {
            if (!_accepting) return false;
            id = ++_nextId;
            token = _stop.Token;
            _tasks.Add(id, completion.Task);
        }
        _ = Task.Run(async () =>
        {
            try { await work(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { try { logger.Error(LogChannel.App, "owned monitoring work failed", ex); } catch { } }
            finally
            {
                lock (_sync) _tasks.Remove(id);
                completion.TrySetResult();
            }
        });
        return true;
    }

    public void CloseAdmission() { lock (_sync) _accepting = false; }

    public async Task<bool> CompleteAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        Task[] tasks;
        lock (_sync) { _accepting = false; tasks = _tasks.Values.ToArray(); }
        try { await Task.WhenAll(tasks).WaitAsync(timeout, ct); return true; }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { _stop.Cancel(); return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (await CompleteAsync(TimeSpan.FromSeconds(20))) _stop.Dispose();
    }
}
