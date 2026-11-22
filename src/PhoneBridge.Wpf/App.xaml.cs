using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhoneBridge.Core.Adb;
using PhoneBridge.Wpf.Services;
using PhoneBridge.Wpf.ViewModels;

namespace PhoneBridge.Wpf;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var buffer = new LogBuffer();
        var sc = new ServiceCollection();
        sc.AddSingleton(buffer);
        sc.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(new BufferLoggerProvider(buffer));
        });

        sc.AddSingleton<IProcessRunner, ProcessRunner>();
        sc.AddSingleton<IAdbLocator, AdbLocator>();
        sc.AddSingleton<IAdbClient, AdbClient>();
        sc.AddSingleton<IDeviceMonitor, DeviceMonitor>();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<SessionService>();
        sc.AddSingleton<Streaming.ScrcpyMirror>();

        sc.AddSingleton<HomeViewModel>();
        sc.AddSingleton<DevicesViewModel>();
        sc.AddSingleton<GamesViewModel>();
        sc.AddSingleton<KeymappingViewModel>();
        sc.AddSingleton<SettingsViewModel>();
        sc.AddSingleton<DiagnosticsViewModel>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();

        var settings = _services.GetRequiredService<SettingsService>();
        settings.Load();
        _services.GetRequiredService<IAdbClient>().ConfiguredPath = settings.Current.AdbPath;

        // Resolve view models so they subscribe to monitor events before polling starts.
        _services.GetRequiredService<DevicesViewModel>();
        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
        _services.GetRequiredService<IDeviceMonitor>().Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services != null)
        {
            _services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        base.OnExit(e);
    }
}
