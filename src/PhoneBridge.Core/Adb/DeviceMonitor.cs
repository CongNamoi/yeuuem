using Microsoft.Extensions.Logging;

namespace PhoneBridge.Core.Adb;

public enum AdbStatusKind { Starting, Ok, AdbNotFound, Error }

public sealed record AdbStatus(AdbStatusKind Kind, string Message);

public interface IDeviceMonitor : IAsyncDisposable
{
    IReadOnlyList<AdbDevice> Devices { get; }
    AdbStatus Status { get; }
    TimeSpan Interval { get; set; }
    /// <summary>Raised on a background thread when the device list changes (and after the first poll).</summary>
    event EventHandler<IReadOnlyList<AdbDevice>>? DevicesUpdated;
    event EventHandler<AdbStatus>? StatusChanged;
    void Start();
    Task PollNowAsync(CancellationToken ct = default);
    Task RestartAdbServerAsync(CancellationToken ct = default);
}

public sealed class DeviceMonitor : IDeviceMonitor
{
    private readonly IAdbClient _adb;
    private readonly ILogger<DeviceMonitor> _log;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _failures;
    private bool _first = true;
    private volatile IReadOnlyList<AdbDevice> _devices = Array.Empty<AdbDevice>();
    private volatile AdbStatus _status = new(AdbStatusKind.Starting, "Starting adb...");

    public DeviceMonitor(IAdbClient adb, ILogger<DeviceMonitor> log)
    {
        _adb = adb;
        _log = log;
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(1500);
    public IReadOnlyList<AdbDevice> Devices => _devices;
    public AdbStatus Status => _status;

    public event EventHandler<IReadOnlyList<AdbDevice>>? DevicesUpdated;
    public event EventHandler<AdbStatus>? StatusChanged;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => LoopAsync(ct), ct);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await PollNowAsync(ct).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public async Task PollNowAsync(CancellationToken ct = default)
    {
        await _pollLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = await _adb.ListDevicesAsync(ct).ConfigureAwait(false);
            _failures = 0;
            SetStatus(new AdbStatus(AdbStatusKind.Ok, "adb ready"));
            Apply(next);
        }
        catch (OperationCanceledException) { throw; }
        catch (AdbNotFoundException ex)
        {
            SetStatus(new AdbStatus(AdbStatusKind.AdbNotFound, ex.Message));
            Apply(Array.Empty<AdbDevice>());
        }
        catch (Exception ex)
        {
            _failures++;
            _log.LogWarning(ex, "adb poll failed (consecutive failures: {Count})", _failures);
            SetStatus(new AdbStatus(AdbStatusKind.Error, ex.Message));
            Apply(Array.Empty<AdbDevice>());
            if (_failures == 3)
            {
                try { await _adb.RestartServerAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception rex) { _log.LogError(rex, "Automatic adb server restart failed"); }
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    public async Task RestartAdbServerAsync(CancellationToken ct = default)
    {
        await _pollLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _adb.RestartServerAsync(ct).ConfigureAwait(false);
            _failures = 0;
        }
        finally
        {
            _pollLock.Release();
        }
        await PollNowAsync(ct).ConfigureAwait(false);
    }

    private void Apply(IReadOnlyList<AdbDevice> next)
    {
        var diff = DeviceDiff.Compute(_devices, next);
        foreach (var d in diff.Added) _log.LogInformation("Device connected: {Serial} {Model} [{State}]", d.Serial, d.Model, d.State);
        foreach (var d in diff.Removed) _log.LogInformation("Device disconnected: {Serial}", d.Serial);
        foreach (var d in diff.Changed) _log.LogInformation("Device changed: {Serial} -> {State}", d.Serial, d.State);

        if (!_first && !diff.Any) return;
        _first = false;
        _devices = next;
        DevicesUpdated?.Invoke(this, next);
    }

    private void SetStatus(AdbStatus s)
    {
        if (_status == s) return;
        _status = s;
        if (s.Kind == AdbStatusKind.Ok) _log.LogInformation("adb status: {Message}", s.Message);
        else _log.LogWarning("adb status: {Kind} - {Message}", s.Kind, s.Message);
        StatusChanged?.Invoke(this, s);
    }

    public async ValueTask DisposeAsync()
    {
        var cts = _cts;
        if (cts == null) return;
        cts.Cancel();
        try { if (_loop != null) await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        cts.Dispose();
        _cts = null;
    }
}
