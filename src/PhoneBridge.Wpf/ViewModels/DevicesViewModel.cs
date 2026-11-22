using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhoneBridge.Core.Adb;
using PhoneBridge.Wpf.Services;

namespace PhoneBridge.Wpf.ViewModels;

public sealed partial class DevicesViewModel : ObservableObject
{
    private readonly IDeviceMonitor _monitor;
    private readonly ILogger<DevicesViewModel> _log;
    private bool _applying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateHint))]
    private AdbDevice? _selectedDevice;

    public DevicesViewModel(IDeviceMonitor monitor, SessionService session, ILogger<DevicesViewModel> log)
    {
        _monitor = monitor;
        _log = log;
        Session = session;
        _monitor.DevicesUpdated += (_, list) => Ui.Post(() => Apply(list));
    }

    public SessionService Session { get; }
    public ObservableCollection<AdbDevice> Devices { get; } = new();

    public string StateHint => SelectedDevice is null
        ? "No device selected. Connect your phone with a USB cable and enable USB debugging."
        : DeviceStateDescriber.Hint(SelectedDevice.State);

    partial void OnSelectedDeviceChanged(AdbDevice? value)
    {
        if (_applying) return;
        _ = Session.SelectAsync(value);
    }

    private void Apply(IReadOnlyList<AdbDevice> list)
    {
        var previousSerial = SelectedDevice?.Serial;
        Session.OnDevicesUpdated(list);

        _applying = true;
        try
        {
            Devices.Clear();
            foreach (var d in list) Devices.Add(d);
            var sel = previousSerial is null ? null : list.FirstOrDefault(d => d.Serial == previousSerial);
            sel ??= list.Count == 1 && list[0].IsReady ? list[0] : null;
            SelectedDevice = sel;
        }
        finally
        {
            _applying = false;
        }
        _ = Session.SelectAsync(SelectedDevice);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await _monitor.PollNowAsync(); }
        catch (Exception ex) { _log.LogError(ex, "Refresh failed"); }
    }

    [RelayCommand]
    private async Task RestartAdbAsync()
    {
        try { await _monitor.RestartAdbServerAsync(); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Restarting adb failed");
            Session.Message = $"Restarting adb failed: {ex.Message}";
        }
    }
}
