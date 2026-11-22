using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace PhoneBridge.Core.Adb;

public interface IAdbClient
{
    string? ConfiguredPath { get; set; }
    string? ResolveAdbPath();
    Task<string> GetVersionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default);
    Task<DeviceDetails> GetDeviceDetailsAsync(string serial, CancellationToken ct = default);
    Task RestartServerAsync(CancellationToken ct = default);
}

public sealed class AdbClient : IAdbClient
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(15);

    private readonly IProcessRunner _runner;
    private readonly IAdbLocator _locator;
    private readonly ILogger<AdbClient> _log;
    private string? _configured;
    private string? _resolved;

    public AdbClient(IProcessRunner runner, IAdbLocator locator, ILogger<AdbClient> log)
    {
        _runner = runner;
        _locator = locator;
        _log = log;
    }

    public string? ConfiguredPath
    {
        get => _configured;
        set { _configured = string.IsNullOrWhiteSpace(value) ? null : value; _resolved = null; }
    }

    public string? ResolveAdbPath() => _resolved ??= _locator.Locate(_configured);

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var o = await RunAsync(new[] { "version" }, Short, ct).ConfigureAwait(false);
        return o.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "unknown";
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        // The first call may start the adb daemon, so allow a longer timeout.
        var o = await RunAsync(new[] { "devices", "-l" }, Long, ct).ConfigureAwait(false);
        return AdbOutputParser.ParseDevices(o);
    }

    public async Task<DeviceDetails> GetDeviceDetailsAsync(string serial, CancellationToken ct = default)
    {
        var props = AdbOutputParser.ParseGetProp(await RunAsync(new[] { "-s", serial, "shell", "getprop" }, Short, ct).ConfigureAwait(false));
        string? P(string k) => props.TryGetValue(k, out var v) && v.Length > 0 ? v : null;

        (int, int)? size = null;
        int? density = null;
        try
        {
            size = AdbOutputParser.ParseWmSize(await RunAsync(new[] { "-s", serial, "shell", "wm", "size" }, Short, ct).ConfigureAwait(false));
            density = AdbOutputParser.ParseWmDensity(await RunAsync(new[] { "-s", serial, "shell", "wm", "density" }, Short, ct).ConfigureAwait(false));
        }
        catch (AdbException ex)
        {
            _log.LogWarning(ex, "Could not read display info for {Serial}", serial);
        }

        int? sdk = int.TryParse(P("ro.build.version.sdk"), out var s) ? s : null;
        return new DeviceDetails(
            serial,
            P("ro.product.manufacturer"),
            P("ro.product.brand"),
            P("ro.product.model"),
            P("ro.build.version.release"),
            sdk,
            P("ro.mi.os.version.name"),
            P("ro.miui.ui.version.name"),
            P("ro.product.cpu.abi"),
            P("ro.build.version.security_patch"),
            size?.Item1, size?.Item2, density);
    }

    public async Task RestartServerAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Restarting adb server");
        _resolved = null;
        try { await RunAsync(new[] { "kill-server" }, Short, ct).ConfigureAwait(false); }
        catch (AdbException ex) when (ex is not AdbNotFoundException) { _log.LogDebug(ex, "kill-server failed (ignored)"); }
        await RunAsync(new[] { "start-server" }, Long, ct).ConfigureAwait(false);
    }

    private async Task<string> RunAsync(string[] args, TimeSpan timeout, CancellationToken ct)
    {
        var path = ResolveAdbPath() ?? throw new AdbNotFoundException();
        ProcessResult r;
        try
        {
            r = await _runner.RunAsync(path, args, timeout, ct).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            throw new AdbException($"Could not start adb at '{path}': {ex.Message}", ex);
        }
        if (r.TimedOut) throw new AdbException($"adb timed out after {timeout.TotalSeconds:0}s (adb {string.Join(' ', args)}).");
        if (r.ExitCode != 0) throw new AdbException($"adb exited with code {r.ExitCode}: {r.StdErr.Trim()}");
        return r.StdOut;
    }
}
