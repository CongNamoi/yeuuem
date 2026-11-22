using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PhoneBridge.Core.Adb;
using PhoneBridge.Wpf.Services;

namespace PhoneBridge.Wpf.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IAdbClient _adb;
    private readonly ILogger<SettingsViewModel> _log;

    [ObservableProperty] private string _adbPath;
    [ObservableProperty] private string _adbInfo = "Press Detect to locate adb.";

    public SettingsViewModel(SettingsService settings, IAdbClient adb, ILogger<SettingsViewModel> log)
    {
        _settings = settings;
        _adb = adb;
        _log = log;
        _adbPath = settings.Current.AdbPath ?? string.Empty;
    }

    public string SettingsFile => _settings.FilePath;

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog { Title = "Select adb.exe", Filter = "adb|adb.exe|All files|*.*" };
        if (dlg.ShowDialog() == true) AdbPath = dlg.FileName;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.Current.AdbPath = string.IsNullOrWhiteSpace(AdbPath) ? null : AdbPath.Trim();
        _settings.Save();
        _adb.ConfiguredPath = _settings.Current.AdbPath;
        await DetectAsync();
    }

    [RelayCommand]
    private async Task DetectAsync()
    {
        var path = _adb.ResolveAdbPath();
        if (path is null) { AdbInfo = "adb.exe not found. Set the path above or put platform-tools in tools\\platform-tools."; return; }
        try
        {
            var version = await _adb.GetVersionAsync();
            AdbInfo = $"{path}\n{version}";
            _log.LogInformation("adb located at {Path} ({Version})", path, version);
        }
        catch (Exception ex)
        {
            AdbInfo = $"{path}\nFound, but it failed to run: {ex.Message}";
        }
    }
}
