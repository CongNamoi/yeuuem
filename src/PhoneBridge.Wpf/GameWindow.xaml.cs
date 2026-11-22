using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PhoneBridge.Core.Adb;
using PhoneBridge.Wpf.Services;
using PhoneBridge.Wpf.Streaming;

namespace PhoneBridge.Wpf;

public partial class GameWindow : Window
{
    private readonly AdbDevice _device;
    private readonly ScrcpyMirror _mirror;
    private readonly SessionService _session;
    private readonly ILogger<GameWindow> _log;
    private readonly Stopwatch _rateWindow = new();
    private CancellationTokenSource? _streamStop;
    private Task? _stream;
    private BitmapSource? _pending;
    private long _superseded;
    private int _presented;
    private bool _closing;
    private bool _fullscreen;
    private WindowState _previousState;

    public GameWindow(AdbDevice device, ScrcpyMirror mirror, SessionService session, ILogger<GameWindow> log)
    {
        _device = device;
        _mirror = mirror;
        _session = session;
        _log = log;
        InitializeComponent();
        Title = $"PhoneBridge Game · {device.DisplayName}";
        _session.PropertyChanged += OnSessionChanged;
        CompositionTarget.Rendering += PresentFrame;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        StartStream();
    }

    private void StartStream()
    {
        if (_closing || _stream is { IsCompleted: false }) return;
        if (_session.ConnectedDevice?.Serial != _device.Serial)
        {
            Status.Text = "Reconnect this device in the launcher.";
            return;
        }

        var options = new MirrorOptions(Value(Fps), Value(SizeLimit), Value(Bitrate),
            Codec.SelectedIndex == 0 ? "h264" : "h265");
        _streamStop = new CancellationTokenSource();
        Video.Source = null;
        Screenshot.IsEnabled = false;
        Interlocked.Exchange(ref _superseded, 0);
        _presented = 0;
        _rateWindow.Restart();
        Metrics.Text = "";
        Status.Text = "Starting USB mirror…";
        SettingsPanel.IsEnabled = false;
        StreamButton.Content = "Stop Stream";
        _stream = RunStreamAsync(options, _streamStop);
    }

    private static int Value(ComboBox box) =>
        int.Parse((string)((ComboBoxItem)box.SelectedItem).Content, CultureInfo.InvariantCulture);

    private async Task RunStreamAsync(MirrorOptions options, CancellationTokenSource stop)
    {
        try
        {
            await Task.Run(() => _mirror.RunAsync(_device.Serial, options, frame =>
            {
                if (Interlocked.Exchange(ref _pending, frame) != null)
                    Interlocked.Increment(ref _superseded);
            }, stop.Token));
        }
        catch (Exception) when (stop.IsCancellationRequested)
        {
            Status.Text = _session.ConnectedDevice?.Serial == _device.Serial
                ? "Stream stopped." : "Device disconnected. Reconnect it in the launcher, then start the stream.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mirror failed for {Serial}", _device.Serial);
            Status.Text = ex.Message;
        }
        finally
        {
            Interlocked.Exchange(ref _pending, null);
            _rateWindow.Stop();
            if (Video.Source != null) Metrics.Text = $"Frames skipped before display: {Interlocked.Read(ref _superseded)}";
            SettingsPanel.IsEnabled = true;
            StreamButton.Content = "Start Stream";
            _streamStop = null;
            stop.Dispose();
        }
    }

    private void PresentFrame(object? sender, EventArgs e)
    {
        if (_closing || _streamStop is not { IsCancellationRequested: false }) return;
        var frame = Interlocked.Exchange(ref _pending, null);
        if (frame != null)
        {
            Video.Source = frame;
            Screenshot.IsEnabled = true;
            Status.Text = $"USB mirror · {frame.PixelWidth} × {frame.PixelHeight}";
            _presented++;
        }
        var seconds = _rateWindow.Elapsed.TotalSeconds;
        if (seconds >= 1 && Video.Source != null)
        {
            Metrics.Text = $"{_presented / seconds:F1} FPS submitted to WPF · {Interlocked.Read(ref _superseded)} skipped before display";
            _presented = 0;
            _rateWindow.Restart();
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionService.IsConnected) && !_session.IsConnected)
            RequestStop();
    }

    private void RequestStop()
    {
        if (_streamStop is not { IsCancellationRequested: false } stop) return;
        Status.Text = "Stopping mirror…";
        stop.Cancel();
    }

    private void StreamClick(object sender, RoutedEventArgs e)
    {
        if (_stream is { IsCompleted: false }) RequestStop();
        else StartStream();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_stream is not { IsCompleted: false } stream) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        RequestStop();
        await stream;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _session.PropertyChanged -= OnSessionChanged;
        CompositionTarget.Rendering -= PresentFrame;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.F11 or Key.F12)) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        if (e.Key == Key.F11) ToggleFullscreen();
        else ToggleToolbar();
    }

    private void ToggleFullscreen()
    {
        if (_fullscreen)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _previousState;
        }
        else
        {
            _previousState = WindowState;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        _fullscreen = !_fullscreen;
    }

    private void ToggleToolbar() =>
        Toolbar.Visibility = Toolbar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ToolbarClick(object sender, RoutedEventArgs e) => ToggleToolbar();
    private void RotateClick(object sender, RoutedEventArgs e) => VideoRotation.Angle = (VideoRotation.Angle + 90) % 360;
    private void LauncherClick(object sender, RoutedEventArgs e) => Close();
    private void SettingsClick(object sender, RoutedEventArgs e) =>
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private async void ScreenshotClick(object sender, RoutedEventArgs e)
    {
        if (Video.Source is not BitmapSource frame) return;
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", FileName = "PhoneBridge" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await Task.Run(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                using var file = File.Create(dialog.FileName);
                encoder.Save(file);
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not save screenshot");
            if (!_closing && IsVisible) MessageBox.Show(this, ex.Message, "Screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
