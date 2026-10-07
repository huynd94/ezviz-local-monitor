using System.Collections.Concurrent;
using System.Diagnostics;
using EzvizLocalMonitor.Models;
using OpenCvSharp;

namespace EzvizLocalMonitor.Services;

public sealed class MonitoringRuntime
{
    private readonly Func<IMonitoringSession> _factory;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Guid, CameraRuntimeSnapshot> _cameras = new();
    private readonly object _eventSync = new();
    private readonly HashSet<long> _seenEvents = [];
    private readonly Queue<long> _recentEvents = new();
    private long _eventCount;
    private AppSettings _settings = new();
    private IMonitoringSession? _session;
    private CancellationTokenSource? _sessionStop;
    private Task? _sessionDisposal;
    private Task? _loop;
    private bool _started;
    private bool _periodicFault;
    private bool _preview;
    private bool? _manualOverride;
    private (bool Allowed, int Profile)? _lastDecision;
    private RuntimeSnapshot _snapshot = new("stopped", false, false, 0, 0);

    public MonitoringRuntime(Func<IMonitoringSession> factory, TimeProvider clock, TimeZoneInfo zone, IAppLogger logger)
    { _factory = factory; _clock = clock; _zone = zone; _logger = logger; }

    public RuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public IReadOnlyDictionary<Guid, CameraRuntimeSnapshot> CameraStates => new Dictionary<Guid, CameraRuntimeSnapshot>(_cameras);
    public event Action<RuntimeSnapshot>? SnapshotChanged;
    public event Action<Exception>? Faulted;
    public event Action<Guid, string>? CameraStatusChanged;
    public event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged;
    public event Action<Guid, Mat>? PreviewReady;
    public event Action<DetectionEvent>? EventRecorded;

    public async Task StartAsync(AppSettings settings, bool previewEnabled, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_started) return;
            if (_stop.IsCancellationRequested) throw new InvalidOperationException("Stopped runtime cannot be restarted; create a new runtime.");
            if (_session is not null) await StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(20), ct);
            _started = true;
            _settings = SettingsCodec.Clone(settings);
            _preview = previewEnabled;
            await EvaluateCoreAsync(_clock.GetUtcNow(), false, ct);
            Volatile.Write(ref _periodicFault, false);
            _loop = RunLoopAsync();
        }
        catch
        {
            _started = false;
            _lastDecision = null;
            _manualOverride = null;
            try { await StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception cleanup) { _logger.Error(LogChannel.App, "failed startup cleanup did not complete", cleanup); }
            Publish("faulted", false, false, _settings.PerformanceProfile);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task EvaluateScheduleAsync(DateTimeOffset now, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_started && !_stop.IsCancellationRequested)
            {
                await EvaluateCoreAsync(now, false, ct);
                Volatile.Write(ref _periodicFault, false);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ApplySettingsAsync(AppSettings settings, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _settings = SettingsCodec.Clone(settings);
            _lastDecision = null;
            _manualOverride = null;
            if (_started && !_stop.IsCancellationRequested) await EvaluateCoreAsync(_clock.GetUtcNow(), true, ct);
            Volatile.Write(ref _periodicFault, false);
        }
        finally { _gate.Release(); }
    }

    public async Task SetManualMonitoringAsync(bool enabled, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_stop.IsCancellationRequested) return;
            _manualOverride = enabled;
            await EvaluateCoreAsync(_clock.GetUtcNow(), false, ct);
            Volatile.Write(ref _periodicFault, false);
        }
        finally { _gate.Release(); }
    }

    public void SetPreviewEnabled(bool enabled)
    {
        _preview = enabled;
        _session?.SetPreviewEnabled(enabled);
    }

    public async Task RunMaintenanceAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        await _gate.WaitAsync(linked.Token);
        try { linked.Token.ThrowIfCancellationRequested(); await operation(linked.Token); }
        finally { _gate.Release(); }
    }

    public async Task<ShutdownResult> StopAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _stop.Cancel();
        var watch = Stopwatch.StartNew();
        Publish("stopping", Snapshot.IsMonitoring, Snapshot.ScheduleAllowed, Snapshot.EffectiveProfile);
        var acquired = false;
        try
        {
            if (!await _gate.WaitAsync(timeout, ct))
            {
                Publish("faulted", Snapshot.IsMonitoring, Snapshot.ScheduleAllowed, Snapshot.EffectiveProfile);
                return new(false, "Runtime transition did not finish before shutdown deadline.");
            }
            acquired = true;
            var remaining = timeout - watch.Elapsed;
            if (remaining <= TimeSpan.Zero) return new(false, "Shutdown deadline reached.");
            await StopSessionAsync().WaitAsync(remaining, ct);
            if (_loop is not null) await _loop.WaitAsync(timeout - watch.Elapsed > TimeSpan.Zero ? timeout - watch.Elapsed : TimeSpan.Zero, ct);
            Publish("stopped", false, false, Snapshot.EffectiveProfile);
            return new(true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogChannel.App, "runtime shutdown did not complete", ex);
            Publish("faulted", Snapshot.IsMonitoring, Snapshot.ScheduleAllowed, Snapshot.EffectiveProfile);
            return new(false, _logger.Redact(ex.Message));
        }
        finally { if (acquired) _gate.Release(); }
    }

    private async Task RunLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), _clock);
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                if (Volatile.Read(ref _periodicFault)) continue;
                try { await EvaluateScheduleAsync(_clock.GetUtcNow(), _stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Pause automatic retries, but keep the scheduler owned/alive.
                    // An explicit successful command resumes the same loop.
                    if (Volatile.Read(ref _periodicFault))
                    {
                        _logger.Error(LogChannel.App, "monitoring runtime fault", ex);
                        Notify(Faulted, ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.Error(LogChannel.App, "monitoring runtime fault", ex);
            Publish("faulted", Snapshot.IsMonitoring, Snapshot.ScheduleAllowed, Snapshot.EffectiveProfile);
            Notify(Faulted, ex);
        }
    }

    private async Task EvaluateCoreAsync(DateTimeOffset now, bool restart, CancellationToken ct)
    {
        try { await EvaluateTransitionAsync(now, restart, ct); }
        catch
        {
            Volatile.Write(ref _periodicFault, true);
            Publish("faulted", false, Snapshot.ScheduleAllowed, Snapshot.EffectiveProfile);
            throw;
        }
    }

    private async Task EvaluateTransitionAsync(DateTimeOffset now, bool restart, CancellationToken ct)
    {
        // A previous command can time out while disposal remains owned work.
        // Join it before publishing running or creating another session.
        if (_session is null) _sessionDisposal = null;
        else if (_sessionDisposal is not null) await _sessionDisposal.WaitAsync(TimeSpan.FromSeconds(20), ct);
        var allowed = MonitorScheduleService.IsMonitoringAllowed(_settings, now, _zone);
        var profile = MonitorScheduleService.ActivePerformanceProfile(_settings, now, _zone) ?? _settings.PerformanceProfile;
        var decision = (allowed, profile);
        if (_lastDecision is not null && _lastDecision != decision) { _manualOverride = null; restart = true; }
        _lastDecision = decision;
        var shouldRun = _manualOverride ?? allowed;
        if (!shouldRun || !_settings.Cameras.Any(c => c.IsEnabled && !string.IsNullOrWhiteSpace(c.RtspUrl)))
        {
            await StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(20), ct);
            Publish(shouldRun ? "idle" : allowed ? "paused" : "outside-schedule", false, allowed, profile);
            return;
        }
        if (restart) await StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(20), ct);
        if (_session is null)
        {
            var effective = SettingsCodec.Clone(_settings);
            if (profile != effective.PerformanceProfile) effective.InferenceFpsPerCamera = profile switch { 0 => 1, 2 => 3, 3 => 1, _ => 2 };
            effective.PerformanceProfile = profile;
            effective.InferenceFpsPerCamera = Math.Clamp(effective.InferenceFpsPerCamera, 1, 3);
            effective.ConfirmationWindow = Math.Clamp(effective.ConfirmationWindow, 1, 5);
            effective.ConfirmationsRequired = Math.Clamp(effective.ConfirmationsRequired, 1, effective.ConfirmationWindow);
            _session = _factory();
            _sessionDisposal = null;
            _session.SetPreviewEnabled(_preview);
            _session.CameraStatusChanged += OnCameraStatus;
            _session.CameraRuntimeChanged += OnCameraRuntime;
            _session.PreviewReady += OnPreview;
            _session.EventRecorded += OnEvent;
            Publish("starting", false, allowed, profile);
            _sessionStop = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
            try
            {
                await _session.StartAsync(effective, _sessionStop.Token);
                _sessionStop.Token.ThrowIfCancellationRequested();
            }
            catch
            {
                try { await StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception cleanup) { _logger.Error(LogChannel.App, "transition startup cleanup did not complete", cleanup); }
                throw;
            }
        }
        _stop.Token.ThrowIfCancellationRequested();
        Publish("running", true, allowed, profile);
    }

    private Task StopSessionAsync()
    {
        if (_session is null) return Task.CompletedTask;
        if (_sessionDisposal is not null) return _sessionDisposal;
        var session = _session;
        Publish("stopping", false, Snapshot.ScheduleAllowed, Snapshot.EffectiveProfile);
        session.SetPreviewEnabled(false);
        _sessionStop?.Cancel();
        return _sessionDisposal = DisposeOwnedSessionAsync(session, _sessionStop);
    }

    private async Task DisposeOwnedSessionAsync(IMonitoringSession session, CancellationTokenSource? lifetime)
    {
        var released = false;
        try { await session.DisposeAsync(); released = true; }
        catch { released = session.CleanupCompleted; throw; }
        finally
        {
            if (released)
            {
                session.CameraStatusChanged -= OnCameraStatus;
                session.CameraRuntimeChanged -= OnCameraRuntime;
                session.PreviewReady -= OnPreview;
                session.EventRecorded -= OnEvent;
                if (ReferenceEquals(_session, session)) _session = null;
                if (ReferenceEquals(_sessionStop, lifetime)) _sessionStop = null;
                lifetime?.Dispose();
                _cameras.Clear();
            }
        }
    }

    private void OnCameraStatus(Guid id, string message) => Notify(CameraStatusChanged, id, _logger.Redact(message));
    private void OnCameraRuntime(Guid id, CameraRuntimeSnapshot state)
    {
        var sanitized = state with { Message = _logger.Redact(state.Message) };
        _cameras[id] = sanitized;
        Notify(CameraRuntimeChanged, id, sanitized);
    }
    private void OnPreview(Guid id, Mat frame)
    {
        var handler = PreviewReady;
        if (handler is null) { frame.Dispose(); return; }
        try { handler(id, frame); }
        catch (Exception ex) { frame.Dispose(); _logger.Error(LogChannel.App, "preview observer failed", ex); }
    }

    private void OnEvent(DetectionEvent item)
    {
        lock (_eventSync)
        {
            if (_seenEvents.Add(item.Id)) { _eventCount++; _recentEvents.Enqueue(item.Id); }
            if (_recentEvents.Count > 4096) _seenEvents.Remove(_recentEvents.Dequeue());
        }
        RuntimeSnapshot previous;
        RuntimeSnapshot updated;
        do
        {
            previous = Snapshot;
            updated = previous with { EventCount = Interlocked.Read(ref _eventCount) };
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, updated, previous), previous));
        Notify(SnapshotChanged, updated);
        Notify(EventRecorded, item);
    }

    private void Publish(string state, bool monitoring, bool allowed, int profile)
    {
        var snapshot = new RuntimeSnapshot(state, monitoring, allowed, profile, Interlocked.Read(ref _eventCount));
        Volatile.Write(ref _snapshot, snapshot);
        Notify(SnapshotChanged, snapshot);
    }

    private void Notify<T>(Action<T>? observers, T value)
    {
        if (observers is null) return;
        foreach (Action<T> observer in observers.GetInvocationList())
            try { observer(value); } catch (Exception ex) { _logger.Error(LogChannel.App, "runtime observer failed", ex); }
    }
    private void Notify<T1, T2>(Action<T1, T2>? observers, T1 first, T2 second)
    {
        if (observers is null) return;
        foreach (Action<T1, T2> observer in observers.GetInvocationList())
            try { observer(first, second); } catch (Exception ex) { _logger.Error(LogChannel.App, "runtime observer failed", ex); }
    }
}
