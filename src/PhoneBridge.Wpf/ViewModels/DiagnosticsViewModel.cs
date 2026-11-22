using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhoneBridge.Core.Adb;
using PhoneBridge.Wpf.Services;

namespace PhoneBridge.Wpf.ViewModels;

public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private const int MaxLines = 1000;
    private readonly LogBuffer _buffer;
    private readonly IAdbClient _adb;
    private readonly IDeviceMonitor _monitor;
    private readonly ILogger<DiagnosticsViewModel> _log;

    public DiagnosticsViewModel(LogBuffer buffer, IAdbClient adb, IDeviceMonitor monitor, ILogger<DiagnosticsViewModel> log)
    {
        _buffer = buffer; _adb = adb; _monitor = monitor; _log = log;
        foreach (var l in buffer.Snapshot()) Entries.Add(l);
        buffer.LineAdded += line => Ui.Post(() =>
        {
            Entries.Add(line);
            while (Entries.Count > MaxLines) Entries.RemoveAt(0);
        });
    }

    public ObservableCollection<string> Entries { get; } = new();

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private void Copy()
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, Entries)); }
        catch (Exception ex) { _log.LogWarning(ex, "Clipboard unavailable"); }
    }

    [RelayCommand]
    private async Task RunCheckAsync()
    {
        _log.LogInformation("--- Diagnostics check ---");
        var path = _adb.ResolveAdbPath();
        _log.LogInformation("adb path: {Path}", path ?? "(not found)");
        if (path is null) return;
        try
        {
            _log.LogInformation("adb version: {Version}", await _adb.GetVersionAsync());
            var devices = await _adb.ListDevicesAsync();
            _log.LogInformation("Devices visible to adb: {Count}", devices.Count);
            foreach (var d in devices) _log.LogInformation("  {Serial} [{State}] {Model}", d.Serial, d.State, d.Model);
            _log.LogInformation("Monitor status: {Kind} - {Message}", _monitor.Status.Kind, _monitor.Status.Message);
        }
        catch (Exception ex) { _log.LogError(ex, "Diagnostics check failed"); }
    }
}
