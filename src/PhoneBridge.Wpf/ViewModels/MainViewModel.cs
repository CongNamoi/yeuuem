using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneBridge.Core.Adb;
using PhoneBridge.Wpf.Services;

namespace PhoneBridge.Wpf.ViewModels;

public sealed record NavItem(string Key, string Title);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, object> _pages;
    private readonly IDeviceMonitor _monitor;

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _selectedProfile = "(no profile)";
    [ObservableProperty] private string _adbStatusText = "adb: starting...";

    public MainViewModel(
        SessionService session, IDeviceMonitor monitor,
        HomeViewModel home, DevicesViewModel devices, GamesViewModel games,
        KeymappingViewModel keymapping, SettingsViewModel settings, DiagnosticsViewModel diagnostics)
    {
        Session = session;
        _monitor = monitor;
        _pages = new Dictionary<string, object>
        {
            ["Home"] = home, ["Devices"] = devices, ["Games"] = games,
            ["Keymapping"] = keymapping, ["Settings"] = settings, ["Diagnostics"] = diagnostics
        };
        foreach (var k in _pages.Keys) NavItems.Add(new NavItem(k, k));
        SelectedNav = NavItems[0];

        session.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(BannerText));
            ToggleConnectionCommand.NotifyCanExecuteChanged();
        };
        monitor.StatusChanged += (_, s) => Ui.Post(() => ApplyStatus(s));
        ApplyStatus(monitor.Status);
    }

    public SessionService Session { get; }
    public ObservableCollection<NavItem> NavItems { get; } = new();
    public string[] Profiles { get; } = { "(no profile)", "FPS/TPS", "MOBA", "Action/RPG" };

    // Placeholders until Phase 2 (streaming) and Phase 3 (input) provide real measurements.
    public string FpsText => "-- fps";
    public string StreamLatencyText => "stream -- ms";
    public string InputLatencyText => "input -- ms";

    public string ConnectButtonText => Session.IsConnected ? "Disconnect" : "Connect";

    public string BannerText =>
        _monitor.Status.Kind is AdbStatusKind.AdbNotFound or AdbStatusKind.Error
            ? _monitor.Status.Message
            : Session.Message;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value != null && _pages.TryGetValue(value.Key, out var page)) CurrentPage = page;
    }

    private void ApplyStatus(AdbStatus s)
    {
        AdbStatusText = s.Kind switch
        {
            AdbStatusKind.Ok => "adb: ready",
            AdbStatusKind.AdbNotFound => "adb: not found",
            AdbStatusKind.Error => "adb: error",
            _ => "adb: starting..."
        };
        OnPropertyChanged(nameof(BannerText));
    }

    private bool CanToggle() => Session.IsConnected || Session.SelectedDevice is { IsReady: true };

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private void ToggleConnection()
    {
        if (Session.IsConnected) Session.Disconnect(); else Session.Connect();
    }
}
