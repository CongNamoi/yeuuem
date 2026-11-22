using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PhoneBridge.Core.Adb;

namespace PhoneBridge.Wpf.Services;

/// <summary>Tracks the selected device and the "connected" session. Must be used from the UI thread.</summary>
public sealed partial class SessionService : ObservableObject
{
    private readonly IAdbClient _adb;
    private readonly ILogger<SessionService> _log;
    public AdbDevice? ConnectedDevice { get; private set; }

    [ObservableProperty] private AdbDevice? _selectedDevice;
    [ObservableProperty] private DeviceDetails? _details;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _message = string.Empty;

    public SessionService(IAdbClient adb, ILogger<SessionService> log)
    {
        _adb = adb;
        _log = log;
    }

    public async Task SelectAsync(AdbDevice? device)
    {
        SelectedDevice = device;
        Details = null;
        if (device is not { IsReady: true }) return;

        try
        {
            var details = await _adb.GetDeviceDetailsAsync(device.Serial);
            if (SelectedDevice?.Serial == device.Serial) Details = details;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read details for {Serial}", device.Serial);
            Message = $"Could not read device details: {ex.Message}";
        }
    }

    public void Connect()
    {
        if (SelectedDevice is not { IsReady: true } d)
        {
            Message = SelectedDevice is null ? "Select a device first." : DeviceStateDescriber.Hint(SelectedDevice.State);
            return;
        }
        ConnectedDevice = d;
        IsConnected = true;
        Message = string.Empty;
        _log.LogInformation("Session connected to {Serial} ({Model}).", d.Serial, d.Model);
    }

    public void Disconnect(string? reason = null)
    {
        if (!IsConnected) return;
        _log.LogInformation("Session disconnected from {Serial}. {Reason}", ConnectedDevice?.Serial, reason);
        // Phase 3+: release all active touches here before tearing down the transport.
        ConnectedDevice = null;
        IsConnected = false;
        Message = reason ?? string.Empty;
    }

    public void OnDevicesUpdated(IReadOnlyList<AdbDevice> devices)
    {
        if (!IsConnected) return;
        var d = devices.FirstOrDefault(x => x.Serial == ConnectedDevice?.Serial);
        if (d is not { IsReady: true })
            Disconnect("The connected device was disconnected or is no longer available.");
    }
}
